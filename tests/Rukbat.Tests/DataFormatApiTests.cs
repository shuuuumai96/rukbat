using System.Text;
using System.Text.Json;

namespace Rukbat.Tests
{
    public class DataFormatApiTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("import csv\nvalue := csv.stringify([])")]
        [InlineData("import json\nvalue := json.stringify([1, 2])")]
        [InlineData("import json\nvalue := json.parse<int[]>('[1,2]')")]
        public void CanonicalDataConversionsCompileAndFormat(string source)
        {
            Success(source);
            using var project = new Project();
            project.Write("main.rukbat", source);
            using var errors = new StringWriter();
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, errors));
            var formatted = File.ReadAllText(project.PathFor("main.rukbat"));
            Assert.Empty(new Compiler().Check(formatted));
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, errors));
            Assert.Equal(formatted, File.ReadAllText(project.PathFor("main.rukbat")));
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("import csv\ncsv.stringify()", "RUK2005")]
        [InlineData("import csv\ncsv.stringify('a,b')", "RUK2001")]
        [InlineData("import csv\ncsv.stringify([[1, 2]])", "RUK2001")]
        [InlineData("import csv\ncsv.stringify<string>([['x']])", "RUK2005")]
        [InlineData("import csv\ncsv.stringify(rows: [['x']])", "RUK2005")]
        [InlineData("import json\njson.parse('1', '2')", "RUK2005")]
        [InlineData("import json\njson.parse<int>(text: '1')", "RUK2005")]
        [InlineData("import json\njson.stringify(value: 1)", "RUK2005")]
        [InlineData("import json\njson.encode(1)", "RUK2004")]
        [InlineData("import json\njson.decode<int>('1')", "RUK2004")]
        public void InvalidDataConversionsAreRejected(string source, string code)
        {
            var result = Compile(source);
            Assert.Null(result.Output);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task CsvAndJsonRoundTripStandaloneWithFileEncodings(string shell)
        {
            using var project = new Project();
            project.Write("main.rukbat", """
            import csv
            import json
            import encoding; import fs

            struct Config { name: string; values: int[]; note: string? }
            rows: string[][] = [
                ['name', 'value'],
                ['日本語', 'a,b'],
                ['quote', 'a"b'],
                ['lines', 'one\r\ntwo\nthree\rfour'],
                [''],
                [' space ', ''],
                ['# // literal', '=literal']
            ]
            original := json.stringify(rows)
            encoded := csv.stringify(rows)
            print(json.stringify(encoded))
            print(json.stringify(csv.parse(encoded)))
            print(json.stringify(rows) == original)
            fs.write_text('records.csv', encoded, encoding.windows_932)
            print(json.stringify(csv.parse(fs.read_text('records.csv', encoding.windows_932))))
            print(json.stringify(csv.stringify([])))
            print(csv.parse(csv.stringify([])).length)

            try { csv.stringify([[]]) } catch err { print(err.message) }
            try { csv.stringify([['valid'], []]) } catch err { print(err.message) }

            config := Config { name: '日本語', values: [1, 2], note: null }
            fs.write_text('config.json', json.stringify(config), encoding.utf8)
            loaded := json.parse<Config>(fs.read_text('config.json', encoding.utf8))
            print(json.stringify(loaded))
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
            File.Delete(project.PathFor("main.rukbat"));
            var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("", result.Error);
            var lines = result.Output.TrimEnd('\n').Split('\n');
            const string expectedCsv = "name,value\r\n日本語,\"a,b\"\r\nquote,\"a\"\"b\"\r\nlines,\"one\r\ntwo\nthree\rfour\"\r\n\"\"\r\n space ,\"\"\r\n# // literal,=literal\r\n";
            Assert.Equal(expectedCsv, JsonSerializer.Deserialize<string>(lines[0]));
            var expected = new[]
            {
                new[] { "name", "value" }, new[] { "日本語", "a,b" }, new[] { "quote", "a\"b" },
                new[] { "lines", "one\r\ntwo\nthree\rfour" }, new[] { "" },
                new[] { " space ", "" }, new[] { "# // literal", "=literal" }
            };
            Assert.Equal(expected, JsonSerializer.Deserialize<string[][]>(lines[1])!);
            Assert.Equal("True", lines[2]);
            Assert.Equal(expected, JsonSerializer.Deserialize<string[][]>(lines[3])!);
            Assert.Equal("\"\"", lines[4]);
            Assert.Equal("0", lines[5]);
            Assert.Equal("csv.stringify record 1 must contain at least one field.", lines[6]);
            Assert.Equal("csv.stringify record 2 must contain at least one field.", lines[7]);
            Assert.Equal("{\"name\":\"日本語\",\"values\":[1,2],\"note\":null}", lines[8]);
            var jsonBytes = File.ReadAllBytes(project.PathFor("config.json"));
            Assert.Equal(Encoding.UTF8.GetBytes(lines[8]), jsonBytes);
        }
    }
}
