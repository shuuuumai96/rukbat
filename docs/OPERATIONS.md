# Operating unattended Rukbat jobs

Use `tools/operations/Invoke-Job.ps1` for both manual and scheduled launches.
The table below separates tool guarantees from deployment responsibilities.

The tools require Windows 10 / Server 2016 or later, FullLanguage, and Windows PowerShell 5.1 or PowerShell 7. State storage must be local fixed NTFS. All configured paths must be absolute local paths without reparse points. Network access performed by the business program is separate from this local deployment profile. No execution-policy bypass, elevation, persistent agent or extra deployment runtime is installed.

## Responsibility and acceptance boundary

| Concern | Rukbat and its supplied tools | Deployment owner |
|---|---|---|
| Program behavior | Static checks, specified runtime semantics, source diagnostics | Correct business rules and input validation |
| Release | Build failure prevents packaging; version and SHA-256 manifest; verify and hold release files during execution | Trusted compiler, protected release/config ACLs, approved signing policy |
| Execution | Whole child-job deadline, process-tree containment, closed stdin, bounded captures | Task registration, account rights, working directory, machine availability |
| Concurrency | Exclusive `(state_directory, job_id)` lock shared by manual/scheduled launch | Use one canonical state directory and ID for the same job; do not bypass the wrapper |
| Results | Durable start/result records, attempt logs, nonzero wrapper failures | Define and check business completion before returning zero |
| Recovery | Journaled move recovery; unresolved-run gate; bounded opt-in exit-code retries | Reconcile effects, retain journals, choose retryable operations |
| Monitoring | JSON health probe and bounded notification adapter invocation | Poll it independently, monitor probe/runner exit codes, route alerts to an owner |
| Disaster recovery | Release identity and reproducible rollback procedure | Independent data backups, restoration exercises, host/storage recovery |

`process.run` nonzero child exit codes are returned values. The author must check them. Caught errors and printed diagnostics alone do not make a job fail. A successful wrapper result means the generated program returned zero; it cannot prove that the program checked the correct business outcome.

## Build a release

On the development machine, package a source entry point:

```powershell
dotnet build src/Rukbat
& .\tools\operations\New-Release.ps1 `
    -Source C:\Source\Rukbat\examples\batch-organize\main.rukbat `
    -CompilerDll C:\Source\Rukbat\src\Rukbat\bin\Debug\net8.0\rukbat.dll `
    -Destination C:\Jobs\organize\releases\v1 -ReleaseId v1
```

The build creates the usual `.ps1` beside the source. Packaging checks the compiler exit code before copying anything; an old `.ps1` is never packaged after a failed build. The destination must not already exist. A packaging I/O failure may leave an incomplete directory without a manifest; never deploy it. Use a new release directory after diagnosing the failure.

Copy the complete release directory to the deployment machine. It contains `job.ps1`, the operations scripts, the embedded process helper source, and `release.json`. Neither the compiler nor the original `.rukbat` sources are needed there. The helper compiles using the machine's existing PowerShell/.NET facilities. Pin the deployment to a tested OS/PowerShell combination; do not assume that all enterprise application-control policies permit `Add-Type`.

Give the execution account read/execute access to releases and configuration, and only the required modification rights on work/state paths. Keep other users from modifying those paths. Manifest verification detects accidental changes; it is not a cryptographic trust boundary against an attacker who can rewrite the tools or manifest. Preserve protected copies or sign the release according to local policy.

## Configure one logical job

Create the work and state directories, copy `tools/operations/job.example.json`, and set every field for the deployment:

- `job_id` and `state_directory` identify the exclusive job across all releases. Keep them unchanged during upgrades and recovery.
- `release_directory` pins a complete release. `powershell` is the absolute path of `powershell.exe` or `pwsh.exe`.
- `working_directory` fixes relative business paths. `arguments` preserves individual strings, including empty values, Unicode, quotes and leading hyphens.
- `timeout_seconds` is a 1–86400 second budget across all attempts, including helper initialization after the start record. Child cleanup can take up to ten extra seconds. The scheduler adds a sixty-second backstop for wrapper setup/teardown.
- `max_attempts` defaults to one in the example and is limited to ten. Only explicitly listed positive `retry_exit_codes` are retried, after `retry_delay_seconds`, within the same deadline. A timeout, capture failure or unknown result is never automatically retried.
- `first_due_utc` is the first time at which absence of any run becomes an alert. Replace the example timestamp. `max_success_age_seconds` limits the age of the last successful completion; include the expected schedule, duration and tolerated delay.

Do not put secrets in arguments. Child stdout/stderr can contain sensitive data; protect logs accordingly. Rotate old attempt logs and historical run records through the site's retention process. Preserve the current `<job_id>.json`, lock path, unresolved run records and business recovery journals. Deleting a lock path while a runner holds it breaks the concurrency contract.

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -File C:\Jobs\organize\releases\v1\Invoke-Job.ps1 -ConfigPath C:\Jobs\organize\job.json
```

The wrapper exits `0` for success, `1` for a recorded business failure/timeout/unknown result, and `2` for an invocation, preflight, lock, reconciliation or record-persistence failure. These are wrapper codes; the actual program code is stored as `child_exit_code`.

Each run has a unique ID and a JSON history record. `<job_id>.json` points to the latest run through a complete copied record. It includes release/artifact/config identity, start/end UTC times, attempts, child exit code, status, previous run ID, recovery note and last successful completion time. Each completed attempt has separate UTF-8 stdout/stderr logs. Output capture is limited to 64 MiB per stream. Abrupt termination, invalid output encoding or capture overflow may prevent saving partial child output; the start record still makes the unresolved run visible.

## Install, inspect and remove a scheduled task

```powershell
$tool = 'C:\Jobs\organize\releases\v1\Manage-Task.ps1'
& $tool -Action Export -ConfigPath C:\Jobs\organize\job.json -At 01:00 -XmlPath C:\Jobs\organize\task.xml
& $tool -Action Install -ConfigPath C:\Jobs\organize\job.json -At 01:00
& $tool -Action Inspect -ConfigPath C:\Jobs\organize\job.json
& $tool -Action Remove -ConfigPath C:\Jobs\organize\job.json
```

The supplied profile is a daily local-time task named `Rukbat-<job_id>`, using the selected account with S4U logon and limited privileges. It can be configured to run without an interactive session. S4U does not provide network or encrypted-file access; use a separately reviewed account/logon profile for those workloads. Task registration may require an administrator or delegated scheduler permissions. The tool never elevates itself or overwrites an existing task. Removal checks its configuration marker and refuses a running task.

The profile specifies `IgnoreNew`, `StartWhenAvailable`, an explicit working directory and execution limit. Scheduler automatic retries are disabled; the runner owns any opt-in retries. Missed daily triggers are not a durable queue of business inputs. A job processing daily inputs needs its own explicit input ledger or plans.

An exported XML definition proves configuration generation, not successful registration or execution. On the actual target, verify registration, logoff execution, last task result, corresponding run record and business output. Test daylight-saving/missed-start behavior if the schedule depends on it. [Task settings](https://learn.microsoft.com/en-us/powershell/module/scheduledtasks/new-scheduledtasksettingsset) and [S4U logon](https://learn.microsoft.com/en-us/windows/win32/api/taskschd/ne-taskschd-task_logon_type) describe the Windows facilities used here.

`tools/operations/Test-SchedulerAcceptance.ps1 -ConfigPath <disposable-job.json>` automates registration, on-demand scheduler launch, new-run verification, task-result/output checks and task removal. Prepare a disposable job whose ID is `scheduler-acceptance-<hex>` and whose compiled program writes `scheduled.txt` containing exactly `completed` in its work directory. The script refuses other job IDs, never removes a pre-existing task, and retains fixture data/logs. Run it under an account authorized to register the S4U task. It does not test logging the user off, a real timed trigger, or a machine reboot; those remain separate target acceptance checks.

## Monitor completion independently

```powershell
powershell.exe -NoLogo -NoProfile -NonInteractive -File C:\Jobs\organize\releases\v1\Test-Job.ps1 -ConfigPath C:\Jobs\organize\job.json
```

The probe emits one JSON object and exits `0` for `healthy`, `not_due` or `running`; `1` for an actionable state; `2` for invalid configuration/state, probe failure or notification failure. Actionable states include `missing`, `stale`, `failed`, `timed_out`, `unknown`, `overdue` and `configuration_changed`. A `running` record with no held job lock means `unknown`, not success. A current run does not hide an expired last-success deadline. A lock without a run record does not suppress the first-due alarm. The host clock must be reliable.

Have the site's monitoring system poll this command and alert on nonzero exit or missing probe results. Also monitor the runner/scheduler result: if state storage itself becomes unwritable, a new failure may not be recordable and the prior success record can remain. The freshness deadline bounds this otherwise silent case. Monitoring only child-program errors is insufficient.

`-NotifyScript C:\Monitoring\notify.ps1` invokes an adapter only for a nonhealthy probe, passes a temporary JSON report path as its sole argument, and bounds it to thirty seconds. The adapter must read the file before returning and return nonzero when delivery fails. `tools/operations/Notify-Webhook.ps1` is an HTTPS JSON POST example using the monitor account's `RUKBAT_ALERT_URL` environment variable; set it only for the intended endpoint. The example does not implement provider-specific payloads. Repeated probe failures invoke the adapter again; deduplication/escalation belongs to the monitoring service.

The integration tests use a local notification sink and check its received JSON. That is not proof that an external service or a person received a production alert. Test delivery and acknowledgment with the actual configured receiver. To detect a stopped machine, run monitoring outside that machine; local scheduled monitoring cannot notify while its host is down.

## Reconcile a failed or interrupted run

Do not delete the current run record to allow another launch. After any nonsuccessful run, the next invocation refuses to execute business code until its ID is explicitly acknowledged. Scheduled retries cannot bypass this gate.

1. Stop scheduling new runs and prevent concurrent manual launches. Preserve the run record, logs, input plan and recovery journal. Identify the exact release and configuration hashes.
2. Inspect actual side effects. For `batch-organize`, run the same release's `job.ps1 --recover <plan.csv>` with the configured working directory. This verifies a committed plan or restores an interrupted plan. It does not undo a committed move.
3. Fix only the environmental/input condition justified by that investigation. A replaced file or corrupt move journal must remain a manual investigation; do not reset the journal.
4. Retry using the unchanged job configuration and the failed run ID:

```powershell
& C:\Jobs\organize\releases\v1\Invoke-Job.ps1 `
    -ConfigPath C:\Jobs\organize\job.json `
    -RetryRunId '<run_id from the record>' `
    -RecoveryNote 'Verified the move journal and restored the interrupted plan.'
```

The note is an operator assertion, not an automatic proof of business correctness. The runner preserves it and links the new run to the old run. The API's own journal verification still applies. Retrying with a changed configuration is rejected. External commands need their own idempotency/reconciliation contract; terminating a process cannot undo its effects.

## Upgrade, roll back and restore data

Keep releases immutable and work/state directories separate. To upgrade or roll back:

1. Stop scheduling new runs and wait for the current job to finish.
2. Reconcile unresolved runs with their original configuration.
3. Retain the old release/configuration; test the replacement on disposable inputs.
4. Change the release path and re-register the task with the matching tool path.

Monitoring reports a configuration change until it completes successfully. Code
rollback does not restore data: verify that the old program accepts the current
format. Never replace files inside an executing release.

For data restoration, stop the task and preserve failure evidence. Restore a
verified backup into staging and compare checksums/content with its inventory
before switching live paths. Restored file IDs may differ from the move journal;
use a new reviewed plan and retain the old journal. Set backup frequency and
permitted data loss for the job, and exercise restoration on target storage.
Arbitrary external side effects have no universal backup/rollback mechanism.

## Acceptance examples and release gate

`OperationsTests` exercises both PowerShell hosts through actual packaged releases:

- CSV file organization with journaled replay (`examples/batch-organize`).
- External CLI checks with output/result verification (`examples/batch-check`).
- Read-only Windows administration, verifying a named service is running and writing typed JSON (`examples/windows-audit`).
- Literal argument preservation without original sources, manifest refusal, stale-build refusal, whole-job deadlines, concurrent launch rejection, supervisor termination, monitoring, notification handoff and explicit reconciliation.
- Scheduler XML, bounded retries and release rollback.

Run `dotnet test --filter FullyQualifiedName~OperationsTests`. Unit/API tests and operations tests establish different claims. The final deployment acceptance must additionally include the actual account, scheduler launch without an interactive login, storage permission/capacity failures, required production volume, backup restoration, remote availability monitoring and receiver acknowledgment. Record each as passed, failed or unverified; never substitute an exported task or a local notification file for those production checks.
