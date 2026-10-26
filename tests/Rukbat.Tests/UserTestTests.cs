using Rukbat;

namespace Rukbat.Tests;

public class UserTestTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell", true)]
    [InlineData("powershell", false)]
    [InlineData("pwsh", true)]
    [InlineData("pwsh", false)]
    public async Task CatchAndReturnSucceedsOnlyWhenAnErrorIsThrown(string shell, bool throwsError)
    {
        using var project = new Project();
        var input = throwsError ? "invalid" : "42";
        project.Write("test_error.rukbat", $$"""
            import test
            import text
            fn test_expected_error(): void {
                try {
                    text.parse_int('{{input}}')
                } catch err {
                    return
                }
                test.fail('Expected Error was not thrown')
            }
            """);
        var result = await RunCli(project, ["test", "--shell", shell]);
        Assert.Equal(throwsError ? 0 : 1, result.ExitCode);
        if (throwsError)
        {
            Assert.Contains("1 passed, 0 failed", result.Output);
            Assert.Empty(result.Error);
        }
        else
        {
            Assert.Contains("0 passed, 1 failed", result.Output);
            Assert.Contains("Expected Error was not thrown", result.Error);
        }
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BundledTestingExampleRuns(string shell)
    {
        using var project = new Project();
        var example = Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples", "testing");
        var result = await RunCli(project, ["test", example, "--shell", shell]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("5 passed, 0 failed", result.Output);
        Assert.Empty(result.Error);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task CleanupDoesNotTraverseDirectoryJunctions(string shell)
    {
        using var project = new Project();
        const string directory = "日本語 O'Brien";
        project.Write(directory + "/make-link.ps1", "param([string]$link, [string]$target)\nNew-Item -ItemType Junction -Path $link -Target $target | Out-Null");
        project.Write(directory + "/preserve.txt", "preserve external data");
        project.Write(directory + "/test_cleanup.rukbat", """
            import test
            import fs
            import path
            fn test_link(ctx: test.Context): void {
                link := path.join([ctx.temp_dir, 'link'])
                script := path.join([ctx.source_dir, 'make-link.ps1'])
                powershell<void>(script, link, ctx.source_dir)
                test.assert(fs.is_dir(link))
            }
            """);
        var result = await RunCli(project, ["test", "--shell", shell]);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Contains("1 passed, 0 failed", result.Output);
        Assert.Equal("preserve external data", File.ReadAllText(project.PathFor(directory + "/preserve.txt")));
        Assert.True(File.Exists(project.PathFor(directory + "/test_cleanup.rukbat")));
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task AssertionSemanticsAndExpectedErrors(string shell)
    {
        using var project = new Project();
        project.Write("test_conditions.rukbat", """
            import test
            import text
            count := 0
            fn next(): int { count = count + 1; return count }
            fn test_conditions(): void {
                test.assert(args().length == 0)
                test.assert(next() == 1)
                test.assert(next() < 3)
                test.assert(next() <= 3)
                test.assert(next() > 3)
                test.assert(next() >= 5)
                test.assert(next() != 0)
                test.assert(count == 6)
                test.assert(true || next() == 99)
                test.assert(!(false && next() == 99))
                test.assert(count == 6)
                test.assert(1.0 == 1.0)
                test.assert('a' != 'A')
                optional: string? = null
                test.assert(optional == null)
                items: int[]? = []
                test.assert(items != null)
                test.assert(
                    text.contains('a(b)', '(') // A comment with a closing )
                )
                rejected := false
                try { text.parse_int('invalid') } catch err {
                    rejected = true
                    test.assert(text.contains(err.message, 'int'))
                }
                test.assert(rejected)
                try { test.fail('explicit failure') } catch err {
                    test.assert(err.message == 'explicit failure')
                }
            }
            """);
        var result = await RunCli(project, ["test", "--shell", shell]);
        Assert.Equal(0, result.ExitCode);
        Assert.Contains("1 passed, 0 failed", result.Output);
        Assert.Empty(result.Error);
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task TimeoutKillsDescendantsAndContinues(string shell)
    {
        using var project = new Project();
        project.Write("test_timeout.rukbat", """
            import test
            import encoding; import fs
            import path
            fn test_a_timeout(ctx: test.Context): void {
                script := path.join([ctx.temp_dir, 'child.ps1'])
                pid_file := path.join([ctx.source_dir, 'child.pid'])
                marker := path.join([ctx.source_dir, 'late.txt'])
                fs.write_text(script,
                    "[IO.File]::WriteAllText('" + pid_file + "', [string]$PID); Start-Sleep -Seconds 8; [IO.File]::WriteAllText('" + marker + "', 'orphan')",
                    encoding.utf8)
                powershell<void>('Start-Process', FilePath: 'powershell', ArgumentList: ['-NoProfile', '-File', script], WindowStyle: 'Hidden')
                powershell<void>('Start-Sleep', Seconds: 20)
            }
            fn test_b_success(): void { test.assert(true) }
            """);
        var result = await RunCli(project, ["test", "--timeout", "4", "--shell", shell]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("timed out after 4 seconds", result.Error);
        Assert.Contains("PASS test_timeout.rukbat::test_b_success", result.Output);
        Assert.True(File.Exists(project.PathFor("child.pid")), result.Error);
        var pid = int.Parse(File.ReadAllText(project.PathFor("child.pid")));
        try
        {
            using var child = System.Diagnostics.Process.GetProcessById(pid);
            Assert.True(child.HasExited, "Timed-out test left a live child process.");
        }
        catch (ArgumentException)
        {
            // Windows has already released the terminated child's process record.
        }
        Assert.False(File.Exists(project.PathFor("late.txt")));
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task MissingHostFailsBeforeExecutingAnyTest()
    {
        using var project = new Project();
        project.Write("test_host.rukbat", "fn test_host(): void { exit(99) }");
        var result = await RunCli(project, ["test"], hidePowerShell: true);
        Assert.Equal(2, result.ExitCode);
        Assert.Contains("Cannot find 'powershell.exe'", result.Error);
        Assert.DoesNotContain("FAIL", result.Output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ImportedTestsAndStructMethodsAreNotCollected()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "fn test_imported(): void { exit(99) }");
        project.Write("test_empty.rukbat", "import './helper.rukbat' as helper; struct Example {}; impl Example { fn test_method(): void {} }");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Assert.Equal(5, Cli.Run(["test", project.DirectoryPath], stdout, stderr));
        Assert.Contains("No tests", stderr.ToString());
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("test.assert(1)")]
    [InlineData("test.assert()")]
    [InlineData("test.assert(true, 'message')")]
    [InlineData("test.assert(condition: true)")]
    [InlineData("test.assert<int>(true)")]
    [InlineData("test.fail(false)")]
    [InlineData("value := test.assert(true)")]
    public void InvalidAssertionCallsAreRejected(string call)
    {
        Assert.NotEmpty(new Compiler().Check("import test; " + call));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void AssertionsUseOrdinarySyntaxAndFormatting()
    {
        const string source = "import test; fn test_example(ctx: test.Context): void { test.assert(ctx.temp_dir != ''); test.fail('reason') }";
        Success(source);
        using var project = new Project();
        project.Write("test_example.rukbat", source);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Assert.Equal(0, Cli.Run(["fmt", project.PathFor("test_example.rukbat")], stdout, stderr));
        var formatted = File.ReadAllText(project.PathFor("test_example.rukbat"));
        Assert.Equal(0, Cli.Run(["fmt", project.PathFor("test_example.rukbat")], stdout, stderr));
        Assert.Equal(formatted, File.ReadAllText(project.PathFor("test_example.rukbat")));
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task DiscoversIsolatesAndReportsTests(string shell)
    {
        using var project = new Project();
        project.Write("math.rukbat", "fn add(a: int, b: int): int { return a + b }");
        project.Write("tests/test_sample.rukbat", """
            import test
            import encoding; import fs
            import path
            import env
            import "../math.rukbat" as arithmetic
            count := 0
            fn next(): int { count = count + 1; return count }
            fn helper(): void { test.fail('helper must not run') }
            fn test_a_failure(): void {
                print('日本語の出力')
                eprint('diagnostic output')
                test.assert(arithmetic.add(next(), 1) == 9)
            }
            fn test_b_isolated(ctx: test.Context): void {
                test.assert(next() == 1)
                test.assert(!fs.is_file('result.txt'))
                fs.write_text('result.txt', '内容', encoding.utf8)
                test.assert(fs.read_text(path.join([ctx.temp_dir, 'result.txt']), encoding.utf8) == '内容')
                test.assert(fs.is_file(path.join([ctx.source_dir, 'test_sample.rukbat'])))
                env.set('RUKBAT_USER_TEST_LOCAL', 'value')
            }
            fn test_c_isolated(): void {
                test.assert(count == 0)
                test.assert(!fs.is_file('result.txt'))
                test.assert(env.get('RUKBAT_USER_TEST_LOCAL') == null)
            }
            """);
        project.Write("bin/test_ignored.rukbat", "invalid");
        project.Write("obj/test_ignored.rukbat", "invalid");
        project.Write(".git/test_ignored.rukbat", "invalid");
        project.Write("tests/test_sample.ps1", "preserve stale output");
        var result = await RunCli(project, ["test", ".", "--shell", shell]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("FAIL tests/test_sample.rukbat::test_a_failure", result.Output);
        Assert.Contains("PASS tests/test_sample.rukbat::test_b_isolated", result.Output);
        Assert.Contains("PASS tests/test_sample.rukbat::test_c_isolated", result.Output);
        Assert.Contains("2 passed, 1 failed", result.Output);
        Assert.Contains("Assertion failed", result.Error);
        Assert.Contains("left = 2", result.Error);
        Assert.Contains("right = 9", result.Error);
        Assert.Contains("test_sample.rukbat:12", result.Error);
        Assert.Contains("日本語の出力", result.Error);
        Assert.Contains("diagnostic output", result.Error);
        Assert.Equal("preserve stale output", File.ReadAllText(project.PathFor("tests/test_sample.ps1")));
        Assert.False(File.Exists(project.PathFor("result.txt")));
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("fn test_wrong(): int { return 1 }")]
    [InlineData("fn test_wrong(value: int): void {}")]
    [InlineData("import test; fn test_wrong(a: test.Context, b: test.Context): void {}")]
    [InlineData("import test; fn test_wrong(a: test.Context?): void {}")]
    [InlineData("struct Context { temp_dir: string }; fn test_wrong(ctx: Context): void {}")]
    public void CollectionRejectsInvalidSignatures(string source)
    {
        using var project = new Project();
        project.Write("test_invalid.rukbat", source);
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var result = Cli.Run(["test", project.DirectoryPath], stdout, stderr);
        Assert.Equal(1, result);
        Assert.Contains("RUK5000", stderr.ToString());
        Assert.DoesNotContain("PASS", stdout.ToString());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void ChecksEveryFileBeforeAnyUserCodeExecutes()
    {
        using var project = new Project();
        project.Write("test_a.rukbat", "exit(99); fn test_a(): void {}");
        project.Write("test_b.rukbat", "fn test_b(): void { missing() }");
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Assert.Equal(1, Cli.Run(["test", project.DirectoryPath], stdout, stderr));
        Assert.Contains("missing", stderr.ToString());
        Assert.DoesNotContain("FAIL", stdout.ToString());
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void EmptyCollectionAndInvalidOptionsAreErrors()
    {
        using var project = new Project();
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        Assert.Equal(5, Cli.Run(["test", project.DirectoryPath], stdout, stderr));
        Assert.Contains("No tests", stderr.ToString());
        Assert.Equal(2, Cli.Run(["test", project.DirectoryPath, "--timeout", "0"], stdout, stderr));
        Assert.Equal(2, Cli.Run(["test", project.DirectoryPath, "--shell", "cmd"], stdout, stderr));
        Assert.Equal(2, Cli.Run(["test", project.DirectoryPath, "--unknown"], stdout, stderr));
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ExitZeroIsNotACompletedTestAndFilteringSelectsOne(string shell)
    {
        using var project = new Project();
        project.Write("test_exit.rukbat", """
            import test
            fn test_a_exit(): void { exit(0) }
            fn test_b_success(): void { test.assert(true) }
            """);
        var result = await RunCli(project, ["test", "--shell", shell]);
        Assert.Equal(1, result.ExitCode);
        Assert.Contains("without completing the test", result.Error);
        Assert.Contains("1 passed, 1 failed", result.Output);
        var filtered = await RunCli(project, ["test", "test_exit.rukbat", "--shell", shell, "--filter", "test_b"]);
        Assert.Equal(0, filtered.ExitCode);
        Assert.Contains("1 passed, 0 failed", filtered.Output);
        Assert.DoesNotContain("test_a_exit", filtered.Output);
    }
}
