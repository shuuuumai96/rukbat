namespace Rukbat.Tests;

public class EncodingValueTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("value: string = encoding.utf8", "RUK2001")]
    [InlineData("value: encoding.Encoding = 'utf-8'", "RUK2001")]
    [InlineData("value: encoding.Encoding = 932", "RUK2001")]
    [InlineData("value := encoding.UTF8", "RUK2004")]
    [InlineData("value := encoding.shift_jis", "RUK2004")]
    [InlineData("encoding.utf8 = encoding.windows_932", "RUK2010")]
    [InlineData("value := encoding.Encoding {}", "RUK2004")]
    [InlineData("value := encoding.utf8 + encoding.utf8", "RUK2001")]
    [InlineData("value := encoding.utf8 > encoding.windows_932", "RUK2001")]
    [InlineData("value := encoding.utf8 == 'utf-8'", "RUK2001")]
    [InlineData("encoding.name('utf-8')", "RUK2001")]
    [InlineData("encoding.parse(932)", "RUK2001")]
    [InlineData("encoding.parse<int>('utf-8')", "RUK2005")]
    [InlineData("encoding.parse(name: 'utf-8')", "RUK2005")]
    [InlineData("encoding.name()", "RUK2005")]
    [InlineData("print(encoding.utf8)", "RUK2010")]
    [InlineData("eprint(encoding.utf8)", "RUK2010")]
    [InlineData("import fs; fs.read_text('x', 'utf-8')", "RUK2001")]
    [InlineData("import fs; fs.write_text('x', 'data', 'utf-8')", "RUK2001")]
    [InlineData("import process; process.run('tool.exe', [], process.options(null, 'utf-8'))", "RUK2001")]
    [InlineData("import fs; x: encoding.Encoding? = null; fs.read_text('x', x)", "RUK2001")]
    [InlineData("struct Encoding {}; x: encoding.Encoding = Encoding {}", "RUK2001")]
    public void InvalidValuesAndConversionsAreRejected(string body, string code)
    {
        Assert.Contains(Compile("import encoding\n" + body).Diagnostics, diagnostic => diagnostic.Code == code);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("json.stringify(encoding.utf8)")]
    [InlineData("json.stringify([encoding.utf8])")]
    [InlineData("json.parse<encoding.Encoding>('\"utf-8\"')")]
    [InlineData("json.as<encoding.Encoding>(json.parse('\"utf-8\"'))")]
    [InlineData("json.set(json.parse('{}'), 'encoding', encoding.utf8)")]
    [InlineData("struct Settings { selected: encoding.Encoding?[] }; json.stringify(Settings { selected: [encoding.utf8] })")]
    [InlineData("struct Settings { selected: encoding.Encoding }; json.parse<Settings>('{\"selected\":\"utf-8\"}')")]
    [InlineData("powershell<encoding.Encoding>('Write-Output', 'utf-8')")]
    [InlineData("powershell<encoding.Encoding?[]>('Write-Output', 'utf-8')")]
    [InlineData("struct Settings { selected: encoding.Encoding }; powershell<Settings>('Write-Output', 'utf-8')")]
    [InlineData("powershell<void>('Write-Output', encoding.utf8)")]
    [InlineData("powershell<void>('Write-Output', [encoding.utf8])")]
    public void ExternalBoundariesRequireExplicitNameConversion(string body)
    {
        Assert.Contains(Compile("import encoding; import json\n" + body).Diagnostics, diagnostic => diagnostic.Code == "RUK2010");
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void EncodingOnlyImportEmitsNoFileOrProcessHelpers()
    {
        var output = Success("import encoding\nprint(encoding.name(encoding.utf8))");
        Assert.DoesNotContain("RukbatWindows932Decoder", output);
        Assert.DoesNotContain("RukbatProcessJob", output);
        Assert.DoesNotContain("RukbatMoveBatch", output);
        Assert.DoesNotContain("'fs.read_text'", output);
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void FormatterPreservesTypedMemberSyntaxCanonically()
    {
        using var project = new Project();
        project.Write("main.rukbat", "import encoding\nfn choose(value:encoding.Encoding):encoding.Encoding{return value}\nx:=choose(encoding.utf8)\n");
        var path = project.PathFor("main.rukbat");
        Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, TextWriter.Null));
        var formatted = File.ReadAllText(path);
        Assert.Contains("fn choose(value: encoding.Encoding): encoding.Encoding {", formatted);
        Assert.Contains("x := choose(encoding.utf8)\n", formatted);
        Assert.DoesNotContain("\n\n\n", formatted);
        Assert.False(formatted.EndsWith("\n\n"));
        Assert.Empty(new Compiler().CheckFile(path));
        Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, TextWriter.Null));
        Assert.Equal(formatted, File.ReadAllText(path));
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "CrossVersion")]
    [Theory]
    [InlineData("powershell")]
    [InlineData("pwsh")]
    public async Task ValidatedValuesWorkAcrossModulesCopiesAndExternalConfiguration(string shell)
    {
        using var project = new Project();
        project.Write("settings.rukbat", """
            import encoding
            struct Settings { selected: encoding.Encoding; fallback: encoding.Encoding? }
            fn create(): Settings { return Settings { selected: encoding.utf8, fallback: null } }
            fn select(value: encoding.Encoding): encoding.Encoding { return value }
            fn maybe(value: encoding.Encoding?): encoding.Encoding? { return value }
            """);
        project.Write("main.rukbat", """
            import encoding
            import json
            import test
            import "./settings.rukbat" as settings

            original := settings.create()
            copied := original
            copied.selected = encoding.windows_932
            copied.fallback = encoding.utf16_be
            test.assert(original.selected == encoding.utf8)
            test.assert(original.fallback == null)
            test.assert(copied.selected != original.selected)
            test.assert(settings.select(copied.selected) == encoding.windows_932)

            all: encoding.Encoding[] = [encoding.utf8, encoding.utf16_le, encoding.utf16_be, encoding.windows_932]
            copied_all := all
            copied_all[0] = encoding.windows_932
            copied_all.append(encoding.utf8)
            test.assert(all.length == 4)
            test.assert(all[0] == encoding.utf8)
            test.assert(copied_all.length == 5)
            for selected in all {
                name := encoding.name(selected)
                test.assert(encoding.parse(name) == selected)
                print(name)
            }
            optional := settings.maybe(encoding.utf16_le)
            if optional == null { test.fail("Missing encoding") } else { test.assert(optional == encoding.utf16_le) }
            optional = settings.maybe(null)
            test.assert(optional == null)
            empty: encoding.Encoding[] = []
            test.assert(empty.length == 0)
            nested: encoding.Encoding?[] = [null, encoding.windows_932]
            selected := nested[1]
            if selected != null { test.assert(selected == encoding.windows_932) }

            restored := encoding.parse(json.parse<string>(json.stringify(encoding.name(encoding.utf8))))
            test.assert(restored == encoding.utf8)
            external := powershell<string>('Write-Output', encoding.name(encoding.utf16_be))
            test.assert(encoding.parse(external) == encoding.utf16_be)
            for invalid in ['UTF-8', 'utf8', 'shift-jis', 'cp932', 'windows-99999', '', ' utf-8', 'utf-8 '] {
                try { encoding.parse(invalid); test.fail("Invalid name accepted") }
                catch err { test.assert(err.message == "unsupported encoding: " + invalid) }
            }
            print('passed')
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        var result = shell == "powershell"
            ? await project.RunOutputWithWindowsPowerShell(compiled.Output!) : await project.RunOutput(compiled.Output!);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal("", result.Error);
        Assert.Equal("utf-8\nutf-16le\nutf-16be\nwindows-932\npassed\n", result.Output);
    }
}
