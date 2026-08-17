---
schemaVersion: 1
workId: 2752-authorship-independent-verification-efficacy
title: Authorship Independent Verification Efficacy
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2752-authorship-independent-verification-efficacy/spec.md
sourceClarifications: work/2752-authorship-independent-verification-efficacy/clarifications.md
sourceChecklist: work/2752-authorship-independent-verification-efficacy/checklist.md
publicOrToolFacingImpact: true
---

# Authorship Independent Verification Efficacy Plan

Prose status: planned

## Source Snapshot
- spec: work/2752-authorship-independent-verification-efficacy/spec.md sha256:f88759a305c33cbdce4f763bcfdaed1f2a193e6918165ba2f49bd95162145091 schemaVersion:1
- clarifications: work/2752-authorship-independent-verification-efficacy/clarifications.md sha256:11dce3e9258fe73b3c7851e53a15b59d5a7d87e0a411b8f4cda112f16ba1a7cc schemaVersion:1
- checklist: work/2752-authorship-independent-verification-efficacy/checklist.md sha256:bc5ede7c63c4f064b081747e57d04283c093321e3e658ebbe2156c395be90171 schemaVersion:1

## Plan Scope
- Work item 2752-authorship-independent-verification-efficacy is planned from the current specification, clarification, and checklist facts.
- Requirement count: 7.
- Clarification decision count: 3.
- Checklist result count: 7.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [FR-001] complete: Site the producer-agreement leg as a numbered step inside `## Gate-inversion evidence`, keyed on the gate's subject rather than on its language, and cite `tests/receiver-validate/run.sh` sections F1/F2/F2m as the reference implementation with exact line numbers; state the relation as declared-and-justified rather than as equality, because equality reddened that very leg the moment its producer became correct (`.github#2395`).
- PD-002 [AC-003] [FR-002] complete: State the witness requirement as a property of each mutation rather than of the sweep, so an aggregate count cannot satisfy it, and fold the branch-versus-predicate boundary clause into it per DEC-001 instead of adding an eighth numbered step.
- PD-003 [AC-004] [FR-003] complete: Publish the independence ladder as three ordered, individually answerable questions with the mechanical tell for each, ordered value -> key -> library, and require the review record to name the highest rung reached plus the residual, so a rung-3 disclosure cannot be presented as independence.
- PD-004 [AC-005] [FR-004] complete: State the two detection methods together with their mutual blindness as the reason neither substitutes for the other, bounded to sweep-shaped remedies so an ordinary one-mutation inversion does not inherit a second environment.
- PD-005 [AC-006] [FR-005] complete: Require both a negative and a positive control, plus each control's up-front shape assertion, and give the harness control its own named form so a harness hardwired to the answer it wants is detectable.
- PD-006 [AC-007] [FR-006] complete: Give every new requirement an explicit owed-on bound and an explicit terminal disposition, and distinguish the two `NOT_MEASURED` grades already implicit in steps 2 and 3 so a declared, evidenced boundary closes the round rather than looping (`.github#2757`).
- PD-007 [AC-008] [FR-007] complete: Ship the discrimination control set inside the contract text, with each entry cited to the other agent's measurement that fixed its verdict before this work existed, and say in terms why the provenance is load-bearing.

## Contract Impact
- PC-001 [PD-001] command report: this work changes no `fsgg-sdd` or `fsgg-coord` command surface, exit code, or report schema — its only tracked-file change outside this SDD package is one Markdown contract, so the command-report contract is untouched in both directions and no consumer needs to re-pin.
- PC-002 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] [PD-007] kit source: `.claude/skills/pnext-item/references/independent-review.md` is a content-addressed `kit:` source in `registry/repos.yml`. Editing it changes the packed `FS.GG.Kit` manifest and therefore obliges a coherent-set republish, which is DECLARED as a post-merge obligation in the machine form and NOT performed here.
- PC-003 [PD-006] review contract: the new requirements are additive to `## Gate-inversion evidence` and to the materiality list; no existing numbered step changes its meaning, and the section's own sweep bound is restated to cover the new legs so it does not become false on landing.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: There is no F# surface to smoke here, so the substitute is stated rather than skipped: re-derive every `path:line`, count and cited comment id written into the contract text against the tracked file or the live REST object, and record the command that established each. A citation asserted from memory in a contract about verification evidence is this row's own mechanism.
- VO-002 [PD-001] [PC-003] semanticTest: Execute `tests/receiver-validate/run.sh` and record its exit status, so the cited reference implementation is shown green at this base rather than cited from reading.
- VO-003 [PD-007] [PC-003] semanticTest: Execute the discrimination control set — each refused entry shown to fail the new rule and the admitted entry shown to pass it — and record command, output and exit for each.
- VO-004 [PD-006] [PC-002] semanticTest: Run the repository gates that score this diff (`scripts/check-kit-published-coherence.py --pr-arm`, the citation/link gates over the edited file, and the shell lint) and record each exit code, including any that is red and why.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] diagnoseOnly: No migration is owed. The change is additive prose inside an existing section; every existing anchor (`#gate-inversion-evidence`, `#root-cause-dedupe-and-materiality`) keeps resolving, and no reader of this file pins a line number. Kit receivers re-materialize the file wholesale from the republished package rather than patching it, so there is no partial-update state to migrate.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2752-authorship-independent-verification-efficacy/work-model.json` is regenerated from these plan sources by `fsgg-sdd analyze`; it is a projection and is never hand-edited. A stale one is reported as `staleGeneratedView` rather than silently rebuilt, which is the same fail-closed posture this work's own subject requires.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2752-authorship-independent-verification-efficacy`.
