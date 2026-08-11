---
schemaVersion: 1
workId: 2402-coherent-set-versioning
title: Coherent Set Versioning
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2402-coherent-set-versioning/spec.md
publicOrToolFacingImpact: true
---

# Coherent Set Versioning Clarifications

## Source Specification
- work/2402-coherent-set-versioning/spec.md

## Clarification Questions
No clarification questions recorded.

## Answers
No clarification answers recorded.

## Decisions
- DEC-001: The "Judgement to surface" reconciliation with .github#2396 is resolved as that item's own
  text anticipates: `.github#2396` governs the permitted lag between a **receiver's** own
  `fs.gg.coord.cli` pin (in a repository outside this coherent set, e.g. `.config/dotnet-tools.json`
  in FS.GG.SDD/Rendering/Governance/Templates/Game/Audio/Net) and the registry's declared engine
  version. This item governs drift **inside** the set — between FS.GG.Kit, FS.GG.Drivers and
  coord-engine's own declared `<Version>` scalars. The two are disjoint subjects (no receiver pin is
  a member of this coherent set, and no member of this coherent set is a receiver of itself) and
  neither item's fix touches the other's Paths, so there is no implementation conflict — only the
  shared vocabulary ("permitted drift") that made them look like the same problem. Verification:
  `.github#2396`'s body names its subject as `scripts/repos-audit.sh`'s receiver-pin sweep across
  seven OTHER repositories; this item's declared `Paths:` names none of them.
- DEC-002: Direct inspection of every coherence gate named in this item's evidence section
  (`scripts/check-source-coherence.py`, `check-feed-coherence.py`, `check-pin-coherence.py`,
  `check-engine-pin.py`, `check-kit-published-coherence.py`, `check-lock-ranges.py`,
  and `contract-coherence.yml`'s registry-schema validation) found that **none of their subjects is
  drift between FS.GG.Kit, FS.GG.Drivers and coord-engine's own version scalars** — the pairing this
  item's Evidence section quotes from `check-engine-pin.py`'s docstring (source-coherence,
  feed-coherence, engine-freshness, pin-coherence) describes four angles on **one package's own**
  registry/source/feed/pin agreement (there `.github`'s canonical dist pin vs. the coord-cli feed),
  replicated per-package across the registry (`fsgg-contracts` for source-coherence;
  many packages for feed-coherence), not a cross-package assertion between Kit, Drivers and
  coord-engine. Concretely: `check-source-coherence.py`'s subject is `fsgg-contracts`
  (FS.GG.SDD), unrelated to this set. `check-feed-coherence.py` iterates every registry contract
  carrying `package-version` — today that list does not include FS.GG.Kit or FS.GG.Drivers at all
  (`registry/dependencies.yml` carries no `kit` or `drivers` contract row; only `coord-engine` is
  registered, at `registry/dependencies.yml:900`). `check-pin-coherence.py`'s subject is
  Renovate-annotated pins the registry validator reads across the fleet — receiver-side, `.github#2396`
  territory, not within-set. `check-engine-pin.py`'s subject is `.github`'s OWN dist tool-manifest
  pin for coord-cli against the coord-cli feed alone — a single-package freshness check unaffected by
  whether Kit/Drivers share its version scalar. `check-kit-published-coherence.py` asserts published
  FS.GG.Kit content matches its canonical on-disk manifest (file identity), never a version
  comparison against Drivers or coord-engine. `check-lock-ranges.py` asserts project-reference ranges
  track their OWN project's declared version — trivially still true once all three share one scalar,
  but not a cross-package assertion either. **Conclusion: zero of the named gates are deletable**,
  because none was ever asserting the cross-package equality this item's title proposes making
  "unrepresentable" — that equality was asserted by NO gate before this change (confirmed by the
  20-minutes-apart drift in commits f26da6ed/d48e1ec2 going undetected). This corrects the item's
  Acceptance Criteria §3 premise; SB-004/FR-004 discharge as "every named gate evaluated, all seven
  justified to keep, zero deletable, evidence attached" rather than by finding gates to delete. This
  is not a decision available to make unilaterally in the other direction (inventing a deletion that
  is not real would satisfy the letter of AC3 while making the PR's evidence false), so it is recorded
  here for the critic and the maintainer to confirm rather than silently resolved.
- DEC-003: Given DEC-001 and DEC-002, and that `release-kit.yml` / `release-drivers.yml` /
  `release-coord-engine.yml` collectively implement the SOLE distribution path for the coordination
  engine the entire fleet depends on to run `fsgg-coord` at all (`release-coord-engine.yml`'s own
  header: "a receiver repo that cannot restore it has no coordination tool at all — there is no
  second engine to fall back to"), consolidating those three workflows into one, and then actually
  cutting and dual-feed-publishing a real coherent-set release, is out of this pass's scope (SB-005).
  That is a maintainer sequencing decision (which package publishes first in dependency order, what
  the new shared release trigger looks like, and a live rehearsal before the only path the fleet has
  is rewritten), not a call one bounded worker session should make unilaterally under time pressure.
  It is filed as follow-up FS-GG/.github#2409 so the maintainer can decide deliberately, per the
  same "state it explicitly rather than letting it land implicitly" instruction this item's own
  Evidence section gives for the #2396 split.
- DEC-004 (repair round 1, critic finding): `.github#2402`'s own AC4 — "`.github#2249`'s
  receiver-pin lag is expressible against the set version, and the PR states whether that item's
  fix becomes simpler or is unaffected" — is answered here: **unaffected**, grounded in `#2249`'s
  own text, not asserted. `#2249`'s AC3 is "a gate compares each receiver's pinned `fs.gg.coord.cli`
  against the registry's declared `coord-engine` version"; AC4's fallback (implemented as
  `.github#2396`) is "criterion 3's gate encodes the permitted lag instead of equality". Both read
  exactly ONE field: `registry/dependencies.yml`'s `coord-engine` row (`version`/`package-version`
  at `registry/dependencies.yml:900-902`) — the same field `scripts/repos-audit.sh`'s engine-pin
  sweep already reads today, unchanged. Two facts make the set-version property invisible to that
  comparison: (1) `#2249`'s own observed evidence is that a receiver's `.config/dotnet-tools.json`
  pins `tools["fs.gg.coord.cli"]` ONLY — no receiver pins FS.GG.Kit or FS.GG.Drivers by version at
  all, so there is no receiver-side subject for those two members to begin with; (2) this item does
  NOT flip `registry/dependencies.yml`'s `coord-engine` row (DEC-003/PC-002 — that is
  `.github#2409`'s scope, deliberately), so the field `#2249`/`#2396` read is byte-identical before
  and after this PR. "Expressible against the set version" is trivially true (`coord-engine`'s
  version now EQUALS `$(FsggCoherentSetVersion)` by construction, since it is one of the three
  members), but that equality is invisible to the sweep, which never reads Kit's or Drivers' version
  and would behave identically if `coord-engine` were still independently versioned. Neither the
  sweep's subject, its predicate, nor `#2396`'s permitted-lag encoding changes in shape or
  complexity — "unaffected" is the honest answer, not "simpler."
  One real, distinct consequence for the FOLLOW-UP (`.github#2409`), named there rather than left
  implicit: when a real coherent-set release is eventually cut and the registry is flipped, every
  receiver's next required bump is to whatever the coherent set's first published version is — a
  larger one-time jump than the incremental per-release cadence `#2396`'s permitted-lag bound was
  reasoned about (that decision compared receivers 1-2 minors behind a single-package release
  cadence, not a one-time cross-package realignment). That is a ROLLOUT consideration for
  `#2409`'s real release, not a change to `#2249`/`#2396`'s gate design, and this item is not the
  one that resolves it.

## Accepted Deferrals
- The workflow-consolidation and real dual-feed release (SB-005 / .github#2402's AC2 and AC7) are
  accepted deferrals to follow-up FS-GG/.github#2409, per DEC-003.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2402-coherent-set-versioning`.
