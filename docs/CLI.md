# Command-line programs

Use `import cli` to define positional arguments, value options, flags and help in one place.
The parser returns data; the program controls output, exit codes and operations.

```rukbat
import cli
import encoding
import fs
import json

command := cli.command("list-files.ps1", "List matching files.")
command.arguments = [cli.argument("directory", "Directory to search")]
pattern := cli.option("pattern", "Filename pattern")
pattern.default_value = "*"
output := cli.option("output", "Output file; omitted means stdout")
output.short_name = "o"
output.value_name = "FILE"
command.options = [pattern, output]

try {
    parsed := cli.parse(command, args())
    if cli.help_requested(parsed) {
        print(cli.help(command))
        exit(0)
    }
    files := fs.list_files(cli.get(parsed, "directory"), cli.get(parsed, "pattern"))
    report := json.stringify(files)
    if cli.has(parsed, "output") {
        fs.write_text(cli.get(parsed, "output"), report, encoding.utf8)
    } else {
        print(report)
    }
} catch err {
    eprint(err.message)
    exit(2)
}
```

Save as `list-files.rukbat`, then run or build:

```powershell
rukbat run list-files.rukbat -- C:\Logs --pattern '*.log'
rukbat build list-files.rukbat
.\list-files.ps1 --pattern '*.log' C:\Logs -o report.json
.\list-files.ps1 --help
```

## Invocation and help

`cli.command` takes a display name; help does not detect whether the program was
started through Rukbat or PowerShell. Add explicit invocation instructions when
supporting both, as in [file-browser](../examples/file-browser/main.rukbat):

```text
Rukbat (from the source directory):
  rukbat run list-files.rukbat -- [options] DIRECTORY

Generated PowerShell (from the generated script's directory):
  .\list-files.ps1 [options] DIRECTORY

VS Code: Rukbat: Run File
  ["C:/Logs", "--pattern", "*.log"]
  Help: ["--help"]
```

The CLI examples assume `rukbat` is on PATH. VS Code uses its bundled compiler;
its argument prompt takes only the JSON array. In a terminal, `--` separates
compiler arguments from program arguments.

## Options and flags

Configure individual definitions before adding them to arrays: copying or appending a definition
copies its fields and arrays. Later changes to the original variable do not update the command.
Parsing also snapshots values; subsequent definition changes do not affect an existing result.

For a presence flag:

```rukbat
recursive := cli.flag("recursive", "Search subdirectories")
recursive.short_name = "r"
command.flags = [recursive]
// After parsing:
if cli.has(parsed, "recursive") {
    print("Recursive search requested")
}
```

For multiple values, set repeatable and use get_all. Only the last positional can be repeatable.

```rukbat
tags := cli.option("tag", "Tag to attach")
tags.repeatable = true
command.options.append(tags)
// --tag first --tag second
for tag in cli.get_all(parsed, "tag") {
    print(tag)
}
```

Set choices to restrict text (`format.choices = ["json", "csv"]`), required to require an option,
or allow_empty to accept an explicit empty value. A default is only valid on an optional
non-repeatable option. Use `text.parse_int(cli.get(parsed, "count"))` for numeric conversion;
range constraints, filesystem checks and business combinations remain ordinary procedural code.
Use `cli.get(parsed, output.name)` when sharing the definition's name avoids duplicate string literals.

## Parsing and errors

Names are exact and lowercase. Options can precede/follow positionals. Non-repeatable duplicates
are errors. `--name=VALUE` safely supplies a dash-prefixed value; use `--` before dash-prefixed
positionals. `--name=` is an empty value, not an omitted value. A value containing spaces or
shell metacharacters remains one string when the invoking shell supplies one argument.
CLI does not evaluate, re-tokenize or expand strings.

`--help`/`-h` before the option terminator wins over invalid user inputs and missing required
arguments. `--name=--help` and `-- --help` remain data. Handle help before reading result values
and before file/process/network operations. Move such operations out of imported modules'
top-level initialization when help must have no business side effects.

`cli.parse` and `cli.help` do not print or exit. Conventional programs print help to stdout and
exit 0, then print caught input Errors to stderr and exit 2. They may choose other exit policies
for operational failures. A Result has no fields and cannot be exported as JSON or PowerShell
data: build an ordinary program-specific struct from get/get_all/has instead.
Unknown result names are errors even for has, preventing typos from silently appearing absent.
has includes effective defaults; it does not report whether the user explicitly supplied a value.

See [STDLIB.md](STDLIB.md#cli) for all fields, validation, argument grammar and error ordering.
