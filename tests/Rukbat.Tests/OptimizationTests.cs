namespace Rukbat.Tests
{
    public class OptimizationTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Fact]
        public void SkipsEmptyStandardLibraryModules()
        {
            var output = Success("""
            import http
            import encoding
            import json
            import text
            print(text.contains("rukbat", "ruk"))
            """);
            var moduleCount = output.Split("New-Module -AsCustomObject").Length - 1;
            Assert.Equal(1, moduleCount);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void EliminatesRedundantClonesForFreshReadOnlyAndImmutableValues()
        {
            var output = Success("""
            import csv

            struct Item {
                id: int
                tags: string[]
            }

            fn read_id(item: Item): int {
                return item.id
            }

            fn make_item(id: int): Item {
                return Item { id: id, tags: ["a", "b"] }
            }

            const items := [Item { id: 1, tags: ["x"] }]
            total := 0
            for item in items {
                total = total + read_id(item)
            }
            fresh := make_item(total)
            row := csv.row(["k"], ["v"])
            print(csv.get(row, "k"))
            print(fresh.id)
            """);
            Assert.DoesNotContain("& $script:__rukbat_clone", output);
        }

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task LazyLoadsCodePageCodecAndHttpOnlyWhenNeeded(bool windowsPowerShell)
        {
            using var project = new Project();
            var output = Success("""
            import encoding
            import http
            import text

            enc := encoding.parse("utf-8")
            print(encoding.name(enc))
            print(powershell<bool>('Invoke-Expression', "$null -ne ('RukbatCodePageCodec' -as [type])"))
            print(powershell<bool>('Invoke-Expression', "$null -ne ('RukbatHttp' -as [type])"))
            if false {
                req := http.request(http.method_get, "https://example.com")
                print(req.url)
                print(text.upper("never"))
            }
            print(powershell<bool>('Invoke-Expression', "$null -ne ('RukbatText' -as [type])"))
            """);
            var result = windowsPowerShell
                ? await project.RunOutputWithWindowsPowerShell(output)
                : await project.RunOutput(output);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("utf-8\nFalse\nFalse\nFalse\n", result.Output);
            Assert.Empty(result.Error);
        }

        [Trait("Category", "CrossVersion")]
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task PreservesValueSemanticsUnderCloneOptimization(bool windowsPowerShell)
        {
            using var project = new Project();
            var output = Success("""
            struct Box {
                values: int[]
                meta: dict[string, int]
            }

            impl Box {
                fn merge(other: Box) {
                    self.values.append(99)
                    self.values.append(other.values.length)
                }
            }

            const original := [
                Box { values: [1, 2], meta: {"a": 10} }
            ]

            // 1. Mutating loop variable over const array must not mutate original
            for b in original {
                b.values.append(3)
                b.meta["a"] = 20
            }
            print(original[0].values.length)
            print(original[0].meta["a"])

            // 2. Copying non-mutated loop variable and mutating the copy must not mutate original
            for b in original {
                copy := b
                copy.values[0] = 999
                copy.meta["b"] = 30
            }
            print(original[0].values[0])
            print(original[0].meta.has("b"))

            // 3. Mutating method receiving self as argument (aliasing)
            a := Box { values: [1, 2], meta: {"a": 1} }
            a.merge(a)
            print(a.values.length)
            print(a.values[3])

            // 4. Global mutated during read-only parameter call must not affect parameter snapshot
            g := Box { values: [7], meta: {"k": 1} }
            fn sneaky(x: Box): int {
                g.values[0] = 88
                return x.values[0]
            }
            print(sneaky(g))
            print(g.values[0])

            // 5. Mutating array during foreach must not affect snapshot
            xs := [10, 20]
            seen := 0
            for v in xs {
                seen = seen + v
                xs.append(30)
            }
            print(seen)
            print(xs.length)
            """);
            var result = windowsPowerShell
                ? await project.RunOutputWithWindowsPowerShell(output)
                : await project.RunOutput(output);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal("2\n10\n1\nFalse\n4\n2\n7\n88\n30\n4\n", result.Output);
            Assert.Empty(result.Error);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void ConstViolationDiagnosticIsPreserved()
        {
            var diagnostics = new Compiler().Check("""
            struct Item { name: string }
            const item := Item { name: "a" }
            item.name = "b"
            """);
            Assert.Contains(diagnostics, d => d.Code == "RUK2011");
        }
    }
}
