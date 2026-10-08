namespace Rukbat;

public sealed record CompilationResult(string? Output, IReadOnlyList<Diagnostic> Diagnostics)
{
    public bool Success => Output is not null && Diagnostics.Count == 0;
    public DebugMap? DebugMap { get; init; }
}
