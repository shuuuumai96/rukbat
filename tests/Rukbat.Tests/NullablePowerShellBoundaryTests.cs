namespace Rukbat.Tests
{
    public class NullablePowerShellBoundaryTests : TestBase
    {
        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task NullableArrayFieldsKeepNullAndArrayShapes(string shell)
        {
            await RunVerified(shell, """
            import json
            import test

            struct Data {
                values: int[]?
            }

            for payload in ['{"values":null}', '{"values":[]}', '{"values":[1]}', '{"values":[1,2]}'] {
                data := powershell<Data>("ConvertFrom-Json", payload)
                test.assert(json.stringify(data) == payload)
            }

            print("passed")
            """);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task NullableElementsAndNestedArraysKeepTheirShapeAndMutability(string shell)
        {
            await RunVerified(shell, """
            import json
            import test

            struct Data {
                values: int?[]?
                groups: int[]?[]
                matrix: int[][]?
            }

            data := powershell<Data>("ConvertFrom-Json", '{"values":[null,1,null],"groups":[null,[],[1],[2,3]],"matrix":[[],[1],[2,3]]}')
            test.assert(json.stringify(data.values) == "[null,1,null]")
            test.assert(json.stringify(data.groups) == "[null,[],[1],[2,3]]")
            test.assert(json.stringify(data.matrix) == "[[],[1],[2,3]]")

            matrix := data.matrix

            if matrix != null {
                test.assert(matrix[0].length == 0)
                test.assert(matrix[1].length == 1)
                matrix[2][1] = 9
                test.assert(matrix[2][1] == 9)
                test.assert(json.stringify(data.matrix) == "[[],[1],[2,3]]")
            } else {
                test.fail("The external matrix must remain non-null")
            }

            print("passed")
            """);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task NullableArrayCopiesOwnTheirNestedValues(string shell)
        {
            await RunVerified(shell, """
            import json
            import test

            struct Entry {
                name: string
            }

            struct Data {
                values: Entry[]?
            }

            original := powershell<Data>("ConvertFrom-Json", '{"values":[{"name":"first"},{"name":"second"}]}')
            copied := original
            values := copied.values

            if values != null {
                values[0].name = "changed"
                values.append(Entry { name: "added" })
                test.assert(values.length == 3)
                test.assert(values[0].name == "changed")
                test.assert(values[2].name == "added")
                copied.values = values
            } else {
                test.fail("The external values must remain non-null")
            }

            test.assert(json.stringify(original) == '{"values":[{"name":"first"},{"name":"second"}]}')
            test.assert(json.stringify(copied) == '{"values":[{"name":"changed"},{"name":"second"},{"name":"added"}]}')
            print("passed")
            """);
        }

        private static async Task RunVerified(string shell, string source)
        {
            using var project = new Project();
            project.Write("main.rukbat", source);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            Assert.Equal("passed\n", result.Output);
        }
    }
}
