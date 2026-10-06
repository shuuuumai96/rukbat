# Editor features

The language server checks source without generating or executing PowerShell.
Open a saved `.rukbat` file in a trusted VS Code workspace. Hover, completion,
definition, formatting, references, rename and signature help use the same
compiler front end and the editor's unsaved contents.

## Find references

Find All References identifies declarations, reads, assignments, calls, struct
construction fields, type annotations and designated type arguments by their
bound declaration. Same-spelled names in different scopes/modules stay separate.
The client controls whether declarations appear in the results. Locations are
deduplicated and sorted by URI and position; positions use UTF-16 as required by LSP.

Workspace folders, including multiple folders, are searched for `.rukbat` files.
Discovery excludes `.git`, `bin`, `obj`, directory junctions and symbolic links.
Explicit imports are still read through the normal module loader. Open buffers
override disk contents; closed callers do not have to be opened. Without a
workspace folder, the current entrypoint and its imports define the search scope.
Callers outside this declared scope cannot be discovered automatically.

Related source errors prevent a complete reference result and are reported with
their location. Unrelated errors do not block a search when imports can be
determined. A lexical error that prevents determining a potential caller's imports,
an unreadable source, or failed directory discovery prevents a complete result.
The server does not silently omit those files.

## Rename a symbol

F2 renames user variables, constants, parameters, loop/catch variables, functions,
methods, structs, fields and relative-import aliases. Standard APIs, standard
imports/types/fields/constants, built-ins and `self` are fixed names. Exported
declarations require an open workspace folder so closed callers can be discovered.
Declarations and required edits must be inside the workspace. In single-file mode,
local names can be changed only in that file. Related source graphs containing
links/junctions are refused because one physical file can have multiple module paths.

The server validates the new identifier with existing lexical/reserved-word rules,
builds the edits in memory, then rechecks every related entry. It compares all
original reference bindings, including references whose spelling did not change.
Rename therefore rejects both redeclarations and accidental capture/shadowing even
when the resulting code would compile. The mapping accounts for changed token
lengths and source positions. Renaming to the same spelling produces no edits.

Prepare Rename returns the selected name's range and spelling. The actual rename
rechecks the latest contents; preparation does not reserve a source version.
Open documents carry their LSP version in the returned edits. Closed documents
carry a null version and are reread immediately before returning to detect changes
during validation. A change is reported as ContentModified and the user can retry.
An external change after the response remains a client/filesystem race.

Rename requires a client supporting versioned `documentChanges` and transactional
or text-only-transactional edit application. VS Code applies the edits, provides
Undo, and decides when to save. The server never writes files or returns a partial
edit after a failed validation.

## Documentation and external data

Bound shorthand documentation links follow a rename, including import/struct
qualifiers such as ``[`files.Record.name`]``. `rukbat check` fences are independent
virtual sources: references to actual renamed declarations are updated and the
examples are rechecked. Their comment prefixes and original source positions are
preserved. Ordinary prose, strings, Markdown links, unchecked fences and PowerShell
code strings are not rewritten.

Safety means preserving static reference bindings within the declared search
scope. It does not establish business-level compatibility: renaming a struct field
changes its JSON key; changing a `test_` prefix can change test discovery. External
data, other projects, unchecked examples and PowerShell strings need author review.
Generated HTML documentation is regenerated with `rukbat doc`, not edited by LSP.

## Signature help

Typing `(` or `,`, moving within an active call, or explicitly requesting parameter
help displays the callable signature and active argument. `)` rechecks nesting and
returns to the outer call when appropriate. Nested calls, arrays, struct literals
and commas inside strings do not advance an outer argument index. Function
declarations, strings, comments, unresolved calls and grouping parentheses do not
produce call hints.

Supported targets are user functions, imported functions, struct methods, standard
APIs, built-ins and array `append`. Designated generic operations select the matching
signature. Struct-literal field hints are not supplied. A zero-parameter signature
has no active parameter; an extra argument on a fixed-arity call has no hint.
PowerShell's variadic tail uses its final parameter label.

Incomplete calls use syntax facts and a temporary analysis copy to bind the real
receiver/callee. The normal compiler diagnostics always describe the original
document. If the target cannot be resolved, help is omitted. Standard signature
labels and descriptions come from the existing canonical standard-library catalog;
user signatures and descriptions come from bound declarations and their comments.
