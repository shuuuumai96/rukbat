using Rukbat;

namespace Rukbat.Tests
{
    public class CommentTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("# comment", 1, 1)]
        [InlineData("value := 1 # comment", 1, 12)]
        [InlineData("print(1)\n# comment", 2, 1)]
        [InlineData("values := [1, # comment\n2]", 1, 15)]
        public void HashOutsideStringsAndCommentsIsRejected(string source, int line, int column)
        {
            var compiled = Compile(source);
            Assert.Null(compiled.Output);
            var diagnostic = Assert.Single(compiled.Diagnostics);
            Assert.Equal("RUK1000", diagnostic.Code);
            Assert.Equal("Unexpected character '#'.", diagnostic.Message);
            Assert.Equal(line, diagnostic.Line);
            Assert.Equal(column, diagnostic.Column);
            Assert.Equal(compiled.Diagnostics, new Compiler().Check(source));

            using var project = new Project();
            project.Write("main.rukbat", source);
            var original = File.ReadAllBytes(project.PathFor("main.rukbat"));
            using var errors = new StringWriter();
            Assert.Equal(1, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, errors));
            Assert.Contains("RUK1000", errors.ToString());
            Assert.Equal(original, File.ReadAllBytes(project.PathFor("main.rukbat")));
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("// # $ { invalid syntax", "// # $ { invalid syntax\n")]
        [InlineData("value:=8/2 // # trailing", "value := 8 / 2  // # trailing\n")]
        [InlineData("value:='# // data'", "value := '# // data'\n")]
        [InlineData("value:=\"https://example.test/#fragment\"", "value := \"https://example.test/#fragment\"\n")]
        public void DoubleSlashCommentsAndLiteralMarkersArePreserved(string source, string expected)
        {
            Assert.Empty(new Compiler().Check(source));
            Success(source);
            using var project = new Project();
            project.Write("main.rukbat", source);
            using var errors = new StringWriter();
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, errors));
            var formatted = File.ReadAllText(project.PathFor("main.rukbat"));
            Assert.Equal(expected, formatted);
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, errors));
            Assert.Equal(formatted, File.ReadAllText(project.PathFor("main.rukbat")));
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task CommentsDoNotChangeDivisionStringsOrPowerShellSource(string shell)
        {
            var output = Success("""
            // # $ { invalid syntax
            value := 8 / 2 // throw Error { message: 'ignored' }
            print(value)
            print('# // data')
            print("https://example.test/#fragment")
            native := powershell<string>('Invoke-Expression', "'# PowerShell string' # native comment")
            print(native)
            // comment at the end
            """);
            using var project = new Project();
            var script = project.PathFor("comments.ps1");
            File.WriteAllText(script, output, new System.Text.UTF8Encoding(true));
            var result = await RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);
            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            Assert.Equal("4\n# // data\nhttps://example.test/#fragment\n# PowerShell string\n", result.Output);
        }
    }
}
