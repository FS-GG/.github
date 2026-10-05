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
- Unknowns and continuation: `unknowns` (0–8 strings, each at most256
  characters), `exception` (explicit `none` when absent), `continuation`
  (each nonempty, at most1024 characters), `narrative` (nonempty, at most2048
  UTF-8 bytes). Overflow must remain in referenced evidence rather than be
  silently truncated.

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
