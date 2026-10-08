namespace Rukbat.Tests;

public class RuntimeResourceTests : TestBase
{
    public static TheoryData<string> StandardModules => new()
    {
        "fs", "path", "json", "csv", "env", "process", "text", "test",
        "encoding", "math", "datetime", "array", "zip", "hash", "http", "cli"
    };

    [Theory]
    [MemberData(nameof(StandardModules))]
    [Trait("Category", "Unit")]
    public void EachStandardModuleCanEmitItsEmbeddedRuntime(string module)
    {
        Success($"import {module}\n");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    [Trait("Category", "CrossVersion")]
    public async Task CombinedRuntimeResourcesExecuteInAStandaloneScript(bool windowsPowerShell)
    {
        using var project = new Project();
        var imports = string.Join('\n', StandardModules.Select(row => "import " + row[0]));
        var output = Success(imports + "\n" + """
            command := cli.command("resource-test", "")
            parsed := cli.parse(command, [])
            test.assert(!cli.help_requested(parsed))
            row := csv.row(["name"], ["Rukbat"])
            test.assert(csv.get(row, "name") == "Rukbat")
            values: dict[string, int] = json.parse<dict[string, int]>('{"a": 1}')
            test.assert(values["a"] == 1)
            print(text.upper("ok"))
            """);
        var result = windowsPowerShell
            ? await project.RunOutputWithWindowsPowerShell(output)
            : await project.RunOutput(output);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("OK\n", result.Output);
        Assert.Empty(result.Error);
    }
}
