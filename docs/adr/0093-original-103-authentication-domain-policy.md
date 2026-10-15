# ADR-0093: Original .103 authentication domain policy

- **Status:** Accepted policy; implementation and native admission pending
- **Date:** 2026-10-15
- **Affects:** UTEL original `.103` authentication-only operation, external phase authority and native supervisor
- **Decision owner:** Programme, under the sponsor's explicit delegation
- **Decision:** [Public domain-policy selection](https://github.com/FS-GG/.github/issues/4517#issuecomment-6199911781)

## Context

The delegated decision selects the domain contract for the single original `.103`
authentication-only operation. Programme owns policy and change control; EHotwagner
remains the credential/App custodian. SystemAdmin owns the proposed external service
implementation and its host evidence. These roles do not substitute for one another.
The [owning plan](../roadmaps/utel-release-successor.md#original-103-authentication-domain-policy--2026-10-15)
retains source and native acceptance separately.

## Decision

A protected programme-controlled issuer produces one versioned, closed, canonically
encoded descriptor. Bind it to the original operation, candidate/dependency/source/runtime
tuple, publisher provenance, executing authentication workflow/run/attempt, original clock
and policy revision. The external phase authority and native supervisor verify it against
an independently enrolled issuer identity. The App signer and reporter transport are not
descriptor issuers. Reject caller-supplied trust roots, duplicate/unknown fields and identity
substitution. A separately enrolled, distinct acknowledgment (ACK) authority supplies an
authenticated durable receipt binding the descriptor, exact phase-frame bytes, ordinal,
phase, store generation and original-clock observation. Home retains bounded key-free facts;
actual signing-key custody and enrollment require proof outside that retained-facts boundary.

Retain ready → issuance-possible → token-owned → revocation-possible → revoked → terminal
order and the existing [frame/ACK limits](../../tests/release-successor-authentication/wire-contract.json).
Require durable write, barrier and independent readback before an effect-enabling ACK.
Ready, issuance-possible and revocation-possible are one-use gates: reserve possible delivery
before transmission. Lost replies, partial sends and restart never authorize a replacement
gate. A child ACK or stored reporter assertion alone is not independent authenticated evidence.

Normal revocation requires the original custodian's independently observed HTTP 204 for
the one owned token and reserved DELETE, a durable authenticated phase receipt, and every
other terminal predicate. Expiry, 401/404, GET and a second DELETE cannot substitute.
Retain the [accepted fault matrix](https://github.com/FS-GG/.github/issues/4517#issuecomment-6194859259):
whole job/runner/VM loss is Unknown, unqualified and unretried unless previously durable
evidence proves a narrower fact. Surviving-custodian cleanup remains limited to the one
already admitted same-token action.

Retain the original 270-second normal, 285-second work and 300-second total cutoffs.
Job, process, service and host restart cannot renew them. Require an authenticated
original-clock mapping with measured finite uncertainty; the latest possible original
time must precede the applicable cutoff. Clock rollback, boot-domain mismatch, overflow
and unknown uncertainty refuse effects. The 285–300 interval is settlement only.
At/after 300, diagnostic reads and loss observations cannot become timely ACKs or
permission to replay.

Prospectively retain nonsecret descriptor/phase/ACK/loss facts for this one operation:
at most six phase records and 64 KiB total, until 90 days after its original total
cutoff, without sliding extension. Reject overflow rather than evict required facts.
Expiry yields RetentionExpired/Unknown and preserves original effect holds. This is
a newly selected phase policy; the existing 90-day CI artifact rule does not govern
these facts or permit a rerun.

## Consequences

This selects policy, not an installed interface or effect. Protected source at
[`f4f40a8d` validates the publisher workflow](https://github.com/FS-GG/.github/blob/f4f40a8dae53b352392423a6b79e5cbfd15c8392/scripts/release_successor_provider.py),
while the [inactive authentication job](https://github.com/FS-GG/.github/blob/f4f40a8dae53b352392423a6b79e5cbfd15c8392/.github/workflows/release-successor-authenticate.yml)
uses `release-successor-authenticate.yml`. A reviewed descriptor extension must distinguish
original publisher provenance from executing authentication identity. Existing inert
source controls do not establish native construction or acceptance.

The exact 17,647-byte restricted proposal is unavailable in this programme environment.
Its internal signing/transport topology must be reconciled before claiming the implementation
interface frozen. SystemAdmin must supply endpoint/store/transport, independently enrolled
identities, durable ACK and clock/loss evidence. The UTEL source owner must supply the
reviewed descriptor extension and compatibility with original pins. A separate bounded
source reservation and later installed/native admission remain required.

All six native selections and all admission/effect flags remain false. The original `.103`
tag result remains attempted-unknown and unreplayed; `.104` release remains held. No
credential issuance, installation, dispatch, journal, tag or release effect is admitted.
