# LEARN-01.2 observation and issue-analysis contract

LEARN-01.2 adds six typed facts to the existing telemetry ingest schema. They use the existing
64 KiB event and batch limits, canonical digest, durable receipt, replay, SQLite writer, and private
dashboard snapshot. No execution intent is copied into a separate journal.

| Fact kind | Required identity |
| --- | --- |
| `learn-task-snapshot` | Item, snapshot and rubric IDs, a SHA-256 snapshot digest, and capture time |
| `learn-context-manifest` | Item, recipe and manifest IDs, plus a SHA-256 digest for each |
| `learn-experiment-assignment` | Item, policy and window IDs, `current` or `focused`, assignment time, and an optional bounded deviation |
| `learn-accounting-inventory/1` | Original item, policy/window, whole-item cutoff, exact expected dispatch and shared-cost rosters, and independent source digest |
| `runtime-native-inventory/1` | Invocation/original item, exact paged native-turn roster, provider/profile, affirmative support, follow-up baseline, and source digest |
| `learn-shared-cost/1` | Stable native cost identity, provider total, exact integer allocation by original item, and source digest |

Each kind is unique per original item. Exact receipt replay is idempotent. Assignments cannot be revised or
redrawn after persistence. The facts remain in `ingest_facts`, preserving schema-10 stores without migration,
and the bounded private dashboard read exposes them as `learningObservations`. The containing item-detail/2
envelope carries the negotiated `fsgg.telemetry.learn-item-detail/3` marker inside its hashed private bytes.
Public projections remain
unchanged: learning-only item identities are excluded from public enumeration and learning facts do not
contribute to the public `factCount`.

`tools/learn-01-analysis.py <contract> --observations <private-item-detail>` verifies the retained workspace and
canonical snapshot revision, then derives assignment, expected-invocation coverage and provider totals solely
from that immutable private snapshot. It refuses a second corpus input on this route. The synthetic source
fixture remains a separate contract test. Exact duplicates and input ordering do not change the observation
digest. A later non-assignment correction supersedes an earlier revision; assignment corrections are refused.
Children, retries, reviews, rescues, and repairs remain costs of the canonical original item.
Provider mismatch, unsupported usage, missing child usage, incomplete CI, and open invocations keep the affected
issue incomplete. Missing provider/profile/support evidence never defaults to complete. Late facts become visible
only on a new analysis of a new immutable input snapshot.

An old item-detail/2 snapshot without the negotiated v3 marker remains explicitly incomplete. A marked v3
snapshot qualifies only when the typed accounting roster exactly matches expected dispatches and shared costs,
every dispatch binds within one original item to an admitted terminal invocation, every paged native roster is
complete and exactly matches observed native turns, provider/profile/support provenance agrees, follow-up baseline
agrees, and shared allocations sum exactly to their native provider totals. Caller-added completion flags, absence
of a gap, or observed usage alone are never authority. Inventory and shared-cost identities are immutable; a
reopen is a prospectively rostered new dispatch/invocation rather than a correction of settled evidence.

The synthetic fixture proves source behavior only. Telemetry configuration, publication, installation, live
collection, and any efficiency conclusion remain pending. Current native usage is `not-configured`, so no live
token total is claimed.
