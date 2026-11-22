namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private static readonly (string Name, string ProbeType, Func<RuntimeUsage, bool> Needed)[] CoreNativeUnits =
        [
            (RuntimeCatalog.Native.DateTime, "RukbatDateTime", static usage => usage.UsesDateTime),
            (RuntimeCatalog.Native.FileText, "RukbatFileText", static usage => usage.UsesFileText),
            (RuntimeCatalog.Native.FileScopes, "RukbatFileScopes", static usage => usage.UsesFileScopes),
            (RuntimeCatalog.Native.MoveBatch, "RukbatMoveBatch", static usage => usage.UsesMoveBatch),
            (RuntimeCatalog.Native.TemporaryEntries, "RukbatTemporaryEntries", static usage => usage.UsesTemporaryEntries),
            (RuntimeCatalog.Native.ProcessJob, "RukbatProcessJob", static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Process)),
            (RuntimeCatalog.Native.Hash, "RukbatHash", static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Hash)),
            (RuntimeCatalog.Native.Regex, "RukbatRegex", static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Re)),
            (RuntimeCatalog.Native.Csv, "RukbatCsv", static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Csv)),
            (RuntimeCatalog.Native.Json, RuntimeCatalog.Native.JsonProbeType, static usage => usage.UsesJsonNative),
            (RuntimeCatalog.Native.Text, "RukbatText", static usage => usage.UsesTextNative),
            (RuntimeCatalog.Native.Array, "RukbatArray", static usage => usage.UsesArrayNative)
        ];

        // Order is intentional: preserve loader setup and helper dependencies.
        private static readonly (Func<RuntimeUsage, bool> Needed, Action<Emitter, RuntimeUsage> Emit)[] StandardPreambleSections =
        [
            (static usage => usage.ActiveStandardModules.Overlaps(RuntimeCatalog.PathContextModules),
                static (emitter, _) => emitter.EmitLines(RuntimeResources.ReadPowerShell(RuntimeCatalog.Resources.PathContext))),
            (static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Path),
                static (emitter, _) => emitter.EmitLines(RuntimeResources.ReadPowerShell(RuntimeCatalog.Resources.PathValue))),
            (static usage => usage.UsesEncodingSelector, static (emitter, _) =>
            {
                emitter.EmitNativeLoader(RuntimeCatalog.Native.CodePageCodec);
                emitter.EmitEncodingSelector();
            }),
            (static usage => usage.UsesDateTime,
                static (emitter, _) => emitter.Line($"& $__rukbat_load_{RuntimeCatalog.Native.DateTime}")),
            (static usage => usage.UsesFileText,
                static (emitter, _) => emitter.EmitLines(RuntimeResources.ReadPowerShell(RuntimeCatalog.Resources.FileTextHelpers))),
            (static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Process),
                static (emitter, _) => emitter.Line("$__rukbat_clock = [Diagnostics.Stopwatch]::StartNew()")),
            (static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Log),
                static (emitter, _) => emitter.EmitLogRuntime()),
            (static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Zip),
                static (emitter, _) => emitter.EmitNativeLoader(RuntimeCatalog.Native.Zip, assemblyName: RuntimeCatalog.Native.ZipAssembly)),
            (static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Http) || usage.UsesHttpBodyType,
                static (emitter, usage) => emitter.EmitHttpLoader(usage)),
            (static usage => usage.ActiveStandardModules.Contains(RuntimeCatalog.Modules.Cli),
                static (emitter, _) => emitter.EmitCliRuntime())
        ];

        private static (string Module, Action<Emitter> Emit) ResourceDispatch(string module, params string[] resources) =>
            (module, emitter =>
            {
                foreach (var resource in resources)
                {
                    emitter.EmitLines(RuntimeResources.ReadPowerShell(resource));
                }
            });

        private static readonly (string Module, Action<Emitter> Emit)[] StandardDispatchEntries =
        [
            ResourceDispatch(RuntimeCatalog.Modules.Fs, RuntimeCatalog.Resources.Files, RuntimeCatalog.Resources.Directories, RuntimeCatalog.Resources.FileScopes),
            ResourceDispatch(RuntimeCatalog.Modules.Path, RuntimeCatalog.Resources.Path),
            ResourceDispatch(RuntimeCatalog.Modules.Csv, RuntimeCatalog.Resources.Csv),
            ResourceDispatch(RuntimeCatalog.Modules.Cli, RuntimeCatalog.Resources.Cli),
            ResourceDispatch(RuntimeCatalog.Modules.Json, RuntimeCatalog.Resources.JsonValue),
            ResourceDispatch(RuntimeCatalog.Modules.Env, RuntimeCatalog.Resources.Environment),
            ResourceDispatch(RuntimeCatalog.Modules.Process, RuntimeCatalog.Resources.Process),
            ResourceDispatch(RuntimeCatalog.Modules.Hash, RuntimeCatalog.Resources.Hash),
            ResourceDispatch(RuntimeCatalog.Modules.Re, RuntimeCatalog.Resources.Regex),
            ResourceDispatch(RuntimeCatalog.Modules.Log, RuntimeCatalog.Resources.Log),
            ResourceDispatch(RuntimeCatalog.Modules.Zip, RuntimeCatalog.Resources.Zip),
            ResourceDispatch(RuntimeCatalog.Modules.Http, RuntimeCatalog.Resources.Http),
            ResourceDispatch(RuntimeCatalog.Modules.Text, RuntimeCatalog.Resources.Text),
            ResourceDispatch(RuntimeCatalog.Modules.Array, RuntimeCatalog.Resources.Array),
            ResourceDispatch(RuntimeCatalog.Modules.Math, RuntimeCatalog.Resources.Math),
            (RuntimeCatalog.Modules.DateTime, static emitter => emitter.EmitDateTimeOperations()),
            (RuntimeCatalog.Modules.Encoding, static emitter => emitter.EmitEncodingOperations())
        ];

        private void EmitCoreNativeBundle(RuntimeUsage usage)
        {
            var activeUnits = CoreNativeUnits
                .Where(unit => unit.Needed(usage))
                .Select(unit => (unit.Name, unit.ProbeType))
                .ToArray();
            EmitBundledNativeLoader(activeUnits);
        }

        private void EmitStandardRuntime(RuntimeUsage usage)
        {
            foreach (var (needed, emit) in StandardPreambleSections)
            {
                if (needed(usage))
                {
                    emit(this, usage);
                }
            }

            if (usage.ActiveStandardModules.Count == 0)
            {
                return;
            }

            Line("$__rukbat_standard = {");
            indentation++;
            Line("param([string]$operation, [object[]]$values)");
            Line("$ErrorActionPreference = 'Stop'");
            Line("switch ($operation) {");
            indentation++;
            foreach (var (moduleName, emitOperations) in StandardDispatchEntries)
            {
                if (usage.ActiveStandardModules.Contains(moduleName))
                {
                    emitOperations(this);
                }
            }
            indentation--;
            Line("}");
            Line("throw \"Unknown standard-library operation '$operation'.\"");
            indentation--;
            Line("}.GetNewClosure()");
        }

        private void EmitEncodingOperations()
        {
            Line($"'{RuntimeCatalog.Operations.EncodingParse}' {{ return (& $__rukbat_text_encoding ([string]$values[0]) 'text').name }}");
            Line($"'{RuntimeCatalog.Operations.EncodingFromCodePage}' {{");
            Line("    $number = [int]$values[0]");
            Line("    switch ($number) {");
            foreach (var value in EncodingValues.All)
            {
                Line($"        {value.CodePage} {{ return (& $__rukbat_text_encoding {Quote(value.Label)} 'text').name }}");
            }

            Line("    }");
            Line("    return (& $__rukbat_text_encoding ('windows-' + $number) 'text').name");
            Line("}");
            Line($"'{RuntimeCatalog.Operations.EncodingName}' {{ return [string]$values[0] }}");
        }

        private void EmitEncodingSelector()
        {
            var codecLoaderCall = $"& $__rukbat_load_{RuntimeCatalog.Native.CodePageCodec}; ";
            Line("$__rukbat_text_encoding = {");
            Line("    param([string]$name, [string]$operation)");
            Line("    switch -CaseSensitive ($name) {");
            foreach (var value in EncodingValues.All)
            {
                var load = value.Codec.Contains(RuntimeCatalog.Native.CodePageCodecType, StringComparison.Ordinal)
                    ? codecLoaderCall : "";
                Line($"        {Quote(value.Label)} {{ {load}return [pscustomobject]@{{ name = {Quote(value.Label)}; encoding = {value.Codec} }} }}");
            }

            Line("    }");
            Line("    if ($name -cmatch '^windows-([1-9][0-9]*)$') {");
            Line("        $number = 0");
            Line("        if ([int]::TryParse($Matches[1], [ref]$number)) {");
            Line("            switch ($number) {");
            foreach (var value in EncodingValues.All.Where(v => v.Label != $"windows-{v.CodePage}"))
            {
                Line($"                {value.CodePage} {{ throw \"Use canonical encoding name {value.Label}.\" }}");
            }

            Line("            }");
            Line($"            & $__rukbat_load_{RuntimeCatalog.Native.CodePageCodec}");
            Line($"            try {{ return [pscustomobject]@{{ name = $name; encoding = [{RuntimeCatalog.Native.CodePageCodecType}]::new($number) }} }}");
            Line("            catch { throw \"unsupported encoding: $name\" }");
            Line("        }");
            Line("    }");
            Line("    throw \"unsupported encoding: $name\"");
            Line("}.GetNewClosure()");
        }

        private void EmitLines(string text)
        {
            foreach (var line in text.Split('\n'))
            {
                var emitted = line.TrimEnd('\r');
                if (emitted.Trim() == "'@ | Out-Null")
                {
                    RawLine(emitted.Trim());
                }
                else
                {
                    Line(emitted);
                }
            }
        }
    }
}
