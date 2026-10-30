using System.Text;
using System.Text.Json;

namespace Rukbat.Tests
{
    public class JsonValueTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("value := json.parse('{}')")]
        [InlineData("value: json.Value = json.parse('null')")]
        [InlineData("values: json.Value[] = json.items(json.parse('[1,null]'))")]
        [InlineData("name := json.as<string>(json.get(json.parse('{\"name\":\"Taro\"}'), 'name'))")]
        [InlineData("struct Config { data: json.Value }; config := json.parse<Config>('{\"data\":{\"unknown\":true}}')")]
        [InlineData("struct Value { name: string }; value := json.parse<Value>('{\"name\":\"Taro\"}')")]
        public void JsonValuesHaveAnExplicitStaticType(string source)
        {
            Success("import json\n" + source);
            Assert.Empty(new Compiler().Check("import json\n" + source));
            using var project = new Project();
            project.Write("main.rukbat", "import json\n" + source);
            using var errors = new StringWriter();
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, errors));
            var formatted = File.ReadAllText(project.PathFor("main.rukbat"));
            Assert.Empty(new Compiler().Check(formatted));
            Assert.Equal(0, Cli.Run(["fmt", project.PathFor("main.rukbat")], TextWriter.Null, errors));
            Assert.Equal(formatted, File.ReadAllText(project.PathFor("main.rukbat")));
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("value := json.parse(1)", "RUK2001")]
        [InlineData("json.as(json.parse('1'))", "RUK2005")]
        [InlineData("json.as<int>('1')", "RUK2001")]
        [InlineData("json.as<Error>(json.parse('{}'))", "RUK2010")]
        [InlineData("json.get('{}', 'name')", "RUK2001")]
        [InlineData("json.get(json.parse('{}'), 1)", "RUK2001")]
        [InlineData("json.items<string>(json.parse('[]'))", "RUK2005")]
        [InlineData("json.keys()", "RUK2005")]
        [InlineData("json.has(json.parse('{}'), name: 'x')", "RUK2005")]
        [InlineData("value: json.Value = 'text'", "RUK2001")]
        [InlineData("value := json.Value {}", "RUK2004")]
        [InlineData("value := json.parse('{}'); print(value)", "RUK2010")]
        [InlineData("value := json.parse('{}'); print(value == value)", "RUK2001")]
        [InlineData("value := json.parse('{}'); value.field = 1", "RUK2004")]
        [InlineData("value := json.parse('{}'); value['name']", "RUK2001")]
        [InlineData("value := powershell<json.Value>('Write-Output', 1)", "RUK2010")]
        [InlineData("struct Box { value: json.Value }; value := powershell<Box>('Write-Output', 1)", "RUK2010")]
        [InlineData("optional: json.Value? = null; json.kind(optional)", "RUK2001")]
        public void JsonValuesDoNotIntroduceGeneralDynamicOperations(string source, string code)
        {
            var result = Compile("import json\n" + source);
            Assert.Null(result.Output);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task UnknownNestedJsonCanBeReadWithoutStructs(string shell)
        {
            const string payload = "{\"data\":{\"users\":[{\"name\":\"日本語\",\"enabled\":true,\"age\":30,\"extra\":{\"anything\":[1,null]}},{\"name\":\"Hanako\",\"enabled\":false,\"age\":null}]},\"weird.key/[]\":{\"\":\"literal\"},\"A\":1,\"a\":2,\"large\":9007199254740993,\"huge\":1e400,\"mixed\":[1,\"two\",false,null,[],{}]}";
            var result = await RunJson(shell, $$"""
            fn name(user: json.Value): string {
                return json.as<string>(json.get(user, 'name'))
            }
            document := json.parse('{{payload}}')
            data := json.get(document, 'data')
            for user in json.items(json.get(data, 'users')) {
                print(name(user))
                print(json.as<bool>(json.get(user, 'enabled')))
                age := json.as<int?>(json.get(user, 'age'))
                if age != null { print(age) } else { print('unknown') }
                print(json.has(user, 'extra'))
            }
            print(json.as<string>(json.get(json.get(document, 'weird.key/[]'), '')))
            print(json.as<int>(json.get(document, 'A')))
            print(json.as<int>(json.get(document, 'a')))
            print(json.stringify(json.get(document, 'large')))
            print(json.stringify(json.get(document, 'huge')))
            for item in json.items(json.get(document, 'mixed')) { print(json.kind(item)) }
            print(json.stringify(document))
            """);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            var lines = result.Output.TrimEnd('\n').Split('\n');
            Assert.Equal(new[] { "日本語", "True", "30", "True", "Hanako", "False", "unknown", "False",
                "literal", "1", "2", "9007199254740993", "1e400", "number", "string", "bool", "null", "array", "object" }, lines[..^1]);
            using var original = JsonDocument.Parse(payload);
            using var actual = JsonDocument.Parse(lines[^1]);
            Assert.Equal(original.RootElement.GetProperty("large").GetRawText(), actual.RootElement.GetProperty("large").GetRawText());
            Assert.Equal(original.RootElement.GetProperty("huge").GetRawText(), actual.RootElement.GetProperty("huge").GetRawText());
            Assert.Equal(2, actual.RootElement.GetProperty("data").GetProperty("users").GetArrayLength());
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task JsonValuesKeepNullEmptyArraysAndCopiesDistinct(string shell)
        {
            var result = await RunJson(shell, """
            fn identity(value: json.Value): json.Value { return value }
            root := identity(json.parse('null'))
            print(json.kind(root))
            print(json.stringify(root))
            print(json.stringify(json.parse('"\\uD800"')))
            object := json.parse('{"null":null,"a":1,"A":2,"":3}')
            print(json.has(object, 'null'))
            print(json.has(object, 'missing'))
            print(json.kind(json.get(object, 'null')))
            print(json.stringify(json.keys(object)))
            print(json.keys(json.parse('{}')).length)
            print(json.items(json.parse('[]')).length)
            values := json.items(json.parse('[null,[],{"a":1}]'))
            copy := values
            copy[0] = json.parse('2')
            copy.append(json.parse('"new"'))
            print(json.stringify(values))
            print(json.stringify(copy))
            optional: json.Value? = null
            print(json.stringify(optional))
            optional = root
            if optional != null { print(json.kind(optional)) }
            struct Box { data: json.Value; values: json.Value[] }
            box := json.parse<Box>('{"data":{"x":[null,1]},"values":[false,{},[]]}')
            print(json.stringify(box))
            selected := json.as<int[]>(json.parse('[1,2]'))
            selected[0] = 3
            print(json.stringify(selected))
            print(json.stringify(json.as<json.Value[]>(json.parse('[1,null]'))))
            """);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            Assert.Equal("null\nnull\n\"\\ud800\"\nTrue\nFalse\nnull\n[\"\",\"A\",\"a\",\"null\"]\n0\n0\n[null,[],{\"a\":1}]\n[2,[],{\"a\":1},\"new\"]\nnull\nnull\n{\"data\":{\"x\":[null,1]},\"values\":[false,{},[]]}\n[3,2]\n[1,null]\n", result.Output);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task JsonValuesPreserveEscapesDeepNestingAndLargeArrays(string shell)
        {
            const string text = "quote\" slash\\ newline\n carriage\r tab\t back\b form\f control\0 日本語 \U0001F600";
            var payload = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["key\"\\\n"] = text,
                ["nested"] = JsonSerializer.Deserialize<JsonElement>(new string('[', 80) + "null" + new string(']', 80),
                    new JsonSerializerOptions { MaxDepth = 128 }),
                ["rows"] = Enumerable.Range(0, 10_000).Select(index => new { id = index, name = "row-" + index }).ToArray()
            }, new JsonSerializerOptions { MaxDepth = 128 });
            var literal = payload.Replace("\\", "\\\\").Replace("'", "\\'");
            var result = await RunJson(shell, $$"""
            document := json.parse('{{literal}}')
            print(json.stringify(document))
            print(json.items(json.get(document, 'rows')).length)
            """);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            var lines = result.Output.TrimEnd('\n').Split('\n');
            Assert.Equal("10000", lines[1]);
            using var actual = JsonDocument.Parse(lines[0], new JsonDocumentOptions { MaxDepth = 128 });
            Assert.Equal(text, actual.RootElement.GetProperty("key\"\\\n").GetString());
            Assert.Equal(10_000, actual.RootElement.GetProperty("rows").GetArrayLength());
            Assert.Equal(9999, actual.RootElement.GetProperty("rows")[9999].GetProperty("id").GetInt32());
            var nested = actual.RootElement.GetProperty("nested");
            for (var index = 0; index < 80; index++)
            {
                nested = nested[0];
            }

            Assert.Equal(JsonValueKind.Null, nested.ValueKind);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task InvalidLookupsAndConversionsUseOrdinaryErrors(string shell)
        {
            var result = await RunJson(shell, """
            try { json.get(json.parse('{}'), 'missing') } catch err { print('missing') }
            try { json.get(json.parse('[]'), 'name') } catch err { print('object required') }
            try { json.has(json.parse('null'), 'name') } catch err { print('has object required') }
            try { json.keys(json.parse('1')) } catch err { print('keys object required') }
            try { json.items(json.parse('{}')) } catch err { print('array required') }
            try { json.as<int>(json.parse('9223372036854775808')) } catch err { print('int overflow') }
            try { json.as<int>(json.parse('1.0')) } catch err { print('int grammar') }
            try { json.as<float>(json.parse('1e400')) } catch err { print('finite float') }
            try { json.as<string>(json.parse('null')) } catch err { print('nullable required') }
            try { json.as<bool>(json.parse('"true"')) } catch err { print('no coercion') }
            try { json.parse('{"a":1,"a":2}') } catch err { print('duplicate') }
            try { json.parse('[1,]') } catch err { print('syntax') }
            try { json.parse('1 trailing') } catch err { print('trailing') }
            """);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            Assert.Equal("missing\nobject required\nhas object required\nkeys object required\narray required\nint overflow\nint grammar\nfinite float\nnullable required\nno coercion\nduplicate\nsyntax\ntrailing\n", result.Output);
        }

        private static async Task<RunResult> RunJson(string shell, string source)
        {
            using var project = new Project();
            project.Write("main.rukbat", "import json\n" + source);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.WriteAllText(project.PathFor("job.ps1"), compiled.Output, new UTF8Encoding(true));
            File.Delete(project.PathFor("main.rukbat"));
            return await RunDeployed(shell, project.PathFor("job.ps1"), project.DirectoryPath, project.DirectoryPath);
        }
    }
}
