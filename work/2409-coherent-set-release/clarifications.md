---
schemaVersion: 1
workId: 2409-coherent-set-release
title: Coherent Set Release
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2409-coherent-set-release/spec.md
publicOrToolFacingImpact: true
---

# Coherent Set Release Clarifications

## Source Specification
- work/2409-coherent-set-release/spec.md

## Clarification Questions
No clarification questions recorded.

## Answers
No clarification answers recorded.

## Decisions
- DEC-001 (dependency-publish order): Direct inspection of all three project files
  (`src/FS.GG.Kit/FS.GG.Kit.csproj`, `src/FS.GG.Drivers/FS.GG.Drivers.csproj`,
  `src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj`) for `ProjectReference`/`PackageReference` elements found
  **zero cross-references among the three** — each depends only on external packages
  (`FS.GG.Coord.Cli` on its own in-repo `FS.GG.Coord.Core`/`FS.GG.Coord.GitHub`, neither of which is a
  member of the set). The three are a coherent set by shared VERSION SCALAR only
  (`$(FsggCoherentSetVersion)`, `Directory.Build.props`), not by a build/publish dependency graph, so
  there is no functional ordering requirement between them. The consolidated workflow therefore runs
  pack+verify for all three IN PARALLEL (three independent jobs), then gates every feed push behind
  ALL THREE succeeding (a single downstream job with `needs: [kit, drivers, coord-engine]`) — the
  ordering that matters is not "which package first" but "no push happens unless every package packed
  and verified clean", which is what actually closes AC2's gap ("nothing stops a maintainer tagging one
  without the other two"). Within the push job, the three `dotnet nuget push` invocations run
  sequentially in the fixed order kit → drivers → coord-engine (alphabetical; arbitrary but stated, for
  legible per-package step logs) — this ordering carries no functional weight since a failed push job
  fails the whole run before any dashboard-tick notifies a receiver of a partial state.
- DEC-002 (gate re-confirmation against the new workflow): Re-running .github#2402's DEC-002 evaluation
  against the new consolidated `release-coherent-set.yml` design (rather than the three independent
  workflows DEC-002 was written against) changes no conclusion: `check-source-coherence.py`
  (fsgg-contracts, unrelated), `check-pin-coherence.py` (receiver-side Renovate pins, unrelated),
  `check-engine-pin.py` (`.github`'s own dist tool-manifest pin, single-package, unaffected by a shared
  trigger), `check-lock-ranges.py` (per-project reference ranges, unaffected) and
  `check-kit-published-coherence.py` (Kit's own published-vs-source file identity, unaffected by which
  workflow performs the push) remain exactly as DEC-002 found them: each a per-package lattice entry,
  none asserting cross-package drift. `check-feed-coherence.py` gains no new subject: consolidating the
  TRIGGER does not add or remove which registry rows carry `package-version` (still only `coord-engine`
  — Kit and Drivers carry no registry contract row, per DEC-002's original finding, unchanged by this
  item). **New in this pass**: `scripts/check-coherent-set-version.py` (.github#2402's own new gate,
  named in this item's evidence trail as a consumer of the version scalar this item's release cuts) is
  re-examined too — its subject is the `<Version>` LITERAL agreement in the three project files at
  SOURCE time, which a release workflow change cannot affect (it runs pre-merge on every PR, not at
  release time) — still justified to keep, unaffected. **Conclusion: zero of the eight now-named gates
  are deletable or made redundant by the new release workflow design** — the same "zero deletable"
  verdict DEC-002 reached, re-confirmed rather than assumed, per this item's own AC3.
- DEC-003 (rollout fit against .github#2396's permitted lag): `.github#2396` (the permitted-lag gate for
  `scripts/repos-audit.sh`'s receiver engine-pin sweep) is, as of this decision, **not yet implemented**
  — direct inspection of `scripts/repos-audit.sh` finds no `permitted-lag`/lag-bound logic present, and
  `.github#2396`'s own issue body is filed `Blocked on: human/decision`, its Paths untouched. So there is
  no live bound to compare this release's jump against. But the jump size is decidable independent of
  that bound's eventual value: `registry/dependencies.yml`'s live `coord-engine` row is `0.23.0`
  (`registry/dependencies.yml:901`) and `FS.GG.Drivers.csproj`'s current independent version is `0.18.0`
  (pre-#2402), against the coherent set's shared `0.50.0` — a jump of **27 minors** for coord-engine and
  **32 minors** for Drivers (Kit's own jump, `0.49.0` → `0.50.0`, is trivial — 1 minor). `.github#2396`'s
  own text frames its candidate bound as "the obvious candidate — one MINOR" and discusses whether a
  *2*-minor outlier (`FS.GG.Net` at `0.21.1` against `0.23.0`) should instead force a receiver bump. A
  27–32 minor jump exceeds even a generous multiple of that candidate by more than an order of magnitude,
  so **no permitted-lag bound #2396 could plausibly choose covers this release** — this is not contingent
  on which bound is eventually picked. **Conclusion, stated per this item's own AC5 rather than left
  implicit: this release's rollout needs a coordinated fan-out in the shape .github#2249's own AC1/AC2
  describe (a deliberate bump PR to every one of the seven receivers, not a wait for Renovate/dashboard-tick
  alone), regardless of what #2396 eventually decides.** This item's `dashboard-tick.py` step (inherited
  unchanged from the three existing workflows) still ticks each receiver's Dependency Dashboard per
  .github#1923 — that remains the DELIVERY notification. The fan-out is the receiver-side BUMP action
  #2249 AC1/AC2 describe, which is out of this item's declared `Paths:` (this item touches only
  `.github`'s own release workflows, registry and docs) and is recorded here as owed rather than
  performed silently or assumed unnecessary.
- DEC-004 (workflow shape — REVISED from an earlier draft of this decision; superseding text kept out
  rather than silently edited, because the reversal is itself evidence): A first draft of this decision
  chose one consolidated workflow FILE (`release-coherent-set.yml`) replacing all three, on the theory
  that "one workflow, one `on:` block" most literally satisfies AC1. Before implementing it, each source
  workflow's own header was re-read in full rather than skimmed, and each states — independently, for
  its own package — that **nuget.org's Trusted Publishing (OIDC) policy is bound to the exact tuple
  (Repository Owner `FS-GG`, Repository `.github`, Workflow file `release-kit.yml` / `release-drivers.yml`
  / `release-coord-engine.yml`, package id), and "Renaming either afterwards invalidates the token
  exchange" (.github#624)**. Those three nuget.org policies are configured on nuget.org's own site, not
  in this repository — nothing in this tree can read or verify their live state, and this item's worker
  has no nuget.org credential to inspect them directly. Deleting the three files and introducing
  `release-coherent-set.yml` would therefore silently invalidate all three OIDC trust bindings the next
  time a real publish ran, and `NuGet/login` would fail-close with a 401 on nuget.org specifically for
  `FS.GG.Coord.Cli` — the package the ENTIRE FLEET restores `fsgg-coord` from
  (`release-coord-engine.yml`'s own header: "a receiver repo that cannot restore it has no coordination
  tool at all"). That is exactly the failure mode ADR-0039 names as invisible ("not an error, just an
  empty version list") and exactly the kind of unilateral, unverifiable-by-one-worker action this item's
  own root cause says should not be made without a maintainer's deliberate sequencing decision — an
  org-owner would need to pre-create three NEW Trusted Publishing policies against the new filename
  BEFORE the real cut, which is a live nuget.org UI/API action this worker cannot perform or confirm.
  **A second draft (still in this same decision, superseded before implementation for a second,
  independent reason) then proposed converging all three tag namespaces onto one shared pattern
  (`coherent-set/v*`) while keeping the three filenames unchanged.** Before implementing THAT, each
  source workflow's header was, again, read in full rather than skimmed for the parts already used —
  and `release-kit.yml`'s own header states a SECOND load-bearing fact about the `kit/v*` namespace
  specifically: "since .github#1772, ALSO the ref the fleet resolves a bump-shape rule from... The
  receiver-side `kit-bump-shape` reporter now resolves the rule it runs from `kit/v<the version the
  receiver pins>`." That reporter lives in a RECEIVER repository (outside this `.github` checkout,
  unreadable from here without a cross-repo fetch this item's `Paths:` does not authorize), and its
  exact tag-string parsing cannot be audited from this tree. Retiring the `kit/v*` namespace in favor
  of `coherent-set/v*` — even preserving the filename — would silently stop `kit-bump-shape` from
  resolving a rule for every future kit version, breaking receiver-side bump-shape verification for a
  contract this worker cannot see the other end of. The same risk shape as the first draft (an
  unverifiable-by-this-worker cross-repo/cross-system contract, silently broken by a rename), just one
  layer deeper. **FINAL CHOICE: change nothing about tag namespaces, filenames, or `workflow_dispatch`
  shape in any of the three files.** Instead, add ONE new precondition to each file's existing
  publish-decision step (both the tag-push arm and the `workflow_dispatch publish=true` arm, alongside
  the existing .github#1772 own-tag check): before setting `push=true`, `git ls-remote` the OTHER TWO
  packages' own tags at the SAME evaluated version (e.g. `release-kit.yml` additionally requires
  `drivers/v<version>` and `coord-engine/v<version>` to exist and resolve to the SAME commit SHA as the
  version being packed) and refuse, fail-closed, naming exactly which sibling tag is missing and the
  remedy, if either does not. This is the identical shape to the existing #1772 precondition, extended
  from "my own tag exists" to "my own tag AND both siblings' tags exist, at the same commit" — no new
  mechanism, no renamed contract surface, and it directly closes AC2's actual complaint: a maintainer
  who tags only `kit/v0.50.0` and pushes it can no longer complete a publish, because the sibling-tag
  precondition refuses until `drivers/v0.50.0` and `coord-engine/v0.50.0` also exist at the same commit.
  All three tags can be created and pushed in one `git push origin kit/v0.50.0 drivers/v0.50.0
  coord-engine/v0.50.0` (multiple refs, one push event) — this is the "one shared trigger" AC1 asks for,
  expressed as one push action containing all three refs rather than one NEW tag namespace, and it is
  the choice that costs zero changes to any contract (nuget.org OIDC, `kit-bump-shape`, tag-immutability
  rulesets) this worker cannot fully see or verify the other end of.
- DEC-005 (dependency-publish order, restated against the final DEC-004 design): unchanged from the
  original finding — the three packages carry no `ProjectReference`/`PackageReference` among each other
  (verified by grep across all three project files), so there is no functional publish-order
  requirement. The sibling-tag precondition (DEC-004) makes ordering moot in a stronger way than the
  earlier parallel-jobs draft did: no workflow's publish step can complete until every package's tag
  exists, so whichever of the three GitHub Actions runs finishes its own resolve/restore/verify/pack
  steps LAST is naturally the one whose publish actually proceeds first in wall-clock terms if the
  others already passed their own precondition — there is no ordering to state beyond "all three tags,
  same commit, before any push," which is what DEC-004 implements. This decision id is kept (rather
  than merged into DEC-004) because AC1 asks for the dependency-publish order to be stated explicitly,
  and "none exists, here is why, here is the mechanism that makes ordering moot" is that statement.

## Accepted Deferrals
- The actual tag push that triggers the real dual-feed publish, the registry flip, and the receiver
  restore verification are execution actions that happen AFTER this PR merges (the workflow that
  performs them does not exist on `main` until it does) — tracked as this item's post-merge obligations,
  not deferred out of the item's scope.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2409-coherent-set-release`.
