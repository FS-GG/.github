# ADR-0098: Per-resource publication claims for disjoint writers

- **Status:** Proposed (not accepted; changes nothing until Root accepts, an implementation is merged and a qualification record is read back)
- **Date:** 2026-10-30
- **Affects:** `.github` publishing workflows and the Authority journal repository; every registered publisher repository only through its own registry row
- **Decision owner:** Programme/root (reopening event named in [FS-GG/.github#4753](https://github.com/FS-GG/.github/issues/4753) "Next action")
- **Amends (when accepted and qualified):** [ADR-0097](0097-one-org-wide-serialized-publication-writer.md) Decision "no parallel publication and no per-resource reservation". Until then ADR-0097 is in force unchanged.
- **Source basis:** `FS-GG/.github` `main` at `e3712811f4241d775d64325e26a3ceeff3559f17` (paths below are at that revision).

## Context

ADR-0097 serializes every publishing job through one lock. Journal compare-and-swap (CAS) rejects a
conflicting journal update but does not fence two runs from interleaving package, tag and release
writes (`scripts/release_successor_journal.py:1-6,768-842`). The lock itself is only as strong as the
Actions job list: its own residual says that if listing lags beyond the 20 s settle, "two jobs could
overlap; the journal CAS and per-effect guards remain the fence"
([ADR-0097](0097-one-org-wide-serialized-publication-writer.md) Consequences).

Root's decision of 2026-10-24 ([#4753 comment 6385677196](https://github.com/FS-GG/.github/issues/4753#issuecomment-6385677196))
chose one serialized writer *instead of* an aggregate-resource profile, and ADR-0097 lists that profile
as replaced. The 2026-10-28 comments ([6470052133](https://github.com/FS-GG/.github/issues/4753#issuecomment-6470052133),
[6470790144](https://github.com/FS-GG/.github/issues/4753#issuecomment-6470790144)) and the issue's
"Next action" still treat "a maintained aggregate profile and actual conflict enforcement for disjoint
writers" as a Root-owned reopening event. This record is a proposal for that event only. It does not
reverse the 24 October decision; if Root regards that decision as final, withdraw this record
(open question 1).

Facts that shape the design:

- A conflict needs the same key, and same-key writers share a repository. nuget.org Trusted Publishing binds each
  package pattern to one repository and workflow (owner-reported list, not agent-verified:
  `docs/coordination/2026-10-28-nuget-trusted-publishing-readout.md`); tags and releases live in the
  producer's repository. What is genuinely org-wide is the **shared budget**: the Authority App's REST
  quota and Authority `main`/journal refs, and hosted runners.
- Only publishers that already hold the Authority ledger credential (the `.github` writers and one
  Coordination environment, [ADR-0097](0097-one-org-wide-serialized-publication-writer.md) "Why this
  mechanism") can write a claim record. No new credential is minted here.
- `BoundIntentFence` (`scripts/release_successor_execution.py:84-168`) already re-reads the journal
  immediately before a send and refuses unless it still shows this invocation's own write. It is opt-in
  and **no production publisher passes `fence=`** (`git grep BoundIntentFence -- scripts` finds only the
  engine; `advance_effects` is called without it at `scripts/new-sdd-workspace-successor-publish.py:196`,
  `scripts/telemetry-host-successor-publish.py:126`, `scripts/release-successor-publish.py:1340`).

## Decision (proposed)

### 1. Conflict resource keys

A writer's key set is a closed function of its admitted candidate descriptor and release binding:

| Kind | Key | Destination it protects |
|---|---|---|
| `package` | `package:<lowercase id>@<normalized version>` (both feeds share the key) | nuget.org and GitHub Packages push of that id/version |
| `tag` | `tag:<repository>/<tag>` | `refs/tags/<tag>` creation |
| `release` | `release:<repository>/<tag>` | draft, assets, promote of the selected release object (`draft=false`; `make_latest=false` in the Wizard route, `scripts/new_sdd_workspace_successor_provider.py:276`) |
| `journal` | `journal:<logical ref>` | the selected journal (`REF`/`logicalRef`) |

Only writers whose key sets intersect conflict, **irrespective of candidate or run identity** (#4753
"Current deliverable"). Disjoint key sets may run concurrently, subject to the aggregate profile in
section 4. There is no shared latest channel or monotonic pointer to claim. A fresh journal identity for the same
version (the 0.22.0 `022r1` case) is caught because the `tag` and `release` keys are unchanged.

### 2. Enforcement mechanism

Two layers, both built from primitives the repo already maintains. Neither grants authority.

**Layer A, aggregate admission (capacity N lock).** Extend `scripts/publication_writer_lock.py`'s
bakery rule from "no strictly older live participant" to "fewer than N strictly older live participants
and no strictly older live `global` participant". N and each registry row's `exclusion: global|keyed`
come from `registry/publication-writers.json`, which the lock already reads live. Default is `global`,
N = 1, which is byte-for-byte today's behaviour. A `global` participant still proceeds only with no older
live participant of any kind, and a younger participant waits for an older `global` one. Job-list lag can
still admit an extra writer; Layer B is what makes that harmless for same-key conflicts.

**Layer B, durable per-key claim with an epoch fence (the gap-closer).** For each key, one protected ref
in the Authority repository under `refs/heads/fsgg/v2/claims/<kind>/<sha256(key)>`, written only with the
primitives `ProtectedReleaseJournal` already uses (`GitAPI` get/post/patch, `scripts/release_successor_journal.py:51-56`):

- **Acquire** = create-if-absent (`POST git/refs`, as journal `initialize`, `:698`). The ref already
  existing is a refusal, not a retry, so exactly one creator wins.
- **Advance / release / takeover** = non-forced fast-forward `PATCH` from the exact head read (`"force": False`, `:830`), with the existing bounded acknowledged-write readback (`:844`). Claims are append-only commits; nothing is force-written or deleted.
- **Record** (canonical JSON, one blob): key, `epoch` (generation, strictly increasing), holder
  (repository, run id, attempt, job id), journal ref plus the generation and `contentId` it is bound to,
  the request digest of the in-flight effect, `state` in {`held`, `effect-uncertain`, `released`},
  `expiresAt` and the profile numbers reserved (section 4). Claim acquisition is all-or-nothing in
  sorted key order; any failure releases what this writer took and refuses. This ordering and the bounded
  wait prevent hold-and-wait deadlock.
- The claim namespace is protected like the journal namespace (only the ledger App writes it). Creating
  that protection is a live Authority change and is not performed or authorized here.

**Why this fences a stale writer that returns after losing exclusion.** The fence is a re-read at the send
boundary, not the lock. The existing `BoundIntentFence.release` pattern (`:134-153`) is extended so that,
in the same step where it re-reads the journal, it also re-reads every claim head and spends the send
permit only if each is exactly the state this invocation wrote (same epoch, `held`, same journal binding).
A writer that lost its claim (lease takeover, section 5) or whose claim moved sees a different epoch and
refuses before `provider.dispatch`. Journal CAS alone cannot do this because a successor on a *different*
journal ref (a fresh identity) shares no journal head with the stale writer; the claim is keyed on the
destination resource, so both writers contend on the same ref.

**Takeover rule (no blind retry).** A claim may be taken over only if **both** its `expiresAt` has passed
and the recorded holder job is observed not `in_progress` through the public Actions job list (the same
read the lock uses, `scripts/publication_writer_lock.py:169`). A takeover of a claim whose state was
`effect-uncertain`, or whose journal has an `intent` not yet `verified`, yields an **observe-only** epoch:
the successor may run `full_transition`'s observe and settle path (`scripts/release_successor_execution.py:283-292`) but may not dispatch that effect.

**Residual window that no GitHub primitive removes.** A request already on the wire from a holder whose
job has ended (runner loss) cannot be recalled. It is bounded by destination behaviour, which differs by
effect:

| Effect | Destination behaviour a late write meets | Status |
|---|---|---|
| `tag` | `POST git/refs` for an existing ref is rejected | to be qualified by fixture, not assumed |
| `package` push | duplicate id/version push is rejected by each feed | to be qualified by fixture |
| asset upload | `_ordinary_release_witness` refuses a duplicate name (`scripts/release_successor_provider.py:1575-1612`) | detected at readback |
| `draft` create | not atomic; a second draft makes the witness refuse as ambiguous | detection, not prevention; `promote` targets one release id, so a stray draft is never promoted |
| `promote` | `PATCH` of one release id with fixed fields | idempotent; changed parameters refuse via the intent digest |

This is stated as a limit, not solved: for `draft` the system detects a duplicate and holds; it does not
prevent it. The lease is therefore sized to the holder's hard job bound plus the liveness check above,
not to a heartbeat (no renewal traffic).

### 3. What is refused

Same-key claim held by a live or unexpired holder; claim head or epoch changed at the send boundary;
key set larger than the claimed set; claim unreadable or unwritable (fail closed, no effect); takeover
without both expiry and holder-not-live; any dispatch under an observe-only epoch; changed parameters
under the same intent digest; a `keyed` registry row without a qualification record (below).

### 4. Aggregate profile

All numbers come from existing source; this record invents no capacity. Claim writes are counted by the
same `CountedAuthorityAPI`, so the per-writer ceiling does not grow.

| Quantity | Value | Source |
|---|---|---|
| Per-writer Authority ceiling, recovery/Creator profile | 4200 application entries; quota floor 4500 | `scripts/release-successor-publish.py:769-770`; Creator `scripts/new-sdd-workspace-successor-publish.py:305` (default counter), `:131` |
| Per-writer Authority ceiling, ordinary profile | 1500; quota floor 1800 | `scripts/release-successor-publish.py:772-773`, applied `:1964` |
| Per-writer visibility probes / reconcile bound | 135 probes; 45 min and `2*effects+120` = 136 iterations (8 effects), 5 s wait | `:774`; `scripts/new-sdd-workspace-successor-publish.py:184-212` |
| Hosted runner per writer | one `ubuntu-latest` job, `timeout-minutes: 90` (60 working + 30 lock wait) | `.github/workflows/release-successor-publish.yml:109-122` |
| Observed single-run use (coverage: one run) | 343 GET + 10 PATCH + 70 POST = 423 entries over 16 min, against ceiling 1500 | [#4753 comment 6366494954](https://github.com/FS-GG/.github/issues/4753#issuecomment-6366494954) |
| Authority core limit | read live from `rate_limit`; 5000 appears only as a test fixture | `scripts/release-successor-publish.py:851-870`; `tests/new-sdd-workspace-successor/run.py:1608,2129` |

Admission rule at claim time (checked after the Layer A decision): live `remaining` must be at least the
sum of the `quotaFloor` reserved by every `held` claim plus this writer's own floor. This deliberately
over-reserves (it ignores spend already made). Consequence at a 5000 limit: one 4500-floor writer, or two
1800-floor writers (3600), never a third (5400). The proposed hard cap is **N = 2** (a policy choice for
Root, not a measured capacity), always further limited by the rule; the recovery/Creator profile effectively
stays exclusive. Claim cost is design-estimated, not measured: one claim transition costs one journal CAS
(3 POST + 1 PATCH + readback GETs, `:736-750`), so acquire plus release of 4 to 6 keys is about 32 to 48
mutating entries, well inside the 1500 ordinary ceiling given 423 observed for a whole run.

Secondary limits: GitHub discourages concurrent mutation and requires `Retry-After` handling
(`docs/coordination/2026-08-30-github-substrate-v2-remaining-migration-architecture-review.md:145`; the
EX_RATE contract notes secondary limits are triggered by concurrent request bursts and are not visible in
`rate_limit`, `.claude/skills/pnext-item/references/command-contracts.md:55`). N = 2 is the first time two Authority mutators overlap, so it is the
primary risk. Any 403/429 carrying `Retry-After` during qualification or later reverts the row to
`global`/N = 1. Hosted-runner concurrency is a plan limit not present in source; it must be read live
before enabling (open question 3).

### 5. Lifecycle

1. **Claim.** Layer A admits; read `rate_limit` and held claims; acquire all keys (section 2).
2. **Fresh authorization.** Existing admission is unchanged and decides every effect: `intent`, `dispatch`
   and `settle` re-authorize against live revocation and protection
   (`scripts/release_successor_execution.py:283-309`). A claim is never authority; revocation after claim
   with no intent releases the claims, with an intent keeps them `held`.
3. **Effect.** Journal `intent` CAS, then the extended send fence re-reads journal and claims, then one send.
4. **Independent readback.** Provider observation compares destination bytes and identity, not name/version
   alone, against the original intent; `matched` settles `verified` by CAS.
5. **Release.** After the last effect is `verified` (or a proven no-send), advance each claim to `released`.
6. **Uncertain effect or lost reply.** Dispatch `unknown`: claim becomes `effect-uncertain`, nothing is
   re-sent, and the engine observes (an `intent` with `absent` stays waiting by design,
   `release_successor_execution.py:283-285`). It is released only on `matched`. A `mismatched` or
   permanently unobservable result keeps the claim and the explicit Unknown; a human decides.
7. **Lease expiry.** Expiry alone changes nothing; takeover needs the holder-not-live check and is
   observe-only for any open effect.
8. **Rollback.** There is none for published artifacts. Before the first effect, claims are released and
   nothing remains. After it, the journal and Unknown are authoritative. The enabling change itself rolls
   back by reverting the registry row to `global`.

**Retaining the global exclusion.** Nothing changes now. After an implementation merges, every row stays
`global`. `scripts/check-publication-writer-coverage.py` (a required selftest) is extended to refuse any
`keyed` row, or N > 1, that does not name a qualification record id; a row is switched by a separate
reviewed registry change after the controls below pass, and a first `keyed` use is a separately admitted
ordinary publication. ADR-0097 stays the rule until then.

## Consequences

If accepted, a later implementation Delivery would need the following (effects, not assignments).

### Source changes and controls

Source: lock capacity rule and registry fields (`scripts/publication_writer_lock.py`,
`registry/publication-writers.json`, coverage checker); a claim module over `GitAPI` beside
`scripts/release_successor_journal.py`; extension of `BoundIntentFence`
(`scripts/release_successor_execution.py`) and wiring it into the three publishers
(none uses it today); quota-reservation admission in `CountedAuthorityAPI`; workflow input/key derivation
from the candidate descriptor. Several of these files are authentication-pinned or pinned by Coordination
(`eng/execution-pair-forward-admission.json`, #4753 comment 6361575306 and ADR-0097 Consequences); each
needs its re-pin decision. Authority claim-namespace protection is a separate admitted live change.

| Fault case (comment 6470052133) | Existing fixture to reuse | New control |
|---|---|---|
| Same candidate across unrelated main change | `tests/new-sdd-workspace-successor/run.py:1414` (added reports), `:1428` (unknown graph keeps equality) | none; reuse unchanged |
| Revoked authority / mismatched provenance | `tests/release-successor-execution/run.py:251`; `tests/new-sdd-workspace-successor/run.py:1090,2088` | revocation with claims held versus released |
| Two writers, same key | `tests/release-successor-journal/run.py` `FakeGit` (create-if-absent, non-fast-forward); `tests/publication-writer-lock/run.py` `VirtualClock`/`FakeGitHub` | two writers contend on each key kind, including a fresh journal identity for the same tag; exactly one proceeds |
| Disjoint keys proceed | same fakes | two disjoint writers overlap under N = 2, a third waits; N = 1 reproduces today's order |
| Stale writer after exclusion loss | `tests/release-successor-execution/fence_controls.py` (`SCENARIOS`, `MUTANTS`, `regression_defence_fenced`); lock `crash_after` | claim-epoch mutant: removing the claim re-read must be killed by a named control; takeover refused while holder live or unexpired |
| Lost reply / uncertain write | `run.py:220,257,276`; `tests/new-sdd-workspace-successor/run.py:1885,1897,1929,1964,2075` | `effect-uncertain` blocks release and observe-only takeover |
| Same operation, changed parameters | `tests/release-successor-execution/run.py:192,198` | claim request-digest mismatch refuses |
| Aggregate quota | `tests/new-sdd-workspace-successor/run.py:2129` (floor 4499/4500) | summed-floor admission refusals |
| Destination atomicity | none | read-only probe of tag, package and asset duplicate behaviour in an isolated repository before any `keyed` row |

### What this decision does not establish

No implementation, no workflow or registry change, no claim namespace or protection, no Authority/API
mutation, no publication, release, installation or consumer adoption. It does not show that destination
duplicate-rejection holds (listed as to-be-qualified), grants no effect authority, relaxes no live-main,
candidate, revocation or journal guard, and measures no economics or savings. The 423-entry figure is one
run. The hosted-runner and Authority-limit values are unread. ADR-0097 remains the governing rule.

## Open questions for Root

1. Does the 24 October decision stand as final (withdraw this), or is a keyed profile still wanted?
2. Is N = 2 the intended hard cap, and should the 4200-profile route stay `global` permanently?
3. What are the live Authority `rate_limit.core.limit` and the plan's hosted concurrent-job limit? Neither is in source.
4. Who admits the claim-namespace protection change in the Authority repository, and the re-pins of pinned publisher files?
5. Is wiring `BoundIntentFence` into the production publishers acceptable as part of this Delivery (it is currently unused)?

## Alternatives considered

- **GitHub `concurrency` group per resource.** Repository-scoped, and its single-pending-run rule silently
  replaces a queued run (ADR-0097); it cannot fence a stale writer. Kept only as same-tag de-duplication.
- **Journal CAS alone.** Established insufficient: a fresh journal identity shares no head with the writer it must fence.
- **Tag-creation atomicity alone.** Fences the tag, not packages, draft creation or promote.
- **Heartbeat lease renewal.** Adds Authority traffic every interval for no safety the job-bound lease plus liveness check lacks.
- **Central publishing workflow or queue.** Rejected in ADR-0097 (credential and Trusted Publishing binding).
- **Keep only ADR-0097.** Safe and in force; costs the serialization #4753 set out to relax.
