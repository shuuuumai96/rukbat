using System.ComponentModel;
using System.Globalization;
using System.Text;

namespace Rukbat
{
    internal static class TestRunner
    {
        private const int Success = 0;
        private const int TestFailure = 1;
        private const int UsageOrIoFailure = 2;
        private const int NoTests = 5;
        private const int DefaultTimeoutSeconds = 30;
        private const int MaximumTimeoutSeconds = 86400;
        private const int MillisecondsPerSecond = 1000;
        private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
        {
            ".git", "bin", "obj", ".local", "artifacts", "TestResults"
        };

        public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
        {
            string? temporaryRoot = null;
            var retainArtifacts = false;
            var status = Success;
            int RunTests()
            {
                var options = ParseOptions(args);
                var root = Path.GetFullPath(options.Path);
                var files = Discover(root).Order(StringComparer.Ordinal).ToArray();
                var compiler = new Compiler();
                var tests = new List<CollectedTest>();
                foreach (var file in files)
                {
                    var collection = compiler.CollectTests(file);
                    Cli.WriteDiagnostics(collection.Diagnostics, stderr);
                    if (collection.Diagnostics.Count != 0)
                    {
                        status = TestFailure;
                    }
                    tests.AddRange(collection.Tests.OrderBy(test => test.Name, StringComparer.Ordinal));
                }
                if (status != Success)
                {
                    return status;
                }

                var displayRoot = Directory.Exists(root) ? root : Path.GetDirectoryName(root)!;
                string Name(CollectedTest test) => Path.GetRelativePath(displayRoot, test.FilePath).Replace('\\', '/') + "::" + test.Name;
                var selected = tests.Where(test => options.Filter is null || Name(test).Contains(options.Filter, StringComparison.Ordinal)).ToArray();
                if (selected.Length == 0)
                {
                    stderr.WriteLine("No tests collected. Use test_*.rukbat files containing fn test_name(): void, or fn test_name(ctx: test.Context): void.");
                    return NoTests;
                }

                if (!OperatingSystem.IsWindows())
                {
                    throw new NotSupportedException("rukbat test executes contained PowerShell processes and requires Windows 10 / Server 2016 or later.");
                }
                var executable = FindShell(options.Shell);
                temporaryRoot = Path.Combine(Path.GetTempPath(), "rukbat-test-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                var prepared = new List<PreparedTest>();
                // Compile all selected entrypoints before any test or module initializer is executed.
                for (var index = 0; index < selected.Length; index++)
                {
                    var directory = Path.Combine(temporaryRoot, index.ToString(CultureInfo.InvariantCulture));
                    var working = Path.Combine(directory, "work");
                    Directory.CreateDirectory(working);
                    var completion = Path.Combine(directory, "completed");
                    var result = selected[index].Compile(working, completion);
                    Cli.WriteDiagnostics(result.Diagnostics, stderr);
                    if (!result.Success)
                    {
                        status = TestFailure;
                        continue;
                    }
                    var script = Path.Combine(directory, "test.ps1");
                    File.WriteAllText(script, result.Output, new UTF8Encoding(true));
                    prepared.Add(new(Name(selected[index]), directory, working, completion, script));
                }
                if (status != Success)
                {
                    return status;
                }

                var passed = 0;
                var failed = 0;
                foreach (var test in prepared)
                {
                    var failure = "";
                    try
                    {
                        var arguments = "-NoLogo -NoProfile -NonInteractive -File " + QuoteArgument(test.Script);
                        var result = RukbatProcessJob.Run(executable, arguments, test.WorkingDirectory, options.TimeoutSeconds * MillisecondsPerSecond);
                        var code = (int)result[0];
                        if (code != Success || !File.Exists(test.CompletionPath) || File.ReadAllText(test.CompletionPath) != "completed")
                        {
                            failure = code == Success
                                ? "PowerShell exited without completing the test (exit code 0)."
                                : $"PowerShell exit code: {code}.";
                            if ((string)result[1] != "")
                            {
                                failure += "\nstdout:\n" + (string)result[1];
                            }
                            if ((string)result[2] != "")
                            {
                                failure += "\nstderr:\n" + (string)result[2];
                            }
                            File.WriteAllText(Path.Combine(test.Directory, "stdout.txt"), (string)result[1], new UTF8Encoding(false));
                            File.WriteAllText(Path.Combine(test.Directory, "stderr.txt"), (string)result[2], new UTF8Encoding(false));
                        }
                    }
                    catch (TimeoutException)
                    {
                        failure = $"Test timed out after {options.TimeoutSeconds} seconds; its process job was terminated.";
                    }
                    catch (Exception error) when (error is IOException or DecoderFallbackException)
                    {
                        failure = error.Message;
                    }

                    if (failure == "")
                    {
                        passed++;
                        stdout.WriteLine("PASS " + test.Name);
                        RemoveOwnedDirectory(test.Directory, temporaryRoot);
                    }
                    else
                    {
                        failed++;
                        retainArtifacts = true;
                        stdout.WriteLine("FAIL " + test.Name);
                        stderr.WriteLine(test.Name + "\n" + failure.TrimEnd());
                        stderr.WriteLine("Artifacts: " + test.Directory);
                    }
                    stdout.Flush();
                    stderr.Flush();
                }
                stdout.WriteLine($"{passed} passed, {failed} failed");
                return failed == 0 ? Success : TestFailure;
            }
            try
            {
                status = RunTests();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or Win32Exception)
            {
                stderr.WriteLine("error: " + error.Message);
                status = UsageOrIoFailure;
            }
            finally
            {
                if (temporaryRoot is not null && !retainArtifacts)
                {
                    try
                    {
                        RemoveOwnedDirectory(temporaryRoot, temporaryRoot);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        stderr.WriteLine($"error: Cannot clean test artifacts '{temporaryRoot}': {error.Message}");
                        status = UsageOrIoFailure;
                    }
                }
            }
            return status;
        }

        private static Options ParseOptions(string[] args)
        {
            string? path = null;
            string? filter = null;
            var shell = "powershell";
            var timeout = DefaultTimeoutSeconds;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (var index = 0; index < args.Length; index++)
            {
                var argument = args[index];
                if (!argument.StartsWith("--", StringComparison.Ordinal))
                {
                    if (path is not null)
                    {
                        throw new ArgumentException("rukbat test accepts one file or directory path.");
                    }
                    path = argument;
                    continue;
                }
                if (argument is not ("--filter" or "--shell" or "--timeout") || !seen.Add(argument) || ++index == args.Length)
                {
                    throw new ArgumentException("Usage: rukbat test [path] [--filter text] [--timeout seconds] [--shell powershell|pwsh]");
                }
                var value = args[index];
                switch (argument)
                {
                    case "--filter":
                        if (string.IsNullOrEmpty(value))
                        {
                            throw new ArgumentException("--filter requires nonempty text.");
                        }
                        filter = value;
                        break;
                    case "--shell":
                        if (value is not ("powershell" or "pwsh"))
                        {
                            throw new ArgumentException("--shell must be powershell or pwsh.");
                        }
                        shell = value;
                        break;
                    case "--timeout":
                        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out timeout) || timeout < 1 || timeout > MaximumTimeoutSeconds)
                        {
                            throw new ArgumentException($"--timeout must be an integer from 1 to {MaximumTimeoutSeconds} seconds.");
                        }
                        break;
                }
            }
            return new(path ?? ".", filter, shell, timeout);
        }

        private static IEnumerable<string> Discover(string path)
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new ArgumentException("Test discovery does not traverse symbolic links or junctions: " + path);
            }
            if ((attributes & FileAttributes.Directory) == 0)
            {
                if (!Path.GetExtension(path).Equals(".rukbat", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException("Expected a .rukbat file or an existing directory: " + path);
                }
                yield return path;
                yield break;
            }
            var options = new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (var file in Directory.EnumerateFiles(path, "*", options))
            {
                if (Path.GetFileName(file).StartsWith("test_", StringComparison.Ordinal) &&
                    Path.GetExtension(file).Equals(".rukbat", StringComparison.OrdinalIgnoreCase))
                {
                    yield return file;
                }
            }
            foreach (var directory in Directory.EnumerateDirectories(path, "*", options))
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(directory)))
                {
                    foreach (var file in Discover(directory))
                    {
                        yield return file;
                    }
                }
            }
        }

        private static string FindShell(string shell)
        {
            foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(directory))
                {
                    continue;
                }
                var candidate = Path.GetFullPath(Path.Combine(directory.Trim('"'), shell + ".exe"));
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            throw new FileNotFoundException($"Cannot find '{shell}.exe' on PATH.");
        }

        // Windows command-line quoting: quotes and trailing backslashes must be escaped for argv.
        private static string QuoteArgument(string value)
        {
            var output = new StringBuilder("\"");
            var backslashes = 0;
            foreach (var character in value)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }
                output.Append('\\', character == '"' ? backslashes * 2 + 1 : backslashes);
                output.Append(character);
                backslashes = 0;
            }
            output.Append('\\', backslashes * 2);
            return output.Append('"').ToString();
        }

        private static void RemoveOwnedDirectory(string directory, string root)
        {
            var fullPath = Path.GetFullPath(directory);
            var ownedRoot = Path.GetFullPath(root);
            if (!fullPath.Equals(ownedRoot, StringComparison.OrdinalIgnoreCase) &&
                !fullPath.StartsWith(ownedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("Refusing to clean a path outside this test run: " + fullPath);
            }
            if (!Directory.Exists(fullPath))
            {
                return;
            }
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) == 0)
            {
                foreach (var entry in Directory.EnumerateFileSystemEntries(fullPath))
                {
                    var attributes = File.GetAttributes(entry);
                    if ((attributes & FileAttributes.Directory) != 0)
                    {
                        RemoveOwnedDirectory(entry, ownedRoot);
                    }
                    else
                    {
                        File.Delete(entry);
                    }
                }
            }
            Directory.Delete(fullPath);
        }

        private sealed record Options(string Path, string? Filter, string Shell, int TimeoutSeconds);
        private sealed record PreparedTest(string Name, string Directory, string WorkingDirectory, string CompletionPath, string Script);
    }
}
