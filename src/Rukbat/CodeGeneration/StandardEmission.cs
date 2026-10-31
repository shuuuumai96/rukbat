namespace Rukbat
{
    internal sealed partial class Emitter
    {
        private static readonly string[] EncodingRuntimeModules =
            ["encoding", "fs", "process", "zip", "hash", "http"];

        // Order is intentional: preserve loader setup and generated operation order.
        private static readonly (string Module, Action<Emitter> Emit)[] RuntimeInitializers =
        [
            ("datetime", static emitter =>
            {
                emitter.EmitNativeLoader("DateTime");
                emitter.Line("& $__rukbat_load_DateTime");
            }),
            ("fs", static emitter =>
            {
                emitter.EmitNativeLoader("FileText");
                emitter.EmitNativeLoader("MoveBatch");
                emitter.EmitNativeLoader("TemporaryEntries");
            }),
            ("process", static emitter => emitter.EmitNativeLoader("ProcessJob")),
            ("hash", static emitter => emitter.EmitNativeLoader("Hash")),
            ("re", static emitter => emitter.EmitNativeLoader("Regex")),
            ("log", static emitter => emitter.EmitLogRuntime()),
            ("zip", static emitter => emitter.EmitNativeLoader("Zip", "System.IO.Compression")),
            ("http", static emitter => emitter.EmitHttpLoader()),
            ("csv", static emitter => emitter.EmitCsvRuntime()),
            ("cli", static emitter => emitter.EmitCliRuntime()),
            ("text", static emitter =>
            {
                emitter.EmitNativeLoader("Text");
                emitter.Line("& $__rukbat_load_Text");
            }),
            ("array", static emitter =>
            {
                emitter.EmitNativeLoader("Array");
                emitter.Line("& $__rukbat_load_Array");
            })
        ];

        private static readonly (string Module, string[] Resources)[] RuntimeOperations =
        [
            ("fs", ["FilesRuntime", "DirectoriesRuntime"]),
            ("path", ["PathRuntime"]),
            ("csv", ["CsvRuntime"]),
            ("cli", ["CliRuntime"]),
            ("json", ["JsonValueRuntime"]),
            ("env", ["EnvironmentRuntime"]),
            ("process", ["ProcessRuntime"]),
            ("hash", ["HashRuntime"]),
            ("re", ["RegexRuntime"]),
            ("log", ["LogRuntime"]),
            ("zip", ["ZipRuntime"]),
            ("http", ["HttpRuntime"]),
            ("text", ["TextRuntime"]),
            ("array", ["ArrayRuntime"]),
            ("math", ["MathRuntime"])
        ];

        private void EmitStandardRuntime(IReadOnlySet<string> modules)
        {
            EmitLines(RuntimeResources.ReadPowerShell("PathContextRuntime"));
            if (modules.Contains("path"))
            {
                EmitLines(RuntimeResources.ReadPowerShell("PathValueRuntime"));
            }

            if (modules.Overlaps(EncodingRuntimeModules))
            {
                EmitNativeLoader("CodePageCodec");
                EmitEncodingSelector();
            }
            foreach (var initializer in RuntimeInitializers)
            {
                if (modules.Contains(initializer.Module))
                {
                    initializer.Emit(this);
                }
            }
            Line("$__rukbat_standard = {");
            indentation++;
            Line("param([string]$operation, [object[]]$values)");
            Line("$ErrorActionPreference = 'Stop'");
            Line("switch ($operation) {");
            indentation++;
            foreach (var operation in RuntimeOperations)
            {
                if (modules.Contains(operation.Module))
                {
                    foreach (var resource in operation.Resources)
                    {
                        EmitLines(RuntimeResources.ReadPowerShell(resource));
                    }
                }
            }
            if (modules.Contains("datetime"))
            {
                EmitDateTimeOperations();
            }

            if (modules.Contains("encoding"))
            {
                EmitEncodingOperations();
            }

            indentation--;
            Line("}");
            Line("throw \"Unknown standard-library operation '$operation'.\"");
            indentation--;
            Line("}.GetNewClosure()");
        }

        private void EmitEncodingOperations()
        {
            Line("'encoding.parse' { return (& $__rukbat_text_encoding ([string]$values[0]) 'text').name }");
            Line("'encoding.from_code_page' {");
            Line("    $number = [int]$values[0]");
            Line("    switch ($number) {");
            foreach (var value in EncodingValues.All)
            {
                Line($"        {value.CodePage} {{ return (& $__rukbat_text_encoding {Quote(value.Label)} 'text').name }}");
            }

            Line("    }");
            Line("    return (& $__rukbat_text_encoding ('windows-' + $number) 'text').name");
            Line("}");
            Line("'encoding.name' { return [string]$values[0] }");
        }

        private void EmitEncodingSelector()
        {
            Line("$__rukbat_text_encoding = {");
            Line("    param([string]$name, [string]$operation)");
            Line("    & $__rukbat_load_CodePageCodec");
            Line("    switch -CaseSensitive ($name) {");
            foreach (var value in EncodingValues.All)
            {
                Line($"        {Quote(value.Label)} {{ return [pscustomobject]@{{ name = {Quote(value.Label)}; encoding = {value.Codec} }} }}");
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
            Line("            try { return [pscustomobject]@{ name = $name; encoding = [RukbatCodePageCodec]::new($number) } }");
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
