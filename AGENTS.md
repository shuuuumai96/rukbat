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

---

## Core Rule

Rukbat is a small, typed, procedural language for Windows system scripting.

Its primary deployment target is standalone Windows PowerShell 5.1 scripts.

The language should prioritize:

- readability;
- predictable behavior;
- static checking;
- explicit procedural control flow;
- data-oriented design;
- one obvious way to write code;
- minimal deployment requirements.

Do not optimize for language cleverness, theoretical completeness, or feature count.

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

Prefer:

- structs as data;
- small functions;
- explicit loops;
- explicit conditionals;
- direct execution order;
- modules and functions over deep object hierarchies.

Avoid introducing:

- inheritance-heavy designs;
- abstraction for its own sake;
- complex fluent APIs;
- excessive method chaining;
- functional-style alternatives that duplicate existing procedural constructs;
- hidden control flow.

The source should be understandable by reading it from top to bottom.

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
Lowering / IR where needed
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

Rukbat operations should be defined by their behavior.

Example:

`files.copy(source, destination)` means copying a file.

It must not be specified in terms of a particular PowerShell cmdlet such as `Copy-Item`.

Generated scripts should remain independent of the Rukbat compiler or runtime unless explicitly documented otherwise.

When practical, generated scripts should remain compatible with Windows PowerShell 5.1.

Do not use PowerShell 7-only syntax or APIs in generated code without an explicit design decision.

---

## Standard Library

The standard library should cover common Windows system-scripting tasks with a small and consistent API.

Likely areas include:

- files;
- paths;
- processes;
- environment variables;
- text;
- CSV;
- JSON;
- HTTP;
- archives;
- hashing;
- Windows-specific system operations.

Prefer a consistent vocabulary.

Do not introduce multiple names for the same conceptual action without a strong reason.

Example:

Prefer one canonical verb such as `remove` rather than mixing `remove`, `delete`, and `erase`.

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

When changing parser, binder, type checker, lowering, or code generation behavior:

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

Do not evolve Rukbat toward these areas without explicit instruction:

- general-purpose application development;
- web frameworks;
- GUI frameworks;
- complex class hierarchies;
- advanced metaprogramming;
- macro systems;
- highly generic type-level programming;
- functional-programming feature sets;
- custom runtime requirements on deployment machines.

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
