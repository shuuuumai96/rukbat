namespace Rukbat.Tests;

public class FileMetadataTests : TestBase
{
    [Trait("Category", "Unit")]
    [Fact]
    public void MetadataTypesAreBoundWithoutAnExplicitDatetimeImport() => Success("""
        import fs
        fn size(info: fs.Info): int? { return info.size }
        info := fs.stat('file.txt')
        result: int? = size(info)
        directory: bool = info.is_dir
        """);

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("fs.stat(1)")]
    [InlineData("fs.stat(path: 'x')")]
    [InlineData("info := fs.stat('x'); count: int = info.size")]
    [InlineData("info := fs.stat('x'); value: string = info.modified")]
    public void RejectsInvalidCallsAndFieldTypes(string source) => Assert.False(Compile("import fs\n" + source).Success);

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ReadsExactSizeUtcTimestampDirectoriesAndErrors(string shell)
    {
        using var project = new Project();
        var file = project.PathFor("data.bin");
        File.WriteAllBytes(file, [1, 2, 3]);
        File.SetLastWriteTimeUtc(file, new DateTime(2024, 2, 29, 3, 4, 5, DateTimeKind.Utc));
        Directory.CreateDirectory(project.PathFor("folder"));
        var root = project.DirectoryPath.Replace('\\', '/');
        project.Write("main.rukbat", $$"""
            import fs; import datetime; import test
            info := fs.stat('{{root}}/data.bin')
            test.assert(!info.is_dir)
            size := info.size
            if size != null { test.assert(size == 3) } else { test.fail('Missing file size') }
            test.assert(info.modified == datetime.parse('2024-02-29T03:04:05Z'))
            cutoff := datetime.parse('2025-01-01T00:00:00Z')
            test.assert(info.modified < cutoff)
            folder := fs.stat('{{root}}/folder')
            test.assert(folder.is_dir)
            test.assert(folder.size == null)
            copied := info
            copied.size = 99
            test.assert(info.size != null)
            rejected := false
            try { fs.stat('{{root}}/missing') } catch err { rejected = true }
            test.assert(rejected)
            powershell<void>('New-Item', Path: '{{root}}/link', ItemType: 'Junction', Target: '{{root}}/folder')
            link_rejected := false
            try { fs.stat('{{root}}/link') } catch err { link_rejected = true }
            test.assert(link_rejected)
            fs.remove_dir('{{root}}/link')
            test.assert(fs.is_dir('{{root}}/folder'))
            print('passed')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("passed\n", result.Output);
        Assert.Empty(result.Error);
    }
}
