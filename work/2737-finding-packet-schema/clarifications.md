---
schemaVersion: 1
workId: 2737-finding-packet-schema
title: Finding Packet Schema
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2737-finding-packet-schema/spec.md
publicOrToolFacingImpact: true
---

# Finding Packet Schema Clarifications

## Source Specification
- work/2737-finding-packet-schema/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: Does a malformed packet get refused at the point of writing, or accepted and flagged?
- CQ-002 [AMB:AMB-002] blocking answered: Do the sentinel-bearing fields become a nullable or a tagged union?
- CQ-003 [AMB:AMB-003] blocking answered: Does the validator apply to the packets already on the registers, or only forward?

## Answers

### CQ-001 — strictness

The question contains a false alternative, and naming that is the answer.

"Refused at the point of writing" and "accepted and flagged" sound like two settings of one dial, but
they are answers to two different questions: **how strict is the validator**, and **where does it
sit**. Separating them dissolves the dilemma.

The data-loss risk is real and measured — this session lost two critics' entire findings comments to a
tooling fault (`gh api -f body=@file` sending the path literally), recoverable only because a host
relayed them from a report. But re-read what that fault was: a packet destroyed **on its way to the
register**. Nothing about a validator caused it, and a lenient validator would not have prevented it.
The risk lives in the write path, and the write path is exactly where this item must not go.

`board-analyst/SKILL.md` already forbids the placement that would carry the risk: *"Nothing waits on
you… a synchronous filing choke-point would wedge chains, and a wedged chain is a worse failure than a
duplicate row."* A validator that could refuse a post would be that choke-point.

So the validator is **strict**, and it is **never in the write path**. It is a command the finder runs
on its own draft, before posting, exactly as `intake validate` is. A packet that fails validation is
still postable — as prose, as it is today — and the finder is told per field what is wrong while it
still holds the context. A despawning agent that has time for one action posts what it has; it loses
nothing it would otherwise have kept, because nothing new stands between it and the register.

This also preserves SB-008 and the no-blocking property that `.github#2737`'s own body requires be kept
unchanged.

### CQ-002 — the sentinels

`.github#2737`'s body proposes `redToday`, `derivedBy` and `classRow` be *"nullable with an explicit
`null`, never the string `none`"*. That is a real improvement on the string `"none"` and it is still
the wrong shape, for a reason the register measures rather than supposes.

Each of those three fields asks the finder to **go and look**: for a command failing on `main`, for a
`scripts/check-*.py` that already derives the condition, for an open row that already anchors the
class. Those are tests 1, 2 and 3 of the filing bar. A `null` says "there is none" — but it cannot say
whether the finder searched and found none, or did not search. Those are different facts and they
oblige the analyst differently: the first is evidence it can weigh, the second is a search it must now
perform itself, which is the whole cost the packet exists to avoid.

**Real packets occupy the second state, explicitly, and had no way to say so.**

- `.github#2691` comment `5304198465` writes: *"an adjudicator should check whether a gate already
  derives 'an application verb with no command-surface dispatch'."* That is `derivedBy` = did not
  search. Under a nullable, that finder must write `null` — asserting a search it did not run.
- `.github#2691` comment `5308627798` names the exact generalising measurement and then writes: *"and
  **I did not run it** — I am reporting one measured instance, not asserting a fleet-wide pattern."*
  A finder being scrupulous about precisely this distinction, in a form that cannot record it.

So each of the three becomes a three-case tagged union, `Answer`:

```json
{"found": "…"}             the finder searched and found this; the string is the answer
{"searchedNotFound": "…"}  the finder searched and there is none; the string is the search performed
{"notSearched": "…"}       the finder did not search; the string is why not
```

**Every case carries a non-blank string, and that is the load-bearing part** — more so than the
arity. `none` carried no evidence and `null` carries less. Here the analyst is handed the search
itself, so it can judge whether the search was adequate rather than trust that one happened; tests 2
and 3 are exactly judgements about search adequacy. A nullable is therefore not merely coarser than
this shape, it is **strictly worse than the prose it replaces**, because the prose at least let a
finder write the sentence.

`cause` keeps two cases — `{"established": "…"}` or `{"notEstablished": "what was measured
instead"}` — which is `.github#2737`'s own proposal and is right: `notEstablished` already *is* the
"I could not" case, and requiring what was measured instead is `.github#1858`'s rule applied one step
earlier, at the packet, while the finder still holds the measurement.

### CQ-003 — migration

**Forward only**, and the measurement is what decides it rather than a preference for the cheap option.

Both registers were read in full on 2026-08-17: **134** comments, of which **99** lead with an
`fsgg:finding-packet` marker. **0 of the 134 carry a parseable JSON block**, and **0 of the 99** carry
all eight fields in the prescribed colon form. Under a deliberately generous prose reading — which
counts a *mention* of a concept rather than an *answer* to the field, and is therefore an upper bound
— **9 of 99** touch all eight. The instrument behind those figures was controlled before any number
was read from it, and two earlier cuts of it were discarded for being wrong in opposite directions;
the spec's User Value section records that in full, because a measurement nobody can check is not one.

The decisive figure needs no instrument judgement at all: **not one packet on either register is in a
form any parser can read.** A validator applied retroactively would therefore reject **all** of
them — 106 of 106 by the anchor, 99 of 99 by the marker, either way 100%. That is not information. It
is the mirror image of the failure `.github#2737` warns against: a validator that accepts everything
cannot fail, and a validator that rejects everything cannot discriminate — neither tells an analyst
anything it did not already know. It would also contradict AC5 of the row, which requires that legacy
prose packets remain readable and not be invalidated.

Forward-only does **not** mean the validator is never tested against reality, and this is the clause
that keeps CQ-003 from becoming the ceremony trap. The fixture corpus is drawn from real,
previously-filed packets, faithfully lifted into the JSON form with their content preserved and
nothing invented. Some lift cleanly and validate; others are **rejected even after a faithful lift**,
because their content — not their formatting — is deficient. The rejected set is named in the plan,
is non-empty, and is what makes this validator falsifiable.

## Decisions
- DEC-001 [AMB:AMB-001]: The validator is strict and sits outside the write path. `packet validate` is run by the finder on its own draft before posting; nothing gains the power to refuse a post, no bot stands between a finder and a register, and a packet that fails validation is still postable as prose.
- DEC-002 [AMB:AMB-002]: `redToday`, `derivedBy` and `classRow` become the three-case tagged union `Answer` — `found` / `searchedNotFound` / `notSearched` — each case carrying a non-blank string, rather than a nullable. `cause` becomes the two-case union `established` / `notEstablished`.
- DEC-003 [AMB:AMB-003]: The validator applies forward only. The 106 anchored packets already on `.github#2691` and `.github#2687` are neither rewritten nor invalidated, the skill text says so explicitly, and real previously-filed packets are instead used as the fixture corpus, with a named non-empty rejected set.

## Accepted Deferrals
- CR-001: Retrofitting the registers to the JSON form is deferred indefinitely, and is not scheduled work. DEC-003 records why: the packets are evidence, not data, and a bulk rewrite would cost their provenance for no gain to any reader.
- CR-002: Automatic extraction of a packet from a PR comment body (finding the fenced block, decoding it, and reporting on the comment) is deferred. It is an IO concern and belongs with the analyst's own metered read, not in the pure core this item ships.

## Remaining Ambiguity
None. AMB-001, AMB-002 and AMB-003 are resolved by DEC-001, DEC-002 and DEC-003 respectively.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2737-finding-packet-schema`.
