using System.Text;
using System.Text.Json;

namespace Auvil.Tests;

public partial class CompilerTests
{
    private const string FileBrowserEntry = "examples/file-browser/main.auv";

    private static void CopyLibraryAndFileBrowser(Project project)
    {
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures");
        foreach (var source in Directory.EnumerateFiles(fixtures, "*.auv", SearchOption.AllDirectories))
        {
            project.Write(Path.GetRelativePath(fixtures, source), File.ReadAllText(source));
        }
    }

    private static void CreateBrowserData(Project project)
    {
        project.Write("data [日本語]/alpha.txt", "alpha");
        project.Write("data [日本語]/日本語.txt", "text");
        project.Write("data [日本語]/note.log", "log");
        project.Write("data [日本語]/folder.txt/nested.txt", "not a direct child");
    }

    [Theory]
    [InlineData("*", new[] { "alpha.txt", "日本語.txt", "note.log" })]
    [InlineData("*.txt", new[] { "alpha.txt", "日本語.txt" })]
    [InlineData("alpha.*", new[] { "alpha.txt" })]
    [InlineData("*.missing", new string[] { })]
    public async Task FileBrowserWritesFilteredJsonToStdout(string pattern, string[] expected)
    {
        using var project = new Project();
        CopyLibraryAndFileBrowser(project);
        CreateBrowserData(project);
        var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "data [日本語]", "--pattern", pattern]);
        Assert.True(result.ExitCode == 0, result.Error);
        var names = JsonSerializer.Deserialize<string[]>(result.Output)!;
        Assert.Equal(expected.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
        Assert.Equal("", result.Error);
    }

    [Fact]
    public async Task FileBrowserDefaultsToAllFilesAndWritesUtf8JsonToLiteralPath()
    {
        using var project = new Project();
        CopyLibraryAndFileBrowser(project);
        CreateBrowserData(project);
        project.Write("result [日本語].json", "old contents");
        var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "data [日本語]", "--output", "result [日本語].json"]);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Output);
        Assert.Equal("", result.Error);
        var bytes = File.ReadAllBytes(project.PathFor("result [日本語].json"));
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var text = Encoding.UTF8.GetString(bytes);
        Assert.EndsWith("]", text);
        var names = JsonSerializer.Deserialize<string[]>(text)!;
        Assert.Equal(new[] { "alpha.txt", "note.log", "日本語.txt" }, names.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task FileBrowserCombinesFilterAndFileOutputAndSupportsStandaloneScript()
    {
        using var project = new Project();
        CopyLibraryAndFileBrowser(project);
        CreateBrowserData(project);
        var compiled = new Compiler().CompileFile(project.PathFor(FileBrowserEntry));
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        foreach (var file in Directory.EnumerateFiles(project.DirectoryPath, "*.auv", SearchOption.AllDirectories))
        {
            File.Delete(file);
        }
        var output = project.PathFor("selected.json");
        var result = await project.RunOutput(compiled.Output!, project.PathFor("data [日本語]"), "--output", output, "--pattern", "*.txt");
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Output);
        Assert.Equal("", result.Error);
        Assert.Equal(new[] { "alpha.txt", "日本語.txt" }, JsonSerializer.Deserialize<string[]>(File.ReadAllText(output))!.Order(StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(new string[] { }, "Usage:")]
    [InlineData(new[] { "" }, "ディレクトリを指定")]
    [InlineData(new[] { ".", "--pattern" }, "--pattern の値")]
    [InlineData(new[] { ".", "--output" }, "--output の値")]
    [InlineData(new[] { ".", "--pattern", "" }, "空でないパターン")]
    [InlineData(new[] { ".", "--output", "" }, "空でない保存先")]
    [InlineData(new[] { ".", "--pattern", "*", "--pattern", "*.txt" }, "一度だけ")]
    [InlineData(new[] { ".", "--output", "one.json", "--output", "two.json" }, "一度だけ")]
    [InlineData(new[] { ".", "--unknown" }, "不明なオプション")]
    [InlineData(new[] { ".", "extra" }, "余分な引数")]
    [InlineData(new[] { "missing-directory" }, "ディレクトリを参照できません")]
    public async Task FileBrowserReportsInputFailuresOnStderr(string[] arguments, string message)
    {
        using var project = new Project();
        CopyLibraryAndFileBrowser(project);
        var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", .. arguments]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.Contains(message, result.Error);
    }

    [Fact]
    public async Task FileBrowserHelpNeedsNoDirectoryAndSucceeds()
    {
        using var project = new Project();
        CopyLibraryAndFileBrowser(project);
        var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "--help"]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("Usage:", result.Output);
        Assert.Contains("--pattern", result.Output);
        Assert.Contains("--output", result.Output);
        Assert.Equal("", result.Error);
    }

    [Fact]
    public async Task FileBrowserMissingInputDoesNotOverwriteOutput()
    {
        using var project = new Project();
        CopyLibraryAndFileBrowser(project);
        project.Write("result.json", "preserved");
        var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "missing-directory", "--output", "result.json"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.Equal("preserved", File.ReadAllText(project.PathFor("result.json")));
    }

    [Fact]
    public async Task FileBrowserWriteFailureIsNotReportedAsSuccess()
    {
        using var project = new Project();
        CopyLibraryAndFileBrowser(project);
        CreateBrowserData(project);
        var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "data [日本語]", "--output", "missing-parent/result.json"]);
        Assert.Equal(1, result.ExitCode);
        Assert.Equal("", result.Output);
        Assert.Contains("error:", result.Error);
        Assert.False(File.Exists(project.PathFor("missing-parent/result.json")));
    }

    [Theory]
    [InlineData()]
    [InlineData("one")]
    [InlineData("日本語", "", "quote\"", "line\nbreak", "back\\slash")]
    public async Task JsonLibraryPreservesArrayShapeAndEscapesValues(params string[] values)
    {
        using var project = new Project();
        CopyLibraryAndFileBrowser(project);
        project.Write("main.auv", "import json\nprint(json.encode(args()))");
        var result = await RunCli(project, ["run", project.PathFor("main.auv"), "--", .. values]);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(values, JsonSerializer.Deserialize<string[]>(result.Output));
        Assert.Equal("", result.Error);
    }
}
