---
schemaVersion: 1
workId: 2366-product-tree-feedback-report-materialization
title: Product Tree Feedback Report Materialization
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2366-product-tree-feedback-report-materialization/spec.md
sourceClarifications: work/2366-product-tree-feedback-report-materialization/clarifications.md
sourceChecklist: work/2366-product-tree-feedback-report-materialization/checklist.md
publicOrToolFacingImpact: true
---

# Product Tree Feedback Report Materialization Plan

Prose status: planned

## Source Snapshot
- spec: work/2366-product-tree-feedback-report-materialization/spec.md sha256:1ffe0462d7b9b4732eef632073980227f04c6b55d48749fa254623d0a34bb91d schemaVersion:1
- clarifications: work/2366-product-tree-feedback-report-materialization/clarifications.md sha256:1e74cf203fe90087c5414b31067e56b3c384ca698d595b83f78a2b51cbb9a146 schemaVersion:1
- checklist: work/2366-product-tree-feedback-report-materialization/checklist.md sha256:ce8d3bc75a05318a052af90357b66987faf614b5647dd62f26e2e292a4f515bc schemaVersion:1

## Plan Scope
- Work item 2366-product-tree-feedback-report-materialization is planned from the current specification, clarification, and checklist facts.
- Requirement count: 3.
- Clarification decision count: 1.
- Checklist result count: 3.

## Plan Decisions
- PD-001 [AC-001] [AC-002] [AC-003] [FR-001] complete: ROOT CAUSE / MECHANISM. `scripts/fsgg-skill-registry-check` (Python, 1081 lines) has six existing checks (`source-exists`, `digest-matches`, `digest-shape`, `manifest-found`, `declared-completeness`, `predicate-matches`), all single-row questions; none compares two different rows' `materializes-when` predicates to each other, and none reads a row's `references/**` files (only `source:`, which names `SKILL.md` alone). `scripts/skill-union-assert.sh` (bash, 780 lines) is the only one of the two with a real predicate grammar (`eval_clause`/`eval_and`/`eval_condition`, lines 304-355), but it evaluates a predicate against ONE concrete `--params` instance, never compares two predicates to each other, and only runs against a live scaffolded product tree a downstream repo's own CI chooses to invoke — `sir` (`registry/repos.yml`, `role: non-participant, receives: []`) never does. Add a NEW, registry-internal, seventh check (`cross-references`) to `fsgg-skill-registry-check`, callable with no live product tree, so `.github`'s own CI can catch this invariant breaking before it ever reaches a scaffold.
- PD-002 [AC-001] [AC-002] [AC-003] [FR-001] complete: DESIGN. Port a Python grammar evaluator (`eval_when(expr, params) -> bool`) mirroring `skill-union-assert.sh`'s existing grammar (`always`/`true`/`false`, `key in [v1,...]`, `key == v`, `key != v`, `and`/`or`) rather than inventing a second grammar. Build `domains(rows) -> dict[key, set[str]]` by scanning every row's `materializes-when` for `key`s and literal values (plus the unset/empty string), per clarification DEC-001. `implies(p, q, domains) -> (bool, witness)` enumerates the cartesian product of domains for keys mentioned in `p` or `q` and returns the first combination where `p` holds and `q` does not, or `(True, None)`. `satisfiable(p, domains) -> bool` is `implies("always"-complement...)`; simpler: a predicate is unsatisfiable iff no combination makes it true — reuse the same enumeration. For each row with a satisfiable predicate, walk its skill directory (`os.path.dirname(source_path)`, already computed for `source-exists`) for `SKILL.md` plus every file under `references/`, regex-scan for `\.(?:agents|claude)/skills/([a-z][a-z0-9-]*)/`, drop self-references, resolve each remaining id against the registry's own rows (not producer manifests — this is registry-internal), and either fail on unregistered-and-satisfiable-referencer, or check `implies(referencing_when, referenced_when, domains)` and fail with the witness on non-implication.
- PD-003 [AC-001] [AC-002] [AC-003] [FR-001] complete: VERIFIED AGAINST LIVE CORPUS BEFORE COMMITTING. Grepped every `.claude/skills/**` and `.agents/skills/**` path-shaped cross-reference in this checkout: `work-roadmap`/`work-board` → `fs-gg-feedback-report` (both `always`, implied — the AC-001 positive case), `lane-steward` → `pnext-item` (`lane-steward` is `materializes-when: "false"`, i.e. unsatisfiable, and `pnext-item` is not a registered row at all — the AC-003 vacuous-unsatisfiable case, which must NOT fail), and several self-references (`check-board`, `cross-repo-coordination`, `intra-repo-parallel-work`, `work-roadmap`, `work-board` all referencing their own directory — excluded by the self-reference rule). No live cross-reference in this repo's own trees is a real (satisfiable, non-self, registered-but-not-implied) violation today, so the new check is expected to be green against current `main` with zero fixture edits — the negative case is exercised only via a constructed, non-committed fixture (SB-001/Non-Goal: `tests/` is outside this item's `Paths:`).
- PD-004 [AC-004] [AC-005] [FR-002] complete: `deep-detail.md`'s "Where this runs" section (both skills, both roots — 4 files) is revised from a two-state predicate (kit source / wrong tree) to three states, with the partial-materialization remedy spelled out inline (continue, zero-event reason in the feedback envelope, no fabricated out-of-workspace tool path, one dedupe-worthy finding). `feedback-contract.md` (same 4 files) gets a short pointer sentence before its `dotnet fsi .agents/skills/fs-gg-feedback-report/...` commands, since that file is read as a standalone reference and does not itself repeat the tree-classification logic.
- PD-005 [AC-006] [FR-003] complete: `registry/repos.yml`'s `sir` row gains a comment (not a new machine-validated field — `scripts/repos.sh`, which defines `KNOWN_CAPS` and the row schema, is outside this item's `Paths:`, and inventing an unvalidated field would be indistinguishable from a comment while looking machine-checked) stating the two-axis distinction and naming the missing mechanical grading as a follow-up.

## Contract Impact
- PC-001 [PD-002] cli-behavior: `scripts/fsgg-skill-registry-check`'s check set grows from six to seven; `--json` output gains a new finding kind. Existing finding kinds, exit-code contract (0 clean / non-zero on any finding), and all six existing checks are unchanged — additive only. A consumer CI that greps for a specific old finding string is unaffected; a consumer that asserts "no findings of any kind" now also sees this one, which is the intended tightening.
- PC-002 [PD-004] agent-guidance: `deep-detail.md`/`feedback-contract.md` wording changes are read by worker agents mid-task; the change is a strict refinement (a case that used to read as "stop, wrong tree" now reads as "continue, partial materialization"), not a removal of the existing kit-source stop condition.
- PC-003 [PD-005] documentation-only: `registry/repos.yml`'s `sir` row prose is not read by `scripts/repos.sh`/`scripts/repos-audit.sh` (both outside `Paths:`; confirmed no "unknown key" rejection exists, so an additional comment cannot break `repos.sh validate`).

## Verification Obligations
- VO-001 [PD-002] [PC-001] semanticTest: CORRECTED after round-1 review (`.github#2377` comment 5251954975) — a plan-time decision to leave this uncommitted was wrong: AC-1 requires the negative fixture be committed, and `Paths:` is a declaration that can be widened, not a boundary that can discharge an acceptance criterion. `Paths:` was widened to add `tests/skill-registry/run.sh` (`disjoint`, no collisions), and three committed cases now live there — 62 (an `always`-referencing skill whose sibling's predicate narrows: AC-1's named missing-fs-gg-feedback-report shape, run in both directions — broken then restored), 63 (a path reference to an unregistered sibling id, both directions), and 64 (the unsatisfiable-referencer suppression, proven silent in the SAME run as a live real violation so the suppression cannot be mistaken for masking one). All three run from their committed location (`bash tests/skill-registry/run.sh`), not from a `/tmp` scratch copy. The original `/tmp` mutation evidence (still recorded in the PR's `Verification:` lines) proved the check works today; this correction is what protects it tomorrow.
- VO-002 [PD-002] [PC-001] regressionRun: `tests/skill-registry/run.sh` (existing suite, self-contained, no network) run unmodified after the change and confirmed still green — proves the six existing checks are unaffected and the new seventh check produces zero findings against every existing fixture in that suite.
- VO-003 [PD-002] [PC-001] regressionRun: `scripts/fsgg-skill-registry-check --registry registry/skills.yml --repos-root .` (repos-root `.` since `.github` checks out itself for its own `driver`/`operator`/`process` rows it owns; product rows owned by other repos have no local checkout and are expected to report their existing `manifest-found`/`source-exists` behavior unchanged, not a new failure from this check) run against this repo's live registry and confirmed the new check reports zero cross-reference findings, consistent with PD-003's manual grep.
- VO-004 [PD-004] manualReview: Both `deep-detail.md` files (and `.agents/` twins, confirmed byte-identical to `.claude/` twins before and after) reviewed for the three-state classification and the feedback-contract.md pointer, confirming the existing kit-source stop condition is preserved verbatim in meaning.
- VO-005 [PD-005] manualReview: `registry/repos.yml`'s `sir` row comment reviewed for accuracy against the live `KNOWN_CAPS` vocabulary (read from `scripts/repos.sh`, read-only) so the documented fabric list does not drift from the validator's actual vocabulary.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] additive: The new `fsgg-skill-registry-check` check is additive (new finding kind, no schema change to `registry/skills.yml`, no CLI flag change); no migration step applies. `registry/repos.yml`'s new comment is prose-only.

## Generated View Impact
- GV-001 [PD-001] [PD-002] [PD-003] workModel: `readiness/2366-product-tree-feedback-report-materialization/work-model.json` refreshes from this plan's PD-001..PD-005 and the FR-001..FR-003 they satisfy; `fsgg-sdd refresh` re-derives it after `tasks`/`analyze` rather than it being hand-authored here.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2366-product-tree-feedback-report-materialization`.
