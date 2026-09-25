# GS2-09.7 recovery native-attempt generation fence

This source-only addition closes a fake-port race between the worker's final
authenticated head read and its one-use native-attempt claim. It adds
`claim_recovery_native_attempt_once(mintId, tokenSha256, attemptId,
expectedGeneration, floorResourceId)` to the protected recovery port. The
recovery claim must use the **same durable one-use native-attempt slot** as the
outer refusal finalizer; a second namespace would allow two provider attempts.
All live pins remain empty. No adapter or protected credential is installed.

## Installed transaction required

At one protected linearization point, the adapter must compare the exact
mint/token/context identity, pending intent, recovery claim, current signed
joint mint/pending head generation, pinned floor resource identity, and absence
of any native-attempt marker, then durably commit the one marker or refuse.
The generation and floor arguments are claims from the worker; the adapter must
read the independently protected head and floor itself. A candidate-visible
descriptor, cached signed head, or separate read followed by a write cannot
serve as this comparison. The finalizer's ordinary attempt claim and recovery's
generation-aware claim must contend for the same key and have one winner.

If the call throws or its response is lost, the worker must treat the result as
unknown, read the marker, observe native token state, and never make a second
fresh provider attempt. A duplicate or foreign marker cannot authorize revoke.
The protected owner must show host ACLs excluding candidate/workflow writes to
the head, floor, vault, pending journal, claim, and shared marker, plus the
exact installed signer/store IDs, SPKI and policy digests, endpoints, code
digest, revoker principal, and restart readbacks. The owner must demonstrate
adversarial stale-generation, foreign-floor, missing-port, concurrent finalizer
versus recovery, lost-result, and crash-after-marker controls.

This source contract does not prove a protected fence between marker commit and
the external GitHub native revoke call. The installed owner must decide and
demonstrate that final provider-call ordering and native observation under
concurrent head publication, or keep Q5/Q6 held. #3690 remains unadmitted;
this draft does not authorize token release, sandbox dispatch, receipt, or
cutover.
