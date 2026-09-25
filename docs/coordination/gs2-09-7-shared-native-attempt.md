# GS2-09.7 shared native-attempt fence

Status: source-only fake-port contract stacked on draft #3732. The protected
finalizer journal, recovery worker, queue, revoker, vault, signer and admission
pins remain empty. #3690 is an unadmitted source observation.

Red-before: the recovery worker observed an active token and issued native
revoke even though the finalizer had already made an ambiguous prior attempt.
Both paths now use one protected `claim_native_attempt_once` record in the
finalizer journal before any normal native revoke call. The record binds mint
ID, token digest, run-context digest, finalizer resource and revoker ID. A
fresh host-generated 256-bit attempt ID must be committed and read back
exactly before the provider call. A stale `committed` response with a prior
attempt ID, a foreign record, missing readback, duplicate claim or lost claim
response cannot authorize another call. Recovery may finish an exact durable
receipt from definitive native `revoked` observation; it does not retry a
prior attempt while native state is active or unknown. Candidate launch is
still limited by its separate one-use decision claim.

The protected implementation must put the marker in the same host-only,
durable atomic-CAS authority read by both finalizer and recovery. Its
credential and storage must be inaccessible to the candidate workspace,
sandbox token, artifacts and caches. The protected owner must prove that a
`committed` response and readback refer to the fresh attempt ID and exact
mint/binding/run context, and that revocation observation is native and
current. A crash after marker commit but before the provider call consumes
the one attempt: the source keeps the token pending and requires an explicit
protected resolution policy. It cannot promise both no duplicate native call
and automatic completion across that cross-system crash window.

The emergency path for a missing or unavailable finalizer journal remains a
best-effort native revoke and has no shared-attempt proof. Protected release
must separately close the mint-before-pending gap: #3716 seals pending
subjects, but a token minted before pending intent may be absent from that
set. Installation requires an independently complete minted-token index,
durable handoff of every unresolved mint and pending subject to the #3715
worker, scheduler progress/readback, and escalation for consumed attempts
that remain active or unknown. None of those ports or a scheduler is installed.

Q5/Q6 native receiver readback and a protected receipt remain open. This
draft authorizes no key or credential installation, token release, sandbox
dispatch, provider effect, Authority write, protected merge, receipt or
cutover.
