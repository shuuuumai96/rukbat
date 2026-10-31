using System.Text.Json;

namespace Rukbat.Tests
{
    public sealed class StringEqualityTests : TestBase
    {
        private static readonly (string Left, string Right, bool Equal)[] Cases =
        {
            ("ae", "æ", false),
            ("abc", "a\u00adbc", false),
            ("abc", "a\0bc", false),
            ("é", "e\u0301", false),
            ("Name", "name", false),
            ("", "", true),
            ("", "a", false),
            ("同じ😀", "同じ😀", true)
        };

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task OperatorsCompareStringsOrdinally(string shell)
        {
            var source = "import json\n";
            var expected = "";
            for (var index = 0; index < Cases.Length; index++)
            {
                var (left, right, equal) = Cases[index];
                source += PairDeclarations(index, left, right);
                source += $"print(left{index} == right{index})\nprint(left{index} != right{index})\n";
                expected += equal ? "True\nFalse\n" : "False\nTrue\n";
            }

            await AssertOutput(shell, source, expected);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task DirectAssertionsUseTheSameOrdinalComparisons(string shell)
        {
            var source = "import json\nimport test\n";
            for (var index = 0; index < Cases.Length; index++)
            {
                var (left, right, equal) = Cases[index];
                source += PairDeclarations(index, left, right);
                source += $"test.assert(left{index} {(equal ? "==" : "!=")} right{index})\n";
            }

            source += "print('passed')\n";
            await AssertOutput(shell, source, "passed\n");
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task ComparisonsEvaluateOperandsOnceInWrittenOrder(string shell)
        {
            const string source = """
                import test

                events := ""

                fn left(): string {
                    events = events + "L"
                    return "ae"
                }

                fn right(): string {
                    events = events + "R"
                    return "æ"
                }

                print(left() == right())
                print(events)
                events = ""
                print(left() != right())
                print(events)
                events = ""
                test.assert(left() != right())
                print(events)
                """;
            await AssertOutput(shell, source, "False\nLR\nTrue\nLR\nLR\n");
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task NullableStringNullComparisonsRetainTheirBehavior(string shell)
        {
            const string source = """
                import test

                value: string? = null
                test.assert(value == null)
                test.assert(null == value)
                print(value == null)
                print(value != null)
                value = "æ"
                test.assert(value != null)

                if value != null {
                    print(value == "ae")
                    test.assert(value != "ae")
                }

                print(null != value)
                """;
            await AssertOutput(shell, source, "True\nFalse\nFalse\nTrue\n");
        }

        private static string PairDeclarations(int index, string left, string right) =>
            $"left{index} := json.parse<string>({JsonLiteral(left)})\n" +
            $"right{index} := json.parse<string>({JsonLiteral(right)})\n";

        private static string JsonLiteral(string value) =>
            "'" + JsonSerializer.Serialize(value).Replace("\\", "\\\\").Replace("'", "\\'") + "'";

        private static async Task AssertOutput(string shell, string source, string expected)
        {
            var output = Success(source);
            using var project = new Project();
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(output)
                : await project.RunOutput(output);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Empty(result.Error);
            Assert.Equal(expected, result.Output);
        }
    }
}
