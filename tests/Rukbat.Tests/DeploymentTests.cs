using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests;

public class DeploymentTests : TestBase
{
    private static string SourcePath(string path) => path.Replace('\\', '/').Replace("'", "\\'");

    private static async Task<RunResult> RunDeployedScript(string shell, string script, string working, string nativeWorking, bool directFile = false)
    {
        var info = new ProcessStartInfo(shell)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
            CreateNoWindow = true, WorkingDirectory = nativeWorking,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
        };
        foreach (var value in new[] { "-NoLogo", "-NoProfile", "-NonInteractive" })
            info.ArgumentList.Add(value);
        info.ArgumentList.Add(directFile ? "-File" : "-Command");
        info.ArgumentList.Add(directFile ? script :
            "Set-Location -LiteralPath '" + working.Replace("'", "''") + "'; & '" + script.Replace("'", "''") + "'");
        using var child = Process.Start(info)!;
        var stdout = child.StandardOutput.ReadToEndAsync();
        var stderr = child.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try { await child.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { child.Kill(true); throw new TimeoutException("Deployment test timed out."); }
        return new(child.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task DeployedScriptKeepsEntryWorkingDirectoryAndScriptDirectory(string shell)
        => await AssertDeployedScriptKeepsEntryWorkingDirectoryAndScriptDirectory(shell);

    [Trait("Smoke", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task DeployedScriptKeepsEntryWorkingDirectoryAndScriptDirectoryOnWindowsPowerShell51()
        => await AssertDeployedScriptKeepsEntryWorkingDirectoryAndScriptDirectory("powershell");

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task DirectFileInvocationKeepsLiteralProcessWorkingDirectory(string shell)
        => await AssertDeployedScriptKeepsEntryWorkingDirectoryAndScriptDirectory(
            shell, "work 日本語 [brackets] ' ; $ &", directFile: true);

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task SessionInvocationKeepsLiteralPowerShellLocationInsteadOfProcessDirectory(string shell)
        => await AssertDeployedScriptKeepsEntryWorkingDirectoryAndScriptDirectory(
            shell, "work 日本語 [brackets] ' ; $ &");

    private async Task AssertDeployedScriptKeepsEntryWorkingDirectoryAndScriptDirectory(
        string shell, string workDirectory = "work", bool directFile = false)
    {
        using var project = new Project();
        project.Write(workDirectory + "/data.txt", "working");
        project.Write("other/data.txt", "wrong");
        project.Write("deploy/config.json", "adjacent");
        var working = project.PathFor(workDirectory);
        var other = project.PathFor("other");
        var windowsShell = Path.Combine(Environment.GetEnvironmentVariable("SystemRoot")!, "System32", "WindowsPowerShell", "v1.0", "powershell.exe");
        project.Write("source/helper.rukbat", "import path\nfn directory(): string { return path.script_dir() }\nfn working(): string { return path.working_dir() }");
        project.Write("source/main.rukbat", $$"""
            import encoding; import fs
            import path
            import process
            import "./helper.rukbat" as helper
            print(path.script_dir())
            print(helper.directory())
            print(path.working_dir())
            print(helper.working())
            powershell<void>('Set-Location', LiteralPath: '{{SourcePath(other)}}')
            powershell<void>('Invoke-Expression', "[Environment]::CurrentDirectory = '{{SourcePath(project.PathFor("deploy"))}}'")
            print(path.working_dir())
            print(helper.working())
            print(fs.read_text('data.txt', encoding.utf8))
            fs.write_text('result.txt', 'saved', encoding.utf8)
            print(fs.is_file('result.txt'))
            names := fs.list_files('.', '*.txt')
            print(names.length)
            print(fs.read_text(path.join([path.script_dir(), 'config.json']), encoding.utf8))
            result: process.Result = process.run('powershell', ['-NoLogo', '-NoProfile', '-Command', '[Console]::OutputEncoding = [Text.Encoding]::UTF8; [Console]::Write([Environment]::CurrentDirectory)'], process.options(null, encoding.utf8))
            print(result.stdout)
            relative: process.Result = process.run('{{SourcePath(Path.GetRelativePath(working, windowsShell))}}', ['-NoLogo', '-NoProfile', '-Command', '[Console]::OutputEncoding = [Text.Encoding]::UTF8; [Console]::Write([Environment]::CurrentDirectory)'], process.options(null, encoding.utf8))
            print(relative.stdout)
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("source/main.rukbat"));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain(project.PathFor("source"), compiled.Output!);
        File.Delete(project.PathFor("source/main.rukbat"));
        File.Delete(project.PathFor("source/helper.rukbat"));
        var script = project.PathFor("deploy/job.ps1");
        File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployedScript(shell, script, working, directFile ? working : other, directFile);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        Assert.Equal($"{project.PathFor("deploy")}\n{project.PathFor("deploy")}\n{working}\n{working}\n{working}\n{working}\nworking\nTrue\n2\nadjacent\n{working}\n{working}\n", result.Output);
        Assert.Equal("saved", File.ReadAllText(Path.Combine(working, "result.txt")));
        Assert.False(File.Exists(project.PathFor("other/result.txt")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", "services/user.rukbat", "./services/user.rukbat")]
    [InlineData("pwsh", "services/user.rukbat", "./services/user.rukbat")]
    [InlineData("powershell", "../shared/config.rukbat", "../shared/config.rukbat")]
    [InlineData("pwsh", "../shared/config.rukbat", "../shared/config.rukbat")]
    public async Task RelocatedErrorsUseRelativeSourceNamesWithoutSourceFiles(string shell, string name, string import)
    {
        using var project = new Project();
        var imported = "source/" + name;
        project.Write(imported, "fn fail(): void {\n    throw Error {message: 'origin'}\n}");
        project.Write("source/main.rukbat", $"import '{import}' as service\ntry {{ service.fail() }} catch err {{ throw err }}");
        var compiled = new Compiler().CompileFile(project.PathFor("source/main.rukbat"));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain(project.DirectoryPath, compiled.Output!);
        Assert.DoesNotContain(project.DirectoryPath.Replace('\\', '/'), compiled.Output!);
        File.Delete(project.PathFor(imported));
        File.Delete(project.PathFor("source/main.rukbat"));
        project.Write("deploy/job.ps1", "");
        File.WriteAllText(project.PathFor("deploy/job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployedScript(shell, project.PathFor("deploy/job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal($"{name}:2: error: origin", result.Error.Trim());
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task PathPredicatesAndEmptyJoinFollowContracts(string shell)
        => await AssertPathPredicatesAndEmptyJoinFollowContracts(shell);

    [Trait("Smoke", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task PathPredicatesAndEmptyJoinFollowContractsOnWindowsPowerShell51()
        => await AssertPathPredicatesAndEmptyJoinFollowContracts("powershell");

    private async Task AssertPathPredicatesAndEmptyJoinFollowContracts(string shell)
    {
        using var project = new Project();
        project.Write("present.txt", "exists");
        project.Write("main.rukbat", """
            import encoding; import fs
            import path
            print(fs.is_file('present.txt'))
            print(fs.is_file('missing.txt'))
            print(fs.is_file('.'))
            print(fs.is_file(''))
            print(fs.is_file('?:invalid'))
            print(fs.is_file('bad\npath'))
            try { path.join([]) } catch err { print('empty join') }
            try { fs.read_text('missing.txt', encoding.utf8) } catch err { print('read error') }
            try { fs.write_text('missing/child.txt', 'x', encoding.utf8) } catch err { print('write error') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployedScript(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("True\nFalse\nFalse\nFalse\nFalse\nFalse\nempty join\nread error\nwrite error\n", result.Output);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task EprintMatchesPrintWithoutImports(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", "print(''); eprint(''); print('line\\n'); eprint('line\\n'); print(42); eprint(42); print(true); eprint(true); eprint('日本語')");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployedScript(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(result.Output + "日本語\n", result.Error.Replace("\r\n", "\n"));
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("import stderr")]
    [InlineData("stderr.print('removed')")]
    [InlineData("fn eprint(): void {}")]
    [InlineData("eprint(text: 'named')")]
    [InlineData("eprint([1])")]
    [InlineData("value: string? = null; eprint(value)")]
    [InlineData("eprint(print('void'))")]
    [InlineData("import path; path.script_dir('extra')")]
    public void RejectsInvalidDeploymentApis(string source) => Assert.NotEmpty(new Compiler().Check(source));
}
