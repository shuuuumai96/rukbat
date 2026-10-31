namespace Rukbat.Tests
{
    public sealed class ArrayInferenceTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("items := [null]")]
        [InlineData("items := [null, null]")]
        [InlineData("item: string? = null\nitems := [null, item]")]
        [InlineData("items := [[null]]")]
        [InlineData("items := {\"values\": [null]}")]
        [InlineData("for item in [null] {}")]
        [InlineData("import array\nitems := array.slice([null], 0, 1)")]
        public void RejectsArrayElementsWithoutAnInferableConcreteType(string source)
        {
            var diagnostics = new Compiler().Check(source);
            Assert.Contains(diagnostics, diagnostic => diagnostic.Code == "RUK2001");

            var result = Compile(source);
            Assert.False(result.Success);
            Assert.Null(result.Output);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK2001");
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("items: string?[] = [null]")]
        [InlineData("items: string?[]? = [null]")]
        [InlineData("items: string?[][] = [[null], []]")]
        [InlineData("items: dict[string, string?[]] = {\"values\": [null]}")]
        [InlineData("fn accept(items: string?[]): void {}\naccept([null])")]
        [InlineData("fn create(): string?[] { return [null] }")]
        [InlineData("struct Batch { items: string?[] }\nbatch := Batch { items: [null] }")]
        [InlineData("item: string? = null\nitems := [item, null]")]
        public void PreservesNullableElementTypesFromContextOrTypedValues(string source)
        {
            Assert.Empty(new Compiler().Check(source));
            Success(source);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "PowerShell51")]
        [Trait("Category", "CrossVersion")]
        [Fact]
        public async Task ContextualNullElementsExecuteOnBothPowerShellHosts()
        {
            using var project = new Project();
            project.Write("main.rukbat", """
                struct Batch {
                    items: string?[]
                }

                fn count_nulls(items: string?[]): int {
                    count := 0

                    for item in items {
                        if item == null {
                            count = count + 1
                        }
                    }

                    return count
                }

                fn create(): string?[] {
                    return [null]
                }

                direct: string?[] = [null]
                print(count_nulls(direct))
                print(count_nulls([null]))
                print(count_nulls(create()))
                batch := Batch {
                    items: [null]
                }
                print(count_nulls(batch.items))
                nested: string?[][] = [[null], [null, null]]

                for row in nested {
                    print(count_nulls(row))
                }

                item: string? = null
                inferred := [item, null]
                print(count_nulls(inferred))
                """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));

            foreach (var result in new[] { await project.RunOutputWithWindowsPowerShell(compiled.Output!),
                await project.RunOutput(compiled.Output!) })
            {
                Assert.True(result.ExitCode == 0, result.Error);
                Assert.Equal("1\n1\n1\n1\n1\n2\n2\n", result.Output);
                Assert.Equal("", result.Error);
            }
        }
    }
}
