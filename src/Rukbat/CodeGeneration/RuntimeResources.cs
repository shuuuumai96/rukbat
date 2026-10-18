using System.Collections.Concurrent;

namespace Rukbat;

internal static class RuntimeResources
{
    private static readonly ConcurrentDictionary<string, string> powerShell = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> native = new(StringComparer.Ordinal);

    public static string ReadPowerShell(string name) => powerShell.GetOrAdd(name, static name =>
    {
        // A source file's final newline terminates its last line; it is not an extra emitted line.
        var text = ReadResource($"Rukbat.Runtime.PowerShell.{name}.ps1", $"Missing embedded PowerShell runtime: {name}")
            .Replace("\r\n", "\n");
        return text.EndsWith('\n') ? text[..^1] : text;
    });

    public static string ReadNative(string name) => native.GetOrAdd(name, static name =>
        ReadResource($"Rukbat.Runtime.{name}.cs.txt", $"Missing embedded runtime: {name}"));

    private static string ReadResource(string resourceName, string missingMessage)
    {
        using var stream = typeof(RuntimeResources).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(missingMessage);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
