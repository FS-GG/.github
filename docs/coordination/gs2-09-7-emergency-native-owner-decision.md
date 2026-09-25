# GS2-09.7 emergency native-revocation owner decision

The source-only finalizer attempts best-effort native revoke when its claim
store cannot be trusted. It returns `pending` and cannot issue a protected
receipt. A fake restart control now shows why this cannot establish one-use
provider ordering: the first host loses the native response, a second host
receives unknown native observation, and both call revoke while candidate
handoff remains blocked. No live token or provider was used.

## Decision the protected owner must make

The protected owner must choose and qualify the emergency revocation authority
before Q5/Q6. A complete installed design needs an independently credentialed,
candidate-inaccessible durable native-attempt key for the exact token digest and
mint binding, shared by normal finalization, recovery, and emergency cleanup.
It must commit an attempt or a durable possible-call state before handing the
token to the provider adapter, preserve that state across process restart, and
return one immutable attempt identity to every caller. An outage of the main
finalizer store cannot silently switch to a second namespace. The owner must
state how an outage of the independent authority is handled while the token
may remain active; source code cannot safely infer a native result.

The owner must demonstrate one of these provider-side resolution capabilities
for an unknown call result:

1. A native operation with independently verified idempotency under a durable
   attempt key, including behavior after timeout, connection loss, and restart;
   or
2. An authoritative native token-state observation that distinguishes active,
   revoked, and unknown for the exact minted installation token, plus a
   protected decision for any later call when the prior call may have reached
   the provider.

If neither can be shown, a durable marker alone cannot tell whether a crash
happened before or after the provider call. The result stays pending, with no
automatic second native call and no receipt; the owner must arrange protected
incident handling and token expiry/containment for the unresolved token.
Current `_emergency_revoke` does issue a best-effort second call under an
unknown readback, so it is **not qualified** as a one-use installed adapter.
This draft records the limitation; it does not change the emergency behavior
without an approved protected replacement.

## Evidence packet before release

The owner must supply the exact deployed adapter and workflow digests, admitted
run/candidate/attempt/nonce and target repository, App/installation/actor,
vault and pending-journal identities, shared native-attempt resource ID and
endpoint, revoker principal and GitHub API route, signer/floor pins, and ACL
evidence excluding candidate and ordinary workflow writes. Capture native
call and token-state readback identifiers without raw token material. Show
protected traces for: main-store outage before pending, crash after durable
marker before call, timeout after possible call, process restart with unknown
observation, concurrent finalizer/recovery/emergency workers, head advance,
and restored store readback. Each trace must show at most one provider call or
the exact provider idempotency proof permitting replay, and must show no
receipt until durable intent plus native revoked state are both observed.

#3690 remains an unadmitted observation. Live pins stay blank. Q5/Q6, token
release, sandbox dispatch, Authority write, receipt, merge, and cutover remain
held.
