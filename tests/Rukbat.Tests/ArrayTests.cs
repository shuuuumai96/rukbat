namespace Rukbat.Tests;

public class ArrayTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("xs: int[] = []; print(xs.length)", "0")]
    [InlineData("xs: int[] = [42]; print(xs.length); print(xs[0])", "1\n42")]
    [InlineData("xs: int[] = [1, 2, 3,]; xs[1] = 9; print(xs[1]); print(xs.length)", "9\n3")]
    [InlineData("xs: string[] = ['a', 'b']; for x in xs { print(x) }", "a\nb")]
    [InlineData("xs: bool[] = [true, false]; for x in xs { print(x) }", "True\nFalse")]
    [InlineData("xs: int[][] = [[], [1], [2, 3]]; print(xs.length); print(xs[0].length); print(xs[1][0]); xs[2][1] = 4; print(xs[2][1])", "3\n0\n1\n4")]
    [InlineData("a: int[][] = [[1]]; b: int[][] = a; b[0][0] = 9; print(a[0][0]); print(b[0][0])", "1\n9")]
    [InlineData("fn copy(xs: int[]): int[] { xs[0] = 9; return xs }; a: int[] = [1]; b: int[] = copy(a); print(a[0]); print(b[0])", "1\n9")]
    [InlineData("fn empty(): int[] { return [] }; fn count(xs: int[]): int { return xs.length }; print(empty().length); print(count([]))", "0\n0")]
    [InlineData("fn one(): int[] { return [7] }; xs: int[] = one(); print(xs.length); print(xs[0])", "1\n7")]
    [InlineData("struct Box { xs: int[] }; a: Box = Box {xs: []}; b: Box = Box {xs: [1]}; a.xs = b.xs; b.xs[0] = 3; print(a.xs[0])", "1")]
    [InlineData("struct C { n: int }; impl C { fn grow(): void { self.n = self.n + 1 } }; xs: C[] = [C {n: 1}]; ys: C[] = xs; ys[0].grow(); print(xs[0].n); print(ys[0].n); ys[0].n = 8; print(ys[0].n)", "1\n2\n8")]
    [InlineData("struct C { xs: int[] }; impl C { fn grow(): void { self.xs[0] = self.xs[0] + 1 } }; a: C = C {xs: [2]}; b: C = a; b.grow(); print(a.xs[0]); print(b.xs[0])", "2\n3")]
    [InlineData("xs: int[] = [1,2,3,4]; sum: int = 0; for x in xs { if x == 2 { continue }; if x == 4 { break }; sum = sum + x }; print(sum)", "4")]
    [InlineData("for a in [1,2] { for b in [3,4] { print(a + b); break } }", "4\n5")]
    [InlineData("xs: int[] = []; for x in xs { print(x) }; print('done')", "done")]
    [InlineData("x: int = 9; for x in [1,2] { x = x + 10; print(x) }; print(x)", "11\n12\n9")]
    [InlineData("xs: int[] = [1,2]; for x in xs { xs[1] = 8; print(x) }; print(xs[1])", "1\n2\n8")]
    [InlineData("struct C { n: int }; impl C { fn grow(): void { self.n = self.n + 1 } }; xs: C[] = [C {n: 1}]; for c in xs { c.grow(); print(c.n) }; print(xs[0].n)", "2\n1")]
    [InlineData("fn first(xs: int[]): int { for x in xs { return x }; return 0 }; print(first([7,8])); print(first([]))", "7\n0")]
    [InlineData("calls: int = 0; fn values(): int[] { calls = calls + 1; return [1,2] }; for x in values() { print(x) }; print(calls)", "1\n2\n1")]
    [InlineData("fn echo(xs: int[][]): int[][] { return xs }; xs: int[][] = echo([[1], []]); for row in xs { print(row.length) }", "1\n0")]
    [InlineData("struct C { xs: int[] }; impl C { fn set(xs: int[]): int[] { self.xs = xs; return self.xs } }; c: C = C {xs: []}; a: int[] = [3]; b: int[] = c.set(a); b[0] = 4; a[0] = 5; print(c.xs[0]); print(b[0])", "3\n4")]
    [InlineData("xs: int[] = [1]; xs = []; print(xs.length)", "0")]
    [InlineData("xs: int[][] = [[1]]; xs[0] = []; print(xs[0].length)", "0")]
    [InlineData("calls: int = 0; fn index(): int { calls = calls + 1; return 0 }; xs: int[][] = [[1]]; xs[index()][index()] = 7; print(calls); print(xs[0][0])", "2\n7")]
    public async Task ExecutesTypedArraysAndLoops(string source, string expected)
    {
        using var project = new Project(); project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("xs: int[] = [1, 'bad']", "array element")]
    [InlineData("xs: int[] = [true]", "Expected int")]
    [InlineData("xs: void[] = []", "cannot have type void")]
    [InlineData("xs: Missing[] = []", "Unknown struct/type")]
    [InlineData("xs: int[] = 1", "Expected int[]")]
    [InlineData("xs: int[] = [1]; xs[0] = 'bad'", "Expected int")]
    [InlineData("xs: int[] = [1]; print(xs[true])", "array index")]
    [InlineData("x: int = 1; print(x[0])", "Cannot index")]
    [InlineData("for x in 1 { print(x) }", "requires an array")]
    [InlineData("for x in [1] {}; print(x)", "Unknown variable")]
    [InlineData("break", "only inside a loop")]
    [InlineData("if true { continue }", "only inside a loop")]
    [InlineData("fn f(): int { for x in [1] { return x } }", "every path")]
    [InlineData("xs: int[] = [1]; xs.length = 2", "read-only")]
    [InlineData("fn f(): int[] { return [1] }; f()[0] = 2", "temporary")]
    [InlineData("xs: int[] = [1]; print(xs)", "array elements")]
    [InlineData("same: bool = [1] == [1]", "cannot combine")]
    [InlineData("for x in [] {}", "empty array requires")]
    [InlineData("fn f(xs: string[]): void {}; f([1])", "Expected string")]
    [InlineData("struct A { n: int }; struct B { n: int }; xs: A[] = [B {n: 1}]", "Expected A")]
    [InlineData("struct Node { children: Node[] }", "Recursive value field")]
    [InlineData("struct U {}; powershell<void>('Write-Output', [U {}])", "cannot be passed directly")]
    public void RejectsInvalidArraysAndLoops(string source, string message)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, d => d.Message.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("xs: int[] = [1]; print(xs[-1])")]
    [InlineData("xs: int[] = []; print(xs[0])")]
    [InlineData("xs: int[] = [1]; xs[1] = 2")]
    [InlineData("xs: int[] = [1]; xs[-1] = 2")]
    public async Task RejectsArrayBoundsAtRuntime(string source)
    {
        using var project = new Project(); project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("Array index", result.Error);
        Assert.Contains("main.rukbat:1", result.Error);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ArrayElementIdentityIncludesTheDefiningModule()
    {
        using var project = new Project();
        project.Write("left.rukbat", "struct U { n: int }");
        project.Write("right.rukbat", "struct U { n: int }");
        project.Write("main.rukbat", "import './left.rukbat' as left\nimport './right.rukbat' as right\na: left.U[] = []; b: right.U[] = a");
        Assert.Contains(project.Compile().Diagnostics,
            d => d.Code == "RUK2001" && d.Message.Contains("left.U[]") && d.Message.Contains("right.U[]"));
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task PreservesArrayValuesAcrossModules()
    {
        using var project = new Project();
        project.Write("model.rukbat", "struct C { n: int }; impl C { fn grow(): void { self.n = self.n + 1 } }; fn copy(xs: C[]): C[] { return xs }; fn empty(): C[] { return [] }; fn nested(xs: int[][]): int[][] { return xs }");
        project.Write("main.rukbat", "import './model.rukbat' as model\na: model.C[] = [model.C {n: 1}]; b: model.C[] = model.copy(a); b[0].grow(); print(a[0].n); print(b[0].n); print(model.empty().length); xs: int[][] = model.nested([[], [5]]); print(xs.length); print(xs[0].length); print(xs[1][0])");
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("1\n2\n0\n2\n0\n5", result.Output.TrimEnd('\n'));
    }
}
