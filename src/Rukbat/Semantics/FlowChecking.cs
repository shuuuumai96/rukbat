namespace Rukbat;

internal sealed partial class TypeChecker
{
    // A loop may execute zero times. Both paths of a try/catch must return or throw.
    private static bool AlwaysExits(List<Statement> body) => body.Any(statement =>
        statement is Return or Throw ||
        statement is ExpressionStatement { Value: Call { Receiver: null, Name: "exit", PowerShellType: null } } ||
        statement is Conditional condition &&
            AlwaysExits(condition.Then) && AlwaysExits(condition.Else) ||
        statement is TryCatch guarded &&
            AlwaysExits(guarded.Body) && AlwaysExits(guarded.Handler.Body));

    private void CheckConditional(Conditional conditional, Symbols scope, ValueType? returnType)
    {
        var baseline = SnapshotRefinements();
        var (symbol, nonNullWhenTrue) = NullTestRefinement(conditional.Condition);

        refinements = SnapshotRefinements(baseline);
        if (symbol is not null && nonNullWhenTrue)
        {
            refinements[symbol] = symbol.Type.NonNullable;
        }
        CheckStatements(conditional.Then, new Symbols(scope), returnType, false);
        var afterThen = SnapshotRefinements();

        refinements = SnapshotRefinements(baseline);
        if (symbol is not null && !nonNullWhenTrue)
        {
            refinements[symbol] = symbol.Type.NonNullable;
        }
        CheckStatements(conditional.Else, new Symbols(scope), returnType, false);
        var afterElse = SnapshotRefinements();

        var thenExits = AlwaysExits(conditional.Then);
        var elseExits = conditional.Else.Count != 0 && AlwaysExits(conditional.Else);
        if (thenExits && !elseExits)
        {
            refinements = afterElse;
        }
        else if (elseExits && !thenExits)
        {
            refinements = afterThen;
        }
        else
        {
            MergeRefinements(afterThen, afterElse);
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
