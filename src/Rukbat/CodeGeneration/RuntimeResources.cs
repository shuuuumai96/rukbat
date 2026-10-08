using System.Collections.Concurrent;

namespace Rukbat;

internal static class RuntimeResources
{
    private static readonly ConcurrentDictionary<string, string> powerShell = new(StringComparer.Ordinal);

    public static string ReadPowerShell(string name) => powerShell.GetOrAdd(name, static name =>
    {
        using var stream = typeof(RuntimeResources).Assembly.GetManifestResourceStream($"Rukbat.Runtime.PowerShell.{name}.ps1")
            ?? throw new InvalidOperationException($"Missing embedded PowerShell runtime: {name}");
        using var reader = new StreamReader(stream);
        // A source file's final newline terminates its last line; it is not an extra emitted line.
        var text = reader.ReadToEnd().Replace("\r\n", "\n");
        return text.EndsWith('\n') ? text[..^1] : text;
    });
}
