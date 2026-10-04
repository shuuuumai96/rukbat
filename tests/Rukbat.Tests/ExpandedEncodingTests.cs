namespace Rukbat.Tests;

public class ExpandedEncodingTests : TestBase
{
    public static IEnumerable<object[]> Fixtures()
    {
        yield return new object[] { "utf8", 65001, "utf-8", "E697A5E69CACF09F9880", "\\u65e5\\u672c\\ud83d\\ude00" };
        yield return new object[] { "utf16_le", 1200, "utf-16le", "E5652C673DD800DE", "\\u65e5\\u672c\\ud83d\\ude00" };
        yield return new object[] { "utf16_be", 1201, "utf-16be", "65E5672CD83DDE00", "\\u65e5\\u672c\\ud83d\\ude00" };
        yield return new object[] { "utf32_le", 12000, "utf-32le", "E56500002C67000000F60100", "\\u65e5\\u672c\\ud83d\\ude00" };
        yield return new object[] { "utf32_be", 12001, "utf-32be", "000065E50000672C0001F600", "\\u65e5\\u672c\\ud83d\\ude00" };
        yield return new object[] { "ascii", 20127, "ascii", "41", "A" };
        yield return new object[] { "iso_8859_1", 28591, "iso-8859-1", "E9", "\\u00e9" };
        yield return new object[] { "windows_874", 874, "windows-874", "A1", "\\u0e01" };
        yield return new object[] { "windows_1250", 1250, "windows-1250", "A3", "\\u0141" };
        yield return new object[] { "windows_1251", 1251, "windows-1251", "DF", "\\u042f" };
        yield return new object[] { "windows_1252", 1252, "windows-1252", "80", "\\u20ac" };
        yield return new object[] { "windows_1253", 1253, "windows-1253", "D9", "\\u03a9" };
        yield return new object[] { "windows_1254", 1254, "windows-1254", "D0", "\\u011e" };
        yield return new object[] { "windows_1255", 1255, "windows-1255", "E0", "\\u05d0" };
        yield return new object[] { "windows_1256", 1256, "windows-1256", "D4", "\\u0634" };
        yield return new object[] { "windows_1257", 1257, "windows-1257", "CB", "\\u0116" };
        yield return new object[] { "windows_1258", 1258, "windows-1258", "D0", "\\u0110" };
        yield return new object[] { "windows_932", 932, "windows-932", "93FA967B", "\\u65e5\\u672c" };
        yield return new object[] { "windows_936", 936, "windows-936", "D6D0", "\\u4e2d" };
        yield return new object[] { "windows_949", 949, "windows-949", "C7D1", "\\ud55c" };
        yield return new object[] { "windows_950", 950, "windows-950", "A4A4", "\\u4e2d" };
        yield return new object[] { "windows_437", 437, "windows-437", "82", "\\u00e9" };
        yield return new object[] { "windows_850", 850, "windows-850", "9B", "\\u00f8" };
        yield return new object[] { "windows_852", 852, "windows-852", "9D", "\\u0141" };
        yield return new object[] { "windows_866", 866, "windows-866", "9F", "\\u042f" };
        yield return new object[] { "gb18030", 54936, "gb18030", "9439FC36", "\\ud83d\\ude00" };
    }

    public static IEnumerable<object[]> HostsAndFixtures() =>
        from shell in new[] { "powershell", "pwsh" }
        from fixture in Fixtures()
        select new object[] { shell }.Concat(fixture).ToArray();

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [MemberData(nameof(HostsAndFixtures))]
    public async Task KnownBytesRoundTripThroughFilesAndProcess(string shell, string member, int number,
        string label, string hex, string escaped)
    {
        using var project = new Project();
        var bytes = Convert.FromHexString(hex);
        File.WriteAllBytes(project.PathFor("input.bin"), bytes);
        project.Write("child.ps1", "$bytes = [Convert]::FromBase64String($args[0]); " +
            "$out = [Console]::OpenStandardOutput(); $out.Write($bytes, 0, $bytes.Length); " +
            "$err = [Console]::OpenStandardError(); $err.Write($bytes, 0, $bytes.Length); exit 7");
        string PathLiteral(string name) => project.PathFor(name).Replace("\\", "/");
        project.Write("main.rukbat", $$"""
            import encoding; import fs; import process; import json; import test
            selected := encoding.{{member}}
            expected := json.parse<string>('"{{escaped.Replace("\\", "\\\\")}}"')
            test.assert(encoding.from_code_page({{number}}) == selected)
            test.assert(encoding.name(selected) == '{{label}}')
            test.assert(encoding.parse(encoding.name(selected)) == selected)
            test.assert(fs.read_text('{{PathLiteral("input.bin")}}', selected) == expected)
            fs.write_text('{{PathLiteral("output.bin")}}', expected, selected)
            result := process.run('powershell', ['-NoProfile', '-NonInteractive', '-File', '{{PathLiteral("child.ps1")}}', '{{Convert.ToBase64String(bytes)}}'], process.options(10000, selected))
            test.assert(result.stdout == expected)
            test.assert(result.stderr == expected)
            test.assert(result.exit_code == 7)
            print('passed')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
        Assert.Equal(bytes, File.ReadAllBytes(project.PathFor("output.bin")));
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("encoding.from_code_page('932')", "RUK2001")]
    [InlineData("encoding.from_code_page()", "RUK2005")]
    [InlineData("encoding.from_code_page(code_page: 932)", "RUK2005")]
    [InlineData("encoding.from_code_page<string>(932)", "RUK2005")]
    public void FactoryIsStaticallyTyped(string source, string code) =>
        Assert.Contains(Compile("import encoding; " + source).Diagnostics, d => d.Code == code);

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FactoryRejectsUnsafeDefaultsAndRestoresAdditionalCodePages(string shell)
    {
        using var project = new Project();
        File.WriteAllBytes(project.PathFor("input.bin"), [0xd5]); // OEM 858 euro sign.
        var path = project.PathFor("input.bin").Replace("\\", "/");
        project.Write("main.rukbat", $$"""
            import encoding; import fs; import json; import test
            value := encoding.from_code_page(858)
            test.assert(encoding.name(value) == 'windows-858')
            test.assert(encoding.parse(encoding.name(value)) == value)
            test.assert(fs.read_text('{{path}}', value) == json.parse<string>('"\\u20ac"'))
            for number in [-1, 0, 1, 2, 3, 42, 20932, 51932, 50220, 50221, 50222, 57002, 65000, 99999] {
                rejected := false
                try { encoding.from_code_page(number) } catch err { rejected = true }
                test.assert(rejected)
            }
            for name in ['default', 'ansi', 'oem', 'windows-065001', 'windows-65001', 'windows-1200', 'windows-54936', 'windows-28591', 'windows-20127', 'windows-0', 'windows-2147483648'] {
                rejected := false
                try { encoding.parse(name) } catch err { rejected = true }
                test.assert(rejected)
            }
            print('passed')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task StrictConversionsRejectMalformedBytesAndPreserveFilesOnWriteFailure(string shell)
    {
        using var project = new Project();
        var invalid = new (string Member, byte[] Bytes)[] {
            ("utf32_le", [0, 0, 0x11, 0]), ("utf32_be", [0, 0x11, 0, 0]),
            ("utf32_le", [65]), ("ascii", [0xff]), ("windows_936", [0x81]),
            ("windows_949", [0x81]), ("windows_950", [0x81]), ("gb18030", [0x81, 0x30, 0x81])
        };
        var source = "import encoding; import fs; import json; import test\n";
        for (int i = 0; i < invalid.Length; i++)
        {
            File.WriteAllBytes(project.PathFor($"invalid{i}.bin"), invalid[i].Bytes);
            var path = project.PathFor($"invalid{i}.bin").Replace("\\", "/");
            source += $"rejected{i} := false\ntry {{ fs.read_text('{path}', encoding.{invalid[i].Member}) }} catch err {{ rejected{i} = true }}\ntest.assert(rejected{i})\n";
        }
        var destination = project.PathFor("output.bin").Replace("\\", "/");
        File.WriteAllBytes(project.PathFor("output.bin"), [65]);
        source += "emoji := json.parse<string>('\"\\\\ud83d\\\\ude00\"')\n";
        foreach (var fixture in Fixtures().Where(v => !((string)v[0]).StartsWith("utf") && (string)v[0] != "gb18030"))
        {
            var member = (string)fixture[0];
            source += $"rejected_{member} := false\ntry {{ fs.write_text('{destination}', emoji, encoding.{member}) }} catch err {{ rejected_{member} = true }}\ntest.assert(rejected_{member})\n";
        }
        // U+221E has a best-fit '8' mapping on CP1252; it must fail rather than change meaning.
        source += $"best_fit_rejected := false\ntry {{ fs.write_text('{destination}', json.parse<string>('\"\\\\u221e\"'), encoding.windows_1252) }} catch err {{ best_fit_rejected = true }}\ntest.assert(best_fit_rejected)\nprint('passed')\n";
        project.Write("main.rukbat", source);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
        Assert.Equal(new byte[] { 65 }, File.ReadAllBytes(project.PathFor("output.bin")));
        Assert.Empty(Directory.GetFiles(project.DirectoryPath, "*.tmp"));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task Utf32FilesStripOnlyMatchingBomAndNativeCodecsRejectLoneSurrogates(string shell)
    {
        using var project = new Project();
        File.WriteAllBytes(project.PathFor("le.bin"), [0xff, 0xfe, 0, 0, 65, 0, 0, 0]);
        File.WriteAllBytes(project.PathFor("be.bin"), [0, 0, 0xfe, 0xff, 0, 0, 0, 65]);
        var directory = project.DirectoryPath.Replace("\\", "/");
        project.Write("main.rukbat", $$"""
            import encoding; import fs; import test
            test.assert(fs.read_text('{{directory}}/le.bin', encoding.utf32_le) == 'A')
            test.assert(fs.read_text('{{directory}}/be.bin', encoding.utf32_be) == 'A')
            for selected in [encoding.utf32_le, encoding.utf32_be] {
                rejected := false
                filename := '{{directory}}/le.bin'
                if selected == encoding.utf32_le { filename = '{{directory}}/be.bin' }
                try { fs.read_text(filename, selected) } catch err { rejected = true }
                test.assert(rejected)
            }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var output = compiled.Output! + "\n$ErrorActionPreference = 'Stop'\n" +
            "foreach ($number in @(1252, 932, 54936)) {\n" +
            "$codec = [RukbatCodePageCodec]::new($number)\n" +
            "foreach ($unit in @(0xd800, 0xdc00)) {\n" +
            "$rejected = $false; try { $null = $codec.GetBytes([string][char]$unit) } catch { $rejected = $true }; " +
            "if (-not $rejected) { throw 'Invalid surrogate accepted' }\n}}\nWrite-Output 'passed'\n";
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(output)
            : await project.RunOutput(output);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }
}
