using System.Management.Automation.Language;
using Auvil;

namespace Auvil.Tests;

public class CompilerTests
{
    private static CompilationResult Compile(string source) => new Compiler().Compile(source);
    private static string Success(string source)
    {
        var result = Compile(source);
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        Parser.ParseInput(result.Output!, out _, out var errors);
        Assert.Empty(errors);
        return result.Output!;
    }

    [Fact]
    public void ConvertsRequestedExample()
    {
        var output = Success("""
            $name: string = "Taro"
            $age: int = 30
            function Add($a: int, $b: int): int {
                return $a + $b
            }
            Add 2 3
            """);
        Assert.Contains("[string]$name", output);
        Assert.Contains("[int]$age", output);
        Assert.Contains("[OutputType([int])]", output);
        Assert.Contains("param([int]$a, [int]$b)", output);
    }

    [Theory]
    [InlineData("$x: int = 'bad'")]
    [InlineData("$x: string = 30")]
    [InlineData("$x: bool = 30")]
    [InlineData("$x: int = 1\n$x = 'bad'")]
    [InlineData("$x: int = 1\n$X = 'bad'")]
    [InlineData("function F($x: int = 'bad') { }")]
    [InlineData("function F($x: int): string { return $x }")]
    [InlineData("function F(): int { return 'bad' }")]
    [InlineData("function F(): int { if ($true) { return 'bad' } }")]
    [InlineData("function F(): int { return }")]
    [InlineData("function F($x: int) {}\nF 'bad'")]
    [InlineData("function F($x: int) {}\nF -x 'bad'")]
    [InlineData("function F($x: int) {}\nF -x:'bad'")]
    [InlineData("$x: int = 1\n$x: string = 'bad'")]
    public void RejectsKnownTypeErrors(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, d => d.Code.StartsWith("AUV2"));
    }

    [Theory]
    [InlineData("$x: bool = $true")]
    [InlineData("$x: int = -3")]
    [InlineData("$x: string = ('a' + 'b')")]
    [InlineData("function F($x: int = 2): int { return ($x + 1) }")]
    [InlineData("function F(): string { function G(): int { return 1 }; return 'ok' }")]
    [InlineData("function F($x: int): int { return $x }; function G($x: string): string { return $x }")]
    [InlineData("function F(): int { $b = { return 'separate scope' }; return 1 }")]
    [InlineData("$untyped = 'no inference'\n$x: int = $untyped")]
    [InlineData("$x: int = 1\nfunction F($x) { $x = 'local' }")]
    [InlineData("$x: int = 1\nfunction F() { $x = 'local' }")]
    [InlineData("$x: int = 1\n$x = Read-Host")]
    [InlineData("function F($x: int, $y: string) {}\nF -y 'ok' 1")]
    public void AcceptsSupportedOrUnknownExpressions(string source) => Success(source);

    [Theory]
    [InlineData("# $x: int = 'bad'\nWrite-Output '$x: int = 2'")]
    [InlineData("$env:PATH\n$script:value = 1\n${name} = 'ok'")]
    [InlineData("<# $x: int = 'bad' #>\nGet-Process | Where-Object { $_.Id -gt 1 }")]
    [InlineData("$s = @'\n$x: int = 'not code'\n'@")]
    [InlineData("$s = @\"\ntext $env:PATH\n\"@")]
    [InlineData("function F([int]$x) { return $x }; F 1")]
    [InlineData("param([string]$Name)\nWrite-Output $Name")]
    public void PreservesOrdinaryPowerShellExactly(string source) => Assert.Equal(source, Success(source));

    [Theory]
    [InlineData("$x: missing = 1")]
    [InlineData("$x: int =")]
    [InlineData("function F($a: int): int { return 1")]
    [InlineData("$x: int")]
    public void RejectsMalformedOrUnsupportedSyntax(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.All(result.Diagnostics, d => { Assert.True(d.Line > 0); Assert.True(d.Column > 0); });
    }

    [Fact]
    public void ReportsOriginalLineAndColumn()
    {
        var result = Compile("# comment\n$x: int = 'bad'");
        var error = Assert.Single(result.Diagnostics);
        Assert.Equal(2, error.Line);
        Assert.Equal(11, error.Column);
    }

    [Fact]
    public void CliWritesOnlyAfterSuccessfulCompilation()
    {
        var directory = Path.Combine(Path.GetTempPath(), "auvil-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        try
        {
            var input = Path.Combine(directory, "example.auv");
            var output = Path.ChangeExtension(input, ".ps1");
            File.WriteAllText(input, "$x: int = 1");
            Assert.Equal(0, Cli.Run(["build", input], TextWriter.Null, TextWriter.Null));
            Assert.Contains("[int]$x", File.ReadAllText(output));
            var previous = File.ReadAllText(output);
            File.WriteAllText(input, "$x: int = 'bad'");
            Assert.Equal(1, Cli.Run(["build", input], TextWriter.Null, TextWriter.Null));
            Assert.Equal(previous, File.ReadAllText(output));
            File.Delete(output);
            Assert.Equal(1, Cli.Run(["build", input], TextWriter.Null, TextWriter.Null));
            Assert.False(File.Exists(output));
            Assert.Equal(2, Cli.Run([], TextWriter.Null, TextWriter.Null));
            Assert.Equal(2, Cli.Run(["build", output], TextWriter.Null, TextWriter.Null));
            Assert.Equal(2, Cli.Run(["build", Path.Combine(directory, "missing.auv")], TextWriter.Null, TextWriter.Null));
        }
        finally { Directory.Delete(directory, true); }
    }
}
