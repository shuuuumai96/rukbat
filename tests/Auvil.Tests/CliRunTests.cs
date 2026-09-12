using System.Diagnostics;

namespace Auvil.Tests;

public partial class CompilerTests
{
    [Fact]
    public async Task CliRunCompilesImportsAndHandlesPathsWithSpaces()
    {
        using var project = new Project();
        project.Write("math.auv", "func add(a: int, b: int): int { return a + b }");
        project.Write("run input.auv", "import math\nprint(math.add(2, 3)); print(powershell<string>('Get-Content', 'data.txt', Raw: true))");
        project.Write("data.txt", "relative path");
        var result = await RunCli(project, ["run", project.PathFor("run input.auv")]);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("5\nrelative path", result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
        Assert.True(File.Exists(project.PathFor("run input.ps1")));
    }

    [Fact]
    public async Task CliRunNeverExecutesStaleOutputAfterCompilationFailure()
    {
        using var project = new Project();
        project.Write("main.auv", "x: int = 'bad'");
        project.Write("main.ps1", "[Console]::WriteLine('STALE OUTPUT EXECUTED')");
        var result = await RunCli(project, ["run", project.PathFor("main.auv")]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.Contains("AUV2001", result.Error);
        Assert.Equal("[Console]::WriteLine('STALE OUTPUT EXECUTED')", File.ReadAllText(project.PathFor("main.ps1")));
    }

    [Fact]
    public async Task CliRunPropagatesRuntimeFailureAndStandardError()
    {
        using var project = new Project();
        project.Write("main.auv", "print('before'); powershell<void>('Write-Error', 'expected failure')");
        var result = await RunCli(project, ["run", project.PathFor("main.auv")]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("before", result.Output.TrimEnd('\n'));
        Assert.Contains("expected failure", result.Error);
    }

    [Fact]
    public async Task CliRunPropagatesTheExactPowerShellExitCode()
    {
        using var project = new Project();
        project.Write("native.psm1", "function Stop-Auvil { [Environment]::Exit(7) }; Export-ModuleMember -Function Stop-Auvil");
        var path = project.PathFor("native.psm1").Replace("\\", "\\\\").Replace("'", "\\'");
        project.Write("main.auv", $"powershell<void>('Import-Module', '{path}', Global: true); powershell<void>('Stop-Auvil')");
        var result = await RunCli(project, ["run", project.PathFor("main.auv")]);
        Assert.Equal(7, result.ExitCode);
    }

    [Fact]
    public async Task CliRunReportsMissingPowerShell()
    {
        using var project = new Project();
        project.Write("main.auv", "print('hello')");
        var result = await RunCli(project, ["run", project.PathFor("main.auv")], hidePowerShell: true);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Could not start pwsh", result.Error);
        Assert.Equal("", result.Output);
    }

    [Theory]
    [InlineData("run")]
    [InlineData("unknown", "main.auv")]
    [InlineData("run", "main.ps1")]
    [InlineData("run", "main.auv", "unexpected")]
    public void CliRunRejectsInvalidArguments(params string[] args)
    {
        using var error = new StringWriter();
        Assert.Equal(2, Cli.Run(args, TextWriter.Null, error));
        Assert.Contains("Usage: auvil <build|run>", error.ToString());
    }

    private static async Task<RunResult> RunCli(Project project, string[] args, bool hidePowerShell = false)
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
            CreateNoWindow = true, WorkingDirectory = project.DirectoryPath
        };
        info.ArgumentList.Add(typeof(Cli).Assembly.Location);
        foreach (var argument in args) info.ArgumentList.Add(argument);
        if (hidePowerShell) info.Environment["PATH"] = project.DirectoryPath;
        using var process = Process.Start(info)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("CLI did not finish in 30 seconds."); }
        return new(process.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
    }
}
