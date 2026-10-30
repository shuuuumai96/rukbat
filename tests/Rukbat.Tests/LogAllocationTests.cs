using System.Globalization;
using System.Text;

namespace Rukbat.Tests
{
    public class LogAllocationTests : TestBase
    {
        private const int MessageLength = 1024 * 1024;
        private const long MaximumFilteredAllocationBytes = 1024 * 1024;
        // Include the formatter's builder/result and strict UTF-8 bytes, plus dispatch.
        // A scalar native message or record argument adds several more full copies.
        private const long MaximumStderrAllocationBytes = 7 * 1024 * 1024;
        private const long MaximumFileAllocationBytes = 8 * 1024 * 1024;

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task GeneratedLoggingBoundsAllocationToTheCurrentRecord(bool debug)
        {
            using var project = new Project();
            var cases = new[]
            {
                new AllocationCase("filtered", "logger.stderr_level = null\nlogger.file_level = log.level_error", "log.debug(logger, message)", MaximumFilteredAllocationBytes),
                new AllocationCase("stderr", "logger.stderr_level = log.level_info\nlogger.file = null", "log.info(logger, message)", MaximumStderrAllocationBytes),
                new AllocationCase("file", $"logger.stderr_level = null\nlogger.file_level = log.level_info\nlogger.file = {Literal(project.PathFor("file.log"))}", "log.info(logger, message)", MaximumFileAllocationBytes),
                new AllocationCase("both", "logger.stderr_level = log.level_info", "log.info(logger, message)", MaximumFileAllocationBytes)
            };
            var source = new StringBuilder($$"""
            import log
            logger := log.create()
            logger.file = {{Literal(project.PathFor("filtered.log"))}}
            message := powershell<string>('Invoke-Expression', "'x' * {{MessageLength}}")
            installed := powershell<bool>('Invoke-Expression', '$script:logAllocationOriginalError = [Console]::Error; [Console]::SetError([IO.TextWriter]::Null); $true')
            """);
            foreach (var allocationCase in cases)
            {
                source.AppendLine($$"""

                {{allocationCase.Configuration}}
                if true {
                    attempt := 0
                    allocated := 0
                    while attempt < 3 {
                        before := powershell<int>('Invoke-Expression', '[GC]::GetAllocatedBytesForCurrentThread()')
                        {{allocationCase.Expression}}
                        after := powershell<int>('Invoke-Expression', '[GC]::GetAllocatedBytesForCurrentThread()')
                        allocated = after - before
                        attempt = attempt + 1
                    }
                    print('{{allocationCase.Name}}')
                    print(allocated)
                }
                """);
            }
            source.AppendLine("restored := powershell<bool>('Invoke-Expression', '[Console]::SetError($script:logAllocationOriginalError); $true')");
            project.Write("main.rukbat", source.ToString());
            var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug);
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

            var result = await project.RunOutput(compiled.Output!);

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Empty(result.Error);
            var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(cases.Length * 2, lines.Length);
            for (var index = 0; index < cases.Length; index++)
            {
                var allocationCase = cases[index];
                Assert.Equal(allocationCase.Name, lines[index * 2]);
                var allocated = long.Parse(lines[index * 2 + 1], CultureInfo.InvariantCulture);
                Assert.True(allocated >= 0 && allocated <= allocationCase.MaximumBytes,
                    $"{allocationCase.Name} allocated {allocated} bytes; budget is {allocationCase.MaximumBytes} bytes (debug: {debug}).");
            }
            Assert.False(File.Exists(project.PathFor("filtered.log")));
            var bytes = File.ReadAllBytes(project.PathFor("file.log"));
            Assert.Equal(6, bytes.Count(value => value == (byte)'\n'));
            Assert.InRange(bytes.Length, 6L * MessageLength, 6L * (MessageLength + 128));
        }

        private static string Literal(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        private sealed record AllocationCase(string Name, string Configuration, string Expression, long MaximumBytes);
    }
}
