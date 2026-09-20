# Practical CLI acceptance scenario: batch file organization

## Status and purpose

This is the acceptance contract for `examples/batch-organize`. It reads a CSV move plan, validates the whole plan before filesystem mutation, creates destination parent directories, and moves files in CSV-record order.

## Invocation and exit codes

```powershell
rukbat run examples/batch-organize/main.rukbat -- plan.csv
```

Exactly one nonempty plan path is required. Normal completion exits `0` and writes no output. Invalid arguments, CSV syntax, plan validation, or filesystem errors write an English diagnostic to stderr and exit `2`.

All relative paths, including the plan path and its CSV fields, resolve from the captured entry working directory. They are not relative to the CSV file.

## Plan schema

The program calls `csv.parse` and validates the parsed records itself. The first logical CSV record is required to be exactly:

```csv
source,destination
```

Every later record must have exactly two nonempty fields. CSV record numbers count logical records, so a quoted multiline field remains one record. Header-only plans succeed without mutation.

## Validation and mutation

Before it creates a directory or moves a file, the program validates every entry:

- source exists as a file;
- destination does not exist as a file or directory;
- source and destination differ under `path.equal`;
- no two entries have equivalent sources or destinations;
- each destination has a valid parent path.

`path.equal` uses Rukbat's captured-working-directory path resolution and Windows ordinal case-insensitive comparison. It handles relative and absolute spelling, `.`, `..`, and separator differences. It does not establish filesystem object identity through links, junctions, hard links, short names, or file IDs.

After validation, records are processed in source order. For each entry, the program creates `path.parent(destination)` and calls `files.move`. The program does not promise rollback, transactionality, locking, or race-free operation. A later filesystem failure can leave earlier moves and created directories in place; a failed cross-volume move can leave its destination entry.

## CSV dependency

`csv.parse` parses CSV structure only. It does not infer types, interpret a header, require a rectangular table, or map records to structs. Its public contract is defined in `STDLIB.md`.
