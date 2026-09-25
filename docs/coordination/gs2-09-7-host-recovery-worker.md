# GS2-09.7 protected pending-revocation worker contract

This draft adds a source-only worker for one pending minted token, stacked on
#3714. It has no scheduler, provider adapter, credential, or workflow entry.
Its four recovery pins, #3714's five finalizer/vault/revoker pins, and the
inherited signer pin remain empty. No production token can be recovered or
revoked through this source as shipped.

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

This source processes one supplied subject. It does not enumerate all pending
tokens or prove that a protected scheduler has a complete, omission-free
census. Installing automated recovery requires that census, a durable worker
queue, a bounded retry/escalation policy for active tokens after unknown
claims, a protected vault lifetime long enough for recovery, and verified
native provider observation. A descriptor assertion and fake-port test do not
establish service ACLs, token escrow, atomicity, durability, or a live
revocation. Any missing journal, vault, revoker, binding, intent, or receipt
keeps the disposition pending.

The verdict's `disposition` is always `pending`, even when its fake provider
and journal report `revocation: revoked`. GS2-09.7 Q5/Q6 still require a
separate protected sandbox qualification and native receiver readback. #3690
remains an unadmitted source-delivery observation; no GS2-09.7 acceptance
receipt, GS2-09.8 acceptance, protected merge, or cutover follows from this
draft.
