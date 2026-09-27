# V2 single-operator execution audit

The remaining roadmap can be prepared and driven by one accountable operator, agents, and protected CI.
It is **not yet executable end to end with the installed routes**. The remaining implementation work
includes migration execution, complete cutover orchestration, permission evidence, recovery, and
observation. The current protected cutover environment also prevents a sole run initiator from
approving their own deployment. [ADR-0090](../adr/0090-single-operator-v2-cutover-execution.md) proposes
an explicit one-owner profile while retaining a genuine human `OpenV2` confirmation.

This 2026-09-25 audit is an execution dependency map, not another acceptance ledger. The
[roadmap](../github-substrate-v2-roadmap.md), Coordination's
[typed index](https://github.com/FS-GG/FS.GG.Coordination/blob/main/eng/github-substrate-v2-units.json),
and exact accepted receipts remain authoritative. Existing accepted GS2-00–GS2-08 and GS2-09 child
receipts are historical evidence; settings and capabilities must still be observed at the new candidate.

## Observed authority and installed capability

Repository source was inspected at `.github` protected main `70ff9d6f74f53ba6be652c10aff4b037de1840b3`
and Coordination candidate `9fcc7c637b6ae628a2914deadb48a028d94b9a84`. Read-only provider calls on
2026-09-25 established the following facts. These readings expire when the provider state changes.

| Surface | Observation | Consequence |
|---|---|---|
| Ordinary source delivery | [ADR-0079](../adr/0079-single-accountable-delivery-authority.md); Coordination ruleset `21633423` requires zero PR approvals and six named technical checks | One owner can deliver qualified source. Plural review roles do not establish a second authorizer. |
| Existing migration sandbox | Registered private repository and Project 2; environment `github-substrate-v2-sandbox`, ID `20974131908`, has no reviewer rule | Reuse the protected Q4 App route, seed bounded fixtures, serialize shared targets and clean up. Current Q4 success proves that route, not migration acceptance. |
| Ordinary-v2 custody | `ordinary-v2`, ID `22664505321`, has a custom branch policy and no reviewer rule; the [installed record](../operations/v2-ci-i1-installed-qualification.md) binds dedicated custody and isolated runs | Routine post-open settlement can run without a credential relay. Production activation remains gated. Its Contents-only App cannot perform org administration or cutover journal writes. |
| Current cutover approval | `fleet-cutover`, ID `21550151971`, has reviewer IDs `1645484` and `4456104`, `prevent_self_review=true`, `can_admins_bypass=false`, exact `main` policy | A sole human who initiates the run cannot approve it under the current configuration. Listing a second reviewer does not establish their availability. |
| Current cutover execution | `fleet-cutover` secret inventory is empty; [authorization workflow](../../.github/workflows/gs2-ledger-protected-authorization.yml) emits a 90-minute initializer receipt and performs no provider write | Neither an approval receipt nor existing App registration proves an installed executor for later phases. Inspect other custody before concluding credentials are absent. |
| Source/live cutover drift | Coordination's [conformance predicate](https://github.com/FS-GG/FS.GG.Coordination/blob/9fcc7c637b6ae628a2914deadb48a028d94b9a84/src/FS.GG.Coordination.GitHub/LedgerProtectionConformance.fs) requires self-review prevention to be false; its plan and accepted unit contract list both reviewer IDs. The [installation manual](../2026-09-09-082700-gs2-08-2-ledger-protection-installation-manual.md) and live environment prevent self-review. | Reconcile the current contract, implementation and live policy before GS2-10. Do not rewrite the GS2-08.2 receipt or treat its historical acceptance as a fresh conformance result. |
| Executable roadmap frontier | Inspected typed index registers GS2-09.1–09.6 and GS2-09.9, but no GS2-09.7/.8 or GS2-10–14 | Register each remaining unit only with real command identities, prerequisite receipts, permission ceilings and qualification contracts. A roadmap edit creates no executable capability. |

## Remaining execution dependencies

Each row is work inside existing roadmap units. Owning repositories must carry its exact dependencies
in the typed index or the existing programme issue; this table does not authorize effects.

| Units | Autonomous route and owner | Evidence needed before advancing |
|---|---|---|
| GS2-09.7/.8 | Coordination supplies complete discovery, seed/migrate/recover/rollback/archive/cleanup interpreters; `.github` supplies the credential-owning sandbox workflow. Reuse the registered cohort and nonce-owned fixtures. | All nine authority families, exact dispositions, every interruption, two migration rounds, zero residue, omission/page-loss controls, and real Q5/Q6 acceptance. Separate sandbox CAS from production journal-protection evidence. |
| GS2-10.1–10.4 | Coordination snapshots and seals candidate inputs, runs Q0–Q7, schedules bounded shadow reads, explains differences and generates the manifest. | Complete live-fleet reading with a read-only credential; schema/settings/workflow/receiver coverage; exact immutable package and verifier bytes. Unreadable org policy is a missing observation, not permission to omit it. |
| GS2-10.5/.6/.9 | `.github` and receivers prepare exact switch PRs/settings plans, disposition backlog and stop candidate-input churn through native APIs and qualified tools. | Every receiver and administrative target has a bound interpreter, protected execution route, observed grants, expected prestate and rollback. No active release or settings saga crosses the window. |
| GS2-10.7 | `.github` orchestrates the entire cutover using Coordination interpreters in existing isolated resources. Capture the operator action trace as well as machine receipts. | Whole-sequence failure/restart exercises, candidate and target guards, credential acquisition inside CI, expiry handling, cleanup and verified restoration. Any additional isolated target requires a demonstrated capability gap. |
| GS2-10.8; GS2-12.10; GS2-14.4 | One accountable owner orchestrates separately generated architecture, security, operations, migration and receiver critiques, using separate black-box controls and provider readback; the owner's final verdict binds that evidence. | Exact candidate/manifest acceptance and each required technical gate. If a verifier requires a native or independent human identity, satisfy its real contract; role naming or a subagent cannot manufacture that identity. |
| GS2-11 | One operator's protected orchestrator publishes the prepared window notice, acquires the approved grant, closes ingress, drains work, applies temporary restrictions and takes two complete frozen reads. | Installed administrative and cutover authority, zero unsettled work, fresh epoch/manifest checks at each effect, tested abort and rollback. No normal V1/V2 overlap. |
| GS2-12 | Protected executors apply the exact prepared receiver changes, migration and settings plans; disable V1 execution; run Q8 canaries and wrong-path controls. | Complete readback of every target, independently assessed Q8, and executable pre-open rollback from current state. A workflow dispatch is not a settled effect receipt. |
| GS2-13.1/.2 | Produce the complete irreversible decision packet automatically; use a qualified protected approval profile for the exact `OpenV2` run. | Current profile requires another eligible approver when the owner initiated the run. Proposed ADR-0090 permits that same human to approve, but still requires a genuine human confirmation. Neither route is unattended agent approval. |
| GS2-13.3–13.10 | Installed ordinary-v2 execution handles the first real journey; scheduled observers capture operational readings and incidents; protected plans revoke V1 authoring and normalize safe settings. | Fresh open receipt and epoch, permanent old-client refusal, sealed assets, exact owner/SLO assignments and fixed observation definition. Reuse one accountable owner for those assignments. |
| GS2-14.1–14.3 | Schedule observations after real work completions and drive eligible authorized product work through V2. Attribute failures and repairs automatically. | Fifteen distinct real completions under the unchanged population and denominator, including failed/pending attempts. Do not create synthetic or trivial work to manufacture the gate; insufficient real work is a workload dependency. |
| GS2-14.4–14.12 | Generate the deletion ledger, independently verify Q9/Q10 and execute the accepted contraction plan; verify clean install and archive access; reconcile docs and close the Epic. | Required destructive-operation authority, exact delete targets and retention exclusions, no remaining V1 production path, accepted `OperatingV2` and all children. Contents-only journal custody does not grant field/App/settings deletion. |

## Pre-freeze execution inventory

GS2-10 readiness must contain one complete inventory of the operations already in the cutover manifest.
For each effect, bind the owning unit, exact interpreter/workflow artifact, trigger, target, installation
and permission ceiling, secret store reference, expected prestate, approval scope, durable attempt/receipt,
readback, and recovery action. Store public identities and fingerprints, never credentials. Cover read
acquisition, phase commits/tags, receiver merges, schema/Project/settings changes, schedules, releases,
credential revocation, archive publication, observation and eventual contraction.

Inspect existing protected installations before demanding a new secret, App, repository, host session
or operator relay. A failed local PAT call does not establish that trusted CI lacks the grant. Conversely,
repository admin flags, a named secret, a dry plan or an old receipt do not prove an effect is executable.
Rehearsal must exercise the installed route and its refusal/recovery controls. Keep secret-bearing code
on the trusted protected revision and select the already-qualified exact product artifact.

The action trace must reduce routine manual steps to automation. Remaining human entries name their
governing rule and prepared decision artifact. The known protected `OpenV2` confirmation remains;
unavailable administrator authority cannot be created by a design edit. Prepare each necessary change
and independent lane before reporting a concrete unresolved authority gap.

## Cutover approval profile transition

The 2026-09-25 provider readback still shows `FS-GG/.github:fleet-cutover` (environment ID
`21550151971`) with reviewer IDs `1645484` and `4456104`, self-review prevention enabled, no
administrator bypass, one custom `main` deployment branch, and no environment secrets. The
[installed workflow](../../.github/workflows/gs2-ledger-protected-authorization.yml) targets that
environment and only produces a short-lived initializer receipt. The proposed
[`fleet-cutover-owner` profile](../adr/0090-single-operator-v2-cutover-execution.md) has no observed
environment ID or installed execution route. The existing environment and its historical GS2-08.2
receipt must remain distinct from any future profile.

The cross-repository source changes have an ordered boundary:

1. Amend the governing design for the operation-specific approval policy, retain distinct
   architecture, security and operations critique evidence, then accept ADR-0090 before changing the
   workflow, provider plan, or conformance contract. Retain the historical accepted receipt unchanged.
2. Prepare the exact desired settings plan for `fleet-cutover-owner`: sole reviewer `1645484`,
   self-review permitted, five-minute wait, no administrator bypass, and only exact `main`. Keep its
   creation or update as a separately authorized administrative effect with a fresh readback.
3. Bind each cutover phase to one declared environment and exact candidate intent. The `.github`
   authorization workflow and Coordination's
   `LedgerProtectionPlanAdapter`, `LedgerProtectionProviderAdapter`, conformance predicate, capture
   script, native approval verifier, fixtures and operator instructions must agree on that binding.
   The current Coordination source still selects `fleet-cutover`; its plan lists both reviewers and
   its conformance predicate expects self-review prevention to be disabled, unlike live state.
4. Qualify the new contract against provider readback and native run evidence before adding it to the
   GS2-10 candidate. Exercise same-owner dispatch and genuine approval, then refuse absent or wrong
   approver, wrong run or attempt, wrong environment ID, non-`main` ref, stale candidate or intent,
   expiry, and changed policy. A generated test alone cannot supply the independent refusal controls.
5. Exercise the installed route in the registered isolated cutover rehearsal. Only the exact accepted
   candidate and later protected production decision may use it for a production phase; `OpenV2`
   retains its run-bound human confirmation.

The workflow hardening in [PR #3686](https://github.com/FS-GG/.github/pull/3686) checks repository,
manual dispatch, `main` and first attempt for the existing initializer receipt. It does not change
the approval profile or prove the later phase executor. A green source PR, a settings plan, or an
environment listing alone cannot close GS2-10.5.

## Acceptance limit

The audit supports a one-operator implementation plan. It cannot certify fully autonomous completion
while the protected approval profile, cutover interpreters, installed grants and real observation cohort
are unfinished. ADR-0090 is proposed; it neither changes the live environment nor retroactively grants
approval. Source and qualification work can continue while those dependencies are completed.
