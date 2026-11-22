using Markdig.Syntax;

namespace Rukbat
{
    internal sealed partial class LanguageAnalysis
    {
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
                var own = Symbols.Where(s => s.Kind is not (CompletionKind.Variable or CompletionKind.Constant) || s.AvailableFrom < offset || Parent is null && insideFunction).ToList();
                return own.Concat(Parent?.Visible(offset, insideFunction) ?? []).DistinctBy(s => s.Name);
            }
        }
        private readonly Dictionary<object, EditorSymbol> symbols = new(ReferenceEqualityComparer.Instance);
        private readonly List<EditorOccurrence> occurrences = [];
        public IReadOnlyList<EditorOccurrence> Occurrences => occurrences;
        private readonly List<Scope> scopes = [];
        private readonly TypeChecker? checker;
        private readonly DocumentationCatalog documentation;
        public IReadOnlyList<Diagnostic> DocumentationDiagnostics { get; }
        public List<Diagnostic> ExampleDiagnostics { get; } = [];
        public HashSet<string> ReferencedPaths { get; } = new(EditorPaths.Comparer);
        public List<Module> Modules
        {
            get;
        }
        public IReadOnlyList<Diagnostic> Diagnostics
        {
            get;
        }
        public Module Entry => Modules[^1];

        public LanguageAnalysis(string text, string path, Func<string, string> readSource, bool includeExamples = false)
        {
            var program = Compiler.CheckCore(text, path, true, readSource, collectSyntax: true);
            Modules = program.Modules!;
            ReferencedPaths.UnionWith(Modules.Where(m => m.StandardName is null).Select(m => m.Source.Path));
            checker = program.Checker;
            documentation = new DocumentationCatalog(Modules, Path.GetDirectoryName(path)!);
            var documentationDiagnostics = new List<Diagnostic>();
            foreach (var module in Modules.Where(m => m.StandardName is null))
            {
                if (!module.Source.Diagnostics.Any(d => d.Code == DiagnosticCode.SyntaxError))
                {
                    documentationDiagnostics.AddRange(module.Documentation.Diagnostics);
                }

                if (program.Diagnostics.Count == 0)
                {
                    foreach (var docText in module.Documentation.Texts)
                    {
                        documentationDiagnostics.AddRange(documentation.ValidateReferences(module, docText, DocumentationMarkdown.Parse(docText)));
                    }
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
                    {
                        root.Symbols.Add(symbols[checker.Functions[function]]);
                    }

                    foreach (var structure in module.Structs.Values)
                    {
                        root.Symbols.Add(symbols[structure]);
                    }

                    foreach (var enumeration in module.Enums.Values)
                    {
                        root.Symbols.Add(symbols[enumeration]);
                    }

                    foreach (var import in module.Statements.OfType<Import>())
                    {
                        var symbol = new EditorSymbol(import.Name, $"import {import.Path}", CompletionKind.Module,
                            module.Imports[import.Name].Documentation.Overview?.Markdown ?? "", Module: module, Declaration: import.NameToken, IsFixed: import.IsStandard);
                        root.Symbols.Add(symbol);
                        Add(module, import.NameToken, symbol);
                        // Preserve the existing module hover on the import keyword, but never edit it.
                        occurrences.Add(new(module, import.Token, symbol, Searchable: false));
                    }
                    WalkStatements(module.Statements.Where(s => s is not Function and not StructDeclaration and not EnumDeclaration and not ImplDeclaration).ToList(), root);
                    WalkStatements(module.Statements.Where(s => s is Function or StructDeclaration or EnumDeclaration or ImplDeclaration).ToList(), root);
                    AddTypeReferences(module);
                }
                if (program.Diagnostics.Count == 0)
                {
                    AddDocumentationReferences();
                    if (includeExamples)
                    {
                        AddCheckedExamples(readSource);
                    }
                }
            }
            Diagnostics = program.Diagnostics;
        }

        private void RegisterDefinitions()
        {
            foreach (var function in checker!.Functions.Values)
            {
                var parameters = string.Join(", ", function.Function.Parameters.Select(p => $"{p.Token.Text}: {checker.DeclaredTypes[p]}"));
                var result = function.ReturnType == ValueType.Void ? "" : $": {function.ReturnType}";
                symbols[function] = new(function.Function.Name, $"fn {function.Function.Name}({parameters}){result}", CompletionKind.Function,
                    Description(function.Module, function.Function),
                    Module: function.Module, Declaration: function.Function.Token);
            }
            foreach (var structure in Modules.SelectMany(m => m.Structs.Values).Append(checker.ErrorStructure))
            {
                symbols[structure] = new(structure.Declaration.Token.Text, $"struct {structure.Type}", CompletionKind.Struct,
                    Description(structure.Module, structure.Declaration),
                    Module: structure.Module, Declaration: structure.Declaration.Token, Type: structure.BoundType);
                foreach (var field in structure.Fields.Values)
                {
                    symbols[field] = new(field.Field.Token.Text, $"{field.Field.Token.Text}: {field.Type}", CompletionKind.Field,
                        Description(structure.Module, field.Field),
                        Module: structure.Module, Declaration: field.Field.Token, Type: field.Type);
                }
            }
            foreach (var enumeration in Modules.SelectMany(m => m.Enums.Values))
            {
                var declaration = enumeration.Declaration;
                symbols[enumeration] = new(declaration.Token.Text, $"enum {enumeration.Type}", CompletionKind.Enum,
                    Description(enumeration.Module, declaration), Module: enumeration.Module, Declaration: declaration.Token);
                foreach (var member in enumeration.Members.Values)
                {
                    symbols[member] = new(member.Token.Text, $"{enumeration.Type}.{member.Token.Text}: {enumeration.Type}", CompletionKind.EnumMember,
                        Description(enumeration.Module, member), Module: enumeration.Module, Declaration: member.Token, Type: enumeration.Type);
                }
            }

            foreach (var (node, variable) in checker.Variables.Where(p => p.Key is Variable or Parameter or For or Catch))
            {
                // The module is attached when its declaration is visited below.
                symbols[variable] = new(variable.Name, $"{(variable.IsConst ? "const " : "")}{variable.Name}: {variable.Type}", variable.IsConst ? CompletionKind.Constant : CompletionKind.Variable,
                    Declaration: node.Token, Type: variable.Type, IsModuleVariable: variable.IsGlobal);
            }
        }

        private string Description(Module module, Node node)
        {
            if (!module.Documentation.Comments.TryGetValue(node, out var text))
            {
                return "";
            }

            var markdown = text.Markdown;
            foreach (var reference in DocumentationMarkdown.Parse(text).Descendants<DocumentationReference>().OrderByDescending(r => r.Span.Start))
            {
                var target = documentation.Resolve(module, reference.Name);
                if (target is null)
                {
                    continue;
                }

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
            {
                return;
            }

            var symbol = symbols[variable] with
            {
                Module = scope.Module,
                Documentation = Description(scope.Module, node),
                AvailableFrom = node is Variable declaration ? declaration.InitializerEndOffset : node.Token.Offset
            };
            symbols[variable] = symbol;
            scope.Symbols.Add(symbol);
            Add(scope.Module, node.Token, symbol);
        }
        private void Add(Module module, Token token, EditorSymbol symbol) => occurrences.Add(new(module, token, symbol));
        private readonly record struct VisitContext(LanguageAnalysis Analysis, Scope Scope);

        private static readonly NodeHandlers<VisitContext> StatementVisitors = new(
            NodeHandlers<VisitContext>.For<Function>(static (context, node) => context.Analysis.VisitFunction(node, context.Scope)),
            NodeHandlers<VisitContext>.For<EnumDeclaration>(static (context, node) => context.Analysis.VisitEnumDeclaration(node, context.Scope)),
            NodeHandlers<VisitContext>.For<StructDeclaration>(static (context, node) => context.Analysis.VisitStructDeclaration(node, context.Scope)),
            NodeHandlers<VisitContext>.For<ImplDeclaration>(static (context, node) => context.Analysis.VisitImplDeclaration(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Variable>(static (context, node) => context.Analysis.VisitVariable(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Assignment>(static (context, node) => context.Analysis.VisitAssignment(node, context.Scope)),
            NodeHandlers<VisitContext>.For<FieldAssignment>(static (context, node) => context.Analysis.VisitFieldAssignment(node, context.Scope)),
            NodeHandlers<VisitContext>.For<IndexAssignment>(static (context, node) => context.Analysis.VisitIndexAssignment(node, context.Scope)),
            NodeHandlers<VisitContext>.For<ExpressionStatement>(static (context, node) => context.Analysis.VisitExpressionStatement(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Return>(static (context, node) => context.Analysis.VisitReturn(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Throw>(static (context, node) => context.Analysis.VisitThrow(node, context.Scope)),
            NodeHandlers<VisitContext>.For<For>(static (context, node) => context.Analysis.VisitFor(node, context.Scope)),
            NodeHandlers<VisitContext>.For<While>(static (context, node) => context.Analysis.VisitWhile(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Conditional>(static (context, node) => context.Analysis.VisitConditional(node, context.Scope)),
            NodeHandlers<VisitContext>.For<TryCatch>(static (context, node) => context.Analysis.VisitTryCatch(node, context.Scope)));

        private static readonly NodeHandlers<VisitContext> ExpressionVisitors = new(
            NodeHandlers<VisitContext>.For<Name>(static (context, node) => context.Analysis.VisitName(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Member>(static (context, node) => context.Analysis.VisitMember(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Call>(static (context, node) => context.Analysis.VisitCall(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Binary>(static (context, node) => context.Analysis.VisitBinary(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Unary>(static (context, node) => context.Analysis.VisitUnary(node, context.Scope)),
            NodeHandlers<VisitContext>.For<ArrayLiteral>(static (context, node) => context.Analysis.VisitArrayLiteral(node, context.Scope)),
            NodeHandlers<VisitContext>.For<DictionaryLiteral>(static (context, node) => context.Analysis.VisitDictionaryLiteral(node, context.Scope)),
            NodeHandlers<VisitContext>.For<Index>(static (context, node) => context.Analysis.VisitIndex(node, context.Scope)));

        private void WalkStatements(List<Statement> statements, Scope scope)
        {
            foreach (var statement in statements)
            {
                StatementVisitors.DispatchIfRegistered(new(this, scope), statement);
            }
        }

        private void Walk(Expression expression, Scope scope) => ExpressionVisitors.DispatchIfRegistered(new(this, scope), expression);

        private void VisitFunction(Function function, Scope scope)
        {
            Add(scope.Module, function.Token, symbols[checker!.Functions[function]]);
            var local = Block(function.Body, scope, function: true);
            if (checker.Functions[function].Owner is { } owner)
            {
                local.Symbols.Add(new("self", $"self: {owner.Type}", CompletionKind.Variable, Module: scope.Module, Declaration: function.Token, Type: owner.BoundType));
            }

            foreach (var parameter in function.Parameters)
            {
                Declare(parameter, local);
            }

            WalkStatements(function.Body, local);
        }

        private void VisitEnumDeclaration(EnumDeclaration enumeration, Scope scope)
        {
            if (scope.Module.Enums.TryGetValue(enumeration.Token.Text, out var boundEnum) && boundEnum.Declaration == enumeration)
            {
                Add(scope.Module, enumeration.Token, symbols[boundEnum]);
                foreach (var member in boundEnum.Members.Values)
                {
                    Add(scope.Module, member.Token, symbols[member]);
                }
            }
        }

        private void VisitStructDeclaration(StructDeclaration structure, Scope scope)
        {
            if (!scope.Module.Structs.TryGetValue(structure.Token.Text, out var bound))
            {
                return;
            }

            Add(scope.Module, structure.Token, symbols[bound]);
            foreach (var field in bound.Fields.Values)
            {
                Add(scope.Module, field.Field.Token, symbols[field]);
            }
        }

        private void VisitImplDeclaration(ImplDeclaration implementation, Scope scope)
        {
            if (!checker!.Implementations.TryGetValue(implementation, out var implemented))
            {
                return;
            }

            Add(scope.Module, implementation.Token, symbols[implemented]);
            WalkStatements(implementation.Methods.Cast<Statement>().ToList(), scope);
        }

        private void VisitVariable(Variable variable, Scope scope)
        {
            Walk(variable.Value, scope);
            Declare(variable, scope);
        }

        private void VisitAssignment(Assignment assignment, Scope scope)
        {
            if (checker!.Variables.TryGetValue(assignment, out var target) && symbols.TryGetValue(target, out var targetSymbol))
            {
                Add(scope.Module, assignment.Token, targetSymbol);
            }

            Walk(assignment.Value, scope);
        }

        private void VisitFieldAssignment(FieldAssignment assignment, Scope scope)
        {
            Walk(assignment.Target, scope);
            Walk(assignment.Value, scope);
        }

        private void VisitIndexAssignment(IndexAssignment assignment, Scope scope)
        {
            Walk(assignment.Target, scope);
            Walk(assignment.Value, scope);
        }

        private void VisitExpressionStatement(ExpressionStatement expression, Scope scope)
        {
            Walk(expression.Value, scope);
        }

        private void VisitReturn(Return result, Scope scope)
        {
            if (result.Value is { } value)
            {
                Walk(value, scope);
            }
        }

        private void VisitThrow(Throw failure, Scope scope)
        {
            Walk(failure.Value, scope);
        }

        private void VisitFor(For loop, Scope scope)
        {
            Walk(loop.Items, scope);
            var loopScope = Block(loop.Body, scope);
            Declare(loop, loopScope);
            WalkStatements(loop.Body, loopScope);
        }

        private void VisitWhile(While loop, Scope scope)
        {
            Walk(loop.Condition, scope);
            WalkStatements(loop.Body, Block(loop.Body, scope));
        }

        private void VisitConditional(Conditional condition, Scope scope)
        {
            Walk(condition.Condition, scope);
            WalkStatements(condition.Then, Block(condition.Then, scope));
            WalkStatements(condition.Else, Block(condition.Else, scope));
        }

        private void VisitTryCatch(TryCatch guarded, Scope scope)
        {
            WalkStatements(guarded.Body, Block(guarded.Body, scope));
            var handler = Block(guarded.Handler.Body, scope);
            Declare(guarded.Handler, handler);
            WalkStatements(guarded.Handler.Body, handler);
        }

        private void VisitName(Name name, Scope scope)
        {
            if (checker!.Variables.TryGetValue(name, out var variable))
            {
                if (symbols.TryGetValue(variable, out var symbol))
                {
                    var type = checker.Types.GetValueOrDefault(name, variable.Type);
                    Add(scope.Module, name.Token, symbol with
                    {
                        Detail = $"{(variable.IsConst ? "const " : "")}{name.Token.Text}: {type}",
                        Type = type
                    });
                }
                else if (name.Token.Text == "self")
                {
                    Add(scope.Module, name.Token, scope.Visible(name.Token.Offset).First(s => s.Name == "self"));
                }
            }
            else if (scope.Module.Imports.ContainsKey(name.Token.Text))
            {
                Add(scope.Module, name.Token, scopes.First(s => s.Module == scope.Module && s.Parent is null).Symbols.First(s => s.Name == name.Token.Text));
            }
        }

        private static readonly IReadOnlyDictionary<MemberKind, Func<LanguageAnalysis, BoundMember, EditorSymbol>> MemberReaders =
            new Dictionary<MemberKind, Func<LanguageAnalysis, BoundMember, EditorSymbol>>
            {
                [MemberKind.EnumMember] = static (analysis, binding) => analysis.symbols[binding.EnumMember!],
                [MemberKind.StandardConstant] = static (_, binding) => EditorCatalog.Constant(binding.Constant!),
                [MemberKind.Field] = static (analysis, binding) => analysis.symbols[binding.Field!],
                [MemberKind.ArrayLength] = static (_, _) => EditorCatalog.ArrayLength,
                [MemberKind.DictionaryLength] = static (_, _) => EditorCatalog.DictionaryLength
            };

        private void VisitMember(Member member, Scope scope)
        {
            Walk(member.Receiver, scope);
            if (checker!.EnumQualifiers.TryGetValue(member.Receiver, out var enumeration))
            {
                Add(scope.Module, member.Receiver.Token, symbols[enumeration]);
            }

            var binding = checker.Members.GetValueOrDefault(member);
            if (MemberReaders.TryGetValue(binding.Kind, out var read))
            {
                Add(scope.Module, member.Token, read(this, binding));
            }
        }

        private void VisitCall(Call call, Scope scope)
        {
            if (call.Receiver is not null)
            {
                Walk(call.Receiver, scope);
            }

            if (FindCallSymbol(call) is { } callable)
            {
                Add(scope.Module, call.Token, callable);
            }

            foreach (var argument in call.Arguments)
            {
                if (checker!.ConstructionFields.TryGetValue(argument, out var argumentField))
                {
                    Add(scope.Module, argument.Token, symbols[argumentField]);
                }

                Walk(argument.Value, scope);
            }
        }

        private void VisitBinary(Binary binary, Scope scope)
        {
            Walk(binary.Left, scope);
            Walk(binary.Right, scope);
        }

        private void VisitUnary(Unary unary, Scope scope)
        {
            Walk(unary.Value, scope);
        }

        private void VisitArrayLiteral(ArrayLiteral array, Scope scope)
        {
            foreach (var item in array.Items)
            {
                Walk(item, scope);
            }
        }

        private void VisitDictionaryLiteral(DictionaryLiteral dictionary, Scope scope)
        {
            foreach (var entry in dictionary.Entries)
            {
                Walk(entry.Key, scope);
                Walk(entry.Value, scope);
            }
        }

        private void VisitIndex(Index index, Scope scope)
        {
            Walk(index.Receiver, scope);
            Walk(index.Offset, scope);
        }

        private EditorSymbol? FindCallSymbol(Call call)
        {
            var binding = checker!.Calls.GetValueOrDefault(call);
            return binding.Kind switch
            {
                CallKind.LocalFunction or CallKind.ImportedFunction => symbols[binding.Function!],
                CallKind.StructLiteral => symbols[binding.Structure!],
                CallKind.Standard => FindStandardCallSymbol(call, binding.StandardOperation!),
                CallKind.DictionaryMethod => EditorCatalog.DictionaryMembers(checker.Types[call.Receiver!]).First(symbol => symbol.Name == call.Name),
                CallKind.ArrayAppend => EditorCatalog.ArrayAppend(checker.Types[call.Receiver!].Element!),
                _ => call.Receiver is null ? EditorCatalog.Builtins.FirstOrDefault(symbol => symbol.Name == call.Name) : null
            };
        }

        private static EditorSymbol? FindStandardCallSymbol(Call call, string operation)
        {
            var ordinary = operation + "(";
            var generic = operation + "<";
            EditorSymbol? first = null;
            foreach (var symbol in EditorCatalog.Standard)
            {
                if (!symbol.Detail.StartsWith(ordinary, StringComparison.Ordinal) &&
                    !symbol.Detail.StartsWith(generic, StringComparison.Ordinal))
                {
                    continue;
                }

                first ??= symbol;
                if (symbol.Detail.Contains("<T>") == (call.TypeArgument is not null))
                {
                    return symbol;
                }
            }
            return first;
        }

        private void AddTypeReferences(Module module)
        {
            if (module.TypeTokens is null)
            {
                return;
            }

            foreach (var (syntaxType, token) in module.TypeTokens)
            {
                if (module.TypeQualifiers?.TryGetValue(syntaxType, out var qualifier) == true)
                {
                    var import = scopes.First(s => s.Module == module && s.Parent is null).Symbols.FirstOrDefault(s => s.Kind == CompletionKind.Module && s.Name == qualifier.Text);
                    if (import is not null)
                    {
                        Add(module, qualifier, import);
                    }
                }
                // Match the actual parsed type node to its checked type, preserving module identity.
                var bound = checker!.BoundTypes.GetValueOrDefault(syntaxType);
                while (bound is not null && (bound.NullableElement is not null || bound.Element is not null))
                {
                    bound = bound.NullableElement ?? bound.Element;
                }

                if (bound?.Struct is { } structure)
                {
                    Add(module, token, symbols[structure]);
                }
                else if (bound?.Enum is { } enumeration)
                {
                    Add(module, token, symbols[enumeration]);
                }
                else if (bound is not null && EditorCatalog.ScalarType(bound) is { } scalar)
                {
                    Add(module, token, scalar);
                }
            }
        }

        internal EditorOccurrence? At(int offset) => occurrences.FirstOrDefault(o => o.Module == Entry && offset >= o.Token.Offset && offset < o.Token.Offset + o.Token.Text.Length);
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
                {
                    found = new(Entry, declaration.Node.Token, new(declaration.Name, declaration.Name, CompletionKind.Function,
                        Description(Entry, declaration.Node), Module: Entry, Declaration: declaration.Node.Token));
                }
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
                var hole = checker?.Types.Keys.OfType<Member>().FirstOrDefault(m => m.Token.Text == "__completion" && m.Token.Offset == offset - prefix.Length);
                if (hole is not null && checker!.EnumQualifiers.TryGetValue(hole.Receiver, out var enumeration))
                {
                    candidates = enumeration.Members.Values.Select(member => symbols[member]);
                }
                else if (Entry.Imports.TryGetValue(receiver, out var imported))
                {
                    candidates = imported.StandardName is { } standard
                        ? EditorCatalog.Standard.Where(s => s.Detail.StartsWith(standard + ".", StringComparison.Ordinal)).Concat(imported.Structs.Values.Select(s => symbols[s]))
                        : imported.Functions.Values.Select(f => symbols[checker!.Functions[f]]).Concat(imported.Structs.Values.Select(s => symbols[s]));
                    candidates = candidates.Concat(imported.Enums.Values.Select(e => symbols[e]));
                    if (imported.StandardName is { } moduleName)
                    {
                        candidates = candidates.Concat(EditorCatalog.TypesForModule(moduleName));
                    }
                }
                else
                {
                    var dot = offset - prefix.Length - 1;
                    while (dot >= 0 && char.IsWhiteSpace(Entry.Source.Text[dot]))
                    {
                        dot--;
                    }

                    var receiverEnd = dot;
                    while (receiverEnd > 0 && char.IsWhiteSpace(Entry.Source.Text[receiverEnd - 1]))
                    {
                        receiverEnd--;
                    }

                    var type = hole is not null ? checker!.Types.GetValueOrDefault(hole.Receiver)
                        : occurrences.LastOrDefault(o => o.Module == Entry && o.Token.Offset + o.Token.Text.Length == receiverEnd)?.Symbol.Type
                        ?? scope?.Visible(offset).FirstOrDefault(s => s.Name == receiver)?.Type;
                    candidates = type?.Struct is { } structure
                        ? structure.Fields.Values.Select(f => symbols[f]).Concat(structure.Methods.Values.Select(f => symbols[f]))
                        : type?.DictionaryKey is not null ? EditorCatalog.DictionaryMembers(type)
                        : type?.Element is { } element
                            ? [EditorCatalog.ArrayLength, EditorCatalog.ArrayAppend(element)] : [];
                }
            }
            else
            {
                candidates = (scope?.Visible(offset) ?? []).Concat(EditorCatalog.Builtins).Concat(EditorCatalog.Keywords.Select(k => new EditorSymbol(k, k, CompletionKind.Keyword)));
            }

            return candidates.Where(s => s.Name.StartsWith(prefix, StringComparison.Ordinal)).DistinctBy(s => s.Name).OrderBy(s => s.Name);
        }
    }
}
