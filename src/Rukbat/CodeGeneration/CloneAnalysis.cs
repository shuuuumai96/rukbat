namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private enum VariableScopeKind
        {
            Self,
            Global,
            Local
        }

        private static readonly HashSet<VariableSymbol> EmptyRoots = new(ReferenceEqualityComparer.Instance);

        private static readonly HashSet<ValueKind> MutableCompositeKinds =
        [
            ValueKind.Struct,
            ValueKind.Array,
            ValueKind.Dictionary
        ];

        private static readonly HashSet<CallKind> FreshCallKinds =
        [
            CallKind.StructLiteral,
            CallKind.PowerShell,
            CallKind.LocalFunction,
            CallKind.ImportedFunction,
            CallKind.Standard,
            CallKind.DictionaryMethod
        ];

        private static readonly NodeHandlers<Emitter> StatementMutationHandlers = new(
            NodeHandlers<Emitter>.For<FieldAssignment>(static (emitter, assignment) => emitter.MarkMutated(assignment.Target)),
            NodeHandlers<Emitter>.For<IndexAssignment>(static (emitter, assignment) => emitter.MarkMutated(assignment.Target)));

        private static readonly IReadOnlyDictionary<CallKind, Func<Emitter, Call, BoundCall, bool>> MutatingCallDetectors =
            new Dictionary<CallKind, Func<Emitter, Call, BoundCall, bool>>
            {
                [CallKind.ArrayAppend] = static (_, _, _) => true,
                [CallKind.DictionaryMethod] = static (_, call, _) => call.Name == ContainerMember.Remove,
                [CallKind.LocalFunction] = static (emitter, _, binding) => emitter.IsMutatingMethod(binding.Function),
                [CallKind.ImportedFunction] = static (emitter, _, binding) => emitter.IsMutatingMethod(binding.Function)
            };

        private static readonly IReadOnlyDictionary<VariableScopeKind, Func<Emitter, Call, int, VariableSymbol, bool>> VariableBorrowStrategies =
            new Dictionary<VariableScopeKind, Func<Emitter, Call, int, VariableSymbol, bool>>
            {
                [VariableScopeKind.Self] = static (_, _, _, _) => false,
                [VariableScopeKind.Global] = static (emitter, _, _, root) => !emitter.mutatedVariables.Contains(root),
                [VariableScopeKind.Local] = static (emitter, call, index, root) =>
                    !emitter.mutatedVariables.Contains(root) || !emitter.AnySiblingMutates(call, index, root)
            };

        private static KeyValuePair<Type, Func<Emitter, Expression, TResult>> ExpressionRule<TExpression, TResult>(
            Func<Emitter, TExpression, TResult> evaluate)
            where TExpression : Expression =>
            new(typeof(TExpression), (emitter, expression) => evaluate(emitter, (TExpression)expression));

        private static readonly IReadOnlyDictionary<Type, Func<Emitter, Expression, VariableSymbol?>> RootVariableExtractors =
            new[]
            {
                ExpressionRule<Name, VariableSymbol?>(static (emitter, name) => emitter.VariableOf(name)),
                ExpressionRule<Member, VariableSymbol?>(static (emitter, member) => emitter.RootVariable(member.Receiver)),
                ExpressionRule<Index, VariableSymbol?>(static (emitter, index) => emitter.RootVariable(index.Receiver))
            }.ToDictionary(entry => entry.Key, entry => entry.Value);

        private static readonly IReadOnlyDictionary<Type, Func<Emitter, Expression, bool>> FreshValueChecks =
            new[]
            {
                ExpressionRule<ArrayLiteral, bool>(static (_, _) => true),
                ExpressionRule<DictionaryLiteral, bool>(static (_, _) => true),
                ExpressionRule<Call, bool>(static (emitter, call) => FreshCallKinds.Contains(emitter.CallKindOf(call)))
            }.ToDictionary(entry => entry.Key, entry => entry.Value);

        private HashSet<VariableSymbol> mutatedVariables = new(ReferenceEqualityComparer.Instance);
        private Dictionary<Expression, HashSet<VariableSymbol>> expressionMutations = new(ReferenceEqualityComparer.Instance);

        private VariableSymbol? VariableOf(Name name) => checker.Variables.GetValueOrDefault(name);

        private CallKind CallKindOf(Call call) => checker.Calls[call].Kind;

        private bool IsMutatingMethod(FunctionSymbol? function) =>
            function is { Owner: not null } && checker.MutatingMethods.Contains(function);

        // Single O(N) memoized bottom-up pass across all statements and expressions.
        private void AnalyzeMutations(List<Module> modules)
        {
            mutatedVariables = new HashSet<VariableSymbol>(ReferenceEqualityComparer.Instance);
            expressionMutations = new Dictionary<Expression, HashSet<VariableSymbol>>(ReferenceEqualityComparer.Instance);

            foreach (var statement in modules.SelectMany(current => SyntaxTraversal.NestedStatements(current.Statements)))
            {
                StatementMutationHandlers.DispatchIfRegistered(this, statement);
                foreach (var expression in SyntaxTraversal.StatementExpressions(statement))
                {
                    mutatedVariables.UnionWith(MutatedRootsOf(expression));
                }
            }
        }

        private HashSet<VariableSymbol> MutatedRootsOf(Expression expression)
        {
            if (expressionMutations.TryGetValue(expression, out var cached))
            {
                return cached;
            }

            HashSet<VariableSymbol>? collected = null;
            if (expression is Call { Receiver: { } receiver } call &&
                IsMutatingCall(call) &&
                RootVariable(receiver) is { } mutatedRoot)
            {
                collected = new HashSet<VariableSymbol>(ReferenceEqualityComparer.Instance) { mutatedRoot };
            }

            foreach (var child in SyntaxTraversal.DirectChildExpressions(expression))
            {
                var childMutations = MutatedRootsOf(child);
                if (childMutations.Count != 0)
                {
                    collected ??= new HashSet<VariableSymbol>(ReferenceEqualityComparer.Instance);
                    collected.UnionWith(childMutations);
                }
            }

            var result = collected ?? EmptyRoots;
            expressionMutations[expression] = result;
            return result;
        }

        private bool IsMutatingCall(Call call)
        {
            var binding = checker.Calls[call];
            return MutatingCallDetectors.TryGetValue(binding.Kind, out var detect) && detect(this, call, binding);
        }

        private void MarkMutated(Expression expression)
        {
            if (RootVariable(expression) is { } root)
            {
                mutatedVariables.Add(root);
            }
        }

        private VariableSymbol? RootVariable(Expression expression) =>
            RootVariableExtractors.TryGetValue(expression.GetType(), out var extract)
                ? extract(this, expression)
                : null;

        private static bool NeedsClone(ValueType type) => MutableCompositeKinds.Contains(type.NonNullable.Kind);

        private bool IsFreshValue(Expression expression) =>
            FreshValueChecks.TryGetValue(expression.GetType(), out var check) && check(this, expression);

        private bool IsNeverMutated(Expression expression) =>
            RootVariable(expression) is { } root &&
            root.Name != RuntimeCatalog.SelfIdentifier &&
            !mutatedVariables.Contains(root);

        private static VariableScopeKind ScopeOf(VariableSymbol symbol) =>
            symbol.Name == RuntimeCatalog.SelfIdentifier ? VariableScopeKind.Self
            : symbol.IsGlobal ? VariableScopeKind.Global
            : VariableScopeKind.Local;

        private bool AnySiblingMutates(Call call, int argumentIndex, VariableSymbol target)
        {
            for (var i = 0; i < call.Arguments.Count; i++)
            {
                if (i != argumentIndex && MutatedRootsOf(call.Arguments[i].Value).Contains(target))
                {
                    return true;
                }
            }
            return false;
        }

        private bool CanBorrowArgument(Call call, int argumentIndex, FunctionSymbol function)
        {
            var parameter = checker.Variables[function.Function.Parameters[argumentIndex]];
            var argument = call.Arguments[argumentIndex].Value;
            return !mutatedVariables.Contains(parameter) &&
                (IsFreshValue(argument) ||
                 (RootVariable(argument) is { } root &&
                  (call.Receiver is null || RootVariable(call.Receiver) != root) &&
                  VariableBorrowStrategies[ScopeOf(root)](this, call, argumentIndex, root)));
        }
    }
}
