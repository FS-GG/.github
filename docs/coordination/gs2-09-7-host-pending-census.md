# GS2-09.7 protected pending-token census contract

This source-only draft supplies exact pending mint and binding IDs to the
#3715 recovery worker after a complete scan. It does not schedule or invoke
that worker. The six queue/journal pins and inherited recovery, finalizer,
vault, revoker, and signer pins are empty. No live authority or credential is
installed.

## Sealed high-water snapshot

The protected journal must atomically seal a snapshot of every pending
revocation whose mint sequence is at or below its current high-water mark.
The seal commits the high-water mark, pending count, SHA-256 digest of the
ordered subject list encoded as ASCII JSON with sorted object keys and compact
comma/colon separators, exact queue/journal/finalizer/recovery/vault
identities, worker identity, and complete snapshot-isolation assertions. The
host independently reads the same seal and current journal high-water mark
before and after pagination. A new mint during the scan, stale seal, changed
seal, or unavailable native readback refuses the entire result. Empty pending
sets still need an authenticated empty-list digest and an end-of-list page.

Each subject binds its sequence, mint ID, binding ID, token and run-context
digests, sandbox repository ID, App ID, actor, installation ID, protected
journal/finalizer/recovery resources, vault ID, and `revokeRequired: true`.
The scan reads each subject again from the sealed snapshot and compares it
with the separately protected, previously verified binding record. Pages must carry
the same seal and high-water mark, strictly increasing sequence numbers,
unique mint and binding IDs, exact cursor progression, and a definite final
page. Only after all pages match the seal's count and digest does the function
return a sanitized list of `(sequence, mint ID, binding ID)` tuples. Any
unknown page, duplicate, cursor loop, missing page, wrong target, partial
scan, count mismatch, or digest mismatch yields no subjects. The source
refuses snapshots above 10,000 pending items or 128 pages; an installed host
needs a separately reviewed sharding protocol before those limits.

This prevents omissions relative to an authentic journal seal. It cannot
prove that a self-reported seal came from the actual complete mint index.
Installation therefore requires an independently verified, host-owned
append-only mint sequence and atomic pending index, immutable snapshot and
high-water readback, protected service credentials, and queue/journal ACLs
that the candidate token, sandbox repository, runner workspace, caches, and
artifacts cannot write or delete. The journal must authenticate each signed
binding and mint record before sealing; source descriptor fields and fake
ports do not establish that. The scheduler must use only a fully verified
snapshot, revalidate each subject through #3715 before native action, and
repeat census for later high-water marks. Its scheduling, durable progress
tracking, and escalation for unresolved active tokens are not installed here.

The result always has `disposition: pending`. It is a bounded input contract
for protected recovery, not a GS2-09.7 Q5/Q6 receipt or sandbox acceptance.
#3690 remains an unadmitted source-delivery observation. GS2-09.8 acceptance
still depends on the protected GS2-09.7 receipt.
