using System.Text.Json;

namespace Rukbat.Tests;

public class DebugTests : TestBase
{
    [Trait("Category", "Unit")]
    [Fact]
    public void DebugMapUsesOriginalPositionsAndBoundNamesWithoutChangingRegularBuilds()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "fn number(): int {\n    value := 2\n    return value\n}");
        project.Write("main.rukbat", "import './helper.rukbat' as helper\nconst value := helper.number()\nprint(value)");
        var ordinary = project.Compile();
        var debug = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug: true);
        Assert.True(debug.Success, string.Join("\n", debug.Diagnostics));
        Assert.Null(ordinary.DebugMap);
        Assert.DoesNotContain("__rukbat_debug", ordinary.Output!);
        Assert.DoesNotContain(project.DirectoryPath, ordinary.Output!);
        var map = Assert.IsType<DebugMap>(debug.DebugMap);
        Assert.Equal(4, map.Points.Count);
        var point = Assert.Single(map.Points.Where(point => point.File == project.PathFor("main.rukbat") && point.Line == 3));
        var variable = Assert.Single(point.Variables);
        Assert.Equal("value", variable.Name);
        Assert.True(variable.IsConst);
        Assert.Equal("int", map.Types[variable.Type].Name);
        Assert.Equal("$__rukbat_location = 'main.rukbat:3'", debug.Output!.Split('\n')[point.GeneratedLine - 1].Trim());
        Assert.Equal(ordinary.Output, project.Compile().Output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DebugBuildWritesSidecarAndRejectsUnsupportedArguments()
    {
        using var project = new Project();
        project.Write("main.rukbat", "value := 1\nprint(value)");
        var output = project.PathFor("session.ps1");
        using var error = new StringWriter();
        Assert.Equal(0, Cli.Run(["build", project.PathFor("main.rukbat"), "--debug", "--output", output], TextWriter.Null, error));
        Assert.Equal("", error.ToString());
        using var map = JsonDocument.Parse(File.ReadAllText(output + ".rukbat-map.json"));
        Assert.Equal(1, map.RootElement.GetProperty("version").GetInt32());
        Assert.Equal(2, map.RootElement.GetProperty("points").GetArrayLength());
        Assert.False(File.Exists(project.PathFor("main.ps1")));
        Assert.Equal(2, Cli.Run(["run", project.PathFor("main.rukbat"), "--debug"], TextWriter.Null, error));
        Assert.Equal(2, Cli.Run(["build", project.PathFor("main.rukbat"), "--debug", "--output", project.PathFor("main.rukbat")], TextWriter.Null, error));
        Assert.Equal("value := 1\nprint(value)", File.ReadAllText(project.PathFor("main.rukbat")));
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "PowerShell51")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task DebugOutputRunsStandaloneAndPreservesLoopsCopiesAndReturns(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", "fn number(): int { return 3 }\ncount := 0\nwhile count < number() { count = count + 1; continue }\nprint(count)");
        var result = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug: true);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        project.Write("main.ps1", result.Output!);
        File.Delete(project.PathFor("main.rukbat"));
        var run = await RunDeployed(shell, project.PathFor("main.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, run.ExitCode);
        Assert.Equal("3", run.Output.Trim());
        Assert.Equal("", run.Error);
    }
}
