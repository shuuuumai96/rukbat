namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private sealed class RuntimeUsage
        {
            private sealed class Builder(TypeChecker checker)
            {
                public TypeChecker Checker { get; } = checker;
                public HashSet<string> StandardCalls { get; } = new(StringComparer.Ordinal);
                public HashSet<string> ActiveStandardModules { get; } = new(StringComparer.Ordinal);
                public HashSet<StructSymbol> VisitedStructs { get; } = new(ReferenceEqualityComparer.Instance);
                public bool UsesPowerShell { get; set; }
                public bool UsesDictionary { get; set; }
                public bool UsesJsonEncode { get; set; }
                public bool UsesDateTime { get; set; }
            }

            private static readonly IReadOnlyDictionary<string, Action<Builder, Call>> StandardOperationHooks =
                new Dictionary<string, Action<Builder, Call>>(StringComparer.Ordinal)
                {
                    [RuntimeCatalog.Operations.JsonStringify] = static (builder, _) => builder.UsesJsonEncode = true,
                    [RuntimeCatalog.Operations.JsonSet] = MarkJsonReplacementEncoding,
                    [RuntimeCatalog.Operations.JsonSetAt] = MarkJsonReplacementEncoding,
                    [RuntimeCatalog.Operations.JsonAppend] = MarkJsonReplacementEncoding
                };

            private static readonly IReadOnlyDictionary<CallKind, Action<Builder, Call, BoundCall>> CallAnalyzers =
                new Dictionary<CallKind, Action<Builder, Call, BoundCall>>
                {
                    [CallKind.PowerShell] = static (builder, _, _) => builder.UsesPowerShell = true,
                    [CallKind.DictionaryMethod] = static (builder, _, _) => builder.UsesDictionary = true,
                    [CallKind.Standard] = AnalyzeStandardCall
                };

            private static readonly IReadOnlyDictionary<MemberKind, Action<Builder, BoundMember>> MemberAnalyzers =
                new Dictionary<MemberKind, Action<Builder, BoundMember>>
                {
                    [MemberKind.DictionaryLength] = static (builder, _) => builder.UsesDictionary = true,
                    [MemberKind.StandardConstant] = static (builder, member) =>
                        builder.UsesDateTime |= member.Constant?.Definition is DateTimeValues.ZoneConstant
                };

            private static readonly IReadOnlyDictionary<ValueKind, Action<Builder, ValueType>> TypeInspectors =
                new Dictionary<ValueKind, Action<Builder, ValueType>>
                {
                    [ValueKind.Dictionary] = static (builder, type) =>
                    {
                        builder.UsesDictionary = true;
                        InspectType(builder, type.DictionaryValue!);
                    },
                    [ValueKind.Array] = static (builder, type) => InspectType(builder, type.Element!),
                    [ValueKind.Struct] = static (builder, type) =>
                    {
                        if (type.Struct is { } structure && builder.VisitedStructs.Add(structure))
                        {
                            foreach (var field in structure.Fields.Values)
                            {
                                InspectType(builder, field.Type);
                            }
                        }
                    },
                    [ValueKind.Scalar] = static (builder, type) => builder.UsesDateTime |= DateTimeValues.Contains(type)
                };

            public HashSet<string> StandardCalls { get; init; } = null!;
            public HashSet<string> ActiveStandardModules { get; init; } = null!;
            public bool UsesPowerShell { get; init; }
            public bool UsesDictionary { get; init; }
            public bool UsesIntegerDivision { get; init; }
            public bool UsesJsonDecode { get; init; }
            public bool UsesJsonEncode { get; init; }
            public bool UsesJsonNative { get; init; }
            public bool UsesTextNative { get; init; }
            public bool UsesArrayNative { get; init; }
            public bool UsesHttpBodyType { get; init; }
            public bool UsesDateTime { get; init; }
            public bool UsesFileText { get; init; }
            public bool UsesFileScopes { get; init; }
            public bool UsesMoveBatch { get; init; }
            public bool UsesTemporaryEntries { get; init; }
            public bool UsesEncodingSelector { get; init; }

            public static RuntimeUsage Analyze(TypeChecker checker)
            {
                var builder = new Builder(checker);

                foreach (var (callNode, call) in checker.Calls)
                {
                    if (CallAnalyzers.TryGetValue(call.Kind, out var analyzeCall))
                    {
                        analyzeCall(builder, callNode, call);
                    }
                }

                foreach (var iteration in checker.Iterations.Values)
                {
                    if (iteration.OpenOperation is { } operation)
                    {
                        RegisterDispatchedOperation(builder, operation);
                    }
                }

                foreach (var member in checker.Members.Values)
                {
                    if (MemberAnalyzers.TryGetValue(member.Kind, out var analyzeMember))
                    {
                        analyzeMember(builder, member);
                    }
                }

                foreach (var type in checker.Types.Values
                    .Concat(checker.Variables.Values.Select(variable => variable.Type))
                    .Concat(checker.Functions.Values.Select(function => function.ReturnType)))
                {
                    InspectType(builder, type);
                }

                var calls = builder.StandardCalls;
                var modules = builder.ActiveStandardModules;
                var usesJsonDecode = calls.Overlaps(RuntimeCatalog.JsonDecodeOperations);
                var usesIntegerDivision = checker.Types.Any(pair =>
                    pair.Key is Binary binary &&
                    pair.Value == ValueType.Int &&
                    RuntimeCatalog.IntegerDivisionOperators.Contains(binary.Token.Text));
                var usesHttpBodyType = checker.Variables.Values.Any(variable => variable.Type == HttpValues.Body) ||
                    checker.Functions.Values.Any(function => function.ReturnType == HttpValues.Body);

                return new RuntimeUsage
                {
                    StandardCalls = calls,
                    ActiveStandardModules = modules,
                    UsesPowerShell = builder.UsesPowerShell,
                    UsesDictionary = builder.UsesDictionary,
                    UsesIntegerDivision = usesIntegerDivision,
                    UsesJsonDecode = usesJsonDecode,
                    UsesJsonEncode = builder.UsesJsonEncode,
                    UsesJsonNative = usesJsonDecode || builder.UsesJsonEncode || modules.Contains(RuntimeCatalog.Modules.Json),
                    UsesTextNative = calls.Overlaps(RuntimeCatalog.NativeTextOperations),
                    UsesArrayNative = calls.Overlaps(RuntimeCatalog.NativeArrayOperations),
                    UsesHttpBodyType = usesHttpBodyType,
                    UsesDateTime = builder.UsesDateTime || modules.Contains(RuntimeCatalog.Modules.DateTime) || calls.Contains(RuntimeCatalog.Operations.FsStat),
                    UsesFileText = calls.Overlaps(RuntimeCatalog.FileTextOperations),
                    UsesFileScopes = calls.Overlaps(RuntimeCatalog.FileScopeOperations),
                    UsesMoveBatch = calls.Overlaps(RuntimeCatalog.MoveBatchOperations),
                    UsesTemporaryEntries = calls.Overlaps(RuntimeCatalog.TemporaryEntryOperations),
                    UsesEncodingSelector = modules.Overlaps(RuntimeCatalog.EncodingSelectorModules) || calls.Overlaps(RuntimeCatalog.EncodingSelectorOperations)
                };
            }

            private static void AnalyzeStandardCall(Builder builder, Call callNode, BoundCall binding)
            {
                if (binding.StandardOperation is not { } operation)
                {
                    return;
                }
                builder.StandardCalls.Add(operation);
                if (!IsInlinedDirectStandardCall(operation))
                {
                    RegisterModuleOf(builder, operation);
                }
                if (StandardOperationHooks.TryGetValue(operation, out var hook))
                {
                    hook(builder, callNode);
                }
            }

            private static void RegisterDispatchedOperation(Builder builder, string operation)
            {
                builder.StandardCalls.Add(operation);
                RegisterModuleOf(builder, operation);
            }

            private static void RegisterModuleOf(Builder builder, string operation)
            {
                var separator = operation.IndexOf(RuntimeCatalog.OperationSeparator);
                if (separator > 0)
                {
                    builder.ActiveStandardModules.Add(operation[..separator]);
                }
            }

            private static void MarkJsonReplacementEncoding(Builder builder, Call callNode)
            {
                var replacementType = builder.Checker.Types[callNode.Arguments[^1].Value];
                builder.UsesJsonEncode |= replacementType != ValueType.Null && !replacementType.IsJsonValue;
            }

            private static void InspectType(Builder builder, ValueType type)
            {
                var current = type.NonNullable;
                if (TypeInspectors.TryGetValue(current.Kind, out var inspect))
                {
                    inspect(builder, current);
                }
            }
        }

        private static readonly (string Variable, Func<RuntimeUsage, bool> Needed, string Resource)[] OptionalCoreRuntimes =
        [
            ("$__rukbat_dictionary", static usage => usage.UsesDictionary, RuntimeCatalog.Resources.Dictionary),
            ("$__rukbat_int_div", static usage => usage.UsesIntegerDivision, RuntimeCatalog.Resources.IntegerDivision),
            ("$__rukbat_runtime", static usage => usage.UsesPowerShell, RuntimeCatalog.Resources.PowerShell),
            ("$__rukbat_json_decode", static usage => usage.UsesJsonDecode, RuntimeCatalog.Resources.JsonDecode),
            ("$__rukbat_json_encode", static usage => usage.UsesJsonEncode, RuntimeCatalog.Resources.JsonEncode)
        ];

        // Keep PowerShell's stream enumeration at the boundary. Internally a List retains spare capacity for append.
        private void EmitRuntime(RuntimeUsage usage)
        {
            EmitLines(RuntimeResources.ReadPowerShell(RuntimeCatalog.Resources.Core));
            foreach (var (variable, needed, resource) in OptionalCoreRuntimes)
            {
                Line($"{variable} = $null");
                if (needed(usage))
                {
                    EmitLines(RuntimeResources.ReadPowerShell(resource));
                }
            }
        }

        private static readonly IReadOnlyDictionary<ValueKind, Func<ValueType, string>> SchemaEmitters =
            new Dictionary<ValueKind, Func<ValueType, string>>
            {
                [ValueKind.Dictionary] = static type => EmitSchema("dictionary", type,
                    $"; key = {Quote(type.DictionaryKey!.Name)}; element = {Schema(type.DictionaryValue!)}"),
                [ValueKind.Nullable] = static type => EmitSchema("nullable", type, $"; element = {Schema(type.NullableElement!)}"),
                [ValueKind.Array] = static type => EmitSchema("array", type, $"; element = {Schema(type.Element!)}"),
                [ValueKind.CsvRow] = static type => EmitSchema("csv_row", type),
                [ValueKind.CliResult] = static type => EmitSchema("cli_result", type),
                [ValueKind.JsonValue] = static type => EmitSchema("json", type),
                [ValueKind.Struct] = EmitStructSchema,
                [ValueKind.Enum] = static type => EmitSchema("enum", type, $"; members = {EnumNames(type.Enum!)}"),
                [ValueKind.Scalar] = static type => EmitSchema(type.Name, type)
            };

        private static string Schema(ValueType type) => SchemaEmitters[type.Kind](type);

        private static string EmitSchema(string kind, ValueType type, string attributes = "") =>
            $"@{{kind = {Quote(kind)}; name = {Quote(type.Name)}{attributes}}}";

        private static string EmitStructSchema(ValueType type)
        {
            var fields = type.Struct!.Fields.Select(field =>
                $"@{{name = {Quote(field.Key)}; key = {Quote(field.Value.Key)}; type = {Schema(field.Value.Type)}}}");
            return EmitSchema("struct", type, $"; fields = @({string.Join("; ", fields)})");
        }

        private static string EnumNames(EnumSymbol enumeration) =>
            $"[string[]]@({string.Join(", ", enumeration.Members.Keys.Select(Quote))})";
    }
}
