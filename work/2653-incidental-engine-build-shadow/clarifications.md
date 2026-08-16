---
schemaVersion: 1
workId: 2653-incidental-engine-build-shadow
title: Incidental Engine Build Shadow
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2653-incidental-engine-build-shadow/spec.md
publicOrToolFacingImpact: true
---

# Incidental Engine Build Shadow Clarifications

## Source Specification
- work/2653-incidental-engine-build-shadow/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: What actually distinguishes a deliberate worktree build from an incidental one, given the resolver cannot see the command that produced it?
- CQ-002 [AMB:AMB-002] blocking answered: `.github#2653`'s criterion 4 asks the gate harnesses to stop producing a resolvable artifact. Why are the producers not changed?
- CQ-003 [AMB:AMB-003] blocking answered: Should the swap away from an incidental build be unconditional, or conditional on the shared engine being current?
- CQ-004 [AMB:AMB-004] blocking answered: Which paths count as "engine build inputs" for the authorship question, and why is that not `stale_guard`'s own subject?
- CQ-005 [AMB:AMB-005] blocking answered: Where does the decision live, given `scripts/fsgg-coord` is kit content and `scripts/fsgg-coord-guards.sh` is not?
- CQ-006 [AMB:AMB-006] blocking answered: Should a build the worker deliberately produced be marked by an environment variable instead?

## Answers

- CQ-001 answer: **Ask the CHECKOUT, not the artifact.** Intent lives in the command that ran, and the
  resolver never sees it; every attempt to recover it from the artifact fails. A marker file written by
  the producer answers only for the producer that knows to write one, and cannot survive the case the
  filer's own follow-up describes — a kit author who runs a harness and then builds deliberately, where
  a no-op `dotnet build` leaves the disclaimer looking current. An mtime comparison is refuted twice
  over: `.github#1572` measured a `dotnet test` in another configuration re-stamping MSBuild-generated
  `.fs` files and manufacturing a false STALE over a tree with zero edited sources, and `.github#2653`'s
  criterion 2 asks for the distinction to be explicit rather than inferred from mtimes in as many words.

  What the resolver *can* ask, cheaply and exactly, is whether this checkout **authors engine build
  inputs of its own** — content that exists here and nowhere upstream:

  - uncommitted work under those inputs (`git status --porcelain`, with
    `status.showUntrackedFiles=normal` forced for `.github#1043`'s reason); or
  - committed work between the merge-base of `HEAD` with the resolved default-branch ref and `HEAD`
    (`git diff --quiet <base> HEAD -- <inputs>`).

  That is a question about content, decided by git object identity. A build cannot move it, a harness
  cannot move it, and a `dotnet test` in another configuration cannot move it. And it is the exact
  question `scripts/fsgg-coord:209-213` was reaching for: *"preempting it would hand them the shared
  engine and silently discard the edits they are testing"* presupposes there ARE edits. Where the answer
  is "none", the sentence has no subject and its conclusion does not follow.

  The five reported occurrences all sit on the "none" side by construction — `.github#2576`,
  `.github#2571` and `.github#2642` are a template-axis item, a review of a gates change, and a prose
  item; none of them authors a byte under `src/FS.GG.Coord.*`.

- CQ-002 answer: **Because the filer's own follow-up refutes that limb, and criterion 4 offers a second
  one.** `.github#2653`'s third comment records that `scripts/generate-driver-manifest --write` REFUSES
  without a Release engine build in the worker's own worktree (*"no engine at
  .../src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"*), and that any edit to a kit-packed
  source stales `registry/coordination-kit-skill-manifest.json`, whose only remedy is that regeneration.
  So every kit-editing worker is *required* to create the artifact at exactly the path tier 2a probes.
  A fix that only taught harnesses to build elsewhere would leave that path broken, and one that forbade
  worktree engine builds outright would make `--write` unusable. The comment draws the same conclusion —
  three populations, not two, with the required-input population being the one cleanup-by-convention
  cannot reach.

  Criterion 4's own second limb is *"or the run is shown to leave no shadowing build behind"*, and that
  is what this work satisfies: the run still leaves a build, and that build demonstrably no longer
  shadows — asserted, not asserted-about, by `tests/coord-engine-parity/shim.sh` §3g. This is also
  strictly the more durable half of the disjunction: a producer-side fix has to be repeated for every
  future producer and is silent when somebody forgets, which is `#570`'s decay mode; a resolver-side one
  covers producers that do not exist yet.

  **Revision 2 correction (see DEC-008).** The paragraph above is preserved as written, and one step in
  it is wrong. *"every kit-editing worker is required to create the artifact at exactly the path tier 2a
  probes"* — the refusal is real, but the path is not. `generate-driver-manifest` shells out to
  `generate-projections` (`:710-724`), which reads `${FSGG_COORD_ENGINE_BIN:-<in-tree default>}`, so
  naming an out-of-tree engine satisfies it: measured `rc=2` unnamed, `rc=0` named. The closing argument
  — that a producer-side fix must be repeated for every future producer and is silent when somebody
  forgets (`#570`) — is the one part that survives intact, and it is answered rather than waived:
  `tests/engine-build-siting/run.py` is the gate that makes forgetting loud, which is why SB-011 exists
  and why it is wired to a workflow carrying no `paths:` filter.

- CQ-003 answer: **Conditional, and the unconditional form is refuted rather than merely riskier.** If
  an incidental build were always passed over, a worker whose own build is CURRENT and whose shared
  checkout is STALE would go from working to refused — and that is not a hypothetical population, it is
  the exact population `.github#2581` exists for, where the shared checkout's repair is host-serialised
  and unbounded. A change that fixes one refusal by manufacturing another in the neighbouring row is not
  a fix. So the swap is allowed only toward a distinct shared checkout that has an executable engine AND
  returns an EMPTY verdict from the same `stale_guard` question, which gives the change a property worth
  stating plainly: **resolution can only ever move to an engine that answers "current"**, never to a
  worse one, and never to a *missing* one.

- CQ-004 answer: The subject is `src/FS.GG.Coord.{Cli,Core,GitHub}` plus `Directory.Build.props`,
  `Directory.Packages.props` and `global.json` — the files that are implicitly imported by every project
  beneath them and therefore genuinely end up in the compiled engine, which is `dirty_guard`'s own
  pathspec minus the shim itself (the shim is not compiled into the binary).

  It is deliberately NOT `stale_guard`'s `ENGINE_SOURCE_TREES`, and the asymmetry runs the opposite way
  from that guard's own (`fsgg-coord-guards.sh:300-308`). There, a wider subject costs an outage, so the
  set is narrow. Here a wider subject costs *nothing but keeping today's behaviour*: every path added
  makes "authored" more likely, and "authored" is the status-quo branch. The failure directions are not
  symmetric — a miss means a worker's own engine edits are silently discarded, a false positive means a
  worker keeps the resolution they have today — so this question is answered on the side that cannot
  discard work. It is equally deliberately NOT all of `src/`: `src/FS.GG.Kit` does not compile into the
  engine, and counting it would re-refuse exactly the kit-editing workers `.github#2642` reported.

- CQ-005 answer: **The verdict lives in the guard module; the kit row gets one branch and the comment
  criterion 2 requires.** `scripts/fsgg-coord` is a `coordination-kit` row (`registry/repos.yml:575`),
  mirrored byte-identical into seven receivers, so every edit republishes the kit — the bill `.github#1586`
  measured six times in one day. `scripts/fsgg-coord-guards.sh` has no kit row, is loaded only from tiers
  2/2b, and is dead code in every receiver, so the coordination knowledge belongs there by that
  precedent. What cannot move is the tier boundary itself: the decision has to be taken where the `exec`
  is, and criterion 2 asks for the mechanism to be *"named in the resolver comment beside the existing
  rationale"*, which is `scripts/fsgg-coord:209-213`. So the shim keeps one guarded branch and gains
  prose; the predicate, its pathspec and its fail-safe direction are authored in the module.

  REJECTED — **let `guards` itself `exec` the shared engine**: it would keep the kit row byte-identical
  and hide a resolution decision inside a function whose whole contract is to measure and warn. The
  resolver's header promises a transparent pipe with a legible tier order; a hidden second exec is worse
  than a republish.

- CQ-006 answer: **No.** An explicit marker for the deliberate build is criterion 2's first option, and
  it fails the population it must serve: the kit author's workflow is a bare `dotnet build`, so an
  opt-in marker would silently demote exactly the build `:209-213` protects unless every author
  remembered a new variable — `#570`'s decay mode, aimed at the one person who must not be caught by it.
  An opt-*out* variable is not needed either, because tier 1 already is one: `FSGG_COORD_ENGINE_BIN` is
  an instruction honoured before any guard runs, documented in the shim's header, asserted by
  `tests/coord-engine-parity/shim.sh`, and it names an engine exactly rather than nudging a preference.
  A second knob for the same job is a second thing to keep in step with the first.

## Decisions

- DEC-001 [CQ-001] [AMB:AMB-001]: **THE DISTINGUISHING FACT IS AUTHORSHIP OF ENGINE BUILD INPUTS, READ
  FROM GIT OBJECT IDENTITY.** Tier 2a prefers the caller's own build when that checkout has uncommitted
  work under the engine's build inputs, or committed work between its merge-base with the resolved
  default-branch ref and `HEAD`; otherwise the build is incidental. REJECTED — **a provenance marker
  written by the producer**: it answers only for producers that know to write one and cannot be
  invalidated soundly by a later no-op `dotnet build`. REJECTED — **any mtime comparison**: refuted by
  `.github#1572` and by criterion 2's own wording.
- DEC-002 [CQ-002] [AMB:AMB-002]: ~~**THE PRODUCERS ARE NOT CHANGED; CRITERION 4 IS SATISFIED BY ITS
  SECOND LIMB.** `scripts/generate-driver-manifest --write` requires the artifact at exactly the probed
  path, so "build somewhere the resolver does not look" is unavailable for the population that matters,
  on the filer's own follow-up evidence. The run still leaves a build; §3g shows it no longer shadows.~~
  **SUPERSEDED by DEC-008, and its premise was false.** Kept struck through rather than deleted, because
  the reason a decision was made is evidence about the decision, and a superseding record that hides the
  refuted premise teaches the next reader nothing.
- DEC-008 [CQ-002] [AMB:AMB-002]: **THE PRODUCERS *ARE* CHANGED; CRITERION 4's FIRST LIMB IS TAKEN TOO.**
  Decided by the operator on 2026-08-16 and recorded at `.github#2653#issuecomment-5308188901`:
  *"redirect incidental builds out of tree"*. Two alternatives are recorded there as REJECTED and are not
  reopened here — *mark intentional builds* (it changes the kit author's workflow, and an unmarked
  deliberate build would then silently resolve to the shared engine, which is the harm
  `scripts/fsgg-coord:209-213` exists to prevent) and *harness cleans up after itself* (fragile: a
  crashed run leaves the shadow, and a kit author who runs the suite after building deliberately would
  have that build deleted underneath them).

  **DEC-002's OBSERVATION held; its INFERENCE did not, and the distinction is the whole decision.**
  `generate-driver-manifest` does refuse without an engine — `env -u FSGG_COORD_ENGINE_BIN python3
  scripts/generate-driver-manifest --check` exits **2** on a checkout with no build. DEC-002 read that as
  "requires the artifact at exactly the probed path", and that step is where it went wrong: the file owns
  no engine resolution at all. At `:710-724` it shells out to `scripts/generate-projections`, which
  resolves `ENGINE="${FSGG_COORD_ENGINE_BIN:-…/bin/Release/net10.0/fsgg-coord-engine}"` at `:148` — the
  probed path is a DEFAULT, not a requirement. The counter-example is one command:
  `FSGG_COORD_ENGINE_BIN=<out-of-tree> python3 scripts/generate-driver-manifest --check` exits **0**.

  So the required-input population DEC-002 identified is real, and it is satisfied by NAMING an engine
  rather than by siting one where tier 2a probes. `scripts/check-skill-quality` therefore EXPORTS
  `FSGG_COORD_ENGINE_BIN` rather than prefixing it onto one call — the first draft prefixed it and still
  failed, because `check-skill-quality.py` reaches `generate-projections` through a grandchild that
  inherited nothing. With the export in place the full 64-case `tests/skill-quality` suite passes and no
  `bin` or `obj` is created under `src/FS.GG.Coord.{Cli,Core,GitHub}`.

  **The two limbs are complements, not alternatives, and both are kept.** `engine_shadows_shared`
  (DEC-001/SB-001) still classifies an incidental artifact as harmless — it must, because a worker who
  runs `dotnet build` by hand, or a future tool outside this repo, can still create one. The producer
  change removes the case that was actually biting the fleet. Removing either would leave a live hole.
- DEC-003 [CQ-003] [AMB:AMB-003]: **THE SWAP IS CONDITIONAL AND ONE-DIRECTIONAL.** An incidental build is
  passed over only for a distinct shared checkout whose engine exists and returns an empty staleness
  verdict. REJECTED — **an unconditional swap**: it would refuse the worker whose own build is current
  and whose shared checkout is not, which is `.github#2581`'s population.
- DEC-004 [CQ-004] [AMB:AMB-004]: **THE AUTHORSHIP SUBJECT IS WIDER THAN THE REFUSAL SUBJECT AND
  NARROWER THAN `src/`.** `src/FS.GG.Coord.{Cli,Core,GitHub}`, `Directory.Build.props`,
  `Directory.Packages.props`, `global.json`. Every unanswerable probe resolves to "authored", because an
  unanswerable intent question is not permission to swap the engine under the caller — the counterpart
  of `.github#1549`'s fourth criterion.
- DEC-005 [CQ-005] [AMB:AMB-005]: **THE PREDICATE IS AUTHORED IN THE NON-KIT GUARD MODULE; THE KIT ROW
  GAINS ONE BRANCH AND THE REQUIRED COMMENT.** REJECTED — **an `exec` hidden inside `guards`**: it would
  avoid a republish by making the tier order illegible.
- DEC-006 [CQ-006] [AMB:AMB-006]: **NO NEW ENVIRONMENT VARIABLE, IN EITHER DIRECTION.** Tier 1 is the
  documented explicit instruction and is unchanged.
- DEC-007 [CQ-001] [AMB:AMB-001]: **THE WORKTREE-LOCAL REFUSAL GAINS ITS REASON AND LOSES ITS
  SELF-CONTRADICTION.** Measured in this work's own reproduction: the `behind` arm prints
  `git -C <worktree> merge --ff-only <ref>` while the appended `.github#2402` block forbids exactly that
  ("Do NOT merge `origin/main` into a feature branch"). The block now states which of the two remaining
  reasons put the reader there — this checkout authors engine source of its own, or there is no current
  shared engine to fall back to — and names the remedy that fits it, superseding the generic line above.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
None. All six blocking ambiguities are resolved by DEC-001 through DEC-007 above; DEC-008 (revision 2)
supersedes DEC-002 on the operator's 2026-08-16 decision and on measurement that its premise was false.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2653-incidental-engine-build-shadow`.
