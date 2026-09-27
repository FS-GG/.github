# SKILL-FS-01.1 frozen conversion contract

Frozen against correction commit `2da4baf23a68816e898f703638124d8ed915d737` on 2026-09-27. The reviewed
implementation inventory is four logical Python files and eight tracked copies. Each `.agents` file is byte
identical to its `.claude` counterpart at this revision:

| Logical implementation | SHA-256 |
|---|---|
| `work-roadmap/scripts/fsgg_telemetry_defaults.py` | `ba04813cf1688065cbe5e2c603bf4761480e9856b965fac130c1b24c1c97eb63` |
| `work-roadmap/scripts/native_collaboration_usage.py` | `ef81137ff93c66b744c68a2f02a48fc18bcfade7cd7cc93f16005c1ed12059c8` |
| `work-roadmap/scripts/roadmap-telemetry.py` | `a82e04b8fbdd1a1cfee93b5582bafb6d71f43ae966ce11ef04b5bbb3588c4e14` |
| `pipeline-preflight/scripts/preflight.py` | `84f6917eaea1c3cb42f9abbef7e92802f02ba7fae891957fd587a6f87705c1f3` |

The executable corpus is in `tests/skill-fsharp/contracts`. It contains public synthetic identities only. It
does not invoke a live telemetry engine, private host, GitHub write, dashboard publisher, or credential client.

## Process command contract

`roadmap-telemetry.py` accepts global `--config PATH`, then one command. Successful command results are one
compact JSON object plus LF on stdout and exit 0. A configured operational refusal is one line on stderr,
`fsgg roadmap telemetry: MESSAGE`, and exit 1. When discovery finds no default config, every syntactically
valid command emits exactly
`{"schema":"fsgg.telemetry.host-status/1","status":"not-configured"}\n` on stdout and exits 2.
Argparse usage errors use argparse's stderr and exit 2.

| Command | Required arguments | Optional arguments and defaults | Successful result |
|---|---|---|---|
| `begin` | `--feature`, `--item`, `--attempt`, `--model`, `--effort` | `--parent-attempt`; `--original-item` defaults to item; `--parent-token`; `--relation root`; `--producer roadmap-orchestrator`; `--late-after-seconds 60` | `fsgg.telemetry.roadmap-dispatch/1`, status `expected`, opaque token, explicit coverage |
| `population-only` | `--feature`, `--item`, `--original-item` | `--producer roadmap-orchestrator` | `fsgg.telemetry.original-binding-result/1`, status `applied` |
| `started` | `--token`, `--native-id` | none | roadmap dispatch, status `started`, token, coverage |
| `finish` | `--token`, `--outcome completed|failed|cancelled|blocked` | `--exit-code`; defaults to 0 only for completed and 1 otherwise | roadmap dispatch, status `terminal`, outcome, coverage, drain and optional advisory dashboard result |
| `usage-reconcile` | `--token` | none | `fsgg.telemetry.roadmap-usage/1`, status `reconciled`, coverage and drain |
| `ci-assignment` | `--feature`, `--item`, `--attempt` | `--parent-attempt`; `--producer routine-delivery` | `fsgg.telemetry.assignment-result/1`, status `ready`, private assignment path |
| `review` | `--token`, `--scope attempt|item`, `--input` | none | `fsgg.telemetry.roadmap-observation/1`, status `recorded`, kind and optional dashboard result |
| `activity` | `--token`, `--input` | none | roadmap observation, kind `activity-span` |
| `usage-attribution` | `--token`, `--input` | none | roadmap observation, kind `activity-usage-attribution` |
| `complication` | `--token`, `--input` | none | roadmap observation, kind `complication` |
| `status` | none | none | `fsgg.telemetry.host-status/1`; ready/0 or unavailable/1 |

Input files for review, activity, attribution and complication must be regular, non-symlink files no larger
than 32 KiB and have an exact closed shape. Their schemas and fields are frozen in `acceptance-corpus.json`.
Output paths and tokens are private values and are shape-contracted rather than byte-contracted. Static JSON
successes and preflight results are byte-contracted.

`preflight.py assess PATH` and `preflight.py graph PATH --requires TARGET:SOURCE[,SOURCE]` read at most 2 MiB.
They pretty-print JSON with Python's `indent=2` format plus LF. `passed`, `insufficient-data`,
`not-cost-justified`, `uncertain`, and `pilot-candidate` exit 0; dependency `blocked` exits 1. Any read,
UTF-8, JSON/YAML, closed-shape, finite-number, parser, graph-shape, or bound refusal writes
`{"decision": "error", "message": "..."}\n` to stderr and exits 2. `graph` accepts literal job mappings and
literal `needs` strings/lists only. It rejects duplicate keys and dependencies, unknown jobs, cycles, more
than 256 jobs, unsupported expressions/shapes, and malformed requirements. It checks dependency ancestry
only; it does not evaluate conditions, job results, reusable workflows, or general GitHub expressions.

## Python import contract and caller seams

These are import APIs today and must be replaced by the narrow typed interface in `SkillConversionContract.fsi`:

- `tools/routine-delivery.py` imports configuration discovery, CI assignment creation, and the CI schema.
- `tools/telemetry-dashboard.py` dynamically imports configuration discovery for dashboard publication.
- `roadmap-telemetry.py` imports all configuration/state helpers and `native_collaboration_usage.collect`.
- `tests/skill-quality/roadmap-telemetry.py` and `native-collaboration-usage.py` import the Python modules as
  production oracles. These tests must move to process/typed calls before oracle retirement.
- `tools/roadmap-telemetry.py` is a process wrapper that `exec`s the `.claude` adapter with unchanged arguments.
- `pipeline-preflight/SKILL.md` and its examples invoke `preflight.py` as a process. It has no package entry.

Repository identity precedence is `FSGG_TELEMETRY_REPOSITORY`, then `GITHUB_REPOSITORY`, then exactly one
credential-free canonical GitHub `origin`. Configuration precedence is explicit `--config`, then
`FSGG_TELEMETRY_CONFIG`, then `$XDG_CONFIG_HOME/fs-gg/telemetry.json` or
`$HOME/.config/fs-gg/telemetry.json`. Configuration is a regular absolute 0600 file, at most 64 KiB, with a
closed schema. Credentials are represented only by a bounded reference. The interface never returns or prints
credential material. Workspace mutations use an already-loaded named environment credential or an
owner-controlled `fdev-telemetry exec` wrapper.

## Durable state compatibility

The production state schema is `fsgg.telemetry.roadmap-dispatch-state/1`. Files live under the selected private
store at `orchestrator-dispatches/<32-hex-token>.json`, are regular non-symlinks, mode 0600, and at most 256 KiB.
Original bindings use `fsgg.telemetry.original-binding-state/1` under `orchestrator-original-bindings`.

Every publication increments `sequence` once and persists `pendingPublication` before submission. Its batch has
schema `fsgg.telemetry.ingest/1`, stable `ingestId`, generation, cursor, count and event bytes. An unknown or
ambiguous submission outcome retains that exact intent and the retry replays equal JSON bytes. Only a definitive
`invalid-request` rolls back that unpublished cursor and intent. A terminal retry must equal the retained outcome
and derived or explicit exit code. Legacy terminal state without `exitCode` may be completed only with the
derived default; an explicit retry is refused. Incompatible schema, token, association, permissions, identity,
phase, or pending intent refuses before a write. The executable replay fixture proves exact pending-batch replay.

A non-self original item is accepted only after an immutable read of `FS-GG/.github` main authorizes the exact
feature/member/original tuple. `population-only` requires a distinct original, a receipt-scoped workspace, and
an `applied` receiver receipt. `durably-received` remains pending and retries the exact binding. Existing binding
state with different protected identity refuses before a network read. Child and follow-up attempts inherit the
original and must share item identity. Missing or partial native usage remains `unknown` or `unsupported`; it is
never converted to zero or measured usage.

The native reader launches `codex app-server` without a shell and uses read-only thread methods. Each request
has an 8 second deadline and a 1 MiB line bound. Paging is limited to ten 100-row pages and 1,000 total rows;
retained App Server and rollout evidence are each bounded to 512 KiB. Rollout paths must remain beneath the
private `CODEX_HOME/sessions` tree, every directory and file is opened without following symlinks, and a rollout
file is limited to 128 MiB. Only `token_usage_record` rows are decoded. Conversation content is neither decoded
nor placed in this corpus. Duplicate response identities use their final counters; cached input and reasoning
must remain subsets, and total must equal input plus output.

## Packaging and observed receiver boundary

At the frozen source, `FS.GG.Coord.Cli`, `FS.GG.Kit`, and `FS.GG.Drivers` share source version `0.91.4`.
`generate-driver-manifest` reads `.claude` content and includes all three work-roadmap Python files in
`FS.GG.Drivers`; pipeline preflight is classified `neither`. `src/FS.GG.Drivers/workspace-inventory.json` stages
only `fsgg_telemetry_defaults.py` from this set as `tools/fsgg_telemetry_defaults.py`. There are no tracked
`.nupkg` files. `skill-view` creates receiver views and refuses to overwrite tracked roots.

The machine's global tool installation and owner tool cache both resolve `FS.GG.Coord.Cli 0.91.4`. Installed
package state inside the receiver worktrees was not present and is therefore unknown. Working-copy pins observed
locally are evidence about those checkouts, not installed proof: SDD pins Coord/Kit/Drivers 0.91.4; Governance,
Game, Audio and Net pin Coord/Kit 0.91.4; Rendering pins Coord 0.90.0 and Kit 0.91.4; Templates pins Coord/Kit
0.90.0; Coordination pins Coord 0.91.4. Other receiver pins remain unknown until milestone .5 selects and reads
an authoritative installed receiver.

## Stage boundary and worker handoff

- Source: this contract and corpus qualify the current Python oracle. Milestones .2, .3 and .4 may build from it.
- Package: no candidate package is built or accepted by this milestone.
- Published: no feed or tag result is claimed.
- Installed: local tool observations above do not prove selected receiver adoption.

Readers (.2) implement the `Configuration` and `NativeUsage` signatures and run all `readers` corpus cases.
Adapter (.3) implements `TelemetryAdapter`, consumes the readers, accepts retained v1 state, and runs all
`adapter` cases including replay and protected original refusals. Preflight (.4) implements `Preflight` and runs
all `preflight` cases. The integrator alone registers commands, changes project/package files and live callers,
synchronizes both skill roots, regenerates manifests, and composes the lanes. A state-schema mismatch must revise
this contract before composition. The current telemetry adapter reports `not-configured`; native usage and event
publication coverage for this work therefore remain unknown.

The proportionate preflight for this contract is the existing static corpus plus focused oracle tests. It catches
interface drift, refusal drift and replay identity changes before the later compiled lanes. No workflow or costly
qualification is introduced here. The current environment has no PyYAML, so parser-backed positive CLI graph,
duplicate-key parsing and actual-workflow binding refuse at exit 2; the corpus freezes those inputs for .4's
selected .NET YAML parser and records the unavailable-parser launch control. Existing required checks remain in
force.
