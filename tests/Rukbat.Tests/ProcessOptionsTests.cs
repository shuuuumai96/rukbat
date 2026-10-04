namespace Rukbat.Tests;

public class ProcessOptionsTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("process.run('x', [], null, encoding.utf8)")]
    [InlineData("process.run('x', [], null)")]
    [InlineData("process.options(1.0, encoding.utf8)")]
    [InlineData("process.options(1, 'utf-8')")]
    [InlineData("o := process.options(1, encoding.utf8); o.working_dir = 1")]
    [InlineData("o := process.options(1, encoding.utf8); o.environment = ['A=B']")]
    [InlineData("o := process.options(1, encoding.utf8); o.stdin = 'text'")]
    [InlineData("o := process.options(1, encoding.utf8); o.stdin = process.Input { text: 'x', encoding: 'utf-8' }")]
    public void ChecksOptionsAndRejectsTheOldSignature(string source) => Assert.False(Compile("import process; import encoding\n" + source).Success);

    [Trait("Category", "Unit")]
    [Fact]
    public void SupportsExplicitOptionsStructs()
    {
        var compiled = Compile("""
            import process; import encoding
            options := process.Options {
                timeout_ms: null,
                output_encoding: encoding.utf8,
                working_dir: null,
                environment: [process.EnvironmentVariable { name: 'A', value: null }],
                stdin: process.Input { text: '', encoding: encoding.utf8 }
            }
            process.run('tool.exe', [], options)
            """);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
    }

    private const string Child = """
        [Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
        switch ($args[0]) {
            'context' {
                $variables = [Environment]::GetEnvironmentVariables()
                [Console]::Write((@{
                    directory = [Environment]::CurrentDirectory
                    marker = [IO.File]::ReadAllText('relative.txt')
                    inherited = [Environment]::GetEnvironmentVariable('RUKBAT_INHERITED')
                    added = [Environment]::GetEnvironmentVariable('RUKBAT_ADDED')
                    removed = $variables.Contains('RUKBAT_REMOVED')
                    empty = $variables.Contains('RUKBAT_EMPTY') -and $variables['RUKBAT_EMPTY'] -eq ''
                } | ConvertTo-Json -Compress))
            }
            'input' {
                $bytes = [IO.MemoryStream]::new()
                [Console]::OpenStandardInput().CopyTo($bytes)
                $received = [Convert]::ToBase64String($bytes.ToArray())
                $japan = [string][char]0x65E5 + [char]0x672C
                $output = [Text.Encoding]::GetEncoding(932).GetBytes($received + ':' + $japan)
                [Console]::OpenStandardOutput().Write($output, 0, $output.Length)
                [Console]::OpenStandardError().Write($output, 0, $output.Length)
            }
            'duplex' {
                $output = [Text.Encoding]::ASCII.GetBytes([string]::new('x', 262144))
                [Console]::OpenStandardOutput().Write($output, 0, $output.Length)
                [Console]::OpenStandardError().Write($output, 0, $output.Length)
                $inputBytes = [IO.MemoryStream]::new()
                [Console]::OpenStandardInput().CopyTo($inputBytes)
                [Console]::Write(';input=' + $inputBytes.Length)
            }
            'block' { Start-Sleep -Seconds 30 }
            'close' { [Console]::OpenStandardInput().Dispose(); exit 7 }
            'mark' { [IO.File]::WriteAllText($args[1], 'started') }
            'eof' { [Console]::Write([Console]::In.ReadToEnd().Length) }
        }
        """;

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ChildEnvironmentAndDirectoryDoNotAlterTheParent(string shell)
    {
        using var project = new Project();
        Directory.CreateDirectory(project.PathFor("work"));
        project.Write("work/relative.txt", "from working directory");
        project.Write("child.ps1", Child);
        var root = project.DirectoryPath.Replace('\\', '/');
        var working = Path.GetRelativePath(Path.GetTempPath(), project.PathFor("work")).Replace('\\', '/');
        project.Write("main.rukbat", $$"""
            import process; import encoding; import env; import json; import test; import path
            env.set('RUKBAT_INHERITED', 'inherited')
            env.set('RUKBAT_REMOVED', 'parent')
            env.remove('RUKBAT_EMPTY')
            env.remove('RUKBAT_ADDED')
            original_directory := powershell<string>('Invoke-Expression', '[Environment]::CurrentDirectory')
            original_path := env.get('PATH')
            options := process.options(10000, encoding.utf8)
            options.working_dir = '{{working}}'
            options.environment = [
                process.EnvironmentVariable { name: 'RUKBAT_ADDED', value: 'child' },
                process.EnvironmentVariable { name: 'rukbat_removed', value: null },
                process.EnvironmentVariable { name: 'RUKBAT_EMPTY', value: '' }
            ]
            struct Context { directory: string; marker: string; inherited: string; added: string; removed: bool; empty: bool }
            result := process.run('powershell', ['-NoProfile', '-File', '{{root}}/child.ps1', 'context'], options)
            context := json.parse<Context>(result.stdout)
            test.assert(path.equal(context.directory, '{{root}}/work'))
            test.assert(context.marker == 'from working directory')
            test.assert(context.inherited == 'inherited')
            test.assert(context.added == 'child')
            test.assert(!context.removed)
            test.assert(context.empty)
            test.assert(env.get('RUKBAT_ADDED') == null)
            test.assert(env.get('RUKBAT_EMPTY') == null)
            removed := env.get('RUKBAT_REMOVED')
            if removed != null { test.assert(removed == 'parent') } else { test.fail('Parent environment changed') }
            test.assert(powershell<string>('Invoke-Expression', '[Environment]::CurrentDirectory') == original_directory)
            tools := powershell<string>('Invoke-Expression', '[IO.Path]::GetDirectoryName((Get-Command powershell.exe).Source)')
            options.environment = [process.EnvironmentVariable { name: 'PATH', value: tools }]
            result = process.run('powershell', ['-NoProfile', '-Command', '[Console]::Write(42)'], options)
            test.assert(result.stdout == '42')
            options.environment = [process.EnvironmentVariable { name: 'PATH', value: null }]
            missing := false
            try { process.run('powershell', ['-NoProfile', '-Command', '[Console]::Write(42)'], options) }
            catch err { missing = true }
            test.assert(missing)
            path_after := env.get('PATH')
            if original_path != null { if path_after != null { test.assert(original_path == path_after) } }
            print('passed')
            """);
        await RunVerified(project, shell);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task InputEncodingIsIndependentAndLargeDuplexInputDoesNotDeadlock(string shell)
    {
        using var project = new Project();
        project.Write("child.ps1", Child);
        var child = project.PathFor("child.ps1").Replace('\\', '/');
        project.Write("main.rukbat", $$"""
            import process; import encoding; import json; import test; import text
            japan := json.parse<string>('"\\u65e5\\u672c"')
            options := process.options(10000, encoding.windows_932)
            options.stdin = process.Input { text: japan, encoding: encoding.windows_932 }
            result := process.run('powershell', ['-NoProfile', '-File', '{{child}}', 'input'], options)
            test.assert(result.stdout == 'k/qWew==:' + japan)
            test.assert(result.stderr == result.stdout)
            options.stdin = process.Input { text: japan, encoding: encoding.utf8 }
            result = process.run('powershell', ['-NoProfile', '-File', '{{child}}', 'input'], options)
            test.assert(result.stdout == '5pel5pys:' + japan)
            options = process.options(10000, encoding.utf8)
            test.assert(options.working_dir == null)
            test.assert(options.stdin == null)
            test.assert(options.environment.length == 0)
            copy := options
            copy.environment = [process.EnvironmentVariable { name: 'COPY_ONLY', value: 'x' }]
            copy.stdin = process.Input { text: 'copied', encoding: encoding.utf8 }
            test.assert(options.environment.length == 0)
            test.assert(options.stdin == null)
            options.stdin = process.Input { text: '', encoding: encoding.utf8 }
            result = process.run('powershell', ['-NoProfile', '-File', '{{child}}', 'eof'], options)
            test.assert(result.stdout == '0')
            options.stdin = null
            result = process.run('powershell', ['-NoProfile', '-File', '{{child}}', 'eof'], options)
            test.assert(result.stdout == '0')
            payload := powershell<string>('Invoke-Expression', "[string]::new('q', 524288)")
            options.stdin = process.Input { text: payload, encoding: encoding.utf8 }
            result = process.run('powershell', ['-NoProfile', '-File', '{{child}}', 'duplex'], options)
            test.assert(text.ends_with(result.stdout, ';input=524288'))
            test.assert(text.length(result.stderr) == 262144)
            print('passed')
            """);
        await RunVerified(project, shell);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BlockedAndInterruptedInputIsCatchable(string shell)
    {
        using var project = new Project();
        project.Write("child.ps1", Child);
        var child = project.PathFor("child.ps1").Replace('\\', '/');
        project.Write("main.rukbat", $$"""
            import process; import encoding; import test; import text
            payload := powershell<string>('Invoke-Expression', "[string]::new('q', 1048576)")
            options := process.options(1500, encoding.utf8)
            options.stdin = process.Input { text: payload, encoding: encoding.utf8 }
            timed_out := false
            try { process.run('powershell', ['-NoProfile', '-File', '{{child}}', 'block'], options) }
            catch err { timed_out = true; test.assert(text.contains(err.message, 'timed out')) }
            test.assert(timed_out)
            options.timeout_ms = 10000
            closed := false
            try { process.run('powershell', ['-NoProfile', '-File', '{{child}}', 'close'], options) }
            catch err { closed = true }
            test.assert(closed)
            print('passed')
            """);
        await RunVerified(project, shell);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task InvalidConditionsNeverStartTheChild(string shell)
    {
        using var project = new Project();
        project.Write("child.ps1", Child);
        var root = project.DirectoryPath.Replace('\\', '/');
        string[] conditions = [
            "options.timeout_ms = 0", "options.timeout_ms = 2147483648",
            $"options.working_dir = '{root}/missing'", $"options.working_dir = '{root}/child.ps1'",
            "options.environment = [process.EnvironmentVariable { name: '', value: 'x' }]",
            "options.environment = [process.EnvironmentVariable { name: 'A=B', value: 'x' }]",
            "options.environment = [process.EnvironmentVariable { name: 'PATH', value: 'x' }, process.EnvironmentVariable { name: 'Path', value: null }]",
            "options.environment = [process.EnvironmentVariable { name: 'A' + nul, value: 'x' }]",
            "options.environment = [process.EnvironmentVariable { name: 'A', value: nul }]",
            "options.environment = [process.EnvironmentVariable { name: 'A', value: malformed }]",
            "options.stdin = process.Input { text: japan, encoding: encoding.ascii }",
            "options.stdin = process.Input { text: malformed, encoding: encoding.utf8 }",
            "options.stdin = process.Input { text: oversized, encoding: encoding.utf8 }",
            "options.stdin = process.Input { text: oversized, encoding: encoding.windows_932 }"
        ];
        var source = "import process; import encoding; import fs; import json; import test\n" +
            "nul := powershell<string>('Invoke-Expression', '[string][char]0')\n" +
            "malformed := powershell<string>('Invoke-Expression', '[string][char]0xD800')\n" +
            "japan := json.parse<string>('\"\\\\u65e5\\\\u672c\"')\n" +
            "oversized := powershell<string>('Invoke-Expression', \"[string]::new('a', 67108865)\")\n";
        for (int i = 0; i < conditions.Length; i++) {
            var marker = $"{root}/marker-{i}";
            source += $"if true {{ options := process.options(10000, encoding.utf8)\n{conditions[i]}\n" +
                $"rejected := false\ntry {{ process.run('powershell', ['-NoProfile', '-File', '{root}/child.ps1', 'mark', '{marker}'], options) }} catch err {{ rejected = true }}\n" +
                $"test.assert(rejected)\ntest.assert(!fs.is_file('{marker}'))\n}}\n";
        }
        project.Write("main.rukbat", source + "print('passed')\n");
        await RunVerified(project, shell);
    }

    private static async Task RunVerified(Project project, string shell)
    {
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }
}
