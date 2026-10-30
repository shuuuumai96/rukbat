namespace Rukbat
{
    internal static class HashValues
    {
        public static readonly ValueType Algorithm = new("hash.Algorithm");
        public static readonly string[] All = ["sha256", "sha384", "sha512", "sha1", "md5"];
        public static string Description(string name) => name is "sha1" or "md5"
            ? "For comparison with legacy checksums; do not use for new tamper detection. Read-only."
            : "A SHA-2 checksum algorithm. Read-only.";
    }
}
