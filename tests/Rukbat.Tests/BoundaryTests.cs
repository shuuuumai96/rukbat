namespace Rukbat.Tests;

public class PowerShellBoundaryTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("xs: int[] = powershell<int[]>('Write-Output', [1,2]); print(xs.length); for x in xs { print(x) }", "2\n1\n2")]
    [InlineData("xs: int[] = powershell<int[]>('Out-Null', 1); print(xs.length)", "0")]
    [InlineData("xs: int[] = powershell<int[]>('Write-Output', 7); print(xs.length); print(xs[0])", "1\n7")]
    [InlineData("xs: int[] = powershell<int[]>('ConvertFrom-Json', '[1,2]'); print(xs[0]); print(xs[1])", "1\n2")]
    [InlineData("xs: int[] = powershell<int[]>('ConvertFrom-Json', '[]'); print(xs.length)", "0")]
    [InlineData("struct U { name: string; age: int }; u: U = powershell<U>('ConvertFrom-Json', '{\"name\":\"Taro\",\"age\":30,\"extra\":true}'); print(u.name); print(u.age)", "Taro\n30")]
    [InlineData("struct U { name: string; age: int; fn grow(): void { self.age = self.age + 1 } }; xs: U[] = powershell<U[]>('ConvertFrom-Json', '[{\"name\":\"Taro\",\"age\":30}]'); xs[0].grow(); print(xs.length); print(xs[0].age)", "1\n31")]
    [InlineData("struct U { age: int }; struct B { users: U[]; flags: bool[] }; b: B = powershell<B>('ConvertFrom-Json', '{\"users\":[{\"age\":30},{\"age\":40}],\"flags\":[]}'); print(b.users[1].age); print(b.flags.length)", "40\n0")]
    [InlineData("struct U { name: string }; u: U = powershell<U>('ConvertFrom-Json', '{\"name\":\"Taro\"}', AsHashtable: true); print(u.name)", "Taro")]
    [InlineData("struct U { Age: int; age: int }; u: U = powershell<U>('ConvertFrom-Json', '{\"Age\":1,\"age\":2}', AsHashtable: true); print(u.Age); print(u.age)", "1\n2")]
    [InlineData("xs: int[][] = powershell<int[][]>('ConvertFrom-Json', '[[1],[],[2,3]]'); print(xs.length); print(xs[0][0]); print(xs[1].length); print(xs[2][1])", "3\n1\n0\n3")]
    [InlineData("struct U { age: int }; xs: U[] = powershell<U[]>('ConvertFrom-Json', '[]'); print(xs.length)", "0")]
    public async Task ImportsTypedPowerShellData(string source, string expected)
    {
        using var project = new Project(); project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("struct U { age: int }; u: U = powershell<U>('ConvertFrom-Json', '{}')", "Missing required field")]
    [InlineData("struct U { age: int }; u: U = powershell<U>('ConvertFrom-Json', '{\"age\":null}')", "Null is incompatible")]
    [InlineData("struct U { age: int }; u: U = powershell<U>('ConvertFrom-Json', '{\"age\":\"30\"}')", "incompatible with int")]
    [InlineData("struct U { age: int }; u: U = powershell<U>('ConvertFrom-Json', '{\"Age\":30}')", "Missing required field")]
    [InlineData("struct U { age: int }; u: U = powershell<U>('ConvertFrom-Json', '{\"age\":9223372036854775808}')", "incompatible with int")]
    [InlineData("struct U { age: int }; u: U = powershell<U>('ConvertFrom-Json', '{\"age\":1.5}')", "incompatible with int")]
    [InlineData("struct U { age: int }; xs: U[] = powershell<U[]>('ConvertFrom-Json', '[{\"age\":1},{\"age\":\"bad\"}]')", "result[1].age")]
    [InlineData("struct U { xs: int[] }; u: U = powershell<U>('ConvertFrom-Json', '{\"xs\":1}')", "expected an array")]
    [InlineData("xs: int[] = powershell<int[]>('Write-Output', 1, 'bad')", "result[1]")]
    [InlineData("struct U {}; u: U = powershell<U>('Write-Output', 1)", "expected an object")]
    [InlineData("struct U {}; u: U = powershell<U>('Out-Null', 1)", "expected one U result")]
    [InlineData("struct U { age: int }; u: U = powershell<U>('ConvertFrom-Json', '[{\"age\":1},{\"age\":2}]')", "expected one U result")]
    public async Task RejectsInvalidExternalData(string source, string message)
    {
        using var project = new Project(); project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        // PowerShell may wrap an error across terminal display lines.
        var error = string.Join(" ", result.Error.Split('\n').Select(line => line.Trim().TrimStart('|').Trim()));
        Assert.Contains(message, error);
        Assert.Contains("main.rukbat:1", result.Error);
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task KeepsPowerShellArrayArgumentsSeparateAndCopied()
    {
        using var project = new Project();
        project.Write("native.psm1", "function Test-RukbatArrays { param([object[]]$First, [object[]]$Second) $First[0] = 9; return ($First.Count * 10 + $Second.Count) }; Export-ModuleMember -Function Test-RukbatArrays");
        var path = project.PathFor("native.psm1").Replace("\\", "\\\\").Replace("'", "\\'");
        project.Write("main.rukbat", $"powershell<void>('Import-Module', '{path}', Global: true); xs: int[] = [1,2]; print(powershell<int>('Test-RukbatArrays', xs, [3])); print(xs[0])");
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("21\n1", result.Output.TrimEnd('\n'));
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task ImportsNativeFileObjectsIntoAnImportedStruct()
    {
        using var project = new Project();
        project.Write("model.rukbat", "struct File { Name: string; PSIsContainer: bool }");
        project.Write("data.txt", "test");
        var path = project.PathFor("data.txt").Replace("\\", "\\\\").Replace("'", "\\'");
        project.Write("main.rukbat", $"import './model.rukbat' as model\nfiles: model.File[] = powershell<model.File[]>('Get-Item', LiteralPath: '{path}'); for file in files {{ print(file.Name); print(file.PSIsContainer) }}");
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("data.txt\nFalse", result.Output.TrimEnd('\n'));
    }
}
