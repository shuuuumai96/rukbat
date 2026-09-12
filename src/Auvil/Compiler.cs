using System.Management.Automation.Language;

namespace Auvil;

public sealed class Compiler
{
    public CompilationResult Compile(string source)
    {
        var syntax = new Syntax(source);
        var ast = syntax.Parse();
        if (syntax.Diagnostics.Count == 0) new TypeChecker(syntax).Check(ast);
        if (syntax.Diagnostics.Count != 0) return new(null, syntax.Diagnostics);
        var output = Emitter.Emit(syntax);
        Parser.ParseInput(output, out _, out var errors);
        foreach (var error in errors)
            syntax.Error("AUV1003", $"Generated PowerShell is invalid: {error.Message}", 0);
        return new(syntax.Diagnostics.Count == 0 ? output : null, syntax.Diagnostics);
    }
}
