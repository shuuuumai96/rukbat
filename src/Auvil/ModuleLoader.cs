namespace Auvil;

internal sealed class ModuleLoader
{
    private readonly Dictionary<string, Module> modules = new(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
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
        var module = new Module(source, $"m{modules.Count}", source.Diagnostics.Count == 0 ? new Parser(source, tokens).Parse() : []);
        modules.Add(path, module);
        visiting.Add(module);
        foreach (var import in module.Statements.OfType<Import>())
        {
            if (module.Imports.ContainsKey(import.Name))
            { source.Error("AUV3000", $"Duplicate import '{import.Name}'.", import.Token); continue; }
            if (!resolveImports)
            { source.Error("AUV3000", "Imports require CompileFile or 'auvil build file.auv'.", import.Token); continue; }
            var importedPath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, import.Name + ".auv"));
            if (modules.TryGetValue(importedPath, out var imported))
            {
                if (visiting.Contains(imported))
                { source.Error("AUV3001", $"Import cycle involving '{import.Name}'.", import.Token); continue; }
            }
            else
            {
                try { imported = Visit(File.ReadAllText(importedPath), importedPath, true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                { source.Error("AUV3002", $"Cannot read module '{import.Name}': {error.Message}", import.Token); continue; }
            }
            module.Imports.Add(import.Name, imported);
        }
        visiting.Remove(module);
        Ordered.Add(module);
        return module;
    }
}
