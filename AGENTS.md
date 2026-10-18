# AGENTS.md

## Purpose

This repository contains the Rukbat programming language and compiler.

Before making implementation or design changes, read and follow:

1. `docs/PHILOSOPHY.md`
2. `docs/LANGUAGE.md`
3. `docs/STDLIB.md`
4. `docs/ARCHITECTURE.md`

`docs/PHILOSOPHY.md` is the highest-level source of truth for product and language-design decisions.

Do not introduce behavior that conflicts with it unless explicitly instructed.

When writing Rukbat source, examples, or documentation snippets, follow
`docs/STYLE.md`. It defines source conventions, not additional language semantics
or the implementation language's coding style.

### Source-of-Truth Hierarchy

- Concrete language semantics → `docs/LANGUAGE.md`
- Standard APIs → `docs/STDLIB.md`
- Compiler stages and responsibilities → `docs/ARCHITECTURE.md`
- Unresolved design choices and principles → `docs/PHILOSOPHY.md`
- `AGENTS.md` does not define new semantics; it provides contributor discipline and process guidance.

---

## Core Rule

Rukbat is a small, typed, procedural language for Windows system scripting.
Its primary deployment target is standalone Windows PowerShell 5.1 scripts.
Do not optimize for language cleverness, theoretical completeness, or feature count.
See `docs/PHILOSOPHY.md` for the complete design philosophy.

---

## Design Decision Order

When implementing a feature or resolving ambiguity, prefer solutions in this order:

1. Keep the language simple.
2. Preserve existing Rukbat semantics.
3. Prefer explicit procedural behavior.
4. Prefer one canonical syntax or API.
5. Solve the problem in the standard library before extending the language.
6. Use PowerShell as an escape hatch rather than expanding Rukbat unnecessarily.
7. Preserve Windows PowerShell 5.1 compatibility where the feature is intended to be deployable.

If a proposed change conflicts with these principles, stop and explain the trade-off instead of silently implementing it.

---

## Language Rules

Preserve the distinction between declaration and assignment.

```rukbat
name := "Taro"
age: int = 30
age = 31
```

- `:=` means new variable declaration with type inference.
- `: Type =` means new variable declaration with an explicit type.
- `=` means assignment to an existing variable.

Do not make `:=` silently reassign an existing variable in the same scope.

Prefer simple and explicit constructs over adding alternative syntax for existing behavior.

Do not add multiple equivalent forms merely for convenience.

---

## Programming Style

Rukbat is primarily procedural and data-oriented.
The source should be understandable by reading it from top to bottom.
Avoid inheritance-heavy designs, abstraction for its own sake, complex fluent APIs, excessive method chaining, functional-style alternatives that duplicate existing procedural constructs, or hidden control flow.
See `docs/PHILOSOPHY.md` for details.

---

## Compiler Architecture

Keep compiler stages conceptually separated.

Preferred flow:

```text
Source
↓
Lexer
↓
Parser
↓
Syntax Tree / AST
↓
Name Resolution / Binding
↓
Type Checking
↓
PowerShell Generation
```

Do not duplicate semantic logic across compiler stages when it can be centralized.

In particular:

- the parser should parse syntax, not perform broad semantic validation;
- name resolution should own symbol lookup and scope rules;
- type checking should own type compatibility rules;
- PowerShell generation should consume already validated semantic structures where practical.

Avoid architecture inspired by large compilers unless the complexity is actually required.

---

## PowerShell Target

Generated PowerShell is an implementation target, not Rukbat's language model.
Do not copy PowerShell syntax or semantics into Rukbat unless necessary.
Operations are defined by their observable behavior, not by the PowerShell cmdlet used to implement them.
Generated scripts should remain independent of the Rukbat compiler or runtime, and must remain compatible with Windows PowerShell 5.1 unless explicitly documented otherwise.
Do not use PowerShell 7-only syntax or APIs in generated code without an explicit design decision.
See `docs/PHILOSOPHY.md` and `docs/ARCHITECTURE.md`.

---

## Standard Library

The standard library covers common Windows system-scripting tasks with a small and consistent API.
Prefer a consistent vocabulary; do not introduce multiple names for the same conceptual action without a strong reason (e.g. prefer one canonical verb such as `remove` rather than mixing `remove`, `delete`, and `erase`).
See `docs/STDLIB.md` for confirmed APIs. Do not invent or assume APIs that are not defined there.

---

## Tooling

Treat developer tooling as part of the language.

When relevant, changes should consider compatibility with:

- `rukbat run`
- `rukbat build`
- `rukbat check`
- `rukbat fmt`
- `rukbat test`
- `rukbat lsp`

Formatting should be canonical rather than user-configurable unless explicitly decided otherwise.

Language features should remain analyzable enough to support reliable diagnostics and editor tooling.

---

## Testing Requirements

When changing parser, binder, type checker, or code generation behavior:

- add or update focused tests;
- include valid and invalid cases where appropriate;
- test generated PowerShell when output semantics change;
- verify Windows PowerShell 5.1 compatibility for features intended to support it;
- avoid weakening existing diagnostics without justification.

Bug fixes should include a regression test whenever practical.

---

## Scope Control

Before adding a new language feature, ask:

1. Is this needed for Windows system scripting?
2. Does it materially improve readability, safety, or predictability?
3. Could it be implemented as a standard-library feature instead?
4. Does it create a second way to express something that already exists?
5. Does it complicate static analysis or tooling disproportionately?
6. Does it compromise PowerShell 5.1 deployment unnecessarily?

If the answer suggests the feature is outside Rukbat's scope, do not add it by default.

---

## Non-Goals

Do not evolve Rukbat toward general-purpose application development, web frameworks, GUI frameworks, complex class hierarchies, advanced metaprogramming, macro systems, highly generic type-level programming, functional-programming feature sets, or custom runtime requirements on deployment machines.
See `docs/PHILOSOPHY.md` for the complete discussion of non-goals.

---

## Working With Existing Code

Before making broad structural changes:

- inspect the current implementation;
- preserve established behavior unless the task requires changing it;
- prefer the smallest coherent change;
- avoid unrelated refactoring;
- do not rename public syntax, APIs, or files without a clear reason;
- do not rewrite working subsystems merely to match a preferred architecture.

When a requested implementation appears to conflict with `docs/PHILOSOPHY.md`, explain the conflict before choosing a different design.

---

## Definition of Done

A change is not complete merely because it compiles.

For relevant changes, verify:

- parsing;
- static semantics;
- diagnostics;
- generated PowerShell;
- regression tests;
- formatting;
- Windows PowerShell 5.1 compatibility.

The implementation should satisfy the requested behavior without unnecessarily expanding the language surface.

---

## Guiding Principle

When uncertain, choose the solution that makes Rukbat code more boring, explicit, predictable, and easy to read.

Rukbat should make Windows scripting easier to understand, not merely shorter to type.
