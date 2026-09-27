# GS2-09.7 pending revocation on restart

Status: source-only fake-port contract stacked on draft #3730. All live
protected pins remain empty, and #3690 is still an unadmitted source
observation.

Red-before controls found that `recover_pending` called native revoke again
after an already revoked token and after a prior attempt whose readback was
unknown. The finalizer now observes native state before mutation. A definite
`revoked` observation permits an exact pending-intent and receipt readback;
`unknown` remains pending. With a validated journal, a known duplicate
pending write and crash replay observe only, even when the token is still
active. Candidate invocation is
never replayed. A fresh execution may attempt native revoke after observing
`active`; cancellation after that attempt retains pending intent, and restart
can finish from a definite revoked observation without another native call.

If a crash occurs after mint but before durable pending intent, emergency
recovery still attempts native revocation. It skips a call when independent
native observation already says `revoked`. A missing or unknown observation
cannot establish exactly-once provider mutation; the emergency path remains
pending without a receipt.

The pending record must be included in the complete sealed high-water census
of #3716 and durably scheduled to the one-use recovery worker of #3715.
Source code does not install that scheduler or prove that an active token
after an ambiguous prior revoke can safely receive another native call.
An ambiguous pending write or unavailable journal also lacks a durable
one-use native-attempt readback; emergency revocation is best effort and
cannot claim an exactly-once effect.
Protected admission requires one shared, host-owned native-attempt identity
across initial finalization, crash recovery and the worker, or a separately
proved native idempotency key. The owner must prove the crash window between
durable attempt claim and provider call, native observation freshness, exact
mint/binding/vault/journal identity, complete minted-before-pending coverage,
and durable retry or escalation of unresolved pending subjects. Without this
authority, active or unknown native state stays pending; no protected receipt
or GS2-09.7 Q5/Q6 acceptance follows from these tests.

No signer/key installation, token release, sandbox dispatch, provider effect,
Authority write, protected merge, receipt or cutover is authorized here.
