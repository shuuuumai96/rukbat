namespace Rukbat.Tests;

[Trait("Category", "Unit")]
public sealed class StandardBoundaryTests
{
    private const string Imports = """
        import encoding; import hash; import log; import datetime; import http
        import fs; import re; import json; import csv; import cli

        """;

    [Theory]
    [InlineData("encoding.Encoding")]
    [InlineData("hash.Algorithm")]
    [InlineData("log.Level")]
    [InlineData("datetime.Timestamp")]
    [InlineData("datetime.Date")]
    [InlineData("datetime.Duration")]
    [InlineData("datetime.Zone")]
    [InlineData("http.Method")]
    [InlineData("http.Body")]
    [InlineData("fs.Lines")]
    [InlineData("re.Pattern")]
    public void RejectsOpaqueScalarsInNestedExternalBoundaries(string type)
    {
        var diagnostics = new Compiler().Check(Imports + $$"""
            struct Wrapper { values: {{type}}?[] }
            fn transfer(value: Wrapper): void {
                json.stringify(value)
                powershell<void>('Write-Output', value)
                powershell<Wrapper>('Read-Host')
            }
            """);

        Assert.Equal(new[]
        {
            "Type Wrapper cannot be encoded as JSON.",
            "Use explicit standard-library conversions before passing opaque values to PowerShell.",
            "Structs cannot be passed directly to PowerShell. Pass primitive fields.",
            "PowerShell result types cannot contain re.Pattern, fs.Lines, json.Value, csv.Row, cli.Result, encoding.Encoding, hash.Algorithm, log.Level, datetime types, http.Method or http.Body. Read text and use the corresponding parser."
        }, diagnostics.Select(diagnostic => diagnostic.Message));
        Assert.All(diagnostics, diagnostic => Assert.Equal("RUK2010", diagnostic.Code));
    }

    [Theory]
    [InlineData("encoding.Encoding?", "A nullable value must be checked against null before it can be printed.")]
    [InlineData("encoding.Encoding[]", "Print individual struct fields or array elements rather than the composite value.")]
    [InlineData("log.Level?", "Use an explicit standard-library conversion before printing this value.")]
    [InlineData("re.Pattern[]", "Use an explicit standard-library conversion before printing this value.")]
    [InlineData("datetime.Timestamp[]", "Print individual struct fields or array elements rather than the composite value.")]
    [InlineData("http.Method?", "A nullable value must be checked against null before it can be printed.")]
    [InlineData("fs.Lines[]", "Print individual struct fields or array elements rather than the composite value.")]
    public void PreservesPrintDiagnosticPriority(string type, string message)
    {
        var diagnostics = new Compiler().Check(Imports + $$"""
            fn output(value: {{type}}): void { print(value); eprint(value) }
            """);

        Assert.Equal(new[] { message, message }, diagnostics.Select(diagnostic => diagnostic.Message));
    }

    [Fact]
    public void AllowsJsonValuesOnlyAtJsonBoundaries()
    {
        var diagnostics = new Compiler().Check(Imports + """
            struct Wrapper { values: dict[string, json.Value?[]] }
            fn transfer(value: Wrapper): void {
                json.stringify(value)
                json.parse<Wrapper>('{}')
                powershell<Wrapper>('Read-Host')
            }
            """);

        Assert.Equal(2, diagnostics.Count);
        Assert.All(diagnostics, diagnostic => Assert.Equal("RUK2010", diagnostic.Code));
        Assert.StartsWith("Dictionaries cannot be returned directly from PowerShell.", diagnostics[0].Message);
        Assert.StartsWith("PowerShell result types cannot contain", diagnostics[1].Message);
    }

    [Theory]
    [InlineData("csv.Row")]
    [InlineData("cli.Result")]
    public void RejectsOpaqueStructsAtJsonAndPowerShellBoundaries(string type)
    {
        var diagnostics = new Compiler().Check(Imports + $$"""
            struct Wrapper { values: {{type}}?[] }
            fn transfer(value: Wrapper): void {
                json.stringify(value)
                powershell<void>('Write-Output', value)
                powershell<Wrapper>('Read-Host')
            }
            """);

        Assert.Equal(3, diagnostics.Count);
        Assert.StartsWith("Type Wrapper cannot be encoded as JSON.", diagnostics[0].Message);
        Assert.StartsWith("Structs cannot be passed directly to PowerShell.", diagnostics[1].Message);
        Assert.StartsWith("PowerShell result types cannot contain", diagnostics[2].Message);
    }

    [Fact]
    public void AllowsOrdinaryStructsWithStandardTypeNames()
    {
        Assert.Empty(new Compiler().Check(Imports + """
            struct Encoding { value: string }
            struct Level { value: string }
            struct Pattern { value: string }
            struct Row { value: string }
            struct Result { value: string }
            struct Wrapper { encoding: Encoding; level: Level; pattern: Pattern; row: Row; result: Result }
            fn transfer(value: Wrapper): void {
                json.stringify(value)
                json.parse<Wrapper>('{}')
                powershell<Wrapper>('Read-Host')
            }
            """));
    }
}
