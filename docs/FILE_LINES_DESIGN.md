# File Line Streaming

`fs.lines(path, encoding)` creates an immutable `fs.Lines` descriptor. Each `for`
opens an independent traversal and owns its cleanup. The implementation uses an
internal iteration contract shared with array iteration.

The [standard library](STDLIB.md#sequential-line-reads) defines the public API,
the [language specification](language.md#control-flow) defines loop semantics, and
the [architecture](ARCHITECTURE.md#standard-library-implementation) identifies compiler
and runtime responsibilities.

## Purpose and design rationale

`fs.read_text` retains a complete decoded file. Combining it with `text.split_lines`
also materializes the line array. Sequential traversal lets scripts search, count,
and transform physical lines while retaining only the data they need. The
[filter-errors example](../examples/filter-errors.rukbat) demonstrates this use.

An inert descriptor fits Rukbat's value-copy semantics: copying a value does not
share a mutable read position or create a cleanup obligation. Each loop acquires
and releases its own resource, including when `break`, `return`, or an Error leaves
the traversal. This uses ordinary procedural control flow without introducing a
public reader, resource-management statement, iterator interface, or generator syntax.

Loop-owned cleanup has precedents in
[C# foreach](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/statements#1395-the-foreach-statement)
and [Lua generic for](https://www.lua.org/manual/5.4/manual.html#3.3.5).
Rukbat's descriptor and repeatability rules follow its own value semantics.
The decision follows [PHILOSOPHY.md](PHILOSOPHY.md): retain simple, explicit control
flow and add only the language support required by a standard-library operation.

The design targets synchronous reads of regular files. Log tailing, asynchronous
I/O, cancellation, seeking, resumable positions, writable streams, and incremental
CSV/JSON parsing are outside its scope. Physical lines are not CSV records;
multiline CSV requires a record parser.

## Traversal ownership and cleanup

A cursor is active until it reaches EOF, fails, or is closed. Opening acquires the
stream and releases it if cursor initialization fails. Traversal state is never
written into the descriptor.

`MoveNext` clears `Current` before attempting retrieval and sets it only when a line
is available. An ended cursor returns false without further reads. A failed or closed
cursor cannot resume. EOF leaves final handle release to the generated `finally`.

| Control path | Cleanup and transfer |
|---|---|
| EOF or `break` targeting the traversal | Closes before the following statement. |
| `return` | Evaluates and copies the value, then closes before emitting it. |
| Escaping body or read Error | Closes before an outer catch receives the Error. Completed side effects remain. |
| `continue` or an Error handled inside the body | Keeps the traversal open. |
| Exit from an inner loop only | Keeps the outer traversal open. |
| Valid `exit` or forced host termination | Does not guarantee generated cleanup; OS handle reclamation is distinct from cleanup execution. |

The stream uses `FileMode.Open`, `FileAccess.Read`, and `FileShare.Read`. Other readers
are permitted; incompatible writers, deletion, or replacement cause sharing errors.
Access to a log being written is not guaranteed. Links follow ordinary filesystem
resolution, and concurrent modification has no snapshot guarantee. The implementation
does not independently bound network latency or special-device behavior.

`Close` is idempotent. It detaches stream, decoder, buffer, accumulated line, decoded
chunk, and `Current` references before disposing the stream. A disposal failure therefore
does not trigger a second disposal on a later `Close`. Garbage collection need not be
immediate, and a failed disposal does not guarantee release of the file lock.

Only reaching EOF flushes the decoder. Closing after an early exit neither decodes
nor validates unread input. A previously raised read-ahead error still follows the
ordinary Error path.

## Decoding and buffering

The read path is `FileStream -> BOM handling -> decoding -> line splitting`.
Splitting decoded text avoids assuming common raw-byte values for CR and LF across
code pages. `RukbatCodePageCodec` uses strict Windows conversion calls and does not
derive from `System.Text.Encoding`; a default `StreamReader` would not preserve all
of the selected encoding and BOM behavior.

### Independent decoders

Each traversal receives a new decoder. Unicode, ASCII, and Latin1 use the selected
strict `System.Text.Encoding.GetDecoder()`. Split sequences remain in decoder state
until more bytes arrive or EOF requests a flush. There is no replacement fallback
or Unicode normalization.

Windows codecs other than GB18030 use the following algorithm:

1. Concatenate the retained suffix and new input.
2. Attempt whole-block conversion with the same native flags as `GetString`.
3. On `ERROR_NO_UNICODE_TRANSLATION` only, retry with one through three trailing bytes
   removed. Convert the longest accepted prefix and retain the suffix. An empty prefix
   is allowed. Other native errors propagate without retry.
4. If no prefix converts, fail the decoder.
5. At EOF, strictly convert all retained bytes. An incomplete or invalid suffix causes Error.

This relies on stateless conversion, a maximum incomplete suffix of three bytes, and
accepted prefixes that later bytes cannot reinterpret. The codec excludes stateful
formats.

The native codec and Unicode adapter expose
`Func<byte[], int, int, bool, string>` internally, with the boolean requesting EOF flush.
This standard delegate avoids dynamic assembly references between native helpers and
PowerShell callbacks during conversion. A conversion returns one string or throws;
it never returns a partial result with an error. Successful final conversion or a
validation/conversion failure terminates the decoder; it cannot be reused afterward.

### GB18030 validation

GB18030 uses explicit sequence validation before native conversion. Native strict
flags alone are insufficient to enforce rejection of unmapped four-byte ranges.
The validator recognizes these units:

| Length | Byte ranges |
|---|---|
| One | `00..7F` |
| Two | `81..FE`, then `40..7E` or `80..FE` |
| Four | `81..FE`, `30..39`, `81..FE`, `30..39` |

Other shapes cause Error. Incremental validation retains up to three bytes of an
incomplete unit and rejects incomplete input at EOF. Known boundaries replace
suffix-trimming retries for this codec.

For a four-byte unit, the pointer is
`p = (((b1 - 0x81) * 10 + b2 - 0x30) * 126 + b3 - 0x81) * 10 + b4 - 0x30`.
The validator rejects `39420 <= p < 189000` and `p > 1237575`, using named constants
for these boundaries. The
[Encoding Standard's GB18030 range check](https://encoding.spec.whatwg.org/#index-gb18030-ranges-code-point)
defines these unmapped ranges. Windows still supplies valid character mappings,
including its two-byte mappings.

The shared scanner returns the length of the validated complete prefix. Whole-input
conversion validates the complete input before calling
[MultiByteToWideChar](https://learn.microsoft.com/en-us/windows/win32/api/stringapiset/nf-stringapiset-multibytetowidechar).
Incremental conversion validates the retained suffix plus new bytes and converts only
the complete prefix. `fs.read_text`, line traversal, and process output decoding share
this validation. A literal question mark remains valid content.

### Buffer and line retention

The cursor uses a 64 KiB I/O buffer, an internal tuning constant. The initial read
collects up to four bytes for BOM inspection, including across short reads. Only a
zero-byte read establishes EOF; a short read alone does not.

A decoded chunk stays available until consumed. CR/LF-delimited spans accumulate in
a `StringBuilder`, and one pending-LF flag joins CRLF across chunk boundaries. A trailing
terminator adds no extra line. A line may be returned at CR without waiting for the
following LF; the exact read timing is not a public guarantee.

Storage grows with line length without repeatedly converting the accumulated prefix.
Additional retained memory is `O(B + L)` per traversal, where B is the fixed buffer
size and L is the longest line encountered; traversal targets `O(n)` work in input
length. Native input/output buffers, decoded chunks, accumulated text, and completed
strings can overlap in lifetime, so no exact byte multiplier is guaranteed.

There is no line-length limit. A single line spanning the file still requires memory
proportional to that file, and recovery from memory exhaustion is not guaranteed.
Nested traversals, user-retained lines, and ordinary function locals add their own
storage. Closing clears internal references without requiring immediate collection.

## Binding, generation, and diagnostics

The checker records a closed `BoundIteration` for each `for`: `ArraySnapshot` or
`OwnedCursor`, the element type, and the internal open operation when applicable.
Arrays retain snapshot iteration and element copies. `fs.Lines` selects owned
iteration with string elements. The emitter consumes this information rather than
rediscovering iterability from type-name strings or call spellings.

Bound type identity distinguishes standard descriptors from same-name user structs.
Nullable and unsupported loop inputs receive ordinary type diagnostics. The parser,
AST, and formatter use the existing `for` syntax. `MoveNext`, `Current`, `Close`, and
`fs.__open_lines` are private implementation operations, excluded from public calls
and completion. Descriptors are annotated as `object` in PowerShell so type resolution
does not require loading native helpers before their first use. Descriptors and
cursors do not implement `IEnumerable`, preventing implicit PowerShell enumeration.

The generated traversal has this structure; this is pseudocode:

```text
source = evaluate_once(input)
cursor = null
primary = null
try:
    location = for_header
    cursor = open_via_standard_runtime(source)
    while true:
        location = for_header
        if not cursor.move_next(): break
        bind_string(cursor.current)
        emit_body_in_current_scope()
catch error:
    primary = normalize(error, location)
    raise(primary)
finally:
    try:
        close_or_raise(cursor, primary, for_header)
    finally:
        clear_cursor_source_and_loop_temporaries()
```

The body remains in the traversal's scope with no extra scriptblock or intervening
loop, preserving `break` and `continue` targets. Cleanup clears the temporary descriptor,
cursor, primary/cleanup errors, and loop-variable reference. Values saved by user code
and a pending return value remain intact.

### Error precedence and locations

| Failure site | Location and handling |
|---|---|
| Argument evaluation or descriptor construction | Ordinary evaluation location. |
| Open, BOM handling, `MoveNext`, or decoding | The `for` header; generation resets the location before each retrieval. |
| Body or called function | Preserves the location already attached to the Error. |
| `Close` alone | The `for` header; Error takes precedence over pending `break` or `return`. |
| Primary Error and `Close` failure | Preserves the primary message and location, appending cleanup-failure information. |

Combining errors constructs a new Error rather than mutating one retained by user
code. The existing internal `__rukbat_location` carries source location without new
public Error fields. Read-ahead can fail on later bytes, so a decoding Error identifies
the Rukbat operation's source location rather than an inferred physical file line.

### Deferred function return output

PowerShell writes a `return` expression to success output before executing `finally`.
If cleanup then fails and a catch returns another value, the earlier output would
remain. Functions and methods containing an owned traversal therefore delay output
until cleanup succeeds.

Generation wraps the entire body in a uniquely labeled `do { ... } while ($false)`.
Every return evaluates and copies its expression once into a local temporary, then
breaks to the function-exit label. A void-expression return evaluates its expression
once with output suppressed. If cleanup fails, the lexical catch handles it; recovery
can replace or cancel the pending return. Only the final result is emitted, preserving
ordinary null, array, nullable-array, and value-copy rules.

Function-specific labels and local temporaries keep recursive invocations independent.
Directly closing all readers at a return site would change which catch receives a
cleanup error, so cleanup remains in the traversal's lexical `finally`. Functions
without owned traversal retain ordinary return generation; module-level loops need
no function-return wrapper.

### Debugging

The function wrapper and final output remain inside the debug-frame cleanup scope.
Execution stops at the loop header on entry and at body statements, as for other
`for` loops. Updating the read-error location does not create another stop point.
Inspection displays `fs.Lines` as opaque and does not expand internal readers or
buffers. A paused traversal keeps its handle open under the normal sharing rules.

## Implementation

| Component | Responsibility |
|---|---|
| [StandardSignatures](../src/Rukbat/StandardLibrary/StandardSignatures.cs), [StructChecking](../src/Rukbat/Semantics/StructChecking.cs), [ValueType](../src/Rukbat/Semantics/ValueType.cs) | Canonical signature and opaque type identity. |
| [CallChecking](../src/Rukbat/Semantics/CallChecking.cs), [ExpressionChecking](../src/Rukbat/Semantics/ExpressionChecking.cs), [BoundaryChecking](../src/Rukbat/Semantics/BoundaryChecking.cs) | Construction, field, equality, printing, JSON, and PowerShell restrictions. |
| [StatementChecking](../src/Rukbat/Semantics/StatementChecking.cs), [BoundIteration](../src/Rukbat/Semantics/BoundIteration.cs) | Iteration strategy and element type. |
| [Emitter](../src/Rukbat/CodeGeneration/Emitter.cs), [StatementEmission](../src/Rukbat/CodeGeneration/StatementEmission.cs) | Traversal cleanup, source locations, and deferred return output. |
| [CodePageCodec](../src/Rukbat/Runtime/Native/CodePageCodec.cs.txt) | Shared strict Windows conversion, GB18030 validation, and independent incremental decoders. |
| [FileText](../src/Rukbat/Runtime/Native/FileText.cs.txt), [FilesRuntime](../src/Rukbat/Runtime/PowerShell/FilesRuntime.ps1), [StandardEmission](../src/Rukbat/CodeGeneration/StandardEmission.cs) | Shared BOM handling, descriptors, streams, line splitting, and lazy loading. |
| [EditorCatalog](../src/Rukbat/LanguageServer/EditorCatalog.cs), [LanguageAnalysis](../src/Rukbat/LanguageServer/LanguageAnalysis.cs) | Completion and type documentation using canonical signatures and bound identities. |

Native sources remain C# 5-compatible and are embedded in standalone generated scripts.
The feature uses the target's existing Windows PowerShell/.NET facilities. Authoring
tool packaging is described in [DISTRIBUTION.md](DISTRIBUTION.md).
