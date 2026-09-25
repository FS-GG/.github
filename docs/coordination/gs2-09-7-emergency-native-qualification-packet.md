# GS2-09.7 emergency native-revocation qualification packet

Status: **hold; read-only owner decision needed**. Baseline is source-only
draft #3747 at `7c982197cc9ff1509f23d5ba7175a24164ba433c`. The
[finalizer source](../../scripts/gs2-09-7-host-refusal-finalizer.py) attempts
best-effort revoke when its store cannot be trusted. A fake restart test shows
two calls after a lost response and unknown observation. Both results remain
pending and candidate handoff is blocked. The [owner decision](gs2-09-7-emergency-native-owner-decision.md)
describes the required authority; this packet assigns each missing fact to a
supplier role. The reviewed GS2-09.7 source and decision notes do not name
installed protected principals or provide their evidence, so each assignment
below needs owner confirmation.

## Decision to record before any protected rehearsal

The protected release owner must select one emergency policy and name the
principals responsible for it:

| Policy | Required proof | Current verdict |
| --- | --- | --- |
| Retry through a provider-supported idempotent operation | An independently verified attempt key with the same effect across timeout, process restart, and concurrent callers; protected traces must show the exact call and native result. | Unproved; hold. |
| Resolve through authoritative native token readback | Exact-token observation and settled prior-call status. `revoked` is terminal; `active` is insufficient while an earlier call may still be in flight; `unknown` stays pending. Any later call needs a separate protected one-use decision. | Unproved; hold. |
| Neither capability available | A protected incident/containment decision for a possibly active token, with no accepted receipt or automatic replay claim. Current best-effort emergency behavior cannot be qualified as one-use. | Requires owner decision; hold. |

No source test can establish provider idempotency or settled native state. A
durable marker proves one protected claim, but a crash after marker commit and
before or after the external call leaves delivery ambiguous without one of the
first two proofs.

## Suppliers and missing evidence

The protected release owner must designate a named human or service principal
for each supplier role. The roles below are responsibilities to assign, not
claims that an installed team or service already owns them.

| Protected fact | Supplier to designate | Evidence required from the installed boundary | State |
| --- | --- | --- | --- |
| Admitted workflow and candidate | Protected release/admission owner | Immutable admitted workflow SHA and path; exact candidate SHA, run ID, attempt, nonce, sandbox target, and decision ID; native protection/readback rather than a candidate-supplied SHA. #3690 must receive a separate admission decision. | Missing |
| App credential and token custody | Protected credential/vault custodian | App and installation IDs, actor, selected repository/grants, vault resource ID, token digest and mint binding; host-only key/token ACL and no raw token in the packet. | Missing |
| Signed joint head and monotonic floor | Protected signer/store owner | Pinned signer SPKI and policy digests, store/floor endpoints and resource IDs, signed challenge-bound current-head readback, monotonic crash recovery, and candidate/workflow write denial. | Missing |
| One shared native-attempt key | Protected durable-store owner | Exact `nativeAttemptResourceId`, endpoint and key `(mintId, tokenSha256)`; one linearizable winner across finalizer, recovery, and emergency paths; immutable attempt ID and readback after lost response and restart. Separate namespaces fail. | Missing |
| Emergency provider operation | Protected revoker/API owner | Exact revoker principal and native route, request identity, provider idempotency evidence **or** exact-token terminal readback with settled prior-call status; timeout and delayed-completion traces. | Missing |
| Store outage policy | Protected release and revoker owners jointly | Decision for main-store outage and for simultaneous shared-attempt-store outage, including token containment, operator escalation, and prohibition on a false receipt. | Missing |
| Q5/Q6 native result and receipt | Protected sandbox operator and acceptance owner | Independently observed receiver/effect state and token revocation, exact durable intent/attempt/receipt lineage, complete Q5/Q6 evidence, and an explicit acceptance decision. | Missing |

The supplier must attach protected evidence with resource IDs, timestamps,
operation/attempt IDs, and immutable digests. It must omit private keys, raw
installation tokens, and candidate workspace secrets. A source pin, fake port,
or self-reported descriptor cannot fill a missing row.

## Required read-only qualification traces

Each trace needs a before/after durable-state read, provider-call count or
provider idempotency evidence, exact-token native observation, and the final
pending/receipt disposition. The protected owner must record the observer and
the installed adapter digest for every trace.

| Trace | Required result |
| --- | --- |
| Normal finalizer and recovery race for one mint | One shared marker winner; loser makes no second provider call. |
| Main finalizer store unavailable before pending intent | Candidate handoff refused; emergency outcome remains pending until protected intent and native state are reconciled. |
| Marker committed, process crashes before provider call | Marker survives restart; later call occurs only under proved idempotency or a new protected decision based on settled native state. |
| Provider call possibly sent, response lost, process restarts | No second call from an `active` or `unknown` snapshot alone; eventual `revoked` observation and durable intent are separately read back. |
| Main store and independent attempt authority unavailable | Containment/incident branch follows the recorded owner policy; no source-only one-use or receipt claim. |
| Concurrent head advance, withdrawal, and recovery claim | Protected transaction order and exact generation/floor/claim binding are observed; an old or foreign marker cannot authorize a new call. |
| Restored store and native token state | Reconcile every pending mint, marker and receipt without omission or duplicate effect; unresolved subjects remain pending. |

Q5/Q6 native receiver readback and a protected receipt remain separate from
this qualification. Live pins are blank, #3690 is unadmitted, and no token
release, sandbox effect, Authority write, merge, receipt, or cutover follows
from this packet.
