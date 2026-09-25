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
| Existing sandbox App credential and token custody | Protected `.github` workflow/credential custodian | Re-read the existing App and installation IDs, actor, selected repository/grants, vault resource ID, token digest and mint binding; prove host-only key/token ACL and omit raw tokens. [ADR-0089](../adr/0089-reuse-the-protected-q4-sandbox-for-migration-rehearsal.md) requires no new maintainer-provisioned copy or credential. | Missing for GS2-09.7 |
| Signed joint head and monotonic floor | Protected signer/store owner | Pinned signer SPKI and policy digests, store/floor endpoints and resource IDs, signed challenge-bound current-head readback, monotonic crash recovery, and candidate/workflow write denial. | Missing |
| One shared native-attempt key | Protected durable-store owner | Exact `nativeAttemptResourceId`, endpoint and key `(mintId, tokenSha256)`; one linearizable winner across finalizer, recovery, and emergency paths; immutable attempt ID and readback after lost response and restart. Separate namespaces fail. | Missing |
| Emergency provider operation | Protected revoker/API owner | Exact revoker principal and native route, request identity, provider idempotency evidence **or** exact-token terminal readback with settled prior-call status; timeout and delayed-completion traces. | Missing |
| Store outage policy | Protected release and revoker owners jointly | Decision for main-store outage and for simultaneous shared-attempt-store outage, including token containment, operator escalation, and prohibition on a false receipt. | Missing |
| Q5/Q6 native result and receipt | Protected `.github` sandbox operator and Coordination migration/acceptance owner | Independently observed receiver/effect state and token revocation, exact durable intent/attempt/receipt lineage, the full rehearsal matrix below, and an explicit protected acceptance decision. | Missing |

The supplier must attach protected evidence with resource IDs, timestamps,
operation/attempt IDs, and immutable digests. It must omit private keys, raw
installation tokens, and candidate workspace secrets. A source pin, fake port,
or self-reported descriptor cannot fill a missing row.

## Accepted rehearsal scope and installed source boundary

The [GS2-09.7 roadmap](../github-substrate-v2-roadmap.md) and
[ADR-0089](../adr/0089-reuse-the-protected-q4-sandbox-for-migration-rehearsal.md)
reuse the registered private Q4 sandbox repository and Project 2. The
protected `.github` workflow supplies its existing App route; Coordination
supplies the migration interpreter and independent controls. No separately
provisioned copy or credential is a prerequisite. Both workflow and interpreter
must pin the App actor, sandbox repository node `R_kgDOUKXpqQ`, Project 2 node
`PVT_kwDOEYAWY84BiESo`, private status, purpose marker, exact candidate and
run nonce before every effect. Seed a bounded nonce-owned representative fixture
from the frozen corpus and capture its exact prestate. The App's organization
Projects grant extends beyond Project 2. A selected-repository token alone
does not bound Project writes. The live Coordination Project and production
Authority journal remain outside the effect target set.

Read-only source inspection of default `.github` `main` at
`2e553e41e58ee2f5e27aedcffc7403ce50e7cdd4` found the
[Q4 sandbox workflow](../../.github/workflows/github-substrate-v2-sandbox-qualification.yml),
[mint-proof workflow](../../.github/workflows/github-substrate-v2-sandbox-mint-proof.yml),
and `scripts/gs2-09-7-mint-sandbox-token.py`. The Q4 workflow still executes
the GS2-04.9 closure command; the mint-proof workflow checks grants and
revocation. That default-branch tree contains none of the stacked GS2-09.7
host signer, vault, joint-head/floor, shared-attempt or recovery port files.
The source references protected App secrets but cannot prove their live value,
ACL, or installed provider semantics. A Q4 or mint-proof run is not a Q5/Q6
migration receipt. #3690 remains an unadmitted source observation.

The [Coordination rehearsal contract](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/gs2-09-7-representative-rehearsal.md)
consumes the exact accepted GS2-09.6 receipt and GS2-09.9 callable handoff.
It requires these additional acceptance readbacks beyond emergency token cleanup:

| Gate | Exact evidence the responsible owner must supply |
| --- | --- |
| Q5 population and manifest | Two complete quiescent source and target reads over all nine authorities: `issues-open-and-relevant-closed`, `project-items`, `project-fields`, `hierarchy-and-dependencies`, `claim-and-event-streams`, `review-delivery-release-records`, `repository-settings`, `workflow-pins`, and `receiver-identities`. Seal the nonce-owned copy population, terminal pages, source bytes, predecessor receipts, exact manifest, transform decisions and operation dispositions; a missing page, newly added subject or unsupported authority refuses. |
| Q5 execution and archive | Use closed typed effects for native issue fields/types, hierarchy and blocking edges, Project state, body metadata, repository settings, receiver pins, schedules and archive publication, with exact prestate, protected journal CAS/generation and persisted intent before dispatch. Read back native relations, Project state, settings, receiver heads and archive records; verify the archive against frozen bytes with its retained verifier. Complete a distinct second migration round from a fresh generation, without reusing the first round's idempotency identity, and compare normalized results and receipts. |
| Q6 interruption and rollback | Interrupt before intent, after intent, before dispatch, after dispatch with lost response, after readback and before receipt. Recover in a fresh process, reconcile native state, prove no duplicate effect and settled replay of the exact completed operation. Restore authority snapshot, schedules, v1 projections, receiver pins and settings in reverse order from chained receipts; refuse rollback after `OpenV2`. |
| Independent controls and cleanup | Inject missing terminal page, unexpected subject/Project item, ambiguous transform, changed receiver head, wrong field type, missing native edge, altered manifest, stale journal generation, unavailable observer, partial settings effect and lost response. Verify nonce-owned cleanup, zero residue, token revocation and no unauthorized effect. Q5 live-fleet shadow remains read-only and separate from sandbox writes. |

No row can be closed by fake-port tests, historical Q4 evidence, a partial
provider capture, or this emergency qualification packet. The protected
acceptance owner must bind one exact candidate and native receiver readback to
the completed matrix before issuing a GS2-09.7 receipt. The retained report
must include provider IDs/revisions, request and journal identities, command
and artifact hashes, interruption points, effect counts, readback, duration,
API budget, refusal/unknown outcomes, cleanup and limitations.

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
