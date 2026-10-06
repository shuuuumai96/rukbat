using System.Text.Json;

namespace Rukbat;

internal sealed class EditorRequestFailure(int code, string message) : Exception(message)
{
    public int Code { get; } = code;
}

public sealed partial class LanguageServer
{
    private const int RequestFailed = -32803;
    private const int ContentModified = -32801;
    private readonly List<string> workspaceRoots = [];
    private bool renameSupported;
    private bool prepareSupported;
    private bool signatureLabelOffsets;
    private bool signatureMarkdown;
    private bool signatureContext;

    private static JsonElement Property(JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(name, out var next))
                return default;
            value = next;
        }
        return value;
    }

    private void ConfigureWorkspace(JsonElement parameters)
    {
        var folders = Property(parameters, "workspaceFolders");
        if (folders.ValueKind == JsonValueKind.Array)
            workspaceRoots.AddRange(folders.EnumerateArray().Select(f => FilePath(f.GetProperty("uri").GetString()!)));
        else if (Property(parameters, "rootUri") is { ValueKind: JsonValueKind.String } root)
            workspaceRoots.Add(FilePath(root.GetString()!));
        var capabilities = Property(parameters, "capabilities");
        var failureHandling = Property(capabilities, "workspace", "workspaceEdit", "failureHandling");
        renameSupported = Property(capabilities, "workspace", "workspaceEdit", "documentChanges").ValueKind == JsonValueKind.True &&
            failureHandling.ValueKind == JsonValueKind.String && failureHandling.GetString() is "transactional" or "textOnlyTransactional";
        prepareSupported = Property(capabilities, "textDocument", "rename", "prepareSupport").ValueKind == JsonValueKind.True;
        signatureLabelOffsets = Property(capabilities, "textDocument", "signatureHelp", "signatureInformation", "parameterInformation", "labelOffsetSupport").ValueKind == JsonValueKind.True;
        signatureContext = Property(capabilities, "textDocument", "signatureHelp", "contextSupport").ValueKind == JsonValueKind.True;
        var formats = Property(capabilities, "textDocument", "signatureHelp", "signatureInformation", "documentationFormat");
        var preferred = formats.ValueKind == JsonValueKind.Array ? formats.EnumerateArray().FirstOrDefault(f => f.GetString() is "markdown" or "plaintext") : default;
        signatureMarkdown = preferred.ValueKind == JsonValueKind.String && preferred.GetString() == "markdown";
    }

    private object RenameCapability() => renameSupported && prepareSupported
        ? new
        {
            prepareProvider = true
        } : (object)renameSupported;

    private object SignatureCapability()
    {
        var result = new Dictionary<string, object> { ["triggerCharacters"] = new[] { "(", "," } };
        if (signatureContext)
            result["retriggerCharacters"] = new[] { ")" };
        return result;
    }

    private void ChangeWorkspace(JsonElement parameters)
    {
        var change = parameters.GetProperty("event");
        foreach (var folder in change.GetProperty("removed").EnumerateArray())
        {
            var path = FilePath(folder.GetProperty("uri").GetString()!);
            workspaceRoots.RemoveAll(r => EditorPaths.Comparer.Equals(r, path));
        }
        foreach (var folder in change.GetProperty("added").EnumerateArray())
        {
            var path = FilePath(folder.GetProperty("uri").GetString()!);
            if (!workspaceRoots.Contains(path, EditorPaths.Comparer))
                workspaceRoots.Add(path);
        }
    }

    private object? ReferenceFeature(string method, JsonElement parameters, string path, int offset)
    {
        if (method != "textDocument/references" && !renameSupported)
            throw new EditorRequestFailure(RequestFailed, "Rename needs a client supporting versioned, transactional text document edits.");
        try
        {
            var buffers = documents.ToDictionary(p => p.Key, p => p.Value.Text, EditorPaths.Comparer);
            var workspace = new EditorWorkspace(workspaceRoots.ToArray(), path, buffers);
            workspace.Capture();
            if (method == "textDocument/references")
                return workspace.References(offset, parameters.GetProperty("context").GetProperty("includeDeclaration").GetBoolean());
            if (method == "textDocument/prepareRename")
            {
                var target = workspace.PrepareRename(offset);
                return new
                {
                    range = target.Location.Range,
                    placeholder = target.Symbol.Name
                };
            }
            var edits = workspace.Rename(offset, StringValue(parameters.GetProperty("newName")));
            var changes = edits.OrderBy(p => p.Key, EditorPaths.Comparer).Select(pair =>
            {
                var map = new EditorText(workspace.Read(pair.Key));
                documents.TryGetValue(pair.Key, out var document);
                return new
                {
                    textDocument = new
                    {
                        uri = document?.Uri ?? new Uri(pair.Key).AbsoluteUri,
                        version = document is null ? (int?)null : document.Version
                    },
                    edits = pair.Value.Select(e => new { range = map.Range(e.Offset, e.Length), newText = e.Text }).ToArray()
                };
            }).ToArray();
            return new
            {
                documentChanges = changes
            };
        }
        catch (InvalidOperationException error)
        {
            throw new EditorRequestFailure(error.Message.StartsWith("Source changed", StringComparison.Ordinal) ? ContentModified : RequestFailed, error.Message);
        }
        catch (IOException error)
        {
            throw new EditorRequestFailure(RequestFailed, $"Cannot read the complete workspace: {error.Message}");
        }
        catch (UnauthorizedAccessException error)
        {
            throw new EditorRequestFailure(RequestFailed, $"Cannot read the complete workspace: {error.Message}");
        }
    }
}
