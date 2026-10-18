namespace Rukbat.Tests;

public class RuntimeResourceTests : TestBase
{
    public static TheoryData<string> StandardModules => new()
    {
        "fs", "path", "json", "csv", "env", "process", "text", "test",
        "encoding", "math", "datetime", "array", "zip", "hash", "http", "cli", "log", "re"
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
    public async Task AssemblyBackedHelpersPreserveTheirInitializationTiming(bool windowsPowerShell)
    {
        using var project = new Project();
        var output = Success("""
            import http
            import zip
            import encoding
            fn identity(body: http.Body): http.Body { return body }
            print(powershell<bool>('Invoke-Expression', "$null -ne ('RukbatHttpBody' -as [type])"))
            print(powershell<bool>('Invoke-Expression', "$null -ne ('RukbatZip' -as [type])"))
            options := zip.options(encoding.utf8)
            print(powershell<bool>('Invoke-Expression', "$null -ne ('RukbatZip' -as [type])"))
            """);
        var result = windowsPowerShell
            ? await project.RunOutputWithWindowsPowerShell(output)
            : await project.RunOutput(output);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("True\nFalse\nTrue\n", result.Output);
        Assert.Empty(result.Error);
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
            logger := log.create()
            logger.stderr_level = null
            test.assert(!log.enabled(logger, log.level_info))
            log.info(logger, "filtered")
            test.assert(re.is_match("resource-test", re.compile("resource-[a-z]+", 1000)))
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
