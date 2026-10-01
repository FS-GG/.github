# LEARN-01.2 observation and issue-analysis contract

LEARN-01.2 adds nine typed facts to the existing telemetry ingest schema. They use the existing
64 KiB event and batch limits, canonical digest, durable receipt, replay, SQLite writer, and private
dashboard snapshot. No execution intent is copied into a separate journal.

| Fact kind | Required identity |
| --- | --- |
| `learn-task-snapshot` | Item, snapshot and rubric IDs, a SHA-256 snapshot digest, and capture time |
| `learn-context-manifest` | Item, recipe and manifest IDs, plus a SHA-256 digest for each |
| `learn-experiment-assignment` | Item, policy and window IDs, `current` or `focused`, assignment time, and an optional bounded deviation |
| `learn-accounting-inventory/1` | Original item, policy/window, prospective capture and whole-item cutoff times, explicit CI applicability, exact expected dispatch and shared-cost rosters, and source digest |
| `runtime-native-inventory/1` | Invocation/original item, exact paged native-turn roster, provider/profile, affirmative support, follow-up baseline, and source digest |
| `runtime-native-inventory-source/1` | Separate immutable producer binding for the inventory's root/invocation, native parent/thread, ordered turn roster, revision and capture time, associated with the inventory source digest |
| `learn-shared-cost/1` | Stable native cost identity, provider total, exact integer allocation by original item, and source digest |
| `learn-shared-cost-allocation/1` | Stable native cost identity, policy/window, pre-assignment freeze time, sorted allocation roster and fixed rule |
| `learn-shared-cost-authority/1` | Stable native cost identity and an exact later reference to the retained native inventory source |

Each fact identity is stable in its declared item/invocation scope. Exact receipt replay is idempotent. All nine learning facts are immutable
after persistence: a changed higher revision is refused for task snapshots, context manifests, assignments,
inventories, and shared costs. A changed snapshot, manifest, roster, or allocation requires a later identity and
versioned prospective contract/window; the current store does not claim correction semantics for these facts.
The facts remain in `ingest_facts`. Schema 11 adds only `learning_fact_order`, whose explicit autoincrement sequence
proves prospective ordering without depending on mutable SQLite row IDs. Upgrade does not backfill historical facts;
their ordering remains unknown rather than inferred. The bounded private dashboard read exposes them as
`learningObservations`. Schema 12 adds protected receipt authority and first-fact admission tables. It does not
backfill authority for existing facts, while schema-11 order survives the upgrade. New admissions retain the
authenticated producer, stream, role, grant identifier and generation, receiver receipt key, and original envelope
digest. The containing item-detail/2 envelope carries the negotiated
`fsgg.telemetry.learn-item-detail/4` marker inside its hashed private bytes. The marker and snapshot hash protect
reproducibility and integrity; they do not authenticate who acquired the snapshot.
Public projections remain
unchanged: learning-only item identities are excluded from public enumeration and learning facts do not
contribute to the public `factCount`.

`tools/learn-01-analysis.py <contract> --observations <private-item-detail>` verifies the retained workspace and
canonical snapshot revision, then derives assignment, expected-invocation coverage and provider totals solely
from that immutable private snapshot. It refuses a second corpus input on this route. The synthetic source
fixture remains a separate contract test. Exact duplicates and input ordering do not change the observation
digest. Changed revisions of every Package A learning fact are refused consistently by ingest and analysis.
Children, retries, reviews, rescues, and repairs remain costs of the canonical original item.
Provider mismatch, unsupported usage, missing child usage, incomplete CI, and open invocations keep the affected
issue incomplete. Missing provider/profile/support evidence never defaults to complete. Late facts become visible
only on a new analysis of a new immutable input snapshot.

An old item-detail/2 snapshot without the negotiated v3 marker remains explicitly incomplete. A marked v3
snapshot validates structural closure only when the typed accounting roster exactly matches expected dispatches and shared costs,
every dispatch binds within one original item to an admitted terminal invocation, every paged native roster is
complete and exactly matches observed native turns, provider/profile/support provenance agrees, follow-up baseline
agrees, explicit CI applicability is reconciled with complete CI coverage, and shared allocations sum exactly to
their native provider totals. Empty CI arrays cannot establish completeness when CI is applicable. Inventory
capture times must be ordered around assignment and cutoff, and all-zero source digests are refused. Caller-added completion flags, absence
of a gap, or observed usage alone are never authority. Inventory and shared-cost identities are immutable; a
reopen is a prospectively rostered new dispatch/invocation rather than a correction of settled evidence.

Schema 10 originally retained each fact's canonical bytes without a binding to independently captured source
bytes and producer identity. The roadmap native collector now retains a canonical producer-owned
binding beside the exact App Server and rollout bytes. Those bound bytes name the producer, root invocation,
invocation, native parent/thread, ordered turn roster and immutable inventory revision; the roadmap adapter
recomputes their digest and checks them against durable dispatch state. It publishes the inventory and a distinct
`runtime-native-inventory-source/1` candidate authority in one applied batch. The store validates canonical binding
bytes and digest, keeps the fact immutable, includes it only in the bounded private read, and excludes it from
public fact counts. Analysis joins it to exact inventory revision/source digest, root lineage, native thread and
ordered turn suffix after the declared follow-up baseline. Missing authority remains visibly incomplete; mismatch
or substitution refuses the analysis. Embedded producer labels and self-computed hashes still do not authenticate
the collector.

Host configuration v2 binds each credential to a typed `generic` or `native-collector` role and a versioned grant.
Version-1 credentials remain generic. The role is loaded only from the protected host configuration: callers cannot
select it on the HTTP request or enrollment command. A receiver-owned `fsgg.telemetry.receipt-admission/1` artifact
wraps the original canonical envelope with the authenticated non-secret principal before acknowledgement. Receipt
index recovery persists that provenance atomically with the transport obligation. First fact admission is then
persisted in the same transaction as the fact. An exact replay, later credential change, or higher-revision mutable
fact cannot upgrade that first provenance. A `native-collector` batch is restricted to native inventory/source and
shared-cost/authority observations; it cannot submit assignments or general telemetry. Revocation and the existing
scope checks remain at the protected credential boundary. Schema-11 pending artifacts migrate as generic, and
historical/direct-ingest facts retain unknown provenance.

Exact receipt retries remain bound to the receiver-retained role and grant. The Host may use a scope-only read to
find the receipt identity, but it re-enters the principal-aware store path before acknowledging matching bytes; a
later credential with the same scope and a different role, grant identifier or generation is refused. Host
configuration also refuses two credentials that assign incompatible authority to one scope, even when their secret
files differ.

Shared allocation uses one fixed rule, `equal-largest-remainder-v1`. Its sorted original-item roster, policy,
window and freeze time are immutable and must agree with every member's prospective accounting inventory and
assignment. The store accepts it only while every roster member remains unassigned, and the bounded read carries
its durable sequence. Analysis requires that sequence to precede every assignment; a backdated later fact cannot
qualify. Exact replay retains the original sequence. Failed ingest, migration of historical facts and missing order
remain unknown. The rule divides the retained provider total equally;
integer remainders go to the lexically first roster members. This makes the allocation reproducible without a
post-outcome choice.

After execution, the separate authority names one existing native invocation and inventory rather than supplying
another total. Its durable order must follow the native source and precede the cost fact. Analysis validates that
invocation's validated `runtime-native-inventory-source/1`, exact source digest, provider and observed turn total.
It propagates incomplete native evidence to every allocated recipient and proves the proposed allocation would
consume one native invocation at most once. One native invocation can appear in only one shared-cost candidate.
Self-consistent caller hashes, an asserted total, a late or foreign roster, a second allocation of the invocation,
or a binding without independently authenticated producer provenance cannot qualify. Missing or generic provenance
reports `collector-principal-unavailable`. Matching protected collector grants on the native-source and authority
facts pass that provenance check. Imported snapshots and legacy captures remain visibly incomplete as
`independent-shared-cost-authority-unavailable`, `native-source-verification-unavailable`, and
`snapshot-origin-unverified`. Those reasons distinguish retained credential provenance from the still-missing
native collector/custody verification and trusted snapshot acquisition. Mismatched evidence refuses the analysis.
An invocation without a validated source candidate similarly reports `independent-inventory-source-unavailable`.

The private snapshot counts learning rows before it emits `selection.complete`. More than 10,000 matching rows are
refused rather than truncated or described as complete.

The protected host command accepts only dispatch and native thread selectors. It resolves admitted child lineage,
original item and requested profile from the durable store, loads its executable, Codex home, evidence root and
restricted principal from a private host-owned installation file, clears the inherited process environment, and
recomputes the native roster, counters and binding with the existing native reader. It atomically retains the
canonical two-fact envelope before submission so an interrupted retry reuses the original bytes and receiver-owned
principal admission. Callers cannot provide inventory, totals, rollout path, source binding, executable, source
root or credential. Missing or ambiguous lineage, a native-agent mismatch and a protected/durable profile mismatch
refuse before invoking or admitting a new candidate.

The installation-v1 command is a refusal-safe boundary, not qualified capture custody. Its result keeps source verification,
snapshot origin and shared-cost completeness `unknown`; imported analysis therefore continues to report
`native-source-verification-unavailable`, `snapshot-origin-unverified`, and
`independent-shared-cost-authority-unavailable`. The synthetic fixture proves source behavior only. Adapter
adoption, installed configuration and credential, trusted capture custody, and trusted snapshot acquisition remain
later operational gates. Live collection and any efficiency conclusion remain pending. Current native usage is
`not-configured`, so no live token total is claimed.

The roadmap adapter adopts that boundary only for a terminal child with durable dispatch, parent-thread and
native-agent selectors. `FSGG_TELEMETRY_NATIVE_COLLECTOR_CONFIG` may select an absolute private `0600` Host
configuration; the adapter passes that path and the three selectors to the fixed `fsgg-telemetry-host
collect-native` command. It passes no credential, role, grant, inventory, total, executable, source root or source
binding. A missing selection, unsafe file, old Host, refusal, timeout or malformed result retains the existing
generic/local reconciliation and remains `native-collaboration-usage-unknown`. A strictly validated protected
success suppresses the adapter's generic inventory/source candidates while generic turn observations remain
unqualified. The adapter accepts only the Host's closed UNKNOWN result and cannot promote collector authority.

## Protected same-host capture and analysis

Installation schema `fsgg.telemetry.native-collector-installation/2` adds exactly one field,
`ExecutableSha256`, to the existing eight-field installation shape. The Host checks the actual executable
against that operator-provisioned pin. The host configuration's parent must be private, and `CodexHome` and
`EvidenceRoot` must lie beneath it through private, nonsymlink, host-owned directories. The configuration
and installation belong to that host user; the executable belongs to that user or root. Version 1 remains accepted with
its prior UNKNOWN meaning. Neither a producer-supplied executable nor inherited PATH/CODEX_HOME selects
the qualified reader.

The trust boundary is the protected host operator and installed collector versus an ordinary enrolled
producer. This is protected host observation of the native provider client; it is not a provider-signed
receipt, isolation from a malicious host owner, or authorization for untrusted local workers to write the
native source. An installation where ordinary producers can modify the native root is ineligible.

For v2, collection retains one private immutable `fsgg.telemetry.protected-native-capture/2` record before
submission. It contains the exact envelope, independently read per-turn counters, exact bounded App Server
request/response bytes, selected rollout usage-record bytes, grant generation and installation digest. Replay
and export recompute the length-framed source digest, compare the source binding with the admitted immutable
fact, and derive each final turn total from the retained rollout records. Source-byte, digest, roster or counter
substitution therefore refuses before the snapshot leaves Host custody. Crash retry reuses that record without
invoking the reader again. No conversation content or credential enters it. Older captures without the retained
source bytes remain unqualified; changing their label cannot promote them.
Changing the pin, installation or grant does not retroactively qualify old records: stale bindings refuse,
and the operator must retain the original qualified installation/custody or select a prospective evidence
root and collection identity. No database migration or history rewrite is introduced by this window.

The protected Host's `export-learning --config ABSOLUTE_PATH` acquires its service lock, reads its existing
bounded private snapshot and exports retained captures from the configured evidence root. There are at most
1,000 captures, each at most 2 MiB and cumulatively at most 3 MiB, and the complete process response is bounded to 4 MiB. The export contains
non-secret receipt/grant references and counters; configuration, executable, credential and source-root paths
are omitted. It does not submit facts or grant authority to an imported snapshot.

The analyzer's explicit operator route is:

```text
python3 tools/learn-01-analysis.py policy/learn-01-current-focused-v1.json \
  --protected-host-executable /absolute/installed/fsgg-telemetry-host \
  --protected-host-sha256 <operator-verified-executable-sha256> \
  --protected-host-config /absolute/private/host.json
```

The executable/config selectors come from the protected operator's installation, independently of any
snapshot. This route verifies executable custody and the pin, launches it directly with a sanitized
environment, bounds output and deadline, and refuses `--observations` or a second corpus. Direct process
acquisition establishes snapshot origin under the same-host trust boundary. Portable JSON and its SHA-256
establish byte consistency only; copying a capture or trusted-looking field into imported JSON cannot select
this route. No new signing subsystem or global trust registry is introduced.

Before admitting a shared source, analysis joins the captured source event to its exact first authenticated
receipt, protected principal/grant and envelope digest, and matches every native turn identity, sequence,
provider/profile and total to the snapshot usage. The existing prospective allocation and durable ordering
checks still apply. A successful join subtracts that invocation once from its recording original and adds
the frozen allocations to the recipients. A source invocation cannot qualify two shared costs. A generic
substitution refuses; absent/mismatched capture counters remain incomplete.

Focused source fixtures exercise actual controlled-reader invocation, retained capture/export, crash-style
retry, executable-pin mismatch, a direct-process analyzer route, and a two-item 100-token allocation yielding
50 and 100 final tokens after the second item's separate 50-token cost. Role substitution, counter
substitution and imported self-hashed snapshots cannot reproduce the qualified result. These source fixtures
are not installed receiver, provider-signature or live experiment evidence. The current Coordination
installation-v1 preparation remains UNKNOWN; explicit v2 installer adoption, published artifacts, protected
host configuration and native-root custody are required before a live qualified window.

## Bounded pre-admission owner assessment

The analyzer also exposes a source-preparation mode on the same protected process acquisition route:

```text
python3 tools/learn-01-analysis.py policy/learn-01-current-focused-v1.json \
  --protected-host-executable /absolute/installed/fsgg-telemetry-host \
  --protected-host-sha256 <operator-verified-executable-sha256> \
  --protected-host-config /absolute/private/host.json \
  --assess-pre-admission --original-item <original> --window <window> \
  --repository <owner/repository>
```

The three final values are lookup selectors. The caller cannot submit readiness, an evidence hash, counters,
rosters, authority, observation time, expiry, revocation, capability or plan attestation. Imported observations
and a second corpus remain refused. The mode requires the existing complete v4 private snapshot, receipt
provenance and protected captures from `export-learning`; the closed export v1 schema is unchanged.
An original can be reused across repositories or windows. A retained record with an explicit foreign repository
or window is excluded from positive evidence for the requested scope. When the retained record has no repository
or window field, the assessment reports its scope as unknown rather than treating the lookup selector as proof
that the record belongs to that scope. A foreign retained assignment remains visible as an assignment conflict.

The result is `fsgg.learn.pre-admission-owner-assessment/1`, not a
`LearningOperationalReadinessSnapshot`. It always keeps `operationalReady` false in this source window and
represents complete native usage support as `unknown`, never as an inferred true value. The assessment records
whether the selected original is unassigned, assigned in the selected window or conflicts with an assignment
in another window. An unassigned original does not need future invocations, terminal outcomes or token counters.
Empty dispatch, shared-cost or observation arrays do not prove prospective completeness.

`evidenceDigest` is the canonical digest of the selectors and the actual retained records selected for the
original: exact learning canonical bytes and receipt bindings, related population/outcome/dispatch/lineage/usage
rows and relevant protected captures. It excludes the changing whole-snapshot revision, export time and rows for
unrelated originals. Repeated acquisition of the same selected records therefore preserves the digest; a change
to a selected record changes it. Selection truncation, conflicting retained identities, executable-pin drift,
capture/first-admission grant mismatch and imported self-hashed JSON refuse rather than degrade into readiness.
Non-object canonical JSON, malformed relation containers or rows and malformed capture events also refuse with a
typed result. Identifier and scope fields used by selection must be non-empty strings when present; malformed
nested rosters and allocations refuse before lookup. Verified protected captures remain post-outcome evidence
only; existing capture records do not
retain repository/window scope, so their reconciliation is reported as `scope-unknown`, not as selected-scope
readiness evidence.

The assessment names these missing owner inputs independently:

| Missing input | Existing owner that must supply or bind it |
| --- | --- |
| Protected operation/window authority, including bounded epoch, expiry and revocation | The `.github` policy/telemetry owner must identify a genuine protected authority record and its custody; the frozen research policy and original-item mapping registry do not supply it. |
| Independent prospective original/descendant dispatch census | The existing `.github` roadmap/routine telemetry producer must add or identify its independently retained census source. `tools/routine-observer.py` currently reports that collector as absent. |
| Prospective shared-cost membership and allocation frozen before assignment | The existing accounting/shared-cost producer must emit the already-defined allocation record from independently retained membership. A generic or self-authored fact is only a candidate record. |
| Root and descendant native-counter capability certification for each selected execution route | Main/SystemAdmin's installed provider, native-root custody and adopter owners must bind qualified route capability before admission. Post-outcome inventory and counters cannot certify unborn descendants prospectively. |
| Exact native delivery identity and source provenance for original, repository, head and merge | The existing routine-delivery/CI producer must retain and export that exact binding. A repository-labelled outcome row alone is insufficient. |

Coordinator-validated plan and WorkItem authority remain Coordination inputs at the later consumer boundary and
are not duplicated in telemetry. No independent window authority, prospective census/allocation emitter or
route-capability certification was found in the existing retained export, so this assessment does not make the
production Host operationally ready or close LEARN-01.4.

## Protected native delivery source readback

The optional private sidecar `host.json.native-delivery-source.json` installs one bounded source adapter. Its
closed `fsgg.telemetry.native-delivery-source-installation/1` shape names the existing native-collector credential,
an absolute private GitHub credential file and at most 64 allowed `owner/repository` values. It is valid only with
the qualified native collector installation and the same protected credential and grant. There is no implicit
token, inherited CLI endpoint or caller-provided URL.

`collect-native-delivery --config ABSOLUTE_PATH --source-ref RETAINED_SOURCE_REF` resolves exactly one existing
immutable `native-item-outcome` by its retained source reference. The resolver compares the projection to the
original canonical fact and binds its digest and first receiver provenance. An absent, ambiguous or changed
projection refuses. The command then performs exactly one unconditional GET to the fixed GitHub API pull-request
path for that fact's repository and PR. It accepts only the same PR number, repository and expected head and
retains the exact response bytes, digest, base identity and open, closed-unmerged or merged state. The caller
cannot provide response bytes, repository, PR, head, merge state, observation time or a readiness claim.
GitHub may return a speculative test-merge SHA in `merge_commit_sha` while an open or closed PR has
`merged=false`. That raw SHA remains only inside the retained response bytes. It is validated when present but
never becomes protected merge identity; `mergeCommit` and `mergedAt` remain null until GitHub reports
`merged=true`. A non-null `merged_at` on an unmerged response refuses.

Before receipt submission, the Host atomically writes one
`fsgg.telemetry.protected-native-delivery-capture/1` record under the existing private evidence root. Its identity
derives from the exact candidate binding. Retry reuses those bytes without another GitHub read or a new observed
time. Changed installation, token file, grant, candidate bytes, response bytes or envelope binding refuses the old
capture. A later primary state therefore needs a new immutable candidate/source reference and produces a distinct
history record; it cannot overwrite a failed state or upgrade a generic first admission.

The new `learn-native-delivery-source/1` learning fact is admitted only through the existing native-collector
role. It records the candidate binding, exact primary response digest and derived native state while fixing
`originalWindowBinding` to `unverified`. GitHub's pull-request API establishes repository, PR, head, base and merge
state only. It does not establish the experiment's original/window association, prospective census, allocation,
native-counter capability or operational authority.

The closed default `export-learning` response remains
`fsgg.telemetry.protected-learning-export/1`; it continues to emit the same three members and can carry the new
learning fact in its existing snapshot without exposing delivery capture bytes. Explicit
`--include-native-delivery` negotiation emits `fsgg.telemetry.protected-learning-export/2` with bounded
`deliveryCaptures`. Export revalidates current installation, grant, exact candidate binding, source digest and
envelope before releasing each capture. The analyzer requests v2 only with `--include-native-delivery`, joins the
capture to the exact first admitted source fact and reports
`native-state-verified/original-window-binding-unverified`. Imported self-hashed records, stale grants, source or
candidate drift and malformed response bytes refuse. This evidence removes only the missing native delivery state
and provenance input; `operationalReady` remains false because independent original/window binding and the other
owner inputs above are still absent.

## Installed origin and native route producer contract

The protected Host now exposes two additional read surfaces for the later Coordination composition. They require
the existing qualified native collector installation and enrolled `native-collector` principal.

`collect-installed-origin --config ABSOLUTE_PATH` reads the installed manager receipt and fixed capability
profile, result, native capture, verification and source reference from their private retained paths. It compares
their scope, grant generation, executable bytes, profile digest, requested model and effort, capture digest,
verification status and observation interval before constructing `learn-installed-origin/1`. The caller supplies
no hashes or attestation fields. The immutable fact enters through the existing receipt and fact admission tables;
generic admission is rejected. `read-installed-origin` repeats the retained byte checks and selects the exact
applied fact through its current workspace, principal, grant and receipt admission. Its
`fsgg.learn.installed-producer-receipt/1` result maps directly to Coordination's existing producer receipt
contract. Changed retained bytes, a foreign installation or grant, substituted selectors, a revoked current
grant, absent admission, or duplicate matching facts refuse.

`read-native-route --config ABSOLUTE_PATH --original-item ID` is a bounded, read-only store snapshot for an
existing durable original mapping. It returns at most 256 dispatch rows and 256 admitted learning facts from one
SQLite transaction and caps canonical output at 1 MiB. Every runtime/relation outside the proved
`collaboration-spawn-agent` child route is explicitly `unsupported-runtime-or-role`. Started and terminal child
states remain observations of that route only: the result fixes `populationComplete` and
`terminalChildEstablishesRoleCoverage` to false. It also fixes `windowBinding` to `unknown` and
`allocationReferenceIsAuthority` to false. Coordination must join this output with its C1 assignment/session
owners and C3 operational window authority. A six-role roster, terminal child, allocation roster reference, or
applied collector receipt cannot establish complete population, window authority, actual allocation, or source
verification.
