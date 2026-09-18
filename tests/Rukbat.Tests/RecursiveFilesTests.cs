using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests;

public partial class CompilerTests
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

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesListRecursiveUsesFixedWorkingDirectoryAndReturnsUniqueAbsolutePaths(string shell)
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
            import files
            powershell<void>('Set-Location', LiteralPath: '{{RukbatLiteral(changed)}}')
            powershell<void>('Invoke-Expression', "[Environment]::CurrentDirectory = '{{RukbatLiteral(project.PathFor("deploy"))}}'")
            for file in files.list_recursive('[data]', '*.json') { print(file) }
            print('question')
            for file in files.list_recursive('[data]', 'test?.csv') { print(file) }
            print('direct')
            for file in files.list('[data]', 'test?.csv') { print(file) }
            print('direct-json')
            for file in files.list('[data]', '*.json') { print(file) }
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
        Assert.Equal("test1.csv\n", split[2]);
            Assert.Equal(new[] { "system.json", "top.json" },
                split[3].Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
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
            import files
            empty := files.list_recursive('empty', '*')
            print(empty.length)
            none := files.list_recursive('root', '*.json')
            print(none.length)
            try { files.list_recursive('missing', '*') } catch err { print('missing') }
            try { files.list_recursive('not-directory.txt', '*') } catch err { print('file root') }
            try { files.list_recursive('root', '') } catch err { print('empty pattern') }
            try { files.list_recursive('root', 'nested/*.txt') } catch err { print('hierarchy pattern') }
            try { files.list_recursive('C:drive-relative', '*') } catch err { print('drive relative') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("job.ps1");
        File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("0\n0\nmissing\nfile root\nempty pattern\nhierarchy pattern\ndrive relative\n", result.Output.Replace("\r\n", "\n"));
        Assert.Equal("", result.Error);
    }

    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesListRecursiveRejectsDirectoryReparsePointRoots(string shell)
    {
        using var project = new Project();
        project.Write("target/item.json", "item");
        var junction = project.PathFor("root-link");
        CreateJunction(junction, project.PathFor("target"));
        try
        {
            project.Write("main.rukbat", """
            import files
            try { files.list_recursive('root-link', '*') } catch err { print('reparse root') }
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
            var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("reparse root\n", result.Output.Replace("\r\n", "\n"));
            Assert.Equal("", result.Error);
        }
        finally
        {
            Directory.Delete(junction);
        }
    }

    [Theory]
    [InlineData("import files\nfiles.list_recursive(1, '*')")]
    [InlineData("import files\nfiles.list_recursive('.', 1)")]
    [InlineData("import files\nfiles.list_recursive('.')")]
    public void FilesListRecursiveUsesFilesTypedContract(string source)
        => Assert.NotEmpty(new Compiler().Check(source));
}
