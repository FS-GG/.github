# Board hygiene — healthcheck leg 14

**Date:** 2026-08-03

**Scope:** Coordination-board liveness and schedulability. This is a bounded
healthcheck definition and evidence report for `.github#2018`; it does not add
a second board scanner, claim protocol, or completion protocol.

Leg 14 covers three ways a board can look healthy while it cannot safely make
progress: an `In progress` row whose holder is gone, a `Ready` row that cannot
reserve any usable `Paths:`, and a closed issue that was never verified through
the delivery completion protocol.

## Authoritative observations and owners

The existing `fsgg-coord` commands are the executable owners. A healthcheck
must consume their fresh, typed results rather than reconstructing a board from
issue prose or a cached scheduler read.

1. **Claim liveness.** `who --all-repos --json` and `reap` classify a claim as
   held, stale, unclaimed, or undetermined. `reconcile --json` then derives
   `STALE-CLAIM`, `UNCLAIMED-IN-PROGRESS`, and `CLAIM-STATUS-LAG` from the same
   fresh facts. A stale lease is eligible for `reap --apply`, but an open
   `item/<n>-*` pull request is proof of life and must be left for its holder
   or adoption route. An unreadable liveness result is not permission to reap.
2. **Schedulable touch-set.** `lint --json` owns `NO-TOUCH-SET` and
   `BAD-TOUCH-SET`; `batch` and `claim` enforce the same grammar before work
   starts. `BAD-TOUCH-SET` means **any** declared token is unmatchable, and
   fails closed in two distinct cases. If every token is unmatchable, the item
   reserves nothing and cannot be scheduled safely. If only some tokens are
   unmatchable, the remaining tokens reserve work but the declaration is more
   dangerous: the bad paths are invisible to overlap checks while the item
   appears safely declared. `TouchSetLintTests`' #646 cases pin this
   `AllUnmatchable`/`SomeUnmatchable` distinction and name only the offending
   tokens. This is deliberately a grammar/resolution check, not a
   file-existence check: a valid declaration may name a new file that does not
   exist yet. The report therefore does not turn a planned new-file path into
   a false finding.
3. **Verified completion.** `done <ref> --flip --pr <pr>` owns the terminal
   receipt: it requires the merged pull request and emits `FSGG-DONE`, then
   projects `Status=Done`. `reconcile --json` detects
   `CLOSED-ISSUE-NOT-DONE` only when no live claim remains, but its safe status
   repair is not itself evidence of a verified delivery. A future
   `org-healthcheck` completion leg must retain the `FSGG-DONE` receipt (or
   return no-verdict when it cannot establish one), rather than treating a
   closed issue and `Done` projection as equivalent. Conversely,
   `AUTO-DONE-LIVE-CLAIM` is report-only: automatic issue closure is not proof
   that release, publication, deployment, or downstream obligations finished.

The ordinary pass is therefore `budget`, fresh `reconcile --json`, `lint
--json`, and `who --all-repos --json`; after a reviewed mechanical repair it
uses `reconcile --apply`, `flush` when needed, and the same fresh reads again.
`reconcile` may make only its typed mechanical repairs. It must not invent a
replacement `Paths:` declaration, reap an undetermined claim, or stamp an
item on a former holder's behalf.

## Verdict and no-verdict discipline

An executable successor must reuse `ExitCode`, `GateError`, and `run` from
`scripts/lib/gate.py`; it must not restate the gate contract. A complete board
observation with no hygiene finding is exit `0`; a readable hygiene defect is
exit `1`; and an inability to establish the relevant board, claim, or issue
population is a permanent **no-verdict at exit `3`** via `GateError`.

Examples of exit-3 conditions include an unreadable or incomplete board scan,
an undetermined claim, malformed `Paths:` data that cannot be classified, or a
closed item whose merged-PR/done-receipt evidence cannot be read. A rate limit
or transport failure remains the shared retryable no-verdict behavior. Neither
kind of no-verdict may become a clean result, a stale claim may not become dead
without its liveness proof, and a closed issue may not become a verified done
stamp by inference.

## Negative controls

The controls exercise the existing owners rather than a parallel report-only
implementation:

- `TouchSetLintTests` supplies the exact #646 controls: an all-unmatchable
  declaration must raise `BAD-TOUCH-SET` as a zero-reservation item, while a
  declaration containing both a matchable token and an unmatchable token must
  also raise `BAD-TOUCH-SET` and name only the bad token. The latter must not
  pass merely because some work is reserved. The coordination-engine
  end-to-end fixture additionally proves that an unmatchable `Paths:` token is
  refused before it writes a claim or widens a touch-set. A declared token with
  a valid trailing subtree form remains the contrasting control, including
  when the work will create files under it.
- The stale-claim fixtures distinguish a lease that can be reaped from an open
  `item/<n>-*` pull request, which withholds reaping as proof of life. An
  undetermined read remains report-only rather than being treated as stale.
- The completion fixture proves `done` emits `FSGG-DONE` for an item closed by
  a merged pull request. Its deferred-status control retains the receipt while
  surfacing `flush`; it does not claim the board projection already landed.
  The live-claim/`Done` case remains a negative control for treating automatic
  closure as terminal.

Together these controls prevent the dangerous green: a clean-looking board
whose comparison population, path reservation, worker liveness, or completion
receipt was never established.

## Boundary and historical evidence

This leg records the board's existing liveness, touch-set, and completion
owners. It does not decide an epic's discharge, choose a missing touch-set,
close a worker's work, or claim current organisation-wide cleanliness.

When kit-delivery history is relevant to a board item, the corrected
`.github#1565` measurement is **16 opened / 4 merged**. The superseded
`12 opened / 0 merged` figure is not valid evidence.
