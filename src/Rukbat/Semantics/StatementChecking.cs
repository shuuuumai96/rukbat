namespace Rukbat
{
    internal sealed partial class TypeChecker
    {
        private readonly record struct StatementContext(TypeChecker Checker, Symbols Scope, ValueType? ReturnType, bool TopLevel);

        private static readonly NodeHandlers<StatementContext> StatementChecks = new(
            NodeHandlers<StatementContext>.For<Import>(static (context, node) => context.Checker.CheckImport(node, context.TopLevel)),
            NodeHandlers<StatementContext>.For<StructDeclaration>(static (context, node) => context.Checker.Error(DiagnosticCode.InvalidDeclarationScope, "Structs are allowed only at module level.", node)),
            NodeHandlers<StatementContext>.For<EnumDeclaration>(static (context, node) => context.Checker.Error(DiagnosticCode.InvalidDeclarationScope, "Enums are allowed only at module level.", node)),
            NodeHandlers<StatementContext>.For<ImplDeclaration>(static (context, node) => context.Checker.Error(DiagnosticCode.InvalidDeclarationScope, "Impl blocks are allowed only at module level.", node)),
            NodeHandlers<StatementContext>.For<Function>(static (context, node) => context.Checker.Error(DiagnosticCode.InvalidDeclarationScope, "Nested functions are not supported yet.", node)),
            NodeHandlers<StatementContext>.For<Variable>(static (context, node) => context.Checker.CheckVariable(node, context.Scope, context.TopLevel)),
            NodeHandlers<StatementContext>.For<Assignment>(static (context, node) => context.Checker.CheckAssignment(node, context.Scope)),
            NodeHandlers<StatementContext>.For<FieldAssignment>(static (context, node) => context.Checker.CheckFieldAssignment(node, context.Scope)),
            NodeHandlers<StatementContext>.For<IndexAssignment>(static (context, node) => context.Checker.CheckIndexAssignment(node, context.Scope)),
            NodeHandlers<StatementContext>.For<For>(static (context, node) => context.Checker.CheckFor(node, context.Scope, context.ReturnType)),
            NodeHandlers<StatementContext>.For<While>(static (context, node) => context.Checker.CheckWhile(node, context.Scope, context.ReturnType)),
            NodeHandlers<StatementContext>.For<LoopControl>(static (context, node) => context.Checker.CheckLoopControl(node)),
            NodeHandlers<StatementContext>.For<Return>(static (context, node) => context.Checker.CheckReturn(node, context.Scope, context.ReturnType)),
            NodeHandlers<StatementContext>.For<Throw>(static (context, node) => context.Checker.CheckThrow(node, context.Scope)),
            NodeHandlers<StatementContext>.For<TryCatch>(static (context, node) => context.Checker.CheckTryCatch(node, context.Scope, context.ReturnType)),
            NodeHandlers<StatementContext>.For<Conditional>(static (context, node) => context.Checker.CheckConditionalStatement(node, context.Scope, context.ReturnType)),
            NodeHandlers<StatementContext>.For<ExpressionStatement>(static (context, node) => context.Checker.CheckExpressionStatement(node, context.Scope)));

        private void CheckStatements(List<Statement> statements, Symbols scope, ValueType? returnType, bool topLevel)
        {
            var context = new StatementContext(this, scope, returnType, topLevel);
            FlowState? terminalState = null;
            foreach (var statement in statements)
            {
                // Continue binding unreachable code for diagnostics and const analysis,
                // but do not let it change a reachable exit's nullable state.
                if (!reachable)
                {
                    terminalState ??= SnapshotFlow();
                }

                StatementChecks.Dispatch(context, statement);
            }
            if (terminalState is not null)
            {
                RestoreFlow(terminalState);
            }
        }

        private void CheckImport(Import import, bool topLevel)
        {
            if (!topLevel)
            {
                Error(DiagnosticCode.InvalidImport, "Imports are allowed only at module level.", import);
            }
        }

        private void CheckVariable(Variable variable, Symbols scope, bool topLevel)
        {
            var declaredType = variable.IsInferred ? null : BindType(variable.Type, variable);
            var actual = TypeOf(variable.Value, scope, declaredType);
            if (variable.IsInferred && actual == ValueType.Null)
            {
                Error(DiagnosticCode.InvalidType, "A null literal requires an explicit nullable type.", variable.Value);
                actual = ValueType.Error;
            }
            Declare(variable, declaredType ?? actual, scope, topLevel);
            Match(Variables[variable].Type, actual, variable.Value);
            UpdateRefinement(Variables[variable], actual);
        }

        private void CheckAssignment(Assignment assignment, Symbols scope)
        {
            var target = scope.Find(assignment.Token.Text);
            if (target is null)
            {
                Error(DiagnosticCode.UnknownName, $"Unknown variable '{assignment.Token.Text}'. Declare it with a type first.", assignment);
            }
            else
            {
                Variables[assignment] = target;
                RequireWritable(target, assignment);
                var assignedType = TypeOf(assignment.Value, scope, target.Type);
                Match(target.Type, assignedType, assignment.Value);
                UpdateRefinement(target, assignedType);
            }
        }

        private void CheckFieldAssignment(FieldAssignment assignment, Symbols scope)
        {
            var fieldType = TypeOf(assignment.Target, scope);
            RequireWritable(RootVariable(assignment.Target), assignment);
            var member = Members.GetValueOrDefault(assignment.Target);
            if (member.Kind == MemberKind.StandardConstant)
            {
                Error(DiagnosticCode.InvalidOperation, "Standard-library constants are read-only.", assignment);
            }
            if (member.Kind == MemberKind.EnumMember)
            {
                Error(DiagnosticCode.InvalidOperation, "Enum members are read-only.", assignment);
            }
            if (!IsVariablePath(assignment.Target))
            {
                Error(DiagnosticCode.InvalidOperation, "Field assignment requires a variable, not a temporary value.", assignment);
            }

            if (assignment.Target.Receiver is { } receiver && Types.GetValueOrDefault(receiver) is { } receiverType &&
                (receiverType.Element is not null || receiverType.DictionaryKey is not null))
            {
                Error(DiagnosticCode.InvalidOperation, "Container length is read-only.", assignment);
            }

            Match(fieldType, TypeOf(assignment.Value, scope, fieldType), assignment.Value, $"field '{assignment.Target.Name}'");
        }

        private void CheckIndexAssignment(IndexAssignment assignment, Symbols scope)
        {
            var elementType = TypeOf(assignment.Target, scope);
            RequireWritable(RootVariable(assignment.Target), assignment);
            if (!IsVariablePath(assignment.Target))
            {
                Error(DiagnosticCode.InvalidOperation, "Array assignment requires a variable, not a temporary value.", assignment);
            }

            Match(elementType, TypeOf(assignment.Value, scope, elementType), assignment.Value);
        }

        private void CheckFor(For loop, Symbols scope, ValueType? returnType)
        {
            var collectionType = TypeOf(loop.Items, scope);
            var iteration = !collectionType.IsComposite && ownedTraversals.TryGetValue(collectionType, out var traversal)
                ? traversal : new BoundIteration(IterationKind.ArraySnapshot, collectionType.Element ?? ValueType.Error);
            Iterations[loop] = iteration;
            if (iteration.Kind == IterationKind.ArraySnapshot && collectionType.Element is null && collectionType != ValueType.Error)
            {
                Error(DiagnosticCode.InvalidType, "for ... in requires an array or a standard traversal descriptor.", loop.Items);
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
        }

        private void CheckWhile(While loop, Symbols scope, ValueType? returnType)
        {
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
        }

        private void CheckLoopControl(LoopControl control)
        {
            if (loops.Count == 0)
            {
                Error(DiagnosticCode.InvalidOperation, $"{control.Token.Text} is allowed only inside a loop.", control);
            }
            else if (reachable)
            {
                var states = control.Token.Text == "break" ? loops.Peek().Breaks : loops.Peek().Continues;
                states.Add(SnapshotFlow());
            }
            reachable = false;
        }

        private void CheckReturn(Return result, Symbols scope, ValueType? returnType)
        {
            var resultType = result.Value is null ? ValueType.Void : TypeOf(result.Value, scope, returnType);
            if (returnType is null)
            {
                Error(DiagnosticCode.InvalidReturn, "return is allowed only inside a function.", result);
            }
            else
            {
                Match(returnType, resultType, (Node?)result.Value ?? result);
            }
            reachable = false;
        }

        private void CheckThrow(Throw failure, Symbols scope)
        {
            Match(ErrorStructure.BoundType, TypeOf(failure.Value, scope), failure.Value, "throw");
            reachable = false;
        }

        private void CheckTryCatch(TryCatch guarded, Symbols scope, ValueType? returnType)
        {
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
        }

        private void CheckConditionalStatement(Conditional conditional, Symbols scope, ValueType? returnType)
        {
            Match(ValueType.Bool, TypeOf(conditional.Condition, scope), conditional.Condition);
            CheckConditional(conditional, scope, returnType);
        }

        private void CheckExpressionStatement(ExpressionStatement expression, Symbols scope)
        {
            TypeOf(expression.Value, scope);
            if (expression.Value is Call call && Calls.GetValueOrDefault(call).Kind == CallKind.Exit)
            {
                reachable = false;
            }
        }
    }
}
