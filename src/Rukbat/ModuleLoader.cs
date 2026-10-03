namespace Rukbat;

internal sealed class ModuleLoader(Func<string, string>? readSource = null, bool collectSyntax = false)
{
    private static readonly HashSet<string> StandardModules = new(StringComparer.Ordinal)
    {
        "fs", "path", "json", "csv", "env", "process", "text", "test", "encoding", "math", "datetime"
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
        var blocks = collectSyntax ? new Dictionary<List<Statement>, (int Start, int End)>(ReferenceEqualityComparer.Instance) : null;
        var typeTokens = collectSyntax ? new Dictionary<ValueType, Token>(ReferenceEqualityComparer.Instance) : null;
        var statements = source.Diagnostics.Count == 0 ? new Parser(source, tokens, blocks: blocks, typeTokens: typeTokens).Parse() : [];
        var module = new Module(source, $"m{modules.Count}", statements);
        module.Blocks = blocks;
        module.TypeTokens = typeTokens;
        modules.Add(path, module);
        // Distinguish an already loaded shared dependency from a cycle on the active traversal path.
        visiting.Add(module);
        foreach (var import in module.Statements.OfType<Import>())
        {
            if (module.Imports.ContainsKey(import.Name))
            {
                source.Error("RUK3000", $"Duplicate import '{import.Name}'.", import.Token);
                continue;
            }
            if (import.IsStandard)
            {
                if (!StandardModules.Contains(import.Path))
                {
                    source.Error("RUK3002", $"Unknown standard module '{import.Path}'.", import.Token);
                    continue;
                }
                if (import.Name != import.Path)
                {
                    source.Error("RUK3002", "Standard modules use their canonical name and cannot be aliased.", import.Token);
                    continue;
                }
                var standardPath = $"<stdlib:{import.Path}>";
                if (!modules.TryGetValue(standardPath, out var standard))
                {
                    var standardText = import.Path switch
                    {
                        "process" => "struct Result { exit_code: int; stdout: string; stderr: string }",
                        "json" => "struct Value {}",
                        "csv" => "struct Row {}; struct Table { columns: string[]; rows: Row[] }",
                        "test" => "struct Context { temp_dir: string; source_dir: string }",
                        _ => ""
                    };
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
                source.Error("RUK3002", $"Import alias '{import.Name}' is reserved for a standard module.", import.Token);
                continue;
            }
            if (!resolveImports)
            {
                source.Error("RUK3000", "Imports require CompileFile or 'rukbat build file.rukbat'.", import.Token);
                continue;
            }
            if (!IsRelativeModulePath(import.Path))
            {
                source.Error("RUK3002", "Import path must be a relative .rukbat file path using '/' separators.", import.Token);
                continue;
            }
            string importedPath;
            try
            {
                importedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, import.Path));
            }
            catch (Exception error) when (error is ArgumentException or IOException or NotSupportedException)
            {
                source.Error("RUK3002", $"Invalid import path '{import.Path}': {error.Message}", import.Token);
                continue;
            }
            if (modules.TryGetValue(importedPath, out var imported))
            {
                if (visiting.Contains(imported))
                {
                    source.Error("RUK3001", $"Import cycle involving '{import.Name}'.", import.Token);
                    continue;
                }
            }
            else
            {
                try
                {
                    imported = Visit((readSource ?? File.ReadAllText)(importedPath), importedPath, true);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    source.Error("RUK3002", $"Cannot read module '{import.Path}' as '{import.Name}': {error.Message}", import.Token);
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
            path.EndsWith(".rukbat", StringComparison.Ordinal);
    }
}
