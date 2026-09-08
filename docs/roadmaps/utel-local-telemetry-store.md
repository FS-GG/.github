# UTEL — Durable local telemetry store

Backlink: [Unified Development Roadmap §9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)

## Outcome

Retain bounded, typed telemetry facts across process loss in one durable SQLite store per host without making
telemetry a delivery authority. Workers never touch SQLite: they atomically publish immutable private batches;
the orchestrator or recovery command owns one bounded drain. Readers use read-only WAL snapshots.

## Delivered window

- [x] Add closed typed fact validation/reduction in `FS.GG.Coord.Core`, including checked counters, native
  identity/content-digest replay, explicit revisions and separate validity, join, coverage and qualification.
- [x] Pin `Microsoft.Data.Sqlite` and its native bundle centrally; require SQLite 3.51.3 or newer at runtime,
  apply checksummed schema migrations, and configure WAL, `synchronous=FULL` and foreign keys for writers.
- [x] Validate an explicit `--store-root` before creation or ingestion. Unconfigured, temporary, symlinked,
  worktree, network, memory, unqualified-container and unknown placement fail closed; tests inject the approved
  disposable-root assessment and production has no unsafe override.
- [x] Publish at most 64 observations/64 KiB to
  `<root>/inbox/<producer-stream>/<batch-id>.<digest>.ready` using exclusive temporary creation, flush, atomic
  no-replace rename and directory sync. A producer may have at most 128 ready batches and never waits for or
  inherits a SQLite/writer lock.
- [x] Drain fairly under the stable nonblocking `writer.lock`, at most 128 batches/8 MiB. Validate before a
  short immediate transaction; commit facts, deduplication, cursor and coverage together; delete and sync only
  after commit. Invalid/conflicting input is privately quarantined with a bounded diagnostic and never implies
  coverage. The lock is never stolen, unlinked or PID/TTL expired.
- [x] Provide deterministic JSON `status`, `init`, `publish`, `drain`, `ingest`, `summary --item` and
  `export --public --item --output`. `ingest` is publish plus opportunistic drain and reports `queued` when the
  writer is busy. Status and summaries never drain or mutate the database.
- [x] Bound public exports to 64 KiB and allowlisted aggregates, retaining unknown population/not-evaluated
  semantics. Extend ignore/index guards for SQLite, WAL/journal, inbox/quarantine and renamed private signatures.
- [x] Preserve `UsageReceiptStore` and ADR-0082; structured storage adds no transcript retention and does not
  rewrite historical receipts.

## Recovery and acceptance evidence

Runtime tests cover reopen stability, replay with a different ingest ID, conflicting identity rollback,
oversize/malformed/count/overflow rejection, live-writer refusal and holder-death takeover, ready replay after
commit, private quarantine, unknown coverage, unconfigured roots, native engine/version and Git-safety cases.
Temporary files are ignored; a ready file is the pre-commit recovery point; a committed-but-not-removed ready
file replays through native deduplication and is then cleaned. Removal failure remains visible and retriable.

## Boundaries and workspace impact

There is no permanent server, host installation, package publication, receiver adoption, automatic collection,
private backfill or Python SQL access in this window. Backup/migrate/restore commands, DuckDB, Parquet and
PostgreSQL remain deferred. Freshly generated workspaces and enabled runtime defaults are unchanged; the first
behavior change is source merge for an explicit caller, while publication and receiver adoption remain pending.

The immutable worker batch is also a future Akka message-contract seam. A later per-host telemetry-writer actor
could own normal-path serialized drains and supervision, but UTEL-02 adds no Akka dependency and does not change
roadmap sequencing. SQLite locking and deduplication remain the final recovery boundary across process death,
upgrades and old/new overlap.

## UTEL-03A — Explicit Codex exec observation

- [x] Add `telemetry runtime codex-exec --assignment <private-file> -- <Codex args>` for future explicit
  `codex exec --json --ephemeral` launches. The closed assignment contains stable feature, original-item,
  attempt, optional parent-attempt and producer-stream identities, never task text.
- [x] Publish admission before launch, then process/thread starts, completed-turn usage, typed gaps and the
  terminal native exit through the UTEL-02 inbox. Native stdout bytes, inherited stdin/current directory,
  permissions and exit status remain the child contract; telemetry failure never changes them.
- [x] Project JSONL incrementally through a bounded nonblocking queue. Prompts, messages, reasoning text,
  commands, tool I/O, diffs, paths and raw JSON are discarded. Oversized/malformed/lost framing becomes a
  bounded gap; exit zero or a final message never fabricates usage.
- [x] Add checksummed schema migration 2 for launcher admissions, process/thread starts, native-thread joins,
  per-turn usage, terminals and gaps. Requested and observed model/effort/backend remain distinct and nullable;
  token scope/provenance is explicit, and native turn identity or documented invocation-local sequence dedups.
- [x] Report admitted, started, terminal, usage and missing-registration/start/terminal/usage populations
  independently in read-only summaries. Add a source-only runtime capability status that reports installed
  Codex, adapter support, configured/unconfigured store and `collaboration.spawn_agent` as unsupported.
- [x] Maintain the operator and schema contract in the
  [local telemetry store reference](../reference/local-telemetry-store.md).

This window does not scan historical sessions, hook or wrap existing agent launches, claim current
`collaboration.spawn_agent` coverage, publish packages, change generated workspaces, or enable collection by
default. Current-host qualification is a separate post-merge future-only check. Publication and adoption remain
pending. The immutable assignment plus observation batch remains compatible with a later telemetry-writer actor;
SQLite locking and deduplication continue to protect recovery and mixed-version overlap.
