using System.Diagnostics;
using Auvil;
using PowerShellParser = System.Management.Automation.Language.Parser;

namespace Auvil.Tests;

public partial class CompilerTests
{
    private static CompilationResult Compile(string source) => new Compiler().Compile(source);
    private static string Success(string source)
    {
        var result = Compile(source);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        PowerShellParser.ParseInput(result.Output!, out _, out var errors);
        Assert.Empty(errors);
        return result.Output!;
    }

    [Theory]
    [InlineData("name: string = 'Taro'\nage: int = 30")]
    [InlineData("function add(a: int, b: int): int { return a + b }\nprint(add(2, 3))")]
    [InlineData("flag: bool = true && !false\nif flag { print('ok') } else { print('no') }")]
    [InlineData("function f(): void { print('ok'); return }")]
    [InlineData("function f(x: int): int { if x > 0 { return x } else { return -x } }")]
    [InlineData("x: int = -2147483648\ny: int = 2147483647")]
    [InlineData("function f(): string { return 'line\\nquote\\\" apostrophe\\\' slash\\\\' }")]
    [InlineData("// return fake(): int {\n# name: int = 'bad'\ns: string = '$x: int; $(throw); { return }'")]
    [InlineData("year: string = powershell<string>('Get-Date', Format: 'yyyy')")]
    [InlineData("function f(\n a: int,\n b: int\n): int { return a +\n b }\nprint(f(\n1,\n2\n))")]
    [InlineData("function f(): int { return 1 }\nfunction F(): int { return 2 }\nprint(f() + F())")]
    [InlineData("x: int = 1\nfunction f(x: string): string { return x }")]
    public void AcceptsModernSyntax(string source) => Success(source);

    [Theory]
    [InlineData("x: int = '30'")]
    [InlineData("x: string = 30")]
    [InlineData("x: bool = 1")]
    [InlineData("x: int = 1\nx = 'bad'")]
    [InlineData("x: int = 1\nX = 2")]
    [InlineData("x = 1")]
    [InlineData("x: int = missing")]
    [InlineData("x: int = x")]
    [InlineData("x: int = 1\nx: int = 2")]
    [InlineData("x: void = print('hello')")]
    [InlineData("x: int = true + 1")]
    [InlineData("x: int = 'hello' - 'world'")]
    [InlineData("x: bool = 1 == '1'")]
    [InlineData("x: bool = 1 && 2")]
    [InlineData("x: bool = !1")]
    [InlineData("x: int = -true")]
    [InlineData("if 1 { print('bad') }")]
    [InlineData("if true { x: int = 1 }\nprint(x)")]
    [InlineData("function f(): int { return 'bad' }")]
    [InlineData("function f(): int { return }")]
    [InlineData("function f(): int { 1 }")]
    [InlineData("function f(x: bool): int { if x { return 1 } }")]
    [InlineData("function f(): void { return 1 }")]
    [InlineData("function f(x: void): void {}")]
    [InlineData("function f(x: int, x: int): int { return x }")]
    [InlineData("function f(): int { return 1 }; function f(): int { return 2 }")]
    [InlineData("function f(x: int): int { return x }\nf('bad')")]
    [InlineData("function f(x: int): int { return x }\nf()")]
    [InlineData("function f(x: int): int { return x }\nf(1, 2)")]
    [InlineData("function f(x: int): int { return x }\nf(x: 1)")]
    [InlineData("return 1")]
    [InlineData("missing()")]
    [InlineData("missing.f()")]
    [InlineData("print()")]
    [InlineData("print(1, 2)")]
    [InlineData("print(print(1))")]
    [InlineData("powershell<int>()")]
    [InlineData("powershell<int>(1)")]
    [InlineData("powershell<int>('x', Name: 1, name: 2)")]
    [InlineData("powershell<int>('x', Name: 1, 2)")]
    [InlineData("powershell<int>('x', print(1))")]
    public void RejectsTypeAndBindingErrors(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, d => d.Code.StartsWith("AUV2"));
    }

    [Theory]
    [InlineData("$name: string = 'Taro'")]
    [InlineData("function f() { return 1 }")]
    [InlineData("function f(): int { return 1")]
    [InlineData("function f(): void { function g(): void {} }")]
    [InlineData("x: object = 'bad'")]
    [InlineData("x: int = 2147483648")]
    [InlineData("x: string = 'not closed")]
    [InlineData("x: string = 'bad\\q'")]
    [InlineData("x: int = 1 y: int = 2")]
    [InlineData("import '../outside'")]
    [InlineData("function print(): void {}")]
    public void RejectsUnsupportedSyntax(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.All(result.Diagnostics, d => { Assert.True(d.Line > 0); Assert.True(d.Column > 0); });
    }

    [Fact]
    public void ReportsSourcePosition()
    {
        var result = Compile("// comment\r\nx: int = 'bad'");
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(2, error.Line);
        Assert.Equal(10, error.Column);
        Assert.Equal("<input>", error.FilePath);
    }

    [Fact]
    public void ImportedErrorsReferToImportedFile()
    {
        using var project = new Project();
        project.Write("main.auv", "import math\nprint(math.add(1, 2))");
        project.Write("math.auv", "function add(a: int, b: int): int { return 'bad' }");
        var result = project.Compile();
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(project.PathFor("math.auv"), error.FilePath);
        Assert.Equal("AUV2001", error.Code);
    }

    [Fact]
    public void ChecksImportedFunctionSignatures()
    {
        using var project = new Project();
        project.Write("main.auv", "import math\nprint(math.add('bad', 2))");
        project.Write("math.auv", "function add(a: int, b: int): int { return a + b }");
        Assert.Contains(project.Compile().Diagnostics, d => d.Code == "AUV2001");
    }

    [Fact]
    public void RejectsCyclesMissingAndDuplicateImports()
    {
        using var project = new Project();
        project.Write("main.auv", "import missing");
        Assert.Contains(project.Compile().Diagnostics, d => d.Code == "AUV3002");
        project.Write("main.auv", "import other");
        project.Write("other.auv", "import main");
        Assert.Contains(project.Compile().Diagnostics, d => d.Code == "AUV3001");
        project.Write("other.auv", "function f(): int { return 1 }");
        project.Write("main.auv", "import other\nimport other");
        Assert.Contains(project.Compile().Diagnostics, d => d.Code == "AUV3000");
        project.Write("main.auv", "function f(): void { import other }");
        Assert.Contains(project.Compile().Diagnostics, d => d.Code == "AUV3000");
        Assert.Contains(Compile("import other").Diagnostics, d => d.Code == "AUV3000");
    }

    [Theory]
    [InlineData("function f(): int { 99; powershell<int>('Write-Output', 88); print('log'); return 7 }\nx: int = f(); print(x)", "log\n7")]
    [InlineData("function f(): void { 99; powershell<int>('Write-Output', 88) }; f(); print('done')", "done")]
    [InlineData("x: int = 1\nfunction f(): int { x: int = 2; return x }\nprint(f()); print(x)", "2\n1")]
    [InlineData("x: int = 1\nif true { x: int = 2; print(x) }; print(x)", "2\n1")]
    [InlineData("x: int = 1\nX: int = 2\nprint(x + X)", "3")]
    [InlineData("function f(): int { return 1 }; function F(): int { return 2 }; print(f() + F())", "3")]
    [InlineData("function f(n: int): int { if n <= 1 { return 1 } else { return n * f(n - 1) } }; print(f(5))", "120")]
    [InlineData("print(2 + 3 * 4); print(-7 / 2); print('A' == 'a'); print(true && !false)", "14\n-3\nFalse\nTrue")]
    [InlineData("print('$x; $(throw) `n'); print('it\\'s fine')", "$x; $(throw) `n\nit's fine")]
    [InlineData("print(powershell<string>('Write-Output', 'hello')); print(powershell<int>('Write-Output', 42)); print(powershell<bool>('Write-Output', false))", "hello\n42\nFalse")]
    [InlineData("powershell<void>('Write-Output', 'discard'); print('done')", "done")]
    [InlineData("print(powershell<string>('Get-Date', Year: 2000, Format: 'yyyy'))", "2000")]
    [InlineData("print(false && powershell<bool>('Missing-Auvil-Command')); print(true || powershell<bool>('Missing-Auvil-Command'))", "False\nTrue")]
    public async Task RunsModernPrograms(string source, string expected)
    {
        using var project = new Project();
        project.Write("main.auv", source);
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
    }

    [Theory]
    [InlineData("print(powershell<int>('Write-Output', '42'))", "incompatible with int")]
    [InlineData("print(powershell<string>('Write-Output', 42))", "incompatible with string")]
    [InlineData("print(powershell<int>('Write-Output', 1, 2))", "expected one int result")]
    [InlineData("print(powershell<string>('Write-Output', powershell<void>('Write-Output', 1)))", null)]
    [InlineData("print(powershell<int>('Write-Error', 'failed'))", "failed")]
    [InlineData("x: int = 2147483647 + 1; print(x)", "")]
    public async Task RejectsRuntimeBoundaryAndArithmeticErrors(string source, string? expected)
    {
        using var project = new Project(); project.Write("main.auv", source);
        if (expected is null) { Assert.False(project.Compile().Success); return; }
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expected, result.Error);
    }

    [Fact]
    public async Task CallsAnExistingPowerShellModule()
    {
        using var project = new Project();
        project.Write("native.psm1", "function Get-AuvilGreeting { param([string]$Name) return ('Hello, ' + $Name) }; Export-ModuleMember -Function Get-AuvilGreeting");
        var modulePath = project.PathFor("native.psm1").Replace("\\", "\\\\").Replace("'", "\\'");
        project.Write("main.auv", $"powershell<void>('Import-Module', '{modulePath}', Global: true)\nprint(powershell<string>('Get-AuvilGreeting', Name: 'Taro'))");
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("Hello, Taro", result.Output.TrimEnd('\n'));
    }

    [Fact]
    public async Task RejectsMissingPowerShellScalarResult()
    {
        using var project = new Project();
        project.Write("main.auv", "x: int = powershell<int>('Out-Null', 1)");
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        var message = string.Join(" ", result.Error.Split('\n').Select(line => line.Trim().TrimStart('|').Trim()));
        Assert.Contains("expected one int result, got 0", message);
        Assert.Contains("main.auv:1", result.Error);
    }

    [Fact]
    public async Task PersistsModuleStateAcrossCalls()
    {
        using var project = new Project();
        project.Write("counter.auv", "count: int = 0\nfunction next(): int { count = count + 1; return count }");
        project.Write("main.auv", "import counter\nprint(counter.next()); print(counter.next())");
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("1\n2", result.Output.TrimEnd('\n'));
    }

    [Fact]
    public async Task BundlesDiamondImportsOnceAndIsolatesModuleState()
    {
        using var project = new Project();
        project.Write("shared.auv", "print('init'); value: int = 10\nfunction get(): int { return value }");
        project.Write("left.auv", "import shared\nvalue: int = 20\nfunction get(): int { return shared.get() + value }");
        project.Write("right.auv", "import shared\nvalue: int = 30\nfunction get(): int { return shared.get() + value }");
        project.Write("main.auv", "import left\nimport right\nvalue: int = 100\nprint(left.get()); print(right.get()); print(value)");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        // Execute the bundle after removing every source module, from a different working directory.
        foreach (var file in Directory.EnumerateFiles(project.DirectoryPath, "*.auv")) File.Delete(file);
        var result = await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("init\n30\n40\n100", result.Output.TrimEnd('\n'));
    }

    [Fact]
    public async Task ModuleGlobalsAreLexicalRatherThanCallerVariables()
    {
        using var project = new Project();
        project.Write("main.auv", "x: int = 1\nfunction read(): int { return x }\nfunction caller(x: int): int { return read() }\nprint(caller(99))");
        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("1", result.Output.TrimEnd('\n'));
    }

    [Fact]
    public void CliPreservesExistingOutputOnFailure()
    {
        using var project = new Project(); var input = project.PathFor("main.auv");
        var output = project.PathFor("main.ps1");
        project.Write("main.auv", "x: int = 1");
        Assert.Equal(0, Cli.Run(["build", input], TextWriter.Null, TextWriter.Null));
        var previous = File.ReadAllText(output);
        project.Write("main.auv", "import missing");
        using var errors = new StringWriter();
        Assert.Equal(1, Cli.Run(["build", input], TextWriter.Null, errors));
        Assert.Contains("AUV3002", errors.ToString());
        Assert.Equal(previous, File.ReadAllText(output));
        File.Delete(output);
        Assert.Equal(1, Cli.Run(["build", input], TextWriter.Null, TextWriter.Null));
        Assert.False(File.Exists(output));
        Assert.Equal(2, Cli.Run([], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, Cli.Run(["build", output], TextWriter.Null, TextWriter.Null));
        Assert.Equal(2, Cli.Run(["build", project.PathFor("missing.auv")], TextWriter.Null, TextWriter.Null));
    }

    private sealed class Project : IDisposable
    {
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "auvil-tests-" + Guid.NewGuid());
        public Project() => Directory.CreateDirectory(DirectoryPath);
        public string PathFor(string file) => Path.Combine(DirectoryPath, file);
        public void Write(string file, string source) => File.WriteAllText(PathFor(file), source);
        public CompilationResult Compile() => new Compiler().CompileFile(PathFor("main.auv"));
        public Task<RunResult> Run()
        {
            var result = Compile(); Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            return RunOutput(result.Output!);
        }
        public async Task<RunResult> RunOutput(string output)
        {
            Write("main.ps1", output);
            var info = new ProcessStartInfo("pwsh")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                WorkingDirectory = Path.GetTempPath(), CreateNoWindow = true
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", PathFor("main.ps1") }) info.ArgumentList.Add(argument);
            using var process = Process.Start(info)!;
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("Generated PowerShell did not finish in 20 seconds."); }
            return new(process.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
        }
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
    private sealed record RunResult(int ExitCode, string Output, string Error);
}
