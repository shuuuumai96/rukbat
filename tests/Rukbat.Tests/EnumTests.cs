namespace Rukbat.Tests
{
    public sealed class EnumTests : TestBase
    {
        [Trait("Category", "Unit")]
        [Theory]
        [InlineData("enum Empty {}", "RUK2001", "must declare at least one member")]
        [InlineData("enum State { ready; ready }", "RUK2002", "Duplicate member")]
        [InlineData("enum State { ready }; enum State { busy }", "RUK2002", "Duplicate enum")]
        [InlineData("struct State {}; enum State { ready }", "RUK2002", "Enum name 'State' conflicts")]
        [InlineData("enum int { ready }", "RUK2002", "Enum name 'int' conflicts")]
        [InlineData("enum State { ready }; State := 1", "RUK2002", "already an enum")]
        [InlineData("fn run() { enum State { ready } }", "RUK1002", "only at module level")]
        [InlineData("enum State { ready = 1 }", "RUK1000", "Expected a newline")]
        [InlineData("enum State { ready }; s := State.Ready", "RUK2004", "Unknown member 'Ready'")]
        [InlineData("enum State { ready }; s := State", "RUK2004", "Unknown variable 'State'")]
        [InlineData("enum State { ready }; s: State = 'ready'", "RUK2001", "Expected State")]
        [InlineData("enum State { ready }; s: State = 0", "RUK2001", "Expected State")]
        [InlineData("enum State { ready }; s := State.ready < State.ready", "RUK2001", "Operator '<'")]
        [InlineData("enum State { ready }; State.ready = State.ready", "RUK2010", "read-only")]
        [InlineData("enum State { ready }; impl State {}", "RUK2004", "must be a struct")]
        [InlineData("enum State { ready }; choices: dict[State, int] = {}", "RUK2001", "Dictionary keys")]
        [InlineData("enum State { ready }; s := powershell<State?>('Write-Output', 'ready')", "RUK2010", "cannot contain enums")]
        [InlineData("enum State { ready }; powershell<void>('Write-Output', [State.ready])", "RUK2010", "cannot be passed directly")]
        [InlineData("import text; s := text.parse_enum('ready')", "RUK2005", "explicit enum type")]
        [InlineData("import text; s := text.parse_enum<string>('ready')", "RUK2001", "requires a non-null enum type")]
        [InlineData("import text; s := text.from_enum('ready')", "RUK2001", "requires a non-null enum type")]
        public void RejectsInvalidDeclarationsAndUses(string source, string code, string message)
        {
            Assert.Contains(new Compiler().Check(source), diagnostic => diagnostic.Code == code && diagnostic.Message.Contains(message));
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void SameNamedEnumsFromDifferentModulesHaveDistinctTypes()
        {
            using var project = new Project();
            project.Write("first.rukbat", "enum State { ready }");
            project.Write("second.rukbat", "enum State { ready }");
            project.Write("main.rukbat", """
                import "./first.rukbat" as first
                import "./second.rukbat" as second

                state: first.State = second.State.ready
                equal := first.State.ready == second.State.ready
                """);

            var diagnostics = new Compiler().CheckFile(project.PathFor("main.rukbat"));
            Assert.Equal(2, diagnostics.Count);
            Assert.All(diagnostics, diagnostic => Assert.Equal("RUK2001", diagnostic.Code));
            Assert.Contains(diagnostics, diagnostic => diagnostic.Message.Contains("Expected first.State, got second.State"));
        }

        [Trait("Category", "Integration")]
        [Theory]
        [InlineData("pwsh")]
        [InlineData("powershell")]
        public async Task RunsTypedEnumsAcrossModulesAndValidatesTextAndJson(string shell)
        {
            if (shell == "powershell" && !OperatingSystem.IsWindows())
            {
                return;
            }

            using var project = new Project();
            project.Write("policy.rukbat", """
                fn select_policy(policy: CollisionPolicy): CollisionPolicy {
                    return policy
                }

                enum CollisionPolicy {
                    fail
                    skip
                    Skip
                    rename
                }
                """);
            project.Write("main.rukbat", """
                import "./policy.rukbat" as policy
                import json
                import text

                struct Config {
                    policy: policy.CollisionPolicy
                    fallback: policy.CollisionPolicy?
                }

                selected := text.parse_enum<policy.CollisionPolicy>(args()[0])
                config := Config { policy: policy.select_policy(selected), fallback: null }
                entries: dict[string, Config[]] = { "jobs": [config] }
                encoded := json.stringify(entries)
                print(encoded)
                decoded := json.parse_strict<dict[string, Config[]]>(encoded)
                fallback := decoded["jobs"][0].fallback
                print(fallback == null)
                print(decoded["jobs"][0].policy == policy.CollisionPolicy.skip)
                print(selected != policy.CollisionPolicy.Skip)
                print(text.from_enum(selected))
                print(selected)

                try {
                    text.parse_enum<policy.CollisionPolicy>(" SKIP ")
                } catch err {
                    print(err.message)
                }

                try {
                    json.parse<Config>('{"policy":"SKIP","fallback":null}')
                } catch err {
                    print(err.message)
                }
                """);

            var compiled = project.Compile();
            Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
            var result = shell == "powershell"
                ? await project.RunOutputWithWindowsPowerShell(compiled.Output!, "skip")
                : await project.RunOutput(compiled.Output!, "skip");
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("", result.Error);
            Assert.Equal("""
                {"jobs":[{"policy":"skip","fallback":null}]}
                True
                True
                True
                skip
                skip
                Unknown member of enum CollisionPolicy.
                $.policy: JSON value is incompatible with CollisionPolicy.

                """, result.Output);
        }

        [Trait("Category", "Unit")]
        [Fact]
        public void FormatsAndDocumentsEnumMembers()
        {
            using var project = new Project();
            project.Write("main.rukbat", "/// Choices.\nenum Mode{\n/// Leave it unchanged.\nskip;rename}\nselected:=Mode.skip");
            var path = project.PathFor("main.rukbat");
            Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, TextWriter.Null));
            var formatted = File.ReadAllText(path);
            Assert.Equal("/// Choices.\nenum Mode {\n    /// Leave it unchanged.\n    skip\n    rename\n}\n\nselected := Mode.skip\n", formatted);
            Assert.Equal(0, Cli.Run(["fmt", path], TextWriter.Null, TextWriter.Null));
            Assert.Equal(formatted, File.ReadAllText(path));
            Assert.Equal(0, Cli.Run(["doc", path], TextWriter.Null, TextWriter.Null));
            var html = File.ReadAllText(project.PathFor("main.html"));
            Assert.Contains("Mode.skip", html);
            Assert.Contains("Leave it unchanged.", html);
        }
    }

    public sealed partial class LanguageServerTests
    {
        [Fact]
        public void CompletesAndResolvesImportedEnumMembers()
        {
            using var project = new Project();
            project.Write("policy.rukbat", "enum Mode { skip; rename }");
            var uri = UriFor(project);
            const string source = "import './policy.rukbat' as policy\nvalue: policy.Mode = policy.Mode.skip\n";
            var messages = Session(Open(uri, source), Feature(uri, "definition", 1, 33, "definition"),
                Feature(uri, "hover", 1, 30, "hover"), Change(uri, source + "other := policy.Mode.\n"),
                Feature(uri, "completion", 2, 21, "completion"));
            Assert.Equal(UriFor(project, "policy.rukbat"), Result(messages, "definition").GetProperty("uri").GetString());
            Assert.Contains("enum Mode", Result(messages, "hover").ToString());
            var labels = Result(messages, "completion").EnumerateArray().Select(item => item.GetProperty("label").GetString());
            Assert.Equal(["rename", "skip"], labels);
        }
    }
}
