# Cohort 90 batched enrollment: auto-added Delivery cards

This carrier is the first real batched enrollment through the [#4941](https://github.com/FS-GG/.github/issues/4941) planner. It runs on installed coherent 0.114.0, with no per-cohort wrapper and no constructor rebuild. It delivers the public commitment of one successor pair. The pair appends 114 rows to the 93 rows of the predecessor (cohort 89, protected at `ebce547e7b2c4d61ec4d2673fdc2ee1b0572633d`), for 207 rows. No predecessor row, order or node id changes.

The 114 rows are Delivery cards that Project 3's auto-add put on the board on 2026-10-31 with no Status, Track or Work level. Auto-add is now off. By repository: .github 62, Coordination 20, Unity.Client 8, Rendering 7, Game 6, Governance 4, SDD 4 and Templates 3. Root's decided targets for the 128 affected cards are 40 Done, 33 Backlog, 31 Blocked, 10 In progress, 7 Ready and 7 In review. Of the 40 Done cards, 33 already carry Done and lack only Track and Work level, and 7 are closed with every field empty. No card in the population was enrolled and closed but not Done.

| Fact | Value |
|---|---|
| Predecessor rows | 93 |
| Appended rows | 114 |
| Total rows | 207 |
| Commitment SHA256 | `d2eb7066d12dbeabead9060888b38b1b3090c7c42eb9e2648d9012e5f497a56a` |
| Public blob SHA256 | `72d310f94804abadf8a6710d6c03943baada52e2a4506cbcf4203f6012fcca73` |
| Private binding SHA256 | `02932ceb20a788100ea31efcc936f5029590068829a67533235caf9b3034b5c9` |
| Constructor source revision | `3c13e67f56de25041c6a5f9de85c54d178134956` |
| Constructor SHA256 (`FS.GG.Coord.GitHub.dll`) | `f76941b2a90a323c9b5d2d7518324a41108fb3291fba82f53abceb041cd1803a` |
| Engine SHA256 (`fsgg-coord-engine.dll`) | `2e21c9a612ba69c846557bef9eee67ba0857ffcd151b83e3332037c01e6aee51` |
| Predecessor prefix byte-identical | yes |
| Payload header and binding fields unchanged | yes |
| Commitment recomputed from the private pair | yes |

The [public manifest](board-v2-import-manifest.json) carries only the two-field commitment document. The private payload, nonce and binding stay outside source.

## Route and receipts

Each stage below printed its own result.

1. `enrollment resolve-issues` printed "native facts resolved; issues=114; read-only". All 114 are issues and Project members, read with complete visibility. None carries a human Roadmap, and all 15 plan documents are on `main`. A second resolve before planning was byte-identical.
2. The first `route-preflight` on `PlanSuccessor` returned `Unsupported` (exit 10). Its only unsupported check was `ConstructorIdentity` (`ConstructorDiffersFromMain`): the installed constructor is `3c13e67f`, live `main` was `d4ce5093`, and the check compares commit ids. The worker stopped there.
3. Root then decided to assert `--main-revision 3c13e67f…`. The basis is that `git diff --stat 3c13e67f..<live main>` over `src/FS.GG.Coord.Cli`, `src/FS.GG.Coord.GitHub`, `src/FS.GG.Coord.*` and `tools/BoardV2Import` is empty, together with the assembly hashes above. The diff was re-checked as empty immediately before each stage. The defect is filed as [#5113](https://github.com/FS-GG/.github/issues/5113) under #4730.
4. With the assertion and the predecessor runtime binding supplied, `PlanSuccessor` and `PrepareSuccessor` returned exit 11. Seventeen of the 18 checks were `SupportedForNextStage`. The remaining check was `ProjectAutoAdd` Unknown (`BareMembershipsObserved:128`), and its subjects are exactly the 128 cards this cohort repairs.
5. `enrollment plan` printed "successor planned; rows=207 appended=114; offline only". `enrollment prepare` printed "successor pair prepared; offline only", and its receipt reports `prefixPreserved`, `headerPreserved` and `recomputedCommitment` all true.
6. Separate Python code confirmed each of the following:
   - the predecessor prefix and the first 93 rows are byte-identical;
   - the header and every binding field other than `selectedIssues` and `populationRevision` are unchanged;
   - the 114 appended rows match the request in order and Roadmap;
   - the nonce is fresh;
   - both the cohort 89 commitment and this commitment recompute from their private pairs.

## Roadmap rule

Root decided that each row inherits the Roadmap already enrolled for its native parent. All 128 parent links and open/closed states were read natively before planning, with 0 mismatches against the target file. For 106 rows the value equals the parent's enrolled Roadmap exactly. The 8 children of Coordination#1026 are Coordination#1046, #1093–#1098 and SDD#1147. Their parent's enrolled value carries the `#original-publisher-failure-and-complete-policy-repair--2026-10-14` anchor, and a protected-document plan cannot carry an anchor. These 8 rows therefore enroll the same document, [`docs/roadmaps/2026-10-05-shared-process-supervision.md`](../roadmaps/2026-10-05-shared-process-supervision.md), without the anchor. Root accepted this.

## Excluded and held

- **Known exceptions.** [FsQuint#50](https://github.com/FS-GG/FsQuint/issues/50) and [SC2.Client#49](https://github.com/FS-GG/FS.GG.SC2.Client/issues/49) are in repositories outside the binding's nine. They stay unenrolled board cards, with no raw field writes.
- **Held cohort, SDK-PATCH-01.** [#4935](https://github.com/FS-GG/.github/issues/4935) and its children #4943–#4947, #5088, #5089, #5090, Templates#831 and Templates#832 have no admissible owning-plan document. [#5114](https://github.com/FS-GG/.github/issues/5114) owns adding one.
- **Held: .github#4811.** It is now a native sub-issue of [#5097](https://github.com/FS-GG/.github/issues/5097) (OPS-MAINT), as Root decided. #5097 is itself unenrolled and declares no owning plan, so there is no Roadmap for #4811 to inherit, and it stays out of this cohort.

## After merge

Root reviews and merges this carrier. Then:

1. `bind-runtime` changes only the successor private binding's `populationRevision` to the actual merge, after the `BindRuntime` preflight.
2. The installed 0.114.0 reader qualifies the complete 207-row population read.
3. Absent-value initialization of Status, Track, Roadmap and Work level for the 114 cards runs through `board-v2 migration-prepare` and `migration-apply`. The cap is 100 issues per scope, so it runs as two serialized batches with complete readback between them.

Existing values are never overwritten. The 33 cards already Done keep that value. Offline construction and this carrier grant no native write, implementation admission or Ready decision. Refs [#4941](https://github.com/FS-GG/.github/issues/4941) and [#4730](https://github.com/FS-GG/.github/issues/4730).
