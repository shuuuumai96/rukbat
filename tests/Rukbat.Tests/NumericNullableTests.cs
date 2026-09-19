namespace Rukbat.Tests;

public class NumericNullableTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task EmptyNullableArrayIsDistinctFromNull()
    {
        using var project = new Project();
        project.Write("main.rukbat", "xs: string[]? = []; print(xs == null); print(xs != null)");
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("False\nTrue", result.Output.TrimEnd('\n'));
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("x: float = 1; print(x + 0.5)", "1.5")]
    [InlineData("fn widen(x: int?): float? { return x }; y: float? = widen(2); if y != null { print(y + 0.5) }", "2.5")]
    [InlineData("fn widen(x: int?): float? { return x }; y: float? = widen(null); print(y == null)", "True")]
    [InlineData("fn show(x: string?): void { if x == null { return }; print(x) }; show(null); show('ok')", "ok")]
    [InlineData("name: string? = null; if name == null { name = 'Taro' }; print(name)", "Taro")]
    [InlineData("xs: string?[] = ['one', null]; first: string? = xs[0]; if first != null { print(first) }; print(xs[1] == null); ys: string[]? = ['x']; if ys != null { print(ys.length) }", "one\nTrue\n1")]
    [InlineData("struct U { name: string?; score: float? }; u: U = U(name: null, score: 1); print(u.name == null); score: float? = u.score; if score != null { print(score) }", "True\n1")]
    [InlineData("fn values(ok: bool): string[]? { if ok { return ['x'] }; return null }; a: string[]? = values(true); if a != null { print(a[0]) }; b: string[]? = values(false); print(b == null)", "x\nTrue")]
    [InlineData("print(1 == 1.0); print(1 < 1.5)", "True\nTrue")]
    [InlineData("fn show(x: string?): void { if x != null { try { print('try') } catch err { print(x) } } }; show('ok')", "try")]
    public async Task ExecutesFloatAndNullableValues(string source, string expected)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("x := null")]
    [InlineData("x: string = null")]
    [InlineData("x: void? = null")]
    [InlineData("x: string?? = null")]
    [InlineData("xs: string?[] = []; ys: string[] = xs")]
    [InlineData("xs: int[] = [1]; ys: float[] = xs")]
    [InlineData("x: string? = null; print(x)")]
    [InlineData("x: string? = null; print(x.length)")]
    [InlineData("a: string? = null; b: string? = null; print(a == b)")]
    [InlineData("print(1.0 % 1.0)")]
    [InlineData("x: int = 1.0")]
    [InlineData("x: string? = 'x'; if x != null { for n in [1, 2] { print(x); x = null } }")]
    [InlineData("x := .5")]
    [InlineData("x := 1.")]
    public void RejectsInvalidFloatAndNullablePrograms(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("try { print(1.0 / 0.0) } catch err { print('zero') }", "zero")]
    [InlineData("try { print(1e308 * 1e308) } catch err { print('finite') }", "finite")]
    [InlineData("x: string? = powershell<string?>('Write-Output', null); print(x == null)", "True")]
    [InlineData("xs: string?[] = powershell<string?[]>('Write-Output', null); print(xs.length); print(xs[0] == null)", "1\nTrue")]
    [InlineData("x: float = powershell<float>('Write-Output', 1.5); print(x)", "1.5")]
    public async Task EnforcesFloatAndNullableRuntimeBoundaries(string source, string expected)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task PowerShellNoOutputIsNotNullableNull()
    {
        using var project = new Project();
        project.Write("main.rukbat", "powershell<string?>('Out-Null', 1)");
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("expected one string? result, got 0", result.Error);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void SameModuleCallsInvalidateGlobalNarrowing()
    {
        var result = Compile("name: string? = 'Taro'; fn clear(): void { name = null }; if name != null { clear(); print(name) }");
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("nullable value"));
    }

    [Trait("Category", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task FloatAndNullableRunOnWindowsPowerShell51()
    {
        using var project = new Project();
        project.Write("main.rukbat", "fn value(x: int?): float? { return x }; x: float? = value(2); if x != null { print(x + 0.5) }; y: string? = powershell<string?>('Write-Output', null); print(y == null)");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await project.RunOutputWithWindowsPowerShell(compiled.Output!);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2.5\nTrue", result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
    }
}
