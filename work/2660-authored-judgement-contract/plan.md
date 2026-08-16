---
schemaVersion: 1
workId: 2660-authored-judgement-contract
title: Authored Judgement Contract
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2660-authored-judgement-contract/spec.md
sourceClarifications: work/2660-authored-judgement-contract/clarifications.md
sourceChecklist: work/2660-authored-judgement-contract/checklist.md
publicOrToolFacingImpact: true
---

# Authored Judgement Contract Plan

Prose status: planned

## Source Snapshot
- spec: work/2660-authored-judgement-contract/spec.md sha256:29c06b9daa7af9393c5ce70b36be8d367b4fc8165a621b63e4e35c0f08c02205 schemaVersion:1
- clarifications: work/2660-authored-judgement-contract/clarifications.md sha256:29b9f46e0e9691aa91461c45a99b9668a3920d37ebfbdd5edd9ab5d4c02ada24 schemaVersion:1
- checklist: work/2660-authored-judgement-contract/checklist.md sha256:293c1d5e0dd2f3bd868a9e9fa28819f24861753fef7d10e04284462ff55636b4 schemaVersion:1

## Plan Scope
- Work item 2660-authored-judgement-contract is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 0.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Adjudicate each of the fifteen headings `b84423e7` deleted under exactly one of four dispositions — `restore`, `restore-condensed`, `superseded-by-ledger`, `already-restored` — and record the table with its reasons in `work/2660-authored-judgement-contract/adjudication.md`. The test applied to each heading is the one the row states: can `fsgg-coord` seal this fact, or is it a judgement a critic authors? Line count is not the measure; a section may be restored to a third of its length and still carry its whole contract.
- PD-002 [AC-001] [FR-002] complete: Restore the adjudicated sections to `.claude/skills/pnext-item/references/independent-review.md` and copy that file byte-for-byte to the `.agents` mirror rather than editing both, because `review-round-contract.py` compares the two roots and a hand-applied second edit is how they drift. **Restoration is not complete until each restored section is CITED** from a non-exempt tracked document, because an uncited section is one the FR-004 gate cannot see deleted — restoring it to a home no gate watches reproduces this item's own defect. Round 1 of review measured exactly that escape for `### Issue and pull-request body evidence`: it was restored uncited, and deleting it left both the checker and its fixture suite green. Verify citedness by mutation — delete each restored section and confirm the gate reds — never by reading the diff.
- PD-003 [AC-001] [FR-003] complete: Reintroduce no v1 prose decision authority. `review-round-contract.py`'s `retired_parts` forbids all five v1 marker names in every `.md` under `.agents` and `.claude`, so the restored prose names the v2 record kinds (`escalation`, `repair-phase`, `acceptance`) and never the retired marker strings. The v1 marker templates, the `key: value` field grammar, and the `## Disposition and repair bounds` literal list are the content the ledger genuinely superseded and stay deleted.
- PD-004 [AC-001] [FR-004] complete: Extend `check-prose-citations.py` with a second, independent predicate over Markdown inline link fragments. Resolve the destination relative to the citing file, require the target to be tracked, derive the target's anchors from its ATX headings using GitHub's slug rule, and red when the fragment matches none. Keep the existing `path:line` predicate untouched so the file-existence corpus and its exit codes do not move.
- PD-005 [AC-001] [FR-005] complete: Bound the grammar to `](dest.md#fragment)` — a Markdown inline link, a repository-local `.md` destination, an explicit fragment. Prose references such as "the numbered steps of X" stay out of scope by construction, which the row asks for in terms; state the bound in the gate docstring and in a new ADR section, and state the residual limit honestly: a restored section that nothing cites is still deletable silently, so the restoration earns its protection by being cited.
- PD-006 [AC-001] [FR-006] complete: Give the fragment predicate its own non-vacuity accounting. A zero-fragment corpus returns `NO_VERDICT` rather than green, exactly as the existing `path:line` leg does, so "no dangling section citations" and "examined no section citations" do not share an exit code.
- PD-007 [AC-001] [FR-007] complete: Restore `### Repair phase` under that exact heading so the four live `independent-review.md#repair-phase` links in the `drive-board-best`, `drive-board-normal` and their `.agents` mirrors resolve; add fragment citations from `pnext-item/SKILL.md` into the other restored sections so the new gate guards them too. Measured pre-fix: 23 local fragment links, 4 dangling, all four naming `#repair-phase`.

## Contract Impact
- PC-001 [PD-001] command report: `fsgg-sdd plan`, `work/2660-authored-judgement-contract/plan.md`, and command-report JSON are tool-facing and compatibility-preserving.
- PC-002 [PD-004] gate contract: `check-prose-citations.py` gains one exit-code-compatible predicate. Exit 0/1/3 keep their meanings; the workflow's four-branch exit-code fan-out in `.github/workflows/prose-citations.yml` needs no change.
- PC-003 [PD-002] review contract: the restored prose is packed kit content consumed org-wide, so the change carries a kit-release obligation into a release frontier the route receipt records as wedged.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: `tests/skill-quality/review-round-contract.py` stays green, proving the mirrors are byte-identical and no retired v1 authority returned.
- VO-002 [PD-004] [PC-002] semanticTest: `tests/prose-citations/run.sh` gains fixture legs for the fragment predicate — a resolving fragment, a dangling fragment, an untracked fragment target, and an empty fragment corpus.
- VO-003 [PD-007] [PC-002] semanticTest: gate-inversion evidence for the modified gate — subject mutation, non-vacuity leg, and the workflow/job/invocation line that runs it, with the trigger's path filter evaluated against this diff.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-002] diagnoseOnly: No migration is owed. The extended gate adds a predicate over a grammar no live document currently violates except the four `#repair-phase` links this change itself repairs, so nothing outside the touch-set must be rewritten to keep CI green. Had the measurement found dangling fragments in documents this row does not own, the posture would have been a staged opt-in rather than an immediate red; it found none, so the gate arms in one step. `Verification:` measured 23 local `.md` fragment links across 441 live subjects, 4 dangling, all four naming `#repair-phase`.

## Generated View Impact
- GV-001 [PD-002] workModel: Two generated views move and both are mechanical rather than authored. `readiness/2660-authored-judgement-contract/work-model.json` refreshes from these plan sources or reports `staleGeneratedView`. Separately, `registry/repos.lock` digests `.claude/skills/pnext-item/SKILL.md`, so editing packed kit source stales it and `repos-registry-selftest` reds `main` until `scripts/repos.sh relock` regenerates it. That lock file is expected drift declared in the PR, never reserved in the touch-set — it is a generated CI-gated artifact, so a collision in it is a rebase and not a decision.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2660-authored-judgement-contract`.
