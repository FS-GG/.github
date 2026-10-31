# Cohort 90 batched enrollment: auto-added Delivery cards

This carrier is the first real batched enrollment through the [#4941](https://github.com/FS-GG/.github/issues/4941) planner. It runs on installed coherent 0.114.0, with no per-cohort wrapper and no constructor rebuild. It delivers the public commitment of one successor pair. The pair appends 114 rows to the 93 rows of the predecessor (cohort 89, protected at `ebce547e7b2c4d61ec4d2673fdc2ee1b0572633d`), for 207 rows. No predecessor row, order or node id changes.

The 114 rows are Delivery cards that Project 3's auto-add put on the board on 2026-10-31 with no Status, Track or Work level. Auto-add is now off. By repository: .github 62, Coordination 20, Unity.Client 8, Rendering 7, Game 6, Governance 4, SDD 4 and Templates 3. Root's decided targets for the 128 affected cards were 40 Done, 33 Backlog, 31 Blocked, 10 In progress, 7 Ready and 7 In review. One retarget during the apply moved Unity.Client#28 from In review to Done (see below), which makes 41 Done targets. The live board, read before each card's first write, shows that 36 of those 41 already carried Done and lacked only Track and Work level. The other 5 were closed with every field empty. No card in the population was enrolled and closed but not Done.

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

## Apply record

Root merged the carrier as [#5116](https://github.com/FS-GG/.github/pull/5116) (`dc9ac5b0780618bb3302a213a127fcace2185be7`, 16:40:23Z) after an independent approval. The manifest at that merge is byte-identical to the prepared public blob.

All 114 cards now carry their decided Status, Track, Roadmap and Work level. A final GraphQL readback, one read per original batch scope, found 114 of 114 cards complete, with 0 wrong and 0 missing values.

| Field | Values now on the 114 cards |
|---|---|
| Status | Done 38, Blocked 29, Backlog 27, In progress 9, Ready 6, In review 5 |
| Track | Active delivery 85, Follow-up 29 |
| Work level | Delivery 114 |
| Roadmap | the parent-inherited value enrolled for each row |

The run sent 423 absent-value effects: Work level 114, Track 114, Roadmap 114 and Status 81. Each one has an HTTP 200 receipt and a native readback. The other 33 Status values already matched their targets and were left unchanged. No existing value was overwritten, and no raw `item-edit` write ran.

### Stages and Root decisions

1. **BindRuntime preflight.** It returned exit 11. Eight of its 9 checks were `SupportedForNextStage`. The remaining check, `ProjectAutoAdd`, was Unknown and named exactly the 14 excluded and held cards. `bind-runtime` printed "runtime binding written; population revision rebound; offline only", and the result differs from the private pair only in `populationRevision`.
2. **Reader-identity rebind (Root-authorized hand step).** The installed 0.114.0 reader refused the binding: "selected binding does not match the loaded adapter assembly SHA-256". The binding still named the 0.113 reader, because `enrollment plan` copies the predecessor's identity fields and `bind-runtime` may change only `populationRevision`. Root authorized one rebind, written to a new file with the original kept:
   - `artifactSha256` changed from `480106b6…cac2fd` to `f76941b2…cd1803a`;
   - `recipeRevision` changed from `79db622f…` to `3c13e67f…`;
   - the file sha256 changed from `8cf8c1aa…38ef` to `eba7a946…f9a5`, and the file is otherwise byte-identical;
   - the installed `FS.GG.Coord.GitHub.dll` hashes to `f76941b2…` and embeds `1.0.0+3c13e67f56de25041c6a5f9de85c54d178134956`.
   [#5120](https://github.com/FS-GG/.github/issues/5120) owns adding a designed stage for this rebind.
3. **207-row qualification, accepted as Unknown.** `board-v2 inspect` accepted the rebound binding but exited 3 after 2,502 reads. Its membership gap reads "bounded refresh deadline/request ceiling reached". Main and the manifest were unchanged, and the 93-row qualification had completed in 1,741 reads. Root accepted the qualification as Unknown for this cohort only and did not retry it. [#5121](https://github.com/FS-GG/.github/issues/5121) owns the reader ceiling.
4. **Batch 1.** This scope covered 16 cards, and its prepare found 55 changes, all matching the targets. The apply hit the 270-second invocation ceiling after 19 effects (16 Work level, 2 Track, 1 Roadmap). Every one of those 19 has a receipt and a correct readback, so Root recorded them as delivered. Each effect re-reads the whole scope; [#5123](https://github.com/FS-GG/.github/issues/5123) owns that cost.
5. **Small scopes.** Root re-split the remaining 113 cards into 29 serialized scopes of at most 4 cards. Each scope ran a rate guard (at least 1,500 GraphQL and REST requests remaining), a fresh absent-only `migration-prepare` with 0 unexpected deltas, one apply and a GraphQL readback of that scope. Every apply exited 0 and read back with 0 wrong and 0 missing values. There were no ceiling partials and no rate pauses.
6. **Retarget.** Root closed Unity.Client#28 at 16:31Z after accepting it, and the Item closed workflow then set its Status to Done. The first s19 prepare correctly refused to overwrite that value. The refusal was read-only and dispatched nothing. Root ruled that any card closed when its scope is prepared targets Done, so #28 went from In review to Done. It was the only retarget.

### Observed cost

- **Invocations:**
  - planner: two `resolve-issues`, one each of `plan`, `prepare`, `render-carrier` and `bind-runtime`, and seven `route-preflight` runs;
  - two `inspect` runs;
  - 31 `migration-prepare`, including the refused s19 attempt;
  - 30 `migration-apply`;
  - one engine `migration-readback`.
- **Wall clock:** planning started at 16:06:58Z and the last apply finished at 19:07:57Z. The apply phase alone ran from 16:55:04Z to 19:07:57Z. The small-scope applies took 6,219 s in total, between 79.5 s and 258.2 s each.
- **Reads:** `inspect` reported its own 2,502. The engine does not count migration requests. During the small-scope run, GraphQL `rateLimit` fell by 3,487 points within reset windows. That is an upper bound, because other sessions share the same token.

The 14 excluded and held cards keep their existing values. Enrollment and initialization grant no implementation admission or Ready decision. Refs [#4941](https://github.com/FS-GG/.github/issues/4941) and [#4730](https://github.com/FS-GG/.github/issues/4730).
