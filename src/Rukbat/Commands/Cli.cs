using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Rukbat;

public static class Cli
{
    private const int Success = 0;
    private const int CompilationFailure = 1;
    private const int UsageOrIoFailure = 2;
    private const int CompilerArgumentCount = 2;
    private const int ApplicationArgumentsOffset = CompilerArgumentCount + 1;
    private static readonly HashSet<string> FormattingExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj" };

    private static int Execute(string output, IEnumerable<string> arguments)
    {
        // Inherit the caller's directory and standard streams, including interactive input.
        var start = new ProcessStartInfo("powershell") { UseShellExecute = false };
        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-File", output })
        {
            start.ArgumentList.Add(argument);
        }
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        var debugBuild = args is ["build", _, "--debug"] or ["build", _, "--debug", "--output", _];
        if (args is ["--version"])
        {
            stdout.WriteLine($"rukbat {Distribution.Version}");
            return Success;
        }
        if (args is ["doctor"])
        {
            return Distribution.Doctor(stdout, stderr);
        }
        if (args.Length != 0 && args[0] == "test")
        {
            return TestRunner.Run(args[1..], stdout, stderr);
        }
        if (args.Length != 0 && args[0] == "doc")
        {
            return Documentation.Run(args[1..], stdout, stderr);
        }
        if (args.Length < CompilerArgumentCount ||
            args[0] is not ("build" or "run" or "check" or "fmt") ||
            (args[0] != "fmt" && !Path.GetExtension(args[1]).Equals(".rukbat", StringComparison.OrdinalIgnoreCase)) ||
            args.Length > CompilerArgumentCount && !debugBuild && (args[0] != "run" || args[CompilerArgumentCount] != "--"))
        {
            stderr.WriteLine("Usage: rukbat <build|run|check|fmt> input.rukbat [-- arguments... (run only)]");
            stderr.WriteLine("       rukbat fmt directory");
            stderr.WriteLine("       rukbat build input.rukbat --debug [--output debug.ps1]");
            stderr.WriteLine("       rukbat doc input.rukbat [--output reference.html | --check]");
            stderr.WriteLine("       rukbat test [path] [--filter text] [--timeout seconds] [--shell powershell|pwsh]");
            stderr.WriteLine("       rukbat lsp");
            stderr.WriteLine("       rukbat --version | doctor");
            return UsageOrIoFailure;
        }
        try
        {
            var input = Path.GetFullPath(args[1]);
            if (args[0] == "check")
            {
                var diagnostics = new Compiler().CheckFile(input);
                WriteDiagnostics(diagnostics, stderr);
                return diagnostics.Count == 0 ? Success : CompilationFailure;
            }
            if (args[0] == "fmt")
            {
                return FormatPath(input, stderr);
            }
            var output = debugBuild && args.Length == 5 ? Path.GetFullPath(args[4]) : Path.ChangeExtension(input, ".ps1");
            if (debugBuild && !Path.GetExtension(output).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
            {
                stderr.WriteLine("error: Debug output must have a .ps1 extension.");
                return UsageOrIoFailure;
            }
            var result = new Compiler().CompileFile(input, debugBuild);
            WriteDiagnostics(result.Diagnostics, stderr);

            if (!result.Success)
            {
                return CompilationFailure;
            }

            // Windows PowerShell 5.1 identifies UTF-8 source reliably only when it has a BOM.
            File.WriteAllText(output, result.Output, new System.Text.UTF8Encoding(true));
            if (result.DebugMap is { } map)
            {
                File.WriteAllText(output + ".rukbat-map.json", JsonSerializer.Serialize(map,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), new System.Text.UTF8Encoding(false));
            }

            if (args[0] == "run")
            {
                return Execute(output, args.Skip(ApplicationArgumentsOffset));
            }

            stdout.WriteLine(output);
            return Success;
        }
        catch (Win32Exception error)
        {
            stderr.WriteLine(
                "error: Could not start Windows PowerShell 5.1-compatible 'powershell' " +
                $"and make it available on PATH. {error.Message}");
            return UsageOrIoFailure;
        }
        catch (Exception error) when (IsInputOutputError(error))
        {
            stderr.WriteLine($"error: {error.Message}");
            return UsageOrIoFailure;
        }
    }

    private static bool IsInputOutputError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException;

    private static int FormatPath(string input, TextWriter stderr)
    {
        if (!Directory.Exists(input) && !Path.GetExtension(input).Equals(".rukbat", StringComparison.OrdinalIgnoreCase))
        {
            stderr.WriteLine($"error: Expected a .rukbat file or an existing directory: {input}");
            return UsageOrIoFailure;
        }

        var result = Success;
        void ReportError(string path, Exception error)
        {
            stderr.WriteLine($"error: {path}: {error.Message}");
            result = UsageOrIoFailure;
        }

        var files = Directory.Exists(input) ? FormattingFiles(input, ReportError) : [input];
        foreach (var file in files)
        {
            try
            {
                var source = File.ReadAllText(file);
                var formatted = Formatter.Format(source, file);
                WriteDiagnostics(formatted.Diagnostics, stderr);
                if (!formatted.Success)
                {
                    result = Math.Max(result, CompilationFailure);
                    continue;
                }
                File.WriteAllText(file, formatted.Output, new System.Text.UTF8Encoding(false));
            }
            catch (Exception error) when (IsInputOutputError(error))
            {
                ReportError(file, error);
            }
        }
        return result;
    }

    private static IEnumerable<string> FormattingFiles(string directory, Action<string, Exception> reportError)
    {
        var options = new EnumerationOptions
        {
            AttributesToSkip = FileAttributes.ReparsePoint,
            IgnoreInaccessible = false
        };
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(directory, "*", options).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception error) when (IsInputOutputError(error))
        {
            reportError(directory, error);
            yield break;
        }

        foreach (var file in files)
        {
            if (Path.GetExtension(file).Equals(".rukbat", StringComparison.OrdinalIgnoreCase))
            {
                yield return file;
            }
        }
        string[] children;
        try
        {
            children = Directory.EnumerateDirectories(directory, "*", options).Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception error) when (IsInputOutputError(error))
        {
            reportError(directory, error);
            yield break;
        }

        foreach (var child in children)
        {
            if (FormattingExcludedDirectories.Contains(Path.GetFileName(child)))
            {
                continue;
            }
            foreach (var file in FormattingFiles(child, reportError))
            {
                yield return file;
            }
        }
    }

    internal static void WriteDiagnostics(IEnumerable<Diagnostic> diagnostics, TextWriter stderr)
    {
        foreach (var diagnostic in diagnostics)
        {
            stderr.WriteLine(
                $"{diagnostic.FilePath}({diagnostic.Line},{diagnostic.Column}): " +
                $"error {diagnostic.Code}: {diagnostic.Message}");
        }
    }
}
