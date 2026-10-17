# Rukbat Philosophy

## Purpose

Rukbat is a small, typed, procedural language for Windows system scripting.
It makes automation easier to write, read, review, and maintain, and compiles to
standalone Windows PowerShell 5.1 scripts. Target machines need no Rukbat installation
or PowerShell 7. This supports Windows environments where additional runtimes cannot
be installed; it does not bypass their security policies.

## Programming model

Prefer explicit control flow that can be read from top to bottom. Model programs
as data and procedures: structs hold data, functions perform operations, and
abstractions earn their place by reducing repeated work or complexity.

Choose one conventional syntax and API for each operation. Avoid stylistic
alternatives, hidden control flow, inheritance-heavy designs, and cleverness that
makes ordinary scripts harder to follow. Formatting is canonical, not configurable.

Declarations and assignments are distinct:

```rukbat
name := "Taro"     // New variable, inferred type.
age: int = 30      // New variable, explicit type.
age = 31           // Assignment to an existing variable.
```

`:=` must never silently reassign a name in the same scope. Concrete semantics
belong in the [language specification](language.md).

## Static checking and errors

Reject meaningful mistakes before generation: invalid syntax, undefined names,
redeclarations, incompatible assignments or arguments, and unsupported target
features. Add static guarantees when they prevent recurring errors without making
ordinary programs disproportionately complex.

Missing files, permissions, network failures, and invalid external state remain
runtime concerns. Use one small, consistent error path with explicit recovery;
avoid competing error-handling styles or elaborate exception hierarchies.

## PowerShell relationship

PowerShell is the compilation target and system-management platform. Rukbat defines
operations by their observable meaning, independent of the cmdlet used to implement
them. Incidental PowerShell session state must not define ordinary Rukbat behavior.

Direct PowerShell access is an explicit, localized escape hatch for uncommon tasks.
A repeated pattern in real scripts may justify a standard-library operation;
it does not automatically justify new language syntax.

## Scope and standard library

Focus on Windows automation: files, paths, processes, environment state, text,
CSV/JSON, HTTP, archives, hashing, and Windows administration. Use a small,
consistent vocabulary, such as one canonical `remove` operation rather than
synonyms. Confirmed APIs belong in [STDLIB.md](STDLIB.md).

Cross-platform support is not a primary goal. The compiler may run elsewhere,
but portability must not weaken Windows-specific behavior that helps users.
Rukbat is not aimed at web or GUI frameworks, games, scientific computing,
large business applications, or complex domain modeling. It should not reproduce
every PowerShell feature or wrap every .NET API.

## Language size and complexity budget

A feature creates lasting costs in learning, implementation, diagnostics,
documentation, formatting, editor support, tests, and interactions with other features.
Before adding one, ask:

1. Does it provide recurring value in Windows system scripting?
2. Does it make programs simpler, clearer, safer, and more predictable?
3. Can the standard library or PowerShell escape hatch satisfy the need?
4. Does it duplicate an existing way to express the operation?
5. Does its practical value justify its permanent complexity?

Novel syntax, theoretical completeness, code golf, or stronger typing for its own
sake are insufficient reasons. Prefer boring, explicit code that different authors
can write and maintain consistently.

## Tooling

Treat tooling as part of the language: checking, building, running, formatting,
testing, documentation, and editor integration should share predictable semantics.
Keep the language analyzable enough for reliable diagnostics, navigation, completion,
rename, and signature help. See [editor support](EDITOR.md) for available features.

## Deployment model

Validate source before generating a deterministic, inspectable, diagnosable `.ps1`.
The generated file is a deployment artifact; `.rukbat` remains the development source
of truth. Generated PowerShell may contain machinery needed to preserve semantics
and need not resemble handwritten scripts.

Standalone means independent of Rukbat. Programs may still explicitly require data,
external executables, network services, or operating-system facilities. Keep those
requirements distinct from authoring tools and document them in the
[distribution guide](DISTRIBUTION.md). Compiler stages belong in [ARCHITECTURE.md](ARCHITECTURE.md).
