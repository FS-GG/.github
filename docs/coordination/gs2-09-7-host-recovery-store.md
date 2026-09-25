# GS2-09.7 protected recovery store boundary

This stacked draft defines a typed, no-effect storage port for the sealed
recovery batch from draft #3739. No durable store, workflow, scheduler, signer,
credential, or native adapter is installed. All live identity pins remain
blank. Protected workflow #3690 remains an unadmitted observation, and
GS2-09.7 Q5/Q6 remain open.

The installed authority must own one candidate-inaccessible durable store for
the following operations:

1. Compare an authentic joint mint/pending seal and high-water mark, then
   append the entire batch in one atomic operation. Partial jobs must never
   become visible. A duplicate or unknown append response cannot cause a new
   batch or a recovery launch.
2. Withdraw a batch by its exact batch and seal IDs. Withdrawal must share a
   linearization domain with the one-use recovery claim. If withdrawal wins,
   the claim refuses. If the claim wins, withdrawal preserves that claim for
   native observation and durable finalization.
3. Commit a one-use claim only for an exact committed job in an exact committed
   batch. Persist its schedule, batch, seal, scheduler and recovery identities.
   The worker now refuses a readback missing or changing any of these fields;
   a string `committed` response alone cannot authorize native revoke.

The fake store exercises both orders of claim versus withdrawal, concurrent
full-batch appends, lost claim response, and durable claim retention. It is an
in-memory model. The descriptor's `atomicWithdrawClaim` bit is a required
capability declaration, not proof of installed linearizability or protected
identity. The installer must provide independent evidence of candidate
isolation, sealed mint-index completeness, authenticated journal/high-water
ownership, durable transaction semantics across process crashes, and native
token custody/readback. A claim committed before later withdrawal still
requires the existing shared native-attempt and finalizer controls; this draft
does not authorize any provider call, receipt, merge, or cutover.
