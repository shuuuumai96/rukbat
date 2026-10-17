# Dictionary design

The [language specification](language.md#dictionaries) owns syntax and semantics;
[STDLIB.md](STDLIB.md#dictionaries-and-json-objects) owns JSON conversion, and
[ARCHITECTURE.md](ARCHITECTURE.md#dictionaries) describes storage and generation.

## Why a language type

CSV joins, ID lookup, counting, and in-memory indexes need dynamic keys with
statically known values. Dictionaries store ordinary Rukbat values, including
structs, arrays, and opaque values, without converting them to JSON. Fixed records
continue to use structs; unknown JSON documents use `json.Value`.

`dict[K, V]` retains key and value types through variables, parameters, and returns.
Reusing literals, indexing, and value-copy semantics avoids type-specific helper
APIs. This built-in container does not introduce user-defined generics.

## Design choices

| Choice | Reason |
|---|---|
| String and integer keys | Cover names and numeric IDs without implicit conversions or new equality/hash contracts. |
| One dictionary type | Avoid hash/tree variants, custom comparers, capacity settings, and user-defined hashing. |
| Strict missing-key reads | Distinguish absence from a stored null; optional lookup uses `has` then indexing. |
| Index assignment inserts or replaces | Reuse ordinary writable-path syntax; `:=` remains declaration-only. |
| Ordinal string equality | Keep key identity independent of culture, path rules, and PowerShell hashtables. |
| Insertion order and snapshot `keys()` | Make iteration predictable across PowerShell versions and during mutation. |
| Deep value copies | Preserve existing assignment, argument, and return semantics without shared mutable aliases. |
| No implicit intermediate insertion | Nested writes remain explicit; intermediate keys must already exist. |
| String-key JSON conversion only | JSON object names are strings; integer keys must be represented explicitly. |
| No direct PowerShell dictionary boundary | Avoid case-insensitive keys, mixed values, implicit conversion, and stream enumeration. |

JSON conversion canonicalizes keys in ordinal order, so it preserves supported
content but not insertion order. Immutable opaque values may share backing storage;
that does not make them JSON-compatible.

## Implementation constraints

Key/type validation belongs to the checker, not the parser. Recursive type-cycle,
const, mutation, and external-boundary checks must traverse dictionary values.
Receiver/key expressions evaluate once; lookup and mutation must not clone the
whole receiver. Clone only at ordinary value boundaries.

Expected costs, excluding copying an individual composite value:

- Average O(1): lookup, `has`, insertion, replacement, removal; O(1): `length`.
- O(n) plus recursive value costs: `keys()` and whole-dictionary copies.
- O(n log n): canonical JSON key ordering.

Function arguments still copy dictionaries. Optimizations must preserve observable
value semantics. Dictionary operations alone require neither Add-Type nor an extra
installed runtime; contained values and imported modules retain their own requirements.

Runnable usage: [CSV category counter](../examples/csv-count/README.md).
