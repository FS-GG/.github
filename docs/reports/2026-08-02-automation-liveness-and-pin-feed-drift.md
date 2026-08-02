# Automation liveness and pin/feed drift — healthcheck legs 9–10

**Date:** 2026-08-02

**Scope:** the automation loops represented by the organisation's repositories, and the
Dependency Dashboard / package-pin evidence those loops consume.  This is a bounded
healthcheck report, not an implementation of a new skill or a second gate contract.

**Verdict vocabulary:** a clean, fully graded observation is exit `0`; an observed
liveness or drift finding is exit `1`; and an input that cannot support a verdict is
exit `3`.  The future leg must import `ExitCode` and `GateError` from
`scripts/lib/gate.py` and call its runner.  It must not duplicate that mapping in a
new script.  Transient transport failures remain the shared runner's retryable
no-verdict (`2`); neither no-verdict is a clean result.

## Leg 9: automation-loop liveness

The question is whether each known bot loop is making progress, not merely whether it
has created activity.  For a bounded observation window, retain the identities of
opened pull requests, then classify each as merged, closed-unmerged, or still open.
Report the three counts together with the window and the API query boundary.  An
opened-only count cannot distinguish a productive loop from one repeatedly proposing
work that never lands; a merged-only count loses abandoned work and makes a stalled
loop look quiet.

A missing or ambiguous loop identity, an unreadable PR state, or a window whose event
history cannot be enumerated is a **no-verdict (exit 3)**.  The checker must name the
loop and the missing evidence rather than silently omitting it from the aggregate.
The result is a finding only after the loop was graded and violates a stated policy;
unreadable evidence is not a finding fabricated from absence.

`#1565` remains the worked historical correction: it observed **16 opened and 4
merged**, not the superseded `12 opened / 0 merged` premise.  Any fixture, report, or
acceptance check that repeats the old numbers is invalid rather than a second source
of evidence.

### Negative control

The leg's fixture must contain a readable synthetic loop with at least one
closed-unmerged PR in addition to an open and a merged PR.  The assertion must prove
the renderer keeps all three buckets distinct.  Mutating that fixture to remove the
merge (or to classify the closed-unmerged PR as merged) must produce an exit-`1`
finding with the loop identity.  Separately, deleting the loop's required state
record must produce exit `3`, never `0` and never an invented liveness finding.

## Leg 10: Dependency Dashboard and pin/feed drift

This leg observes two separate facts and must not collapse them:

1. **Dashboard intent.** Parse Dependency Dashboard entries into their subject,
   requested version, source, and actionable state.  An absent, malformed, or
   ambiguously attributable entry means the requested subject cannot be graded.
2. **Delivered state.** Read the repository pin and the authoritative feed version
   for the same package.  Compare parsed versions and record pin age from a defined,
   reproducible timestamp source.

A dashboard request without a matching pin change is actionable drift; a pin that
names a version unavailable from the relevant feed is a different finding.  The
report must preserve those causes separately, because resolving dashboard text cannot
prove publication and reading a feed cannot prove that a dashboard request was seen.
Feed data that cannot be read, a package/version that cannot be parsed, or dashboard
text that cannot be attributed is **exit 3** through `GateError`, not a pass based on
the subset that happened to be readable.

### Negative control

Use a canned dashboard entry for a package with an older valid pin and a newer
authoritative feed version.  It must yield an exit-`1` drift diagnostic identifying
the package, requested version, pin, and feed version.  A companion fixture with an
unparseable dashboard marker or feed version must yield exit `3`; treating it as an
empty dashboard or a newest-version match would be a false clean verdict.

## Evidence boundary for the eventual implementation

The future checker should emit a compact per-loop and per-package ledger, preserving
the source URL/query, observation window, and the exact no-verdict reason.  It should
reuse roster traversal and the shared gate primitives where they already exist rather
than introduce a parallel repository list or locally re-spell `ExitCode`/
`GateError`.  This report deliberately does not claim a present organisation-wide
health result: no leg-9/10 executor and no captured fleet run are in this change.
