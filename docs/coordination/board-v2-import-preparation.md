# Coordination V2 bounded import preparation

This is the retained dry run for **COORD-BOARD-V2-01.2**. It fixes the first candidate boundary,
adjudication, schema ownership and representative pilot before a Projects write. The machine-readable
[manifest](board-v2-import-manifest.json) records the exact source issue node IDs and observed issue
revisions. The legacy `Coordination` Project 1 remains intact.

Observed 2026-09-30 through current repository issue REST reads and the existing legacy-board
inventory. The bounded population is deliberately eight issues: direct active program identities,
one representative cross-repository receiver, and one superseded V1 comparator. It is not a claim
that every legacy row was adjudicated.

## Live consumer inventory

| Consumer | Current binding or behavior | V2 disposition |
|---|---|---|
| `src/FS.GG.Coord.GitHub/Board.fs` and `.fsi` | `exact-project1` pins owner `FS-GG`, Project 1, title `Coordination` and node `PVT_kwDOEYAWY84Bb08W`; normal bootstrap resolves owner/title and caches the result | Keep the pinned mode V1-only. COORD-BOARD-V2-01.3 must add an explicit identity-bound V2 route; title substitution is unsafe. |
| `src/FS.GG.Coord.Cli/Client.fs` | Scheduling, reconciliation, claim, delivery and field writes consume the legacy schema through `Board.bootstrapCached` | Do not point this lifecycle writer at the smaller schema. Only a restricted V2 membership/observation adapter may write the new board. |
| `src/FS.GG.Coord.GitHub/Cache.fs` and `.fsi` | Scan and board-map caches are keyed by owner plus title; item IDs are retained without a TTL | V2 adoption must use a distinct title/key and refresh the V2 board map after exact field readback. Old item-ID cache entries stay associated with `Coordination`. |
| `.agents/skills/check-board`, `drive-board`, `p-add` and `pnext-item` | Invoke `scripts/fsgg-coord`; several treat legacy fields, claims or board state as scheduling inputs | Keep them on V1 until an explicit V2 consumer contract is qualified. V2 project membership alone cannot switch these skills. |
| `scripts/NewSddWorkspace/Program.fs`, README and tests | Generated coordinated workspaces default `FSGG_COORD_OWNER=FS-GG` and `FSGG_COORD_PROJECT=Coordination`; `--board` is title based | COORD-BOARD-V2-01.5/.6 must publish an immutable V2 binding and upgrade path. This import does not change generated defaults. |
| `.github/workflows/coord-board-reconcile.yml` | Manual, credential-free retirement diagnostic; reads and writes no board state | Retain as the V1 retirement diagnostic. It is not a V2 writer. |
| `.github/workflows/coord-board-archive.yml` and `scripts/coord-board-archive.py` | Scheduled legacy Project 1 archive writer using a narrowly scoped App token and reversible manifest | Keep targeting legacy Project 1. Do not run it against V2; broader archive policy is outside this import. |
| `scripts/fsgg-coord` and installed `fsgg-coord` | Sole metered Projects GraphQL principal; can resolve and mutate an existing selected board but has no project-creation command or V2 schema contract | Extend or add a separately reviewed fixed operation before creation/import. Direct `gh project` or `gh api graphql` calls are prohibited by `graphql-budget.md`. |

## Exact V2 schema and owners

Built-in Title, Repository and Assignees continue to come from each existing issue. The new project
has exactly four custom planning fields:

| Field | Type and values | Write owner after import | Initial import |
|---|---|---|---|
| Status | single select: Backlog, Ready, In progress, Blocked, Done | Human scheduling owner | Seed the adjudicated scheduling state once. Restricted refresh cannot overwrite it. |
| Roadmap | text link/path to the owning outcome and acceptance evidence | Owning plan or issue owner | Seed the adjudicated owning plan once. Refresh may report a broken link but cannot replace it. |
| Track | single select: Active delivery, Follow-up | Human scheduling owner | Seed the selected track once. |
| Observation | single select: Verified, Stale, Unknown | Restricted refresh writer | Seed `Verified` only for the recorded complete REST observation. Later refresh reports identify the last successful observation separately. |

The checked-in JSON Schema fixes those names, options and owners. Project and field node IDs remain
`null` until the authorized operation creates the project and reads every identity back. The manifest
refuses invented IDs, duplicate canonical issue/node identities, a pilot item selected for omission,
or machine ownership of a scheduling field.

## Adjudicated dry run

| Existing issue | Decision | Initial projection | Reason |
|---|---|---|---|
| `FS-GG/.github#2954` | Omit, superseded | none | ADR-0091 superseded the unfinished V1 migration sequence. The open issue stays historical and does not become Ready again. |
| `FS-GG/.github#2963` | Import, pilot | In progress / Active delivery | The V2 roadmap retains unfinished qualification, publication and protected effects. |
| `FS-GG/.github#2994` | Import | Backlog / Active delivery | The program anchor retains open consumer, retirement and decision outcomes. |
| `FS-GG/.github#2995` | Import, pilot | Blocked / Active delivery | Its producer dependency `FS-GG/FS.GG.SDD#924` is closed, but the owning design requires Q5 acceptance and #2963 remains open. |
| `FS-GG/.github#2996` | Import | Blocked / Active delivery | Retirement still depends on adoption and soak evidence. |
| `FS-GG/.github#2997` | Import as selected follow-up | Blocked / Follow-up | The default decision remains deliberately separate and depends on #2996. |
| `FS-GG/FS.GG.Coordination#24` | Import, pilot | Blocked / Active delivery | Its distinct read-only projection outcome remains behind #2963. |
| `FS-GG/FS.GG.Templates#438` | Import | Ready / Active delivery | The cross-repository receiver still exact-pins the prior Game.Core version. |

There are seven approved additions, one omission, three pilot additions and no unresolved
adjudications. Dependencies remain references to their existing issues. No issue is cloned, reopened,
closed or edited.

Run the deterministic checks from the repository root:

```console
python3 tools/board-v2-import.py docs/coordination/board-v2-import-manifest.json
python3 tools/board-v2-import.py docs/coordination/board-v2-import-manifest.json --pilot-plan
python3 tests/board-v2-import/run.py
```

## Pilot operation and current boundary

The pilot order is `.github#2963`, `.github#2995`, then `FS.GG.Coordination#24`. For each item the
operation must read membership first, add only when absence is established from a complete response,
seed the four manifest fields, and read back:

1. the exact target project and four field identities;
2. one target membership for the unchanged source issue node ID;
3. all four seeded field values; and
4. the unchanged dependency references and their observed states.

A lost response, incomplete page, unknown field or target mismatch stops the affected item and records
an unresolved observation. Retrying begins with membership readback. The operation never mutates issue
state, title, body, assignees, dependencies or legacy Project 1 membership.

Project creation and pilot application remain pending. The current PAT could read issue REST data and
the existing board through the legacy path, but the organization project command reported that the PAT
cannot access the organization Projects collection. More decisively, repository policy permits Projects
operations only through `fsgg-coord`, and the installed client has neither a project-creation command nor
an explicit V2 schema/import route. Direct Projects GraphQL would bypass the shared budget and operation
boundary. COORD-BOARD-V2-01.2 therefore remains unchecked until that fixed operation is available and
the identities, memberships, fields and dependency references above are read back.

## Workspace effect

This preparation changes no generated workspace, package or default. The first organization planning
effect occurs only when the pending project and representative pilot are applied. Fresh and retained
workspace behavior remains assigned to COORD-BOARD-V2-01.5/.6 after the restricted adapter is qualified.
