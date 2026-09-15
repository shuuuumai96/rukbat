# Rukbat Philosophy

## Purpose

Rukbat is a small, typed, procedural language for Windows system scripting.

It exists to make the kind of automation traditionally written in batch files or PowerShell easier to write, easier to read, easier to review, and safer to maintain.

Rukbat is not intended to replace PowerShell as a system-management platform.

Instead, Rukbat provides a modern authoring language and compiles to standalone Windows PowerShell scripts that can run without the Rukbat compiler or runtime.

The primary deployment target is Windows PowerShell 5.1.

This is intentional.

Many Windows environments, especially restricted enterprise and administrative environments, already provide Windows PowerShell 5.1 but do not allow additional runtimes such as Python, Node.js, or PowerShell 7 to be installed.

Rukbat should allow developers to use a modern language during development while producing artifacts that remain compatible with such environments.

---

## Core Idea

Rukbat separates the development experience from the execution environment.

During development, Rukbat should provide:

- a clear and modern syntax;
- static typing;
- type inference;
- predictable semantics;
- strong diagnostics;
- standardized formatting;
- editor support;
- static analysis.

At deployment time, Rukbat should produce:

- standalone `.ps1` files;
- no Rukbat runtime dependency;
- no requirement for PowerShell 7;
- no additional runtime installation where possible.

In short:

**Modern authoring, static checking, native Windows deployment.**

---

## Programming Model

Rukbat is primarily procedural.

Programs should be easy to understand by reading them from top to bottom.

The visible order of the source code should correspond closely to the order in which operations occur.

Rukbat prioritizes human comprehensibility over mathematical elegance, terseness, declarative composition, or clever abstraction.

A straightforward sequence of explicit operations is preferable to a shorter but more abstract expression.

For example, explicit loops and conditionals are generally preferred over deeply chained transformations or elaborate functional composition.

---

## Data-Oriented Design

Rukbat treats programs primarily as data and procedures that operate on that data.

Structs should primarily represent data.

Behavior should generally be expressed through functions and modules rather than large object hierarchies or highly stateful objects.

The preferred design order is:

**Data first. Procedures second. Abstractions last.**

Abstraction is useful only when it clearly reduces repetition or complexity.

Abstraction is not a goal by itself.

---

## One Obvious Way

Rukbat should minimize the number of equivalent ways to express the same operation.

Inspired by Go, code written by different developers should naturally look similar.

Language features should not be added merely to provide stylistic alternatives.

Formatting should be standardized rather than configurable.

A formatter should define the canonical representation of Rukbat source code.

Where possible, syntax and standard-library APIs should guide developers toward one conventional solution.

---

## Variable Declarations

Variable syntax should remain explicit and unambiguous.

```auv
name := "Taro"
age: int = 30

age = 31
```

The intended meanings are:

- `:=` declares a new variable and infers its type.
- `: Type =` declares a new variable with an explicit type.
- `=` assigns a new value to an existing variable.

`:=` must not silently reassign an existing variable in the same scope.

Declarations and assignments should remain semantically distinct.

---

## Static Checking

Rukbat should reject as many errors as reasonably possible before producing PowerShell.

This includes, where practical:

- syntax errors;
- undefined names;
- invalid redeclarations;
- incompatible assignments;
- invalid function arguments;
- type inconsistencies;
- unsupported target features.

Generated PowerShell should therefore be more strongly validated than typical ad-hoc hand-written PowerShell.

This does not mean Rukbat can guarantee successful execution.

Runtime failures such as missing files, insufficient permissions, unavailable networks, missing external programs, or invalid external state remain runtime concerns.

---

## PowerShell Relationship

PowerShell is the primary compilation target, not the language model Rukbat should imitate.

Rukbat should not expose PowerShell syntax or conventions unless doing so is necessary.

Rukbat should define operations according to their meaning, not according to the PowerShell command used to implement them.

For example, a file-copy operation means:

> Copy a file from one path to another.

It should not be defined as:

> Invoke `Copy-Item`.

The implementation may change while the Rukbat semantics remain stable.

Direct PowerShell access should remain available as an escape hatch for operations not covered by Rukbat.

PowerShell-specific functionality should not force the core language to grow unnecessarily.

---

## Windows Focus

Rukbat is intentionally focused on Windows system scripting.

Cross-platform support is not a primary design goal.

The compiler itself may run on multiple platforms if convenient, but language and standard-library design should not be weakened merely to preserve portability.

Windows-specific functionality is acceptable when it meaningfully improves Windows automation.

The target environment should remain explicit and predictable.

---

## Standard Library

The standard library should focus on common system-scripting tasks.

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
- operating-system information;
- Windows-specific administration where appropriate.

The standard library should prefer a small, coherent vocabulary.

Equivalent operations should not be duplicated under multiple names without a strong reason.

For example, if `remove` is the conventional verb, the library should not arbitrarily mix `remove`, `delete`, and `erase`.

---

## Scope

Rukbat is not intended to become a general-purpose application-development language.

Its primary domain is system scripting and automation.

Typical Rukbat programs may:

- manipulate files and directories;
- execute external programs;
- inspect or modify environment state;
- process CSV or JSON;
- perform HTTP requests;
- automate Windows administration;
- orchestrate command-line tools;
- perform repeatable operational tasks.

Rukbat is not primarily intended for:

- web application development;
- GUI application development;
- games;
- large business applications;
- complex domain modeling;
- scientific computing;
- highly abstract framework development.

When a task clearly belongs to those domains, another language may be more appropriate.

---

## Language Size

Rukbat should remain deliberately small.

A feature should not be added merely because another language provides it.

Before adding a language feature, ask:

1. Does it make Windows system scripting easier to read or write?
2. Does it reduce real complexity?
3. Does it preserve a clear and predictable programming model?
4. Can the same need be satisfied by the standard library?
5. Can PowerShell or another escape hatch handle the uncommon case instead?
6. Does it create another way to express something that already has a clear form?

If the benefit is primarily theoretical elegance, syntactic novelty, or completeness, the feature probably does not belong in Rukbat.

---

## Tooling

Tooling is part of the language experience.

The long-term toolchain should provide a small and predictable set of commands, such as:

```text
auv run
auv build
auv check
auv fmt
auv test
auv lsp
```

Editor integration should eventually support:

- syntax highlighting;
- diagnostics;
- hover information;
- completion;
- go to definition;
- find references;
- rename;
- signature help;
- inferred-type hints.

The language should remain simple enough that these tools can provide reliable answers.

---

## Deployment Model

Rukbat should distinguish clearly between authoring and deployment.

Typical workflow:

```text
Rukbat source
    ↓
parse
    ↓
name resolution
    ↓
type checking
    ↓
static analysis
    ↓
PowerShell generation
    ↓
standalone Windows PowerShell 5.1 script
```

The generated script should not normally require Rukbat to be installed on the target machine.

This property is central to Rukbat's usefulness in restricted Windows environments.

---

## Non-Goals

Rukbat should not:

- attempt to reproduce every PowerShell feature;
- become a wrapper for every .NET API;
- add abstractions solely for theoretical completeness;
- provide multiple equivalent syntax styles;
- optimize for code golf or minimal character count;
- encourage clever or highly implicit code;
- require a custom runtime on deployment machines;
- become a general-purpose language without a clear system-scripting reason.

---

## Design Principle

The highest-level design question is:

**Does this make Windows system scripting simpler, clearer, safer, and more predictable?**

If the answer is no, it probably does not belong in Rukbat.

Rukbat should favor boring, explicit, unsurprising code.

The goal is not to let expert programmers demonstrate language cleverness.

The goal is for two competent developers solving the same system-scripting problem to produce code that looks broadly similar and is easy for a third person to understand.
