namespace Rukbat
{
    internal sealed class EnumSymbol(Module module, EnumDeclaration declaration)
    {
        public Module Module { get; } = module;
        public EnumDeclaration Declaration { get; } = declaration;
        public ValueType Type => new(Declaration.Token.Text, Enum: this);
        public Dictionary<string, EnumMember> Members { get; } = new(StringComparer.Ordinal);
    }

    internal sealed partial class TypeChecker
    {
        public Dictionary<Expression, EnumSymbol> EnumQualifiers { get; } = new(ReferenceEqualityComparer.Instance);

        private void RegisterEnums(Module current)
        {
            foreach (var declaration in current.Statements.OfType<EnumDeclaration>())
            {
                var name = declaration.Token.Text;
                var enumeration = new EnumSymbol(current, declaration);
                if (!current.Enums.TryAdd(name, enumeration))
                {
                    Error(DiagnosticCode.ConflictingName, $"Duplicate enum '{name}'.", declaration);
                }

                if (IsPrimitive(name) || name is "Error" or "dict" ||
                    current.Structs.ContainsKey(name) || current.Imports.ContainsKey(name))
                {
                    Error(DiagnosticCode.ConflictingName, $"Enum name '{name}' conflicts with an existing type or import.", declaration);
                }

                if (declaration.Members.Count == 0)
                {
                    Error(DiagnosticCode.InvalidType, $"Enum '{name}' must declare at least one member.", declaration);
                }

                foreach (var member in declaration.Members)
                {
                    if (!enumeration.Members.TryAdd(member.Token.Text, member))
                    {
                        Error(DiagnosticCode.ConflictingName, $"Duplicate member '{member.Token.Text}' on enum {name}.", member);
                    }
                }
            }
        }

        private ValueType EnumTextCallType(Call call, Symbols scope)
        {
            if (!RequireCallParentheses(call))
            {
                return ValueType.Error;
            }

            var operation = $"text.{call.Name}";
            var parse = call.Name == "parse_enum";
            if (parse && call.TypeArgument is null)
            {
                Error(DiagnosticCode.InvalidCall, $"{operation} requires an explicit enum type.", call);
            }
            else if (!parse && call.TypeArgument is not null)
            {
                Error(DiagnosticCode.InvalidCall, $"{operation} infers its enum type and does not accept a type argument.", call);
            }

            var target = parse && call.TypeArgument is not null ? BindType(call.TypeArgument, call) : ValueType.Error;
            CheckStandardArguments(call, scope, [parse ? ValueType.String : ValueType.Error], operation);
            var enumType = parse ? target : call.Arguments.Count == 1 ? Types[call.Arguments[0].Value] : ValueType.Error;
            if (enumType != ValueType.Error && enumType.Enum is null)
            {
                Error(DiagnosticCode.InvalidType, $"{operation} requires a non-null enum type, got {enumType}.", call);
            }

            Calls[call] = new(CallKind.Standard, StandardOperation: operation);
            return parse ? target : ValueType.String;
        }

        private EnumSymbol? FindEnumQualifier(Expression expression) => expression switch
        {
            Name name => module.Enums.GetValueOrDefault(name.Token.Text),
            Member { Receiver: Name alias } member when module.Imports.TryGetValue(alias.Token.Text, out var imported) =>
                imported.Enums.GetValueOrDefault(member.Name),
            _ => null
        };
    }
}
