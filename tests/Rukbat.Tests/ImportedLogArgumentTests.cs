using System.Text;

namespace Rukbat.Tests
{
    public sealed class ImportedLogArgumentTests : TestBase
    {
        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell", false)]
        [InlineData("powershell", true)]
        [InlineData("pwsh", false)]
        [InlineData("pwsh", true)]
        public async Task ImportedMessagesReachBothDestinationsAndEvaluateOnce(string shell, bool debug)
        {
            using var project = new Project();
            project.Write("messages.rukbat", """
                import log

                calls := ""

                fn message(value: string): string {
                    calls = calls + value + ";"
                    return value
                }

                fn trace(): string {
                    return calls
                }

                fn level(): log.Level {
                    return log.level_debug
                }

                fn filename(value: string): string {
                    return value
                }
                """);
            var logPath = project.PathFor("imported.log").Replace("\\", "/");
            project.Write("main.rukbat", $$"""
                import log
                import test
                import "./messages.rukbat" as messages

                logger := log.create()
                log.debug(logger, messages.message("filtered"))
                logger.stderr_level = messages.level()
                logger.file = messages.filename("{{logPath}}")
                logger.file_level = messages.level()
                log.debug(logger, messages.message("debug"))
                log.info(logger, messages.message("info"))
                log.warn(logger, messages.message("warn"))
                log.error(logger, messages.message("error"))
                test.assert(messages.trace() == "filtered;debug;info;warn;error;")
                print("passed")
                """);
            var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug);
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("passed\n", result.Output);
            var file = File.ReadAllText(project.PathFor("imported.log"), Encoding.UTF8);
            foreach (var records in new[] { result.Error, file })
            {
                var lines = records.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.Equal(4, lines.Length);
                foreach (var level in new[] { "debug", "info", "warn", "error" })
                {
                    Assert.Single(lines, line => line.Contains("level=" + level.ToUpperInvariant() + " message=\"" + level + "\"", StringComparison.Ordinal));
                }
            }
        }
    }
}
