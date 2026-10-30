using Rukbat;

namespace Rukbat.Tests
{
    [Trait("Category", "Unit")]
    public sealed class ConstantTests : TestBase
    {
        [Theory]
        [InlineData("const count := 3; print(count)")]
        [InlineData("const count: int = 3; print(count)")]
        [InlineData("const message := '日本語'; print(message)")]
        [InlineData("fn configured(): int { return 3 }; const count := configured(); print(count)")]
        [InlineData("const arguments := args(); print(arguments.length)")]
        [InlineData("const items: int[] = []; copy := items; copy.append(1)")]
        [InlineData("const item: int? = null; print(item == null)")]
        [InlineData("const count := 1; fn f(): void { count := 2; count = 3 }; f(); print(count)")]
        [InlineData("const values := [1]; fn change(values: int[]): void { values[0] = 2; values.append(3) }; change(values)")]
        [InlineData("struct Item { value: int }; const items := [Item { value: 1 }]; for item in items { item.value = 2 }")]
        [InlineData("const value: int? = 1; fn f(): void {}; if value != null { f(); print(value + 1) }")]
        [InlineData("fn f(): void { if true { const value := 1; print(value) } }; f()")]
        public void AcceptsInitializedConstantsAndMutableValueCopies(string source) => Success(source);

        [Theory]
        [InlineData("const count := 1; count = 2")]
        [InlineData("const count := 1; fn change(): void { count = 2 }")]
        [InlineData("fn f(): void { const count := 1; if true { count = 2 } }")]
        [InlineData("const items := [1]; items[0] = 2")]
        [InlineData("const items := [1]; items.append(2)")]
        [InlineData("const items: int[]? = []; if items != null { items.append(1) }")]
        [InlineData("struct Item { value: int }; const item := Item { value: 1 }; item.value = 2")]
        [InlineData("struct Item { values: int[] }; const item := Item { values: [1] }; item.values[0] = 2")]
        [InlineData("struct Item { values: int[] }; const items := [Item { values: [1] }]; items[0].values.append(2)")]
        [InlineData("const items := [[1]]; (items)[0][0] = 2")]
        public void RejectsWritesThroughAnyConstantPath(string source)
        {
            var result = Compile(source);
            Assert.Null(result.Output);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK2011" && diagnostic.Message.Contains("const"));
        }

        private const string Counter = """
        struct Counter {
            value: int
            values: int[]
        }
        impl Counter {
            fn increment(): void { self.value = self.value + 1 }
            fn forward(): void { self.increment() }
            fn append(): void { self.values.append(1) }
            fn label(): int { return self.value }
            fn copied(): int { copy := self; copy.forward(); return copy.value }
            fn recursive(count: int): void {
                if count > 0 { self.recursive(count - 1) } else { self.forward() }
            }
            fn read_recursive(count: int): int {
                if count > 0 { return self.read_recursive(count - 1) } else { return self.label() }
            }
            fn argument_copy(): void { change(self) }
        }
        fn change(counter: Counter): void { counter.increment() }
        """;

        [Theory]
        [InlineData("counter.increment()")]
        [InlineData("counter.forward()")]
        [InlineData("counter.append()")]
        [InlineData("counter.recursive(2)")]
        [InlineData("print([counter.label(), counter.copied(), counter.label()]); counter.forward()")]
        public void RejectsDirectIndirectAndRecursiveMutatingMethods(string operation)
        {
            var result = Compile(Counter + "\nconst counter := Counter { value: 1, values: [] }; " + operation);
            Assert.Null(result.Output);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "RUK2011");
        }

        [Fact]
        public void AllowsReadOnlyMethodsAndMethodsThatMutateCopies()
        {
            Success(Counter + "\nconst counter := Counter { value: 1, values: [] }; print(counter.label()); print(counter.copied()); print(counter.read_recursive(2)); counter.argument_copy()");
            Success(Counter + "\nconst counters := [Counter { value: 1, values: [] }]; print(counters[0].label()); copy := counters; copy[0].forward()");
        }

        [Theory]
        [InlineData("self.inner.forward()")]
        [InlineData("self.items[0].append()")]
        [InlineData("if false { self.inner.forward() }")]
        [InlineData("try { self.items[0].forward() } catch err {}")]
        public void TracksNestedReceiversAndControlFlow(string body)
        {
            var source = Counter + "\nstruct Container { inner: Counter; items: Counter[] }; impl Container { fn change(): void { " + body + " } }\n" +
                "const container := Container { inner: Counter { value: 1, values: [] }, items: [Counter { value: 1, values: [] }] }; container.change()";
            Assert.Contains(Compile(source).Diagnostics, diagnostic => diagnostic.Code == "RUK2011");
        }

        [Fact]
        public void PropagatesMutationAcrossMutuallyRecursiveMethods()
        {
            var source = "struct Item { value: int }; impl Item { fn first(): void { self.second() }; fn second(): void { self.first(); self.value = 2 } }; const item := Item { value: 1 }; item.first()";
            Assert.Contains(Compile(source).Diagnostics, diagnostic => diagnostic.Code == "RUK2011");
        }

        [Fact]
        public void ReportsImportedMethodWritesInTheCallingSource()
        {
            using var project = new Project();
            project.Write("helper.rukbat", Counter + "\nfn create(): Counter { return Counter { value: 1, values: [] } }");
            project.Write("main.rukbat", "import './helper.rukbat' as helper\nconst counter := helper.create()\ncounter.forward()\n");
            var diagnostic = Assert.Single(project.Compile().Diagnostics);
            Assert.Equal("RUK2011", diagnostic.Code);
            Assert.Equal(project.PathFor("main.rukbat"), diagnostic.FilePath);
            Assert.Equal(3, diagnostic.Line);
            Assert.Equal(9, diagnostic.Column);
        }

        [Theory]
        [InlineData("const value", "RUK1000")]
        [InlineData("const value = 1", "RUK1000")]
        [InlineData("const value: int", "RUK1000")]
        [InlineData("const fn f(): void {}", "RUK1000")]
        [InlineData("fn f(const value: int): void {}", "RUK1000")]
        [InlineData("struct Item { const value: int }", "RUK1000")]
        [InlineData("const := 1", "RUK1000")]
        [InlineData("fn const(): void {}", "RUK1000")]
        [InlineData("const value := null", "RUK2001")]
        [InlineData("const items := []", "RUK2001")]
        [InlineData("const value: int = 'wrong'", "RUK2001")]
        [InlineData("const value := 1; const value := 2", "RUK2002")]
        [InlineData("const value := 1; value := 2", "RUK2002")]
        public void PreservesDeclarationAndTypeRules(string source, string code) =>
            Assert.Contains(Compile(source).Diagnostics, diagnostic => diagnostic.Code == code);

        [Fact]
        public void ConstChangesNoGeneratedRuntimeMechanism() =>
            Assert.Equal(Success("value := 3; print(value)"), Success("const value := 3; print(value)"));

        [Fact]
        public void CliDoesNotReplaceBuildOutputAfterAnInvalidWrite()
        {
            using var project = new Project();
            project.Write("main.rukbat", "const value := 1\nvalue = 2\n");
            project.Write("main.ps1", "approved output");
            foreach (var command in new[] { "check", "build", "run" })
            {
                using var errors = new StringWriter();
                Assert.Equal(1, Cli.Run([command, project.PathFor("main.rukbat")], TextWriter.Null, errors));
                Assert.Contains("RUK2011", errors.ToString());
                Assert.Equal("approved output", File.ReadAllText(project.PathFor("main.ps1")));
            }
        }
    }
}
