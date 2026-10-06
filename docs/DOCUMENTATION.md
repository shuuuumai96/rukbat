# Documentation from source comments

Keep usage instructions and API explanations beside the code they describe.
`rukbat doc` combines them with compiler-checked signatures in one offline HTML file.
It never executes source code or examples and does not require PowerShell.

```powershell
rukbat doc examples/documentation/main.rukbat
rukbat doc examples/documentation/main.rukbat --output reference.html
rukbat doc examples/documentation/main.rukbat --check
```

The default output is `main.html` beside the entry source. `--output` accepts an
`.html` path; its parent directory must exist. `--check` validates documentation
without writing output. These options cannot be combined. The command checks the
entrypoint and its reachable user imports, including shared dependencies once.
It does not scan unrelated files or embed standard-library API pages.

## Writing comments

Use a single contiguous `//!` block before the first code token for module usage:

```rukbat
//! Lists CSV files directly inside the directory supplied as the first argument.
//!
//! Relative paths use the caller's working directory. No files are modified.
//! Success exits 0; invalid arguments or listing failures exit 1.
import fs
```

Use contiguous `///` lines immediately before a top-level function, struct,
variable, struct field, or struct method. A blank line or ordinary comment breaks
the attachment. Documentation markers must start their own line; indentation is
allowed. Local variables, imports, and executable statements cannot receive `///`.

```rukbat
/// Adds the offset and returns the result.
/// Integer overflow raises Error.
fn add(value: int, offset: int): int {
    return value + offset
}
```

Functions, structs, fields, and methods appear even without comments. Top-level
variables appear only when documented and are labelled module-local: documenting
them does not make them accessible through imports. Write constraints, errors,
side effects, and examples as ordinary prose; parameter and return types come
from the compiler and need no duplicate tags.

Documented `const` declarations retain their modifier and are labelled module-local
constants. They follow the same attachment rules as ordinary variables.

All these markers remain comments for `build`, `run`, `check`, and `test`.
`////` is an ordinary comment. Markers inside strings are literal text.
`fmt` preserves documentation indentation and trailing spaces, including Markdown
hard line breaks, while using canonical source indentation and LF line endings.

## Markdown, links, and examples

Comment bodies use CommonMark. One optional space after the marker is removed;
further indentation is preserved. Headings, paragraphs, lists, emphasis, ordinary
links, and fenced code are supported. Raw HTML is displayed as text. Images are
reduced to their captions and never loaded. Links allow fragments, `https`, `http`,
and `mailto`; other destinations are disabled. HTML includes its own stylesheet,
with no scripts or external resources.

Use ``[`add`]`` to link a declaration in the same module, ``[`files.list_csv`]``
for an imported user module's declaration, and ``[`Listing.paths`]`` for a struct
member. Names are case-sensitive. Ordinary CommonMark reference definitions take
precedence over this shorthand. Standard-library names are not shorthand targets;
use an ordinary link to the standard-library guide. User-defined types in generated
signatures automatically link to their declarations.

An ordinary `rukbat` fence is displayed without checking it. Add `check` to opt in:

~~~~text
/// ```rukbat check
/// import "./math.rukbat" as arithmetic
/// result := arithmetic.add(2, 3)
/// print(result)
/// ```
~~~~

Each checked fence is an independent source file. Explicitly import the module
being demonstrated and any other dependencies; declarations surrounding the
comment are not implicitly available. Relative imports resolve beside the source
containing the comment, not beside the entrypoint or output file. The normal
compiler checks syntax, names, imports, and types. It never executes the example,
checks expected output, or proves runtime success. Failures point back to the
actual comment line; failures inside imports identify the imported source.

## Reading and maintaining the result

The entry module's usage comes first, followed by a name-sorted index and modules.
Declarations follow source order. Source locations and stable anchors use paths
relative to the entrypoint. Output has no generation timestamp or machine-specific
absolute source paths. Unchanged source produces identical UTF-8 HTML without a BOM.

Source errors, misplaced documentation (`RUK6000`), unresolved shorthand links
(`RUK6001`), and checked-example errors prevent output replacement. Successful
generation replaces an existing HTML file only after validation and staging.
Exit codes are `0` for success, `1` for source/documentation failures, and `2` for
invalid arguments or I/O failures. CI can run `rukbat doc main.rukbat --check`.

The language server uses the same attached comments for full hover descriptions
and first-paragraph completion summaries, including unsaved imported sources.
It reports misplaced comments and unresolved links as warnings; it suppresses
link validation while ordinary compiler errors prevent reliable resolution.
Checked examples are validated by `rukbat doc` and during reference/rename requests;
ordinary editor diagnostics do not check them.
Parsed declaration comments can still be hovered when a broken import prevents
binding. References require successful binding.

In VS Code, **Rukbat: Generate Documentation** saves files and runs the command
using the same compiler as other editor actions. `doc` and `module_doc` snippets
insert comment starters. See the runnable [documentation example](../examples/documentation/main.rukbat).
