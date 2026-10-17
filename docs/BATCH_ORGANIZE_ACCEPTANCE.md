# Batch file organization example

The [batch-organize example](../examples/batch-organize/main.rukbat) reads a CSV move plan and calls `fs.move_files_batch` to move files with a persistent recovery journal.

## Invocation and exit codes

```powershell
rukbat run examples/batch-organize/main.rukbat -- plan.csv
```

One nonempty plan path is required. `--recover <plan.csv>` restores an interrupted plan without executing it again; it reads `<plan.csv>.rukbat-journal`, not the CSV contents. Normal completion exits `0` and writes no output. Invalid arguments, CSV syntax, plan validation, or filesystem errors write an English diagnostic to stderr and exit `2`.

All relative paths, including the plan path and its CSV fields, resolve from the captured entry working directory. They are not relative to the CSV file.

## Plan schema

The program calls `csv.parse` and validates the parsed records itself. The first logical CSV record is required to be exactly:

```csv
source,destination
```

Every later record must have exactly two nonempty fields. CSV record numbers count logical records, so a quoted multiline field remains one record. Header-only plans succeed and record a completed empty plan in the journal.

## Validation and mutation

The CSV plan itself must not be listed as a source. It must remain available for repeat invocations.

`fs.move_files_batch` validates the complete plan, locks sources and directories, and flushes `<plan.csv>.rukbat-journal` before any source rename. Sources must be regular files with one link; destinations must be absent. Sources, destinations and journal must have non-overlapping paths on one local fixed NTFS drive. Reparse points, alternate data streams, and ambiguous paths are rejected. Parent directory creation can leave empty directories on failure, but no source is moved until preparation and journaling succeed.

The journal records absolute paths, file identities and content hashes. A normal failure rolls back moved files. After abrupt termination, the same invocation restores interrupted moves and retries; `--recover` only restores them. A committed journal causes verified replay without another move. Different plans cannot reuse a journal, and concurrent uses of one journal fail before mutation. Changed files or a corrupt journal stop recovery without overwriting data.

These are the [standard-library contracts](STDLIB.md#recoverable-file-moves), with operating instructions in [UNATTENDED.md](UNATTENDED.md). Created directories are retained, and other readers can observe intermediate paths; the batch is recoverable rather than atomically visible.
