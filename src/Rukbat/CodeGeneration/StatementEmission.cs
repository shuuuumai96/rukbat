namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private static readonly NodeHandlers<Emitter> StatementEmitters = new(
            NodeHandlers<Emitter>.For<Variable>(static (emitter, node) => emitter.EmitVariable(node)),
            NodeHandlers<Emitter>.For<Assignment>(static (emitter, node) => emitter.EmitAssignment(node)),
            NodeHandlers<Emitter>.For<FieldAssignment>(static (emitter, node) => emitter.Line($"{emitter.Address(node.Target)} = {emitter.Expr(node.Value)}")),
            NodeHandlers<Emitter>.For<IndexAssignment>(static (emitter, node) => emitter.EmitIndexAssignment(node)),
            NodeHandlers<Emitter>.For<Return>(static (emitter, node) => emitter.EmitReturn(node)),
            NodeHandlers<Emitter>.For<Throw>(static (emitter, node) => emitter.Line($"& $script:__rukbat_raise ({emitter.Expr(node.Value)}) {emitter.Location(node)}")),
            NodeHandlers<Emitter>.For<TryCatch>(static (emitter, node) => emitter.EmitTryCatch(node)),
            NodeHandlers<Emitter>.For<For>(static (emitter, node) => emitter.EmitFor(node)),
            NodeHandlers<Emitter>.For<While>(static (emitter, node) => emitter.EmitWhile(node)),
            NodeHandlers<Emitter>.For<LoopControl>(static (emitter, node) => emitter.Line(node.Token.Text)),
            NodeHandlers<Emitter>.For<ExpressionStatement>(static (emitter, node) => emitter.EmitExpressionStatement(node)),
            NodeHandlers<Emitter>.For<Conditional>(static (emitter, node) => emitter.EmitConditional(node)));

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
                StatementEmitters.Dispatch(this, statement);
            }
            debugVariables = outerDebugVariables;
        }

        private static readonly IReadOnlyDictionary<IterationKind, Action<Emitter, For>> IterationEmitters =
            new Dictionary<IterationKind, Action<Emitter, For>>
            {
                [IterationKind.ArraySnapshot] = static (emitter, loop) => emitter.EmitArrayIteration(loop),
                [IterationKind.OwnedCursor] = static (emitter, loop) => emitter.EmitOwnedIteration(loop)
            };

        private void EmitFor(For loop) => IterationEmitters[checker.Iterations[loop].Kind](this, loop);

        private void EmitAssignment(Assignment assignment) =>
            Line($"{checker.Variables[assignment].PowerShellName} = {Expr(assignment.Value)}");

        private void EmitVariable(Variable variable)
        {
            var symbol = checker.Variables[variable];
            Line($"[{TypeName(symbol.Type)}]{symbol.PowerShellName} = {Expr(variable.Value)}");
            debugVariables.RemoveAll(existing => existing.Name == symbol.Name);
            debugVariables.Insert(0, symbol);
        }

        private void EmitIndexAssignment(IndexAssignment assignment)
        {
            if (checker.Types[assignment.Target.Receiver].DictionaryKey is not null)
            {
                EmitDictionaryAssignment(assignment);
            }
            else
            {
                Line($"{Address(assignment.Target)} = {Expr(assignment.Value)}");
            }
        }

        private void EmitTryCatch(TryCatch guarded)
        {
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
        }

        private void EmitArrayIteration(For loop)
        {
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
        }

        private void EmitWhile(While loop)
        {
            Line($"while ({Expr(loop.Condition)}) {{");
            indentation++;
            EmitStatements(loop.Body);
            indentation--;
            Line("}");
        }

        private void EmitExpressionStatement(ExpressionStatement expression)
        {
            // Suppress unused values to prevent PowerShell's implicit function output.
            Line($"$null = {Expr(expression.Value)}");
        }

        private void EmitConditional(Conditional conditional)
        {
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

            EmitReturnValue(Expr(result.Value), direct: true);
        }

        private enum ReturnShape { Void, Scalar, Array, NullableArray }

        private static readonly IReadOnlyDictionary<ReturnShape, Action<Emitter, string, bool>> ReturnEmitters =
            new Dictionary<ReturnShape, Action<Emitter, string, bool>>
            {
                [ReturnShape.Void] = static (emitter, _, _) => emitter.Line("return"),
                [ReturnShape.Scalar] = static (emitter, value, _) => emitter.Line($"return {value}"),
                [ReturnShape.Array] = static (emitter, value, _) => emitter.Line($"return ,({value})"),
                [ReturnShape.NullableArray] = static (emitter, value, stabilize) => emitter.EmitNullableArrayReturn(value, stabilize)
            };

        private void EmitReturnValue(string value, bool direct = false)
        {
            var shape = currentReturnType switch
            {
                var type when type == ValueType.Void && !direct => ReturnShape.Void,
                { NullableElement.Kind: ValueKind.Array } => ReturnShape.NullableArray,
                { Kind: ValueKind.Array } => ReturnShape.Array,
                _ => ReturnShape.Scalar
            };
            ReturnEmitters[shape](this, value, direct);
        }

        private void EmitNullableArrayReturn(string value, bool stabilize)
        {
            // Preserve successful null returns and prevent PowerShell from enumerating an array.
            if (stabilize)
            {
                var temporary = $"$__rukbat_return{temporaryId++}";
                Line($"{temporary} = {value}");
                value = temporary;
            }

            Line($"if ($null -eq {value}) {{ return $null }}");
            Line($"return ,({value})");
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
            var source = "$__rukbat_source" + temporaryId++;
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
            Line($"[{TypeName(variable.Type)}]{variable.PowerShellName} = {cursor}.Current");
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
            Line($"    {cleanup} = @{{ message = {primary}.message + '; cleanup failed: ' + {cleanup}.message; __rukbat_location = {primary}.__rukbat_location; __rukbat_error_kind = {primary}.__rukbat_error_kind }}");
            Line("}");
            Line($"& $script:__rukbat_raise {cleanup} {Location(loop)}");
            indentation--;
            Line("} finally {");
            Line($"    {cursor} = $null; {source} = $null; {primary} = $null; {cleanup} = $null");
            var reset = variable.Type == ValueType.Bool ? "$false" : "$null";
            Line($"    {variable.PowerShellName} = {reset}");
            Line("}");
            indentation--;
            Line("}");
        }
    }
}
