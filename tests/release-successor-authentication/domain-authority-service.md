# Descriptor authority source contract

`UTEL-AUTH-SVC-01` promotes the ADR-0093 closed codecs, schemas and signing-domain
bytes into `scripts/release_successor_domain_authority.py`. The pinned inert model
remains byte identical as the frozen compatibility oracle. Focused controls compare
its codec AST and every fixed maximum vector with the production codec. Existing
maximum vectors and legacy ABI bytes remain the
compatibility oracle. `DescriptorAuthority` is a policy component with unimplemented
trusted effect ports. It has no CLI, key, credential lookup, network endpoint,
native enabling switch, installation or workflow activation.

## Provisioning and admission

Programme owns descriptor policy/source; descriptor key custody and enrollment are
separate prerequisites. The App custodian, reporter transport and separately
supervised external ACK authority cannot substitute for the descriptor issuer.
The facts store is key free. A Python constructor, subclass, digest, fake port
result or caller boolean is not proof of authenticity. There is no usable default
construction: independently provisioned policy bytes and all three trusted ports
are required. This release supplies no qualified concrete port implementations.

The provisioned policy is one immutable canonical JSON object, version 1, containing
`original`, `binding`, `ready`, `authentication`, `enrollment` and `population`.
`original` contains the exact four protected publisher constants (candidate, all
nine dependencies, compatible dependencies and original checkpoint). `binding`
is the complete unchanged legacy native binding, including publisher provenance,
source/runtime closures, process birth, invocation, App/installation and original
clock. `ready` is the exact framed ready message in lowercase hex, retained along
with that full binding. `authentication` is the separately executing repository,
workflow revision/run/attempt. `enrollment` has the existing version 3 closed role
and generation schema. `population` has version 1, scope
`complete-physical-population`, an evidence digest, positive bytes at most 65,536,
and limit 65,536. That declaration is necessary input, not a fit proof.

A request contains exactly `binding`, `ready` and `authentication`, byte equal to
the provisioned selection. Every input is immutable canonical `bytes` before a
callback; mutable mappings, duplicate keys and noncanonical documents refuse.
The descriptor binds the complete ready and binding digests, every original pin,
separate authentication identity and enrollment digest without wire extensions.

`IssuerAdmission.authenticate(policy, request, evidence)` must independently prove:

- Programme admission of these exact protected original constants, source/runtime
  closures, policy revision and operation; no reporter-selected policy or trust root.
- Authenticated executing repository/workflow revision/run/attempt and original
  publisher provenance; exact invocation, process, binding and retained ready joins.
- Independent issuer/ACK/reporter/custodian/supervisor enrollment, store generation,
  authorized descriptor key custody, and the authenticated ingress principal's
  permission to request this one descriptor. App credentials confer no issuer role.
- A reviewed, complete simultaneous physical population under the operative cap,
  including this controller, codec, signing buffers, runtime, transport, backend,
  retained artifacts and consumer copies. Partial model arithmetic cannot qualify it.
- Authenticated mapping to the retained original clock, finite measured uncertainty,
  boot continuity, overflow and rollback checks, and current permission before the
  original normal cutoff. Original cutoffs remain 270/285/300 seconds from original
  start. No process, job, service, host or policy restart renews them.

The result is canonical version 1 admission bytes with `policy` and `request`
SHA256 digests; `identityEvidence`, `permissionEvidence`, `enrollmentEvidence`,
`populationEvidence`, `clockEvidence` digests; `domain` (SHA256 of the exact retained
clock object); and integer `lower`/`upper` original ticks. The controller checks
closed shape, joins, complete population evidence identity and
`start <= lower <= upper < normal`. Evidence digests record independently verified
artifacts; digest syntax does not implement their verification. Reject absent,
incomplete, expired or unauthenticated evidence in the adapter. Authentication must
never be implemented by echoing these request fields or trusting a reporter flag.

`recheck(policy, request, admission)` returns the same admission with a fresh clock
interval/evidence after reservation. Both interval bounds must be monotonic. Other
proof bindings must remain identical. The qualified signer must check the original
clock and current authorization at actual signing dispatch as well: a synchronous
port return alone cannot bound arbitrary scheduling/transport delay. Clock evidence
and admission receipts need explicit finite lifetimes and replay protection in the
concrete adapter. This source does not establish them.

## One possible issuance

`IssuanceStore.reserve_once(original_key, commitment)` atomically consumes the
original attempt across every process, boot and concurrent issuer before returning
exact, durable, independently read-back-equal commitment bytes. The stable original
key binds operation/generation/candidate/checkpoint. Authentication run, enrollment,
policy and clock changes cannot mint another attempt. Commitment bytes bind that
key, policy/request digests and exact descriptor signing-byte digest. Reservation
failure, result loss, unreadable state or ambiguous recovery must preserve consumed
Unknown. An empty/unqualified read cannot authorize a fresh attempt. A concrete
backend must prove these properties with durable locking, barriers and cold recovery;
the in-memory test store supplies no such proof.

The controller consumes its local attempt before calling `reserve_once`. Only exact
reservation readback and the fresh clock join permit the descriptor-only signer
call. `DescriptorSigner.issue_descriptor(signing_bytes, commitment, admission)`
uses independently enrolled issuer custody; it must verify reservation/policy/clock
and the exact `FSGG-UTEL-DESCRIPTOR3\0` domain. It returns a canonical unpadded URL-safe
64-byte signature string. Codec validation checks encoding only. Future native
consumer verification must independently verify cryptography and enrollment.
No reporter-facing generic signing service is provided.

`complete(original_key, commitment, descriptor)` retains the exact signed envelope
and returns its exact durable readback. Any exception, malformed/lost signature,
clock refusal after reservation, completion loss or mismatch holds local Unknown.
No replacement signing request is made. Store recovery must preserve the earliest
failure and consumed attempt, even if a process dies before it can report failure.
Diagnostic recovery of a previously retained signed envelope has an observational
role; it cannot manufacture a second issuance or timely gate. `observe` is reserved
for a future read-only backend adapter; the current controller's `diagnostic()`
reads only local state/first failure and calls no port. First local failure stage
is preserved without retaining secret exception text. A second instance still
requires the store to refuse the same original key, even after policy changes.

## Acceptance and deployment boundary

`domain-authority-tests.py` exercises the real controller with explicitly inert
ports: exact bytes and dependency joins, missing/wrong identity/policy/permission/
clock/population evidence, mutation isolation, reserve-before-sign ordering,
reentrant refusal, every reservation/signature/completion ambiguity, first failure,
restart/policy replacement and diagnostic refusal to create another attempt.
The established hosted `run.py` includes it and the unchanged vector suite.
Injected success is source evidence only. Production cryptography, durable backend,
authenticated enrollment/clock, concrete issuer service runtime, complete real fit,
independent ACK verification and installed consumer acceptance remain open.

No deployment, key creation, credential access, native adapter, workflow activation,
original operation or cap change is selected by this source. The original `.103`
remains attempted-unknown and unreplayed; `.104` remains held. Later activation
requires independently verified custody/enrollment, concrete adapter qualification,
complete population fit and operation-specific admission. This module supplies no
way to recover or repeat the uncertain original tag dispatch.

Shared-import adoption is sequenced separately: changing `domain_contract.py`
requires the closed slot-pair source manifest/profile, role-count/closure controls,
regenerated bundle-bound vectors and the Coordination pin follow-up to move together.
The initial import attempt correctly failed those immutable source/closure guards;
this slice retains the old consumer and every old source/vector pin. Production
codec promotion here does not establish consumer adoption.
