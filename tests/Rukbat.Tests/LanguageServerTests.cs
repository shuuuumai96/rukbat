using System.Diagnostics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Rukbat;

namespace Rukbat.Tests;

[Trait("Category", "Unit")]
public sealed partial class LanguageServerTests : TestBase
{
    private static object Request(object id, string method, object? parameters = null) => new { jsonrpc = "2.0", id, method, @params = parameters };
    private static object Notice(string method, object parameters) => new { jsonrpc = "2.0", method, @params = parameters };
    private static object Open(string uri, string text, int version = 1) => Notice("textDocument/didOpen", new { textDocument = new { uri, languageId = "rukbat", version, text } });
    private static object Change(string uri, string text, int version = 2) => Notice("textDocument/didChange", new { textDocument = new { uri, version }, contentChanges = new[] { new { text } } });
    private static object Feature(string uri, string method, int line, int character, object? id = null) => Request(id ?? "feature", "textDocument/" + method,
        new
        {
            textDocument = new
            {
                uri
            },
            position = new
            {
                line,
                character
            }
        });
    private static byte[] Frames(IEnumerable<object> messages)
    {
        using var stream = new MemoryStream();
        foreach (var message in messages)
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(message, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            stream.Write(Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"));
            stream.Write(bytes);
        }
        return stream.ToArray();
    }
    private static List<JsonElement> Decode(byte[] bytes)
    {
        var results = new List<JsonElement>();
        var start = 0;
        while (start < bytes.Length)
        {
            var end = start;
            while (!(bytes[end] == '\r' && bytes[end + 1] == '\n' && bytes[end + 2] == '\r' && bytes[end + 3] == '\n'))
                end++;
            var length = int.Parse(Encoding.ASCII.GetString(bytes, start, end - start).Split(':')[1]);
            using var json = JsonDocument.Parse(bytes.AsMemory(end + 4, length));
            results.Add(json.RootElement.Clone());
            start = end + 4 + length;
        }
        return results;
    }
    private static List<JsonElement> Session(params object[] messages)
    {
        using var input = new MemoryStream(Frames([Request(1, "initialize", new { capabilities = new { } }), .. messages, Request("shutdown", "shutdown"), Notice("exit", new { })]));
        using var output = new MemoryStream();
        using var log = new StringWriter();
        Assert.Equal(0, LanguageServer.Run(input, output, log));
        Assert.Equal("", log.ToString());
        return Decode(output.ToArray());
    }
    private static JsonElement Answer(List<JsonElement> messages, string id = "feature") => messages.Single(m => m.TryGetProperty("id", out var value) && value.ToString() == id);
    private static JsonElement Result(List<JsonElement> messages, string id = "feature") => Answer(messages, id).GetProperty("result");
    private static JsonElement LastDiagnostics(List<JsonElement> messages, string uri) => messages.Last(m => m.TryGetProperty("method", out var method) && method.GetString() == "textDocument/publishDiagnostics" &&
        m.GetProperty("params").GetProperty("uri").GetString() == uri).GetProperty("params");
    private static string UriFor(Project project, string file = "main.rukbat") => new Uri(project.PathFor(file)).AbsoluteUri;

    [Fact]
    public void ConstHoverCompletionAndDiagnosticsUseTheBoundDeclaration()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var source = "/// 再試行回数。\nconst count := 3\nprint(count)\ncount = 4\n";
        var messages = Session(Open(uri, source), Feature(uri, "hover", 1, 7, "declaration"),
            Feature(uri, "hover", 2, 7, "reference"), Feature(uri, "completion", 4, 0, "completion"));
        foreach (var id in new[] { "declaration", "reference" })
        {
            var markdown = Result(messages, id).GetProperty("contents").GetProperty("value").GetString();
            Assert.Contains("const count: int", markdown);
            Assert.Contains("再試行回数。", markdown);
        }
        var completion = Result(messages, "completion").EnumerateArray().Single(item => item.GetProperty("label").GetString() == "count");
        Assert.Equal(21, completion.GetProperty("kind").GetInt32());
        Assert.Equal("const count: int", completion.GetProperty("detail").GetString());
        var diagnostic = Assert.Single(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("RUK2011", diagnostic.GetProperty("code").GetString());
        Assert.Equal(1, diagnostic.GetProperty("severity").GetInt32());
    }

    [Fact]
    public void DocumentationTracksUnsavedImportsAndCompletionUsesOnlyTheSummary()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "/// Disk description.\nfn number(): int { return 1 }");
        var main = UriFor(project);
        var helper = UriFor(project, "helper.rukbat");
        var messages = Session(Open(main, "import './helper.rukbat' as helper\nhelper.number()\nhelper.num"),
            Open(helper, "/// 日本語の概要。\n///\n/// Detailed explanation.\nfn number(): int { return 1 }"),
            Feature(main, "hover", 1, 9, "hover"), Feature(main, "completion", 2, 10, "completion"));
        var hover = Result(messages, "hover").GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("日本語の概要。", hover);
        Assert.Contains("Detailed explanation.", hover);
        Assert.DoesNotContain("Disk description.", hover);
        var completion = Result(messages, "completion").EnumerateArray().Single(item => item.GetProperty("label").GetString() == "number");
        var description = completion.GetProperty("documentation").GetProperty("value").GetString();
        Assert.Contains("日本語の概要。", description);
        Assert.DoesNotContain("Detailed explanation.", description);
    }

    [Fact]
    public void DocumentationIsAvailableOnStructFieldsMethodsVariablesAndImports()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "//! Helper overview.\nfn f(): void {}");
        var uri = UriFor(project);
        var source = "import './helper.rukbat' as helper\n/// Record description.\nstruct Record {\n/// Name description.\nname: string\n/// Method description.\nfn f(): void {}\n}\n/// Variable description.\nvalue := Record { name: 'one' }\nvalue.f()\nprint(value.name)\n";
        var messages = Session(Open(uri, source), Feature(uri, "hover", 0, 0, "import"),
            Feature(uri, "hover", 9, 10, "struct"), Feature(uri, "hover", 9, 1, "variable"),
            Feature(uri, "hover", 10, 6, "method"), Feature(uri, "hover", 11, 13, "field"));
        foreach (var (id, description) in new[] { ("import", "Helper overview."), ("struct", "Record description."),
            ("variable", "Variable description."), ("method", "Method description."), ("field", "Name description.") })
            Assert.Contains(description, Result(messages, id).GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void DocumentationWarningsAreSeparateAndDeclarationHoverSurvivesBrokenImports()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var source = "import './missing.rukbat' as missing\n/// Explains this function.\nfn f(): void {}\n/// orphan\n";
        var messages = Session(Open(uri, source), Feature(uri, "hover", 2, 3));
        Assert.Contains("Explains this function.", Result(messages).GetProperty("contents").GetProperty("value").GetString());
        var diagnostics = LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray().ToArray();
        Assert.Contains(diagnostics, d => d.GetProperty("code").GetString() == "RUK6000" && d.GetProperty("severity").GetInt32() == 2);
        Assert.Contains(diagnostics, d => d.GetProperty("code").GetString() == "RUK3002" && d.GetProperty("severity").GetInt32() == 1);
    }

    [Fact]
    public void DocumentationReferencesBecomeDefinitionLinksAndExamplesDoNotRunInTheEditor()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var source = "/// See [`f`].\n///\n/// ```rukbat check\n/// missing()\n/// ```\nfn f(): void {}\nf()";
        var messages = Session(Open(uri, source), Feature(uri, "hover", 6, 0));
        Assert.Contains(uri + "#L6", Result(messages).GetProperty("contents").GetProperty("value").GetString());
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        Assert.False(File.Exists(project.PathFor("main.ps1")));
    }

    [Fact]
    public void InitializationAdvertisesImplementedFeatures()
    {
        var capabilities = Result(Session(), "1").GetProperty("capabilities");
        Assert.Equal("utf-16", capabilities.GetProperty("positionEncoding").GetString());
        Assert.Equal(1, capabilities.GetProperty("textDocumentSync").GetProperty("change").GetInt32());
        foreach (var name in new[] { "hoverProvider", "definitionProvider", "documentFormattingProvider" })
            Assert.True(capabilities.GetProperty(name).GetBoolean());
        Assert.False(capabilities.GetProperty("renameProvider").GetBoolean());
    }

    [Fact]
    public void PathCompletionAndHoverUseTheNewCanonicalContracts()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var completion = Result(Session(Open(uri, "import path\npath."), Feature(uri, "completion", 1, 5)));
        foreach (var name in new[] { "working_dir", "resolve", "relative", "is_absolute", "stem", "change_extension" })
        {
            var item = completion.EnumerateArray().Single(item => item.GetProperty("label").GetString() == name);
            Assert.Contains("path." + name + "(", item.GetProperty("detail").GetString());
            Assert.NotEmpty(item.GetProperty("documentation").GetProperty("value").GetString()!);
        }
        var messages = Session(Open(uri, "import path\nvalue := path.working_dir()"), Feature(uri, "hover", 1, 17));
        var hover = Result(messages).GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("path.working_dir(): string", hover);
        Assert.Contains("captured at entry-script startup", hover);
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
    }

    [Theory]
    [InlineData("list_dirs", "fs.list_dirs('.', '*')", "Hidden")]
    [InlineData("move_dir", "fs.move_dir('source', 'destination')", "same-volume")]
    [InlineData("append_text", "fs.append_text('log', 'entry', encoding.utf8)", "without a BOM")]
    public void FilesystemCompletionHoverAndDiagnosticsUseTheNewContracts(string name, string call, string description)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var completion = Result(Session(Open(uri, "import fs\nfs."), Feature(uri, "completion", 1, 3)));
        var item = completion.EnumerateArray().Single(item => item.GetProperty("label").GetString() == name);
        Assert.Contains("fs." + name + "(", item.GetProperty("detail").GetString());
        Assert.Contains(description, item.GetProperty("documentation").GetProperty("value").GetString());
        var messages = Session(Open(uri, "import fs; import encoding\n" + call), Feature(uri, "hover", 1, 4));
        var hover = Result(messages).GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("fs." + name + "(", hover);
        Assert.Contains(description, hover);
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
    }

    [Theory]
    [InlineData("value := missing", "RUK2004")]
    [InlineData("value: int = \"東京\"", "RUK2001")]
    [InlineData("fn bad(: void {}", "RUK1000")]
    [InlineData("# unsupported", "RUK1000")]
    [InlineData("import \"./missing.rukbat\" as missing", "RUK3002")]
    public void DiagnosticsReuseCompilerCodes(string text, string code)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var diagnostics = LastDiagnostics(Session(Open(uri, text)), uri);
        Assert.Equal(1, diagnostics.GetProperty("version").GetInt32());
        Assert.Contains(diagnostics.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == code);
        Assert.False(File.Exists(project.PathFor("main.ps1")));
    }

    [Fact]
    public void DiagnosticsTrackUnsavedVersionsAndClearOnRepairAndClose()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var messages = Session(Open(uri, "x := missing"), Change(uri, "x := 1", 3), Change(uri, "x := missing", 2),
            Notice("textDocument/didClose", new
            {
                textDocument = new
                {
                    uri
                }
            }));
        var reports = messages.Where(m => m.TryGetProperty("method", out _)).Select(m => m.GetProperty("params")).ToArray();
        Assert.Equal(3, reports.Length);
        Assert.NotEmpty(reports[0].GetProperty("diagnostics").EnumerateArray());
        Assert.Empty(reports[1].GetProperty("diagnostics").EnumerateArray());
        Assert.Equal(3, reports[1].GetProperty("version").GetInt32());
        Assert.Empty(reports[2].GetProperty("diagnostics").EnumerateArray());
        Assert.False(reports[2].TryGetProperty("version", out _));
        Assert.False(File.Exists(project.PathFor("main.rukbat")));
    }

    [Fact]
    public void ImportsUseUnsavedBuffersAndCloseRestoresDiskContent()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "fn number(): int { return 1 }");
        var helper = UriFor(project, "helper.rukbat");
        var main = UriFor(project);
        var messages = Session(Open(main, "import \"./helper.rukbat\" as helper\nx: int = helper.number()"),
            Open(helper, "fn number(): string { return \"東京\" }"),
            Notice("textDocument/didClose", new
            {
                textDocument = new
                {
                    uri = helper
                }
            }));
        var reports = messages.Where(m => m.TryGetProperty("method", out _) && m.GetProperty("params").GetProperty("uri").GetString() == main).ToArray();
        Assert.Empty(reports[0].GetProperty("params").GetProperty("diagnostics").EnumerateArray());
        Assert.Contains(reports[1].GetProperty("params").GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == "RUK2001");
        Assert.Empty(LastDiagnostics(messages, main).GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("fn number(): int { return 1 }", File.ReadAllText(project.PathFor("helper.rukbat")));
    }

    [Fact]
    public void EncodedWindowsDriveUrisResolveImportsAndUnsavedOverlays()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var project = new Project();
        project.Write("helper.rukbat", "fn number(): int { return 1 }");
        var mainUri = UriFor(project);
        var helperUri = UriFor(project, "helper.rukbat");
        var main = mainUri[..8] + mainUri[8..].Replace(":/", "%3A/", StringComparison.Ordinal);
        var helper = helperUri[..8] + helperUri[8..].Replace(":/", "%3A/", StringComparison.Ordinal);
        var messages = Session(Open(main, "import \"./helper.rukbat\" as helper\nx := helper.number()"),
            Open(helper, "fn number(): string { return \"東京\" }"), Feature(main, "hover", 1, 0));
        Assert.Empty(LastDiagnostics(messages, main).GetProperty("diagnostics").EnumerateArray());
        Assert.Contains("x: string", Result(messages).GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void DiagnosticsUseUtf16PositionsAndAcceptIncrementalChanges()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "print(\"😀東京\"); value := missing\r\n";
        var character = source.IndexOf("missing", StringComparison.Ordinal);
        var messages = Session(Open(uri, source), Notice("textDocument/didChange", new
        {
            textDocument = new
            {
                uri,
                version = 2
            },
            contentChanges = new[] { new { text = "1", range = new { start = new { line = 0, character }, end = new { line = 0, character = character + 7 } } } }
        }));
        var initial = messages.First(m => m.TryGetProperty("method", out _)).GetProperty("params").GetProperty("diagnostics")[0];
        Assert.Equal(character, initial.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
    }

    [Theory]
    [InlineData("fs", "read_text")]
    [InlineData("fs", "stat")]
    [InlineData("fs", "Info")]
    [InlineData("fs", "move_files_batch")]
    [InlineData("path", "script_dir")]
    [InlineData("csv", "parse_table")]
    [InlineData("json", "as")]
    [InlineData("env", "get")]
    [InlineData("process", "run")]
    [InlineData("process", "options")]
    [InlineData("process", "Options")]
    [InlineData("process", "Input")]
    [InlineData("process", "EnvironmentVariable")]
    [InlineData("zip", "create")]
    [InlineData("zip", "list")]
    [InlineData("zip", "extract")]
    [InlineData("zip", "options")]
    [InlineData("zip", "Options")]
    [InlineData("zip", "Entry")]
    [InlineData("hash", "sha256")]
    [InlineData("hash", "Algorithm")]
    [InlineData("hash", "file")]
    [InlineData("hash", "text")]
    [InlineData("hash", "parse")]
    [InlineData("hash", "name")]
    [InlineData("text", "parse_int")]
    [InlineData("text", "length")]
    [InlineData("text", "slice")]
    [InlineData("text", "index_of")]
    [InlineData("text", "upper")]
    [InlineData("text", "lower")]
    [InlineData("array", "slice")]
    [InlineData("array", "remove_at")]
    [InlineData("array", "sort")]
    [InlineData("array", "sort_descending")]
    [InlineData("array", "sort_by")]
    [InlineData("array", "sort_by_descending")]
    [InlineData("test", "assert")]
    [InlineData("encoding", "utf8")]
    [InlineData("encoding", "Encoding")]
    [InlineData("encoding", "parse")]
    [InlineData("datetime", "tokyo")]
    [InlineData("datetime", "eastern")]
    [InlineData("datetime", "pacific_mexico")]
    [InlineData("datetime", "utc_plus_12")]
    public void IncompleteMemberCompletionShowsCanonicalStandardApi(string module, string expected)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var result = Result(Session(Open(uri, $"import {module}\nvalue := {module}."), Feature(uri, "completion", 1, 9 + module.Length + 1)));
        Assert.Contains(result.EnumerateArray(), s => s.GetProperty("label").GetString() == expected);
        Assert.DoesNotContain(result.EnumerateArray(), s => s.GetProperty("label").GetString() == "directories");
    }

    [Fact]
    public void ImportCompletionDoesNotRequireValidSyntax()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var result = Result(Session(Open(uri, "import js"), Feature(uri, "completion", 0, 9)));
        Assert.Equal("json", result[0].GetProperty("label").GetString());
    }

    [Fact]
    public void CompletionFiltersScopesAndPrefixesInsideUnclosedFunction()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "fn other(): void { private_name := 1 }\nfn current(parameter: int): void {\n    local_name := 1\n    lo";
        var result = Result(Session(Open(uri, source), Feature(uri, "completion", 3, 6)));
        Assert.Contains(result.EnumerateArray(), s => s.GetProperty("label").GetString() == "local_name");
        Assert.DoesNotContain(result.EnumerateArray(), s => s.GetProperty("label").GetString() == "private_name");
    }

    [Fact]
    public void CompletionFindsFieldsMethodsAndArrayMembers()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "struct User { name: string; fn label(): string { return self.name } }\nuser := User { name: \"Taro\" }\nuser.\n";
        var result = Result(Session(Open(uri, source), Feature(uri, "completion", 2, 5)));
        Assert.Contains(result.EnumerateArray(), s => s.GetProperty("label").GetString() == "name");
        Assert.Contains(result.EnumerateArray(), s => s.GetProperty("label").GetString() == "label");
        var array = Result(Session(Open(uri, "items := [1]\nitems."), Feature(uri, "completion", 1, 6)));
        Assert.Equal(new[] { "append", "length" }, array.EnumerateArray().Select(s => s.GetProperty("label").GetString()).ToArray());
    }

    [Theory]
    [InlineData("struct User { name: string }\nfn create(): User { return User { name: \"Taro\" } }\ncreate().", 2, 9)]
    [InlineData("struct User { name: string }\nusers := [User { name: \"Taro\" }]\nusers[0].", 2, 9)]
    [InlineData("struct User { name: string }\nstruct Box { user: User }\nbox := Box { user: User { name: \"Taro\" } }\nbox.user.", 3, 9)]
    public void CompletionUsesCheckedTypesForExpressionReceivers(string source, int line, int character)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var result = Result(Session(Open(uri, source), Feature(uri, "completion", line, character)));
        Assert.Contains(result.EnumerateArray(), s => s.GetProperty("label").GetString() == "name");
    }

    [Fact]
    public void CompletionDoesNotLeakClosedBlocksOrFutureLocalDeclarations()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "fn run(parameter: int): void {\n    if true { secret := 1 }\n    \n    future := 1\n}";
        var result = Result(Session(Open(uri, source), Feature(uri, "completion", 2, 4)));
        Assert.Contains(result.EnumerateArray(), s => s.GetProperty("label").GetString() == "parameter");
        Assert.DoesNotContain(result.EnumerateArray(), s => s.GetProperty("label").GetString() is "secret" or "future");
    }

    [Fact]
    public void DefinitionFindsTypesInsideArrayAndNullableAnnotations()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "struct User { name: string }\nusers: User[]? = null";
        var result = Result(Session(Open(uri, source), Feature(uri, "definition", 1, 8)));
        Assert.Equal(0, result.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(7, result.GetProperty("range").GetProperty("start").GetProperty("character").GetInt32());
    }

    [Theory]
    [InlineData("// fs.", 6)]
    [InlineData("print(\"fs.\")", 10)]
    public void CompletionIsEmptyInsideCommentsAndStrings(string source, int character)
    {
        using var project = new Project();
        var uri = UriFor(project);
        Assert.Empty(Result(Session(Open(uri, source), Feature(uri, "completion", 0, character))).EnumerateArray());
    }

    [Fact]
    public void HoverUsesInferredAndNarrowedTypes()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var result = Result(Session(Open(uri, "x: string? = null\nif x != null {\n    print(x)\n}"), Feature(uri, "hover", 2, 10)));
        Assert.Contains("x: string\n", result.GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public void MathCompletionAndLargeIntegerDiagnosticsUseTheNumericContract()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var completion = Result(Session(Open(uri, "import math\nmath."), Feature(uri, "completion", 1, 5)));
        var conversion = completion.EnumerateArray().Single(item => item.GetProperty("label").GetString() == "to_float");
        Assert.Equal("math.to_float(value: int): float", conversion.GetProperty("detail").GetString());
        var accepted = Session(Open(uri, "value := 9223372036854775807"));
        Assert.Empty(LastDiagnostics(accepted, uri).GetProperty("diagnostics").EnumerateArray());
        var rejected = Session(Open(uri, "value := 1 + 1.0"));
        Assert.Contains(LastDiagnostics(rejected, uri).GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "RUK2001");
    }

    [Fact]
    public void DateTimeCompletionHoverAndDiagnosticsUseOpaqueTypes()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var completion = Result(Session(Open(uri, "import datetime\ndatetime."), Feature(uri, "completion", 1, 9)));
        foreach (var name in new[] { "Timestamp", "Date", "Duration", "Zone", "utc", "tokyo", "eastern", "now", "parse_local", "add_months", "from_unix_seconds" })
            Assert.Contains(completion.EnumerateArray(), item => item.GetProperty("label").GetString() == name);
        var now = completion.EnumerateArray().Single(item => item.GetProperty("label").GetString() == "now");
        Assert.Equal("datetime.now(): datetime.Timestamp", now.GetProperty("detail").GetString());
        const string source = "import datetime\ninstant := datetime.now()\nzone: datetime.Zone = datetime.utc";
        var messages = Session(Open(uri, source), Feature(uri, "hover", 1, 2, "inferred"), Feature(uri, "hover", 2, 16, "type"));
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        Assert.Contains("instant: datetime.Timestamp", Result(messages, "inferred").GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("datetime.Zone", Result(messages, "type").GetProperty("contents").GetProperty("value").GetString());
        Assert.NotEmpty(LastDiagnostics(Session(Open(uri, "import datetime\ndatetime.utc = datetime.local_zone()")), uri).GetProperty("diagnostics").EnumerateArray());
    }

    [Theory]
    [InlineData("tokyo", "Tokyo Standard Time")]
    [InlineData("eastern", "Eastern Standard Time")]
    [InlineData("pacific_mexico", "Pacific Standard Time (Mexico)")]
    [InlineData("utc_minus_02", "UTC-02")]
    [InlineData("utc_plus_12", "UTC+12")]
    public void NamedZoneCompletionHoverAndDiagnosticsUseTheSameCatalog(string name, string id)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var completion = Result(Session(Open(uri, "import datetime\ndatetime."), Feature(uri, "completion", 1, 9)));
        var constant = completion.EnumerateArray().Single(item => item.GetProperty("label").GetString() == name);
        Assert.Equal($"datetime.{name}: datetime.Zone", constant.GetProperty("detail").GetString());
        Assert.Contains(id, constant.GetProperty("documentation").GetProperty("value").GetString());
        var messages = Session(Open(uri, $"import datetime\nzone := datetime.{name}"), Feature(uri, "hover", 1, 18));
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        var hover = Result(messages).GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains($"datetime.{name}: datetime.Zone", hover);
        Assert.Contains(id, hover);
        Assert.Contains("Read-only", hover);
        var invalid = Session(Open(uri, $"import datetime\ndatetime.{name} = datetime.utc"));
        Assert.Contains(LastDiagnostics(invalid, uri).GetProperty("diagnostics").EnumerateArray(),
            diagnostic => diagnostic.GetProperty("code").GetString() == "RUK2010");
    }

    [Fact]
    public void StandardHoverIncludesSignatureAndDocumentation()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var result = Result(Session(Open(uri, "import encoding; import fs\nx := fs.read_text(\"data.csv\", encoding.utf8)"), Feature(uri, "hover", 1, 9)));
        var markdown = result.GetProperty("contents").GetProperty("value").GetString();
        Assert.Contains("fs.read_text(path: string, encoding: encoding.Encoding): string", markdown);
        Assert.Contains("Error", markdown);
    }

    [Fact]
    public void ProcessEncodingAppearsInHoverAndCompletion()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var messages = Session(Open(uri, "import encoding; import process\nresult := process.run('tool.exe', [], process.options(null, encoding.windows_932))"),
            Feature(uri, "hover", 1, 19));
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        var signature = "process.run(executable: string, arguments: string[], options: process.Options): process.Result";
        Assert.Contains(signature, Result(messages).GetProperty("contents").GetProperty("value").GetString());
        var completion = Result(Session(Open(uri, "import process\nprocess."), Feature(uri, "completion", 1, 8)));
        var run = completion.EnumerateArray().Single(item => item.GetProperty("label").GetString() == "run");
        Assert.Equal(signature, run.GetProperty("detail").GetString());
    }

    [Fact]
    public void HashAlgorithmAndZipOptionsHaveTypedHoverAndMemberCompletion()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var messages = Session(Open(uri, "import hash\nselected := hash.sha256"), Feature(uri, "hover", 1, 18));
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        Assert.Contains("hash.sha256: hash.Algorithm", Result(messages).GetProperty("contents").GetProperty("value").GetString());
        var members = Result(Session(Open(uri, "import zip; import encoding\noptions := zip.options(encoding.utf8)\noptions."), Feature(uri, "completion", 2, 8)));
        foreach (var name in new[] { "name_encoding", "max_entries", "max_total_bytes" })
            Assert.Contains(members.EnumerateArray(), item => item.GetProperty("label").GetString() == name);
    }

    [Fact]
    public void HttpConstantsTypesAndOptionsHaveHoverAndCompletion()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var messages = Session(Open(uri, "import http\nvalue := http.status_200_ok"), Feature(uri, "hover", 1, 20));
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        Assert.Contains("http.status_200_ok: int = 200", Result(messages).GetProperty("contents").GetProperty("value").GetString());
        var constants = Result(Session(Open(uri, "import http\nhttp.status_"), Feature(uri, "completion", 1, 12)));
        Assert.Contains(constants.EnumerateArray(), item => item.GetProperty("label").GetString() == "status_404_not_found");
        var options = Result(Session(Open(uri, "import http\noptions := http.options(1000)\noptions."), Feature(uri, "completion", 2, 8)));
        foreach (var name in new[] { "timeout_ms", "max_response_bytes", "proxy_url", "use_windows_credentials", "max_redirects" })
            Assert.Contains(options.EnumerateArray(), item => item.GetProperty("label").GetString() == name);
        var functions = Result(Session(Open(uri, "import http\nhttp."), Feature(uri, "completion", 1, 5)));
        foreach (var name in new[] { "request", "send", "text", "download", "Body", "Method" })
            Assert.Contains(functions.EnumerateArray(), item => item.GetProperty("label").GetString() == name);
    }

    [Fact]
    public void EncodingCompletionAndHoverDescribeValidatedValuesAndTheirType()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var completion = Result(Session(Open(uri, "import encoding\nencoding."), Feature(uri, "completion", 1, 9)));
        foreach (var name in new[] { "utf8", "utf16_le", "utf16_be", "utf32_le", "utf32_be", "ascii", "iso_8859_1",
            "windows_874", "windows_1250", "windows_1251", "windows_1252", "windows_1253", "windows_1254",
            "windows_1255", "windows_1256", "windows_1257", "windows_1258", "windows_932", "windows_936",
            "windows_949", "windows_950", "gb18030", "windows_437", "windows_850", "windows_852", "windows_866" })
        {
            var item = completion.EnumerateArray().Single(item => item.GetProperty("label").GetString() == name);
            Assert.Equal(20, item.GetProperty("kind").GetInt32());
            Assert.Equal($"encoding.{name}: encoding.Encoding", item.GetProperty("detail").GetString());
            Assert.Contains("Read-only", item.GetProperty("documentation").GetProperty("value").GetString());
        }
        var factory = completion.EnumerateArray().Single(item => item.GetProperty("label").GetString() == "from_code_page");
        Assert.Contains("encoding.from_code_page(number: int): encoding.Encoding", factory.GetProperty("detail").GetString());
        const string source = "import encoding\nselected := encoding.windows_932\nother: encoding.Encoding? = selected";
        var messages = Session(Open(uri, source), Feature(uri, "hover", 1, 23, "value"),
            Feature(uri, "hover", 1, 2, "inferred"), Feature(uri, "hover", 2, 19, "type"));
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        Assert.Contains("Windows CP932", Result(messages, "value").GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("selected: encoding.Encoding", Result(messages, "inferred").GetProperty("contents").GetProperty("value").GetString());
        Assert.Contains("validated encoding type", Result(messages, "type").GetProperty("contents").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("encoding.utf8 = encoding.windows_932", "RUK2010")]
    [InlineData("selected := encoding.UTF8", "RUK2004")]
    [InlineData("import fs; fs.read_text('data.txt', 'utf-8')", "RUK2001")]
    public void EncodingDiagnosticsReuseCompilerValidation(string body, string code)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var diagnostics = LastDiagnostics(Session(Open(uri, "import encoding\n" + body)), uri);
        Assert.Contains(diagnostics.GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == code);
    }

    [Fact]
    public void DefinitionResolvesShadowedVariables()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "value := 1\nfn run(): void {\n    value := \"local\"\n    print(value)\n}\nprint(value)";
        var messages = Session(Open(uri, source), Feature(uri, "definition", 3, 11, "local"), Feature(uri, "definition", 5, 7, "global"));
        Assert.Equal(2, Result(messages, "local").GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(0, Result(messages, "global").GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public void DefinitionResolvesImportedFunctionsFieldsAndTypeAnnotations()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "struct User { name: string }\nfn create(): User { return User { name: \"東京\" } }");
        var uri = UriFor(project);
        var helper = UriFor(project, "helper.rukbat");
        const string source = "import \"./helper.rukbat\" as helper\nuser: helper.User = helper.create()\nprint(user.name)";
        var messages = Session(Open(uri, source), Feature(uri, "definition", 1, 28, "function"),
            Feature(uri, "definition", 1, 14, "type"), Feature(uri, "definition", 2, 12, "field"));
        foreach (var id in new[] { "function", "type", "field" })
            Assert.Equal(helper, Result(messages, id).GetProperty("uri").GetString());
        Assert.Equal(1, Result(messages, "function").GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(0, Result(messages, "type").GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
    }

    [Fact]
    public void FormattingReturnsAnEditWithoutChangingTheFile()
    {
        using var project = new Project();
        project.Write("main.rukbat", "original");
        var uri = UriFor(project);
        var request = Request("feature", "textDocument/formatting", new
        {
            textDocument = new
            {
                uri
            },
            options = new
            {
                tabSize = 8,
                insertSpaces = false
            }
        });
        var result = Result(Session(Open(uri, "x:=1\r\n\r\n"), request));
        Assert.Equal("x := 1\n", result[0].GetProperty("newText").GetString());
        Assert.Equal("original", File.ReadAllText(project.PathFor("main.rukbat")));
        Assert.Empty(Result(Session(Open(uri, "x :="), request)).EnumerateArray());
    }

    [Fact]
    public void ProtocolReportsUnknownMethodsAndInvalidPositionsThenKeepsServing()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var messages = Session(Open(uri, "x := 1\nprint(x)"), Request("unknown", "unsupported"), Feature(uri, "hover", 500, 0, "bad"), Feature(uri, "hover", 1, 6));
        Assert.Equal(-32601, Answer(messages, "unknown").GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32602, Answer(messages, "bad").GetProperty("error").GetProperty("code").GetInt32());
        Assert.Contains("x: int", Result(messages).GetProperty("contents").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("Content-Length: 20\r\n\r\n{}")]
    [InlineData("Content-Length: -1\r\n\r\n")]
    [InlineData("Content-Length: 16777217\r\n\r\n")]
    [InlineData("Content-Length: 2\r\nContent-Length: 2\r\n\r\n{}")]
    [InlineData("Unknown: 2\r\n\r\n")]
    public void InvalidTransportFailsWithStderrOnly(string frame)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(frame));
        using var output = new MemoryStream();
        using var log = new StringWriter();
        Assert.Equal(2, LanguageServer.Run(input, output, log));
        Assert.Empty(output.ToArray());
        Assert.Contains("LSP transport failure", log.ToString());
    }

    [Fact]
    public void RequestsBeforeInitializationAndAfterShutdownAreRejected()
    {
        using var input = new MemoryStream(Frames([Request(1, "textDocument/hover"), Request(2, "initialize"), Request(3, "shutdown"), Request(4, "textDocument/hover"), Notice("exit", new { })]));
        using var output = new MemoryStream();
        Assert.Equal(0, LanguageServer.Run(input, output, TextWriter.Null));
        var messages = Decode(output.ToArray());
        Assert.Equal(-32002, Answer(messages, "1").GetProperty("error").GetProperty("code").GetInt32());
        Assert.Equal(-32600, Answer(messages, "4").GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void MalformedJsonReportsParseErrorAndRecoversAtTheNextFrame()
    {
        using var input = new MemoryStream([.. Encoding.UTF8.GetBytes("Content-Length: 3\r\n\r\n???"),
            .. Frames([Request(1, "initialize"), Request(2, "shutdown"), Notice("exit", new { })])]);
        using var output = new MemoryStream();
        Assert.Equal(0, LanguageServer.Run(input, output, TextWriter.Null));
        var messages = Decode(output.ToArray());
        Assert.Equal(-32700, messages[0].GetProperty("error").GetProperty("code").GetInt32());
        Assert.True(Result(messages, "1").GetProperty("capabilities").GetProperty("hoverProvider").GetBoolean());
    }

    [Fact]
    public void InvalidNotificationDoesNotCorruptTheOpenDocument()
    {
        using var project = new Project();
        var uri = UriFor(project);
        using var input = new MemoryStream(Frames([Request(1, "initialize"), Open(uri, "x := 1\nprint(x)"),
            Notice("textDocument/didChange", new { textDocument = new { uri, version = 2 }, contentChanges = new[] { new { text = (string?)null } } }),
            Feature(uri, "hover", 1, 6), Request(2, "shutdown"), Notice("exit", new { })]));
        using var output = new MemoryStream();
        using var log = new StringWriter();
        Assert.Equal(0, LanguageServer.Run(input, output, log));
        Assert.Contains("textDocument/didChange: Expected a string value", log.ToString());
        Assert.Contains("x: int", Result(Decode(output.ToArray())).GetProperty("contents").GetProperty("value").GetString());
    }

    [Fact]
    public async Task CliUsesBinaryStdioAndDoesNotExecuteUserCode()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        info.ArgumentList.Add(typeof(Cli).Assembly.Location);
        info.ArgumentList.Add("lsp");
        using var process = Process.Start(info)!;
        var output = new MemoryStream();
        var capture = process.StandardOutput.BaseStream.CopyToAsync(output);
        var error = process.StandardError.ReadToEndAsync();
        await process.StandardInput.BaseStream.WriteAsync(Frames([Request(1, "initialize"), Open(uri, "throw Error { message: \"東京😀\" }"), Request(2, "shutdown"), Notice("exit", new { })]));
        process.StandardInput.Close();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(deadline.Token);
        }
        catch (OperationCanceledException) { process.Kill(true); throw; }
        await capture;
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("", await error);
        Assert.Empty(LastDiagnostics(Decode(output.ToArray()), uri).GetProperty("diagnostics").EnumerateArray());
        Assert.False(File.Exists(project.PathFor("main.ps1")));
    }
}
