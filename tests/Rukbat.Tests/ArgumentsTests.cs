using System.Text.Json;

namespace Rukbat.Tests;

public class ArgumentsTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task CliArgumentsPreserveLiteralStrings()
    {
        using var project = new Project();
        project.Write("main.rukbat", "print(powershell<string>('ConvertTo-Json', InputObject: args(), Compress: true))");
        string[] supplied = ["hello world", "", "日本語", "--flag", "42", "false", "a\"b", "C:\\path with space\\", "$literal; $(not-code)", "line\nbreak"];
        var result = await RunCli(project, ["run", project.PathFor("main.rukbat"), "--", .. supplied]);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(supplied, JsonSerializer.Deserialize<string[]>(result.Output));
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CliWithoutArgumentsProvidesAnEmptyArray(bool separator)
    {
        using var project = new Project();
        project.Write("main.rukbat", "print(args().length)");
        string[] command = separator ? ["run", project.PathFor("main.rukbat"), "--"] : ["run", project.PathFor("main.rukbat")];
        var result = await RunCli(project, command);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("0\n", result.Output);
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task ArgumentsAreEntryArgumentsAndCopiedAcrossModules()
    {
        using var project = new Project();
        project.Write("other.rukbat", "fn changed(value: int): string[] { values: string[] = args(); values[0] = 'changed'; return values }");
        project.Write("main.rukbat", "import './other.rukbat' as other\nvalues: string[] = other.changed(42); print(values[0]); print(args()[0])");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.Delete(project.PathFor("main.rukbat"));
        File.Delete(project.PathFor("other.rukbat"));
        var result = await project.RunOutput(compiled.Output!, "original");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("changed\noriginal\n", result.Output);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("args(1)")]
    [InlineData("args(value: 'x')")]
    [InlineData("value: int = args()")]
    [InlineData("args: int = 1")]
    [InlineData("fn args(): void {}")]
    [InlineData("args()[0] = 'changed'")]
    public void RejectsInvalidArgumentsBuiltin(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Trait("Category", "PowerShell51")]
    [Trait("Smoke", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task GeneratedScriptRunsWithWindowsPowerShell51()
    {
        using var project = new Project();
        project.Write("main.rukbat", "message := args()[0]\nprint(message)");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await project.RunOutputWithWindowsPowerShell(compiled.Output!, "PowerShell 5.1");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("PowerShell 5.1\n", result.Output);
        Assert.Equal("", result.Error);
    }
}
