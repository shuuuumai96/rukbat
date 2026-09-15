namespace Rukbat.Tests;

public partial class CompilerTests
{
    [Fact]
    public async Task JsonRejectsWhitespaceOutsideTheJsonGrammar()
    {
        using var project = new Project();
        project.Write("main.rukbat", "import json\njson.decode<int>('1\u00a0')");
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Unexpected trailing content", result.Error);
    }

    [Theory]
    [InlineData("print(json.decode<int>('30'))", "30")]
    [InlineData("print(json.decode<float>('1e0') + 0.5)", "1.5")]
    [InlineData("x: string? = json.decode<string?>('null'); print(x == null)", "True")]
    [InlineData("xs: string?[] = json.decode<string?[]>('[\"a\",null]'); first: string? = xs[0]; if first != null { print(first) }; print(xs[1] == null)", "a\nTrue")]
    [InlineData("struct U { name: string; nickname: string?; score: float }; u: U = json.decode<U>('{\"name\":\"Taro\",\"nickname\":null,\"score\":12.5,\"extra\":1}'); print(u.name); print(u.nickname == null); print(u.score)", "Taro\nTrue\n12.5")]
    [InlineData("struct U { name: string; values: int[] }; u: U = U(name: 'A', values: [1,2]); print(json.encode(u))", "{\"name\":\"A\",\"values\":[1,2]}")]
    [InlineData("x: string? = null; print(json.encode(x)); print(json.encode(['a\\n\"b']))", "null\n[\"a\\n\\\"b\"]")]
    public async Task EncodesAndDecodesTypedJson(string source, string expected)
    {
        using var project = new Project();
        project.Write("main.rukbat", "import json\n" + source);
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
    }

    [Theory]
    [InlineData("json.decode<int>('1.0')", "incompatible with int")]
    [InlineData("json.decode<int>('1e0')", "incompatible with int")]
    [InlineData("json.decode<int>('2147483648')", "incompatible with int")]
    [InlineData("json.decode<float>('1e400')", "incompatible with float")]
    [InlineData("json.decode<string>('null')", "null is incompatible")]
    [InlineData("struct U { name: string? }; json.decode<U>('{}')", "Missing required property")]
    [InlineData("struct U { name: string }; json.decode<U>('{\"Name\":\"x\"}')", "Missing required property")]
    [InlineData("struct U { name: string }; json.decode<U>('{\"name\":\"x\",\"name\":\"y\"}')", "Duplicate property")]
    [InlineData("json.decode<int>('1 trailing')", "trailing")]
    public async Task RejectsInvalidTypedJson(string source, string message)
    {
        using var project = new Project();
        project.Write("main.rukbat", "import json\n" + source);
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("import json\njson.decode('1')")]
    [InlineData("import json\njson.encode<int>(1)")]
    [InlineData("import json\njson.decode<int>(1)")]
    [InlineData("import json\njson.decode<Error>('{}')")]
    public void RejectsInvalidJsonCalls(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Fact]
    public async Task JsonRunsOnWindowsPowerShell51()
    {
        using var project = new Project();
        project.Write("main.rukbat", "import json\nstruct U { name: string; score: float? }; u: U = json.decode<U>('{\"name\":\"Taro\",\"score\":null}'); print(json.encode(u))");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await project.RunOutputWithWindowsPowerShell(compiled.Output!);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("{\"name\":\"Taro\",\"score\":null}", result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
    }
}
