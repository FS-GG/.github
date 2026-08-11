---
schemaVersion: 1
workId: 2366-product-tree-feedback-report-materialization
title: Product Tree Feedback Report Materialization
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Product Tree Feedback Report Materialization Specification

Prose status: specified

## User Value
A worker whose product tree materialized fs-gg-sdd-* but not fs-gg-feedback-report gets a diagnosis naming the gap as a partial product materialization instead of the wrong-tree stop condition, and the registry gate that could have caught the class of drift before it reached a scaffolded product now exists and is proven able to fail.

## Scope
- SB-001: A new cross-reference-implication check inside `scripts/fsgg-skill-registry-check`, operating purely on `registry/skills.yml` plus the skill source trees already reachable at `--repos-root` (including `.github`'s own checkout for `scope: driver`/`operator` rows) — no live scaffolded product tree is required.
- SB-002: `.claude/skills/work-roadmap/references/deep-detail.md` and `.claude/skills/work-board/references/deep-detail.md`, plus their byte-identical `.agents/` twins, and `feedback-contract.md` in the same four locations (a short cross-pointer only).
- SB-003: `registry/repos.yml`'s `sir` row (and its preceding comment block), documented in place.
- SB-004: `registry/skills.yml` itself is read, not restructured — schemaVersion stays 3; no new row field is introduced, because the check derives everything it needs from `materializes-when` and `source:`, already present on every row.

## Non-Goals
- SB-005: Do not change the `FS.GG.SDD` CLI, any scaffold/template tooling, or anything under a different repository's checkout — those are outside this repo's `Paths:` and, per the item's own delivery-route rationale, staged consumer-side work.
- SB-006: Do not re-materialize the already-scaffolded `EHotwagner/S.I.R.` tree; that tree is not touched, referenced only as motivating evidence.
- SB-007: Do not extend `scripts/repos.sh` or `scripts/repos-audit.sh` to mechanically grade `registry/repos.yml`'s new `sir`-row documentation against `registry/skills.yml` — both scripts are outside this item's `Paths:`. The gap is filed as a distinct follow-up rather than widened into here.
- SB-008: Do not add a `requires:`/`co-materializes:` field to `registry/skills.yml` rows — the two real rows this item concerns (`fs-gg-feedback-report`, `work-roadmap`/`work-board`) already both declare `materializes-when: always`, so the missing piece is a gate that reads and asserts that fact, not new schema to declare it a second time.

## User Stories
- US-001 (P1): As a worker driving `work-roadmap`/`work-board` in a product tree that materialized `fs-gg-sdd-*` but not `fs-gg-feedback-report`, I want the tree-classification guidance to name my situation as a partial product materialization rather than telling me to stop because I am in the wrong tree, so I do not misdiagnose a real, working product tree as unsupported.
- US-002 (P2): As the `.github` registry maintainer, I want a gate that fails when a materialized skill's text references a sibling skill path whose `materializes-when` predicate is not implied by the referencing skill's own predicate, so a future edit that breaks this invariant (e.g. narrowing `fs-gg-feedback-report`'s predicate while `work-roadmap` stays `always`) is caught in `.github`'s own CI before it ever reaches a scaffolded product.
- US-003 (P2): As a reader of `registry/repos.yml`, I want the `sir` row to say plainly that `receives: []` covers only org fabrics and not per-scaffold skill materialization, so I do not conclude a 26-skill delivery contradicts a zero-fabric roster row.

## Acceptance Scenarios
- AC-001 [US-002] [FR-001]: Given the registry as it exists today (every real cross-reference implied), when `scripts/fsgg-skill-registry-check` runs the new check, then it exits zero and reports the check's population (ids scanned, references found, references verified).
- AC-002 [US-002] [FR-001]: Given a constructed fixture where an `always`-materializing skill's body references a sibling registered id whose `materializes-when` is narrower than `always` (or the id is unregistered), when the check runs, then it exits non-zero and names the referencing id, the referenced id, and the two predicates (or "unregistered").
- AC-003 [US-002] [FR-001]: Given a skill row whose own `materializes-when` is unsatisfiable (e.g. `false`, as `lane-steward` is today), when its body references a sibling with an unrelated or unregistered id, then the check does not flag it — an unreachable predicate can never surface the referenced-but-absent failure mode, so flagging it would be a false positive over live content (`lane-steward` → `pnext-item`, which is not a registered row).
- AC-004 [US-001] [FR-002]: Given a tree that materializes `fs-gg-sdd-*` but not `fs-gg-feedback-report`, when a worker reads `deep-detail.md`'s "Where this runs" section, then it is classified as a partial product materialization (not "the wrong tree") with a named, non-blocking remedy — continue the loop, record a zero-event reason, do not fabricate an out-of-workspace substitute tool path, and file/dedupe one finding rather than rediscovering the gap every cycle.
- AC-005 [US-001] [FR-002]: Given the same partial-materialization tree, when a worker reads `feedback-contract.md` in isolation (without first reading `deep-detail.md`), then it carries a short pointer to the partial-materialization guidance so the absent-tool case is not reinterpreted as "wrong tree" from that narrower context.
- AC-006 [US-003] [FR-003]: Given `registry/repos.yml`'s `sir` row, when a reader reads it (row plus comment), then it states explicitly that `receives: []` is scoped to the org-fabric vocabulary (`labels, coordination-kit, build-config, lockfile-sync, contract-coherence, skill-union`) and that per-scaffold skill materialization is a separate, already-governed axis (`registry/skills.yml`), naming the specific gap (no script in this item's `Paths:` mechanically grades the two against each other yet) as a named follow-up rather than a silent omission.

## Functional Requirements
- FR-001: `scripts/fsgg-skill-registry-check` gains a check that, for every registry row whose `materializes-when` is satisfiable, scans the row's skill directory (`SKILL.md` plus every file under its `references/` subtree) for path-shaped references (`\.(?:agents|claude)/skills/([a-z][a-z0-9-]*)/`) to another id; excludes self-references; for each remaining reference, resolves the target id against the registry and, if found, asserts the referencing row's `materializes-when` implies the target row's `materializes-when` (checked by enumeration over the domain of parameter values that actually appear across the registry's own `materializes-when` corpus, plus the empty/unset value), and if not found, fails as an unregistered-sibling reference; the check exits non-zero with a named finding on any failure and zero otherwise. (Stories: US-002; Acceptance: AC-001, AC-002, AC-003)
- FR-002: `deep-detail.md` in both `work-roadmap` and `work-board` (both `.claude/` and `.agents/` roots) is revised to name three tree states — kit source, full product tree, partial product materialization — with the partial state's remedy spelled out (continue, zero-event reason, no substitute path, one dedupe-worthy finding), and `feedback-contract.md` in the same four locations gets a short cross-pointer to that guidance. (Stories: US-001; Acceptance: AC-004, AC-005)
- FR-003: `registry/repos.yml`'s `sir` row (and its surrounding comment) documents the distinction between the `receives:` org-fabric axis and the `registry/skills.yml` skill-materialization axis, and names the absence of mechanical grading between them as a follow-up rather than leaving it unstated. (Stories: US-003; Acceptance: AC-006)

## Ambiguities
No material ambiguities recorded. One judgment call is recorded as DEC-001 in clarifications: the implication check uses domain enumeration over corpus-observed parameter values rather than a full symbolic solver, which is sound for the registry's current shape (a single `profile` parameter with five observed values) and is documented as a bounded, not universal, guarantee.

## Public Or Tool-Facing Impact
- `scripts/fsgg-skill-registry-check` is invoked by consumer-repo CI (its own docstring: "the FS.GG.Templates composition gate is the first caller"); a new check class changes its exit-code/finding surface additively (a new finding id, no existing check's behavior changes) but can turn an existing green CI run red if that consumer's registry snapshot has an unimplied cross-reference — this is the intended, in-scope behavior change.
- `.claude/skills/work-roadmap/references/deep-detail.md`, `.claude/skills/work-board/references/deep-detail.md`, and their `.agents/` twins are agent-facing guidance materialized into every scaffolded product tree that receives these drivers; the wording change is read directly by worker agents mid-task.
- `registry/repos.yml` is read by `scripts/repos.sh`/`scripts/repos-audit.sh`, both outside this item's `Paths:`; the new `sir`-row prose is inert to those scripts (an unvalidated, unrejected field/comment) until a follow-up item wires grading.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2366-product-tree-feedback-report-materialization`.
