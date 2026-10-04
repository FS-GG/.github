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
