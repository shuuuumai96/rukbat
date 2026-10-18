# Rukbat style guide

[日本語](ja/STYLE.md) · [Language](language.md) · [Standard library](STDLIB.md)

Use this guide when writing Rukbat programs, examples and documentation snippets.
It follows the [language philosophy](PHILOSOPHY.md): make scripts explicit,
predictable and easy to read. Language semantics belong in `language.md`;
this guide defines source conventions, not additional syntax restrictions.

## Use consistent names

| Name | Convention | Example |
|---|---|---|
| Variables, parameters and fields | `snake_case` | `output_path` |
| Functions and methods | `snake_case` | `verify_release` |
| User-defined types | `PascalCase` | `CheckResult` |
| Module aliases and source filenames | `snake_case` | `release_check.rukbat` |
| `const` bindings, in any scope | `snake_case` | `max_retries` |

Use names that describe the value or operation. Include units where they matter,
such as `timeout_ms` and `max_bytes`. Prefer verbs for operations and names such
as `is_valid` or `has_errors` for booleans. Keep names consistent with the standard
library, such as `remove` for removal.

`const` uses the same casing as other variables. It prevents writes through a
binding and can hold a runtime value; uppercase does not distinguish a separate
kind of constant. Use `const` when a binding is intended to remain unchanged.

```rukbat
const max_retries := 3
const arguments := args()
retry_count := 0
retry_count = retry_count + 1
```

Use `:=` when the initializer gives a clear type. Use `: Type =` when an explicit
type is needed or helps explain an interface, including empty arrays and nullable
initialization. Use `=` to update an existing binding.

## Let the formatter define the layout

Run `rukbat fmt` on source files before submitting changes. Its output is the
canonical layout; do not maintain a competing manual style.

- Use four spaces per indentation level and one statement per line.
- Put opening braces on the declaration or control-flow line, and `else`/`catch`
  on the same line as the preceding closing brace.
- Use the formatter's spacing around operators, commas and colons, and its blank
  lines between imports, functions and control-flow blocks.
- Save UTF-8 without a BOM, with LF line endings and a final newline.

The [formatting specification](language.md#formatting) defines the exact rules.
There is no configurable indentation or line-width limit. When an expression is
hard to read, give an intermediate value a useful name or simplify the operation.
Indentation is a source convention; braces define blocks.

## Give policy values names

Use existing standard constants, such as `http.status_200_ok`, `encoding.utf8`
and `hash.sha256`. Define a local `const` for application-specific timeouts,
retry limits, exit codes and other values whose meaning would otherwise be hidden.
Keep it in the smallest scope that needs it; share it only when the meaning is shared.

Ordinary counting values can stay inline: `count := 0`, `count + 1`, or
`items.length == 0`. Do not replace them with names such as `zero` or `one`.
Use unit-bearing names instead of explanatory numeric comments.

## Keep control flow and failures explicit

Place imports first. Keep entry-point operations together so their order can be
read from top to bottom. Helpers should perform one understandable operation;
extract them when they clarify intent or remove meaningful repetition.
Avoid import-time work in reusable helper modules.

Validate external input before acting on it. Throw `Error` from helpers when
the caller must handle a failure. Catch where recovery is possible or at the
command entry point, where an error can become a diagnostic and an exit code.
Do not silently swallow errors or report success after a failed operation.

```rukbat
const invalid_input := 2

fn require_path(value: string): void {
    if value == "" {
        throw Error {
            message: "Input path must not be empty."
        }
    }
}

try {
    require_path("")
} catch err {
    eprint(err.message)
    exit(invalid_input)
}
```

Use comments to explain a reason, constraint or external assumption. Use `///`
and `//!` for [source documentation](DOCUMENTATION.md). Prefer standard APIs;
keep PowerShell escape hatches localized to operations that need them.

## Apply the guide during review

Format sources, run `rukbat check` on each entry point, and run the relevant tests.
Review names and intent separately: the formatter does not rename identifiers.
Current tooling does not provide `rukbat fmt --check` or naming-style diagnostics.
These conventions do not make otherwise valid programs compiler errors.
