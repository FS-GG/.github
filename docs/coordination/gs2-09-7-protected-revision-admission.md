# GS2-09.7 protected workflow revision admission hold

Status: source-only, disabled. This draft is stacked on #3718. It changes no
workflow, installs no key or protected authority, and does not admit #3690 or
issue a GS2-09.7 Q5/Q6 receipt.

Two independent negative controls failed red-before: the host signer accepted
`workflowSha` and `protectedSha` changed together to a different valid SHA,
and the host release verifier accepted a valid signature over that changed
SHA. Equality with the runner checkout is useful but does not establish that
the checkout revision was admitted for the protected run. The signer and
release verifier now require an exact independently supplied workflow commit
SHA in addition to the existing runner and signed-envelope equality checks.
An absent admission SHA refuses before signing or token handoff. The source
placeholder remains empty.

The protected release authority must publish a durable, immutable admission
record outside the candidate workspace and workflow checkout. It must bind
one reviewed workflow commit SHA, exact candidate SHA, run ID and attempt,
derived nonce, sandbox target, signer key identity, and the host release
policy revision. The host must authenticate and read that record through a
separately pinned protected identity before App secret use, then supply its
workflow SHA to these source functions. That authority is not implemented or
configured here. A workflow commit cannot contain a source constant pinning
its own SHA: adding the constant would change the SHA. An environment value
from the same runner is also insufficient as independent admission.

No local test establishes real runner variable provenance, signer FD custody,
vault ACL, native token readback or host journal durability. #552 remains
candidate consistency only; #558 needs a protected trust anchor and expected
run facts. The #3690 workflow still sends the minted token directly to
candidate execute/cleanup. Source green cannot authorize sandbox dispatch,
protected merge, receipt, or cutover. GS2-09.8 acceptance remains dependent
on a protected GS2-09.7 receipt.
