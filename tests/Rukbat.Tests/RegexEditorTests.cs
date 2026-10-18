using System.Text.Json;

namespace Rukbat.Tests;

public sealed partial class LanguageServerTests
{
    [Fact]
    public void RegexCompletionIncludesTheEightOperationsAndBothTypes()
    {
        using var project = new Project();
        var uri = UriFor(project);
        var completion = Result(EditorSession(project, Open(uri, "import re\nre."), Feature(uri, "completion", 1, 3)));
        var items = completion.EnumerateArray().ToArray();
        var names = new[] { "compile", "is_match", "find", "find_all", "replace_all", "replace_all_template", "split", "escape", "Pattern", "Match" };
        Assert.Equal(names.Order(), items.Select(item => item.GetProperty("label").GetString()).Order());
        foreach (var item in items.Where(item => item.GetProperty("kind").GetInt32() == 3))
            Assert.NotEmpty(item.GetProperty("documentation").GetProperty("value").GetString()!);
        var pattern = Assert.Single(items.Where(item => item.GetProperty("label").GetString() == "Pattern"));
        Assert.Equal("re.Pattern", pattern.GetProperty("detail").GetString());
        Assert.Contains("finite match timeout", pattern.GetProperty("documentation").GetProperty("value").GetString());

        var modules = Result(EditorSession(project, Open(uri, "import r"), Feature(uri, "completion", 0, 8)));
        Assert.Contains(modules.EnumerateArray(), item => item.GetProperty("label").GetString() == "re");
    }

    [Fact]
    public void RegexPatternTypeReferencesShareAnOpaqueFixedIdentity()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "import re\nstruct Holder { pattern: re.Pattern }\nfn same(pattern: re.Pattern): re.Pattern { return pattern }\npattern: re.Pattern = re.compile('a', 1000)\npatterns: re.Pattern[] = [same(pattern)]\noptional: re.Pattern? = pattern\nlookup: dict[string, re.Pattern] = {'one': pattern}\n";
        var lines = source.Split('\n');
        var requests = new List<object> { Open(uri, source) };
        var requestIds = new List<string>();
        for (var line = 0; line < lines.Length; line++)
        {
            var start = 0;
            while ((start = lines[line].IndexOf("re.Pattern", start, StringComparison.Ordinal)) >= 0)
            {
                var id = "type" + requestIds.Count;
                requestIds.Add(id);
                requests.Add(Feature(uri, "hover", line, start + "re.".Length, id));
                start += "re.Pattern".Length;
            }
        }
        requests.Add(References(uri, 3, 12));
        requests.Add(Feature(uri, "prepareRename", 3, 12, "rename"));
        requests.Add(Feature(uri, "definition", 3, 12, "definition"));
        var messages = EditorSession(project, requests.ToArray());
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        foreach (var id in requestIds)
        {
            var hover = Result(messages, id).GetProperty("contents").GetProperty("value").GetString();
            Assert.Contains("re.Pattern", hover);
            Assert.Contains("immutable validated regular expression", hover);
        }
        Assert.Equal(requestIds.Count, Result(messages).GetArrayLength());
        Assert.True(Answer(messages, "rename").TryGetProperty("error", out _));
        Assert.Equal(JsonValueKind.Null, Result(messages, "definition").ValueKind);
    }

    [Fact]
    public void RegexMatchCompletionAndHoverExposeOrdinaryCheckedFields()
    {
        using var project = new Project();
        var uri = UriFor(project);
        const string source = "import re\npattern := re.compile('(?<name>a)', 1000)\nfound := re.find('a', pattern)\nif found != null { print(found.value); print(found.start); print(found.count); groups := found.groups }\n";
        var fields = source.Split('\n')[3];
        var messages = EditorSession(project, Open(uri, source), Feature(uri, "hover", 1, 16, "compile"),
            Feature(uri, "hover", 2, 14, "find"), Feature(uri, "hover", 3, fields.IndexOf(".value", StringComparison.Ordinal) + 1, "value"),
            Feature(uri, "hover", 3, fields.IndexOf(".groups", StringComparison.Ordinal) + 1, "groups"));
        Assert.Empty(LastDiagnostics(messages, uri).GetProperty("diagnostics").EnumerateArray());
        Assert.Contains("re.compile(pattern: string, timeout_ms: int): re.Pattern", Result(messages, "compile").ToString());
        Assert.Contains("re.find(value: string, pattern: re.Pattern): re.Match?", Result(messages, "find").ToString());
        Assert.Contains("value: string", Result(messages, "value").ToString());
        Assert.Contains("groups: dict[string, string?]", Result(messages, "groups").ToString());

        const string prefix = "import re\npattern := re.compile('a', 1000)\nmatch := re.find_all('a', pattern)[0]\nmatch.";
        var completion = Result(EditorSession(project, Open(uri, prefix), Feature(uri, "completion", 3, 6)));
        Assert.Equal(new[] { "count: int", "groups: dict[string, string?]", "start: int", "value: string" },
            completion.EnumerateArray().Select(item => item.GetProperty("detail").GetString()).Order());

        const string opaque = "import re\npattern := re.compile('a', 1000)\npattern.";
        var members = Result(EditorSession(project, Open(uri, opaque), Feature(uri, "completion", 2, 8)));
        Assert.Empty(members.EnumerateArray());
    }

    [Theory]
    [InlineData("re.compile('a', ", "re.compile(pattern: string, timeout_ms: int): re.Pattern", 1)]
    [InlineData("re.is_match('a', ", "re.is_match(value: string, pattern: re.Pattern): bool", 1)]
    [InlineData("re.find('a', ", "re.find(value: string, pattern: re.Pattern): re.Match?", 1)]
    [InlineData("re.find_all('a', ", "re.find_all(value: string, pattern: re.Pattern): re.Match[]", 1)]
    [InlineData("re.replace_all('a', pattern, ", "re.replace_all(value: string, pattern: re.Pattern, replacement: string): string", 2)]
    [InlineData("re.replace_all_template('a', pattern, ", "re.replace_all_template(value: string, pattern: re.Pattern, template: string): string", 2)]
    [InlineData("re.split('a', ", "re.split(value: string, pattern: re.Pattern): string[]", 1)]
    [InlineData("re.escape(", "re.escape(value: string): string", 0)]
    public void RegexSignatureHelpUsesCanonicalDocumentation(string call, string signature, int activeParameter)
    {
        using var project = new Project();
        var uri = UriFor(project);
        var source = "import re\npattern := re.compile('a', 1000)\n" + call;
        var result = Result(EditorSession(project, Open(uri, source), Feature(uri, "signatureHelp", 2, call.Length)));
        Assert.NotEqual(JsonValueKind.Null, result.ValueKind);
        Assert.Equal(signature, result.GetProperty("signatures")[0].GetProperty("label").GetString());
        Assert.Equal(activeParameter, result.GetProperty("activeParameter").GetInt32());
        Assert.NotEmpty(result.GetProperty("signatures")[0].GetProperty("documentation").GetProperty("value").GetString()!);
    }
}
