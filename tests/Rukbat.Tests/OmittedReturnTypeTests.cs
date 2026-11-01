namespace Rukbat.Tests
{
    public sealed class OmittedReturnTypeTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("fn f() {}")]
        [InlineData("fn f()\n{ return }")]
        [InlineData("fn f(): void {}")]
        [InlineData("fn f(): int { return 1 }")]
        [InlineData("fn f() { 1 }")]
        [InlineData("struct Item {}; impl Item { fn f() { return } }")]
        public void AcceptsVoidDefaultsAndExplicitResults(string source) => Success(source);

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("fn f() { return 1 }")]
        [InlineData("fn f() { return 'value' }")]
        [InlineData("fn f() {}; value := f()")]
        [InlineData("fn f() { 1 }; value: int = f()")]
        [InlineData("struct Item {}; impl Item { fn f() { return true } }")]
        public void DoesNotInferResultsFromTheBody(string source)
        {
            var result = Compile(source);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK2001");
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task ExecutesImportedMethodsRecursionAndEarlyReturns(bool windowsPowerShell)
        {
            using var project = new Project();
            project.Write("model.rukbat", """
            struct Counter { value: int }
            impl Counter {
                fn increment() { self.value = self.value + 1 }
            }
            fn visit(remaining: int) {
                if remaining == 0 { return }
                print(remaining)
                visit(remaining - 1)
            }
            """);
            project.Write("main.rukbat", """
            import "./model.rukbat" as model
            fn ignored() { 99 }
            counter := model.Counter { value: 1 }
            counter.increment()
            print(counter.value)
            model.visit(2)
            ignored()
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = windowsPowerShell
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("2\n2\n1\n", result.Output);
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("powershell")]
        [InlineData("pwsh")]
        public async Task DiscoversTestsWithoutReturnAnnotations(string shell)
        {
            using var project = new Project();
            project.Write("test_main.rukbat", """
            import test
            fn test_without_annotation() { test.assert(true) }
            fn test_with_context(context: test.Context) { test.assert(context.temp_dir != "") }
            """);
            var result = await RunCli(project, ["test", "--shell", shell]);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Contains("2 passed, 0 failed", result.Output);
        }
    }
}
