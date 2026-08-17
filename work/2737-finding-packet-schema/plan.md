---
schemaVersion: 1
workId: 2737-finding-packet-schema
title: Finding Packet Schema
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2737-finding-packet-schema/spec.md
sourceClarifications: work/2737-finding-packet-schema/clarifications.md
sourceChecklist: work/2737-finding-packet-schema/checklist.md
publicOrToolFacingImpact: true
---

# Finding Packet Schema Plan

Prose status: planned

## Source Snapshot
- spec: work/2737-finding-packet-schema/spec.md sha256:e3a28b2e54e9f5dddc26af53eee42c4a5fdc85e13c9b0e0fac20a15940db6184 schemaVersion:1
- clarifications: work/2737-finding-packet-schema/clarifications.md sha256:fae0f44a43fb82497fe400ea21f436a5dc28e40718a6dfaf69a6ffd6a6b0668f schemaVersion:1
- checklist: work/2737-finding-packet-schema/checklist.md sha256:f8beb058f69c17878e142124f5d8e6606f8204bd375533a4a7884a1412a897f4 schemaVersion:1

## Plan Scope
- Work item 2737-finding-packet-schema is planned from the current specification, clarification, and checklist facts.
- Requirement count: 9.
- Clarification decision count: 3.
- Checklist result count: 9.

### The schema, in full

`fsgg.coord.finding-packet/v1`, posted as a fenced `json` block under the existing
`<!-- fsgg:finding-packet -->` anchor. Nine fields, closed set, all required:

```json
{
  "schema":     "fsgg.coord.finding-packet/v1",
  "surface":    "src/FS.GG.Coord.Cli/DeliveryRouteApplication.fs",
  "cause":      { "established": "the verb exists in the application layer and was never wired into the command surface" },
  "redToday":   { "found": "`route validate` is unreachable; Options.fs:1963 routes only show and record" },
  "derivedBy":  { "notSearched": "an adjudicator should check whether a gate already derives 'an application verb with no command-surface dispatch'" },
  "classRow":   { "notSearched": "this may be evidence on a wiring/coverage class row rather than a row of its own" },
  "whyNotHere": "no claim, no lane, and the fix is engine source the pass did not declare",
  "paths":      ["src/FS.GG.Coord.Cli/Options.fs", "src/FS.GG.Coord.Cli/DeliveryRouteApplication.fs"],
  "finder":     "merlin-efd3"
}
```

That example is not invented: it is `.github#2691` comment `5304198465`, lifted field by field.

### Where the decoder lives, and why not where `intake` puts it

`intake` splits its decoder from its validator: `IntakeApplication.readDraft` (CLI) does JSON →
`Intake.Draft` and returns a single joined `string`, while `Intake.validate` (Core) does
`Draft` → typed `Finding list`. This item deliberately does **not** copy that split, and the departure
is worth stating because `.github#2737`'s body names `readDraft` as "the standard to copy".

Copy its *shape* — closed field set, unknown-field rejection, per-field errors — but not its *siting*.
Three of this item's requirements are decode-time facts: FR-001 (unknown fields), FR-002 (`"none"` and
`null` in a union position), FR-003 (never throws, including on unreadable JSON). Under `intake`'s
split, all three would live in the CLI, where they can only be tested through a process boundary, and
would return untyped strings in violation of FR-003.

Parsing a string is pure. `FindingPacket.parse: string -> Result<Packet, Finding list>` therefore sits
in Core with the validator, reads no file, and the CLI keeps only the genuinely impure half —
`File.ReadAllText` and printing. `System.Text.Json` is in the `net10.0` shared framework, so this adds
no `PackageReference` and leaves `packages.lock.json` unchanged.

### The fixture corpus, and the packets the validator REJECTS

`.github#2737`'s route decision requires that this specification state which real, previously-filed
packets the validator rejects, and that the set not be empty — because a validator that accepts every
packet ever written cannot fail, which is `.github#266`'s exact shape arriving in the machinery built
to serve `#266`'s own register.

Fixtures are drawn from the live registers, faithfully lifted: content preserved, nothing invented,
and where a packet names a bare worker id inside a prose sentence that id is lifted rather than the
sentence. Every fixture records its source comment id so a reader can check the lift.

**Rejected — real comments, rejected after a faithful lift, for content rather than formatting:**

- RJ-001 — `.github#2691` comment `5304189944` ("Pending packet 1 — `Review.repairAssertionValid` carries the same unreachable conjunct pair"). It proposes **no declaration at all**; it says of itself *"its evidence stays in the PR comment; this entry is the address, not a copy."* A faithful lift cannot invent `paths`. Finding: `paths is required`.
- RJ-002 — `.github#2691` comment `5309266535` ("Release debt with no destination"). A request for a release act, not a finding. It carries **no `redToday` in any form** — nothing is red and it never says so — and proposes no `paths` of its own. Findings: `redToday is required`, `paths is required`. This rejection is the filing bar's own scope limit made mechanical: the bar governs findings, and a decision is not required to be red.
- RJ-003 — `.github#2691` comment `5306816009`, whose first line reads *"Increment on an EXISTING packet — not a new finding"*. It carries the anchor, so every search for packets returns it, and it is not a packet: no `surface`, no `whyNotHere`, no `paths`, no `derivedBy`. It stands for the **24 of 106** anchor-carrying comments that are increments or adjudications rather than packets. Findings: four required fields.
- RJ-004 — `.github#2691` comment `5311301051`, the corpus's strongest packet by a prose reading. It answers surface, cause, red-today, class-row, why-not-here, paths and finder, and still never says whether a gate already derives its condition. That is **test 2 of the filing bar, unanswered** — the single most valuable thing this rejection catches, and it is caught in the best packet on the board rather than the worst.
- RJ-005 — the corpus-wide **shape** rejection. Not one comment on either register carries a parseable JSON block, so every packet writes its sentinel-bearing fields as prose or as the literal word `none`; a lift that carries `none` across as `"redToday": "none"` — or as the `null` `.github#2737`'s body first proposed — is rejected with a finding naming the three-case object shape. This is FR-002 exercised against the form the corpus actually uses, not against a synthetic string.

**Accepted — real comments that lift cleanly, so the validator is not merely reject-everything:**

- AJ-001 — `.github#2691` comment `5304198465`. Lifts to a complete packet, and **cannot be lifted faithfully under a nullable**: its `derivedBy` and `classRow` are both `notSearched`, stated in the comment itself. This fixture is DEC-002's evidence, not an illustration of it.
- AJ-002 — `.github#2691` comment `5307639382`. It answers every field under an explicit heading and exercises the union's middle case twice: `redToday` and `derivedBy` are both `searchedNotFound`, each carrying the search actually run, and `classRow` is `found` (`.github#2648`, via its criterion 6).
- AJ-003 — `.github#2691` comment `5307153964` — the packet behind `.github#2735`. Answers all nine; its `classRow` is `searchedNotFound` carrying the finder's own note that the dedupe was partial, which a nullable could not have recorded either.

Both sets are non-empty, and both are required to be so: a rejected set that is empty means the check
cannot fail, and an accepted set that is empty means it cannot discriminate.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: `FindingPacket.parse` enumerates the document's properties against a closed nine-name set and returns a `Finding` naming the first offending field, before any field is decoded.
- PD-002 [AC-002] [FR-002] complete: A union-position value that is a string, a `null`, a non-object, an object with no recognised key, or an object with more than one key is rejected with a finding naming `found` / `searchedNotFound` / `notSearched`.
- PD-003 [AC-003] [FR-003] complete: `parse` catches `JsonException` at the decode boundary and returns it as a `Finding` on the pseudo-field `document`; no other exception can arise because no IO is performed.
- PD-004 [AC-004] [FR-004] complete: The fixture corpus ships as files under `tests/FS.GG.Coord.Core.Tests/fixtures/`, each named for its source comment id, with RJ-001..RJ-004 asserted to fail on named fields and AJ-001..AJ-003 asserted to validate.
- PD-005 [AC-005] [FR-005] complete: `FindingPacket.toIntakeSeed` lifts `surface` to `observed`, `paths` to `paths`, and `cause` to `rootCause` — rendering `notEstablished` in `.github#1858`'s form rather than dropping it — and a round-trip test builds an `Intake.Draft` from a seed and asserts `Intake.validate` accepts it.
- PD-006 [AC-006] [FR-006] complete: `PacketApplication.run` performs `File.ReadAllText`, calls `parse` then `validate`, prints one finding per line to stderr and exits red, or prints the `fsgg.coord.packet-result/v1` document and exits green.
- PD-007 [AC-007] [FR-007] complete: `board-analyst/SKILL.md` and `pnext-item/references/findings-and-filing.md` carry the JSON form and an explicit forward-only sentence; both `.claude` and `.agents` roots move byte-for-byte.
- PD-008 [AC-002] [FR-008] complete: Each union case's payload is checked non-blank by `validate`, so no case can assert a search the analyst cannot inspect.
- PD-009 [AC-004] [FR-009] complete: `finder` is matched against a bare-worker-id pattern, because `scripts/fsgg-coord say <ref> --to <worker>` — the documented reply path — takes an id and not a sentence naming one.

## Contract Impact
- PC-001 [PD-001] schema: `fsgg.coord.finding-packet/v1`'s nine-name field set is a **closed** receiver-visible contract, so every future field is a breaking change to every producer. That is accepted deliberately: an open set would let a finder answer a question the analyst never asked while silently omitting one it did, which is the failure this item exists to remove. Growth is a `/v2`, never a tenth name in `/v1`.
- PC-002 [PD-006] command surface: `packet validate <file>` joins the coordination CLI's public verb set and its rendered `command-contract`, additively; no existing verb changes.
- PC-003 [PD-001] schema: `fsgg.coord.finding-packet/v1` and `fsgg.coord.packet-result/v1` become receiver-visible schema ids. Both are new; neither supersedes an existing id.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: The closed field set is verified by a test that adds **one** unknown property to an otherwise-valid packet and asserts the exact offending name is reported — not merely that parsing failed. A test asserting only failure would pass against a parser that rejects everything, which is the control failure VO-004 guards; naming the field is what distinguishes the two.
- VO-002 [PD-004] [PC-003] inversionEvidence: **Every assertion added here ships with evidence it can fail.** For each of RJ-001..RJ-004 the corresponding required-field or shape check is inverted in the production source, the suite is re-run, and the observed red is recorded with the exact mutation. An inversion that leaves the suite green is a defect in the test, not a pass.
- VO-003 [PD-004] [PC-003] inversionApplied: Each inversion is confirmed **by `git diff`** to have actually modified the file before the suite is run. A scripted inversion that silently fails to apply reports exactly like one that applied and was killed; this session measured that fault, and the `diff` is what separates the two.
- VO-004 [PD-004] [PC-003] instrumentControl: The accepted set AJ-001..AJ-003 is the control for the rejected set. A suite in which every fixture is rejected proves nothing about the validator's discrimination, so the accepted assertions must be shown to fail when the packet is damaged.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] rejectUnknownVersion: A document whose `schema` is not exactly `fsgg.coord.finding-packet/v1` is rejected naming the expected value, rather than being decoded on a best-effort basis. A packet is read once, by an analyst deciding whether to spend a row on it; silently accepting an unrecognised version would let a `/v2` producer's differently-meant fields be read under `/v1` semantics, and a misread packet is worse than a refused one because nothing signals the misreading.
- PM-002 [PC-003] forwardOnly: Per DEC-003 the validator applies forward only. The 106 anchored comments on `.github#2691` and `.github#2687` are not rewritten and not invalidated, and the skill text says so in terms.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2737-finding-packet-schema/work-model.json` and `analysis.json` are this package's own generated views and are committed with it, because `Client.sddEvidenceErrors` reads `analysis.json`'s `workId` and `status` to answer `sddPackageReady` on the item's delivery-route receipt. They are regenerated by re-running the lifecycle, never hand-edited; a hand-edited `status: implementationReady` would be the archetypal check that cannot fail.
- GV-002 [PD-007] skillManifest: editing `.claude/skills/**` stales `registry/coordination-kit-skill-manifest.json` (pnext-item's per-file rows) and `registry/driver-skill-manifest.json` (board-analyst's `sha256` and `tree-sha256`). Both are listed by `scripts/generated-paths`, so `verify-paths` exempts them and `widen` refuses them; they are regenerated in the PR with `scripts/generate-driver-manifest --write`. `registry/repos.lock` does **not** move: `scripts/repos.sh` digests a skill directory as its `SKILL.md` alone, and no kit `SKILL.md` changes here.

## Accepted Deferrals
- CR-003: Automatic extraction of a packet from a live PR or issue comment is deferred — an IO concern belonging with the analyst's own metered read, not with this pure core.
- CR-004: Retrofitting the registers to the JSON form is deferred indefinitely, per DEC-003.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- `.github#2735` should land first if scheduling allows, so this row can be exercised through the real `intake apply` path. It is **not** a blocking edge and no `Blocked by` is recorded; the lanes are disjoint.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2737-finding-packet-schema`.
