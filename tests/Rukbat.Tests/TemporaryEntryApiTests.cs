using System.Diagnostics;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Rukbat.Tests;

public sealed class TemporaryEntryApiTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("fs.create_temp_file()", "RUK2005")]
    [InlineData("fs.create_temp_file(1)", "RUK2001")]
    [InlineData("fs.create_temp_file(null)", "RUK2001")]
    [InlineData("fs.create_temp_file('.', 'prefix')", "RUK2005")]
    [InlineData("fs.create_temp_file(directory: '.')", "RUK2005")]
    [InlineData("fs.create_temp_dir()", "RUK2005")]
    [InlineData("fs.create_temp_dir(1)", "RUK2001")]
    [InlineData("fs.create_temp_dir(null)", "RUK2001")]
    [InlineData("fs.create_temp_dir('.', 'prefix')", "RUK2005")]
    [InlineData("fs.create_temp_dir(directory: '.')", "RUK2005")]
    public void InvalidCallsFailBeforeGeneration(string call, string code)
    {
        var source = "import fs\n" + call;
        var diagnostic = Assert.Single(new Compiler().Check(source));
        Assert.Equal(code, diagnostic.Code);
        Assert.Equal(2, diagnostic.Line);
        Assert.Null(Compile(source).Output);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task TemporaryEntriesAreCreatedInTheCapturedLiteralParentAndCleanedExplicitly(string shell)
    {
        using var project = new Project();
        const string parent = "work 日本語 [literal] ' ; $ &";
        project.Write(parent + "/kept.txt", "existing content");
        Directory.CreateDirectory(project.PathFor("changed"));
        project.Write("helper.rukbat", """
            import fs
            fn allocate(directory: string): string[] {
                return [fs.create_temp_file(directory), fs.create_temp_dir(directory)]
            }
            """);
        project.Write("main.rukbat", $$"""
            import './helper.rukbat' as helper
            import fs
            import path
            import encoding
            import test
            powershell<void>('Set-Location', LiteralPath: '{{Literal(project.PathFor("changed"))}}')
            powershell<void>('Invoke-Expression', "[Environment]::CurrentDirectory = '{{Literal(project.PathFor("changed"))}}'")
            parent := path.resolve(path.working_dir(), '{{Literal(parent)}}')
            first := helper.allocate('{{Literal(parent)}}/')
            second := helper.allocate(parent)
            for entry in [first[0], first[1], second[0], second[1]] {
                test.assert(path.is_absolute(entry))
                test.assert(path.equal(path.parent(entry), parent))
            }
            test.assert(first[0] != second[0])
            test.assert(first[1] != second[1])
            test.assert(first[0] != first[1])
            for file in [first[0], second[0]] {
                test.assert(fs.is_file(file))
                test.assert(!fs.is_dir(file))
                test.assert(fs.read_text(file, encoding.utf8) == '')
                size := fs.stat(file).size
                if size != null { test.assert(size == 0) } else { throw Error { message: 'missing file size' } }
                fs.write_text(file, '東京', encoding.utf8)
                fs.append_text(file, '\n追記', encoding.utf8)
                test.assert(fs.read_text(file, encoding.utf8) == '東京\n追記')
                fs.remove_file(file)
            }
            for directory in [first[1], second[1]] {
                test.assert(fs.is_dir(directory))
                test.assert(!fs.is_file(directory))
                test.assert(fs.list_dirs(directory, '*').length == 0)
                test.assert(fs.list_files(directory, '*').length == 0)
                fs.remove_dir(directory)
            }
            retained := fs.create_temp_dir(parent)
            try { throw Error { message: 'later failure' } } catch err { test.assert(fs.is_dir(retained)) }
            child := fs.create_temp_file(retained)
            fs.write_text(child, 'payload', encoding.utf8)
            fs.remove_dir_all(retained)
            test.assert(!fs.is_dir(retained))
            test.assert(fs.read_text('{{Literal(parent)}}/kept.txt', encoding.utf8) == 'existing content')
            print('passed')
            """);
        await AssertProgram(project, shell);
        Assert.Equal(["kept.txt"], Directory.GetFileSystemEntries(project.PathFor(parent)).Select(Path.GetFileName));
        Assert.Empty(Directory.GetFileSystemEntries(project.PathFor("changed")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task InvalidParentsFailWithoutCreatingParentsOrChangingExistingFiles(string shell)
    {
        using var project = new Project();
        project.Write("file.txt", "kept");
        project.Write("main.rukbat", """
            import fs
            import test
            for parent in ['', 'missing', 'missing/child', 'file.txt', 'file.txt/child', 'C:relative', 'wild*card', 'bad|path'] {
                rejected := false
                try { fs.create_temp_file(parent) } catch err { rejected = true; test.assert(err.message != '') }
                test.assert(rejected)
                rejected = false
                try { fs.create_temp_dir(parent) } catch err { rejected = true; test.assert(err.message != '') }
                test.assert(rejected)
            }
            print('passed')
            """);
        await AssertProgram(project, shell);
        Assert.Equal("kept", File.ReadAllText(project.PathFor("file.txt")));
        Assert.False(Directory.Exists(project.PathFor("missing")));
        Assert.Equal(["file.txt", "job.ps1", "main.rukbat"],
            Directory.GetFileSystemEntries(project.DirectoryPath).Select(Path.GetFileName).Order());
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task AnExplicitJunctionParentUsesNormalFilesystemResolution(string shell)
    {
        using var project = new Project();
        using var target = new Project();
        var junction = project.PathFor("parent [literal] 日本語");
        CreateJunction(junction, target.DirectoryPath);
        try
        {
            project.Write("main.rukbat", """
                import fs
                import test
                file := fs.create_temp_file('parent [literal] 日本語')
                directory := fs.create_temp_dir('parent [literal] 日本語')
                test.assert(fs.is_file(file))
                test.assert(fs.is_dir(directory))
                print(file)
                print(directory)
                """);
            var result = await Execute(project, shell, CompileProject(project));
            AssertSuccess(result);
            var paths = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, paths.Length);
            Assert.All(paths, path => Assert.Equal(Path.GetFullPath(junction), Path.GetDirectoryName(path)));
            Assert.Empty(File.ReadAllBytes(target.PathFor(Path.GetFileName(paths[0]))));
            Assert.Empty(Directory.GetFileSystemEntries(target.PathFor(Path.GetFileName(paths[1]))));
        }
        finally { Directory.Delete(junction); }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", "create_temp_file", false)]
    [InlineData("pwsh", "create_temp_file", false)]
    [InlineData("powershell", "create_temp_dir", false)]
    [InlineData("pwsh", "create_temp_dir", false)]
    [InlineData("powershell", "create_temp_file", true)]
    [InlineData("pwsh", "create_temp_file", true)]
    [InlineData("powershell", "create_temp_dir", true)]
    [InlineData("pwsh", "create_temp_dir", true)]
    public async Task CollisionsWithFilesAndDirectoriesRetryOrFailWithoutMutatingThem(string shell, string operation, bool exhaust)
    {
        using var project = new Project();
        project.Write("file-parent/.rukbat-temp-collision", "existing file");
        project.Write("directory-parent/.rukbat-temp-collision/marker.txt", "existing directory");
        project.Write("main.rukbat", $$"""
            import fs
            import test
            for parent in ['file-parent', 'directory-parent'] {
                rejected := false
                try {
                    created := fs.{{operation}}(parent)
                    print(created)
                } catch err {
                    rejected = true
                    test.assert(err.message != '')
                }
                test.assert(rejected == {{(exhaust ? "true" : "false")}})
            }
            print(powershell<int>('Invoke-Expression', "[RukbatTemporaryEntries].GetField('__collisionAttempt', [Reflection.BindingFlags]'NonPublic, Static').GetValue($null)"))
            """);
        var output = ForceCandidates(CompileProject(project), exhaust
            ? "(++__collisionAttempt > 0 ? \"collision\" : \"unused\")"
            : "(++__collisionAttempt % 2 == 1 ? \"collision\" : \"created\")");
        var result = await Execute(project, shell, output);
        AssertSuccess(result);
        Assert.Equal("existing file", File.ReadAllText(project.PathFor("file-parent/.rukbat-temp-collision")));
        Assert.Equal("existing directory", File.ReadAllText(project.PathFor("directory-parent/.rukbat-temp-collision/marker.txt")));
        if (exhaust) Assert.Equal("64\n", result.Output);
        else
        {
            foreach (var parent in new[] { "file-parent", "directory-parent" })
            {
                var created = project.PathFor(parent + "/.rukbat-temp-created");
                if (operation == "create_temp_file") Assert.Empty(File.ReadAllBytes(created));
                else Assert.Empty(Directory.GetFileSystemEntries(created));
            }
            Assert.EndsWith("\n4\n", result.Output);
        }
        foreach (var parent in new[] { "file-parent", "directory-parent" })
            Assert.Equal(exhaust ? 1 : 2, Directory.GetFileSystemEntries(project.PathFor(parent)).Length);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ConcurrentCallersAcquireDistinctEntriesEvenWithTheSameCandidates(string shell)
    {
        using var project = new Project();
        project.Write("shared/.rukbat-temp-1", "existing file");
        project.Write("shared/.rukbat-temp-2/marker.txt", "existing directory");
        project.Write("main.rukbat", """
            import fs
            print(fs.create_temp_file('shared'))
            print(fs.create_temp_dir('shared'))
            """);
        var script = project.PathFor("job.ps1");
        File.WriteAllText(script, ForceCandidates(CompileProject(project), "(++__collisionAttempt).ToString()"), new UTF8Encoding(true));
        var results = await Task.WhenAll(Enumerable.Range(0, 4)
            .Select(_ => RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath)));
        var paths = new List<string>();
        foreach (var result in results)
        {
            AssertSuccess(result);
            var acquired = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, acquired.Length);
            Assert.Empty(File.ReadAllBytes(acquired[0]));
            Assert.Empty(Directory.GetFileSystemEntries(acquired[1]));
            paths.AddRange(acquired);
        }
        Assert.Equal(8, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(10, Directory.GetFileSystemEntries(project.PathFor("shared")).Length);
        Assert.Equal("existing file", File.ReadAllText(project.PathFor("shared/.rukbat-temp-1")));
        Assert.Equal("existing directory", File.ReadAllText(project.PathFor("shared/.rukbat-temp-2/marker.txt")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task CollisionsWithAnExclusivelyLockedReadonlyFilePreserveIt(string shell)
    {
        using var project = new Project();
        const string collision = "parent/.rukbat-temp-collision";
        project.Write(collision, "locked content");
        project.Write("main.rukbat", """
            import fs
            print(fs.create_temp_file('parent'))
            print(fs.create_temp_dir('parent'))
            """);
        var output = ForceCandidates(CompileProject(project),
            "(++__collisionAttempt % 2 == 1 ? \"collision\" : __collisionAttempt.ToString())");
        File.SetAttributes(project.PathFor(collision), FileAttributes.ReadOnly);
        try
        {
            using (var held = File.Open(project.PathFor(collision), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = await Execute(project, shell, output);
                AssertSuccess(result);
                Assert.Equal(2, result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
            }
            Assert.Equal("locked content", File.ReadAllText(project.PathFor(collision)));
            Assert.Empty(File.ReadAllBytes(project.PathFor("parent/.rukbat-temp-2")));
            Assert.Empty(Directory.GetFileSystemEntries(project.PathFor("parent/.rukbat-temp-4")));
        }
        finally { File.SetAttributes(project.PathFor(collision), FileAttributes.Normal); }
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task AccessDeniedDoesNotRetryOrLeaveAnEntry(string shell)
    {
        if (!OperatingSystem.IsWindows()) throw new NotSupportedException("This test verifies Windows directory permissions.");
        using var project = new Project();
        var parent = Directory.CreateDirectory(project.PathFor("denied"));
        var original = parent.GetAccessControl();
        var restricted = parent.GetAccessControl();
        using var identity = WindowsIdentity.GetCurrent();
        restricted.AddAccessRule(new FileSystemAccessRule(identity.User!,
            FileSystemRights.CreateFiles | FileSystemRights.CreateDirectories, AccessControlType.Deny));
        project.Write("main.rukbat", """
            import fs
            import test
            rejected := false
            try { fs.create_temp_file('denied') } catch err { rejected = true }
            test.assert(rejected)
            rejected = false
            try { fs.create_temp_dir('denied') } catch err { rejected = true }
            test.assert(rejected)
            print(powershell<int>('Invoke-Expression', "[RukbatTemporaryEntries].GetField('__collisionAttempt', [Reflection.BindingFlags]'NonPublic, Static').GetValue($null)"))
            """);
        var output = ForceCandidates(CompileProject(project), "(++__collisionAttempt).ToString()");
        try
        {
            parent.SetAccessControl(restricted);
            var result = await Execute(project, shell, output);
            AssertSuccess(result);
            Assert.Equal("2\n", result.Output);
            Assert.Empty(parent.GetFileSystemInfos());
        }
        finally { parent.SetAccessControl(original); }
    }

    private static string ForceCandidates(string output, string expression)
    {
        // Substitute only candidate selection in the emitted helper, so real exclusive creation
        // and collision handling run against deterministic collisions without a production test hook.
        const string declaration = "public static class RukbatTemporaryEntries {";
        const string candidate = "return Path.Combine(directory, \".rukbat-temp-\" + Guid.NewGuid().ToString(\"N\"));";
        Assert.Equal(1, output.Split(candidate).Length - 1);
        return output.Replace(declaration, declaration + "\nprivate static int __collisionAttempt;")
            .Replace(candidate, "return Path.Combine(directory, \".rukbat-temp-\" + " + expression + ");");
    }

    private static string Literal(string value) => value.Replace("\\", "/").Replace("'", "\\'");

    private static string CompileProject(Project project)
    {
        var result = project.Compile();
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
        return result.Output!;
    }

    private static Task<RunResult> Execute(Project project, string shell, string output)
    {
        var script = project.PathFor("job.ps1");
        File.WriteAllText(script, output, new UTF8Encoding(true));
        return RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);
    }

    private static void AssertSuccess(RunResult result)
    {
        Assert.True(result.ExitCode == 0, result.Output + result.Error);
        Assert.Empty(result.Error);
    }

    private static async Task AssertProgram(Project project, string shell)
    {
        var result = await Execute(project, shell, CompileProject(project));
        AssertSuccess(result);
        Assert.Equal("passed\n", result.Output);
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
