namespace Rukbat
{
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
                        {
                            EmitDictionaryAssignment(assignment);
                        }
                        else
                        {
                            Line($"{Address(assignment.Target)} = {Expr(assignment.Value)}");
                        }

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
                        if (checker.Iterations[loop].Kind == IterationKind.OwnedCursor)
                        {
                            EmitOwnedIteration(loop);
                            break;
                        }
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
            if (returnLabel is not null)
            {
                if (result.Value is not null)
                {
                    Line(currentReturnType == ValueType.Void ? $"$null = {Expr(result.Value)}" : $"{returnValue} = {Expr(result.Value)}");
                }

                Line($"break {returnLabel}");
                return;
            }
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

        private void EmitReturnValue(string value)
        {
            if (currentReturnType == ValueType.Void)
            {
                Line("return");
            }
            else if (currentReturnType.NonNullable.Element is not null)
            {
                if (currentReturnType.IsNullable)
                {
                    Line($"if ($null -eq {value}) {{ return $null }}");
                }

                Line($"return ,({value})");
            }
            else
            {
                Line($"return {value}");
            }
        }

        private bool ContainsOwnedIteration(IEnumerable<Statement> statements) => statements.Any(statement => statement switch
        {
            For loop => checker.Iterations[loop].Kind == IterationKind.OwnedCursor || ContainsOwnedIteration(loop.Body),
            While loop => ContainsOwnedIteration(loop.Body),
            Conditional conditional => ContainsOwnedIteration(conditional.Then) || ContainsOwnedIteration(conditional.Else),
            TryCatch guarded => ContainsOwnedIteration(guarded.Body) || ContainsOwnedIteration(guarded.Handler.Body),
            _ => false
        });

        private void EmitOwnedIteration(For loop)
        {
            var source = "$__rukbat_lines" + temporaryId++;
            var cursor = "$__rukbat_cursor" + temporaryId++;
            var primary = "$__rukbat_primary" + temporaryId++;
            var cleanup = "$__rukbat_cleanup" + temporaryId++;
            var variable = checker.Variables[loop];
            Line($"{source} = {Expr(loop.Items)}");
            Line($"{cursor} = $null");
            Line($"{primary} = $null");
            Line("try {");
            indentation++;
            Line($"{cursor} = & $script:__rukbat_standard {Quote(checker.Iterations[loop].OpenOperation!)} @({source})");
            Line("while ($true) {");
            indentation++;
            Line($"$__rukbat_location = {Location(loop)}");
            Line($"if (-not {cursor}.MoveNext()) {{ break }}");
            Line($"[string]{variable.PowerShellName} = {cursor}.Current");
            var before = debugVariables;
            debugVariables = [variable, .. debugVariables];
            EmitStatements(loop.Body);
            debugVariables = before;
            indentation--;
            Line("}");
            indentation--;
            Line("} catch {");
            indentation++;
            Line($"{primary} = & $script:__rukbat_error $_ $__rukbat_location");
            Line($"& $script:__rukbat_raise {primary} $__rukbat_location");
            indentation--;
            Line("} finally {");
            indentation++;
            Line("try {");
            indentation++;
            Line($"if ($null -ne {cursor}) {{ {cursor}.Close() }}");
            indentation--;
            Line("} catch {");
            indentation++;
            Line($"{cleanup} = & $script:__rukbat_error $_ {Location(loop)}");
            Line($"if ($null -ne {primary}) {{");
            Line($"    {cleanup} = @{{ message = {primary}.message + '; cleanup failed: ' + {cleanup}.message; __rukbat_location = {primary}.__rukbat_location }}");
            Line("}");
            Line($"& $script:__rukbat_raise {cleanup} {Location(loop)}");
            indentation--;
            Line("} finally {");
            Line($"    {cursor} = $null; {source} = $null; {primary} = $null; {cleanup} = $null");
            Line($"    {variable.PowerShellName} = ''");
            Line("}");
            indentation--;
            Line("}");
        }
    }
}
