namespace Rukbat.Tests
{
    public sealed class DictionaryKeyTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("-9223372036854775808", "-(-9223372036854775808)")]
        [InlineData("-(-9223372036854775808)", "-9223372036854775808")]
        [InlineData("-(-9223372036854775808)", "-(-9223372036854775808)")]
        [InlineData("--9223372036854775808", "-9223372036854775808")]
        public void OverflowingNegationRemainsARuntimeExpression(string first, string second)
        {
            Success($"values := {{{first}: 1, {second}: 2}}");
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("'same'", "\"same\"")]
        [InlineData("1", "+1")]
        [InlineData("-1", "-(1)")]
        [InlineData("0", "-0")]
        [InlineData("-0", "+0")]
        [InlineData("9223372036854775807", "+9223372036854775807")]
        [InlineData("-9223372036854775808", "+(-9223372036854775808)")]
        public void EquivalentLiteralKeysStillProduceDuplicateDiagnostics(string first, string second)
        {
            var result = Compile($"values := {{{first}: 1, {second}: 2}}");
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK2002" &&
                diagnostic.Message.StartsWith("Duplicate dictionary key", StringComparison.Ordinal));
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task OverflowingKeysRaiseTheOrdinaryCatchableIntegerError(string shell)
        {
            const string minimum = "-9223372036854775808";
            var dictionaries = new[]
            {
                $"{{{minimum}: 1, -({minimum}): 2}}",
                $"{{-({minimum}): 1, {minimum}: 2}}",
                $"{{-({minimum}): 1, -({minimum}): 2}}"
            };
            var source = $$"""
                expected_error := ""
                try {
                    print(-({{minimum}}))
                } catch err {
                    expected_error = err.message
                }
                caught_count := 0
                """ + "\n";
            foreach (var dictionary in dictionaries)
            {
                source += $$"""
                    try {
                        values := {{dictionary}}
                    } catch err {
                        print(err.message == expected_error)
                        caught_count = caught_count + 1
                    }
                    """ + "\n";
            }

            source += "print(caught_count)\n";
            var output = Success(source);
            using var project = new Project();
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(output)
                : await project.RunOutput(output);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Empty(result.Error);
            Assert.Equal("True\nTrue\nTrue\n3\n", result.Output);
        }
    }
}
