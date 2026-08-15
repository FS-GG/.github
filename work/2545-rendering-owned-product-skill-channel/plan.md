---
schemaVersion: 1
workId: 2545-rendering-owned-product-skill-channel
title: Rendering Owned Product Skill Channel
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2545-rendering-owned-product-skill-channel/spec.md
sourceClarifications: work/2545-rendering-owned-product-skill-channel/clarifications.md
sourceChecklist: work/2545-rendering-owned-product-skill-channel/checklist.md
publicOrToolFacingImpact: true
---

# Rendering Owned Product Skill Channel Plan

Prose status: planned

## Source Snapshot
- spec: work/2545-rendering-owned-product-skill-channel/spec.md sha256:838cbe3590ae3f867d2c4f85c6d16c4e94a91f51828c98fcb91c4df07054fd50 schemaVersion:1
- clarifications: work/2545-rendering-owned-product-skill-channel/clarifications.md sha256:768db4d522c67d0eaba249842d1a646de13b2c6eee4aed9493045e6c1ca79745 schemaVersion:1
- checklist: work/2545-rendering-owned-product-skill-channel/checklist.md sha256:21509f554867b43ef765036e9a1f2ff83148ff994f6dc480a65b9521682e03b1 schemaVersion:1

## Plan Scope
- Work item 2545-rendering-owned-product-skill-channel is planned from the current specification, clarification, and checklist facts.
- Requirement count: 10.
- Clarification decision count: 3.
- Checklist result count: 10.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Author `registry/skills.delivery-channels.yml` with one entry per `(owner, scope)` class, each carrying exactly one disposition — `delivered`, `provider-scoped`, `withheld`, or `gap` — plus that disposition's required fields. The vocabulary is four-valued, not two-valued, because `fs-gg-feedback-report` HAS a channel and lacks only reach; a has-channel/no-channel file would let this item's own subject be declared green. Six classes exist today; the file is authored, not generated, because the channel a class rides is a semantic declaration with no upstream to derive from — ADR-0058 clause 2 keeps the fail-closed gate on exactly that kind of field.
- PD-002 [AC-002] [FR-002] complete: Implement closure in the arm as `classes(registry) − classes(declaration)`. The class set is DERIVED from `registry/skills.yml` on every run, never restated in the arm, so a new owner or a new scope value reds the gate without any code change. This is the property all three ADR-0063 instances lacked.
- PD-003 [AC-003] [FR-003] complete: Implement the reverse direction, `classes(declaration) − classes(registry)`, as a separate finding kind. Without it the declaration rots into a restatement of a class set that has moved on — the exact failure mode ADR-0058 exists to prevent, reintroduced by the fix for it.
- PD-004 [AC-004] [FR-004] complete: Enforce each disposition's required fields, and validate `tracked-by` against `^[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._-]*#[1-9][0-9]*$` — full `owner/repo#n` only. The board's own `<repo>#<n>` shorthand is deliberately rejected: `.github#2107` measured that GitHub's closing-keyword grammar does not parse it, and a reference a reader cannot resolve names nobody. A `provider-scoped` entry must carry exactly one of `tracked-by` or `accepted`, so shortfall in reach is never silent. Liveness of the referenced issue is NOT checked; that would need the network PD-005 forbids.
- PD-005 [AC-005] [FR-005] complete: Keep the arm offline. It reads the two YAML files and nothing else — no `repos_root` access, no clone, no token. This is what lets it run in the `fixture` job as well as the `registry-coherence` job, and it is why a fixture can exercise it without producer trees.
- PD-006 [AC-006] [FR-006] complete: Add the fixture to `tests/skill-registry/run.sh` as a self-contained temp-tree case per finding kind (green, missing-class, dead-entry, malformed `tracked-by`, both-channel-and-gap, neither). Record the gate-inversion mutation and observed red in `work/.../verification/verification-evidence.md`, produced by a runnable `verification/run-checks.sh` rather than asserted in prose.
- PD-007 [AC-007] [FR-007] complete: Record the route decision in `spec.md` § Route decision, not in a new ADR. ADR-0063 states "This ADR builds nothing" and delegates transport per class to coordination rows; adding an ADR to re-decide what ADR-0063 already decided would itself be a restatement. AMB-003 records this and invites a reviewer to disagree against a stated position.
- PD-008 [AC-008] [FR-008] complete: Answer acceptance criterion 4 for all 18 Rendering-owned rows in one table keyed by measured predicate, and state explicitly that "in scope of the channel" and "detectable today" are different columns — the item's own framing conflates them, and the conflation is what would justify narrowing the channel to one row.
- PD-009 [AC-009] [FR-009] complete: File the byte-owner row (FS.GG.Rendering#1240) and the consumer row (FS.GG.SDD#864), plus the `.github`-side follow-up (#2639) for the two records that cannot be written until the package exists on the feed. Wire `Blocked by` on the Projects v2 FIELD, not as a body line.
- PD-010 [AC-010] [FR-010] complete: Add `registry/skills.delivery-channels.yml` to BOTH the `pull_request` and `push: main` path filters of `.github/workflows/skill-registry-coherence.yml`. `.github#1606` measured what one-sided or absent filter entries cost: the gate went stale in silence and the red surfaced on an unrelated PR.

## Contract Impact
- PC-001 [PD-001] command report: `registry/skills.delivery-channels.yml` is a NEW `.github`-owned file. It is deliberately not a new key in `registry/skills.yml`, whose shape is the `registry-schema` contract (owner `sdd`, consumers `[github]`) read by FS.GG.SDD's typed `Fsgg.Registry` validator — so no cross-repo contract surface changes, and `skill-registry-autofix.yml`, which rewrites `registry/skills.yml` unattended, never touches the new file. `scripts/fsgg-skill-registry-check` gains one finding `check` value, `delivery-channel`; its CLI surface, exit codes, and `--write`/`--baseline-registry` behaviour are unchanged.

## Verification Obligations
- VO-001 [PD-002] [PC-001] semanticTest: The closure arm reports a finding for a class present in the registry and absent from the declaration — proved by a temp-tree fixture that adds a row with a new owner, not by asserting the code path exists.
- VO-002 [PD-003] [PC-001] semanticTest: The dead-entry arm reports a finding for a declaration entry matching no registry row.
- VO-003 [PD-004] [PC-001] semanticTest: A `tracked-by` that is absent, is board shorthand (`repo#n`), or is prose reports a finding; a well-formed `owner/repo#n` does not.
- VO-004 [PD-001] [PC-001] semanticTest: An entry declaring no disposition, one outside the vocabulary, or a `provider-scoped` entry carrying both `tracked-by` and `accepted` or neither, reports a finding — an entry that says two things says nothing accountable.
- VO-005 [PD-005] [PC-001] semanticTest: The arm reaches its verdict with no producer checkout present, proved by running the fixture with an empty `--repos-root`.
- VO-006 [PD-006] [PC-001] gateInversion: Removing the `fs-gg-rendering` / `product` entry from the shipped declaration makes the real gate red over the REAL registry, naming that class and its 18 rows. The mutation and the observed red are recorded verbatim.
- VO-007 [PD-010] [PC-001] semanticTest: The workflow's `pull_request` and `push` filters both select `registry/skills.delivery-channels.yml`, asserted from the workflow file rather than from memory.
- VO-008 [PD-001] [PC-001] semanticTest: The shipped `registry/skills.delivery-channels.yml` is green against the shipped `registry/skills.yml` — the gate this item adds passes on the tree this item lands.

## Performance Intent
No performance intent is declared for this work item. The arm is two YAML reads and a set difference over 63 rows; it adds no measurable time to a job that already clones six producer repositories.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: The declaration carries `schemaVersion: 1`. The arm refuses an unknown `schemaVersion` with a finding rather than parsing it optimistically, so a future shape change cannot be read as today's shape. No existing artefact is migrated: nothing consumed this file before it existed.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2545-rendering-owned-product-skill-channel/work-model.json` refreshes from current plan sources or reports staleGeneratedView. `registry/skills.delivery-channels.yml` is NOT a generated artefact and is deliberately absent from `scripts/generated-paths`, so `verify-paths` does not subtract it and it must stay in the item's declared `Paths:`.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2545-rendering-owned-product-skill-channel`.
