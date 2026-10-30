using Rukbat;

namespace Rukbat.Tests
{
    public sealed class BlankLineFormattingTests : TestBase
    {
        [Theory]
        [InlineData("value:=1")]
        [InlineData("value:=1\n")]
        [InlineData("value:=1\n\n")]
        [InlineData("value:=1\r\n\r\n")]
        [Trait("Category", "Unit")]
        public void EndsWithOneNewlineWithoutTrailingBlankLines(string source)
        {
            using var project = new Project();
            var input = project.PathFor("main.rukbat");
            project.Write("main.rukbat", source);
            Assert.Equal(0, Cli.Run(["fmt", input], TextWriter.Null, TextWriter.Null));
            Assert.Equal("value := 1\n", File.ReadAllText(input));
            Assert.Equal(0, Cli.Run(["fmt", input], TextWriter.Null, TextWriter.Null));
            Assert.Equal("value := 1\n", File.ReadAllText(input));
        }

        [Theory]
        [InlineData("a:=1\n\nb:=2", "a := 1\n\nb := 2\n")]
        [InlineData("a:=1\n \n\t\n\nb:=2\n\n\n", "a := 1\n\nb := 2\n")]
        [InlineData("\r\n\r\na:=1\r\n\r\nb:=2\r\n", "a := 1\n\nb := 2\n")]
        [InlineData("fn f():void{\n\nprint(1)\n\n\nprint(2)\n\n}\n\nfn g():void{}", "fn f(): void {\n    print(1)\n\n    print(2)\n}\n\nfn g(): void {\n}\n")]
        [InlineData("// first\n\n// second\n\na:=1", "// first\n\n// second\n\na := 1\n")]
        [InlineData("if true{print(1)}\n\nelse{print(2)}", "if true {\n    print(1)\n} else {\n    print(2)\n}\n")]
        [InlineData("a:=1+\n\n2;b:=[1,\n\n2]", "a := 1 + 2\nb := [1, 2]\n")]
        [InlineData("a:=1;\n\nb:=2", "a := 1\n\nb := 2\n")]
        [InlineData("a:=1", "a := 1\n")]
        [InlineData("a:=1\n\n", "a := 1\n")]
        [InlineData("// comment", "// comment\n")]
        [InlineData("", "\n")]
        [InlineData("\n\n \t\n", "\n")]
        [InlineData("import csv;import fs;fn f():void{};arguments:=args();try{}catch err{}", "import csv\nimport fs\n\nfn f(): void {\n}\n\narguments := args()\n\ntry {\n} catch err {\n}\n")]
        [InlineData("a:=1;if true{};b:=2;while false{};for item in []{};try{}catch err{}", "a := 1\n\nif true {\n}\n\nb := 2\n\nwhile false {\n}\n\nfor item in [] {\n}\n\ntry {\n} catch err {\n}\n")]
        [InlineData("a:=1\n// function documentation\nfn f():void{}", "a := 1\n\n// function documentation\nfn f(): void {\n}\n")]
        [InlineData("a:=1 // trailing\n// function documentation\nfn f():void{}", "a := 1  // trailing\n\n// function documentation\nfn f(): void {\n}\n")]
        [InlineData("fn f():void{} // trailing\n// next function\nfn g():void{}", "fn f(): void {\n}  // trailing\n\n// next function\nfn g(): void {\n}\n")]
        [InlineData("if true{if false{}else{};print(1)}else{print(2)}", "if true {\n    if false {\n    } else {\n    }\n\n    print(1)\n} else {\n    print(2)\n}\n")]
        [Trait("Category", "Unit")]
        public void NormalizesBlankLinesAndEndsWithOneNewline(string source, string expected)
        {
            using var project = new Project();
            var input = project.PathFor("main.rukbat");
            project.Write("main.rukbat", source);
            using var errors = new StringWriter();
            Assert.True(Cli.Run(["fmt", input], TextWriter.Null, errors) == 0, errors.ToString());
            Assert.Equal(expected, File.ReadAllText(input));
            Assert.Equal(0, Cli.Run(["fmt", input], TextWriter.Null, errors));
            Assert.Equal(expected, File.ReadAllText(input));
            var compiler = new Compiler();
            Assert.Equal(
                compiler.Check(source).Select(diagnostic => (diagnostic.Code, diagnostic.Message)),
                compiler.Check(expected).Select(diagnostic => (diagnostic.Code, diagnostic.Message)));
        }

        [Fact]
        [Trait("Category", "Unit")]
        public void RestoresReadableGroupsInBatchOrganizeWithoutExistingBlankLines()
        {
            using var project = new Project();
            var fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples", "batch-organize", "main.rukbat");
            var source = string.Join("\n", File.ReadAllLines(fixture).Where(line => !string.IsNullOrWhiteSpace(line)));
            var input = project.PathFor("main.rukbat");
            project.Write("main.rukbat", source);
            var compiler = new Compiler();
            Assert.Empty(compiler.CheckFile(input));
            Assert.Equal(0, Cli.Run(["fmt", input], TextWriter.Null, TextWriter.Null));
            var formatted = File.ReadAllText(input);
            Assert.Contains("import csv\nimport encoding\nimport fs\nimport path\nimport text\n\nfn usage()", formatted);
            Assert.Contains("}\n\narguments: string[] = args()\n\ntry {", formatted);
            Assert.Contains("        }\n\n        fs.recover_file_moves", formatted);
            Assert.Contains("        exit(0)\n    }\n\n    if arguments.length", formatted);
            Assert.Contains("    }\n\n    rows := csv.parse", formatted);
            Assert.Contains("    rows := csv.parse(fs.read_text(arguments[0], encoding.utf8))\n\n    if rows.length", formatted);
            Assert.Contains("    }\n\n    header := rows[0]\n\n    if header.length", formatted);
            Assert.Contains("    sources: string[] = []\n    destinations: string[] = []\n    record := 1\n\n    while record", formatted);
            Assert.Contains("        row := rows[record]\n\n        if row.length", formatted);
            Assert.Contains("        }\n\n        sources.append", formatted);
            Assert.Contains("    }\n\n    fs.move_files_batch", formatted);
            Assert.Contains("} catch err {\n    eprint(err.message)\n    exit(2)\n}\n", formatted);
            Assert.DoesNotContain("\n\n\n", formatted);
            Assert.Empty(compiler.CheckFile(input));
            Assert.Equal(0, Cli.Run(["fmt", input], TextWriter.Null, TextWriter.Null));
            Assert.Equal(formatted, File.ReadAllText(input));
        }
    }
}
