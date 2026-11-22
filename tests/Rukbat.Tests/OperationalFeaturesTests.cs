using System.Text;

namespace Rukbat.Tests
{
    public class OperationalFeaturesTests : TestBase
    {
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task StreamingAndOperatingSystemApisWorkTogether(string shell)
        {
            using var project = new Project();
            project.Write("input/source.csv", "category,note\r\nerror,\"line\r\nbreak\"\r\n");
            project.Write("missing-column.csv", "wrong\n");
            project.Write("header-only.csv", "category\n");
            project.Write("bad-width.csv", "category,note\nerror,ok\nshort\n");
            project.Write("output.txt", "previous");
            project.Write("main.rukbat", """
            import csv
            import encoding
            import fs
            import json
            import process
            import test
            import text

            for writer in fs.stage_text("output.txt", encoding.utf8) {
                fs.write_chunk(writer, "unfinished")
            }
            test.assert(fs.read_text("output.txt", encoding.utf8) == "previous")
            let_records := 0
            for writer in fs.stage_text("output.txt", encoding.utf8) {
                for file in fs.walk_files("input", "*.csv") {
                    for row in csv.rows(file, encoding.utf8, ["category", "note"]) {
                        let_records = let_records + 1
                        fs.write_chunk(writer, csv.get(row, "category"))
                        fs.write_chunk(writer, csv.get(row, "note"))
                    }
                }
                fs.commit(writer)
            }
            test.assert(let_records == 1)
            test.assert(fs.read_text("output.txt", encoding.utf8) == "errorline\r\nbreak")
            test.assert(fs.list_files(".", ".rukbat-write-*").length == 0)

            try {
                for writer in fs.stage_text("output.txt", encoding.utf8) {
                    for row in csv.rows("bad-width.csv", encoding.utf8, ["category"]) {
                        fs.write_chunk(writer, csv.get(row, "category"))
                    }
                    fs.commit(writer)
                }
                test.fail("bad row was accepted")
            } catch err { test.assert(text.contains(err.message, "CSV record 3")) }
            test.assert(fs.read_text("output.txt", encoding.utf8) == "errorline\r\nbreak")
            test.assert(fs.list_files(".", ".rukbat-write-*").length == 0)

            for writer in fs.stage_text("output.txt", encoding.ascii) {
                try { fs.write_chunk(writer, "東京"); test.fail("encoding accepted") } catch err {}
                try { fs.commit(writer); test.fail("commit accepted") } catch err {
                    test.assert(text.contains(err.message, "closed or has failed"))
                }
            }
            test.assert(fs.read_text("output.txt", encoding.utf8) == "errorline\r\nbreak")

            for row in csv.rows("header-only.csv", encoding.utf8, ["category"]) {
                test.fail("unexpected row")
            }
            try {
                for row in csv.rows("missing-column.csv", encoding.utf8, ["category"]) {}
                test.fail("header was accepted")
            } catch err { test.assert(text.contains(err.message, "Missing required CSV column")) }

            lock_count := 0
            for acquired in fs.try_lock("job.lock") {
                test.assert(acquired)
                for busy in fs.try_lock("job.lock") { test.fail("duplicate acquisition") }
                try { fs.read_text("job.lock", encoding.utf8) } catch err {
                    test.assert(fs.error_kind(err) == "in_use")
                }
                lock_count = lock_count + 1
            }
            for acquired in fs.try_lock("job.lock") { lock_count = lock_count + 1 }
            test.assert(lock_count == 2)
            try { fs.read_text("absent", encoding.utf8); test.fail("read accepted") } catch err {
                test.assert(fs.error_kind(err) == "not_found")
            }

            struct Settings { values: Item[] }
            struct Item { value: int }
            test.assert(json.parse_strict<Settings>('{"values":[{"value":1}]}').values[0].value == 1)
            try {
                json.parse_strict<Settings>('{"values":[{"value":1,"extra":2}]}')
                test.fail("extra key accepted")
            } catch err { test.assert(text.contains(err.message, "extra: Unknown property")) }
            const started := process.monotonic_ms()
            process.sleep(40)
            test.assert(process.monotonic_ms() > started)
            const options := process.options(10000, encoding.utf8)
            const command := "[Console]::Write('out'); [Console]::Error.Write('err'); exit 9"
            const result := process.run_to_files("powershell", ["-NoProfile", "-NonInteractive", "-Command", command], options, "stdout.log", "stderr.log")
            test.assert(result == 9)
            test.assert(fs.read_text("stdout.log", encoding.utf8) == "out")
            test.assert(fs.read_text("stderr.log", encoding.utf8) == "err")
            try {
                process.run_to_files("powershell", ["-NoProfile", "-Command", command], options, "stdout.log", "unused.log")
                test.fail("existing output was overwritten")
            } catch err { test.assert(text.contains(err.message, "Cannot create process output file")) }
            test.assert(fs.read_text("stdout.log", encoding.utf8) == "out")
            print("passed")
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
            var result = await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("passed\n", result.Output);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void FormatCheckDoesNotRewriteNoncanonicalBytes()
        {
            using var project = new Project();
            var path = project.PathFor("program.rukbat");
            File.WriteAllText(path, "print(\"value\")\n", new UTF8Encoding(true));
            var original = File.ReadAllBytes(path);
            using var errors = new StringWriter();
            Assert.Equal(1, Cli.Run(["fmt", "--check", path], TextWriter.Null, errors));
            Assert.Contains(path, errors.ToString());
            Assert.Equal(original, File.ReadAllBytes(path));
            Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, errors));
            Assert.Equal(0, Cli.Run(["fmt", "--check", project.DirectoryPath], TextWriter.Null, errors));
        }
    }
}
