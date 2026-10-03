namespace Rukbat.Tests;

public class DateTimeTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("datetime.utc = datetime.local_zone()")]
    [InlineData("print(datetime.now())")]
    [InlineData("json.stringify([datetime.now()])")]
    [InlineData("powershell('Write-Output', datetime.now())")]
    [InlineData("powershell<datetime.Date>('Get-Date')")]
    [InlineData("datetime.now() + datetime.seconds(1)")]
    [InlineData("datetime.now() < datetime.create_date(2024, 1, 1)")]
    [InlineData("datetime.utc < datetime.local_zone()")]
    [InlineData("datetime.seconds(1.0)")]
    [InlineData("datetime.now().ticks")]
    [InlineData("datetime.unknown()")]
    [InlineData("struct Data { date: datetime.Date? }; json.parse<Data>('{}')")]
    [InlineData("json.parse<datetime.Timestamp>('\"2024-01-01T00:00:00Z\"')")]
    [InlineData("value: datetime.Timestamp? = null; print(value)")]
    public void RejectsInvalidTypesAndImplicitBoundaries(string expression) =>
        Assert.False(Compile("import datetime; import json\n" + expression).Success);

    [Trait("Category", "Unit")]
    [Fact]
    public void SupportsQualifiedOpaqueTypesAndComparisons() => Success("""
        import datetime; import test
        struct Schedule { deadline: datetime.Timestamp; day: datetime.Date?; zones: datetime.Zone[] }
        fn identity(value: datetime.Duration): datetime.Duration { return value }
        t: datetime.Timestamp = datetime.now()
        s := Schedule { deadline: t, day: null, zones: [datetime.utc] }
        test.assert(s.deadline == t)
        test.assert(identity(datetime.days(1)) >= datetime.hours(24))
        """);

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task PreservesInstantsCalendarAndUnixConversions(string shell)
    {
        await RunVerified(shell, """
            import datetime; import json; import test; import text
            t := datetime.parse('2024-02-29T12:34:56.1234567+09:00')
            test.assert(datetime.stringify(t) == '2024-02-29T03:34:56.1234567Z')
            test.assert(datetime.parse(datetime.stringify(t)) == t)
            test.assert(datetime.parse(json.parse<string>(json.stringify(datetime.stringify(t)))) == t)
            test.assert(datetime.format(t, 'yyyy/MM/dd HH:mm:ss.fffffff zzz', datetime.utc) == '2024/02/29 03:34:56.1234567 +00:00')
            test.assert(datetime.format(t, "yyyy-MM-dd'T'HH:mm:ss", datetime.utc) == '2024-02-29T03:34:56')
            d := datetime.create_date(2024, 1, 31)
            test.assert(datetime.stringify_date(datetime.add_months(d, 1)) == '2024-02-29')
            test.assert(datetime.stringify_date(datetime.add_days(datetime.add_months(d, 1), 1)) == '2024-03-01')
            test.assert(datetime.add_days(d, -30) == datetime.parse_date('2024-01-01'))
            test.assert(datetime.add_months(d, -1) == datetime.parse_date('2023-12-31'))
            test.assert(datetime.format_date(d, 'yyyyMMdd') == '20240131')
            test.assert(datetime.date(t, datetime.utc) == datetime.parse_date('2024-02-29'))
            test.assert(datetime.at(datetime.parse_date('2024-02-29'), 3, 34, 56, datetime.utc) < t)
            test.assert(datetime.days(1) == datetime.hours(24))
            test.assert(datetime.minutes(1) == datetime.seconds(60))
            test.assert(datetime.total_seconds(datetime.milliseconds(-1500)) == -1.5)
            epoch := datetime.from_unix_seconds(0)
            test.assert(datetime.stringify(epoch) == '1970-01-01T00:00:00.0000000Z')
            test.assert(datetime.to_unix_seconds(datetime.parse('1969-12-31T23:59:59.9999999Z')) == -1)
            test.assert(datetime.to_unix_milliseconds(datetime.parse('1969-12-31T23:59:59.9999999Z')) == -1)
            test.assert(datetime.to_unix_seconds(datetime.from_unix_seconds(4102444800)) == 4102444800)
            test.assert(datetime.to_unix_milliseconds(datetime.from_unix_milliseconds(4102444800123)) == 4102444800123)
            test.assert(datetime.between(epoch, datetime.add(epoch, datetime.days(-1))) == datetime.days(-1))
            test.assert(datetime.utc == datetime.zone('UTC'))
            test.assert(datetime.zone_id(datetime.utc) == 'UTC')
            test.assert(datetime.zone_id(datetime.local_zone()) != '')
            current := datetime.now()
            test.assert(datetime.parse(datetime.stringify(current)) == current)
            optional: datetime.Timestamp? = t
            if optional != null { test.assert(optional == t) }
            struct Item { when: datetime.Timestamp; dates: datetime.Date[] }
            item := Item { when: t, dates: [d] }
            test.assert(item.when == t)
            test.assert(item.dates[0] == d)
            copy := item
            copy.when = datetime.add(copy.when, datetime.seconds(1))
            copy.dates[0] = datetime.add_days(d, 1)
            test.assert(item.when == t)
            test.assert(item.dates[0] == d)
            minimum := datetime.parse('0001-01-01T00:00:00Z')
            maximum := datetime.parse('9999-12-31T23:59:59.9999999Z')
            test.assert(datetime.to_unix_seconds(minimum) == -62135596800)
            test.assert(datetime.to_unix_seconds(maximum) == 253402300799)
            test.assert(datetime.from_unix_seconds(-62135596800) == minimum)
            calls := 0
            fn next(): datetime.Timestamp { calls = calls + 1; return datetime.from_unix_seconds(calls) }
            test.assert(next() < next())
            test.assert(calls == 2)
            failed := false
            try { test.assert(minimum > maximum) } catch err {
                failed = true
                test.assert(text.contains(err.message, 'left = 0001-01-01T00:00:00.0000000Z'))
                test.assert(text.contains(err.message, 'right = 9999-12-31T23:59:59.9999999Z'))
            }
            test.assert(failed)
            print('passed')
            """);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ResolvesExplicitOffsetsAndDistinguishesCalendarDays(string shell)
    {
        await RunVerified(shell, """
            import datetime; import test
            eastern := datetime.zone('Eastern Standard Time')
            first := datetime.parse('2024-11-03T01:30:00-04:00')
            second := datetime.parse('2024-11-03T01:30:00-05:00')
            test.assert(first < second)
            test.assert(datetime.between(first, second) == datetime.hours(1))
            test.assert(datetime.format(first, 'HH:mm zzz', eastern) == '01:30 -04:00')
            test.assert(datetime.format(second, 'HH:mm zzz', eastern) == '01:30 -05:00')
            start := datetime.at(datetime.parse_date('2024-03-09'), 12, 0, 0, eastern)
            next := datetime.at(datetime.parse_date('2024-03-10'), 12, 0, 0, eastern)
            test.assert(datetime.between(start, next) == datetime.hours(23))
            test.assert(datetime.format(datetime.add(start, datetime.days(1)), 'HH:mm', eastern) == '13:00')
            test.assert(datetime.parse_local('2024-03-10 12:00', 'yyyy-MM-dd HH:mm', eastern) == next)
            test.assert(datetime.parse_local('2024-03-10T12:00:00.1234567', "yyyy-MM-dd'T'HH:mm:ss.fffffff", eastern) == datetime.parse('2024-03-10T12:00:00.1234567-04:00'))
            print('passed')
            """);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task RejectsInvalidDatesPatternsDstAndOverflow(string shell)
    {
        string[] expressions = [
            "datetime.parse('2024-01-01T00:00:00')", "datetime.parse('2024-01-01T00:00:00.12345678Z')",
            "datetime.parse('2024-01-01T00:00:60Z')", "datetime.parse('2024-01-01T24:00:00Z')",
            "datetime.parse('2024-01-01T00:00:00-00:00')", "datetime.parse('2024-01-01T00:00:00+14:01')",
            "datetime.parse_date('2023-02-29')", "datetime.parse_date('2024-1-01')", "datetime.parse_date(' 2024-01-01')",
            "datetime.create_date(0, 1, 1)", "datetime.create_date(2024, 13, 1)", "datetime.create_date(9223372036854775807, 1, 1)",
            "datetime.zone('Unknown Zone')", "datetime.zone('utc')",
            "datetime.at(datetime.parse_date('2024-03-10'), 2, 30, 0, eastern)",
            "datetime.parse_local('2024-11-03 01:30', 'yyyy-MM-dd HH:mm', eastern)",
            "datetime.parse_local('12:00', 'HH:mm', datetime.utc)",
            "datetime.parse_local('2024-2024-01-01 12:00', 'yyyy-yyyy-MM-dd HH:mm', datetime.utc)",
            "datetime.parse_local('2024-01-01 12:00 +00:00', 'yyyy-MM-dd HH:mm zzz', datetime.utc)",
            "datetime.format(t, 'O', datetime.utc)", "datetime.format(t, 'MMMM', datetime.utc)",
            "datetime.format(t, 'ffffffff', datetime.utc)", "datetime.format(t, \"yyyy 'unterminated\", datetime.utc)",
            "datetime.format_date(d, 'yyyy-MM-dd HH:mm')",
            "datetime.add_days(d, -1)", "datetime.add_days(d, 9223372036854775807)", "datetime.add_months(d, -1)",
            "datetime.days(9223372036854775807)", "datetime.from_unix_seconds(9223372036854775807)",
            "datetime.from_unix_milliseconds(-9223372036854775808)", "datetime.add(t, datetime.seconds(1))",
            "datetime.at(d, 24, 0, 0, datetime.utc)", "datetime.format(t, 'yyyy', datetime.zone('Tokyo Standard Time'))"
        ];
        var source = "import datetime; import test\neastern := datetime.zone('Eastern Standard Time')\nt := datetime.parse('9999-12-31T23:59:59.9999999Z')\nd := datetime.parse_date('0001-01-01')\n";
        for (var i = 0; i < expressions.Length; i++)
            source += $"rejected{i} := false\ntry {{ {expressions[i]} }} catch err {{ rejected{i} = true }}\ntest.assert(rejected{i})\n";
        await RunVerified(shell, source + "print('passed')\n");
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task PreservesTypesAcrossImportedFunctionsAndSubMillisecondArithmetic(string shell)
    {
        using var project = new Project();
        project.Write("clock.rukbat", """
            import datetime
            initial := datetime.parse('2000-01-01T00:00:00Z')
            fn echo(value: datetime.Timestamp): datetime.Timestamp { return value }
            fn delta(): datetime.Duration { return datetime.between(initial, datetime.parse('2000-01-01T00:00:00.0000001Z')) }
            """);
        project.Write("main.rukbat", """
            import datetime; import test; import './clock.rukbat' as clock
            t := datetime.parse('2000-01-01T00:00:00Z')
            test.assert(clock.echo(t) == t)
            test.assert(datetime.stringify(datetime.add(t, clock.delta())) == '2000-01-01T00:00:00.0000001Z')
            test.assert(clock.delta() > datetime.milliseconds(0))
            test.assert(clock.delta() < datetime.milliseconds(1))
            test.assert(datetime.total_seconds(clock.delta()) == 0.0000001)
            print('passed')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }

    private static async Task RunVerified(string shell, string source)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var output = "[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::GetCultureInfo('fr-FR')\n" + compiled.Output!;
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(output) : await project.RunOutput(output);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }
}
