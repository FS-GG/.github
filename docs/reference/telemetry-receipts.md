# Scoped telemetry receipts

R1 introduces transport envelope `fsgg.telemetry.envelope/1` and SQLite migration 9. It preserves migrations 1–8 and the closed `fsgg.telemetry.ingest/1` observation contract. Source delivery does not activate a receiver or publish a package.

The implementation is `TelemetryReceipt` in Core and the enrollment, submit, lookup and drain functions in `TelemetryStoreApplication`. Local tooling and the HTTP receiver must call this same boundary. An authenticated adapter supplies `Scope`; the envelope cannot authorize itself. Producer credentials are deliberately absent from the storage contract, so rotation never changes receipt identity.

## Workspace and identity

Each enrolled store has one immutable workspace association. A host serving several workspaces must select separate explicitly configured stores, then enforce its total admission limits across them. Caller-controlled workspace IDs never become paths. Enrollment rejects a store with legacy applied or pending batches; select a new prospective store while preserving the legacy store unassigned. An explicit historical import remains a separate operation. Re-enrolling the same producer/stream is idempotent. Another workspace cannot replace the association.

The envelope contains exactly `schema`, `workspaceId`, `producerId`, `streamId`, `batchId` and `payload`. All four identifiers match `[A-Za-z0-9][A-Za-z0-9._-]{0,127}`. The payload retains its original schema and native fact identities/revisions. The engine uses a derived producer/stream/source cursor namespace and a derived transport ingest ID, binding the full envelope digest. Two producers can therefore reuse a batch ID while native fact replay remains independent.

Digest v1 is SHA-256 over UTF-8 canonical JSON produced by the existing `CanonicalJson` writer: ordinal property order, preserved array order, compact output and its JSON string escaping. Strings preserve Unicode scalars without normalization. Reject duplicate members at every depth, invalid UTF-8, unknown properties, unsupported schema versions, and noncanonical integer spellings (including `-0`, decimal and exponent forms). Both the incoming and canonical envelope are bounded to 72 KiB; the observation payload retains its 64 KiB/64-fact limit. The immutable cross-adapter ASCII vector is in `TelemetryReceiptTests`, with digest `c7c5813d3c35ae17b6f955d43a4bfb238699c13f26467a2deb3d05eb50c198bc`.

## Acceptance and recovery

Receipt identity is `(logical producer, batch ID)`. Its immutable digest includes workspace and stream. The existing writer lock serializes admission, recovery and application; request concurrency/timeouts belong to the hosting adapter. A retry checks prior identity before reserving capacity. Changed content returns `identity-conflict`, including after detail expiry.

Admission creates a private exclusive temporary file, flushes it, renames it to a server-derived SHA-256 filename and syncs the directory. Only then may the recoverable SQLite index record `durably-received`. Recovery validates artifacts and bindings before rebuilding missing index rows. It syncs the inbox before acknowledging a recovered obligation and removes abandoned temporary files under the writer lock. Missing or corrupt accepted input refuses further admission/drain; it never becomes an empty-store fallback.

Fact application, native acceptance/cursor updates and the receipt's `applied` outcome commit in the same transaction. A semantic conflict rolls back the facts and commits a bounded `rejected` result. Only terminal receipts permit removal of the recoverable envelope. Reopening after commit-before-cleanup removes the leftover input without reapplying old revisions. Lookup is read-only; a lost response is retried with the same identity/content. An unavailable lookup is not proof that the batch was never accepted.

Terminal detail becomes `expired` after 30 days. Immutable identity/digest metadata remains for lifetime conflict detection. Rejected transport payloads are removed after their terminal result is durable; this path retains no raw quarantine payload. The legacy quarantine retains its existing independent behavior. Neither a durable receipt nor an applied transport batch establishes coverage or native delivery truth.

Limits per enrolled store: 128 producers, 1,024 producer/stream associations, 128 pending batches/8 MiB per producer, 1,024 pending batches/64 MiB total, and 1,000,000 lifetime receipt identities. Admission refuses at capacity. A pass drains at most 128 batches/8 MiB with producer interleaving and a durable cursor. A host must add its global workspace, disk, concurrency and timeout limits before accepting remote requests. Disk failures preserve accepted obligations and return `storage-unavailable`; no automatic eviction weakens identity.

Stable adapter errors include `invalid-request`, `unsupported-version`, `oversized-batch`, `unauthorized-scope`, `identity-conflict`, `overload`, `receipt-unavailable` and `storage-unavailable`. A receipt includes its schema, scope, batch ID, verified digest, status and optional bounded rejection code. HTTP status alone is never a receipt.

## Qualification

Run the focused storage and receipt suite:

```sh
dotnet test tests/FS.GG.Coord.Cli.Tests/FS.GG.Coord.Cli.Tests.fsproj \
  --filter 'FullyQualifiedName~TelemetryReceiptTests|FullyQualifiedName~TelemetryStoreApplicationTests'
```

Synthetic fixtures cover legacy migration/checksum refusal, duplicate/conflict/expiry, unauthorized scope, independent producer keys, producer exhaustion and fair drain, corrupt/missing accepted input, and failure hooks around file sync, rename, index commit, application commit and cleanup. Eight subprocess cases exit immediately at those boundaries and reopen the store in a different process. These prove process-exit recovery on the tested Linux filesystem; they do not establish storage-specific power-loss guarantees. Remote credential/TLS, cross-adapter HTTP conformance, package installation, measured production budgets and Main deployment remain later roadmap obligations.
