using System.Diagnostics;
using System.Text.Json;

namespace Rukbat.Tests;

public sealed partial class LanguageServerTests
{
    private static List<JsonElement> EditorSession(Project project, params object[] messages)
    {
        using var input = new MemoryStream(Frames([
            Request(1, "initialize", new
            {
                workspaceFolders = new[] { new { uri = new Uri(project.DirectoryPath).AbsoluteUri, name = "test" } },
                capabilities = new
                {
                    workspace = new
                    {
                        workspaceEdit = new
                        {
                            documentChanges = true,
                            failureHandling = "textOnlyTransactional"
                        }
                    },
                    textDocument = new
                    {
                        rename = new
                        {
                            prepareSupport = true
                        },
                        signatureHelp = new
                        {
                            signatureInformation = new
                            {
                                parameterInformation = new
                                {
                                    labelOffsetSupport = true
                                },
                                documentationFormat = new[] { "markdown" }
                            }
                        }
                    }
                }
            }),
            .. messages,
            Request("shutdown", "shutdown"),
            Notice("exit", new { })]));
        using var output = new MemoryStream();
        using var log = new StringWriter();
        Assert.Equal(0, LanguageServer.Run(input, output, log));
        Assert.Equal("", log.ToString());
        return Decode(output.ToArray());
    }

    private static object References(string uri, int line, int character, bool declarations = true) => Request("feature", "textDocument/references",
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
            },
            context = new
            {
                includeDeclaration = declarations
            }
        });
    private static object Rename(string uri, int line, int character, string name) => Request("feature", "textDocument/rename",
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
            },
            newName = name
        });

    private static string ApplyEdits(string source, JsonElement edits)
    {
        var lines = new[] { 0 }.Concat(source.Select((c, i) => (c, i)).Where(p => p.c == '\n').Select(p => p.i + 1)).ToArray();
        int Offset(JsonElement position) => lines[position.GetProperty("line").GetInt32()] + position.GetProperty("character").GetInt32();
        foreach (var edit in edits.EnumerateArray().OrderByDescending(e => Offset(e.GetProperty("range").GetProperty("start"))))
        {
            var range = edit.GetProperty("range");
            source = source[..Offset(range.GetProperty("start"))] + edit.GetProperty("newText").GetString() + source[Offset(range.GetProperty("end"))..];
        }
        return source;
    }

    [Fact]
    public void ReferencesKeepShadowedAndNarrowedVariableIdentities()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "value: string? = '東京'\nif value != null { print(value) }\nfn f(): void { value := 1; print(value) }\n";
        var all = Result(EditorSession(project, Open(uri, source), References(uri, 0, 1)));
        Assert.Equal(3, all.GetArrayLength());
        Assert.DoesNotContain(all.EnumerateArray(), item => item.GetProperty("range").GetProperty("start").GetProperty("line").GetInt32() == 2);
        Assert.Equal(2, Result(EditorSession(project, Open(uri, source), References(uri, 0, 1, false))).GetArrayLength());
        Assert.Equal(2, Result(EditorSession(project, Open(uri, source), References(uri, 2, 16))).GetArrayLength());
    }

    [Fact]
    public void ReferencesFindClosedCallersAndDeduplicateSharedDependencies()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "fn number(): int { return 1 }");
        project.Write("main.rukbat", "import './helper.rukbat' as h\nprint(h.number())");
        project.Write("other.rukbat", "import './helper.rukbat' as h\nprint(h.number())");
        project.Write("broken.rukbat", "x :=");
        project.Write(".git/ignored.rukbat", "import '../helper.rukbat' as h\nprint(h.number())");
        var helper = UriFor(project, "helper.rukbat");
        var result = Result(EditorSession(project, Open(helper, File.ReadAllText(project.PathFor("helper.rukbat"))), References(helper, 0, 4)));
        Assert.Equal(3, result.GetArrayLength());
        Assert.Equal(3, result.EnumerateArray().Select(item => item.GetProperty("uri").GetString()).Distinct().Count());
    }

    [Fact]
    public void BrokenCallerAfterSyntaxErrorPreventsPartialReferences()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "fn number(): int { return 1 }");
        project.Write("main.rukbat", "bad :=\nimport './helper.rukbat' as h\nprint(h.number())");
        var uri = UriFor(project, "helper.rukbat");
        var answer = Answer(EditorSession(project, Open(uri, File.ReadAllText(project.PathFor("helper.rukbat"))), References(uri, 0, 4)));
        Assert.Contains("Cannot resolve all references", answer.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public void RenameUsesUnsavedContentsVersionedEditsAndLeavesDiskUntouched()
    {
        using var project = new Project();
        project.Write("main.rukbat", "disk");
        var uri = UriFor(project);
        const string source = "const 名前 := 1\r\nprint('😀'); print(名前)\r\n// 名前\r\n";
        var messages = EditorSession(project, Open(uri, source, 7), Rename(uri, 0, 7, "回数"));
        var change = Assert.Single(Result(messages).GetProperty("documentChanges").EnumerateArray());
        Assert.Equal(7, change.GetProperty("textDocument").GetProperty("version").GetInt32());
        Assert.Equal(source.Replace("const 名前", "const 回数").Replace("print(名前)", "print(回数)"), ApplyEdits(source, change.GetProperty("edits")));
        Assert.Equal("disk", File.ReadAllText(project.PathFor("main.rukbat")));
        Assert.False(File.Exists(project.PathFor("main.ps1")));
    }

    [Theory]
    [InlineData("count := 1\nif true { total := 2; print(count) }", 0, 1, "total")]
    [InlineData("count := 1\nif true { value := 2; print(count); print(value) }", 1, 12, "count")]
    [InlineData("count := 1\nother := 2\nprint(count)", 0, 1, "other")]
    public void RenameRejectsCaptureInBothDirectionsAndRedeclaration(string source, int line, int character, string name)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var answer = Answer(EditorSession(project, Open(uri, source), Rename(uri, line, character, name)));
        Assert.True(answer.TryGetProperty("error", out _), answer.ToString());
        Assert.False(answer.TryGetProperty("result", out _));
    }

    [Theory]
    [InlineData("const")]
    [InlineData("a b")]
    [InlineData("1name")]
    [InlineData("")]
    [InlineData("name\n")]
    public void RenameRejectsInvalidIdentifiers(string name)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var answer = Answer(EditorSession(project, Open(uri, "value := 1"), Rename(uri, 0, 1, name)));
        Assert.Equal(-32602, answer.GetProperty("error").GetProperty("code").GetInt32());
    }

    [Fact]
    public void RenameRechecksAfterPrepareAndSameNameHasNoEdits()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var same = Result(EditorSession(project, Open(uri, "value := 1"), Rename(uri, 0, 1, "value")));
        Assert.Empty(same.GetProperty("documentChanges").EnumerateArray());
        var messages = EditorSession(project, Open(uri, "value := 1\nprint(value)"), Feature(uri, "prepareRename", 0, 1, "prepare"),
            Change(uri, "value := 1\nother := 2\nprint(value)"), Rename(uri, 0, 1, "other"));
        Assert.Equal("value", Result(messages, "prepare").GetProperty("placeholder").GetString());
        Assert.True(Answer(messages).TryGetProperty("error", out _));
    }

    [Fact]
    public void RenameImportedFunctionIncludesClosedCallersAndUnsavedDefinition()
    {
        using var project = new Project();
        const string helperSource = "fn number(): int { return 1 }";
        const string mainSource = "import './helper.rukbat' as h\nprint(h.number())";
        project.Write("helper.rukbat", helperSource);
        project.Write("main.rukbat", mainSource);
        var helper = UriFor(project, "helper.rukbat");
        var result = Result(EditorSession(project, Open(helper, helperSource.Replace("1", "2"), 4), Rename(helper, 0, 4, "count")));
        var changes = result.GetProperty("documentChanges").EnumerateArray().ToArray();
        Assert.Equal(2, changes.Length);
        var main = changes.Single(c => c.GetProperty("textDocument").GetProperty("uri").GetString() == UriFor(project));
        Assert.Equal(JsonValueKind.Null, main.GetProperty("textDocument").GetProperty("version").ValueKind);
        Assert.Contains("h.count()", ApplyEdits(mainSource, main.GetProperty("edits")));
        Assert.Equal(helperSource, File.ReadAllText(project.PathFor("helper.rukbat")));
    }

    [Fact]
    public void AliasRenameIncludesQualifiedTypesAndGenericTypeArguments()
    {
        using var project = new Project();
        project.Write("types.rukbat", "struct User { name: string }");
        const string source = "import './types.rukbat' as model\nimport json\nu: model.User = model.User { name: 'A' }\nv := json.parse<model.User>('{\"name\":\"B\"}')\n";
        var uri = UriFor(project);
        var change = Assert.Single(Result(EditorSession(project, Open(uri, source), Rename(uri, 0, 27, "types"))).GetProperty("documentChanges").EnumerateArray());
        Assert.Equal(source.Replace("model", "types"), ApplyEdits(source, change.GetProperty("edits")));
    }

    [Theory]
    [InlineData("User", "Person", 0, 8)]
    [InlineData("name", "label", 0, 15)]
    [InlineData("User", "Person", 0, 36)]
    [InlineData("get", "read", 0, 46)]
    public void RenameStructFieldAndMethodUseBoundOccurrences(string oldName, string newName, int line, int character)
    {
        using var project = new Project();
        const string source = "struct User { name: string }; impl User { fn get(): string { return self.name } }\nu: User = User { name: 'A' }\nprint(u.get())\n";
        var uri = UriFor(project);
        var answer = Answer(EditorSession(project, Open(uri, source), Rename(uri, line, character, newName)));
        Assert.True(answer.TryGetProperty("result", out var result), answer.ToString());
        var change = Assert.Single(result.GetProperty("documentChanges").EnumerateArray());
        Assert.Equal(source.Replace(oldName, newName), ApplyEdits(source, change.GetProperty("edits")));
    }

    [Fact]
    public void ImplBeforeStructKeepsTypeNavigationMethodDocumentationAndRename()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "impl User {\n/// Reads the name.\nfn read(): string { return self.name }\n}\n/// User record.\nstruct User { name: string }\nuser := User { name: 'A' }\nprint(user.read())\n";
        var messages = EditorSession(project, Open(uri, source),
            Feature(uri, "definition", 0, 6, "type"),
            Feature(uri, "definition", 7, 12, "method"),
            Feature(uri, "hover", 7, 12, "hover"));
        Assert.Equal(5, Result(messages, "type").GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Equal(2, Result(messages, "method").GetProperty("range").GetProperty("start").GetProperty("line").GetInt32());
        Assert.Contains("Reads the name.", Result(messages, "hover").GetProperty("contents").GetProperty("value").GetString());
        var change = Assert.Single(Result(EditorSession(project, Open(uri, source), Rename(uri, 2, 4, "label")))
            .GetProperty("documentChanges").EnumerateArray());
        Assert.Equal(source.Replace("read", "label"), ApplyEdits(source, change.GetProperty("edits")));
    }

    [Fact]
    public void ImplIsOfferedAsAKeywordAndCannotBeUsedAsARenameTarget()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var completions = Result(EditorSession(project, Open(uri, "imp"), Feature(uri, "completion", 0, 3)));
        Assert.Contains(completions.EnumerateArray(), item => item.GetProperty("label").GetString() == "impl");
        Assert.True(Answer(EditorSession(project, Open(uri, "value := 1"), Rename(uri, 0, 1, "impl"))).TryGetProperty("error", out _));
    }

    [Fact]
    public void RenameUpdatesBoundDocumentationLinksAndCheckedExamplesOnly()
    {
        using var project = new Project();
        const string helper = "/// Returns an integer.\nfn number(): int { return 1 }";
        const string source = "//! See [`h.number`].\n//! ```rukbat check\n//! import './helper.rukbat' as h\n//! print(h.number())\n//! ```\n//! ```rukbat\n//! print(h.number())\n//! ```\nimport './helper.rukbat' as h\nprint(h.number())\n// h.number\n";
        project.Write("helper.rukbat", helper);
        project.Write("main.rukbat", source);
        var uri = UriFor(project, "helper.rukbat");
        var answer = Answer(EditorSession(project, Open(uri, helper), Rename(uri, 1, 4, "count")));
        Assert.True(answer.TryGetProperty("result", out var result), answer.ToString());
        var change = result.GetProperty("documentChanges").EnumerateArray().Single(c => c.GetProperty("textDocument").GetProperty("uri").GetString() == UriFor(project));
        var updated = ApplyEdits(source, change.GetProperty("edits"));
        Assert.Contains("[`h.count`]", updated);
        Assert.Contains("//! print(h.count())", updated);
        Assert.Contains("//! print(h.number())", updated);
        Assert.Contains("// h.number", updated);
    }

    [Theory]
    [InlineData("import fs\nfs.is_file('a')", 1, 4)]
    [InlineData("print(1)", 0, 2)]
    [InlineData("items: int[] = []\nitems.append(1)", 1, 8)]
    public void BuiltinsCannotBeRenamed(string source, int line, int character)
    {
        using var project = new Project();
        var uri = UriFor(project);
        Assert.True(Answer(EditorSession(project, Open(uri, source), Rename(uri, line, character, "other"))).TryGetProperty("error", out _));
    }

    [Fact]
    public void RenameRequiresVersionedTransactionalEdits()
    {
        using var project = new Project();
        var uri = UriFor(project);
        Assert.True(Answer(Session(Open(uri, "value := 1"), Rename(uri, 0, 1, "count"))).TryGetProperty("error", out _));
        var capabilities = Result(EditorSession(project), "1").GetProperty("capabilities");
        Assert.True(capabilities.GetProperty("referencesProvider").GetBoolean());
        Assert.True(capabilities.GetProperty("renameProvider").GetProperty("prepareProvider").GetBoolean());
    }

    [Theory]
    [InlineData("fn add(a: int, b: int): int { return a + b }\nprint(add(1, ", 1, "fn add(a: int, b: int): int")]
    [InlineData("import fs\nfs.write_text('a,b', ", 1, "fs.write_text(path: string, text: string, encoding: encoding.Encoding): void")]
    [InlineData("import fs\nfs.list_dirs('.', ", 1, "fs.list_dirs(directory: string, pattern: string): string[]")]
    [InlineData("import fs\nfs.move_dir('source', ", 1, "fs.move_dir(source: string, destination: string): void")]
    [InlineData("import fs\nfs.append_text('log', 'entry', ", 2, "fs.append_text(path: string, text: string, encoding: encoding.Encoding): void")]
    [InlineData("import fs\nfs.create_temp_file(", 0, "fs.create_temp_file(directory: string): string")]
    [InlineData("import fs\nfs.create_temp_dir(", 0, "fs.create_temp_dir(directory: string): string")]
    [InlineData("import process\nprocess.run('x', ['a,b', 'c'], ", 2, "process.run(executable: string, arguments: string[], options: process.Options): process.Result")]
    [InlineData("items: int[] = []\nitems.append(", 0, "append(value: int): void")]
    [InlineData("struct S {}; impl S { fn get(a: int): int { return a } }\ns := S {}\ns.get(", 0, "fn get(a: int): int")]
    [InlineData("import json\njson.parse<int>(", 0, "json.parse<T>(text: string): T")]
    [InlineData("import array\narray.sort(", 0, "array.sort(values: T[]): T[]")]
    [InlineData("import path\npath.resolve('C:/work', ", 1, "path.resolve(base_dir: string, value: string): string")]
    [InlineData("import path\npath.relative('C:/work', ", 1, "path.relative(base_dir: string, target: string): string")]
    [InlineData("import path\npath.change_extension('report.csv', ", 1, "path.change_extension(value: string, extension: string): string")]
    public void SignatureHelpWorksForIncompleteCalls(string source, int parameter, string label)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var lines = source.Split('\n');
        var answer = Answer(EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", lines.Length - 1, lines[^1].Length)));
        Assert.True(answer.TryGetProperty("result", out var result), answer.ToString());
        Assert.NotEqual(JsonValueKind.Null, result.ValueKind);
        var signature = result.GetProperty("signatures")[0];
        Assert.Equal(label, signature.GetProperty("label").GetString());
        Assert.Equal(parameter, result.GetProperty("activeParameter").GetInt32());
        var span = signature.GetProperty("parameters")[parameter].GetProperty("label");
        Assert.NotEmpty(label[span[0].GetInt32()..span[1].GetInt32()]);
    }

    [Theory]
    [InlineData("print('a,b')", 0, 10)]
    [InlineData("// print(", 0, 9)]
    [InlineData("unknown(", 0, 8)]
    [InlineData("value := (", 0, 10)]
    public void SignatureHelpDoesNotGuessInsideStringsCommentsOrUnknownCalls(string source, int line, int character)
    {
        using var project = new Project();
        var uri = UriFor(project);
        Assert.Equal(JsonValueKind.Null, Result(EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", line, character))).ValueKind);
    }

    [Fact]
    public void SignatureHelpReturnsToOuterCallAfterInnerClosingParenthesis()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "fn add(a: int, b: int): int { return a + b }\nprint(add(1, 2))";
        var messages = EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", 1, 13, "inner"), Feature(uri, "signatureHelp", 1, 15, "outer"));
        Assert.Contains("add", Result(messages, "inner").GetProperty("signatures")[0].GetProperty("label").GetString());
        Assert.Equal("print(value): void", Result(messages, "outer").GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void WorkspaceFolderChangesIncludeAndExcludeClosedCallers()
    {
        using var project = new Project();
        using var other = new Project();
        project.Write("helper.rukbat", "fn number(): int { return 1 }");
        var relative = Path.GetRelativePath(other.DirectoryPath, project.PathFor("helper.rukbat")).Replace('\\', '/');
        other.Write("main.rukbat", $"import '{relative}' as h\nprint(h.number())");
        var uri = UriFor(project, "helper.rukbat");
        var folder = new
        {
            uri = new Uri(other.DirectoryPath).AbsoluteUri,
            name = "other"
        };
        var add = Notice("workspace/didChangeWorkspaceFolders", new
        {
            @event = new
            {
                added = new[] { folder },
                removed = Array.Empty<object>()
            }
        });
        var remove = Notice("workspace/didChangeWorkspaceFolders", new
        {
            @event = new
            {
                added = Array.Empty<object>(),
                removed = new[] { folder }
            }
        });
        Assert.Equal(2, Result(EditorSession(project, Open(uri, File.ReadAllText(project.PathFor("helper.rukbat"))), add, References(uri, 0, 4))).GetArrayLength());
        Assert.Single(Result(EditorSession(project, Open(uri, File.ReadAllText(project.PathFor("helper.rukbat"))), add, remove, References(uri, 0, 4))).EnumerateArray());
    }

    [Fact]
    public void RenameRefusesRequiredEditsOutsideWorkspace()
    {
        using var project = new Project();
        using var external = new Project();
        project.Write("helper.rukbat", "fn number(): int { return 1 }");
        var toHelper = Path.GetRelativePath(external.DirectoryPath, project.PathFor("helper.rukbat")).Replace('\\', '/');
        external.Write("wrapper.rukbat", $"import '{toHelper}' as h\nfn run(): int {{ return h.number() }}");
        var toWrapper = Path.GetRelativePath(project.DirectoryPath, external.PathFor("wrapper.rukbat")).Replace('\\', '/');
        project.Write("main.rukbat", $"import '{toWrapper}' as w\nprint(w.run())");
        var uri = UriFor(project, "helper.rukbat");
        var answer = Answer(EditorSession(project, Open(uri, File.ReadAllText(project.PathFor("helper.rukbat"))), Rename(uri, 0, 4, "count")));
        Assert.Contains("outside the workspace", answer.GetProperty("error").GetProperty("message").GetString());
    }

    [Fact]
    public void StandardImportNamesAreFixedAndStandardCallsShareOneReferenceIdentity()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "import fs\nfs.is_file('a')\nfs.is_file('b')";
        Assert.Equal(2, Result(EditorSession(project, Open(uri, source), References(uri, 1, 4))).GetArrayLength());
        Assert.True(Answer(EditorSession(project, Open(uri, source), Feature(uri, "prepareRename", 0, 8))).TryGetProperty("error", out _));
    }

    [Theory]
    [InlineData("print(add(1, 2))\nfn add(a: int, b: int): int { return a + b }", 0, 10)]
    [InlineData("print(add(\nfn add(a: int, b: int): int { return a + b }", 0, 10)]
    public void SignatureHelpResolvesFunctionsDeclaredAfterTheCall(string source, int line, int character)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var result = Result(EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", line, character)));
        Assert.NotEqual(JsonValueKind.Null, result.ValueKind);
        Assert.Equal("fn add(a: int, b: int): int", result.GetProperty("signatures")[0].GetProperty("label").GetString());
    }

    [Fact]
    public void SignatureHelpIgnoresDeclarationParametersAndPreservesRealDiagnostics()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "fn add(a: int, b: int): int { return a + b }\nprint(add(1, ";
        var messages = EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", 0, 13, "declaration"), Feature(uri, "signatureHelp", 1, 13));
        Assert.Equal(JsonValueKind.Null, Result(messages, "declaration").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, Result(messages).ValueKind);
        Assert.Contains(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("code").GetString() == "RUK1000");
    }

    [Fact]
    public void BrokenExampleCallerCannotBeSilentlySkipped()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "fn number(): int { return 1 }");
        project.Write("main.rukbat", "//! ```rukbat check\n//! import './helper.rukbat' as h\n//! print(h.number())\n//! ```\nbad :=");
        var uri = UriFor(project, "helper.rukbat");
        Assert.True(Answer(EditorSession(project, Open(uri, File.ReadAllText(project.PathFor("helper.rukbat"))), Rename(uri, 0, 4, "count"))).TryGetProperty("error", out _));
    }

    [Fact]
    public void SignatureHelpKeepsLaterMethodsWhenAnArgumentIsMissing()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "struct S {}; impl S {\nfn first(): int { return self.add(1, ) }\nfn add(a: int, b: int): int { return a + b }\n}\n";
        var result = Result(EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", 1, 36)));
        Assert.NotEqual(JsonValueKind.Null, result.ValueKind);
        Assert.Equal("fn add(a: int, b: int): int", result.GetProperty("signatures")[0].GetProperty("label").GetString());
        Assert.Equal(1, result.GetProperty("activeParameter").GetInt32());
    }

    [Fact]
    public void RenameRefusesAnImportedJunctionAliasOfTheEditedModule()
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var project = new Project();
        const string helper = "fn number(): int { return 1 }";
        project.Write("real/helper.rukbat", helper);
        project.Write("main.rukbat", "import './real/helper.rukbat' as direct\nimport './link/helper.rukbat' as linked\nprint(direct.number())\nprint(linked.number())");
        var link = project.PathFor("link");
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{link}\" \"{project.PathFor("real")}\""
        };
        using var command = Process.Start(start)!;
        var output = command.StandardOutput.ReadToEnd();
        var error = command.StandardError.ReadToEnd();
        command.WaitForExit();
        Assert.True(command.ExitCode == 0, $"Cannot create the junction fixture: {output}{error}");
        try
        {
            var uri = UriFor(project, "real/helper.rukbat");
            var answer = Answer(EditorSession(project, Open(uri, helper), Rename(uri, 0, 4, "count")));
            Assert.Contains("links or junctions", answer.GetProperty("error").GetProperty("message").GetString());
            Assert.Equal(helper, File.ReadAllText(project.PathFor("real/helper.rukbat")));
        }
        finally
        {
            Directory.Delete(link);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SignatureHelpFallsBackToTextWithoutSupportedDocumentationFormats(bool empty)
    {
        using var project = new Project();
        var uri = UriFor(project);
        using var input = new MemoryStream(Frames([
            Request(1, "initialize", new
            {
                capabilities = new
                {
                    textDocument = new
                    {
                        signatureHelp = new
                        {
                            signatureInformation = new
                            {
                                documentationFormat = empty ? Array.Empty<string>() : new[] { "unsupported" }
                            }
                        }
                    }
                }
            }),
            Open(uri, "print("),
            Feature(uri, "signatureHelp", 0, 6),
            Request("shutdown", "shutdown"),
            Notice("exit", new { })]));
        using var output = new MemoryStream();
        using var log = new StringWriter();
        Assert.Equal(0, LanguageServer.Run(input, output, log));
        var result = Result(Decode(output.ToArray()));
        Assert.Equal(JsonValueKind.String, result.GetProperty("signatures")[0].GetProperty("documentation").ValueKind);
        Assert.Equal("value", result.GetProperty("signatures")[0].GetProperty("parameters")[0].GetProperty("label").GetString());
        Assert.Equal("", log.ToString());
    }
}
