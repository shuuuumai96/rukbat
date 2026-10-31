namespace Rukbat.Tests
{
    public sealed class WhileKeywordTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("fn while(): void {}")]
        [InlineData("fn identity(while: int): int { return while }")]
        [InlineData("struct while { value: int }")]
        [InlineData("struct Item { while: int }")]
        [InlineData("struct Item {}; impl Item { fn while(): void {} }")]
        [InlineData("const while := 1")]
        [InlineData("while := 1")]
        [InlineData("while: int = 1")]
        [InlineData("for while in [1] { print(while) }")]
        [InlineData("try { print(1) } catch while { print(while.message) }")]
        public void RejectsWhileAsADeclaredName(string source)
        {
            var result = Compile(source);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK1000");
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void RejectsWhileAsAnImportAlias()
        {
            using var project = new Project();
            project.Write("helper.rukbat", "fn value(): int { return 1 }");
            project.Write("main.rukbat", "import './helper.rukbat' as while\nprint(while.value())\n");
            var result = project.Compile();
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK1000");
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void PreservesWhileLoopsLongerNamesAndStringKeys()
        {
            Success("""
                struct WhileState {
                    while_count: int
                }

                impl WhileState {
                    fn while_increment(): void {
                        self.while_count = self.while_count + 1
                    }
                }

                fn while_value(while_count: int): int {
                    return while_count
                }

                const while_limit := 2
                state := WhileState {
                    while_count: 0
                }

                while state.while_count < while_limit {
                    state.while_increment()
                    print(while_value(state.while_count))
                }

                labels := {"while": 1}
                print(labels["while"])
                """);
        }
    }

    public sealed partial class LanguageServerTests
    {
        [Fact]
        public void WhileKeywordRenameRejectsTheReservedWord()
        {
            using var project = new Project();
            var uri = UriFor(project);
            const string source = "fn identity(value: int): int { return value }\nprint(identity(1))\n";
            var answer = Answer(EditorSession(project, Open(uri, source),
                Rename(uri, 0, source.IndexOf("value", StringComparison.Ordinal), "while")));

            Assert.Equal(-32602, answer.GetProperty("error").GetProperty("code").GetInt32());
            Assert.False(answer.TryGetProperty("result", out _));
        }
    }
}
