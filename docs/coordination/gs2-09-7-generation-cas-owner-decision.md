# GS2-09.7 protected generation comparison decision

This source-only contract binds scheduler batch append and recovery claim to the
same authenticated joint mint/pending head generation and pinned monotonic floor.
It supplies no installed protected adapter, credential, private key, or receiver.
All production pins remain empty; #3690 remains an unadmitted observation and
Q5/Q6 require protected qualification and native readback.

## Protected owner decision needed

The protected owner must select and attest the exact joint-seal origin, endpoint,
store resource ID, signer resource ID, signer SPKI SHA-256, policy SHA-256, and
the monotonic floor origin, endpoint, resource ID, plus the scheduler and recovery
store origin, endpoints, resource IDs, worker identity, census queue/journal IDs,
vault ID, finalizer ID, native revoker identity, sandbox repository ID, App ID,
actor, and installation ID. The signed head and floor must refer to one
candidate-inaccessible durable authority domain. A source pin or descriptor
returned by a candidate-controlled port is not installation evidence.

The owner must show ACL evidence that only the protected host signer can sign
head attestations, only the protected scheduler/recovery principals can append,
withdraw, or claim, and candidate workspaces cannot read escrow or write the
head, floor, mint index, pending journal, batch, claim, or native-attempt store.
The exact deployed code digest, signer key custody, credential scopes, resource
IDs, and endpoint routing must be observed from that host, not supplied by the
candidate. Key rotation and rollback need a protected decision before use.

The installed store must prove a linearizable transaction for each call:

1. `append_schedule_batch_once` compares the authentic sealed mint/pending
   high-water, complete subject digest, signed current head generation, and
   pinned floor resource ID with its arguments, then commits the whole batch
   and every job together or none.
2. `claim_recovery_once` compares the same current signed generation and floor
   identity, exact batch and schedule binding, and withdrawal state at the
   claim linearization point. A withdrawal or head advance before that point
   refuses a fresh claim. A prior committed claim remains durable for recovery.
3. The signed head, floor CAS, batch, claim, native-attempt marker, and receipt
   readbacks must be bound to one protected store generation after a crash.
   Lost responses and unknown results must remain pending; they cannot authorize
   another fresh append, claim, native attempt, launch, or revoke.

The post-commit signed-head checks in this draft catch a later generation
advance before the scheduler reports success or the worker starts native revoke.
They do not make two independent services atomic. If the protected owner cannot
prove the signed-head/floor comparison at each durable transaction boundary,
the adapter must refuse installation. If head advancement occurs after a claim,
the worker leaves the claim pending for protected finalization and must not infer
that the token was revoked. A head advance after the final read and before native
action still requires a host-owned linearization/fence with the native-attempt
authority; this source draft does not establish that installed custody.

## Negative evidence required before Q5/Q6

Run protected, owner-observed controls for a foreign floor ID, stale signed
generation, omitted pending subject, duplicate batch/job, withdrawal racing
claim, lost append/claim response, crash after claim before native action, and
head advance immediately after append and claim. Observe the exact durable
batch/claim/attempt/receipt and native token state after restart. No candidate
assertion or fake-port test can replace these readbacks. Keep token release,
sandbox dispatch, Authority receipt, and cutover held until those controls and
the separate #3690 admission decision are complete.
