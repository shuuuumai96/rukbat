using System.Text;
using System.Text.Json;

namespace Rukbat.Tests;

public class TextTests : TestBase
{
    private static string Literal(string value) => "'" + value.Replace("\\", "\\\\")
        .Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t") + "'";

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task TextTrimHandlesLongWhitespaceWithoutRevisitingTheMiddle(string shell)
    {
        using var project = new Project();
        var spaces = new string(' ', 100_000);
        project.Write("main.rukbat", $$"""
            import text
            middle := 'x{{spaces}}y'
            print(text.trim(middle) == middle)
            print(text.trim('{{spaces}}x'))
            print(text.trim('x{{spaces}}'))
            print(text.trim('{{spaces}}') == '')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));

        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal("True\nx\nx\nTrue\n", result.Output.Replace("\r\n", "\n"));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", "en-US")]
    [InlineData("powershell", "tr-TR")]
    [InlineData("pwsh", "en-US")]
    [InlineData("pwsh", "tr-TR")]
    public async Task TextOperationsHaveLiteralCultureIndependentContracts(string shell, string culture)
    {
        var cases = new List<(string Expression, object Expected)>
        {
            ("text.trim('  東京  ')", "東京"),
            ("text.trim(' a  b ')", "a  b"),
            ("text.trim('')", ""),
            ("text.trim('plain')", "plain"),
            ("text.contains('接続エラー', 'エラー')", true),
            ("text.contains('ERROR', 'error')", false),
            ("text.contains('file.txt', '.')", true),
            ("text.contains('file.txt', '*')", false),
            ("text.contains('FILE', 'ı')", false),
            ("text.contains('café', 'café')", false),
            ("text.contains('', '')", true),
            ("text.contains('', 'x')", false),
            ("text.starts_with('report.csv', 'report')", true),
            ("text.starts_with('xreport.csv', 'report')", false),
            ("text.starts_with('Report.csv', 'report')", false),
            ("text.starts_with('Istanbul', 'ı')", false),
            ("text.starts_with('abc', '')", true),
            ("text.starts_with('', '')", true),
            ("text.starts_with('', 'x')", false),
            ("text.ends_with('report.csv', '.csv')", true),
            ("text.ends_with('report.csv.bak', '.csv')", false),
            ("text.ends_with('report.CSV', '.csv')", false),
            ("text.ends_with('fileI', 'ı')", false),
            ("text.ends_with('abc', '')", true),
            ("text.ends_with('', '')", true),
            ("text.ends_with('', 'x')", false),
            ("text.replace_all('a-b-c', '-', '_')", "a_b_c"),
            ("text.replace_all('aaa', 'aa', 'b')", "ba"),
            ("text.replace_all('a', 'a', 'aa')", "aa"),
            ("text.replace_all('a.b.*', '.', '$1')", "a$1b$1*"),
            ("text.replace_all('aAa', 'a', '')", "A"),
            ("text.replace_all('abc', 'z', 'x')", "abc"),
            ("text.replace_all('', 'x', 'y')", ""),
            ("text.replace_all('東京😀東京', '東京', '大阪')", "大阪😀大阪"),
            ("text.split('赤,青,緑', ',')", new[] { "赤", "青", "緑" }),
            ("text.split('a,,b,', ',')", new[] { "a", "", "b", "" }),
            ("text.split(',a', ',')", new[] { "", "a" }),
            ("text.split('', ',')", new[] { "" }),
            ("text.split('abc', ',')", new[] { "abc" }),
            ("text.split(' a , b ', ',')", new[] { " a ", " b " }),
            ("text.split('a.*b.*', '.*')", new[] { "a", "b", "" }),
            ("text.split('aaa', 'aa')", new[] { "", "a" }),
            ("text.split('aXbxb', 'x')", new[] { "aXb", "b" }),
            ("text.split('東京😀大阪', '😀')", new[] { "東京", "大阪" }),
            ("text.split_lines('')", Array.Empty<string>()),
            ("text.split_lines('one')", new[] { "one" }),
            ("text.split_lines('one\\r\\ntwo\\nthree\\rfour')", new[] { "one", "two", "three", "four" }),
            ("text.split_lines('one\\n')", new[] { "one" }),
            ("text.split_lines('one\\r\\n')", new[] { "one" }),
            ("text.split_lines('one\\r')", new[] { "one" }),
            ("text.split_lines('\\n')", new[] { "" }),
            ("text.split_lines('\\r\\n')", new[] { "" }),
            ("text.split_lines('\\r')", new[] { "" }),
            ("text.split_lines('\\n\\n')", new[] { "", "" }),
            ("text.split_lines('one\\r\\n\\r\\n')", new[] { "one", "" }),
            ("text.split_lines('\\n a \\n\\nb')", new[] { "", " a ", "", "b" }),
            ("text.split_lines('a\u2028b')", new[] { "a\u2028b" }),
            ("text.join(['赤', '青'], ',')", "赤,青"),
            ("text.join([], ',')", ""),
            ("text.join(['one'], ',')", "one"),
            ("text.join(['', 'a', ''], ',')", ",a,"),
            ("text.join(['a', 'b'], '')", "ab"),
            ("text.join(['a', 'b'], '.*')", "a.*b"),
            ("text.join(text.split(' a,,b,', ','), ',')", " a,,b,")
        };
        // Pin every member of the specified whitespace set, plus commonly confused nonmembers.
        const string whitespace = "\u0009\u000A\u000B\u000C\u000D\u0020\u0085\u00A0\u1680\u2000\u2001\u2002\u2003\u2004\u2005\u2006\u2007\u2008\u2009\u200A\u2028\u2029\u202F\u205F\u3000";
        foreach (var character in whitespace)
            cases.Add(($"text.trim({Literal(character + "x" + character)})", "x"));
        cases.Add(($"text.trim({Literal(whitespace)})", ""));
        foreach (var character in "\u180E\u200B\uFEFF")
        {
            var value = character + "x" + character;
            cases.Add(($"text.trim({Literal(value)})", value));
        }
        using var project = new Project();
        var source = "import text\nimport json\n" + string.Join("\n", cases.Select(c => $"print(json.stringify({c.Expression}))"));
        var output = Success(source);
        var script = project.PathFor("job.ps1");
        File.WriteAllText(script,
            $"[System.Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('{culture}')\n" + output,
            new UTF8Encoding(true));
        var result = await RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        var lines = result.Output.TrimEnd('\n').Split('\n');
        Assert.Equal(cases.Count, lines.Length);
        for (var index = 0; index < cases.Count; index++)
        {
            var expected = cases[index].Expected;
            var actual = JsonSerializer.Deserialize(lines[index], expected.GetType());
            if (expected is string[] array) Assert.Equal(array, Assert.IsType<string[]>(actual));
            else Assert.Equal(expected, actual);
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task EmptySearchAndSeparatorAreCatchableErrorsWithSourceLocations(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", """
            import text
            try { text.split('abc', '') } catch err { print(err.message) }
            try { text.replace_all('', '', 'x') } catch err { print(err.message) }
            text.split('', '')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("text.split: separator must not be empty.\ntext.replace_all: search must not be empty.\n", result.Output);
        Assert.Equal("main.rukbat:4: error: text.split: separator must not be empty.", result.Error.Trim());
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("text.trim()")]
    [InlineData("text.trim(1)")]
    [InlineData("text.trim(null)")]
    [InlineData("text.trim('x', 'y')")]
    [InlineData("text.trim(value: 'x')")]
    [InlineData("text.trim<string>('x')")]
    [InlineData("fn clean(value: string?): string { return text.trim(value) }")]
    [InlineData("text.contains('x')")]
    [InlineData("text.contains('x', true)")]
    [InlineData("text.starts_with([], 'x')")]
    [InlineData("text.ends_with('x', null)")]
    [InlineData("text.replace_all('x', 'y')")]
    [InlineData("text.replace_all('x', 'y', 1)")]
    [InlineData("text.split('x', ', ', true)")]
    [InlineData("text.split_lines(['x'])")]
    [InlineData("text.join('x', ',')")]
    [InlineData("text.join([1], ',')")]
    [InlineData("values: string?[] = ['x']; text.join(values, ',')")]
    [InlineData("text.join([], 1)")]
    [InlineData("value: int = text.trim('x')")]
    [InlineData("value: string = text.contains('x', 'x')")]
    [InlineData("value: string = text.split('x', ',')")]
    [InlineData("text.replace('x', 'x', 'y')")]
    public void TextRejectsInvalidCalls(string statement)
        => Assert.NotEmpty(new Compiler().Check("import text\n" + statement));

    [Trait("Category", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Trait("Smoke", "PowerShell51")]
    [Fact]
    public async Task TextValuesKeepCopyAndEvaluationOrderSemanticsOnWindowsPowerShell51()
    {
        using var project = new Project();
        var output = Success("""
            import text
            trace := ''
            fn value(marker: string, content: string): string {
                trace = trace + marker
                return content
            }
            original := 'a-b-a'
            changed := text.replace_all(value('v', original), value('s', 'a'), value('r', 'x'))
            parts := text.split(original, '-')
            copy := parts
            copy[0] = 'changed'
            copy.append('extra')
            print(trace)
            print(original)
            print(changed)
            print(text.join(parts, ','))
            print(text.join(copy, ','))
            lines := text.split_lines('')
            lines.append('first')
            print(lines[0])
            """);
        var result = await project.RunOutputWithWindowsPowerShell(output);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        Assert.Equal("vsr\na-b-a\nx-b-x\na,b,a\nchanged,b,a,extra\nfirst\n", result.Output);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task LogFilterBuildsAndRunsStandaloneWithUtf8AndMixedLineEndings(string shell)
    {
        using var project = new Project();
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples", "filter-errors.rukbat");
        var example = File.ReadAllText(fixture);
        Assert.DoesNotContain("powershell<", example);
        project.Write("source/main.rukbat", example);
        var source = project.PathFor("source/main.rukbat");
        var build = await RunCli(project, ["build", source]);
        Assert.True(build.ExitCode == 0, build.Error);
        var bytes = File.ReadAllBytes(project.PathFor("source/main.ps1"));
        Assert.True(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var output = "\uFEFF" + File.ReadAllText(project.PathFor("source/main.ps1"));
        var inputPath = project.PathFor("入力 [log].txt");
        var outputPath = project.PathFor("結果 [log].txt");
        File.WriteAllText(inputPath, " INFO: start\r\n\tERROR: 接続失敗　\n\nerror: ignored\r　ERROR: 保存失敗 \r\n");
        var cli = await RunCli(project, ["run", source, "--", inputPath, outputPath]);
        Assert.True(cli.ExitCode == 0, cli.Error);
        Assert.Equal("", cli.Output);
        Assert.Equal("ERROR: 接続失敗\r\nERROR: 保存失敗", File.ReadAllText(outputPath));
        File.Delete(source);
        Task<RunResult> RunExample(params string[] arguments) => shell == "powershell"
            ? project.RunOutputWithWindowsPowerShell(output, arguments)
            : project.RunOutput(output, arguments);

        foreach (var (input, expected) in new[]
        {
            (" INFO: start\r\n\tERROR: 接続失敗　\n\nerror: ignored\r　ERROR: 保存失敗 \r\n", "ERROR: 接続失敗\r\nERROR: 保存失敗"),
            ("ERROR: one\n", "ERROR: one"),
            ("INFO: ok\n", ""),
            ("", "")
        })
        {
            File.WriteAllText(inputPath, input);
            File.WriteAllText(outputPath, "stale");
            var result = await RunExample(inputPath, outputPath);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Output);
            Assert.Equal("", result.Error);
            Assert.Equal(Encoding.UTF8.GetBytes(expected), File.ReadAllBytes(outputPath));
        }

        File.WriteAllText(outputPath, "keep");
        var usage = await RunExample();
        Assert.Equal(2, usage.ExitCode);
        Assert.Equal("", usage.Output);
        Assert.Contains("Usage:", usage.Error);
        var missingInput = await RunExample(project.PathFor("missing.txt"), outputPath);
        Assert.Equal(1, missingInput.ExitCode);
        Assert.Equal("", missingInput.Output);
        Assert.NotEmpty(missingInput.Error);
        Assert.Equal("keep", File.ReadAllText(outputPath));
        var failedSave = await RunExample(inputPath, project.PathFor("missing/output.txt"));
        Assert.Equal(1, failedSave.ExitCode);
        Assert.Equal("", failedSave.Output);
        Assert.NotEmpty(failedSave.Error);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void TextTypesInferenceNarrowingAndFormattingUseExistingRules()
    {
        const string source = """
            import text
            value: string? = ' x '
            if value != null {
                cleaned := text.trim(value)
                found: bool = text.contains(cleaned, 'x')
                first: bool = text.starts_with(cleaned, 'x')
                last: bool = text.ends_with(cleaned, 'x')
                changed: string = text.replace_all(cleaned, 'x', 'y')
                parts := text.split(changed, ',')
                lines: string[] = text.split_lines(changed)
                joined: string = text.join(parts, ',')
                parts.append(joined)
            }
            """;
        using var project = new Project();
        project.Write("main.rukbat", source);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var arguments = new[] { "fmt", project.PathFor("main.rukbat") };
        Assert.Equal(0, Cli.Run(arguments, stdout, stderr));
        var formatted = File.ReadAllText(project.PathFor("main.rukbat"));
        Assert.Equal(0, Cli.Run(arguments, stdout, stderr));
        Assert.Equal(formatted, File.ReadAllText(project.PathFor("main.rukbat")));
        Assert.Equal(0, Cli.Run(["check", project.PathFor("main.rukbat")], stdout, stderr));
        Assert.False(File.Exists(project.PathFor("main.ps1")));
        Assert.Equal("", stderr.ToString());
        Assert.Contains("'text.split_lines'", Success(formatted));
        Assert.DoesNotContain("'text.split_lines'", Success("print('plain')"));
    }
}
