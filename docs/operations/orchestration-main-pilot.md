# Bounded Main orchestration pilot

This is the operator contract for O2. It is not an activation receipt or a
substitute for the separately qualified Main deployment. The pilot may select
exactly one immutable `routine-documentation-delivery` WorkItem, with one
ordinary execution slot and one reserved recovery slot. Do not admit another
item or job class during the pilot.

The initial runner is trusted and cooperative. Route generations, enrollment
and revocation fence the supported route, but they cannot stop a runner that
has independently held authority from writing directly. The selected execution
context is the existing developer-owned Codex ChatGPT subscription session in
`fsharp-dev`; do not copy its login data or rebuild the container to adopt it.
The session and workspace currently live in the container's writable layer.

The execution adapter removes unnecessary GitHub, package-feed, telemetry,
publisher, database and operator credentials from its child's environment.
That is hygiene, not containment: the child still shares the developer's
filesystem and process authority. Stronger credential isolation remains
deferred. Codex login authorizes model execution, not the Host's GitHub effects;
candidate publication, PR creation and merge require separately configured and
verified GitHub authorization. No OpenAI API token or API-price provisioning is
required for this selected subscription path.

## Before any dispatch

Keep Main and the runner manually started and paused by default. A reboot or
process replacement invalidates startup readback and must recover in the
paused state; it never resumes dispatch automatically. Before resuming, the
operator must verify all of the following against current native state:

1. The one selected WorkItem, its repository identity, generation, workflow
   revision, allowed path and `routine-documentation-delivery` class are exact.
2. The stable driver has durably stopped and excluded only that WorkItem. No
   other stable-route scope is transferred or excluded.
3. The runner has been explicitly enrolled with its exact principal,
   fingerprint, generation and finite expiry.
4. A finite, nonrenewing permit binds that WorkItem, generation, principals,
   deadline, attempt ceiling, runtime limit and a supported versioned accounting
   policy. Record provider-native token coverage and unknown or not-applicable
   subscription cost explicitly; do not turn missing values into zero or claim
   a hard token ceiling that the adapter cannot enforce. A legacy numeric-cost
   permit is not silently reinterpreted as a subscription permit. Restart or
   reconnect must not mint a new permit or replenish a reservation.
5. The effect-free deployment and runner previews are ready, report no effects,
   and bind the installed Host/executor artifacts, selected container/session
   context, workspace/input/state roots, configuration, store, backup, fence,
   limits and accounting policy. Bind the units, broker, socket, DNS, TLS,
   certificate and credential identities required by the selected transport and
   effect route. Subscription execution does not require an API-pricing record.
   Refuse dispatch if any required binding is absent, unsupported, stale or
   changed; an older inert API-oriented preview cannot qualify this route.
6. Main has durably accepted a fresh provider readback for the current route,
   ownership generation and workflow revision after the most recent startup.

These requirements describe the intended subscription route, not installed
capability. Accepted source and immutable artifacts must implement the complete
Host-to-executor path, durable execution-session journal, digest-bound input and
independent candidate inspection before a preview can be ready. An adapter
library or actor factory alone does not satisfy that condition. Preserve the
existing closed runner wire; unsupported new execution/accounting versions must
refuse rather than silently fall back.

Telemetry health is not an orchestration authority. An outage must not block a
transition that the orchestration journal can safely make, but it must never
authorize, resume, retry, renew or widen work.

## One journal, seven ordered operations

Use one durable WorkItem journal for all seven operations below. For every
operation, persist intent before release and accept only a typed receipt bound
to the selected WorkItem, route, attempt, candidate, repository, generation,
workflow revision and observation window.

1. `AcquireExternalClaim` — acquire and natively read back the selected item's
   external claim at the current generation and revision.
2. `DispatchRunner` — release the single ordinary slot only to the enrolled
   runner while the permit, deadline and budget remain current.
3. `StoreCandidate` — accept and read back the digest-bound candidate in
   owner-controlled durable storage before any branch publication.
4. `PublishCandidateBranch` — publish only the accepted candidate head to the
   selected repository and branch namespace.
5. `CreatePullRequest` — create one pull request whose observed head is the
   stored candidate head.
6. `MergePullRequest` — use the protected delivery route only after its exact
   required checks pass; retain the native merge receipt.
7. `ReadNativeDelivery` — read back the pull request as merged, with its head
   equal to the stored candidate head, its merge commit equal to the merge
   receipt, and the selected path present at that protected merged revision.

These words describe different boundaries:

- **Accepted** means the journal durably admitted a command or receipt. It does
  not prove that an external effect happened.
- **Committed** means the provider reports the exact external effect as
  applied under the operation identity. It does not prove later delivery.
- **Reconciled** means a fresh operation-bound readback settled a previously
  unknown outcome as absent or applied. Only proven absence permits a retry,
  using the same operation identity and current authority.
- **Natively read back** means the owning provider was queried for the final
  repository, pull request, head, merge commit and protected-path facts.
  Runner, adapter, console or telemetry claims do not satisfy this boundary.

## Pause, reconcile and return to the stable route

At any ambiguity, pause new dispatch first. Preserve the assignment,
reservation, ownership generation, operation IDs, journal and candidate bytes.
Do not classify a timeout or lost response as failure, do not issue a new
permit, and do not start a replacement attempt while an outcome is unknown.

| Observation | Required reconciliation before fallback |
|---|---|
| Lost heartbeat or runner process death | Mark the attempt outcome unknown. Inspect the enrolled runner/session and all operation readbacks. Keep its reservation until a terminal runner observation or explicit reconciliation is durably recorded. |
| Disconnect and reconnect | Recover the same attempt, permit, budget, generation and monotone client/server cursors. Require fresh enrollment and route readback; reject replay, gaps and changed bytes. |
| Stale output | Reject it without changing the current candidate or journal. Compare attempt, generation, revision, assignment and content digests, then retain it only as diagnostic evidence. |
| Claim ambiguity | Read the native claim state for the same claim operation. If applied, record that receipt; if absent, retry the same operation under current authority; otherwise keep paused. |
| Artifact submission ambiguity | Query durable candidate storage by candidate ID and content digest. Continue only after exact bytes are read back; retry only after proven absence. |
| Pull-request creation ambiguity | Query the selected repository and branch for a matching PR and exact candidate head. Adopt the matching native result or retry only after proven absence. |
| Merge before receipt | Query the PR and protected branch before any merge retry. If merged, record its native merge commit and continue to delivery readback; if not provably absent or applied, remain paused. |
| Host reboot | Recover both journals and unsettled operations, append the startup pause, invalidate old readback, and require a fresh provider readback before any resume. |

Fallback to the stable driver only after pausing and reconciling every
assignment, claim, reservation and provider effect. Revoke the supported pilot
route, durably return ownership at the next generation, restore stable routing
only for this WorkItem, and verify that no duplicate owner or unresolved
recovery obligation remains. The recovery slot is for reconciliation and
compensation; it is not a second ordinary execution slot.

## Pilot completion evidence

O2 is complete only after one representative item reaches all seven operations
and the operator retains:

- the immutable WorkItem, route, attempt, permit, generation, workflow
  revision, runner enrollment and finite budget identities;
- current journal receipts for every operation, including each ambiguous or
  injected failure and its reconciliation;
- the durable candidate identity, baseline/head/tree identities, manifest and
  content digests, byte count, storage receipt and retention bound;
- the protected-route required checks bound to the exact PR head;
- native PR readback proving the stored candidate head, merged state and exact
  merge commit; and
- native protected-branch readback proving the selected path is present at the
  merged revision, followed by paused, reconciled ownership return with no
  duplicate route or unresolved effect.

Do not expand scope merely because the item merged. O3 adoption remains a
separate decision after this evidence and the remaining upstream gates pass.

## Authoritative source contracts

- [Standalone telemetry, durable host and optional orchestration roadmap](../roadmaps/2026-09-09-190726-standalone-telemetry-host-and-orchestration.md)
- [Coordination state and runner contract](https://github.com/FS-GG/FS.GG.Coordination/blob/b1160ff4fd19a8e8887401bdac2d3eb808f46b8c/docs/architecture/orchestration-state-and-runner-contract.md)
- [Coordination pilot permit boundary](https://github.com/FS-GG/FS.GG.Coordination/blob/b1160ff4fd19a8e8887401bdac2d3eb808f46b8c/docs/architecture/pilot-permit-boundary.md)
- [Coordination hosted-writer boundary](https://github.com/FS-GG/FS.GG.Coordination/blob/b1160ff4fd19a8e8887401bdac2d3eb808f46b8c/docs/architecture/orchestration-hosted-writer.md)
- [Coordination paused administration host and runner wire](https://github.com/FS-GG/FS.GG.Coordination/blob/b1160ff4fd19a8e8887401bdac2d3eb808f46b8c/docs/architecture/orchestration-administration-host.md)
- [Accepted Main deployment and runner broker source](https://github.com/EHotwagner/SystemAdmin/tree/1387ffdd0c61e6c8fcdd0670ded45842dfedf28b/Services/orchestration-main)

The linked revisions are source contracts, not live receipts. Keep credentials,
runtime identifiers and mutable operational evidence out of this document.
