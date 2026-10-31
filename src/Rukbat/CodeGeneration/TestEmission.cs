namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private string TestAssertion(Call call)
        {
            var condition = call.Arguments[0].Value;
            var label = Quote(AssertionSource(call));
            // Capture primitive comparison operands once, preserving evaluation order and conversions.
            // Keep short-circuit expressions and nullable array/null comparisons on the ordinary path.
            if (condition is Binary binary && binary.Token.Text is "==" or "!=" or "<" or "<=" or ">" or ">=" &&
                !checker.Types[binary.Left].IsComposite && !checker.Types[binary.Right].IsComposite)
            {
                var op = BinaryOperator(binary.Token.Text);
                return "(& { " +
                    $"$left = {Expr(binary.Left)}; $right = {Expr(binary.Right)}; " +
                    $"if (-not {Comparison("$left", "$right", op, checker.Types[binary.Left], checker.Types[binary.Right])}) {{ " +
                    $"$message = 'Assertion failed: ' + {label} + \"`nleft = \" + {AssertionValue(checker.Types[binary.Left], "$left")} + " +
                    $"\"`nright = \" + {AssertionValue(checker.Types[binary.Right], "$right")}; " +
                    $"& $script:__rukbat_raise @{{message = $message}} {Location(call)}" + " } })";
            }

            return $"(& {{ if (-not ({Expr(condition)})) {{ & $script:__rukbat_raise " +
                $"@{{message = ('Assertion failed: ' + {label})}} {Location(call)}" + " } })";
        }

        private static string AssertionValue(ValueType type, string name) =>
            "(& {param($value) if ($null -eq $value) { '<null>' } else { " +
            (DateTimeValues.Contains(type) ? "$value.ToString()" : "ConvertTo-Json -InputObject $value -Compress") + " } } " + name + ")";

        private string AssertionSource(Call call)
        {
            var tokens = new Lexer(module.Source).Scan();
            var opening = tokens.FindIndex(token => token.Offset > call.Token.Offset && token.Text == "(");
            var depth = 0;
            for (var index = opening; index < tokens.Count; index++)
            {
                if (tokens[index].StringValue is not null)
                {
                    continue;
                }
                if (tokens[index].Text == "(")
                {
                    depth++;
                }
                if (tokens[index].Text == ")" && --depth == 0)
                {
                    return module.Source.Text[(tokens[opening].Offset + 1)..tokens[index].Offset].Trim();
                }
            }
            throw new InvalidOperationException("Missing assertion closing parenthesis.");
        }
    }
}
