namespace Rukbat
{
    public sealed record Diagnostic(
        string Code,
        string Message,
        int Line,
        int Column,
        string FilePath = "<input>");
}
