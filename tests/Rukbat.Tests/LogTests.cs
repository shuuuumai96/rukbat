using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Rukbat.Tests;

public class LogTests : TestBase
{
    private static readonly string[] Levels = ["debug", "info", "warn", "error"];
    private static readonly Regex Record = new(
        "^time=(?<time>[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}\\.[0-9]{7}Z) level=(?<level>DEBUG|INFO|WARN|ERROR) message=(?<message>\".*\")$",
        RegexOptions.CultureInvariant);

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", false)]
    [InlineData("pwsh", true)]
    public async Task DefaultsAndIndependentThresholdsWriteIdenticalRecords(string shell, bool debug)
    {
        using var project = new Project();
        var source = new StringBuilder("import log; import test\nlogger := log.create()\n");
        foreach (var level in Levels) source.AppendLine($"log.{level}(logger, 'default.{level}')");
        for (var threshold = 0; threshold < Levels.Length; threshold++)
        {
            source.AppendLine($$"""
                if true {
                    configured := log.create()
                    configured.stderr_level = log.level_{{Levels[threshold]}}
                    configured.file = {{Literal(project.PathFor($"threshold-{threshold}.log"))}}
                    configured.file_level = log.level_{{Levels[Levels.Length - 1 - threshold]}}
                """);
            for (var level = 0; level < Levels.Length; level++)
            {
                var enabled = level >= threshold || level >= Levels.Length - 1 - threshold;
                source.AppendLine($"test.assert(log.enabled(configured, log.level_{Levels[level]}) == {enabled.ToString().ToLowerInvariant()})");
                source.AppendLine($"log.{Levels[level]}(configured, 'threshold-{threshold}.{Levels[level]}')");
            }
            source.AppendLine("}");
        }
        source.AppendLine("""
            filtered_file := log.create()
            filtered_file.file = ''
            filtered_file.file_level = log.level_error
            log.info(filtered_file, 'filtered-file.info')
            print('completed')
            """);

        var result = await RunProgram(project, source.ToString(), shell, debug);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("completed\n", result.Output);
        var stderr = ParseRecords(result.Error);
        var expected = Levels.Skip(1).Select(level => $"default.{level}").ToList();
        for (var threshold = 0; threshold < Levels.Length; threshold++)
        {
            expected.AddRange(Levels.Skip(threshold).Select(level => $"threshold-{threshold}.{level}"));
            var file = ParseRecords(File.ReadAllText(project.PathFor($"threshold-{threshold}.log")));
            Assert.Equal(Levels.Skip(Levels.Length - 1 - threshold).Select(level => $"threshold-{threshold}.{level}"), file.Select(item => item.Message));
            foreach (var item in file)
            {
                var common = stderr.SingleOrDefault(other => other.Message == item.Message);
                if (common is not null) Assert.Equal(common.Raw, item.Raw);
            }
        }
        expected.Add("filtered-file.info");
        Assert.Equal(expected, stderr.Select(item => item.Message));
        Assert.All(stderr, item => Assert.Equal(item.Message.Split('.').Last().ToUpperInvariant(), item.Level));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", false)]
    [InlineData("pwsh", true)]
    public async Task WritesStrictBomFreeUtf8AndEscapesOnePhysicalRecord(string shell, bool debug)
    {
        using var project = new Project();
        const string message = "A\"\\\b\t\n\f\r\0\u001b\u001f\u007f\u0085\u009f\u2028\u2029東京😀";
        const string escaped = "\"A\\\"\\\\\\b\\t\\n\\f\\r\\u0000\\u001b\\u001f\\u007f\\u0085\\u009f\\u2028\\u2029東京😀\"";
        var file = project.PathFor("記録 [literal] 'name'.log");
        // The existing prefix is already a complete compatible record boundary.
        const string prefix = "existing\r\n";
        File.WriteAllBytes(file, Encoding.UTF8.GetBytes(prefix));
        var units = string.Join(',', message.Select(character => ((int)character).ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var source = $$"""
            import log
            logger := log.create()
            logger.file = {{Literal(file)}}
            value := powershell<string>('Invoke-Expression', {{Literal($"-join [char[]]({units})")}})
            log.info(logger, value)
            log.info(logger, '')
            """;

        var result = await RunProgram(project, source, shell, debug,
            "[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('tr-TR')\n");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Output);
        var bytes = File.ReadAllBytes(file);
        Assert.False(bytes.AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Equal(Encoding.UTF8.GetBytes(prefix + result.Error), bytes);
        var records = ParseRecords(result.Error);
        Assert.Equal(new[] { message, "" }, records.Select(item => item.Message));
        Assert.EndsWith("message=" + escaped, records[0].Raw);
        Assert.EndsWith("message=\"\"", records[1].Raw);
        Assert.DoesNotContain('\u001b', result.Error);
        Assert.DoesNotContain('\u0085', result.Error);
        Assert.DoesNotContain('\u2028', result.Error);
        Assert.DoesNotContain('\u2029', result.Error);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", false)]
    [InlineData("pwsh", true)]
    public async Task FilteredCallsEvaluateArgumentsButSkipValidationPathsAndNativeLoading(string shell, bool debug)
    {
        using var project = new Project();
        var source = """
            import log; import test
            logger := log.create()
            logger.stderr_level = null
            logger.file = ''
            logger.file_level = log.level_error
            calls := ''
            fn configuration(): log.Logger {
                calls = calls + 'l'
                return logger
            }
            fn message(): string {
                calls = calls + 'm'
                return powershell<string>('Invoke-Expression', '-join [char[]](0xD800)')
            }
            fn failure(): string {
                calls = calls + 'f'
                throw Error { message: 'argument failure' }
            }
            test.assert(log.enabled(logger, log.level_error))
            test.assert(!log.enabled(logger, log.level_warn))
            log.info(configuration(), message())
            test.assert(calls == 'lm')
            caught := false
            try { log.debug(configuration(), failure()) } catch err {
                caught = true
                test.assert(err.message == 'argument failure')
            }
            test.assert(caught)
            test.assert(calls == 'lmlf')
            logger.file = null
            test.assert(!log.enabled(logger, log.level_error))
            log.error(logger, message())
            loaded := powershell<bool>('Invoke-Expression', "$null -ne ('RukbatLog' -as [type])")
            test.assert(!loaded)
            print('passed')
            """;

        var result = await RunProgram(project, source, shell, debug,
            "function Add-Type { throw 'Filtered logging must not load native code.' }\n");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
        Assert.Empty(Directory.GetFiles(project.DirectoryPath, "*.log"));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", false)]
    [InlineData("pwsh", true)]
    public async Task CopiesLoggerBeforeMessageEvaluationAndUsesCapturedEntryDirectoryAcrossModules(string shell, bool debug)
    {
        using var project = new Project();
        Directory.CreateDirectory(project.PathFor("entry [cwd]"));
        Directory.CreateDirectory(project.PathFor("changed"));
        project.Write("lib/worker.rukbat", """
            import log
            fn emit(logger: log.Logger, message: string): void {
                log.info(logger, message)
                logger.file = 'local-copy.log'
            }
            """);
        var changed = project.PathFor("changed");
        var source = $$"""
            import log; import test
            import './lib/worker.rukbat' as worker
            logger := log.create()
            logger.stderr_level = null
            logger.file = 'snapshot.log'
            calls := ''
            fn configuration(): log.Logger {
                calls = calls + 'l'
                return logger
            }
            fn message(): string {
                calls = calls + 'm'
                logger.file = 'changed.log'
                return 'snapshot'
            }
            moved := powershell<bool>('Invoke-Expression', {{Literal($"Set-Location -LiteralPath {PowerShellLiteral(changed)}; [Environment]::CurrentDirectory = {PowerShellLiteral(changed)}; $true")}})
            test.assert(moved)
            log.info(configuration(), message())
            test.assert(calls == 'lm')
            worker.emit(logger, 'module')
            log.info(logger, 'caller')
            print('passed')
            """;

        var result = await RunProgram(project, source, shell, debug, working: project.PathFor("entry [cwd]"));

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
        Assert.Equal(new[] { "snapshot" }, ParseRecords(File.ReadAllText(project.PathFor("entry [cwd]/snapshot.log"))).Select(item => item.Message));
        Assert.Equal(new[] { "module", "caller" }, ParseRecords(File.ReadAllText(project.PathFor("entry [cwd]/changed.log"))).Select(item => item.Message));
        Assert.Empty(Directory.GetFiles(changed, "*.log"));
        Assert.False(File.Exists(project.PathFor("entry [cwd]/local-copy.log")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task SelectedFileFailuresStillDeliverStderrAndReleaseEveryFile(string shell)
    {
        using var project = new Project();
        File.WriteAllText(project.PathFor("locked.log"), "keep\r\n", new UTF8Encoding(false));
        var locked = PowerShellLiteral(project.PathFor("locked.log"));
        var source = $$"""
            import log; import test
            logger := log.create()
            logger.file = 'missing/child.log'
            try { log.info(logger, 'private-missing') } catch err { print(err.message) }
            logger.file = ''
            try { log.info(logger, 'private-empty') } catch err { print(err.message) }
            locked := powershell<bool>('Invoke-Expression', {{Literal($"$script:logTestHeld = [IO.File]::Open({locked}, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $true")}})
            test.assert(locked)
            logger.file = 'locked.log'
            try { log.info(logger, 'private-locked') } catch err { print(err.message) }
            released := powershell<bool>('Invoke-Expression', '$script:logTestHeld.Dispose(); $true')
            test.assert(released)
            log.info(logger, 'released')
            free := powershell<bool>('Invoke-Expression', {{Literal($"$handle = [IO.File]::Open({locked}, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None); $handle.Dispose(); $true")}})
            test.assert(free)
            """;

        var result = await RunProgram(project, source, shell, false);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(new[] { "private-missing", "private-empty", "private-locked", "released" }, ParseRecords(result.Error).Select(item => item.Message));
        var failures = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, failures.Length);
        Assert.All(failures, failure => Assert.Contains("file", failure, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("private-", result.Output);
        Assert.Contains(Path.GetFullPath(project.PathFor("missing/child.log")), failures[0]);
        Assert.Contains(project.PathFor("locked.log"), failures[2]);
        Assert.False(Directory.Exists(project.PathFor("missing")));
        var retained = File.ReadAllText(project.PathFor("locked.log"));
        Assert.StartsWith("keep\r\n", retained);
        Assert.Equal(new[] { "released" }, ParseRecords(retained["keep\r\n".Length..]).Select(item => item.Message));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task StderrWriteAndFlushFailuresStillAttemptTheFileAndReportBothFailures(string shell)
    {
        using var project = new Project();
        const string prefix = """
            Add-Type -TypeDefinition @'
            using System;
            using System.IO;
            using System.Text;
            public sealed class LogTestFailureWriter : TextWriter {
                public static bool FailWrite;
                public override Encoding Encoding { get { return new UTF8Encoding(false); } }
                public override void Write(string value) { if (FailWrite) throw new IOException("injected stderr write failure"); }
                public override void Flush() { throw new IOException("injected stderr flush failure"); }
            }
            '@
            $script:logTestOriginalError = [Console]::Error

            """;
        const string source = """
            import log; import test
            logger := log.create()
            logger.file = 'success.log'
            installed := powershell<bool>('Invoke-Expression', '[Console]::SetError((New-Object LogTestFailureWriter)); $true')
            test.assert(installed)
            flush_error := ''
            try { log.info(logger, 'flush') } catch err { flush_error = err.message }
            switched := powershell<bool>('Invoke-Expression', '[LogTestFailureWriter]::FailWrite = $true; $true')
            test.assert(switched)
            write_error := ''
            try { log.info(logger, 'write') } catch err { write_error = err.message }
            logger.file = 'missing/child.log'
            both_error := ''
            try { log.info(logger, 'private-both') } catch err { both_error = err.message }
            restored := powershell<bool>('Invoke-Expression', '[Console]::SetError($script:logTestOriginalError); $true')
            test.assert(restored)
            print(flush_error)
            print(write_error)
            print(both_error)
            """;

        var result = await RunProgram(project, source, shell, false, prefix);

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        var failures = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, failures.Length);
        Assert.Contains("stderr", failures[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("injected stderr flush failure", failures[0]);
        Assert.Contains("stderr", failures[1], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("injected stderr write failure", failures[1]);
        Assert.Contains("stderr", failures[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("file", failures[2], StringComparison.OrdinalIgnoreCase);
        Assert.Contains(Path.GetFullPath(project.PathFor("missing/child.log")), failures[2]);
        Assert.DoesNotContain("private-both", result.Output);
        Assert.Equal(new[] { "flush", "write" }, ParseRecords(File.ReadAllText(project.PathFor("success.log"))).Select(item => item.Message));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", false)]
    [InlineData("pwsh", true)]
    public async Task InvalidUnicodePreparationWritesNeitherDestination(string shell, bool debug)
    {
        using var project = new Project();
        var source = new StringBuilder("import log; import test\nlogger := log.create()\nlogger.file = 'invalid.log'\n");
        var invalid = new[] { "0xD800", "0xDC00", "0xD800,0x78", "0x78,0xD800", "0xD800,0xD800", "0xDC00,0xD800", "0xD83D,0xDE00,0xDC00" };
        foreach (var units in invalid)
        {
            source.AppendLine($$"""
                if true {
                    invalid := powershell<string>('Invoke-Expression', '-join [char[]]({{units}})')
                    caught := false
                    try { log.info(logger, invalid) } catch err {
                        caught = true
                        print(err.message)
                    }
                    test.assert(caught)
                }
                """);
        }
        source.AppendLine("print('passed')");

        var result = await RunProgram(project, source.ToString(), shell, debug);

        Assert.True(result.ExitCode == 0, result.Error);
        var messages = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(invalid.Length + 1, messages.Length);
        Assert.Equal("passed", messages[^1]);
        Assert.All(messages[..^1], message => Assert.Contains("unpaired UTF-16 surrogate", message));
        Assert.Empty(result.Error);
        Assert.False(File.Exists(project.PathFor("invalid.log")));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task BuildsChecksFormatsAndRunsSourceFreeWithCallSiteErrors(string shell)
    {
        using var project = new Project();
        project.Write("source/main.rukbat", "import log\nlogger:=log.create()\nlogger.file='job.log'\nlog.info(logger,'standalone')\n");
        var input = project.PathFor("source/main.rukbat");
        var format = await RunCli(project, ["fmt", input]);
        Assert.True(format.ExitCode == 0, format.Error);
        var formatted = File.ReadAllText(input);
        var secondFormat = await RunCli(project, ["fmt", input]);
        Assert.True(secondFormat.ExitCode == 0, secondFormat.Error);
        Assert.Equal(formatted, File.ReadAllText(input));
        var check = await RunCli(project, ["check", input]);
        Assert.True(check.ExitCode == 0, check.Error);
        Assert.False(File.Exists(project.PathFor("source/main.ps1")));
        var build = await RunCli(project, ["build", input]);
        Assert.True(build.ExitCode == 0, build.Error);
        var script = project.PathFor("source/main.ps1");
        Assert.True(File.ReadAllBytes(script).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        var cliRun = await RunCli(project, ["run", input]);
        Assert.True(cliRun.ExitCode == 0, cliRun.Error);
        Assert.Empty(cliRun.Output);
        Assert.Equal("standalone", Assert.Single(ParseRecords(cliRun.Error)).Message);
        File.Delete(project.PathFor("job.log"));
        File.Delete(input);

        var deployed = await RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);

        Assert.True(deployed.ExitCode == 0, deployed.Error);
        Assert.Empty(deployed.Output);
        Assert.Equal(deployed.Error, File.ReadAllText(project.PathFor("job.log")));
        project.Write("source/main.rukbat", "import log\nlogger := log.create()\nlogger.file = 'missing/child.log'\nlog.info(logger, 'private-message')\n");
        var failed = new Compiler().CompileFile(input);
        Assert.True(failed.Success, string.Join("\n", failed.Diagnostics));
        File.WriteAllText(script, failed.Output, new UTF8Encoding(true));
        File.Delete(input);
        var error = await RunDeployed(shell, script, project.DirectoryPath, project.DirectoryPath);
        Assert.Equal(1, error.ExitCode);
        Assert.Empty(error.Output);
        Assert.Contains("main.rukbat:4: error:", error.Error);
        Assert.Contains("file", error.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("private-message", Assert.Single(ParseRecords(error.Error[..error.Error.IndexOf("main.rukbat:4: error:", StringComparison.Ordinal)])).Message);
    }

    private static async Task<RunResult> RunProgram(Project project, string source, string shell, bool debug,
        string prefix = "", string? working = null)
    {
        project.Write("main.rukbat", source);
        var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), prefix + compiled.Output, new UTF8Encoding(true));
        return await RunDeployed(shell, project.PathFor("job.ps1"), working ?? project.DirectoryPath, project.DirectoryPath);
    }

    private static List<LogRecord> ParseRecords(string value)
    {
        if (value.Length == 0) return [];
        Assert.EndsWith("\r\n", value);
        var lines = value[..^2].Split("\r\n", StringSplitOptions.None);
        return lines.Select(line =>
        {
            var match = Record.Match(line);
            Assert.True(match.Success, "Invalid record: " + line);
            Assert.True(DateTime.TryParseExact(match.Groups["time"].Value, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
                System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal, out _));
            return new LogRecord(line, match.Groups["level"].Value, JsonSerializer.Deserialize<string>(match.Groups["message"].Value)!);
        }).ToList();
    }

    private static string Literal(string value) => "'" + value.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\r", "\\r").Replace("\n", "\\n") + "'";
    private static string PowerShellLiteral(string value) => "'" + value.Replace("'", "''") + "'";
    private sealed record LogRecord(string Raw, string Level, string Message);
}
