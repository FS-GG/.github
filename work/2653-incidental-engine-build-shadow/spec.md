---
schemaVersion: 1
workId: 2653-incidental-engine-build-shadow
title: Incidental Engine Build Shadow
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Incidental Engine Build Shadow Specification

Prose status: specified

## User Value

A worker whose worktree holds an engine build it never asked for — one a gate harness produced as a
side effect, or one `scripts/generate-driver-manifest --write` *required* it to produce — is no longer
refused every board write, because that incidental artifact stops shadowing the shared checkout's
current engine.

Today it does shadow it, and the bill was **five occurrences across three agents in one board-driver
run** on 2026-08-15 (`.github#2653` body and its comments): `plover-9938` ×4 on `.github#2576` — one of
them blocking `heartbeat` — `snipe-4fec` ×1 while critiquing `.github#2571`, and `curlew-48f7` twice on
`.github#2642`. Every one self-repaired by deleting `bin`/`obj` under `src/FS.GG.Coord.*` in its own
worktree; every one first spent a diagnosis the tool does not offer.

The mechanism is not in dispute and is reproduced hermetically by this work
(`tests/coord-engine-parity/shim.sh` §3g, and see `work/.../evidence.yml`): `scripts/fsgg-coord`'s tier
2a prefers a source build under the **caller's own** toplevel, `stale_guard`'s `upstream_drift` arm then
measures that build's checkout against the default branch, and a feature branch is behind `main` under
`src/FS.GG.Coord.{Cli,Core,GitHub}` the moment any engine commit lands after it was cut. The refusal is
fail-closed on every board write, so the whole worktree is poisoned until somebody knows to delete a
directory.

Tier 2a's preference is itself correct and is on the record at `scripts/fsgg-coord:209-213`:

> **AFTER 2a, NEVER BEFORE IT.** A worker who builds in their own worktree gets THEIR build — that is
> the kit author's whole workflow, and preempting it would hand them the shared engine and silently
> discard the edits they are testing.

The premise of that sentence is *a worktree build expresses intent to use it as the engine*. This work
does not weaken the preference; it replaces the unstated premise with a stated question the resolver can
actually answer — **does this checkout author engine build inputs of its own?** — so the kit author, who
does, keeps winning, and the worker whose item touches no engine source, who does not, stops being
refused for an artifact they never asked for.

## Scope

- SB-001: **Tier 2a gains one explicit precondition.** The caller's own source build is preferred when
  that checkout *authors* engine build inputs — uncommitted work under them, or committed work between
  the merge-base with the resolved default-branch ref and `HEAD`. A checkout that authors none of them
  holds a build of code that exists upstream unchanged, so there is nothing of the worker's own to
  discard and `:209-213`'s reasoning does not reach it.
- SB-002: **The decision is git object identity, never mtimes.** `.github#2653`'s criterion 2 asks for
  the distinction to be explicit rather than inferred, and mtimes are what
  `stale_guard`'s own `.github#1572` incident proved unreliable for intent: a `dotnet test` in another
  configuration re-stamped generated `.fs` files and manufactured a false STALE over a tree with zero
  edited sources. `git status --porcelain` and `git diff --quiet <merge-base> HEAD` answer about content
  and cannot be moved by a build.
- SB-003: **The swap is one-directional and never lands on a worse engine.** An incidental build is
  passed over only when a *distinct* shared checkout exists, carries an executable engine, and that
  engine returns an EMPTY staleness verdict from the same `stale_guard` question. Where the shared
  checkout is itself stale, absent, or is the caller's own toplevel, the incidental build is kept and
  guarded exactly as today — so a worker whose own build is current and whose shared checkout is not
  keeps working, which is the availability `.github#2581` is separately fighting for.
- SB-004: **Every unanswerable probe resolves to today's behaviour.** No default-branch ref, no
  merge-base, a `git` invocation that fails: each yields "authored", tier 2a is preferred, and the guard
  runs as it does now. `.github#1549`'s fourth criterion — an unanswerable staleness question is not
  freshness — has its counterpart here: an unanswerable *intent* question is not permission to swap the
  engine under the caller.
- SB-005: **The worktree-local refusal says why that build was preferred, and its remedy is runnable in
  the checkout it names.** `.github#2471` settled the wording that names which checkout is stale, and
  `.github#2402` added the block that says the checkout is not the shared one. Neither says why tier 2a
  chose it, and — measured in this work's own reproduction — the `behind` arm prints
  `git -C <worktree> merge --ff-only origin/main` in the same message whose appended block then forbids
  exactly that (*"Do NOT merge `origin/main` into a feature branch to fix this either"*). A remedy a
  message contradicts three paragraphs later is the shape `.github#1664` already refuses in this file.
- SB-006: **Executable coverage both ways, in the shape the reports actually hit.** A linked worktree
  hanging off a shared checkout, with a build in each, is a shape no existing leg builds: §3's fixture is
  a single `git init` directory with no remote and no linked worktree, and §3e's is a real clone that is
  its own main working tree. The new §3g builds the two-checkout world and asserts the positive (an
  incidental stale build writes the board through the shared engine), the negative (an authored stale
  build is still refused and no engine runs), the no-worse-engine leg, and the message.
- SB-007: **The mechanism is named in the resolver comment beside `:209-213`**, which is
  `.github#2653`'s criterion 2 in as many words.
- SB-008: **The kit surface changes, and the obligation is discharged.** `scripts/fsgg-coord` is a
  `coordination-kit` row (`registry/repos.yml:575`), so `registry/repos.lock` is regenerated with
  `scripts/repos.sh relock` and a kit release follows the merge.

### Revision 2 — the second half of criterion 4, on the operator's decision (2026-08-16)

SB-001…SB-008 above shipped in PR #2669 (`490972d7`), which took criterion 4's SECOND limb: the run
still left a build, and §3g proved it no longer shadowed. The operator then chose criterion 4's FIRST
limb as well, on the record at `.github#2653#issuecomment-5308188901` — *"redirect incidental builds out
of tree"* — over two alternatives recorded there as rejected. `.github#2653` stayed OPEN, so this is the
same row's remaining work rather than a new one, and the two halves are complements: SB-001…SB-008 make
an incidental artifact HARMLESS, SB-009…SB-011 stop one being MADE.

- SB-009: **The gate harness builds the engine OUT OF TREE.** `scripts/check-skill-quality` — measured
  as the only harness under `scripts/` or `tests/` that builds the engine into whatever checkout invokes
  it — delegates to a new `scripts/build-gate-engine`, which builds with `--artifacts-path` to a stable
  per-checkout directory outside the checkout and prints the resulting engine on stdout. Both `bin/` and
  `obj/` move, so `src/FS.GG.Coord.{Cli,Core,GitHub}` are left with neither.
- SB-010: **One answer, not one convention.** The gate and the gate's own fixture
  (`tests/skill-quality/run.sh`) both ASK `scripts/build-gate-engine` where the engine is, rather than
  each restating the artifacts layout. A duplicated path suffix is this repo's most-filed bug class.
- SB-011: **The absence is gated, and the gate can see.** `tests/engine-build-siting/run.py` refuses any
  `dotnet build` under `scripts/` or `tests/` that could put an engine where tier 2a probes. Candidacy is
  DEFAULT-DENY (a project named through a variable is in scope, because the gate cannot read it), the
  declared-site manifest is compared as a SET EQUALITY so a vanished site reds too, and a self-test
  corpus of must-match/must-not-match spellings runs on every invocation — a matcher that stops matching
  exits 3, never green. It is wired to `coherence.yml`, which carries no `paths:` filter, because the
  population it guards is one no path filter encloses.

## Non-Goals

- SB-101: **Does not change the verb partition.** `BOARD_WRITES`, `BOARD_WRITES_CONDITIONAL` and
  `BOARD_READS` keep exactly today's membership and remain three plain literal assignments, which is
  what `tests/coord-engine-parity/shim.sh` §3b requires.
- SB-102: **Does not weaken `stale_guard` for any checkout it is asked about.** Nothing here changes what
  the guard decides once it is asked; it changes only *which* checkout tier 2a hands it, and only toward
  one that answers "current".
- SB-103: ~~**Does not change the producers.**~~ **SUPERSEDED at revision 2 — and its stated reason was
  factually wrong, which is recorded here rather than quietly dropped.** The original text refuted the
  producer-change limb on the ground that "`generate-driver-manifest --write` *requires* the artifact to
  exist at exactly that path".

  **The OBSERVATION was right; the INFERENCE from it was wrong, and only the inference is retired.**
  `generate-driver-manifest` really does refuse without an engine — measured here, not assumed:
  `env -u FSGG_COORD_ENGINE_BIN python3 scripts/generate-driver-manifest --check` exits **2** on a
  checkout with no build, printing *"no engine at …/bin/Release/net10.0/fsgg-coord-engine"*. But it owns
  no engine resolution of its own: at `:710-724` it shells out to `scripts/generate-projections`, and
  THAT file resolves `ENGINE="${FSGG_COORD_ENGINE_BIN:-$HERE/src/FS.GG.Coord.Cli/bin/Release/net10.0/
  fsgg-coord-engine}"` at `:147`. The probed path is the DEFAULT, never the requirement. So
  "requires the artifact **at exactly that path**" — the clause the whole non-goal rested on — is false:
  the same command exits **0** when an out-of-tree engine is named, which is the direct counter-example.

      env -u FSGG_COORD_ENGINE_BIN  … --check   → rc=2, "no engine at <in-tree path>"
      FSGG_COORD_ENGINE_BIN=<out-of-tree> … --check → rc=0, "…manifests are current."

  Live refutation of the limb as a whole: with the build moved out of tree,
  `scripts/check-skill-quality` exits 0 and `bash tests/skill-quality/run.sh` passes all 64 rejection
  cases, while `ls -d src/FS.GG.Coord.*/bin src/FS.GG.Coord.*/obj` finds nothing afterwards. The limb was
  available the whole time; what it needed was an exported `FSGG_COORD_ENGINE_BIN`, not an in-tree build.

  `tests/skill-registry` is separately retired from the original sentence: it builds no engine at all —
  `tests/skill-registry/run.sh` invokes `dotnet build` nowhere — so the row's attribution of a shadowing
  build to that suite was to a `generate-projections` consumer reached from elsewhere, not to it.
- SB-104: **Does not touch the engine.** No file under `src/` changes.
- SB-105: **Does not add `scripts/fsgg-coord-guards.sh` to any workflow `paths:` filter.** That gap is
  real and is `.github#2581`'s SB-008, in flight on PR #2651 against the same file; duplicating it here
  would collide. This change edits `scripts/fsgg-coord`, which both `paths:` lists already name, so every
  suite whose subject is this change is selected for this diff.
- SB-106: **Does not add an environment-variable opt-out** for preferring a local build. Tier 1
  (`FSGG_COORD_ENGINE_BIN`) is already the documented instruction for naming an engine explicitly, and a
  second knob with the same job is a second thing to keep in step. See DEC-003.
- SB-107: **Does not make the incidental build disappear.** It stays on disk and stays usable; it stops
  being *resolved as the engine* when a current shared one exists.

## User Stories

- US-001 (P1): As a worker whose item touches no engine source, my gate harness's or generator's engine
  build does not cost me my board writes, because the resolver prefers the shared checkout's current
  engine over an artifact I never asked for.
- US-002 (P1): As the kit's author, a build I made in my own worktree to test my own engine edits still
  wins, and I am never silently handed the shared engine instead.
- US-003 (P1): As a worker who *is* refused over a build in my own checkout, the message tells me why
  that build was preferred and gives me a remedy I can run there, without instructing me to move a head
  an independent critic may already have confirmed.
- US-004 (P1): As the next person to change this resolution order, an executable leg fails if the
  preference stops distinguishing the two cases, in either direction.
- US-005 (P1): As a worker whose item touches no engine source, running this repo's gate suite leaves my
  checkout exactly as it found it, so there is no artifact for tier 2a to prefer and nothing to diagnose
  — and as the author of the NEXT gate harness, I am told at review time if mine would create one.

## Acceptance Scenarios

- AC-001 [US-001] [FR-001]: Given a linked worktree that authors no engine build inputs, whose own
  engine build is BEHIND the default branch under `src/FS.GG.Coord.{Cli,Core,GitHub}`, and whose shared
  checkout carries a current engine, when `scripts/fsgg-coord release <ref>` is run from that worktree,
  then it exits 0, no refusal is printed, and the SHARED checkout's engine is the one that ran.
- AC-002 [US-002] [FR-002]: Given that same world except that the worktree's branch carries a commit
  editing `src/FS.GG.Coord.Core/Protocol.fs` that the default branch does not, when the same board write
  is run, then it is REFUSED with exit 69 and neither engine runs — the deliberate build is still
  preferred, and still guarded.
- AC-003 [US-001] [FR-003]: Given a worktree that authors no engine build inputs whose own build is
  current, and a SHARED checkout whose engine is stale, when the same board write is run, then the
  worktree's own engine runs and the write completes — resolution never moves to a worse engine.
- AC-004 [US-002] [FR-004]: Given a checkout with no resolvable default-branch ref (no `origin`), when a
  board write is run, then the caller's own build is preferred and guarded exactly as before the change
  — an unanswerable intent question never swaps the engine.
- AC-005 [US-003] [FR-005]: Given the AC-002 world, when the refusal is printed, then it names the
  worktree by absolute path, states that this checkout authors engine source of its own and that this is
  why its build was preferred, and does not instruct a `merge --ff-only` into that branch.
- AC-006 [US-002] [FR-006]: Given a checkout whose engine is NEWER than its source and which is not
  behind, when any verb is run, then the guard is silent, the engine runs, and the new precondition
  manufactures neither a refusal nor a swap on the happy path.
- AC-007 [US-004] [FR-007]: Given the repaired resolver, when the precondition is inverted by deleting
  the tier-2a shadow check, then AC-001's leg fails; and when the fixture's shared engine is removed so
  the swap has nothing to look at, then AC-001's leg fails rather than passing over an empty subject.
- AC-008 [US-001] [FR-008]: Given `registry/repos.lock`, when `scripts/repos.sh relock` is run after the
  shim is edited, then the recorded digest for the `fsgg-coord` kit row matches the file that ships.
- AC-009 [US-005] [FR-009]: Given a checkout with no engine build, when `scripts/check-skill-quality`
  runs to completion, then it exits 0 and `src/FS.GG.Coord.{Cli,Core,GitHub}` contain neither `bin` nor
  `obj` — the engine it used exists, and exists outside the checkout.
- AC-010 [US-005] [FR-010]: Given a kit author's checkout that ALREADY holds a deliberate in-tree engine
  build, when the gate suite runs, then that build is neither deleted nor refreshed and the fixture leg
  passes — the assertion is before-and-after identity, so it cannot red on a legitimate state, and it
  reds on both creation and refresh.
- AC-011 [US-005] [FR-011]: Given a new harness that builds the engine into the caller's checkout —
  whether it names the project literally or through a variable — when `tests/engine-build-siting/run.py`
  runs, then it exits 1 and names the file; and given a matcher that has stopped matching, it exits 3
  rather than reporting a green absence.

## Functional Requirements

- FR-001: Tier 2a prefers the caller's own source build only when that checkout authors engine build inputs of its own; otherwise a distinct shared checkout with a current engine is preferred. (Stories: US-001; Acceptance: AC-001)
- FR-002: A checkout that authors engine build inputs — uncommitted under them, or committed between the merge-base with the default-branch ref and HEAD — keeps tier 2a's preference and `stale_guard`'s refusal unchanged. (Stories: US-002; Acceptance: AC-002)
- FR-003: The swap happens only toward a distinct shared checkout whose engine exists and returns an empty staleness verdict, so resolution never moves to a worse engine. (Stories: US-001; Acceptance: AC-003)
- FR-004: Every unanswerable probe — no default-branch ref, no merge-base, a failed git invocation — resolves to preferring the caller's own build. (Stories: US-002; Acceptance: AC-004)
- FR-005: A refusal raised over a build under the caller's own toplevel states why that build was preferred and names a remedy runnable in the checkout it names, and no longer instructs a merge into a branch under review. (Stories: US-003; Acceptance: AC-005)
- FR-006: The happy path is unchanged — a current engine produces no warning, no refusal, and no swap. (Stories: US-002; Acceptance: AC-006)
- FR-007: The new behaviour is executable in both directions and non-vacuous: inverting the precondition reds the positive leg, and emptying the fixture's shared engine reds it too. (Stories: US-004; Acceptance: AC-007)
- FR-008: `registry/repos.lock` is regenerated for the edited `fsgg-coord` kit row and the kit release obligation is named before merge. (Stories: US-001; Acceptance: AC-008)
- FR-009: The gate harness that builds the engine does so to an explicit output path outside the caller's checkout, leaving no artifact under `src/FS.GG.Coord.{Cli,Core,GitHub}` that `scripts/fsgg-coord`'s tier 2a can resolve. (Stories: US-005; Acceptance: AC-009)
- FR-010: A build the caller made deliberately is neither removed nor refreshed by any gate run, and the coverage that asserts this measures before-and-after identity rather than absence. (Stories: US-005; Acceptance: AC-010)
- FR-011: A repo-wide, always-selected gate refuses any `dotnet build` under `scripts/` or `tests/` that could site an engine where tier 2a probes; it is default-deny on unreadable targets, set-equal on its declared-site manifest, and fails to NO VERDICT rather than green when its own matcher corpus does not hold. (Stories: US-005; Acceptance: AC-011)

## Ambiguities

All ambiguities raised by this specification are answered in
`work/2653-incidental-engine-build-shadow/clarifications.md` as DEC-001 through DEC-005. None remains
blocking.

## Public Or Tool-Facing Impact

- `scripts/fsgg-coord` **is** kit content (`registry/repos.yml:575`, `id: fsgg-coord`), so this change
  restages the `coordination-kit` and stales `registry/repos.lock` until `scripts/repos.sh relock` is
  run. That is expected drift, is CI-gated by `repos-registry-selftest`, and is not reserved in the
  touch-set (`.github#309`, `.github#527`).
- **Receiver resolution is unchanged.** The new precondition sits inside tier 2a's
  `[ -x "$(engine_path "$TOP")" ]` branch, which only a checkout owning coord's source can enter; a
  receiver resolves at tier 1/3/4 exactly as before and pays no new subprocess on its hot path.
- `scripts/fsgg-coord-guards.sh` is **not** kit content (`.github#1586`) and republishes nothing.
- The refusal's **text** is operator-facing and changes. Its exit code (69), the verb partition, and the
  set of invocations refused for a given checkout do not.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2653-incidental-engine-build-shadow`.
