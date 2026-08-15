---
schemaVersion: 1
workId: 2662-critic-succession-ledger
title: Critic Succession Ledger
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2662-critic-succession-ledger/spec.md
publicOrToolFacingImpact: true
---

# Critic Succession Ledger Clarifications

## Source Specification
- work/2662-critic-succession-ledger/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: Can any existing `review-decision/v2` field carry the grant, and if not, which additive shape is least costly to consumers?
- CQ-002 [AMB:AMB-002] blocking answered: How can the grant be digest-bound without changing any digest already written?
- CQ-003 [AMB:AMB-003] blocking answered: Which record kinds may carry a grant, and may a grant ride a record that changes nothing?
- CQ-004 [AMB:AMB-004] blocking answered: After a succession, which identity is "the generation critic" for the next grant and for the accepted receipt?
- CQ-005 [AMB:AMB-005] blocking answered: Must the engine resolve or verify the grant URL?
- CQ-006 [AMB:AMB-006] blocking answered: How is a ledger leg driven inside a fixture that deliberately has no board, token, or network?
- CQ-007 [AMB:AMB-007] blocking answered: Does `Review.criticSuccessionValid` need to change for the two layers to agree?

## Answers

**CQ-001.** No existing field can carry it, and that is a measured result rather than a judgement
call. The record's complete field set is `schema, subject, revision, previousDigest, headSha, critic,
verdict, acceptedExceptions, routeApplicability, routeEvidence, policyVersion, kind, round,
initialReview, precedingReview, diffAuditRequired, diffAuditReceipts, timestamp, digest`. It already
carries the successor (`critic`) and the granted head (`headSha`) and has nowhere for the outgoing
critic, the granter, or the grant's URL. `initialReview` and `precedingReview` are pinned by the
writer to exact preceding-comment URLs, so neither is free; `routeEvidence` has fixed arity, exactly
four entries for `meaningful` and exactly one for `not-meaningful`; `acceptedExceptions` is documented
as the critic's accepted-findings list, so putting protocol identity plumbing there overloads a field
consumers enumerate as review content; and `round`, `verdict` and `kind` are closed vocabularies.

Passing the grant to the validator out-of-band instead does not work either, and this is the part
worth pinning so no later attempt spends a round on it. `Driver.structuredReviewLedger` makes the sole
`validateReviewLedger` call and is the sole entry for `reviewPhaseFacts`, the live/retired generation
split, and `parseReviewComments`/`parseEffectiveReviewComments`. Those are the host's acceptance-time
and `landable`-time readers, and they see only the comment ledger. A succession that is not legible in
the records themselves is written and then refused later by the very path that has to accept it — so
US-002's legibility is what makes US-001 reachable at all, not a decoration on top of it.

A new `kind: "succession"` value was the other candidate. It reuses a field and leaves written digests
intact, but it still cannot carry the granter or the grant URL, so it discharges only half of US-002 —
and it regenerates the `fsgg-protocol:review-policy` block in both `independent-review.md` mirrors,
which is a wider blast radius for less.

**CQ-002.** By folding the grant into `reviewDigest` only when it is present. `reviewDigest` joins its
framed fields with `|`, so appending nothing for an absent grant leaves the joined string — and
therefore the digest — byte-identical for every record ever written. Appending the three framed grant
fields when present makes the grant tamper-evident, and makes an engine that predates the field fail
CLOSED on a succession record: it decodes the record while ignoring the unknown `succession` key,
recomputes a digest over eighteen fields, gets a different value from the one recorded, and reports
`digest does not match its structured inputs`. That is the behaviour we want from an old engine
meeting a new record — a refusal, never a silent acceptance that drops the grant and then applies the
unwidened continuity rule to a record that no longer satisfies it.

Emitting `"succession": null` on ordinary records is the right wire spelling. The encoder already
emits `previousDigest`, `initialReview` and `precedingReview` as explicit nulls, so this matches the
record's existing convention rather than inventing a second one, and the decoder treats an absent key
and an explicit null identically. Records already posted are never rewritten and keep validating.

**CQ-003.** Exactly the three kinds that can be blocked by the continuity rule while a chain is live:
`confirmation`, `escalation` and `repair-phase`. A `confirmation`-only exemption was the tempting
narrow answer and it is wrong: the same conjunct is keyed on `Kind <> Initial` and does not exempt
`Escalation`, so a `confirmation`-only fix leaves the escalate-into-repair-phase hatch wedged shut —
the successor would be able to pass a chain but not to escalate one. That was measured directly on a
live chain, not inferred.

`initial` is excluded because an initial record binds its own generation critic outright and has
nothing to succeed. `acceptance` is excluded because it is the host's record, and by the time it is
written the generation critic is already whoever the last grant rebound it to; an acceptance that
wanted to change the critic would be changing whose pass is being accepted, which is not succession.

A grant on a record that does NOT change the critic is refused. Succession is an exception to a rule;
a record that does not trip that rule has no exception to claim, and admitting a decorative grant
there would let a record assert an unearned provenance no reader could distinguish from a real one.

**CQ-004.** The successor. The generation critic is a value that CHANGES HANDS at a valid grant, and
every consumer must see the identity currently in force, not the one that opened the generation.
Concretely: if `A` hands to `B`, a later round's grant must name `B` as its outgoing critic (a second
succession is legitimate — a successor can despawn too), and the accepted receipt must name `B`,
because `B` is the critic whose pass the host accepted.

This is not a behaviour change for any ledger that exists today. `Driver` derives the fact from the
generation's opening `initial` record; under the unwidened continuity rule every later record in that
generation binds the same critic, so "the opening record's critic" and "the last record's critic" are
the same string in every ledger the engine has ever validated. The definition is being made correct
for the case that is now reachable, not altered for the cases that already were — and that equivalence
is itself worth a test rather than a claim.

**CQ-005.** No. The engine holds a URL because a human or an auditing agent must be able to reach the
grant, and because a record that names WHERE the grant lives is checkable by someone who can read the
pull request. Resolving it would mean a network read inside a pure validator that today has none, and
would make ledger validity depend on GitHub's availability. What the validator owes is that the field
is present and non-blank; what it must not do is imply an authenticity it never checked. This bound is
stated in the prose rather than left for a reader to assume.

The residual is real and named: a grant URL can point anywhere. It is bounded by the fact that this
route confers strictly less than the forgeries it would presuppose — an agent that can post a review
record under a chosen identity does not need a fake grant URL to do damage — and by the grant's own
head binding, which no URL affects.

**CQ-006.** By driving the compiled engine's own `StructuredDecision` module directly, in-process,
from the fixture — `dotnet fsi` against the `FS.GG.Coord.Core` assembly the fixture's engine build
already produced. That is the ledger's real code path: it computes the candidate's digest with the
shipped `reviewDigest` and calls the shipped `validateReviewLedger` over `existing @ [candidate]`,
which is exactly the list `Client.recordReview` submits before it posts. Nothing is re-implemented in
the fixture; a mutated engine changes what the leg observes.

What that leg deliberately does NOT cover is `recordReview`'s own backlink checks and the REST post,
because those need a transport. That boundary is stated in the fixture header instead of being
papered over, and it is why a second, complementary leg drives the same succession ledger through the
shipped `review --snapshot` binary: together they show the ledger is accepted both at the write-time
gate and by the read-time consumers, which is the pair the two live failures actually needed.

**CQ-007.** No. `Review.criticSuccessionValid` is correct as it stands, and this work must leave it
alone: it binds a grant to the EXACT candidate head, which is precisely what stops a grant being
replayed after the head moves, and it already refuses generic identities and self-grants. The two
layers agree because the ledger's rule is the same rule expressed over the evidence the ledger can
see. The one place they must not silently diverge is the generic-identity predicate, which already
exists as two deliberate copies with a stated rename discipline; the ledger's copy joins that
discipline rather than starting a third convention.

## Decisions
- DEC-001 [AMB:AMB-001]: Carry the grant as one additive, optional `succession` object on `fsgg.coord.review-decision/v2` with fields `originalCritic`, `grantedBy` and `grantUrl`; do not overload `acceptedExceptions`, do not add a `kind` value, and do not pass the grant to the validator out-of-band.
- DEC-002 [AMB:AMB-002]: Fold the grant into `reviewDigest` only when present, so every already-written digest is byte-identical and a pre-field engine fails closed by digest mismatch; emit `succession` as an explicit null on ordinary records, matching the encoder's existing convention for optional fields.
- DEC-003 [AMB:AMB-003]: Admit a grant on `confirmation`, `escalation` and `repair-phase` alike; refuse it on `initial` and `acceptance`, and refuse it on any record that does not actually change the generation's critic.
- DEC-004 [AMB:AMB-004]: Define the generation critic as the identity currently in force — rebound at each valid grant — and publish that identity from `Driver.reviewPhaseFacts` and `Driver.parseReviewComments`; pin with a test that this is unchanged for every ledger without a grant.
- DEC-005 [AMB:AMB-005]: Require `grantUrl` to be present and non-blank and do not resolve it; state the bound in the prose so no reader infers an authenticity the validator never checked.
- DEC-006 [AMB:AMB-006]: Drive FR-010's ledger leg by calling the shipped `reviewDigest` and `validateReviewLedger` in-process against the fixture's own engine build, add a complementary `review --snapshot` leg over the same succession ledger, and state in the fixture header exactly which part of the live write path neither leg reaches.
- DEC-007 [AMB:AMB-007]: Leave `Review.criticSuccessionValid` and the whole `.github#2417` decision layer unchanged; express the ledger's rule over ledger-visible evidence and join the existing two-copy rename discipline for the generic-identity predicate rather than adding a third spelling.

## Accepted Deferrals
- DEC-008: A durable, machine-readable on-PR marker for the GRANT itself is deferred. Today the grant exists as prose plus a fenced JSON block and reaches the engine only through a hand-assembled `--snapshot` fact; nothing under `src/` parses it. Recording the grant's URL inside the review record — which this work does — is what makes the grant reachable and auditable from the ledger, and is strictly the prerequisite for any future marker. Building the marker here would be a second contract with its own schema, writer, and refusals, and would widen a repair three chains are parked behind.

## Remaining Ambiguity
None. Every blocking ambiguity carried from `spec.md` is resolved by a decision above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2662-critic-succession-ledger`.
