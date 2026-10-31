namespace Rukbat.Tests
{
    public sealed class CallDelimiterTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("fn value(number: int): int { return number }; print(value { 7 })")]
        [InlineData("fn value(): int { return 7 }; print(value {})")]
        [InlineData("struct Item { number: int }; impl Item { fn read(): int { return self.number } }; item := Item { number: 7 }; print(item.read {})")]
        [InlineData("struct Item { number: int }; impl Item { fn set(number: int): void { self.number = number } }; item := Item { number: 7 }; item.set { 8 }")]
        [InlineData("items := [1]; items.append { 2 }")]
        [InlineData("items := {\"one\": 1}; items.keys {}")]
        [InlineData("items := {\"one\": 1}; items.has { \"one\" }")]
        [InlineData("items := {\"one\": 1}; items.remove { \"one\" }")]
        [InlineData("import text; print(text.length { \"answer\" })")]
        [InlineData("import json; print(json.stringify { 1 })")]
        [InlineData("import json; json.parse { \"1\" }")]
        [InlineData("import json; json.set { json.parse(\"{}\"), \"answer\", 1 }")]
        [InlineData("import array; array.slice { [1], 0, 1 }")]
        [InlineData("import array; array.sort { [1] }")]
        public void RejectsBracesForFunctionsAndMethods(string source)
        {
            AssertCallDelimiterDiagnostic(new Compiler().Check(source));
            var result = Compile(source);
            Assert.False(result.Success);
            Assert.Null(result.Output);
            AssertCallDelimiterDiagnostic(result.Diagnostics);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("remote.value { 7 }")]
        [InlineData("item.plus { 1 }")]
        public void RejectsBracesForImportedFunctionsAndMethods(string operation)
        {
            using var project = CreateImportedProject();
            project.Write("main.rukbat", "import \"./helper.rukbat\" as remote\nitem := remote.Item { number: 7 }\n" + operation);
            AssertCallDelimiterDiagnostic(new Compiler().CheckFile(project.PathFor("main.rukbat")));
            var result = project.Compile();
            Assert.False(result.Success);
            Assert.Null(result.Output);
            AssertCallDelimiterDiagnostic(result.Diagnostics);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ReportsTheCalledNameAsTheDelimiterDiagnosticLocation()
        {
            var diagnostics = new Compiler().Check("""
                fn value(number: int): int {
                    return number
                }

                result := value { 7 }
                """);
            var diagnostic = Assert.Single(diagnostics);
            Assert.Equal("RUK2005", diagnostic.Code);
            Assert.Equal(5, diagnostic.Line);
            Assert.Equal(11, diagnostic.Column);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("import json; json.Value {}")]
        [InlineData("import re; re.Pattern {}")]
        [InlineData("import csv; csv.Row {}")]
        [InlineData("import fs; fs.Lines {}")]
        [InlineData("import log; log.Level {}")]
        [InlineData("import encoding; encoding.Encoding {}")]
        [InlineData("import text; text.unknown {}")]
        [InlineData("import json; json.unknown {}")]
        [InlineData("import array; array.unknown {}")]
        public void PreservesUnknownStandardFunctionDiagnostics(string source)
        {
            var diagnostic = Assert.Single(new Compiler().Check(source));
            Assert.Equal("RUK2004", diagnostic.Code);
            var result = Compile(source);
            Assert.False(result.Success);
            Assert.Null(result.Output);
            Assert.Equal("RUK2004", Assert.Single(result.Diagnostics).Code);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("items := {\"one\": 1}; items.unknown {}")]
        [InlineData("items := [1]; items.unknown {}")]
        [InlineData("struct Item { number: int }; item := Item { number: 7 }; item.unknown {}")]
        public void PreservesUnknownMethodDiagnostics(string source)
        {
            var diagnostic = Assert.Single(new Compiler().Check(source));
            Assert.Equal("RUK2004", diagnostic.Code);
            var result = Compile(source);
            Assert.False(result.Success);
            Assert.Null(result.Output);
            Assert.Equal("RUK2004", Assert.Single(result.Diagnostics).Code);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("fn value(number: int): int { return number }; print(value(7))")]
        [InlineData("struct Item { number: int }; impl Item { fn read(): int { return self.number } }; item := Item { number: 7 }; print(item.read())")]
        [InlineData("items := [1]; items.append(2)")]
        [InlineData("items := {\"one\": 1}; items.keys(); items.has(\"one\"); items.remove(\"one\")")]
        [InlineData("import text; print(text.length(\"answer\"))")]
        [InlineData("import array; array.slice([1], 0, 1)")]
        [InlineData("import json; print(json.parse<int>(\"7\"))")]
        [InlineData("struct Item { number: int }; item := Item { number: 7 }")]
        [InlineData("error := Error { message: \"failed\" }; print(error.message)")]
        [InlineData("import process; result := process.Result { exit_code: 0, stdout: \"\", stderr: \"\" }")]
        [InlineData("import re; result := re.Match { value: \"answer\", start: 0, count: 6, groups: {} }")]
        public void PreservesParenthesizedCallsAndStructLiterals(string source)
        {
            Assert.Empty(new Compiler().Check(source));
            Success(source);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "PowerShell51")]
        [Trait("Category", "CrossVersion")]
        [Fact]
        public async Task CanonicalCallsAndStructLiteralsExecuteOnBothHosts()
        {
            using var project = CreateImportedProject();
            project.Write("main.rukbat", """
                import "./helper.rukbat" as remote
                import json
                import process
                import text

                print(remote.value(7))
                item := remote.Item {
                    number: 7
                }
                print(item.plus(1))
                items := [1]
                items.append(2)
                print(items.length)
                counts := {"one": 1}
                print(counts.has("one"))
                counts.remove("one")
                print(counts.length)
                print(text.length("abc"))
                print(json.parse<int>("4"))
                result := process.Result {
                    exit_code: 5,
                    stdout: "",
                    stderr: ""
                }
                print(result.exit_code)
                error := Error {
                    message: "failed"
                }
                print(error.message)
                """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

            foreach (var result in new[] { await project.RunOutputWithWindowsPowerShell(compiled.Output!),
                await project.RunOutput(compiled.Output!) })
            {
                Assert.True(result.ExitCode == 0, result.Error);
                Assert.Equal("7\n8\n2\nTrue\n0\n3\n4\n5\nfailed\n", result.Output);
                Assert.Equal("", result.Error);
            }
        }

        private static Project CreateImportedProject()
        {
            var project = new Project();
            project.Write("helper.rukbat", """
                struct Item {
                    number: int
                }

                impl Item {
                    fn plus(amount: int): int {
                        return self.number + amount
                    }
                }

                fn value(number: int): int {
                    return number
                }
                """);
            return project;
        }

        private static void AssertCallDelimiterDiagnostic(IReadOnlyList<Diagnostic> diagnostics)
        {
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "RUK2005" && diagnostic.Message.Contains("'(...)'", StringComparison.Ordinal));
        }
    }
}
