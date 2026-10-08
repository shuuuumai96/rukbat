namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitStatements(List<Statement> statements)
    {
        var outerDebugVariables = debugVariables;
        debugVariables = [.. debugVariables];
        foreach (var statement in statements)
        {
            if (statement is Import)
            {
                continue;
            }
            if (debugMap is not null && statement is While debugLoop)
            {
                // Put the checkpoint before every condition evaluation, including
                // the final false evaluation and iterations reached via continue.
                Line("while ($true) {");
                indentation++;
                DebugPoint(statement);
                Line($"$__rukbat_location = {Location(statement)}");
                Line($"if (-not ({Expr(debugLoop.Condition)})) {{ break }}");
                EmitStatements(debugLoop.Body);
                indentation--;
                Line("}");
                continue;
            }
            DebugPoint(statement);
            // A function has its own location variable, so calls cannot overwrite their caller's location.
            Line($"$__rukbat_location = {Location(statement)}");
            switch (statement)
            {
                case Variable variable:
                    var symbol = checker.Variables[variable];
                    Line($"[{TypeName(symbol.Type)}]{symbol.PowerShellName} = {Expr(variable.Value)}");
                    debugVariables.RemoveAll(existing => existing.Name == symbol.Name);
                    debugVariables.Insert(0, symbol);
                    break;
                case Assignment assignment:
                    Line($"{checker.Variables[assignment].PowerShellName} = {Expr(assignment.Value)}");
                    break;
                case FieldAssignment assignment:
                    Line($"{Address(assignment.Target)} = {Expr(assignment.Value)}");
                    break;
                case IndexAssignment assignment:
                    if (checker.Types[assignment.Target.Receiver].DictionaryKey is not null)
                        EmitDictionaryAssignment(assignment);
                    else
                        Line($"{Address(assignment.Target)} = {Expr(assignment.Value)}");
                    break;
                case Return result:
                    EmitReturn(result);
                    break;
                case Throw failure:
                    Line($"& $script:__rukbat_raise ({Expr(failure.Value)}) {Location(failure)}");
                    break;
                case TryCatch guarded:
                    Line("try {");
                    indentation++;
                    EmitStatements(guarded.Body);
                    indentation--;
                    Line("} catch {");
                    indentation++;
                    Line($"[hashtable]{checker.Variables[guarded.Handler].PowerShellName} = & $script:__rukbat_error $_ $__rukbat_location");
                    var beforeHandler = debugVariables;
                    debugVariables = [checker.Variables[guarded.Handler], .. debugVariables];
                    EmitStatements(guarded.Handler.Body);
                    debugVariables = beforeHandler;
                    indentation--;
                    Line("}");
                    break;
                case For loop:
                    var item = "$__rukbat_item" + temporaryId++;
                    Line($"foreach ({item} in {Expr(loop.Items)}) {{");
                    indentation++;
                    var loopVariable = checker.Variables[loop];
                    Line($"[{TypeName(loopVariable.Type)}]{loopVariable.PowerShellName} = {Clone(item, loopVariable.Type)}");
                    var beforeLoop = debugVariables;
                    debugVariables = [loopVariable, .. debugVariables];
                    EmitStatements(loop.Body);
                    debugVariables = beforeLoop;
                    indentation--;
                    Line("}");
                    break;
                case While loop:
                    Line($"while ({Expr(loop.Condition)}) {{");
                    indentation++;
                    EmitStatements(loop.Body);
                    indentation--;
                    Line("}");
                    break;
                case LoopControl control:
                    Line(control.Token.Text);
                    break;
                case ExpressionStatement expression:
                    // Suppress unused values to prevent PowerShell's implicit function output.
                    Line($"$null = {Expr(expression.Value)}");
                    break;
                case Conditional conditional:
                    Line($"if ({Expr(conditional.Condition)}) {{");
                    indentation++;
                    EmitStatements(conditional.Then);
                    indentation--;
                    Line("}");
                    if (conditional.Else.Count != 0)
                    {
                        Line("else {");
                        indentation++;
                        EmitStatements(conditional.Else);
                        indentation--;
                        Line("}");
                    }
                    break;
            }
        }
        debugVariables = outerDebugVariables;
    }

    private void EmitReturn(Return result)
    {
        if (result.Value is null)
        {
            Line("return");
            return;
        }

        // Unary comma returns an array as one value instead of letting PowerShell enumerate it.
        var value = Expr(result.Value);
        if (currentReturnType.IsNullable && currentReturnType.NonNullable.Element is not null)
        {
            var temporary = $"$__rukbat_return{temporaryId++}";
            Line($"{temporary} = {value}");
            Line($"if ($null -eq {temporary}) {{ return $null }}");
            Line($"return ,({temporary})");
        }
        else
        {
            Line(currentReturnType.Element is not null ? $"return ,({value})" : $"return {value}");
        }
    }
}
