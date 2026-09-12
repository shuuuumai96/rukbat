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
    private static CompilationResult CompileCore(string source, string path, bool resolveImports)
    {
        var loader = new ModuleLoader();
        loader.Load(source, path, resolveImports);
        var diagnostics = loader.Diagnostics.ToList();
        if (diagnostics.Count != 0) return new(null, diagnostics);
        var checker = new TypeChecker();
        checker.Check(loader.Ordered);
        diagnostics = loader.Diagnostics.ToList();
        if (diagnostics.Count != 0) return new(null, diagnostics);
        var output = new Emitter(checker).Emit(loader.Ordered);
        PowerShellParser.ParseInput(output, out _, out var errors);
        diagnostics.AddRange(errors.Select(e => new Diagnostic("AUV9000", $"Invalid generated PowerShell: {e.Message}", 1, 1, path)));
        return new(diagnostics.Count == 0 ? output : null, diagnostics);
    }
}
