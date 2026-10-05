using Rukbat;

namespace Rukbat.Tests;

[Trait("Category", "Unit")]
public sealed class DocumentationTests : TestBase
{
    private static (int Code, string Output, string Errors) Doc(Project project, params string[] arguments)
    {
        using var output = new StringWriter();
        using var errors = new StringWriter();
        var code = Cli.Run(["doc", project.PathFor("main.rukbat"), .. arguments], output, errors);
        return (code, output.ToString(), errors.ToString());
    }

    [Fact]
    public void GeneratesOfflineDocumentationInSourceOrderAndLinksBoundTypes()
    {
        using var project = new Project();
        project.Write("models.rukbat", "//! Shared records.\n/// A person.\nstruct Person {\n    /// Display name.\n    name: string\n    /// Changes the display name.\n    fn rename(name: string): void { self.name = name }\n}\n");
        project.Write("main.rukbat", "//! # Usage\n//! Creates a report without changing input.\nimport './models.rukbat' as model\nimport './models.rukbat' as again\n/// Creates a [`model.Person`].\nfn make(): model.Person { return model.Person { name: 'Taro' } }\nworking := 'not documentation'\n/// Configured report title.\ntitle := 'Report'\nfn undocumented(): void {}\n");
        var result = Doc(project);
        Assert.True(result.Code == 0, result.Errors);
        var html = File.ReadAllText(project.PathFor("main.html"));
        Assert.Contains("<h1>Usage</h1>", html);
        Assert.True(html.IndexOf("Usage", StringComparison.Ordinal) < html.IndexOf("Index</h2>", StringComparison.Ordinal));
        Assert.Contains("Display name.", html);
        Assert.Contains("Changes the display name.", html);
        Assert.Contains("Module-local variable", html);
        Assert.Contains("undocumented", html);
        Assert.DoesNotContain("not documentation", html);
        Assert.DoesNotContain("working", html);
        Assert.DoesNotContain(project.DirectoryPath, html);
        Assert.Contains("<a href=\"#module-", html);
        Assert.Contains("</a></code></pre>", html);
        Assert.Equal(1, html.Split("<h3>Person</h3>").Length - 1);
        Assert.False(File.ReadAllBytes(project.PathFor("main.html")).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
        Assert.Equal(html, GenerateAgain());
        string GenerateAgain() { Assert.Equal(0, Doc(project).Code); return File.ReadAllText(project.PathFor("main.html")); }
    }

    [Fact]
    public void CheckValidatesExamplesWithSelfImportsWithoutExecutingAnything()
    {
        using var project = new Project();
        var marker = project.PathFor("should-not-exist.txt").Replace('\\', '/');
        project.Write("main.rukbat", $"//! ```rukbat check\n//! import './main.rukbat' as api\n//! api.number()\n//! throw Error {{ message: 'must never execute' }}\n//! ```\nimport encoding\nimport fs\nfs.write_text('{marker}', 'executed', encoding.utf8)\nfn number(): int {{ return 1 }}\n");
        var result = Doc(project, "--check");
        Assert.True(result.Code == 0, result.Errors);
        Assert.Equal("", result.Output);
        Assert.False(File.Exists(project.PathFor("main.html")));
        Assert.False(File.Exists(project.PathFor("main.ps1")));
        Assert.False(File.Exists(project.PathFor("should-not-exist.txt")));
        Assert.Equal(0, Doc(project).Code);
        Assert.Contains("Statically checked; not executed.", File.ReadAllText(project.PathFor("main.html")));
        Assert.False(File.Exists(project.PathFor("should-not-exist.txt")));
        Assert.Empty(Directory.GetFiles(project.DirectoryPath, ".rukbat-doc-*"));
    }

    [Fact]
    public void OrdinaryExamplesAndCodeStringsAreNotDocumentation()
    {
        using var project = new Project();
        project.Write("main.rukbat", "//! ```rukbat\n//! incomplete(\n//! [`missing`]\n//! ```\n//// divider\nvalue := '/// not a comment'\n");
        Assert.Equal(0, Doc(project, "--check").Code);
        Assert.Equal(0, Doc(project).Code);
        Assert.DoesNotContain("Statically checked", File.ReadAllText(project.PathFor("main.html")));
    }

    [Fact]
    public void EmptyAndNestedCheckedExamplesUseCommonMarkFences()
    {
        using var project = new Project();
        project.Write("main.rukbat", "//! ```rukbat check\n//! ```\n//!\n//! > ~~~rukbat check\n//! > print(1)\n//! > ~~~\n");
        var result = Doc(project);
        Assert.True(result.Code == 0, result.Errors);
        Assert.Equal(2, File.ReadAllText(project.PathFor("main.html")).Split("Statically checked; not executed.").Length - 1);
    }

    [Theory]
    [InlineData("/// Orphan\n\nfn f(): void {}", "RUK6000")]
    [InlineData("x := 1 // normal\n//! Late overview", "RUK6000")]
    [InlineData("//! First\n\n//! Second", "RUK6000")]
    [InlineData("fn f(): void {\n/// Local\nx := 1\n}", "RUK6000")]
    [InlineData("x := 1 /// Inline", "RUK6000")]
    [InlineData("/// See [`missing`].\nfn f(): void {}", "RUK6001")]
    [InlineData("//! ```rukbat check\n//! x: int = 'wrong'\n//! ```", "RUK2001")]
    [InlineData("//! ```rukbat check\n//! import './missing.rukbat' as missing\n//! ```", "RUK3002")]
    public void InvalidDocumentationLeavesExistingOutputAndOrdinaryCheckUnaffected(string source, string code)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        project.Write("main.html", "approved output");
        var result = Doc(project);
        Assert.Equal(1, result.Code);
        Assert.Contains(code, result.Errors);
        Assert.Equal("approved output", File.ReadAllText(project.PathFor("main.html")));
        Assert.Equal(0, Cli.Run(["check", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
    }

    [Fact]
    public void ExampleDiagnosticsPointToTheOriginalUnicodeCrlfComment()
    {
        using var project = new Project();
        project.Write("main.rukbat", "//! 日本語の例\r\n//!\r\n//! ```rukbat check\r\n//!     value: int = \"東京\"\r\n//! ```\r\n");
        var result = Doc(project, "--check");
        Assert.Equal(1, result.Code);
        Assert.Contains("main.rukbat(4,22): error RUK2001", result.Errors);
        Assert.DoesNotContain(".rukbat-doc-", result.Errors);
    }

    [Fact]
    public void ExampleImportsResolveBesideImportedModuleRatherThanEntryOrOutput()
    {
        using var project = new Project();
        project.Write("main.rukbat", "import './lib/helper.rukbat' as helper\n");
        project.Write("lib/other.rukbat", "fn value(): int { return 1 }");
        project.Write("lib/helper.rukbat", "//! ```rukbat check\n//! import './other.rukbat' as other\n//! value: int = other.value()\n//! ```\nfn f(): void {}\n");
        var result = Doc(project, "--output", project.PathFor("elsewhere.html"));
        Assert.True(result.Code == 0, result.Errors);
        Assert.True(File.Exists(project.PathFor("elsewhere.html")));
    }

    [Fact]
    public void MarkdownIsEscapedAndNeverLoadsExternalImagesOrActiveContent()
    {
        using var project = new Project();
        project.Write("main.rukbat", "//! <script>alert(1)</script>\n//!\n//! ![picture](https://example.org/picture.png)\n//! [unsafe](javascript:alert%281%29)\n//! [safe](https://example.org/)\n//!\n//! - **Bold**\n//! - `code`\n");
        Assert.Equal(0, Doc(project).Code);
        var html = File.ReadAllText(project.PathFor("main.html"));
        Assert.DoesNotContain("<script>", html);
        Assert.DoesNotContain("<img", html);
        Assert.DoesNotContain("href=\"javascript:", html);
        Assert.Contains("href=\"https://example.org/\"", html);
        Assert.Contains("<strong>Bold</strong>", html);
        Assert.Contains("Content-Security-Policy", html);
    }

    [Fact]
    public void OrdinaryMarkdownReferencesTakePrecedenceAndEscapedSymbolReferencesRemainLiteral()
    {
        using var project = new Project();
        project.Write("main.rukbat", "//! [`external`]\n//!\n//! [`external`]: https://example.org/\n//!\n//! \\[\\`not_a_link\\`\\]\n");
        var result = Doc(project);
        Assert.True(result.Code == 0, result.Errors);
        Assert.Contains("href=\"https://example.org/\"", File.ReadAllText(project.PathFor("main.html")));
    }

    [Theory]
    [InlineData("--strict")]
    [InlineData("--output")]
    [InlineData("--check", "--output", "out.html")]
    [InlineData("--output", "main.rukbat")]
    public void RejectsInvalidOptions(params string[] arguments)
    {
        using var project = new Project();
        project.Write("main.rukbat", "");
        Assert.Equal(2, Doc(project, arguments).Code);
    }

    [Fact]
    public void IoAndSourceErrorsAreReportedWithoutCreatingOutput()
    {
        using var project = new Project();
        Assert.Equal(2, Doc(project).Code);
        project.Write("main.rukbat", "x := missing");
        Assert.Equal(1, Doc(project).Code);
        project.Write("main.rukbat", "");
        Assert.Equal(2, Doc(project, "--output", project.PathFor("missing/out.html")).Code);
        Assert.False(File.Exists(project.PathFor("main.html")));
    }

    [Fact]
    public async Task DocumentationGenerationWorksWithoutPowerShellOnPath()
    {
        using var project = new Project();
        project.Write("main.rukbat", "//! A small script.\n//! ```rukbat check\n//! print(1)\n//! ```\nprint(1)");
        var result = await RunCli(project, ["doc", "main.rukbat"], hidePowerShell: true);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.True(File.Exists(project.PathFor("main.html")));
    }
}
