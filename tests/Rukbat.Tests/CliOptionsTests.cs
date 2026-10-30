using System.Diagnostics;
using System.Text;
using Rukbat;

namespace Rukbat.Tests
{
    public sealed class CliOptionsTests : TestBase
    {
        private const string Definition = """
        import cli
        fn definition(): cli.Command {
            command := cli.command("demo.ps1", "ファイルを処理します。")
            directory := cli.argument("directory", "検索するフォルダー")
            tail := cli.argument("files", "追加のファイル")
            tail.required = false
            tail.repeatable = true
            command.arguments = [directory, tail]
            pattern := cli.option("pattern", "検索パターン")
            pattern.default_value = "*"
            pattern.short_name = "p"
            output := cli.option("output", "出力先")
            output.short_name = "o"
            output.value_name = "FILE"
            tag := cli.option("tag", "タグ")
            tag.repeatable = true
            command.options = [pattern, output, tag]
            recursive := cli.flag("recursive", "サブディレクトリも検索")
            recursive.short_name = "r"
            command.flags = [recursive]
            return command
        }
        fn read(result: cli.Result): string { return cli.get(result, "directory") }
        """;

        [Trait("Category", "Unit")]
        [Fact]
        public void ChecksAllDefinitionFieldsAndResultContainersWithoutRunningTheParser()
        {
            var output = Success(Definition + "\nparsed := cli.parse(definition(), args())\nvalues: cli.Result[] = [parsed]\nprint(read(values[0]))");
            Assert.Contains("$__rukbat_cli_parse", output);
            Assert.DoesNotContain("Add-Type", output);
            Assert.DoesNotContain("Get-Command", output);
            var checkedSource = new Compiler().Check(Definition + "\ncli.parse(definition(), ['--unknown'])");
            Assert.Empty(checkedSource);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void SupportsOrdinaryDefinitionStructLiterals()
        {
            Success("""
            import cli
            argument := cli.Argument {
                name: "directory", description: "Directory", required: true,
                repeatable: false, allow_empty: false, choices: []
            }
            option := cli.Option {
                name: "pattern", description: "Pattern", short_name: "p", value_name: "PATTERN",
                required: false, default_value: "*", repeatable: false, allow_empty: false, choices: []
            }
            flag := cli.Flag { name: "recursive", description: "Recursive", short_name: "r" }
            command := cli.Command {
                name: "demo.ps1", description: "Demo", arguments: [argument], options: [option], flags: [flag]
            }
            parsed := cli.parse(command, args())
            print(cli.get(parsed, "directory"))
            """);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("cli.parse(cli.command('x', ''), [1])")]
        [InlineData("cli.option('x')")]
        [InlineData("cli.command(name: 'x', description: '')")]
        [InlineData("cli.get(cli.command('x', ''), 'x')")]
        [InlineData("cli.parse<int>(cli.command('x', ''), [])")]
        [InlineData("cli.Result {}")]
        [InlineData("cli.parse(cli.command('x', ''), []).help")]
        [InlineData("print(cli.parse(cli.command('x', ''), []))")]
        [InlineData("import json\njson.stringify([cli.parse(cli.command('x', ''), [])])")]
        [InlineData("powershell<cli.Result>('Get-Date')")]
        [InlineData("powershell<void>('Write-Output', cli.parse(cli.command('x', ''), []))")]
        public void RejectsInvalidCallsAndOpaqueResultBoundaries(string source)
        {
            var result = Compile("import cli\n" + source);
            Assert.False(result.Success);
            Assert.DoesNotContain(result.Diagnostics, d => d.Code == "RUK9000");
        }

        private static async Task<RunResult> Execute(Project project, string output, string shell, params string[] arguments)
        {
            File.WriteAllText(project.PathFor("main.ps1"), output, new UTF8Encoding(true));
            var start = new ProcessStartInfo(shell)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = project.DirectoryPath
            };
            foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", project.PathFor("main.ps1") }.Concat(arguments))
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) { process.Kill(true); throw new TimeoutException("CLI runtime did not finish."); }
            return new(process.ExitCode, (await stdout).Replace("\r\n", "\n"), await stderr);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task ParsesLiteralEntryArgumentsAndReturnsIndependentArrays(string shell)
        {
            using var project = new Project();
            var output = Success(Definition + """

            parsed := cli.parse(definition(), args())
            print(read(parsed))
            print(cli.get(parsed, "pattern"))
            print(cli.get(parsed, "output"))
            print(cli.has(parsed, "recursive"))
            print(cli.has(parsed, "pattern"))
            files := cli.get_all(parsed, "files")
            print(files.length)
            for file in files { print(file) }
            tags := cli.get_all(parsed, "tag")
            print(tags.length)
            tags[0] = "changed"
            for tag in cli.get_all(parsed, "tag") { print(tag) }
            """);
            var result = await Execute(project, output, shell, "--tag=a=b", "日本語 path", "-p", "*.log", "-o=--help", "-r", "--tag", "$literal; $(not-code)", "--", "--help", "a\"b", @"C:\path with space\");
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("日本語 path\n*.log\n--help\nTrue\nTrue\n3\n--help\na\"b\nC:\\path with space\\\n2\na=b\n$literal; $(not-code)\n", result.Output);
            Assert.Equal("", result.Error);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task HandlesHelpDefaultsEmptyValuesAndValueCopies(string shell)
        {
            using var project = new Project();
            var output = Success(Definition + """

            import test
            command := definition()
            copy := command
            copy.options[0].default_value = "changed"
            normal := cli.parse(command, ["folder", "-o", "-1e3"])
            test.assert(cli.get(normal, "pattern") == "*")
            test.assert(cli.get(normal, "output") == "-1e3")
            test.assert(!cli.has(normal, "recursive"))
            test.assert(cli.get_all(normal, "files").length == 0)
            test.assert(cli.get_all(normal, "tag").length == 0)
            command.options[0].default_value = "later"
            test.assert(cli.get(normal, "pattern") == "*")
            help := cli.parse(command, ["--unknown", "--output", "--help"])
            test.assert(cli.help_requested(help))
            try { cli.has(help, "output"); test.fail("help data was readable") }
            catch err { test.assert(err.message == "Help was requested. Handle it before reading arguments.") }
            empty := cli.option("empty", "空文字を許可")
            empty.allow_empty = true
            empty.default_value = "fallback"
            command.options.append(empty)
            parsed := cli.parse(command, ["folder", "--empty="])
            test.assert(cli.has(parsed, "empty"))
            test.assert(cli.get(parsed, "empty") == "")
            print("passed")
            """);
            var result = await Execute(project, output, shell);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("passed\n", result.Output);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell", "['folder', '--unknown']", "Unknown option '--unknown'.")]
        [InlineData("pwsh", "['folder', '--unknown']", "Unknown option '--unknown'.")]
        [InlineData("powershell", "['folder', '--output', '--recursive']", "requires a value")]
        [InlineData("pwsh", "['folder', '--output', '--recursive']", "requires a value")]
        [InlineData("powershell", "['folder', '--pattern=a', '-p', 'a']", "may only be specified once")]
        [InlineData("pwsh", "['folder', '--pattern=a', '-p', 'a']", "may only be specified once")]
        [InlineData("powershell", "['folder', '-r', '--recursive']", "may only be specified once")]
        [InlineData("pwsh", "['folder', '-r', '--recursive']", "may only be specified once")]
        [InlineData("powershell", "['folder', '--recursive=false']", "does not take a value")]
        [InlineData("pwsh", "['folder', '--recursive=false']", "does not take a value")]
        [InlineData("powershell", "['folder', '--output=']", "requires a non-empty value")]
        [InlineData("pwsh", "['folder', '--output=']", "requires a non-empty value")]
        [InlineData("powershell", "[]", "Missing required argument 'directory'")]
        [InlineData("pwsh", "[]", "Missing required argument 'directory'")]
        [InlineData("powershell", "['folder', '-pr']", "Unknown option '-pr'")]
        [InlineData("pwsh", "['folder', '-pr']", "Unknown option '-pr'")]
        public async Task RejectsInputMistakesWithoutWritingToStreams(string shell, string arguments, string message)
        {
            using var project = new Project();
            var output = Success(Definition + "\ntry { cli.parse(definition(), " + arguments + "); print('not rejected') } catch err { print(err.message) }");
            var result = await Execute(project, output, shell);
            Assert.Equal(0, result.ExitCode);
            Assert.Contains(message, result.Output);
            Assert.Contains("Usage: demo.ps1", result.Output);
            Assert.Contains("Use --help for details.", result.Output);
            Assert.Equal("", result.Error);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task ValidatesDefinitionsEvenForHelpAndAfterFieldMutation(string shell)
        {
            using var project = new Project();
            var output = Success(Definition + """

            import test
            import text
            cases: cli.Command[] = []
            original := definition()
            bad := original
            bad.name = ""
            cases.append(bad)
            bad = original
            bad.name = "demo\nscript"
            cases.append(bad)
            bad = original
            bad.arguments[0].name = "help"
            cases.append(bad)
            bad = original
            bad.arguments[0].name = "Directory"
            cases.append(bad)
            bad = original
            bad.flags[0].name = "pattern"
            cases.append(bad)
            bad = original
            bad.options[0].short_name = "h"
            cases.append(bad)
            bad = original
            bad.flags[0].short_name = "p"
            cases.append(bad)
            bad = original
            bad.flags[0].short_name = "rr"
            cases.append(bad)
            bad = original
            bad.options[0].value_name = "FILE NAME"
            cases.append(bad)
            bad = original
            bad.options[0].required = true
            cases.append(bad)
            bad = original
            bad.options[0].repeatable = true
            cases.append(bad)
            bad = original
            bad.options[0].choices = ["txt"]
            cases.append(bad)
            bad = original
            bad.options[0].choices = ["*", "*"]
            cases.append(bad)
            bad = original
            bad.options[0].choices = ["", "*"]
            cases.append(bad)
            bad = original
            bad.arguments[0].required = false
            bad.arguments[1].required = true
            cases.append(bad)
            bad = original
            bad.arguments[0].repeatable = true
            cases.append(bad)
            bad = original
            bad.options[0].default_value = ""
            cases.append(bad)
            for command in cases {
                try { cli.parse(command, ["--help"]); test.fail("invalid definition parsed") }
                catch err { test.assert(text.starts_with(err.message, "Invalid CLI definition:")) }
                try { cli.help(command); test.fail("invalid definition rendered") }
                catch err { test.assert(text.starts_with(err.message, "Invalid CLI definition:")) }
            }
            print(cases.length)
            """);
            var result = await Execute(project, output, shell);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("17\n", result.Output);
            Assert.Equal("", result.Error);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task EnforcesChoicesRequiredOptionsGetterKindsAndErrorOrder(string shell)
        {
            using var project = new Project();
            var output = Success(Definition + """

            import test
            import text
            command := definition()
            format := cli.option("format", "Output format")
            format.required = true
            format.choices = ["json", "csv"]
            command.options.append(format)
            normal := cli.parse(command, ["--format=csv", "-0.5", "-", "--output=--"])
            test.assert(read(normal) == "-0.5")
            test.assert(cli.get(normal, "output") == "--")
            test.assert(cli.get_all(normal, "files")[0] == "-")
            try { cli.parse(command, ["folder"]); test.fail("required option omitted") }
            catch err { test.assert(text.starts_with(err.message, "Missing required option '--format'.")) }
            try { cli.parse(command, ["folder", "--format=JSON"]); test.fail("case-insensitive choice") }
            catch err { test.assert(text.starts_with(err.message, "Option '--format' must be one of: json, csv.")) }
            try { cli.parse(command, ["", "--unknown"]); test.fail("empty positional accepted") }
            catch err { test.assert(text.starts_with(err.message, "Argument 'directory' requires a non-empty value.")) }
            try { cli.parse(command, ["folder", "--patten=x"]); test.fail("unknown option accepted") }
            catch err { test.assert(text.contains(err.message, "Did you mean '--pattern'?")) }
            try { cli.has(normal, "ouput"); test.fail("unknown getter name") }
            catch err { test.assert(err.message == "Unknown CLI item 'ouput'.") }
            try { cli.get(normal, "recursive"); test.fail("flag get") }
            catch err { test.assert(err.message == "Use cli.has to read flag 'recursive'.") }
            try { cli.get(normal, "tag"); test.fail("repeatable get") }
            catch err { test.assert(text.starts_with(err.message, "Item 'tag' is repeatable.")) }
            try { cli.get_all(normal, "output"); test.fail("single get_all") }
            catch err { test.assert(text.starts_with(err.message, "Item 'output' is not repeatable.")) }
            noOutput := cli.parse(command, ["folder", "--format=json"])
            test.assert(!cli.has(noOutput, "output"))
            try { cli.get(noOutput, "output"); test.fail("absent get") }
            catch err { test.assert(text.starts_with(err.message, "Item 'output' was not provided")) }
            help := cli.parse(command, ["--format", "--help"])
            test.assert(cli.help_requested(help))
            positional := cli.parse(definition(), ["--", "--help"])
            test.assert(!cli.help_requested(positional))
            test.assert(read(positional) == "--help")
            command.options[3].choices = ["é"]
            try { cli.parse(command, ["folder", "--format=é"]); test.fail("Unicode normalization changed the choice") }
            catch err { test.assert(text.starts_with(err.message, "Option '--format' must be one of:")) }
            print("passed")
            """);
            var result = await Execute(project, output, shell);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("passed\n", result.Output);
            Assert.Equal("", result.Error);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task GeneratesDeterministicUnicodeHelpWithoutATrailingNewline(string shell)
        {
            using var project = new Project();
            var output = Success("""
            import cli
            import text
            command := cli.command("demo.ps1", "ファイル一覧")
            command.arguments = [cli.argument("directory", "検索先\nフォルダー")]
            option := cli.option("pattern", "検索パターン")
            option.default_value = "*"
            option.short_name = "p"
            command.options = [option]
            recursive := cli.flag("recursive", "再帰検索")
            command.flags = [recursive]
            help := cli.help(command)
            print(help)
            print(text.ends_with(help, "\n"))
            """);
            var result = await Execute(project, output, shell);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("""
            ファイル一覧

            Usage: demo.ps1 [options] DIRECTORY

            Arguments:
              DIRECTORY
                検索先
                フォルダー

            Options:
              -p, --pattern PATTERN
                検索パターン
                Default: "*"

              --recursive
                再帰検索

              -h, --help
                Show this help.
            False

            """, result.Output);
            Assert.Equal("", result.Error);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task HelpAndInvalidInputsLeaveAnExistingOutputUntouched(string shell)
        {
            using var project = new Project();
            project.Write("report.txt", "preserved");
            project.Write("main.rukbat", """
            import cli
            import encoding
            import fs
            command := cli.command("main.ps1", "Demo")
            command.arguments = [cli.argument("directory", "Directory")]
            try {
                parsed := cli.parse(command, args())
                if cli.help_requested(parsed) { print(cli.help(command)); exit(0) }
                fs.write_text("report.txt", cli.get(parsed, "directory"), encoding.utf8)
            } catch err { eprint(err.message); exit(2) }
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.Delete(project.PathFor("main.rukbat"));
            var help = await Execute(project, compiled.Output!, shell, "--unknown", "--help");
            Assert.Equal(0, help.ExitCode);
            Assert.Equal("", help.Error);
            Assert.Contains("Usage: main.ps1", help.Output);
            Assert.Equal("preserved", File.ReadAllText(project.PathFor("report.txt")));
            var invalid = await Execute(project, compiled.Output!, shell, "--unknown");
            Assert.Equal(2, invalid.ExitCode);
            Assert.Equal("", invalid.Output);
            Assert.Contains("Unknown option", invalid.Error);
            Assert.Equal("preserved", File.ReadAllText(project.PathFor("report.txt")));
        }
    }
}
