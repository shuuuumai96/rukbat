namespace Rukbat.Tests
{
    public class ImportedTextArgumentTests : TestBase
    {
        private const string HelperSource = """
            history := ""

            fn string_value(marker: string, value: string): string {
                history = history + marker
                return value
            }

            fn int_value(marker: string, value: int): int {
                history = history + marker
                return value
            }

            fn trace(): string {
                return history
            }
            """;

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell", false)]
        [InlineData("powershell", true)]
        [InlineData("pwsh", false)]
        [InlineData("pwsh", true)]
        public async Task ImportedResultsReachEveryNativeTextArgumentSlot(string shell, bool debug)
        {
            using var project = new Project();
            project.Write("helper.rukbat", HelperSource);
            project.Write("main.rukbat", """
            import text
            import test
            import "./helper.rukbat" as helper

            test.assert(text.length(helper.string_value("", "A😀B")) == 3)
            test.assert(text.length(helper.string_value("", "")) == 0)
            test.assert(text.slice(helper.string_value("", "A😀B"), 1, 1) == "😀")
            test.assert(text.slice("A😀B", helper.int_value("", 1), 1) == "😀")
            test.assert(text.slice("A😀B", 1, helper.int_value("", 1)) == "😀")
            test.assert(text.slice(helper.string_value("", ""), 0, 0) == "")

            found_value := text.index_of(helper.string_value("", "A😀B"), "B")

            if found_value != null {
                test.assert(found_value == 2)
            } else {
                test.fail("Missing match in an imported value")
            }

            found_search := text.index_of("A😀B", helper.string_value("", "😀"))

            if found_search != null {
                test.assert(found_search == 1)
            } else {
                test.fail("Missing match for an imported search")
            }

            test.assert(text.index_of("abc", helper.string_value("", "missing")) == null)
            test.assert(text.upper(helper.string_value("", "aBc")) == "ABC")
            test.assert(text.lower(helper.string_value("", "aBc")) == "abc")
            print("passed")
            """);

            await RunVerified(project, shell, debug);
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData("powershell", false)]
        [InlineData("powershell", true)]
        [InlineData("pwsh", false)]
        [InlineData("pwsh", true)]
        public async Task ImportedTextArgumentsEvaluateOnceInWrittenOrder(string shell, bool debug)
        {
            using var project = new Project();
            project.Write("helper.rukbat", HelperSource);
            project.Write("main.rukbat", """
            import text
            import test
            import "./helper.rukbat" as helper

            test.assert(text.length(helper.string_value("l", "abc")) == 3)
            test.assert(text.slice(helper.string_value("v", "abc"), helper.int_value("s", 1), helper.int_value("c", 1)) == "b")
            found := text.index_of(helper.string_value("i", "abc"), helper.string_value("n", "b"))

            if found != null {
                test.assert(found == 1)
            } else {
                test.fail("Missing match")
            }

            test.assert(text.upper(helper.string_value("u", "abc")) == "ABC")
            test.assert(text.lower(helper.string_value("d", "ABC")) == "abc")
            test.assert(helper.trace() == "lvscinud")
            print("passed")
            """);

            await RunVerified(project, shell, debug);
        }

        private static async Task RunVerified(Project project, string shell, bool debug)
        {
            var compiled = new Compiler().CompileFile(project.PathFor("main.rukbat"), debug);
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Empty(result.Error);
            Assert.Equal("passed\n", result.Output);
        }
    }
}
