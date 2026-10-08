namespace Rukbat;

internal sealed partial class Emitter
{
    private string DictionaryLiteral(DictionaryLiteral literal)
    {
        var name = "$__rukbat_dict" + temporaryId++;
        var keyType = checker.Types[literal].DictionaryKey!.Name;
        var entries = literal.Entries.Select(entry =>
            $"& $script:__rukbat_dictionary 'add' {name} ({Expr(entry.Key)}) ({Expr(entry.Value)}) {Location(entry.Key)} $script:__rukbat_raise;");
        return $"(& {{ {name} = & $script:__rukbat_dictionary 'new' $null {Quote(keyType)}; " +
            string.Join(" ", entries) + $" return {name} }})";
    }

    private void EmitDictionaryAssignment(IndexAssignment assignment)
    {
        // Capture the receiver and key before evaluating the RHS, exactly once.
        // Intermediate indexes use strict reads; only this terminal slot may insert.
        var receiver = "$__rukbat_dict" + temporaryId++;
        var key = "$__rukbat_key" + temporaryId++;
        Line($"{receiver} = {RawExpr(assignment.Target.Receiver)}");
        Line($"{key} = {Expr(assignment.Target.Offset)}");
        Line($"& $script:__rukbat_dictionary 'set' {receiver} {key} ({Expr(assignment.Value)}) {Location(assignment)} $script:__rukbat_raise");
    }
}
