using System.Text;
using System.Text.Json;

namespace Rukbat.Tests;

public class PracticalDataTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("table := csv.parse_table('name\\na'); print(csv.get(table.rows[0], 'name'))")]
    [InlineData("print(csv.has_column(csv.table(['x'], []), 'x'))")]
    [InlineData("table := csv.table(['name'], []); table.rows.append(csv.row(table.columns, ['a'])); table.rows[0] = csv.set(table.rows[0], 'name', 'b')")]
    [InlineData("value := json.set(json.parse('{}'), 'x', null)")]
    [InlineData("row: csv.Row? = csv.row(['x'], ['y']); if row != null { print(csv.get(row, 'x')) }")]
    [InlineData("struct Box { row: csv.Row }; fn read(box: Box): string { return csv.get(box.row, 'x') }; print(read(Box { row: csv.row(['x'], ['y']) }))")]
    [InlineData("value := json.append(json.parse('[]'), [1,2]); value = json.set_at(value, 0, true); value = json.remove_at(value, 0)")]
    public void PracticalApisCompileAndFormat(string body)
    {
        var source = "import csv\nimport json\n" + body;
        Success(source);
        using var project = new Project();
        project.Write("main.rukbat", source);
        Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
        var formatted = File.ReadAllText(project.PathFor("main.rukbat"));
        Assert.Empty(new Compiler().Check(formatted));
        Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
        Assert.Equal(formatted, File.ReadAllText(project.PathFor("main.rukbat")));
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("csv.get(['a'], 'name')", "RUK2001")]
    [InlineData("csv.has_column(csv.row(['x'], ['y']), 'x')", "RUK2001")]
    [InlineData("csv.set(csv.row(['x'], ['y']), 'x', 1)", "RUK2001")]
    [InlineData("csv.parse_table<string>('x')", "RUK2005")]
    [InlineData("csv.stringify_table(csv.row(['x'], ['y']))", "RUK2001")]
    [InlineData("json.set('{}', 'x', 1)", "RUK2001")]
    [InlineData("json.set(json.parse('{}'), 1, 1)", "RUK2001")]
    [InlineData("json.set<int>(json.parse('{}'), 'x', 1)", "RUK2005")]
    [InlineData("json.set(json.parse('{}'), 'x')", "RUK2005")]
    [InlineData("json.set_at(json.parse('[]'), 'x', 1)", "RUK2001")]
    [InlineData("json.append(json.parse('[]'), value: 1)", "RUK2005")]
    [InlineData("json.stringify(csv.row(['x'], ['y']))", "RUK2010")]
    [InlineData("json.parse<csv.Table>('{}')", "RUK2010")]
    [InlineData("struct Box { row: csv.Row }; json.parse<Box>('{}')", "RUK2010")]
    [InlineData("json.set(json.parse('{}'), 'row', csv.row(['x'], ['y']))", "RUK2010")]
    [InlineData("value := powershell<csv.Row>('Write-Output', 1)", "RUK2010")]
    [InlineData("value := powershell<csv.Table>('Write-Output', 1)", "RUK2010")]
    [InlineData("row := csv.row(['x'], ['y']); print(row)", "RUK2010")]
    [InlineData("row := csv.row(['x'], ['y']); print(row == row)", "RUK2001")]
    [InlineData("row := csv.Row {}", "RUK2004")]
    [InlineData("row := csv.row(['x'], ['y']); row['x']", "RUK2001")]
    public void InvalidPracticalApisAreRejected(string body, string code)
    {
        var result = Compile("import csv\nimport json\n" + body);
        Assert.Null(result.Output);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task CsvNamedRowsSupportFileConversionFilteringAndCopies(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", """
            import csv
            import fs
            import json
            fs.write_text('input.csv', 'name,enabled,note\r\n日本語,true,"a,b"\r\nHanako,false,""\r\n', 'windows-932')
            table := csv.parse_table(fs.read_text('input.csv', 'windows-932'))
            original := table
            kept: csv.Row[] = []
            for row in table.rows {
                if csv.get(row, 'enabled') == 'true' { kept.append(csv.set(row, 'note', 'a"b\nnext')) }
            }
            table.rows = kept
            table.rows.append(csv.row(table.columns, ['追加', 'true', '']))
            fs.write_text('output.csv', csv.stringify_table(table), 'utf-8')
            print(json.stringify(csv.parse(fs.read_text('output.csv', 'utf-8'))))
            print(csv.get(original.rows[0], 'note'))
            print(original.rows.length)
            print(csv.stringify_table(csv.table(['name'], [])) == 'name\r\n')
            print(csv.parse_table('name\n').rows.length)
            special := csv.parse_table('A,a,weird.key/[]\nUP,down,value\n')
            print(csv.get(special.rows[0], 'A'))
            print(csv.get(special.rows[0], 'a'))
            print(csv.get(special.rows[0], 'weird.key/[]'))
            single := csv.parse_table('x\n""\n')
            print(csv.get(single.rows[0], 'x') == '')
            print(csv.has_column(csv.table(['x'], []), 'x'))
            print(csv.has_column(csv.table(['x'], []), 'X'))
            """);
        var result = await DeployAndRun(project, shell);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        var lines = result.Output.TrimEnd('\n').Split('\n');
        Assert.Equal(new[] { new[] { "name", "enabled", "note" }, new[] { "日本語", "true", "a\"b\nnext" }, new[] { "追加", "true", "" } }, JsonSerializer.Deserialize<string[][]>(lines[0])!);
        Assert.Equal(new[] { "a,b", "2", "True", "0", "UP", "down", "value", "True", "True", "False" }, lines[1..]);
        Assert.Equal(Encoding.UTF8.GetBytes("name,enabled,note\r\n日本語,true,\"a\"\"b\nnext\"\r\n追加,true,\"\"\r\n"), File.ReadAllBytes(project.PathFor("output.csv")));
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task JsonPartialUpdatesPreserveUnknownDataAndOriginalValues(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", """
            import json
            import fs
            struct Worker { name: string; enabled: bool }
            fs.write_text('config.json', '{"service":{"enabled":false,"unknown":{"huge":1e400,"large":9007199254740993}},"workers":[{"name":"old","other":true}],"obsolete":1,"A":1,"a":2}', 'utf-8')
            doc := json.parse(fs.read_text('config.json', 'utf-8'))
            original := doc
            service := json.get(doc, 'service')
            service = json.set(service, 'enabled', true)
            doc = json.set(doc, 'service', service)
            workers := json.get(doc, 'workers')
            first := json.items(workers)[0]
            first = json.set(first, 'name', '日本語')
            workers = json.set_at(workers, 0, first)
            workers = json.append(workers, Worker { name: 'added', enabled: true })
            workers = json.append(workers, null)
            workers = json.remove_at(workers, 2)
            doc = json.set(doc, 'workers', workers)
            doc = json.remove(doc, 'obsolete')
            doc = json.remove(doc, 'missing')
            doc = json.set(doc, 'optional', null)
            doc = json.set(doc, 'A', 3)
            optional: int? = null
            doc = json.set(doc, 'nullable', optional)
            doc = json.set(doc, 'array', [1, 2])
            fs.write_text('config.json', json.stringify(doc), 'utf-8')
            print(fs.read_text('config.json', 'utf-8'))
            print(json.as<bool>(json.get(json.get(original, 'service'), 'enabled')))
            print(json.as<string>(json.get(json.items(json.get(original, 'workers'))[0], 'name')))
            print(json.has(original, 'obsolete'))
            print(json.stringify(json.remove_at(json.parse('[1]'), 0)))
            print(json.stringify(json.append(json.parse('[]'), 'x')))
            """);
        var result = await DeployAndRun(project, shell);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        var lines = result.Output.TrimEnd('\n').Split('\n');
        using var json = JsonDocument.Parse(lines[0]);
        var root = json.RootElement;
        Assert.True(root.GetProperty("service").GetProperty("enabled").GetBoolean());
        Assert.Equal("1e400", root.GetProperty("service").GetProperty("unknown").GetProperty("huge").GetRawText());
        Assert.Equal("9007199254740993", root.GetProperty("service").GetProperty("unknown").GetProperty("large").GetRawText());
        Assert.Equal("日本語", root.GetProperty("workers")[0].GetProperty("name").GetString());
        Assert.True(root.GetProperty("workers")[0].GetProperty("other").GetBoolean());
        Assert.Equal("added", root.GetProperty("workers")[1].GetProperty("name").GetString());
        Assert.Equal(2, root.GetProperty("workers").GetArrayLength());
        Assert.False(root.TryGetProperty("obsolete", out _));
        Assert.Equal(JsonValueKind.Null, root.GetProperty("optional").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("nullable").ValueKind);
        Assert.Equal(3, root.GetProperty("A").GetInt32());
        Assert.Equal(2, root.GetProperty("a").GetInt32());
        Assert.Equal("[1,2]", root.GetProperty("array").GetRawText());
        Assert.Equal(new[] { "False", "old", "True", "[]", "[\"x\"]" }, lines[1..]);
        Assert.Equal(Encoding.UTF8.GetBytes(lines[0]), File.ReadAllBytes(project.PathFor("config.json")));
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task InvalidHeadersWidthsColumnsAndJsonUpdatesRaiseErrors(string shell)
    {
        using var project = new Project();
        project.Write("main.rukbat", """
            import csv
            import json
            try { csv.parse_table('') } catch err { print(err.message) }
            try { csv.parse_table('x,x\na,b') } catch err { print(err.message) }
            try { csv.parse_table(',x\na,b') } catch err { print(err.message) }
            try { csv.parse_table('x,y\na') } catch err { print(err.message) }
            try { csv.parse_table('x\na,b') } catch err { print(err.message) }
            try { csv.table([], []) } catch err { print(err.message) }
            try { csv.row(['x'], []) } catch err { print(err.message) }
            try { csv.get(csv.row(['x'], ['a']), 'X') } catch err { print(err.message) }
            try { csv.set(csv.row(['x'], ['a']), 'missing', 'b') } catch err { print(err.message) }
            table := csv.table(['x'], [['a']])
            table.columns[0] = 'y'
            try { csv.stringify_table(table) } catch err { print(err.message) }
            table = csv.table(['x'], [])
            table.rows.append(csv.row(['y'], ['a']))
            try { csv.stringify_table(table) } catch err { print(err.message) }
            try { json.set(json.parse('[]'), 'x', 1) } catch err { print(err.message) }
            try { json.remove(json.parse('null'), 'x') } catch err { print(err.message) }
            try { json.append(json.parse('{}'), 1) } catch err { print(err.message) }
            try { json.set_at(json.parse('[]'), 0, 1) } catch err { print(err.message) }
            try { json.set_at(json.parse('[1]'), -1, 1) } catch err { print(err.message) }
            try { json.remove_at(json.parse('[1]'), 1) } catch err { print(err.message) }
            print('done')
            """);
        var result = await DeployAndRun(project, shell);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        var lines = result.Output.TrimEnd('\n').Split('\n');
        Assert.Equal(18, lines.Length);
        Assert.Contains("header", lines[0]);
        Assert.Contains("Duplicate", lines[1]);
        Assert.Contains("empty", lines[2]);
        Assert.Contains("record 2", lines[3]);
        Assert.Contains("expected 2", lines[3]);
        Assert.Contains("record 2", lines[4]);
        Assert.Contains("header", lines[5]);
        Assert.Contains("expected 1", lines[6]);
        Assert.Contains("X", lines[7]);
        Assert.Contains("missing", lines[8]);
        Assert.Contains("columns", lines[9]);
        Assert.Contains("columns", lines[10]);
        Assert.Contains("object", lines[11]);
        Assert.Contains("object", lines[12]);
        Assert.Contains("array", lines[13]);
        Assert.Contains("index", lines[14]);
        Assert.Contains("index", lines[15]);
        Assert.Contains("index", lines[16]);
        Assert.Equal("done", lines[17]);
    }

    [Trait("Category", "Runtime")]
    [Theory]
    [InlineData("csv-filter")]
    [InlineData("json-config")]
    public async Task RunnableExamplesValidateBeforeReplacingOutput(string example)
    {
        using var project = new Project();
        var entry = "examples/" + example + "/main.rukbat";
        project.Write(entry, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", entry)));
        var csv = example == "csv-filter";
        var input = csv ? "people.csv" : "config.json";
        var output = csv ? "enabled.csv" : "updated.json";
        project.Write(input, csv ? "note,enabled,name,extra\n\"a,b\",true,Taro,unknown\n,false,Hanako,other\n" :
            "{\"service\":{\"enabled\":false,\"other\":1e400},\"obsolete\":true,\"large\":9007199254740993}");
        var arguments = new List<string> { "run", project.PathFor(entry), "--", input, output };
        if (csv) arguments.Add("utf-8");
        var valid = await RunCli(project, arguments.ToArray());
        Assert.True(valid.ExitCode == 0, valid.Error);
        Assert.Equal("", valid.Output);
        Assert.Equal("", valid.Error);
        var contents = File.ReadAllText(project.PathFor(output));
        if (csv) Assert.Equal("note,enabled,name,extra\r\nchecked,true,Taro,unknown\r\n", contents);
        else Assert.Equal("{\"large\":9007199254740993,\"service\":{\"enabled\":true,\"other\":1e400}}", contents);
        project.Write(input, csv ? "name,enabled,note\nTaro,invalid,old" : "{\"service\":[]} ");
        var invalid = await RunCli(project, arguments.ToArray());
        Assert.Equal(2, invalid.ExitCode);
        Assert.NotEmpty(invalid.Error);
        Assert.Equal("", invalid.Output);
        Assert.Equal(contents, File.ReadAllText(project.PathFor(output)));
        if (csv)
        {
            project.Write(input, "name\n");
            var missingColumns = await RunCli(project, arguments.ToArray());
            Assert.Equal(2, missingColumns.ExitCode);
            Assert.Contains("enabled and note", missingColumns.Error);
            Assert.Equal(contents, File.ReadAllText(project.PathFor(output)));
        }
        using var diagnostics = new StringWriter();
        Assert.Equal(0, Cli.Run(["build", project.PathFor(entry)], TextWriter.Null, diagnostics));
        Assert.True(File.Exists(Path.ChangeExtension(project.PathFor(entry), ".ps1")));
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void StandardRowsAndTablesKeepTheirIdentityAcrossModules()
    {
        using var project = new Project();
        project.Write("worker.rukbat", """
            import csv
            fn update(row: csv.Row): csv.Row { return csv.set(row, 'name', 'updated') }
            fn create(): csv.Table { return csv.table(['name'], [['original']]) }
            """);
        project.Write("main.rukbat", """
            import csv
            import './worker.rukbat' as worker
            table := worker.create()
            table.rows[0] = worker.update(table.rows[0])
            print(csv.stringify_table(table))
            """);
        var result = project.Compile();
        Assert.True(result.Success, string.Join("\n", result.Diagnostics));
    }

    private static async Task<RunResult> DeployAndRun(Project project, string shell)
    {
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
        File.Delete(project.PathFor("main.rukbat"));
        return await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task TenThousandCsvRowsCanBeReadUpdatedAndWritten(string shell)
    {
        using var project = new Project();
        var input = new StringBuilder("id,name,note\r\n");
        for (var index = 0; index < 10000; index++)
            input.Append(index).Append(",person").Append(index).Append(",\"value,").Append(index).Append("\"\r\n");
        project.Write("input.csv", input.ToString());
        project.Write("main.rukbat", """
            import csv
            import fs
            table := csv.parse_table(fs.read_text('input.csv', 'utf-8'))
            print(table.rows.length)
            print(csv.get(table.rows[9999], 'note'))
            table.rows[9999] = csv.set(table.rows[9999], 'note', 'updated')
            fs.write_text('output.csv', csv.stringify_table(table), 'utf-8')
            """);
        var result = await DeployAndRun(project, shell);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        Assert.Equal("10000\nvalue,9999\n", result.Output);
        Assert.Equal(input.ToString().Replace("\"value,9999\"", "updated"), File.ReadAllText(project.PathFor("output.csv")));
    }
}
