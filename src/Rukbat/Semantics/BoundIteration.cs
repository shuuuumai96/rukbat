namespace Rukbat;

internal enum IterationKind { ArraySnapshot, OwnedCursor }

// Closed compiler contract. Only the checker selects an iteration strategy.
internal sealed record BoundIteration(IterationKind Kind, ValueType ElementType, string? OpenOperation = null);
