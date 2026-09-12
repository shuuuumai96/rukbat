namespace Auvil;

internal static class Emitter
{
    public static string Emit(Syntax syntax)
    {
        var variableEdits = syntax.Variables.SelectMany(a => new[]
        {
            new Edit(a.VariableOffset, a.VariableOffset, $"[{a.Type.ToString().ToLowerInvariant()}]"),
            new Edit(a.Start, a.End, "")
        }).ToList();
        var edits = new List<Edit>(variableEdits);
        foreach (var annotation in syntax.Returns)
        {
            var start = annotation.OpenParen + 1;
            var end = annotation.CloseParen;
            var parameters = Syntax.Apply(syntax.Source[start..end],
                variableEdits.Where(e => e.Start >= start && e.End <= end), start);
            edits.RemoveAll(e => e.Start >= annotation.OpenParen && e.End <= annotation.Brace + 1);
            // Keep header comments/trivia, masking only the return annotation.
            var trivia = Syntax.Apply(syntax.Source[(end + 1)..annotation.Brace],
                [new Edit(annotation.Start, annotation.End, "")], end + 1);
            edits.Add(new(annotation.OpenParen, annotation.Brace + 1,
                $"{trivia}{{\n    [OutputType([{annotation.Type.ToString().ToLowerInvariant()}])]\n    param({parameters})\n"));
        }
        return Syntax.Apply(syntax.Source, edits);
    }
}
