using System.Diagnostics;
using System.Text;
using Rukbat;
using PowerShellParser = System.Management.Automation.Language.Parser;

namespace Rukbat.Tests
{
    public abstract class TestBase
    {
        protected static CompilationResult Compile(string source) => new Compiler().Compile(source);

        protected static string Success(string source)
        {
            var result = Compile(source);
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            PowerShellParser.ParseInput(result.Output!, out _, out var errors);
            Assert.Empty(errors);
            return result.Output!;
        }

        protected static async Task<RunResult> RunCli(Project project, string[] args, bool hidePowerShell = false)
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
                CreateNoWindow = true, WorkingDirectory = project.DirectoryPath,
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

        protected static async Task<RunResult> RunDeployed(string shell, string script, string working, string nativeWorking)
        {
            var info = new ProcessStartInfo(shell)
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true, WorkingDirectory = nativeWorking,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
            };
            foreach (var value in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-Command",
                "Set-Location -LiteralPath '" + working.Replace("'", "''") + "'; & '" + script.Replace("'", "''") + "'" })
            {
                info.ArgumentList.Add(value);
            }

            using var child = Process.Start(info)!;
            var stdout = child.StandardOutput.ReadToEndAsync();
            var stderr = child.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            try { await child.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { child.Kill(true); throw new TimeoutException("Deployment test timed out."); }
            return new(child.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
        }

        protected sealed class Project : IDisposable
        {
            public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "rukbat-tests-" + Guid.NewGuid());
            public Project() => Directory.CreateDirectory(DirectoryPath);
            public string PathFor(string file) => Path.Combine(DirectoryPath, file);
            public void Write(string file, string source)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PathFor(file))!);
                File.WriteAllText(PathFor(file), source);
            }
            public CompilationResult Compile() => new Compiler().CompileFile(PathFor("main.rukbat"));
            public Task<RunResult> Run()
            {
                var result = Compile(); Assert.True(result.Success, string.Join("\n", result.Diagnostics));
                return RunOutput(result.Output!);
            }
            public async Task<RunResult> RunOutput(string output, params string[] arguments)
            {
                Write("main.ps1", output);
                var info = new ProcessStartInfo("pwsh")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Path.GetTempPath(), CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", PathFor("main.ps1") })
                {
                    info.ArgumentList.Add(argument);
                }

                foreach (var argument in arguments)
                {
                    info.ArgumentList.Add(argument);
                }

                using var process = Process.Start(info)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Generated PowerShell did not finish in 20 seconds."); }
                return new(process.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
            }
            public async Task<RunResult> RunOutputWithWindowsPowerShell(string output, params string[] arguments)
            {
                // Match the CLI's deployment encoding; PowerShell 5.1 otherwise reads UTF-8 as ANSI.
                File.WriteAllText(PathFor("main.ps1"), output, new UTF8Encoding(true));
                var info = new ProcessStartInfo("powershell")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Path.GetTempPath(), CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
                };
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", PathFor("main.ps1") })
                {
                    info.ArgumentList.Add(argument);
                }

                foreach (var argument in arguments)
                {
                    info.ArgumentList.Add(argument);
                }

                using var process = Process.Start(info)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Generated PowerShell did not finish in 20 seconds."); }
                return new(process.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
            }
            public async Task<RunResult> RunOutputWithEnvironment(string output, string executable, string variable, string value)
            {
                Write("main.ps1", output);
                var info = new ProcessStartInfo(executable)
                {
                    UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                    WorkingDirectory = Path.GetTempPath(), CreateNoWindow = true
                };
                info.Environment[variable] = value;
                foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", PathFor("main.ps1") })
                {
                    info.ArgumentList.Add(argument);
                }

                using var process = Process.Start(info)!;
                var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Generated PowerShell did not finish in 20 seconds."); }
                return new(process.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
            }
            public void Dispose() => Directory.Delete(DirectoryPath, true);
        }

        protected sealed record RunResult(int ExitCode, string Output, string Error);
    }
}
