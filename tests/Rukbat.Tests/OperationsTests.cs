using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rukbat.Tests;

public class OperationsTests : TestBase
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Rukbat.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static Process Start(string shell, string script, params string[] arguments)
    {
        var info = new ProcessStartInfo(shell) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script }.Concat(arguments)) info.ArgumentList.Add(arg);
        return Process.Start(info)!;
    }

    private static async Task<RunResult> Run(string shell, string script, params string[] arguments)
    {
        using var process = Start(shell, script, arguments);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(deadline.Token); }
        catch { process.Kill(true); throw; }
        return new(process.ExitCode, await output, await error);
    }

    private static async Task<JsonObject> Prepare(Project project, string shell, string source, string[]? args = null)
    {
        project.Write("main.rukbat", source);
        Directory.CreateDirectory(project.PathFor("work"));
        Directory.CreateDirectory(project.PathFor("state"));
        var result = await Run(shell, Path.Combine(Root(), "tools/operations/New-Release.ps1"), "-Source", project.PathFor("main.rukbat"), "-CompilerDll", typeof(Cli).Assembly.Location, "-Destination", project.PathFor("release"), "-ReleaseId", "acceptance-v1");
        Assert.True(result.ExitCode == 0, result.Error + result.Output);
        var host = shell == "powershell" ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell/v1.0/powershell.exe") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PowerShell/7/pwsh.exe");
        var config = JsonSerializer.SerializeToNode(new
        {
            schema_version = 1, job_id = "acceptance", release_directory = project.PathFor("release"), working_directory = project.PathFor("work"), state_directory = project.PathFor("state"), powershell = host,
            arguments = args ?? [], timeout_seconds = 15, max_attempts = 1, retry_exit_codes = Array.Empty<int>(), retry_delay_seconds = 0,
            first_due_utc = "2020-01-01T00:00:00Z", max_success_age_seconds = 3600
        })!.AsObject();
        SaveConfig(project, config);
        return config;
    }

    private static void SaveConfig(Project project, JsonObject config) => project.Write("job.json", config.ToJsonString());
    private static JsonObject State(Project project) => JsonNode.Parse(File.ReadAllText(project.PathFor("state/acceptance.json")))!.AsObject();
    private static Task<RunResult> Invoke(Project project, string shell, params string[] more) => Run(shell, project.PathFor("release/Invoke-Job.ps1"), new[] { "-ConfigPath", project.PathFor("job.json") }.Concat(more).ToArray());
    private static Task<RunResult> Monitor(Project project, string shell, params string[] more) => Run(shell, project.PathFor("release/Test-Job.ps1"), new[] { "-ConfigPath", project.PathFor("job.json") }.Concat(more).ToArray());

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task ReleaseRunsWithoutSourcesPreservesArgumentsAndRecordsSuccess(string shell)
    {
        using var project = new Project();
        await Prepare(project, shell, "import encoding; import fs; import json; fs.write_text('result.json', json.stringify(args()), encoding.utf8)", ["", "日本語 ' ; $value", "-switch", "a\\b\\", "line\nnext"]);
        File.Delete(project.PathFor("main.rukbat"));
        var missing = await Monitor(project, shell);
        Assert.True(missing.ExitCode == 1, missing.Output + missing.Error);
        Assert.Equal("missing", JsonNode.Parse(missing.Output)!["state"]!.GetValue<string>());
        using (var starting = File.Open(project.PathFor("state/acceptance.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None))
        {
            var probe = await Monitor(project, shell);
            Assert.Equal("missing", JsonNode.Parse(probe.Output)!["state"]!.GetValue<string>());
        }
        var result = await Invoke(project, shell);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(new[] { "", "日本語 ' ; $value", "-switch", "a\\b\\", "line\nnext" }, JsonSerializer.Deserialize<string[]>(File.ReadAllText(project.PathFor("work/result.json"))));
        var state = State(project);
        Assert.Equal("succeeded", state["status"]!.GetValue<string>());
        Assert.Equal("acceptance-v1", state["release_id"]!.GetValue<string>());
        Assert.True(File.Exists(project.PathFor("state/" + state["run_id"] + ".1.stdout.log")));
        var monitor = await Monitor(project, shell);
        Assert.Equal(0, monitor.ExitCode);
        Assert.Equal("healthy", JsonNode.Parse(monitor.Output)!["state"]!.GetValue<string>());
        state["last_success_utc"] = "2020-01-01T00:00:00Z";
        project.Write("state/acceptance.json", state.ToJsonString());
        Assert.Equal("stale", JsonNode.Parse((await Monitor(project, shell)).Output)!["state"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task FailedJobsRequireExplicitReconciliationAndNotificationsAreBounded(string shell)
    {
        using var project = new Project();
        var config = await Prepare(project, shell, "import fs; if !fs.is_file('ready') { eprint('not ready'); exit(7) }; print('ready')");
        config["max_attempts"] = 2;
        config["retry_exit_codes"] = new JsonArray(8);
        SaveConfig(project, config);
        Assert.Equal(1, (await Invoke(project, shell)).ExitCode);
        var first = State(project);
        Assert.Equal(7, first["child_exit_code"]!.GetValue<int>());
        Assert.Equal(1, first["attempts"]!.GetValue<int>());
        Assert.Equal(2, (await Invoke(project, shell)).ExitCode);
        Assert.Equal(first["run_id"]!.GetValue<string>(), State(project)["run_id"]!.GetValue<string>());
        project.Write("notify.ps1", "param([string]$path)\n[IO.File]::Copy($path, '" + project.PathFor("notification.json").Replace("'", "''") + "', $true)");
        Assert.Equal(1, (await Monitor(project, shell, "-NotifyScript", project.PathFor("notify.ps1"))).ExitCode);
        Assert.Equal("failed", JsonNode.Parse(File.ReadAllText(project.PathFor("notification.json")))!["state"]!.GetValue<string>());
        project.Write("work/ready", "ready");
        Assert.Equal(0, (await Invoke(project, shell, "-RetryRunId", first["run_id"]!.GetValue<string>(), "-RecoveryNote", "Verified no side effects; supplied prerequisite.")).ExitCode);
        Assert.Equal("succeeded", State(project)["status"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task OnlyConfiguredExitCodesAreRetriedWithinOneDeadline(string shell)
    {
        using var project = new Project();
        var config = await Prepare(project, shell, "import encoding; import fs; if !fs.is_file('attempt') { fs.write_text('attempt', 'first', encoding.utf8); exit(7) }; print('done')");
        config["max_attempts"] = 2;
        config["retry_exit_codes"] = new JsonArray(7);
        SaveConfig(project, config);
        var result = await Invoke(project, shell);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(2, State(project)["attempts"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task WholeJobDeadlineStopsAnInfiniteRukbatLoop(string shell)
    {
        using var project = new Project();
        var config = await Prepare(project, shell, "import encoding; import fs; fs.write_text('loop-started', 'yes', encoding.utf8); while true { }");
        config["timeout_seconds"] = 4;
        SaveConfig(project, config);
        Assert.Equal(1, (await Invoke(project, shell)).ExitCode);
        Assert.Equal("timed_out", State(project)["status"]!.GetValue<string>());
        Assert.True(File.Exists(project.PathFor("work/loop-started")));
        Assert.Equal(2, (await Invoke(project, shell)).ExitCode);
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task ConcurrentLaunchIsRejectedAndKilledSupervisorLeavesUnknownResult(string shell)
    {
        using var project = new Project();
        var config = await Prepare(project, shell, "import encoding; import fs; pid := powershell<int>('Invoke-Expression', '$PID'); import text; fs.write_text('pid.txt', text.from_int(pid), encoding.utf8); while true { }");
        config["timeout_seconds"] = 30;
        SaveConfig(project, config);
        using var runner = Start(shell, project.PathFor("release/Invoke-Job.ps1"), "-ConfigPath", project.PathFor("job.json"));
        var output = runner.StandardOutput.ReadToEndAsync();
        var error = runner.StandardError.ReadToEndAsync();
        try
        {
            var watch = Stopwatch.StartNew();
            while (!File.Exists(project.PathFor("work/pid.txt")) && watch.Elapsed < TimeSpan.FromSeconds(12)) await Task.Delay(30);
            Assert.True(File.Exists(project.PathFor("work/pid.txt")), "Child never reached marker.");
            using var child = Process.GetProcessById(int.Parse(File.ReadAllText(project.PathFor("work/pid.txt"))));
            var running = await Monitor(project, shell);
            Assert.Equal("running", JsonNode.Parse(running.Output)!["state"]!.GetValue<string>());
            Assert.Equal(2, (await Invoke(project, shell)).ExitCode);
            runner.Kill();
            await runner.WaitForExitAsync();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await child.WaitForExitAsync(deadline.Token);
            var monitor = await Monitor(project, shell);
            Assert.Equal("unknown", JsonNode.Parse(monitor.Output)!["state"]!.GetValue<string>());
            Assert.Equal(2, (await Invoke(project, shell)).ExitCode);
        }
        finally { if (!runner.HasExited) { runner.Kill(true); await runner.WaitForExitAsync(); } }
        await output; await error;
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task BuildFailureAndArtifactTamperingNeverExecuteStaleCode(string shell)
    {
        using var project = new Project();
        await Prepare(project, shell, "import encoding; import fs; fs.write_text('changed', 'yes', encoding.utf8)");
        File.AppendAllText(project.PathFor("release/job.ps1"), "# altered");
        Assert.Equal(2, (await Invoke(project, shell)).ExitCode);
        Assert.False(File.Exists(project.PathFor("work/changed")));
        project.Write("main.rukbat", "broken :=");
        var result = await Run(shell, Path.Combine(Root(), "tools/operations/New-Release.ps1"), "-Source", project.PathFor("main.rukbat"), "-CompilerDll", typeof(Cli).Assembly.Location, "-Destination", project.PathFor("bad-release"), "-ReleaseId", "bad");
        Assert.Equal(2, result.ExitCode);
        Assert.False(Directory.Exists(project.PathFor("bad-release")));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task RepresentativeFileCliAndWindowsJobsRunThroughTheSameOperationsPath(string shell)
    {
        using var project = new Project();
        project.Write("work/input.txt", "original");
        project.Write("work/plan.csv", "source,destination\ninput.txt,archive/output.txt\n");
        await Prepare(project, shell, File.ReadAllText(Path.Combine(Root(), "examples/batch-organize/main.rukbat")), ["plan.csv"]);
        Assert.Equal(0, (await Invoke(project, shell)).ExitCode);
        Assert.Equal(0, (await Invoke(project, shell)).ExitCode);
        Assert.Equal("original", File.ReadAllText(project.PathFor("work/archive/output.txt")));

        using var audit = new Project();
        await Prepare(audit, shell, File.ReadAllText(Path.Combine(Root(), "examples/windows-audit/main.rukbat")), ["Schedule", "services.json"]);
        var result = await Invoke(audit, shell);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.True(JsonNode.Parse(File.ReadAllText(audit.PathFor("work/services.json")))!["running"]!.GetValue<bool>());

        using var cli = new Project();
        cli.Write("work/input/a.txt", "checked");
        cli.Write("work/checker.ps1", "param([string]$file)\n[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)\n[Console]::Write([IO.File]::ReadAllText($file))");
        var config = await Prepare(cli, shell, File.ReadAllText(Path.Combine(Root(), "examples/batch-check/main.rukbat")), ["checker.json", "report.json"]);
        cli.Write("work/checker.json", JsonSerializer.Serialize(new { directory = "input", pattern = "*.txt", executable = config["powershell"]!.GetValue<string>(), arguments = new[] { "-NoProfile", "-File", cli.PathFor("work/checker.ps1") } }));
        result = await Invoke(cli, shell);
        Assert.True(result.ExitCode == 0, result.Error);
        var report = JsonNode.Parse(File.ReadAllText(cli.PathFor("work/report.json")))!.AsArray();
        Assert.Single(report);
        Assert.Equal("checked", report[0]!["stdout"]!.GetValue<string>());
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task SchedulerDefinitionUsesTheVerifiedReleaseAndExplicitExecutionLimits(string shell)
    {
        using var project = new Project();
        await Prepare(project, shell, "print('scheduled')");
        var result = await Run(shell, project.PathFor("release/Manage-Task.ps1"), "-Action", "Export", "-ConfigPath", project.PathFor("job.json"), "-XmlPath", project.PathFor("task.xml"));
        Assert.True(result.ExitCode == 0, result.Error);
        var xml = System.Xml.Linq.XDocument.Load(project.PathFor("task.xml"));
        var ns = xml.Root!.Name.Namespace;
        Assert.Equal("IgnoreNew", xml.Descendants(ns + "MultipleInstancesPolicy").Single().Value);
        Assert.Equal("S4U", xml.Descendants(ns + "LogonType").Single().Value);
        Assert.Equal(TimeSpan.FromSeconds(75), System.Xml.XmlConvert.ToTimeSpan(xml.Descendants(ns + "ExecutionTimeLimit").Single().Value));
        Assert.Equal(project.PathFor("work"), xml.Descendants(ns + "WorkingDirectory").Single().Value);
        Assert.Empty(xml.Descendants(ns + "RestartOnFailure"));
        var encoded = xml.Descendants(ns + "Arguments").Single().Value.Split(' ').Last();
        var command = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
        Assert.Contains(Path.GetFullPath(project.PathFor("release/Invoke-Job.ps1")), command);
        Assert.Contains(project.PathFor("job.json"), command);
        Assert.DoesNotContain("Bypass", command);
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task ReleaseRollbackKeepsHistoryAndBackupRestorationIsSeparate(string shell)
    {
        using var project = new Project();
        project.Write("work/data.txt", "backup original");
        File.Copy(project.PathFor("work/data.txt"), project.PathFor("backup.txt"));
        var config = await Prepare(project, shell, "import encoding; import fs; fs.write_text('version.txt', 'v1', encoding.utf8)");
        Assert.Equal(0, (await Invoke(project, shell)).ExitCode);
        var firstId = State(project)["run_id"]!.GetValue<string>();
        project.Write("main.rukbat", "import encoding; import fs; fs.write_text('version.txt', 'v2', encoding.utf8); fs.write_text('data.txt', 'changed', encoding.utf8)");
        var build = await Run(shell, Path.Combine(Root(), "tools/operations/New-Release.ps1"), "-Source", project.PathFor("main.rukbat"), "-CompilerDll", typeof(Cli).Assembly.Location, "-Destination", project.PathFor("release2"), "-ReleaseId", "acceptance-v2");
        Assert.True(build.ExitCode == 0, build.Error);
        config["release_directory"] = project.PathFor("release2");
        SaveConfig(project, config);
        var second = await Run(shell, project.PathFor("release2/Invoke-Job.ps1"), "-ConfigPath", project.PathFor("job.json"));
        Assert.True(second.ExitCode == 0, second.Error);
        Assert.Equal("v2", File.ReadAllText(project.PathFor("work/version.txt")));
        config["release_directory"] = project.PathFor("release");
        SaveConfig(project, config);
        Assert.Equal(0, (await Invoke(project, shell)).ExitCode);
        Assert.Equal("v1", File.ReadAllText(project.PathFor("work/version.txt")));
        Assert.Equal("changed", File.ReadAllText(project.PathFor("work/data.txt")));
        File.Copy(project.PathFor("backup.txt"), project.PathFor("restored.txt"));
        Assert.Equal(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(project.PathFor("backup.txt"))), System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(project.PathFor("restored.txt"))));
        File.Copy(project.PathFor("restored.txt"), project.PathFor("work/data.txt"), overwrite: true);
        Assert.Equal("backup original", File.ReadAllText(project.PathFor("work/data.txt")));
        Assert.True(File.Exists(project.PathFor("state/" + firstId + ".json")));
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    public async Task PreflightAndMonitoringFailuresDoNotPretendTheJobSucceeded(string shell)
    {
        using var project = new Project();
        var config = await Prepare(project, shell, "import encoding; import fs; fs.write_text('changed', 'yes', encoding.utf8)");
        config["first_due_utc"] = "2099-01-01T00:00:00Z";
        SaveConfig(project, config);
        Assert.Equal("not_due", JsonNode.Parse((await Monitor(project, shell)).Output)!["state"]!.GetValue<string>());
        project.Write("state/acceptance.json", "broken json");
        Assert.Equal(2, (await Invoke(project, shell)).ExitCode);
        Assert.False(File.Exists(project.PathFor("work/changed")));
        project.Write("notify.ps1", "exit 9");
        var probe = await Monitor(project, shell, "-NotifyScript", project.PathFor("notify.ps1"));
        Assert.Equal(2, probe.ExitCode);
        Assert.Contains("Notification failed", probe.Output);
        File.Delete(project.PathFor("state/acceptance.json"));
        using (var unavailable = File.Open(project.PathFor("state/acceptance.json"), FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Equal(2, (await Invoke(project, shell)).ExitCode);
            Assert.False(File.Exists(project.PathFor("work/changed")));
        }
        config["job_id"] = "NUL";
        SaveConfig(project, config);
        var reserved = await Invoke(project, shell);
        Assert.Equal(2, reserved.ExitCode);
        Assert.Contains("reserved Windows device", reserved.Error);
        Assert.False(File.Exists(project.PathFor("work/changed")));
    }
}
