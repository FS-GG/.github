# ADR-0098: A shared algorithms capability: the FS.GG.Algorithms library and the `fs-gg-algorithms` product knowledge pack

- **Status:** Accepted
- **Date:** 2026-11-01
- **Affects:** FS.GG.Algorithms (new repository); FS.GG.Game; FS.GG.Rendering; FS.GG.SDD; FS.GG.Templates;
  `.github` (`registry/repos.yml`, `registry/skills.yml`, `registry/skills.delivery-channels.yml`,
  `registry/dependencies.yml`, the Atlas skill, the pnext-item and work-board closure rules); every
  product workspace whose scaffold satisfies the predicate in §7
- **Decision owner:** Root/fdev. The merge of this record's PR is its acceptance.
- **Outcome / Delivery:** [.github#5222](https://github.com/FS-GG/.github/issues/5222) (ALGO-01) /
  [.github#5223](https://github.com/FS-GG/.github/issues/5223) (ALGO-01.1)
- **Inputs:** three analyst inventories on #5223
  ([coordination and formal](https://github.com/FS-GG/.github/issues/5223#issuecomment-6564262819),
  [games and netcode](https://github.com/FS-GG/.github/issues/5223#issuecomment-6564266523),
  [engine and product](https://github.com/FS-GG/.github/issues/5223#issuecomment-6564286610)), Root's
  [private-repository ruling](https://github.com/FS-GG/.github/issues/5223#issuecomment-6564271532) and
  Root's [prior-art algorithm survey](https://github.com/FS-GG/.github/issues/5223#issuecomment-6564547547)
  (taxonomy, ranked new items with oracles, verified reference licences)

## Context

ALGO-01 promises a growing algorithms and optimization capability with three parts: a tested F#
library on nuget.org, a product skill `fs-gg-algorithms` carrying a searchable knowledge pack, and a
growth loop. The pack must be **usable inside a product workspace**, starting with workspaces
scaffolded from `fs-gg-fable-game`. The framework Atlas holds only the decisions about the pack.

What already exists, as measured on 2026-11-01 (Game `eb48a19`, Rendering `2d35dc9`, SDD `2a2770be`,
Templates `858b624`, `.github` `79359691`):

1. **`FS.GG.Game.Core` is already a de facto algorithms library.** It ships A*, BFS, JPS, ALT
   landmarks, `Regions`, flow and distance fields, weighted move range, hex grids, thin-wall edge
   graphs, Bresenham and supercover LOS, symmetric shadowcasting FOV, a visibility polygon, a uniform
   spatial hash, SAT and swept narrow phase, SplitMix64, procedural generation, map analysis
   (articulation points), influence maps and the fixed-step loop. Most of it is FsCheck
   property-tested. `Pathfinding.astar`, `Los.lineOfSightBy` and `Edges.edgeBetween` are graded
   `LockstepExact` in `src/Game.Core/Fable/compatibility-profile.v1.json`. Most other surfaces are
   graded Unclassified or DotNetOnly, even where they are integer-only. The `fs-gg-fable-game` template
   already calls `Pathfinding.astar` and `Kinematics.advance`.
2. **Generic kernels also exist outside games.** `.github`'s `FS.GG.Coord.Core` holds Tarjan SCC
   (`Blockers.cycles`), union-find (`Lanes.partition`), patience-diff anchors with an LIS
   (`SemanticDiff`) and interval union (`TelemetryBudget`). Governance holds a nearest-rank
   percentile. The private games hold an 8-way A* with door overlays (RuneScape), a budgeted
   Dijkstra with an independent oracle and an exact-rational slab ray cast (FourD), and a
   priority-band controller arbiter (RuneScape PR #14, unmerged).
3. **No algorithm kernel is benchmarked in time anywhere.** FS.GG.Game
   [#761](https://github.com/FS-GG/FS.GG.Game/pull/761) and
   [#792](https://github.com/FS-GG/FS.GG.Game/issues/792) measure **rendering** frame cost through
   SVG, Pixi and Babylon. The only algorithm evidence is deterministic node and work counts.
   [FS.GG.Game#780](https://github.com/FS-GG/FS.GG.Game/issues/780) records the quiet-host rule: each
   run is admitted by `fdev-lane-capacity.py --max-load-per-core 0.25`.
4. **Gaps.** Nothing in scope implements HPA\*, D\* Lite, behaviour trees, GOAP, HTN, utility AI,
   minimax, MCTS, LP or ILP, annealing, GA, constraint solving, k-d trees, topological sort with
   critical path, backoff with jitter, a token bucket or fixed-point arithmetic. Game's AI design
   (`docs/reports/2026-07-10-ai-decision-layer-design.md` §6) excludes behaviour trees and utility AI
   from Game.Core by decision. Game's
   `docs/reports/2026-07-19-redblobgames-algorithm-incorporation-roadmap.md` is prior art for the
   grid tier.
5. **How product skills reach a workspace today.** Each owner publishes a content-only
   `FS.GG.<Owner>.Skills` package. The `fsgg-sdd` CLI embeds it at build time
   (`FS.GG.SDD.Commands.fsproj`) and materializes it at scaffold time into `.claude/skills` and
   `.agents/skills`. `ProductPredicate.evaluate` (`GameSkillManifest.fs`) evaluates
   `materializes-when` against `effectiveParameters` plus `template`, which is derived from the
   template id. An unset key reads as the empty string. The Rendering and Audio route
   (`RenderingSkills.planFilesFrom`) carries a whole skill directory with per-file schema-v2 digests
   and verifies it as one unit. The Game route stages and embeds `SKILL.md` only. The `atlas` driver
   already ships 513 files, including a ~2 MB record archive, so multi-file skills are proven.
6. **The four algorithm skills are duplicated, and three of them miss fable-game workspaces.**
   `fs-gg-grids`, `fs-gg-collision`, `fs-gg-line-drawing` and `fs-gg-visibility` ship from both
   Rendering and Game with different bytes. `registry/skills.yml` names Rendering as the owner, with
   Rendering's digests. SDD actually delivers Game's bodies: `HandlersScaffold.fs`
   `plannedRenderingSkillOutcome` yields any path Game already kept, and
   `acceptedGameRenderingSharedIds` lists the four ids. The `fable-game` provider declares no
   `profile`, so `profile in [game, sample-pack]` is false there. Only `fs-gg-collision`, whose Game
   row adds `or template == fable-game`, reaches a fable-game workspace. Templates' oracle
   `tests/svg-workspace-owner-skills/expected.json` (`fableGameSelections`) confirms this: every
   bundle lists collision and never grids, line-drawing or visibility.
7. **Upgrade writes missing skill files but never replaces changed ones.** `fsgg-sdd upgrade` runs
   `Drift.ownerSourcedBackfill` with the installed CLI's embedded packages. It writes every planned
   path that is missing, so a **new** skill id appears once the CLI embeds it. A file that exists
   with different bytes is refused by `CommandEffects.canOverwrite`, because owner skill files are
   `AgentGuidanceTarget`, and surfaces only as a drift advisory. A pack whose `catalog.json` changed
   would therefore never update in an existing workspace.
8. **The Atlas record format fits a technique entry.** A record is a closed `fsgg.atlas/1` object.
   Its summary is capped at 8000 characters, the canonical record at 32 KiB, tags at 20 and
   citations at 1 to 16 immutable repository citations. An archive directory may contain only
   `catalog.json`, `README.md` and `records/`. `atlas.py --archive <dir> search` builds a temporary
   FTS5 index and needs no database argument.
9. **Earlier decisions bind the shape.** One implementation per invariant
   ([ADR-0033](0033-fixed-step-double-buffer-is-a-simulation-primitive.md) §5,
   [ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md),
   [ADR-0085](0085-fsquint-single-owner-package-boundary.md)). Skills are sourced from their owner
   repository ([ADR-0063](0063-scaffold-materializer-sources-skills-from-the-owner-repo.md)).
   Materialization is condition-aware
   ([ADR-0017](0017-skill-registry-condition-aware-materialization.md)). Derive, don't restate
   ([ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md)).

## Decision

### §1 Producer: a new repository, `FS-GG/FS.GG.Algorithms`

The library and the knowledge pack live in a **new public repository, `FS-GG/FS.GG.Algorithms`**,
which Root creates. It is a bottom-layer component like Audio and Net
([ADR-0023](0023-onboard-fs-gg-audio-as-an-sdd-driven-component.md),
[ADR-0052](0052-onboard-fs-gg-net-transport-component.md)): it **depends on no FS-GG component**, and
any FS-GG repository may depend on it.

- **Dependency direction.** Coordination code (Tarjan SCC, union-find, patience diff) must not depend
  on a game package. Rendering already sits below Game, because `Game.Render` depends on it, so a
  Rendering consumer of an algorithm in a Game package would create a repository cycle. Only a leaf
  repository can be consumed by Game, Rendering, the games, Coordination and `.github` alike.
- **Release cadence.** Algorithm additions are small and frequent. Game releases ride a coherent set
  with WASM, Fable and browser qualification, and a release of either should not gate the other.
- **The pack has one owner.** Each entry cites the implementation, its property tests and its bench
  record. The capture rule (§9) is simplest when the entry lands in the repository that owns the
  pack.

**Game.Core stays the implementation of record for what it already ships.** This ADR does **not**
extract Game.Core's grid, collision, procedural or simulation algorithms. They are typed over
Game.Core's own primitives (`Cell`, `Edge`), carry Fable lockstep fixtures and are published API
under [ADR-0025](0025-first-class-shipped-surface-mutation-event.md). The pack **indexes** them in
place. Simulation primitives such as the fixed-step accumulator stay Game.Core-owned under ADR-0033.
A Game.Core kernel moves to FS.GG.Algorithms only on demand, when a non-game consumer needs it. When
it moves:

1. Game.Core keeps its published surface and delegates to the moved kernel.
2. Its existing fixtures and property tests must pass unchanged. For `LockstepExact` surfaces, the
   Fable fixture corpus must be byte-identical.
3. No second implementation survives the release that moves it.

One dependency is decided now rather than on demand: the layer-0 heap (§3). Game.Core's A* `Set`
frontier migrates onto `FS.GG.Algorithms.Collections`, so Game.Core becomes the library's first
FS-GG consumer. That direction (Game → Algorithms) is the one §1 permits.

### §2 Packages and namespaces

| Package | Content | Dependencies | Fable |
|---|---|---|---|
| `FS.GG.Algorithms` | The library | `FSharp.Core` only | Required (§3) |
| `FS.GG.Algorithms.Skills` | Content-only skill package carrying `fs-gg-algorithms` and its pack | none | n/a |
| `FS.GG.Algorithms.Solvers` | **Reserved.** Created only when the first admitted entry needs a native or .NET-only solver (for example LP or ILP bindings) | that solver, never an FS-GG component | not required; marked DotNetOnly |

Namespaces are `FS.GG.Algorithms.<Family>`, one per taxonomy family (§11): `.Collections`, `.Random`,
`.Graph`, `.Search.Grid`, `.Search.Mesh`, `.Search.MultiAgent`, `.Steering`, `.Decision`, `.Adversarial`,
`.Learning`, `.ProcGen`, `.Spatial`, `.Geometry`, `.Optimization`, `.Numerics`, `.Net` and `.Systems`.
The namespace equals the pack's family tag. Public surface is declared in `.fsi` signature files and
baselined as a shipped surface (ADR-0025).

The library and the skills package version independently, as `FS.GG.Game` and `FS.GG.Game.Skills`
do. Both are dual-published to nuget.org and GitHub Packages
([ADR-0012](0012-dual-publish-to-nuget-org.md), [ADR-0013](0013-trusted-publishing-oidc-for-nuget-org.md))
through the org publication writer ([ADR-0097](0097-one-org-wide-serialized-publication-writer.md)).
They start at `0.x`. The licence is MIT, matching FS.GG.Game.

### §3 Fable-portability rules

1. Every module in `FS.GG.Algorithms` compiles under Fable to JavaScript. The package packs its
   canonical sources under `fable/`, the pattern `src/Game.Core/Fable/FS.GG.Game.Core.fsproj` uses.
   There is no second implementation for Fable.
2. Each public surface is graded in a `compatibility-profile.v1.json`, using Game.Core's vocabulary:
   - `LockstepExact`: byte-identical results on .NET and JavaScript, proven by a shared fixture
     corpus run under both runtimes.
   - `Portable`: compiles and runs under Fable, with no byte promise.
   - `DotNetOnly`: allowed in `FS.GG.Algorithms.Solvers`, and for a Task- or IO-bound adapter in the
     `Net` or `Systems` family that sits beside a portable core of the same algorithm.

   Unclassified is not an admissible grade for a new surface. Pointer entries may report
   Game.Core's existing Unclassified grade as it stands.
3. Integer-keyed algorithms (searches, grids, graph kernels, RNG) target `LockstepExact`. Use `int`
   in hot paths, not `int64` (a BigInt in JavaScript) or `decimal`. Tie-break with a total order,
   never a hash or dictionary iteration order. Floating-point surfaces are at most `Portable` unless
   a fixed-point representation (§11, `numerics`) is used.
4. Avoid APIs Fable does not support: reflection, `System.Threading`, `System.Random`, mutable static
   state, and `System.Collections.Generic.PriorityQueue`. The library ships its own heap in
   `.Collections`. Randomness takes an explicit seeded state, as Game.Core's `Rng` does.
5. Recursion that is unbounded in the input size is refused. Tarjan SCC, DFS and flood fills are
   written iteratively. JavaScript stacks are small, and the harvested `Blockers.cycles` is recursive.
6. **Layering.** Two layer-0 foundations, `Collections.Heap` and `Numerics.Fixed`, may be used by
   every family and use nothing else in the library. Only `Net` and `Systems` may touch Tasks or IO,
   which keeps every other family compilable in the Fable view.

**Layer-0 foundations (tranche 1, before anything that depends on them).**

- **A Fable-portable binary heap and indexed heap** (decrease-key), with a total order and a
  deterministic tie-break supplied by the caller. Every priority-queue search in the org migrates
  onto it:
  - Game.Core's A* `Set` frontier;
  - FourD's re-sorted list frontier;
  - RuneScape's BCL `PriorityQueue`.

  The Game.Core migration must keep its `LockstepExact` fixture corpus byte-identical and must not
  regress the bench that Game's Red Blob Games roadmap used to keep the `Set` frontier. FourD and
  RuneScape are private products, and their migrations are their owners' work.
- **Q16.16 fixed-point** (`Numerics.Fixed`): add, multiply, divide, square root, and LUT or CORDIC
  trigonometry, bit-identical on .NET and JavaScript.
  - Multiply and divide must not use `int64`, because it is a BigInt under Fable. The 32-bit
    operands are split into 16-bit halves.
  - Fixed-point is what lets steering, utility scoring and future physics reach `LockstepExact`.
  - Q32.32 waits until the `int64` cost under Fable is measured (Consequences, open questions).

### §4 Admission bar

A library entry is admitted only with all of the following in the same PR:

1. **Property tests** (FsCheck) against an independent oracle. Examples: A* cost equals BFS hop
   count on unit-cost grids and Dijkstra cost on weighted ones; JPS cost equals A* cost; alpha-beta
   value equals minimax value; a k-d tree's nearest result equals brute force; SCC components match
   a reachability closure; an optimizer's result satisfies every constraint and matches exhaustive
   search on small instances. Determinism under a fixed seed is a property for every randomized
   entry. Example-only tests do not admit.
2. **A Fable proof** for each surface graded `LockstepExact` or `Portable`: the fixture corpus runs
   under a Fable test project.
3. **Bench numbers** for every performance-relevant entry (searches, spatial structures, optimizers,
   anything with a hot loop), on the micro-bench route below.
4. **A pack entry** (§6) describing it.

**The micro-bench route (decided).** #761 and #792 time rendering frames, not kernels. Kernels are
timed on two runtimes, in a `bench/` folder of the repository that owns the kernel. For
FS.GG.Algorithms kernels that is FS.GG.Algorithms; for Game.Core pointer entries it is FS.GG.Game.
A bench project never ships, so it adds no package dependency.

- **.NET:** **BenchmarkDotNet**.
- **JavaScript:** a minimal timed harness, `bench/fable/`, runs the Fable-compiled kernel under
  Node with the same inputs. It is required for performance-relevant entries graded
  `LockstepExact` or `Portable`, because a fable-game workspace runs them in the browser.

- **Protocol.** Fixed seeded inputs at three declared sizes, and the work counts each run
  performed (for example nodes expanded) beside the timings. Work counts are deterministic and may
  be gated. Timings are evidence, not gates.
  - On .NET: a fixed BenchmarkDotNet job (declared warm-up and iteration counts, the default
    toolchain, `MemoryDiagnoser`).
  - On JavaScript: a declared number of warm-up and measured iterations, reporting the median and
    p95 by nearest rank (`Numerics` percentile, tranche 1).
- **Quiet host.** Each bench invocation is admitted by
  `fdev-lane-capacity.py --max-load-per-core 0.25`, the #780 rule. A held probe waits; it is not
  overridden.
- **Storage.** Results are committed as `bench/baselines/<host>-<yyyy-mm-dd>-<package-version>.json`
  against `bench/results.schema.v1.json`, which FS.GG.Algorithms owns and FS.GG.Game reuses by
  reference. The pack entry cites the baseline file by immutable revision and digest, and summarises
  host, date, version, sizes, median and work counts. A summary never reports a number without its
  host and version.
- **One schema, two runtimes.** Both runtimes write the same results schema, which carries a
  `runtime` field. The #792 runner is not required. An entry may add in-page browser timings
  through it once it is published.

Entries indexed in place (§1) meet the same bar where they live. The owed items (the explicit A* vs
BFS property, Fable grades for integer-only surfaces, timing benches) are Game-owned work under
#5224. Until each item lands, the entry's `Tests` or `Bench` section says "owed" rather than
claiming it.

### §5 Harvest and private-repository rules

Root's ruling on #5223 governs harvest from private repositories:

- **Allowed.** Generic algorithm code authored by FS-GG may be **cleanly re-implemented** in the
  public library and described in the public pack, with its own tests. Examples: A* with door
  overlays, budgeted Dijkstra, the exact-rational slab ray cast, priority-band arbitration,
  effect-proof reconciliation, delta and snapshot delivery.
- **Forbidden in any public artefact** (library, pack, bench baselines, examples):
  - game data, asset-derived or cache-derived values, and game-specific constants (for example
    RuneScape collision flags, coordinates or ids);
  - private product code that is not an algorithm;
  - code derived from a third-party upstream unless its licence allows it and its notice is
    carried. rs-sdk and LostCity code is MIT and allowed only with its notice; prefer a clean
    re-implementation. RecoilEngine and HighBarV3 are excluded. OpenAL Soft (LGPL, via
    `FS.GG.Audio.Host`) never enters the library.
- **Tests and benches use synthetic inputs only.** A harvested entry's tests never use the source
  game's data.
- **Citations.** Every pack entry carries at least one public citation (its implementation of
  record). A private-repository citation is permitted only as harvest provenance: repository,
  revision, path and digest, never content. Readers without access treat it as a retrieval gap.
- **Unknown licence.** Where a repository's licence is unstated (FourD, SC2.Client, Unity.Client),
  FS-GG authorship is the basis. `Game.Core/Physics.fs` is not harvested until its author confirms
  it is not derived from Box2D-Lite or another source.

**Porting rules for external reference code** (licences verified in the survey on 2026-11-01):

- **Default: implement from the paper.** Every survey row marked "paper" is a clean
  implementation from the published description, with the paper under **References**.
- **Permissive, port only with the notice carried.** The licence notice goes in the library's
  `THIRD-PARTY-NOTICES.md`, and the entry names the licence. Prefer implementing from the paper
  even here.
  - **MIT:** WFC (mxgmn), GGPO, FastNoiseLite, rbush, MiniSat.
  - **CC0-1.0:** OpenSimplex2.
  - **Zlib:** Recast and DotRecast.
  - **Apache-2.0, with its NOTICE file:** RVO2.
  - **BSD-3-Clause:** pycma, libccd.
  - **ISC:** Delaunator, kdbush, flatbush.
  - **Dual MIT/Apache-2.0:** GGRS.
- **Never port:** Stockfish (GPL-3.0), and any GPL or LGPL source (for example OpenAL Soft).
  - Search techniques Stockfish uses are implemented from their papers.
  - OR-Tools (Apache-2.0) may serve as a **test oracle only**, in a test project, never as shipped
    code.
- **Copyrighted books** (for example *The Algorithmic Beauty of Plants*, AIMA, CLRS, RTCD, the
  Game AI Pro chapters) are summarised and cited, never copied, including their pseudo-code.

### §6 Pack format: an Atlas archive inside the skill

The pack is an **unmodified Atlas archive**, in exactly the `fsgg.atlas/1` format of
`.agents/skills/atlas/references/records.md`, so `atlas.py` validates and searches it with no new
code or fields. Its layout in the producer is:

```text
template/product-skills/fs-gg-algorithms/
  SKILL.md                 # when to use, retrieval commands, taxonomy, index of related skills
  references/taxonomy.md   # the family table (§11) and selection guides
  references/contributing.md
  pack/README.md           # archive README (allowed by atlas.py)
  pack/catalog.json        # fsgg.atlas/1 catalog
  pack/records/<sha256>.json
```

**Entry schema.** One record per technique:

| Field | Rule |
|---|---|
| `id` | `algorithms/<family>/<technique>`, where `<family>` is the lowercased §11 family (`search.grid`, `procgen`), stable, for example `algorithms/search.grid/astar` |
| `repository` | `FS-GG/FS.GG.Algorithms` (the archive owner), whichever package holds the code |
| `kind` | `contract` for status `pointer`, `harvested` or `new` (a public FS-GG package implements it). `lesson` for status `entry-only` (a pattern or pitfall with no shipped kernel). `decision` for a selection guide ("which pathfinder"). `question` (with `status: question`) for a gap entry that names the missing knowledge |
| `title` | The technique's common name, at most 160 characters |
| `summary` | The body. Fixed headings, in this order: **Idea**, **Complexity**, **Use when**, **Avoid when**, **Pitfalls**, **F# usage** (package id, module, signature, and an original snippet of at most 15 lines), **Tests** (property names, or "owed"), **Bench** (host, date, version, sizes, median, work counts, or "owed"), **Related** (entry ids and skill ids such as `fs-gg-grids`), **References** (bibliographic: author, title, year, venue, DOI or URL) |
| `tags` | At most 20. The pack metadata is carried in three tags, so the record schema stays closed: audience (`audience-game`, `audience-framework` or `audience-both`), determinism grade (`grade-lockstep-exact`, `grade-portable`, `grade-unclassified` or `grade-dotnet-only`) and status (`status-pointer`, `status-harvested`, `status-new` or `status-entry-only`). The other tags are the family, the technique and its aliases (`astar`, `a-star`) |
| `citations` | 1 to 16 immutable citations: the implementation of record (`.fsi`/`.fs`), its property tests, its bench baseline, and harvest provenance if any (§5) |
| `status`, `recordedAt`, `supersedes`, `conflictsWith` | Atlas semantics. A changed entry is a new record that supersedes the old one, and history is never rewritten |

External literature cannot be an Atlas citation, because a citation must name a repository revision.
It goes under **References** instead. Citations prove the FS-GG implementation; references credit
the idea.

**Citation and licence rules.** Summarise in your own words and cite. Never copy text, pseudo-code,
figures or tables from textbooks, papers, Wikipedia, Red Blob Games or any other source. Quote at
most one short attributed sentence, and only when the wording itself matters. Snippets are original
FS-GG code under the library's licence. Every entry with a primary source (the paper that introduced
the technique) names it.

**Size limits.** `atlas.py` enforces 8000 summary characters and 32 KiB per record. The pack adds
policy limits, checked by the producer gate:

- 6 KiB target per canonical record;
- the pack warns at 2 MiB and refuses above 4 MiB (it is materialized twice per workspace, once per
  agent root);
- `SKILL.md` refuses above 16 KiB, because the knowledge belongs in the pack;
- at most one active record per id.

**Retrieval in a workspace.** `SKILL.md` instructs:

```sh
python3 .agents/skills/atlas/scripts/atlas.py --archive .agents/skills/fs-gg-algorithms/pack search 'grid pathfinding doors'
python3 .agents/skills/atlas/scripts/atlas.py --archive .agents/skills/fs-gg-algorithms/pack get algorithms/search.grid/astar
```

The helper is the `atlas` driver skill (`materializes-when: always`, delivered by FS.GG.Drivers). It
is **resolved, not copied** (ADR-0067), so the pack carries no second `atlas.py`. A workspace that
keeps its own Atlas archive can pass `--archive` twice to search both. If the helper is absent,
`SKILL.md` falls back to `grep` over `pack/records/*.json` and reports the missing helper as a gap.

### §7 Distribution

`fs-gg-algorithms` rides the **existing owner-package route** (ADR-0063), using the multi-file
Rendering and Audio transport:

1. **Producer manifest.** `FS.GG.Algorithms/template/skill-manifest/skill-manifest.json`, schema v2,
   lists every file of the skill with its own digest, including each pack record. It is generated
   by the producer's `scripts/generate-skill-manifest.fsx --check` and gated in CI. The predicate is:

   `materializes-when: "profile in [game, sample-pack] or template == fable-game"`

   This matches `fs-gg-game-core`. Widening it later is a producer manifest change, never a registry
   edit.
2. **Package.** `FS.GG.Algorithms.Skills` stages the whole directory, as Rendering's
   `stage-skills.py` does.
3. **Receiver.** FS.GG.SDD pins and embeds `FS.GG.Algorithms.Skills` and plans it with
   `RenderingSkills.planFilesFrom`, as it already does for Audio. It adds an algorithms outcome
   beside `plannedAudioSkillOutcome`, and an `ownerSourcedBackfill` arm, so that scaffold and upgrade
   both carry the skill.
4. **`.github` registries.** All of these are reconciled after the producer and receiver exist, never
   ahead of them:
   - `registry/repos.yml`: a roster row for `FS-GG/FS.GG.Algorithms`, so
     `fsgg-skill-registry-check` clones it as a producer.
   - `registry/skills.yml`: a row `{ id: fs-gg-algorithms, scope: product, owner: fs-gg-algorithms,
     source: FS.GG.Algorithms/template/product-skills/fs-gg-algorithms/SKILL.md, sha256: …,
     materializes-when: … }`, reconciled by `--write` from the producer manifest, never hand-authored.
   - `registry/skills.delivery-channels.yml`: a class `owner: fs-gg-algorithms, scope: product`.
     It is `gap` with `tracked-by` the Delivery until SDD embeds the package, then `delivered`,
     `kind: package`, `channel: FS.GG.Algorithms.Skills`.
   - `registry/dependencies.yml`: an `algorithms-skills` contract (`consumers: [sdd]`) and, once a
     framework repository references the library, an `algorithms` contract.
5. **Verification.** `scripts/skill-union-assert.sh --params` must report the skill present for a
   fresh `fs-gg-fable-game` provenance, and `tests/skill-union` gains that case. A fresh scaffold
   must also return hits for `atlas.py --archive .agents/skills/fs-gg-algorithms/pack search`. A
   declaration alone proves nothing (Atlas `platform/skill-delivery-authority`).

**The upgrade path.**

- A **new skill id** reaches an existing workspace through `fsgg-sdd upgrade` once the installed CLI
  embeds the package, because missing paths are written.
- **Pack updates** need one receiver change, which this ADR decides: an owner-sourced skill file
  whose current bytes equal the digest recorded for it at materialization is owner-managed. Upgrade
  **replaces such pristine files as one skill-directory unit**.
- A skill with any locally edited file keeps today's refusal and drift advisory, so author edits are
  never overwritten.
- The rule applies to every owner-sourced product skill, not only this one. Without it, every owner
  skill's body goes stale in existing workspaces today.

SDD owns the implementation. ALGO-01's upgrade acceptance cannot be met before it lands.

### §8 The four existing algorithm skills: index them, with one owner

- **Owner.** `fs-gg-grids`, `fs-gg-collision`, `fs-gg-line-drawing` and `fs-gg-visibility` have one
  owner, **FS.GG.Game**. Game holds the code, its bodies are the newer ones, and they are what SDD
  already delivers. Rendering's copies are retired on the
  [.github#1862](https://github.com/FS-GG/.github/issues/1862) /
  [Rendering#1147](https://github.com/FS-GG/FS.GG.Rendering/issues/1147) (P6) precedent, and
  `registry/skills.yml` is reconciled to owner `fs-gg-game` with Game's digests and predicates.
  **The Rendering owner must confirm the retirement.** Rendering's `template/fragments/` adaptable
  sources, such as the frozen `Visibility.fs`, go with it or are explicitly marked non-canonical.
  Until then, the pack names Game.Core as the implementation of record.
- **Predicate fix.** Game's manifest rows for grids, line-drawing and visibility gain
  `or template == fable-game`, as collision already has. This closes the gap in Context item 6.
  [.github#5225](https://github.com/FS-GG/.github/issues/5225) implements it with the skill, and
  Templates' `fableGameSelections` oracle is updated to expect them.
- **Index, not fold.** The umbrella does not absorb the four bodies. `fs-gg-algorithms`'s `SKILL.md`
  carries an index from each taxonomy family to the skill ids that hold usage guidance (these four,
  plus `fs-gg-game-core`, `fs-gg-ai`, `fs-gg-physics`, `fs-gg-ballistics`, `fs-gg-mapcraft` and
  `fs-gg-replay`). Pack entries for those techniques carry only what the skills lack: complexity,
  when not to use, pitfalls, property-test names, bench numbers and references. They link back by
  skill id and never restate the skill's usage text.
- **Fold condition.** Folding a skill into the umbrella is a separate owner decision. It is allowed
  only when that skill's implementation moves to FS.GG.Algorithms under §1, and then follows the
  ADR-0022 P4 byte-identical owner migration.

### §9 Growth loop

**Framework capture rule.** A framework Delivery, in any FS-GG repository, that adds, replaces or
materially tunes an algorithm in a public FS-GG package must do one of two things before its
closure:

- add or supersede the pack entry in FS.GG.Algorithms, citing the merged implementation, its tests
  and its bench baseline; or
- record in its acceptance evidence why no entry is needed, or a capture gap with an owner.

"Materially tunes" means that results, complexity or the declared bench numbers change. The rule
joins the existing Atlas delivery rule in the pnext-item and work-board closure contracts
([.github#5226](https://github.com/FS-GG/.github/issues/5226)). FS-GG-specific framework
applications, such as the claim fence or the publication lock, stay framework Atlas records (§10).

**Product contribution route.**

1. A workspace keeps product-specific tuning in its own knowledge store and never edits the
   materialized pack. The pack is owner-managed and replaced on upgrade (§7).
2. To contribute upstream, the workspace drafts an entry JSON in the §6 schema. It validates the
   draft locally with `atlas.py --archive <scratch-archive> capture` plus the producer's pack-lint
   script, which the skill documents.
3. It opens an **"Algorithm entry" issue form** on FS.GG.Algorithms, optionally with a PR that adds
   a generic implementation and its tests.
4. The contributor applies §5: no product data, no private source, synthetic inputs only.
5. Maintainers admit under §4, and the entry ships in the next `FS.GG.Algorithms.Skills` release.

### §10 Audience split

| Audience | Where it lives | Examples |
|---|---|---|
| **Game** (`audience-game`) | Library and pack, materialized into workspaces | Search, steering, decision, adversarial, procgen, spatial, geometry |
| **Both** (`audience-both`) | Library and pack. The FS-GG instance of the technique stays a framework Atlas record | Tarjan SCC, toposort and CPM, union-find, the Hungarian algorithm, local search, bandits, backoff with jitter, token buckets, the expected-version append-only event log, bounded queues with backpressure, delta and snapshot delivery (pointer to FS.GG.Net), interval union, exact-rational apportionment, BM25 ranking (the pack's own retrieval) |
| **Framework library entry** (`audience-framework`) | Library and pack, tagged so workspaces can filter it out. These are generic algorithms with no game use yet | Consistent and jump hashing, reservoir sampling, Bloom filters, Hopcroft-Karp |
| **Framework-only applications** | Framework Atlas, tagged `algorithm`. Never in the pack | Lowest-comment-id claim arbitration ([ADR-0075](0075-per-receiver-operation-lock-and-lease-free-election.md)), comment-order CAS ([ADR-0027](0027-worker-keyed-claim-lock-and-worker-channel.md)), the bakery publication lock (ADR-0097), conflict-free batch admission with rank aging, host capacity admission, content-addressing canonicalization, the release journal, Quint model use |

A pack entry never cites or describes FS-GG coordination internals. The pack is public product
guidance.

### §11 Taxonomy and the first-tranche seed list

**Families.** This ADR adopts the survey's 17-family taxonomy. The family is the library namespace
`FS.GG.Algorithms.<Family>`, and its lowercase form is the second segment of an entry id.

| Family (id segment) | Scope |
|---|---|
| `Collections` (`collections`) | **Layer 0:** heaps and the indexed heap. Also LRU, Bloom filters, and the sequence utilities listed under placements |
| `Random` (`random`) | SplitMix64, alias method, reservoir sampling, dice distributions |
| `Graph` (`graph`) | Toposort and CPM, shortest paths, SCC, dominators, MST, flow, matching, articulation points, union-find |
| `Search.Grid` (`search.grid`) | A*, BFS and Dijkstra, JPS, ALT, fields, Theta*, HPA*, D* Lite |
| `Search.Mesh` (`search.mesh`) | Navmesh A* and the funnel algorithm |
| `Search.MultiAgent` (`search.multiagent`) | WHCA*, CBS |
| `Steering` (`steering`) | Reynolds behaviours, boids and formations, ORCA |
| `Decision` (`decision`) | FSM and HFSM, behaviour trees, utility AI, GOAP, HTN, influence maps, arbitration |
| `Adversarial` (`adversarial`) | Negamax α-β with transposition tables, expectimax, the MCTS family, CFR |
| `Learning` (`learning`) | Bandits, Q-learning, ES, CMA-ES, GA |
| `ProcGen` (`procgen`) | Noise, cellular automata, BSP, Poisson disk, WFC, L-systems, grammars |
| `Spatial` (`spatial`) | Spatial hash, k-d tree, quadtree and octree, BVH, R-tree, sweep-and-prune |
| `Geometry` (`geometry`) | SAT, GJK and EPA, Delaunay and Voronoi, exact predicates. LOS, FOV, visibility and grid coordinates are pointers |
| `Optimization` (`optimization`) | LP, ILP, CSP, SAT, local search, assignment, flows, knapsack, scheduling |
| `Numerics` (`numerics`) | **Layer 0:** fixed-point. Also integrators, splines and easing, springs, PID, percentiles |
| `Net` (`net`) | Prediction and reconciliation, interpolation, rollback, delta encoding |
| `Systems` (`systems`) | Rate limiting, backoff, hashing, full-text ranking |

The survey's taxonomy left some existing entries without a family. They are placed as follows, and
this ADR keeps no family beyond the 17:

- LIS with patience-diff anchors, and interval union → `Collections`, as sequence utilities.
- Hex coordinates, thin-wall edges and grid parts → `Geometry`, as pointers. Their search
  functions are `Search.Grid` pointers.
- The fixed-step accumulator and interpolation → `Numerics`, as pointers.
- Replay checkpoints → `Net`, as a pointer. The simulation primitives stay Game.Core-owned under
  ADR-0033.
- Influence maps → `Decision`, where the survey puts them.
- BM25 ranking → `Systems`.

**Pack metadata** follows the survey: audience, determinism grade and status (`pointer`,
`harvested`, `new` or `entry-only`), carried as tags (§6).

**Seed list.** It is ordered in three parts:

1. **Layer-0 foundations**, because almost everything else uses them.
2. **Harvest:** pointer entries over reviewed Game.Core code, then harvests of FS-GG code from the
   coordination and game inventories.
3. **The survey's ranked new items**, each with its property oracle in brackets.

The Outcome's 15-entry floor is met by part 2 alone, from reviewed, property-tested code.

**1. Layer-0 foundations (tranche 1)**

| # | Entry id | Status | Grade target | Oracle and acceptance |
|---|---|---|---|---|
| F1 | `collections/binary-heap` (with the indexed heap and decrease-key) | new | LockstepExact | Sort equivalence. Then the migrations in §3: Game.Core's `Set` frontier, FourD's list re-sort, RuneScape's `PriorityQueue` |
| F2 | `numerics/fixed-q16-16` | new | LockstepExact | Error bounds against exact rationals, and bit-identity between .NET and Fable. No `int64` |

**2. Harvest (tranche 1)**

Pointer entries cite Game.Core as the implementation of record. Harvested entries are clean
re-implementations into `FS.GG.Algorithms`.

| # | Entry id | Implementation of record | Status | Audience | Owed before the bar is met |
|---|---|---|---|---|---|
| 1 | `search.grid/astar` (with `astarStraight`, `smooth`) | Game.Core `Pathfinding.astar` (LockstepExact, ~40 FsCheck; used by the fable-game template) | pointer | game | A* vs BFS cost property; F1 migration; benches |
| 2 | `search.grid/bfs-dijkstra-reachability` (`bfs`, `reachable`, `reachableWithin`, budgets) | Game.Core `Pathfinding`. FourD's budgeted Dijkstra is provenance | pointer | game | Benches. FourD's re-sorted list frontier is recorded as a pitfall |
| 3 | `search.grid/jps` | Game.Core `Pathfinding.jps` | pointer | game | Fable grade; benches |
| 4 | `search.grid/flow-field` (distance and flee fields) | Game.Core `distanceField`, `flowField` | pointer | game | Fable grade; benches |
| 5 | `search.grid/alt-landmarks` (with the `Regions` early-out) | Game.Core `Landmarks`, `Regions` | pointer | game | Benches |
| 6 | `search.grid/doors-and-thin-walls` | Game.Core `Edges` A*, with RuneScape's door overlay and per-session re-close as a generic technique | pointer (+ lesson) | game | No flag data (§5); benches |
| 7 | `geometry/line-and-los` (Bresenham, supercover, symmetric LOS) | Game.Core `Los` (LockstepExact) | pointer | game | Benches |
| 8 | `geometry/shadowcasting-fov` | Game.Core `Fov` | pointer | game | Fable grade (integer-only); benches |
| 9 | `geometry/visibility-polygon` | Game.Core `Visibility` | pointer | game | Records the Harness and Rendering-fragment copies as non-canonical |
| 10 | `geometry/hex-coordinates` | Game.Core `Hex` | pointer | game | Fable grade |
| 11 | `spatial/uniform-grid-hash` (radius, bounds and nearest queries) | Game.Core `SpatialGrid` (Portable) | pointer | game | Benches. RuneScape's SQL full-scan nearest query is the baseline pitfall. Records its relation to Rendering's `SpatialWorkingSet` (culling, a different purpose) |
| 12 | `geometry/sat-and-swept` | Game.Core `Geometry` (Portable, ~28 FsCheck) | pointer | game | Benches |
| 13 | `decision/influence-maps` | Game.Core `Ai.influenceMap`, threat and flee fields | pointer | game | Benches |
| 14 | `random/splitmix64` (splittable streams) | Game.Core `Rng` | pointer | both | Fable grade; the modulo-bias pitfall |
| 15 | `procgen/cellular-caves-and-bsp` | Game.Core `MapGen` | pointer | game | None beyond seed byte-identity |
| 16 | `procgen/poisson-disk` | Game.Core `MapGen.poissonScatter` (Bridson) | pointer | game | Benches |
| 17 | `graph/articulation-points` (map analysis) | Game.Core `MapAnalysis` | pointer | both | None |
| 18 | `graph/iterative-tarjan-scc` | Harvest of `.github` `Blockers.cycles`, rewritten iteratively | harvested | both | [Equals the recursive version; a 10⁵-node chain]. Pitfalls: recursive Tarjan, and SDD `WorkModel.cycleDiagnostics` with no visited set |
| 19 | `graph/union-find` | Harvest of `.github` `Lanes.partition`, generic over the conflict predicate | harvested | both | [Equals BFS components] |
| 20 | `numerics/nearest-rank-percentile` | Harvest of Governance `Performance.nearestRank` | harvested | both | Used by the JavaScript bench summaries themselves |
| 21 | `decision/priority-arbitration` (bands, pre-emption, suspend and resume) | Clean re-implementation of RuneScape's arbiter | harvested | game | **Waits for RuneScape PR #14 to merge.** Port its 400-run invariant to FsCheck |

**3. New, ranked (tranche 1)**

These are the survey's §14 order, without the three items already placed above: the heap (F1),
fixed-point (F2) and iterative Tarjan (harvest 18). Ids that build on Game.Core concepts stay
generic. For example, `net/rollback` takes a caller-supplied step and checkpoint interface, so
FS.GG.Algorithms never depends on Game.Core. Game.Core's `Replay` is one consumer of it.

| # | Entry id | Oracle | Audience |
|---|---|---|---|
| N1 | `graph/topological-sort-cpm` (Kahn, critical path) | Edges are respected, and a cycle is reported exactly when iterative Tarjan finds an SCC larger than one node | both |
| N2 | `decision/behaviour-tree` (with a blackboard) | Tick determinism | game |
| N3 | `decision/utility-ai` (IAUS) | Scores bounded to 0–1; monotone curves (on F2) | game |
| N4 | `decision/hfsm` | Entry and exit properly nested | both |
| N5 | `adversarial/negamax-alpha-beta-tt` (iterative deepening, Zobrist keys as two `int32` halves) | Equals plain minimax on random trees | game |
| N6 | `adversarial/mcts-uct` (with an ISMCTS mode) | Converges on small solved games | game |
| N7 | `steering/reynolds` | Arrives within ε with no overshoot | game |
| N8 | `net/prediction-reconciliation-interpolation` | Converges to the server under a lossy simulator | game |
| N9 | `net/rollback` | Byte-identical to a zero-latency run (needs a `LockstepExact` step) | game |
| N10 | `systems/backoff-jitter-token-bucket` | Delays stay within bounds; the long-run rate is at most r | both |
| N11 | `search.mesh/navmesh-astar-funnel` | No longer than the portal-midpoint path; LOS-clear | game |
| N12 | `search.grid/lazy-theta-star` | No longer than A* + `smooth`; LOS-valid | game |
| N13 | `search.grid/hpa-star` | Suboptimality against A* stays within a bound | game |
| N14 | `spatial/kd-tree` | Equals a brute-force scan | game |
| N15 | `optimization/csp-ac3` (MRV, LCV) | Agrees with brute force on small instances | both |
| N16 | `optimization/local-search` (hill climbing, simulated annealing, tabu) | Never worse than the start; seed-deterministic | both |
| N17 | `optimization/hungarian` | Equals the brute-force minimum for n ≤ 7 | both |
| N18 | `procgen/wfc-simple-tiled` | All adjacencies legal; seed byte-identity | game |
| N19 | `procgen/opensimplex2-worley` | Range bounds; seed byte-identity | game |
| N20 | `learning/bandits-ucb1-thompson` | Sublinear regret on synthetic arms | both |
| N21 | `decision/goap` | Plan cost equals BFS over the state space | game |

#5224's "at least two classic additions" are taken from this list in order.

**Pitfall entries (tranche 1, `entry-only`):**

- `numerics/deterministic-float-pitfalls` (FMA, x87, transcendentals, fast-math; a survey P1);
- the glob matcher's backtracking (worst case exponential);
- three visibility implementations and two spatial indexes;
- recursive Tarjan, and DFS with no visited set (also attached to harvest 18).

**Next tranche.**

- **Harvest and entry-only:**
  - `geometry/exact-slab-raycast` (FourD, exact rationals);
  - `collections/lis-patience-diff` (`.github` `SemanticDiff`);
  - `collections/interval-union`;
  - `numerics/fixed-timestep` (pointer);
  - `net/delta-snapshot-delivery` (pointer to FS.GG.Net `Delivery`);
  - `net/baseline-delta-resync`;
  - `net/bounded-queue-backpressure` (across BAR, Unity, Net and SC2);
  - `decision/observe-decide-execute` and `decision/effect-proof-reconciliation` (RuneScape);
  - `random/dice-distributions` (pointer);
  - `systems/bm25-fts`.
- **New, in the survey's next-in-line order:** D* Lite, ORCA, Delaunay and Voronoi, simplex LP,
  flows, WHCA*, GJK/EPA, CMA-ES, LRU. The survey's P2 and P3 rows follow after these.

## Consequences

- **FS.GG.Algorithms (new).**
  - Root creates the repository.
  - It needs its build, Fable test, bench and release workflows, Trusted Publishing for both
    packages, the publication-writer registration, and the producer manifest generator and gate.
  - It also needs a pack-lint gate that runs `atlas.py validate` and enforces the §6 limits, the
    summary headings, at least one public citation, and the absence of private content.
- **FS.GG.Game.**
  - Game becomes the single owner of the four algorithm skills, and widens three of their
    predicates (§8).
  - It owes timing benches, Fable grades for integer-only surfaces, and the explicit A* vs BFS
    property for indexed entries (§4).
  - Game.Core's published surface is unchanged. Delegation happens only under §1's move-on-demand
    rule.
  - The one exception is the layer-0 heap (§3): Game.Core takes a package dependency on
    `FS.GG.Algorithms`, and its Fable view resolves the library's packed sources.
- **FS.GG.Rendering.** Rendering retires its four duplicate skill copies and their adaptable
  fragments, subject to its owner's confirmation (§8).
- **FS.GG.SDD.**
  - SDD embeds `FS.GG.Algorithms.Skills` through the multi-file planner.
  - It adds the pristine owner-file replacement rule to upgrade (§7), which also cures stale owner
    skill bodies everywhere.
- **FS.GG.Templates.** The `fableGameSelections` oracle expects grids, line-drawing, visibility and
  algorithms. No template payload changes; the skill rides the owner route.
- **`.github`.**
  - The registry rows of §7 are added. The four skill rows are reconciled to owner Game.
  - The capture rule enters the pnext-item and work-board closure contracts (#5226).
  - `docs/architecture.md` records FS.GG.Algorithms as a decided, not-yet-created bottom layer. Its full row is reconciled after the roster row lands.
  - This record's Atlas pointer is `platform/algorithms-capability`.
- **Cost accepted.** A new repository adds one more release line and one more SDD embed, and pack
  updates reach workspaces only through an SDD release that pins the new skills package. The
  alternative couples coordination to a game package, so the cost is accepted.
- **Open questions, with the default this ADR applies until Root decides otherwise:**
  1. *#5224's "Absorbed" clause* ("the algorithms behind the existing grids, collision, line-drawing
     and visibility skills"). **Default:** satisfied by indexing them in place with their owed
     benches and grades. No extraction from Game.Core (§1).
  2. *Pack update cadence.* **Default:** SDD takes `FS.GG.Algorithms.Skills` bumps through its
     existing dependency-update route, like the other owner skills packages. No out-of-band pack
     channel.
  3. *Predicate breadth.* **Default:** `profile in [game, sample-pack] or template == fable-game`.
     Widen to app profiles only on a recorded request.
  4. *Pristine-replacement scope.* **Default:** all owner-sourced product skills, not only this one.
  5. *Bench gating.* **Default:** work counts may gate regressions; timings never gate.
  6. *Rendering's confirmation of the retirement in §8.* **Default:** until Rendering confirms, the
     registry still reconciles to owner Game (it reflects delivered bytes), and Rendering's copies
     are marked non-canonical.
  7. *Fable support for the BCL `PriorityQueue`.* Unverified. **Default:** treat it as
     unsupported. The library ships its own heap (§3), and #5224's tranche measures support.
  8. *The cost of `int64` as a BigInt under Fable.* Unmeasured. **Default:** no `int64` in hot
     paths.
     - Q16.16 splits multiplies into 16-bit halves.
     - Zobrist keys use two `int32` halves.
     - Q32.32 and jump hashing wait for #5224's measurement.
  9. *Acceptance for the Game.Core heap migration.* **Default:** the `LockstepExact` fixture corpus
     stays byte-identical, and the bench shows no regression against the `Set` frontier on both
     runtimes. If the bench regresses, Game.Core keeps its `Set` frontier and records why.

## Alternatives considered

- **FS.GG.Game as the producer**, with a new `FS.GG.Game.Algorithms` package or Game.Core itself.
  - For: no new repository; Game.Core already holds most of the code; `FS.GG.Game.Skills` already
    reaches workspaces.
  - Rejected on dependency direction: Coordination and `.github` would depend on a game package, and
    Rendering would cycle with Game. Also rejected on cadence: algorithm releases would ride Game's
    coherent set.
  - It would also need the Game skill channel extended past `SKILL.md`-only, which is the same work
    as embedding a new owner package.
- **Extract all of Game.Core's algorithms into FS.GG.Algorithms now.**
  - Rejected. It churns a published, Fable-graded surface with no non-game consumer waiting.
  - It puts Game.Core's lockstep fixtures at risk, and makes every game take a new dependency.
  - §1's move-on-demand rule keeps the option open.
- **Library in FS.GG.Algorithms, pack in `FS.GG.Game.Skills`.** Rejected: entry and implementation
  would land in different repositories, and the capture rule would always need two PRs.
- **Fold the four skills into the umbrella.** Rejected: it forces new digests, an owner migration and
  re-seeds for no knowledge gain. Indexing preserves each skill's usage text as the one copy (§8).
- **A new pack record format.** Rejected: it would need its own validator and search tooling. The
  closed Atlas format already carries every field, and `atlas.py` already reaches workspaces as a
  driver.
- **A copy of `atlas.py` inside the skill.** Rejected under ADR-0067: two copies drift. The driver is
  resolved instead, with a `grep` fallback.
- **Pack only in the framework Atlas.** Rejected by the Outcome: it must be usable in product
  workspaces.
- **Pack as a Templates payload.** Rejected: it would be provider-scoped, missing `profile game`
  workspaces, which is ADR-0063's defect class.
- **Append-only, version-named pack directories**, so that upgrade only ever writes missing paths.
  - Considered as a way to avoid the SDD change.
  - Rejected: `SKILL.md` itself cannot update, superseded versions accumulate in every workspace,
    and the multi-file planner refuses extra files in a skill directory.
  - It remains the fallback if SDD declines §7's pristine-replacement rule.
- **The BCL `PriorityQueue` as the shared frontier.** Rejected for three reasons:
  - its support under Fable is unverified;
  - it has no decrease-key;
  - it does not define an order among equal priorities, which would leak non-determinism into
    lockstep searches.
- **This ADR's first-draft families** (`pathfinding`, `grids`, `sequences`, `statistics`,
  `simulation`) instead of the survey's 17. Rejected: the survey also covers steering, adversarial,
  learning, net and systems. One vocabulary shared by the survey, the library namespaces and the
  pack tags needs no mapping. §11 records where the first draft's extra families now live.
- **Rendering's #761/#792 harness as the admission bench.** Rejected: it times frames, not kernels.
  It is kept only for optional browser timing (§4).
