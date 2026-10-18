namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitStandardRuntime(IReadOnlySet<string> modules)
    {
        EmitLines(RuntimeResources.ReadPowerShell("PathContextRuntime"));
        if (modules.Contains("path")) EmitLines(RuntimeResources.ReadPowerShell("PathValueRuntime"));
        if (modules.Contains("encoding") || modules.Contains("fs") || modules.Contains("process") || modules.Contains("zip") || modules.Contains("hash") || modules.Contains("http"))
        {
            EmitNativeLoader("CodePageCodec");
            EmitEncodingSelector();
        }
        if (modules.Contains("datetime"))
        {
            EmitNativeLoader("DateTime");
            Line("& $__rukbat_load_DateTime");
        }
        if (modules.Contains("fs"))
        {
            EmitNativeLoader("FileText");
            EmitNativeLoader("MoveBatch");
            EmitNativeLoader("TemporaryEntries");
        }
        if (modules.Contains("process")) EmitNativeLoader("ProcessJob");
        if (modules.Contains("hash")) EmitNativeLoader("Hash");
        if (modules.Contains("re")) EmitNativeLoader("Regex");
        if (modules.Contains("log")) EmitLogRuntime();
        if (modules.Contains("zip")) EmitZipLoader();
        if (modules.Contains("http")) EmitHttpLoader();
        if (modules.Contains("csv")) EmitCsvRuntime();
        if (modules.Contains("cli")) EmitCliRuntime();
        if (modules.Contains("text"))
        {
            EmitNativeLoader("Text");
            Line("& $__rukbat_load_Text");
        }
        if (modules.Contains("array"))
        {
            EmitNativeLoader("Array");
            Line("& $__rukbat_load_Array");
        }
        Line("$__rukbat_standard = {");
        indentation++;
        Line("param([string]$operation, [object[]]$values)");
        Line("$ErrorActionPreference = 'Stop'");
        Line("switch ($operation) {");
        indentation++;
        if (modules.Contains("fs"))
        {
            EmitLines(RuntimeResources.ReadPowerShell("FilesRuntime"));
            EmitLines(RuntimeResources.ReadPowerShell("DirectoriesRuntime"));
        }
        if (modules.Contains("path")) EmitLines(RuntimeResources.ReadPowerShell("PathRuntime"));
        if (modules.Contains("csv")) EmitLines(RuntimeResources.ReadPowerShell("CsvRuntime"));
        if (modules.Contains("cli")) EmitLines(RuntimeResources.ReadPowerShell("CliRuntime"));
        if (modules.Contains("json")) EmitLines(RuntimeResources.ReadPowerShell("JsonValueRuntime"));
        if (modules.Contains("env")) EmitLines(RuntimeResources.ReadPowerShell("EnvironmentRuntime"));
        if (modules.Contains("process")) EmitLines(RuntimeResources.ReadPowerShell("ProcessRuntime"));
        if (modules.Contains("hash")) EmitLines(RuntimeResources.ReadPowerShell("HashRuntime"));
        if (modules.Contains("re")) EmitLines(RuntimeResources.ReadPowerShell("RegexRuntime"));
        if (modules.Contains("log")) EmitLines(RuntimeResources.ReadPowerShell("LogRuntime"));
        if (modules.Contains("zip")) EmitLines(RuntimeResources.ReadPowerShell("ZipRuntime"));
        if (modules.Contains("http")) EmitLines(RuntimeResources.ReadPowerShell("HttpRuntime"));
        if (modules.Contains("text")) EmitLines(RuntimeResources.ReadPowerShell("TextRuntime"));
        if (modules.Contains("array")) EmitLines(RuntimeResources.ReadPowerShell("ArrayRuntime"));
        if (modules.Contains("math")) EmitLines(RuntimeResources.ReadPowerShell("MathRuntime"));
        if (modules.Contains("datetime")) EmitDateTimeOperations();
        if (modules.Contains("encoding")) EmitEncodingOperations();
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
            Line($"        {value.CodePage} {{ return (& $__rukbat_text_encoding {Quote(value.Label)} 'text').name }}");
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
            Line($"        {Quote(value.Label)} {{ return [pscustomobject]@{{ name = {Quote(value.Label)}; encoding = {value.Codec} }} }}");
        Line("    }");
        Line("    if ($name -cmatch '^windows-([1-9][0-9]*)$') {");
        Line("        $number = 0");
        Line("        if ([int]::TryParse($Matches[1], [ref]$number)) {");
        Line("            switch ($number) {");
        foreach (var value in EncodingValues.All.Where(v => v.Label != $"windows-{v.CodePage}"))
            Line($"                {value.CodePage} {{ throw \"Use canonical encoding name {value.Label}.\" }}");
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
            if (emitted.Trim() == "'@ | Out-Null") RawLine(emitted.Trim());
            else Line(emitted);
        }
    }
}
