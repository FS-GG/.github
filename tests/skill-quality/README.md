# Semantic skill quality gate

Run `bash tests/skill-quality/run.sh` with the pinned Python dependencies and
.NET SDK installed. The workflow also runs the compiled contract/reader/adapter/
preflight/CLI fixtures first. A local source-only check is available through
`bash tests/skill-quality/run.sh --preflight BASE_SHA`; it does not qualify the
full semantic or CLR gate.

## Runtime and mutation controls (#5228)

The cancelled [run 41409628030](https://github.com/FS-GG/.github/actions/runs/41409628030)
completed setup at 08:14:47 UTC, compiled fixtures at 08:16:25, and was cancelled
at 08:24:39 while executing the semantic rejection cases. Its log places the
initial engine build/catalog check between 08:16:33.733 and 08:18:05.182
(91.45 seconds). The board mutation harness occupies the interval from
08:18:35.685 to 08:23:40.258 (304.57 seconds), before the remaining semantic
fixtures and rejection cases.

`routine-delivery-board-mutate.py` first runs every test in the three board
classes against the unmutated helper. Each mutant then uses unittest fail-fast:
one failing test is sufficient to reject that mutant. A surviving mutant still
runs all tests and fails the harness. All mutants remain enumerated, with a
separate elapsed time and flushed result for each. No coverage is selected from
changed files, and the workflow's ten-minute timeout remains in place.

The preflight choice for this change is source inspection and the existing
mutation harness, followed by the full semantic gate. The defect is redundant
execution after a mutation has already been rejected; no job dependency,
concurrency, artifact or cache contract changes. A custom state model would add
maintenance without testing the affected unittest behavior. Keep initial local
qualification bounded to fifteen minutes, with disk-backed temporary storage
and the host's admitted CPU/memory limits. Hosted runs, including queue time,
remain separate measurements.

## Required status decision

A timeout-cancelled `semantic-skill-quality` is failed/incomplete qualification,
not neutral merge evidence. A merge requiring this check must wait for a
successful complete check at the candidate head under the native merge rules.
Do not rewrite a timeout to success/neutral or accept a partial fixture run.
Cancellation of a superseded PR head does not qualify its replacement head.
This decision does not change branch protection or bypass any other required
check.

Delivery #5228 remains open until ten consecutive hosted runs of the fix
complete without timeout, with their job wall times recorded. A local pass or
source merge alone does not meet that acceptance.
