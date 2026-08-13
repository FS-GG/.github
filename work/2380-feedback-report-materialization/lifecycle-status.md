# SDD lifecycle status — `.github#2380`

Stated plainly so a reviewer reads the real state rather than inferring a better one.

## Where the package stands

| Gate | State | Evidence |
|---|---|---|
| charter | authored | `charter.md` |
| specify | authored | `spec.md` (findings F1-F8, the deliverable) |
| clarify | authored | `clarifications.md` — AMB-001/003 decided, AMB-002 deferred to `.github#2545` |
| checklist | generated, all 7 FRs covered | `checklist.md` — CR-001..CR-007 `pass` |
| plan | authored | `plan.md` — every PD/PC/VO/PM/GV entry carries a real decision |
| tasks | generated | `tasks.yml` |
| **analyze** | **`implementationReady`** | `readiness/…/analysis.json` — 0 blocking, 0 stale, 0 generated-view findings |
| evidence | authored, 4 of 28 obligations observed | `evidence.yml` |
| **verify** | **BLOCKED — `verify.unobservedRequiredTest`** | see below |
| ship | not reached | — |

The delivery route (`fsgg:delivery-route/v1`, `sdd-required`) declares
`requiredGates: [implementationReady, analyze, verify, ship]`. The first two are met. `verify` is not,
and this file records why rather than leaving it to be discovered.

## Why `verify` is blocked, and why it was not forced green

`verify` requires an `observedRun` receipt — a test report SDD actually read — for **every** obligation.
Four have one: `EV003`, `EV004`, `EV011`, `EV012`, satisfied by
`verification/run-checks.sh`, whose JUnit report is recorded with an exact-bytes digest. Those are the
obligations that genuinely rest on executed measurement.

The remaining ten (`EV001`, `EV002`, `EV005`-`EV010`, `EV013`, `EV014`) are documentation and routing
obligations: stating the root cause, adjudicating two candidate mechanisms, filing three rows at their
causes, routing one human decision, and keeping a deferral visible. **There is no suite for prose**,
and this item ships no code — its declared `Paths:` are the SDD package alone.

Two ways existed to turn `verify` green, and both were rejected deliberately:

1. **Relabel the ten as `kind: verification`** so `--from-test-report` would attach the existing
   receipt to them. That would attach a receipt from a run measuring predicate evaluation to
   obligations about filing issues and routing a decision. The receipt would be real and its
   attachment false.
2. **Widen `run-checks.sh` to "cover" them** with checks that grep `spec.md` for issue numbers and
   assert files exist. That is a leg asserting *X is wired* by looking for X's name instead of running
   it — the precise anti-pattern this repository has measured repeatedly, and it would convert an
   honest red into a green that means nothing.

The ten obligations are typed `kind: review`, which is what they actually are: claims whose verification
is independent review, not a test run. They carry `result: pass`, `synthetic: false`, and each names the
artifact backing it.

## What this means for review and merge

This is offered as a **known, stated limitation**, not a claim that the gate does not apply. A reviewer
may reasonably conclude either that `verify`'s test-observation requirement should not gate a
record-only package, or that the package should not merge until that requirement is reconciled. That
judgement belongs to the critic and the host, and this file exists so it is made on the real state.

What is *not* in question: `analyze` reports `implementationReady` with zero blocking findings, and the
measurement obligations that could be executed were executed, with gate-inversion evidence for all
eight checks recorded in `verification/verification-evidence.md`.
