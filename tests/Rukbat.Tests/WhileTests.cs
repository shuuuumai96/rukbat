namespace Rukbat.Tests;

public class WhileTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("n: int = 0; while n < 3 { print(n); n = n + 1 }", "0\n1\n2")]
    [InlineData("n: int = 0; while n < 5 { n = n + 1; if n == 2 { continue }; if n == 4 { break }; print(n) }; print(n)", "1\n3\n4")]
    [InlineData("outer: int = 0; while outer < 2 { inner: int = 0; while inner < 3 { inner = inner + 1; if inner == 2 { break }; print(inner) }; outer = outer + 1 }; print(outer)", "1\n1\n2")]
    [InlineData("value: string? = 'ready'; while value != null { print(value); value = null }; print(value == null)", "ready\nTrue")]
    public async Task ExecutesWhileLoops(string source, string expected)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("while 1 {}", "Expected bool")]
    [InlineData("while true { value: int = 1 }; print(value)", "Unknown variable")]
    [InlineData("fn f(): int { while true { return 1 } }", "every path")]
    public void RejectsInvalidWhileLoops(string source, string message)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void FormatsWhileLoopsCanonically()
    {
        using var project = new Project();
        var input = project.PathFor("main.rukbat");
        project.Write("main.rukbat", "n:=0\nwhile n<2{\nn=n+1\nif n==1{continue}\nbreak\n}\n");

        Assert.Equal(0, Cli.Run(["fmt", input], TextWriter.Null, TextWriter.Null));
        Assert.Equal(
            "n := 0\n\nwhile n < 2 {\n    n = n + 1\n\n    if n == 1 {\n        continue\n    }\n\n    break\n}\n\n",
            File.ReadAllText(input));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task WhileRunsOnWindowsPowerShell51AndPowerShell7()
    {
        using var project = new Project();
        project.Write("main.rukbat", "attempt := 0; while attempt < 3 { attempt = attempt + 1 }; print(attempt)");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        var modern = await project.RunOutput(compiled.Output!);
        Assert.Equal(0, modern.ExitCode);
        Assert.Equal("3", modern.Output.TrimEnd('\n'));

        var windows = await project.RunOutputWithWindowsPowerShell(compiled.Output!);
        Assert.Equal(0, windows.ExitCode);
        Assert.Equal("3", windows.Output.TrimEnd('\r', '\n'));
    }
}
