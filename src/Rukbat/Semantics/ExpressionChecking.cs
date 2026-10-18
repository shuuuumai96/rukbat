namespace Rukbat;

internal sealed partial class TypeChecker
{
    private ValueType TypeOf(Expression expression, Symbols scope, ValueType? expected = null)
    {
        // Retain each node's checked type so the emitter never needs to infer it again.
        var type = expression switch
        {
            Literal literal => literal.Value switch
            {
                long => ValueType.Int,
                double => ValueType.Float,
                string => ValueType.String,
                bool => ValueType.Bool,
                NullValue => ValueType.Null,
                _ => ValueType.Error
            },
            ArrayLiteral array => ArrayType(array, scope, expected),
            DictionaryLiteral dictionary => DictionaryType(dictionary, scope, expected),
            Index index => IndexType(index, scope),
            Name name => Resolve(name, scope),
            Member member => MemberType(member, scope),
            Unary unary => UnaryType(unary, scope),
            Binary binary => BinaryType(binary, scope),
            Call call => CallType(call, scope),
            _ => ValueType.Error
        };
        Types[expression] = type;
        return type;
    }

    private ValueType UnaryType(Unary unary, Symbols scope)
    {
        var actual = TypeOf(unary.Value, scope);
        if (unary.Token.Text == "!")
        {
            Match(ValueType.Bool, actual, unary.Value);
            return ValueType.Bool;
        }
        if (actual == ValueType.Int || actual == ValueType.Float)
        {
            return actual;
        }
        Error("RUK2001", $"Unary operator '{unary.Token.Text}' requires int or float, got {actual}.", unary);
        return ValueType.Error;
    }

    private ValueType BinaryType(Binary binary, Symbols scope)
    {
        var left = TypeOf(binary.Left, scope);
        var right = TypeOf(binary.Right, scope);
        if (left == ValueType.Error || right == ValueType.Error)
        {
            return ValueType.Error;
        }

        var op = binary.Token.Text;
        if (op is "==" or "!=" && IsNullableNullComparison(binary))
        {
            return ValueType.Bool;
        }
        if (left == right && !left.IsNullable && !left.IsComposite && left != ValueType.Void && left != HttpValues.Body && left != ValueType.FileLines && left != ValueType.RegexPattern && op is "==" or "!=")
        {
            return ValueType.Bool;
        }

        if (left == ValueType.Bool && right == ValueType.Bool && op is "&&" or "||")
        {
            return ValueType.Bool;
        }

        if (left == right && DateTimeValues.Ordered(left) && op is "<" or "<=" or ">" or ">=")
            return ValueType.Bool;

        if (left == ValueType.Int && right == ValueType.Int)
        {
            return op switch
            {
                "<" or "<=" or ">" or ">=" => ValueType.Bool,
                "+" or "-" or "*" or "/" or "%" => ValueType.Int,
                _ => Invalid()
            };
        }

        if (left == ValueType.Float && right == ValueType.Float)
        {
            return op switch
            {
                "==" or "!=" or "<" or "<=" or ">" or ">=" => ValueType.Bool,
                "+" or "-" or "*" or "/" => ValueType.Float,
                _ => Invalid()
            };
        }

        if (left == ValueType.String && right == ValueType.String && op == "+")
        {
            return ValueType.String;
        }

        return Invalid();

        ValueType Invalid()
        {
            Error("RUK2001", $"Operator '{op}' cannot combine {left} and {right}.", binary);
            return ValueType.Error;
        }
    }
}
