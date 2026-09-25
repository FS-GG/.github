# GS2-09.7 protected head-floor owner decision

This stacked source draft adds an injected monotonic floor port. It has no
installed endpoint, credential, workflow, key or live effect. The three floor
pins and all joint-seal pins remain blank. Protected workflow #3690 remains
an unadmitted observation; GS2-09.7 Q5/Q6 remain open.

## Decision needed from protected owners

The protected owners must identify the joint mint/pending store, signer and
independent floor authority by exact resource ID and HTTPS endpoint. They must
select the signer SPKI SHA-256 and policy digest, name the host identity allowed
to read the signed head and atomically advance the floor, and provide ACL
evidence that the candidate, sandbox token, workflow workspace, caches and
artifacts cannot write either authority or access the signing key. The floor
must survive a rollback or crash of the joint store and signer; placing it in
the same rollback domain would defeat this control.

The floor's durable record binds its own resource ID, joint store ID, signer
ID, policy digest, highest observed generation and exact seal ID. Its
`advance_head_floor_once(expected, generation, sealId)` operation must compare
the complete prior record and commit a strictly higher generation atomically.
It must never lower or fork a generation. Reads before and after CAS must be
linearizable and independent of candidate-provided data. A lost CAS response,
false `committed` response, stale read, foreign record or lower freshly signed
head refuses; the verifier never retries a possibly committed advance in the
same call. The scheduler's batch append and recovery claim must still compare
the current signed generation in their own durable transaction.

## Protected qualification before any live pin

Use protected, read-only evidence and a no-provider-effect rehearsal to show:

- generation 2 remains the floor after signer and joint-store process restart;
  a fresh valid signature for generation 1 then refuses;
- a concurrent floor advance cannot be hidden by stale pre-read or post-read;
  exact readback is required after a lost or duplicate CAS result;
- the same generation with another seal ID refuses, and a missing or
  candidate-writable floor authority cannot produce a verified seal;
- the signer obtains the current head in a linearizable store read before
  signing a new challenge, and the minted/pending snapshot covers every mint
  below the sealed high-water mark.

The fake tests exercise these cases in memory. They do not establish protected
durability, independent failure domains, credential custody or native provider
readback. The smallest external prerequisite is a protected owner decision on
the three authorities and their ACL and transaction evidence. No key or
credential provisioning, sandbox dispatch, provider call, protected merge,
Authority write, receipt or cutover is performed by this draft.
