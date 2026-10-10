using System.Diagnostics;
using System.Text;

namespace Rukbat.Tests;

public sealed class FilesystemExtensionTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("fs.list_dirs('.')")]
    [InlineData("fs.list_dirs('.', 1)")]
    [InlineData("fs.list_dirs(directory: '.', pattern: '*')")]
    [InlineData("fs.move_dir('source')")]
    [InlineData("fs.move_dir(1, 'destination')")]
    [InlineData("fs.move_dir('source', null)")]
    [InlineData("fs.append_text('log', 'entry')")]
    [InlineData("fs.append_text('log', 1, encoding.utf8)")]
    [InlineData("fs.append_text('log', 'entry', 'utf-8')")]
    [InlineData("fs.append_text('log', 'entry', null)")]
    public void InvalidCallsAreRejectedBeforeExecution(string source)
        => Assert.NotEmpty(new Compiler().Check("import fs; import encoding\n" + source));

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ListDirsIncludesEmptyHiddenAndSystemDirectoriesAndExcludesChildLinks(string shell)
    {
        using var project = new Project();
        const string root = "tree 日本語 [literal] ' ; $ &";
        project.Write(root + "/visible/nested/data.txt", "data");
        Directory.CreateDirectory(project.PathFor(root + "/empty"));
        Directory.CreateDirectory(project.PathFor(root + "/hidden"));
        Directory.CreateDirectory(project.PathFor(root + "/system"));
        project.Write(root + "/ordinary-file.txt", "file");
        project.Write("outside/marker.txt", "outside");
        var hidden = project.PathFor(root + "/hidden");
        var system = project.PathFor(root + "/system");
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.Directory);
        File.SetAttributes(system, FileAttributes.System | FileAttributes.Directory);
        var childLink = project.PathFor(root + "/link");
        var rootLink = project.PathFor("root-link");
        CreateJunction(childLink, project.PathFor("outside"));
        CreateJunction(rootLink, project.PathFor(root));
        try
        {
            project.Write("main.rukbat", $$"""
                import fs
                import path
                import array
                import test
                import text
                names: string[] = []
                for directory in fs.list_dirs('{{Literal(root)}}', '*') {
                    test.assert(path.is_absolute(directory))
                    names.append(path.filename(directory))
                }
                print(text.join(array.sort(names), ','))
                filtered := fs.list_dirs('{{Literal(root)}}', 'hid*')
                test.assert(filtered.length == 1)
                test.assert(path.filename(filtered[0]) == 'hidden')
                test.assert(fs.list_dirs('{{Literal(root)}}/empty', '*').length == 0)
                test.assert(fs.list_dirs('root-link', '*').length == 4)
                for value in ['missing', '{{Literal(root)}}/ordinary-file.txt', '', 'C:relative'] {
                    rejected := false
                    try { fs.list_dirs(value, '*') } catch err { rejected = true }
                    test.assert(rejected)
                }
                for pattern in ['', ' ', 'nested/*', 'nested\\*'] {
                    rejected := false
                    try { fs.list_dirs('{{Literal(root)}}', pattern) } catch err { rejected = true }
                    test.assert(rejected)
                }
                """);
            await AssertProgram(project, shell, "empty,hidden,system,visible\n");
        }
        finally
        {
            Directory.Delete(rootLink);
            Directory.Delete(childLink);
            File.SetAttributes(hidden, FileAttributes.Directory);
            File.SetAttributes(system, FileAttributes.Directory);
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task MoveAndAppendUseCapturedDirectoryInsideImportedFunctions(string shell)
    {
        using var project = new Project();
        const string source = "incoming/batch 日本語 [literal] ' ; $ &";
        project.Write(source + "/nested/data.txt", "kept");
        Directory.CreateDirectory(project.PathFor(source + "/empty"));
        var hidden = project.PathFor(source + "/hidden");
        Directory.CreateDirectory(hidden);
        File.SetAttributes(hidden, FileAttributes.Hidden | FileAttributes.Directory);
        Directory.CreateDirectory(project.PathFor("changed"));
        project.Write("helper.rukbat", """
            import fs
            import path
            import encoding
            fn process(): void {
                fs.create_dir_all('processed')
                for directory in fs.list_dirs('incoming', 'batch*') {
                    destination := path.resolve(path.resolve(path.working_dir(), 'processed'), path.filename(directory))
                    fs.move_dir(directory + '/', destination + '/')
                    fs.append_text('moves.log', path.filename(destination) + '\r\n', encoding.utf8)
                }
            }
            """);
        project.Write("main.rukbat", $$"""
            import './helper.rukbat' as helper
            powershell<void>('Set-Location', LiteralPath: '{{Literal(project.PathFor("changed"))}}')
            powershell<void>('Invoke-Expression', "[Environment]::CurrentDirectory = '{{Literal(project.PathFor("changed"))}}'")
            helper.process()
            helper.process()
            print('passed')
            """);
        try
        {
            await AssertProgram(project, shell);
            var moved = project.PathFor("processed/" + Path.GetFileName(source));
            Assert.False(Directory.Exists(project.PathFor(source)));
            Assert.Equal("kept", File.ReadAllText(Path.Combine(moved, "nested/data.txt")));
            Assert.True(Directory.Exists(Path.Combine(moved, "empty")));
            Assert.True(File.GetAttributes(Path.Combine(moved, "hidden")).HasFlag(FileAttributes.Hidden));
            Assert.Equal(Path.GetFileName(source) + "\r\n", File.ReadAllText(project.PathFor("moves.log")));
            Assert.False(File.Exists(project.PathFor("changed/moves.log")));
        }
        finally
        {
            var movedHidden = project.PathFor("processed/" + Path.GetFileName(source) + "/hidden");
            if (Directory.Exists(movedHidden)) File.SetAttributes(movedHidden, FileAttributes.Directory);
            if (Directory.Exists(hidden)) File.SetAttributes(hidden, FileAttributes.Directory);
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task MoveDirRejectsInvalidPlansWithoutChangingEitherTree(string shell)
    {
        using var project = new Project();
        project.Write("source/kept.txt", "source");
        project.Write("existing/kept.txt", "existing");
        project.Write("file.txt", "file");
        var root = Path.GetPathRoot(project.DirectoryPath)!;
        var otherDrive = root.StartsWith("Z:", StringComparison.OrdinalIgnoreCase) ? "Y:/" : "Z:/";
        project.Write("main.rukbat", $$"""
            import fs
            import test
            fn reject(source: string, target: string): void {
                failed := false
                try { fs.move_dir(source, target) } catch err { failed = true }
                test.assert(failed)
            }
            reject('missing', 'destination')
            reject('file.txt', 'destination')
            reject('source', 'existing')
            reject('source', 'file.txt')
            reject('source', './source/')
            reject('source', 'SOURCE')
            reject('source', 'source/child')
            reject('source', 'missing-parent/child')
            reject('source', '{{otherDrive}}rukbat-move-rejected')
            reject('source', '{{Literal(root)}}')
            reject('.', 'destination')
            reject('..', 'destination')
            reject('{{Literal(root)}}', 'destination')
            reject('', 'destination')
            reject('source', 'C:relative')
            print('passed')
            """);
        await AssertProgram(project, shell);
        Assert.Equal("source", File.ReadAllText(project.PathFor("source/kept.txt")));
        Assert.Equal("existing", File.ReadAllText(project.PathFor("existing/kept.txt")));
        Assert.Equal("file", File.ReadAllText(project.PathFor("file.txt")));
        Assert.False(Directory.Exists(project.PathFor("source/child")));
        Assert.False(Directory.Exists(project.PathFor("missing-parent")));
        Assert.False(Directory.Exists(project.PathFor("destination")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task MoveDirCarriesChildJunctionsWithoutMovingTheirTargets(string shell)
    {
        using var project = new Project();
        project.Write("source/data.txt", "source");
        project.Write("outside/marker.txt", "outside");
        var link = project.PathFor("source/link");
        var sourceLink = project.PathFor("source-link");
        CreateJunction(link, project.PathFor("outside"));
        CreateJunction(sourceLink, project.PathFor("source"));
        try
        {
            project.Write("main.rukbat", """
                import fs
                import test
                failed := false
                try { fs.move_dir('source-link', 'should-not-exist') } catch err { failed = true }
                test.assert(failed)
                fs.move_dir('source', 'destination')
                print('passed')
                """);
            await AssertProgram(project, shell);
            Assert.False(Directory.Exists(project.PathFor("source")));
            Assert.False(Directory.Exists(project.PathFor("should-not-exist")));
            Assert.Equal("outside", File.ReadAllText(project.PathFor("outside/marker.txt")));
            Assert.True(File.GetAttributes(project.PathFor("destination/link")).HasFlag(FileAttributes.ReparsePoint));
        }
        finally
        {
            Directory.Delete(sourceLink);
            var currentLink = Directory.Exists(project.PathFor("destination/link")) ? project.PathFor("destination/link") : link;
            Directory.Delete(currentLink);
        }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task AppendTextPreservesExistingBytesAndWritesExactEncodedSuffixes(string shell)
    {
        using var project = new Project();
        var bom = Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("first\r\n")).ToArray();
        File.WriteAllBytes(project.PathFor("bom.txt"), bom);
        File.WriteAllBytes(project.PathFor("binary-prefix.txt"), [0xFF, 0x00]);
        project.Write("main.rukbat", """
            import fs
            import encoding
            fs.append_text('bom.txt', '日本語', encoding.utf8)
            fs.append_text('bom.txt', '', encoding.utf8)
            fs.append_text('bom.txt', '\nlast', encoding.utf8)
            fs.append_text('binary-prefix.txt', 'A', encoding.utf8)
            fs.append_text('new.txt', '日本語', encoding.utf8)
            fs.append_text('new.txt', '末尾', encoding.utf8)
            fs.append_text('empty.txt', '', encoding.utf8)
            fs.append_text('utf16.txt', '東京', encoding.utf16_le)
            fs.append_text('utf16.txt', '大阪', encoding.utf16_le)
            fs.append_text('cp932.txt', '東京', encoding.windows_932)
            fs.append_text('cp932.txt', '大阪', encoding.windows_932)
            print('passed')
            """);
        await AssertProgram(project, shell);
        Assert.Equal(bom.Concat(Encoding.UTF8.GetBytes("日本語\nlast")), File.ReadAllBytes(project.PathFor("bom.txt")));
        Assert.Equal(new byte[] { 0xFF, 0x00, 0x41 }, File.ReadAllBytes(project.PathFor("binary-prefix.txt")));
        Assert.Equal(Encoding.UTF8.GetBytes("日本語末尾"), File.ReadAllBytes(project.PathFor("new.txt")));
        Assert.Empty(File.ReadAllBytes(project.PathFor("empty.txt")));
        Assert.Equal(Encoding.Unicode.GetBytes("東京大阪"), File.ReadAllBytes(project.PathFor("utf16.txt")));
        Assert.Equal(new byte[] { 0x93, 0x8C, 0x8B, 0x9E, 0x91, 0xE5, 0x8D, 0xE3 }, File.ReadAllBytes(project.PathFor("cp932.txt")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task AppendValidationAndSharingFailuresPreserveTheFile(string shell)
    {
        using var project = new Project();
        project.Write("existing.txt", "kept");
        project.Write("locked.txt", "locked");
        project.Write("readonly.txt", "readonly");
        Directory.CreateDirectory(project.PathFor("directory"));
        File.SetAttributes(project.PathFor("readonly.txt"), FileAttributes.ReadOnly);
        using var held = File.Open(project.PathFor("locked.txt"), FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        try
        {
            project.Write("main.rukbat", """
                import fs
                import encoding
                import json
                import test
                invalid := json.parse<string>('"\\uD800"')
                for file in ['existing.txt', 'missing.txt'] {
                    failed := false
                    try { fs.append_text(file, invalid, encoding.utf8) } catch err { failed = true }
                    test.assert(failed)
                    failed = false
                    try { fs.append_text(file, '😀', encoding.windows_932) } catch err { failed = true }
                    test.assert(failed)
                }
                for file in ['locked.txt', 'readonly.txt', 'directory', 'missing-parent/log.txt', '', 'C:relative', 'bad|path'] {
                    failed := false
                    try { fs.append_text(file, 'new', encoding.utf8) } catch err { failed = true }
                    test.assert(failed)
                }
                print('passed')
                """);
            await AssertProgram(project, shell);
            held.Dispose();
            Assert.Equal("kept", File.ReadAllText(project.PathFor("existing.txt")));
            Assert.Equal("locked", File.ReadAllText(project.PathFor("locked.txt")));
            Assert.Equal("readonly", File.ReadAllText(project.PathFor("readonly.txt")));
            Assert.False(File.Exists(project.PathFor("missing.txt")));
            Assert.False(Directory.Exists(project.PathFor("missing-parent")));
        }
        finally { File.SetAttributes(project.PathFor("readonly.txt"), FileAttributes.Normal); }
    }

    private static string Literal(string value) => value.Replace("\\", "/").Replace("'", "\\'");

    private static async Task AssertProgram(Project project, string shell, string expected = "passed\n")
    {
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var script = project.PathFor("job.ps1");
        File.WriteAllText(script, compiled.Output, new UTF8Encoding(true));
        var result = await RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        Assert.Equal(expected, result.Output);
    }

    private static void CreateJunction(string path, string target)
    {
        var info = new ProcessStartInfo("cmd.exe")
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            Arguments = $"/c mklink /J \"{Path.GetFullPath(path)}\" \"{Path.GetFullPath(target)}\""
        };
        using var child = Process.Start(info)!;
        var output = child.StandardOutput.ReadToEnd();
        var error = child.StandardError.ReadToEnd();
        child.WaitForExit();
        Assert.True(child.ExitCode == 0, output + error);
    }
}
