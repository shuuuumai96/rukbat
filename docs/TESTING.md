# Testing

Rukbat keeps compiler checks separate from generated-PowerShell execution.
Windows PowerShell 5.1 is the primary deployment target. PowerShell 7 is an
additional runtime and cross-version check; it is never required for deployed scripts.

## Categories

| Category | Purpose |
|---|---|
| `Unit` | Lexer, parser, module loading, resolver, type checker, emitter, formatter, and non-executing CLI behavior. |
| `Runtime` | Generated-script semantics exercised on a PowerShell host. Most tests use PowerShell 7; tests also tagged `PowerShell51` exercise the primary target directly. |
| `PowerShell51` | Direct Windows PowerShell 5.1 execution, including primary-target checks. |
| `CrossVersion` | Selected public behaviors verified with both PowerShell 5.1 and PowerShell 7. |

`Smoke=PowerShell51` is an execution-frequency trait, not a fifth test category.
It selects representative primary-target coverage for generated script startup,
standard-library paths, process execution, errors, CLI exit behavior, and standalone deployment.
Categories intentionally overlap: `Runtime` marks a generated-script execution
test, while `PowerShell51` and `CrossVersion` specify its target-host coverage.
Thus both host-specific categories are subsets of `Runtime`. `Smoke=PowerShell51`
is the PR-facing representative suite: every selected test case executes on
Windows PowerShell 5.1. It is independent of the category traits.

## Commands

`dotnet test` defaults to `Unit` through the VSTest `VSTestTestCaseFilter` MSBuild property.
The explicit equivalent is:

```powershell
dotnet test -p:RukbatTestCategory=Unit
```

Run a category explicitly:

```powershell
dotnet test -p:RukbatTestCategory=Runtime
dotnet test -p:RukbatTestCategory=PowerShell51
dotnet test -p:RukbatTestCategory=CrossVersion
```

Run the primary-target smoke selection or every category:

```powershell
dotnet test --filter "Smoke=PowerShell51"
dotnet test -p:RukbatTestCategory=All
```

The final command intentionally suppresses the default filter. Test parallelism is
explicitly disabled in this change; no process sharing or bounded parallelism is enabled.
