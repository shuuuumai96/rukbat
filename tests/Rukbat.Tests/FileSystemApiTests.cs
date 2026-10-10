namespace Rukbat.Tests;

public class FileSystemApiTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("list_files('.', '*')", "string[]")]
    [InlineData("list_files_recursive('.', '*')", "string[]")]
    [InlineData("list_dirs('.', '*')", "string[]")]
    [InlineData("read_text('x', encoding.utf8)", "string")]
    [InlineData("is_file('x')", "bool")]
    [InlineData("is_dir('x')", "bool")]
    [InlineData("write_text('x', 'text', encoding.utf8)", "void")]
    [InlineData("append_text('x', 'text', encoding.utf8)", "void")]
    [InlineData("copy_file('x', 'y')", "void")]
    [InlineData("move_file('x', 'y')", "void")]
    [InlineData("move_dir('x', 'y')", "void")]
    [InlineData("remove_file('x')", "void")]
    [InlineData("create_dir_all('x')", "void")]
    [InlineData("remove_dir('x')", "void")]
    [InlineData("remove_dir_all('x')", "void")]
    [InlineData("move_files_batch(['x'], ['y'], 'journal')", "void")]
    [InlineData("recover_file_moves('journal')", "void")]
    public void CanonicalFilesystemOperationsHaveCheckedReturnTypes(string operation, string type)
    {
        // Check is intentionally non-executing, even for destructive operations.
        var source = $"import encoding; import fs\nfn operation(): {type} {{ return fs.{operation} }}";
        if (type == "void") source = $"import encoding; import fs\nfn operation(): void {{ fs.{operation} }}";
        Assert.Empty(new Rukbat.Compiler().Check(source));
        Assert.False(Compile($"import encoding; import fs\nwrong: int = fs.{operation}").Success);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("files")]
    [InlineData("directories")]
    public void NonexistentFilesystemModulesAreRejected(string module)
    {
        var source = $"import {module}\nprint('unreachable')";
        var checkedDiagnostics = new Rukbat.Compiler().Check(source);
        var diagnostic = Assert.Single(checkedDiagnostics);
        Assert.Equal("RUK3002", diagnostic.Code);
        Assert.Equal(1, diagnostic.Line);
        Assert.Equal($"Unknown standard module '{module}'.", diagnostic.Message);
        var compiled = Compile(source);
        Assert.Null(compiled.Output);
        Assert.Equal(checkedDiagnostics, compiled.Diagnostics);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("fs", "read('x', encoding.utf8)")]
    [InlineData("fs", "write('x', 'text', encoding.utf8)")]
    [InlineData("fs", "list('.', '*')")]
    [InlineData("fs", "list_recursive('.', '*')")]
    [InlineData("fs", "exists('x')")]
    [InlineData("fs", "create('x')")]
    [InlineData("fs", "create_dir('x')")]
    [InlineData("fs", "copy('x', 'y')")]
    [InlineData("fs", "move('x', 'y')")]
    [InlineData("fs", "move_batch(['x'], ['y'], 'j')")]
    [InlineData("fs", "recover_moves('j')")]
    [InlineData("fs", "remove('x')")]
    [InlineData("fs", "remove_all('x')")]
    [InlineData("fs", "join(['x', 'y'])")]
    [InlineData("fs", "parent('x')")]
    [InlineData("path", "exists('x')")]
    [InlineData("path", "is_dir('x')")]
    [InlineData("path", "read_text('x', encoding.utf8)")]
    [InlineData("path", "remove_file('x')")]
    public void AmbiguousOrMisplacedOperationsHaveNoAliases(string module, string call)
    {
        var source = $"import {module}\n{module}.{call}";
        var diagnostic = Assert.Single(new Rukbat.Compiler().Check(source));
        Assert.Equal("RUK2004", diagnostic.Code);
        Assert.Equal(2, diagnostic.Line);
        var name = call[..call.IndexOf('(')];
        Assert.Equal($"Unknown standard-library function '{module}.{name}'.", diagnostic.Message);
        Assert.Null(Compile(source).Output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void FilesystemArgumentDiagnosticsIdentifyTheOperationAndActualProblem()
    {
        var count = Assert.Single(new Rukbat.Compiler().Check("import fs\nfs.read_text('x')"));
        Assert.Contains("fs.read_text expects 2 positional arguments, got 1", count.Message);
        var named = Assert.Single(new Rukbat.Compiler().Check("import encoding; import fs\nfs.read_text(path: 'x', encoding: encoding.utf8)"));
        Assert.Contains("fs.read_text accepts positional arguments only", named.Message);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void FilesystemImportUsesOneCanonicalNameAndReservesItsLocalAlias()
    {
        Assert.False(Compile("import fs as filesystem").Success);
        Assert.False(Compile("import fs\nimport fs").Success);
        using var project = new Project();
        project.Write("local.rukbat", "fn name(): string { return 'local' }");
        project.Write("main.rukbat", "import './local.rukbat' as fs");
        Assert.Contains(project.Compile().Diagnostics, d => d.Message.Contains("reserved for a standard module"));
        project.Write("main.rukbat", "import './local.rukbat' as local\nimport fs\nprint(local.name()); print(fs.is_file('x'))");
        Assert.True(project.Compile().Success);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task FilesystemWorkflowUsesExplicitTargetsAndPreservesSafety(string shell)
    {
        using var project = new Project();
        var root = project.PathFor("data").Replace('\\', '/').Replace("'", "\\'");
        var output = Success($$"""
            import encoding; import fs
            import path

            root := '{{root}}'
            nested := path.join([root, 'nested'])
            fs.create_dir_all(nested)
            fs.create_dir_all(nested)
            source := path.join([root, 'source.txt'])
            copy := path.join([nested, 'copy.txt'])
            moved := path.join([nested, 'moved.txt'])
            fs.write_text(source, '東京', encoding.utf8)
            print(fs.is_file(source))
            print(fs.is_dir(source))
            print(fs.is_dir(root))
            print(fs.is_file(root))
            fs.copy_file(source, copy)
            try { fs.copy_file(source, copy) } catch err { print('existing destination') }
            fs.move_file(copy, moved)
            print(fs.is_file(copy))
            print(fs.read_text(moved, encoding.utf8))
            direct := fs.list_files(root, '*.txt')
            recursive := fs.list_files_recursive(root, '*.txt')
            print(direct.length)
            print(recursive.length)
            try { fs.remove_file(root) } catch err { print('directory is not a file') }
            try { fs.remove_dir(source) } catch err { print('file is not a directory') }
            try { fs.remove_dir(nested) } catch err { print('directory is not empty') }
            fs.remove_file(moved)
            fs.remove_file(moved)
            fs.remove_dir(nested)
            fs.remove_dir(nested)
            fs.remove_dir_all(root)
            print(fs.is_dir(root))
            """);
        var scriptPath = project.PathFor("workflow.ps1");
        // Match CLI deployment: Windows PowerShell 5.1 needs the UTF-8 source BOM,
        // and the shared deployed-script runner explicitly decodes stdout as UTF-8.
        File.WriteAllText(scriptPath, output, new System.Text.UTF8Encoding(true));
        var result = await RunDeployed(shell, scriptPath, project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal("True\nFalse\nTrue\nFalse\nexisting destination\nFalse\n東京\n1\n2\ndirectory is not a file\nfile is not a directory\ndirectory is not empty\nFalse\n", result.Output);
        Assert.False(Directory.Exists(project.PathFor("data")));
    }
}
