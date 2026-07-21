# drive-board run — 2026-07-21

A `/drive-board` run over the org-level **Coordination** board. The host reconciled the board,
sized each wave, spawned one fresh disposable subagent per schedulable item (each running
`pnext-item` to merged + done-stamped), verified every claimed result against ground truth
(never the subagent's word), and re-planned after each wave. The board grew twice mid-run (the
operator added the consumer-docs and workBoard epics); each addition was reconciled into the loop.

**Outcome:** the board is burned down to a single deliberately-deferred backlog item. **Three epics
closed, ~24 PRs merged across all 8 repos, 4 packages published.** No rate-limit backoff was needed.

## What shipped, by repo

### FS.GG.Game
- **#449** → PR#450 — created `FS.GG.Game.Skills`, a content-addressed package for the owner-authored
  `mirrored:false` product skills (the byte-source the epic assumed existed but did not).
- **#451** → PR#452 — `skills-package.yml` (CI verify) + `release-skills.yml`; **published `FS.GG.Game.Skills 0.1.0`** (org feed + nuget.org, OIDC Trusted Publishing).
- **#454** → PR#455 — flipped `fs-gg-{game-core,audio,persistence,model-swap}` `mirrored:true → false` in the producer manifest; **republished `FS.GG.Game.Skills 0.2.0`** (13 skills).
- **#453** → PR#456 — docs M3: Acquire section + library quick-start.

### FS.GG.SDD
- **#623** → PR#627 — the scaffold materializer now sources owner skill bytes from the pinned, content-addressed package (not the frozen provider template).
- **#624** → PR#629 — scaffold backfill + `fsgg-sdd upgrade` behavior (non-destructive re-seed); also answered #620.
- **#631** → PR#634 — adopted `FS.GG.Game.Skills 0.2.0` (deliver the four flipped skills via package).
- **#628** → PR#630 — docs M4: de-counted the platform framing.
- **#632** (workBoard W4) → PR#636 — re-pinned onto `FS.GG.Drivers 0.2.0`; **republished `FS.GG.SDD.Cli 0.20.0`**.
- **#633** (workBoard W5) → PR#638 — acceptance test asserting both the coordination-wired happy path and the `--no-coordination` graceful-fail.

### .github
- **#1308** → closed as superseded (the byte-transport belongs in FS.GG.Game, not `.github` — carrying it here would be the frozen-copy ADR-0058/0062/0063 forbid).
- **#1318** → PR#1319 — **amended ADR-0063** to record the operator decision: the four game skills go uniformly owner-sourced (`mirrored:false`); the mirrored path is retired for them.
- **#1310** → PR#1316 — docs M1 (org front door / profile).
- **#1311** → PR#1317 — docs M2 (the consumer-README standard).
- **#1312** → PR#1322 — docs M5 (reconcile the consumer guide).
- **#1313** → PR#1328 — docs M6 (`generate-projections`: version/count fragments rendered from the registry — the durable fix).
- **#1325** (workBoard W1) → PR#1327 — authored `workBoard/SKILL.md` (both skill roots, byte-identical).
- **#1326** (workBoard W3) → PR#1329 — added workBoard to `FS.GG.Drivers`; **published `FS.GG.Drivers 0.2.0`**.
- **(auto) PR#1320** — `skill-registry-autofix` reconciled `registry/skills.yml` to `mirrored:false` for the four (triggered early rather than waiting on the hourly cron).

### FS.GG.Rendering
- **#965** → PR#970 — **retired the frozen `--profile game` mirror** of the four game skills (44 files: deleted the dirs, moved them `Mirrored → NoCounterpart`, regenerated the manifest, fixed template.json / skill-refs / test rosters). The final step of ADR-0063's game class.
- **#966** → PR#969 — docs M4: a `generate-doc-fragments.fsx` renderer + **re-aimed the Feature 242 gate** from "a version/count literal is present" to "the value lives in a generated fragment and equals source" (resolving #968).

### FS.GG.Audio / FS.GG.Net / FS.GG.Governance / FS.GG.Templates
- **Audio#191** → PR#192, **Net#10** → PR#11 (+ new `docs/`), **Governance#294** → PR#295, **Templates#265** → PR#266 — consumer-docs M3/M4: acquisition sections and de-counted framing to the M2 standard.

## Epics closed
- **SDD#622** — Owner-sourced skill delivery (ADR-0063). Closed by #965.
- **.github#1314** (M3) and **.github#1315** (M4) — consumer-docs, rolled up by their last children (#453, #966). M1/M2/M5/M6 landed directly.
- **.github#1324** — Ship workBoard (ADR-0064). Closed by W5 (#633).

## Blockers discovered mid-work, and where they were filed (all resolved this run)
- **Game#449** — filed from #1308 when the worker found the byte-source did not exist and `.github` was the wrong place for it. Root cause, not a surface patch.
- **Game#451** — filed from #449 (the package existed but nothing published it).
- **Game#454** — filed from Rendering#965's wave-6 recon; became the operator-approved lead once the decision was made.
- **SDD#631** — filed by the host as the real prerequisite for #965 (publish-before-flip: scaffolds must deliver the four via 0.2.0 before the mirror is dropped).
- **Rendering#968** — the Feature 242 docs-currency gate mandated exactly what M2 forbids. Decided (operator-directed) as **option 2**: M6 renders the fragments; #966 re-aims the gate. Closed by #966.

## Decisions surfaced to the operator
- **Flip the four `mirrored:true` game skills to `mirrored:false`** (uniform owner-sourcing) vs. keep them mirrored — operator chose the flip; ADR-0063 amended (#1318).
- **nuget.org publish path for `FS.GG.Game.Skills`** — separate `release-skills.yml` + a dedicated Trusted-Publishing policy (`fs-gg-game-skills-publishing`), which the operator created.

## Packages published
`FS.GG.Game.Skills` 0.1.0 then 0.2.0 · `FS.GG.Drivers` 0.2.0 · `FS.GG.SDD.Cli` 0.20.0 — each verified present on nuget.org **and** the org feed before its item was done-stamped.

## Follow-ups noted (not filed — operator's call)
- **FS.GG.Game has no `scripts/generated-paths`** — so `verify-paths` mis-flags a regenerated manifest as drift (advisory only; the #498 fail-closed-noise shape one repo over).
- **`registry/dependencies.yml` `role` text is engineering-oriented** — the M6-generated component-inventory reads more technically than M1's curated prose; the fix is to improve the `role` strings in the registry (their single home), which also improves `docs/architecture.md`.

## Outstanding
- **FS.GG.SDD#626** (Backlog) — "driver backfill for existing scaffolds: doctor reports + upgrade re-seeds a missing driver" (FR-010, carved from #621). Deliberately deferred; not human-blocked. Left for the operator to schedule.

No item is parked on a human decision. No rate-limit (`EX_RATE`) backoff occurred.
