---
schemaVersion: 1
workId: 2583-consolidation-tax
title: Consolidation Tax
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2583-consolidation-tax/spec.md
publicOrToolFacingImpact: true
---

# Consolidation Tax Clarifications

## Source Specification
- work/2583-consolidation-tax/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: A pure insertion can redefine scope. Is the structural rule still right, and what pays for the gap?
- CQ-002 [AMB:AMB-002] blocking answered: How wide must each judged-line digest be before a collision can make a scope change invisible?
- CQ-003 [AMB:AMB-003] blocking answered: Who authors the judged-line record — the agent, or `record`?
- CQ-004 [AMB:AMB-004] blocking answered: Does the judged-line record live inside the receipt JSON or beside it?
- CQ-005 [AMB:AMB-005] blocking answered: Is refusing an empty judged subject a bolted-on special case, or an instance of a rule already stated?
- CQ-006 [AMB:AMB-006] blocking answered: Which boundaries must pay the additive notice?

## Answers

**CQ-001.** Yes, and the gap is paid for rather than denied. The alternative to a structural rule is a
semantic one, and nothing in this engine can read intent from prose; the only other alternative is the
status quo, which is the defect. The residual is genuinely narrower than it first looks: an insertion
cannot *change* any statement the route decision judged — every one of them is still present,
byte-identical, in order — it can only add a statement beside them. What it can do is add a statement
that *should* have re-opened the decision. That is real, it is this work's most dangerous edge, and it
is discharged by making the read say so (FR-004) instead of letting an additive resolution be
indistinguishable from a byte-identical body.

**CQ-002.** The question dissolves, and the reason is the load-bearing part of this design. The judged-
line digests are a **locator**, not the safety boundary. Once a candidate subsequence of the current
subject has been located, those matched lines are taken **from the current body**, joined, and hashed
with the full, unchanged `hashHex` — and the result is compared against the receipt's own
`subjectRevision`. So the additive candidate accepts exactly when:

> `subjectRevision` equals the canonical revision of a **subsequence of the current subject**.

That is a 256-bit check, the same strength `.github#2392` already relies on, and it is the final
arbiter. A digest collision can therefore only cause the *wrong* alignment to be located, which then
fails the full check — a false **negative** (`Stale`), never a false positive. A false positive would
require a full SHA-256 collision, which is precisely today's assumption.

Two consequences worth writing down. First, when nothing was inserted the located subsequence is the
whole subject and the check degenerates to the canonical check exactly — the new candidate is a strict
generalisation, not a parallel scheme. Second, because a digest is a full-strength identity of one line
in the no-collision case, *every* valid alignment reconstructs byte-identical text, so a single greedy
leftmost scan is exact and no search is required. 16 hex characters (64 bits) is chosen for the
locator: false-negative probability 2^-64, and roughly 1.2 KB of marker for a 70-line body such as
`.github#2583`'s own.

**CQ-003.** `record` derives it. It is a mechanical function of the very body `record` has just
validated the receipt's `subjectRevision` against, so derivation inherits that proof exactly and adds no
new trust. Charging it to the author would mean hand-computing seventy digests, and — worse — would
create a new failure mode in which an authored record disagrees with the body, which `record` would
then have to police. A derived binding is not a judgement and must not be charged to the author.

**CQ-004.** Beside it, as a sibling `<!-- fsgg:delivery-route-subject-lines/v1 … -->` line inside the
same receipt comment, between the existing marker and the JSON. This keeps the agent-authored receipt
JSON **byte-verbatim** in the posted comment, and leaves `DeliveryRouteApplication.decode` — shared by
the offline `route validate` path — entirely untouched. Placing it inside the object would force
`record` to re-serialize the agent's authored receipt, losing its formatting and any field it did not
know about, to store data the agent did not write.

**CQ-005.** It is an instance of a rule this work already states, and finding it required executing the
arm rather than reading it. `#266`'s rule — "I could not evaluate this" is never "I evaluated it and it
passed" — was applied to a MISSING locator record (`splitSubjectLineLocators` fails closed) but not to a
PRESENT BUT EMPTY one. An empty judged set is *nothing was judged*; the arm read it as *everything was
judged and all of it survived*. So the repair is not a new principle, it is the same principle reaching
the second of its two shapes, and the two are kept distinct in the source because their reasons differ.

The mechanism matters more than the verdict here, because it is the one false positive that does not go
THROUGH the 256-bit hash — it goes AROUND it. `record` writes a zero-locator marker and a
`subjectRevision` of `hashHex ""`; alignment consumes an empty want-list vacuously; the full-width guard
then compares `hashHex ""` against a recorded revision that IS `hashHex ""` and holds for every possible
body. Nothing legitimate is lost by refusing it: if the subject is still empty the CANONICAL arm has
already matched and this candidate is never reached, and if the subject has since gained lines then the
route decision genuinely judged none of them.

It also forces a correction to a claim this package made: "a false positive requires a full SHA-256
collision" is true only for a NON-EMPTY judged subject. That sentence now carries its condition, in the
source and in the pull request.

**CQ-006.** Every boundary that INSPECTS or ACTS on the row; not the one that merely counts candidates.

`delivery-route show` is where an agent inspects, and the claim/take mutation boundary is where a worker
COMMITS to the route — that is precisely the moment the AMB-001 trade is spent, so it is where the
consideration must be delivered. Round 1 shipped the notice at `show` only, while the two paths that
consume the widened row — the scheduler that keeps it in `batch` and the worker that claims it — were
told nothing; a claiming worker learned nothing unless it independently chose to run `show`.

The per-candidate SCHEDULING read stays silent, deliberately. `readDeliveryRouteVerdict` and
`sddPackageTokens` run once per candidate item on every `batch`/`next`/`verify-paths`, and a notice that
fires on everything is read as noise, which is how a control stops being a control (#266). The honest
consequence is that the universal claim must be narrowed rather than defended: the guarantee is bounded
to an agent inspecting or claiming the row, and the source says so.

## Decisions
- DEC-001 [AMB:AMB-001]: Adopt the structural subsequence rule and accept that an insertion can redefine scope; discharge it with FR-004's mandatory reporting of the additive match and the inserted-line count, never with silence.
- DEC-002 [AMB:AMB-002]: Judged-line digests are a locator only; acceptance is decided by re-hashing the located subsequence with the unchanged full-width `hashHex` and comparing to the receipt's own `subjectRevision`. Locator width is 16 hex characters, and a collision costs a false negative, never a false positive.
- DEC-003 [AMB:AMB-003]: `delivery-route record` derives the judged-line record from the body it just validated `subjectRevision` against; the agent never authors it.
- DEC-004 [AMB:AMB-004]: The judged-line record is a sibling marker line inside the receipt comment, so the agent-authored JSON stays byte-verbatim and `DeliveryRouteApplication.decode` is unchanged.
- DEC-006 [AMB:AMB-005]: Give the empty judged subject its OWN named outcome (`AdditiveOutcome.JudgedNothing`) decided before alignment, carrying its own diagnosis into the refusal — the shape ported from `scripts/check-gate-finding-history.py`'s zero-runs arm after verifying the mapping arm by arm, rather than invented. Refuse the additive candidate when the recorded locator record is empty — "nothing was judged" is never permission (#266) — and record that the full-width safety claim is conditional on a non-empty judged subject, in the source and in the pull request. Cover it with a degenerate-body leg held OUTSIDE the corpus floor that excluded the shape.
- DEC-007 [AMB:AMB-006]: Emit the additive notice from one shared spelling at `delivery-route show` AND at the claim/take mutation boundary; keep the per-candidate scheduling read silent, and narrow the source's universal claim to the boundaries on which it actually holds.

## Accepted Deferrals
- DEC-005: Retroactively upgrading receipts recorded before this change is deferred. There is no stored judged-line record for them and none can be recovered, so they keep exactly today's behaviour until re-recorded — the same posture `.github#2392` AC5 took for its own legacy bridge. Re-recording is itself a route re-affirmation, which is the correct act for a row whose subject an agent wants re-judged.

## Remaining Ambiguity
None. Every blocking ambiguity carried from `spec.md` is resolved by a decision above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2583-consolidation-tax`.
