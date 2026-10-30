using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests
{
    public class CliRunTests : TestBase
    {
        [Trait("Category", "PowerShell51")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task CliRunCompilesImportsAndHandlesPathsWithSpaces()
        {
            using var project = new Project();
            project.Write("math.rukbat", "fn add(a: int, b: int): int { return a + b }");
            project.Write("run input.rukbat", "import './math.rukbat' as arithmetic\nprint(arithmetic.add(2, 3)); print(powershell<string>('Get-Content', 'data.txt', Raw: true))");
            project.Write("data.txt", "relative path");
            var result = await RunCliProcess(project, ["run", project.PathFor("run input.rukbat")]);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("5\nrelative path", result.Output.TrimEnd('\n'));
            Assert.Equal("", result.Error);
            Assert.True(File.Exists(project.PathFor("run input.ps1")));
        }

        [Trait("Category", "PowerShell51")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task CliRunKeepsLiteralWorkingDirectoryAndArguments()
        {
            using var project = new Project();
            const string workDirectory = "work 日本語 [brackets] ' ; $ &";
            const string contents = "literal argument 日本語 [brackets] ' ; $ &";
            project.Write(workDirectory + "/input.txt", "relative input");
            project.Write("source/main.rukbat", """
            import fs
            import encoding
            import path
            arguments := args()
            print(fs.read_text(arguments[0], encoding.utf8))
            fs.write_text(arguments[1], arguments[2], encoding.utf8)
            print(path.script_dir())
            """);
            var working = project.PathFor(workDirectory);
            var result = await RunCliProcess(project,
                ["run", project.PathFor("source/main.rukbat"), "--", "input.txt", "output.txt", contents],
                workingDirectory: working);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"relative input\n{project.PathFor("source")}\n", result.Output);
            Assert.Empty(result.Error);
            Assert.Equal(contents, File.ReadAllText(Path.Combine(working, "output.txt")));
            Assert.False(File.Exists(project.PathFor("source/output.txt")));
        }

        [Trait("Category", "PowerShell51")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task CliRunNeverExecutesStaleOutputAfterCompilationFailure()
        {
            using var project = new Project();
            project.Write("main.rukbat", "x: int = 'bad'");
            project.Write("main.ps1", "[Console]::WriteLine('STALE OUTPUT EXECUTED')");
            var result = await RunCliProcess(project, ["run", project.PathFor("main.rukbat")]);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.Contains("RUK2001", result.Error);
            Assert.Equal("[Console]::WriteLine('STALE OUTPUT EXECUTED')", File.ReadAllText(project.PathFor("main.ps1")));
        }

        [Trait("Category", "PowerShell51")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task CliRunPropagatesRuntimeFailureAndStandardError()
        {
            using var project = new Project();
            project.Write("main.rukbat", "print('before'); powershell<void>('Write-Error', 'expected failure')");
            var result = await RunCliProcess(project, ["run", project.PathFor("main.rukbat")]);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("before", result.Output.TrimEnd('\n'));
            Assert.Contains("expected failure", result.Error);
        }

        [Trait("Category", "PowerShell51")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task CliRunPropagatesTheExactPowerShellExitCode()
        {
            using var project = new Project();
            project.Write("native.psm1", "function Stop-Rukbat { [Environment]::Exit(7) }; Export-ModuleMember -Function Stop-Rukbat");
            var path = project.PathFor("native.psm1").Replace("\\", "\\\\").Replace("'", "\\'");
            project.Write("main.rukbat", $"powershell<void>('Import-Module', '{path}', Global: true); powershell<void>('Stop-Rukbat')");
            var result = await RunCliProcess(project, ["run", project.PathFor("main.rukbat")]);
            Assert.Equal(7, result.ExitCode);
        }

        [Trait("Category", "PowerShell51")]
        [Trait("Category", "Runtime")]
        [Fact]
        public async Task CliRunReportsMissingPowerShell()
        {
            using var project = new Project();
            project.Write("main.rukbat", "print('hello')");
            var result = await RunCliProcess(project, ["run", project.PathFor("main.rukbat")], hidePowerShell: true);
            Assert.Equal(2, result.ExitCode);
            Assert.Contains("Could not start Windows PowerShell", result.Error);
            Assert.Equal("", result.Output);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("run")]
        [InlineData("unknown", "main.rukbat")]
        [InlineData("run", "main.ps1")]
        [InlineData("run", "main.rukbat", "unexpected")]
        [InlineData("build", "main.rukbat", "--")]
        [InlineData("build", "main.rukbat", "--", "argument")]
        public void CliRunRejectsInvalidArguments(params string[] args)
        {
            using var error = new StringWriter();
            Assert.Equal(2, Cli.Run(args, TextWriter.Null, error));
            Assert.Contains("Usage: rukbat <build|run|check|fmt>", error.ToString());
        }

        private static async Task<RunResult> RunCliProcess(Project project, string[] args, bool hidePowerShell = false,
            string? workingDirectory = null)
        {
            var host = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
            if (string.IsNullOrEmpty(host))
            {
                var executable = OperatingSystem.IsWindows() ? "dotnet.exe" : "dotnet";
                host = (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator)
                    .Select(directory => Path.Combine(directory, executable)).First(File.Exists);
            }
            var info = new ProcessStartInfo(host)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, WorkingDirectory = workingDirectory ?? project.DirectoryPath,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            info.ArgumentList.Add(typeof(Cli).Assembly.Location);
            foreach (var argument in args)
            {
                info.ArgumentList.Add(argument);
            }

            if (hidePowerShell)
            {
                info.Environment["PATH"] = project.DirectoryPath;
            }

            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("CLI did not finish in 30 seconds."); }
            return new(process.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
        }
    }
}
