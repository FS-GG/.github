---
schemaVersion: 1
workId: 2725-cli-kernel-extraction
title: Cli Kernel Extraction
stage: plan
changeTier: tier1
status: planned
sourceSpec: work/2725-cli-kernel-extraction/spec.md
sourceClarifications: work/2725-cli-kernel-extraction/clarifications.md
sourceChecklist: work/2725-cli-kernel-extraction/checklist.md
publicOrToolFacingImpact: true
---

# Cli Kernel Extraction Plan

Prose status: planned

## Source Snapshot
- spec: work/2725-cli-kernel-extraction/spec.md sha256:f4d490271c557ce53a55799f561de0c42e23733eb97a32ebee3e57940531786f schemaVersion:1
- clarifications: work/2725-cli-kernel-extraction/clarifications.md sha256:2b8b03d6721374c488fb2623d202a19031daeed76e64585d7b323bb2f7cb3892 schemaVersion:1
- checklist: work/2725-cli-kernel-extraction/checklist.md sha256:a78148337d84ff99d7b2decacd1943cdb7096deaa8b9e1d24ad7426b0f56c200 schemaVersion:1

## Plan Scope
- Work item 2725-cli-kernel-extraction is planned from the current specification, clarification, and checklist facts.
- Requirement count: 8.
- Clarification decision count: 3.
- Checklist result count: 8.

### The boundary, stated exactly

`src/FS.GG.Coord.Cli.Kernel` (namespace `FS.GG.Coord.Cli`, unchanged), compiled in this order:

| # | Module | Origin | Kind |
|---|---|---|---|
| 1 | `Json` | `src/FS.GG.Coord.Cli/Json.{fs,fsi}` | relocated verbatim (72 + 42 lines) |
| 2 | `Options` | `src/FS.GG.Coord.Cli/Options.{fs,fsi}` | relocated verbatim (2,099 + 383) |
| 3 | `Identity` | `src/FS.GG.Coord.Cli/Identity.{fs,fsi}` | relocated verbatim (224 + 107) |
| 4 | `RefParsing` | `src/FS.GG.Coord.Cli/RefParsing.{fs,fsi}` | relocated verbatim (57 + 29) |
| 5 | `Render` | `src/FS.GG.Coord.Cli/Render.{fs,fsi}` | relocated verbatim (696 + 263) |
| 6 | `Kernel` | extracted from `src/FS.GG.Coord.Cli/Client.fs` | **the extraction** |

`Kernel` holds five clusters, each a contiguous block of `Client.fs` today:

1. The exit-code vocabulary — `ExitGreen`, `ExitError`, `ExitRed`, `ExitNoVerdict`, `ExitNone`,
   `ExitContended`, `ExitPending`, `ExitNotOpen`.
2. The stderr and failure helpers — `eprint`, `failWith`, `fail`, `boardWriteNote`, `env`.
3. The ambient `Context` record and `usesLiveHttp`.
4. Ref parsing bound to that context — `parseRefIn`, `parseRef`.
5. Worker resolution and the refusals — `worker`, `oneArg`, `sessionOf`, `selfOf`, `mintRemedy`,
   `twinRefusal`, `impersonationRefusal`, `noteWorkerDisagreement`.
6. The checkout-scope readers — `resolveRepo`, `parseGitHubSlug`, `gitRemoteRepo`, `parseChoreLocks`,
   `scopedRepo`, `defaultRepoForOwner`, `defaultRepoScope`.

Cluster 6 is the one addition to the row's stated list, and it is what makes SB-004 reachable rather
than nominal: `parseChoreLocks` is the only `Client` binding `OptionsTests.fs` uses, and
`parseRefIn` + `defaultRepoForOwner` are the only two `RefParseTests.fs` uses. Moving those three (and
the private readers they compose with) turns 1,307 lines of test from "welded to `Client`" into
"covers the Kernel", which is the difference between a Kernel with a test client and one without.

### The lanes this boundary creates (FR-006)

`.github#2726`–`#2729` each extract one command family. After this work, adding family *F* means:

- create `src/FS.GG.Coord.Cli.<F>/` — a **new directory and new project file**, touched by nobody else;
- create `tests/FS.GG.Coord.Cli.<F>.Tests/` — likewise;
- delete that family's handlers from `src/FS.GG.Coord.Cli/Client.fs` and its entries from
  `Client.fsi`, and move its tests out of `tests/FS.GG.Coord.Cli.Tests/`.

The first two are disjoint by construction. The third is **shared residue**, and this plan states so
rather than claiming a disjointness it does not have: `Client.fs`, `Client.fsi` and
`FS.GG.Coord.Cli.fsproj` remain a common editing surface until the last family leaves. What changes is
that the *shared base* is no longer part of that residue — no family extraction needs to touch
`Options`, `Render`, `Json`, `Identity`, `RefParsing` or the Kernel module at all, and none needs to
add a `<Compile Include>` line to `FS.GG.Coord.Cli.fsproj` for the code it depends on. That is the
concrete concurrency this work buys, and FR-006's evidence is the enumeration above rather than an
assertion that the residue vanished.

### Identified next seam, not taken here

`Snapshot` (1,169 + 118 lines, depends only on `Json`) is the obvious next candidate, and moving it
would let `SnapshotTests.fs`, `ScanRoundTripTests.fs` and `RuleSubsetTests.fs` follow. DEC-001 keeps
it out of this work; it is recorded here so the next row does not rediscover it.

### Blast radius outside the declared paths

Relocating `Options.fs` and adding a `ProjectReference` are each observable to machinery that names
paths literally. This plan enumerates it before touching anything, because a hard-coded subject path
that silently stops matching is the failure mode this repository has the most history with.

- `scripts/check-paths-coherence.py` rule (b) computes each workflow's project closure from
  `ProjectReference` and requires every closure member to be selected by the workflow's `paths:`. The
  new reference therefore reds `coord-engine.yml`, `graphql-monopoly.yml`, `projections.yml` and
  `skill-quality.yml` until each gains `src/FS.GG.Coord.Cli.Kernel/**` in both trigger blocks.
- `scripts/check-worker-id-attractor.py` hard-codes `DISPATCH_SOURCE =
  "src/FS.GG.Coord.Cli/Options.fs"` and **fails open** when it is absent — its rule 5 audits nothing
  and the gate exits green. This is the highest-severity item in the list precisely because it is the
  one that does *not* announce itself.
- `scripts/fsgg-coord-guards.sh` (`ENGINE_SOURCE_TREES`) and `scripts/check-engine-freshness.py`
  (`ENGINE_SOURCE`) each hard-code the three engine trees; a Kernel-only edit would be judged neither
  stale nor an authored engine source.
- `scripts/check-engine-build-determinism.py` grades exactly three assembly stems, so the Kernel's
  `DeterministicSourcePaths` would be asserted by nothing; its `clean()` also hard-codes three project
  directories, and adding the stem without adding the directory would grade a stale artifact.
- Workflows and fixtures that `grep` `Options.fs` by literal path: `recipe-followup.yml`,
  `recipe-landable.yml`, `fsgg-dispatch-broker-selftest.yml`, `tests/dispatch-broker/run.sh`,
  `tests/worker-id-attractor/run.sh`.

Each of these is repaired in this work, and the touch-set is widened to cover them before any of them
is edited.

## Plan Decisions
- PD-001 [AC-001] [FR-001] complete: Create `src/FS.GG.Coord.Cli.Kernel` with `IsPackable=false`, `TreatWarningsAsErrors`, `GenerateDocumentationFile`, `InvariantGlobalization` and `DeterministicSourcePaths` mirroring the sibling coord projects, referencing `FS.GG.Coord.Core` and `FS.GG.Coord.GitHub`; add the `ProjectReference` from `FS.GG.Coord.Cli`; generate and commit `packages.lock.json` for the new project and regenerate the CLI's, because locked restore is inherited and every consuming workflow passes `--locked-mode`.
- PD-002 [AC-002] [FR-002] complete: Record the pre-change suite total, move exactly the test files that cover moved modules, and reconcile the post-change totals arithmetically rather than reporting each suite green in isolation — a suite that runs nothing also reports green.
- PD-003 [AC-003] [FR-003] complete: `tests/FS.GG.Coord.Cli.Kernel.Tests` references only `FS.GG.Coord.Cli.Kernel`. It carries its own `AssemblyInfo.fs` disabling xUnit class parallelisation, because `IdentityTests.fs` mutates process-global environment variables; it does not carry a copy of `CacheSandbox.fs`, because nothing in the moved set reaches the scan cache.
- PD-004 [AC-004] [FR-004] complete: `Client` reaches the extracted bindings by `open FS.GG.Coord.Cli.Kernel`, so its own ~350 `eprint`, ~143 `fail` and ~100 exit-literal sites keep the spelling they have and the extraction diff stays readable; only external call sites (the test projects) gain the `Kernel.` qualifier.
- PD-005 [AC-005] [FR-005] complete: Confirm AC5 by execution — `dotnet pack` the tool, enumerate the real `.nupkg` entries, and drive `scripts/release-saga.py`'s own payload function over the produced artifact, rather than reading the enumeration code and concluding it is dynamic.
- PD-006 [AC-006] [FR-006] complete: State the lanes as the enumeration under *The lanes this boundary creates*, including the residue that remains shared, and put that statement in the pull-request body where the next four rows will read it.
- PD-007 [AC-007] [FR-007] complete: Every `.fs` in the Kernel project is preceded by its `.fsi` in the compile list; the new `Kernel` module gets a hand-authored signature and the five relocated modules keep theirs unchanged.
- PD-008 [AC-008] [FR-008] complete: Apply the Documentation Siting Rule mechanically and report the outcome as counts — lines carried into `Kernel.fsi` from `Client.fsi`, lines carried into `Kernel.fs` from `Client.fs`, and signature entries newly authored for declarations whose visibility changed.

## Contract Impact
- PC-001 [PD-001] project graph: `src/FS.GG.Coord.Cli.fsproj` gains one `ProjectReference` and loses ten `<Compile Include>` entries; a new `src/FS.GG.Coord.Cli.Kernel.fsproj` and a new `tests/FS.GG.Coord.Cli.Kernel.Tests.fsproj` enter the build graph. Both new projects carry a committed `packages.lock.json`, because locked restore is inherited from the org-shared build config and every consuming workflow passes `--locked-mode` as a global property.
- PC-002 [PD-001] packed payload: the `FS.GG.Coord.Cli` tool package gains `FS.GG.Coord.Cli.Kernel.dll` and `FS.GG.Coord.Cli.Kernel.pdb` under `tools/net10.0/any/`, taking the coord entries from six to eight. Package id, tool command name and version are unchanged.
- PC-003 [PD-004] module surface: `Client`'s public surface loses the eight exit literals, `Context`, `parseRefIn`, `parseGitHubSlug`, `defaultRepoForOwner` and `parseChoreLocks`; they are public on `Kernel` instead. The four bindings production code outside the module requires — `run`, `whoami`, `followupAudit`, `predicate` — are untouched.

## Verification Obligations
- VO-001 [PD-001] [PC-001] semanticTest: Build every project with `TreatWarningsAsErrors` on and run all four test projects, then exercise the built tool through its real entry point (`--help`, `whoami`, a JSON decision verb) so the assembly split is observed at runtime and not only at compile time.
- VO-002 [PD-002] [PC-003] suiteReconciliation: Record `Total:` from every test project before and after, and show the arithmetic. A per-suite "Passed!" is not evidence that the same tests ran.
- VO-003 [PD-003] [PC-003] negativeReference: Prove the Kernel test project cannot see `FS.GG.Coord.Cli` — not merely that it does not name it — by observing the compile error when a Kernel test references a `Client` binding.
- VO-004 [PD-005] [PC-002] executedPayload: Enumerate the packed entries from the real `.nupkg` and run the release saga's payload comparison over it. Reading the source is not the confirmation AC5 asks for.
- VO-005 [PD-001] [PC-002] gateInversion: For each gate repaired in the blast-radius list, demonstrate it can fail — in particular that `check-worker-id-attractor.py` exits green over an absent `DISPATCH_SOURCE`, which is what makes that repair load-bearing rather than cosmetic.

## Performance Intent
No performance intent is declared for this work item.

## Migration Posture
- PM-001 [PC-001] noConsumerMigration: Nothing downstream migrates. The namespace is unchanged across the assembly boundary, so `open FS.GG.Coord.Cli` still reaches every relocated module; receivers restore the tool by package id and never name an assembly. The only call sites that change spelling are inside this repository's own test projects, and the compiler finds all of them.

## Generated View Impact
- GV-001 [PD-001] workModel: `readiness/2725-cli-kernel-extraction/work-model.json` and `analysis.json` are regenerated by `fsgg-sdd analyze` from the authored sources above and are committed with the change. No other generated view in this repository projects over `src/` project membership, so none goes stale from the new project itself; the gates that hard-code project lists are repaired as source edits under *Blast radius outside the declared paths* rather than regenerated.

## Accepted Deferrals
No accepted plan deferrals recorded.

## Planning Findings
No blocking planning findings recorded.

## Advisory Notes
- Optional Governance pointers remain compatibility facts only.
- `Snapshot` remains in `FS.GG.Coord.Cli` by DEC-001, and is recorded above as the identified next
  seam so the next row does not rediscover it.
- The build-time case for this split is refuted on the row's own evidence and stays refuted here.
  Lane concurrency is the whole case; FR-006 is therefore not a formality.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd tasks --work 2725-cli-kernel-extraction`.
