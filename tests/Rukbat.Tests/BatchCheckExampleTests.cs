using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Rukbat.Tests;

public class BatchCheckExampleTests : TestBase
{
    private static string ExampleSource => File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "batch-check", "main.rukbat"));

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchCheckWritesAllResultsAndPreservesLiteralArguments(string shell)
    {
        using var project = new Project();
        project.Write("source/main.rukbat", ExampleSource);
        project.Write("work/input/good [one].txt", "good");
        project.Write("work/input/nested/bad 日本語.txt", "bad");
        project.Write("work/input/nested/ignored.md", "ignored");
        project.Write("config/checker.ps1", """
            param([string]$first, [string]$second, [string]$third, [string]$file)
            [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
            [Console]::Out.Write("out:$first|$second|$third|$file`nsecond line`n")
            [Console]::Error.Write("err:$file`nsecond line`n")
            if ($file -like '*bad*') { exit 7 }
            """);
        var config = new
        {
            directory = "input",
            pattern = "*.txt",
            executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            arguments = new[] { "-NoLogo", "-NoProfile", "-File", project.PathFor("config/checker.ps1"), "first value", "[brackets]", "日本語;$literal" }
        };
        project.Write("config/config.json", JsonSerializer.Serialize(config));
        var compiled = new Compiler().CompileFile(project.PathFor("source/main.rukbat"));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain("powershell<", ExampleSource, StringComparison.Ordinal);
        Directory.CreateDirectory(project.PathFor("deploy"));
        File.WriteAllText(project.PathFor("deploy/batch-check.ps1"), compiled.Output!, new UTF8Encoding(true));
        File.Delete(project.PathFor("source/main.rukbat"));

        var result = await RunScript(shell, project.PathFor("deploy/batch-check.ps1"), project.PathFor("work"), "../config/config.json", "report.json");

        Assert.True(result.ExitCode == 1, result.Error);
        Assert.Equal("", result.Output);
        Assert.Equal("", result.Error);
        var reportPath = project.PathFor("work/report.json");
        var reportBytes = File.ReadAllBytes(reportPath);
        Assert.True(reportBytes.Length > 0);
        Assert.False(reportBytes[0] == 0xef);
        Assert.False(reportBytes[^1] == (byte)'\n');
        var report = JsonDocument.Parse(reportBytes).RootElement.EnumerateArray().ToDictionary(item => item.GetProperty("file").GetString()!);
        Assert.Equal(2, report.Count);
        foreach (var (file, item) in report)
        {
            Assert.True(Path.IsPathFullyQualified(file));
            Assert.Contains("out:first value|[brackets]|日本語;$literal|" + file + "\nsecond line\n", item.GetProperty("stdout").GetString()!);
            Assert.Equal("err:" + file + "\nsecond line\n", item.GetProperty("stderr").GetString());
            Assert.Equal(file.Contains("bad", StringComparison.Ordinal) ? 7 : 0, item.GetProperty("exit_code").GetInt32());
        }
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task BatchCheckHelpAndOperationalFailuresDoNotReplaceAnExistingReport()
    {
        using var project = new Project();
        project.Write("main.rukbat", ExampleSource);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("batch-check.ps1");
        File.WriteAllText(script, compiled.Output!, new UTF8Encoding(true));

        var help = await RunScript("pwsh", script, project.DirectoryPath, "--help");
        Assert.Equal(0, help.ExitCode);
        Assert.Contains("Usage:", help.Output);
        Assert.Equal("", help.Error);

        project.Write("report.json", "preserve me");
        var malformed = await RunScript("pwsh", script, project.DirectoryPath, "missing.json", "report.json");
        Assert.Equal(2, malformed.ExitCode);
        Assert.NotEqual("", malformed.Error);
        Assert.Equal("preserve me", File.ReadAllText(project.PathFor("report.json")));
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task BatchCheckRejectsInvalidArgumentsAndConfigurationAndWritesAnEmptyReport()
    {
        using var project = new Project();
        project.Write("main.rukbat", ExampleSource);
        project.Write("empty/.keep", "");
        project.Write("malformed.json", "{");
        project.Write("invalid.json", "{\"directory\":\"\",\"pattern\":\"*\",\"executable\":\"checker.exe\",\"arguments\":[]}");
        project.Write("empty.json", "{\"directory\":\"empty\",\"pattern\":\"*.txt\",\"executable\":\"missing-checker.exe\",\"arguments\":[]}");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("batch-check.ps1");
        File.WriteAllText(script, compiled.Output!, new UTF8Encoding(true));

        foreach (var arguments in new[] { Array.Empty<string>(), new[] { "config.json" }, new[] { "config.json", "report.json", "extra" }, new[] { "", "report.json" } })
        {
            project.Write("report.json", "preserve me");
            var result = await RunScript("pwsh", script, project.DirectoryPath, arguments);
            Assert.Equal(2, result.ExitCode);
            Assert.NotEqual("", result.Error);
            Assert.Equal("preserve me", File.ReadAllText(project.PathFor("report.json")));
        }

        foreach (var config in new[] { "malformed.json", "invalid.json" })
        {
            project.Write("report.json", "preserve me");
            var result = await RunScript("pwsh", script, project.DirectoryPath, config, "report.json");
            Assert.Equal(2, result.ExitCode);
            Assert.NotEqual("", result.Error);
            Assert.Equal("preserve me", File.ReadAllText(project.PathFor("report.json")));
        }

        var empty = await RunScript("pwsh", script, project.DirectoryPath, "empty.json", "report.json");
        Assert.Equal(0, empty.ExitCode);
        Assert.Equal("", empty.Output);
        Assert.Equal("", empty.Error);
        Assert.Equal("[]", File.ReadAllText(project.PathFor("report.json")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchCheckStopsBeforeStartingTheCheckerWhenEnumerationFails(string shell)
    {
        using var project = new Project();
        var script = CompileExample(project);
        project.Write("root-file.txt", "not a directory");
        var checkerLog = project.PathFor("checker.log");
        var cases = new[]
        {
            ("missing root", "missing", "*.txt"),
            ("file root", "root-file.txt", "*.txt"),
            ("invalid pattern", ".", "nested/*.txt")
        };

        foreach (var (name, directory, pattern) in cases)
        {
            project.Write("report.json", "preserve " + name);
            WriteConfig(project, "config.json", directory, pattern, WindowsPowerShell(), CheckerArguments(project, checkerLog));

            var result = await RunScript(shell, script, project.DirectoryPath, "config.json", "report.json");

            Assert.True(result.ExitCode == 2, result.Error);
            Assert.Equal("", result.Output);
            Assert.NotEqual("", result.Error);
            Assert.False(File.Exists(checkerLog), name + " unexpectedly started the checker.");
            Assert.Equal("preserve " + name, File.ReadAllText(project.PathFor("report.json")));
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchCheckDoesNotWriteAReportWhenTheExecutableIsMissing(string shell)
    {
        using var project = new Project();
        var script = CompileExample(project);
        project.Write("input/first.txt", "first");
        project.Write("input/second.txt", "second");
        project.Write("report.json", "preserve me");
        WriteConfig(project, "config.json", "input", "*.txt", "missing-batch-check-executable", []);

        var result = await RunScript(shell, script, project.DirectoryPath, "config.json", "report.json");

        Assert.True(result.ExitCode == 2, result.Error);
        Assert.Equal("", result.Output);
        Assert.NotEqual("", result.Error);
        Assert.Equal("preserve me", File.ReadAllText(project.PathFor("report.json")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchCheckReportsAReportWriteFailureAfterCheckingFiles(string shell)
    {
        using var project = new Project();
        var script = CompileExample(project);
        project.Write("input/file.txt", "content");
        var checkerLog = project.PathFor("checker.log");
        WriteConfig(project, "config.json", "input", "*.txt", WindowsPowerShell(), CheckerArguments(project, checkerLog));

        var result = await RunScript(shell, script, project.DirectoryPath, "config.json", "missing-parent/report.json");

        Assert.True(result.ExitCode == 2, result.Error);
        Assert.Equal("", result.Output);
        Assert.NotEqual("", result.Error);
        Assert.Equal(new[] { Path.GetFullPath(project.PathFor("input/file.txt")) }, File.ReadAllLines(checkerLog));
        Assert.False(File.Exists(project.PathFor("missing-parent/report.json")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchCheckExcludesHiddenFilesAndHiddenSubtrees(string shell)
    {
        using var project = new Project();
        var script = CompileExample(project);
        project.Write("input/visible.txt", "visible");
        project.Write("input/hidden.txt", "hidden");
        project.Write("input/hidden-directory/inside.txt", "hidden");
        var hiddenFile = project.PathFor("input/hidden.txt");
        var hiddenDirectory = project.PathFor("input/hidden-directory");
        File.SetAttributes(hiddenFile, FileAttributes.Hidden);
        File.SetAttributes(hiddenDirectory, FileAttributes.Hidden | FileAttributes.Directory);
        try
        {
            var checkerLog = project.PathFor("checker.log");
            WriteConfig(project, "config.json", "input", "*.txt", WindowsPowerShell(), CheckerArguments(project, checkerLog));

            var result = await RunScript(shell, script, project.DirectoryPath, "config.json", "report.json");

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Output);
            Assert.Equal("", result.Error);
            Assert.Equal(new[] { Path.GetFullPath(project.PathFor("input/visible.txt")) }, File.ReadAllLines(checkerLog));
            var report = JsonDocument.Parse(File.ReadAllText(project.PathFor("report.json"))).RootElement.EnumerateArray().ToArray();
            Assert.Single(report);
            Assert.Equal(Path.GetFullPath(project.PathFor("input/visible.txt")), report[0].GetProperty("file").GetString());
        }
        finally
        {
            File.SetAttributes(hiddenFile, FileAttributes.Normal);
            File.SetAttributes(hiddenDirectory, FileAttributes.Directory);
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchCheckDoesNotTraverseDiscoveredReparsePoints(string shell)
    {
        using var project = new Project();
        var script = CompileExample(project);
        project.Write("input/visible.txt", "visible");
        project.Write("outside/escaped.txt", "escaped");
        var junction = project.PathFor("input/link");
        CreateJunction(junction, project.PathFor("outside"));
        try
        {
            var checkerLog = project.PathFor("checker.log");
            WriteConfig(project, "config.json", "input", "*.txt", WindowsPowerShell(), CheckerArguments(project, checkerLog));

            var result = await RunScript(shell, script, project.DirectoryPath, "config.json", "report.json");

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Output);
            Assert.Equal("", result.Error);
            Assert.Equal(new[] { Path.GetFullPath(project.PathFor("input/visible.txt")) }, File.ReadAllLines(checkerLog));
            Assert.DoesNotContain("escaped.txt", File.ReadAllText(project.PathFor("report.json")), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    private static string CompileExample(Project project)
    {
        project.Write("main.rukbat", ExampleSource);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("batch-check.ps1");
        File.WriteAllText(script, compiled.Output!, new UTF8Encoding(true));
        return script;
    }

    private static string[] CheckerArguments(Project project, string log)
    {
        project.Write("checker.ps1", """
            param([string]$log, [string]$file)
            [System.IO.File]::AppendAllText($log, $file + [Environment]::NewLine, (New-Object System.Text.UTF8Encoding($false)))
            """);
        return ["-NoLogo", "-NoProfile", "-File", project.PathFor("checker.ps1"), log];
    }

    private static void WriteConfig(Project project, string name, string directory, string pattern, string executable, string[] arguments)
        => project.Write(name, JsonSerializer.Serialize(new { directory, pattern, executable, arguments }));

    private static string WindowsPowerShell()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    private static void CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{path}\" \"{target}\""
        };
        using var command = Process.Start(start)!;
        var output = command.StandardOutput.ReadToEnd();
        var error = command.StandardError.ReadToEnd();
        command.WaitForExit();
        Assert.True(command.ExitCode == 0,
            $"Reparse-point fixture unavailable; could not create junction '{path}': {output}{error}");
    }

    private static async Task<RunResult> RunScript(string shell, string script, string working, params string[] arguments)
    {
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = working,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        info.ArgumentList.Add("-NoLogo");
        info.ArgumentList.Add("-NoProfile");
        info.ArgumentList.Add("-NonInteractive");
        info.ArgumentList.Add("-File");
        info.ArgumentList.Add(script);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var child = Process.Start(info)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { child.Kill(true); throw new TimeoutException("Batch-check generated script did not finish in 30 seconds."); }
        return new(child.ExitCode, (await stdout).Replace("\r\n", "\n"), (await stderr).Replace("\r\n", "\n"));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Rukbat.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException("Could not locate the repository root.");
    }
}
