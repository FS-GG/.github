# GS2-09.7 joint mint/pending seal installation boundary

This draft adds no live signer or store. The census and recovery worker now
require the same signed joint mint/pending seal, a pinned signer public-key
digest, and two matching reads of the current protected store head. All six
joint-seal pins, all inherited live pins, and the protected adapter remain
empty. Protected workflow #3690 is still an unadmitted observation. GS2-09.7
Q5/Q6 remain open.

The protected journal must atomically construct its complete mint index,
pending index, high-water mark, and seal. An independently protected signer
must sign the exact seal and current head generation using RSA-PSS SHA-256.
The source verifies the signature against a pinned SPKI SHA-256 digest and
binds the record to exact store, signer and policy IDs. It refuses a missing
key or envelope, a foreign key or signature, expired records, changed head,
replayed prior generation, type aliases, and candidate-writable descriptors.
The signed record expires within 15 minutes. A later recovery needs a fresh
protected seal and scheduler batch. The census returns the verified generation;
the scheduler binds it into every job and batch, and the worker requires the
same generation in its current signed readback and durable claim. The installed
batch append and one-use claim must compare that generation in the same durable
authority as the current head, including after a process restart.
The draft census, schedule, batch and claim schemas advance their versions to
carry `jointGeneration`; the generated schedule and batch IDs also include it.

The protected owner must decide and provision the signer resource, key
custody, public-key SPKI pin, policy digest, store resource and HTTPS endpoint,
and read-only host access to the current head and signed envelope. The
private key and store write credential must remain outside candidate and
workflow workspaces, artifacts, caches, and sandbox tokens. The store must
persist a strictly monotonic generation across process crashes and expose
linearizable head reads. The verifier also needs a trusted host clock for its
expiry check. The source cannot authenticate a self-reported head
if the installed adapter can replay both the head and its still-valid signed
envelope. The owner must demonstrate that mint-before-pending records cannot
be omitted from the signed high-water snapshot and that signer input comes
from this durable store, not a caller-supplied seal.

Fake ports use ephemeral test keys and an in-memory head. Their negative
controls prove source refusal only. The smallest remaining protected action
is an authority decision that selects and pins the signer/store identities
and grants a host-only readback path with independent custody evidence. No
key installation, workflow activation, token release, sandbox dispatch,
provider effect, protected merge, Authority write, receipt or cutover follows
from this draft.

## Current-head challenge readback

The follow-up source contract requires two distinct 256-bit host challenges.
For each, `read_joint_seal_head(challenge)` must return a separately signed
head attestation containing the exact challenge, current head, pinned store,
signer and policy identities, and a canonical UTC observation time within
60 seconds of the verifier clock. The two signed heads must match each other
and the signed joint seal. A cached response for an earlier challenge, an
unsigned head, or a stale observation refuses before any census subject or
recovery claim is released. The authority descriptor now declares
`linearizableHead: true` and `challengeBoundReadback: true` under schema v2.

The protected signer must obtain the head from the durable store itself in a
linearizable read before signing each challenge. It must not sign a head
supplied by the caller or a runner cache. After a crash, it must read the
persisted monotonic generation, not reconstruct it from a local process. An
unavailable read or uncertain signing result must fail closed. Challenge
binding prevents replay of an old signed response; it cannot prove the signer
used the true current head if the installed signer or its store reader is
compromised. Owner qualification therefore needs a protected signer/store
transaction trace, ACL evidence excluding candidate and workflow writes, and
restart tests that advance the generation and reject old seal/batch/claim IDs.
