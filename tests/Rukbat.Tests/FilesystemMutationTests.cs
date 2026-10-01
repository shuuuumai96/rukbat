using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests;

public class FilesystemMutationTests : TestBase
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
        Assert.True(command.ExitCode == 0, $"Could not create junction: {output}{error}");
    }

    private static string RukbatLiteral(string value) => value.Replace("\\", "/").Replace("'", "\\'");

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesWriteValidatesUtf8BeforeChangingTheDestination(string shell)
    {
        using var project = new Project();
        var existing = project.PathFor("existing.txt");
        var missing = project.PathFor("missing.txt");
        var empty = project.PathFor("empty.txt");
        var original = Encoding.UTF8.GetBytes("known existing contents\n");
        File.WriteAllBytes(existing, original);
        project.Write("main.rukbat", $$"""
            import fs
            import json
            invalid := json.parse<string>('"\\uD800"')
            try { fs.write_text('{{RukbatLiteral(existing)}}', invalid, 'utf-8') } catch err { print('existing') }
            try { fs.write_text('{{RukbatLiteral(missing)}}', invalid, 'utf-8') } catch err { print('missing') }
            """);

        var invalidCompiled = project.Compile();
        Assert.True(invalidCompiled.Success, string.Join("\n", invalidCompiled.Diagnostics));
        var invalidResult = await RunOnHost(project, invalidCompiled.Output!, shell);
        Assert.Equal(0, invalidResult.ExitCode);
        Assert.Equal("existing\nmissing\n", invalidResult.Output);
        Assert.Equal("", invalidResult.Error);
        Assert.Equal(original, File.ReadAllBytes(existing));
        Assert.False(File.Exists(missing));
        Assert.Empty(Directory.EnumerateFiles(project.DirectoryPath, ".rukbat-write-*"));

        project.Write("main.rukbat", $$"""
            import fs
            fs.write_text('{{RukbatLiteral(existing)}}', 'new contents', 'utf-8')
            fs.write_text('{{RukbatLiteral(empty)}}', 'old', 'utf-8')
            fs.write_text('{{RukbatLiteral(empty)}}', '', 'utf-8')
            """);
        var validCompiled = project.Compile();
        Assert.True(validCompiled.Success, string.Join("\n", validCompiled.Diagnostics));
        var validResult = await RunOnHost(project, validCompiled.Output!, shell);
        Assert.Equal(0, validResult.ExitCode);
        Assert.Equal("", validResult.Output);
        Assert.Equal("", validResult.Error);
        Assert.Equal(Encoding.UTF8.GetBytes("new contents"), File.ReadAllBytes(existing));
        Assert.Empty(File.ReadAllBytes(empty));
        Assert.Empty(Directory.EnumerateFiles(project.DirectoryPath, ".rukbat-write-*"));
    }

    private static Task<RunResult> RunOnHost(Project project, string output, string shell)
        => shell == "powershell"
            ? project.RunOutputWithWindowsPowerShell(output)
            : project.RunOutput(output);

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesystemMutationsUseCapturedWorkingDirectoryAndLiteralPaths(string shell)
        => await AssertFilesystemMutationsUseCapturedWorkingDirectoryAndLiteralPaths(shell);

    [Trait("Category", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Trait("Smoke", "PowerShell51")]
    [Fact]
    public async Task FilesystemMutationsUseCapturedWorkingDirectoryAndLiteralPathsOnWindowsPowerShell51()
        => await AssertFilesystemMutationsUseCapturedWorkingDirectoryAndLiteralPaths("powershell");

    private async Task AssertFilesystemMutationsUseCapturedWorkingDirectoryAndLiteralPaths(string shell)
    {
        using var project = new Project();
        const string special = "fixture space 日本語 [brackets] ' ; $ &";
        project.Write("tree/hidden.txt", "hidden");
        project.Write("readonly.txt", "readonly");
        File.SetAttributes(project.PathFor("tree/hidden.txt"), FileAttributes.Hidden);
        File.SetAttributes(project.PathFor("readonly.txt"), FileAttributes.ReadOnly);
        var changed = project.PathFor("changed");
        var deploy = project.PathFor("deploy");
        var root = Path.GetPathRoot(project.DirectoryPath)!;
        var listedDirectory = project.PathFor(special + "/nested");
        var rootRelativeListedDirectory = "/" + Path.GetRelativePath(root, listedDirectory).Replace("\\", "/");
        Directory.CreateDirectory(changed);
        project.Write("source/main.rukbat", $$"""
            import fs
            import path

            powershell<void>('Set-Location', LiteralPath: '{{RukbatLiteral(changed)}}')
            powershell<void>('Invoke-Expression', "[Environment]::CurrentDirectory = '{{RukbatLiteral(deploy)}}'")
            fs.create_dir_all('{{RukbatLiteral(special)}}/nested')
            print(fs.is_dir('{{RukbatLiteral(special)}}/nested'))
            fs.write_text('{{RukbatLiteral(special)}}/nested/source.txt', 'one', 'utf-8')
            fs.copy_file('{{RukbatLiteral(special)}}/nested/source.txt', '{{RukbatLiteral(special)}}/nested/copy.txt')
            listed := fs.list_files('{{RukbatLiteral(special)}}/nested', '*.txt')
            print(listed.length)
            absolute_listed := fs.list_files('{{RukbatLiteral(listedDirectory)}}', '*.txt')
            print(absolute_listed.length)
            root_listed := fs.list_files('{{RukbatLiteral(rootRelativeListedDirectory)}}', '*.txt')
            print(root_listed.length)
            try { fs.copy_file('{{RukbatLiteral(special)}}/nested/source.txt', '{{RukbatLiteral(special)}}/nested/copy.txt') } catch err { print('existing destination') }
            fs.move_file('{{RukbatLiteral(special)}}/nested/copy.txt', '{{RukbatLiteral(special)}}/nested/moved.txt')
            print(fs.read_text('{{RukbatLiteral(special)}}/nested/moved.txt', 'utf-8'))
            fs.remove_file('{{RukbatLiteral(special)}}/nested/moved.txt')
            fs.remove_file('missing.txt')
            print(fs.is_file('{{RukbatLiteral(special)}}/nested/moved.txt'))
            try { fs.remove_file('readonly.txt') } catch err { print('readonly') }
            fs.create_dir_all('empty')
            fs.remove_dir('empty')
            print(fs.is_dir('empty'))
            fs.remove_dir('missing-directory')
            fs.remove_dir_all('tree')
            print(fs.is_dir('tree'))
            print(path.parent('a/b.txt'))
            print(path.parent('a.txt'))
            print(path.parent('a/b/'))
            print(path.parent('{{RukbatLiteral(root)}}child/'))
            print(path.parent('{{RukbatLiteral(root)}}'))
            print(path.parent('C:/child/'))
            print(path.parent('//server/share/'))
            print(path.parent('//server/share/child'))
            print(path.parent('C:relative'))
            try { path.parent('') } catch err { print('empty parent') }
            try { path.parent('?:invalid') } catch err { print('invalid parent') }
            try { fs.copy_file('C:relative.txt', 'destination.txt') } catch err { print('drive relative') }
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("source/main.rukbat"));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        Assert.DoesNotContain("File]::Move($source, $destination,", compiled.Output!);
        Directory.CreateDirectory(deploy);
        var script = project.PathFor("deploy/job.ps1");
        File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
        File.Delete(project.PathFor("source/main.rukbat"));
        try
        {
            var result = await RunDeployed(shell, script, project.DirectoryPath, changed);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            Assert.Equal($"True\n2\n2\n2\nexisting destination\none\nFalse\nreadonly\nFalse\nFalse\na\n.\na\n{root}\n{root}\nC:\\\n\\\\server\\share\\\n\\\\server\\share\\\nC:\nempty parent\ninvalid parent\ndrive relative\n", result.Output.Replace("\r\n", "\n"));
            Assert.Equal("one", File.ReadAllText(project.PathFor(special + "/nested/source.txt")));
            Assert.False(File.Exists(project.PathFor(special + "/nested/moved.txt")));
            Assert.False(Directory.Exists(project.PathFor("tree")));
        }
        finally
        {
            if (File.Exists(project.PathFor("readonly.txt"))) File.SetAttributes(project.PathFor("readonly.txt"), FileAttributes.Normal);
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task RemoveAllDoesNotTraverseReparsePointsAndGuardsDangerousRoots(string shell)
        => await AssertRemoveAllDoesNotTraverseReparsePointsAndGuardsDangerousRoots(shell);

    [Trait("Category", "PowerShell51")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task RemoveAllDoesNotTraverseReparsePointsAndGuardsDangerousRootsOnWindowsPowerShell51()
        => await AssertRemoveAllDoesNotTraverseReparsePointsAndGuardsDangerousRoots("powershell");

    private async Task AssertRemoveAllDoesNotTraverseReparsePointsAndGuardsDangerousRoots(string shell)
    {
        using var project = new Project();
        project.Write("root/nested/file.txt", "remove");
        project.Write("outside/keep.txt", "keep");
        project.Write("target/keep.txt", "keep");
        project.Write("file.txt", "file");
        CreateJunction(project.PathFor("root/link"), project.PathFor("outside"));
        CreateJunction(project.PathFor("root-link"), project.PathFor("target"));
        var root = Path.GetPathRoot(project.DirectoryPath)!;
        var rootRelativeWorking = "/" + Path.GetRelativePath(root, project.DirectoryPath).Replace("\\", "/");
        var samePrefixSibling = project.DirectoryPath + "2";
        Directory.CreateDirectory(samePrefixSibling);
        File.WriteAllText(Path.Combine(samePrefixSibling, "remove.txt"), "remove");
        project.Write("source/main.rukbat", $$"""
            import fs

            fs.remove_dir_all('root')
            print(fs.is_dir('root'))
            print(fs.is_file('outside/keep.txt'))
            fs.remove_dir_all('root-link')
            print(fs.is_dir('root-link'))
            print(fs.is_file('target/keep.txt'))
            fs.remove_dir_all('missing')
            try { fs.remove_dir_all('file.txt') } catch err { print('file') }
            try { fs.remove_dir_all('.') } catch err { print('working') }
            try { fs.remove_dir_all('..') } catch err { print('ancestor') }
            try { fs.remove_dir_all('./') } catch err { print('trailing working') }
            try { fs.remove_dir_all('{{RukbatLiteral(project.DirectoryPath.ToUpperInvariant())}}') } catch err { print('case working') }
            try { fs.remove_dir_all('{{RukbatLiteral(rootRelativeWorking)}}') } catch err { print('root relative working') }
            try { fs.remove_dir_all('{{RukbatLiteral(root)}}') } catch err { print('root') }
            fs.remove_dir_all('{{RukbatLiteral(samePrefixSibling)}}')
            print(fs.is_dir('{{RukbatLiteral(samePrefixSibling)}}'))
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("source/main.rukbat"));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        Assert.Equal("False\nTrue\nFalse\nTrue\nfile\nworking\nancestor\ntrailing working\ncase working\nroot relative working\nroot\nFalse\n", result.Output.Replace("\r\n", "\n"));
        Assert.True(File.Exists(project.PathFor("outside/keep.txt")));
        Assert.True(File.Exists(project.PathFor("target/keep.txt")));
        try { Directory.Delete(project.PathFor("root/link")); } catch (DirectoryNotFoundException) { }
        try { Directory.Delete(project.PathFor("root"), true); } catch (DirectoryNotFoundException) { }
        try { Directory.Delete(project.PathFor("root-link")); } catch (DirectoryNotFoundException) { }
        try { Directory.Delete(samePrefixSibling, true); } catch (DirectoryNotFoundException) { }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FileCopyAndMoveRejectEquivalentPathsIgnoringWindowsPathSpelling(string shell)
    {
        using var project = new Project();
        project.Write("same-relative.txt", "one");
        project.Write("same-dot.txt", "two");
        project.Write("same-dotdot.txt", "three");
        project.Write("folder/same-separator.txt", "four");
        project.Write("same-case.txt", "five");
        var absolute = RukbatLiteral(project.PathFor("same-relative.txt"));
        project.Write("main.rukbat", $$"""
            import fs
            try { fs.copy_file('same-relative.txt', '{{absolute}}') } catch err { print('copy relative') }
            try { fs.copy_file('./same-dot.txt', 'same-dot.txt') } catch err { print('copy dot') }
            try { fs.copy_file('folder/../same-dotdot.txt', 'same-dotdot.txt') } catch err { print('copy dotdot') }
            try { fs.copy_file('folder/same-separator.txt', 'folder\\same-separator.txt') } catch err { print('copy separator') }
            try { fs.copy_file('same-case.txt', 'SAME-CASE.TXT') } catch err { print('copy case') }
            try { fs.move_file('same-relative.txt', '{{absolute}}') } catch err { print('move relative') }
            try { fs.move_file('./same-dot.txt', 'same-dot.txt') } catch err { print('move dot') }
            try { fs.move_file('folder/../same-dotdot.txt', 'same-dotdot.txt') } catch err { print('move dotdot') }
            try { fs.move_file('folder/same-separator.txt', 'folder\\same-separator.txt') } catch err { print('move separator') }
            try { fs.move_file('same-case.txt', 'SAME-CASE.TXT') } catch err { print('move case') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal("copy relative\ncopy dot\ncopy dotdot\ncopy separator\ncopy case\nmove relative\nmove dot\nmove dotdot\nmove separator\nmove case\n", result.Output.Replace("\r\n", "\n"));
        Assert.True(File.Exists(project.PathFor("same-relative.txt")));
        Assert.True(File.Exists(project.PathFor("same-dot.txt")));
        Assert.True(File.Exists(project.PathFor("same-dotdot.txt")));
        Assert.True(File.Exists(project.PathFor("folder/same-separator.txt")));
        Assert.True(File.Exists(project.PathFor("same-case.txt")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task PathEqualUsesFilesystemResolutionWithoutRequiringExistingEntries(string shell)
    {
        using var project = new Project();
        var absolute = RukbatLiteral(project.PathFor("missing/item.txt"));
        project.Write("main.rukbat", $$"""
            import path
            print(path.equal('missing/item.txt', '{{absolute}}'))
            print(path.equal('./missing/item.txt', 'folder/../missing/ITEM.txt'))
            print(path.equal('missing/item.txt', 'missing\\item.txt'))
            try { path.equal('C:relative.txt', 'missing/item.txt') } catch err { print('drive relative') }
            try { path.equal('?:invalid', 'missing/item.txt') } catch err { print('invalid') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal("True\nTrue\nTrue\ndrive relative\ninvalid\n", result.Output.Replace("\r\n", "\n"));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task PathFindDuplicateUsesPathEqualityAndReturnsTheFirstRepeatedSpelling(string shell)
    {
        using var project = new Project();
        var absolute = RukbatLiteral(project.PathFor("missing/item.txt"));
        project.Write("main.rukbat", $$"""
            import path
            print(path.find_duplicate([]) == null)
            print(path.find_duplicate(['a', 'b']) == null)
            early := path.find_duplicate(['a', './A', 'bad|path'])
            if early != null { print(early == './A') }
            duplicate := path.find_duplicate(['missing/item.txt', '{{absolute}}', './missing/ITEM.txt'])
            if duplicate != null { print(duplicate == '{{absolute}}') }
            variants := path.find_duplicate(['missing/item.txt', 'folder/../missing/ITEM.txt'])
            if variants != null { print(variants == 'folder/../missing/ITEM.txt') }
            separators := path.find_duplicate(['missing/item.txt', 'missing\\item.txt'])
            if separators != null { print(path.equal(separators, 'missing/item.txt')) }
            try { path.find_duplicate(['C:relative.txt']) } catch err { print('drive relative') }
            try { path.find_duplicate(['bad|path']) } catch err { print('invalid') }
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("", result.Error);
        Assert.Equal("True\nTrue\nTrue\nTrue\nTrue\nTrue\ndrive relative\ninvalid\n", result.Output);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FileMoveAcrossVolumesRemovesTheSource(string shell)
    {
        using var project = new Project();
        var sourceRoot = Path.GetPathRoot(project.DirectoryPath)!;
        var targetDrive = DriveInfo.GetDrives().FirstOrDefault(drive =>
            drive.IsReady && drive.DriveType == DriveType.Fixed &&
            !string.Equals(drive.RootDirectory.FullName, sourceRoot, StringComparison.OrdinalIgnoreCase));
        Assert.True(targetDrive is not null,
            $"Cross-volume fs.move_file test requires a writable fixed drive other than {sourceRoot}.");
        var targetDirectory = Path.Combine(targetDrive!.RootDirectory.FullName, "rukbat-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(targetDirectory);
        project.Write("source.txt", "cross-volume");
        var destination = Path.Combine(targetDirectory, "moved.txt");
        project.Write("main.rukbat", $$"""
            import fs
            fs.move_file('source.txt', '{{RukbatLiteral(destination)}}')
            print(fs.is_file('source.txt'))
            print(fs.is_file('{{RukbatLiteral(destination)}}'))
            """);
        try
        {
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
            var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("", result.Error);
            Assert.Equal("False\nTrue\n", result.Output.Replace("\r\n", "\n"));
            Assert.False(File.Exists(project.PathFor("source.txt")));
            Assert.Equal("cross-volume", File.ReadAllText(destination));
        }
        finally
        {
            if (Directory.Exists(targetDirectory)) Directory.Delete(targetDirectory, true);
        }
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("import fs\nfs.create_dir_all(1)")]
    [InlineData("import fs\nfs.is_dir('x', 'y')")]
    [InlineData("import fs\nfs.remove_dir_all()")]
    [InlineData("import fs\nfs.copy_file('x')")]
    [InlineData("import fs\nfs.move_file('x', 1)")]
    [InlineData("import fs\nfs.remove_file(1)")]
    [InlineData("import path\npath.parent(1)")]
    [InlineData("import path\npath.equal('x')")]
    [InlineData("import path\npath.equal('x', 1)")]
    [InlineData("import path\npath.find_duplicate('x')")]
    [InlineData("import path\npath.find_duplicate([1])")]
    [InlineData("import path\npath.find_duplicate([] , [])")]
    public void FilesystemMutationsUseTypedStandardLibraryContracts(string source)
        => Assert.NotEmpty(new Compiler().Check(source));

    [Trait("Category", "Unit")]
    [Fact]
    public void GeneratedScriptIncludesFilesystemRuntimesOnlyWhenImported()
    {
        var filesystem = Success("import fs\nfs.create_dir_all('x'); fs.remove_file('y')");
        Assert.Contains("'fs.remove_dir_all'", filesystem);
        Assert.Contains("'fs.copy_file'", filesystem);
        Assert.Contains("[System.IO.File]::Replace($tempPath, $path, $noBackup)", filesystem);
        Assert.Contains("[System.IO.FileMode]::CreateNew", filesystem);
        Assert.DoesNotContain("[System.IO.File]::WriteAllText($path", filesystem);

        var paths = Success("import path\nprint(path.filename('x'))");
        Assert.DoesNotContain("'fs.remove_dir_all'", paths);
        Assert.DoesNotContain("'fs.copy_file'", paths);
        Assert.DoesNotContain("class RukbatMoveBatch", paths);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void FilesWriteEmitsSameDirectoryStagedReplacement()
    {
        var output = Success("import fs\nfs.write_text('report.txt', 'contents', 'utf-8')");

        Assert.Contains("[System.IO.Path]::Combine($directory, '.rukbat-write-' + [System.IO.Path]::GetRandomFileName())", output);
        Assert.Contains("[System.IO.File]::Open($candidate, [System.IO.FileMode]::CreateNew", output);
        Assert.Contains("[System.IO.File]::Move($tempPath, $path)", output);
        Assert.Contains("[System.IO.File]::Delete($tempPath)", output);
    }
}
