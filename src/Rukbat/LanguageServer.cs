using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rukbat;

// A stdio LSP server. It checks source, but never generates or executes PowerShell.
public sealed partial class LanguageServer
{
    private const int MaximumHeaderBytes = 8192;
    private const int MaximumMessageBytes = 16 * 1024 * 1024;
    private const int ParseError = -32700;
    private const int InvalidRequest = -32600;
    private const int MethodNotFound = -32601;
    private const int InvalidParameters = -32602;
    private const int InternalError = -32603;
    private const int ServerNotInitialized = -32002;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private sealed record Document(string Uri, string Text, int Version);
    private readonly Dictionary<string, Document> documents = new(PathComparer);
    private readonly Dictionary<string, LanguageAnalysis> analyses = new(PathComparer);
    private readonly HashSet<string> published = new(PathComparer);
    private readonly Stream input;
    private readonly Stream output;
    private readonly TextWriter log;
    private bool initialized;
    private bool shutdown;

    private LanguageServer(Stream input, Stream output, TextWriter log)
    {
        this.input = input;
        this.output = output;
        this.log = log;
    }

    public static int Run(Stream input, Stream output, TextWriter log)
    {
        var server = new LanguageServer(input, output, log);
        try
        {
            return server.Listen();
        }
        catch (Exception error) when (error is IOException or InvalidDataException or DecoderFallbackException)
        {
            log.WriteLine($"LSP transport failure: {error.Message}");
            return 2;
        }
    }

    private int Listen()
    {
        while (ReadMessage() is { } bytes)
        {
            JsonDocument message;
            try
            {
                message = JsonDocument.Parse(bytes);
            }
            catch (JsonException) { Error(null, ParseError, "Invalid JSON."); continue; }
            using (message)
            {
                var root = message.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("jsonrpc", out var protocol) || protocol.ValueKind != JsonValueKind.String || protocol.GetString() != "2.0" ||
                    !root.TryGetProperty("method", out var methodValue) || methodValue.ValueKind != JsonValueKind.String)
                {
                    Error(null, InvalidRequest, "Expected a JSON-RPC 2.0 request or notification.");
                    continue;
                }
                if (root.TryGetProperty("id", out var invalidId) && invalidId.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
                {
                    Error(null, InvalidRequest, "Request ID must be a string or number.");
                    continue;
                }
                object? id = root.TryGetProperty("id", out var identifier) ? identifier.Clone() : null;
                var request = id is not null;
                var method = methodValue.GetString()!;
                var parameters = root.TryGetProperty("params", out var args) ? args : default;
                if (method == "exit")
                    return shutdown ? 0 : 1;
                if (!initialized && method != "initialize")
                {
                    if (request)
                        Error(id, ServerNotInitialized, "Server has not been initialized.");
                    continue;
                }
                if (shutdown)
                {
                    if (request)
                        Error(id, InvalidRequest, "Server has shut down.");
                    continue;
                }
                try
                {
                    switch (method)
                    {
                        case "initialize":
                            if (initialized)
                            {
                                Error(id, InvalidRequest, "Server is already initialized.");
                                break;
                            }
                            initialized = true;
                            ConfigureWorkspace(parameters);
                            Result(id, new
                            {
                                capabilities = new
                                {
                                    positionEncoding = "utf-16",
                                    textDocumentSync = new
                                    {
                                        openClose = true,
                                        change = 1,
                                        save = new
                                        {
                                            includeText = false
                                        }
                                    },
                                    completionProvider = new
                                    {
                                        triggerCharacters = new[] { "." }
                                    },
                                    hoverProvider = true,
                                    definitionProvider = true,
                                    referencesProvider = true,
                                    renameProvider = RenameCapability(),
                                    signatureHelpProvider = SignatureCapability(),
                                    workspace = new { workspaceFolders = new { supported = true, changeNotifications = true } },
                                    documentFormattingProvider = true
                                },
                                serverInfo = new
                                {
                                    name = "Rukbat",
                                    version = typeof(LanguageServer).Assembly.GetName().Version?.ToString()
                                }
                            });
                            break;
                        case "shutdown":
                            shutdown = true;
                            Result(id, null);
                            break;
                        case "initialized":
                        case "$/cancelRequest":
                        case "$/setTrace":
                            break;
                        case "textDocument/didOpen":
                            Open(parameters);
                            break;
                        case "textDocument/didChange":
                            Change(parameters);
                            break;
                        case "textDocument/didClose":
                            Close(parameters);
                            break;
                        case "textDocument/didSave":
                        case "workspace/didChangeWatchedFiles":
                            Recheck();
                            break;
                        case "workspace/didChangeWorkspaceFolders":
                            ChangeWorkspace(parameters);
                            break;
                        case "textDocument/hover":
                        case "textDocument/definition":
                        case "textDocument/completion":
                        case "textDocument/formatting":
                        case "textDocument/references":
                        case "textDocument/prepareRename":
                        case "textDocument/rename":
                        case "textDocument/signatureHelp":
                            Result(id, Feature(method, parameters));
                            break;
                        default:
                            if (request)
                                Error(id, MethodNotFound, $"Unknown method '{method}'.");
                            break;
                    }
                }
                catch (EditorRequestFailure error)
                {
                    if (request)
                        Error(id, error.Code, error.Message);
                }
                catch (Exception error) when (error is ArgumentException or KeyNotFoundException or JsonException or InvalidOperationException or FormatException)
                {
                    if (request)
                        Error(id, InvalidParameters, error.Message);
                    else
                        log.WriteLine($"{method}: {error.Message}");
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    if (request)
                        Error(id, InternalError, error.Message);
                    else
                        log.WriteLine($"{method}: {error.Message}");
                }
            }
        }
        return shutdown ? 0 : 1;
    }

    private void Open(JsonElement parameters)
    {
        var document = parameters.GetProperty("textDocument");
        var uri = document.GetProperty("uri").GetString()!;
        documents[FilePath(uri)] = new(uri, StringValue(document.GetProperty("text")), document.GetProperty("version").GetInt32());
        Recheck();
    }
    private void Change(JsonElement parameters)
    {
        var document = parameters.GetProperty("textDocument");
        var path = FilePath(document.GetProperty("uri").GetString()!);
        if (!documents.TryGetValue(path, out var current))
            throw new ArgumentException("Document is not open.");
        var version = document.GetProperty("version").GetInt32();
        if (version <= current.Version)
            return;
        var text = current.Text;
        foreach (var change in parameters.GetProperty("contentChanges").EnumerateArray())
        {
            var replacement = StringValue(change.GetProperty("text"));
            if (change.TryGetProperty("range", out var range))
            {
                var map = new EditorText(text);
                var start = map.Offset(Position(range.GetProperty("start")));
                var end = map.Offset(Position(range.GetProperty("end")));
                if (end < start)
                    throw new ArgumentException("Invalid change range.");
                text = text[..start] + replacement + text[end..];
            }
            else
                text = replacement;
        }
        documents[path] = current with
        {
            Text = text,
            Version = version
        };
        Recheck();
    }
    private void Close(JsonElement parameters)
    {
        var path = FilePath(parameters.GetProperty("textDocument").GetProperty("uri").GetString()!);
        documents.Remove(path);
        Recheck();
    }
    private string ReadSource(string path) => documents.TryGetValue(path, out var document) ? document.Text : File.ReadAllText(path);

    private void Recheck()
    {
        analyses.Clear();
        var diagnostics = new Dictionary<string, List<Diagnostic>>(PathComparer);
        var sources = new Dictionary<string, string>(PathComparer);
        foreach (var (path, document) in documents)
        {
            var analysis = new LanguageAnalysis(document.Text, path, ReadSource);
            analyses[path] = analysis;
            foreach (var module in analysis.Modules.Where(m => m.StandardName is null))
            {
                diagnostics.TryAdd(module.Source.Path, []);
                sources[module.Source.Path] = module.Source.Text;
            }
            foreach (var diagnostic in analysis.Diagnostics.Concat(analysis.DocumentationDiagnostics))
            {
                if (diagnostic.FilePath.StartsWith('<'))
                    continue;
                if (!diagnostics.TryGetValue(diagnostic.FilePath, out var list))
                    diagnostics[diagnostic.FilePath] = list = [];
                list.Add(diagnostic);
            }
        }
        foreach (var path in published.Concat(diagnostics.Keys).Distinct(PathComparer).ToList())
        {
            documents.TryGetValue(path, out var document);
            var text = sources.GetValueOrDefault(path, document?.Text ?? "");
            var mapped = (diagnostics.GetValueOrDefault(path) ?? []).Distinct().Select(d =>
            {
                var map = new EditorText(text);
                int offset;
                try
                {
                    offset = map.Offset(new(d.Line - 1, d.Column - 1));
                }
                catch (ArgumentException) { offset = text.Length; }
                var length = offset < text.Length && text[offset] is not ('\r' or '\n') ? 1 : 0;
                return new
                {
                    range = map.Range(offset, length),
                    severity = d.Code.StartsWith("RUK6", StringComparison.Ordinal) ? 2 : 1,
                    code = d.Code,
                    source = "rukbat",
                    message = d.Message
                };
            }).ToArray();
            var payload = new Dictionary<string, object?> { ["uri"] = document?.Uri ?? new Uri(path).AbsoluteUri, ["diagnostics"] = mapped };
            if (document is not null)
                payload["version"] = document.Version;
            Notify("textDocument/publishDiagnostics", payload);
        }
        published.Clear();
        published.UnionWith(diagnostics.Keys);
    }

    private object? Feature(string method, JsonElement parameters)
    {
        var path = FilePath(parameters.GetProperty("textDocument").GetProperty("uri").GetString()!);
        if (!documents.TryGetValue(path, out var document))
            throw new ArgumentException("Document is not open.");
        var map = new EditorText(document.Text);
        if (method == "textDocument/formatting")
        {
            var formatted = Formatter.Format(document.Text, path);
            return !formatted.Success || formatted.Output == document.Text ? Array.Empty<object>()
                : new object[] { new { range = map.Range(0, document.Text.Length), newText = formatted.Output } };
        }
        var offset = map.Offset(Position(parameters.GetProperty("position")));
        var analysis = analyses[path];
        if (method is "textDocument/references" or "textDocument/prepareRename" or "textDocument/rename")
            return ReferenceFeature(method, parameters, path, offset);
        if (method == "textDocument/signatureHelp")
            return SignatureHelp.Get(document.Text, path, offset, ReadSource, signatureLabelOffsets, signatureMarkdown);
        if (method == "textDocument/hover")
            return analysis.Hover(offset);
        if (method == "textDocument/definition")
            return analysis.Definition(offset);

        var source = new Source(document.Text, path);
        var tokens = new Lexer(source).Scan();
        // Do not complete inside strings or comments (which the compiler lexer omits).
        if (tokens.Any(t => t.StringValue is not null && offset > t.Offset && offset < t.Offset + t.Text.Length))
            return Array.Empty<object>();
        var prior = tokens.LastOrDefault(t => t.Offset < offset && t.Text != "<eof>");
        var gapStart = prior is null ? 0 : Math.Min(offset, prior.Offset + prior.Text.Length);
        if (document.Text[gapStart..offset].Contains("//", StringComparison.Ordinal))
            return Array.Empty<object>();
        var start = offset;
        while (start > 0 && (char.IsLetterOrDigit(document.Text[start - 1]) || document.Text[start - 1] == '_'))
            start--;
        var prefix = document.Text[start..offset];
        var before = document.Text[..start];
        if (Regex.IsMatch(before, @"\bimport\s+$"))
            return EditorCatalog.Modules.Where(m => m.StartsWith(prefix, StringComparison.Ordinal)).Select(m => new EditorSymbol(m, $"import {m}", 9).Completion).ToArray();
        var member = Regex.Match(before, @"(?<receiver>[\p{L}\p{N}_]+)\s*\.\s*$");
        var precedingToken = tokens.LastOrDefault(t => t.Offset < start && t.Text != "\n");
        var receiver = member.Success ? member.Groups["receiver"].Value : precedingToken?.Text == "." ? "" : null;
        if (receiver is not null || analysis.Diagnostics.Any(d => d.Code.StartsWith("RUK1", StringComparison.Ordinal)))
        {
            // Repair only the completion hole and unclosed delimiters in an ephemeral copy.
            // Real diagnostics always come from the unmodified document above.
            var end = offset;
            while (end < document.Text.Length && (char.IsLetterOrDigit(document.Text[end]) || document.Text[end] == '_'))
                end++;
            var repaired = receiver is null ? document.Text : document.Text[..start] + "__completion" + document.Text[end..];
            var repairTokens = new Lexer(new Source(repaired, path)).Scan();
            var closings = new Stack<string>();
            foreach (var token in repairTokens)
            {
                if (token.StringValue is not null)
                    continue;
                if (token.Text is "{" or "(" or "[")
                    closings.Push(token.Text == "{" ? "}" : token.Text == "(" ? ")" : "]");
                else if (closings.Count > 0 && token.Text == closings.Peek())
                    closings.Pop();
            }
            repaired += "\n" + string.Join("\n", closings);
            analysis = new LanguageAnalysis(repaired, path, ReadSource);
        }
        return analysis.Complete(offset, receiver, prefix).Select(s => s.Completion).ToArray();
    }

    private static string StringValue(JsonElement value) => value.ValueKind == JsonValueKind.String
        ? value.GetString()! : throw new ArgumentException("Expected a string value.");
    private static EditorPosition Position(JsonElement value) => new(value.GetProperty("line").GetInt32(), value.GetProperty("character").GetInt32());
    private static string FilePath(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.IsFile)
            throw new ArgumentException("Only file: document URIs are supported.");
        var path = uri.LocalPath;
        // VS Code percent-encodes the drive colon. Uri then treats /c:/ as a URI path,
        // rather than recognizing the drive, leaving a leading separator in LocalPath.
        if (OperatingSystem.IsWindows() && path.Length >= 3 && path[0] is '/' or '\\' && char.IsAsciiLetter(path[1]) && path[2] == ':')
            path = path[1..];
        return Path.GetFullPath(path);
    }
    private void Result(object? id, object? result) => Write(new { jsonrpc = "2.0", id, result });
    private void Error(object? id, int code, string message) => Write(new { jsonrpc = "2.0", id, error = new { code, message } });
    private void Notify(string method, object parameters) => Write(new { jsonrpc = "2.0", method, @params = parameters });
    private void Write(object message)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        output.Write(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"));
        output.Write(bytes);
        output.Flush();
    }
    private byte[]? ReadMessage()
    {
        var header = new List<byte>();
        while (true)
        {
            var next = input.ReadByte();
            if (next < 0)
            {
                if (header.Count == 0)
                    return null;
                throw new InvalidDataException("Truncated message header.");
            }
            header.Add((byte)next);
            if (header.Count > MaximumHeaderBytes)
                throw new InvalidDataException("Message header is too large.");
            if (header.Count >= 4 && header[^4] == '\r' && header[^3] == '\n' && header[^2] == '\r' && header[^1] == '\n')
                break;
        }
        int? length = null;
        foreach (var line in Encoding.ASCII.GetString(header.ToArray()).Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator < 0)
                throw new InvalidDataException("Invalid message header.");
            if (!line[..separator].Equals("Content-Length", StringComparison.OrdinalIgnoreCase))
                continue;
            if (length is not null || !int.TryParse(line[(separator + 1)..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var count) || count > MaximumMessageBytes)
                throw new InvalidDataException("Invalid Content-Length.");
            length = count;
        }
        if (length is null)
            throw new InvalidDataException("Missing Content-Length.");
        var body = new byte[length.Value];
        input.ReadExactly(body);
        return body;
    }
}
