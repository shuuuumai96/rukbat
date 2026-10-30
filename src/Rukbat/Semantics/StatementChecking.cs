namespace Rukbat
{
    internal sealed partial class TypeChecker
    {
        private void CheckStatements(List<Statement> statements, Symbols scope, ValueType? returnType, bool topLevel)
        {
            FlowState? terminalState = null;
            foreach (var statement in statements)
            {
                // Continue binding unreachable code for diagnostics and const analysis,
                // but do not let it change a reachable exit's nullable state.
                if (!reachable)
                {
                    terminalState ??= SnapshotFlow();
                }

                switch (statement)
                {
                    case Import import when !topLevel:
                        Error("RUK3000", "Imports are allowed only at module level.", import);
                        break;
                    case StructDeclaration structure:
                        Error("RUK1002", "Structs are allowed only at module level.", structure);
                        break;
                    case ImplDeclaration implementation:
                        Error("RUK1002", "Impl blocks are allowed only at module level.", implementation);
                        break;
                    case Function function:
                        Error("RUK1002", "Nested functions are not supported yet.", function);
                        break;
                    case Variable variable:
                        var declaredType = variable.IsInferred ? null : BindType(variable.Type, variable);
                        var actual = TypeOf(variable.Value, scope, declaredType);
                        if (variable.IsInferred && actual == ValueType.Null)
                        {
                            Error("RUK2001", "A null literal requires an explicit nullable type.", variable.Value);
                            actual = ValueType.Error;
                        }
                        Declare(variable, declaredType ?? actual, scope, topLevel);
                        Match(Variables[variable].Type, actual, variable.Value);
                        UpdateRefinement(Variables[variable], actual);
                        break;
                    case Assignment assignment:
                        var target = scope.Find(assignment.Token.Text);
                        if (target is null)
                        {
                            Error("RUK2004", $"Unknown variable '{assignment.Token.Text}'. Declare it with a type first.", assignment);
                        }
                        else
                        {
                            Variables[assignment] = target;
                            RequireWritable(target, assignment);
                            var assignedType = TypeOf(assignment.Value, scope, target.Type);
                            Match(target.Type, assignedType, assignment.Value);
                            UpdateRefinement(target, assignedType);
                        }
                        break;
                    case FieldAssignment assignment:
                        var fieldType = TypeOf(assignment.Target, scope);
                        RequireWritable(RootVariable(assignment.Target), assignment);
                        if (EncodingConstants.ContainsKey(assignment.Target) || HashConstants.ContainsKey(assignment.Target) || LogConstants.ContainsKey(assignment.Target) || HttpConstants.ContainsKey(assignment.Target) || ZoneConstants.ContainsKey(assignment.Target))
                        {
                            Error("RUK2010", "Standard-library constants are read-only.", assignment);
                        }
                        if (!IsVariablePath(assignment.Target))
                        {
                            Error("RUK2010", "Field assignment requires a variable, not a temporary value.", assignment);
                        }

                        if (assignment.Target.Receiver is { } receiver && Types.GetValueOrDefault(receiver) is { } receiverType &&
                            (receiverType.Element is not null || receiverType.DictionaryKey is not null))
                        {
                            Error("RUK2010", "Container length is read-only.", assignment);
                        }

                        Match(fieldType, TypeOf(assignment.Value, scope, fieldType), assignment.Value, $"field '{assignment.Target.Name}'");
                        break;
                    case IndexAssignment assignment:
                        var elementType = TypeOf(assignment.Target, scope);
                        RequireWritable(RootVariable(assignment.Target), assignment);
                        if (!IsVariablePath(assignment.Target))
                        {
                            Error("RUK2010", "Array assignment requires a variable, not a temporary value.", assignment);
                        }

                        Match(elementType, TypeOf(assignment.Value, scope, elementType), assignment.Value);
                        break;
                    case For loop:
                        var collectionType = TypeOf(loop.Items, scope);
                        var iteration = collectionType == ValueType.FileLines
                            ? new BoundIteration(IterationKind.OwnedCursor, ValueType.String, "fs.__open_lines")
                            : new BoundIteration(IterationKind.ArraySnapshot, collectionType.Element ?? ValueType.Error);
                        Iterations[loop] = iteration;
                        if (collectionType.Element is null && collectionType != ValueType.FileLines && collectionType != ValueType.Error)
                        {
                            Error("RUK2001", "for ... in requires an array or fs.Lines.", loop.Items);
                        }

                        var beforeLoop = SnapshotFlow();
                        InvalidateAssignedRefinements(loop.Body, scope);
                        if (ContainsUnqualifiedUserCall(loop.Body))
                        {
                            InvalidateGlobalRefinements();
                        }

                        var forFlow = CheckLoopBody(loop.Body, scope, returnType, SnapshotFlow(), loop);
                        // A for loop may execute zero times. Normal completion, continue,
                        // and break all reach the statement after an exhausted loop.
                        MergeFlows([beforeLoop, forFlow.End, .. forFlow.Breaks, .. forFlow.Continues]);
                        break;
                    case While loop:
                        var loopDiagnosticCount = module.Source.Diagnostics.Count;
                        var beforeCondition = SnapshotFlow();
                        Match(ValueType.Bool, TypeOf(loop.Condition, scope), loop.Condition);
                        InvalidateAssignedRefinements(loop.Body, scope);
                        if (ContainsUnqualifiedUserCall(loop.Body))
                        {
                            InvalidateGlobalRefinements();
                        }

                        var (whileSymbol, nonNullWhileTrue) = NullTestRefinement(loop.Condition);
                        var whileFlow = CheckLoopBody(loop.Body, scope, returnType, SnapshotFlow(),
                            nonNullCondition: nonNullWhileTrue ? whileSymbol : null);
                        // Only fallthrough and continue evaluate the condition again.
                        // Preserve initial narrowing when all returning paths restore it,
                        // or when a nullable write is followed by break/return instead.
                        MergeFlows([beforeCondition, whileFlow.End, .. whileFlow.Continues]);
                        if (module.Source.Diagnostics.Count == loopDiagnosticCount)
                        {
                            Match(ValueType.Bool, TypeOf(loop.Condition, scope), loop.Condition);
                        }

                        MergeFlows([SnapshotFlow(), .. whileFlow.Breaks]);
                        break;
                    case LoopControl control:
                        if (loops.Count == 0)
                        {
                            Error("RUK2010", $"{control.Token.Text} is allowed only inside a loop.", control);
                        }
                        else if (reachable)
                        {
                            var states = control.Token.Text == "break" ? loops.Peek().Breaks : loops.Peek().Continues;
                            states.Add(SnapshotFlow());
                        }
                        reachable = false;
                        break;
                    case Return result:
                        var resultType = result.Value is null ? ValueType.Void : TypeOf(result.Value, scope, returnType);
                        if (returnType is null)
                        {
                            Error("RUK2003", "return is allowed only inside a function.", result);
                        }
                        else
                        {
                            Match(returnType, resultType, (Node?)result.Value ?? result);
                        }
                        reachable = false;
                        break;
                    case Throw failure:
                        Match(ErrorStructure.BoundType, TypeOf(failure.Value, scope), failure.Value, "throw");
                        reachable = false;
                        break;
                    case TryCatch guarded:
                        var beforeTry = SnapshotFlow();
                        CheckStatements(guarded.Body, new Symbols(scope), returnType, false);
                        var afterTry = SnapshotFlow();
                        RestoreFlow(beforeTry);
                        InvalidateAssignedRefinements(guarded.Body, scope);
                        if (ContainsCurrentModuleCall(guarded.Body))
                        {
                            InvalidateGlobalRefinements();
                        }
                        var handlerScope = new Symbols(scope);
                        Declare(guarded.Handler, ErrorStructure.BoundType, handlerScope, false);
                        CheckStatements(guarded.Handler.Body, handlerScope, returnType, false);
                        MergeFlows(afterTry, SnapshotFlow());
                        break;
                    case Conditional conditional:
                        Match(ValueType.Bool, TypeOf(conditional.Condition, scope), conditional.Condition);
                        CheckConditional(conditional, scope, returnType);
                        break;
                    case ExpressionStatement expression:
                        TypeOf(expression.Value, scope);
                        if (expression.Value is Call { Receiver: null, Name: "exit", PowerShellType: null })
                        {
                            reachable = false;
                        }

                        break;
                }
            }
            if (terminalState is not null)
            {
                RestoreFlow(terminalState);
            }
        }
    }
}
