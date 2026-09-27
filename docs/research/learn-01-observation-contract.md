# LEARN-01.2 observation and issue-analysis contract

LEARN-01.2 adds three pre-dispatch facts to the existing telemetry ingest schema. They use the existing
64 KiB event and batch limits, canonical digest, durable receipt, replay, SQLite writer, and private
dashboard snapshot. No execution intent is copied into a separate journal.

| Fact kind | Required identity |
| --- | --- |
| `learn-task-snapshot` | Item, snapshot and rubric IDs, a SHA-256 snapshot digest, and capture time |
| `learn-context-manifest` | Item, recipe and manifest IDs, plus a SHA-256 digest for each |
| `learn-experiment-assignment` | Item, policy and window IDs, `current` or `focused`, assignment time, and an optional bounded deviation |

Each kind is unique per original item. Exact receipt replay is idempotent. Assignments cannot be revised or
redrawn after persistence. The facts remain in `ingest_facts`, preserving schema-10 stores without migration,
and the bounded private dashboard read exposes them as `learningObservations`. Public projections remain
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

Schema 10 does not contain affirmative closure facts for the expected dispatch population, expected native turns,
expected shared costs, expected provider or supported native join. Private-snapshot analysis therefore reports
those coverage dimensions incomplete and cannot qualify a token comparison. Completing that boundary requires a
separately versioned producer contract; absence of a gap, observed usage, or caller-added fields is not closure.

The synthetic fixture proves source behavior only. Telemetry configuration, publication, installation, live
collection, and any efficiency conclusion remain pending. Current native usage is `not-configured`, so no live
token total is claimed.
