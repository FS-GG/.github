# ADR-0095: Enforced UTEL resource containment for fresh profiles

- **Status:** Accepted policy; inherited-hold fencing amended by [ADR-0096](0096-proportionate-verification-and-unrecoverable-holds.md); implementation, production budgets and admission pending
- **Date:** 2026-10-16
- **Affects:** `.github`, Coordination and SystemAdmin UTEL integration
- **Decision owner:** Programme
- **Amends:** [ADR-0093](0093-original-103-authentication-domain-policy.md), resource acceptance for explicitly selected fresh profiles only; the original `.103` policy remains unchanged.

## Context

The [original policy](0093-original-103-authentication-domain-policy.md) requires a
complete simultaneous physical-copy bound under 65,536 bytes. Existing encoded models,
[copy observations](../../tests/release-successor-authentication/joint-copy-ledger-contract.md)
and [closed source construction](../../tests/release-successor-authentication/closed-source-contract.md)
provide useful source evidence, but do not prove that bound or qualify a production
provider. Raising the cap without closing the population would not supply the proof.

The future acceptance objective is controlled resource use with safe overload behavior.
This differs from proving a strict global physical-copy maximum. For example,
[Linux documents](https://www.kernel.org/doc/html/latest/admin-guide/cgroup-v2.html)
that its memory limit can be exceeded temporarily. Enforcement must be qualified within
its actual platform semantics.

## Decision

Select enforced resource containment for a new immutable UTEL profile and a separately
authorized fresh original. The initial scope is one operational construction and one
retained original. A larger concurrent or retained population requires its own finite
multiplicity budget and reviewed profile change. This decision promises neither
availability under resource pressure nor a universal physical-copy or erasure guarantee.

Before admission, the selected profile must bind exact protected source, delivered
artifacts, runtime and configuration to finite resource limits at every controlled
execution, storage, transport and recipient boundary. Each limit names its owner,
metric/unit, enforcement mechanism, multiplicity, lifetime, independent readback,
overload action and coverage exclusions. Missing or unlimited required limits refuse
admission. Actual production limits remain unset until the integrated implementation
has been measured and qualified.

| Boundary | Required acceptance scope |
| --- | --- |
| Execution and authorities | Memory including runtime overhead, swap, tasks/threads, descriptors, CPU and wall lifetime; descendants and helpers remain inside the selected boundary. Authority principals remain separate. |
| Storage | All writable paths; finite allocated storage and inode/file population; reservation/data/replacement/uncertainty/observation overlap and the selected mechanism's metadata coverage. Required facts cannot be evicted or truncated to fit. |
| Transport and peers | Finite endpoints, queues, framing and request concurrency; each remote service has its own admitted envelope. Shared infrastructure outside local control is an explicit dependency, not a counted local copy. |
| Diagnostics and observations | Enrolled owned recipients, cumulative lifetime reservations, bounded status/log output and retention. No general raw exporter, payload dump or caller-selected destination. |

The existing eight-domain inventory remains useful to identify coverage and exclusions.
Resource limits must not populate physical-copy metrics or imply that the original
upper-bound registry has accepted a proof. Host/platform caches, remote copies,
backups and physical-media remnants are not covered by an unstated global guarantee.
The proposed fresh execution profile disables swap and payload crash dumps; any
exception requires explicit finite coverage and review.

Suppliers propose numeric budgets from the actual integrated success, maximum-valid-input,
stress and fault journeys, including runtime baseline and finite uncertainty overlap.
Programme accepts the budgets and justified headroom in the exact profile. A measured
peak is not a universal maximum. No fixed percentage is mandated, and a source-test
resource envelope is not a production or 90-day retention budget. Overload tests must
show refusal or consumed Unknown; normal supported journeys must complete within the
selected envelope. No run may silently enlarge its limits.

The current application framing and lifetime limits remain governed by the
[closed source contract](../../tests/release-successor-authentication/closed-source-contract.md)
and [wire contract](../../tests/release-successor-authentication/wire-contract.json).
Retain one outstanding callback response, thirteen exchanges, six phase ordinals and
two diagnostic roles with three original-lifetime purposes each. Reservation precedes
possible mutation or extraction. Loss never refunds an attempt or permits replacement
recipients, gates or originals. Current source diagnostics allow four controlled
pre-retention-expiry deliveries and refuse both expiry purposes while custody is unknown.

Resource exhaustion, uncertain writes, lost replies, termination and host loss preserve
first failure, append-once observations and the original-effect hold. No automatic
operational restart follows uncertainty. GC, cancellation, scope exit or task completion
proves neither physical release nor permission to reuse an unresolved reservation.
Qualified ownership and resource readback are required before capacity reuse.

Retain the 270/285/300-second original-clock windows and authenticated finite uncertainty.
Effects require the latest possible original time to precede the applicable cutoff;
restart, rollback, boot mismatch, unknown uncertainty or queue delay cannot renew it.
Diagnostics after 300 seconds remain observational. Retained-evidence eligibility ends
90 days after the original total cutoff, without read-based renewal. Uncertain expiry
refuses extraction; uncertain cleanup preserves a bounded held allocation rather than
proving deletion or renewing eligibility. The owning decision retains the effect hold
after data expiry. This is not a promise of global physical erasure.

[ADR-0094](0094-separate-v3-and-utel-principals-and-custody.md) continues to govern
purpose-separated principals, keys, custody and independent enrollment. Exact delivered
authority code requires independent review. An effect-enabling ACK still requires the
original-bound descriptor/frame/checkpoint/ordinal/generation/clock join, durable barrier,
fresh exact readback, one-use reservation and complete delivery confirmation. Diagnostic
status, synthetic receipts, TLS and resource telemetry cannot substitute for that proof.
The original custodian's same-token reserved DELETE and independently observed HTTP204
remain necessary for normal revocation.

## Acceptance and migration

Source acceptance requires one actual maintained parent→Python callback owner→typed
sink→owned provider→readback→checked receipt journey and its diagnostic reservation path,
with coherent producer/consumer pins. Public source controls remain distinct from private
provider integration; public CI must not require private source or credentials. Required
fault controls cover identity and retained-evidence substitution, every reservation/write/
barrier/readback/return cut, pressure and quota failure, lost ownership, concurrent or
repeated attempts, clock boundaries, expiry and alternate egress.

Runtime acceptance additionally requires independently read-back enforcement on the exact
delivered configuration, qualified persistence/fencing/recovery and authenticated clocks,
recipient containment, custody and enrollment. Reviewers must be independent of the
implementation. Tests of synthetic ports or injected faults do not establish native
media durability, installed authority or availability under host loss.

Migration requires a fresh profile identity and separately authorized original/journal.
Legacy 65,536-byte accounting remains operative and unproved for its original profile;
physical upper bound stays null, fit unproved and admission false. Existing `.103`
remains attempted-unknown and unreplayed, all 439 inherited reservations survive, and
`.104` remains held under its separate release gates. No profile selection can reset,
reinterpret or supersede those effects. This decision admits no credentials, deployment,
workflow dispatch, native execution or release operation.

## Consequences and alternatives

The new profile can establish bounded resource operation with tested safe failure within
explicit platform boundaries. It sacrifices the stronger claim about every physical copy.
If a later requirement needs that strict total or physical erasure, it needs separate
proof and a suitable implementation/platform.

Retaining the universal theorem as the prerequisite for all future work was considered;
it does not directly answer the selected operational resource objective. Raising the
legacy cap or treating logical lengths as physical bytes was rejected because neither
establishes complete coverage. Copy observations remain useful diagnostics and regression
evidence, without becoming the acceptance result.
