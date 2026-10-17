# Editor features

Open a saved `.rukbat` file in a trusted workspace. Analysis uses unsaved source
and imports without generating or executing PowerShell. Formatting returns edits;
it does not write files. See the [extension guide](../editors/vscode/README.md) for
installation and commands, and [DEBUGGING.md](DEBUGGING.md) for debugging.

## Workspace scope

References and rename search `.rukbat` files in all workspace folders, including
closed callers. Discovery excludes `.git`, `bin`, `obj`, links, and junctions;
explicit imports use the normal module loader. Open buffers override disk contents.
Without a workspace folder, search covers the entrypoint and its imports. Callers
outside that scope cannot be discovered automatically.

Related source errors prevent a complete result. Unrelated errors do not block
search if imports can be determined. Unreadable sources, failed discovery, or
lexical errors hiding potential imports are reported rather than silently omitted.

## Find references

**Find All References** finds declarations, reads, writes, calls, construction
fields, annotations, and designated type arguments by bound declaration. Identical
spellings in different scopes remain separate. Results are deduplicated and sorted
by URI/position; the client chooses whether to include declarations.

## Rename a symbol

F2 renames user variables/constants, parameters, loop/catch bindings, functions,
methods, structs, fields, and relative-import aliases. Standard names, built-ins,
and `self` cannot be renamed.

- Exported declarations require a workspace folder. All declarations and edits
  must be inside it; single-file mode allows only local names in that file.
- Related graphs with links/junctions are refused to avoid multiple module paths
  for the same physical file.
- Edits are checked in memory against identifier rules, source errors, and all
  original bindings. Redeclarations and accidental capture/shadowing are rejected,
  even if the edited code would compile. The same spelling produces no edits.
- VS Code applies changes and provides Undo; saving remains the client's decision.
  Failed validation returns no partial edit, and the server never writes files.

### Documentation and external data

Rename updates bound shorthand links such as ``[`files.Record.name`]`` and
references inside independently checked `rukbat check` fences, preserving comment
prefixes and source positions. Ordinary prose, strings, Markdown links, unchecked
examples, and PowerShell strings remain unchanged.

Static binding safety does not guarantee external compatibility. A struct-field
rename changes its JSON key; changing `test_` can affect test discovery. Review
external data and other projects, then regenerate HTML with `rukbat doc`.

## Signature help

Typing `(` or `,`, moving within a call, or requesting parameter help shows the
signature and active argument. Closing an inner call returns to its outer call;
nested literals and quoted commas do not advance the outer argument index.

Supported targets include user/imported functions, methods, standard APIs,
built-ins, array `append`, and dictionary methods. Designated generic operations
select the matching signature. PowerShell's variadic tail uses its final label.
Zero-parameter calls and extra fixed-arity arguments have no active parameter.
Declarations, strings, comments, grouping parentheses, and unresolved calls have
no call hints. Struct-literal field hints and inferred-type hints are unavailable.

Incomplete calls are analyzed in a temporary copy; diagnostics still describe the
real source. Signatures use bound declarations/comments and the standard catalog.

## Language-server protocol

`rukbat lsp` uses stdio with UTF-8 Content-Length framing and UTF-16 positions.
Stdout is protocol-only; logs use stderr. Only `file:` documents are supported.
The server advertises full-document sync and accepts ordered incremental edits.
Changes, saves, closes, and watched-file changes trigger reanalysis.

Completion may repair missing members or delimiters in a temporary copy. Syntax
errors can limit semantic features; real-source diagnostics remain unchanged.

Rename requires versioned `documentChanges` and transactional or text-only-
transactional edits. Prepare Rename returns a range/name without reserving a source
version. Rename rechecks current contents: open files carry their LSP version;
closed files carry null versions and are reread before returning. Detected changes
produce ContentModified; changes after the response remain a client/filesystem race.

Documentation hover and completion share comment attachment and validation with
[the documentation tool](DOCUMENTATION.md#reading-and-maintaining-the-result).
