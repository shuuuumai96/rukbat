using PowerShellParser = System.Management.Automation.Language.Parser;

namespace Auvil;

public sealed class Compiler
{
    public CompilationResult Compile(string source) => CompileCore(source, "<input>", false);
    public CompilationResult CompileFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return CompileCore(File.ReadAllText(fullPath), fullPath, true);
    }

    public IReadOnlyList<Diagnostic> Check(string source) => CheckCore(source, "<input>", false).Diagnostics;
    public IReadOnlyList<Diagnostic> CheckFile(string path)
    {
        var fullPath = Path.GetFullPath(path);
        return CheckCore(File.ReadAllText(fullPath), fullPath, true).Diagnostics;
    }

    private static CompilationResult CompileCore(string source, string path, bool resolveImports)
    {
        var checkedProgram = CheckCore(source, path, resolveImports);
        var diagnostics = checkedProgram.Diagnostics.ToList();
        if (diagnostics.Count != 0)
        {
            return new(null, diagnostics);
        }

        // Validate generated syntax with the official PowerShell parser without executing it.
        var output = new Emitter(checkedProgram.Checker!).Emit(checkedProgram.Modules!);
        PowerShellParser.ParseInput(output, out _, out var errors);
        diagnostics.AddRange(errors.Select(error => new Diagnostic(
            "AUV9000",
            $"Invalid generated PowerShell: {error.Message}",
            1,
            1,
            path)));
        return new(diagnostics.Count == 0 ? output : null, diagnostics);
    }

    private static CheckedProgram CheckCore(string source, string path, bool resolveImports)
    {
        // Stop after the language front end. `auvil check` deliberately performs no lowering
        // or PowerShell generation, so it remains independent of backend behavior.
        var loader = new ModuleLoader();
        loader.Load(source, path, resolveImports);
        var diagnostics = loader.Diagnostics.ToList();
        if (diagnostics.Count != 0)
        {
            return new(null, null, diagnostics);
        }

        var checker = new TypeChecker();
        checker.Check(loader.Ordered);
        diagnostics = loader.Diagnostics.ToList();
        return new(loader.Ordered, checker, diagnostics);
    }

    private sealed record CheckedProgram(
        List<Module>? Modules,
        TypeChecker? Checker,
        IReadOnlyList<Diagnostic> Diagnostics);
}
