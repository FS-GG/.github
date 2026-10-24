# ADR-0093: Original .103 authentication domain policy

- **Status:** Accepted policy; principal/custody selection amended by [ADR-0094](0094-separate-v3-and-utel-principals-and-custody.md); fresh-profile resource scope selected by [ADR-0095](0095-utel-resource-containment-for-fresh-profiles.md); inherited-hold fencing amended by [ADR-0096](0096-proportionate-verification-and-unrecoverable-holds.md); implementation and native admission pending
- **Date:** 2026-10-15
- **Affects:** UTEL original `.103` authentication-only operation, external phase authority and native supervisor
- **Decision owner:** Programme, under the sponsor's explicit delegation
- **Decision:** [Domain policy](https://github.com/FS-GG/.github/issues/4517#issuecomment-6199911781), [accounting and ACK topology](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200107860), and [bounded source reservation](https://github.com/FS-GG/.github/issues/4517#issuecomment-6200287829)

**Fresh-profile resource scope — 2026-10-16:** [ADR-0095](0095-utel-resource-containment-for-fresh-profiles.md)
selects resource containment only for separately authorized fresh profiles. This record's
original `.103` physical-copy requirement and 65,536-byte cap remain unchanged and unproved;
no existing reservation, original-effect hold or admission is migrated by that decision.

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

**2026-10-16 amendment:** [ADR-0094](0094-separate-v3-and-utel-principals-and-custody.md)
names the distinct descriptor, phase-proof and ACK principals and assigns their
prospective credential custodians. It resolves the policy-selection gap in this
paragraph; actual secret provisioning, host custody, independent enrollment and
installed verification remain unproved. The App credential custodian is unchanged.

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

[PR #4565](https://github.com/FS-GG/.github/pull/4565) and the [late-cause correction #4567](https://github.com/FS-GG/.github/pull/4567),
protected at `a8384157c5622c7e0629b6d9129a6fbe8a96428a`, deliver the accepted **inert serialized
construction subset**. Its [closed contract and maximum vectors](https://github.com/FS-GG/.github/blob/a8384157c5622c7e0629b6d9129a6fbe8a96428a/tests/release-successor-authentication/domain-contract.md)
count 57,562 proposed encoded bytes and 7,974 bytes of headroom under that model's
65,536-byte cap. The existing authentication entry passed 68 pure tests and the PR's
hosted qualifications passed. These results establish the proposed encoding/model only;
the production provider and workflow remain unchanged and interface freeze stays held.

The construction retains all original dependency pins and the exact legacy child binding,
with separate executing authentication identity. The external descriptor commits the exact
binding digest and full ready-frame digest. Complete ready/start structural bounds are
1,291/1,273 bytes including framing, within the 2,048-byte child ABI. The closed legacy
`b,h,k,n,v` child ACK stays distinct from the 510-byte framed external ACK. The separate
546-byte framed diagnostic response cannot replace the existing 512-byte callback retain
response or become a gate. Proposed trusted verification must still join issuer,
descriptor/frame/checkpoint, ordinal, purpose, challenge, generation and original clock to
`TakeAck`/`ConfirmAckSent` one-use, full-write and cutoff checks. Placeholder signatures
are never authenticated positive evidence.

The closed model counts distinct host/peer control overlap, child/external receipts,
diagnostic bytes and independent same-token 204 observations. Core uncertainty refuses
further core/phase/gate mutation; separate loss and expiry slots remain append-once,
including consumption of an uncertain observation write without retry. First failure and
original-effect holds survive. The signed loss/expiry envelope includes optional closed
`lateCause` phase/code fields for a later discovery after core freeze, preserving the
earlier core `firstCause` even when absent. Each observation remains immutable and
append-once after its first possible write; loss/restart/ambiguity cannot supply a retry.
The 497-byte observation fits its 512-byte slot; four reserved copies add 280 bytes.
These modeled rules do not establish a native persistence
algorithm or actual population fit.

[Coordination PR #999](https://github.com/FS-GG/FS.GG.Coordination/pull/999), protected at
`16c28c0419612a358aa572ca5cbc1e3540bdd8d0`,
narrows the maintained inert ownership profile. Each decoded snapshot retains one
immutable exact frame; a checked payload offset/length view replaces its separate
retained payload array. Internal callback base64 encoding reads that owned frame,
and pending response-correlation metadata drops base64 after request encoding.
Public `Frame`/`Payload` accessors still return independent caller copies. Legacy
BE-u32 start/phase/ACK and callback encodings, full-write and one-use checks remain
unchanged. The proposed inventory limits decoded snapshot arrays to six × 2,048 bytes;
that is only one part of the complete retained population.

The protected [ownership inventory](https://github.com/FS-GG/FS.GG.Coordination/blob/16c28c0419612a358aa572ca5cbc1e3540bdd8d0/tests/engineering/phase-result-protocol/README.md)
and [managed source](https://github.com/FS-GG/FS.GG.Coordination/blob/16c28c0419612a358aa572ca5cbc1e3540bdd8d0/src/FS.GG.Coordination.Orchestration.Execution/PhaseResultProtocol.fs)
passed hosted compiler, required suites and coherent qualification (48 success, six
skipped). The [qualifier pin](https://github.com/FS-GG/FS.GG.Coordination/blob/16c28c0419612a358aa572ca5cbc1e3540bdd8d0/eng/utel-callback-source.json)
selects the separate inert callback pair at `.github` `71227e17` and exact 23-case census;
its corpus and actual paired Python callback journey do not qualify factory/native
compatibility, the new slot sink, credentials or original-operation effects.

The opt-in `fsgg.finite-custody-managed-ownership/1` inventory explicitly refuses a
closed physical-copy profile because selected endpoint capture/release is unqualified.
Legacy inert preparation supplies no fit assertion. Arbitrary accessor/caller retention,
stream captures and async request/task lifetime remain unbounded by this API. The
remaining fit blocker is the actual simultaneous-copy/lifetime census and slot mapping:
binding/start/ACK arrays, parser/encoder/base64 temporaries, callback/read buffers,
async writes and completed-task results, caller copies, and host/sink/backend/peer
storage. Snapshot aliases must not be double-counted as distinct arrays, and independent
buffers cannot be omitted as aliases. Scope end, terminal status, cancellation, timeout,
task completion and restart do not prove physical release or permit unresolved slot reuse.

The serialized model remains 57,562 bytes with 7,974 headroom; this source inventory
supplies no revised total or joined fit. Seven extra maximum-ready caller copies could
add 9,037 bytes and exceed headroom; this is a possible-copy counterexample, not an
observed population. Working-array exclusions need bounded lifetime, lack of authority
and separate finite memory proof. Actual host/sink mapping, filesystem metadata/allocated
blocks, runtime allocations and native enforcement remain unproved.

The protected [sink witness in PR #4570](https://github.com/FS-GG/.github/pull/4570)
at `966e3216713f2bc2959af5f01ebe9bb41be4b73b` supplies a concrete source-permitted
counterexample. Its [actual-sink test and accounting](https://github.com/FS-GG/.github/blob/966e3216713f2bc2959af5f01ebe9bb41be4b73b/tests/release-successor-authentication/sink-copy-contract.md)
construct a permitted padded report and intercept the pre-replace cut with an inert
filesystem double. Distinct old/temp file stores (16,384/16,215 bytes), prior/new
serialized memory (16,384/16,215) and incoming frame (972) total **66,170 bytes**,
exceeding 65,536 by 634 despite each report satisfying its 16-KiB limit. The five
focused controls, 77-test authentication suite and required hosted checks passed.
This proves a permitted serialized-copy topology, not a deployed allocation count,
filesystem block use or complete managed/native census. Parsed objects, other frames,
ACKs and additional copies are excluded from that lower bound. It establishes that
the existing per-report limit cannot prove joined fit; an actual sink/backend/peer
mapping and population proof remain required. It supplies no durable ACK, barrier,
clock, recovery or retry authority.

Protected [PR #4571](https://github.com/FS-GG/.github/pull/4571) at
`df803eb90f1d3f1d6175319d06dcb30478df5093` adds an [inert typed slot sink](https://github.com/FS-GG/.github/blob/df803eb90f1d3f1d6175319d06dcb30478df5093/tests/release-successor-authentication/slot-sink-contract.md)
that injects a closed-report callable into the actual `InertCallbackOwner`
bind/validate/retain route. It rejects padding/extra fields before reservation,
preserves the publisher transition graph including early terminal, and models one
attempt per core slot plus independent append-once loss/expiry observations. Ambiguous
cuts retain Unknown and first/late causes without retry. Its 12 focused controls,
89-test authentication suite and required hosted checks passed, including the unchanged
publisher overflow witness.

The 23,552-byte reservation covers only explicit modeled stores: six phase capacities
(12,288), four core old/candidate/temp/readback capacities (8,192), and six loss/expiry
candidate/file/readback capacities (3,072). It excludes caller/callback copies,
parser/serialization temporaries, Python/runtime objects, managed lifetimes and native
filesystem/backend behavior. This model does not replace or repair the publisher sink,
select a production sink, or provide cold reconstruction/cross-process custody, real
barriers or authenticated receipts. Its reservation cannot be added to or substituted
into 57,562 as a joined fit proof; the common-population cap remains 65,536. The reusable
source finding is that a closed injected sink can exercise the actual callback route
while its named-store budget remains separate from complete fit.

Protected [PR #4573](https://github.com/FS-GG/.github/pull/4573) at
`d3dd657b23fc1ac2f3aeb6cba06b6ca455406bdf` adds an [inert framed Python fixture](https://github.com/FS-GG/.github/blob/d3dd657b23fc1ac2f3aeb6cba06b6ca455406bdf/tests/release-successor-authentication/slot-sink-contract.md#framed-injected-stream-fixture)
through the actual decoder → callback owner → typed slot sink. The original report
passes the scalar guard before copying. A full response write and successful flush
precede confirmation; partial/malformed input and write/flush failure permanently
close the fixture, preserving existing backend attempts/stores and any existing Unknown
without retry; malformed input before a phase does not create Unknown.
The protected 13 exchanges and six retained frames match exactly. Thirteen focused
controls, 103 full authentication tests and required hosted checks passed (32 success,
two skipped). This accepts only injected-stream Python integration: it changes no
source pin or managed profile selection and establishes no production transport,
physical-copy total, real flush/barrier durability, cold restart, authenticated host
clock or native ACK custody. The same Atlas capture covers this reusable integration
finding; the original operation and complete-fit holds remain.

The [scalar-guard correction in PR #4572](https://github.com/FS-GG/.github/pull/4572),
protected at `11cecded9486cd74dfb0ff51946799a21f4469db`, addresses the review
counterexample that custom scalar equality could satisfy the literal closed-report
comparison. The [guard](https://github.com/FS-GG/.github/blob/11cecded9486cd74dfb0ff51946799a21f4469db/tests/release-successor-authentication/inert_phase_slot_sink.py)
now requires an exact built-in two-key dict with exact built-in string keys/values
before literal equality. [Adversarial controls](https://github.com/FS-GG/.github/blob/11cecded9486cd74dfb0ff51946799a21f4469db/tests/release-successor-authentication/slot-sink-tests.py)
reject custom equality, string subclasses and dict subclasses without invoking custom
equality or changing reservation/stores/attempts/events/phase state; a subsequent valid
frame succeeds. Thirteen focused controls, 90 full authentication tests and required
hosted checks passed. This closes that input-type counterexample in the inert sink;
it establishes no host deployment, complete population fit or native qualification.

Protected [PR #4576](https://github.com/FS-GG/.github/pull/4576) at
`fca016e1db30a279a5489223d19408f086eb18b5` extends the [inert preparation guard](https://github.com/FS-GG/.github/blob/fca016e1db30a279a5489223d19408f086eb18b5/tests/release-successor-authentication/inert_phase_slot_sink.py)
to identity, original-window and population/role declarations. Exact built-in dicts,
string keys, string identity/window values and integer generation/capacities are checked
before set/equality operations; admitted literals are snapshotted before caller-controlled
preparation hooks. Later caller identity mutation cannot change admitted path, binding,
invocation or generation. [Adversarial controls](https://github.com/FS-GG/.github/blob/fca016e1db30a279a5489223d19408f086eb18b5/tests/release-successor-authentication/slot-sink-tests.py)
refuse spoofed scalars/mappings without custom comparison or backend reservation, then
permit valid preparation and a phase. Sixteen focused controls, 106 full authentication
tests and required hosted checks passed (32 success, two skipped). This closes those
inert declaration-boundary counterexamples; complete population fit, installed host and
native qualification remain unproved.

Actual signer custody and independent enrollment, backend barrier/fresh exact readback,
authenticated original-clock continuity/uncertainty, and independent loss observation
remain unqualified. SystemAdmin owes those host/trust proofs; the UTEL source owner owes
the closed consumer/descriptor/copy join and compatibility. Later installed/native
admission remains separate from this bounded source reservation.

All six native selections and all admission/effect flags remain false. The original `.103`
tag result remains attempted-unknown and unreplayed; `.104` release remains held. No
credential issuance, installation, dispatch, journal, tag or release effect is admitted.
