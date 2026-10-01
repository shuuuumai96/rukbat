using System.Text;
using System.Text.Json;

namespace Rukbat.Tests;

public class CsvTests : TestBase
{
    private static string RukbatLiteral(string value) => value
        .Replace("\\", "\\\\")
        .Replace("'", "\\'")
        .Replace("\r", "\\r")
        .Replace("\n", "\\n");

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task CsvParsesRecordsFieldsQuotesAndNewlines(string shell)
    {
        const string input = "name,value\r\ntemperature,23.4\r\n\r\n\"line one\r\nline two\",\"a,b\",\"d\"\"e\"\n日本語,,last";
        var expected = new[]
        {
            new[] { "name", "value" },
            new[] { "temperature", "23.4" },
            new[] { "" },
            new[] { "line one\r\nline two", "a,b", "d\"e" },
            new[] { "日本語", "", "last" }
        };
        using var project = new Project();
        project.Write("main.rukbat", $$"""
            import csv
            import json
            print(json.stringify(csv.parse('{{RukbatLiteral(input)}}')))
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal(expected, JsonSerializer.Deserialize<string[][]>(result.Output)!);
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("", "[]")]
    [InlineData("a,b", "[[\"a\",\"b\"]]")]
    [InlineData("a,b\n", "[[\"a\",\"b\"]]")]
    [InlineData("a,b\nc,d,e\nf", "[[\"a\",\"b\"],[\"c\",\"d\",\"e\"],[\"f\"]]")]
    [InlineData("a,\n,\nb", "[[\"a\",\"\"],[\"\",\"\"],[\"b\"]]")]
    public async Task CsvPreservesEmptyAndVariableWidthRecords(string input, string expected)
    {
        using var project = new Project();
        project.Write("main.rukbat", $$"""
            import csv
            import json
            print(json.stringify(csv.parse('{{RukbatLiteral(input)}}')))
            """);
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal(expected, result.Output.TrimEnd('\r', '\n'));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task CsvRejectsMalformedSyntaxWithoutReturningPartialRows(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", """
            import csv
            try { csv.parse('good,row\n"unclosed') } catch err { print('unclosed') }
            try { csv.parse('bad"quote') } catch err { print('unquoted') }
            try { csv.parse('"done"x') } catch err { print('after quote') }
            try { csv.parse('one\rtwo') } catch err { print('bare cr') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("unclosed\nunquoted\nafter quote\nbare cr\n", result.Output.Replace("\r\n", "\n"));
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task CsvRunsStandaloneOnWindowsPowerShell51AndKeepsNestedArraysByValue()
    {
        using var project = new Project();
        project.Write("source/main.rukbat", """
            import csv
            rows: string[][] = csv.parse('one,two')
            copy: string[][] = rows
            copy[0][0] = 'changed'
            print(rows[0][0])
            print(copy[0][0])
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("source/main.rukbat"));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("deploy/job.ps1");
        Directory.CreateDirectory(Path.GetDirectoryName(script)!);
        File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
        File.Delete(project.PathFor("source/main.rukbat"));
        var result = await RunDeployed("powershell", script, project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("one\nchanged\n", result.Output.Replace("\r\n", "\n"));
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("import csv\ncsv.parse()")]
    [InlineData("import csv\ncsv.parse(1)")]
    [InlineData("import csv\ncsv.parse<string>('x')")]
    public void CsvUsesItsTypedStandardLibraryContract(string source)
        => Assert.NotEmpty(new Compiler().Check(source));

    [Trait("Category", "Unit")]
    [Fact]
    public void CsvRuntimeIsEmittedOnlyForTheCsvModule()
    {
        Assert.Contains("'csv.parse'", Success("import csv\ncsv.parse('a,b')"));
        Assert.DoesNotContain("'csv.parse'", Success("import fs\nfs.is_file('x')"));
    }
}
