namespace Rukbat.Tests
{
    public class ProcessSleepTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("process.sleep()", "RUK2005")]
        [InlineData("process.sleep(1.0)", "RUK2001")]
        [InlineData("value := process.sleep(0)", "RUK2001")]
        public void ChecksSleepSignature(string source, string code)
        {
            var result = Compile("import process\n" + source);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task WaitsAndRejectsOutOfRangeDurations(string shell)
        {
            using var project = new Project();
            var output = Success("""
            import process
            import test

            fn monotonic_ms(): int {
                return powershell<int>('Invoke-Expression', '[long]([Diagnostics.Stopwatch]::GetTimestamp() * 1000.0 / [Diagnostics.Stopwatch]::Frequency)')
            }

            fn wait() {
                process.sleep(0)
                const start := monotonic_ms()
                process.sleep(120)
                test.assert(monotonic_ms() - start >= 90)
            }

            wait()
            try { process.sleep(-1); print('unexpected') } catch err { print('negative') }
            try { process.sleep(2147483648); print('unexpected') } catch err { print('too large') }
            """);
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(output)
                : await project.RunOutput(output);
            Assert.Equal("", result.Error);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("negative\ntoo large\n", result.Output);
        }
    }
}
