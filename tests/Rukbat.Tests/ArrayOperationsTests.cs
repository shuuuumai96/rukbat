namespace Rukbat.Tests;

public class ArrayOperationsTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("array.sort([true, false])")]
    [InlineData("values: int?[] = [1, null]; array.sort(values)")]
    [InlineData("array.sort([[1]])")]
    [InlineData("array.slice([1], 0.0, 1)")]
    [InlineData("array.remove_at([1], index: 0)")]
    [InlineData("array.sort<int>([1])")]
    [InlineData("array.sort_by([1], [true])")]
    [InlineData("array.sort_by([1], 1)")]
    [InlineData("array.slice([] , 0, 0)")]
    [InlineData("array.sort(1)")]
    [InlineData("array.unknown([1])")]
    public void RejectsInvalidCallsAndUnorderableKeys(string source) => Assert.False(Compile("import array\n" + source).Success);

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task PreservesTypesCopiesStableOrderingAndExactIntegers(string shell)
    {
        await RunVerified(shell, """
            import array; import test; import json; import datetime; import csv
            values := [9223372036854775807, 9007199254740993, -9223372036854775808, 9007199254740992]
            sorted := array.sort(values)
            test.assert(sorted[0] == -9223372036854775808)
            test.assert(sorted[1] == 9007199254740992)
            test.assert(sorted[2] == 9007199254740993)
            test.assert(sorted[3] == 9223372036854775807)
            test.assert(values[0] == 9223372036854775807)
            test.assert(array.sort_descending(values)[0] == 9223372036854775807)
            test.assert(json.stringify(array.sort(['a', 'B', 'b', 'A'])) == '["A","B","a","b"]')
            test.assert(json.stringify(array.sort([3.0, -1.5, 0.0])) == '[-1.5,0,3]')
            test.assert(json.stringify(array.slice([1, 2, 3], 1, 2)) == '[2,3]')
            test.assert(array.slice([1], 1, 0).length == 0)
            test.assert(json.stringify(array.remove_at([1, 2, 3], 1)) == '[1,3]')
            test.assert(array.remove_at([1], 0).length == 0)
            empty: int[] = []
            test.assert(array.sort(empty).length == 0)
            test.assert(array.slice(empty, 0, 0).length == 0)
            nullable: int?[] = [1, null, 3]
            test.assert(json.stringify(array.slice(nullable, 1, 2)) == '[null,3]')
            test.assert(json.stringify(array.slice(nullable, 1, 1)) == '[null]')
            test.assert(json.stringify(array.sort_by(nullable, [3, 1, 2])) == '[null,3,1]')
            test.assert(array.sort_by(empty, empty).length == 0)
            nested := [[1], [2]]
            nested_copy := array.slice(nested, 0, 1)
            nested_copy[0][0] = 9
            test.assert(nested[0][0] == 1)
            struct Row { name: string; values: int[] }
            rows := [Row { name: 'first', values: [1] }, Row { name: 'second', values: [2] }, Row { name: 'third', values: [3] }]
            ordered := array.sort_by(rows, [2, 1, 2])
            test.assert(ordered[0].name == 'second')
            test.assert(ordered[1].name == 'first')
            test.assert(ordered[2].name == 'third')
            ordered[1].values[0] = 99
            test.assert(rows[0].values[0] == 1)
            descending := array.sort_by_descending(rows, [2, 1, 2])
            test.assert(descending[0].name == 'first')
            test.assert(descending[1].name == 'third')
            test.assert(descending[2].name == 'second')
            test.assert(array.sort_by(['a', 'b'], ['z', 'A'])[0] == 'b')
            test.assert(array.sort_by(['a', 'b'], [2.0, 1.0])[0] == 'b')
            keys := [2, 1]
            array.sort_by(['a', 'b'], keys)
            test.assert(keys[0] == 2)
            dates := [datetime.parse_date('2024-01-02'), datetime.parse_date('2024-01-01')]
            test.assert(array.sort(dates)[0] == dates[1])
            instants := [datetime.from_unix_seconds(1), datetime.from_unix_seconds(0)]
            test.assert(array.sort(instants)[0] == instants[1])
            test.assert(array.sort([datetime.hours(1), datetime.seconds(-1)])[0] == datetime.seconds(-1))
            test.assert(array.sort_by(['a', 'b'], dates)[0] == 'b')
            table := csv.parse_table('name,amount\nA,2\nB,1\nC,2\n')
            table.rows = array.sort_by_descending(table.rows, [2, 1, 2])
            test.assert(csv.get(table.rows[0], 'name') == 'A')
            test.assert(csv.get(table.rows[1], 'name') == 'C')
            test.assert(csv.get(table.rows[2], 'name') == 'B')
            calls := 0
            fn items(): int[] { calls = calls + 1; return [calls] }
            test.assert(array.sort_by(items(), items())[0] == 1)
            test.assert(calls == 2)
            print('passed')
            """);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ProcessExitCodesRemainIntegerSortKeysAcrossValueBoundaries(string shell)
    {
        await RunVerified(shell, """
            import array
            import encoding
            import json
            import process
            import test

            fn exit_code(result: process.Result): int {
                return result.exit_code
            }

            fn copy_values(values: int[]): int[] {
                return values
            }

            const timeout_ms := 10000
            const expected_exit_code := 3
            options := process.options(timeout_ms, encoding.utf8)
            result := process.run("powershell.exe", ["-NoProfile", "-Command", "exit 3"], options)
            test.assert(result.exit_code == expected_exit_code)

            values := [result.exit_code, 1, 2]
            test.assert(json.stringify(array.sort(values)) == "[1,2,3]")
            test.assert(json.stringify(array.sort_descending(values)) == "[3,2,1]")
            labels := ["exit", "one", "two"]
            test.assert(json.stringify(array.sort_by(labels, values)) == '["one","two","exit"]')
            test.assert(json.stringify(array.sort_by_descending(labels, values)) == '["exit","two","one"]')
            test.assert(array.sort([exit_code(result), 1])[0] == 1)
            test.assert(json.stringify(array.sort(copy_values(values))) == "[1,2,3]")

            copied := values
            values[0] = 0
            test.assert(array.sort(copied)[2] == expected_exit_code)
            test.assert(values[0] == 0)
            test.assert(result.exit_code == expected_exit_code)
            print("passed")
            """);
    }

    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task RejectsOutOfRangeAndMismatchedKeysBeforeChangingInputs(string shell)
    {
        string[] expressions = [ "array.slice(values, -1, 0)", "array.slice(values, 2, 1)", "array.slice(values, 0, -1)",
            "array.slice(values, 1, 9223372036854775807)", "array.slice(values, 9223372036854775807, 0)",
            "array.remove_at(values, -1)", "array.remove_at(values, 2)", "array.remove_at(values, 9223372036854775807)",
            "array.remove_at(empty, 0)", "array.sort_by(values, empty)", "array.sort_by_descending(values, [1])" ];
        var source = "import array; import test\nvalues := [2, 1]\nempty: int[] = []\n";
        for (int i = 0; i < expressions.Length; i++)
            source += $"rejected{i} := false\ntry {{ {expressions[i]} }} catch err {{ rejected{i} = true }}\ntest.assert(rejected{i})\ntest.assert(values[0] == 2)\n";
        await RunVerified(shell, source + "print('passed')\n");
    }

    private static async Task RunVerified(string shell, string source)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var output = "[Threading.Thread]::CurrentThread.CurrentCulture = [Globalization.CultureInfo]::GetCultureInfo('tr-TR')\n" + compiled.Output!;
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(output) : await project.RunOutput(output);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Empty(result.Error);
        Assert.Equal("passed\n", result.Output);
    }
}
