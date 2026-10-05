# Work-programme acceptance entry

Run `bash tests/work-programme/run.sh`. The existing three FSI suites, Python
collector tests and runner controls execute serially. `unittest` still collects
independent test failures and owns its per-test cleanup.

`run.py` declares each suite's required tools, exact source inputs and dependency
list. The FSI suites share the programme script but create separate temporary
state. Source hashes are checked before and after each child. Missing tools or
inputs block their dependent suites; unknown dependency declarations never admit
execution. Changed shared inputs invalidate the current observation and block
later readers. Independent suites can continue after an ordinary nonzero exit.

The entry has one 170-second local deadline, with five seconds reserved for child
settlement and reporting, within the unchanged three-minute hosted job limit.
The whole invocation captures at most 1 MiB of child output. Deadline/output
exhaustion, launch uncertainty, signal termination or unobserved cleanup stops
further launches. Each child owns a new process group and scratch directory;
settlement kills only that group and observes its disappearance before continuing.
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
