namespace Rukbat.Tests;

public sealed class RegexTests : TestBase
{
    [Trait("Category", "Unit")]
    [Fact]
    public void ChecksFixedOperationsAndOrdinaryMatchValues()
    {
        Assert.Empty(new Compiler().Check("""
            import re
            struct Holder { pattern: re.Pattern; matches: re.Match[]; optional: re.Pattern? }
            fn pattern(value: re.Pattern): re.Pattern { return value }
            fn optional(value: re.Pattern?): re.Pattern? { return value }
            fn matches(value: re.Match[]): re.Match[] { return value }
            selected := pattern(re.compile('[a-z]+', 1000))
            print(re.is_match('name', selected))
            found: re.Match? = re.find('name', selected)
            if found != null { print(found.value); print(found.start); print(found.count) }
            all := matches(re.find_all('first second', selected))
            print(re.replace_all('name', selected, '$1'))
            print(re.replace_all_template('name', selected, '${0}'))
            print(re.split('name', selected).length)
            print(re.escape('a.b'))
            literal := re.Match { value: 'a', start: 0, count: 1, groups: {'0': 'a', 'unused': null} }
            literal.groups['0'] = 'changed'
            patterns: re.Pattern[] = [selected]
            lookup: dict[string, re.Pattern?[]] = {'one': [selected, null]}
            holder := Holder { pattern: patterns[0], matches: [literal], optional: optional(lookup['one'][0]) }
            copied := holder
            copied.matches[0].groups['0'] = null
            for item in all { print(item.value) }
            """));
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("re.compile('a')", "RUK2005")]
    [InlineData("re.compile('a', 1000, 1)", "RUK2005")]
    [InlineData("re.compile<int>('a', 1000)", "RUK2005")]
    [InlineData("re.compile(pattern: 'a', timeout_ms: 1000)", "RUK2005")]
    [InlineData("re.compile(1, 1000)", "RUK2001")]
    [InlineData("re.compile('a', 1.0)", "RUK2001")]
    [InlineData("re.is_match('a', 'a')", "RUK2001")]
    [InlineData("re.is_match(null, re.compile('a', 1000))", "RUK2001")]
    [InlineData("re.find('a')", "RUK2005")]
    [InlineData("re.find_all('a', null)", "RUK2001")]
    [InlineData("re.replace_all('a', re.compile('a', 1000), 1)", "RUK2001")]
    [InlineData("re.replace_all_template('a', re.compile('a', 1000), null)", "RUK2001")]
    [InlineData("re.split('a', 'a')", "RUK2001")]
    [InlineData("re.escape(1)", "RUK2001")]
    [InlineData("re.match('a', re.compile('a', 1000))", "RUK2004")]
    [InlineData("re.search('a', re.compile('a', 1000))", "RUK2004")]
    [InlineData("value: re.Pattern = 'a'", "RUK2001")]
    [InlineData("value := re.Pattern {}", "RUK2004")]
    [InlineData("value := re.compile('a', 1000).value", "RUK2004")]
    [InlineData("value := re.compile('a', 1000) == re.compile('a', 1000)", "RUK2001")]
    [InlineData("value := re.compile('a', 1000) < re.compile('a', 1000)", "RUK2001")]
    [InlineData("value: re.Match = re.find('a', re.compile('a', 1000))", "RUK2001")]
    [InlineData("value := re.Match {}", "RUK2005")]
    [InlineData("value := re.Match { value: 'a', start: 0, count: 1, groups: {'0': 1} }", "RUK2001")]
    public void RejectsInvalidCallsAndOpaqueOperations(string body, string code)
        => Assert.Contains(new Compiler().Check("import re\n" + body), diagnostic => diagnostic.Code == code);

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("print(re.compile('a', 1000))")]
    [InlineData("eprint(re.compile('a', 1000))")]
    [InlineData("print([re.compile('a', 1000)])")]
    [InlineData("value: re.Pattern? = null; print(value)")]
    [InlineData("json.stringify(re.compile('a', 1000))")]
    [InlineData("json.stringify({'pattern': re.compile('a', 1000)})")]
    [InlineData("json.parse<re.Pattern>('\"a\"')")]
    [InlineData("json.as<re.Pattern>(json.parse('\"a\"'))")]
    [InlineData("json.set(json.parse('{}'), 'pattern', re.compile('a', 1000))")]
    [InlineData("json.set_at(json.parse('[null]'), 0, re.compile('a', 1000))")]
    [InlineData("struct Holder { values: dict[string, re.Pattern?[]] }; json.stringify(Holder { values: {'one': [re.compile('a', 1000), null]} })")]
    [InlineData("struct Holder { values: dict[string, re.Pattern?[]] }; json.parse<Holder>('{}')")]
    [InlineData("powershell<void>('Write-Output', re.compile('a', 1000))")]
    [InlineData("powershell<void>('Write-Output', [re.compile('a', 1000)])")]
    [InlineData("powershell<re.Pattern>('Write-Output', 'a')")]
    [InlineData("powershell<re.Pattern?[]>('Write-Output', 'a')")]
    [InlineData("struct Holder { values: re.Pattern?[] }; powershell<Holder>('Write-Output')")]
    public void RejectsOpaqueValuesAtRecursiveExternalBoundaries(string body)
        => Assert.Contains(new Compiler().Check("import re; import json\n" + body), diagnostic => diagnostic.Code == "RUK2010");

    [Trait("Category", "Unit")]
    [Fact]
    public void ReservesCanonicalImportNameAndFormatsExistingSyntax()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "fn value(): int { return 1 }");
        project.Write("main.rukbat", "import './helper.rukbat' as re\n");
        Assert.Contains(new Compiler().CheckFile(project.PathFor("main.rukbat")), diagnostic => diagnostic.Code == "RUK3002");
        Assert.Contains(new Compiler().Check("import re as regex"), diagnostic => diagnostic.Code == "RUK3002");
        Assert.Empty(new Compiler().Check("re := 1; print(re)"));

        project.Write("main.rukbat", "import re;fn keep(value:re.Pattern):re.Pattern{return value};pattern:=keep(re.compile('a',1000));matches:=re.find_all('a',pattern)");
        var path = project.PathFor("main.rukbat");
        Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, TextWriter.Null));
        var formatted = File.ReadAllText(path);
        Assert.Contains("fn keep(value: re.Pattern): re.Pattern {", formatted);
        Assert.Contains("pattern := keep(re.compile('a', 1000))", formatted);
        Assert.Empty(new Compiler().CheckFile(path));
        Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, TextWriter.Null));
        Assert.Equal(formatted, File.ReadAllText(path));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void GeneratedScriptIncludesOnlyTheImportedRegexRuntime()
    {
        var output = Success("import re\npattern := re.compile('a', 1000)\nprint(re.is_match('a', pattern))");
        Assert.Contains("$__rukbat_load_Regex", output);
        Assert.Contains("public static class RukbatRegex", output);
        Assert.Contains("'re.compile' {", output);
        Assert.Contains("'re.is_match' {", output);
        Assert.DoesNotContain("RukbatProcessJob", output);
        Assert.DoesNotContain("RukbatFileText", output);
        Assert.DoesNotContain("RukbatMoveBatch", output);
        Assert.DoesNotContain("RukbatLog", output);
        Assert.DoesNotContain("'fs.read_text'", output);
        Assert.DoesNotContain("'process.run'", output);
        Assert.DoesNotContain("'log.info'", output);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task MatchesCapturesAndUsesInvariantOptions(string shell)
    {
        await Verify(shell, """
            import re; import test
            fn expect_group(found: re.Match, key: string, expected: string): void {
                value := found.groups[key]
                if value == null { test.fail('Missing group: ' + key) }
                else { test.assert(value == expected) }
            }
            const timeout_ms := 1000
            pattern := re.compile('(?<key>[a-z]+)=([0-9]+)(?<unused>x)?(?<empty>)', timeout_ms)
            test.assert(re.is_match('before port=443 after', pattern))
            test.assert(!re.is_match('PORT=443', pattern))
            found := re.find('before port=443 after', pattern)
            if found == null { test.fail('Missing match') } else {
                matched := found
                test.assert(matched.value == 'port=443')
                test.assert(matched.start == 7)
                test.assert(matched.count == 8)
                expect_group(matched, '0', 'port=443')
                expect_group(matched, '1', '443')
                expect_group(matched, '2', 'port')
                expect_group(matched, 'key', 'port')
                expect_group(matched, '4', '')
                expect_group(matched, 'empty', '')
                test.assert(matched.groups['3'] == null)
                test.assert(matched.groups['unused'] == null)
                test.assert(!matched.groups.has('missing'))
                rejected := false
                try { matched.groups['missing'] } catch error { rejected = true }
                test.assert(rejected)
            }
            test.assert(re.find('none', pattern) == null)
            test.assert(re.find_all('none', pattern).length == 0)
            all := re.find_all('a12 b34', re.compile('[0-9]+', timeout_ms))
            test.assert(all.length == 2)
            test.assert(all[0].value == '12' && all[0].start == 1 && all[0].count == 2)
            test.assert(all[1].value == '34' && all[1].start == 5 && all[1].count == 2)
            repeated := re.find('abc', re.compile('(?<letter>[abc])+', timeout_ms))
            if repeated != null { expect_group(repeated, 'letter', 'c') } else { test.fail('Missing repeated match') }
            test.assert(re.is_match('i', re.compile('(?i)I', timeout_ms)))
            test.assert(!re.is_match('a', re.compile('A', timeout_ms)))
            test.assert(re.is_match('first\nsecond', re.compile('(?m)^second$', timeout_ms)))
            test.assert(re.is_match('a\nb', re.compile('(?s)a.b', timeout_ms)))
            test.assert(!re.is_match('a\nb', re.compile('a.b', timeout_ms)))
            test.assert(re.is_match('abc', re.compile('\\A[a-z]+\\z', timeout_ms)))
            test.assert(!re.is_match('abc!', re.compile('\\A[a-z]+\\z', timeout_ms)))
            print('passed')
            """, "[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('tr-TR')\n");
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ReplacesSplitsAndEscapesLiterally(string shell)
    {
        await Verify(shell, """
            import re; import test
            pattern := re.compile('(?<left>[a-z]+)=(?<right>[0-9]+)', 1000)
            test.assert(re.replace_all('x=1 y=2', pattern, '$1${left}$$') == '$1${left}$$ $1${left}$$')
            test.assert(re.replace_all_template('x=1 y=2', pattern, '${right}:${left}:$$') == '1:x:$ 2:y:$')
            test.assert(re.replace_all_template('x=1', pattern, '${0}/${1}/${2}') == 'x=1/x/1')
            test.assert(re.replace_all('abc', re.compile('a', 1000), 'aa') == 'aabc')
            test.assert(re.replace_all('abc', re.compile('a', 1000), '') == 'bc')
            test.assert(re.replace_all('abc', pattern, 'changed') == 'abc')
            test.assert(re.replace_all_template('abc', pattern, '${left}') == 'abc')
            test.assert(re.replace_all_template('b', re.compile('(a)?b', 1000), '<${1}>') == '<>')
            parts := re.split(',a,,b,', re.compile('(,)', 1000))
            test.assert(parts.length == 5)
            test.assert(parts[0] == '' && parts[1] == 'a' && parts[2] == '' && parts[3] == 'b' && parts[4] == '')
            untouched := re.split('abc', pattern)
            test.assert(untouched.length == 1 && untouched[0] == 'abc')
            empty := re.split('', pattern)
            test.assert(empty.length == 1 && empty[0] == '')
            literal := 'a.b+[x](y){z}?^$|\\ #'
            escaped := re.compile('\\A' + re.escape(literal) + '\\z', 1000)
            test.assert(re.is_match(literal, escaped))
            test.assert(!re.is_match('other', escaped))
            test.assert(re.escape('') == '')
            print('passed')
            """);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task UsesCodePointOffsetsAndAdvancesEmptyMatchesByCodePoint(string shell)
    {
        await Verify(shell, """
            import re; import json; import test
            emoji := json.parse<string>('"\\ud83d\\ude00"')
            value := 'A' + emoji + 'B'
            found := re.find(value, re.compile(emoji + 'B', 1000))
            if found == null { test.fail('Missing Unicode match') } else {
                test.assert(found.start == 1 && found.count == 2)
                test.assert(found.value == emoji + 'B')
            }
            empty := re.compile('', 1000)
            all := re.find_all(value, empty)
            test.assert(all.length == 4)
            for item in all { test.assert(item.value == '' && item.count == 0) }
            test.assert(all[0].start == 0 && all[1].start == 1 && all[2].start == 2 && all[3].start == 3)
            empty_input := re.find_all('', empty)
            test.assert(empty_input.length == 1 && empty_input[0].start == 0)
            test.assert(re.replace_all(value, empty, '|') == '|A|' + emoji + '|B|')
            test.assert(re.replace_all_template(value, empty, '<${0}>') == '<>A<>' + emoji + '<>B<>')
            parts := re.split(value, empty)
            test.assert(parts.length == 5)
            test.assert(parts[0] == '' && parts[1] == 'A' && parts[2] == emoji && parts[3] == 'B' && parts[4] == '')
            marks := re.find_all('A' + emoji + 'B', re.compile('(?=B)|$', 1000))
            test.assert(marks.length == 2 && marks[0].start == 2 && marks[1].start == 3)
            behind := re.find(value, re.compile('(?<=(?<pair>' + emoji + '))B', 1000))
            if behind == null { test.fail('Missing lookbehind match') } else {
                test.assert(behind.start == 2 && behind.count == 1)
                pair := behind.groups['pair']
                if pair == null { test.fail('Missing lookbehind capture') } else { test.assert(pair == emoji) }
            }
            print('passed')
            """);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task RejectsInvalidPatternsTemplatesUnicodeAndTimeouts(string shell)
    {
        var source = """
            import re; import json; import test
            pattern := re.compile('(?<name>a)', 1000)
            emoji := json.parse<string>('"\\ud83d\\ude00"')
            invalid := powershell<string>('Invoke-Expression', '-join [char[]](0xD800)')
            invalid_low := powershell<string>('Invoke-Expression', '-join [char[]](0xDC00)')
            """ + "\n";
        var expressions = new[]
        {
            "re.compile('[', 1000)", "re.compile('a', -1)", "re.compile('a', 0)",
            "re.compile('a', 2147483647)", "re.compile('a', 9223372036854775807)",
            "re.compile(invalid, 1000)", "re.escape(invalid)",
            "re.compile(invalid_low, 1000)", "re.escape(invalid_low)", "re.find(invalid_low, pattern)",
            "re.is_match(invalid, pattern)", "re.find(invalid, pattern)", "re.find_all(invalid, pattern)",
            "re.replace_all(invalid, pattern, '')", "re.replace_all('none', pattern, invalid)",
            "re.replace_all_template(invalid, pattern, '')", "re.replace_all_template('none', pattern, invalid)",
            "re.split(invalid, pattern)",
            "re.is_match(emoji, re.compile('.', 1000))", "re.find(emoji, re.compile('.', 1000))",
            "re.find_all(emoji, re.compile('.', 1000))", "re.replace_all(emoji, re.compile('.', 1000), '')",
            "re.replace_all_template(emoji, re.compile('.', 1000), '')", "re.split(emoji, re.compile('.', 1000))",
            "re.is_match('A' + emoji, re.compile('(?=(?<part>..))', 1000))",
            "re.find(emoji, re.compile('(?<part>.)*', 1000))"
        };
        for (var index = 0; index < expressions.Length; index++)
            source += $"rejected{index} := false\ntry {{ {expressions[index]} }} catch error {{ rejected{index} = true }}\ntest.assert(rejected{index})\n";
        foreach (var template in new[] { "$", "$1", "$name", "${}", "${name", "${missing}", "${2}", "${01}", "${00}" })
        {
            source += "if true {\nrejected := false\ntry { re.replace_all_template('zzz', pattern, '" + template + "') } catch error { rejected = true }\ntest.assert(rejected)\n}\n";
        }
        source += """
            pathological := powershell<string>('Invoke-Expression', "'a' * 20000 + '!'")
            timed_out := false
            try { re.is_match(pathological, re.compile('^(a+)+$', 1)) }
            catch error { timed_out = true; test.assert(error.message != '') }
            test.assert(timed_out)
            print('passed')
            """;
        await Verify(shell, source);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", false)]
    [InlineData("pwsh", true)]
    public async Task PreservesIdentityCopiesAndArgumentEvaluationAcrossModules(string shell, bool debug)
    {
        using var project = new Project();
        project.Write("helper.rukbat", """
            import re
            fn timeout(): int { return 1000 }
            fn text(): string { return 'a' }
            fn template(): string { return '${word}' }
            fn create(): re.Pattern { return re.compile('(?<word>a)', 1000) }
            fn keep(value: re.Pattern): re.Pattern { return value }
            fn matches(value: re.Pattern): re.Match[] { return re.find_all('a a', value) }
            fn copy(value: re.Match): re.Match { value.groups['word'] = 'argument'; return value }
            """);
        project.Write("main.rukbat", """
            import re; import json; import test
            import './helper.rukbat' as helper
            struct Match { value: int }
            struct Pattern { value: int }
            struct Holder { patterns: dict[string, re.Pattern?[]] }
            calls := ''
            fn value(): string { calls = calls + 'v'; return 'a' }
            fn pattern(): re.Pattern { calls = calls + 'p'; return helper.create() }
            fn replacement(): string { calls = calls + 'r'; return 'b' }
            test.assert(re.replace_all(value(), pattern(), replacement()) == 'b')
            test.assert(calls == 'vpr')
            selected := helper.keep(helper.create())
            test.assert(re.is_match(helper.text(), re.compile(helper.text(), helper.timeout())))
            imported_match := re.find(helper.text(), helper.keep(selected))
            if imported_match == null { test.fail('Missing imported match') } else { test.assert(imported_match.value == 'a') }
            test.assert(re.find_all(helper.text(), helper.keep(selected)).length == 1)
            test.assert(re.replace_all(helper.text(), helper.keep(selected), helper.text()) == 'a')
            test.assert(re.replace_all_template(helper.text(), helper.keep(selected), helper.template()) == 'a')
            test.assert(re.split(helper.text(), helper.keep(selected)).length == 2)
            test.assert(re.escape(helper.text()) == 'a')
            holder := Holder { patterns: {'main': [selected, null]} }
            copied_holder := holder
            copied_holder.patterns['main'][0] = re.compile('b', 1000)
            original_pattern := holder.patterns['main'][0]
            if original_pattern == null { test.fail('Missing pattern') } else { test.assert(re.is_match('a', original_pattern)) }
            all := helper.matches(selected)
            copied := all
            copied[0].groups['word'] = 'changed'
            copied[0].value = 'changed'
            original := all[0].groups['word']
            if original == null { test.fail('Missing original capture') } else { test.assert(original == 'a') }
            test.assert(all[0].value == 'a')
            returned := helper.copy(all[0])
            returned_group := returned.groups['word']
            if returned_group == null { test.fail('Missing returned capture') } else { test.assert(returned_group == 'argument') }
            original_after := all[0].groups['word']
            if original_after == null { test.fail('Missing preserved capture') } else { test.assert(original_after == 'a') }
            for item in all { item.groups['word'] = 'loop' }
            loop_original := all[0].groups['word']
            if loop_original == null { test.fail('Missing loop original') } else { test.assert(loop_original == 'a') }
            restored := json.parse<re.Match>(json.stringify(all[0]))
            test.assert(restored.value == 'a' && restored.start == 0 && restored.count == 1)
            print('passed')
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ExecutesTheStandaloneRegexExample(string shell)
    {
        using var project = new Project();
        var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples", "regex.rukbat");
        project.Write("main.rukbat", File.ReadAllText(fixture));
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell"
            ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("host: web-01\nport: 443\nhost:web-01 port:443\n", result.Output);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task RuntimeErrorsRetainTheOperationSourceLocation(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", "import re\npattern := re.compile('a', 1000)\nre.replace_all_template('none', pattern, '${missing}')\n");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell"
            ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("main.rukbat:3", result.Error);
        Assert.DoesNotContain(project.DirectoryPath, result.Error);
    }

    private static async Task Verify(string shell, string source, string prelude = "")
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell"
            ? await project.RunOutputWithWindowsPowerShell(prelude + compiled.Output!)
            : await project.RunOutput(prelude + compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }
}
