using System.Text;

namespace Rukbat.Tests;

public class FileEncodingTests : TestBase
{
    private static string RukbatLiteral(string value) => value.Replace("\\", "/").Replace("'", "\\'");

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task Gb18030RejectsUnmappedSequencesWithoutReplacingThem(string shell)
    {
        using var project = new Project();
        File.WriteAllBytes(project.PathFor("gap.bin"), [0x84, 0x31, 0xa5, 0x30]);
        File.WriteAllBytes(project.PathFor("overflow.bin"), [0xfe, 0x39, 0xfe, 0x39]);
        File.WriteAllBytes(project.PathFor("question.bin"), [0x3f]);
        var directory = RukbatLiteral(project.DirectoryPath);
        project.Write("main.rukbat", $$"""
            import fs; import encoding; import test
            rejected := 0
            try { fs.read_text('{{directory}}/gap.bin', encoding.gb18030) } catch err { rejected = rejected + 1 }
            try { fs.read_text('{{directory}}/overflow.bin', encoding.gb18030) } catch err { rejected = rejected + 1 }
            test.assert(rejected == 2)
            test.assert(fs.read_text('{{directory}}/question.bin', encoding.gb18030) == '?')
            print('passed')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task Windows932ReadsExternalNecAndIbmMappingsWithoutReplacingInvalidBytes(string shell)
    {
        using var project = new Project();
        File.WriteAllBytes(project.PathFor("nec.csv"), [0x6e, 0x61, 0x6d, 0x65, 0x0d, 0x0a, 0xee, 0xe0]);
        File.WriteAllBytes(project.PathFor("ibm.txt"), [0xfb, 0xfc]);
        File.WriteAllBytes(project.PathFor("invalid.txt"), [0x82, 0x20]);
        var directory = RukbatLiteral(project.DirectoryPath);
        project.Write("main.rukbat", $$"""
            import encoding; import fs
            import csv
            import json
            expected := json.parse<string>('"\\u9ad9"')
            rows := csv.parse(fs.read_text('{{directory}}/nec.csv', encoding.windows_932))
            print(rows[1][0] == expected)
            print(fs.read_text('{{directory}}/ibm.txt', encoding.windows_932) == expected)
            try { fs.read_text('{{directory}}/invalid.txt', encoding.windows_932) } catch err { print('invalid') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell"
            ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("True\nTrue\ninvalid\n", result.Output);
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void FilesTextOperationsRequireAnExplicitEncoding()
    {
        Assert.False(Compile("import fs\nfs.read_text('input.txt')").Success);
        Assert.False(Compile("import fs\nfs.write_text('output.txt', 'text')").Success);
        Assert.True(Compile("import encoding; import fs\nname := encoding.utf8\nfs.write_text('output.txt', 'text', name)\nfs.read_text('output.txt', name)").Success);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesRoundTripAllExplicitEncodingsWithoutBom(string shell)
    {
        using var project = new Project();
        var directory = RukbatLiteral(project.DirectoryPath);
        project.Write("main.rukbat", $$"""
            import encoding; import fs
            import json
            selected := encoding.utf8
            text := json.parse<string>('"ASCII \\u65e5\\u672c\\u8a9e \\uff8a\\uff9d\\uff76\\uff85"')
            cp932_text := json.parse<string>('"ASCII \\u65e5\\u672c\\u8a9e \\uff8a\\uff9d\\uff76\\uff85 \\u9ad9"')
            fs.write_text('{{directory}}/utf8.txt', text, selected)
            fs.write_text('{{directory}}/le.txt', text, encoding.utf16_le)
            fs.write_text('{{directory}}/be.txt', text, encoding.utf16_be)
            fs.write_text('{{directory}}/cp932.txt', cp932_text, encoding.windows_932)
            fs.write_text('{{directory}}/empty.txt', '', encoding.windows_932)
            print(fs.read_text('{{directory}}/utf8.txt', encoding.utf8) == text)
            print(fs.read_text('{{directory}}/le.txt', encoding.utf16_le) == text)
            print(fs.read_text('{{directory}}/be.txt', encoding.utf16_be) == text)
            print(fs.read_text('{{directory}}/cp932.txt', encoding.windows_932) == cp932_text)
            """);

        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell"
            ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal("True\nTrue\nTrue\nTrue\n", result.Output);
        Assert.Empty(File.ReadAllBytes(project.PathFor("empty.txt")));
        Assert.False(File.ReadAllBytes(project.PathFor("utf8.txt")).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.False(File.ReadAllBytes(project.PathFor("le.txt")).AsSpan().StartsWith(new byte[] { 0xff, 0xfe }));
        Assert.False(File.ReadAllBytes(project.PathFor("be.txt")).AsSpan().StartsWith(new byte[] { 0xfe, 0xff }));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesReadValidatesBomAndEncodingNamesStrictly(string shell)
    {
        using var project = new Project();
        File.WriteAllBytes(project.PathFor("utf8-bom.txt"), [0xef, 0xbb, 0xbf, 0x41]);
        File.WriteAllBytes(project.PathFor("le-bom.txt"), [0xff, 0xfe, 0x41, 0x00]);
        File.WriteAllBytes(project.PathFor("be-bom.txt"), [0xfe, 0xff, 0x00, 0x41]);
        File.WriteAllBytes(project.PathFor("odd.txt"), [0x41]);
        File.WriteAllBytes(project.PathFor("invalid-cp932.txt"), [0x82]);
        var directory = RukbatLiteral(project.DirectoryPath);
        project.Write("main.rukbat", $$"""
            import encoding; import fs
            print(fs.read_text('{{directory}}/utf8-bom.txt', encoding.utf8))
            print(fs.read_text('{{directory}}/le-bom.txt', encoding.utf16_le))
            print(fs.read_text('{{directory}}/be-bom.txt', encoding.utf16_be))
            try { fs.read_text('{{directory}}/le-bom.txt', encoding.utf16_be) } catch err { print('opposite') }
            try { fs.read_text('{{directory}}/odd.txt', encoding.utf16_le) } catch err { print('odd') }
            try { fs.read_text('{{directory}}/invalid-cp932.txt', encoding.windows_932) } catch err { print('cp932 invalid') }
            try { fs.read_text('{{directory}}/utf8-bom.txt', encoding.parse('shift-jis')) } catch err { print(err.message) }
            try { fs.read_text('{{directory}}/utf8-bom.txt', encoding.parse('UTF-8')) } catch err { print(err.message) }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell"
            ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("A\nA\nA\nopposite\nodd\ncp932 invalid\nunsupported encoding: shift-jis\nunsupported encoding: UTF-8\n", result.Output);
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ConversionFailuresPreserveDestinationsAndLeaveNoTemporaryFiles(string shell)
    {
        using var project = new Project();
        foreach (var name in new[] { "utf8", "utf16", "cp932" }) File.WriteAllBytes(project.PathFor(name + ".txt"), Encoding.ASCII.GetBytes(name));
        var directory = RukbatLiteral(project.DirectoryPath);
        project.Write("main.rukbat", $$"""
            import encoding; import fs
            import json
            invalid := json.parse<string>('"\\uD800"')
            emoji := json.parse<string>('"\\uD83D\\uDE00"')
            try { fs.write_text('{{directory}}/utf8.txt', invalid, encoding.utf8) } catch err { print('utf8') }
            try { fs.write_text('{{directory}}/utf16.txt', invalid, encoding.utf16_le) } catch err { print('utf16') }
            try { fs.write_text('{{directory}}/cp932.txt', emoji, encoding.windows_932) } catch err { print('cp932') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell"
            ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
            : await project.RunOutput(compiled.Output!);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("utf8\nutf16\ncp932\n", result.Output);
        Assert.Equal("", result.Error);
        Assert.Equal(Encoding.ASCII.GetBytes("utf8"), File.ReadAllBytes(project.PathFor("utf8.txt")));
        Assert.Equal(Encoding.ASCII.GetBytes("utf16"), File.ReadAllBytes(project.PathFor("utf16.txt")));
        Assert.Equal(Encoding.ASCII.GetBytes("cp932"), File.ReadAllBytes(project.PathFor("cp932.txt")));
        Assert.Empty(Directory.EnumerateFiles(project.DirectoryPath, ".rukbat-write-*"));
    }

    [Trait("Category", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Trait("Smoke", "PowerShell51")]
    [Fact]
    public async Task StandaloneGeneratedScriptUsesWindows932OnWindowsPowerShell51()
    {
        using var project = new Project();
        var path = RukbatLiteral(project.PathFor("legacy.csv"));
        project.Write("main.rukbat", $$"""
            import csv
            import encoding; import fs
            import json
            source := json.parse<string>('"\\u540d\\u524d,\\u5024\\n\\u592a\\u90ce,\\uff8a\\uff9d\\uff76\\uff85"')
            fs.write_text('{{path}}', source, encoding.windows_932)
            print(csv.parse(fs.read_text('{{path}}', encoding.windows_932))[1][1] == json.parse<string>('"\\uff8a\\uff9d\\uff76\\uff85"'))
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("standalone.ps1");
        File.WriteAllText(script, compiled.Output!, new UTF8Encoding(true));
        File.Delete(project.PathFor("main.rukbat"));
        var result = await RunDeployed("powershell", script, project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("True\n", result.Output);
        Assert.Equal("", result.Error);
    }
}
