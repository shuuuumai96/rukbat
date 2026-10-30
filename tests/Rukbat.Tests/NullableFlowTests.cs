namespace Rukbat.Tests;

public sealed class NullableFlowTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("items: int[]? = [1]; while items.length > 0 { items = null }")]
    [InlineData("items: int[]? = [1]; for n in [1] { if n == 1 { items = null; break }; items = [1] }; print(items.length)")]
    [InlineData("items: int[]? = [1]; for n in [1] { items = null; break; items = [1] }; print(items.length)")]
    [InlineData("items: int[]? = [1]; for n in [1] { items = null; continue; items = [1] }; print(items.length)")]
    [InlineData("items: int[]? = [1]; while true { if true { items = null; break }; items = [1] }; print(items.length)")]
    [InlineData("items: int[]? = [1]; for n in [1] { if n == 1 { items = null; continue }; items = [1] }; print(items.length)")]
    [InlineData("items: int[]? = [1]; while items.length > 0 { if true { items = null; continue }; items = [] }")]
    [InlineData("items: int[]? = [1]; while items.length > 0 { for n in [1] { items = null; break } }")]
    [InlineData("items: int[]? = [1]; while items.length > 0 { try { items = null; continue } catch err { items = [] }; items = [] }")]
    [InlineData("items: int[]? = [1]; for n in [1] { try { items = null; break } catch err { items = [1] }; items = [1] }; print(items.length)")]
    [InlineData("items: int[]? = [1]; struct S { n: int }; impl S { fn clear(): void { items = null } }; s := S { n: 0 }; if items != null { s.clear(); print(items.length) }")]
    [InlineData("items: int[]? = [1]; struct S { n: int }; impl S { fn clear(): void { items = null } }; s := S { n: 0 }; for n in [1, 2] { print(items.length); s.clear() }")]
    [InlineData("items: int[]? = [1]; struct S { n: int }; impl S { fn clear(): void { items = null } }; s := S { n: 0 }; try { s.clear(); throw Error { message: 'failed' } } catch err { print(items.length) }")]
    public void RejectsNullableUsesAcrossLoopAndMethodPaths(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code is "RUK2001" or "RUK2004");
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("items: int[]? = [1]; while items.length > 0 { items = [] }")]
    [InlineData("items: int[]? = [1]; while items.length > 0 { items = null; break }")]
    [InlineData("items: int[]? = [1]; while items.length > 0 { for n in [1] { items = null; break }; break }")]
    [InlineData("items: int[]? = [1]; while items.length > 0 { try { items = null; break } catch err { items = [] }; items = [] }")]
    [InlineData("fn f(items: int[]?): int { if items == null { return 0 }; while items.length > 0 { items = null; return 1 }; return 0 }")]
    [InlineData("items: int[]? = [1]; while items != null { print(items.length); items = null }")]
    [InlineData("items: int[]? = [1]; for n in [1] { if items == null { continue }; print(items.length) }")]
    [InlineData("items: int[]? = [1]; for n in [1] { if items == null { break }; print(items.length) }")]
    [InlineData("fn f(items: int[]?): int { if items == null { return 0 }; for n in [1] { items = null; return 1 }; return items.length }")]
    [InlineData("items: int[]? = [1]; for n in [1] { items = [1]; if n == 1 { items = null; continue }; print(items.length) }")]
    [InlineData("items: int[]? = null; try { throw Error { message: 'failed' } } catch err { items = [1] }; print(items.length)")]
    [InlineData("fn f(items: int[]?): int { if items == null { return 0 }; if true { return 1 } else { return 2 }; print(items.length) }")]
    public void PreservesNarrowingOnReachablePaths(string source) => Success(source);

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("fn f(): void { return; const items := [1]; items.append(2) }", "RUK2011")]
    [InlineData("struct S { value: int }; impl S { fn change(): void { return; self.value = 2 } }; const s := S { value: 1 }; s.change()", "RUK2011")]
    [InlineData("fn f(): void { return; print(missing) }", "RUK2004")]
    [InlineData("fn f(): void { return; count: int = 'wrong' }", "RUK2001")]
    public void StillChecksUnreachableStatements(string source, string code) =>
        Assert.Contains(Compile(source).Diagnostics, diagnostic => diagnostic.Code == code);

    [Trait("Category", "Unit")]
    [Fact]
    public void BindsDeeplyNestedNarrowedLoopsWithSameModuleMethods()
    {
        const int depth = 20;
        var source = "items: int[]? = [1]; struct S { n: int }; impl S { fn observe(): int { return self.n } }; s := S { n: 1 }; " +
            string.Concat(Enumerable.Repeat("while items != null { ", depth)) +
            "copy := items; print(copy.length); s.observe()" + new string('}', depth);
        Success(source);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ImportedMethodsPreserveCurrentModuleNarrowingInLoopsAndHandlers()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "struct S { n: int }; impl S { fn observe(): int { return self.n } }; fn create(): S { return S { n: 1 } }");
        project.Write("main.rukbat", "import './helper.rukbat' as helper; items: int[]? = [1]; s := helper.create(); for n in [1] { print(items.length); s.observe() }; try { s.observe() } catch err { print(items.length) }; print(items.length)");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "PowerShell51")]
    [Trait("Category", "CrossVersion")]
    [Fact]
    public async Task ReachableNarrowingAndReboundLoopSymbolsExecuteOnBothHosts()
    {
        using var project = new Project();
        project.Write("main.rukbat", """
            items: int[]? = [1]
            struct S { n: int }
            impl S {
                fn observe(): int { return self.n }
            }
            s := S { n: 2 }
            for n in [1, 2] {
                if items == null { continue }
                copy := items
                print(s.observe() + n)
                print(copy.length)
            }
            items = [1]
            while items.length > 0 {
                items = []
            }
            print(items.length)
            while items.length == 0 {
                items = null
                break
            }
            print(items == null)
            fn first(items: int[]?): int {
                if items == null { return 0 }
                while items.length > 0 {
                    items = null
                    return 1
                }
                return 0
            }
            print(first([1]))
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        foreach (var result in new[] { await project.RunOutputWithWindowsPowerShell(compiled.Output!),
            await project.RunOutput(compiled.Output!) })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("3\n1\n4\n1\n0\nTrue\n1\n", result.Output);
            Assert.Equal("", result.Error);
        }
    }
}
