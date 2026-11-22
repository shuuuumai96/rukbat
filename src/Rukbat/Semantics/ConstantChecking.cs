namespace Rukbat
{
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
            {
                Error(DiagnosticCode.ConstMutation, $"Cannot modify const '{variable.Name}'. Copy its value to a mutable variable first.", operation);
            }
        }

        public HashSet<FunctionSymbol> MutatingMethods { get; } = new(ReferenceEqualityComparer.Instance);

        // Bind every body first, then propagate self writes through bound method calls.
        // Arguments and local declarations are value copies, so only receiver paths
        // rooted at self can forward a mutation to the caller's original value.
        private void CheckConstMethodCalls(List<Module> modules)
        {
            var mutating = MutatingMethods;
            mutating.Clear();
            var forwarding = new Dictionary<FunctionSymbol, List<FunctionSymbol>>(ReferenceEqualityComparer.Instance);
            foreach (var method in Functions.Values.Where(function => function.Owner is not null))
            {
                var statements = NestedStatements(method.Function.Body).ToArray();
                var writesSelf = statements.Any(statement => statement switch
                {
                    Assignment assignment => Variables.GetValueOrDefault(assignment)?.Name == "self",
                    FieldAssignment assignment => IsSelf(assignment.Target),
                    IndexAssignment assignment => IsSelf(assignment.Target),
                    _ => false
                });

                var targets = new List<FunctionSymbol>();
                foreach (var call in statements.SelectMany(StatementExpressions).SelectMany(Expressions).OfType<Call>())
                {
                    if (call.Receiver is null || !IsSelf(call.Receiver))
                    {
                        continue;
                    }

                    var binding = Calls.GetValueOrDefault(call);
                    writesSelf |= binding.Kind == CallKind.ArrayAppend ||
                        binding.Kind == CallKind.DictionaryMethod && call.Name == ContainerMember.Remove;
                    if (binding.Function is { Owner: not null } target)
                    {
                        targets.Add(target);
                    }
                }
                if (writesSelf)
                {
                    mutating.Add(method);
                }

                forwarding.Add(method, targets);
            }
            bool changed;
            do
            {
                changed = false;
                foreach (var (method, targets) in forwarding)
                {
                    if (targets.Any(mutating.Contains) && mutating.Add(method))
                    {
                        changed = true;
                    }
                }
            } while (changed);

            foreach (var current in modules)
            {
                module = current;
                foreach (var call in NestedStatements(current.Statements).SelectMany(StatementExpressions)
                    .SelectMany(Expressions).OfType<Call>())
                {
                    if (call.Receiver is not null && Calls.GetValueOrDefault(call).Function is { } target && mutating.Contains(target))
                    {
                        RequireWritable(RootVariable(call.Receiver), call);
                        if (!IsVariablePath(call.Receiver))
                        {
                            Error(DiagnosticCode.InvalidOperation, "A mutating method requires a variable, not a temporary value.", call.Receiver);
                        }
                    }
                }
            }
            bool IsSelf(Expression expression) => RootVariable(expression)?.Name == "self";
        }

        private static IEnumerable<Statement> NestedStatements(IEnumerable<Statement> statements) =>
            SyntaxTraversal.NestedStatements(statements);

        private static IEnumerable<Expression> Expressions(Expression expression) =>
            SyntaxTraversal.Expressions(expression);
    }
}
