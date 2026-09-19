namespace Rukbat.Tests;

public class CliFoundationTests : TestBase
{
    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task CliFoundationLocaleAndImportedExit()
    {
        using var project = new Project();
        project.Write("stop.rukbat", "fn stop(code: int): int { exit(code) }");
        project.Write("main.rukbat", """
            import "./stop.rukbat" as stop
            import text
            eprint(text.from_bool(text.parse_bool('false')))
            value := text.parse_float('1.2345678901234567')
            print(text.from_float(value))
            print(text.parse_float(text.from_float(value)) == value)
            try { stop.stop(text.parse_int('23')) } catch err { print('caught') }
            print('after')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        const string culture = "[System.Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('fr-FR')\n";
        foreach (var result in new[] { await project.RunOutput(culture + compiled.Output!),
            await project.RunOutputWithWindowsPowerShell(culture + compiled.Output!) })
        {
            Assert.Equal(23, result.ExitCode);
            Assert.Equal("1.2345678901234567\nTrue", result.Output.TrimEnd('\r', '\n'));
            Assert.Equal("false", result.Error.TrimEnd('\r', '\n'));
        }
        var cli = await RunCli(project, ["run", project.PathFor("main.rukbat")]);
        Assert.Equal(23, cli.ExitCode);
        Assert.DoesNotContain("caught", cli.Output);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("exit(0)", 0, "", "")]
    [InlineData("eprint('bad'); print('out'); exit(2)", 2, "out", "bad")]
    [InlineData("fn stop(): int { exit(7) }; try { stop() } catch err { print('caught') }; print('after')", 7, "", "")]
    [InlineData("exit(2147483647)", int.MaxValue, "", "")]
    [InlineData("try { exit(-1) } catch err { print('negative') }", 0, "negative", "")]
    public async Task CliFoundationExitAndStreams(string source, int code, string stdout, string stderr)
        => await AssertCliFoundationExitAndStreams(source, code, stdout, stderr, crossVersion: true);

    [Trait("Smoke", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("exit(0)", 0, "", "")]
    [InlineData("eprint('bad'); print('out'); exit(2)", 2, "out", "bad")]
    [InlineData("fn stop(): int { exit(7) }; try { stop() } catch err { print('caught') }; print('after')", 7, "", "")]
    [InlineData("exit(2147483647)", int.MaxValue, "", "")]
    [InlineData("try { exit(-1) } catch err { print('negative') }", 0, "negative", "")]
    public async Task CliFoundationExitAndStreamsOnWindowsPowerShell51(string source, int code, string stdout, string stderr)
        => await AssertCliFoundationExitAndStreams(source, code, stdout, stderr, crossVersion: false);

    private async Task AssertCliFoundationExitAndStreams(
        string source, int code, string stdout, string stderr, bool crossVersion)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var results = crossVersion
            ? new[] { await project.RunOutput(compiled.Output!), await project.RunOutputWithWindowsPowerShell("\uFEFF" + compiled.Output!) }
            : new[] { await project.RunOutputWithWindowsPowerShell("\uFEFF" + compiled.Output!) };
        foreach (var result in results)
        {
            Assert.Equal(code, result.ExitCode);
            Assert.Equal(stdout, result.Output.TrimEnd('\r', '\n'));
            Assert.Equal(stderr, result.Error.TrimEnd('\r', '\n'));
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("int", " +123 ", "123")]
    [InlineData("int", "-2147483648", "-2147483648")]
    [InlineData("float", " +1E-3 ", "0.001")]
    [InlineData("bool", " true ", "true")]
    [InlineData("bool", "\tfalse\r\n", "false")]
    public async Task CliFoundationTextRoundTrips(string type, string input, string expected)
    {
        using var project = new Project();
        var literal = input.Replace("\t", "\\t").Replace("\r", "\\r").Replace("\n", "\\n");
        project.Write("main.rukbat", $"import text; print(text.from_{type}(text.parse_{type}('{literal}')))");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        foreach (var result in new[] { await project.RunOutput(compiled.Output!),
            await project.RunOutputWithWindowsPowerShell("\uFEFF" + compiled.Output!) })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(expected, result.Output.TrimEnd('\r', '\n'));
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("int", "2147483648")]
    [InlineData("int", "1e2")]
    [InlineData("int", "1.0")]
    [InlineData("int", "１２")]
    [InlineData("int", "\u00a012")]
    [InlineData("float", "NaN")]
    [InlineData("float", "Infinity")]
    [InlineData("float", "1e400")]
    [InlineData("float", "1e-400")]
    [InlineData("float", "1,5")]
    [InlineData("float", ".5")]
    [InlineData("float", "1.")]
    [InlineData("bool", "True")]
    [InlineData("bool", "1")]
    [InlineData("bool", "yes")]
    [InlineData("int", "")]
    public async Task CliFoundationRejectsInvalidText(string type, string input)
    {
        using var project = new Project();
        project.Write("main.rukbat", $"import text; try {{ text.parse_{type}('{input}'); print('accepted') }} catch err {{ print('invalid') }}");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        foreach (var result in new[] { await project.RunOutput(compiled.Output!),
            await project.RunOutputWithWindowsPowerShell("\uFEFF" + compiled.Output!) })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("invalid", result.Output.TrimEnd('\r', '\n'));
        }
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("exit()")]
    [InlineData("exit('2')")]
    [InlineData("exit(code: 2)")]
    [InlineData("x := exit(0)")]
    [InlineData("fn exit(): void {}")]
    [InlineData("eprint()")]
    [InlineData("import text; text.from_int(true)")]
    [InlineData("import text; text.parse_bool(1)")]
    public void CliFoundationRejectsInvalidCalls(string source)
        => Assert.NotEmpty(new Compiler().Check(source));
}
