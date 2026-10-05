using Rukbat;

namespace Rukbat.Tests;

public sealed class FormatterTests : TestBase
{
    [Fact]
    [Trait("Category", "Unit")]
    public void DocumentationFormattingPreservesMarkdownAndDeclarationAttachment()
    {
        using var project = new Project();
        var source = "//! Module overview.  \r\n//! Second line.\r\nimport fs\r\n/// Function overview.  \r\n/// Second line.\r\n///\r\n/// ```rukbat check\r\n///     x := 1\r\n///     print(x)\r\n/// ```\r\nfn f():void{}\r\nstruct S{\r\n/// Field overview.\r\nvalue:int\r\n/// Method overview.\r\nfn method():void{}\r\n}\r\n";
        project.Write("main.rukbat", source);
        Assert.Equal(0, Cli.Run(["doc", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
        var before = File.ReadAllText(project.PathFor("main.html"));
        Assert.Contains("overview.<br />", before);
        Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
        var formatted = File.ReadAllText(project.PathFor("main.rukbat"));
        Assert.Contains("/// Function overview.  \n", formatted);
        Assert.Contains("///     x := 1\n", formatted);
        Assert.Contains("/// ```\nfn f()", formatted);
        Assert.Contains("    /// Method overview.\n    fn method()", formatted);
        Assert.Equal(0, Cli.Run(["doc", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
        var after = File.ReadAllText(project.PathFor("main.html"));
        foreach (var text in new[] { "Module overview.<br />", "Function overview.<br />", "Field overview.", "Method overview.", "    x := 1" })
            Assert.Contains(text, after);
        Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
        Assert.Equal(formatted, File.ReadAllText(project.PathFor("main.rukbat")));
    }

    public static TheoryData<string, string, string> SyntaxCases => new()
    {
        { "const declarations", "const a:=1;const b:int=2;fn f():void{const items:int[]=[];print(items.length)}", "const a := 1\nconst b: int = 2\n\nfn f(): void {\n    const items: int[] = []\n    print(items.length)\n}\n" },
        { "datetime types", "import datetime;value:datetime.Timestamp?=null;zones:datetime.Zone[]=[datetime.utc]", "import datetime\n\nvalue: datetime.Timestamp? = null\nzones: datetime.Zone[] = [datetime.utc]\n" },
        { "imports", "import text as txt;import './missing.rukbat' as missing", "import text as txt\nimport './missing.rukbat' as missing\n" },
        { "declarations and assignments", "a:=1;b : int=2;a=\n b+1", "a := 1\nb: int = 2\na = b + 1\n" },
        { "nullable and array types", "a : mod.User?[]?=null;b:int[][]=[[1],[]]", "a: mod.User?[]? = null\nb: int[][] = [[1], []]\n" },
        { "literals", "a:=2147483647;b := -9223372036854775808;c:=1.5e-2;d:=true;e:=false;f:string?=null", "a := 2147483647\nb := -9223372036854775808\nc := 1.5e-2\nd := true\ne := false\nf: string? = null\n" },
        { "strings", "a:=\"# // {} ; < >\";b:='\\n\\r\\t\\\\\\\"\\\''", "a := \"# // {} ; < >\"\nb := '\\n\\r\\t\\\\\\\"\\\''\n" },
        { "multiline string values", "value:=\"first\r\nsecond\nthird\rfinal\"", "value := \"first\\r\\nsecond\\nthird\\rfinal\"\n" },
        { "operators", "a:=1+2-3*4/5%6;b:=a==1||a!=2&&a<3&&a<=4&&a>5&&a>=6", "a := 1 + 2 - 3 * 4 / 5 % 6\nb := a == 1 || a != 2 && a < 3 && a <= 4 && a > 5 && a >= 6\n" },
        { "unary and grouping", "a:=-(1+2);b:=!!true;c:=1+-2;d:=+ +1", "a := -(1 + 2)\nb := !!true\nc := 1 + -2\nd := ++1\n" },
        { "functions and bare return", "fn f(\na:int,\nb:string?[]\n):void\n{\nreturn;\n}", "fn f(a: int, b: string?[]): void {\n    return\n}\n" },
        { "return continuations", "fn f():int{ return (\n1+\n2\n) }", "fn f(): int {\n    return (1 + 2)\n}\n" },
        { "conditional header", "if true\n{print(1)}\nelse\n{print(2)}", "if true {\n    print(1)\n} else {\n    print(2)\n}\n" },
        { "loops", "for x in [1,2]\n{while x>0\n{if x==1{break}else{continue}}}", "for x in [1, 2] {\n    while x > 0 {\n        if x == 1 {\n            break\n        } else {\n            continue\n        }\n    }\n}\n" },
        { "errors", "try\n{throw Error{message:'bad'}}\ncatch error\n{eprint(error.message)}", "try {\n    throw Error {\n        message: 'bad'\n    }\n} catch error {\n    eprint(error.message)\n}\n" },
        { "struct fields and methods", "struct User\n{name:string;age:int;fn set(age:int):void{self.age=age;return}}", "struct User {\n    name: string\n    age: int\n\n    fn set(age: int): void {\n        self.age = age\n        return\n    }\n}\n" },
        { "struct literal", "user:=mod.User{name:'Taro',age:30}", "user := mod.User {\n    name: 'Taro', age: 30\n}\n" },
        { "nested struct literals", "user:=User{address:Address{city:'Tokyo'},age:30}", "user := User {\n    address: Address {\n        city: 'Tokyo'\n    }, age: 30\n}\n" },
        { "struct literal in call", "print(Error{message:'bad'})", "print(Error {\n    message: 'bad'\n})\n" },
        { "struct literal in array", "errors:Error[]=[Error{message:'bad'},Error{message:'worse'}]", "errors: Error[] = [Error {\n    message: 'bad'\n}, Error {\n    message: 'worse'\n}]\n" },
        { "struct literal postfix", "message:=Error{message:'bad'}.message", "message := Error {\n    message: 'bad'\n}.message\n" },
        { "calls and named arguments", "result:=mod.f(\n1,\nname:'two'\n);print(result);eprint('bad');exit(0);values:=args()", "result := mod.f(1, name: 'two')\nprint(result)\neprint('bad')\nexit(0)\nvalues := args()\n" },
        { "member and index updates", "values[\n0\n].items[1]=2;values[0].items.append(3);n:=values[0].items.length", "values[0].items[1] = 2\nvalues[0].items.append(3)\nn := values[0].items.length\n" },
        { "empty arrays and trailing comma", "values:int[]=[];other:=[1,\n2,\n]", "values: int[] = []\nother := [1, 2,]\n" },
        { "PowerShell boundary", "value:=powershell< string?[] >('Write-Output',null)", "value := powershell<string?[]>('Write-Output', null)\n" },
        { "typed module calls", "value:=json.parse<mod.User[]?>('[]');other:=module.convert<int>('1')", "value := json.parse<mod.User[]?>('[]')\nother := module.convert<int>('1')\n" },
        { "comparison using decode name", "value:=decode<other>(last)", "value := decode < other > (last)\n" },
        { "comments in expression", "value:=1+ // add\n2;other:=[1, // first\n2]", "value := 1 +  // add\n    2\nother := [1,  // first\n    2]\n" },
        { "comments in header", "if true // condition\n{print(1)}", "if true  // condition\n{\n    print(1)\n}\n" },
        { "comments in arguments", "fn f():void{print( // value\n// explanation\n1 // done\n)}", "fn f(): void {\n    print(  // value\n        // explanation\n        1  // done\n    )\n}\n" },
        { "empty struct literals", "struct Empty{};value:=Empty{};values:=[Empty{}]", "struct Empty {\n}\n\nvalue := Empty {\n}\nvalues := [Empty {\n}]\n" },
        { "array in return and throw grouping", "fn f():int[]{return [1,2]};throw (Error{message:'bad'})", "fn f(): int[] {\n    return [1, 2]\n}\n\nthrow (Error {\n    message: 'bad'\n})\n" },
        { "comments in block", "// top\nif true{ // block\n// inside\nprint(1) // end\n}\n// tail", "// top\nif true {\n    // block\n    // inside\n    print(1)  // end\n}\n// tail\n" },
        { "comment between else and braces", "if true{} // then\nelse{};try{} // try\ncatch error{}", "if true {\n}  // then\nelse {\n}\n\ntry {\n}  // try\ncatch error {\n}\n" },
        { "comment-only source", "  // first\r\n\t// second", "// first\n// second\n" },
        { "empty source", " \r\n\t", "\n" },
    };

    [Theory]
    [MemberData(nameof(SyntaxCases))]
    [Trait("Category", "Unit")]
    public void FormatsEverySyntaxPreservesDiagnosticsAndIsIdempotent(string name, string source, string expected)
    {
        using var project = new Project();
        var input = project.PathFor("main.rukbat");
        project.Write("main.rukbat", source);
        using var errors = new StringWriter();
        Assert.True(Cli.Run(["fmt", input], TextWriter.Null, errors) == 0, $"{name}: {errors}");
        var formatted = File.ReadAllText(input);
        Assert.Equal(expected, formatted);
        var compiler = new Compiler();
        Assert.Equal(
            compiler.Check(source).Select(diagnostic => (diagnostic.Code, diagnostic.Message)),
            compiler.Check(formatted).Select(diagnostic => (diagnostic.Code, diagnostic.Message)));
        Assert.Equal(0, Cli.Run(["fmt", input], TextWriter.Null, errors));
        Assert.Equal(formatted, File.ReadAllText(input));
        Assert.False(File.ReadAllBytes(input).AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }));
    }

    [Theory]
    [InlineData("value := 'unterminated")]
    [InlineData("if true { print(1)")]
    [InlineData("fn f(a: int,): void {}")]
    [InlineData("value := [1,,2]")]
    [InlineData("value := powershell<int('echo')")]
    [InlineData("value := 1e+")]
    [InlineData("value := 9223372036854775808")]
    [InlineData("$value = 1")]
    [Trait("Category", "Unit")]
    public void InvalidSyntaxLeavesBytesUnchanged(string source)
    {
        using var project = new Project();
        var input = project.PathFor("main.rukbat");
        File.WriteAllText(input, source, new System.Text.UTF8Encoding(true));
        var original = File.ReadAllBytes(input);
        using var errors = new StringWriter();
        Assert.Equal(1, Cli.Run(["fmt", input], TextWriter.Null, errors));
        Assert.NotEmpty(errors.ToString());
        Assert.Equal(original, File.ReadAllBytes(input));
    }

    [Theory]
    [InlineData("import './missing.rukbat' as missing;value:=missing.f()")]
    [InlineData("value:int='wrong';unknown(value)")]
    [Trait("Category", "Unit")]
    public void FormattingRequiresOnlyValidSyntax(string source)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
    }

    public static IEnumerable<object[]> Examples()
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples");
        return Directory.GetFiles(directory, "*.rukbat", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new object[] { Path.GetRelativePath(directory, path) });
    }

    [Theory]
    [MemberData(nameof(Examples))]
    [Trait("Category", "Unit")]
    public void FormatsExamplesAndPreservesCompilationResults(string relativePath)
    {
        using var project = new Project();
        var fixtures = Path.Combine(AppContext.BaseDirectory, "Fixtures", "examples");
        foreach (var path in Directory.GetFiles(fixtures, "*.rukbat", SearchOption.AllDirectories))
        {
            project.Write(Path.GetRelativePath(fixtures, path), File.ReadAllText(path));
        }
        var input = project.PathFor(relativePath);
        var compiler = new Compiler();
        var before = compiler.CompileFile(input);
        if (relativePath == "struct-error.rukbat")
        {
            Assert.False(before.Success);
            Assert.Equal("RUK2001", Assert.Single(before.Diagnostics).Code);
        }
        else
        {
            Assert.True(before.Success, string.Join("\n", before.Diagnostics));
        }
        using var errors = new StringWriter();
        Assert.True(Cli.Run(["fmt", input], TextWriter.Null, errors) == 0, errors.ToString());
        var after = compiler.CompileFile(input);
        Assert.Equal(before.Success, after.Success);
        Assert.Equal(
            before.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)),
            after.Diagnostics.Select(diagnostic => (diagnostic.Code, diagnostic.Message)));
        var formatted = File.ReadAllText(input);
        Assert.Equal(0, Cli.Run(["fmt", input], TextWriter.Null, errors));
        Assert.Equal(formatted, File.ReadAllText(input));
    }

    [Theory]
    [InlineData("if true{print(1)}else{print(2)}")]
    [InlineData("if true\r\n{\r\n\tprint( 1 )\r\n}\r\nelse\r\n{print(2)}")]
    [InlineData("if true {\n            print(1);\n} else {\nprint(2)\n}")]
    [Trait("Category", "Unit")]
    public void DifferentIndentationAndLineEndingsProduceTheSameLayout(string source)
    {
        using var project = new Project();
        project.Write("main.rukbat", source);
        Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, TextWriter.Null));
        Assert.Equal("if true {\n    print(1)\n} else {\n    print(2)\n}\n", File.ReadAllText(project.PathFor("main.rukbat")));
    }

    [Fact]
    [Trait("Category", "Runtime")]
    [Trait("Category", "PowerShell51")]
    [Trait("Category", "CrossVersion")]
    public async Task FormattedProgramPreservesBehaviorOnBothPowerShellHosts()
    {
        using var project = new Project();
        project.Write("data.rukbat", "struct Item{value:int};fn get():Item{return Item{value:42}}");
        project.Write("main.rukbat", """
            import './data.rukbat' as data;import json
            struct Box{items:int[];fn add(value:int):void{self.items.append(value);return}}
            fn sum(values:int[]):int
            {total:=0;for value in values{total=total+value};return (total)}
            box:=Box{items:[1,2]};box.add(3);box.items[0]=4
            print(sum(box.items))
            name:string?='ready';if name!=null{print(name)}else{print('missing')}
            print(-(-2)==+2&&!(false||false))
            index:=0;while index<3{index=index+1;if index==1{continue};if index==3{break};print(index)}
            try{throw Error{message:'caught'}}catch error{print(error.message)}
            print(data.get().value)
            print(json.parse<int>('7')+powershell<int>('Write-Output',1))
            print([Error{message:'nested'}][0].message)
            count:=1+ // continuation
            2;print(count)
            """ + "\nraw:=\"first\r\nsecond\nthird\rfinal\";print(raw==\"first\\r\\nsecond\\nthird\\rfinal\")");
        var before = project.Compile();
        Assert.True(before.Success, string.Join("\n", before.Diagnostics));
        foreach (var file in new[] { "data.rukbat", "main.rukbat" })
        {
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor(file)], TextWriter.Null, TextWriter.Null));
        }
        var after = project.Compile();
        Assert.True(after.Success, string.Join("\n", after.Diagnostics));
        const string expected = "9\nready\nTrue\n2\ncaught\n42\n8\nnested\n3\nTrue\n";
        foreach (var output in new[] { before.Output!, after.Output! })
        {
            var modern = await project.RunOutput(output);
            Assert.True(modern.ExitCode == 0, modern.Error);
            Assert.Equal(expected, modern.Output);
            var windows = await project.RunOutputWithWindowsPowerShell(output);
            Assert.True(windows.ExitCode == 0, windows.Error);
            Assert.Equal(expected, windows.Output);
        }
    }
}
