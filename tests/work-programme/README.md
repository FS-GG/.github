# Work-programme acceptance entry

Run `bash tests/work-programme/run.sh`. The four FSI suites, Python
collector tests, context controls, owner-restart structural controls, synthetic
watcher regressions and runner controls execute serially. `unittest` still collects
independent test failures and owns its per-test cleanup.

`run.py` declares each suite's required tools, exact source inputs and dependency
list. The restart structural suite pins its script, fixture index, all six delta
inputs and all six expectations; the watcher suite pins its script and imported
`tools/routine-delivery.py`. Their statuses and captured evidence appear as required
`context-restart-structure` and `context-restart-watcher` rows in the same report.
These synthetic controls establish input shape and watcher behavior only; integrated
restart, native usage, context savings and adoption remain separate. The FSI suites share the programme script but create separate temporary
state. Source hashes are checked before and after each child. Missing tools or
inputs block their dependent suites; unknown dependency declarations never admit
execution. Changed shared inputs invalidate the current observation and block
later readers. Independent suites can continue after an ordinary nonzero exit.

The entry has one 170-second local deadline, with five seconds reserved for child
settlement and reporting, within the unchanged three-minute hosted job limit.
The whole invocation captures at most 1 MiB of child output. Deadline/output
exhaustion, launch uncertainty, signal termination or unobserved cleanup stops
further launches. Each child owns a new process group and scratch directory;
settlement retains the unreaped direct leader while signalling its group and
observing that no live group members remain through Linux `/proc`, then reaps the
leader last. It never signals or probes the numeric group after reaping.
This ordinary runner assumes trusted repository tests; it is not a native custody
or hostile-process sandbox.

One JSON projection retains input identities, each required suite's status,
dependencies, exit, captured evidence, first cause, cleanup results, reporting
status and omitted coverage. Captured output can expand during JSON escaping but
remains bounded by the fixed capture cap. Failed, blocked, unknown or budget-limited
required work returns nonzero and cannot qualify a partial run. Reporting errors
also return nonzero after cleanup without replacing an earlier cause.

`python3 tests/work-programme/test_run.py` exercises disposable controlled children,
including the actual shell entry in a copied layout with FSI stand-ins. These
controls do not establish actual FSI qualification. The real complete entry must
still pass for the exact candidate under its admitted local or hosted resources.
Fault injection stays in temporary fixtures; production inputs have no injection
switches. No new model is needed for this fixed serial entry: executable dependency,
input-drift and process-settlement controls cover its small interaction surface.

The additive `routine-decisions` suite exercises the real pure reducer and CLI, including
original-effect retention, same-owner repair, typed limits, revision conflict and the
2048-byte UTF-8 stdout bound. Recorded private replay remains observation-only and separate
from these synthetic controls; neither supplies execution or publication authority.
