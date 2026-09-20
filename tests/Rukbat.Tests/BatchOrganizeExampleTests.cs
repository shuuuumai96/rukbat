using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests;

public class BatchOrganizeExampleTests : TestBase
{
    private static string ExampleSource => File.ReadAllText(Path.Combine(RepositoryRoot(), "examples", "batch-organize", "main.rukbat"));

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchOrganizeMovesFilesCreatesParentsAndRunsStandalone(string shell)
    {
        using var project = new Project();
        project.Write("source/main.rukbat", ExampleSource);
        project.Write("work/incoming/report [one] 日本語;$.txt", "first");
        project.Write("work/incoming/nested/data.csv", "second");
        project.Write("plans/plan.csv", """
            source,destination
            incoming/report [one] 日本語;$.txt,archive/2026/report [one] 日本語;$.txt
            incoming/nested/data.csv,processed/nested/data.csv
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("source/main.rukbat"));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain("powershell<", ExampleSource, StringComparison.Ordinal);
        var script = project.PathFor("deploy/batch-organize.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
        File.Delete(project.PathFor("source/main.rukbat"));

        var result = await RunScript(shell, script, project.PathFor("work"), "../plans/plan.csv");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.Equal("", result.Error);
        Assert.False(File.Exists(project.PathFor("work/incoming/report [one] 日本語;$.txt")));
        Assert.False(File.Exists(project.PathFor("work/incoming/nested/data.csv")));
        Assert.Equal("first", File.ReadAllText(project.PathFor("work/archive/2026/report [one] 日本語;$.txt")));
        Assert.Equal("second", File.ReadAllText(project.PathFor("work/processed/nested/data.csv")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchOrganizeRejectsPlansBeforeAnyMutation(string shell)
    {
        var cases = new[]
        {
            ("missing header", "wrong,destination\nincoming/keep.txt,archive/keep.txt"),
            ("wrong width", "source,destination\nincoming/keep.txt,archive/keep.txt,extra"),
            ("blank record", "source,destination\n\nincoming/keep.txt,archive/keep.txt"),
            ("empty source", "source,destination\n,archive/keep.txt"),
            ("empty destination", "source,destination\nincoming/keep.txt,"),
            ("missing source", "source,destination\nmissing.txt,archive/missing.txt"),
            ("existing destination", "source,destination\nincoming/keep.txt,archive/existing.txt"),
            ("destination directory", "source,destination\nincoming/keep.txt,archive/existing-directory"),
            ("duplicate source", "source,destination\nincoming/keep.txt,archive/one.txt\n./incoming/KEEP.txt,archive/two.txt"),
            ("duplicate destination", "source,destination\nincoming/keep.txt,archive/same.txt\nincoming/other.txt,./archive/SAME.txt"),
            ("same path", "source,destination\nincoming/keep.txt,./incoming/KEEP.txt"),
            ("malformed csv", "source,destination\n\"unclosed")
        };
        foreach (var (name, plan) in cases)
        {
            using var project = new Project();
            project.Write("main.rukbat", ExampleSource);
            project.Write("incoming/keep.txt", "keep");
            project.Write("incoming/other.txt", "other");
            project.Write("archive/existing.txt", "existing");
            Directory.CreateDirectory(project.PathFor("archive/existing-directory"));
            project.Write("plan.csv", plan);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var script = project.PathFor("job.ps1");
            File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));

            var result = await RunScript(shell, script, project.DirectoryPath, "plan.csv");
            Assert.Equal(2, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.NotEqual("", result.Error);
            Assert.True(File.Exists(project.PathFor("incoming/keep.txt")), name);
            Assert.True(File.Exists(project.PathFor("incoming/other.txt")), name);
            Assert.True(File.Exists(project.PathFor("archive/existing.txt")), name);
            Assert.False(File.Exists(project.PathFor("archive/one.txt")), name);
            Assert.False(File.Exists(project.PathFor("archive/two.txt")), name);
            Assert.False(File.Exists(project.PathFor("archive/same.txt")), name);
            Assert.False(Directory.Exists(project.PathFor("processed")), name);
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BatchOrganizeLeavesCompletedMovesWhenLaterMutationFails(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", ExampleSource);
        project.Write("incoming/first.txt", "first");
        project.Write("incoming/second.txt", "second");
        project.Write("blocked", "file prevents parent creation");
        project.Write("plan.csv", """
            source,destination
            incoming/first.txt,archive/first.txt
            incoming/second.txt,blocked/second.txt
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("job.ps1");
        File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));

        var result = await RunScript(shell, script, project.DirectoryPath, "plan.csv");
        Assert.Equal(2, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.NotEqual("", result.Error);
        Assert.Equal("first", File.ReadAllText(project.PathFor("archive/first.txt")));
        Assert.False(File.Exists(project.PathFor("incoming/first.txt")));
        Assert.True(File.Exists(project.PathFor("incoming/second.txt")));
    }

    [Trait("Category", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task BatchOrganizeAcceptsAHeaderOnlyPlanOnWindowsPowerShell51()
    {
        using var project = new Project();
        project.Write("main.rukbat", ExampleSource);
        project.Write("plan.csv", "source,destination");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunScript("powershell", project.PathFor("job.ps1"), project.DirectoryPath, "plan.csv");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.Equal("", result.Error);
    }

    private static async Task<RunResult> RunScript(string shell, string script, string working, params string[] arguments)
    {
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = working,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var value in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", script }) info.ArgumentList.Add(value);
        foreach (var argument in arguments) info.ArgumentList.Add(argument);
        using var child = Process.Start(info)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { child.Kill(true); throw new TimeoutException("Batch-organize generated script did not finish in 30 seconds."); }
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
