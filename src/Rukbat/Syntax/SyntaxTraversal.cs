namespace Rukbat
{
    // Single source of truth for walking child statements and expressions across the AST.
    internal static class SyntaxTraversal
    {
        private static KeyValuePair<Type, Func<TBase, IEnumerable<TChild>>> ChildMap<TBase, TNode, TChild>(
            Func<TNode, IEnumerable<TChild>> selector)
            where TBase : Node
            where TNode : TBase =>
            new(typeof(TNode), node => selector((TNode)node));

        private static readonly IReadOnlyDictionary<Type, Func<Statement, IEnumerable<Statement>>> ChildStatements =
            new[]
            {
                ChildMap<Statement, Function, Statement>(static node => node.Body),
                ChildMap<Statement, ImplDeclaration, Statement>(static node => node.Methods.SelectMany(method => method.Body)),
                ChildMap<Statement, Conditional, Statement>(static node => node.Then.Concat(node.Else)),
                ChildMap<Statement, For, Statement>(static node => node.Body),
                ChildMap<Statement, While, Statement>(static node => node.Body),
                ChildMap<Statement, TryCatch, Statement>(static node => node.Body.Concat(node.Handler.Body))
            }.ToDictionary(entry => entry.Key, entry => entry.Value);

        private static readonly IReadOnlyDictionary<Type, Func<Statement, IEnumerable<Expression>>> DirectExpressions =
            new[]
            {
                ChildMap<Statement, Variable, Expression>(static node => [node.Value]),
                ChildMap<Statement, Assignment, Expression>(static node => [node.Value]),
                ChildMap<Statement, FieldAssignment, Expression>(static node => [node.Target, node.Value]),
                ChildMap<Statement, IndexAssignment, Expression>(static node => [node.Target, node.Value]),
                ChildMap<Statement, Return, Expression>(static node => node.Value is { } value ? [value] : []),
                ChildMap<Statement, Throw, Expression>(static node => [node.Value]),
                ChildMap<Statement, For, Expression>(static node => [node.Items]),
                ChildMap<Statement, While, Expression>(static node => [node.Condition]),
                ChildMap<Statement, Conditional, Expression>(static node => [node.Condition]),
                ChildMap<Statement, ExpressionStatement, Expression>(static node => [node.Value])
            }.ToDictionary(entry => entry.Key, entry => entry.Value);

        private static readonly IReadOnlyDictionary<Type, Func<Expression, IEnumerable<Expression>>> ChildExpressions =
            new[]
            {
                ChildMap<Expression, Unary, Expression>(static node => [node.Value]),
                ChildMap<Expression, Binary, Expression>(static node => [node.Left, node.Right]),
                ChildMap<Expression, ArrayLiteral, Expression>(static node => node.Items),
                ChildMap<Expression, DictionaryLiteral, Expression>(static node => node.Entries.SelectMany(entry => new[] { entry.Key, entry.Value })),
                ChildMap<Expression, Index, Expression>(static node => [node.Receiver, node.Offset]),
                ChildMap<Expression, Member, Expression>(static node => [node.Receiver]),
                ChildMap<Expression, Call, Expression>(static node =>
                    (node.Receiver is null ? Enumerable.Empty<Expression>() : [node.Receiver])
                    .Concat(node.Arguments.Select(argument => argument.Value)))
            }.ToDictionary(entry => entry.Key, entry => entry.Value);

        public static IEnumerable<Statement> NestedStatements(IEnumerable<Statement> statements)
        {
            foreach (var statement in statements)
            {
                yield return statement;
                if (ChildStatements.TryGetValue(statement.GetType(), out var children))
                {
                    foreach (var child in NestedStatements(children(statement)))
                    {
                        yield return child;
                    }
                }
            }
        }

        public static IEnumerable<Expression> StatementExpressions(Statement statement) =>
            DirectExpressions.TryGetValue(statement.GetType(), out var selector)
                ? selector(statement)
                : [];

        public static IEnumerable<Expression> DirectChildExpressions(Expression expression) =>
            ChildExpressions.TryGetValue(expression.GetType(), out var children)
                ? children(expression)
                : [];

        public static IEnumerable<Expression> Expressions(Expression expression)
        {
            yield return expression;
            foreach (var child in DirectChildExpressions(expression).SelectMany(Expressions))
            {
                yield return child;
            }
        }
    }
}
