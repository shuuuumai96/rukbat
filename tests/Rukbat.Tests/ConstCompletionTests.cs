using System.Text.Json;

namespace Rukbat.Tests
{
    public sealed partial class LanguageServerTests
    {
        [Fact]
        public void ConstCompletionFollowsModuleDeclarationOrder()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "print(1)\nconst later := 2\nprint(later)\n";
            var messages = Session(Open(uri, source),
                Feature(uri, "completion", 0, 0, "before"),
                Feature(uri, "completion", 2, 0, "after"));

            Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
            Assert.DoesNotContain(Result(messages, "before").EnumerateArray(), item => item.GetProperty("label").GetString() == "later");
            var completion = Assert.Single(Result(messages, "after").EnumerateArray(), item => item.GetProperty("label").GetString() == "later");
            Assert.Equal("const later: int", completion.GetProperty("detail").GetString());
        }

        [Fact]
        public void ConstCompletionFollowsLocalDeclarationOrder()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "fn run(): void {\n    if true { const secret := 1 }\n    print(1)\n    const later := 2\n    print(later)\n}\n";
            var messages = Session(Open(uri, source),
                Feature(uri, "completion", 2, 4, "before"),
                Feature(uri, "completion", 4, 4, "after"));

            Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
            Assert.DoesNotContain(Result(messages, "before").EnumerateArray(), item => item.GetProperty("label").GetString() is "secret" or "later");
            var completion = Assert.Single(Result(messages, "after").EnumerateArray(), item => item.GetProperty("label").GetString() == "later");
            Assert.Equal("const later: int", completion.GetProperty("detail").GetString());
        }

        [Fact]
        public void ConstCompletionPreservesOuterBindingUntilShadowDeclaration()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "value := \"outer\"\n\nfn run(): void {\n    print(value)\n    const value := 2\n    print(value)\n}\n";
            var messages = Session(Open(uri, source),
                Feature(uri, "completion", 3, 4, "before"),
                Feature(uri, "completion", 5, 4, "after"));

            Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
            var before = Assert.Single(Result(messages, "before").EnumerateArray(), item => item.GetProperty("label").GetString() == "value");
            Assert.Equal("value: string", before.GetProperty("detail").GetString());
            var after = Assert.Single(Result(messages, "after").EnumerateArray(), item => item.GetProperty("label").GetString() == "value");
            Assert.Equal("const value: int", after.GetProperty("detail").GetString());
        }

        [Fact]
        public void ConstCompletionIncludesCapturedModuleValuesDeclaredAfterFunction()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "fn run(): void {\n    print(later)\n}\n\nconst later := 2\nrun()\n";
            var messages = Session(Open(uri, source), Feature(uri, "completion", 1, 4));

            Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
            var completion = Assert.Single(Result(messages).EnumerateArray(), item => item.GetProperty("label").GetString() == "later");
            Assert.Equal("const later: int", completion.GetProperty("detail").GetString());
        }

        [Theory]
        [InlineData("value := |\"one\"")]
        [InlineData("value := \"one\"|")]
        [InlineData("const value := |\"one\"")]
        [InlineData("const value := \"one\"|")]
        [InlineData("value: string = |\"one\"")]
        [InlineData("value: string = \"one\"|")]
        [InlineData("const value: string = |\"one\"")]
        [InlineData("const value: string = \"one\"|")]
        public void ConstCompletionExcludesTheInitializingName(string source)
        {
            var completion = CompletionAtDeclarationMarker(source);
            Assert.DoesNotContain(completion.EnumerateArray(), item => item.GetProperty("label").GetString() == "value");
        }

        [Theory]
        [InlineData("")]
        [InlineData("const ")]
        public void ConstCompletionInitializerStillUsesTheOuterBinding(string qualifier)
        {
            var source = "value := [1]\n\nfn run(): void {\n    " + qualifier + "value := |value.length\n}\n";
            var completion = Assert.Single(CompletionAtDeclarationMarker(source).EnumerateArray(), item => item.GetProperty("label").GetString() == "value");
            Assert.Equal("value: int[]", completion.GetProperty("detail").GetString());
        }

        [Theory]
        [InlineData("const value := 1\n\n|", true)]
        [InlineData("const value := 1;|", true)]
        [InlineData("const value := \"one\"|;", false)]
        [InlineData("fn run(): void { const value := 1 |}", false)]
        [InlineData("fn run(): void { const value := 1 }\n|", false)]
        [InlineData("fn run(): void { const value := 1; |print(value) }", true)]
        [InlineData("const value := [\n    1,\n    |2\n]\n", false)]
        [InlineData("const value := [\n    1,\n    2\n]|\n", false)]
        [InlineData("const value := [\n    1,\n    2\n]\n|", true)]
        public void ConstCompletionRespectsInitializerBoundaries(string source, bool visible)
        {
            var completion = CompletionAtDeclarationMarker(source);
            Assert.Equal(visible, completion.EnumerateArray().Any(item => item.GetProperty("label").GetString() == "value"));
        }

        private static JsonElement CompletionAtDeclarationMarker(string markedSource)
        {
            using var project = new Project();
            var uri = UriFor(project);
            var offset = markedSource.IndexOf('|');
            Assert.True(offset >= 0);
            var source = markedSource.Remove(offset, 1);
            var prefix = source[..offset];
            var line = prefix.Count(c => c == '\n');
            var character = offset - prefix.LastIndexOf('\n') - 1;
            var messages = Session(Open(uri, source), Feature(uri, "completion", line, character));
            Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
            return Result(messages);
        }
    }
}
