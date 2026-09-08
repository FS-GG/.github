# Local telemetry store reference

The local telemetry store is private, host-local SQLite storage for bounded coordination observations. It is
not a delivery authority, transcript archive, network database, or automatic collector.

## Configure and initialize

Set `FSGG_TELEMETRY_STORE` to an absolute durable host-backed directory, or pass `--store-root` explicitly.
The explicit option wins. With neither, `status` reports `unconfigured` and creates nothing. Initialization
rejects temporary, repository/worktree, symlinked, network, memory, overlay and unverified placement, as well
as unsafe ownership or permissions. Production has no allow-unsafe switch.

```console
fsgg-coord-engine telemetry store init --store-root /durable/private/fsgg-telemetry
fsgg-coord-engine telemetry store status --store-root /durable/private/fsgg-telemetry
```

Use mode `0700` for the root and `0600` for private assignment files. Never place the database on a
network-shared filesystem.

## Schema and relations

Migration 1 creates store metadata/migration receipts, items, features, attempts, parent-child and PR-head
relations, source generations/cursors, usage/delivery/evidence/coverage observations, diagnostics, immutable
batch acceptance, native fact identities/content digests and correction history.

Migration 2 adds `runtime_admissions`, `runtime_starts`, `runtime_turn_usage`, `runtime_terminals`, and
`runtime_gaps`. An invocation joins its admission, process/thread/turn starts, native thread, zero or more completed
turns, and terminal exit. Usage rows carry requested and observed model/effort/backend separately, nullable
native turn identity, invocation-local turn sequence, `completed-turn` accounting scope and
`codex-exec-jsonl` provenance. No response or session count is inferred. Each versioned SQL migration has a
stored checksum and runs under the same writer lock as ordinary mutation.

## Identities, inbox, and drain

Assignments use schema `fsgg.telemetry.codex-assignment/1` with only `featureId`, `itemId`, `attemptId`, optional
`parentAttemptId`, and `producerStream`. Workers publish closed `fsgg.telemetry.ingest/1` batches to
`inbox/<producer>/<batch>.<digest>.ready` using exclusive temporary creation, file flush, atomic rename and
directory sync. A batch is at most 64 KiB/64 observations, each producer has at most 128 pending batches, and a
drain accepts at most 128 batches/8 MiB fairly.

Native fact identity is independent of importer and host. Same identity/content replays; changed content
conflicts unless its explicit revision increases. Acceptance, facts, deduplication and cursor commit together.
Malformed or conflicting ready files move to bounded private quarantine and do not become coverage.

## Runtime adapter

```console
fsgg-coord-engine telemetry runtime codex-exec \
  --assignment /private/attempt.json -- --json --ephemeral -m MODEL "explicit task"
fsgg-coord-engine telemetry runtime status
```

The adapter admits before launch, tees exact child stdout, inherits stdin and working directory, and returns the
child exit status. Its bounded projector retains only thread identity, completed-turn token counters and typed
outcomes. It discards prompts, messages, reasoning, commands, tool I/O, diffs, paths and raw JSON. Publication,
framing and queue loss are gaps, not delivery failures. `collaboration.spawn_agent` is currently unsupported;
this command covers only future explicit launches through it.

## Locking and crash recovery

SQLite uses WAL, `synchronous=FULL`, foreign keys, a bounded busy timeout and short `BEGIN IMMEDIATE`
transactions. Every initializer/migrator/writer also holds stable `writer.lock` through nonblocking Linux
`flock`; readers open read-only WAL snapshots and never initialize or checkpoint. The lock is never unlinked,
TTL/PID-stolen or inherited by a child.

Temporary spool files are ignored. A published ready file replays after process loss. Precommit death rolls
back; postcommit/pre-delete death replays through dedup and then removes the file. A replacement orchestrator
can drain late child publication. Removal failure stays visible and retriable.

## Read-only inspection and privacy

```sql
SELECT item_id, count(*) AS admitted FROM runtime_admissions GROUP BY item_id;
SELECT invocation_id, thread_id, turn_sequence, input_count, cached_input, output_count, total
FROM runtime_turn_usage ORDER BY invocation_id, turn_sequence;
SELECT a.invocation_id FROM runtime_admissions a
WHERE NOT EXISTS (SELECT 1 FROM runtime_terminals t WHERE t.invocation_id = a.invocation_id);
```

Use `telemetry store summary --item ID` or bounded `export --public --output FILE` for allowlisted aggregates.
Unknown population remains unknown; persistence alone is not coverage. Private identifiers, raw content and
paths are never public-export fields. The Python observer does not access SQLite.

Backup, restore and migration CLI workflows are not implemented yet. Until they are qualified, stop writers,
copy the database plus WAL state only with SQLite-supported tooling, and do not call that an application-level
restore guarantee.

The immutable batch is a future Akka message contract. A per-host telemetry-writer actor may later serialize
normal drains and supervision, while SQLite locking and native deduplication remain mandatory for process
recovery, upgrades and old/new overlap.
