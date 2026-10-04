using System.Text;
using System.Text.Json;

namespace Rukbat.Tests;

public class ProcessEncodingTests : TestBase
{
    private const int TimeoutMilliseconds = 10000;
    private static string Literal(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'")
        .Replace("\r", "\\r").Replace("\n", "\\n") + "'";
    // Keep expected values ASCII in the generated test script, which is written without a BOM.
    private static string Expected(string value) => "json.parse<string>(" + Literal(JsonSerializer.Serialize(value)) + ")";

    private static string Child(Project project, string name, byte[] stdout, byte[] stderr, int exitCode = 0)
    {
        project.Write(name, $$"""
            $stdout = [Console]::OpenStandardOutput()
            $stderr = [Console]::OpenStandardError()
            $outBytes = [Convert]::FromBase64String('{{Convert.ToBase64String(stdout)}}')
            $errBytes = [Convert]::FromBase64String('{{Convert.ToBase64String(stderr)}}')
            $stdout.Write($outBytes, 0, $outBytes.Length)
            $stderr.Write($errBytes, 0, $errBytes.Length)
            $stdout.Flush()
            $stderr.Flush()
            exit {{exitCode}}
            """);
        return "['-NoLogo', '-NoProfile', '-NonInteractive', '-File', " + Literal(project.PathFor(name)) + "]";
    }

    private static Task<RunResult> Run(Project project, string output, string shell) => shell == "powershell"
        ? project.RunOutputWithWindowsPowerShell(output) : project.RunOutput(output);

    [Trait("Category", "Unit")]
    [Fact]
    public void EncodingIsRequiredAndStaticallyTyped()
    {
        Assert.Contains(Compile("import process\nprocess.run('tool.exe', [])").Diagnostics,
            d => d.Code == "RUK2005");
        Assert.Contains(Compile("import process\nprocess.run('tool.exe', [], process.options(null, 932))").Diagnostics,
            d => d.Code == "RUK2001");
        Assert.False(Compile("import process\nprocess.run('tool.exe', [], process.options(null, 'utf-8'))").Success);
        Assert.False(Compile("import encoding; import process\nprocess.run('tool.exe', [], process.options(null, encoding: encoding.utf8))").Success);
        Assert.False(Compile("import encoding; import process\nprocess.run('tool.exe', [], null, encoding.utf8, encoding.utf8)").Success);
        var output = Success("import encoding; import process\nselected := encoding.windows_932\nresult := process.run('tool.exe', [], process.options(null, selected))\nprint(result.stdout)");
        Assert.Contains("RukbatCodePageCodec", output);
        Assert.DoesNotContain("'fs.read_text'", output);
        Assert.DoesNotContain("RukbatMoveBatch", output);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", "windows-932")]
    [InlineData("pwsh", "windows-932")]
    [InlineData("powershell", "utf-8")]
    [InlineData("pwsh", "utf-8")]
    [InlineData("powershell", "utf-16le")]
    [InlineData("pwsh", "utf-16le")]
    [InlineData("powershell", "utf-16be")]
    [InlineData("pwsh", "utf-16be")]
    public async Task ExplicitEncodingReadsBothStreamsWithoutChangingExitCode(string shell, string encoding)
    {
        using var project = new Project();
        const string stdout = "日本髙\r\n";
        const string stderr = "髙\n";
        Encoding codec = encoding switch
        {
            "utf-16le" => new UnicodeEncoding(false, false, true),
            "utf-16be" => new UnicodeEncoding(true, false, true),
            _ => new UTF8Encoding(false, true)
        };
        // CP932 NEC-selected and IBM extension bytes must both decode to U+9AD9.
        byte[] outBytes = encoding == "windows-932" ? [0x93, 0xfa, 0x96, 0x7b, 0xee, 0xe0, 0x0d, 0x0a] : codec.GetBytes(stdout);
        byte[] errBytes = encoding == "windows-932" ? [0xfb, 0xfc, 0x0a] : codec.GetBytes(stderr);
        var exitCode = encoding == "windows-932" ? 0 : 7;
        var arguments = Child(project, "output.ps1", outBytes, errBytes, exitCode);
        project.Write("main.rukbat", $$"""
            import process
            import json
            import encoding
            selected := encoding.parse('{{encoding}}')
            result := process.run('powershell', {{arguments}}, process.options({{TimeoutMilliseconds}}, selected))
            print(result.stdout == {{Expected(stdout)}})
            print(result.stderr == {{Expected(stderr)}})
            print(result.exit_code)
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await Run(project, compiled.Output!, shell);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"True\nTrue\n{exitCode}\n", result.Output);
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task InvalidBytesInEitherStreamRaiseCatchableErrors(string shell)
    {
        using var project = new Project();
        var source = new StringBuilder("import process\nimport encoding\n");
        var invalid = new (string Encoding, byte[] Bytes)[]
        {
            ("utf-8", [0xff]), ("utf-16le", [0x41]), ("utf-16be", [0x41]),
            ("windows-932", [0x82]), ("windows-932", [0x82, 0x20])
        };
        for (var index = 0; index < invalid.Length; index++)
        {
            foreach (var stream in new[] { "stdout", "stderr" })
            {
                var arguments = Child(project, index + "-" + stream + ".ps1",
                    stream == "stdout" ? invalid[index].Bytes : [], stream == "stderr" ? invalid[index].Bytes : []);
                source.AppendLine($"try {{ process.run('powershell', {arguments}, process.options({TimeoutMilliseconds}, encoding.parse('{invalid[index].Encoding}'))) }} catch err {{ print('invalid') }}");
            }
        }
        project.Write("main.rukbat", source.ToString());
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await Run(project, compiled.Output!, shell);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Concat(Enumerable.Repeat("invalid\n", invalid.Length * 2)), result.Output);
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task UnsupportedEncodingIsRejectedBeforeLaunch(string shell)
    {
        using var project = new Project();
        var marker = project.PathFor("started.txt");
        project.Write("marker.ps1", "[IO.File]::WriteAllText('" + marker.Replace("'", "''") + "', 'started')");
        var arguments = "['-NoProfile', '-NonInteractive', '-File', " + Literal(project.PathFor("marker.ps1")) + "]";
        project.Write("main.rukbat", $$"""
            import process
            import encoding
            for name in ['UTF-8', 'shift-jis', '', 'windows-99999'] {
                try { process.run('powershell', {{arguments}}, process.options({{TimeoutMilliseconds}}, encoding.parse(name))) }
                catch err { print(err.message) }
            }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await Run(project, compiled.Output!, shell);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("unsupported encoding: UTF-8\nunsupported encoding: shift-jis\nunsupported encoding: \nunsupported encoding: windows-99999\n", result.Output);
        Assert.Equal("", result.Error);
        Assert.False(File.Exists(marker));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task EmptyStreamsAndBomCharactersArePreserved(string shell)
    {
        using var project = new Project();
        var empty = Child(project, "empty.ps1", [], []);
        var source = new StringBuilder("import process\nimport json\nimport encoding\n");
        source.AppendLine($"for selected in [encoding.utf8, encoding.utf16_le, encoding.utf16_be, encoding.windows_932] {{ result := process.run('powershell', {empty}, process.options({TimeoutMilliseconds}, selected)); print(result.stdout == ''); print(result.stderr == '') }}");
        foreach (var pair in new (string Name, Encoding Codec)[]
        {
            ("utf-8", new UTF8Encoding(false, true)),
            ("utf-16le", new UnicodeEncoding(false, false, true)),
            ("utf-16be", new UnicodeEncoding(true, false, true))
        })
        {
            const string value = "\ufeff日本\n";
            var bytes = pair.Codec.GetBytes(value);
            var arguments = Child(project, pair.Name + ".ps1", bytes, bytes);
            source.AppendLine($"if true {{ result := process.run('powershell', {arguments}, process.options({TimeoutMilliseconds}, encoding.parse('{pair.Name}'))); print(result.stdout == {Expected(value)}); print(result.stderr == {Expected(value)}) }}");
        }
        project.Write("main.rukbat", source.ToString());
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = await Run(project, compiled.Output!, shell);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(string.Concat(Enumerable.Repeat("True\n", 14)), result.Output);
        Assert.Equal("", result.Error);
    }
}
