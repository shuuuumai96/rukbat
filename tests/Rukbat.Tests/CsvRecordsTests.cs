using System.Text;

namespace Rukbat.Tests
{
    public class CsvRecordsTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("csv.records('report.csv')", "RUK2005")]
        [InlineData("csv.records('report.csv', 'utf-8')", "RUK2001")]
        [InlineData("csv.__open_records(csv.records('report.csv', encoding.utf8))", "RUK2004")]
        [InlineData("source: csv.Records? = null; for record in source {}", "RUK2001")]
        [InlineData("source := csv.records('report.csv', encoding.utf8); print(source == source)", "RUK2001")]
        [InlineData("print([csv.records('report.csv', encoding.utf8)])", "RUK2010")]
        [InlineData("json.stringify(csv.records('report.csv', encoding.utf8))", "RUK2010")]
        [InlineData("powershell<void>('Write-Output', csv.records('report.csv', encoding.utf8))", "RUK2010")]
        public void EnforcesDescriptorAndCallTypes(string source, string code)
        {
            var result = Compile("import csv; import encoding; import json\n" + source);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task ReadsCompleteIndependentRecordsWithStrictEncoding(string shell)
        {
            using var project = new Project();
            var csv = PathLiteral(project, "records.csv");
            var legacy = PathLiteral(project, "legacy.csv");
            var empty = PathLiteral(project, "empty.csv");
            // The first CR is the last character in the first full decoding buffer.
            var contents = "header," + new string('x', 65532) + "\r\n1,\"first\r\nsecond\"\"quote\"\n\n2,last,";
            File.WriteAllText(csv, contents, new UTF8Encoding(false));
            File.WriteAllBytes(legacy, [0x93, 0xfa, 0x2c, 0x78, 0x0a]); // 日,x
            File.WriteAllBytes(empty, [0xef, 0xbb, 0xbf]);
            project.Write("main.rukbat", $$"""
            import csv
            import encoding
            import json
            import test
            import text

            const source := csv.records('{{csv}}', encoding.utf8)
            records: string[][] = []
            for record in source {
                for first in source {
                    test.assert(first[0] == 'header')
                    break
                }
                records.append(record)
                record[0] = 'changed'
            }
            test.assert(records.length == 4)
            test.assert(records[0][0] == 'header')
            test.assert(text.length(records[0][1]) == 65532)
            test.assert(records[1][1] == 'first\r\nsecond"quote')
            test.assert(records[2].length == 1 && records[2][0] == '')
            test.assert(records[3].length == 3 && records[3][2] == '')
            for record in csv.records('{{legacy}}', encoding.windows_932) {
                test.assert(record[0] == json.parse<string>('"\\u65e5"'))
            }
            for record in csv.records('{{empty}}', encoding.utf8) {
                test.fail('Expected zero records')
            }
            print('passed')
            """);
            await AssertPasses(project, shell);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task StopsBeforeUnreadSyntaxAndReleasesOnReturnAndError(string shell)
        {
            using var project = new Project();
            var csv = PathLiteral(project, "records.csv");
            var invalid = PathLiteral(project, "invalid.csv");
            var missing = PathLiteral(project, "missing.csv");
            File.WriteAllText(csv, "skip\nkeep,\"\"\n\"unclosed");
            File.WriteAllBytes(invalid, [0xff]);
            project.Write("main.rukbat", $$"""
            import csv
            import encoding
            import test
            import text

            fn first_value(source: csv.Records): string[] {
                for record in source {
                    if record[0] == 'skip' {
                        continue
                    }
                    return record
                }
                return []
            }

            const source := csv.records('{{csv}}', encoding.utf8)
            const first := first_value(source)
            test.assert(first.length == 2 && first[0] == 'keep' && first[1] == '')
            seen := 0
            try {
                for record in source {
                    seen = seen + 1
                }
                test.fail('Expected invalid CSV')
            } catch err {
                test.assert(text.contains(err.message, 'CSV at offset 22: Unclosed quoted field.'))
                test.assert(seen == 2)
            }
            powershell<void>('Rename-Item', LiteralPath: '{{csv}}', NewName: 'released.csv')

            const unread := csv.records('{{missing}}', encoding.utf8)
            errors := 0
            try {
                for record in unread {}
            } catch err {
                errors = errors + 1
            }
            try {
                for record in csv.records('{{invalid}}', encoding.utf8) {}
            } catch err {
                errors = errors + 1
            }
            test.assert(errors == 2)
            powershell<void>('Rename-Item', LiteralPath: '{{invalid}}', NewName: 'released-invalid.csv')
            print('passed')
            """);
            await AssertPasses(project, shell);
        }

        private static string PathLiteral(Project project, string file) => project.PathFor(file).Replace('\\', '/').Replace("'", "\\'");

        private static async Task AssertPasses(Project project, string shell)
        {
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);
            Assert.Equal("", result.Error);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("passed\n", result.Output);
        }
    }
}
