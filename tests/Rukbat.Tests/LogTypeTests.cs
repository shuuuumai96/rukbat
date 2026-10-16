namespace Rukbat.Tests;

[Trait("Category", "Unit")]
public sealed class LogTypeTests : TestBase
{
    [Fact]
    public void ChecksAllFixedOperationsAndOrdinaryConfigurationValues()
    {
        var diagnostics = new Compiler().Check("""
            import log
            struct Job { logger: log.Logger; levels: log.Level?[] }
            fn create(): log.Logger { return log.create() }
            fn choose(level: log.Level): log.Level { return level }
            fn optional(level: log.Level?): log.Level? { return level }
            fn configure(logger: log.Logger): log.Logger {
                logger.file = 'job.log'
                logger.stderr_level = null
                logger.file_level = log.level_debug
                return logger
            }
            const initial := create()
            logger := configure(initial)
            literal := log.Logger { stderr_level: log.level_info, file: null, file_level: log.level_debug }
            logger.stderr_level = log.level_warn
            logger.file = null
            logger.file_level = choose(log.level_error)
            log.debug(logger, 'debug')
            log.info(literal, 'info')
            log.warn(logger, 'warn')
            log.error(initial, 'error')
            print(log.enabled(logger, log.level_debug))
            print(log.level_debug == log.level_debug)
            print(log.level_info != log.level_warn)
            levels: log.Level[] = [log.level_debug, log.level_info, log.level_warn, log.level_error]
            choices: dict[string, log.Level?] = {'one': choose(levels[0]), 'two': null}
            nullable: log.Level? = optional(choices['one'])
            if nullable != null { print(nullable == log.level_debug) }
            values: log.Logger[] = [initial, literal]
            values[0].file = 'copy.log'
            lookup: dict[string, log.Logger] = {'job': values[0]}
            lookup['job'].stderr_level = null
            job := Job { logger: lookup['job'], levels: [log.level_error, null] }
            copy := job
            copy.logger.file_level = log.level_info
            optional_logger: log.Logger? = initial
            if optional_logger != null { log.info(optional_logger, 'optional') }
            for level in levels { print(log.enabled(initial, level)) }
            """);
        Assert.Empty(diagnostics);
    }

    [Theory]
    [InlineData("log.create(1)", "RUK2005")]
    [InlineData("log.create<int>()", "RUK2005")]
    [InlineData("log.info()", "RUK2005")]
    [InlineData("log.debug(log.create(), 1)", "RUK2001")]
    [InlineData("log.info('logger', 'message')", "RUK2001")]
    [InlineData("log.warn(log.create(), null)", "RUK2001")]
    [InlineData("log.error(logger: log.create(), message: 'message')", "RUK2005")]
    [InlineData("log.info<int>(log.create(), 'message')", "RUK2005")]
    [InlineData("log.enabled(log.create())", "RUK2005")]
    [InlineData("log.enabled(log.create(), 'info')", "RUK2001")]
    [InlineData("log.enabled(log.create(), 1)", "RUK2001")]
    [InlineData("log.enabled(log.create(), null)", "RUK2001")]
    [InlineData("value: bool = log.create()", "RUK2001")]
    [InlineData("value: log.Logger = log.enabled(log.create(), log.level_info)", "RUK2001")]
    [InlineData("value := log.info(log.create(), 'message')", "RUK2001")]
    [InlineData("log.trace(log.create(), 'message')", "RUK2004")]
    [InlineData("log.write(log.create(), log.level_info, 'message')", "RUK2004")]
    [InlineData("log.warning(log.create(), 'message')", "RUK2004")]
    [InlineData("level: log.Level = 'info'", "RUK2001")]
    [InlineData("level: log.Level = 1", "RUK2001")]
    [InlineData("level: string = log.level_info", "RUK2001")]
    [InlineData("level := log.Level {}", "RUK2004")]
    [InlineData("level := log.level_off", "RUK2004")]
    [InlineData("level := log.level_INFO", "RUK2004")]
    [InlineData("log.level_info = log.level_debug", "RUK2010")]
    [InlineData("value := log.level_info.name", "RUK2004")]
    [InlineData("value := log.level_info < log.level_error", "RUK2001")]
    [InlineData("value := log.level_info + log.level_debug", "RUK2001")]
    [InlineData("value := log.level_info == 'info'", "RUK2001")]
    [InlineData("first: log.Level? = log.level_info; second: log.Level? = null; value := first == second", "RUK2001")]
    [InlineData("log.Logger {}", "RUK2005")]
    [InlineData("log.Logger { stderr_level: null, file: null, file_level: null }", "RUK2001")]
    [InlineData("log.Logger { stderr_level: 'info', file: null, file_level: log.level_info }", "RUK2001")]
    [InlineData("log.Logger { stderr_level: null, file: true, file_level: log.level_info }", "RUK2001")]
    [InlineData("value := log.create(); value.file_level = null", "RUK2001")]
    [InlineData("struct Logger {}; value: log.Logger = Logger {}", "RUK2001")]
    [InlineData("struct Level {}; value: log.Level = Level {}", "RUK2001")]
    public void RejectsUnsupportedCallsValuesAndConversions(string body, string code)
        => Assert.Contains(new Compiler().Check("import log\n" + body), diagnostic => diagnostic.Code == code);

    [Theory]
    [InlineData("print(log.level_info)")]
    [InlineData("eprint(log.level_info)")]
    [InlineData("print([log.level_info])")]
    [InlineData("value: log.Level? = null; print(value)")]
    [InlineData("eprint(log.create())")]
    [InlineData("json.stringify(log.level_info)")]
    [InlineData("json.stringify([log.level_info])")]
    [InlineData("json.stringify({'level': log.level_info})")]
    [InlineData("json.stringify(log.create())")]
    [InlineData("json.parse<log.Level>('\"info\"')")]
    [InlineData("json.parse<log.Logger>('{}')")]
    [InlineData("json.as<log.Level>(json.parse('\"info\"'))")]
    [InlineData("json.set(json.parse('{}'), 'level', log.level_info)")]
    [InlineData("json.set_at(json.parse('[null]'), 0, log.create())")]
    [InlineData("struct Settings { values: dict[string, log.Logger?[]] }; json.stringify(Settings { values: {'one': [log.create(), null]} })")]
    [InlineData("struct Settings { values: dict[string, log.Level?[]] }; json.parse<Settings>('{}')")]
    [InlineData("powershell<void>('Write-Output', log.level_info)")]
    [InlineData("powershell<void>('Write-Output', [log.level_info])")]
    [InlineData("powershell<void>('Write-Output', log.create())")]
    [InlineData("powershell<log.Level>('Write-Output', 'info')")]
    [InlineData("powershell<log.Level?[]>('Write-Output', 'info')")]
    [InlineData("powershell<log.Logger>('Write-Output', 'info')")]
    [InlineData("struct Settings { values: log.Logger?[] }; powershell<Settings>('Write-Output')")]
    public void RejectsRecursiveExternalBoundaries(string body)
        => Assert.Contains(new Compiler().Check("import log; import json\n" + body), diagnostic => diagnostic.Code == "RUK2010");

    [Theory]
    [InlineData("const logger := log.create(); logger.file = 'changed.log'")]
    [InlineData("const logger := log.create(); logger.stderr_level = null")]
    [InlineData("const logger := log.create(); logger.file_level = log.level_debug")]
    [InlineData("const values := [log.create()]; values[0].file = 'changed.log'")]
    [InlineData("const values := {'one': log.create()}; values['one'].file_level = log.level_debug")]
    [InlineData("struct Holder { logger: log.Logger }; const holder := Holder { logger: log.create() }; holder.logger.file = 'changed.log'")]
    [InlineData("const levels := [log.level_info]; levels[0] = log.level_debug")]
    public void RejectsConfigurationWritesThroughConstNames(string body)
        => Assert.Contains(new Compiler().Check("import log\n" + body), diagnostic => diagnostic.Code == "RUK2011");

    [Fact]
    public void RetainsCanonicalTypesAcrossModuleImportsAndSameNamedStructs()
    {
        using var project = new Project();
        project.Write("helper.rukbat", """
            import log
            fn logger(): log.Logger { return log.create() }
            fn level(value: log.Level): log.Level { return value }
            fn levels(): log.Level?[] { return [log.level_debug, null] }
            """);
        project.Write("main.rukbat", """
            import log; import './helper.rukbat' as helper
            struct Logger { value: int }
            struct Level { value: int }
            logger := helper.logger()
            logger.file = 'job.log'
            logger.file_level = helper.level(log.level_error)
            level_values := helper.levels()
            selected: log.Level? = level_values[0]
            if selected != null { print(log.enabled(logger, selected)) }
            """);
        Assert.Empty(new Compiler().CheckFile(project.PathFor("main.rukbat")));
    }

    [Fact]
    public void ReservesOnlyCanonicalLogImportName()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "fn value(): int { return 1 }");
        project.Write("main.rukbat", "import './helper.rukbat' as log\n");
        Assert.Contains(new Compiler().CheckFile(project.PathFor("main.rukbat")), diagnostic => diagnostic.Code == "RUK3002");
        Assert.Contains(new Compiler().Check("import log as logging"), diagnostic => diagnostic.Code == "RUK3002");
        Assert.Empty(new Compiler().Check("log := 1; print(log)"));
    }

    [Fact]
    public void FormattingPreservesLevelTypesAndLoggerLiterals()
    {
        using var project = new Project();
        project.Write("main.rukbat", "import log;fn configured(level:log.Level):log.Logger{return log.Logger{stderr_level:null,file:'job.log',file_level:level}};logger:=configured(log.level_debug);log.info(logger,'message')");
        var path = project.PathFor("main.rukbat");
        Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, TextWriter.Null));
        var formatted = File.ReadAllText(path);
        Assert.Contains("fn configured(level: log.Level): log.Logger {", formatted);
        Assert.Contains("return log.Logger {\n", formatted);
        Assert.Contains("stderr_level: null, file: 'job.log', file_level: level", formatted);
        Assert.Empty(new Compiler().CheckFile(path));
        Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, TextWriter.Null));
        Assert.Equal(formatted, File.ReadAllText(path));
    }
}
