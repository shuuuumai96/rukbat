# CSV category counter

Counts rows by `category` and looks up display labels using typed dictionaries.
It reads UTF-8 CSV, iterates keys in explicit sorted order, and writes one count per line.

```powershell
rukbat run examples/csv-count/main.rukbat -- examples/csv-count/rows.csv
rukbat build examples/csv-count/main.rukbat
powershell -NoProfile -File examples/csv-count/main.ps1 examples/csv-count/rows.csv
```

The generated `main.ps1` needs no Rukbat compiler or source files on the target machine.
PowerShell 5.1 and 7 produce:

```text
Errors: 2
Information: 1
Warnings: 1
```
