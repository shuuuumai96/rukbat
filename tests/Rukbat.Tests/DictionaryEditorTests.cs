using System.Text.Json;

namespace Rukbat.Tests
{
    public sealed partial class LanguageServerTests
    {
        [Fact]
        public void DictionaryCompletionAndHoverUseCheckedKeyAndValueTypes()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "d := {'a': [1]}\nd.has('a')\nd.\n";
            var messages = EditorSession(project, Open(uri, source), Feature(uri, "completion", 2, 2, "completion"));
            var items = Result(messages, "completion").EnumerateArray().ToArray();
            Assert.Contains(items, item => item.GetProperty("detail").GetString() == "has(key: string): bool");
            Assert.Contains(items, item => item.GetProperty("detail").GetString() == "remove(key: string): void");
            Assert.Contains(items, item => item.GetProperty("detail").GetString() == "keys(): string[]");
            var hover = EditorSession(project, Open(uri, source[..source.LastIndexOf("d.", StringComparison.Ordinal)]), Feature(uri, "hover", 1, 3, "hover"));
            Assert.Contains("has(key: string): bool", Result(hover, "hover").ToString());
        }

        [Theory]
        [InlineData("d: dict[int, int] = {}\nd.has(", "has(key: int): bool")]
        [InlineData("d: dict[string, int] = {}\nd.remove(", "remove(key: string): void")]
        [InlineData("d: dict[string, int] = {}\nd.keys(", "keys(): string[]")]
        [InlineData("fn count(d: dict[string, dict[int, int]], n: int): int { return n }\ncount(", "fn count(d: dict[string, dict[int, int]], n: int): int")]
        public void DictionarySignaturesWorkInUnfinishedCalls(string source, string expected)
        {
            using var project = new Project();
            var uri = UriFor(project);
            var line = source.Count(character => character == '\n');
            var column = source.Length - source.LastIndexOf('\n') - 1;
            var result = Result(EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", line, column)));
            Assert.NotEqual(JsonValueKind.Null, result.ValueKind);
            Assert.Equal(expected, result.GetProperty("signatures")[0].GetProperty("label").GetString());
            if (expected.StartsWith("fn count", StringComparison.Ordinal))
            {
                Assert.Equal(2, result.GetProperty("signatures")[0].GetProperty("parameters").GetArrayLength());
            }
        }

        [Fact]
        public void DictionaryNestedTypeReferencesAndEntryExpressionsCanBeRenamed()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "struct Item { n: int }\nkey := 'Item'\nd: dict[string, Item[]] = {key: [Item {n: 1}]}\n";
            var responses = EditorSession(project, Open(uri, source), References(uri, 0, 8), Rename(uri, 0, 8, "Record"))
                .Where(message => message.TryGetProperty("id", out var id) && id.ToString() == "feature").ToArray();
            Assert.Equal(3, responses[0].GetProperty("result").GetArrayLength());
            var change = Assert.Single(responses[1].GetProperty("result").GetProperty("documentChanges").EnumerateArray());
            var renamed = ApplyEdits(source, change.GetProperty("edits"));
            Assert.Contains("dict[string, Record[]]", renamed);
            Assert.Contains("[Record {n: 1}]", renamed);
            Assert.Contains("'Item'", renamed);
        }

        [Theory]
        [InlineData("d: dict[string, ")]
        [InlineData("d := {'key': ")]
        [InlineData("d: dict[string, dict[int, ")]
        public void IncompleteDictionaryInputProducesDiagnosticsWithoutBreakingEditorRequests(string source)
        {
            using var project = new Project();
            var uri = UriFor(project);
            var messages = EditorSession(project, Open(uri, source), Feature(uri, "completion", 0, source.Length));
            Assert.NotEmpty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
            Assert.Equal(JsonValueKind.Array, Result(messages).ValueKind);
        }
    }

    public sealed class DictionaryToolingTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Fact]
        public void FormattingIsCanonicalAndIdempotent()
        {
            const string source = "import json;d:dict[string,dict[string,int[]]]={'x':{'y':[2],},};p:=json.parse<dict[string,dict[string,int[]]]>('{}')";
            using var project = new Project();
            project.Write("main.rukbat", source);
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
            var formatted = File.ReadAllText(project.PathFor("main.rukbat"));
            Assert.Contains("dict[string, dict[string, int[]]]", formatted);
            Assert.True(project.Compile().Success);
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
            Assert.Equal(formatted, File.ReadAllText(project.PathFor("main.rukbat")));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void DebugMetadataDescribesDictionariesWithoutExposingTheirStorage()
        {
            using var project = new Project();
            project.Write("main.rukbat", "d := {'x': [1]}\nprint(d.length)");
            var compilation = new Rukbat.Compiler().CompileFile(project.PathFor("main.rukbat"), debug: true);
            Assert.True(compilation.Success);
            var map = compilation.DebugMap!;
            var type = Assert.Single(map.Types.Values.Where(type => type.Name == "dict[string, int[]]"));
            Assert.Equal("string", map.Types[type.DictionaryKey!].Name);
            Assert.Equal("int[]", map.Types[type.DictionaryValue!].Name);
            Assert.Null(type.Fields);
        }
    }
}
