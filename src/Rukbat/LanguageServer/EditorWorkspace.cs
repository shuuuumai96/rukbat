using Markdig.Syntax;

namespace Rukbat;

internal sealed record EditorReplacement(int Offset, int Length, string Text);

// A request owns one immutable source snapshot. The compiler still owns binding and types.
internal sealed class EditorWorkspace(string[] roots, string entry, IReadOnlyDictionary<string, string> buffers)
{
    private readonly Dictionary<string, string> sources = new(EditorPaths.Comparer);
    private readonly Dictionary<string, LanguageAnalysis> analyses = new(EditorPaths.Comparer);
    private readonly List<string> candidates = [];
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase) { ".git", "bin", "obj" };

    public void Capture()
    {
        foreach (var path in Discover().Order(EditorPaths.Comparer))
        {
            Read(path);
            candidates.Add(path);
        }
    }

    private HashSet<string> Discover()
    {
        var files = new HashSet<string>(EditorPaths.Comparer);
        foreach (var root in roots)
            Collect(root, files);
        files.UnionWith(buffers.Keys.Where(p => Path.GetExtension(p).Equals(".rukbat", StringComparison.OrdinalIgnoreCase) && roots.Any(r => EditorPaths.Contains(r, p))));
        files.Add(entry);
        return files;
    }

    private static void Collect(string directory, HashSet<string> files)
    {
        if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            return;
        foreach (var path in Directory.EnumerateFileSystemEntries(directory))
        {
            var attributes = File.GetAttributes(path);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                continue;
            if ((attributes & FileAttributes.Directory) != 0)
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(path)))
                    Collect(path, files);
            }
            else if (Path.GetExtension(path).Equals(".rukbat", StringComparison.OrdinalIgnoreCase))
                files.Add(Path.GetFullPath(path));
        }
    }

    public string Read(string path)
    {
        if (!sources.TryGetValue(path, out var text))
            sources[path] = text = buffers.TryGetValue(path, out var buffer) ? buffer : File.ReadAllText(path);
        return text;
    }

    private LanguageAnalysis Analyze(string path)
    {
        if (!analyses.TryGetValue(path, out var analysis))
            analyses[path] = analysis = new(Read(path), path, Read, includeExamples: true);
        return analysis;
    }

    public EditorOccurrence? Target(int offset) => Analyze(entry).At(offset);

    private List<LanguageAnalysis> Related(EditorOccurrence target)
    {
        var local = target.Symbol.Kind == 9 || target.Symbol.Kind is 6 or 21 && !target.Symbol.IsModuleVariable;
        var declarationPath = target.Symbol.Module?.Source.Path;
        var result = new List<LanguageAnalysis>();
        foreach (var path in local ? [entry] : candidates)
        {
            var analysis = Analyze(path);
            // Even after a parser error, scan lexed import statements to avoid losing a caller.
            var related = EditorPaths.Comparer.Equals(path, entry) || declarationPath is null || analysis.ReferencedPaths.Contains(declarationPath) ||
                analysis.Occurrences.Any(o => o.Identity == target.Identity);
            if (!related && analysis.Diagnostics.Count != 0 && declarationPath is not null)
                related = CouldImport(path, declarationPath, new(EditorPaths.Comparer));
            if (!related)
                continue;
            var errors = analysis.Diagnostics.Concat(analysis.ExampleDiagnostics).ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException($"Cannot resolve all references: {errors[0]}");
            result.Add(analysis);
        }
        return result;
    }

    private bool CouldImport(string path, string target, HashSet<string> visited)
    {
        if (!visited.Add(path))
            return false;
        var source = new Source(Read(path), path);
        var tokens = new Lexer(source).Scan();
        if (source.Diagnostics.Count != 0)
            throw new InvalidOperationException($"Cannot determine imports in {path}: {source.Diagnostics[0].Message}");
        var module = Analyze(path).Modules.First(m => EditorPaths.Comparer.Equals(m.Source.Path, path));
        var scans = new List<List<Token>> { tokens };
        foreach (var text in module.Documentation.Texts)
            foreach (var code in DocumentationMarkdown.Parse(text).Descendants<Markdig.Syntax.FencedCodeBlock>().Where(DocumentationMarkdown.IsChecked))
            {
                var snippet = new Source(string.Join("\n", code.Lines.Lines.Take(code.Lines.Count).Select(l => l.ToString())), path);
                scans.Add(new Lexer(snippet).Scan());
                if (snippet.Diagnostics.Count != 0)
                    throw new InvalidOperationException($"Cannot determine example imports in {path}: {snippet.Diagnostics[0].Message}");
            }
        foreach (var scan in scans)
            for (var i = 0; i + 1 < scan.Count; i++)
            {
                if (scan[i].Text != "import" || scan[i + 1].StringValue is not { } relative ||
                    !(relative.StartsWith("./", StringComparison.Ordinal) || relative.StartsWith("../", StringComparison.Ordinal)))
                    continue;
                var imported = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, relative));
                if (EditorPaths.Comparer.Equals(imported, target) || CouldImport(imported, target, visited))
                    return true;
            }
        return false;
    }

    public EditorLocation[] References(int offset, bool includeDeclaration)
    {
        var target = Target(offset);
        if (target is null || !target.Searchable)
            return [];
        return Related(target).SelectMany(a => a.Occurrences)
            .Where(o => o.Searchable && !o.Module.Source.Path.StartsWith('<') && o.Identity == target.Identity && (includeDeclaration || !o.IsDeclaration))
            .Select(o => o.Location).Distinct().OrderBy(o => o.Uri, StringComparer.Ordinal)
            .ThenBy(o => o.Range.Start.Line).ThenBy(o => o.Range.Start.Character).ToArray();
    }

    private bool CanEdit(string path) => roots.Length == 0 ? EditorPaths.Comparer.Equals(path, entry) : roots.Any(r => EditorPaths.Contains(r, path));

    public EditorOccurrence PrepareRename(int offset)
    {
        var target = Target(offset);
        if (target is null || !target.Searchable || !target.Symbol.CanRename)
            throw new InvalidOperationException("Select a user-defined name to rename.");
        if (!CanEdit(target.Symbol.Module!.Source.Path))
            throw new InvalidOperationException("The declaration is outside the workspace. Open its folder before renaming it.");
        if (roots.Length == 0 && target.Symbol.Kind is 3 or 5 or 22)
            throw new InvalidOperationException("Open a workspace folder to find all callers before renaming an exported declaration.");
        _ = Related(target);
        return target;
    }

    public Dictionary<string, List<EditorReplacement>> Rename(int offset, string name)
    {
        var source = new Source(name, "<rename>");
        var tokens = new Lexer(source).Scan();
        if (source.Diagnostics.Count != 0 || tokens.Count != 2 || tokens[0].StringValue is not null || tokens[0].Text != name ||
            name.Length == 0 || !(char.IsLetter(name[0]) || name[0] == '_') || Parser.IsReservedWord(name))
            throw new ArgumentException("The new name must be one valid Rukbat identifier and cannot be a reserved word.");
        var target = PrepareRename(offset);
        if (target.Symbol.Name == name)
            return new(EditorPaths.Comparer);
        var before = Related(target);
        var edits = before.SelectMany(a => a.Occurrences).Where(o => o.Searchable && o.Identity == target.Identity)
            .DistinctBy(o => (EditorPaths.Key(o.Module.Source.Path), o.Token.Offset))
            .GroupBy(o => o.Module.Source.Path, EditorPaths.Comparer)
            .ToDictionary(g => g.Key, g => g.Select(o => new EditorReplacement(o.Token.Offset, o.Token.Text.Length, name)).OrderBy(e => e.Offset).ToList(), EditorPaths.Comparer);
        foreach (var path in edits.Keys)
        {
            if (!CanEdit(path))
                throw new InvalidOperationException($"Rename needs an edit outside the workspace: {path}");
        }
        // A linked dependency can alias an edited file under a second module path.
        // Refuse that graph rather than returning edits that only update one alias.
        foreach (var path in before.SelectMany(a => a.ReferencedPaths).Distinct(EditorPaths.Comparer))
        {
            for (var current = path; current is not null; current = Path.GetDirectoryName(current))
                if (File.Exists(current) || Directory.Exists(current))
                    if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException($"Rename cannot validate module aliases through links or junctions: {path}");
        }
        var changed = new Dictionary<string, string>(sources, EditorPaths.Comparer);
        foreach (var (path, replacements) in edits)
        {
            var text = Read(path);
            foreach (var edit in replacements.AsEnumerable().Reverse())
                text = text[..edit.Offset] + edit.Text + text[(edit.Offset + edit.Length)..];
            changed[path] = text;
        }
        string ReadChanged(string path) => changed.TryGetValue(path, out var text) ? text : Read(path);
        int MapOffset(string path, int original) => original + (edits.GetValueOrDefault(path) ?? [])
            .Where(e => e.Offset < original).Sum(e => e.Text.Length - e.Length);
        EditorSymbolId MapId(EditorSymbolId id) => id with
        {
            Offset = MapOffset(id.Path, id.Offset)
        };
        foreach (var analysis in before)
        {
            var path = analysis.Entry.Source.Path;
            var after = new LanguageAnalysis(ReadChanged(path), path, ReadChanged, includeExamples: true);
            var errors = after.Diagnostics.Concat(after.ExampleDiagnostics).ToArray();
            if (errors.Length != 0)
                throw new InvalidOperationException($"Rename would produce an error: {errors[0]}");
            foreach (var occurrence in analysis.Occurrences.Where(o => o.Searchable))
            {
                var position = MapOffset(occurrence.Module.Source.Path, occurrence.Token.Offset);
                var rebound = after.Occurrences.FirstOrDefault(o => o.Searchable && EditorPaths.Comparer.Equals(o.Module.Source.Path, occurrence.Module.Source.Path) && o.Token.Offset == position);
                if (rebound is null || rebound.Identity != MapId(occurrence.Identity))
                    throw new InvalidOperationException($"Rename would change the declaration referenced at {occurrence.Module.Source.Path}:{new EditorText(occurrence.Module.Source.Text).Position(occurrence.Token.Offset).Line + 1}.");
            }
        }
        // Check all sources used for validation, including closed dependencies, immediately before returning.
        if (!Discover().SetEquals(candidates))
            throw new InvalidOperationException("Source changed during rename. The workspace file list changed; retry the operation.");
        foreach (var (path, text) in sources)
            if (!buffers.ContainsKey(path) && File.ReadAllText(path) != text)
                throw new InvalidOperationException($"Source changed during rename. Retry the operation: {path}");
        return edits;
    }
}
