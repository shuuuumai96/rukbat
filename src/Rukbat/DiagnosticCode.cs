namespace Rukbat
{
    internal static class DiagnosticCode
    {
        public const string SyntaxError = "RUK1000";
        public const string InvalidLiteral = "RUK1001";
        public const string InvalidDeclarationScope = "RUK1002";
        public const string InvalidType = "RUK2001";
        public const string ConflictingName = "RUK2002";
        public const string InvalidReturn = "RUK2003";
        public const string UnknownName = "RUK2004";
        public const string InvalidCall = "RUK2005";
        public const string InvalidOperation = "RUK2010";
        public const string ConstMutation = "RUK2011";
        public const string InvalidImport = "RUK3000";
        public const string ImportCycle = "RUK3001";
        public const string ImportResolution = "RUK3002";
        public const string InvalidGeneratedPowerShell = "RUK5000";
        public const string InvalidDocumentation = "RUK6000";
        public const string UnknownDocumentationReference = "RUK6001";
        public const string InternalError = "RUK9000";
    }
}
