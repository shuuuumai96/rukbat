namespace Auvil.Tests;

public partial class CompilerTests
{
    [Fact]
    public void GeneratedScriptIncludesOnlyImportedStandardLibraryRuntimes()
    {
        var plain = Success("print('hello')");
        Assert.DoesNotContain("Unknown standard-library operation", plain);
        Assert.DoesNotContain("JSON value", plain);

        var json = Success("import json\nprint(json.encode([1]))");
        Assert.Contains("JSON value", json);
        Assert.DoesNotContain("Unknown standard-library operation", json);

        var files = Success("import files\nprint(files.exists('x'))");
        Assert.Contains("Unknown standard-library operation", files);
        Assert.DoesNotContain("JSON value", files);
        Assert.Contains("'files.exists'", files);
        Assert.DoesNotContain("'env.get'", files);
        Assert.DoesNotContain("'process.run'", files);
    }

    [Fact]
    public async Task FilesPathAndEnvironmentModulesUseTypedContracts()
    {
        using var project = new Project();
        var directory = project.DirectoryPath.Replace("\\", "\\\\");
        var variable = ("AUVIL_TEST_" + Guid.NewGuid().ToString("N")).Replace("\\", "\\\\");
        project.Write("main.auv", $$"""
            import files
            import path
            import env
            target: string = path.join(['{{directory}}', 'data.txt'])
            files.write(target, 'hello')
            print(files.exists(target))
            print(files.read(target))
            print(path.filename(target))
            print(path.extension(target))
            names: string[] = files.list('{{directory}}', '*.txt')
            print(names.length)
            env.set('{{variable}}', 'value')
            value: string? = env.get('{{variable}}')
            if value != null { print(value) }
            env.remove('{{variable}}')
            print(env.get('{{variable}}') == null)
            """);
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("True\nhello\ndata.txt\n.txt\n1\nvalue\nTrue", result.Output.TrimEnd('\n'));
        Assert.Equal("", result.Error);
        var bytes = File.ReadAllBytes(project.PathFor("data.txt"));
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf);
    }

    [Fact]
    public async Task EnvironmentRejectsEmptySetAndPreservesInheritedEmptyValue()
    {
        using var project = new Project();
        var variable = "AUVIL_EMPTY_" + Guid.NewGuid().ToString("N");
        project.Write("main.auv", $$"""
            import env
            value: string? = env.get('{{variable}}')
            if value == null { print('missing') } else { print(value == '') }
            try { env.set('{{variable}}', '') } catch err { print(err.message) }
            env.remove('{{variable}}')
            print(env.get('{{variable}}') == null)
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        var modern = await project.RunOutputWithEnvironment(compiled.Output!, "pwsh", variable, "");
        Assert.Equal(0, modern.ExitCode);
        Assert.Equal("True\nenv.set does not accept an empty value; use env.remove to delete '" + variable + "'.\nTrue", modern.Output.TrimEnd('\n'));
        Assert.Equal("", modern.Error);

        var windows = await project.RunOutputWithEnvironment(compiled.Output!, "powershell", variable, "");
        Assert.Equal(0, windows.ExitCode);
        Assert.Equal(modern.Output, windows.Output);
        Assert.Equal("", windows.Error);
    }

    [Fact]
    public async Task ProcessRunReturnsNonzeroExitAndSeparateOutput()
    {
        using var project = new Project();
        project.Write("main.auv", """
            import process
            result: process.Result = process.run('powershell', [
                '-NoLogo', '-NoProfile', '-Command',
                '[Console]::Out.Write("out"); [Console]::Error.Write("err"); exit 7'
            ])
            print(result.exit_code)
            print(result.stdout)
            print(result.stderr)
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        foreach (var result in new[]
        {
            await project.RunOutput(compiled.Output!),
            await project.RunOutputWithWindowsPowerShell(compiled.Output!)
        })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("7\nout\nerr", result.Output.TrimEnd('\n'));
            Assert.Equal("", result.Error);
        }
    }

    [Fact]
    public async Task FilesReadRejectsNonUtf8OnBothPowerShellTargets()
    {
        using var project = new Project();
        var path = project.PathFor("utf16.txt").Replace("\\", "\\\\");
        File.WriteAllBytes(project.PathFor("utf16.txt"), [0xff, 0xfe, 0x41, 0x00]);
        project.Write("main.auv", $$"""
            import files
            try { files.read('{{path}}') } catch err { print('invalid UTF-8') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        foreach (var result in new[]
        {
            await project.RunOutput(compiled.Output!),
            await project.RunOutputWithWindowsPowerShell(compiled.Output!)
        })
        {
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("invalid UTF-8", result.Output.TrimEnd('\n'));
            Assert.Equal("", result.Error);
        }
    }

    [Fact]
    public async Task ProcessLaunchFailureIsAnAuvilError()
    {
        using var project = new Project();
        project.Write("main.auv", "import process\nprocess.run('missing-auvil-executable', [])");
        var result = await project.Run();
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("missing-auvil-executable", result.Error);
    }

    [Theory]
    [InlineData("import files\nfiles.read(1)")]
    [InlineData("import path\npath.join('one')")]
    [InlineData("import env\nenv.set('x', null)")]
    [InlineData("import process\nprocess.run('x', [1])")]
    [InlineData("import json as j")]
    [InlineData("import './json.auv' as json")]
    public void RejectsInvalidStandardLibraryCallsAndImports(string source)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.NotEmpty(result.Diagnostics);
    }
}
