namespace Rukbat
{
    internal sealed class NodeHandlers<TContext>(params NodeHandlers<TContext>.Handler[] handlers)
    {
        private readonly Dictionary<Type, Action<TContext, Node>> strategies =
            handlers.ToDictionary(handler => handler.NodeType, handler => handler.Execute);

        public void Dispatch(TContext context, Node node) => strategies[node.GetType()](context, node);

        public void DispatchIfRegistered(TContext context, Node node)
        {
            if (strategies.TryGetValue(node.GetType(), out var execute))
            {
                execute(context, node);
            }
        }

        public readonly record struct Handler(Type NodeType, Action<TContext, Node> Execute);

        public static Handler For<TNode>(Action<TContext, TNode> execute) where TNode : Node =>
            new(typeof(TNode), (context, node) => execute(context, (TNode)node));
    }
}
