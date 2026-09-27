# LEARN-01.2 observation and issue-analysis contract

LEARN-01.2 adds six typed facts to the existing telemetry ingest schema. They use the existing
64 KiB event and batch limits, canonical digest, durable receipt, replay, SQLite writer, and private
dashboard snapshot. No execution intent is copied into a separate journal.

| Fact kind | Required identity |
| --- | --- |
| `learn-task-snapshot` | Item, snapshot and rubric IDs, a SHA-256 snapshot digest, and capture time |
| `learn-context-manifest` | Item, recipe and manifest IDs, plus a SHA-256 digest for each |
| `learn-experiment-assignment` | Item, policy and window IDs, `current` or `focused`, assignment time, and an optional bounded deviation |
| `learn-accounting-inventory/1` | Original item, policy/window, prospective capture and whole-item cutoff times, explicit CI applicability, exact expected dispatch and shared-cost rosters, and source digest |
| `runtime-native-inventory/1` | Invocation/original item, exact paged native-turn roster, provider/profile, affirmative support, follow-up baseline, and source digest |
| `learn-shared-cost/1` | Stable native cost identity, provider total, exact integer allocation by original item, and source digest |

Each kind is unique per original item. Exact receipt replay is idempotent. All six learning facts are immutable
after persistence: a changed higher revision is refused for task snapshots, context manifests, assignments,
inventories, and shared costs. A changed snapshot, manifest, roster, or allocation requires a later identity and
versioned prospective contract/window; the current store does not claim correction semantics for these facts.
The facts remain in `ingest_facts`, preserving schema-10 stores without migration,
and the bounded private dashboard read exposes them as `learningObservations`. The containing item-detail/2
envelope carries the negotiated `fsgg.telemetry.learn-item-detail/3` marker inside its hashed private bytes.
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

Schema 10 retains each fact's canonical bytes, but it does not retain a binding from those bytes to independently
captured source bytes and producer identity. The same caller can currently submit the outcomes, inventories and
their claimed source digests in one receipt. Shared allocation facts likewise have no independently retained
expected authority. Consequently every v3 issue remains `tokenComparisonQualified: false` with explicit
`independent-inventory-source-unavailable` and `independent-shared-cost-authority-unavailable` reasons. The report
may describe structurally validated observations, but it does not publish them as complete token totals. A future
producer contract must retain the independent bytes, producer identity and capture ordering before qualification
can become true; labels or self-hashed caller bytes are insufficient.

The private snapshot counts learning rows before it emits `selection.complete`. More than 10,000 matching rows are
refused rather than truncated or described as complete.

The synthetic fixture proves source behavior only. Telemetry configuration, publication, installation, live
collection, and any efficiency conclusion remain pending. Current native usage is `not-configured`, so no live
token total is claimed.
