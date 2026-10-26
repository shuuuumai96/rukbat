using System.Text;

namespace Rukbat.Tests;

public class FileLinesTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("fs.lines('a')", "RUK2005")]
    [InlineData("fs.lines('a', 'utf-8')", "RUK2001")]
    [InlineData("fs.lines<string>('a', encoding.utf8)", "RUK2005")]
    [InlineData("fs.lines(path: 'a', encoding: encoding.utf8)", "RUK2005")]
    [InlineData("fs.Lines {}", "RUK2004")]
    [InlineData("fs.__open_lines(fs.lines('a', encoding.utf8))", "RUK2004")]
    [InlineData("print(fs.lines('a', encoding.utf8))", "RUK2010")]
    [InlineData("eprint([fs.lines('a', encoding.utf8)])", "RUK2010")]
    [InlineData("json.stringify({'a': fs.lines('a', encoding.utf8)})", "RUK2010")]
    [InlineData("powershell<string>('Write-Output', fs.lines('a', encoding.utf8))", "RUK2010")]
    [InlineData("powershell<fs.Lines>('Write-Output')", "RUK2010")]
    [InlineData("a := fs.lines('a', encoding.utf8); print(a == a)", "RUK2001")]
    [InlineData("a := fs.lines('a', encoding.utf8); print(a.length)", "RUK2004")]
    [InlineData("a: fs.Lines? = null; for line in a {}", "RUK2001")]
    [InlineData("struct Lines {}; a := Lines {}; for line in a {}", "RUK2001")]
    public void RejectsUnsupportedUses(string source, string code)
    {
        var result = Compile("import fs; import encoding; import json; " + source);
        Assert.False(result.Success);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void DescriptorsBindAcrossValuesModulesAndDebugTypes()
    {
        using var project = new Project();
        project.Write("helper.rukbat", "import fs; import encoding; fn lines(): fs.Lines { return fs.lines('unused', encoding.utf8) }");
        project.Write("main.rukbat", """
            import fs; import './helper.rukbat' as helper
            struct Holder { lines: fs.Lines }
            const source := helper.lines()
            holder := Holder { lines: source }
            values: fs.Lines[] = [holder.lines]
            lookup: dict[string, fs.Lines] = {'a': values[0]}
            optional: fs.Lines? = lookup['a']
            if optional != null { for line in optional { print(line) } }
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug: true);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var type = Assert.Single(compiled.DebugMap!.Types.Values.Where(value => value.Name == "fs.Lines"));
        Assert.Null(type.Fields);
        Assert.DoesNotContain("[fs.Lines]", compiled.Output!);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task LinesMatchWholeReadsForEncodingsBomsAndBufferBoundaries(string shell)
    {
        using var project = new Project();
        var body = new StringBuilder("import fs; import encoding; import text; import test\n");
        foreach (var fixture in ExpandedEncodingTests.Fixtures())
        {
            var member = (string)fixture[0];
            var bytes = Convert.FromHexString((string)fixture[3]);
            File.WriteAllBytes(project.PathFor(member + ".txt"), bytes);
            AddComparison(member + ".txt", "encoding." + member);
        }
        foreach (var number in new[] { 37, 858, 20261, 20269, 20949 })
        {
            // ASCII is not assumed here: zero is a valid unit in each tested codec.
            File.WriteAllBytes(project.PathFor(number + ".txt"), new byte[] { 0, 0, 0, 0 });
            AddComparison(number + ".txt", $"encoding.from_code_page({number})");
        }
        var texts = new[] { "", "a", "a\n", "\n", "a\n\n", "a\r\nb\rc\n", "\r\r\n", "\0\u0085\u2028\u2029\ufeff", new string('x', 65539) + "\r\nend" };
        for (var i = 0; i < texts.Length; i++)
        {
            File.WriteAllText(project.PathFor($"text{i}.txt"), texts[i], new UTF8Encoding(false, true));
            AddComparison($"text{i}.txt", "encoding.utf8");
        }
        var boms = new[] { ("utf8", new byte[] { 239, 187, 191 }), ("utf16_le", new byte[] { 255, 254 }),
            ("utf16_be", new byte[] { 254, 255 }), ("utf32_le", new byte[] { 255, 254, 0, 0 }), ("utf32_be", new byte[] { 0, 0, 254, 255 }) };
        foreach (var (member, bom) in boms)
        {
            File.WriteAllBytes(project.PathFor(member + "-bom.txt"), bom);
            AddComparison(member + "-bom.txt", "encoding." + member);
        }
        File.WriteAllBytes(project.PathFor("le-prefix.txt"), new byte[] { 255, 254, 0, 0 });
        AddComparison("le-prefix.txt", "encoding.utf16_le");
        File.WriteAllBytes(project.PathFor("cp932.txt"), Enumerable.Repeat((byte)65, 65539).Concat(new byte[] { 0xee, 0xe0, 13, 10, 0xfb, 0xfc }).ToArray());
        AddComparison("cp932.txt", "encoding.windows_932");
        File.WriteAllBytes(project.PathFor("gb.txt"), Enumerable.Repeat((byte)65, 65538).Concat(new byte[] { 0x94, 0x39, 0xfc, 0x36 }).ToArray());
        AddComparison("gb.txt", "encoding.gb18030");
        body.AppendLine("print('passed')");
        project.Write("main.rukbat", body.ToString());
        var result = await Execute(project, shell);
        Assert.Equal(0, result.ExitCode); Assert.Empty(result.Error); Assert.Equal("passed\n", result.Output);

        void AddComparison(string file, string encoding)
        {
            var path = project.PathFor(file).Replace('\\', '/');
            body.AppendLine("if true {");
            body.AppendLine($"expected := text.split_lines(fs.read_text('{path}', {encoding}))");
            body.AppendLine($"index := 0; for line in fs.lines('{path}', {encoding}) {{ test.assert(line == expected[index]); index = index + 1 }}");
            body.AppendLine("test.assert(index == expected.length)");
            body.AppendLine("}");
        }
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell", false)]
    [InlineData("pwsh", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", true)]
    public async Task RepeatedNestedTraversalsCopiesReturnsAndCleanupWork(string shell, bool debug)
    {
        using var project = new Project();
        File.WriteAllText(project.PathFor("lines.txt"), "one\ntwo\nthree", new UTF8Encoding(false));
        var path = project.PathFor("lines.txt").Replace('\\', '/');
        project.Write("main.rukbat", $$"""
            import fs; import encoding; import test
            fn make(): fs.Lines { return fs.lines('{{path}}', encoding.utf8) }
            fn first(source: fs.Lines): string { for line in source { return line }; return '' }
            fn empty(source: fs.Lines): int[] { for line in source { return [] }; return [9] }
            fn optional(source: fs.Lines): int[]? { for line in source { return null }; return [] }
            fn recurse(source: fs.Lines, depth: int): int {
                for line in source { if depth == 0 { return 1 }; return recurse(source, depth - 1) + 1 }; return 0
            }
            struct Counter {
                number: int
            }
            impl Counter {
                fn read(source: fs.Lines): int { for line in source { self.number = self.number + 1; return self.number }; return 0 }
                fn copy(source: fs.Lines): Counter { for line in source { return self }; return self }
            }
            const source := make()
            counter := Counter { number: 0 }
            test.assert(counter.read(source) == 1)
            copied := counter.copy(source)
            copied.number = 9
            test.assert(counter.number == 1)
            count := 0
            for line in source {
                for inner in source { count = count + 1; break }
                if line == 'two' { continue }
                count = count + 1
            }
            test.assert(count == 5)
            test.assert(first(source) == 'one')
            test.assert(empty(source).length == 0)
            result := optional(source); test.assert(result == null)
            test.assert(recurse(source, 3) == 4)
            current := source
            for line in current { current = fs.lines('missing', encoding.utf8); count = count + 1 }
            test.assert(count == 8)
            try { for line in source { throw Error { message: 'body' } } } catch err { test.assert(err.message == 'body') }
            powershell<void>('Rename-Item', LiteralPath: '{{path}}', NewName: 'renamed.txt')
            fs.remove_file('{{project.PathFor("renamed.txt").Replace('\\', '/')}}')
            unused := fs.lines('missing', encoding.utf8)
            print('passed')
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug: debug);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
        Assert.Equal(0, result.ExitCode); Assert.Empty(result.Error); Assert.Equal("passed\n", result.Output);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task InvalidBytesAreRejectedByWholeAndIncrementalReads(string shell)
    {
        using var project = new Project();
        var cases = new[] { ("utf8", new byte[] { 65, 10, 255 }), ("utf16_le", new byte[] { 65 }),
            ("utf16_be", new byte[] { 255, 254 }), ("windows_932", new byte[] { 0x82 }),
            ("gb18030", new byte[] { 0x84, 0x31, 0xa5, 0x30 }), ("gb18030", new byte[] { 0xfe, 0x39, 0xfe, 0x39 }),
            ("gb18030", new byte[] { 0x81, 0x30, 0x81 }) };
        var source = new StringBuilder("import fs; import encoding; import test; import process\nerrors := 0\n");
        for (var i = 0; i < cases.Length; i++)
        {
            File.WriteAllBytes(project.PathFor(i + ".bin"), cases[i].Item2);
            var path = project.PathFor(i + ".bin").Replace('\\', '/');
            source.AppendLine($"try {{ fs.read_text('{path}', encoding.{cases[i].Item1}) }} catch err {{ errors = errors + 1 }}");
            source.AppendLine($"try {{ for line in fs.lines('{path}', encoding.{cases[i].Item1}) {{}} }} catch err {{ errors = errors + 1 }}");
            source.AppendLine($"fs.remove_file('{path}')");
        }
        project.Write("invalid-output.ps1", "$bytes = [byte[]](0x84,0x31,0xa5,0x30); $output = [Console]::OpenStandardOutput(); $output.Write($bytes,0,$bytes.Length)");
        var child = project.PathFor("invalid-output.ps1").Replace('\\', '/');
        source.AppendLine($"try {{ process.run('powershell', ['-NoProfile', '-NonInteractive', '-File', '{child}'], process.options(10000, encoding.gb18030)) }} catch err {{ errors = errors + 1 }}");
        source.AppendLine($"test.assert(errors == {cases.Length * 2 + 1}); print('passed')");
        project.Write("main.rukbat", source.ToString());
        var result = await Execute(project, shell);
        Assert.Equal(0, result.ExitCode); Assert.Empty(result.Error); Assert.Equal("passed\n", result.Output);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell", false)]
    [InlineData("pwsh", false)]
    [InlineData("powershell", true)]
    [InlineData("pwsh", true)]
    public async Task CleanupFailureReplacesReturnsAndPreservesPrimaryErrors(string shell, bool debug)
    {
        using var project = new Project();
        File.WriteAllText(project.PathFor("lines.txt"), "line\n", new UTF8Encoding(false));
        var path = project.PathFor("lines.txt").Replace('\\', '/');
        project.Write("main.rukbat", $$"""
            import fs; import encoding; import test; import text
            fn recover(source: fs.Lines): int {
                try { for line in source { return 42 } } catch err { return 99 }; return 0
            }
            fn fallthrough(source: fs.Lines): int {
                try { for line in source { return 42 } } catch err {}; return 100
            }
            fn values(source: fs.Lines): int[] {
                try { for line in source { return [] } } catch err { return [7] }; return [9]
            }
            fn optional(source: fs.Lines): int[]? {
                try { for line in source { return [42] } } catch err { return null }; return []
            }
            fn effect(): void { print('effect') }
            fn action(source: fs.Lines): void { for line in source { return effect() } }
            fn nested(source: fs.Lines): int {
                try {
                    for outer in source {
                        try { for inner in source { return 42 } } catch err { return 99 }
                    }
                } catch err { return 100 }; return 0
            }
            source := fs.lines('{{path}}', encoding.utf8)
            test.assert(recover(source) == 99)
            test.assert(fallthrough(source) == 100)
            test.assert(values(source).length == 1)
            test.assert(values(source)[0] == 7)
            maybe := optional(source); test.assert(maybe == null)
            test.assert(nested(source) == 100)
            try { action(source) } catch err { test.assert(text.contains(err.message, 'injected close')); print('close') }
            try { for line in source { throw Error { message: 'primary' } } } catch err {
                test.assert(text.contains(err.message, 'primary; cleanup failed:'))
                test.assert(text.contains(err.message, 'injected close'))
            }
            powershell<void>('Rename-Item', LiteralPath: '{{path}}', NewName: 'released.txt')
            print('passed')
            """);
        var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug: debug);
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        // Fault injection modifies only this generated test artifact, after real file disposal.
        var output = compiled.Output!.Replace("public static class RukbatFileText {", CloseFaultStream + "\npublic static class RukbatFileText {")
            .Replace("Stream stream = File.Open(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read);",
                "Stream stream = new CloseFaultStream(File.Open(source.Path, FileMode.Open, FileAccess.Read, FileShare.Read));");
        var result = shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(output) : await project.RunOutput(output);
        Assert.Equal(0, result.ExitCode); Assert.Empty(result.Error); Assert.Equal("effect\nclose\npassed\n", result.Output);
    }

    private const string CloseFaultStream = """
        public sealed class CloseFaultStream : Stream {
            private readonly Stream inner;
            public CloseFaultStream(Stream inner) { this.inner = inner; }
            public override bool CanRead { get { return inner.CanRead; } }
            public override bool CanSeek { get { return inner.CanSeek; } }
            public override bool CanWrite { get { return false; } }
            public override long Length { get { return inner.Length; } }
            public override long Position { get { return inner.Position; } set { inner.Position = value; } }
            public override int Read(byte[] b, int o, int n) { return inner.Read(b, o, n); }
            public override long Seek(long o, SeekOrigin origin) { return inner.Seek(o, origin); }
            public override void Flush() { inner.Flush(); }
            public override void SetLength(long n) { throw new NotSupportedException(); }
            public override void Write(byte[] b, int o, int n) { throw new NotSupportedException(); }
            protected override void Dispose(bool disposing) { inner.Dispose(); throw new IOException("injected close"); }
        }
        """;

    private static async Task<RunResult> Execute(Project project, string shell)
    {
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        return shell == "powershell" ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
    }
}
