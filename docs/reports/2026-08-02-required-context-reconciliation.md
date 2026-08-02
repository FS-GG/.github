# Required-context reconciliation — healthcheck leg 3

**Scope:** the existing organisation-wide required-context reconciliation. This
is an evidence report for `.github#2013`, not a second implementation of the
gate.

`scripts/check-required-contexts.py` compares the required status contexts from
both classic branch protection and rulesets with the contexts the audited
repository can report on every pull request. It also follows cross-repository
reusable-workflow calls, so a caller/callee job-name mismatch cannot leave a
required check permanently pending without being detected. The roster workflow
in `.github/workflows/required-context-coherence.yml` performs that comparison
with an installation token that can read receiver protection; a receiver's
ordinary `GITHUB_TOKEN` cannot supply `administration: read`.

The executable gate imports `ExitCode`, `GateError`, `Unreachable`, and `run`
from `scripts/lib/gate.py`. That shared harness is the sole verdict contract:
in particular, an unreadable protection store, malformed workflow, unresolved
callee, or other permanent inability to establish the graph is the shared
no-verdict exit **3**, not a clean reconciliation or an invented finding.

## Negative control and evidence

`tests/required-contexts/run.sh` is offline and exercises the gate through a
stubbed GitHub API. Its headline negative control uses FS.GG.Audio's real
caller, this repository's `lock-range-coherence.yml` callee, and Audio's real
required contexts. Renaming only the callee job from `lock-ranges` makes the
fixture return a finding and name the missing `lock-ranges / lock-ranges`
context. The same fixture proves unreadable and ambiguous inputs reach the
shared no-verdict paths rather than passing.

This report records the existing executable owner and its test evidence. It
does not introduce another required-context reader or a parallel exit-code
definition. Any historical kit-delivery reference retains the corrected
`.github#1565` measurement: **16 opened / 4 merged**.
