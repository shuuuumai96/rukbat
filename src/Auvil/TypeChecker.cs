using System.Management.Automation.Language;

namespace Auvil;

internal sealed class TypeChecker(Syntax syntax)
{
    private readonly Dictionary<ScriptBlockAst, Dictionary<string, SimpleType>> scopes = [];
    private readonly Dictionary<ParameterAst, SimpleType> parameters = [];
    private readonly Dictionary<FunctionDefinitionAst, SimpleType> returns = [];

    private static ScriptBlockAst Scope(Ast node)
    {
        for (Ast? current = node; current is not null; current = current.Parent)
            if (current is ScriptBlockAst block) return block;
        throw new InvalidOperationException("AST has no script scope.");
    }

    private static FunctionDefinitionAst? Function(Ast node)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (current is FunctionDefinitionAst function) return function;
        return null;
    }

    public void Check(ScriptBlockAst root)
    {
        var nodes = root.FindAll(_ => true, true).ToArray();
        // Untyped local bindings shadow outer declarations without inferring their types.
        foreach (var variable in nodes.OfType<VariableExpressionAst>())
        {
            if (variable.Parent is not ParameterAst &&
                !(variable.Parent is AssignmentStatementAst assignment && assignment.Left == variable)) continue;
            var scope = variable.Parent is ParameterAst { Parent: FunctionDefinitionAst owner } ? owner.Body : Scope(variable);
            if (!scopes.TryGetValue(scope, out var symbols)) scopes[scope] = symbols = new(StringComparer.OrdinalIgnoreCase);
            symbols.TryAdd(variable.VariablePath.UserPath, SimpleType.Unknown);
        }
        foreach (var annotation in syntax.Variables)
        {
            var variable = nodes.OfType<VariableExpressionAst>()
                .FirstOrDefault(v => v.Extent.StartOffset == annotation.VariableOffset);
            if (variable is null) continue;
            var parameter = variable.Parent as ParameterAst;
            var assignment = variable.Parent as AssignmentStatementAst;
            if (parameter is null && (assignment?.Left != variable || assignment.Operator != TokenKind.Equals))
            {
                syntax.Error("AUV1004", "Type annotations require a variable assignment or a function parameter.", annotation.Start);
                continue;
            }
            var scope = parameter?.Parent is FunctionDefinitionAst owner ? owner.Body : Scope(variable);
            if (!scopes.TryGetValue(scope, out var symbols))
                scopes[scope] = symbols = new(StringComparer.OrdinalIgnoreCase);
            var name = variable.VariablePath.UserPath;
            if (symbols.TryGetValue(name, out var existing) && existing != SimpleType.Unknown && existing != annotation.Type)
                syntax.Error("AUV2002", $"Conflicting types for '${name}'.", annotation.VariableOffset);
            else symbols[name] = annotation.Type;
            if (parameter is not null) parameters[parameter] = annotation.Type;
        }
        foreach (var annotation in syntax.Returns)
        {
            var function = nodes.OfType<FunctionDefinitionAst>().FirstOrDefault(f => f.Extent.StartOffset == annotation.FunctionOffset);
            if (function is not null) returns[function] = annotation.Type;
        }
        foreach (var node in nodes)
        {
            switch (node)
            {
                case AssignmentStatementAst assignment when assignment.Left is VariableExpressionAst variable:
                    if (assignment.Operator == TokenKind.Equals)
                        Match(Lookup(variable), TypeOf(assignment.Right), assignment.Right, $"assignment to '{variable.Extent.Text}'");
                    break;
                case ParameterAst parameter when parameters.TryGetValue(parameter, out var type) && parameter.DefaultValue is not null:
                    Match(type, TypeOf(parameter.DefaultValue), parameter.DefaultValue, "parameter default");
                    break;
                case ReturnStatementAst statement:
                    var function = Function(statement);
                    if (function is not null && Scope(statement) == function.Body && returns.TryGetValue(function, out var returnType))
                        Match(returnType, statement.Pipeline is null ? SimpleType.Void : TypeOf(statement.Pipeline), statement, "return value");
                    break;
                case CommandAst command:
                    CheckCall(command, nodes.OfType<FunctionDefinitionAst>());
                    break;
            }
        }
    }

    private SimpleType Lookup(VariableExpressionAst variable)
    {
        var name = variable.VariablePath.UserPath;
        if (name.Equals("true", StringComparison.OrdinalIgnoreCase) || name.Equals("false", StringComparison.OrdinalIgnoreCase))
            return SimpleType.Bool;
        var parameterOwner = variable.Parent?.Parent as FunctionDefinitionAst;
        Ast? current = parameterOwner?.Body ?? Scope(variable);
        while (current is not null)
        {
            if (current is ScriptBlockAst block && scopes.TryGetValue(block, out var symbols) && symbols.TryGetValue(name, out var type))
                return type;
            current = current.Parent;
        }
        return SimpleType.Unknown;
    }

    // Only locally known expression types; never infer a declaration's type.
    private SimpleType TypeOf(Ast ast) => ast switch
    {
        StringConstantExpressionAst or ExpandableStringExpressionAst => SimpleType.String,
        ConstantExpressionAst constant => constant.Value switch
        {
            int => SimpleType.Int,
            string => SimpleType.String,
            bool => SimpleType.Bool,
            _ => SimpleType.Other
        },
        VariableExpressionAst variable => Lookup(variable),
        CommandExpressionAst expression => TypeOf(expression.Expression),
        PipelineAst pipeline when pipeline.PipelineElements.Count == 1 => TypeOf(pipeline.PipelineElements[0]),
        ParenExpressionAst paren => TypeOf(paren.Pipeline),
        BinaryExpressionAst binary => BinaryType(binary),
        UnaryExpressionAst unary when unary.TokenKind is TokenKind.Minus or TokenKind.Plus && TypeOf(unary.Child) == SimpleType.Int => SimpleType.Int,
        _ => SimpleType.Unknown
    };

    private SimpleType BinaryType(BinaryExpressionAst binary)
    {
        var left = TypeOf(binary.Left);
        var right = TypeOf(binary.Right);
        if (left == SimpleType.Int && right == SimpleType.Int && binary.Operator is TokenKind.Plus or TokenKind.Minus or TokenKind.Multiply)
            return SimpleType.Int;
        if (left == SimpleType.String && right == SimpleType.String && binary.Operator == TokenKind.Plus)
            return SimpleType.String;
        return SimpleType.Unknown;
    }

    private void CheckCall(CommandAst command, IEnumerable<FunctionDefinitionAst> functions)
    {
        if (command.InvocationOperator != TokenKind.Unknown || command.Parent is not PipelineAst { PipelineElements.Count: 1 }) return;
        var name = command.GetCommandName();
        var candidates = functions.Where(f => Scope(f) == Scope(command) && f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (candidates.Length != 1 || candidates[0].Parameters is not { } signature) return;
        var bound = new List<(ParameterAst Parameter, Ast Argument)>();
        var used = new HashSet<ParameterAst>();
        var positional = 0;
        for (var i = 1; i < command.CommandElements.Count; i++)
        {
            var argument = command.CommandElements[i];
            ParameterAst? parameter;
            if (argument is CommandParameterAst named)
            {
                parameter = signature.FirstOrDefault(p => p.Name.VariablePath.UserPath.Equals(named.ParameterName, StringComparison.OrdinalIgnoreCase));
                if (parameter is null) return;
                if (named.Argument is not null) argument = named.Argument;
                else if (++i < command.CommandElements.Count) argument = command.CommandElements[i];
                else return;
            }
            else
            {
                while (positional < signature.Count && used.Contains(signature[positional])) positional++;
                if (positional >= signature.Count) return;
                parameter = signature[positional++];
            }
            if (argument is VariableExpressionAst { Splatted: true } || !used.Add(parameter)) return;
            bound.Add((parameter, argument));
        }
        foreach (var (parameter, argument) in bound)
            if (parameters.TryGetValue(parameter, out var type)) Match(type, TypeOf(argument), argument, $"argument '{parameter.Name.Extent.Text}'");
    }

    private void Match(SimpleType expected, SimpleType actual, Ast node, string context)
    {
        if (expected != SimpleType.Unknown && actual != SimpleType.Unknown && expected != actual)
            syntax.Error("AUV2001", $"Type mismatch in {context}: expected {expected.ToString().ToLowerInvariant()}, got {actual.ToString().ToLowerInvariant()}.", node.Extent.StartOffset);
    }
}
