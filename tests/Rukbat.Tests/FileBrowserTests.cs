using System.Text;
using System.Text.Json;

namespace Rukbat.Tests
{
    public class FileBrowserTests : TestBase
    {
        private const string FileBrowserEntry = "examples/file-browser/main.rukbat";

        private static void CopyFileBrowser(Project project)
        {
            var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples", "file-browser");
            foreach (var source in Directory.EnumerateFiles(fixtures, "*.rukbat", SearchOption.AllDirectories))
            {
                project.Write("examples/file-browser/" + Path.GetRelativePath(fixtures, source), File.ReadAllText(source));
            }
        }

        private static void CreateBrowserData(Project project)
        {
            project.Write("data [日本語]/alpha.txt", "alpha");
            project.Write("data [日本語]/日本語.txt", "text");
            project.Write("data [日本語]/note.log", "log");
            project.Write("data [日本語]/folder.txt/nested.txt", "not a direct child");
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("*", new[] { "alpha.txt", "日本語.txt", "note.log" })]
        [InlineData("*.txt", new[] { "alpha.txt", "日本語.txt" })]
        [InlineData("alpha.*", new[] { "alpha.txt" })]
        [InlineData("*.missing", new string[] { })]
        public async Task FileBrowserWritesFilteredJsonToStdout(string pattern, string[] expected)
        {
            using var project = new Project();
            CopyFileBrowser(project);
            CreateBrowserData(project);
            var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "data [日本語]", "--pattern", pattern]);
            Assert.True(result.ExitCode == 0, result.Error);
            var names = JsonSerializer.Deserialize<string[]>(result.Output)!;
            Assert.Equal(expected.Order(StringComparer.Ordinal), names.Order(StringComparer.Ordinal));
            Assert.Equal("", result.Error);
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task FileBrowserDefaultsToAllFilesAndWritesUtf8JsonToLiteralPath()
        {
            using var project = new Project();
            CopyFileBrowser(project);
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

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task FileBrowserCombinesFilterAndFileOutputAndSupportsStandaloneScript()
        {
            using var project = new Project();
            CopyFileBrowser(project);
            CreateBrowserData(project);
            var compiled = new Compiler().CompileFile(project.PathFor(FileBrowserEntry));
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            foreach (var file in Directory.EnumerateFiles(project.DirectoryPath, "*.rukbat", SearchOption.AllDirectories))
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

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData(new string[] { }, "Usage:")]
        [InlineData(new[] { "" }, "Argument 'directory' requires a non-empty value")]
        [InlineData(new[] { ".", "--pattern" }, "Option '--pattern' requires a value")]
        [InlineData(new[] { ".", "--output" }, "Option '--output' requires a value")]
        [InlineData(new[] { ".", "--pattern", "" }, "Option '--pattern' requires a non-empty value")]
        [InlineData(new[] { ".", "--output", "" }, "Option '--output' requires a non-empty value")]
        [InlineData(new[] { ".", "--pattern", "*", "--pattern", "*.txt" }, "only be specified once")]
        [InlineData(new[] { ".", "--output", "one.json", "--output", "two.json" }, "only be specified once")]
        [InlineData(new[] { ".", "--unknown" }, "Unknown option")]
        [InlineData(new[] { ".", "extra" }, "Too many positional arguments")]
        [InlineData(new[] { "missing-directory" }, "Cannot access directory")]
        public async Task FileBrowserReportsInputFailuresOnStderr(string[] arguments, string message)
        {
            using var project = new Project();
            CopyFileBrowser(project);
            var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", .. arguments]);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.Contains(message, result.Error);
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task FileBrowserHelpNeedsNoDirectoryAndSucceeds()
        {
            using var project = new Project();
            CopyFileBrowser(project);
            var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "--help"]);
            Assert.Equal(0, result.ExitCode);
            AssertBrowserHelp(result.Output);
            Assert.Contains("--pattern", result.Output);
            Assert.Contains("--output", result.Output);
            Assert.Equal("", result.Error);
        }

        private static void AssertBrowserHelp(string output)
        {
            Assert.Contains(@"Usage: .\main.ps1 [options] DIRECTORY", output);
            Assert.Contains("Rukbat (from repository root):", output);
            Assert.Contains(@".\rukbat.cmd run examples/file-browser/main.rukbat -- [options] DIRECTORY", output);
            Assert.Contains("directory containing main.ps1", output);
            Assert.Contains("VS Code: Rukbat: Run File", output);
            Assert.Contains("[\"C:/Windows\", \"--pattern\", \"*.exe\"]", output);
            Assert.Contains("Help: [\"--help\"]", output);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task StandaloneHelpRetainsAllExecutionGuidanceWithoutSources(bool windowsPowerShell)
        {
            using var project = new Project();
            CopyFileBrowser(project);
            var compiled = new Compiler().CompileFile(project.PathFor(FileBrowserEntry));
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            foreach (var file in Directory.EnumerateFiles(project.DirectoryPath, "*.rukbat", SearchOption.AllDirectories))
            {
                File.Delete(file);
            }

            var result = windowsPowerShell
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!, "--help")
                : await project.RunOutput(compiled.Output!, "--help");
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("", result.Error);
            AssertBrowserHelp(result.Output);
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task FileBrowserMissingInputDoesNotOverwriteOutput()
        {
            using var project = new Project();
            CopyFileBrowser(project);
            project.Write("result.json", "preserved");
            var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "missing-directory", "--output", "result.json"]);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.Equal("preserved", File.ReadAllText(project.PathFor("result.json")));
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task FileBrowserWriteFailureIsNotReportedAsSuccess()
        {
            using var project = new Project();
            CopyFileBrowser(project);
            CreateBrowserData(project);
            var result = await RunCli(project, ["run", project.PathFor(FileBrowserEntry), "--", "data [日本語]", "--output", "missing-parent/result.json"]);
            Assert.Equal(1, result.ExitCode);
            Assert.Equal("", result.Output);
            Assert.Contains("error:", result.Error);
            Assert.False(File.Exists(project.PathFor("missing-parent/result.json")));
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData()]
        [InlineData("one")]
        [InlineData("日本語", "", "quote\"", "line\nbreak", "back\\slash")]
        public async Task JsonLibraryPreservesArrayShapeAndEscapesValues(params string[] values)
        {
            using var project = new Project();
            CopyFileBrowser(project);
            project.Write("main.rukbat", "import json\nprint(json.stringify(args()))");
            var result = await RunCli(project, ["run", project.PathFor("main.rukbat"), "--", .. values]);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal(values, JsonSerializer.Deserialize<string[]>(result.Output));
            Assert.Equal("", result.Error);
        }
    }
}
