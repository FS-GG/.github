---
schemaVersion: 1
workId: 2737-finding-packet-schema
title: Finding Packet Schema
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Finding Packet Schema Specification

Prose status: specified

## User Value

Under the standing direction on `.github#2695` the board analyst is **the only actor that files**.
Every finder — every worker, and every independent critic — reaches the board only as a finding
packet. That makes the packet the single input on which all board growth depends, and
`board-analyst/SKILL.md:47` states its status exactly:

> The anchor is a search key, not an engine marker: nothing parses it.

Three actors are worse off for that, and each gets something concrete here.

**The finder**, which is usually an agent about to despawn, can check its packet against the contract
the analyst will read it under **before** posting it, while it still holds the tree and the cause. The
alternative is what happens today: the packet is discovered to be unjudgeable a pass later, by which
time the context that produced it is gone and re-deriving it costs a whole worker slot — the exact
cost `drive-board`'s host loop says the packet exists to avoid.

**The analyst** receives a shape it can trust and spends its judgement on the three tests of the
filing bar rather than on working out which of eight fields a free-prose comment happened to answer.

**Whoever files** does not retype what the finder established: a validated packet's `paths` and
`cause` lift directly into the `fsgg.coord.intake/v1` draft that `intake validate` already accepts.

### The measurement, taken rather than estimated

Both registers were read in full on 2026-08-17 with
`gh api repos/FS-GG/.github/issues/<n>/comments --paginate`:

| register | comments |
|---|---|
| `.github#2691` (pending) | 112 |
| `.github#2687` (rejected) | 22 |
| **total** | **134** |

Of those 134, **106** mention the `fsgg:finding-packet` string anywhere, and **99** lead with an
`fsgg:finding-packet` marker — the documented placement. The gap is itself a finding: the anchor is a
plain string with no parser, so a comment that merely *quotes* it is indistinguishable from a packet.
Seven of the 106 lead with a different marker entirely (four `fsgg:analyst-rejection`, and one each of
adjudication, increment and registration).

| measurement | result |
|---|---|
| carry a parseable ` ```json ` block | **0 of 134** |
| carry all eight fields in the prescribed colon form | **0 of 99** |
| touch all eight concepts under a deliberately *generous* prose reading | **9 of 99** |
| are increments or adjudications rather than packets, by heading | **24 of 106** |

**The instrument is controlled, and it needed to be: the first two cuts of it were both wrong, in
opposite directions.** The first reported `surface` present in 0 of 106 — a broken regex, because the
corpus answers its fields as bold prose headings (`**Surface.**`) rather than as `surface:` lines. The
second, rebuilt around bold headings, reported `derived-by` in 1 of 106; a direct search for the
*concept* found **28**, so that instrument was under-counting by a factor of nearly thirty and its
"0 of 106 carry all eight fields" was not a fact. Neither error was visible in its own output. Both
were caught only by measuring the same question a second way.

The figures above come from a third instrument run against controls before any number was read from
it: the exact eight-field form `board-analyst/SKILL.md` prescribes must score **8/8**, and a body with
no fields must score **0/8**. That control fired on this cut too — it rejected a `paths` pattern that
could not match the prescribed form — which is the only reason the pattern was fixed rather than
shipped. The surviving bias is stated rather than hidden: the generous instrument counts a *mention*
of a concept, not an *answer* to the field, so **9 of 99 is an upper bound on conformance**, and the
true count of complete packets is lower. The one figure that carries no instrument risk is the first:
**no packet anywhere on either register is in a form any parser can read.**

## Scope

- SB-001: A `FindingPacket` module in `FS.GG.Coord.Core` — pure, total, never throwing — carrying the `fsgg.coord.finding-packet/v1` type, its JSON decoder, and its validator, with a signature file stating the contract.
- SB-002: A `packet validate <file>` verb on the coordination CLI, mirroring `intake validate`, so a finder gates its own output before posting. This reaches `src/FS.GG.Coord.Cli.Kernel/Options.fs(i)`, `src/FS.GG.Coord.Cli/Program.fs`, and a new `PacketApplication`.
- SB-003: `.claude/skills/board-analyst/SKILL.md` and `.claude/skills/pnext-item/references/findings-and-filing.md` carry the JSON form, and `SKILL.md:47`'s "nothing parses it" sentence is replaced by what now does.
- SB-004: The sentinel-bearing fields typed as a three-case tagged union rather than as a nullable, so that "I looked and found nothing" stays distinguishable from "I did not look".
- SB-005: A fixture corpus drawn from **real, previously filed packets** on `.github#2691` and `.github#2687`, faithfully lifted, carrying both an accepted set and a non-empty rejected set.
- SB-006: This SDD package under `work/2737-finding-packet-schema/` and `readiness/2737-finding-packet-schema/`.

## Non-Goals

- SB-007: **The validator does not judge.** Test 1 asks whether the named command is genuinely red, test 2 whether the named gate genuinely derives the condition, test 3 whether the named row genuinely anchors the class. None of those is mechanical, none becomes so here, and a packet whose `redToday` is `notSearched` still parses and still validates — it is simply a packet the analyst can see is not yet judgeable under test 1.
- SB-008: **Nothing gains the power to refuse a post.** Posting a packet still blocks no review round, no merge and no done stamp. There is no bot between a finder and a register, and this item does not add one.
- SB-009: **No retroactive validation.** The 106 packets already on the registers are not rewritten, not migrated, and not invalidated. See AMB-003.
- SB-010: **The registers themselves are unchanged.** `.github#2691`, `.github#2687` and `.github#2703` keep their present role; giving packets an address is already done and is not re-litigated here.
- SB-011: **`.github#2735` is not a dependency.** It fixes the reason `intake apply` cannot file in this repository, and landing it first would let this row be exercised through the real filing path rather than a simulated one. That is a sequencing preference with a reason, not a blocking edge, and no `Blocked by` is recorded for it. Its declared lane (`src/FS.GG.Coord.GitHub/Reads.fs`, `Reads.fsi`, `tests/FS.GG.Coord.GitHub.Tests/ReadTests.fs`) is disjoint from this one.

## User Stories

- US-001 (P1): As a finder about to despawn, I can validate my finding packet against the analyst's contract before posting it, and be told per field what is wrong while I still hold the context that produced it.
- US-002 (P1): As the board analyst, I can read a packet whose shape I can trust, tell "the finder looked and found nothing" apart from "the finder did not look", and lift a packet I decide to file into an intake draft without retyping it.
- US-003 (P2): As a reader of the skill text, I learn the validated path, and I learn explicitly that the packets already on the registers remain valid and readable.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a packet document carrying a field that is not one of the nine named fields, when it is parsed, then parsing fails with a finding naming that field, and no packet is produced.
- AC-002 [US-002] [FR-002]: Given a packet whose `redToday`, `derivedBy` or `classRow` is written as the string `"none"` or as JSON `null`, when it is parsed, then parsing fails with a finding naming that field and stating the three-case object shape that was meant.
- AC-003 [US-001] [FR-003]: Given any input at all — unreadable JSON, a JSON array, a number, an empty string — when it is parsed, then a typed `Finding list` is returned and no exception escapes.
- AC-004 [US-002] [FR-004]: Given the fixture corpus lifted from real previously-filed packets, when each is parsed and validated, then the named accepted set validates and the named rejected set fails with the recorded per-field findings, and the rejected set is not empty.
- AC-005 [US-002] [FR-005]: Given a validated packet, when its `paths` and `cause` are lifted through `toIntakeSeed` into an `fsgg.coord.intake/v1` draft, then `Intake.validate` accepts that draft.
- AC-006 [US-001] [FR-006]: Given `packet validate <file>`, when the file holds a malformed packet then the process exits non-zero having printed one finding per offending field, and when it holds a valid packet then it exits zero having printed an `fsgg.coord.packet-result/v1` document.
- AC-007 [US-003] [FR-007]: Given the shipped skill text, when a reader looks for the packet contract, then they find the JSON form, the sentence naming what parses it, and an explicit statement that free-prose packets already posted remain valid.

## Functional Requirements

- FR-001: `FindingPacket.parse` MUST reject any document carrying a field that is not one of the nine named fields, naming the offending field. (Stories: US-001; Acceptance: AC-001)
- FR-002: `FindingPacket.parse` MUST reject the string `"none"` and JSON `null` in any of `redToday`, `derivedBy` or `classRow`, with a message naming the three-case object shape that was meant. (Stories: US-002; Acceptance: AC-002)
- FR-003: `FindingPacket.parse` and `FindingPacket.validate` MUST return a typed `Finding list` rather than throwing, for every input including unreadable JSON. (Stories: US-001; Acceptance: AC-003)
- FR-004: The validator MUST reject a non-empty, named set of real packets already filed on `.github#2691` and `.github#2687`, and MUST accept a named set of others, so the check is falsifiable rather than universally green. (Stories: US-002; Acceptance: AC-004)
- FR-005: A validated packet's `paths` and `cause` MUST lift into an `fsgg.coord.intake/v1` draft that `Intake.validate` accepts, exercised as a round-trip test. (Stories: US-002; Acceptance: AC-005)
- FR-006: `packet validate` MUST exit non-zero with per-field findings on a malformed packet, and MUST emit an `fsgg.coord.packet-result/v1` document on success. (Stories: US-001; Acceptance: AC-006)
- FR-007: Packets already posted in free prose MUST remain valid and readable, the validator MUST apply forward only, and the skill text MUST say so explicitly. (Stories: US-003; Acceptance: AC-007)
- FR-008: Every sentinel case MUST carry a non-blank string — the answer, the search performed, or the reason none was performed — so that no case of the union can assert something the analyst cannot check. (Stories: US-002; Acceptance: AC-002)
- FR-009: `finder` MUST be a bare minted worker id, because `scripts/fsgg-coord say <ref> --to <worker>` is the documented way an analyst replies to a finder and it takes an id, not a sentence naming one. (Stories: US-002; Acceptance: AC-004)

## Ambiguities

The route decision on `.github#2737` named three design questions and required that this specification
settle rather than skip them. Each is recorded here with the evidence that settles it.

- AMB-001: **Strictness.** Does a malformed packet get refused at the point of writing, or accepted and flagged? Refusing is cleaner and risks losing a finding that a despawning agent had one chance to record — and that risk is measured, not hypothetical: this session lost two critics' entire findings comments to a tooling fault, recoverable only because a host relayed them from a report.
- AMB-002: **The sentinels.** The fields carrying `none` and `not established` map onto either a nullable or a tagged union, and the choice decides whether "I looked and found nothing" stays distinguishable from "I did not look" — the distinction the whole filing bar rests on.
- AMB-003: **Migration.** The registers already hold many free-form packets. Whether the validator applies to them, or only forward, is a real decision with cost either way.

## Public Or Tool-Facing Impact

- A new command verb, `packet validate <file>`, joins the coordination CLI's public surface and its rendered command contract.
- A new schema id, `fsgg.coord.finding-packet/v1`, and a new result schema id, `fsgg.coord.packet-result/v1`, become receiver-visible contracts.
- Two coordination-kit skill files change, so the packed kit manifest changes and a kit release obligation follows. `board-analyst/SKILL.md` is a digested `SKILL.md`; `pnext-item/references/findings-and-filing.md` is not, but is packed.

## Lifecycle Notes

- Next lifecycle action: `fsgg-sdd clarify --work 2737-finding-packet-schema`.
