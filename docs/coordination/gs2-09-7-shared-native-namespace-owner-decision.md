# GS2-09.7 shared native-attempt namespace decision

This source-only draft requires a pinned `nativeAttemptResourceId` in the
protected finalizer descriptor and every native-attempt marker. An absent or
foreign identity now refuses candidate handoff; the recovery worker uses the
same finalizer pin and marker readback. The pin is blank in production source.
No store, signer, credential, provider call, or sandbox receiver is installed.

## Exact protected owner evidence

The protected owner must identify one durable native-attempt resource and key
space for `(mintId, tokenSha256)`. Both `claim_native_attempt_once` from the
refusal finalizer and `claim_recovery_native_attempt_once` from recovery must
call that resource. The recovery operation additionally compares the current
signed joint head generation, pinned floor resource, and exact durable recovery
claim. The owner must show a serializable trace with one winner when both
operations race, including reversed order, a lost winner response, process
restart, and a foreign marker. The losing path may observe and finalize but
must not issue another native provider call. Marker reads must use the same
resource and return the immutable winning attempt ID; separate tables or
per-worker namespaces are not sufficient even if both descriptors display the
same ID.

The owner must attest the deployed adapter digest, namespace endpoint and
resource ID, signer/store/floor IDs and keys, policy digest, revoker principal,
and ACLs. Candidate workspaces and ordinary workflow principals must have no
marker, head, floor, vault, pending journal, or recovery-claim write access.
The source descriptor and fake-port controls are configuration checks, not
evidence of that deployed ACL or transaction.

## Provider-call ordering hold

The ordinary paths write and read back the shared marker before one native
revoke call. A lost or unknown response leaves the token pending for native
observation; the source paths do not retry a fresh marked revoke. However,
`_emergency_revoke` deliberately bypasses the marker when the finalizer store
is unavailable so an exposed token still receives a best-effort revoke. A
fake unknown native readback can make two emergency invocations call revoke
twice. Both remain pending with no protected receipt, but source alone cannot
prove global one-attempt behavior or provider idempotence in that failure mode.

The protected owner must decide and demonstrate the native call's ordering
relative to marker commit, head publication, emergency cleanup, and token
observation. The evidence must cover crash after marker before provider call,
crash or timeout after provider call before response, store outage during
emergency cleanup, and recovery/finalizer overlap. If the native API lacks an
independently observed idempotent operation or a protected one-use revoker
authority, the owner must keep the result pending and Q5/Q6 held; a fake
observation or candidate claim cannot supply that authority.

#3690 remains an unadmitted observation. This draft does not admit token
release, sandbox dispatch, Authority receipt, merge, or cutover.
