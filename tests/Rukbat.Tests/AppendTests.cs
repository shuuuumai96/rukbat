using System.Text.Json;

namespace Rukbat.Tests;

public class AppendTests : TestBase
{
    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("names: string[] = []; names.append('Taro'); names.append('Hanako'); print(names.length); print(names[0]); print(names[1])", "2\nTaro\nHanako")]
    [InlineData("matrix: int[][] = [[]]; matrix[0].append(1); matrix[0].append(2); print(matrix[0].length); print(matrix[0][1])", "2\n2")]
    [InlineData("struct User { names: string[] }; users: User[] = [User {names: []}]; users[0].names.append('Taro'); print(users[0].names[0])", "Taro")]
    [InlineData("struct User { tags: string[] }; struct Group { users: User[] }; groups: Group[] = [Group {users: [User {tags: []}]}]; groups[0].users[0].tags.append('admin'); print(groups[0].users[0].tags[0])", "admin")]
    [InlineData("row: int[] = [1]; matrix: int[][] = []; matrix.append(row); row[0] = 2; print(matrix[0][0]); copy: int[][] = matrix; copy[0].append(3); print(matrix[0].length); print(copy[0].length)", "1\n1\n2")]
    [InlineData("fn added(values: int[]): int[] { values.append(2); return values }; original: int[] = [1]; result: int[] = added(original); print(original.length); print(result.length)", "1\n2")]
    [InlineData("items: string[] = ['a', 'b']; count: int = 0; for item in items { count = count + 1; items.append('x') }; print(count); print(items.length)", "2\n4")]
    [InlineData("values: string?[] = [null]; values.append('x'); values.append(null); item: string? = values[1]; print(values.length); print(values[0] == null); if item != null { print(item) }; print(values[2] == null)", "3\nTrue\nx\nTrue")]
    [InlineData("values: int[]? = []; if values != null { values.append(1); print(values[0]) }", "1")]
    [InlineData("struct Box { values: int[] }; impl Box { fn add(): void { self.values.append(1) } }; box: Box = Box {values: []}; box.add(); print(box.values[0])", "1")]
    public async Task AppendsToMutableValueArrays(string source, string expected)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, result.Output.TrimEnd('\n'));
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task EvaluatesNestedAppendIndexesOnceFromLeftToRight()
    {
        using var project = new Project();
        project.Write("main.rukbat", """
            struct User { tags: string[] }
            struct Group { users: User[] }
            calls: int = 0
            fn next(expected: int): int {
                print(expected)
                calls = calls + 1
                return 0
            }
            groups: Group[] = [Group {users: [User {tags: []}]}]
            groups[next(1)].users[next(2)].tags.append('admin')
            print(calls)
            print(groups[0].users[0].tags[0])
            """);

        var result = await project.Run();
        Assert.Equal(0, result.ExitCode);
        Assert.Equal("1\n2\n2\nadmin", result.Output.TrimEnd('\n'));
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("values: int[] = []; values.append('bad')", "Expected int")]
    [InlineData("values: int[] = []; values.append()", "one positional argument")]
    [InlineData("values: int[] = []; values.append(1, 2)", "one positional argument")]
    [InlineData("values: int[] = []; values.append(value: 1)", "one positional argument")]
    [InlineData("fn values(): int[] { return [] }; values().append(1)", "temporary")]
    [InlineData("values: int[] = []; result := values.append(1)", "void")]
    [InlineData("value: int = 1; value.append(2)", "Unknown method")]
    public void RejectsInvalidArrayAppend(string source, string message)
    {
        var result = Compile(source);
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Message.Contains(message, StringComparison.OrdinalIgnoreCase));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task ArrayAppendWorksAcrossModulesAndPowerShellTargets()
    {
        using var project = new Project();
        project.Write("values.rukbat", "fn add(values: string[]): string[] { values.append('module'); return values }");
        project.Write("main.rukbat", "import './values.rukbat' as values; original: string[] = []; changed: string[] = values.add(original); print(original.length); print(changed[0])");
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

        var modern = await project.RunOutput(compiled.Output!);
        Assert.Equal(0, modern.ExitCode);
        Assert.Equal("0\nmodule", modern.Output.TrimEnd('\n'));

        var windows = await project.RunOutputWithWindowsPowerShell(compiled.Output!);
        Assert.Equal(0, windows.ExitCode);
        Assert.Equal("0\nmodule", windows.Output.TrimEnd('\r', '\n'));
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task AppendsManyPrimitiveValuesWithCapacityBackedStorage()
    {
        const int itemCount = 50_000;
        using var project = new Project();
        project.Write("main.rukbat", $$"""
            values: int[] = []
            index := 0
            while index < {{itemCount}} {
                values.append(index)
                index = index + 1
            }
            print(values.length)
            print(values[0])
            print(values[{{itemCount - 1}}])
            """);

        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        Assert.Contains("[System.Collections.Generic.List[object]]", compiled.Output);
        Assert.DoesNotContain("[object[]]::new($values.Length + 1)", compiled.Output);

        var modern = await project.RunOutput(compiled.Output!);
        Assert.Equal(0, modern.ExitCode);
        Assert.Equal($"{itemCount}\n0\n{itemCount - 1}", modern.Output.TrimEnd('\n'));

        var windows = await project.RunOutputWithWindowsPowerShell(compiled.Output!);
        Assert.Equal(0, windows.ExitCode);
        Assert.Equal($"{itemCount}\n0\n{itemCount - 1}", windows.Output.TrimEnd('\r', '\n'));
    }

    [Trait("Category", "Runtime")]
    [Fact]
    public async Task CollectsFilesRetriesAProcessAndWritesJsonWithoutPowerShellEscape()
    {
        using var project = new Project();
        File.WriteAllText(project.PathFor("keep.log"), "log");
        File.WriteAllText(project.PathFor("skip.txt"), "text");
        var directory = project.DirectoryPath.Replace("\\", "/", StringComparison.Ordinal);
        project.Write("main.rukbat", $$"""
            import encoding; import fs
            import path
            import json
            import process

            matches: string[] = []
            for file in fs.list_files('{{directory}}', '*') {
                if path.extension(file) == '.log' {
                    matches.append(file)
                }
            }

            attempt := 0
            while attempt < 3 {
                result: process.Result = process.run('dotnet', ['--version'], process.options(null, encoding.utf8))
                if result.exit_code == 0 {
                    break
                }
                attempt = attempt + 1
            }

            fs.write_text(path.join(['{{directory}}', 'result.json']), json.stringify(matches), encoding.utf8)
            """);

        var result = await project.Run();
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(JsonSerializer.Serialize(new[] { project.PathFor("keep.log") }), File.ReadAllText(project.PathFor("result.json")));
    }
}
