# Typed dictionaries — design record

Status: implemented. This document records design rationale and acceptance criteria.
Confirmed semantics are owned by LANGUAGE.md, JSON APIs by STDLIB.md, and runtime details
by ARCHITECTURE.md.

## Purpose and scope

Rukbat needs a typed, dynamically keyed container for CSV joins, ID lookup, counting and
in-memory indexing. A dictionary stores ordinary Rukbat values directly, including structs,
arrays and opaque values such as timestamps. It must not require JSON conversion for ordinary
reads or updates. Fixed records continue to use structs.

Use one dictionary type with string or integer keys. There is no choice of hash/tree variants,
custom comparer, capacity setting, user-defined hashing or separate set type in this implementation.
The public behavior must remain independent of PowerShell hashtable conventions.

## Canonical syntax

```rukbat
counts: dict[string, int] = {}
counts["error"] = 1
counts["error"] = counts["error"] + 1

if counts.has("error") {
    print(counts["error"])
}

for key in counts.keys() {
    print(key)
    print(counts[key])
}

counts.remove("error")
print(counts.length)
```

A typed dictionary needs to retain both K and V through variables, parameters and returns. This
requires a container type in the language even if helper operations live in the runtime. Reusing
indexing and existing value semantics keeps the new surface smaller than type-specific APIs.

No import is needed. `dict[K, V]` is a dedicated built-in type constructor, like the existing
array and nullable constructors. It does not introduce user-defined generic functions or types.
`dict` becomes a reserved built-in type name; it cannot name a user struct. It does not become
an additional standard module or reserve an otherwise legal variable/import alias.

Use one initializer form, `{key: value, ...}`, including `{}` for empty dictionaries. Keys are
expressions, not struct field declarations. No implicit string key conversion or dot-key access
is provided. `d["length"]` is a stored item; `d.length` is the item count.

```rukbat
ports := {"http": 80, "https": 443}  // dict[string, int]
labels: dict[int, string] = {200: "OK", 404: "Not found"}
name := "daily"
jobs := {name: "backup"}            // key is the value of name
```

Struct literals retain their explicit type name, for example `User { name: "Taro" }`.
Dictionary literals are parsed in expression positions; statement blocks retain their current
meaning. Trailing commas and multiline formatting follow existing collection literal rules.

An empty literal requires a contextual dictionary type from a declaration, parameter, return,
field or enclosing collection. `d := {}` is an error. For a nonempty literal without a contextual
type, infer K and V from the first pair and require subsequent pairs to match, following the
existing array inference rules. No implicit int/float mixing or inferred widening to nullable
is added. With a contextual type, each key/value is checked against that type.

## Types

K is exactly non-null `string` or `int`. Strings cover names and textual identifiers; integers
cover numeric IDs without repeated conversion to strings. Other key types are initially rejected:
keys must be usable without nullable handling, while float, composite and opaque keys would
require additional equality/hash contracts. Boolean keys are unnecessary for the initial target
workflows and remain outside the supported key types.

V may be any storable Rukbat type: primitives, nullable values, structs, arrays, other dictionaries,
and opaque standard-library values. `void` is not storable. This does not relax the existing
prohibition on recursive struct type cycles, including cycles through dictionary values.

Both type parameters are invariant. `dict[string, int]` does not implicitly convert to
`dict[string, float]` or `dict[string, int?]`. A literal can initialize a contextual nullable-value
dictionary using the existing individual-value conversions.

```rukbat
values: dict[string, int?] = {"limit": null}
optional: dict[string, int]? = null
rows: dict[int, User][] = []
groups: dict[string, string[]] = {}
```

The existing nullable narrowing rules apply to a nullable dictionary variable. Checking
`d.has(key)` does not narrow a nullable stored value. Read it into a variable and test that
variable against null, exactly as for an array element or struct field.

## Operations

| Expression | Result and behavior |
|---|---|
| `d[key]` | Reads a V. An absent key raises ordinary Error, including when V is nullable. |
| `d[key] = value` | Inserts an absent key or replaces an existing value. Requires a writable receiver. |
| `d.has(key)` | Returns bool. A present key whose value is null still returns true. |
| `d.remove(key)` | Removes the key and value in place; returns void. An absent key succeeds. |
| `d.keys()` | Returns an independent K[] in insertion order. |
| `d.length` | Returns the number of entries as int; read-only. |

Each method has exactly the positional arguments shown. There is no second get/set spelling,
automatic default value, nullable-on-missing read, implicit insertion on read, get-or-add callback,
values/items API, or dictionary-specific loop syntax in the first implementation.

As with array element assignment, `=` updates a storage location. The declaration/assignment
rule is preserved: `:=` declares variables and is not permitted on an indexed target.

A counter uses explicit initialization:

```rukbat
counts: dict[string, int] = {}
for name in names {
    if counts.has(name) {
        counts[name] = counts[name] + 1
    } else {
        counts[name] = 1
    }
}
```

`has` followed by indexing is the canonical optional lookup. It is not an atomic compound
operation. If intervening code removes the key, the subsequent read raises Error.

## Key identity and ordering

String keys use ordinal, case-sensitive equality, independent of culture. No Unicode
normalization, case folding, whitespace trimming or path normalization occurs. Empty strings
are valid keys. `"A"` and `"a"` are different keys. Integer keys use exact signed 64-bit equality;
negative integers and the full existing int range are supported.

A dictionary tracks insertion order explicitly. Replacing a value preserves its position.
Removing and then inserting the key places it at the end. This behavior must agree on Windows
PowerShell 5.1 and PowerShell 7 and must not depend on .NET Dictionary enumeration order.

A literal cannot contain duplicate keys. Repeated string or signed-integer literal keys are
compile-time errors. Duplicates involving other expressions are checked during construction and
raise Error. Entries evaluate in written order, key then value; insertion/duplicate checking
follows evaluation of that pair. Earlier expression side effects are not rolled back.
Ordinary indexed assignment intentionally uses replacement semantics instead.

`keys()` takes a snapshot when called. The existing array for-loop then iterates that snapshot.
Changing the original dictionary does not change the keys being iterated. Reading `d[key]`
inside that loop still reads the current dictionary and can fail if the key was removed. To
iterate stable key/value contents while changing the original, first make an ordinary copy.
Use `array.sort(d.keys())` when key-sorted traversal is wanted.

## Value semantics, const and nested updates

Dictionaries follow the existing deep value-copy rules for assignment, function arguments,
returns, insertion into another container, and extracting a value into a variable. Immutable
opaque values can share their immutable backing storage, as they already do elsewhere.

```rukbat
struct User { name: string; active: bool }
users: dict[int, User] = {10: User { name: "Taro", active: true }}

user := users[10]
user.active = false          // users[10] is unchanged
users[10].active = false     // updates the stored User

copy := users
copy[10].name = "Hanako"     // users[10].name remains Taro
```

Writes and mutating methods require a variable-rooted writable path, extending the existing
field/array-index paths with dictionary indexes. Const roots reject insertion, replacement,
removal and nested mutations using the existing const diagnostic. Mutating a function-result
temporary is rejected. A function that receives a dictionary gets a copy; callers retain a
returned dictionary when they want that function's changes.

Nested writes must resolve every intermediate key. Only a final dictionary-index assignment
can insert a new entry. There is no automatic creation of intermediate structs/arrays/dictionaries.
For example, `groups["team"].append("Taro")` requires an existing array at "team". The program
initializes it with `groups["team"] = []` when necessary.

Index/key expressions evaluate exactly once in left-to-right order and nested writes update
the addressed storage location, as existing array updates do. Do not change existing evaluation
order or side-effect rules. Reading/has/keys/length and indexed updates must not internally clone
the entire receiver just to call a helper.

Dictionary equality and direct print/eprint are not defined, consistent with existing mutable
composites. Print individual values, iterate keys, or explicitly serialize supported content.

## JSON and PowerShell boundaries

Support existing JSON APIs for `dict[string, V]` when V is recursively JSON-compatible:

- `json.stringify` encodes a JSON object, sorting dictionary keys by ordinal order.
- `json.parse<dict[string, V]>` and `json.as<dict[string, V]>` require an object and strictly
  convert every property value to V. They insert keys in ordinal order, matching `json.keys`.
- Existing JSON update operations accept such dictionaries as replacement values.
- Missing dictionaries, JSON null, numeric conversion and duplicate JSON properties retain
  their existing strict rules. A nullable dictionary follows the existing nullable rules.

JSON conversion deliberately canonicalizes object order. Dictionary content survives a
supported round trip, but original insertion order is not retained. JSON conversions containing
integer-keyed dictionaries are rejected at compile time; no implicit conversion of integer keys
to property strings is performed. Explicit entry structs in an array can represent those keys.

Values such as `datetime.Timestamp` and `fs.Info` remain legal dictionary contents but do not
become JSON-compatible. Existing recursive checks must visit dictionary value types.

Direct PowerShell arguments/results containing dictionaries are rejected recursively in the
first version. This avoids importing PowerShell's case-insensitive keys, mixed values, implicit
conversions and stream enumeration into the language. Existing explicit JSON/text or arrays of
ordinary entry structs remain available for external interchange.

## Implementation responsibilities

- Parser: dedicated dictionary type/literal syntax; retain key/value source spans and formatting
  facts. Extend type-argument lookahead so existing calls such as json.parse<dict[string, int>>
  remain unambiguous. Do not implement semantic key/type validation in the parser.
- Binding/type checking: bind K/V, validate key restrictions and inference, check known duplicate
  literal keys, classify dictionary indexes/methods/length, and extend const and mutating-method
  analysis. Include dictionary values in recursive-type and external-boundary traversal.
- Emission/runtime: consume validated K/V and writable-path information. Use an internal tagged
  dictionary wrapper containing a typed BCL dictionary (string keys with StringComparer.Ordinal
  or Int64 keys) and an explicit linked insertion-order list. Each entry links its value and order
  node so removal can preserve the order of other entries without scanning all keys.
- Clone support: recognize the private dictionary wrapper before ordinary struct storage and
  recursively clone its entries/order. Do not treat it as the immutable cli.Result backing store.
  Stored user keys must never become wrapper fields or PowerShell property names.
- Generated code: use PowerShell 5.1-compatible framework collections and helpers. Dictionary
  operations alone introduce no Add-Type compilation or additional installed runtime. Imports
  and value operations can still require their own existing helpers.
- JSON runtime/schema: add a dictionary value schema and convert keys/values according to the
  JSON contract above, reusing existing strict numeric and opaque-value checks.

Expected costs, excluding copying a stored/read composite value: average O(1) lookup, has,
insertion, replacement and removal; O(1) length; O(n) keys and whole-dictionary copies, plus
recursive value-copy costs. JSON's canonical key ordering adds O(n log n) sorting. Ordinary
function argument copies can be expensive for large dictionaries; the design does not hide
shared mutable references to avoid that cost. A later internal optimization must preserve
observable value semantics.

## Tooling and documentation

Formatting uses `dict[K, V]` and `{key: value}` spacing, preserves entry order, and follows the
existing multiline literal layout. Hover and completion show concrete key/value types and the
three methods plus length. Signature help covers all three methods; diagnostics use contextual
key/value types for literals.
Rename/references follow type names nested in K/V and expressions in literal entries; string
keys are data and are not renamed as symbols.

Debugger variables display dictionary type, length, and entries in insertion order, without
exposing wrapper implementation fields or executing user code. Watch paths support literal
string/integer dictionary indexes, including escaped strings and negative integers, and retain
array index rules when the receiver is an array.

Implementation updates LANGUAGE.md for syntax, values, control-flow interactions and boundaries;
STDLIB.md for JSON/array interaction; ARCHITECTURE.md for bound types and generation; and the
VS Code grammar, snippets and bundled distribution. Those confirmed specifications take precedence
over this design record.

## Acceptance criteria

1. Typed and inferred literals; contextual empty/null values; invalid key/value types and
   duplicate literal/dynamic keys; nested/qualified types; unchanged struct and block parsing.
2. Missing versus present-null values; overwrite/remove/reinsert ordering; empty/negative/extreme
   keys; "length"/"keys" as ordinary stored keys; ordinal Unicode/culture-independent equality.
3. Deep copies across assignments, parameters, returns and nested containers; stored struct/array
   mutations; const and temporary receiver rejection; each key expression evaluated once.
4. Snapshot key iteration under insertion/removal, and explicit copies for stable entry reads.
5. Strict JSON round trips, deterministic ordering, malformed/mismatched input and rejection of
   integer-keyed or opaque-valued dictionaries, including nested external boundaries.
6. Large counter/index construction performs ordinary mutations without cloning the whole map
   per operation; tests verify this mechanism without brittle wall-clock thresholds.
7. Formatter idempotence, diagnostics/source spans, completion/hover/signature help, rename and
   debugger inspection, including incomplete dictionary literals/types in the editor.
8. Execute representative CSV lookup and counting programs on PowerShell 5.1 and 7; run generated
   scripts after removing source/compiler dependencies. Finally package and test the VSIX so
   the new syntax works in the user's normal editor workflow.
