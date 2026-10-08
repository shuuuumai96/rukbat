namespace Rukbat;

internal sealed partial class TypeChecker
{
    private VariableSymbol? RootVariable(Expression expression) => expression switch
    {
        Name name => Variables.GetValueOrDefault(name),
        Member member => RootVariable(member.Receiver),
        Index index => RootVariable(index.Receiver),
        _ => null
    };

    private void RequireWritable(VariableSymbol? variable, Node operation)
    {
        if (variable is { IsConst: true })
            Error("RUK2011", $"Cannot modify const '{variable.Name}'. Copy its value to a mutable variable first.", operation);
    }

    // Bind every body first, then propagate self writes through bound method calls.
    // Arguments and local declarations are value copies, so only receiver paths
    // rooted at self can forward a mutation to the caller's original value.
    private void CheckConstMethodCalls(List<Module> modules)
    {
        var mutating = new HashSet<FunctionSymbol>(ReferenceEqualityComparer.Instance);
        var forwarding = new Dictionary<FunctionSymbol, List<FunctionSymbol>>(ReferenceEqualityComparer.Instance);
        foreach (var method in Functions.Values.Where(function => function.Owner is not null))
        {
            var statements = NestedStatements(method.Function.Body).ToArray();
            var calls = statements.SelectMany(StatementExpressions).SelectMany(Expressions).OfType<Call>().ToArray();
            var writesSelf = statements.Any(statement => statement switch
            {
                Assignment assignment => Variables.GetValueOrDefault(assignment)?.Name == "self",
                FieldAssignment assignment => IsSelf(assignment.Target),
                IndexAssignment assignment => IsSelf(assignment.Target),
                _ => false
            }) || calls.Any(call => (ArrayAppends.Contains(call) || DictionaryCalls.Contains(call) && call.Name == "remove") && IsSelf(call.Receiver!));
            if (writesSelf)
                mutating.Add(method);
            forwarding.Add(method, calls.Where(call => call.Receiver is not null && IsSelf(call.Receiver) &&
                    Calls.TryGetValue(call, out var target) && target.Owner is not null)
                .Select(call => Calls[call]).ToList());
        }
        bool changed;
        do
        {
            changed = false;
            foreach (var (method, targets) in forwarding)
                if (targets.Any(mutating.Contains) && mutating.Add(method))
                    changed = true;
        } while (changed);

        foreach (var current in modules)
        {
            module = current;
            foreach (var call in NestedStatements(current.Statements).SelectMany(StatementExpressions)
                .SelectMany(Expressions).OfType<Call>())
                if (call.Receiver is not null && Calls.TryGetValue(call, out var target) && mutating.Contains(target))
                {
                    RequireWritable(RootVariable(call.Receiver), call);
                    if (!IsVariablePath(call.Receiver))
                        Error("RUK2010", "A mutating method requires a variable, not a temporary value.", call.Receiver);
                }
        }
        bool IsSelf(Expression expression) => RootVariable(expression)?.Name == "self";
    }

    private static IEnumerable<Statement> NestedStatements(IEnumerable<Statement> statements)
    {
        foreach (var statement in statements)
        {
            yield return statement;
            var nested = statement switch
            {
                Function function => function.Body,
                StructDeclaration structure => structure.Methods.SelectMany(method => method.Body),
                Conditional conditional => conditional.Then.Concat(conditional.Else),
                For loop => loop.Body,
                While loop => loop.Body,
                TryCatch guarded => guarded.Body.Concat(guarded.Handler.Body),
                _ => []
            };
            foreach (var child in NestedStatements(nested))
                yield return child;
        }
    }

    private static IEnumerable<Expression> Expressions(Expression expression)
    {
        yield return expression;
        var children = expression switch
        {
            Unary unary => new[] { unary.Value },
            Binary binary => [binary.Left, binary.Right],
            ArrayLiteral array => array.Items,
            DictionaryLiteral dictionary => dictionary.Entries.SelectMany(entry => new[] { entry.Key, entry.Value }),
            Index index => [index.Receiver, index.Offset],
            Member member => [member.Receiver],
            Call call => (call.Receiver is null ? Enumerable.Empty<Expression>() : [call.Receiver])
                .Concat(call.Arguments.Select(argument => argument.Value)),
            _ => []
        };
        foreach (var child in children.SelectMany(Expressions))
            yield return child;
    }
}
