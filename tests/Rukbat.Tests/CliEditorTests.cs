using System.Text.Json;

namespace Rukbat.Tests
{
    public sealed partial class LanguageServerTests
    {
        [Fact]
        public void CliDefinitionsSupportCompletionHoverReferencesAndCheckedRename()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "import cli\ncommand := cli.command('demo', 'Demo')\npattern := cli.option('pattern', 'Pattern')\npattern.default_value = '*'\ncommand.options = [pattern]\n";
            var messages = EditorSession(project, Open(uri, source), Feature(uri, "completion", 1, 15, "completion"),
                Feature(uri, "hover", 2, 18, "hover"), References(uri, 2, 2), Rename(uri, 2, 2, "filter"));
            Assert.Contains(Result(messages, "completion").EnumerateArray(), item => item.GetProperty("label").GetString() == "parse");
            Assert.Contains("cli.option(name: string, description: string): cli.Option", Result(messages, "hover").ToString());
            // References/rename share the feature id; inspect responses in request order.
            var responses = messages.Where(m => m.TryGetProperty("id", out var id) && id.ToString() == "feature").ToArray();
            Assert.Equal(3, responses[0].GetProperty("result").GetArrayLength());
            var change = Assert.Single(responses[1].GetProperty("result").GetProperty("documentChanges").EnumerateArray());
            Assert.Contains("filter.default_value", ApplyEdits(source, change.GetProperty("edits")));
            Assert.DoesNotContain(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray(), d => d.GetProperty("severity").GetInt32() == 1);
        }

        [Fact]
        public void CliSignatureHelpUsesCanonicalDocsInAnUnfinishedCall()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "import cli\ncli.option('pattern', ";
            var result = Result(EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", 1, 22)));
            Assert.NotEqual(JsonValueKind.Null, result.ValueKind);
            Assert.Equal("cli.option(name: string, description: string): cli.Option", result.GetProperty("signatures")[0].GetProperty("label").GetString());
            Assert.Equal(1, result.GetProperty("activeParameter").GetInt32());
        }
    }
}
