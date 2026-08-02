# Sparse-checkout closure — fleet-wide healthcheck leg 4

**Date:** 2026-08-02

**Scope:** every repository in `registry/repos.yml`, not just the authority checkout.
**Verdict vocabulary:** `0` means the audit reached a clean verdict; `1` means it found a
closure violation; `3` means a deterministic no-verdict.  A no-verdict is deliberately
not a clean fleet result.

## What is being checked

The defect class is a cross-repository `actions/checkout` that uses
`sparse-checkout` to name individual files.  That fetch can load the intended script
while omitting a load-time sibling, so the consumer fails before the assertion runs.
The original local gate is deliberately offline and only sees the authority tree.
Fleet reach belongs to `scripts/repos-audit.sh`: it already reads every rostered
repository's workflows, manages the API failure modes, and imports the authority's
rule rather than maintaining a second interpretation.

The shared rule is `scripts/check-sparse-checkout-closure.py`.  It reads its parser
and verdict contract from `scripts/lib/gate.py` (`ExitCode` and `GateError`), while
the sparse block reader comes from `scripts/lib/sparse.py`.  The fleet caller borrows
the shared grader and both refusal types (`GateError` and `SparseRefusal`).  This is
important: a copied rule or a handler that catches only one refusal type can turn a
failure to grade into a clean-looking sweep.

## Closure boundary and outcome semantics

For every rostered workflow, the audit selects only an `actions/checkout` step with
both `repository:` and `sparse-checkout:`.  It grades the same four rules as the
local gate: non-cone patterns are anchored and directory-shaped; all patterns are
literal; and, where the named repository can be resolved, each selected directory
contains a tracked path.  Cone mode still needs the last check because a file-shaped
string can otherwise look like a directory.

The audit requests foreign repository trees lazily and records an ungraded boundary
when a repository expression or an off-roster target cannot be resolved.  A tree
that cannot be read is not a boundary: it is a retryable no-verdict for the full
audit.  A refused sparse shape is a permanent **exit 3** no-verdict.  In particular,
the audit must never replace either condition with `0`, and it must never fabricate a
finding (`1`) merely because it could not establish the subject.

This is the operational command:

```sh
bash scripts/repos-audit.sh
```

Its sparse ledger reports the number of cross-repository steps, graded patterns,
full clones, ungraded steps, and rule-4 checks.  That ledger, plus its process exit,
is the fleet result; a successful local invocation of the authority gate alone is
not evidence about sibling workflows.

## Negative controls

The control is intentionally two-layered.

1. `tests/sparse-checkout-closure/run.sh` restores the pre-fix file-enumerating
   forms from `#1510` and `#1515`, and also tests unanchored `scripts/`.  Each must
   be a real finding (`1`) with the expected diagnostic; the unmodified directory
   form must remain clean.
2. `tests/repos-audit/run.sh` drives the fleet borrower through refused sparse
   shapes.  Its assertions require the shared refusal to reach the operator as an
   annotation and require **exit 3**, rather than a traceback, an exit-1 finding, or
   a clean result.

Together these controls cover both failure modes that matter to the fleet sweep:
the rule must catch an actual under-fetch, and the borrowing/orchestration layer
must preserve the no-verdict contract when it cannot grade one.

## Interpretation

The standing gate-history observation was 94 retained local runs without a red, but
that is not a health verdict on its own.  The mutation adjudication demonstrated the
local rule fires when the anchoring check is removed (`38/0` control to `35/3`
fixture result), and the fleet audit supplies the reach that local history cannot.
The report therefore retains the gate and treats the daily roster audit as the
authoritative fleet measurement.

This leg has no relationship to kit-delivery throughput except that it uses the
same roster.  Any reference to `#1565` uses the corrected measurement: **16 opened,
4 merged** — not the superseded 12/0 figure.
