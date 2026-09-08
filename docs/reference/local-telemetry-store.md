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

Migration 3 adds `ci_bindings`, `ci_pages`, `ci_runs`, `ci_jobs`, `ci_steps`, and `ci_coverage`. A collection
binds one item/attempt to an explicit repository, PR, 40-hex head and workflow. Pages are distinct recovery
evidence; run identity is repository plus native run ID, while attempt, job and step extend that native key.
The coverage row keeps inventory, attempts, job pages, terminal state, timestamps, lineage, classification and
critical-path evidence independent. Older v1/v2 stores upgrade in place; migration SQL and checksum receipts
are immutable.

Migration 4 adds private `budget_population_facts`, `budget_attribution_facts`, `budget_interval_facts` and
`budget_intervention_facts` projections. `budget_shared_cost_refs` prevents one source cost from entering two
provider/accounting scopes. `budget_dirty_items` bounds reevaluation work; `budget_epochs` and
`budget_epoch_membership` retain an original item's first epoch; `budget_assessment_revisions` preserves each
derived dimension decision; `budget_breaches` counts distinct items; and `budget_interventions` permits one
open-to-verified transition per epoch. Stores at schema versions 1, 2 or 3 upgrade in place under `writer.lock`.

Migration 5 adds `operational_activations`, `expected_dispatches`, `invocation_lineage`, and
`operational_event_times`. These are prospective observation facts: an activation is limited to
`explicit-future-dispatches`, binds one named runtime and activation time, and declares the delay after which an
observation is late. Expected dispatches retain root, child, and follow-up parentage; invocation rows preserve the
observed dispatch/invocation/root identities; event times retain nullable occurrence and observation timestamps
with separate `host-wall`, `provider-native`, or `github-native` clock provenance for each timestamp. Event names
are closed to `admission`, `start`, and `terminal`, with one row per item/invocation/event. Stores at schema
versions 1 through 4 upgrade in place. Migrations 1–4 and their stored checksums are unchanged.

Migration 6 adds immutable `ci_population_admissions`, revisioned `ci_check_runs`, and
`ci_population_coverage`. First admission requires a routine-eligible `ready`/`not-delivered` candidate plus
an explicit matching observed head and native confirmation of repository, PR, base ref, base SHA, and head.
Later observations remain fenced to that stored admission, including after the PR head moves. Actions runs,
all attempts, attempt-specific jobs and native check-runs are reconciled independently; partial pagination,
inventory mutation, external checks and unsupported event bindings stay explicit rather than becoming zero or
complete. Stores at schema versions 1 through 5 upgrade in place without changing earlier migration checksums.

Migration 7 adds `native_item_outcomes`, the durable machine-authored result of the repository-owned routine
delivery readback. Each revision binds item, repository, PR, base ref/SHA, candidate head, delivery outcome,
code-delivery truth, optional merge commit, native occurrence time, observation time and private source reference.
It is queued and drained before provider reconciliation, so the native result survives unavailable CI reads.
Stores at schema versions 1 through 6 upgrade in place without changing earlier migrations.

## Identities, inbox, and drain

Assignments use schema `fsgg.telemetry.codex-assignment/1` with only `featureId`, `itemId`, `attemptId`, optional
`parentAttemptId`, and `producerStream`. Workers publish closed `fsgg.telemetry.ingest/1` batches to
`inbox/<producer>/<batch>.<digest>.ready` using exclusive temporary creation, file flush, atomic rename and
directory sync. A batch is at most 64 KiB/64 observations, each producer has at most 128 pending batches, and a
drain accepts at most 128 batches/8 MiB fairly.

Native fact identity is independent of importer and host. Same identity/content replays; changed content
conflicts unless its explicit revision increases. Acceptance, facts, deduplication and cursor commit together.
Malformed or conflicting ready files move to bounded private quarantine and do not become coverage.

## Prospective dispatch reconciliation

```console
fsgg-coord-engine telemetry store reconcile --item UTEL-06.1 \
  --store-root /durable/private/fsgg-telemetry
```

The read-only reconciliation joins expected dispatches to observed invocation identities and event times. It
reports lineage coverage separately as matched, unknown, invalid, unsupported, or out of scope. Timing coverage
is separately complete, late, missing, invalid, or not evaluated and requires one complete admission, start, and
terminal timing witness. Stable diagnostics distinguish missing parents, conflicting identities, cycles,
unsupported runtimes, missing/duplicate required events, missing timestamps or clocks, reversed event time, and
observations beyond the activation's declared delay. Complete timing also requires the admission → start →
terminal occurrence sequence and observation sequence to be nondecreasing on one common clock domain. Latency is
evaluated only when occurrence and observation share that domain; incomparable clocks are invalid, not
subtracted. Likewise, prospective scope is unknown rather than time-compared when activation and
expected-dispatch clocks differ. Usage and native terminal outcome coverage remain `not-evaluated`. The first
supported runtime is `codex-exec`; unsupported runtimes remain explicit rather than being counted as observed.
Status and reconciliation never drain or mutate the store.

This contract does not enumerate processes, discover historical sessions, read transcript/session storage, infer
an expected population, or activate a host collector. Producers must first publish an explicit prospective
activation and one expected-dispatch fact per invocation they intend to observe. Missing or late evidence remains
diagnostic and cannot change native delivery truth or establish qualification.

## Runtime adapter

```console
fsgg-coord-engine telemetry runtime codex-exec \
  --assignment /private/attempt.json --late-after-seconds 60 -- \
  --json --ephemeral -m MODEL "root task"
# Inside that worker, the inherited context makes this a child by default:
fsgg-coord-engine telemetry runtime codex-exec -- \
  --json --ephemeral -m MODEL "child task"
# Use the same inherited context for a later follow-up:
fsgg-coord-engine telemetry runtime codex-exec --relation follow-up -- \
  --json --ephemeral -m MODEL "follow-up task"
fsgg-coord-engine telemetry runtime status
```

The dotnet tool package contains this launcher entrypoint; no sibling script or receiver manifest is required.
The root caller supplies the closed private assignment once. The launcher then creates the prospective activation,
expected dispatch, invocation lineage, admission/start/terminal observations and host-wall event times. It passes a
closed private invocation context in the child environment containing only work and lineage identities, the selected
store root and activation delay. A descendant calling the same entrypoint consumes that inherited context, creates a
new child or follow-up dispatch and invocation, and passes the new context onward. Grandchildren, repeated child
attempts, and follow-ups therefore need no assignment copy and callers never author ingest batches.

The adapter admits before launch, tees exact child stdout, inherits stdin and working directory, preserves every
Codex argument (including model, effort, sandbox and permission options), and returns the child exit status. A
successful process with no completed usage remains an explicit usage gap; launch failure records a terminal without
a start; conventional cancellation exits are classified as cancelled without changing their code. Projection waits
for delayed stdout frames before terminal publication. Because the context is inherited by value, a surviving child
can launch a descendant after its parent process has exited.

Its bounded projector retains only thread identity, completed-turn token counters and typed outcomes. It discards
prompts, messages, reasoning, commands, tool I/O, diffs, paths and raw JSON. Publication, framing, queue loss, a full
inbox and writer-lock contention are fail-visible telemetry diagnostics, never delivery failures; a bounded
opportunistic drain after the native process exits does not change that exit. `telemetry runtime status` reports the
packaged entrypoint but deliberately reports host activation as `not-installed`. Source merge and package presence do
not claim receiver installation or default activation. `collaboration.spawn_agent` remains explicitly unsupported;
coverage is limited to future repository-owned launches that use this entrypoint.

## Explicit CI collection

```console
fsgg-coord-engine telemetry ci collect --assignment /private/ci-attempt.json \
  --repo FS-GG/.github --pr 1234 --head 0123456789abcdef0123456789abcdef01234567 \
  --workflow coord-engine.yml --store-root /durable/private/fsgg-telemetry
fsgg-coord-engine telemetry store drain --store-root /durable/private/fsgg-telemetry
fsgg-coord-engine telemetry ci summary --item UTEL-04A --store-root /durable/private/fsgg-telemetry
```

The assignment schema is `fsgg.telemetry.ci-assignment/1` and carries only stable feature, item, attempt,
optional parent-attempt and producer-stream IDs. The reader accepts at most 20 single-page GETs, 100 records
per page, 4 MiB per response and 30 seconds total. It never retries, sleeps or follows a redirect, and follows
only validated same-origin/same-repository continuations. Every declared run attempt uses GitHub's
attempt-specific jobs endpoint; the latest-jobs endpoint is not historical evidence.

The source-controlled `.fsgg/telemetry-ci-attribution.json` profile matches exact workflow/job/step identities
and records rationale. Missing or drifted matches are unclassified and classification coverage stays unknown;
`mixed` is not redistributed. Runner seconds sum valid job intervals, while wall seconds union them. Queue,
avoidable-rerun and critical-path values require their own native witnesses; absent, reversed, pending or
skipped timestamps never become zero. Monetary cost is not collected.

Repository-owned routine delivery can invoke the same bounded observer advisory-only:

```console
python3 tools/routine-delivery.py --repo FS-GG/.github --pr 1234 --head 0123456789abcdef0123456789abcdef01234567 \
  --telemetry-engine fsgg-coord-engine --telemetry-assignment /private/ci-attempt.json \
  --telemetry-store-root /durable/private/fsgg-telemetry
```

The driver passes its exact generated public delivery JSON to `telemetry ci reconcile` before an eligible merge
and again after the final native readback; assignment and store locations remain private command context. Each
readback has a fresh observation timestamp ordered after its native outcome timestamp. The observer drains its
bounded inbox before checking an existing admission and after publishing a revision, closing the pre/post process
race without waiting indefinitely. `telemetryHealth` in the existing delivery summary is `complete`, `open`,
`pending`, `missing-outcome`, or `unavailable`. Observer failure is diagnostic and cannot change delivery outcome.
This is source capability only until a coherent package is published, installed and activated.

## Whole-item budget assessment

```console
fsgg-coord-engine telemetry budget summary --item UTEL-05A --store-root /durable/private/fsgg-telemetry
fsgg-coord-engine telemetry budget status --store-root /durable/private/fsgg-telemetry
```

For an item with a migration-7 outcome, the writer transaction derives budget population, attribution and interval
projections from native runtime/lineage, CI and delivery facts. Callers do not supply Budget facts or assessment
verdicts on this route. A delivered outcome completes only after a prospective activation/root expectation exists
and every expected supported dispatch has exactly one terminal invocation. A native refusal may complete the
zero-dispatch population. A merge with a live child remains open; a later expected follow-up rewrites the one stable
derived population identity to open, then its terminal/usage produces a new assessment revision. The reducer reads
exactly the latest canonical population projection. Migration-4 caller facts remain readable for old stores but
cannot override machine-derived projections for a migration-7 item.

Runtime usage supplies known provider denominators, but absent activity classification leaves the administrative
numerator and verdict unknown. Human effort and priced cost remain unknown. CI step timestamps produce private
administrative/useful/productive interval projections, but absent native critical-path binding leaves them
unwitnessed and the delay verdict unknown. CI administrative runner share is reported as diagnostic
`not-applicable`; it cannot enter the section 7.4 breach trigger. Missing facts never become zero or a passing
verdict. Intervention facts remain evidence of deployment or later verification, never caller-authored reset
authority.

The reducer assesses dimensions independently. It computes the 10% ceiling as `10 × numerator > denominator`
and the severe threshold as `4 × numerator > denominator` with overflow-safe integers and no rounding. Zero
over zero is not applicable. Missing denominators, partial CI pages, runtime gaps, mixed attribution, incomplete
lineage and unwitnessed critical-path intervals are unknown, so they neither pass nor breach. Known partial
measurements remain visible beside an unknown verdict. Administrative
intervals are unioned before witnessed useful/productive overlap is removed; useful tests do not become
bureaucracy merely because they are slow or repeated.

Each completed original item remains in its first epoch. Fifteen distinct nonsevere breaching items, or any
item above 25%, opens the epoch's sole intervention. Corrections may revise the item's assessment and breach,
but retries, dimensions and attempts cannot multiply the distinct count. An epoch advances atomically only
when complete evidence proves deployment occurred before a successful verification; all other combinations
leave the intervention open. Evaluation is capped at 32 dirty items per transaction and 4,096 references per
item, with remaining dirty work visible to a later drain.

Representative read-only inspection:

```sql
SELECT item_id, dimension, provider, accounting_scope, verdict, numerator, denominator, severe, reason
FROM budget_assessment_revisions a
WHERE assessment_revision = (
  SELECT max(assessment_revision) FROM budget_assessment_revisions b
  WHERE b.item_id=a.item_id AND b.dimension=a.dimension
    AND b.provider=a.provider AND b.accounting_scope=a.accounting_scope
)
ORDER BY item_id, dimension, provider, accounting_scope;
SELECT epoch_id, count(DISTINCT item_id) AS distinct_breaches
FROM budget_breaches GROUP BY epoch_id ORDER BY epoch_id;
SELECT epoch_id, state, trigger_kind FROM budget_interventions ORDER BY epoch_id;
```

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
SELECT repository, run_id, attempt, status, conclusion FROM ci_runs ORDER BY repository, run_id, attempt;
SELECT run_id, attempt, job_id, started_at, completed_at FROM ci_jobs
WHERE item_id = 'UTEL-04A' ORDER BY run_id, attempt, job_id;
SELECT inventory, attempts, job_pages, terminal, timestamps, lineage, classification, critical_path
FROM ci_coverage WHERE item_id = 'UTEL-04A' ORDER BY rowid DESC LIMIT 1;
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
