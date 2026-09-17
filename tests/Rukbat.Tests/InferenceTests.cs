namespace Rukbat.Tests;

public partial class CompilerTests
{
    [Theory]
    [InlineData("process.Result", "process.run('powershell', ['-NoLogo', '-NoProfile', '-Command', '[Console]::Write(42); exit 7'])", "print(value.exit_code); print(value.stdout)", "7\n42\n")]
    [InlineData("Local", "make()", "print(value.number)", "42\n")]
    [InlineData("remote.Item", "remote.make()", "print(value.number)", "42\n")]
    [InlineData("remote.Item", "remote.Item(number: 42)", "print(value.number)", "42\n")]
    [InlineData("remote.Item[]", "remote.many()", "print(value[0].number)", "42\n")]
    [InlineData("remote.Item?", "remote.maybe()", "if value != null { print(value.number) }", "42\n")]
    [InlineData("int", "integer()", "print(value)", "42\n")]
    [InlineData("float", "decimal_value()", "print(value == 1.5)", "True\n")]
    [InlineData("bool", "boolean()", "print(value)", "True\n")]
    [InlineData("string", "message()", "print(value)", "ok\n")]
    [InlineData("int[]", "numbers()", "value.append(7); print(value[0]); print(value[1])", "42\n7\n")]
    public async Task InferencePreservesExpressionTypeAndMatchesExplicitEmission(
        string type, string expression, string use, string expected)
    {
        using var project = new Project();
        project.Write("model.rukbat", """
            struct Item { number: int }
            fn make(): Item { return Item(number: 42) }
            fn many(): Item[] { return [make()] }
            fn maybe(): Item? { return make() }
            """);
        const string declarations = """
            import process
            import "./model.rukbat" as remote
            struct Local { number: int }
            fn make(): Local { return Local(number: 42) }
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
        // Type annotations must not change code generation or runtime value representation.
        Assert.Equal(explicitResult.Output, inferred.Output);
        foreach (var result in new[] { await project.RunOutputWithWindowsPowerShell(inferred.Output!),
            await project.RunOutput(inferred.Output!) })
        {
            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal(expected, result.Output);
            Assert.Equal("", result.Error);
        }
    }

    [Fact]
    public async Task InferenceKeepsModuleIdentityAcrossAliasesCopiesAndNestedValues()
    {
        using var project = new Project();
        project.Write("model.rukbat", """
            struct Item { number: int; fn read(): int { return self.number } }
            fn make(): Item { return Item(number: 42) }
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

    [Fact]
    public void InferenceDoesNotConfuseSameNamedStructsFromDifferentModules()
    {
        using var project = new Project();
        project.Write("left.rukbat", "struct Item { n: int }; fn make(): Item { return Item(n: 1) }");
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

    [Theory]
    [InlineData("value := null")]
    [InlineData("value := []")]
    [InlineData("value := print('void')")]
    [InlineData("value := 1; value := 2")]
    [InlineData("value: Missing = 1")]
    public void InferenceStillRejectsUninferableOrInvalidDeclarations(string source)
        => Assert.NotEmpty(new Compiler().Check(source));
}
