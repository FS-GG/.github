---
schemaVersion: 1
workId: 2206-board-roster-closure
title: Board Roster Closure
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2206-board-roster-closure/spec.md
sourceClarifications: work/2206-board-roster-closure/clarifications.md
sourceChecklist: work/2206-board-roster-closure/checklist.md
publicOrToolFacingImpact: true
---

# Board Roster Closure Plan

Prose status: planned

## Source Snapshot
- spec: work/2206-board-roster-closure/spec.md sha256:a84777951917aa195c25ecbeac4fb0ce657de9bb67f80753beee922834afffdc schemaVersion:1
- clarifications: work/2206-board-roster-closure/clarifications.md sha256:6c0e08c45acb6274acdd4e0eccf64b59370bcc3822935403f3969599ba5b18a4 schemaVersion:1
- checklist: work/2206-board-roster-closure/checklist.md sha256:05bb4b7ba6e9f0e7094e06b5873eac083b1eb4b0388a0f28d8637554d42e7f9f schemaVersion:1

## Plan Scope
- Work item 2206-board-roster-closure is planned from the current specification, clarification, and checklist facts.
- Requirement count: 6.
- Clarification decision count: 0.
- Checklist result count: 6.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Add `board_closure_findings(roster, items)` to `scripts/check-roster-closure.py`. Filter board `items` to those whose `status` (case-insensitive, trim) is not `"done"` — a missing/empty status counts as non-Done. For each such item's `owner/repo`, if `full` is in neither the rostered set nor the `outside-fabric:` set, emit one violation naming the repository and the item's issue number. Wired into `main()` as a third bucket contributing to `findings`, alongside direction A (`check_registry_closure`) and direction B (`org_closure_findings`), using the same exit-1 precedence.
- PD-002 [AC-002] [FR-002] complete: No new opt-out schema. `board_closure_findings` treats a repo as closed by checking membership in the *same* `rostered`/`exempt` sets directions A/B already compute from `roster.get("repos")` and `roster.get("outside-fabric")` — unlike direction B, direction C applies no `_org_owned` owner filter, since a board row already carries its own owner and needs no org-membership proof to be graded. The `role: non-participant` + mandatory `reason:` row (`.github#2245`) and an `outside-fabric:` entry (mandatory `reason:`, `scripts/repos.sh validate`-enforced) are therefore both valid, already-falsifiable dispositions for a board-present repo — nothing new is authored.
- PD-003 [AC-003] [FR-003] complete: Board items are read from a normalized shape `{"owner", "repo", "number", "status"}` per row. The default source is a new `_fetch_board_items(owner, title)` that resolves the Coordination project number by title via `gh api graphql` (`organization(login:$owner){projectsV2}`), then pages `items(first:100,after:$cursor)` reading `fieldValueByName(name:"Status")` and `content{... on Issue{number repository{owner{login} name}}, ... on PullRequest{...}}` — the same `gh api graphql` subprocess pattern `scripts/project-field-options` already uses, never `GET /orgs/{org}/repos`. `--board-json FILE` overrides with a fixture file (a bare list, or `{"items":[...]}`) for offline tests, mirroring `--org-repos-json`.
- PD-004 [AC-004] [FR-004] complete: `_fetch_board_items` raises on any GraphQL transport failure, non-200, malformed JSON, a `data.errors` reply, a `totalCount` that disagrees across pages, or a null `content`/`repository` on more than can be safely ignored (draft items with no repository are skipped, not an error). `main()` catches those exceptions plus an empty (zero-item) result into `noverdicts`, using board-specific message text (`"could not read the board:"`, `"the board reported ZERO items"`) that never reuses direction A's or B's wording, so a board no-verdict is distinguishable by substring from a closed-world pass and from any violation.
- PD-005 [AC-005] [FR-005] complete: `.github/workflows/coherence.yml`'s `roster-closure` job needs no new step. The existing "Run the roster-closure fixture" step already runs `tests/roster-closure/run.sh`, which grows direction C's offline cases in place (VO-001). The existing "Assert the org roster is closed" step's `python scripts/check-roster-closure.py --org FS-GG` call runs direction C too, by default (PM-001) — one invocation now performs A, B, and C, and the step's existing exit-3-is-a-warning / exit-1-fails logic covers all three without modification, because the three directions share one `main()` and one exit-code contract.
- PD-006 [AC-006] [FR-006] complete: After implementation, run `python scripts/check-roster-closure.py --skip-org` (registry closure) is not sufficient; run the full command once against the live board (no `--board-json`, network read via `gh`) and capture its stdout as verification evidence in `evidence.yml`, confirming `EHotwagner/rogue3` and `EHotwagner/S.I.R.` each appear with an explicit disposition (or, for `rogue3`, are absent from the violation list because its only board row is `Done` — a fact, not a silent skip, checked by inspecting the live board status directly).

## Contract Impact
- PC-001 [PD-001] [PD-004] cli exit-code contract: `scripts/check-roster-closure.py` keeps its existing three-way split (0 closed / 1 violation / 3 no-verdict) and extends the *set* of conditions each code can mean, without changing the codes' meanings — a public contract per `.github#2206`'s delivery-route rationale, consumed by `.github/workflows/coherence.yml`.
- PC-002 [PD-003] new CLI flags: `--board-json FILE`, `--skip-board`, `--board-owner` (default `FS-GG`), `--board-title` (default `Coordination`) are added to `check-roster-closure.py`'s argument parser, following the existing `--org-repos-json`/`--skip-org` naming convention.
- PC-003 [PD-005] workflow: `.github/workflows/coherence.yml`'s `roster-closure` job's live-check step gains direction C by running unqualified (no `--skip-board`); its `tests/roster-closure/run.sh` invocation is unchanged (the fixture script itself grows new cases that pass `--board-json`).

## Verification Obligations
- VO-001 [PD-001] [PD-002] [PD-004] semanticTest: Extend `tests/roster-closure/run.sh` with `expect_finding`/`expect_noverdict` cases for direction C: an unrostered schedulable repo (violation), a rostered or `outside-fabric:` schedulable repo (closed), a `Done`-status row on an unrostered repo (no violation — not schedulable), a user-owned schedulable repo with no roster/exempt row (violation, proving no org-enumeration blind spot), an unreadable/malformed/empty `--board-json` (no-verdict), and `--skip-board` (loud, offline-only).
- VO-002 [PD-001] gateInversion: With direction C's call temporarily removed from `main()`, re-run the new fixture cases and confirm the violation/no-verdict assertions that direction C alone establishes go RED (the test fails because the tool now reports exit 0), proving the new tests exercise the new code rather than passing vacuously; then restore the call and confirm green. Recorded as authored evidence, not merely asserted.
- VO-003 [PD-006] liveVerification: Run the implemented `check-roster-closure.py` against the real Coordination board once (network, via `gh api graphql`) and record its stdout/exit code as evidence that `EHotwagner/rogue3` and `EHotwagner/S.I.R.` are each dispositioned, satisfying AC-006's live-board requirement.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] [PC-002] additive: `check-roster-closure.py`'s existing flags, exit codes, and directions A/B behavior are unchanged; the new flags default to running direction C against the live board (fail-open would silently disable exactly the coverage this item exists to add), so an existing caller that never passes the new flags gains direction C automatically. No `registry/repos.yml` schema or data change is required by this item (`EHotwagner/S.I.R.` is already rostered per `.github#2245`; `EHotwagner/rogue3` needs no row because its only board row is `Done`).

## Generated View Impact
- GV-001 [PD-001] [PD-002] [PD-003] [PD-004] [PD-005] [PD-006] workModel: `readiness/2206-board-roster-closure/work-model.json` and `analysis.json` are regenerated by `fsgg-sdd analyze`/`tasks` from this plan's PD/PC/VO/PM entries after every edit here, so the six plan decisions above stay the single source a reviewer reads rather than a second, hand-maintained summary drifting from them.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2206-board-roster-closure`.
