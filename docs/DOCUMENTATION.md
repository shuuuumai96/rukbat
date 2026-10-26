# Documentation from source comments

`rukbat doc` combines source comments and checked signatures into one offline HTML
file. It never executes source/examples and does not require PowerShell.

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
//! File-listing utilities. Paths use the caller's working directory.
import fs
```

Use contiguous `///` lines immediately before a top-level function, struct,
variable, struct field, or method inside an `impl`. An `impl` block itself is not
a documentation target; document the data type before `struct` and each operation
before its `fn`. A blank line or ordinary comment breaks
the attachment. Documentation markers must start their own line; indentation is
allowed. Local variables, imports, and executable statements cannot receive `///`.

```rukbat
/// Adds the offset and returns the result.
/// Integer overflow raises Error.
fn add(value: int, offset: int): int {
    return value + offset
}
```

Functions, structs, fields, and methods appear even without comments. Module
variables/constants appear only when documented and remain module-local; constants
retain their modifier. Describe constraints, errors, effects, and usage. Types come
from checked signatures and need no duplicate tags.

Markers remain comments during compilation. `////` is ordinary; markers in strings
are literal. Formatting preserves comment-body indentation and trailing spaces
(including Markdown breaks), while applying canonical source indentation and LF.

## Markdown, links, and examples

Bodies use CommonMark: one optional space after the marker is removed; further
indentation stays. Raw HTML is text, images become captions, and links allow only
fragments, `https`, `http`, and `mailto`. Output embeds its stylesheet and loads no
scripts or external resources.

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
