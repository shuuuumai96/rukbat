using PowerShellParser = System.Management.Automation.Language.Parser;

namespace Rukbat;

public sealed class Compiler
{
    public CompilationResult Compile(string source) => CompileCore(source, "<input>", false);
    public CompilationResult CompileFile(string path) => CompileFile(path, debug: false);
    public CompilationResult CompileFile(string path, bool debug)
    {
        var fullPath = Path.GetFullPath(path);
        return CompileCore(File.ReadAllText(fullPath), fullPath, true, debug);
    }

    public IReadOnlyList<Diagnostic> Check(string source) => CheckCore(source, "<input>", false).Diagnostics;
    public IReadOnlyList<Diagnostic> CheckFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return CheckCore(File.ReadAllText(fullPath), fullPath, true).Diagnostics;
    }

    private static CompilationResult CompileCore(string source, string path, bool resolveImports, bool debug = false)
    {
        var checkedProgram = CheckCore(source, path, resolveImports);
        var diagnostics = checkedProgram.Diagnostics.ToList();
        if (diagnostics.Count != 0)
        {
            return new(null, diagnostics);
        }

        var map = debug ? new DebugMap() : null;
        var output = new Emitter(checkedProgram.Checker!, map).Emit(checkedProgram.Modules!);
        return ValidateOutput(output, path) with { DebugMap = map };
    }

    private static CompilationResult ValidateOutput(string output, string path)
    {
        // Validate generated syntax with the official PowerShell parser without executing it.
        var diagnostics = new List<Diagnostic>();
        PowerShellParser.ParseInput(output, out _, out var errors);
        diagnostics.AddRange(errors.Select(error => new Diagnostic(
            "RUK9000",
            $"Invalid generated PowerShell: {error.Message}",
            1,
            1,
            path)));
        return new(diagnostics.Count == 0 ? output : null, diagnostics);
    }

    internal TestCollection CollectTests(string path)
    {
        var program = CheckCore(File.ReadAllText(path), path, true);
        if (program.Diagnostics.Count != 0)
        {
            return new([], program.Diagnostics);
        }

        var entry = program.Modules![^1];
        var checker = program.Checker!;
        var tests = new List<CollectedTest>();
        foreach (var function in entry.Statements.OfType<Function>().Where(function => function.Name.StartsWith("test_", StringComparison.Ordinal)))
        {
            var symbol = checker.Functions[function];
            var validParameter = function.Parameters.Count == 0 || function.Parameters.Count == 1 &&
                checker.Variables[function.Parameters[0]].Type is { IsNullable: false, Element: null, Struct: { } structure } &&
                structure.Module.StandardName == "test" && structure.Declaration.Token.Text == "Context";
            if (symbol.ReturnType != ValueType.Void || !validParameter)
            {
                entry.Source.Error("RUK5000", "A test must return void and take either no parameters or one test.Context parameter.", function.Token);
                continue;
            }

            tests.Add(new(function.Name, path, (workingDirectory, completionPath) =>
                ValidateOutput(new Emitter(checker).Emit(program.Modules,
                    new TestInvocation(symbol, workingDirectory, completionPath)), path)));
        }
        return new(tests, entry.Source.Diagnostics);
    }

    internal static CheckedProgram CheckCore(string source, string path, bool resolveImports,
        Func<string, string>? readSource = null, bool collectSyntax = false)
    {
        // Stop after the language front end. `rukbat check` deliberately performs no lowering
        // or PowerShell generation, so it remains independent of backend behavior.
        var loader = new ModuleLoader(readSource, collectSyntax);
        loader.Load(source, path, resolveImports);
        var diagnostics = loader.Diagnostics.ToList();
        if (diagnostics.Count != 0)
        {
            return new(loader.Ordered, null, diagnostics);
        }

        var checker = new TypeChecker();
        checker.Check(loader.Ordered);
        diagnostics = loader.Diagnostics.ToList();
        return new(loader.Ordered, checker, diagnostics);
    }

    internal sealed record CheckedProgram(
        List<Module>? Modules,
        TypeChecker? Checker,
        IReadOnlyList<Diagnostic> Diagnostics);
}

internal sealed record TestInvocation(FunctionSymbol Function, string WorkingDirectory, string CompletionPath);
internal sealed record CollectedTest(string Name, string FilePath, Func<string, string, CompilationResult> Compile);
internal sealed record TestCollection(List<CollectedTest> Tests, IReadOnlyList<Diagnostic> Diagnostics);
