namespace Rukbat;

// Optional syntax facts for formatting. The parser remains the single authority
// for distinguishing statement boundaries from expression continuations.
internal sealed class FormattingSyntax
{
    public HashSet<int> ContinuationNewlines { get; } = [];
    public HashSet<int> ExpressionClosings { get; } = [];
    public HashSet<int> GenericDelimiters { get; } = [];
    public HashSet<int> UnaryOperators { get; } = [];
    public HashSet<int> ExpressionOpenings { get; } = [];
    public HashSet<int> RequiredBlankLines { get; } = [];

    public void SeparateStatements(Statement? previous, Statement next, int offset)
    {
        if (previous is not null &&
            (previous is Import && next is not Import ||
             previous is not Import && next is Import ||
             IsCompound(previous) || IsCompound(next)))
        {
            RequiredBlankLines.Add(offset);
        }
    }

    private static bool IsCompound(Statement statement) =>
        statement is Function or StructDeclaration or ImplDeclaration or Conditional or For or While or TryCatch;
}
