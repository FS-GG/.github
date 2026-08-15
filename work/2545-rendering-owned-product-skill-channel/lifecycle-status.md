# SDD lifecycle status — `.github#2545`

Stated plainly so a reviewer reads the real state rather than inferring a better one.

## Where the package stands

| Gate | State | Evidence |
|---|---|---|
| charter | authored | `charter.md` |
| specify | authored | `spec.md` — the route decision, the disposition vocabulary, all 18 rows, the filed receiver rows |
| clarify | authored | `clarifications.md` — AMB-001/002/003 all decided, none deferred, none remaining |
| checklist | generated, 10 of 10 FRs covered | `checklist.md` — CR-001..CR-010 `pass`, 0 blocking |
| plan | authored | `plan.md` — every PD/PC/VO/PM/GV entry carries a real decision |
| tasks | generated | `tasks.yml` — 24 tasks, 0 blocking findings |
| **analyze** | **`implementationReady`** | `readiness/…/analysis.json` — 68 ready, 0 blocking, 0 stale, 0 generated-view findings |
| evidence | **`evidenceReady`** — 24 of 24 obligations supported, 0 missing, 0 stale, 0 invalid | `evidence.yml` |
| **verify** | **BLOCKED — `verify.unobservedRequiredTest`** (5 of 24 obligations self-attested) | see below |
| ship | not reached — `fsgg-sdd ship` requires `verify.json`, which `verify` does not write while blocked | — |

The delivery route (`fsgg.coord.route-decision/v2`, `sdd-required`) declares
`requiredGates: [implementationReady, analyze, verify, ship]`. The first two are met with zero
findings. `verify` is not, and this file records why rather than leaving it to be discovered — the
same posture, for the same reason, that `work/2380-feedback-report-materialization/lifecycle-status.md`
recorded and that item merged under.

## Why `verify` is blocked

`verify` requires an `observedRun` receipt — a test report SDD actually read — for **every**
obligation. **Nineteen have one**, from `verification/run-checks.sh`'s JUnit report, whose exact bytes
are digested into each receipt. Those nineteen are the ones that genuinely rest on executed
measurement, and this package went out of its way to make them so: obligations that a first pass would
have typed `review` are typed `verification` here because a real check was written for them.

| Obligation | Why it is genuinely measured |
|---|---|
| EV006 (FR-006) | `run-checks.sh` case `FR-006` **executes** `tests/skill-registry/run.sh` — hermetic, no network — and additionally requires cases 69-77 to have been reached, so a suite that passed by never running them fails here |
| EV008 (FR-008) | case `FR-008` derives the 18 Rendering row ids from `registry/skills.yml` and requires each to be named in `spec.md`'s disposition section — a 19th row reds it |
| EV012 (DEC-002), EV014 (PC-001) | case `PC-001` runs `git diff --quiet origin/main -- registry/skills.yml`: the "the contract surface is untouched" claim, measured |
| EV023 (PM-001) | case `PM-001` feeds the arm `schemaVersion: 99` and requires a refusal |
| EV001-EV005, EV010, EV015-EV022 | each runs the real arm against a real declaration pair |

**Five have no receipt, and no honest one exists:**

- **EV007** (FR-007) — the route decision and its ADR-0058/0062/0063 argument are recorded.
- **EV009** (FR-009) — the receiver rows are filed at the byte owner and the consumer.
- **EV011** (DEC-001) — Route B is chosen.
- **EV013** (DEC-003) — ADR-0063 is deliberately not amended.
- **EV024** (GV-001) — the generated work model is current; typed `generated-view`, which is what it is.

Two ways existed to turn `verify` green, and both were rejected:

1. **Type them `verification` and let `--from-test-report` attach the existing receipt.** The receipt
   would be real and its attachment false — it measures the `delivery-channel` arm, not whether a
   decision was made or an issue was filed. That is the trap `#2380` named and refused.
2. **Add checks that grep `spec.md` for its own section headings and issue numbers.** A leg that
   asserts *X is recorded* by looking for X's name is the anti-pattern this repository has measured
   repeatedly. Note the difference from EV008, which is typed `verification`: that check derives the
   expected ids from a **different artefact** (`registry/skills.yml`) and can fail when that artefact
   moves. A heading-presence grep cannot fail for any reason a reviewer would care about.

The five are typed for what they are, carry `result: pass`, `synthetic: false`, and each names the
artefact backing it. Their verification is independent review.

## What this means for review and merge

This is offered as a **known, stated limitation**, not a claim that the gate does not apply. A
reviewer may reasonably conclude either that `verify`'s test-observation requirement should not gate
the four record-and-routing obligations of an item whose implementation half is fully measured, or
that the item should not merge until that requirement is reconciled. That judgement belongs to the
critic and the host, and this file exists so it is made against the real state.

What is *not* in question: `analyze` reports `implementationReady` with zero blocking findings,
`evidence` reports `evidenceReady` with zero missing/stale/invalid, and every gate this change adds
ships with recorded gate-inversion evidence — five source mutations to the arm and twelve expectation
inversions, all observed red, in `verification/verification-evidence.md`.
