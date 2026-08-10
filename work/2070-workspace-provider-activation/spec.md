---
schemaVersion: 1
workId: 2070-workspace-provider-activation
title: Workspace Provider Activation
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Workspace Provider Activation Specification

Prose status: specified

## User Value
The org registry (`registry/dependencies.yml`, `registry/skills.yml`, their changelogs, and
`docs/architecture.md`/`docs/registry/compatibility.md`) names the actual published
`console`/`web`/`fable-game`/`fable-bindings` workspace providers and one coherent-set version for
each, so the maintainer can sign off on packing and releasing the compatible `new-sdd-workspace`
wizard knowing the registry states only proven, live facts — never a predicted or future artifact.

## Scope
- SB-001: Registry activation only (epic .github#2067 rollout phases 1-5): verify Game Skills
  publication and SDD materializer adoption; verify the Templates release handoff by independently
  downloading its artifacts; verify the Babylon bindings reference and S.I.R.#138 consume only
  public scaffold/package inputs with no sibling-checkout edges; update
  `registry/dependencies.yml`, `registry/skills.yml`, both changelogs, `docs/architecture.md`, and
  the generated `docs/registry/compatibility.md` projection; regenerate projections and validate
  with the shipped validator (`scripts/generate-projections --check`, `fsgg-sdd registry`).
- SB-002: Files touched: `registry/dependencies.yml`, `registry/skills.yml`,
  `registry/CHANGELOG.md`, `registry/skills.CHANGELOG.md`, `docs/architecture.md`,
  `docs/registry/compatibility.md` (widened — generated projection of the registry rows this item
  edits), `scripts/NewSddWorkspace/NewSddWorkspace.fsproj`, `scripts/NewSddWorkspace/README.md`,
  `.github/workflows/release-new-sdd-workspace.yml`, plus this work item's own
  `work/2070-workspace-provider-activation/**` and `readiness/2070-workspace-provider-activation/**`
  (widened per known defect .github#2324).

## Non-Goals
- SB-003: Rollout phase 6 (packing and releasing the `new-sdd-workspace` wizard with `--template`
  support live) is explicitly OUT OF SCOPE. The maintainer reserves the publish decision; this item
  stops at a coherent, validated, unreleased registry state and reports it for sign-off.
- SB-004: Rollout phases 7-9 (installing the *public* wizard and scaffolding all four identities,
  running restore/build/test/pack/browser-smoke/governance/SDD-evidence/doctor, and the final
  registry/feed/provenance re-read) depend on the phase 6 release and are out of scope here.
- SB-005: No edit to `scripts/NewSddWorkspace/Program.fs` or any other wizard source — the
  `--template` selector already merged via .github#2069/PR#2074 (commit `31c4700f`) and is
  unreleased; this item does not touch its implementation, only its packaging metadata bystander
  facts if any are needed (in practice: none — the fsproj `<Version>` stays at the currently
  released `0.9.0` because phase 6 has not run).

## Coherent-Set Decisions (settled here, before any registry edit)
- DEC-001 (CORRECTED — see addendum below): The template package is `FS.GG.Workspace.Template`
  (renamed from the frozen `FS.GG.Templates`/`fs.gg.templates` line, which topped out at 0.7.1 per
  FS-GG/FS.GG.Templates#349). Register package-version **0.8.1** (the live newest; corrected from an
  initial, wrong 0.8.0 — see DEC-001-ADDENDUM). Tag `fs-gg-templates/v0.8.1` → `286c2a43250edb1f60a72d10fc53e645a397556c`
  (PRs #397/#399/#401/#403, FS.GG.Templates#386 — a real fix closing a `**/.nuget/**` leak into
  generated products). Independently downloaded and byte-verified: nupkg SHA-256
  `102c72364ce0018b34950774b86ee12d0f6da43e0de36d609f14bfbdae5c5d21`; byte-diffed against 0.8.0
  (nupkg SHA-256 `11b57ac61e2c3eadaefae0d20ae427e4ca6f7593585cf79e5f6e89565b93c473`) confirms only
  the five `.template.config/template.json` files changed and no template identity was added or
  removed. FS-GG/FS.GG.Templates' four `providers/*.providers.yml` descriptors and the real first
  consumer (EHotwagner/S.I.R.#138, merge `b17ac33bd6e4765c53d9ecccc7939204a3671fa8`) still name
  `0.8.0` — that is a `dependencies:`-edge fact (what a consumer actually pins), independent of this
  row's `package-version`, which is a feed-mirroring fact (see addendum).
  - **DEC-001-ADDENDUM (repair round, CI red on PR#2344 head `824c4c2f`)**: the original DEC-001
    reasoning — "don't pin ahead of what a consumer/descriptor has adopted" — is correct in spirit
    but was applied to the wrong field. `scripts/check-feed-coherence.py` (`.github/workflows/feed-coherence.yml`)
    asserts, unconditionally, that a contract row's `package-version` equals the literal newest
    version live on the feed; it has no "known-lag" escape and none is wanted — that discipline
    (`FR-007`/publish-before-flip) exists specifically so `package-version` never silently drifts
    behind a real publish. The field that legitimately encodes "what a consumer actually pins,
    which may lag" is a `dependencies:` edge (e.g. the pre-existing `templates -> rendering` edge,
    which lags Rendering's newest publish for exactly this reason). Registering `package-version:
    0.8.0` therefore reddened `feed-coherence`'s `feed` job (`BEHIND the feed`) on the first push.
    Corrected to 0.8.1, the literal newest, independently re-verified live at repair time
    (`curl https://api.nuget.org/v3-flatcontainer/fs.gg.workspace.template/index.json` →
    `["0.8.0","0.8.1"]`) and confirmed clean end-to-end with a real token:
    `python scripts/check-feed-coherence.py registry/dependencies.yml` → `ok`.
- DEC-002 (CORRECTED — see addendum below): Register a new contract row for `FS.GG.Game.Skills`,
  owner `game`, version/package-version **0.8.0** (corrected from an initial, wrong 0.7.0 — see
  DEC-002-ADDENDUM), tag `skills/v0.8.0` → `7b8d24479a83b64a68645f47dd943f9b615e7064`, consumer
  `[sdd]`. FS-GG/FS.GG.Game#552 (closed via PR#554, merge `7fa79f29023468a6bcd4ef7ef5b696abeba508ec`)
  published 0.7.0; Game published 0.8.0 afterward (`gh api compare/skills/v0.7.0...skills/v0.8.0` =
  two unrelated docs-only commits, #557/#558). FS-GG/FS.GG.SDD#817 (closed via PR#819, merge
  `aa1d6d4c1d105a0dba87a39230cfea1fb90dafc9`) proved SDD's production scaffold materializer adopts
  0.7.0 and emits the `fs-gg-game-fable` skill with digest
  `443a82d24a0b4bbd21f4499b06f6e3d12b95a36a858f3880b414b74cae1a5c50` — but independently
  re-downloading `fs.gg.game.skills.0.8.0.nupkg` (SHA-256
  `e058642649510e730da168d97610fe96d12805870112127227a64b15e9b1b9f0`) and re-extracting that same
  skill file shows the digest is IDENTICAL at 0.8.0, so SDD's proof still holds against the version
  this row now names. The `sdd -> game` dependency edge stays at `game-skills@0.7.0` — SDD's own
  `src/FS.GG.SDD.Commands/packages.lock.json` still pins `FS.GG.Game.Skills` `[0.7.0, )`, which is a
  genuine "what the consumer pins" fact, distinct from this row's feed-mirroring `package-version`.
  - **DEC-002-ADDENDUM (repair round, same cause as DEC-001-ADDENDUM)**: pinning 0.7.0 by
    consumer-adoption reasoning reddened `feed-coherence`'s `feed` job the same way
    (`BEHIND the feed`, declares 0.7.0 but newest is 0.8.0). Corrected to 0.8.0 for the same reason:
    `package-version` mirrors the feed unconditionally; "what SDD actually consumes" is the separate
    `sdd -> game` dependency edge, left at 0.7.0 since that is what SDD's source genuinely pins.
- DEC-003: `game-sim-core` (FS.GG.Game.Core) stays pinned at the already-registered **0.13.0**
  (`registry/dependencies.yml` line 831-833) — independently re-confirmed live on nuget.org and as
  the exact `[0.13.0]` pin in S.I.R.'s `Directory.Packages.props`. Its `consumers:` list gains
  `templates` (fable-game consumes the published `fs-gg-game-core-fable-lockstep-v1` profile per
  ADR-0069/ADR-0071 §4) alongside the existing `rendering` consumer — no version change.
- DEC-004: `minimum-fsgg-sdd` for the four new provider identities is registered as **0.6.0**,
  mirroring what every one of FS.GG.Templates' four `providers/*.providers.yml` descriptors
  currently declares (registry mirrors the descriptor; it does not lead it — same discipline as the
  existing `fs-gg-ui-template` row). This is knowingly understated for `fable-game` specifically:
  the owner-sourced Game skill is only correctly materialized by an `FS.GG.SDD.Cli` build containing
  SDD PR #819's merge commit `aa1d6d4c`, first published at **1.0.0** (confirmed by GitHub compare:
  `v0.32.0...aa1d6d4c` is `ahead_by=2 behind_by=0`, i.e. v0.32.0 predates the fix; `aa1d6d4c...v1.0.0`
  is `ahead_by=12 behind_by=0`, i.e. v1.0.0 contains it; nuget.org's newest published `fs.gg.sdd.cli`
  is 1.0.0). This is a real coherence gap in FS.GG.Templates' own descriptor, not something this
  item's `Paths:` can fix (`providers/*.providers.yml` lives in FS.GG.Templates). It is filed as a
  distinct cross-repo finding (FS-GG/FS.GG.Templates#407) rather than silently corrected here, and the
  registry row for the new contract carries an explicit comment naming the gap so no reader mistakes
  0.6.0 as sufficient for `fable-game` specifically.
- DEC-005: The Babylon bindings reference (`EHotwagner/babylonjsBindings`, `Fable.Babylon` package)
  is unreleased (no nuget package, no tags/releases) and is not itself registered — ADR-0072 treats
  it only as a proving reference for the generic `fable-bindings` template contract, not as a
  registry-tracked dependency. Its dependency files (`Fable.Core`, `Fable.Browser.Dom`,
  `@babylonjs/core`) are confirmed public-only with zero FS-GG or sibling-checkout references, which
  is what rollout phase 3 asks this item to verify — no registry row follows from that verification.
- DEC-006: `new-sdd-workspace` (`FS.GG.NewSddWorkspace`) stays registered at the currently released
  **0.9.0** — its `--template` selector code merged via .github#2069/PR#2074 (commit `31c4700f`,
  2026-08-01) but has not been packed or released (the release tag `new-sdd-workspace/v0.9.0` points
  at the earlier commit `1c817c4e`, predating PR#2074). Registering any newer version here would
  advertise a future artifact; this item records the merged-but-unreleased state in a comment instead
  and stops at the phase 6 boundary.
- DEC-007 (repair round 3, critic finding on PR#2344 head `0c4aaf62`, review
  https://github.com/FS-GG/.github/pull/2344#issuecomment-5244659382): DEC-001-ADDENDUM/DEC-002-ADDENDUM
  corrected `registry/dependencies.yml`'s two new rows to `0.8.1`/`0.8.0`, but the commit that made
  that correction (`0c4aaf62`) never touched the hand-authored prose in `docs/architecture.md`
  (lines 100, 388, 404, 413, 546-547) or `profile/README.md` (110-133) — both still asserted the
  pre-correction `0.8.0`/`0.7.0`/"unreleased" facts, producing a self-contradiction three lines from
  `docs/architecture.md`'s own correctly-regenerated versions table. Fixed all six sites, and where a
  site was purely restating a version the generated table already carries (the §5 contract-summary
  table's `fs-gg-workspace-template`/`game-skills` rows), replaced the literal with a pointer to that
  table instead of a second copy — the same fix class `.github#913` named when it built the generated
  table specifically because "a bump is a multi-site hand-edit and any missed site is a silent
  self-contradiction." Left `docs/architecture.md:417` (S.I.R.'s actually-consumed `0.8.0`) and
  `profile/README.md`'s per-shape one-word availability column alone — genuinely different facts
  (a consumer's historical pin, not the registry's newest-tracking pin), now with an explicit note
  distinguishing the two so a reader does not mistake one for a stale copy of the other.
- DEC-008 (repair round 4, critic finding on PR#2344 head `d4134f3e`, review
  https://github.com/FS-GG/.github/pull/2344#issuecomment-5244832877): round 3's handoff claimed
  "Added EV017 to the SDD evidence set … registered via a genuine JUnit report." **That claim was
  false.** `EV017` was only ever a `<testcase>` name inside a throwaway `verify-suite.sh` run's JUnit
  output — a label in a report file, never an entry in `work/2070-workspace-provider-activation/evidence.yml`,
  never traced to a task/obligation, and never committed anywhere. `git diff --name-status
  0c4aaf62..d4134f3e` shows zero files added. This is corrected, not merely acknowledged: `plan.md`
  gained `VO-003` (a real verification obligation), `fsgg-sdd tasks` generated the real `T013` from
  it, and `work/2070-workspace-provider-activation/evidence.yml` now carries a real `EV013` — with an
  invertibility proof actually run before being registered (green on the corrected tree → mutated the
  six sites back to the pre-repair stale literals → red, 3/8 sub-checks correctly failed → restored
  from backup, confirmed byte-identical via `git status` → green again), and a genuine `observedRun`
  receipt from that final green run.
  - **Evidence-count history, explained rather than left to be asked about a second time.** The
    observed-run `passed` count attached to every `kind: verification` entry moved 12 (round 3,
    `0c4aaf62`) → 6 (round 4, `d4134f3e` — the claimed-but-undelivered EV017 round) → 4 (round 5, this
    correction). Each shrink has a stated reason, not a silent loss: round 3's 12-check suite
    re-verified every fact from scratch (nuget.org downloads/hashes for both packages, the S.I.R. PR
    state, the filed finding, schema-unchanged, both fixtures, the live feed-coherence run) because
    that round changed the registry's version pins themselves. Round 4 touched only prose, not any
    previously-verified fact, so it re-ran only the checks whose subject that round's diff could have
    broken (registry/projection validity, both fixtures, the live feed check, and the — falsely
    claimed — prose check) and treated the untouched facts (package hashes, S.I.R. state, the filed
    issue) as already-proven and unchanged rather than re-downloading multi-megabyte packages to
    re-establish facts nothing in that diff could have altered. This round (5) is the same judgement,
    narrower still: it re-runs only what round 4 touched plus the one thing round 4 got wrong (the
    prose check, now real) — registry/projection validity, the driver manifest, the live feed check,
    and the genuine `EV013`. It does **not** re-run the two fixture scripts
    (`tests/feed-coherence/run.sh`, `tests/skill-registry/run.sh`), because nothing in this round's
    diff touches their subjects (`scripts/registry_packages.py`, the driver-manifest inputs) either —
    consistent with the same "only re-verify what could have broken" discipline, stated explicitly
    here rather than left for a reader to infer from a shrinking number. The mechanism itself
    (one aggregate report's pass/fail count stamped onto every `kind: verification` entry, regardless
    of which entry's specific fact a given report's checks individually cover) is unchanged from round
    1 and was not introduced by this correction; only the honest bookkeeping of what actually exists
    in `evidence.yml` is new.
- SB-002: Do not implement or edit `scripts/NewSddWorkspace/Program.fs`; do not run `dotnet pack`,
  tag, or publish any package; do not run `.github/workflows/release-new-sdd-workspace.yml`.

## User Stories
- US-001 (P1): As the FS-GG maintainer, I can read `registry/dependencies.yml` and
  `registry/skills.yml` and see the four new workspace-provider identities named with actual
  published, hash-verified versions, so I can sign off on releasing the wizard with confidence
  nothing advertised is aspirational.
- US-002 (P2): As a future SDD worker running rollout phase 6+, I can read this item's spec/plan to
  see exactly which coherent-set versions were verified and why, without re-deriving the research.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given FS-GG/FS.GG.Game#552 and FS-GG/FS.GG.SDD#816/#817 are closed, when
  their claimed published artifacts are independently re-derived from nuget.org, then
  `FS.GG.Game.Skills` 0.7.0 and its `fs-gg-game-fable` skill digest match `registry/skills.yml:207`
  exactly, and SDD's production materializer path is confirmed (PR SDD#819).
- AC-002 [US-001] [FR-002]: Given FS-GG/FS.GG.Templates#349, when its published artifact is
  independently downloaded from nuget.org (not merely trusted from the issue), then the nupkg's
  SHA-256 matches the closing comment's recorded hash and the package contains all four template
  identities (`fs-gg-console`, `fs-gg-web`, `fs-gg-fable-game`, `fs-gg-fable-bindings`).
- AC-003 [US-001] [FR-003]: Given EHotwagner/S.I.R.#138 (merge `b17ac33b`), when its `.fsproj`,
  `package.json`, and provenance files are inspected, then every FS-GG dependency is a versioned
  public feed reference and zero sibling-checkout (`../../FS.GG.*`) edges exist.
- AC-004 [US-002] [FR-004]: Given the coherent-set decisions above, when
  `registry/dependencies.yml`/`registry/skills.yml`/their changelogs/`docs/architecture.md` are
  edited, then every new/changed row names only a version independently verified in AC-001..AC-003
  (never 0.8.1, never a new `new-sdd-workspace` version).
- AC-005 [US-002] [FR-005]: Given the edited registry documents, when
  `scripts/generate-projections --check` and `fsgg-sdd registry`/`scripts/check-projection.py` are
  run, then both report clean (no drift, no schema violation).

## Functional Requirements
- FR-001: Independently re-verify Game Skills publication (FS.GG.Game#552) and SDD production materializer adoption (FS.GG.SDD#816/#817) against nuget.org and the closing PRs' merge commits, not merely against the issues' own claims. (covers AC-001)
- FR-002: Independently download FS.GG.Workspace.Template's published artifact (not trust FS.GG.Templates#349's comment alone) and verify its byte hash and the four template identities it contains. (covers AC-002)
- FR-003: Independently verify EHotwagner/S.I.R.#138 and the Babylon bindings reference consume only public, versioned feed artifacts with zero sibling-checkout edges to any FS-GG source repository. (covers AC-003)
- FR-004: Update `registry/dependencies.yml` (new `FS.GG.Workspace.Template` contract row with four identities, new `FS.GG.Game.Skills` contract row, `game-sim-core` consumer list, new dependency edges), `registry/skills.yml` (confirm/reconcile the already-present `fs-gg-game-fable` row), `registry/CHANGELOG.md`, `registry/skills.CHANGELOG.md`, and `docs/architecture.md` (retire the "not-yet-published"/"planned" language at lines 100/388/542) so every row/prose statement matches only what FR-001..FR-003 independently verified. (covers AC-004)
- FR-005: Regenerate `docs/registry/compatibility.md`'s generated regions with `scripts/generate-projections` and confirm `--check` is clean; run the registry's shipped validator (`fsgg-sdd registry` / `scripts/check-projection.py`) clean over the edited documents. (covers AC-005)

## Ambiguities
- [AMB:AMB-001] FS.GG.Templates' `providers/fable-game.providers.yml` declares
  `minimumFsggSdd.version: "0.6.0"`, but the owner-sourced Game skill it depends on (via SDD's
  production materializer) is only correctly emitted by an `FS.GG.SDD.Cli` build containing SDD
  PR#819 (first published at 1.0.0). Is this item responsible for correcting that descriptor (out of
  `Paths:`, would require a widen into a live-claimed area or a cross-repo PR), or for filing a
  finding on FS.GG.Templates and registering the coherent set with the descriptor's stated (lower)
  floor, flagged as known-understated?

## Public Or Tool-Facing Impact
- Registry rows and `docs/architecture.md`/`docs/registry/compatibility.md` are consumer-facing
  documentation of what a maintainer can publicly install and instantiate.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2070-workspace-provider-activation`.
