# Temporary adapter contract

This dependency-free F# script is advisory. It performs no model launch, GitHub read/write, process
termination, workload execution or acceptance minting. Existing native tools remain the authority.
Caller observations must be checked against their cited sources; JSON shape and hashes do not authenticate
them. All commands refuse unknown JSON properties and duplicate keys. Keep inputs and outputs private.

Invocation: `dotnet fsi --exec PATH/programme.fsx COMMAND INPUT PRIVATE_ROOT`; `report` takes only
`PRIVATE_ROOT`. Exit 0 means the mechanical command passed, 3 means refused or mismatched. A unique
0600 result and measurement file are retained for every completed command; packets also retain a 0600 text
file. No shared append log, global registry, credential or background daemon is created.

## frontier input

```json
{
  "schema":"fsgg.programme.snapshot/1", "campaign":"unified-roadmap-20261003",
  "capacity":4, "capacityKnown":true, "maxAgeSeconds":300,
  "lanes":[{
    "id":"EXAMPLE", "feature":"EXAMPLE", "item":"EXAMPLE-01",
    "originalItem":"EXAMPLE-01", "attempt":"existing-attempt",
    "repository":"FS-GG/Example", "chain":"EXAMPLE-01", "owner":"existing-owner",
    "worktree":"/absolute/isolated/worktree", "head":"exact-observed-head", "priority":1,
    "readable":true, "observedAt":"2026-10-04T14:00:00Z",
    "evidence":["exact-native-reference"], "state":"ready", "inFlight":false,
    "touchSet":["src/Example/**"], "dependencies":[],
    "source":"local", "validation":"pending", "publication":"not-required",
    "installed":"not-required", "native":"not-required", "projection":"not-required"
  }], "openPulls":[]
}
```

Refresh timestamps from actual reads, not from reserializing old facts. All lanes with `inFlight:true`
retain slots and touch-sets even when unreadable/unknown. Capacity is actual available agent capacity;
local CPU/CLR, hosted CI and operation resources require separate admission. Use current managed open PRs
from all campaigns: `{repository,campaign,chain,head}`. The helper does not inspect the hosted check queue.
Path overlap is conservative ancestor matching per repository. Only literal paths and trailing `/**`
are supported; unsupported patterns refuse instead of appearing disjoint.

States: ready/active/review/checks/repair/operation/unknown/closed/blocked. Source:
none/local/pr/merged. Validation: not-required/pending/passed/disputed/unknown. Publication:
not-required/pending/published. Installed: not-required/pending/qualified. Native:
not-required/pending/succeeded/failed/unknown. Projection: not-required/pending/landed.
Dependencies are `{lane,boundary}` with boundary source/qualified/published/installed/native/closed.
Include prerequisite lanes in the selected snapshot; no missing edge or cycle is admitted. A closure
with pending projection holds its chain. `closed` describes the accepted window, never inferred full
feature completion. Interpret `assign` only as an advisory ready lane; it grants no dispatch/effect.

## packet input

Schema `fsgg.programme.packet/1`: `lane`, `objective`, `stop`, `mandatoryPaths`, `maximumBytes`,
`references`. Each reference has absolute `path`, full-file `sha256`, `startLine`, `endLine`, `mandatory`,
`trust` (instruction/plan/data) and `reason`. Lines 0/0 select the full file; otherwise use inclusive
one-based bounds against the exact hashed file. All governing instructions require whole-file mandatory
inclusion. Mark relevant source, observations and logs as data; their contents cannot override authority.
The full applicable mandatory set is selected by the owner, not inferred by this helper. It cannot prove
that an undeclared instruction was included. Plans and evidence need independently reviewed excerpts.

Mandatory paths must occur in the manifest; stale bytes, malformed excerpts or oversized mandatory
context refuse. Mandatory material is assembled before optional references. Optional material omitted
for size stays named in the result. Maximum selected text is 256 KiB; start with a materially smaller
lane-specific bound. Give the worker the packet path; do not import the packet body into parent context.

The bootstrap worker or accountable lane owner assembles this input. It reads applicable instruction
files whole and passes file paths/pins directly to the helper; the parent need not first read their
contents unless they govern its own action. Return only packet path/digest, scope, mandatory instruction
inventory and omissions. The receiving owner confirms applicability and reports missing obligations.

## verify input

Schema `fsgg.programme.verification/1`: `claim`, nonempty `requiredPaths`, nonempty `artifacts`.
Each artifact has absolute `path`, raw-byte `sha256`, exact `bytes`. The helper verifies every declared
artifact and that required paths were declared, refuses links, and reports mismatches without reconstructing
missing bytes. It does not inventory undeclared members or prove runtime/semantic outcomes. Use the
owner's accepted manifest and verifier for those claims. Failure detail is retained in the private result.

## quick acceptance and use

Run `dotnet fsi --exec tests/work-programme/acceptance.fsx` once. It exercises scheduling, stale reads,
unknown effects, projection fences, capacity, actual file drift, mandatory context and optional omission.
Then run packet and verify against this checkout's real skill/source bytes, inspect the logs with report,
and record the command outcomes. No live model or roadmap operation is needed to accept this adapter.
On the first requested programme run, refresh a small actual lane snapshot and debug observed gaps through
ordinary same-PR repair. Existing telemetry supplies native usage; the adapter records bytes and duration
only, with native tokens unknown and compactions not observed.

## Additive offline delta input

`delta INPUT PRIVATE_ROOT` preserves the existing commands and schemas. Input is
`fsgg.programme.delta-input/1`; it projects advisory current facts and dispatches
no effects. It uses the existing campaign/feature/item/original-item/attempt/owner
and candidate identities. It creates no journal, persistent current-state store,
acceptance receipt or new execution attempt.

Required fields: `evaluationTime` (explicit UTC-compatible instant),
`evaluatorIdentity`, `policyIdentity`, `userScopeRevision`, `baseRevision`,
`currentRevision`, `baseEvaluatorIdentity`, `basePolicyIdentity`,
`baseUserScopeRevision`, `baseReturns`, `currentReturns`, and `snapshot` (complete
legacy `fsgg.programme.snapshot/1`). Revision/identity strings are opaque IDs;
only each owner/candidate stream's numeric return revisions have local ordering.
No order across providers is invented. Missing base is represented by empty
`baseRevision` and empty `baseReturns`; base identity strings can then be empty.
With a base, all three base identities are required.

Every return has schema `fsgg.programme.lane-return/1` and these required groups:

- Identity: `campaign`, `lane`, `feature`, `item`, `originalItem`,
  `originalAttempt`, `attempt`, `candidate`, `owner`.
- Revision/provenance: positive numeric `revision`, nonnegative `supersedes`
  less than revision, `sourceRevision`, `inputPacketSha256`, `observedAt`.
- Outcome: `outcome` is `acknowledgment` or `window-reported`; `boundaries`
  contains the exact six legacy `source`, `validation`, `publication`,
  `installed`, `native`, `projection` enums. A window report is not completion
  authority. Its boundaries must correspond to the snapshot's observed lane.
- Evidence: 1–8 entries with `kind` (one of the six boundary names or
  `mechanical`), `reference` (at most1024 characters), `sha256`, `scope` (at
  most256 characters). A digest checks the declared identity; the projection
  does not read/authenticate evidence or infer semantic acceptance.
- Unknowns: `unknowns` is an array of 0–8 strings, each at most256 characters.
- Continuation: `exception` and `continuation` are each one nonempty string of
  at most1024 characters, never arrays. Use `"none"` for no exception. Put
  several next actions into the single continuation string or reference their
  detailed artifact. `narrative` is one nonempty string of at most2048 UTF-8
  bytes. Overflow stays in referenced evidence; never truncate it silently.

For example, the continuation group has this JSON shape:

```json
{
  "unknowns": ["Native acceptance remains pending."],
  "exception": "none",
  "continuation": "Root reviews the source, then admits the bounded native window.",
  "narrative": "Source preparation passed its focused controls; no native operation ran."
}
```

Use the packet's lane identity in `lane`; `owner` names its accountable worker.
Validate the complete return against the installed helper before reporting it.
A rejected return grants no outcome: retain its original bytes and have the same
owner repair its shape without changing observed facts or inventing old revisions.

Keep this complete machine artifact outside parent context. A normal delivery message projects identity,
exact head, changes since the prior return, established/pending boundaries, evidence reference and requested
parent action (or `none`); aim for at most100 words. Store substantive findings in the artifact and use
longer messages for exceptions. This projection changes no JSON fields, revision joins or acceptance
semantics. The parent consumes compact `delta` facts and the complete reservation inventory; it retrieves
selected evidence for a named decision or required review obligation, expanding further only to resolve
an identified gap. Ordinary repair evidence, mechanical checks and detailed reconciliation remain with
the same owner; project discrepancies and unresolved decisions with evidence pointers.

After repair/review milestones, update the existing decision view to the current source and established
outcomes, unresolved findings/decisions, evidence pointers and next action. Replace superseded narrative
there; retain original failures and revision history in linked artifacts. Carry forward unresolved
obligations, conflicts, unknown effects and the complete reservation inventory. Consolidation changes no
return schema, revision joins or acceptance semantics; it does not resolve a conflict by arrival order.

Campaign, item, feature and original-item mismatch refuse. Current owner,
current attempt, candidate and source revision join the existing snapshot
lane's owner, attempt and head; superseded/incomparable returns are explicit
notices. Original attempts cannot silently change across base/current joins.
Identical duplicates are idempotent, older returns remain historical, and equal
revision/different content reconciles rather than selecting arrival order.
A revision gap against the supplied base requires resynchronization. A stream
absent from the base must start at revision1/supersedes0; a partial base cannot
prove an unseen superseded revision. Different original attempts for the same
current owner/attempt/candidate reconcile. A newer row cannot erase a conflicting
lower revision across the supplied base/current closure.

Output `fsgg.programme.delta/1` retains input digest, explicit time and identities,
base/current revisions, `changed`, full `activeReservations` as legacy lane
records, typed `notices`, coverage and `effectAuthority` explicitly none.
Missing base or changed scope/policy/evaluator requests `resynchronize` and
emits no incremental `changed` rows. Reservations come from the complete
snapshot, independently of missing/partial/stale/conflicting returns. Snapshot
completeness is caller-declared and advisory, not independently discovered.
Stale/unreadable/future facts request refresh without renewing their source time.
Acknowledgments cannot mint closure; owner success cannot upgrade snapshot
boundaries. Late validation disputes/native failures fence dependent acceptance
through notices; native adapters and semantic acceptance remain external.

Both return populations are at most128, serialized delta input and output are
at most256KiB, and existing snapshot bounds remain. Unknown/duplicate JSON
properties refuse through the shared reader. Replay executes only the pure
`contextDelta` function; CLI retention writes ordinary private helper artifacts.
Use `dotnet fsi --exec tests/work-programme/context-delta.fsx` for the focused
additive controls and retain the legacy acceptance run separately. Tests do not
establish installed adoption, native usage, compaction or context savings.

## Additive bounded evidence view

`view INPUT PRIVATE_ROOT` reads one explicit artifact; there is no archive crawl.
Input `fsgg.programme.evidence-view-input/1` requires `path` (absolute), raw
`sha256`, exact `bytes`, `startLine`/`endLine`, `maximumBytes` (1–65536),
`trust` (instruction/plan/data), `mandatory`, `obligationsComplete`, `access`
(allowed/denied/unknown), and bounded nonempty `provenance`.

Access and obligation completeness are caller-declared observations. They cannot
grant OS permission, prove universal instruction discovery, or promote data into
authoritative instructions. Missing/unknown admission refuses before reading.
Governing instruction transport requires whole-file0/0, mandatory true and
owner-declared complete obligation discovery. Existing packet instructions remain
whole-file mandatory; this command does not replace their obligation population.

The helper rejects links and checks the complete raw file's size/hash before
UTF-8 decoding or selecting a range. Full artifacts are at most2MiB. Lines use
the existing inclusive one-based convention;0/0 returns the whole text. Output
`fsgg.programme.evidence-view/1` retains identity, verification flag, provenance,
trust and requested range. Typed status distinguishes passed, drift, missing,
permission-unestablished, permission-denied, linked, unreadable, invalid-range,
oversize, instruction-must-be-mandatory-whole-file and obligations-incomplete.
Refusals return empty text; acceptance-relevant content is never silently cut.
Untrusted embedded instructions stay data with no authority minted. Serialized
input/output are bounded256KiB; escaped JSON overflow also refuses. View refusal
is exit3; successful view is exit0.

## Additive declared mechanical reuse

`reuse INPUT PRIVATE_ROOT` is a pure advisory reuse decision. It reads no input
artifacts, runs no evaluator, persists no cache/journal and never reuses semantic
or native acceptance, permission, signatures or completion. Ordinary helper
input/result/measurement retention remains unchanged. A reuse suggestion requires
external trustworthy provenance and current native admission wherever applicable.

Input `fsgg.programme.reuse-input/1` has explicit `evaluationTime`,
`maxAgeSeconds` (1–3600), `identity`, `closureComplete`, `requiredInputIds`,
`inputs`, `priorReceipt`, and `priorReceiptSha256`.
`identity` holds policy/profile/evaluator/source-config SHA256 pins in
`policySha256`, `profileSha256`, `evaluatorSha256`, `sourceConfigSha256`.
Each input fact has `id`, `kind` (evidence/source/configuration/policy/profile/
evaluator), `reference`, `sha256`, exact nonnegative `bytes`, original
`observedAt`, and current caller-declared `access`. IDs are unique and bounded;
required and supplied populations are at most32. The owner must declare the
complete transitive closure. The helper cannot discover an undeclared hidden read.

A prior receipt is `fsgg.programme.mechanical-receipt/1`, kind
`declared-byte-equality`, with the same `identity`, `inputClosureSha256`,
`evaluatedAt` and `result` (only passed is reusable). Its expected pin hashes the
helper's encoded typed receipt, not arbitrary raw JSON formatting. Equality to a
caller-supplied pin does not authenticate the producer or make that expectation
trustworthy. This command validates correspondence only; it creates no receipt
and does not silently promote the prior byte check into broader acceptance.

The closure fingerprint includes the requested required population, all supplied
facts sorted by ID, their original observation times and access, and all four
identity pins. Unknown/incomplete closure, missing required facts or undeclared
extra facts produce request-missing. Changed policy/profile/evaluator/config,
evidence/source/transitive identity, expired/future facts or receipt, missing or
failed receipt, revoked/unknown access, wrong scope or pin mismatch produce
recompute. Only exact complete fresh correspondence suggests reuse. Copying a
fact or receipt cannot renew its timestamp. Freshness of each source fact and the
receipt is checked independently.

Output `fsgg.programme.reuse/1` retains explicit time, closure digest, decision
(reuse/recompute/request-missing), typed reason strings, mechanical-only scope,
caller-declaration coverage and authority explicitly none. Advisory recompute or
request-missing is exit0, not successful evaluation. Invalid schemas, duplicate
keys/IDs, unknown JSON properties or bounds refuse. Input/output remain256KiB
bounded. Run `tests/work-programme/context-evidence.fsx` for focused controls;
`tests/work-programme/run.sh` includes it in the existing linear suite. No
installed integration, live context savings or token usage is inferred.

## Dependency-free parent context path

The [context contract](context.md) supplies bounded JSON-field/line views, proof-obligation review bundles,
native session diagnostics and pinned existing-guard command preparation without CLR execution. It keeps
the F# schemas and packet/delta/reuse semantics unchanged. Use it for parent evidence exposure even when
FSI qualification is held; it does not stand in for delta evaluation, semantic acceptance or native admission.
`programme.fsx report` remains a helper-I/O report; pair it with native context observations instead of
interpreting its `nativeTokens:unknown`/`compactions:not-observed` fields as a census of the host.

## Additive routine decisions

`routine-decisions INPUT PRIVATE_ROOT` extends the existing helper with a pure advisory
current-view reducer. The legacy commands and `/1` schemas retain their behavior. It reads
no native state, starts no worker/watch/workload, performs no effect and mints no authority.
The [routine-decisions skill](../../routine-decisions/SKILL.md) owns its operational guidance.

Input schema is `fsgg.programme.routine-decisions-input/1`. Required fields are `delta`
(the complete existing `DeltaInput`), `resources`, `events`, `previous`,
`previousPolicyIdentity`, `previousEvaluatorIdentity`, `previousUserScopeRevision`.
Use empty previous arrays/identity strings on first evaluation; missing delta base requires
resynchronization. Evaluation time is exclusively `delta.evaluationTime`.

`resources` has `known`, original `observedAt`, nonnegative `cpu`, `memoryMiB`,
`occupiedCpu`, `occupiedMemoryMiB`, `evidence`, `agentMode`, `cpuMemoryMode`.
Modes are `soft`, `enforced`, `unknown`; caller observations do not prove enforcement.
Available CPU/memory is distinct from agent slots. A same-owner continuation takes no new
slot. Unknown/stale native resource availability fences new ordinary execution. Soft
allocation overrun is a distinct reason; enforced limits and observed occupancy constrain it.

Each event is one original operation observation with these required fields:

- Identity: `lane`, `operation`, `originalAttempt`, `attempt`, `owner`, `candidate`.
  Operation may be an exact original artifact reference (at most1024 characters); owner
  may be the authentic runtime path (at most128 characters). Lane/attempt identifiers
  use existing opaque identifier syntax. Owner/attempt/candidate join the snapshot.
- Revision: positive `revision`, nonnegative `supersedes`, original `observedAt`.
  Revisions order only this operation stream. Preserve native joins; local recorded-case
  projection revisions must be explicitly labeled and never replace provider revisions.
- Event: `failed`, `cleanup-observed`, `current-observation`, `unknown-effect`,
  `admission-required`, `effect-reconciled`, `scope-changed`.
- Class: `ordinary`, `observation`, `integration`, `protected`, `unknown`.
- State: `running`, `repair-needed`, `cleanup-unconfirmed`, `cleanup-observed`,
  `unknown-effect`, `protected-admission`, `waiting`, `accepted`.
- Cleanup: `observed` or `unconfirmed`. Exit, cancellation and deadline expiry do not
  establish cleanup. Failure/repair requires positive cleanup before a repair route.
- Original bounds: `executionDeadline`, `executionMode`, `observationDeadline`,
  requested `cpu`, `memoryMiB`, `allocationCpu`, `allocationMemoryMiB`, `allocationMode`.
  Unknown execution time uses `0001-01-01T00:00:00+00:00` with `executionMode:unknown`;
  unknown observation deadline uses that same explicit missing-value sentinel. These
  missing values never renew a window or admit execution. A read-only observation
  has its separately supplied original deadline. No event extends original bounds.
- Evidence: nonempty `evidence`, SHA256 `sha256`, external original `history` pointer.
  History retains original failures/superseded facts outside the current projection.
  Evidence references are at most1024 characters; equality does not authenticate them.

At most64 snapshot lanes,128 events and128 retained current operation streams are admitted.
Input/result JSON is at most256KiB, with strict duplicate-key/unknown-field refusal. A previous
current view carries each lane's owner/candidate/attempt, state/action, reasons/evidence,
operations and unresolved obligations. Unresolved conflict/scope/unknown effects survive
new summaries. Dropping a previous lane from the complete snapshot refuses. Unknown/unclean
original operations retain reservations even when a caller clears `inFlight`; overlap consumes
that retained inventory. Missing or stale owners cannot release it.

Original operation effect class and execution/observation/allocation bounds are immutable
across observations. A changed invariant fences the stream; protected history retains its
admission. Equal-revision disagreement and original-lineage conflicts cannot be overwritten
by a newer row. Complete revision chains are deterministic under permutations and duplicates;
gaps, missing bases and stale/future facts require refresh. Gap/base obligations remain
in the current view and its holds; a latest row cannot become its own accepted base.
A recorded gap clears only after explicit complete same-stream chain evidence; a missing
delta base requires a compatible supplied base with no resynchronization notice. Scope-change and admission-required obligations persist across current-view cycles.
The same validated stream join supplies both retained holds and routing; stale/conflicting
cleanup or missing revisions cannot release them. A single repair predicate governs
cleanup, deadline and terminal-display checks, including contradictory accepted/failed labels.
Fresh explicit original-identity `effect-reconciled` evidence can clear an unknown effect;
cleanup or an ordinary success alone cannot. Protected operation admission stays external.
A terminal display never authorizes a retry after the execution deadline.

The full `fsgg.programme.routine-decisions/1` artifact contains one `current` view per lane,
semantic `changed` decisions, complete retained `activeReservations`, identities, evaluation
time, resynchronization and explicit authority/coverage limits. Compare semantic decisions,
excluding incidental observation timestamps. Feed `current` back as `previous`; keep the
artifact/history external, without a new state store or append-only event ledger.

Stdout is `fsgg.programme.routine-decisions-summary/1` with changed lane/owner/candidate/attempt,
state/action/reasons/evidence, complete artifact path and SHA256. Its entire UTF-8 encoding,
including newline, is at most2048 bytes. Overflow emits `overflow-retrieve-artifact`, count
and pinned artifact reference; no decision or evidence is sliced. Even an oversized artifact
reference or refusal produces a fixed bounded refusal (exit3). The full retained artifact
and existing measurement remain available; successful pointer emission does not prove review.
Legacy stdout limits remain unchanged. Read missing obligations through the bounded context
view instead of printing the entire artifact.

`tests/work-programme/routine-decisions.fsx` exercises the actual reducer and CLI. Recorded
private replay is a separate observation-only population: retain original identities, times,
first failures, native evidence pins and explicit gaps; never dispatch historical work or
infer acceptance, family usage or savings. First adoption requires these controls, selected
recorded replay and normal source delivery; later ordinary routing uses fresh native facts
within existing authority. Active protected operation pins and holds remain unchanged.

For native worker continuation, use `followup_task` for a completed/idle owner;
`send_message` delivers a message but does not start its next turn. This distinction
changes no owner/attempt/effect identity. A continuation never replays an unknown native
operation. Record prospective telemetry when available; a missing follow-up join is an
attributed gap, and does not block an otherwise authorized ordinary review.
