# GS2-09.7 protected pending-revocation worker contract

This draft adds a source-only worker for one pending minted token, now stacked
on #3737. It has no scheduler, provider adapter, credential, or workflow
entry. Its recovery, scheduler, census queue/journal,
finalizer/vault/revoker, and inherited signer pins remain empty. No
production token can be recovered or revoked through this source as shipped.

## One exact protected subject

A protected scheduler supplies a mint ID and the SHA-256 binding ID previously
verified and committed by the protected host. The worker checks those IDs
against an independently credentialed recovery journal, the protected mint
record, the exact finalizer resource, the encrypted vault ID, the sandbox
repository ID, App ID, actor, installation ID, token digest, and run-context
digest. It also requires readback of the durable `revokeRequired: true` intent
from #3714. The `verifiedAtCommit` bit in the binding record is only a source
contract: the installed journal must actually authenticate the signed
envelope and proof before writing it. The candidate token, checkout, sandbox
repository, artifacts, caches, and runner workspace must be unable to write,
delete, or impersonate any of these protected records or credentials.

The worker also requires a pinned, host-owned durable scheduler descriptor and
an exact committed schedule record for the mint, binding, token, run context,
installation, target, queue, journal and recovery resources. It independently
reads the joint mint/pending seal and the exact pending subject from the
protected census journal, then reads the seal again to reject drift. A
schedule record's own `censusComplete: true` assertion is insufficient. The
schedule ID is passed to the one-use recovery claim; the installed atomic CAS
must refuse a withdrawn schedule before any native effect. Missing, foreign,
candidate-writable, stale or unknown schedule evidence blocks the claim.

The recovery journal must be a pinned HTTPS resource with durable atomic CAS
and native readback. A fresh committed recovery claim requires exact readback
before any provider mutation. The worker observes the token natively first.
It makes one native revoke call only when that fresh claim is confirmed and
the provider reports the token active. It observes again before writing a
durable receipt. A lost native or receipt response is resolved through
provider and journal readback. A duplicate, lost, or falsely committed claim
cannot issue another native revoke call; when native readback already reports
revoked, it may append and read back the exact receipt. If a prior claim
committed but the worker crashed before revocation, an active token remains
pending for protected owner intervention. The worker never retries the
candidate invocation or asserts that a GitHub App token is single-use.

This source processes one supplied subject. The #3736/#3737 census verifies
complete mint/pending coverage in fake ports, but this worker cannot recompute
the full list from one job. Installing automated recovery requires a protected
scheduler that durably enqueues every subject from the verified joint seal,
does not silently omit jobs, and keeps schedule admission and recovery claim
in one authority. The owner must prove schedule revocation ordering through
native action, a bounded retry/escalation policy for active tokens after
unknown claims, a protected vault lifetime long enough for recovery, and
native provider observation. Descriptor assertions and fake-port tests do
not establish service ACLs, escrow, atomicity, durability, or a live
revocation. Unresolved minted-before-pending tokens require separate
protected resolution because they cannot satisfy this worker's pending-intent
gate. Any missing schedule, journal, vault, revoker, binding, intent, or
receipt blocks native action or leaves the token pending.

The verdict's `disposition` is always `pending`, even when its fake provider
and journal report `revocation: revoked`. GS2-09.7 Q5/Q6 still require a
separate protected sandbox qualification and native receiver readback. #3690
remains an unadmitted source-delivery observation; no GS2-09.7 acceptance
receipt, GS2-09.8 acceptance, protected merge, or cutover follows from this
draft.
