namespace Rukbat.Tests;

public class JsonTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Trait("Category", "PowerShell51")]
    [Fact]
    public async Task TypedJsonRoundTripCompletesForTenThousandRecordsOnBothHosts()
    {
        using var project = new Project();
        project.Write("main.rukbat", TypedJsonRoundTripSource(10_000));
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        var modern = await project.RunOutput(compiled.Output!);
        Assert.Equal(0, modern.ExitCode);
        Assert.Equal("", modern.Error);

        var windows = await project.RunOutputWithWindowsPowerShell(compiled.Output!);
        Assert.Equal(0, windows.ExitCode);
        Assert.Equal("", windows.Error);
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task JsonRejectsWhitespaceOutsideTheJsonGrammar()
    {
        using var project = new Project();
        project.Write("main.rukbat", "import json\njson.parse<int>('1\u00a0')");
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unexpected trailing content", result.Error);
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("print(json.parse<int>('30'))", "30")]
    [InlineData("print(json.parse<float>('1e0') + 0.5)", "1.5")]
    [InlineData("x: string? = json.parse<string?>('null'); print(x == null)", "True")]
    [InlineData("xs: string?[] = json.parse<string?[]>('[\"a\",null]'); first: string? = xs[0]; if first != null { print(first) }; print(xs[1] == null)", "a\nTrue")]
    [InlineData("struct U { name: string; nickname: string?; score: float }; u: U = json.parse<U>('{\"name\":\"Taro\",\"nickname\":null,\"score\":12.5,\"extra\":1}'); print(u.name); print(u.nickname == null); print(u.score)", "Taro\nTrue\n12.5")]
    [InlineData("struct U { name: string; values: int[] }; u: U = U {name: 'A', values: [1,2]}; print(json.stringify(u))", "{\"name\":\"A\",\"values\":[1,2]}")]
    [InlineData("x: string? = null; print(json.stringify(x)); print(json.stringify(['a\\n\"b']))", "null\n[\"a\\n\\\"b\"]")]
    public async Task EncodesAndDecodesTypedJson(string source, string expected)
    {
        using var project = new Project();
        project.Write("main.rukbat", "import json\n" + source);
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("json.parse<int>('1.0')", "incompatible with int")]
    [InlineData("json.parse<int>('1e0')", "incompatible with int")]
    [InlineData("json.parse<int>('9223372036854775808')", "incompatible with int")]
    [InlineData("json.parse<float>('1e400')", "incompatible with float")]
    [InlineData("json.parse<string>('null')", "null is incompatible")]
    [InlineData("struct U { name: string? }; json.parse<U>('{}')", "Missing required property")]
    [InlineData("struct U { name: string }; json.parse<U>('{\"Name\":\"x\"}')", "Missing required property")]
    [InlineData("struct U { name: string }; json.parse<U>('{\"name\":\"x\",\"name\":\"y\"}')", "Duplicate property")]
    [InlineData("json.parse<int>('1 trailing')", "trailing")]
    public async Task RejectsInvalidTypedJson(string source, string message)
    {
        using var project = new Project();
        project.Write("main.rukbat", "import json\n" + source);
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("import json\njson.parse()")]
    [InlineData("import json\njson.stringify<int>(1)")]
    [InlineData("import json\njson.parse<int>(1)")]
    [InlineData("import json\njson.parse<Error>('{}')")]
    public void RejectsInvalidJsonCalls(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Trait("Category", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task JsonRunsOnWindowsPowerShell51()
    {
        using var project = new Project();
        project.Write("main.rukbat", "import json\nstruct U { name: string; score: float? }; u: U = json.parse<U>('{\"name\":\"Taro\",\"score\":null}'); print(json.stringify(u))");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await project.RunOutputWithWindowsPowerShell(compiled.Output!);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{\"name\":\"Taro\",\"score\":null}", result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
    }

    private static string TypedJsonRoundTripSource(int count)
    {
        var text = new System.Text.StringBuilder("[");
        for (var index = 0; index < count; index++)
        {
            if (index != 0) text.Append(',');
            text.Append("{\"id\":").Append(index)
                .Append(",\"name\":\"record-").Append(index.ToString("D5"))
                .Append("\",\"score\":").Append(index).Append(".5}");
        }
        text.Append(']');
        Assert.InRange(text.Length, 460_000, 600_000);
        return "import json\n" +
            "struct Record { id: int; name: string; score: float }\n" +
            "payload: string = '" + text + "'\n" +
            "records := json.parse<Record[]>(payload)\n" +
            "encoded := json.stringify(records)\n";
    }
}
