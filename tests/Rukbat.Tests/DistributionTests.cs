namespace Rukbat.Tests;

public class DistributionTests : TestBase
{
    [Trait("Category", "Unit")]
    [Fact]
    public void VersionIsAvailableWithoutInputOrPowerShell()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, Cli.Run(["--version"], output, error));
        Assert.Matches(@"^rukbat \d+\.\d+\.\d+", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("--version")]
    [InlineData("doctor")]
    public void DistributionCommandsRejectUnexpectedArguments(string command)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(2, Cli.Run([command, "extra"], output, error));
        Assert.Equal("", output.ToString());
        Assert.Contains("--version | doctor", error.ToString());
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public void DoctorChecksActualWindowsPowerShellAndExplainsScope()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        Assert.Equal(0, Cli.Run(["doctor"], output, error));
        Assert.Contains("Language mode: FullLanguage", output.ToString());
        Assert.Contains("PowerShell: 5.1", output.ToString());
        Assert.Contains("Compiler directory:", output.ToString());
        Assert.Contains("job permissions", output.ToString());
        Assert.Equal("", error.ToString());
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task MissingPowerShellExplainsHowToRecoverWithoutBreakingVersion()
    {
        using var project = new Project();
        var result = await RunCli(project, ["doctor"], hidePowerShell: true);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Make powershell.exe available on PATH", result.Error);
        var version = await RunCli(project, ["--version"], hidePowerShell: true);
        Assert.Equal(0, version.ExitCode);
        Assert.StartsWith("rukbat ", version.Output);
    }
}
