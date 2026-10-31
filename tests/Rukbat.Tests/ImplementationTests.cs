namespace Rukbat.Tests
{
    public sealed class ImplementationTests : TestBase
    {
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task CapturesUpdateReceiversBeforeEvaluatingValues(bool windowsPowerShell)
        {
            using var project = new Project();
            project.Write("main.rukbat", """
            struct Box { values: int[]; value: int }
            box := Box { values: [1], value: 1 }
            fn replace_box(): int {
                box = Box { values: [9], value: 9 }
                return 2
            }
            box.values.append(replace_box())
            print(box.values[0])
            print(box.values.length)
            box.value = replace_box()
            print(box.value)
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = windowsPowerShell
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("9\n1\n9\n", result.Output);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("struct Data { value: int }; impl Data { fn read(): int { return self.value } }; data := Data { value: 1 }; print(data.read())")]
        [InlineData("impl Data { fn read(): int { return self.value } }; struct Data { value: int }; data := Data { value: 1 }; print(data.read())")]
        [InlineData("struct Data {}; impl Data {}")]
        [InlineData("struct Data { value: int }; impl Data { fn first(): int { return self.second() }; fn second(): int { return self.value } }")]
        [InlineData("struct Data { value: int }; impl Data { fn read(): int { return self.value } }; print(Data { value: 1 }.read())")]
        public void AcceptsSeparateDataAndMethodDeclarations(string source) => Success(source);

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("struct Data { value: int; fn read(): int { return self.value } }", "RUK1000", "impl Data")]
        [InlineData("struct Data {}; impl Data { value: int }", "RUK1000", "only method declarations")]
        [InlineData("impl Missing {}", "RUK2004", "same module")]
        [InlineData("impl int {}", "RUK2004", "same module")]
        [InlineData("impl Error {}", "RUK2004", "same module")]
        [InlineData("struct Data {}; impl Data {}; impl Data {}", "RUK2002", "Duplicate impl")]
        [InlineData("struct Data {}; fn f(): void { impl Data {} }", "RUK1002", "only at module level")]
        [InlineData("struct Data {}; if true { impl Data {} }", "RUK1002", "only at module level")]
        [InlineData("struct Data { value: int }; impl Data { fn change(): Data { self.value = 2; return self } }; print(Data { value: 1 }.change().value)", "RUK2010", "temporary value")]
        [InlineData("impl := 1", "RUK1000", "identifier")]
        [InlineData("fn impl(): void {}", "RUK1000", "identifier")]
        public void RejectsInvalidImplementationDeclarations(string source, string code, string message)
        {
            var result = Compile(source);
            Assert.False(result.Success);
            Assert.Null(result.Output);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == code && diagnostic.Message.Contains(message, StringComparison.OrdinalIgnoreCase));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void RejectsImplementationsOfImportedTypes()
        {
            using var project = new Project();
            project.Write("model.rukbat", "struct Data { value: int }");
            project.Write("main.rukbat", "import './model.rukbat' as model\nimpl model {}\n");
            Assert.Contains(project.Compile().Diagnostics, diagnostic => diagnostic.Code == "RUK2004" && diagnostic.Message.Contains("same module"));
            project.Write("main.rukbat", "import './model.rukbat' as model\nimpl model.Data {}\n");
            Assert.Contains(project.Compile().Diagnostics, diagnostic => diagnostic.Code == "RUK1000");
        }

        [Trait("Category", "Runtime")]
        [Trait("Category", "PowerShell51")]
        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PreservesReceiverUpdatesValueCopiesAndPartialUpdatesBeforeErrorsOnBothHosts(bool windowsPowerShell)
        {
            using var project = new Project();
            project.Write("model.rukbat", """
            impl Counter {
                fn read(): int { return self.values[0] }
                fn increment(): void { self.values[0] = self.values[0] + 1 }
                fn twice(): void { self.increment(); self.increment() }
                fn fail(): void { self.increment(); throw Error { message: "failed" } }
            }
            struct Counter {
                values: int[]
            }
            fn change_copy(counter: Counter): Counter { counter.increment(); return counter }
            """);
            project.Write("main.rukbat", """
            import "./model.rukbat" as model
            struct Container {
                counters: model.Counter[]
            }
            original := Container { counters: [model.Counter { values: [1] }] }
            copy := original
            copy.counters[0].twice()
            print(original.counters[0].read())
            print(copy.counters[0].read())
            changed := model.change_copy(copy.counters[0])
            print(copy.counters[0].read())
            print(changed.read())
            try { copy.counters[0].fail() } catch err { print(err.message) }
            print(copy.counters[0].read())
            const snapshot := copy
            print(snapshot.counters[0].read())
            """);
            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            File.Delete(project.PathFor("main.rukbat"));
            File.Delete(project.PathFor("model.rukbat"));
            var result = windowsPowerShell
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!)
                : await project.RunOutput(compiled.Output!);
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("1\n3\n3\n4\nfailed\n4\n4\n", result.Output);
            Assert.Equal("", result.Error);
        }
    }
}
