using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests;

public class RecursiveFilesTests : TestBase
{
    private static void CreateJunction(string path, string target)
    {
        var start = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{path}\" \"{target}\""
        };
        using var command = Process.Start(start)!;
        var output = command.StandardOutput.ReadToEnd();
        var error = command.StandardError.ReadToEnd();
        command.WaitForExit();
        Assert.True(command.ExitCode == 0, $"Could not create a directory junction for the reparse-point contract test: {output}{error}");
    }

    private static string RukbatLiteral(string value) => value.Replace("\\", "/").Replace("'", "\\'");

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesListRecursiveUsesFixedWorkingDirectoryAndReturnsUniqueAbsolutePaths(string shell)
        => await AssertFilesListRecursiveUsesFixedWorkingDirectoryAndReturnsUniqueAbsolutePaths(shell);

    [Trait("Smoke", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task FilesListRecursiveUsesFixedWorkingDirectoryAndReturnsUniqueAbsolutePathsOnWindowsPowerShell51()
        => await AssertFilesListRecursiveUsesFixedWorkingDirectoryAndReturnsUniqueAbsolutePaths("powershell");

    private async Task AssertFilesListRecursiveUsesFixedWorkingDirectoryAndReturnsUniqueAbsolutePaths(string shell)
    {
        using var project = new Project();
        project.Write("work/[data]/top.json", "top");
        project.Write("work/[data]/first/result.json", "first");
        project.Write("work/[data]/second/result.json", "second");
        project.Write("work/[data]/first/skip.txt", "skip");
        project.Write("work/[data]/test1.csv", "one");
        project.Write("work/[data]/test12.csv", "two");
        project.Write("work/[data]/hidden.json", "hidden");
        project.Write("work/[data]/hidden-dir/inside.json", "hidden directory");
        project.Write("work/[data]/system.json", "system");
        project.Write("work/[data]/system-dir/inside.json", "system directory");
        project.Write("outside/escaped.json", "outside");
        var root = project.PathFor("work/[data]");
        var hidden = project.PathFor("work/[data]/hidden.json");
        var hiddenDirectory = project.PathFor("work/[data]/hidden-dir");
        var system = project.PathFor("work/[data]/system.json");
        var systemDirectory = project.PathFor("work/[data]/system-dir");
        File.SetAttributes(hidden, FileAttributes.Hidden);
        File.SetAttributes(hiddenDirectory, FileAttributes.Hidden | FileAttributes.Directory);
        File.SetAttributes(system, FileAttributes.System);
        File.SetAttributes(systemDirectory, FileAttributes.System | FileAttributes.Directory);
        var junction = project.PathFor("work/[data]/linked");
        CreateJunction(junction, project.PathFor("outside"));
        try
        {
            var working = project.PathFor("work");
            var changed = project.PathFor("changed");
            Directory.CreateDirectory(changed);
            project.Write("source/main.rukbat", $$"""
            import fs
            powershell<void>('Set-Location', LiteralPath: '{{RukbatLiteral(changed)}}')
            powershell<void>('Invoke-Expression', "[Environment]::CurrentDirectory = '{{RukbatLiteral(project.PathFor("deploy"))}}'")
            for file in fs.list_files_recursive('[data]', '*.json') { print(file) }
            print('question')
            for file in fs.list_files_recursive('[data]', 'test?.csv') { print(file) }
            print('direct')
            for file in fs.list_files('[data]', 'test?.csv') { print(file) }
            print('direct-json')
            for file in fs.list_files('[data]', '*.json') { print(file) }
            """);
            var compiled = new Compiler().CompileFile(project.PathFor("source/main.rukbat"));
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var script = project.PathFor("deploy/job.ps1");
            Directory.CreateDirectory(Path.GetDirectoryName(script)!);
            File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
            var result = await RunDeployed(shell, script, working, changed);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
        var split = result.Output.Replace("\r\n", "\n").Split(["question\n", "direct\n", "direct-json\n"], StringSplitOptions.None);
        Assert.Equal(4, split.Length);
            var jsonPaths = split[0].Split('\n', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Equal(new[]
            {
                Path.GetFullPath(Path.Combine(root, "top.json")),
                Path.GetFullPath(Path.Combine(root, "first/result.json")),
                Path.GetFullPath(Path.Combine(root, "second/result.json")),
                Path.GetFullPath(Path.Combine(root, "system.json")),
                Path.GetFullPath(Path.Combine(root, "system-dir/inside.json"))
            }.ToHashSet(StringComparer.OrdinalIgnoreCase), jsonPaths);
            Assert.Equal(new[] { Path.GetFullPath(Path.Combine(root, "test1.csv")) },
                split[1].Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(Path.GetFullPath(Path.Combine(root, "test1.csv")) + "\n", split[2]);
            Assert.Equal(new[]
            {
                Path.GetFullPath(Path.Combine(root, "system.json")),
                Path.GetFullPath(Path.Combine(root, "top.json"))
            }, split[3].Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
            Assert.DoesNotContain("escaped.json", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.SetAttributes(hidden, FileAttributes.Normal);
            File.SetAttributes(hiddenDirectory, FileAttributes.Directory);
            File.SetAttributes(system, FileAttributes.Normal);
            File.SetAttributes(systemDirectory, FileAttributes.Directory);
            Directory.Delete(junction);
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesListRecursiveHandlesEmptyNoMatchAndFailuresWithoutPartialResults(string shell)
    {
        using var project = new Project();
        Directory.CreateDirectory(project.PathFor("empty"));
        project.Write("root/file.txt", "file");
        project.Write("root/nested/only.txt", "file");
        project.Write("not-directory.txt", "file");
        project.Write("main.rukbat", """
            import fs
            empty := fs.list_files_recursive('empty', '*')
            print(empty.length)
            directEmpty := fs.list_files('empty', '*')
            print(directEmpty.length)
            none := fs.list_files_recursive('root', '*.json')
            print(none.length)
            direct := fs.list_files('root', '*.txt')
            print(direct.length)
            try { fs.list_files_recursive('missing', '*') } catch err { print('missing') }
            try { fs.list_files_recursive('not-directory.txt', '*') } catch err { print('file root') }
            try { fs.list_files_recursive('root', '') } catch err { print('empty pattern') }
            try { fs.list_files_recursive('root', 'nested/*.txt') } catch err { print('hierarchy pattern') }
            try { fs.list_files_recursive('C:drive-relative', '*') } catch err { print('drive relative') }
            try { fs.list_files('missing', '*') } catch err { print('direct missing') }
            try { fs.list_files('not-directory.txt', '*') } catch err { print('direct file root') }
            try { fs.list_files('root', '') } catch err { print('direct empty pattern') }
            try { fs.list_files('root', 'nested/*.txt') } catch err { print('direct hierarchy pattern') }
            try { fs.list_files('C:drive-relative', '*') } catch err { print('direct drive relative') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("job.ps1");
        File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0\n0\n0\n1\nmissing\nfile root\nempty pattern\nhierarchy pattern\ndrive relative\ndirect missing\ndirect file root\ndirect empty pattern\ndirect hierarchy pattern\ndirect drive relative\n", result.Output.Replace("\r\n", "\n"));
        Assert.Equal("", result.Error);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesListRecursiveAllowsDirectoryReparsePointRootsWithoutFollowingChildLinks(string shell)
    {
        using var project = new Project();
        project.Write("target/item.json", "item");
        project.Write("target/nested/inside.json", "inside");
        project.Write("outside/escaped.json", "outside");
        var junction = project.PathFor("root-link");
        var childJunction = project.PathFor("target/child-link");
        CreateJunction(junction, project.PathFor("target"));
        CreateJunction(childJunction, project.PathFor("outside"));
        try
        {
            project.Write("main.rukbat", """
            import fs
            for file in fs.list_files_recursive('root-link', '*.json') { print(file) }
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
            var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("", result.Error);
            Assert.Equal(new[]
            {
                Path.GetFullPath(Path.Combine(junction, "item.json")),
                Path.GetFullPath(Path.Combine(junction, "nested/inside.json"))
            }.ToHashSet(StringComparer.OrdinalIgnoreCase),
                result.Output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase));
            Assert.DoesNotContain("escaped.json", result.Output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(childJunction);
            Directory.Delete(junction);
        }
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("import fs\nfs.list_files_recursive(1, '*')")]
    [InlineData("import fs\nfs.list_files_recursive('.', 1)")]
    [InlineData("import fs\nfs.list_files_recursive('.')")]
    public void FilesListRecursiveUsesFilesTypedContract(string source)
        => Assert.NotEmpty(new Compiler().Check(source));
}
