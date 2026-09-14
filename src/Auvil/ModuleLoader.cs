namespace Auvil;

internal sealed class ModuleLoader
{
    private static readonly HashSet<string> StandardModules = new(StringComparer.Ordinal)
    {
        "files", "path", "json", "env", "process"
    };
    private readonly Dictionary<string, Module> modules = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private readonly HashSet<Module> visiting = [];
    public List<Module> Ordered { get; } = [];
    public IEnumerable<Diagnostic> Diagnostics => modules.Values.SelectMany(m => m.Source.Diagnostics);

    public void Load(string source, string path, bool resolveImports)
    {
        Visit(source, path, resolveImports);
    }

    private Module Visit(string text, string path, bool resolveImports)
    {
        var source = new Source(text, path);
        var tokens = new Lexer(source).Scan();
        var statements = source.Diagnostics.Count == 0 ? new Parser(source, tokens).Parse() : [];
        var module = new Module(source, $"m{modules.Count}", statements);
        modules.Add(path, module);
        // Distinguish an already loaded shared dependency from a cycle on the active traversal path.
        visiting.Add(module);
        foreach (var import in module.Statements.OfType<Import>())
        {
            if (module.Imports.ContainsKey(import.Name))
            {
                source.Error("AUV3000", $"Duplicate import '{import.Name}'.", import.Token);
                continue;
            }
            if (import.IsStandard)
            {
                if (!StandardModules.Contains(import.Path))
                {
                    source.Error("AUV3002", $"Unknown standard module '{import.Path}'.", import.Token);
                    continue;
                }
                if (import.Name != import.Path)
                {
                    source.Error("AUV3002", "Standard modules use their canonical name and cannot be aliased.", import.Token);
                    continue;
                }
                var standardPath = $"<stdlib:{import.Path}>";
                if (!modules.TryGetValue(standardPath, out var standard))
                {
                    var standardText = import.Path == "process"
                        ? "struct Result { exit_code: int; stdout: string; stderr: string }"
                        : "";
                    var standardSource = new Source(standardText, standardPath);
                    var standardTokens = new Lexer(standardSource).Scan();
                    var standardStatements = new Parser(standardSource, standardTokens).Parse();
                    standard = new Module(standardSource, $"m{modules.Count}", standardStatements, import.Path);
                    modules.Add(standardPath, standard);
                    Ordered.Add(standard);
                }
                module.Imports.Add(import.Name, standard);
                continue;
            }
            if (StandardModules.Contains(import.Name))
            {
                source.Error("AUV3002", $"Import alias '{import.Name}' is reserved for a standard module.", import.Token);
                continue;
            }
            if (!resolveImports)
            {
                source.Error("AUV3000", "Imports require CompileFile or 'auvil build file.auv'.", import.Token);
                continue;
            }
            if (!IsRelativeModulePath(import.Path))
            {
                source.Error("AUV3002", "Import path must be a relative .auv file path using '/' separators.", import.Token);
                continue;
            }
            string importedPath;
            try
            {
                importedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, import.Path));
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
            {
                source.Error("AUV3002", $"Invalid import path '{import.Path}': {error.Message}", import.Token);
                continue;
            }
            if (modules.TryGetValue(importedPath, out var imported))
            {
                if (visiting.Contains(imported))
                {
                    source.Error("AUV3001", $"Import cycle involving '{import.Name}'.", import.Token);
                    continue;
                }
            }
            else
            {
                try
                {
                    imported = Visit(File.ReadAllText(importedPath), importedPath, true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    source.Error("AUV3002", $"Cannot read module '{import.Path}' as '{import.Name}': {error.Message}", import.Token);
                    continue;
                }
            }
            module.Imports.Add(import.Name, imported);
        }
        visiting.Remove(module);
        // Append dependencies first to establish initialization order for code generation.
        Ordered.Add(module);
        return module;
    }

    private static bool IsRelativeModulePath(string path)
    {
        // Require forward slashes so source imports remain portable across Windows and Unix.
        return !string.IsNullOrWhiteSpace(path) &&
            (path.StartsWith("./", StringComparison.Ordinal) || path.StartsWith("../", StringComparison.Ordinal)) &&
            !path.StartsWith('/') &&
            !path.Contains('\\') &&
            !path.Contains(':') &&
            !path.Any(char.IsControl) &&
            path.EndsWith(".auv", StringComparison.Ordinal);
    }
}
