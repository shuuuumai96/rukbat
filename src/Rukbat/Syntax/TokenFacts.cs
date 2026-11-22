namespace Rukbat
{
    internal static class TokenFacts
    {
        public const string EndOfFile = "<eof>";
        public const string Assign = "=";
        public const string Declare = ":=";

        private static readonly IReadOnlyDictionary<string, int> BinaryPrecedences = new Dictionary<string, int>(StringComparer.Ordinal)
        {
            ["||"] = 1,
            ["&&"] = 2,
            ["=="] = 3,
            ["!="] = 3,
            ["<"] = 4,
            ["<="] = 4,
            [">"] = 4,
            [">="] = 4,
            ["+"] = 5,
            ["-"] = 5,
            ["*"] = 6,
            ["/"] = 6,
            ["%"] = 6
        };

        public static int Precedence(string token) => BinaryPrecedences.GetValueOrDefault(token);

        public static bool IsOperator(string token) =>
            token is Assign or Declare || BinaryPrecedences.ContainsKey(token);
    }
}
