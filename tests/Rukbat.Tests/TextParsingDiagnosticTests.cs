namespace Rukbat.Tests
{
    public class TextParsingDiagnosticTests : TestBase
    {
        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task InvalidTextIncludesTheOriginalEscapedInput(string shell)
        {
            using var project = new Project();
            project.Write("main.rukbat", """
            import text

            try { text.parse_int("three") } catch err { print(err.message) }
            try { text.parse_float("NaN") } catch err { print(err.message) }
            try { text.parse_bool("True") } catch err { print(err.message) }
            try { text.parse_int(" \tthree\r\n") } catch err { print(err.message) }
            try { text.parse_int("bad\"\\path\n") } catch err { print(err.message) }
            try { text.parse_int("") } catch err { print(err.message) }
            try { text.parse_int("１２") } catch err { print(err.message) }
            try { text.parse_int(" 12") } catch err { print(err.message) }
            """);
            var result = await Run(project, shell);

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            Assert.Equal(
                Message("int", "three") + Message("float", "NaN") + Message("bool", "True") +
                Message("int", " \\tthree\\r\\n") + Message("int", "bad\\\"\\\\path\\n") +
                Message("int", "") + Message("int", "１２") + Message("int", "\\u00a012"), result.Output);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task LongDiagnosticInputsAreBoundedWithoutSplittingSurrogatePairs(string shell)
        {
            using var project = new Project();
            project.Write("main.rukbat", $$"""
            import text

            try { text.parse_int("{{new string('a', 80)}}") } catch err { print(err.message) }
            try { text.parse_int("{{new string('a', 81)}}") } catch err { print(err.message) }
            try { text.parse_int("{{new string('a', 79)}}😀z") } catch err { print(err.message) }
            try { text.parse_int("{{new string('a', 78)}}😀z") } catch err { print(err.message) }
            """);
            var result = await Run(project, shell);

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            Assert.Equal(Message("int", new string('a', 80)) +
                Message("int", new string('a', 80), truncated: true) +
                Message("int", new string('a', 79), truncated: true) +
                Message("int", new string('a', 78) + "\\ud83d\\ude00", truncated: true), result.Output);
        }

        private static string Message(string type, string preview, bool truncated = false) =>
            $"text.parse_{type}: invalid format or value outside the supported range. Input: \"{preview}\"" +
            (truncated ? " (truncated).\n" : ".\n");

        private static async Task<RunResult> Run(Project project, string shell)
        {
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            return shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);
        }
    }
}
