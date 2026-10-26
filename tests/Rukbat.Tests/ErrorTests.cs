namespace Rukbat.Tests;

public class ErrorTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("try { throw Error {message: 'failed'}; print('unreachable') } catch err { print(err.message) }; print('after')", "failed\nafter")]
    [InlineData("try { print('ok') } catch err { print('unexpected') }", "ok")]
    [InlineData("fn f(): int { throw Error {message: 'failed'} }; try { print(f()) } catch err { print(err.message) }", "failed")]
    [InlineData("fn f(): int { try { throw Error {message: 'failed'} } catch err { return 42 } }; print(f())", "42")]
    [InlineData("fn f(): int { try { return 42 } catch err { throw err } }; print(f())", "42")]
    [InlineData("fn f(): void { try { return } catch err { print('unexpected') }; print('unreachable') }; f(); print('after')", "after")]
    [InlineData("try { try { throw Error {message: 'original'} } catch inner { throw inner } } catch outer { print(outer.message) }", "original")]
    [InlineData("try { try { throw Error {message: 'original'} } catch inner { throw Error {message: 'replacement'} } } catch outer { print(outer.message) }", "replacement")]
    [InlineData("err: string = 'outer'; try { throw Error {message: 'inner'} } catch err { print(err.message) }; print(err)", "inner\nouter")]
    [InlineData("original: Error = Error {message: 'original'}; copy: Error = original; copy.message = 'copy'; print(original.message); print(copy.message)", "original\ncopy")]
    [InlineData("for n in [1,2,3] { try { if n == 1 { continue }; throw Error {message: 'stop'} } catch err { print(n); break } }; print('done')", "2\ndone")]
    [InlineData("for n in [1,2,3] { try { throw Error {message: 'next'} } catch err { if n == 1 { continue }; print(n) } }", "2\n3")]
    [InlineData("try { powershell<void>('Write-Error', 'external failure') } catch err { print(err.message) }", "external failure")]
    [InlineData("try { print(1 / 0) } catch err { print('arithmetic') }; try { xs: int[] = []; print(xs[0]) } catch err { print('bounds') }", "arithmetic\nbounds")]
    [InlineData("try { print(powershell<int>('Write-Output', 'bad')) } catch err { print('type') }", "type")]
    [InlineData("try { powershell<void>('Missing-Rukbat-Command') }\ncatch err { print('missing') }", "missing")]
    [InlineData("try { print(9223372036854775807 + 1) } catch err { print('overflow') }", "overflow")]
    [InlineData("try { powershell<void>('pwsh', '-NoLogo', '-NoProfile', '-Command', 'exit 0'); print('success') } catch err { print('wrong') }", "success")]
    [InlineData("struct S { n: int }; impl S { fn fail(): void { self.n = 2; throw Error {message: 'failure'} } }; s: S = S {n: 1}; try { s.fail() } catch err { print(s.n) }", "2")]
    [InlineData("fn message(err: Error): string { err.message = 'local'; return err.message }; try { throw Error {message: 'original'} } catch err { print(message(err)); print(err.message) }", "local\noriginal")]
    public async Task ExecutesErrorHandling(string source, string expected)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("throw 'wrong'")]
    [InlineData("throw")]
    [InlineData("try {}")]
    [InlineData("try {} catch {}")]
    [InlineData("try {} catch err {} catch other {}")]
    [InlineData("try {} catch err {} finally {}")]
    [InlineData("try {} catch err { print(err.missing) }")]
    [InlineData("try {} catch err { err.message = 1 }")]
    [InlineData("try {} catch err {}; print(err.message)")]
    [InlineData("try { n: int = 1 } catch err { print(n) }")]
    [InlineData("fn f(): int { try { throw Error {message: 'x'} } catch err {} }")]
    [InlineData("fn f(): int { try {} catch err { return 1 } }")]
    [InlineData("Error {}")]
    [InlineData("Error {message: 1}")]
    [InlineData("Error {'wrong'}")]
    [InlineData("struct Error {}")]
    [InlineData("fn Error {}: void {}")]
    [InlineData("Error: int = 1")]
    [InlineData("try: int = 1")]
    [InlineData("try {} catch err { import other }")]
    [InlineData("try {} catch Error {}")]
    [InlineData("try {} catch err: Error {}")]
    [InlineData("Error {message: 'one', message: 'two'}")]
    [InlineData("Error {message: 'one', code: 1}")]
    public void RejectsInvalidErrorHandling(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task RethrowAcrossModulesPreservesOriginAndErrorValueSemantics()
    {
        using var project = new Project();
        project.Write("service.rukbat", "fn fail(): int {\n    throw Error {message: 'original'}\n}\nfn forward(): int { try { return fail() } catch err { saved: Error = err; saved.message = 'copied'; throw saved } }");
        project.Write("main.rukbat", "import './service.rukbat' as service\nprint('before'); print(service.forward()); print('unreachable')");
        var result = await RunCli(project, ["run", project.PathFor("main.rukbat")]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("before\n", result.Output);
        Assert.Equal("service.rukbat:2: error: copied", result.Error.Trim());
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task NativeFailureIsCaughtAndDoesNotPoisonLaterPowerShellCalls()
    {
        using var project = new Project();
        project.Write("main.rukbat", "try { powershell<void>('pwsh', '-NoLogo', '-NoProfile', '-Command', 'exit 7'); print('unreachable') } catch err { print(err.message) }; print(powershell<string>('Write-Output', 'after'))");
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Native command 'pwsh' exited with code 7.\nafter", result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task UnhandledNativeFailureMakesCliFail()
    {
        using var project = new Project();
        project.Write("main.rukbat", "powershell<void>('pwsh', '-NoLogo', '-NoProfile', '-Command', 'exit 7'); print('unreachable')");
        var result = await RunCli(project, ["run", project.PathFor("main.rukbat")]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.Contains("exited with code 7", result.Error);
        Assert.Contains("main.rukbat:1: error:", result.Error);
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task NativeAliasFailureIsNormalizedBeforeResultTypeChecking()
    {
        using var project = new Project();
        project.Write("main.rukbat", "powershell<void>('Set-Alias', 'rukbat-native', 'pwsh', Scope: 'Global'); try { print(powershell<int>('rukbat-native', '-NoLogo', '-NoProfile', '-Command', 'exit 7')) } catch err { print(err.message) }");
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("Native command 'rukbat-native' exited with code 7.\n", result.Output);
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task ArithmeticAfterFunctionCallReportsCallerLocation()
    {
        using var project = new Project();
        project.Write("main.rukbat", "fn zero(): int {\n return 0\n}\nprint(1 / zero())");
        var result = await project.Run();
        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith("main.rukbat:4: error:", result.Error);
        Assert.DoesNotContain("main.ps1", result.Error);
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task ErrorTypeIsSharedAcrossModulesAndArrayReturnsRemainSingleValues()
    {
        using var project = new Project();
        project.Write("service.rukbat", "fn relay(err: Error): Error { return err }; fn values(): Error[] { try { throw Error {message: 'array'} } catch err { return [err] } }");
        project.Write("main.rukbat", "import './service.rukbat' as service\ntry { throw service.relay(service.values()[0]) } catch err { print(err.message) }");
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("array\n", result.Output);
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void RejectsImportThatShadowsErrorType()
    {
        using var project = new Project();
        project.Write("other.rukbat", "");
        project.Write("main.rukbat", "import './other.rukbat' as Error");
        var result = project.Compile();
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Message.Contains("built-in type"));
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("throw Error {message: 'startup'}", "startup")]
    [InlineData("print(1 / 0)", "divide by zero")]
    public async Task ModuleInitializationFailureReportsImportedSource(string source, string message)
    {
        using var project = new Project();
        project.Write("service.rukbat", "// initialization\n" + source);
        project.Write("main.rukbat", "import './service.rukbat' as service\ntry { print('unreachable') } catch err { print('also unreachable') }");
        var result = await project.Run();
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.StartsWith("service.rukbat:2: error:", result.Error);
        Assert.Contains(message, result.Error);
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("try {\n throw Error {message: 'first'}\n} catch err {\n throw Error {message: 'second'}\n}", 4, "second")]
    [InlineData("try {\n throw Error {message: 'first'}\n} catch err {\n print(1 / 0)\n}", 4, "divide by zero")]
    [InlineData("try {\n powershell<void>('Write-Error', 'first')\n} catch err {\n throw err\n}", 2, "first")]
    [InlineData("try {\n xs: int[] = []; print(xs[0])\n} catch err {\n throw err\n}", 2, "Array index")]
    public async Task UnhandledErrorsUseOriginRatherThanCatchLocation(string source, int line, string message)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.Equal(1, result.ExitCode);
        Assert.StartsWith($"main.rukbat:{line}: error:", result.Error);
        Assert.Contains(message, result.Error);
    }
}
