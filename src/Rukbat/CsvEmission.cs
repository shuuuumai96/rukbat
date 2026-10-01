namespace Rukbat;

internal sealed partial class Emitter
{
    private void EmitCsvRuntime()
    {
        EmitLines(CsvNativeRuntime);
        EmitLines(CsvHelpers);
    }

    private const string CsvNativeRuntime = """
    Add-Type -TypeDefinition @'
    using System;
    using System.Collections;
    using System.Collections.Generic;
    public sealed class RukbatCsvRow {
        private sealed class Header {
            public readonly string[] Columns;
            public readonly Dictionary<string, int> Indices;
            public Header(string[] columns) {
                if (columns.Length == 0) throw new Exception("CSV table requires at least one header column.");
                Columns = (string[])columns.Clone();
                Indices = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int index = 0; index < columns.Length; index++) {
                    if (columns[index].Length == 0) throw new Exception("CSV header column " + (index + 1) + " is empty.");
                    if (Indices.ContainsKey(columns[index])) throw new Exception("Duplicate CSV header column '" + columns[index] + "'.");
                    Indices.Add(columns[index], index);
                }
            }
        }
        private readonly Header header;
        private readonly string[] values;
        private RukbatCsvRow(Header header, string[] values, int recordNumber) {
            if (values.Length != header.Columns.Length) throw new Exception("CSV record " + recordNumber + ": expected " + header.Columns.Length + " fields, got " + values.Length + ".");
            this.header = header; this.values = (string[])values.Clone();
        }
        public static void ValidateColumns(string[] columns) { new Header(columns); }
        public static bool HasColumn(string[] columns, string column) { return new Header(columns).Indices.ContainsKey(column); }
        public static RukbatCsvRow Create(string[] columns, string[] values) { return new RukbatCsvRow(new Header(columns), values, 1); }
        public static RukbatCsvRow[] CreateRows(string[] columns, object[] records, int firstRecordNumber) {
            // Validate headers even when no data rows exist, then share immutable lookup data.
            var header = new Header(columns);
            var rows = new RukbatCsvRow[records.Length];
            for (int index = 0; index < records.Length; index++) {
                var fields = new List<string>();
                foreach (object field in (IEnumerable)records[index]) fields.Add((string)field);
                rows[index] = new RukbatCsvRow(header, fields.ToArray(), firstRecordNumber + index);
            }
            return rows;
        }
        private int Index(string column) {
            int index;
            if (!header.Indices.TryGetValue(column, out index)) throw new Exception("CSV row has no column '" + column + "'.");
            return index;
        }
        public string Get(string column) { return values[Index(column)]; }
        public RukbatCsvRow Set(string column, string value) {
            var copy = (string[])values.Clone(); copy[Index(column)] = value;
            return new RukbatCsvRow(header, copy, 1);
        }
        public string[] Fields(string[] columns) {
            if (columns.Length != header.Columns.Length) throw new Exception("CSV row columns do not match table columns.");
            for (int index = 0; index < columns.Length; index++)
                if (!String.Equals(columns[index], header.Columns[index], StringComparison.Ordinal))
                    throw new Exception("CSV row columns do not match table columns at column " + (index + 1) + ".");
            return (string[])values.Clone();
        }
    }
    '@ | Out-Null
    """;

    private const string CsvHelpers = """
    $__rukbat_csv_parse = {
        param([string]$text)
        $records = [System.Collections.Generic.List[object]]::new()
        if ($text.Length -eq 0) { return ,([object[]]$records) }
        $data = @{
            record = [System.Collections.Generic.List[object]]::new()
            field = [System.Text.StringBuilder]::new()
            recordStarted = $false
        }
        $state = 'field'
        $finishField = {
            $null = $data.record.Add($data.field.ToString())
            $null = $data.field.Clear()
        }
        $finishRecord = {
            & $finishField
            $null = $records.Add($data.record)
            $data.record = [System.Collections.Generic.List[object]]::new()
            $data.recordStarted = $false
        }
        for ($index = 0; $index -lt $text.Length; $index++) {
            $character = $text[$index]
            switch ($state) {
                'field' {
                    if ($character -eq '"') {
                        $state = 'quoted'
                        $data.recordStarted = $true
                    } elseif ($character -eq ',') {
                        & $finishField
                        $data.recordStarted = $true
                    } elseif ($character -eq [char]10) {
                        & $finishRecord
                    } elseif ($character -eq [char]13) {
                        if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                            throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                        }
                        $index++
                        & $finishRecord
                    } else {
                        $null = $data.field.Append($character)
                        $state = 'unquoted'
                        $data.recordStarted = $true
                    }
                    break
                }
                'unquoted' {
                    if ($character -eq '"') {
                        throw "CSV at offset ${index}: Quote is not allowed in an unquoted field."
                    } elseif ($character -eq ',') {
                        & $finishField
                        $state = 'field'
                    } elseif ($character -eq [char]10) {
                        & $finishRecord
                        $state = 'field'
                    } elseif ($character -eq [char]13) {
                        if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                            throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                        }
                        $index++
                        & $finishRecord
                        $state = 'field'
                    } else {
                        $null = $data.field.Append($character)
                    }
                    break
                }
                'quoted' {
                    if ($character -eq '"') {
                        $state = 'after quote'
                    } else {
                        $null = $data.field.Append($character)
                    }
                    break
                }
                'after quote' {
                    if ($character -eq '"') {
                        $null = $data.field.Append('"')
                        $state = 'quoted'
                    } elseif ($character -eq ',') {
                        & $finishField
                        $state = 'field'
                    } elseif ($character -eq [char]10) {
                        & $finishRecord
                        $state = 'field'
                    } elseif ($character -eq [char]13) {
                        if ($index + 1 -ge $text.Length -or $text[$index + 1] -ne [char]10) {
                            throw "CSV at offset ${index}: Bare carriage return outside a quoted field."
                        }
                        $index++
                        & $finishRecord
                        $state = 'field'
                    } else {
                        throw "CSV at offset ${index}: Expected a separator or record end after a closing quote."
                    }
                    break
                }
            }
        }
        if ($state -eq 'quoted') { throw "CSV at offset $($text.Length): Unclosed quoted field." }
        if ($data.recordStarted) { & $finishRecord }
        return ,([object[]]$records)
    }.GetNewClosure()
    $__rukbat_csv_stringify = {
        param([object]$records)
        $text = [System.Text.StringBuilder]::new()
        $recordNumber = 0
        foreach ($record in $records) {
            $recordNumber++
            if ($record.Count -eq 0) {
                throw "csv.stringify record $recordNumber must contain at least one field."
            }
            for ($index = 0; $index -lt $record.Count; $index++) {
                if ($index -ne 0) { $null = $text.Append(',') }
                $field = [string]$record[$index]
                if ($field.Length -eq 0 -or $field.IndexOfAny([char[]]",`"`r`n") -ge 0) {
                    $null = $text.Append('"').Append($field.Replace('"', '""')).Append('"')
                } else {
                    $null = $text.Append($field)
                }
            }
            $null = $text.Append("`r`n")
        }
        return $text.ToString()
    }.GetNewClosure()
    """;
}
