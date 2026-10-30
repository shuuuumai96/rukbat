namespace Rukbat;

internal sealed partial class TypeChecker
{
    private sealed record FlowState(bool Reachable, Dictionary<VariableSymbol, ValueType> Refinements);

    private sealed class LoopFlow
    {
        public List<FlowState> Breaks { get; } = [];
        public List<FlowState> Continues { get; } = [];
        public FlowState End { get; set; } = null!;
    }

    private FlowState SnapshotFlow() => new(reachable, SnapshotRefinements());

    private void RestoreFlow(FlowState state)
    {
        reachable = state.Reachable;
        refinements = SnapshotRefinements(state.Refinements);
    }

    private void MergeFlows(params FlowState[] states)
    {
        var continuing = states.Where(state => state.Reachable).ToArray();
        reachable = continuing.Length != 0;
        // Retain useful types for diagnostics in unreachable code without making
        // any terminated path reachable again.
        var checkedStates = reachable ? continuing : states;
        MergeRefinements(checkedStates.Select(state => state.Refinements).ToArray());
    }

    private void CheckConditional(Conditional conditional, Symbols scope, ValueType? returnType)
    {
        var baseline = SnapshotFlow();
        var (symbol, nonNullWhenTrue) = NullTestRefinement(conditional.Condition);

        RestoreFlow(baseline);
        if (symbol is not null && nonNullWhenTrue)
        {
            refinements[symbol] = symbol.Type.NonNullable;
        }
        CheckStatements(conditional.Then, new Symbols(scope), returnType, false);
        var afterThen = SnapshotFlow();

        RestoreFlow(baseline);
        if (symbol is not null && !nonNullWhenTrue)
        {
            refinements[symbol] = symbol.Type.NonNullable;
        }
        CheckStatements(conditional.Else, new Symbols(scope), returnType, false);
        MergeFlows(afterThen, SnapshotFlow());
    }

    private void InvalidateAssignedRefinements(List<Statement> body, Symbols scope)
    {
        foreach (var name in AssignedNames(body))
            if (scope.Find(name) is { } assigned)
                refinements.Remove(assigned);
    }

    private bool ContainsCurrentModuleCall(List<Statement> body) => NestedStatements(body)
        .SelectMany(StatementExpressions).SelectMany(Expressions).OfType<Call>()
        .Any(call => Calls.TryGetValue(call, out var target) && target.Module == module);

    private LoopFlow CheckLoopBody(List<Statement> body, Symbols scope, ValueType? returnType,
        FlowState baseline, For? iteration = null, VariableSymbol? nonNullCondition = null)
    {
        if (reboundLoopBodies.Contains(body))
        {
            // An enclosing loop may bind this body again. Its bound calls and
            // backedge already required global invalidation, so start there
            // instead of recursively repeating nested loop bodies once more.
            RestoreFlow(baseline);
            InvalidateGlobalRefinements();
            baseline = SnapshotFlow();
        }
        var diagnosticCount = module.Source.Diagnostics.Count;
        var result = CheckBody(baseline);
        if (module.Source.Diagnostics.Count == diagnosticCount &&
            (result.End.Reachable || result.Continues.Count != 0) &&
            baseline.Refinements.Keys.Any(symbol => symbol.IsGlobal && !symbol.IsConst) &&
            ContainsCurrentModuleCall(body))
        {
            // Binding the first pass identifies method owners without duplicating type
            // inference in a syntax pre-scan. One repeat suffices: all mutable global
            // refinements are absent at the next iteration's entry, regardless of calls.
            reboundLoopBodies.Add(body);
            RestoreFlow(baseline);
            InvalidateGlobalRefinements();
            result = CheckBody(SnapshotFlow());
        }
        return result;

        LoopFlow CheckBody(FlowState entry)
        {
            RestoreFlow(entry);
            if (nonNullCondition is not null)
                refinements[nonNullCondition] = nonNullCondition.Type.NonNullable;
            var locals = new Symbols(scope);
            if (iteration is not null)
                Declare(iteration, Iterations[iteration].ElementType, locals, false);
            var flow = new LoopFlow();
            loops.Push(flow);
            CheckStatements(body, locals, returnType, false);
            loops.Pop();
            flow.End = SnapshotFlow();
            return flow;
        }
    }

    private static IEnumerable<string> AssignedNames(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            if (statement is Assignment assignment)
            {
                yield return assignment.Token.Text;
            }
            var nested = statement switch
            {
                Conditional conditional => conditional.Then.Concat(conditional.Else),
                For loop => loop.Body,
                While loop => loop.Body,
                TryCatch guarded => guarded.Body.Concat(guarded.Handler.Body),
                _ => []
            };
            foreach (var name in AssignedNames(nested))
            {
                yield return name;
            }
        }
    }

    private static bool ContainsUnqualifiedUserCall(IEnumerable<Statement> statements) => statements.Any(statement =>
        StatementExpressions(statement).Any(ContainsUnqualifiedUserCall) || statement switch
        {
            Conditional conditional => ContainsUnqualifiedUserCall(conditional.Then) || ContainsUnqualifiedUserCall(conditional.Else),
            For loop => ContainsUnqualifiedUserCall(loop.Body),
            While loop => ContainsUnqualifiedUserCall(loop.Body),
            TryCatch guarded => ContainsUnqualifiedUserCall(guarded.Body) || ContainsUnqualifiedUserCall(guarded.Handler.Body),
            _ => false
        });

    private static IEnumerable<Expression> StatementExpressions(Statement statement) => statement switch
    {
        Variable variable => [variable.Value],
        Assignment assignment => [assignment.Value],
        FieldAssignment assignment => [assignment.Target, assignment.Value],
        IndexAssignment assignment => [assignment.Target, assignment.Value],
        Return { Value: { } value } => [value],
        Throw failure => [failure.Value],
        For loop => [loop.Items],
        While loop => [loop.Condition],
        Conditional conditional => [conditional.Condition],
        ExpressionStatement expression => [expression.Value],
        _ => []
    };

    private static bool ContainsUnqualifiedUserCall(Expression expression)
    {
        if (expression is Call { Receiver: null } directCall && directCall.Name is not ("print" or "eprint" or "args" or "powershell" or "Error"))
        {
            return true;
        }
        return expression switch
        {
            Unary unary => ContainsUnqualifiedUserCall(unary.Value),
            Binary binary => ContainsUnqualifiedUserCall(binary.Left) || ContainsUnqualifiedUserCall(binary.Right),
            ArrayLiteral array => array.Items.Any(ContainsUnqualifiedUserCall),
            DictionaryLiteral dictionary => dictionary.Entries.Any(entry => ContainsUnqualifiedUserCall(entry.Key) || ContainsUnqualifiedUserCall(entry.Value)),
            Index index => ContainsUnqualifiedUserCall(index.Receiver) || ContainsUnqualifiedUserCall(index.Offset),
            Member member => ContainsUnqualifiedUserCall(member.Receiver),
            Call call => (call.Receiver is not null && ContainsUnqualifiedUserCall(call.Receiver)) ||
                call.Arguments.Any(argument => ContainsUnqualifiedUserCall(argument.Value)),
            _ => false
        };
    }

    private (VariableSymbol? Symbol, bool NonNullWhenTrue) NullTestRefinement(Expression condition)
    {
        if (condition is not Binary { Token.Text: "==" or "!=" } binary)
        {
            return (null, false);
        }

        var name = IsNullLiteral(binary.Left) ? binary.Right as Name :
            IsNullLiteral(binary.Right) ? binary.Left as Name : null;
        if (name is null || !Variables.TryGetValue(name, out var symbol) || !symbol.Type.IsNullable)
        {
            return (null, false);
        }
        return (symbol, binary.Token.Text == "!=");
    }

    private bool IsNullableNullComparison(Binary binary)
    {
        var other = IsNullLiteral(binary.Left) ? binary.Right :
            IsNullLiteral(binary.Right) ? binary.Left : null;
        if (other is null)
        {
            return false;
        }
        return Types[other].IsNullable || other is Name name &&
            Variables.TryGetValue(name, out var symbol) && symbol.Type.IsNullable;
    }

    private static bool IsNullLiteral(Expression expression) =>
        expression is Literal { Value: NullValue };

    private void UpdateRefinement(VariableSymbol symbol, ValueType assigned)
    {
        refinements.Remove(symbol);
        if (symbol.Type.IsNullable && assigned != ValueType.Null && !assigned.IsNullable &&
            CanConvert(symbol.Type.NonNullable, assigned))
        {
            refinements[symbol] = symbol.Type.NonNullable;
        }
    }

    private Dictionary<VariableSymbol, ValueType> SnapshotRefinements() =>
        SnapshotRefinements(refinements);

    private static Dictionary<VariableSymbol, ValueType> SnapshotRefinements(
        Dictionary<VariableSymbol, ValueType> source) => new(source);

    private void MergeRefinements(params Dictionary<VariableSymbol, ValueType>[] states)
    {
        refinements = states.Length == 0 ? [] : states[0]
            .Where(pair => states.Skip(1).All(state => state.TryGetValue(pair.Key, out var type) && type == pair.Value))
            .ToDictionary();
    }

    private void InvalidateGlobalRefinements()
    {
        foreach (var symbol in refinements.Keys.Where(symbol => symbol.IsGlobal && !symbol.IsConst).ToArray())
        {
            refinements.Remove(symbol);
        }
    }
}
