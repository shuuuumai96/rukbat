using Rukbat;

namespace Rukbat.Tests
{
    [Trait("Category", "Runtime")]
    [Trait("Category", "PowerShell51")]
    [Trait("Category", "CrossVersion")]
    public sealed class ConstantRuntimeTests : TestBase
    {
        [Fact]
        [Trait("Smoke", "PowerShell51")]
        public async Task ConstantsInitializeOnceAndCopiesStayMutableOnBothHosts()
        {
            using var project = new Project();
            project.Write("helper.rukbat", """
            struct Counter {
                values: int[]
            }
            impl Counter {
                fn add(): void { self.values.append(2) }
                fn count(): int { return self.values.length }
                fn copied_count(): int { copy := self; copy.add(); return copy.count() }
            }
            fn create(): Counter { print("initialize"); return Counter { values: [1] } }
            fn changed(counter: Counter): Counter { counter.add(); return counter }
            """);
            project.Write("main.rukbat", """
            import "./helper.rukbat" as helper
            const original := helper.create()
            copy := original
            copy.add()
            print(original.count())
            print(copy.count())
            print(original.copied_count())
            const changed := helper.changed(original)
            print(changed.count())
            print(original.count())
            const matrix := [[1]]
            matrix_copy := matrix
            matrix_copy[0][0] = 9
            print(matrix[0][0])
            print(matrix_copy[0][0])
            const arguments := args()
            print(arguments[0])
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.Delete(project.PathFor("main.rukbat"));
            File.Delete(project.PathFor("helper.rukbat"));
            foreach (var result in new[] { await project.RunOutputWithWindowsPowerShell(compiled.Output!, "argument"),
                await project.RunOutput(compiled.Output!, "argument") })
            {
                Assert.Equal(0, result.ExitCode);
                Assert.Equal("initialize\n1\n2\n2\n2\n1\n1\n9\nargument\n", result.Output);
                Assert.Equal("", result.Error);
            }
        }
    }
}
