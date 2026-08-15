---
schemaVersion: 1
workId: 2653-incidental-engine-build-shadow
title: Incidental Engine Build Shadow
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2653-incidental-engine-build-shadow/spec.md
sourceClarifications: work/2653-incidental-engine-build-shadow/clarifications.md
sourceChecklist: work/2653-incidental-engine-build-shadow/checklist.md
publicOrToolFacingImpact: true
---

# Incidental Engine Build Shadow Plan

Prose status: planned

## Source Snapshot
- spec: work/2653-incidental-engine-build-shadow/spec.md sha256:af14594bae43fddafe1d209ee5094cb7104990087c23b21a3231c0ef5bfa8c6e schemaVersion:1
- clarifications: work/2653-incidental-engine-build-shadow/clarifications.md sha256:92fa58cc5f423a3fd227bcd525671b7fd839b2edaae19a3a7da6ae8988366cd9 schemaVersion:1
- checklist: work/2653-incidental-engine-build-shadow/checklist.md sha256:0715593fe65893dc1bd29625be24af93b75689541b7d6188940c724e69884e5b schemaVersion:1

## Plan Scope
- Work item 2653-incidental-engine-build-shadow is planned from the current specification, clarification, and checklist facts.
- Requirement count: 8.
- Clarification decision count: 7.
- Checklist result count: 8.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: ADD ONE PRECONDITION TO TIER 2a, AND NOTHING ELSE TO THE KIT ROW. `scripts/fsgg-coord`'s tier-2a branch keeps its `[ -x "$(engine_path "$TOP")" ]` test and gains a single guarded call — `engine_shadows_shared "$TOP" "$CANDIDATE"` — which, when true, falls through to tier 2b instead of exec'ing. The predicate itself is authored in `scripts/fsgg-coord-guards.sh`, which is not kit content (DEC-005). Loading the guard module moves from inside `guards` into an idempotent `load_guards`, called at the same two places and on the same fail-closed terms, so a missing module is still a refusal rather than an unguarded exec.
- PD-002 [AC-002] [FR-002] complete: THE PREDICATE ASKS ABOUT AUTHORSHIP, IN TWO PARTS, AND FAILS TOWARD TODAY. `authored_engine_source "$top"` is true when `git -C "$top" -c status.showUntrackedFiles=normal status --porcelain -- <inputs>` reports anything, or when `git -C "$top" diff --quiet "$(git merge-base HEAD <ref>)" HEAD -- <inputs>` reports a difference. `status.showUntrackedFiles=normal` is forced for `.github#1043`'s reason: `--porcelain` is a formatting flag and does not override a `no` inherited from `~/.gitconfig`, which would make a tree full of WIP read as clean. Every failure path — no resolvable default-branch ref, no merge-base, a `git` invocation that errors — returns TRUE, i.e. preferred, i.e. exactly today's resolution.
- PD-003 [AC-003] [FR-003] complete: THE SWAP IS GATED ON THE DESTINATION, NOT ONLY ON THE SOURCE. `engine_shadows_shared` returns true only when the checkout authors nothing, a `shared_toplevel` distinct from `$TOP` exists, its `engine_path` is executable, AND `stale_detail` over that shared pair is EMPTY. The last conjunct is the one that makes this change monotone: resolution can move only to an engine that answers "current", never to a worse or absent one, so no worker who works today is refused tomorrow (DEC-003).
- PD-004 [AC-004] [FR-004] complete: ONE SPELLING OF THE DEFAULT-BRANCH QUESTION, AND ONE OF THE STALENESS VERDICT. `upstream_drift`'s inline ref resolution is extracted to `default_branch_ref`, which both it and `authored_engine_source` call — two copies would be one edit away from measuring drift against one ref and authorship against another. `stale_guard`'s detail composition is extracted to a pure `stale_detail` that prints and never dies, so the tier-2a precondition and the tier-2b refusal ask the identical question of the identical code.
- PD-005 [AC-005] [FR-005] complete: THE WORKTREE-LOCAL BLOCK GAINS ITS REASON AND SUPERSEDES THE CONTRADICTED REMEDY. The `.github#2402` block at `fsgg-coord-guards.sh:480-495` already fires when `$shared != $top`. It now also states which of the two remaining reasons put the reader there — this checkout authors engine build inputs of its own, so its build was preferred deliberately; or it authors none but there is no current shared engine to fall back to — and, in the first case, explicitly supersedes the `git -C $top merge --ff-only $b` line the same message goes on to forbid. Composed inside the existing `[ -n "$detail" ]` branch, which is the slow path by construction.
- PD-006 [AC-006] [FR-006] complete: THE HAPPY PATH IS UNCHANGED AND IS ASSERTED SO. The precondition runs only inside tier 2a's existing `-x` branch, which a receiver cannot enter, and its first act is the authorship probe, which short-circuits for the kit author before any `git worktree list` or staleness read is paid. Existing legs pin this: `tests/coord-engine-parity/shim.sh` §3's fixtures are `git init` trees whose sources are untracked (authored ⇒ preferred, byte-identical behaviour), and §3e's clone is its own main working tree (no distinct shared checkout ⇒ preferred).
- PD-007 [AC-007] [FR-007] complete: A NEW §3g BUILDS THE SHAPE THE REPORTS HIT, WHICH NO EXISTING LEG DOES. §3's fixture is one `git init` directory with no remote and no linked worktree; §3e's is a real clone that is its own main working tree. Neither can express "a linked worktree with its own build, hanging off a shared checkout with its own build". §3g builds that world with `git worktree add`, a synthetic `refs/remotes/origin/{HEAD,main}` and two fake engines that print distinguishable strings, so every leg asserts WHICH engine ran rather than only that something did — the property `.github#1008` proves an assertion on silence alone cannot buy.
- PD-008 [AC-008] [FR-008] complete: THE KIT OBLIGATION IS DISCHARGED IN THE ORDER THE `take` RECEIPT NAMED. `scripts/fsgg-coord` is a `coordination-kit` row (`registry/repos.yml:575`), so `scripts/repos.sh relock` regenerates `registry/repos.lock` before the PR is opened and that file is named as EXPECTED DRIFT rather than reserved in the touch-set (`.github#309`, `.github#527`). The post-merge kit release is named with evidence before merge.

## Contract Impact
- PC-001 [PD-001] [PD-003] engine resolution: the resolver's tier ORDER is unchanged and its published semantics gain one stated precondition — tier 2a is the caller's own build *of code it authored*. Receivers are structurally unaffected: both new call sites are inside `[ -x "$(engine_path "$TOP")" ]`, which only a checkout owning coord's source can enter, so tiers 1/3/4 resolve byte-for-byte as before and pay no new subprocess.
- PC-002 [PD-005] operator-facing diagnostic: the text `stale_guard` prints on a refusal changes. Its exit code (69), its timing (before the `exec`), and the verb partition behind it do not. `tests/coord-engine-parity/shim.sh` §3c matches refusals case-insensitively on the word `refused` and on the engine not having run, so added text is compatible with it by construction.
- PC-003 [PD-008] kit distribution: `scripts/fsgg-coord` IS a `kit:` row, so this change restages the `coordination-kit`, stales `registry/repos.lock`, and obliges a kit release after merge. `scripts/fsgg-coord-guards.sh` has no row (`.github#1586`) and ships to no receiver.
- PC-004 [PD-004] internal shell contract: `stale_guard`'s observable behaviour is unchanged by the `stale_detail` extraction — same verdict, same message, same die() sites. The extraction adds two new module-level functions (`default_branch_ref`, `stale_detail`, `authored_engine_source`, `engine_shadows_shared`) that no receiver loads.

## Verification Obligations
- VO-001 [PD-001] [PD-003] [PC-001] semanticTest: `bash tests/coord-engine-parity/shim.sh` §3g drives the real `scripts/fsgg-coord` from inside a linked worktree whose own build is BEHIND the default branch under the engine's source trees, and requires exit 0, no refusal, and the SHARED fixture engine as the one that ran.
- VO-002 [PD-002] [PC-001] semanticTest: the same fixture with one commit on the worktree's branch under `src/FS.GG.Coord.Core` requires exit 69, the word `refused`, and NEITHER engine having run — the deliberate build still preferred and still guarded.
- VO-003 [PD-003] semanticTest: a leg in which the SHARED engine is stale and the worktree's own build is current requires the WORKTREE engine to run at exit 0 — resolution never moves to a worse engine.
- VO-004 [PD-005] [PC-002] semanticTest: the VO-002 refusal is asserted to name the worktree by absolute path, to state that this checkout authors engine source of its own, and NOT to instruct a `merge --ff-only` into that branch.
- VO-005 [PD-006] semanticTest: §3 and §3e run unchanged and green, which is the assertion that the happy path and the receivers' shape are untouched; plus a leg with a fresh build and a level checkout requiring total silence and `ENGINE RAN`.
- VO-006 [PD-007] gateInversion: each new leg is demonstrated RED against the unrepaired resolver — the tier-2a precondition removed — and the positive leg is additionally demonstrated RED against a fixture whose shared engine has been deleted, so "found a current shared engine" and "looked at no shared engine" cannot share an exit code. The exact mutation and the observed red are recorded in `evidence.yml`.
- VO-007 [PD-001] [PD-002] staticCheck: `scripts/lint-shell.sh` (the `shell-lint` workflow, which carries no `paths:` filter) shellchecks the edited shim, the edited module and the edited suite; the deliberate word-splitting of the pathspec list carries an explicit `# shellcheck disable=SC2086` as the file's existing occurrences do.
- VO-008 [PD-008] staticCheck: `scripts/repos.sh relock` is run and `registry/repos.lock` is shown to match the shipped `scripts/fsgg-coord`; `repos-registry-selftest` is the CI arm that grades it.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] [PC-003] diagnoseOnly: nothing persisted changes shape — no marker grammar, no board field, no receipt schema. The only skew is between a receiver running the previous `scripts/fsgg-coord` and one running this: neither can enter tier 2a, so both resolve identically, and the kit release is a content refresh rather than a migration.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2653-incidental-engine-build-shadow/work-model.json` is regenerated by the lifecycle commands from the plan sources above; it is SDD-owned and refreshed rather than hand-edited.
- GV-002 [PD-008] generatedRegistry: `registry/repos.lock` is a GENERATED, CI-gated digest artifact over `kit:` rows. `scripts/fsgg-coord` has one, so the lock is regenerated with `scripts/repos.sh relock` and declared EXPECTED DRIFT — never reserved in the touch-set, which is the three-worker deadlock `.github#527` removed.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- `scripts/fsgg-coord-guards.sh` is named by no workflow `paths:` filter, so a guards-only PR starts `coord-engine.yml` on no path. That gap is `.github#2581`'s SB-008 and is in flight on PR #2651 against this same file; it is deliberately not duplicated here (SB-105). This change edits `scripts/fsgg-coord`, which both `paths:` lists already name, so every suite whose subject is this change is selected for this diff.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2653-incidental-engine-build-shadow`.
