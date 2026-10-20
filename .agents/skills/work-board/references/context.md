# Parent context operations

The canonical [work-board](../SKILL.md) governs current behavior. Historical pilot/default,
packet/return/index and reporting-cadence instructions below describe retained diagnostic contracts
only: they are optional and cannot restore roadmap scheduling, duplicate current-status/closure
upkeep or an active legacy workflow. Current scope and source/effect admission take precedence.

Apply the canonical [scope and coordinator boundary](../SKILL.md). Organization scope uses the
accepted Project 3 contract; product scope uses only its selected V2 binding and provenance.
Recover exact outcome/slice/attempt, owner, existing PR/head and current native issue/body/plan joins.
Optional diagnostic helpers never become a current-state ledger, capacity supplier or delivery gate.
Missing facts remain attributed Unknown; recheck determining facts beside every operation.


Use `scripts/context.py COMMAND INPUT PRIVATE_ROOT --input-sha256 SHA` for parent reads and context
diagnostics. Commands are `view`, `review`, `report`, `command`, and `restart`. Python standard library only; no CLR,
GitHub, model, workload launch or daemon. Fresh native admission still governs every actual effect.
Inputs are pinned JSON of at most256KiB; duplicate keys/unknown fields refuse. Results use unique0600
files under a dedicated0700 private root. Exit0 means the projection succeeded; exit3 means refusal.
Default stdout is8KiB (`--stdout-bytes` accepts1024–65536). Larger results return a path/digest, never a
truncated finding. Retrieve the required sections through `view`; a retained handle is not proof of review.

## Capacity window and assembled test candidate

Admit the actual operation and its transitive launch closure. Supported profiles, conservative demand
and current remaining capacity can permit useful independent builds, installations or validations
alongside remote CI/publication/supplier waits. Start serially when those bounds are missing; do not
require another serial run of an already qualified profile or impose a blanket one-expensive-operation
rule on independently qualified operations that fit together. Preserve actual large-image profiles,
exclusive shared writes and existing source custody.

Retain current CPU/memory limits, availability and observation time in the existing owner artifact,
including helpers/build servers and all known/Unknown reservations. Use the applicable quota, CPU set and
profile limits rather than host core count. For the existing integer resource fields, floor fractional
CPU capacity and round demand/reservations conservatively upward; retain the actual fractions in the
evidence. Convert memory to the same units conservatively too. A brief idle sample, free agent slots
or a supplied machine envelope establishes neither demand nor admission.

Account for combined memory peaks, transitive children, setup and uncertainty/headroom. A managed heap
limit is not a process/group RSS cap; sampled peaks can miss overlap. Distinguish anonymous memory,
shared backing and file cache without double counting or assuming inactive cache is safely reclaimable.
Verify actual storage backing, available space and source/output/cache write domains; an overlay or
directory name leaves SSD/HDD placement unknown. Unbounded writers or uncertain shared state hold the
affected operation until its bounds are established. Coordinate unresolved storage/profile changes
with V3-ENV-01. An unrelated supplier gap or scoped Unknown does not suspend independent work; preserve
its original hold and release condition without treating its demand as zero.

At relevant supplier reply/deadline boundaries, the accountable owner disposes the input through the
existing owning issue/channel and names the next supplier action or decision. Do not defer useful
follow-up to an unrelated runtime checkpoint or create a reporting worker/mailbox ledger. Missing
operator access or a runtime profile still fences that effect; source/design preparation and bounded
read-only work can proceed within their own authority and verified capacity. Neither preparation nor
a supplier request invents credentials, an executor or a supported profile.

### Worked admission and evidence route

Use the existing [routine-decisions contract](adapter.md#additive-routine-decisions), not a second
scheduler. For example, one component has a supported bounded qualification while another owner has
an independently useful local validation. The designated capacity owner follows this route:

1. Retain each operation's exact source/profile, demand evidence, original bounds and observed result/
   cleanup in its existing owner artifact. Two CLR processes inside one caller are one workload, not
   two independently admitted operations. Where demand is unqualified, first obtain an admitted serial
   measurement; retain sampling uncertainty/headroom rather than convert a sample into a hard cap.
2. Refresh the complete retained reservation view and native process/storage evidence, including
   helpers/build servers. Preserve unknown processes without inventing lane/operation identities. Disjoint worktrees do not
   prove independent caches, outputs or backing storage. Verify remaining combined headroom before
   admitting the next operation; retain any narrower profile or unresolved affected hold.
3. Map observations to the existing input fields below, retaining genuine source/owner/attempt joins
   and original observation times. Carry `previous` and original operation history forward; a newer
   summary cannot release an unresolved reservation or renew its deadline.

| Existing input | Evidence supplied by the owner |
|---|---|
| `resources.known`, `observedAt`, `evidence` | Current completeness/freshness and the original inventory/measurement pointer; unknown availability stays `known:false`. |
| `resources.cpu`, `memoryMiB`, `occupiedCpu`, `occupiedMemoryMiB` | Verified capacity and conservative occupied/reserved totals in the same units; include admitted helpers and build servers. |
| `resources.agentMode`, `cpuMemoryMode` | Actual soft/enforced/unknown modes; agent slots and machine resources remain separate. |
| Operation `cpu`, `memoryMiB`, allocation and deadline fields | That operation's supported profile/demand and unchanged original bounds, backed by its evidence/history. |

4. Evaluate through the existing `routine-decisions` route. Unknown/stale resource evidence yields
   `wait-for-resources`; insufficient remaining CPU/memory yields the same action with a distinct
   reason. Preserve the full retained reservation view and the original blocker. The reducer compares
   each operation with caller-supplied occupied totals; it does not atomically reserve several proposed
   workloads. Account every newly admitted demand before evaluating or launching another operation.
5. A favorable advisory result grants no execution. The existing native runner/owner rechecks actual
   source, resources, disjoint outputs and shared caches/state beside launch under the applicable
   admission. For an admitted pair, retain actual overlap, per-operation and combined demand, timing,
   results, first failures and independently observed cleanup in the same owner artifacts. If the
   second operation stays held, record the determining scope/reopening condition; free cores alone
   do not clear it. Source/CI delivery, publication and installed caller/instruction adoption remain
   separate. Two-operation concurrency acceptance needs useful local operations, not synthetic activity;
   economic comparisons require comparable coverage and can remain inconclusive.

This field mapping is not a runnable input, a grant or proof of complete inventory. Reuse the current
[typed controls](https://github.com/FS-GG/.github/blob/75dfe5a98ce49fc5db3dab6232e554c7862a32f3/tests/work-programme/routine-decisions.fsx) for unknown/stale resources,
CPU/memory occupancy and retained holds; no new scheduler or process ledger is needed.

### Preflight the actual handoff

Reuse maintained preparation/collection routes and existing owner artifacts. Inspect forwarding scripts
and every transitive launch port before classifying a Python entry as pure: a Python wrapper can start
CLR, acquisition or a product workload. Start the original clock before imports/preparation and include
startup, children, output collection, actual helper cleanup and terminal verification. A helper's
five-second cleanup reserve consumes that same window; wrapper repair or input finalization renews
neither an original execution deadline nor an uncertain effect.

Check genuine producer bytes and the declared graph before freezing fixture expectations. Worker
source with two trailing newlines can be valid; original-resource integrity and encoded `.br`/`.gz`
integrity differ; a reduced cache fixture does not establish the real cache graph. Preserve supported
variants and deliberate negative controls. Diagnose wrong fixture/input assumptions at their owning
boundary instead of treating a private guard refusal as a native product defect or replaying work.

Before an effect-capable handoff, create the final input exclusively with mode0600 under its private
root, freeze its hash, and check the actual closed schema, identity joins and branch predicates. For
integrator observations, each of `capacity.observation`, `reservations.observation` and
`slices[].observation` needs a genuine supplier, current bounded times and nonempty evidence. Accepted
action prerequisites need their real truth/supplying owner and complete coverage before setting
`prerequisitesComplete`; dating an empty/pending draft supplies none of those facts. Use existing cheap
controls for missing evidence and false/incomplete prerequisites instead of discovering draft defects
through another expensive read.
Use a supported typed route for its own domain: `routine-decisions` does not re-evaluate a saved V2
inspection with a different schema. A projection cannot turn an original Unknown selection into Ready.

At dispatch and push, reconcile the actual protected base and current native/body/owning-plan/PR/head
joins. An unchanged immutable blob can preserve a source fact while the maintained reader reports a
new current protected revision; verify that equality and refresh the exact attributed join. Preserve
the original result instead of assuming equal content preserves a stale commit/event binding. If
required CI's event base is stale, integrate the current protected base into the same owner/branch/PR
and qualify that candidate through the normal route; do not manually rerun the stale event or invent
a replacement PR. Stable explicitly classified unenrolled memberships remain visible without automatic
enrollment; membership movement during a read leaves that original incomplete and grants no selection.

Run final checks and invocation in one failure-gated sequence as required by the
[final-input guidance](delivery-reliability.md#final-inputs-and-failure-gated-invocation). A failed check
must stop every following launch, including later shell lines. Keep original refusals/partial effects,
scoped Unknowns and all reservations. These are finite cooperative checks, not atomic/server CAS.

### Cheap assembled carrier preflight

For changes to skills or Atlas delivery, run from the final candidate checkout:

```bash
bash tests/skill-quality/run.sh --preflight BASE_SHA
```

Replace `BASE_SHA` with the exact 40-character base commit available in local Git objects. Requires
Bash, standard Unix file tools, Python 3, Git and Python SQLite with FTS5; no downloads, PyYAML, SDK or CLR launch. The command
runs the existing Atlas delivery checks, including archive/seed agreement, history and staged payload,
then uses the full skill-quality fixture's same partial tree and relative-link predicate. Python
bytecode writing is disabled throughout; existing generated caches or changes to delivered skill bytes
refuse before any gate-engine build. A link can
resolve in this checkout but fail when the fixture omits its target. Keep repository-only references
as explicit source URLs or prose paths instead of claiming they are carried with the skill.

A failed check, malformed base, missing prerequisite or uninterpretable result returns nonzero before
any gate-engine build. Exit0 proves only this assembled archive/link scope; it grants no resource or
effect admission and replaces no semantic, compiler, native, installed or required hosted assertion.
The normal `bash tests/skill-quality/run.sh` also runs these cheap checks before its unchanged full
qualification (without an explicit history baseline; the dedicated Atlas gate retains that obligation).
Measure the complete command including fixture preparation, staging and cleanup; retain original
failures and cold/warm setup uncertainty rather than claim avoided attempts or comparative benefit.

For this repository's work-board suites, run from the checkout:

```bash
bash tests/work-programme/run.sh --preflight
```

Requires Bash and Python 3; checks `dotnet` availability for the selected FSI suites without invoking it.
No dependency installation, CLR launch or external request occurs. This is the existing maintained test
entry, with a static preflight mode. It checks the assembled suite inventory, declared launcher/tool joins,
entrypoint membership in hashed inputs, dependency order, file presence and symlink/path containment.
JSON names blocked suites and missing tools/inputs; fix the named declaration/file/tool and rerun.
Exit0 means only those checks passed; `executed:false` and `qualification:not-evaluated` retain that limit.
Any blocked/malformed/unknown input returns nonzero; timeout or missing output supplies no pass.
It cannot discover undeclared dynamic reads or transitive launches in trusted test source: the owner still
inspects that closure and obtains its profile. Hashes establish identity, not correctness or authority.

Normal `bash tests/work-programme/run.sh` performs the same assembled-plan inspection before any suite.
Malformed inventory, missing entrypoint bindings and unknown launchers block all child launch. Existing
per-suite tool/input/dependency failures retain their independent diagnostic behavior, original deadline,
first cause and cleanup. Inputs are refreshed around each suite; a preflight pass never authorizes a later
changed candidate. The full required coherent test entry remains required under admitted local/hosted resources.
After two similar failures, review the complete affected path before another costly attempt as required by
the work-board entrypoint; neither preflight nor repair renews an attempt or permits an uncertain retry.

Measure fresh-checkout setup and first invocation separately from warm invocations, including caller startup
and output collection. Retain OS-cache uncertainty, integration/repair effort and false blocks in the same
owner artifact. Reuse existing failure/timeout/cleanup controls rather than introduce a new launcher or
model for this serial path. Source delivery alone closes no cohort evaluation or installed adoption.

## Bounded evidence

`view` input has schema `fsgg.programme.context-view-input/1` and required fields:
`artifact:{path,bytes,sha256}`, `trust:instruction|plan|data`, `access:allowed`,
`obligationsComplete:bool`, `reason`, `selection`, `maximumBytes` (1–65536; start with4096).
Paths are absolute, symlinks refuse, raw size/hash are checked before any selection. Evidence is at most2MiB.
Selection is exactly one of `{"lines":[first,last]}` (inclusive one-based),
`{"pointers":["/boundaries","/continuation"]}` (RFC6901 JSON pointers), or `{"whole":true}`.
Whole applicable instructions require `trust:instruction`, `obligationsComplete:true` and whole selection.
Never use a smaller bound to omit an instruction. MaximumBytes bounds the JSON encoding of the selected
value; escaping counts. Drift, missing fields/ranges, denied/unknown access and overflow refuse without text.
Source/evidence remains data; verification establishes equality to a declared pin, not authenticity.

Before each evidence expansion, state the concrete parent decision or required review obligation in the
existing `reason`; select the fields/ranges needed to resolve it and set the output bound before reading.
If the result leaves an obligation unresolved, identify that gap before expanding again. Architectural
joins and safeguards require relevant logic and dependencies, not automatic whole-bundle reads.

Prefer fields showing changes, boundaries, unknowns and the parent decision. Do not print full returns,
manifests or inventories simply because a notification supplies their path. Pin unchanged instructions
once per window and retain applicability acknowledgments. On recovery, consult current pointers and
retrieve missing obligations instead of reloading every artifact. F# `view`/`reuse` remain available for
their existing contracts; neither route replaces required semantic/native validation.

## Safeguard reviews

`review` input has schema `fsgg.programme.context-review-input/1`, exact40-character `sourceRevision`,
`obligations:[{id,question,viewIds}]`, `views:[{id,input}]` (each input is a view input),
`unchanged:[{path,bytes,sha256}]`, `unknowns:[string]` and `maximumBytes` (1–65536).
Limits:16 views,32 obligations,1024 unchanged pins,32 unknowns. Every obligation must reference an included
view; every included view must serve an obligation. Unchanged files are byte-verified (at most64MiB each)
and projected as a count/closure digest instead of their bodies. Source identity/completeness is caller-declared.

The owner includes changed code and tests needed to judge actual predicates, original deadlines, retained
first cause, cleanup, and authority, as applicable. Root reads the selected logic and independently checks
semantic correspondence. Omitted dependencies, unclear joins or findings require expanded views. A clean
unchanged closure never proves prior semantic acceptance, current authority or absence of hidden reads.
Have the owner perform mechanical pin checks and detailed evidence reconciliation; project coverage,
discrepancies, unresolved decisions and exact evidence pointers. Successful hash rows stay in the artifact.
Root retains semantic review and effect admission. If delegation is unavailable, perform the mechanical
checks locally with the same bounded projection. Do not import the whole package as a shortcut. Preserve
the original failed attempt outside the review.

## Existing guard commands

`command` prepares an argument vector; it never executes it. Input schema
`fsgg.programme.guard-command-input/1` has pinned `python`, `launcher`, `operations`, `window`, `runtime`,
`binding`, plus `bindingSha256`, `index`, and absolute unused `receipt` path. Pins have path/bytes/sha256.
The helper verifies their bytes and operation/binding/window identity joins and rejects an existing receipt.
It emits the existing selected-entry launcher's fixed flags, with `executed:false` and `windowRenewed:false`.
Root still supplies independently checked source/runtime/support closure and fresh effect-specific admission;
the existing launcher validates live deadlines, capabilities, accounting and cleanup. Command preparation
is not one-use consumption, executable authenticity or admission. Do not replace a refused guard with an
unguarded command. Use the pinned descriptor and fixed launcher invocation rather than rebuilding repeated
assembly/check scripts in model context; novel guard semantics remain separately reviewed source changes.

## Native context report

`report` input has schema `fsgg.programme.context-report-input/1`, UTC `start`/`end`,
`sessions:[{sessionId,artifact:{path,bytes,sha256}}]`, `expectedSessionIds:[string]`,
`helperDirectories:[absolute-path]`. Explicit selections only: no session-directory crawl or recursive
helper discovery. Up to32 sessions,64 named helper directories and10000 helper measurements. Pin a stable
copy of an actively appended native log using the existing private artifact route; do not modify its original.
Each session snapshot is at most64MiB. Native metadata must match the declared session ID. Parent/agent IDs
come from metadata; missing participants remain named. An unterminated malformed final append is reported
as incomplete coverage; malformed complete/interior records refuse. Raw messages and credentials are not
printed. Compactions are observed events in the window, never inferred from text size or elapsed time.

The report gives native input-token min/max, tool exposure bytes/calls, compaction timestamps, and deltas
of cumulative input/cached/output/reasoning counters only with a pre-window baseline and no reset. Missing
baseline or any counter decrease yields unknown deltas. Token counter inputs count repeated inference
history; caching reduces some cost, not context length. Session snapshots/selected records do not prove
continuous observation or complete whole-family/item usage. Join genuine dispatch/attempt evidence through
the existing observer before making economic claims; this command cannot mint or repair those joins.

Helper measurements are collected only from the named directories and timestamped filenames within the
window, separately from native counters. Existing `programme.fsx report` covers one helper directory's I/O;
use both reports without treating byte counters as native tokens or unobserved compactions as zero.

## Context rotation and restart

At a completed milestone, repeated native compactions or substantial recovery rereads justify considering
fresh-context continuation. The current owner prepares a compact checkpoint: original/current identities,
exact source, current return/packet pins, full reservation pointers, pending effects, first failures and next
bounded action. Prefer the runtime's same-owner fresh-context mechanism. Never interrupt a live effect or
transfer ownership implicitly. If the mechanism is unavailable, retain the owner and report the capability
gap; a packet does not erase inherited history. No rotation renews an execution window or changes an attempt.

Use [routine-decisions](../../routine-decisions/SKILL.md) for the sole current lane view and its bounded changed-decision projection.
Keep the dispatch index focused on current navigation; replace superseded entries and link detailed history.
A delegated recovery owner reads the full handoff once. Parent recovery consumes its compact decision view,
complete reservations and unresolved decisions. With delegation prohibited/unavailable, the parent performs
the same bounded recovery directly. Preserve every unknown reservation and unresolved operation.

## Compact restart joins

`restart` verifies navigation for an existing owner without dispatching, retrying or releasing a reservation.
Its input schema is `fsgg.programme.context-restart-input/1`, with `campaign`, `lane`, `owner`, `attempt`,
exact40-hex `sourceRevision`, `repository`, positive `pullRequest`, `maximumBytes` (1–65536), and pins
`packet`, `currentReturn`, `reservations`. `previousReturn` and `watcher` are pins or null.
`reservationCompleteness` is `declared-complete` or `unknown`; the declaration is not authenticated.
Pins use the existing `{path,bytes,sha256}` contract and2MiB artifact bound.

The current artifact uses the existing `fsgg.programme.lane-return/1` field names. Its campaign/lane/owner/
attempt/source must match the input, candidate must equal that source, and packet digest must match the
complete packet bytes. Adjacent owner revisions retain campaign/lane/feature/item/original item/original
attempt/owner/attempt/packet identity; source repair may advance candidate bytes. Conflicting joins refuse.
A missing base above revision1 requests resynchronization. Missing original identities remain null and hold
continuation; they are never inferred from the current item or attempt. Canonical return/delta/routine
validation remains required: this command verifies navigation joins and does not replace those contracts.

The reservation artifact is verified and retained as an opaque pin, including inventories whose missing
member fields prevent conversion to helper lanes. Unknown completeness holds continuation. The command
cannot discover omitted reservations or authorize global routing from a complete declaration. Preserve the
original missing-inventory pointer and custodian gap in that artifact; a successful projection clears no hold.

An optional single existing `fsgg.routine-check-watch/v1` record must match repository/PR/exact head and
retain `readiness:not-evaluated` and `applyAuthorized:false`. Retain a stable copy of one JSONL record if
needed, linking its original watch artifact in existing evidence. Watch-local revisions remain separate
from owner-return revisions. Terminal events, restored notifications and matching pins still require fresh
exact-head native observation; they grant no effect or acceptance authority. Results retain the current and
previous return pins, full reservation pin, original lineage, separate revisions and holds. Oversize results
refuse without partial inventory. This source helper does not establish actual interruption recovery or
V2-CTX-01.4–.6 operational acceptance.
