# GS2-09.7 protected pending-token census contract

This source-only draft, stacked on #3736, supplies exact pending mint and
binding IDs to the #3715 recovery worker after a complete scan. It does not schedule or invoke
that worker. The six queue/journal pins and inherited recovery, finalizer,
vault, revoker, and signer pins are empty. No live authority or credential is
installed.

## Sealed high-water snapshot

The protected journal must atomically seal a snapshot of every pending
revocation whose mint sequence is at or below its current high-water mark.
The seal commits the high-water mark, pending count and digest, and a mint
count equal to the high-water mark with a SHA-256 digest of the ordered mint
index. Both digests cover ASCII JSON with sorted object keys and compact
comma/colon separators. The seal also binds exact queue, journal, finalizer,
recovery and vault identities, worker identity, and complete snapshot-isolation assertions. The
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
page. After all pages match the seal's count and digest, the scan checks the
full mint index. That index must have one unique mint at every sequence from
1 through the sealed high-water mark; each entry is read again from the
protected journal. Each index entry binds mint ID, binding ID, token digest
and installation ID. A pending mint must match all four values and its
sequence. Every other mint needs an exact protected terminal receipt with
`nativeObserved: true` and `state: revoked`, followed by a fresh native
readback for the same token, installation, sandbox repository, App, actor and
revoker. The readback must echo a new challenge and the seal ID. An active,
stale, foreign or unknown readback refuses the whole scan. An unresolved mint created
before pending intent therefore refuses the whole scan. A fully covered scan
returns sanitized `(sequence, mint ID, binding ID)` tuples. Unknown pages,
duplicates, cursor loops, missing pages, index gaps, unaccounted mints,
count mismatches and digest mismatches yield no subjects. The source refuses
snapshots above 10,000 total mints or 128 pending pages; a larger host needs
a separately reviewed sharding protocol.

This prevents omissions relative to an authentic joint pending-and-mint
journal seal. It cannot prove that a self-reported seal came from the actual
complete mint index or that a source-only port's challenge echo reflects a
fresh native provider observation.
Installation therefore requires an independently verified, host-owned
append-only mint sequence and atomic pending index, immutable snapshot and
high-water readback, a protected native revocation readback adapter with
fresh challenge binding, protected service credentials, and queue/journal ACLs
that the candidate token, sandbox repository, runner workspace, caches, and
artifacts cannot write or delete. The journal must authenticate each signed
binding and mint record before sealing; source descriptor fields and fake
ports do not establish that. The scheduler must use only a fully verified
snapshot, revalidate each pending subject through #3715 before native action,
repeat census for later high-water marks, and route a refused pre-pending mint
to protected resolution. Durable scheduling, progress tracking, and
escalation for unresolved active tokens are not installed here.

The result always has `disposition: pending`. It is a bounded input contract
for protected recovery, not a GS2-09.7 Q5/Q6 receipt or sandbox acceptance.
#3690 remains an unadmitted source-delivery observation. GS2-09.8 acceptance
still depends on the protected GS2-09.7 receipt.
