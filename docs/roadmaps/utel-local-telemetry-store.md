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

## UTEL-04A — One explicit workflow/head CI population

- [x] Add `telemetry ci collect --assignment <private-file> --repo <owner/repo> --pr <n> --head
  <40hex> --workflow <id-or-file> --store-root <root>` and read-only `telemetry ci summary --item <id>`.
  Collection is explicit and limited to that repository, PR, head and workflow; moved heads remain unresolved
  and merge-group heads are unsupported.
- [x] Use an additive single-page GitHub transport capped at 4 MiB per response, 100 rows per page, 20 GETs
  and 30 seconds overall, with no retry or sleep. Redirects, cross-origin/repository continuations, loops,
  malformed counts and inventories over 1,000 fail closed. A failure after an accepted page is retained as
  partial, resumable coverage rather than reported complete.
- [x] Collect native workflow runs and every declared run attempt through attempt-specific job endpoints,
  including embedded steps and source timestamps. Empty native `pull_requests` is allowed only after the
  independently supplied PR/head binding succeeds.
- [x] Add checksummed migration 3 for private collection bindings/pages, run attempts, jobs, steps and eight
  independent coverage dimensions. Facts retain repository-scoped native identity and flow through the same
  immutable inbox, single writer, transaction, cursor and replay/conflict boundary as earlier observations.
- [x] Reduce runner duration as valid job-interval sum and wall duration as their union. Invalid, missing,
  pending or skipped endpoints remain unknown rather than zero. Exact workflow/job/step attribution rules
  classify useful validation, administration, necessary setup, mixed or unclassified work; unmatched profile
  identities remain unknown. Money and avoidable rerun remain unknown without explicit native evidence, and
  critical path remains unknown without supported causal witnesses.

This window selects no population automatically, adds no workflow or service, and changes no Coordination or
§7.4 counters. It does not collect billing, scan historical sessions, backfill, upload private data or inspect
the retained host database. Publication, generated-workspace defaults and receiver adoption remain pending.

## UTEL-05A — Derived whole-item budget assessment

- [x] Add a pure `TelemetryBudget` domain for exact, overflow-safe rational thresholds. More than 10% is a
  breach, more than 25% is severe, and 15 distinct breaching original items opens one intervention; equality
  at either boundary is non-triggering.
- [x] Accept only source-referenced population, attribution, interval and intervention-evidence facts through
  the existing immutable inbox. Callers cannot submit pass/breach/reset decisions. Native original-item
  completion gates assessment, while incomplete population, partial source coverage, unresolved attribution,
  runtime gaps and unwitnessed intervals remain unknown. Known zero activity is not applicable.
  `budget-population` is the canonical whole-item completion and epoch-membership fact keyed by
  `originalItemId`; there is no redundant fifth whole-item fact kind.
- [x] Add checksummed migration 4 for normalized fact projections, shared-cost references, dirty items,
  immutable assessment revisions, sticky epoch membership, dimension-level breaches and one intervention per
  epoch. Provider/accounting scopes remain separate and unique cost references prevent overlapping legacy and
  runtime totals from being counted twice.
- [x] Reevaluate at most 32 dirty items and 4,096 cost references per item under the existing writer lock and
  short transaction. Corrections can remove a mistaken breach, but replay, retries, extra dimensions and new
  attempts do not create another distinct item. Further breaches fold into an open intervention.
- [x] Union administrative intervals before subtracting witnessed useful/productive overlap at nanosecond
  precision. Useful validation, including expensive or duplicated tests, is excluded; CI-only administrative
  diagnostics cannot independently trigger an intervention.
- [x] Advance exactly one successor epoch only from complete evidence that deployment preceded a verified
  improvement. Deployment alone, verification-before-deployment, failed improvement and unknown coverage do
  not reset. Old-epoch items remain bound to their original epoch.
- [x] Provide read-only `telemetry budget summary --item` and `telemetry budget status` JSON. They expose no
  source references, assignment IDs, local paths or content and never drain, reset or mutate the store.

This window adds no authoritative mutation command, automatic population selection, workflow, service,
Coordination counter, private backfill or package publication. Generated workspaces and defaults remain
unchanged; source publication and adoption remain pending.

## UTEL-08A — Post-completion process observation

- [x] Add checksummed migration 8 for immutable, revisioned attempt/item process reviews, typed activity spans,
  exact native-usage attribution, and repair/complication events. All observations retain original item,
  attempt, invocation, and activity identities where applicable.
- [x] Gate attempt reviews on a terminal admitted invocation and item reviews on complete, exactly joined
  expected population. Unknown or missing evidence stays explicit; reviews never become delivery authority.
- [x] Bound review prose, arrays, evidence digests, durations, files, row counts, and output size. Retain reviews
  privately by default and exclude their prose and private identities from public export.
- [x] Support overlapping and open planning, implementation, review, validation, delivery, repair, operations,
  other, and unclassified activity spans. Revisions close or correct observations without destructive updates.
- [x] Link activity accounting only to exact native usage records. Direct, mixed, and unclassified attribution
  are explicit, each native usage record is counted once, and elapsed time never allocates tokens.
- [x] Add closed roadmap-helper hooks plus bounded private `review summary` and versioned `item-detail` reads.
  The store owns latest-revision joins and accounting; dashboards remain presentation-only consumers.

The orchestrating agent authors the review after terminal state; this adds no LLM service, daemon, transcript
collection, or native-tool interception. SQLite remains the host-local WAL store with atomic inbox publication
and one drainer. Source delivery and future host activation are separate operational steps.

## Equal feature and item identities

The roadmap adapter permits a root whose feature and item IDs are equal. Native fact
identities are global across kinds, so that root emits its item fact with the stable
`roadmap-item-<digest>` identity while retaining the literal item ID in `itemId` and
feature ID in `featureId`. The feature fact and every distinct-ID root retain their
existing identities. Population-only emission uses the same rule. This internal
repair changes no public signature, schema, dispatch token, usage accounting, or
logical item reference. Adapter regressions cover equal IDs, unchanged distinct
IDs, and retry without a second publication. The receiver fixture rejects duplicate
fact identities across kinds and the population-only case uses a declared protected
original-item mapping. Locked restore, Release build, and the complete adapter
harness pass; fixture usage counters establish adapter behavior only, not actual
programme token coverage.

A rejected pending publication may be repaired only after retaining its original
bytes and proving its ingest ID, generation cursor, and runtime admission are absent.
Change only the colliding item fact identity, preserving the existing dispatch,
activation, invocation, token, timestamps and batch identifiers. Retry the durable
pending intent; historical unobserved child dispatches and usage remain gaps.

The retained programme root was recovered under its original token and applied with four new facts
and one feature replay. A prospective child completed and produced a native usage observation after
its verified short agent name was bound correctly. The observation guide now distinguishes that name
from the thread UUID. This establishes the selected recovery and collection path; historical root
usage, whole-programme coverage and deployed emitter adoption remain separate and unresolved.

## Bounded native dispatch state repair

The private dispatch writer could retain full native metadata above the reader's 256 KiB limit,
causing strict inventory matching to reject later dispatches. Reader and writer now share a 1 MiB
state-file bound, including the persisted newline. A transition exceeding it refuses before replacing
the original state or publishing its intent. The reader remains bounded and rejects malformed JSON,
symlinks and unsafe permissions; inventory matching still validates every token-named state.
Synthetic adapter regressions cover metadata above the former limit, preserved identity and follow-up
lineage, unknown usage baselines, exact-bound writer refusal and strict invalid-inventory handling.
This is a source-only repair: retained private records and published binaries remain unchanged;
qualification, publication and installed adoption are separate, and prior observation gaps remain unknown.

### Operational observation validation preparation — 2026-10-07

The prepared coherent0.99.1 source uses canonical full-batch parsing before new durable
intent and a closed byte/destination/engine-bound local parser rejection for legacy intent.
This adds no Store migration, persistent schema or counter change. Unknown receiver outcomes
retain pending custody. See the [operational plan](utel-operational-completeness.md#2026-10-07-operational-recovery-source-window)
and [reference recipe](../reference/local-telemetry-store.md#operational-collection-and-malformed-observation-recovery).
Compiled qualification, publication, installed selection and retained-root recovery remain
separately admitted; original rejected input/state and unavailable observation baseline persist.


## Current input-version selection — 2026-10-08

Historical completed migration milestones remain evidence of their original delivered windows. Current source accepts fresh0/current14 normal init and authentic14 normal restore. Only explicit exact13 migration and authentic13 backup import remain historical transition entry points; no1..12 automatic upgrade or9/12 import is supported. Exact earlier SQL/digests, facts/cursors/files, correction lineage and unknown acceptance survive the transition. All six preservation/rollback/integrity/framing obligations remain required through the explicit APIs. Source preparation is unqualified for installed execution; see the [operator matrix and backup scope](../reference/local-telemetry-store.md#schema-and-relations).
