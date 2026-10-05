using System.Text.RegularExpressions;
using Markdig.Syntax;

namespace Rukbat;

internal sealed record EditorPosition(int Line, int Character);
internal sealed record EditorRange(EditorPosition Start, EditorPosition End);
internal sealed record EditorLocation(string Uri, EditorRange Range);

// LSP uses UTF-16 code units, as do .NET strings and the compiler's token offsets.
internal sealed class EditorText(string text)
{
    private readonly int[] lines = [0, .. text.Select((c, i) => (c, i)).Where(p => p.c == '\n').Select(p => p.i + 1)];
    public int Offset(EditorPosition position)
    {
        if (position.Line < 0 || position.Line >= lines.Length || position.Character < 0)
            throw new ArgumentException("Position is outside the document.");
        var end = position.Line + 1 < lines.Length ? lines[position.Line + 1] - 1 : text.Length;
        if (end > lines[position.Line] && text[end - 1] == '\r')
            end--;
        if (position.Character > end - lines[position.Line])
            throw new ArgumentException("Position is outside the line.");
        return lines[position.Line] + position.Character;
    }
    public EditorPosition Position(int offset)
    {
        offset = Math.Clamp(offset, 0, text.Length);
        var line = Array.BinarySearch(lines, offset);
        if (line < 0)
            line = ~line - 1;
        return new(line, offset - lines[line]);
    }
    public EditorRange Range(int start, int length) => new(Position(start), Position(start + length));
}

internal sealed record EditorSymbol(string Name, string Detail, int Kind, string Documentation = "",
    Module? Module = null, Token? Declaration = null, ValueType? Type = null)
{
    public object Completion => new
    {
        label = Name,
        kind = Kind,
        detail = Detail,
        documentation = new
        {
            kind = "markdown",
            value = $"```rukbat\n{Detail}\n```" + (Documentation.Length == 0 ? "" : "\n\n" + Documentation.Split("\n\n")[0])
        }
    };
    public string Markdown => $"```rukbat\n{Detail}\n```" + (Documentation.Length == 0 ? "" : "\n\n" + Documentation);
    public EditorLocation? Location => Module is null || Declaration is null || Module.Source.Path.StartsWith('<') ? null
        : new(new Uri(Module.Source.Path).AbsoluteUri, new EditorText(Module.Source.Text).Range(Declaration.Offset, Declaration.Text.Length));
}

internal static class EditorCatalog
{
    public static EditorSymbol HttpConstant(HttpConstant value) => new(value.Name, $"http.{value.Name}: {value.Type} = {value.Value}", 21,
        "Read-only HTTP constant.", Type: value.Type);
    public static EditorSymbol HttpType(ValueType value) => new(value.Name.Split('.')[1], value.Name, 13,
        value == HttpValues.Body ? "Immutable HTTP response bytes. Decode with http.text and an explicit encoding." : "A validated HTTP method. Use http.method_* constants.", Type: value);
    // Embed the canonical API documentation rather than maintain a second list of signatures.
    public static readonly EditorSymbol HashType = new("Algorithm", "hash.Algorithm", 13,
        "A validated checksum algorithm. Use hash.parse/name at external boundaries.", Type: HashValues.Algorithm);
    public static EditorSymbol HashValue(string name) => new(name, $"hash.{name}: hash.Algorithm", 20, HashValues.Description(name), Type: HashValues.Algorithm);
    public static readonly EditorSymbol EncodingType = new("Encoding", "encoding.Encoding", 13,
        "A validated encoding type. Use predefined encoding values or encoding.from_code_page for an explicit Windows code page.", Type: ValueType.Encoding);
    public static EditorSymbol EncodingValue(StandardEncoding value) => new(value.Name,
        $"encoding.{value.Name}: encoding.Encoding", 20, value.Documentation + " Read-only.", Type: ValueType.Encoding);
    public static EditorSymbol DateTimeType(ValueType value) => new(value.Name.Split('.')[1], value.Name, 13, DateTimeValues.Description(value), Type: value);
    public static EditorSymbol ZoneConstant(DateTimeValues.ZoneConstant value) => new(value.Name,
        $"datetime.{value.Name}: datetime.Zone", 21, value.Documentation, Type: DateTimeValues.Zone);
    public static readonly List<EditorSymbol> Standard = Load();
    public static readonly string[] Modules = Standard.Select(s => s.Detail.Split('.')[0]).Distinct().Order().ToArray();
    public static readonly string[] Keywords = ["import",
        "fn",
        "struct",
        "return",
        "if",
        "else",
        "for",
        "in",
        "while",
        "break",
        "continue",
        "try",
        "catch",
        "throw",
        "true",
        "false",
        "null",
        "int",
        "float",
        "string",
        "bool",
        "void"];
    public static readonly EditorSymbol[] Builtins =
    [
        new("print", "print(value): void", 3, "Writes one value and a newline to standard output."),
        new("eprint", "eprint(value): void", 3, "Writes one value and a newline to standard error."),
        new("args", "args(): string[]", 3, "Returns the entry program's arguments."),
        new("exit", "exit(code: int): void", 3, "Terminates the program with a nonnegative exit code."),
        new("powershell", "powershell<T>(command: string, ...): T", 3, "Explicit PowerShell escape hatch; validates the returned value against T."),
        new("Error", "struct Error { message: string }", 22, "The ordinary runtime error value.")
    ];
    private static List<EditorSymbol> Load()
    {
        using var stream = typeof(EditorCatalog).Assembly.GetManifestResourceStream("Rukbat.StandardLibrary.md")!;
        using var reader = new StreamReader(stream);
        var result = new List<EditorSymbol>();
        foreach (var line in reader.ReadToEnd().Split('\n'))
        {
            var match = Regex.Match(line, @"^\| `(?<signature>(?<module>[a-z]+)\.(?<name>[a-z_]+)(?:<T>)?\([^`]+)` \| (?<docs>.*)");
            if (!match.Success)
                continue;
            var docs = match.Groups["docs"].Value.Trim().TrimEnd('|').Trim().Replace(" | ", "\n\n");
            result.Add(new(match.Groups["name"].Value, match.Groups["signature"].Value, 3, docs));
        }
        // Zero-argument signatures are covered by the same expression (the ')' is nonempty).
        result.AddRange(HashValues.All.Select(HashValue));
        result.AddRange(HttpValues.Constants.Select(HttpConstant));
        result.AddRange(EncodingValues.All.Select(EncodingValue));
        result.AddRange(DateTimeValues.Zones.Select(ZoneConstant));
        return result;
    }
}

internal sealed class LanguageAnalysis
{
    private sealed record Occurrence(Module Module, Token Token, EditorSymbol Symbol);
    private sealed class Scope(Module module, int start, int end, Scope? parent = null, bool function = false)
    {
        public Module Module { get; } = module;
        public int Start { get; } = start;
        public int End { get; } = end;
        public Scope? Parent { get; } = parent;
        public bool Function { get; } = function;
        public List<EditorSymbol> Symbols { get; } = [];
        public IEnumerable<EditorSymbol> Visible(int offset, bool insideFunction = false)
        {
            insideFunction |= Function;
            var own = Symbols.Where(s => s.Kind != 6 || s.Declaration!.Offset < offset || Parent is null && insideFunction).ToList();
            return own.Concat(Parent?.Visible(offset, insideFunction) ?? []).DistinctBy(s => s.Name);
        }
    }
    private readonly Dictionary<object, EditorSymbol> symbols = new(ReferenceEqualityComparer.Instance);
    private readonly List<Occurrence> occurrences = [];
    private readonly List<Scope> scopes = [];
    private readonly TypeChecker? checker;
    private readonly DocumentationCatalog documentation;
    public IReadOnlyList<Diagnostic> DocumentationDiagnostics { get; }
    public List<Module> Modules
    {
        get;
    }
    public IReadOnlyList<Diagnostic> Diagnostics
    {
        get;
    }
    public Module Entry => Modules[^1];

    public LanguageAnalysis(string text, string path, Func<string, string> readSource)
    {
        var program = Compiler.CheckCore(text, path, true, readSource, collectSyntax: true);
        Modules = program.Modules!;
        checker = program.Checker;
        documentation = new DocumentationCatalog(Modules, Path.GetDirectoryName(path)!);
        var documentationDiagnostics = new List<Diagnostic>();
        foreach (var module in Modules.Where(m => m.StandardName is null))
        {
            if (!module.Source.Diagnostics.Any(d => d.Code == "RUK1000"))
                documentationDiagnostics.AddRange(module.Documentation.Diagnostics);
            if (program.Diagnostics.Count == 0)
            {
                var texts = module.Documentation.Comments.Values.AsEnumerable();
                if (module.Documentation.Overview is { } overview)
                    texts = texts.Prepend(overview);
                foreach (var docText in texts)
                    foreach (var reference in DocumentationMarkdown.Parse(docText).Descendants<DocumentationReference>())
                        if (documentation.Resolve(module, reference.Name) is null)
                            documentationDiagnostics.Add(docText.Error("RUK6001", $"Unknown documentation reference '{reference.Name}'.", reference.Span.Start));
            }
        }
        DocumentationDiagnostics = documentationDiagnostics;
        if (checker is not null)
        {
            RegisterDefinitions();
            foreach (var module in Modules)
            {
                var root = new Scope(module, 0, module.Source.Text.Length);
                scopes.Add(root);
                foreach (var function in module.Functions.Values)
                    root.Symbols.Add(symbols[checker.Functions[function]]);
                foreach (var structure in module.Structs.Values)
                    root.Symbols.Add(symbols[structure]);
                foreach (var import in module.Statements.OfType<Import>())
                {
                    var symbol = new EditorSymbol(import.Name, $"import {import.Path}", 9,
                        module.Imports[import.Name].Documentation.Overview?.Markdown ?? "", Module: module, Declaration: import.Token);
                    root.Symbols.Add(symbol);
                    Add(module, import.Token, symbol);
                }
                WalkStatements(module.Statements.Where(s => s is not Function and not StructDeclaration).ToList(), root);
                WalkStatements(module.Statements.Where(s => s is Function or StructDeclaration).ToList(), root);
                AddTypeReferences(module);
            }
        }
        Diagnostics = program.Diagnostics;
    }

    private void RegisterDefinitions()
    {
        foreach (var function in checker!.Functions.Values)
        {
            var parameters = string.Join(", ", function.Function.Parameters.Select(p => $"{p.Token.Text}: {checker.DeclaredTypes[p]}"));
            symbols[function] = new(function.Function.Name, $"fn {function.Function.Name}({parameters}): {function.ReturnType}", 3,
                Description(function.Module, function.Function),
                Module: function.Module, Declaration: function.Function.Token);
        }
        foreach (var structure in Modules.SelectMany(m => m.Structs.Values).Append(checker.ErrorStructure))
        {
            symbols[structure] = new(structure.Declaration.Token.Text, $"struct {structure.Type}", 22,
                Description(structure.Module, structure.Declaration),
                Module: structure.Module, Declaration: structure.Declaration.Token, Type: structure.BoundType);
            foreach (var field in structure.Fields.Values)
                symbols[field] = new(field.Field.Token.Text, $"{field.Field.Token.Text}: {field.Type}", 5,
                    Description(structure.Module, field.Field),
                    Module: structure.Module, Declaration: field.Field.Token, Type: field.Type);
        }
        foreach (var (node, variable) in checker.Variables.Where(p => p.Key is Variable or Parameter or For or Catch))
        {
            // The module is attached when its declaration is visited below.
            symbols[variable] = new(variable.Name, $"{variable.Name}: {variable.Type}", 6,
                Declaration: node.Token, Type: variable.Type);
        }
    }

    private string Description(Module module, Node node)
    {
        if (!module.Documentation.Comments.TryGetValue(node, out var text))
            return "";
        var markdown = text.Markdown;
        foreach (var reference in DocumentationMarkdown.Parse(text).Descendants<DocumentationReference>().OrderByDescending(r => r.Span.Start))
        {
            var target = documentation.Resolve(module, reference.Name);
            if (target is null)
                continue;
            var line = new EditorText(target.Module.Source.Text).Position(target.Declaration.Node.Token.Offset).Line + 1;
            var link = $"[`{reference.Name}`]({new Uri(target.Module.Source.Path).AbsoluteUri}#L{line})";
            markdown = markdown[..reference.Span.Start] + link + markdown[(reference.Span.End + 1)..];
        }
        return markdown;
    }

    private Scope Block(List<Statement> body, Scope parent, bool function = false)
    {
        var span = parent.Module.Blocks?.GetValueOrDefault(body) ?? (parent.Start, parent.End);
        var scope = new Scope(parent.Module, span.Item1, span.Item2, parent, function);
        scopes.Add(scope);
        return scope;
    }
    private void Declare(Node node, Scope scope)
    {
        if (!checker!.Variables.TryGetValue(node, out var variable))
            return;
        var symbol = symbols[variable] with
        {
            Module = scope.Module,
            Documentation = Description(scope.Module, node)
        };
        symbols[variable] = symbol;
        scope.Symbols.Add(symbol);
        Add(scope.Module, node.Token, symbol);
    }
    private void Add(Module module, Token token, EditorSymbol symbol) => occurrences.Add(new(module, token, symbol));
    private void WalkStatements(List<Statement> statements, Scope scope)
    {
        foreach (var statement in statements)
        {
            switch (statement)
            {
                case Function function:
                    Add(scope.Module, function.Token, symbols[checker!.Functions[function]]);
                    var local = Block(function.Body, scope, function: true);
                    if (checker.Functions[function].Owner is { } owner)
                        local.Symbols.Add(new("self", $"self: {owner.Type}", 6, Module: scope.Module, Declaration: function.Token, Type: owner.BoundType));
                    foreach (var parameter in function.Parameters)
                        Declare(parameter, local);
                    WalkStatements(function.Body, local);
                    break;
                case StructDeclaration structure:
                    if (!scope.Module.Structs.TryGetValue(structure.Token.Text, out var bound))
                        break;
                    Add(scope.Module, structure.Token, symbols[bound]);
                    foreach (var field in bound.Fields.Values)
                        Add(scope.Module, field.Field.Token, symbols[field]);
                    WalkStatements(structure.Methods.Cast<Statement>().ToList(), scope);
                    break;
                case Variable variable:
                    Walk(variable.Value, scope);
                    Declare(variable, scope);
                    break;
                case Assignment assignment:
                    if (checker!.Variables.TryGetValue(assignment, out var target) && symbols.TryGetValue(target, out var targetSymbol))
                        Add(scope.Module, assignment.Token, targetSymbol);
                    Walk(assignment.Value, scope);
                    break;
                case FieldAssignment assignment:
                    Walk(assignment.Target, scope);
                    Walk(assignment.Value, scope);
                    break;
                case IndexAssignment assignment:
                    Walk(assignment.Target, scope);
                    Walk(assignment.Value, scope);
                    break;
                case ExpressionStatement expression:
                    Walk(expression.Value, scope);
                    break;
                case Return result when result.Value is not null:
                    Walk(result.Value, scope);
                    break;
                case Throw failure:
                    Walk(failure.Value, scope);
                    break;
                case For loop:
                    Walk(loop.Items, scope);
                    var loopScope = Block(loop.Body, scope);
                    Declare(loop, loopScope);
                    WalkStatements(loop.Body, loopScope);
                    break;
                case While loop:
                    Walk(loop.Condition, scope);
                    WalkStatements(loop.Body, Block(loop.Body, scope));
                    break;
                case Conditional condition:
                    Walk(condition.Condition, scope);
                    WalkStatements(condition.Then, Block(condition.Then, scope));
                    WalkStatements(condition.Else, Block(condition.Else, scope));
                    break;
                case TryCatch guarded:
                    WalkStatements(guarded.Body, Block(guarded.Body, scope));
                    var handler = Block(guarded.Handler.Body, scope);
                    Declare(guarded.Handler, handler);
                    WalkStatements(guarded.Handler.Body, handler);
                    break;
            }
        }
    }
    private void Walk(Expression expression, Scope scope)
    {
        switch (expression)
        {
            case Name name:
                if (checker!.Variables.TryGetValue(name, out var variable))
                {
                    if (symbols.TryGetValue(variable, out var symbol))
                    {
                        var type = checker.Types.GetValueOrDefault(name, variable.Type);
                        Add(scope.Module, name.Token, symbol with
                        {
                            Detail = $"{name.Token.Text}: {type}",
                            Type = type
                        });
                    }
                    else if (name.Token.Text == "self")
                        Add(scope.Module, name.Token, scope.Visible(name.Token.Offset).First(s => s.Name == "self"));
                }
                else if (scope.Module.Imports.ContainsKey(name.Token.Text))
                    Add(scope.Module, name.Token, scopes.First(s => s.Module == scope.Module && s.Parent is null).Symbols.First(s => s.Name == name.Token.Text));
                break;
            case Member member:
                Walk(member.Receiver, scope);
                if (checker!.ZoneConstants.TryGetValue(member, out var zone))
                    Add(scope.Module, member.Token, EditorCatalog.ZoneConstant(zone));
                else if (checker.EncodingConstants.TryGetValue(member, out var encoding))
                    Add(scope.Module, member.Token, EditorCatalog.EncodingValue(encoding));
                else if (checker.HashConstants.TryGetValue(member, out var algorithm))
                    Add(scope.Module, member.Token, EditorCatalog.HashValue(algorithm));
                else if (checker.HttpConstants.TryGetValue(member, out var httpConstant))
                    Add(scope.Module, member.Token, EditorCatalog.HttpConstant(httpConstant));
                else if (checker.Fields.TryGetValue(member, out var field))
                    Add(scope.Module, member.Token, symbols[field]);
                else if (member.Name == "length" && checker.Types.GetValueOrDefault(member.Receiver)?.Element is not null)
                    Add(scope.Module, member.Token, new("length", "length: int", 5, "The number of array elements."));
                break;
            case Call call:
                if (call.Receiver is not null)
                    Walk(call.Receiver, scope);
                if (checker!.Calls.TryGetValue(call, out var function))
                    Add(scope.Module, call.Token, symbols[function]);
                else if (checker.Constructions.TryGetValue(call, out var structure))
                    Add(scope.Module, call.Token, symbols[structure]);
                else if (checker.StandardCalls.TryGetValue(call, out var standard))
                {
                    var candidates = EditorCatalog.Standard.Where(s => s.Detail.StartsWith(standard + "(", StringComparison.Ordinal) || s.Detail.StartsWith(standard + "<", StringComparison.Ordinal));
                    var signature = candidates.FirstOrDefault(s => s.Detail.Contains("<T>") == (call.TypeArgument is not null)) ?? candidates.FirstOrDefault();
                    if (signature is not null)
                        Add(scope.Module, call.Token, signature);
                }
                else if (call.Receiver is null && EditorCatalog.Builtins.FirstOrDefault(s => s.Name == call.Name) is { } builtin)
                    Add(scope.Module, call.Token, builtin);
                else if (checker.ArrayAppends.Contains(call))
                    Add(scope.Module, call.Token, new("append", $"append(value: {checker.Types[call.Receiver!].Element}): void", 3));
                foreach (var argument in call.Arguments)
                {
                    if (checker.ConstructionFields.TryGetValue(argument, out var argumentField))
                        Add(scope.Module, argument.Token, symbols[argumentField]);
                    Walk(argument.Value, scope);
                }
                break;
            case Binary binary:
                Walk(binary.Left, scope);
                Walk(binary.Right, scope);
                break;
            case Unary unary:
                Walk(unary.Value, scope);
                break;
            case ArrayLiteral array:
                foreach (var item in array.Items)
                    Walk(item, scope);
                break;
            case Index index:
                Walk(index.Receiver, scope);
                Walk(index.Offset, scope);
                break;
        }
    }

    private void AddTypeReferences(Module module)
    {
        if (module.TypeTokens is null)
            return;
        foreach (var (syntaxType, token) in module.TypeTokens)
        {
            // Match the actual parsed type node to its checked type, preserving module identity.
            var bound = checker!.DeclaredTypes.FirstOrDefault(p => ReferenceEquals(syntaxType, p.Key switch
            {
                Function function => function.Type,
                Parameter parameter => parameter.Type,
                Field field => field.Type,
                Variable variable => variable.Type,
                Call call => call.TypeArgument ?? call.PowerShellType,
                _ => null
            })).Value;
            while (bound is not null && (bound.NullableElement is not null || bound.Element is not null))
                bound = bound.NullableElement ?? bound.Element;
            if (bound?.Struct is { } structure)
                Add(module, token, symbols[structure]);
            else if (bound is not null && DateTimeValues.Contains(bound))
                Add(module, token, EditorCatalog.DateTimeType(bound));
            else if (bound == HashValues.Algorithm)
                Add(module, token, EditorCatalog.HashType);
            else if (bound is not null && HttpValues.Contains(bound))
                Add(module, token, EditorCatalog.HttpType(bound));
            else if (bound == ValueType.Encoding)
                Add(module, token, EditorCatalog.EncodingType);
        }
    }

    private Occurrence? At(int offset) => occurrences.FirstOrDefault(o => o.Module == Entry && offset >= o.Token.Offset && offset < o.Token.Offset + o.Token.Text.Length);
    public object? Hover(int offset)
    {
        var found = At(offset);
        if (found is null)
        {
            // Keep documentation at parsed declarations when imports cannot bind.
            // Do not guess the identities of unresolved references.
            var declaration = Entry.Documentation.Declarations.FirstOrDefault(d =>
                offset >= d.Node.Token.Offset && offset < d.Node.Token.Offset + d.Node.Token.Text.Length);
            if (declaration is not null && Entry.Documentation.Comments.ContainsKey(declaration.Node))
                found = new(Entry, declaration.Node.Token, new(declaration.Name, declaration.Name, 3,
                    Description(Entry, declaration.Node), Module: Entry, Declaration: declaration.Node.Token));
        }
        return found is null ? null : new
        {
            contents = new
            {
                kind = "markdown",
                value = found.Symbol.Markdown
            },
            range = new EditorText(Entry.Source.Text).Range(found.Token.Offset, found.Token.Text.Length)
        };
    }
    public EditorLocation? Definition(int offset) => At(offset)?.Symbol.Location;

    public IEnumerable<EditorSymbol> Complete(int offset, string? receiver, string prefix)
    {
        var scope = scopes.Where(s => s.Module == Entry && s.Start <= offset && (offset < s.End || s.Parent is null && offset == s.End)).OrderBy(s => s.End - s.Start).FirstOrDefault();
        IEnumerable<EditorSymbol> candidates;
        if (receiver is not null)
        {
            if (Entry.Imports.TryGetValue(receiver, out var imported))
            {
                candidates = imported.StandardName is { } standard
                    ? EditorCatalog.Standard.Where(s => s.Detail.StartsWith(standard + ".", StringComparison.Ordinal)).Concat(imported.Structs.Values.Select(s => symbols[s]))
                    : imported.Functions.Values.Select(f => symbols[checker!.Functions[f]]).Concat(imported.Structs.Values.Select(s => symbols[s]));
                if (imported.StandardName == "hash")
                    candidates = candidates.Append(EditorCatalog.HashType);
                if (imported.StandardName == "http")
                    candidates = candidates.Append(EditorCatalog.HttpType(HttpValues.Method)).Append(EditorCatalog.HttpType(HttpValues.Body));
                if (imported.StandardName == "encoding")
                    candidates = candidates.Append(EditorCatalog.EncodingType);
                if (imported.StandardName == "datetime")
                    candidates = candidates.Concat(DateTimeValues.All.Select(EditorCatalog.DateTimeType));
            }
            else
            {
                var dot = offset - prefix.Length - 1;
                while (dot >= 0 && char.IsWhiteSpace(Entry.Source.Text[dot]))
                    dot--;
                var receiverEnd = dot;
                while (receiverEnd > 0 && char.IsWhiteSpace(Entry.Source.Text[receiverEnd - 1]))
                    receiverEnd--;
                var hole = checker?.Types.Keys.OfType<Member>().FirstOrDefault(m => m.Token.Text == "__completion" && m.Token.Offset == offset - prefix.Length);
                var type = hole is not null ? checker!.Types.GetValueOrDefault(hole.Receiver)
                    : occurrences.LastOrDefault(o => o.Module == Entry && o.Token.Offset + o.Token.Text.Length == receiverEnd)?.Symbol.Type
                    ?? scope?.Visible(offset).FirstOrDefault(s => s.Name == receiver)?.Type;
                candidates = type?.Struct is { } structure
                    ? structure.Fields.Values.Select(f => symbols[f]).Concat(structure.Methods.Values.Select(f => symbols[f]))
                    : type?.Element is { } element
                        ? [new("length", "length: int", 5), new("append", $"append(value: {element}): void", 3)] : [];
            }
        }
        else
            candidates = (scope?.Visible(offset) ?? []).Concat(EditorCatalog.Builtins).Concat(EditorCatalog.Keywords.Select(k => new EditorSymbol(k, k, 14)));
        return candidates.Where(s => s.Name.StartsWith(prefix, StringComparison.Ordinal)).DistinctBy(s => s.Name).OrderBy(s => s.Name);
    }
}
