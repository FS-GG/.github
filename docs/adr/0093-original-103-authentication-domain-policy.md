# ADR-0093: Original .103 authentication domain policy

- **Status:** Accepted policy; implementation and native admission pending
- **Date:** 2026-10-15
- **Affects:** UTEL original `.103` authentication-only operation, external phase authority and native supervisor
- **Decision owner:** Programme, under the sponsor's explicit delegation
- **Decision:** [Domain policy](https://github.com/FS-GG/.github/issues/4517#issuecomment-6199911781), [accounting and ACK topology](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200107860), and [bounded source reservation](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200287829)

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
SystemAdmin owns the design and later operational custody of a separately supervised
ACK-signing service outside that facts store and the original job/runner/VM. Its signing
keys require separately approved private custody. The protected native supervisor must
verify an independently enrolled ACK public identity. The service may sign only an exact,
read-back-equal, barrier-complete receipt for the original descriptor/frame/ordinal/store
generation under the original clock policy. A reporter assertion cannot establish that
backend evidence. This selects a topology and owner; actual custody and independent
enrollment remain unproved and no key creation or installation is admitted.

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

Prospectively retain nonsecret facts for this one operation under six logical phase
ordinals and **65,536 total retained bytes**. Count the complete worst-case signed/schema
population: descriptor, exact frames/ACKs, loss/first-failure/terminal/expiry facts,
immutable checkpoints, and distinct old/temp/candidate uncertainty evidence. Every
simultaneous physical copy, peer control object, write and replacement overlap counts
toward the byte cap. Reserve the complete bounded population before admission; an
unproved fit or overflow refuses source/runtime admission. Required facts cannot be
evicted, omitted or replaced by checkpoint hashes to fit.

Retention ends 90 days after the original total cutoff, without read-based renewal.
Expiry yields RetentionExpired/Unknown and preserves the original-effect hold in its
owning decision record. This is a newly selected phase policy; the existing 90-day CI
artifact rule does not govern these facts or permit a rerun.

## Consequences

This selects policy, not an installed interface or effect. Protected source at
[`f4f40a8d` validates the publisher workflow](https://github.com/FS-GG/.github/blob/f4f40a8dae53b352392423a6b79e5cbfd15c8392/scripts/release_successor_provider.py),
while the [inactive authentication job](https://github.com/FS-GG/.github/blob/f4f40a8dae53b352392423a6b79e5cbfd15c8392/.github/workflows/release-successor-authenticate.yml)
uses `release-successor-authenticate.yml`. A reviewed descriptor extension must distinguish
original publisher provenance from executing authentication identity. Existing inert
source controls do not establish native construction or acceptance.

The earlier unavailable-packet status is historical. SystemAdmin [reported restored,
read-back-equal private v1/v2 materials](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200035172)
and [delivered private v3](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200247987).
Programme independently verified the received v3 digest and reviewed its planning claims.
V1's 128-frame/131,072-byte proposal remains historical, outside the selected cap.
Programme accepted v3's 64,000-byte constructive slot arithmetic and separate supervised
ACK-service topology only as a [planning subset](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200287829).
Receipt and slot arithmetic do not prove complete signed-schema fit or freeze an interface.
Reported readiness v10 supplies no native qualification.

Programme/fdev1 owns one reserved source-only proof slice: a versioned inert contract,
complete maximum encoded vectors and exhaustive slot/copy accounting in an isolated
checkout. SystemAdmin independently owns host layout/backend/trust review against that
exact contract digest; Programme decides the join. The reservation permits pure contract,
vectors and tests. It grants no production provider/workflow change, service installation,
key enrollment, credential use, original dispatch or release-state change.

The exact remaining hold is the complete signed descriptor/frame/ACK/read schemas and
maximum vectors, including distinct original publisher and executing authentication
identities and preservation of original pins; the current ready ABI must fit its
2,048-byte slot. The peer must prove its irreversible-control persistence algorithm and
simultaneous-copy bound: two extra 1,024-byte control copies would raise 64,000 to 66,048
bytes and refuse admission. Diagnostic/raw-receipt reads must stay distinct from a
replacement effect gate, and ambiguous-write/loss/expiry states need counted encodings.
The [exact source-join review](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200296350)
also distinguishes the closed legacy child ACK (`b,h,k,n,v` plus BE-u32 framing) from
the external signed ACK. Its proposed size does not prove child-ABI compatibility.
The trusted persistence/readback callback must authenticate issuer, descriptor, frame,
ordinal, purpose and original clock before returning typed readback; three echoed fields
cannot prove independent authority. Preserve `TakeAck`/`ConfirmAckSent` one-use,
full-write and cutoff checks. Start/ready must include the exact child binding and fit
2,048 bytes including framing and wrappers. The 4,096-byte external descriptor slot
cannot replace that binding: freeze their authenticated relationship and supply complete
maximum wrapped vectors without removing required original fields.

Actual signer custody and independent enrollment, backend barrier/fresh exact readback,
authenticated original-clock continuity/uncertainty, and independent loss observation
remain unqualified. SystemAdmin owes those host/trust proofs; the UTEL source owner owes
the closed consumer/descriptor/copy join and compatibility. Later installed/native
admission remains separate from this bounded source reservation.

All six native selections and all admission/effect flags remain false. The original `.103`
tag result remains attempted-unknown and unreplayed; `.104` release remains held. No
credential issuance, installation, dispatch, journal, tag or release effect is admitted.
