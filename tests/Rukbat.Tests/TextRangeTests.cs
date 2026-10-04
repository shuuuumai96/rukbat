namespace Rukbat.Tests;

public class TextRangeTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("text.slice('abc', 1.0, 1)")]
    [InlineData("text.length(3)")]
    [InlineData("text.index_of('a', 'a') + 1")]
    [InlineData("text.upper(null)")]
    public void RejectsInvalidCalls(string expression) => Assert.False(Compile("import text\n" + expression).Success);

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task UsesCodePointsAndExplicitNullableSearchResults(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", """
            import text; import json; import test
            value := json.parse<string>('"A\\ud83d\\ude00B\\u0301"')
            emoji := json.parse<string>('"\\ud83d\\ude00"')
            test.assert(text.length(value) == 4)
            test.assert(text.slice(value, 1, 1) == emoji)
            test.assert(text.slice(value, 4, 0) == '')
            test.assert(text.length('') == 0)
            test.assert(text.slice('', 0, 0) == '')
            found := text.index_of(value, 'B')
            if found != null { test.assert(found == 2) } else { test.fail('Missing match') }
            test.assert(text.index_of(value, 'b') == null)
            first := text.index_of(value, '')
            if first != null { test.assert(first == 0) } else { test.fail('Missing empty match') }
            test.assert(text.upper('abc-I') == 'ABC-I')
            test.assert(text.lower('ABC-I') == 'abc-i')
            test.assert(text.upper(emoji) == emoji)
            """);
        var expressions = new[] { "text.slice(value, -1, 1)", "text.slice(value, 4, 1)", "text.slice(value, 0, -1)",
            "text.slice(value, 9223372036854775807, 0)", "text.slice(value, 1, 9223372036854775807)",
            "text.length(invalid)", "text.slice(invalid, 0, 0)", "text.index_of(value, invalid)", "text.upper(invalid)" };
        var source = File.ReadAllText(project.PathFor("main.rukbat")) + "\ninvalid := powershell<string>('Invoke-Expression', '[string][char]0xD800')\n";
        for (int i = 0; i < expressions.Length; i++)
            source += $"rejected{i} := false\ntry {{ {expressions[i]} }} catch err {{ rejected{i} = true }}\ntest.assert(rejected{i})\n";
        project.Write("main.rukbat", source + "print('passed')\n");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var output = "[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('tr-TR')\n" + compiled.Output!;
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(output) : await project.RunOutput(output);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }
}
