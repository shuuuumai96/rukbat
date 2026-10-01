# CSV filter

Keep rows whose `enabled` cell parses as `true`, set their `note` to `checked`, and write UTF-8 CSV.
Column names are case-sensitive. `enabled` and `note` are required; other columns are retained.
The source may place the columns in any order. Values of `enabled` must be `true` or `false`.

```powershell
rukbat run examples/csv-filter/main.rukbat -- input.csv enabled.csv windows-932
rukbat build examples/csv-filter/main.rukbat
powershell -NoProfile -File ./examples/csv-filter/main.ps1 input.csv enabled.csv windows-932
```

Example input:

```csv
name,enabled,note
Taro,true,
Hanako,false,
```

Output:

```csv
name,enabled,note
Taro,true,checked
```

An invalid header, record width, boolean, or missing column exits with code 2 before saving output.
Existing output is untouched on input/validation failure. Output uses CRLF and UTF-8 without BOM.
The entire file is held in memory. Choose separate input and output paths to retain the input.
