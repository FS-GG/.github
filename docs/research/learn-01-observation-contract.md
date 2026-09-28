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
facts pass that provenance check, but shared costs remain visibly incomplete as
`independent-shared-cost-authority-unavailable`, `native-source-verification-unavailable`, and
`snapshot-origin-unverified`. Those reasons distinguish retained credential provenance from the still-missing
native collector/custody verification and trusted snapshot acquisition. Mismatched evidence refuses the analysis.
An invocation without a validated source candidate similarly reports `independent-inventory-source-unavailable`.

The private snapshot counts learning rows before it emits `selection.complete`. More than 10,000 matching rows are
refused rather than truncated or described as complete.

The synthetic fixture proves source behavior only. The protected native collector command and custody installation,
adapter adoption, published version/configuration and credential, and trusted snapshot acquisition remain later
source or operational gates. Live collection and any efficiency conclusion remain pending. Current native usage is
`not-configured`, so no live token total is claimed.
