using System.Text.Json;

namespace Auvil.Tests;

public partial class CompilerTests
{
    [Fact]
    public async Task CliArgumentsPreserveLiteralStrings()
    {
        using var project = new Project();
        project.Write("main.auv", "print(powershell<string>('ConvertTo-Json', InputObject: args(), Compress: true))");
        string[] supplied = ["hello world", "", "日本語", "--flag", "42", "false", "a\"b", "C:\\path with space\\", "$literal; $(not-code)", "line\nbreak"];
        var result = await RunCli(project, ["run", project.PathFor("main.auv"), "--", .. supplied]);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(supplied, JsonSerializer.Deserialize<string[]>(result.Output));
        Assert.Equal("", result.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CliWithoutArgumentsProvidesAnEmptyArray(bool separator)
    {
        using var project = new Project();
        project.Write("main.auv", "print(args().length)");
        string[] command = separator ? ["run", project.PathFor("main.auv"), "--"] : ["run", project.PathFor("main.auv")];
        var result = await RunCli(project, command);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("0\n", result.Output);
    }

    [Fact]
    public async Task ArgumentsAreEntryArgumentsAndCopiedAcrossModules()
    {
        using var project = new Project();
        project.Write("other.auv", "fn changed(value: int): string[] { values: string[] = args(); values[0] = 'changed'; return values }");
        project.Write("main.auv", "import './other.auv' as other\nvalues: string[] = other.changed(42); print(values[0]); print(args()[0])");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.Delete(project.PathFor("main.auv"));
        File.Delete(project.PathFor("other.auv"));
        var result = await project.RunOutput(compiled.Output!, "original");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("changed\noriginal\n", result.Output);
    }

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

    [Fact]
    public async Task GeneratedScriptRunsWithWindowsPowerShell51()
    {
        using var project = new Project();
        project.Write("main.auv", "message := args()[0]\nprint(message)");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await project.RunOutputWithWindowsPowerShell(compiled.Output!, "PowerShell 5.1");
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("PowerShell 5.1\n", result.Output);
        Assert.Equal("", result.Error);
    }
}
