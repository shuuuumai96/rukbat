namespace Rukbat.Tests
{
    public sealed class DictionaryTests : TestBase
    {
        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("d: dict[string, int] = {}; d['b'] = 2; d['A'] = 3; d['a'] = 4; d[''] = 5; d['b'] = 9; print(d.length); print(d['b']); print(d.has('A')); print(d.has('missing')); d.remove('missing'); d.remove('b'); d['b'] = 6; for k in d.keys() { print(k) }", "4\n9\nTrue\nFalse\nA\na\n\nb\n")]
        [InlineData("d := {-9223372036854775808: 'min', 9223372036854775807: 'max', -1: 'negative'}; print(d[-1]); for k in d.keys() { print(k) }; print(d[-9223372036854775808])", "negative\n-9223372036854775808\n9223372036854775807\n-1\nmin\n")]
        [InlineData("key := 'actual'; d := {key: 1, 'length': 2, 'entries': 3, '__rukbat_dictionary': 4}; print(d[key]); print(d['length']); print(d.length)", "1\n2\n4\n")]
        [InlineData("dict := {'é': 1, 'é': 2, 'I': 3, 'ı': 4}; print(dict.length); print(dict['é']); print(dict['é']); print(dict.has('i'))", "4\n1\n2\nFalse\n")]
        [InlineData("d: dict[string, int?] = {'x': null}; print(d.has('x')); n := d['x']; print(n == null); d.remove('x'); print(d.has('x')); try { n = d['x'] } catch e { print(e.message) }", "True\nTrue\nFalse\nDictionary has no key 'x'.\n")]
        [InlineData("d: dict[string, int]? = {'x': 1}; if d != null { d['y'] = 2; print(d.length); print(d['y']) }; d = null; print(d == null)", "2\n2\nTrue\n")]
        [InlineData("struct C { values: int[] }; impl C { fn grow(): void { self.values.append(2) } }; d := {'a': C {values: [1]}}; b := d; b['a'].grow(); b['a'].values[0] = 9; v := b['a']; v.values.append(7); print(d['a'].values.length); print(b['a'].values.length); print(b['a'].values[0]); print(v.values.length)", "1\n2\n9\n3\n")]
        [InlineData("fn f(d: dict[string, int[]]): dict[string, int[]] { d['x'].append(2); return d }; a := {'x': [1]}; b := f(a); c := [b]; c[0]['x'].append(3); print(a['x'].length); print(b['x'].length); print(c[0]['x'].length)", "1\n2\n3\n")]
        [InlineData("d: dict[string, dict[int, int[]]] = {'x': {1: [2]}}; d['x'][1].append(3); d['x'][2] = []; print(d['x'][1][1]); print(d['x'][2].length)", "3\n0\n")]
        [InlineData("d := {'a': 1, 'b': 2}; keys := d.keys(); keys[0] = 'z'; for k in d.keys() { d.remove(k); d['c'] = 3; print(k) }; print(d.length); print(keys[0])", "a\nb\n1\nz\n")]
        [InlineData("calls := ''; fn key(k: string): string { calls = calls + k; return k }; fn value(): int { calls = calls + 'V'; return 1 }; d := {key('a'): value(), key('b'): value()}; d[key('a')] = value(); print(calls); print(d.length)", "aVbVaV\n2\n")]
        [InlineData("calls := 0; fn key(): string { calls = calls + 1; return 'x' }; d := {'x': [[1]]}; d[key()][0].append(2); d[key()][0][0] = 3; print(calls); print(d['x'][0][0]); print(d['x'][0].length)", "2\n3\n2\n")]
        [InlineData("fn empty(): dict[string, int] { return {} }; fn count(d: dict[string, int]): int { return d.length }; print(empty().length); print(count({})); d: dict[string, float] = {'a': 1.0}; print(d['a'])", "0\n0\n1\n")]
        [InlineData("import datetime; d := {'x': datetime.parse_date('2026-10-07')}; print(datetime.format_date(d['x'], 'yyyy-MM-dd'))", "2026-10-07\n")]
        public async Task ExecutesOnBothPowerShellTargets(string source, string expected)
        {
            using var project = new Project();
            var output = Success(source);
            var modern = await project.RunOutput(output);
            Assert.True(modern.ExitCode == 0, modern.Error);
            Assert.Equal(expected, modern.Output);
            if (!OperatingSystem.IsWindows())
            {
                return;
            }

            var windows = await project.RunOutputWithWindowsPowerShell(output);
            Assert.True(windows.ExitCode == 0, windows.Error);
            Assert.Equal(expected, windows.Output);
        }

        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("d := {}", "empty dictionary")]
        [InlineData("d: dict[bool, int] = {}", "keys must")]
        [InlineData("d: dict[string?, int] = {}", "keys must")]
        [InlineData("d := {1.0: 2}", "keys must")]
        [InlineData("d: dict[string, void] = {}", "cannot have type void")]
        [InlineData("d := {'x': null}", "storable type")]
        [InlineData("d := {'x': print(1)}", "storable type")]
        [InlineData("d := {'x': 1, 'x': 2}", "Duplicate dictionary key")]
        [InlineData("d := {-1: 1, -1: 2}", "Duplicate dictionary key")]
        [InlineData("d := {+1: 1, 1: 2}", "Duplicate dictionary key")]
        [InlineData("d := {'x': 1, 'y': 2.0}", "Expected int")]
        [InlineData("d := {'x': 1}; print(d[1])", "dictionary key")]
        [InlineData("d := {'x': 1}; d.has(1)", "dictionary key")]
        [InlineData("d := {'x': 1}; d.remove(1)", "dictionary key")]
        [InlineData("d := {'x': 1}; d.keys(1)", "0 positional")]
        [InlineData("d := {'x': 1}; d.has()", "1 positional")]
        [InlineData("d := {'x': 1}; d.has(key: 'x')", "positional")]
        [InlineData("d := {'x': 1}; d.get('x')", "Unknown method")]
        [InlineData("d := {'x': 1}; d.length = 2", "read-only")]
        [InlineData("const d := {'x': [1]}; d['x'].append(2)", "const")]
        [InlineData("const d := {'x': 1}; d['y'] = 2", "const")]
        [InlineData("const d := {'x': 1}; d.remove('x')", "const")]
        [InlineData("struct C { n: int }; impl C { fn set(): void { self.n = 2 } }; const d := {'x': C {n: 1}}; d['x'].set()", "const")]
        [InlineData("struct C { d: dict[string, int] }; impl C { fn remove(): void { self.d.remove('x') } }; const c := C {d: {'x': 1}}; c.remove()", "const")]
        [InlineData("fn f(): dict[string, int] { return {} }; f().remove('x')", "temporary")]
        [InlineData("fn f(): dict[string, int] { return {} }; f()['x'] = 1", "temporary")]
        [InlineData("struct C { n: int }; impl C { fn set(): void { self.n = 2 } }; fn f(): dict[string, C] { return {'x': C {n: 1}} }; f()['x'].set()", "temporary")]
        [InlineData("import datetime; import json; d := {'x': datetime.parse_date('2026-10-07')}; print(json.stringify(d))", "JSON")]
        [InlineData("import json; values: dict[string, int?] = {'x': 1}; typed: dict[string, int] = values", "Expected dict")]
        [InlineData("d := {'x': 1}; print(d)", "composite")]
        [InlineData("d := {'x': 1}; print(d == d)", "cannot combine")]
        [InlineData("d := {'x': 1}; for k in d {}", "requires an array")]
        [InlineData("d: dict[string, int]? = null; print(d.length)", "Cannot access")]
        [InlineData("a := {'x': 1}; b: dict[string, float] = a", "Expected dict")]
        [InlineData("struct dict {}", "conflicts")]
        [InlineData("struct Node { nodes: dict[string, Node] }", "Recursive value")]
        [InlineData("powershell<dict[string, int]>('Get-Item')", "PowerShell")]
        [InlineData("powershell<void>('Write-Output', {'x': 1})", "cannot be passed directly")]
        [InlineData("import json; print(json.stringify({1: 2}))", "JSON")]
        [InlineData("import json; struct S { d: dict[int, int] }; print(json.stringify(S {d: {1: 2}}))", "JSON")]
        public void RejectsInvalidDictionaries(string source, string message)
        {
            var result = Compile(source);
            Assert.False(result.Success);
            Assert.Contains(result.Diagnostics, d => d.Message.Contains(message, StringComparison.OrdinalIgnoreCase));
        }

        [Trait("Category", "Runtime")]
        [Theory]
        [InlineData("key := 'a'; d := {key: 1, key: 2}", "Duplicate dictionary key")]
        [InlineData("d: dict[string, int] = {}; print(d['missing'])", "has no key")]
        [InlineData("d: dict[string, int[]] = {}; d['x'].append(1)", "has no key")]
        [InlineData("d: dict[string, dict[string, int]] = {}; d['x']['y'] = 1", "has no key")]
        public async Task ReportsAbsentAndDynamicDuplicateKeys(string source, string message)
        {
            using var project = new Project();
            var output = Success(source);
            foreach (var result in new[] { await project.RunOutput(output), await project.RunOutputWithWindowsPowerShell(output) })
            {
                Assert.NotEqual(0, result.ExitCode);
                Assert.Contains(message, result.Error);
                Assert.Contains("<input>:1", result.Error);
            }
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task JsonUsesStrictValuesAndOrdinalKeysRecursively()
        {
            const string source = "import json; d := {'z': [1], 'A': [2], 'a': [3]}; print(json.stringify(d)); p := json.parse<dict[string, int[]]>(json.stringify(d)); for k in p.keys() { print(k) }; q := json.as<dict[string, int[]]>(json.parse(json.stringify(d))); q['A'].append(4); print(q['A'].length); j := json.set(json.parse('{}'), 'data', d); print(json.stringify(j)); try { bad := json.parse<dict[string, int]>('{\"x\":1.5}') } catch e { print('invalid') }; try { bad := json.parse<dict[string, int]>('[]') } catch e { print('object required') }";
            const string expected = "{\"A\":[2],\"a\":[3],\"z\":[1]}\nA\na\nz\n2\n{\"data\":{\"A\":[2],\"a\":[3],\"z\":[1]}}\ninvalid\nobject required\n";
            await ExecutesOnBothPowerShellTargets(source, expected);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void LookupHelpersBorrowTheDictionaryAndDoNotCloneIt()
        {
            var output = Success("d := {'a': [1]}; print(d.has('a')); print(d.length); d.remove('a'); d['a'] = [2]; d['a'].append(3); keys := d.keys()");
            var calls = output.Split('\n').Where(line => line.Contains("$script:__rukbat_dictionary", StringComparison.Ordinal));
            Assert.DoesNotContain(calls, line => line.Contains("'has' (& $script:__rukbat_clone", StringComparison.Ordinal));
            Assert.DoesNotContain(calls, line => line.Contains("'get' (& $script:__rukbat_clone", StringComparison.Ordinal));
            foreach (var operation in new[] { "has", "remove", "keys", "entry" })
            {
                Assert.Matches("'" + operation + @"' \(\$script:__rukbat_v\d+\)", output);
            }

            Assert.Contains("Dictionary[string,object]", output);
            Assert.Contains("LinkedList[object]", output);
            Assert.DoesNotContain("Add-Type", output);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void DictionaryValueIdentityRetainsTheDefiningModule()
        {
            using var project = new Project();
            project.Write("left.rukbat", "struct Item { n: int }; fn empty(): dict[string, Item] { return {} }");
            project.Write("right.rukbat", "struct Item { n: int }");
            project.Write("main.rukbat", "import './left.rukbat' as left; import './right.rukbat' as right; a := left.empty(); b: dict[string, right.Item] = a");
            Assert.Contains(project.Compile().Diagnostics, diagnostic => diagnostic.Code == "RUK2001" && diagnostic.Message.Contains("dict[string, left.Item]") && diagnostic.Message.Contains("dict[string, right.Item]"));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void MultilineDictionaryTypesWorkInDeclarationsAndTypedCalls()
        {
            Success("import json; d: dict[\nstring\n,\nint\n] = {}; p := json.parse<dict[\nstring\n,\nint\n]>('{}')");
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task CounterBuildsAndUpdatesThousandsOfKeys()
        {
            await ExecutesOnBothPowerShellTargets("d: dict[int, int] = {}; i := 0; while i < 2000 { d[i] = 1; i = i + 1 }; for k in d.keys() { d[k] = d[k] + 1 }; print(d.length); print(d[1999])", "2000\n2\n");
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task DynamicDuplicatesEvaluateValuesBeforeRaisingWithoutRollingBackEffects()
        {
            await ExecutesOnBothPowerShellTargets("events := ''; fn key(): string { events = events + 'K'; return 'x' }; fn value(): int { events = events + 'V'; return 1 }; try { d := {key(): value(), key(): value()} } catch e { print(events); print(e.message) }", "KVKV\nDuplicate dictionary key 'x'.\n");
        }

        [Trait("Category", "Runtime")]
        [Fact]
        public async Task GeneratedCsvCounterRunsWithoutItsSourceOrCompiler()
        {
            var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
            var result = new Rukbat.Compiler().CompileFile(Path.Combine(root, "examples/csv-count/main.rukbat"));
            Assert.True(result.Success, string.Join("\n", result.Diagnostics));
            using var project = new Project();
            project.Write("rows.csv", File.ReadAllText(Path.Combine(root, "examples/csv-count/rows.csv")));
            // Only the generated script and its CSV input are placed in the deployment directory.
            Assert.Empty(Directory.GetFiles(project.DirectoryPath, "*.rukbat"));
            foreach (var run in new[] { await project.RunOutput(result.Output!, project.PathFor("rows.csv")), await project.RunOutputWithWindowsPowerShell(result.Output!, project.PathFor("rows.csv")) })
            {
                Assert.True(run.ExitCode == 0, run.Error);
                Assert.Equal("Errors: 2\nInformation: 1\nWarnings: 1\n", run.Output);
            }
        }
    }
}
