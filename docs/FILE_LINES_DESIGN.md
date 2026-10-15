# File Line Streaming: Iteration Ownership and Decoding

Status: Design baseline with local implementation added; not released.
Design and initial probes: 2026-10-14. Baseline commit: `a64fecc`.
The API is now documented in STDLIB.md and LANGUAGE.md. Section 10 records implementation
evidence and remaining release validation; sections 8 and 9 retain the original experiment
results and acceptance criteria.

This proposal recommends `fs.lines(path, encoding): fs.Lines`, with each `for` managing
the resources of its traversal. It introduces an internal iteration and cleanup contract,
but no public generic iterator type. Existing array iteration remains unchanged.

The original incremental-decoding proposal required G1 to establish compatibility across
the existing accepted encoding range. Implementation tests now exercise every locally
accepted native codec; wider Windows-version coverage remains a release requirement.
The investigation found that the baseline GB18030 codec substituted `?` for invalid
four-byte sequences. This violates the existing no-silent-substitution contract.
Repairing the shared codec is therefore a prerequisite, G0.

This revision withdraws the mandatory line-length limit, support restricted to the 26
predefined encodings, and guaranteed deferral of decoding errors in subsequent lines.
Those restrictions were not derived from usage requirements. It retains an immutable
read descriptor and loop ownership based on the comparison below.
Proposed behavior and measured results are separate; section 9 defines release gates.

## 1. Scope and memory requirements

The current [fs.read_text](../src/Rukbat/Runtime/PowerShell/FilesRuntime.ps1) uses
`ReadAllBytes` followed by whole-input decoding.
[filter-errors.rukbat](../examples/filter-errors.rukbat) then calls `text.split_lines`.
As the file grows, so does the memory needed to retain its text and line array together.
This implementation property is verified. Actual memory exhaustion in user workloads
and the required maximum file size have not been established.

The requirement is to read physical lines sequentially and use ordinary Rukbat statements
to aggregate, search, or transform them. Files must be released when a search uses
`break`, a function returns, or an Error escapes.
Existing encoding selection, BOM handling, Error semantics, and value-copy semantics
must be preserved. Deployment continues to require Windows PowerShell 5.1 and the
existing Windows/.NET facilities.

Excluding user-retained data, memory should be proportional to a fixed buffer plus the
longest line encountered. If one line spans the entire file, that line still requires
memory proportional to the file. Without a line-length limit, there is no bounded-memory
guarantee for adversarially large lines or guarantee of recovery from memory exhaustion.
The API changes the unit of retention from an entire file to one line.

Log tailing, asynchronous I/O, cancellation, seeking, resumable positions, writable
streams, and incremental parsing of CSV records or JSON documents are out of scope.
In particular, documentation must not recommend splitting multiline CSV records into
physical lines and passing them to `csv.parse`.

## 2. Iteration and resource-management alternatives

The following comparison separates observed language implementations from decisions
about whether their mechanisms belong in Rukbat.

| Reference | Iteration and cleanup | Assessment for Rukbat |
|---|---|---|
| Go 1.25 Scanner | `Scan` / `Text` / `Err`; the caller owns file closure. | Procedural, but requires a public reader, shared position, resource management, and a separate post-iteration error check. Do not copy it directly into Rukbat's single Error path. [Implementation](https://github.com/golang/go/blob/go1.25.0/src/bufio/scan.go) |
| Rust 1.90 Lines | `Lines<B>` holds a reader and returns `Option<Result<String>>`. File release follows ownership and Drop; a loop does not necessarily close a borrowed reader. | There is no justification for adopting ownership, borrowing, and Result together for this feature. [Iteration](https://github.com/rust-lang/rust/blob/1.90.0/library/std/src/io/mod.rs#L3204), [File cleanup](https://github.com/rust-lang/rust/blob/1.90.0/library/std/src/fs.rs#L49) |
| CPython 3.14 | A file is an iterator. EOF and break do not close it; a `with` statement invokes `__exit__` to close it. | Requires a reader and a general resource scope. Copying only the iterator protocol would not guarantee cleanup. [Iteration](https://github.com/python/cpython/blob/v3.14.0/Modules/_io/textio.c), [Cleanup](https://github.com/python/cpython/blob/v3.14.0/Modules/_io/iobase.c#L474) |
| C# / .NET 10 | `foreach` disposes applicable enumerators in finally. `File.ReadLines` opens on invocation and retains an implementation that shares a reader between enumerators. | Use iteration-owned cleanup as a reference, but do not copy File.ReadLines-specific ownership or repeated-enumeration behavior. [Reader implementation](https://github.com/dotnet/runtime/blob/v10.0.0/src/libraries/System.Private.CoreLib/src/System/IO/ReadLinesIterator.cs), [Language specification](https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/language-specification/statements#1395-the-foreach-statement) |
| Java 25 Files.lines | Registers an opened resource with Stream.onClose; callers close it through try-with-resources or equivalent. BufferedReader.lines alone does not register that cleanup. | Ownership cannot be inferred from an API name. Adding lazy enumeration alone is insufficient. [Files](https://github.com/openjdk/jdk/blob/jdk-25-ga/src/java.base/share/classes/java/nio/file/Files.java#L3625), [BufferedReader](https://github.com/openjdk/jdk/blob/jdk-25-ga/src/java.base/share/classes/java/io/BufferedReader.java#L536) |
| Ruby 3.4 File.foreach | The block form surrounds reading and closure with `rb_ensure`. | Library ownership is possible, but Rukbat has no function values or block arguments. Do not add callbacks and their return semantics for this feature. [Implementation](https://github.com/ruby/ruby/blob/v3_4_0/io.c#L11462) |
| Lua 5.4.8 io.lines | With a filename, io.lines returns a closeable resource as its fourth result; generic for closes it when the loop ends. | A precedent for including cleanup in a small iteration contract, without adopting Lua's multiple returns or dynamic typing. [Implementation](https://github.com/lua/lua/blob/v5.4.8/liolib.c#L363), [Specification](https://www.lua.org/manual/5.4/manual.html#3.3.5) |

Candidate approaches require different language and implementation changes.

| Candidate | Language and type changes | Resource ownership | Decision |
|---|---|---|---|
| Existing read_text and split_lines | None | Contained within calls | Still appropriate for small files; does not solve whole-file retention. |
| Public reader with using/finally/defer | A resource type, scope or statement, and definitions for copying and shared position | User-managed | Useful for coordinated reads, writes, and multiple resources, but adds permanent concepts beyond the current requirement. |
| Callback-based library ownership | Function values, captures, and callback control flow | Library-managed | Requires foundations absent from the current language. |
| Immutable descriptor and internal iteration contract | One opaque type, a new for input type, and cleanup rules | Each for owns an independent traversal | Recommended. Supports ordinary control flow without new syntax. |
| Public Iterator<T> | Public protocol, typing rules, and cleanup on termination | Ownership and cleanup responsibility require definition | No confirmed demand for user-defined iteration types. Keep the protocol internal. |
| Generators using yield | Suspension and resumption, retained locals, and cleanup while suspended | Requires a generator termination contract | Independent of Iterator. Not needed for file reading; do not add it. |
| Batches that reopen the file | APIs for position, character boundaries, and change detection | Each call closes its file | Does not preserve the same open file throughout traversal; complicates I/O and behavior under modification. |

A public reader is possible if handle sharing and lifetimes are defined. This feature
does not require those additions. Internal iteration still costs type diagnostics,
code generation, debugging support, and cleanup; absence of new syntax does not make
the change inexpensive.
This decision follows [PHILOSOPHY.md](PHILOSOPHY.md): consider the standard library first
and introduce only necessary language changes.

## 3. Public API and descriptor semantics

```text
fs.lines(path: string, encoding: encoding.Encoding): fs.Lines
```

```rukbat
import encoding
import fs
import text

count := 0
for line in fs.lines("application.log", encoding.utf8) {
    if text.contains(line, "ERROR") {
        count = count + 1
    }
}
print(count)
```

`fs.Lines` is an immutable opaque descriptor containing only an absolute path and a
canonical encoding name. It holds no cursor, file handle, codec, or module closure.
Evaluate arguments once, left to right, then resolve the path and validate the encoding
through the existing selector.
Resolve relative paths against the working directory captured at script startup.
Literal paths, empty paths, and drive-relative paths follow existing fs behavior.
Do not check existence or perform a trial open.
If descriptor creation requires loading a native helper, a load failure is an Error
at that call.

Allow storage in variables, const values, parameters, return values, structs, arrays,
dictionaries, and nullable values. Copies do not share a mutable read position.
A const descriptor does not freeze external file contents.
As with existing opaque types, recursively reject print/eprint, equality comparisons,
JSON conversion, and transfer across the PowerShell boundary, including inside containers.
There are no public fields, constructors, indexing operations, length, next, or close.
A nullable descriptor can be iterated after existing null checks narrow its type.
Elements of `fs.Lines[]` have type `fs.Lines`.

| Operation or situation | Observable behavior |
|---|---|
| Create, retain, or discard a descriptor | Does not open the target. An unused value retains no handle. |
| Enter for | Evaluate the input expression once and open the absolute path stored in the descriptor once. Existence and access errors occur here. |
| Repeat or nest traversal of the same value | Each for has its own handle and position, starting at the beginning. |
| Replace the file after descriptor creation | The next for opens the replacement. The descriptor is not a snapshot taken at creation. |
| Reassign the descriptor variable inside the body | Does not affect the active traversal. |
| Change the loop variable | Changes only the retrieved string value, not the traversal position. |

Repeatability preserves Rukbat's value-copy semantics and avoids imposing cleanup
obligations on unused values; it is not chosen to match .NET.
The tradeoff is that descriptor creation neither checks file existence nor preserves
the identity of a previously opened file.

## 4. Traversal ownership and cleanup

An internal traversal has states `Open -> Active -> Ended/Failed -> Closed`.
If initialization fails after partially opening resources, Open itself releases them.
None of this state is written back into the public descriptor.
MoveNext first clears Current's reference to the previous string and assigns a new
string only on success. Strings retained by user code are unaffected.
Ended returns false without reading again. Failed and Closed cannot resume.
Even at normal EOF, generated finally code owns final handle release.

| Control path | Cleanup and transfer |
|---|---|
| EOF or break targeting this loop | Close before executing the following statement. |
| return | Evaluate and copy the value normally, then close before emitting the return value. |
| Escaping body/read Error | Close before transferring to an outer catch. Do not roll back completed side effects. |
| continue or an Error handled inside the body | Keep the file open and continue traversal. |
| Termination of an inner loop only | Keep the outer traversal open. |
| Valid exit or forced termination | Do not guarantee finally execution. Preserve existing Environment.Exit behavior and distinguish it from OS handle reclamation. |

Open using `FileMode.Open / FileAccess.Read / FileShare.Read`, as existing read_text
and hash.file do. Permit other readers; report sharing conflicts with writers,
deletion, or replacement as Error.
The API does not guarantee access to logs that are currently being written.
Do not reevaluate the path after opening.
Explicit links follow normal filesystem resolution; no complete snapshot is guaranteed.
Target synchronous reads of regular files without independently controlling network
latency or special-device behavior.

Close is idempotent: detach the resource reference before releasing it, so the same
handle is not released twice.
Closing a read-only traversal does not flush the decoder or validate unread input.
Flush decoding only when a read reaches EOF.
After break, do not inspect previously unread invalid input and raise a new decoding error.

## 5. Line splitting and encoding behavior

Treat CRLF as one terminator; lone CR and LF also terminate lines. Exclude terminators
from returned strings. Preserve spaces, tabs, NUL, internal U+FEFF, U+0085, U+2028,
and U+2029.
Represent EOF with an internal bool. Never interpret an empty string as EOF or false.

| Text after BOM handling | Returned lines |
|---|---|
| `""` | No lines |
| `"a"`, `"a\n"` | `"a"` |
| `"\n"` | `""` |
| `"a\n\n"` | `"a"`, `""` |
| `"a\r\nb\rc\n"` | `"a"`, `"b"`, `"c"` |
| `"\r\r\n"` | `""`, `""` |

Complete traversal of valid input must produce the same result as
`text.split_lines(fs.read_text(path, encoding))` for identical contents.
Exclude concurrent file modification from this equivalence condition.
The implementation must not use read_text or whole-input splitting.

A release requirement is to support every encoding accepted by the existing
`encoding.Encoding`. Compatibility verification is incomplete and belongs to G1
in section 9.
Include additional values created by `encoding.from_code_page`, not just predefined
values. Restricting support to 26 encodings does not satisfy this requirement.
If an accepted encoding cannot be decoded compatibly in chunks, redesign rather than
silently expanding the rejected encoding range.
Preserve supported encodings and conversions of valid input.
The invalid GB18030 sequences in section 6 are implementation behavior that violates
the existing specification; G0 explicitly corrects that behavior.

| Selected encoding | Leading BOM handling |
|---|---|
| UTF-8 | Remove only the matching three bytes. |
| UTF-16 LE/BE | Remove the matching two bytes. An opposite-endian two-byte BOM causes Error. |
| UTF-32 LE/BE | Remove the matching four bytes. An opposite-endian four-byte BOM causes Error. |
| Other encodings | Do not treat a BOM specially. |

A missing BOM is valid. Do not automatically switch encodings.
With UTF-16LE selected, `FF FE 00 00` removes only the first two bytes and retains NUL.
A second BOM is content. Share BOM handling with current read_text and regression-test
behavior before and after extraction.

Decoding or I/O errors during read-ahead may occur before any line from that chunk
is returned. If invalid bytes follow a valid first line, whether that line is returned
depends on read boundaries.
Break does not undo a read-ahead error that has already occurred.
Only complete traversal validates the entire input.
For invalid input, do not guarantee how many line bodies execute.
Python and .NET exhibit similar read-ahead behavior, but the reason for this choice
is to avoid implementing a separate error-deferral mechanism.

## 6. Incremental decoding and buffering

Separate reading into `FileStream -> BOM handling -> decoding -> line splitting`.
Do not assume shared raw-byte values for CR/LF. In the experiment, LF in additional
code page CP37 was `25`.
Existing [RukbatCodePageCodec](../src/Rukbat/Runtime/Native/CodePageCodec.cs.txt)
does not derive from System.Text.Encoding; it uses Windows conversion with strict flags.
Simply replacing it with StreamReader would not preserve the existing contract,
including CP932 extensions.

### Create a decoder per traversal and retain incomplete suffixes

For Unicode, ASCII, and Latin1, use the existing strict
`System.Text.Encoding.GetDecoder()`. Retain sequences split across input chunks and
flush at EOF. Do not add replacement fallback, automatic BOM detection, or Unicode
normalization.

For Windows codecs other than GB18030, add an internal decoder with independent state.
The candidate algorithm is below. GB18030 uses the dedicated boundary validation
described later.

1. Concatenate the retained suffix and new input.
2. Convert the entire block with the same native flags as existing GetString.
3. Only for `ERROR_NO_UNICODE_TRANSLATION`, retry prefixes with one through three
   trailing bytes removed. Emit the longest decodable prefix and retain the removed
   suffix for the next call. An empty prefix is allowed.
4. If no prefix converts, raise Error. Propagate other Win32 errors without retrying.
5. At EOF, strictly convert the entire retained suffix. Incomplete or invalid sequences
   cause Error; never discard the suffix.

This requires a stateless codec whose successfully decoded prefix cannot be
reinterpreted by subsequent bytes, and whose split conversion matches whole-input
conversion. An incomplete suffix must also be at most three bytes.
Do not distinguish an invalid suffix from an incomplete suffix immediately; always
recheck it with the next input or at EOF.
The current codec excludes stateful formats, but that alone does not establish these
premises for all additional code pages.
Do not infer general character boundaries solely from
[CPINFOEXW MaxCharSize/LeadByte](https://learn.microsoft.com/en-us/windows/win32/api/winnls/ns-winnls-cpinfoexw).
The candidate has passed the experiments in section 8; G1 in section 9 remains the
condition for finalizing its released implementation.
If a premise has a counterexample, add codec-specific boundary handling to satisfy
the same public contract. Do not release the for extension before G1 passes.

Do not add dynamic assembly references between native helpers.
The codec's `CreateDecoder()` returns the standard .NET delegate type
`Func<byte[], int, int, bool, string>` to the reader.
Provide a Unicode adapter with the same signature; bool requests the EOF flush.
Keep conversion in synchronous native calls rather than invoking PowerShell scriptblocks
as per-line conversion callbacks.
Do not introduce a public function type or replace existing whole-input conversion
with this decoder. The GB18030 validation below is shared between whole-input and
incremental conversion.

### Validate GB18030 sequences before native conversion

On 2026-10-14, current GetString converted `84 31 A5 30` and `FE 39 FE 39`
to U+003F without throwing.
Agreement between whole-input and incremental conversion does not establish strictness
when both perform the same substitution. This violates STDLIB's statement that
"no converter silently substitutes characters."
Specifying `MB_ERR_INVALID_CHARS` to
[MultiByteToWideChar](https://learn.microsoft.com/en-us/windows/win32/api/stringapiset/nf-stringapiset-multibytetowidechar)
did not prevent the problem in the tested environment.

The proposed validator scans GB18030 in one-, two-, or four-byte units.
A one-byte unit is `00..7F`. A two-byte unit starts with `81..FE`, followed by
`40..7E` or `80..FE`.
A leading `81..FE` followed by `30..39` starts a four-byte unit, requiring
`81..FE` as its third byte and `30..39` as its fourth.
Other shapes cause Error.
At a chunk boundary, retain up to three bytes of an incomplete unit.
An incomplete unit at EOF causes Error.
Because boundaries are known, GB18030 does not retry native conversion by trimming suffixes.

Compute the four-byte pointer as
`p = (((b1 - 0x81) * 10 + b2 - 0x30) * 126 + b3 - 0x81) * 10 + b4 - 0x30`.
Raise Error if `39420 <= p < 189000` or `p > 1237575`.
These ranges match both the
[Encoding Standard's four-byte range check](https://encoding.spec.whatwg.org/#index-gb18030-ranges-code-point)
and the observed substitution ranges.
Do not adopt the complete web codec or its two-byte mapping table.
Continue using Windows for valid character mappings without replacing Windows-specific
mappings wholesale. Express boundary values as responsibility-specific named constants
and document their source in comments.

Use an internal operation equivalent to `Scan(bytes, offset, count, final)`, returning
the length of the validated complete prefix.
Whole-input conversion validates all input with final=true before invoking native
conversion. Incremental conversion validates the previous suffix together with new
input and converts only the complete prefix.
For the incremental decoder, empty input with final=false leaves state unchanged and
returns an empty string. Allow final=true only once: success transitions to Finished,
and failure to Failed. Further conversion requests violate the internal contract.
A call that fails validation or conversion returns no partial string.
Close does not flush the validator.

Adding validation to whole-input conversion makes previously substituted invalid input
raise Error. Do not hide this behavior change inside the new API.
Treat G0 as a separate bug fix and regression-test callers of the shared codec,
including fs.read_text and process output decoding.
The local implementation adds this shared validator; section 10 records its verification.

### Build at most one line at a time

Start with a 64 KiB I/O buffer, an internal tuning constant rather than an API contract.
Do not treat a short read as EOF; a zero-byte read transitions to EOF.
BOM detection retains at most four bytes and forwards unconsumed bytes to the decoder.
Retain a decoded chunk until consumed before advancing to the next chunk.

Find CR/LF and append spans to StringBuilder.
Track whether one LF immediately following CR must be skipped, so CRLF split across
chunks does not introduce an empty line.
A trailing terminator does not create an extra empty line.
A line may be returned at CR, but the timing of the next read or decode is not a public
guarantee.
Grow storage with line length rather than reserving a maximum line in advance.
Avoid repeatedly converting or concatenating the entire accumulated prefix of a long line.

Target `O(B + L)` additional memory per traversal and `O(n)` time in input length.
B is the fixed chunk size and L the longest line encountered.
Native conversion storage, input copies, StringBuilder, and completed strings may
overlap in lifetime; do not promise an exact byte multiplier.
Clear Current and buffer references on Close. Do not create a whole-file line list
or a static reader registry.
Account separately for user-retained lines, nested readers, and existing function locals.
Do not require immediate garbage collection.

## 7. Type checking, lowering, and diagnostics

Introduce internal `BoundIteration` information recording input semantics, element type,
cleanup requirements, and the standard runtime open operation.
Initially, only arrays and fs.Lines are supported.
This is a closed compiler representation, not a user-implementable interface or a
general plugin mechanism.
Centralize the mapping from standard types to iteration information in type checking.
The emitter must not rediscover iterability from type-name strings or call spellings.
Distinguish `ArraySnapshot` from `OwnedCursor`, with an internal open operation for
the latter. Fix one internal protocol: MoveNext/Current for retrieval and Close for
cleanup. Do not initially add configurable method names or automatic application to
user-defined types.

Preserve array snapshots and element copying.
fs.Lines provides resource-owning iteration with string elements.
Distinguish user-defined structs with the same name by type identity.
Reject nullable and unsupported input types through existing type diagnostics.
The parser remains responsible only for syntax; add no syntax to the lexer, parser,
AST, or formatter.

The generated resource-owning loop has the following shape. This is pseudocode,
not production code.

```text
source = evaluate_and_copy_once(input)
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
    raise(primary)  // Preserve the original location.
finally:
    try:
        close_or_raise(cursor, primary, for_header)
    finally:
        clear_cursor_source_and_loop_temporaries()
```

Do not wrap the body in another scriptblock or insert another loop between the body
and the traversal while. This preserves ordinary break/continue targets.
Neither descriptor nor cursor implements IEnumerable, avoiding automatic PowerShell
enumeration and pipeline output.
Do not register the open operation as a public API or LSP completion.
Descriptors crossing modules use the same runtime.
Annotate descriptors as object in PowerShell so native type resolution is not required
before lazy Add-Type.

In finally, clear references to the cursor, temporary descriptor, primary error,
temporary Current, and loop variable.
Use clearing operations compatible with existing type annotations; cleanup of temporary
references must not introduce a new Error after successful resource release.
Do not clear lines saved in outer variables or the pending return-value temporary.

### Preserve the primary Error and source location

| Failure site | Location and handling |
|---|---|
| Argument evaluation or descriptor construction | Existing evaluation location. |
| Open, BOM handling, MoveNext, or decoding | The relevant for header; update location before each MoveNext. |
| Body or called function | Preserve the location already recorded in the Error. |
| Close alone | The for header. Error takes precedence over pending break/return. |
| Both a primary Error and Close failure | Preserve the primary message and location; append cleanup-failure information to the message. |

Construct a new Error when adding cleanup information; do not mutate an Error retained
by user code.
Pass internal `__rukbat_location` through the existing raise path without adding public
Error fields.
Because decoding failures may concern later lines in a prefetched chunk, do not report
the next expected line number as the invalid input's line number.
Report the known operation, encoding, and source location.
If Close fails, do not guarantee that the file lock was released.

### Emit return values only after successful cleanup

PowerShell's `return 42` writes 42 to success output before finally runs.
If finally fails and a catch in the same function returns 99, both 42 and 99 remain
in the result.
Adding finally to existing
[EmitReturn](../src/Rukbat/CodeGeneration/StatementEmission.cs) is therefore insufficient.

For a function or method containing resource-owning iteration, surround the entire
body with a uniquely labeled `do { ... } while ($false)`.
Lower every return in that function into expression evaluation, ordinary value copying,
temporary result storage, and break to the function-exit label.
For a return with a void expression, evaluate it once and suppress output before
transferring control.
If finally fails while transferring to the label, enter the catch associated with the
original location. If that catch recovers and continues execution, do not emit the
canceled return value.
Return only the final result once, using existing rules for null, arrays, and nullable
arrays.

Do not close all readers directly at each return statement: that changes which errors
an inner catch can handle.
Use function-specific labels and result variables; recursive invocations use their
own local values.
Place both the wrapper and final output inside the existing debug-frame finally scope.
Do not change generation for functions without resource-owning iteration.
A module-level for needs no function-return wrapper.

As with existing for loops, debugging stops at the header on entry and at each body
statement. Updating the read location does not add a stop point.
Do not expand internal readers or buffers.
Handles remain open while paused, so file editing remains subject to the sharing mode.

### Implementation responsibilities

| Area | Planned change |
|---|---|
| StandardModules / StandardSignatures | Define fs.Lines and its fixed two-argument signature. Do not duplicate signatures for completion. |
| StructChecking / ValueType / CallChecking / BoundaryChecking | Type identity, opaque construction, and boundary restrictions. Do not change display behavior for fs.Info and other types wholesale. |
| StatementChecking / TypeChecker | BoundIteration and element types; preserve existing nullable, loop-scope, and flow behavior. |
| StatementEmission / Emitter | Resource-owning iteration, location updates, cleanup, and return lowering only where required. |
| CodePageCodec.cs.txt | Add GB18030 whole-input validation in G0, then an independent incremental decoder that preserves mappings. |
| New FileText.cs.txt / FilesRuntime / StandardEmission | Shared BOM handling, immutable descriptors, internal cursors, line splitting, and lazy loading. Connect native helpers through standard delegates. |
| DebugEmission / VS Code debug adapter | Opaque display for fs.Lines; verify stop points, variable types, and lifetimes. |
| LANGUAGE / STDLIB / ARCHITECTURE | Update the authoritative documents with for semantics, APIs, and responsibilities during implementation. This proposal does not replace confirmed specifications. |

Because read_text will call the shared BOM helper, existing APIs need regression tests.
Sharing avoids duplicated BOM rules; it does not authorize changes to existing conversion
or accepted encoding behavior.
Keep native source compatible with C# 5 and embed it in generated ps1 files.
Require no additional NuGet packages or deployment runtime.

## 8. Experimental evidence and limitations

Initial local experiments ran on 2026-10-12; exhaustive boundary experiments ran on
2026-10-14. The two initial probes were also rerun on 2026-10-14 with the same results.
These are not tests of an implemented fs.lines product API.
Reproduction scripts are the [codec probe](probes/file-lines-codec.ps1),
[control-flow probe](probes/file-lines-control.ps1), and
[boundary probe](probes/file-lines-boundaries.ps1).
The boundary probe examines native acceptance and strict decoding separately.

| Experiment | Observed result | Limitations |
|---|---|---|
| Candidate Windows chunk decoding | On each of PowerShell 5.1.26100.9549 and 7.6.6, 114 code pages accepted by the current native codec matched whole-input conversion for 18,009 accepted cases and 4,581 rejected cases. | Does not cover all byte sequences or Windows versions. 114 is a local observation, not a public support count. |
| All two-byte boundaries | On both hosts, 114 code pages times 65,536 inputs, or 7,471,104 pairs, matched whole-input results/rejection when split into single-byte chunks. All 256 individual bytes were also examined per code page. | Does not prove behavior for concatenations of three or more bytes, encoding format properties, or correctness against independent mapping tables. |
| GB18030 four-byte shapes | On both hosts, 1,587,600 shapes matched at all three split positions, totaling 4,762,800 comparisons. Native conversion rejected 160,146 incomplete prefixes. | Native conversion accepted every complete four-byte shape, substituting `?` for 499,604 of them. These substitutions are not valid-input successes. |
| Function-exit labels | All 15 assertions passed on both hosts: successful return, replacement return after Close failure, inner catch/outer finally order, break/continue, nullable arrays, void expressions, continuation after catch, and recursion. | Compiler lowering, debug frames, and actual file release remain untested. |
| Python/.NET read-ahead from earlier research | Python 3.14.3 and .NET under both PowerShell hosts raised on the first line retrieval when invalid UTF-8 followed a valid line. | The same input was not executed across the other reference languages. |

The codec probe checks current constructor acceptance for code-page numbers 4 through
65535. Inputs include valid single bytes, fixed-seed two-byte samples, terminators/NUL,
CP932 NEC/IBM extensions, GB18030 four-byte sequences, and fixed-seed potentially
invalid inputs. It compares chunk widths 1 through 9.
This is not exhaustive single-byte mapping-table validation or a proof over all
multibyte sequences.
PowerShell 5.1 used CLR 4.0.30319.42000; PowerShell 7.6.6 used CLR 10.0.12.

The boundary probe observed `?` for four-byte pointers 39420 through 188999 and
1237576 through 1587599.
The earlier random sample did not include this check, so agreement with whole-input
conversion did not establish strictness.
The added probe cross-checks its whole-input reference on small inputs and checks
UTF-16 marshalling of native output.
It does not implement or validate the proposed G0 validator.

Run the following from the repository root. The probes do not modify product files.

```powershell
Get-Content docs/probes/file-lines-codec.ps1 -Raw | powershell.exe -NoProfile -Command -
Get-Content docs/probes/file-lines-codec.ps1 -Raw | pwsh -NoProfile -Command -
Get-Content docs/probes/file-lines-control.ps1 -Raw | powershell.exe -NoProfile -Command -
Get-Content docs/probes/file-lines-control.ps1 -Raw | pwsh -NoProfile -Command -
Get-Content docs/probes/file-lines-boundaries.ps1 -Raw | powershell.exe -NoProfile -Command -
Get-Content docs/probes/file-lines-boundaries.ps1 -Raw | pwsh -NoProfile -Command -
```

## 9. Release gates

The following completion criteria remain outstanding. Partial experiments do not
establish implementation completion or compatibility guarantees.

| Gate | Acceptance criteria |
|---|---|
| G0: Repair existing strictness | Reject invalid GB18030 four-byte sequences in the shared codec. Cover all four-byte shapes, incomplete prefixes, values on either side of every boundary, one-/two-byte input, legitimate `?`, and existing mappings. Share validation between whole-input and incremental conversion. Regression-test existing fs/process paths and Errors on both hosts. |
| G1: Establish decoding correctness | Classify boundary assumptions across the 26 predefined encodings and accepted additional code pages. Cover all SBCS single bytes, standalone leads/all pairs for applicable DBCS codecs, GB18030 boundaries and invalid sequences, and additional values including CP37/858/20261/20949. Verify agreement for valid whole-input conversions and no silent replacement or deletion of invalid sequences. Resolve counterexamples before release. |
| G2: Internal reader | Test BOMs, UTF-8/16/32, multibyte characters, and CRLF at every split position and with short reads. Include BOM-only input, opposite/partial/doubled/other-format BOMs, NUL, Unicode separators, empty lines, and long unterminated lines. Flush remaining input at EOF; do not validate unread input on Close. |
| G3: Static semantics and generation | Test descriptor copies, const, nullable, parameters/returns, structs/arrays/dictionaries, same-name user types, recursive boundary rejection, and argument count/types. Cover array-snapshot regressions, one-time evaluation of for input, cross-import use, descriptor reassignment, and independent nested traversal. |
| G4: Cleanup and Error | After EOF/break/return/throw/decode failure, verify exclusive open and rename/remove, including immediately upon entering catch. Keep outer traversals open on continue or inner break. Inject read/Close double failures and preserve primary Error location. Release resources after post-open initialization failure. |
| G5: Return and debug | Reproduce the 15 assertion scenarios through Rukbat generation. Include void, void-expression returns, null, arrays, nullable arrays, struct copying, methods, recursion, and return-expression side effects. Verify in normal/debug generation that Close failures reach the correct catch and no return value is emitted prematurely. |
| G6: Windows and capacity | Cover literal metacharacters, Japanese paths, cwd changes, links, and sharing conflicts. Identify the environment used for UNC validation. Increase file size while holding line length constant; measure full traversal, maximum internal retention, and process memory. Inspect post-Close reader references in nested traversals and long-lived functions. Do not use absolute elapsed-time thresholds in CI. |
| G7: Tooling and distribution | Verify check, fmt idempotence, standalone build/run, rukbat test, and LSP completion/hover/signature/type display/references. Package CLI and VSIX from the same revision; verify Run File and debugging in actual VS Code with the bundled compiler, not a substitute source-tree compiler. |

Run execution tests on both Windows PowerShell 5.1 and PowerShell 7.
Reuse fixtures from FileEncodingTests, ExpandedEncodingTests, ErrorTests, and DebugTests.
A parent process successfully opening a file exclusively after forced termination does
not prove that Dispose ran.
Separate measurements for huge lines from those for many lines, and record initial
helper loading and GC variability separately.

The selected direction is a two-argument API, loop ownership, an internal iteration
contract, and splitting after decoding.
The Windows incremental codec candidate has undergone partial experiments, but G0
and G1 remain incomplete.
Implementation order is G0, then G1/G2, then type checking and lowering.
Keep G0 separate as a bug fix for existing APIs.
Do not narrow encoding support to declare G1 complete or finalize the public API
before validating its prerequisites.

## 10. Local implementation and verification

The implementation adds fs.lines and the fs.Lines opaque type, independent native cursors,
shared BOM handling, per-traversal incremental decoders, BoundIteration, cleanup-aware
loop lowering, and deferred return output. Editor completion and type documentation use
the canonical signature and bound type identity. No public iterator or generator syntax
is introduced. The filter-errors example uses sequential input traversal.

The shared GB18030 validator rejects the unmapped four-byte ranges before Windows
conversion. FileLineRuntimeTests checks all 1,587,600 four-byte shapes: 1,087,996 accepted
units and 499,604 rejected units. It also checks short reads, Unicode boundaries, retained
suffixes, EOF, repeated Close, and resource detachment before an injected disposal failure.
The native tests compile the same helper sources with a test-only nullable-context header;
generated PowerShell embeds the original C# 5 source.

AllAcceptedNativeCodecsPreserveConcatenatedUnitsAndIndependentState enumerates locally
accepted native codecs. For each codec it concatenates accepted single-byte units and all
accepted pairs whose first byte cannot stand alone, compares whole-input decoding with
production incremental decoding at widths 1, 2, 3, 7, and 65,536, and checks independent
empty decoders. This tests composition and the implementation, while preserving the
limitation that finite fixtures do not prove behavior for every longer input or Windows version.

FileLinesTests executes generated normal and debug scripts on Windows PowerShell 5.1
and PowerShell 7. It covers supported encodings, BOMs, buffer boundaries, empty and long
lines, repeated/nested traversal, value copies, nullable/array returns, recursion, break,
continue, escaping Error, and rename/remove after cleanup. Close failure is injected into
generated test artifacts after real handle disposal. The tests verify replacement returns,
catch fallthrough, nested cleanup, void-expression effects, and combined primary/cleanup Errors.

The release gates above remain the validation checklist. In particular, UNC behavior,
broader Windows-version coverage, process-memory measurements, and deployment on a
machine without an installed .NET runtime must not be inferred from local execution.

Local validation passed 1,092 Unit tests, 206 reader/LSP tests, and 121 encoding/file/process
regression tests (these sets overlap; do not add their counts). Generated-script execution
tests ran on PowerShell 5.1 and 7. A debug-adapter test inspected fs.Lines as opaque while
paused and verified completion after break. The actual VS Code acceptance suite used
the bundled self-contained compiler and passed fs.lines completion, hover, signature help,
and Run File traversal, alongside existing editor and debug checks. No extension was
installed into the user's normal VS Code profile.
The extension's 28 Node tests also passed. The portable compiler passed check, idempotent
fmt, build, doctor, and rukbat test with both host choices on a file-line fixture.
