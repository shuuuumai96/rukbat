namespace Rukbat.Tests;

public class InferenceTests : TestBase
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("process.Result", "process.run('powershell', ['-NoLogo', '-NoProfile', '-Command', '[Console]::Write(42); exit 7'], null)", "print(value.exit_code); print(value.stdout)")]
    [InlineData("Local", "make()", "print(value.number)")]
    [InlineData("remote.Item", "remote.make()", "print(value.number)")]
    [InlineData("remote.Item", "remote.Item {number: 42}", "print(value.number)")]
    [InlineData("remote.Item[]", "remote.many()", "print(value[0].number)")]
    [InlineData("remote.Item?", "remote.maybe()", "if value != null { print(value.number) }")]
    [InlineData("int", "integer()", "print(value)")]
    [InlineData("float", "decimal_value()", "print(value == 1.5)")]
    [InlineData("bool", "boolean()", "print(value)")]
    [InlineData("string", "message()", "print(value)")]
    [InlineData("int[]", "numbers()", "value.append(7); print(value[0]); print(value[1])")]
    public void InferencePreservesExpressionTypeAndMatchesExplicitEmission(
        string type, string expression, string use)
    {
        var (explicitOutput, inferredOutput) = CompileInferenceOutputs(type, expression, use);
        // The resolved type, including its defining module, must be preserved by inference.
        Assert.Equal(explicitOutput, inferredOutput);
    }

    [Trait("Category", "Runtime")]
    [Trait("Category", "PowerShell51")]
    [Theory]
    [InlineData("process.Result", "process.run('powershell', ['-NoLogo', '-NoProfile', '-Command', '[Console]::Write(42); exit 7'], null)", "print(value.exit_code); print(value.stdout)", "7\n42\n")]
    [InlineData("remote.Item", "remote.make()", "print(value.number)", "42\n")]
    [InlineData("remote.Item?", "remote.maybe()", "if value != null { print(value.number) }", "42\n")]
    [InlineData("int", "integer()", "print(value)", "42\n")]
    [InlineData("int[]", "numbers()", "value.append(7); print(value[0]); print(value[1])", "42\n7\n")]
    public async Task InferredRepresentativeValuesRunOnWindowsPowerShell51(
        string type, string expression, string use, string expected)
    {
        var (_, output) = CompileInferenceOutputs(type, expression, use);
        using var project = new Project();
        var result = await project.RunOutputWithWindowsPowerShell(output);
        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(expected, result.Output);
        Assert.Equal("", result.Error);
    }

    private static (string ExplicitOutput, string InferredOutput) CompileInferenceOutputs(
        string type, string expression, string use)
    {
        using var project = new Project();
        project.Write("model.rukbat", """
            struct Item { number: int }
            fn make(): Item { return Item {number: 42} }
            fn many(): Item[] { return [make()] }
            fn maybe(): Item? { return make() }
            """);
        const string declarations = """
            import process
            import "./model.rukbat" as remote
            struct Local { number: int }
            fn make(): Local { return Local {number: 42} }
            fn integer(): int { return 42 }
            fn decimal_value(): float { return 1.5 }
            fn boolean(): bool { return true }
            fn message(): string { return 'ok' }
            fn numbers(): int[] { return [42] }
            """;
        project.Write("main.rukbat", declarations + $"\nvalue: {type} = {expression}\n{use}");
        var explicitResult = project.Compile();
        Assert.True(explicitResult.Success, string.Join("\n", explicitResult.Diagnostics));
        project.Write("main.rukbat", declarations + $"\nvalue := {expression}\n{use}");
        var inferred = project.Compile();
        Assert.True(inferred.Success, string.Join("\n", inferred.Diagnostics));
        return (explicitResult.Output!, inferred.Output!);
    }

    [Trait("Category", "CrossVersion")]
    [Trait("Category", "Runtime")]
    [Fact]
    public async Task InferenceKeepsModuleIdentityAcrossAliasesCopiesAndNestedValues()
    {
        using var project = new Project();
        project.Write("model.rukbat", """
            struct Item { number: int; fn read(): int { return self.number } }
            fn make(): Item { return Item {number: 42} }
            """);
        project.Write("service.rukbat", """
            import "./model.rukbat" as model
            fn load(): model.Item { return model.make() }
            """);
        project.Write("main.rukbat", """
            import "./service.rukbat" as service
            import "./model.rukbat" as first
            import "./model.rukbat" as second
            struct Item { number: int }
            fn read(value: second.Item): int { return value.read() }
            fn local(): int {
                value := service.load()
                copy := value
                nested := [[copy]]
                item := nested[0][0]
                typed: first.Item = item
                return read(typed)
            }
            print(local())
            """);
        var compiled = project.Compile();
        Assert.True(compiled.Success, string.Join("\n", compiled.Diagnostics));
        foreach (var result in new[] { await project.RunOutputWithWindowsPowerShell(compiled.Output!),
            await project.RunOutput(compiled.Output!) })
        {
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal("42\n", result.Output);
        }
    }

    [Trait("Category", "Unit")]
    [Fact]
    public void InferenceDoesNotConfuseSameNamedStructsFromDifferentModules()
    {
        using var project = new Project();
        project.Write("left.rukbat", "struct Item { n: int }; fn make(): Item { return Item {n: 1} }");
        project.Write("right.rukbat", "struct Item { n: int }");
        project.Write("main.rukbat", """
            import "./left.rukbat" as left
            import "./right.rukbat" as right
            value := left.make()
            wrong: right.Item = value
            """);
        var error = Assert.Single(project.Compile().Diagnostics);
        Assert.Equal("RUK2001", error.Code);
        Assert.Equal(4, error.Line);
    }

    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("value := null")]
    [InlineData("value := []")]
    [InlineData("value := print('void')")]
    [InlineData("value := 1; value := 2")]
    [InlineData("value: Missing = 1")]
    public void InferenceStillRejectsUninferableOrInvalidDeclarations(string source)
        => Assert.NotEmpty(new Compiler().Check(source));
}
