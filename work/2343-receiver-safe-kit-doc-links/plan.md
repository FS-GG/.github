---
schemaVersion: 1
workId: 2343-receiver-safe-kit-doc-links
title: Make coordination-kit documentation links receiver-safe
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2343-receiver-safe-kit-doc-links/spec.md
sourceClarifications: work/2343-receiver-safe-kit-doc-links/clarifications.md
sourceChecklist: work/2343-receiver-safe-kit-doc-links/checklist.md
publicOrToolFacingImpact: true
---

# Make coordination-kit documentation links receiver-safe Plan

Prose status: planned

## Source Snapshot
- spec: work/2343-receiver-safe-kit-doc-links/spec.md sha256:10580afc554a4bde8470ca7fbd372d41e551959c380d3736b487a23d90c36b47 schemaVersion:1
- clarifications: work/2343-receiver-safe-kit-doc-links/clarifications.md sha256:bc8717833a7824459397d559a6bae73c0fcc654d52507f23f287f742b2629492 schemaVersion:1
- checklist: work/2343-receiver-safe-kit-doc-links/checklist.md sha256:240393e5e6de0749c7b6b380f89317294e8e74329bf0c9c4e61433364b029a32 schemaVersion:1

## Plan Scope
- Work item 2343-receiver-safe-kit-doc-links is planned from the current specification, clarification, and checklist facts.
- Requirement count: 1.
- Clarification decision count: 0.
- Checklist result count: 1.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Rewrite the two escaping relative links (in
  `.agents/skills/cross-repo-coordination/SKILL.md` and
  `.agents/skills/pnext-item/references/deep-detail.md`, mirrored byte-identically into
  `.claude/skills/...`) to absolute `https://github.com/FS-GG/.github/blob/main/docs/...` URLs,
  rather than adding `docs/coordination/receiver-proj-migration-shape.md` or
  `docs/adr/0044-generated-artifacts-are-derived-from-their-generators.md` as new
  `registry/repos.yml` `kit:` rows. The kit's unit of delivery is a skill directory
  (ADR-0063/ADR-0067); the two referenced documents are supporting rationale, not lifecycle
  prose the driver loop consumes, so shipping them would add a permanent transport obligation
  (a new digest, a new republish trigger) for reference-only reading material. The decision and
  its rationale are recorded inline next to each rewritten link.
- PD-002 [AC-001] [FR-001] complete: Add a `validate_receiver_safe_links` check to
  `scripts/check-skill-quality.py`, restricted to the `kind: skill` rows of `registry/repos.yml`'s
  `kit:` block (read via the same `--root`/YAML pattern `check-kit-published-coherence.py`'s
  `kit_sources` already uses). For every relative link inside a kit-delivered skill's `.md` files,
  under each of `ROOTS = (".claude/skills", ".agents/skills")`, the resolved target must fall
  inside one of the kit-delivered skill directories under that same root — the union a receiver
  actually materializes — not merely exist anywhere in this checkout, which is the weaker
  guarantee `validate_links` already gives and the reason this defect shipped unnoticed.

## Contract Impact
- PC-001 [PD-001] [PD-002] command report: `scripts/check-skill-quality` /
  `scripts/check-skill-quality.py --root <tree> --contract <contract>` gain one more failure mode
  (a receiver-unsafe relative link) inside their existing exit-code contract (0 clean, 1 with
  `::error::skill-quality: ...` lines on stderr); no flag, argument, or JSON shape changes. The two
  rewritten links are tool-facing prose inside `SKILL.md`/reference files already covered by
  `skill-quality.yml`'s existing path filters (`.claude/skills/**`, `.agents/skills/**`,
  `scripts/check-skill-quality.py`) — no new CI wiring is required.

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PC-001] semanticTest: `scripts/check-skill-quality` passes over the
  fixed catalog (both roots byte-identical, no receiver-unsafe link). `tests/skill-quality/run.sh`
  gains one `expect_rejection` case that injects a link escaping the kit-delivered directory set
  into a materialized kit skill and asserts the new check fails it with an identifying message —
  the gate-inversion evidence this change's own gate must ship with, per pnext-item §3.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: Plan schemaVersion 1 is accepted; unsupported plan schemas diagnose
  before write. No receiver migration step is required by this plan: merging to `main` lets the
  existing `kit-auto-publish` workflow (push-to-main + hourly schedule) tag and publish the next
  `FS.GG.Kit` version carrying the corrected bytes; each receiver's own Renovate/kit-materialize
  loop then re-pins on its own schedule. This plan does not touch any receiver checkout directly.

## Generated View Impact
- GV-001 [PD-001] [PD-002] workModel: readiness/2343-receiver-safe-kit-doc-links/work-model.json
  refreshes from current plan sources or reports staleGeneratedView. No other generated projection
  (`scripts/generate-projections`, `scripts/generate-driver-manifest`) depends on the two edited
  reference links or on `check-skill-quality.py`'s link-validation logic.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2343-receiver-safe-kit-doc-links`.
