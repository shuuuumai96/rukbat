# Unattended Windows jobs

See the [operations guide](OPERATIONS.md) for release packaging, scheduling, monitoring, and recovery.

Rukbat's supported unattended path uses Windows 10 / Server 2016 or later, PowerShell 5.1 or 7 in FullLanguage mode, a dedicated working directory, and an account allowed to operate the required files. Journaled moves require one local fixed NTFS drive. Restricted language modes are rejected before user operations; no policy bypass is attempted.

## Recoverable file organization

Build the example once and deploy the generated `.ps1`:

```powershell
rukbat build examples/batch-organize/main.rukbat
powershell.exe -NoLogo -NoProfile -NonInteractive -File main.ps1 C:\Jobs\plan.csv
```

Set the scheduler's working directory explicitly: CSV fields resolve against it. Keep that directory, the plan, journal, and generated script under the job account's control. The example stores `plan.csv.rukbat-journal` beside the plan. Exit `0` means completion or verified replay; exit `2` means an error whose diagnostic goes to stderr. Retain stderr and the exit code in the caller's job history and alert on failure. The compiler does not install or configure a scheduler or monitoring service.

After a timeout, crash, or forced host termination, repeat the same command with the same plan and journal. The standard library verifies recorded file identities and SHA-256 hashes, restores an interrupted plan, then executes it. A completed plan is verified and not executed twice. Concurrent runs sharing a journal cannot enter the mutation phase together.

To restore an interrupted plan without executing it again:

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -File main.ps1 --recover C:\Jobs\plan.csv
```

Recovery is repeatable. If a file has been replaced or changed, both paths exist, the journal is corrupt, or permissions prevent verification, it stops instead of overwriting the competing data. Preserve the journal and both file locations for investigation. Do not delete the journal to force a retry. Recovery of an already committed job only verifies its completed state; it does not undo successful work. A new logical job needs a new plan/journal path.

The API locks file handles against concurrent writes/deletes and uses non-replacing handle renames. Ordinary failures trigger rollback; a failed rollback retains its journal. Created empty directories may remain. Other readers can see intermediate paths: this does not provide database-style atomic visibility across files. Cross-volume/network moves and links are rejected before moving sources.

## External commands

Use a positive `process.run` deadline. A Windows Job Object contains the root and ordinary descendants from creation. Timeout, capture failure, or termination of the PowerShell host terminates that job. The call waits for descendants as well as the root, so it is unsuitable for launching a persistent service. Options default to closed standard input. Explicit process.Input is transmitted concurrently with both output reads under the same deadline. Input and each output stream are limited to 64 MiB. A nonzero executable exit code is a returned result that the Rukbat program must check.

Containment is established before child code runs, using the Windows [job-list process attribute](https://learn.microsoft.com/en-us/windows/win32/api/processthreadsapi/nf-processthreadsapi-updateprocthreadattribute) and [kill-on-close job semantics](https://learn.microsoft.com/en-us/windows/win32/procthread/job-objects). Failure to establish it rejects launch. This does not undo external side effects or contain work delegated to an existing service or a remote host. For a modifying external command, retries need that command's own idempotency or recovery contract.

## Storage limits

`fs.write_text` flushes staged bytes before replacement; journals flush their plan
before moves and commit after completion. Flush completion does not guarantee
recovery from power loss or damaged storage. Keep independent data backups.
