namespace Rukbat.Tests
{
    public class NumberContractTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("x := 9223372036854775808")]
        [InlineData("x := -9223372036854775809")]
        [InlineData("x := 1e-400")]
        [InlineData("x: float = 1")]
        [InlineData("x: float? = 1")]
        [InlineData("fn f(x: int?): float? { return x }")]
        [InlineData("print(1 + 1.0)")]
        [InlineData("print(9007199254740993 == 9007199254740992.0)")]
        [InlineData("print(1 < 1.0)")]
        [InlineData("x := [1, 2.0]")]
        [InlineData("x := [1.0, 2]")]
        [InlineData("x: float[] = [1]")]
        [InlineData("import math; math.to_float(1.0)")]
        [InlineData("import math; math.to_int(1)")]
        [InlineData("import math; math.trunc<int>(1.5)")]
        [InlineData("import math; math.ceil(value: 1.5)")]
        public void RejectsOutOfRangeLiteralsAndImplicitConversions(string source) => Assert.False(Compile(source).Success);

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task IntegersRemainExactAcrossArithmeticAndBoundaries(string shell)
        {
            using var project = new Project();
            project.Write("main.rukbat", """
            import math; import json; import text; import test
            maximum := 9223372036854775807
            minimum := -9223372036854775808
            large := 9007199254740993
            test.assert(maximum / 1 == maximum)
            test.assert(maximum / 3 == 3074457345618258602)
            test.assert(minimum / 3 == -3074457345618258602)
            test.assert(minimum % -1 == 0)
            test.assert(-7 / 2 == -3)
            test.assert(-7 % 2 == -1)
            test.assert(7 % -2 == 1)
            test.assert(large + 1 == 9007199254740994)
            test.assert(+large == large)
            test.assert(large * 2 == 18014398509481986)
            test.assert(text.parse_int(text.from_int(minimum)) == minimum)
            test.assert(text.parse_int(text.from_int(maximum)) == maximum)
            test.assert(json.parse<int>(json.stringify(large)) == large)
            test.assert(json.as<int>(json.parse('9223372036854775807')) == maximum)
            test.assert(powershell<int>('Write-Output', large) == large)
            test.assert(powershell<int>('Invoke-Expression', '[long]::MinValue') == minimum)
            struct Data { values: int[]; optional: int? }
            data := Data { values: [minimum, large, maximum], optional: large }
            copied := json.parse<Data>(json.stringify(data))
            test.assert(copied.values[0] == minimum)
            test.assert(copied.values[1] == large)
            test.assert(copied.values[2] == maximum)
            test.assert(copied.values[2] > copied.values[1])
            test.assert(math.to_float(large) == 9007199254740992.0)
            test.assert(math.to_float(9007199254740995) == 9007199254740996.0)
            test.assert(math.to_int(-9223372036854775808.0) == minimum)
            test.assert(math.to_int(3.0) == 3)
            test.assert(math.to_int(math.trunc(-3.9)) == -3)
            test.assert(math.to_int(math.floor(-3.9)) == -4)
            test.assert(math.to_int(math.ceil(-3.9)) == -3)
            test.assert(-0.0 == 0.0)
            test.assert(1e-300 * 1e-300 == 0.0)
            print('passed')
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Empty(result.Error);
            Assert.Equal("passed\n", result.Output);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task OverflowAndInvalidConversionsAreCatchable(string shell)
        {
            using var project = new Project();
            var expressions = new[] {
                "9223372036854775807 + 1", "-9223372036854775808 - 1", "9223372036854775807 * 2",
                "-(-9223372036854775808)", "-9223372036854775808 / -1", "1 / 0", "1 % 0",
                "math.to_int(3.9)", "math.to_int(9223372036854775808.0)", "math.to_int(-1e30)",
                "json.parse<int>('9223372036854775808')", "json.parse<int>('1.0')", "json.parse<float>('1e-400')",
                "text.parse_int('-9223372036854775809')", "text.parse_float('1e-400')",
                "powershell<int>('Invoke-Expression', '[ulong]::MaxValue')", "powershell<int>('Write-Output', 1.0)",
                "1e308 * 1e308", "1.0 / 0.0", "exit(2147483648)", "exit(-1)",
                "values[9223372036854775807]"
            };
            var source = "import math; import json; import text; import test\nvalues := [1]\n";
            for (var i = 0; i < expressions.Length; i++)
            {
                source += $"rejected{i} := false\ntry {{ {expressions[i]} }} catch err {{ rejected{i} = true }}\ntest.assert(rejected{i})\n";
            }

            source += "print('passed')\n";
            project.Write("main.rukbat", source);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Empty(result.Error);
            Assert.Equal("passed\n", result.Output);
        }
    }
}
