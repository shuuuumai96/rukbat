namespace Rukbat
{
    internal static class ContainerMember
    {
        public const string Length = "length";
        public const string Append = "append";
        public const string Has = "has";
        public const string Remove = "remove";
        public const string Keys = "keys";
    }

    internal enum MemberKind { Unbound, EnumMember, StandardConstant, ArrayLength, DictionaryLength, Field }

    internal readonly record struct BoundMember(MemberKind Kind, FieldSymbol? Field = null,
        EnumMember? EnumMember = null, StandardConstant? Constant = null);
}
