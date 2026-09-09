---
title: GS2-08.2 ledger-protection installation manual
category: FS.GG
description: Operator runbook for establishing the dedicated GitHub Apps, protected refs and tags, fleet-cutover environment, control issue, custody, monitoring, and conformance evidence required by GS2-08.2.
---

# GS2-08.2 ledger-protection installation manual

Status: current operator manual, written 2026-09-09 08:27 Europe/Vienna.

This manual turns the qualified GS2-08.2 source contract into an executable human operation. It is for the
organization owner, secret custodian, cutover reviewers, and independent verifier who will install the protected
fleet ledger boundary. It also states what an implementation agent needs from those people and what evidence must
exist before GS2-08.2 can be accepted.

The present preparation is **not apply authority**. Coordination's retained preparation has
`ApplyAuthorized=false`, no provider writes, and state `CurrentPreInstall`. Following this manual requires a new,
explicitly approved administrative operation bound to a fresh pre-state observation and plan. The existing
`gh` user token is useful for observation but is not one of the required runtime identities.

## 1. Fixed contract

Do not reinterpret these values during installation:

| Concern | Required value |
|---|---|
| Fleet identity | `fleet-cutover:fs-gg-production` |
| Authority repository | `FS-GG/FS.GG.Coordination.Authority` (repository id `1351660651`) |
| Fleet ledger ref | `refs/heads/fsgg/v2/journal/cutover/d5` |
| Journal namespace | `refs/heads/fsgg/v2/journal/**/*` |
| Phase-tag namespace | `refs/tags/fsgg/v2/fleet-cutover/**/*` |
| Ordinary writer | A dedicated GitHub App, selected-repository installation, only `contents: write` |
| Cutover writer | A different dedicated GitHub App, selected-repository installation, only `contents: write` |
| Installed repository for both Apps | Only `FS-GG/FS.GG.Coordination.Authority` |
| Shared App exception | Forbidden; `fs-gg-cross-repo-dispatch` App id `4166418` must not cover the fleet ref |
| Protected environment | `FS-GG/.github:fleet-cutover` |
| Required reviewers | `EHotwagner` (id `1645484`) and `nuklearwanze` (id `4456104`) |
| Environment self-review | Prevented |
| Administrator bypass | Disabled |
| Deployment branch policy | Custom patterns only; exact pattern `main` |
| Control issue | One ordinary issue in `FS-GG/FS.GG.Coordination.Authority`; projection, never authority |

GitHub automatically grants a GitHub App read access to metadata. Here, “contents-only” therefore means the
explicit permission set is `contents: write`, with only implicit `metadata: read`; every other repository,
organization, account, and user permission is `No access`.

### Why the existing App token is not equivalent

The currently installed `fs-gg-cross-repo-dispatch` App has an organization-wide installation and a broad
permission set that includes administration and several write surfaces. Its installation tokens are short-lived,
but they retain the authority granted to the token for their lifetime. Reusing that identity for the fleet ledger
would have four concrete consequences:

- compromise or accidental disclosure would have a fleet-wide rather than single-repository blast radius;
- an ordinary coordination defect could reach the exceptional cutover ref;
- the same audit identity would represent routine journal work and protected cutover transitions;
- later permission or installation expansion of the shared App would silently expand ledger authority.

Repository-scoping an individual token is useful defense in depth, but it does not replace a selected-repository,
contents-only installation and distinct ruleset bypass identity. A temporary shared-App exception would therefore
require an explicit, time-bounded security acceptance that names these residual risks and a removal deadline. The
current desired policy deliberately forbids that exception.

The source contract is in the `FS.GG.Coordination` repository:

- `evidence/github-substrate-v2/gs2-08-2/desired-policy.json` is the machine-readable desired policy;
- `src/FS.GG.Coordination.GitHub/LedgerProtectionPlanAdapter.fs` defines the exact refs and plan;
- `src/FS.GG.Coordination.GitHub/LedgerProtectionConformance.fs` defines accepted composition and states;
- `eng/capture-github-ledger-protection.py` performs the read-only provider capture;
- `eng/validate-github-ledger-protection-provider.fsx` validates retained captures.

## 2. Roles and separation of duties

Assign these roles before opening the change window. One person may hold more than one role, but the authorizer and
independent verifier should be different people.

| Role | Responsibility |
|---|---|
| Operation authorizer | Approves one exact sealed plan after reviewing the refreshed pre-state |
| Organization App manager | Registers both Apps and installs each on the single selected repository |
| Secret custodian | Generates, stores, rotates, and revokes private keys without exposing key bytes |
| Repository administrator | Creates rulesets and the non-authoritative control issue |
| Environment administrator | Creates the `.github` environment and its protection rules |
| Cutover reviewers | Approve protected cutover jobs; cannot approve their own deployment |
| Independent verifier | Performs both post-change captures and checks exact conformance |

Use two operator sessions where practical: one performs the mutations and the other performs readback. Do not paste
a private key into an issue, PR, terminal transcript, telemetry event, shell history, or retained evidence file.

## 3. Inputs and evidence directory

Create a private operation record outside Git before doing anything. It should contain no private-key material.
Record:

- operation id and UTC start time;
- authorizer, operator, custodian, reviewers, and verifier;
- exact `.github` and `FS.GG.Coordination` source revisions;
- fresh pre-state capture digest and sealed-plan digest;
- ordinary App id, slug, installation id, and selected repository;
- cutover App id, slug, installation id, and selected repository;
- secret-manager record identifiers and public-key fingerprints, never keys;
- ruleset ids, environment URL, control-issue number, initialization commit, and tag object ids;
- first and second post-install capture SHA-256 values and normalized-set digests;
- every deviation, refusal, rollback action, and final disposition.

Use a private content-addressed receipt store if one is available. A protected incident/change record is acceptable
for the initial operation. The evidence copied into Git must be sanitized and contain identifiers and hashes only.

## 4. Preflight and stop conditions

Start from clean, current checkouts. The capture tool requires `gh`, Python 3, .NET, and an authenticated principal
that can read repository rulesets, organization installations, environments, refs, and issues.

```bash
gh auth status
gh api repos/FS-GG/FS.GG.Coordination.Authority --jq '{id,full_name,default_branch}'
gh api repos/FS-GG/.github --jq '{id,full_name,default_branch}'
```

From a current `FS.GG.Coordination` checkout, take two fresh read-only captures. Store working captures outside the
checkout; do not overwrite the historical qualification fixtures.

```bash
operation_dir=$(mktemp -d)
python3 eng/capture-github-ledger-protection.py \
  --output "$operation_dir/prestate-pass1.json"
python3 eng/capture-github-ledger-protection.py \
  --previous "$operation_dir/prestate-pass1.json" \
  --output "$operation_dir/prestate-pass2.json"
sha256sum "$operation_dir"/prestate-pass*.json
```

The second command must report `continuity=matched` and `gaps=0`. Stop and replan if either capture is incomplete,
the normalized set drifts, an endpoint is unreadable, the authority repository id differs, or any expected absence
is merely unknown.

Before live installation, the implementation must also be updated and qualified to bind the real
`ordinaryWriter.appId`, `cutoverWriter.appId`, and `controlIssue.number`. At the time of writing, the desired-policy
values are null and the capture decoder deliberately constructs unbound identities. Do not call an installation
complete merely because the GitHub UI looks right; the machine conformance path must be capable of recognizing it.

The authorizer now reviews the fresh observation and a regenerated operation plan. The authorization receipt must
bind its exact observation digest, desired-policy digest, operation order, real App ids, issue number, and expiry.
Any provider change after authorization invalidates that plan. Never change `ApplyAuthorized` in a fixture as a
substitute for an authorization receipt.

## 5. Register the two GitHub Apps

GitHub's organization owner opens **Organization settings → Developer settings → GitHub Apps → New GitHub App**.
GitHub documents the registration fields and ownership model in
[Registering a GitHub App](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/registering-a-github-app)
and the least-privilege choice in
[Choosing permissions for a GitHub App](https://docs.github.com/en/apps/creating-github-apps/registering-a-github-app/choosing-permissions-for-a-github-app).

Register two private Apps. Recommended names and role labels are:

| App | Recommended name | Contract role |
|---|---|---|
| Ordinary journal writer | `FS.GG ordinary journal writer` | `dedicated-ordinary-contents-only` |
| Fleet cutover writer | `FS.GG fleet cutover writer` | `dedicated-cutover-contents-only` |

For each App:

1. Set ownership to the `FS-GG` organization and keep the App private to that owner.
2. Use an informational homepage URL under the organization. No callback URL is required.
3. Disable user authorization. Do not request user-to-server permissions.
4. Disable webhooks. The v2 cutover design explicitly has no hosted App/webhook runtime on this path.
5. Under repository permissions, grant **Contents: Read and write**.
6. Leave every other configurable repository permission at **No access**.
7. Leave all organization, account, and user permissions at **No access**.
8. Do not request events; webhooks are disabled.
9. Restrict where the App can be installed to **Only on this account**.
10. Create the App and record its numeric App id and slug.

Compare the two permission screens side by side. They must be distinct Apps with identical minimal permission
sets; role separation comes from their identities and ruleset bypass placement, not broader permissions.

## 6. Establish private-key custody

For each App, the secret custodian opens the App settings and generates a private key. GitHub downloads the key
once; GitHub stores the public half. Follow
[Managing private keys for GitHub Apps](https://docs.github.com/en/apps/creating-github-apps/authenticating-with-a-github-app/managing-private-keys-for-github-apps)
and the key-rotation guidance in
[GitHub App best practices](https://docs.github.com/en/apps/creating-github-apps/about-creating-github-apps/best-practices-for-creating-a-github-app).

Immediately:

1. Move the downloaded PEM into the approved secret manager.
2. Give ordinary and cutover keys separate records and access policies.
3. Allow only the workflow/reconciler that needs a role to read that role's key.
4. Record the secret-manager record id, App id, creation time, custodian, and public fingerprint.
5. Remove the downloaded plaintext copy using the workstation's approved secure-disposal procedure.
6. Confirm the key is absent from shell history, clipboard history, Downloads, Git, logs, and operation evidence.

Canonical secret metadata names are:

| Value | Name | Secret? |
|---|---|---|
| Ordinary App id | `FSGG_ORDINARY_LEDGER_APP_ID` | No |
| Ordinary installation id | `FSGG_ORDINARY_LEDGER_INSTALLATION_ID` | No |
| Ordinary PEM | `FSGG_ORDINARY_LEDGER_APP_PRIVATE_KEY` | Yes |
| Cutover App id | `FSGG_CUTOVER_LEDGER_APP_ID` | No |
| Cutover installation id | `FSGG_CUTOVER_LEDGER_INSTALLATION_ID` | No |
| Cutover PEM | `FSGG_CUTOVER_LEDGER_APP_PRIVATE_KEY` | Yes |

These names define the future consumer contract; creating GitHub Actions secrets is not useful until a reviewed
workflow consumes them. Prefer organization secrets restricted to the exact consumer repository over duplicated
repository secrets. Never make either PEM available to all repositories.

Rotate without downtime by creating a second key, deploying and proving it, then revoking the old key. If a key may
have escaped custody, revoke it immediately, suspend or uninstall the affected App if necessary, freeze ledger
writes, and treat every write since the last trusted observation as suspect.

## 7. Install each App on exactly one repository

Open each App's public page, select **Install**, choose `FS-GG`, select **Only select repositories**, and select only
`FS.GG.Coordination.Authority`. GitHub's installation flow is described in
[Installing a GitHub App from a third party](https://docs.github.com/en/apps/using-github-apps/installing-a-github-app-from-a-third-party).

Record both installation ids. Read them back with an organization-owner session:

```bash
gh api --paginate --slurp 'orgs/FS-GG/installations?per_page=100' \
  --jq 'map(.installations // .) | flatten | map({id,app_id,app_slug,repository_selection,permissions})'
```

For each installation id, prove that the selected set is complete and exact:

```bash
gh api --paginate --slurp \
  'user/installations/INSTALLATION_ID/repositories?per_page=100' \
  --jq 'map(.repositories // .) | flatten | map(.full_name) | sort'
```

The result must be exactly `["FS-GG/FS.GG.Coordination.Authority"]`. Stop if either installation says `all`, if a
second repository appears, if pagination is incomplete, or if permissions contain any explicit permission other
than `contents: write` (implicit `metadata: read` is allowed).

## 8. Create the protected `fleet-cutover` environment

In `FS-GG/.github`, open **Settings → Environments → New environment** and name it exactly `fleet-cutover`.
Configure:

1. Required reviewers: `EHotwagner` and `nuklearwanze`.
2. Prevent self-review: enabled.
3. Allow administrators to bypass configured protection rules: disabled.
4. Deployment branches and tags: selected/custom rules, not all branches and not protected branches.
5. One deployment branch rule: `main`.
6. No environment secrets yet unless a reviewed cutover workflow consumes them.

GitHub's field semantics are documented in
[Deployments and environments](https://docs.github.com/en/actions/reference/workflows-and-actions/deployments-and-environments).
Read back the environment and deployment policies:

```bash
gh api repos/FS-GG/.github/environments/fleet-cutover
gh api --paginate --slurp \
  'repos/FS-GG/.github/environments/fleet-cutover/deployment-branch-policies?per_page=100'
```

The reviewer ids must be exactly `1645484` and `4456104`; `prevent_self_review` must be true;
`can_admins_bypass` must be false; the policy must use custom branches with only `main`.

The environment approves execution; it does not grant repository contents access. The cutover App installation
and its private key provide that identity. The ordinary App must never be used by a job targeting this environment.

## 9. Create and verify the five rulesets

Create repository rulesets in `FS-GG/FS.GG.Coordination.Authority`. GitHub documents organization/repository
ruleset creation and bypass actors in
[Creating rulesets for repositories in your organization](https://docs.github.com/en/organizations/managing-organization-settings/creating-rulesets-for-repositories-in-your-organization)
and ruleset composition in
[About rulesets](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/about-rulesets).

The final effective composition is exact:

| Ruleset | Target/include | Exclude | Restrictions | Always-bypass actor |
|---|---|---|---|---|
| Ordinary journal writer | Branch: `refs/heads/fsgg/v2/journal/**/*` | Fleet ref | creation, update | Ordinary App only |
| Fleet cutover writer | Branch: exact fleet ref | none | creation, update | Cutover App only |
| Journal integrity | Branch: journal namespace | none | deletion, non-fast-forward | none |
| Phase-tag creation | Tag: phase-tag namespace | none | creation | Cutover App only |
| Phase-tag integrity | Tag: phase-tag namespace | none | update, deletion | none |

In the GitHub UI, the corresponding restrictions are **Restrict creations**, **Restrict updates**,
**Restrict deletions**, and **Block force pushes**. Set enforcement to **Active**, not Evaluate or Disabled. Do not
add an organization-admin bypass, repository-role bypass, team bypass, or pull-request-only bypass.

Apply them in this lockout-safe order:

1. Add the exact fleet-ref exclusion to the existing ordinary/shared writer ruleset while preserving its current
   journal selector and ordinary App bypass. If the current rule still names shared App `4166418`, replace its
   bypass with the new ordinary App in the same reviewed plan.
2. Create the exact fleet-writer ruleset with only the cutover App bypass.
3. Verify the existing journal-integrity ruleset still blocks deletion and non-fast-forward changes with no bypass.
4. Create the phase-tag creation ruleset with only the cutover App bypass.
5. Create the phase-tag integrity ruleset with no bypass.
6. Re-read all rulesets, then query effective rules for the exact fleet branch.

The first row's exclude must be the exact `refs/heads/fsgg/v2/journal/cutover/d5`, not the whole cutover namespace.
The fleet writer must not bypass journal deletion/non-fast-forward protection. The cutover writer may create a
phase tag but may not update or delete one after creation.

```bash
gh api --paginate --slurp \
  'repos/FS-GG/FS.GG.Coordination.Authority/rulesets?includes_parents=true&per_page=100'

gh api \
  'repos/FS-GG/FS.GG.Coordination.Authority/rules/branches/fsgg%2Fv2%2Fjournal%2Fcutover%2Fd5'
```

For every listed ruleset id, read its detail rather than trusting the summary:

```bash
gh api repos/FS-GG/FS.GG.Coordination.Authority/rulesets/RULESET_ID
```

Record all five ids. A ruleset is not conformant if another inherited or repository ruleset changes the effective
composition, even when these five rows individually look correct.

## 10. Bind the control issue

Create one ordinary issue in `FS-GG/FS.GG.Coordination.Authority` titled `Fleet cutover control — FS.GG production`.
The body should show:

- fleet identity and exact fleet ref;
- current projected phase and ledger commit;
- current manifest or evidence digest;
- latest successful monitoring observation and time;
- operator guidance and escalation contacts;
- a prominent statement that the protected Git ledger, not the issue, is authority.

Do not encode a command in issue labels, title, body, comments, reactions, assignees, or status. Automation may
update the projection only after it has read and verified the authoritative ledger state.

Record the issue number in the desired policy and the qualified binding implementation. Confirm it is an issue,
not a pull request:

```bash
gh api repos/FS-GG/FS.GG.Coordination.Authority/issues/ISSUE_NUMBER \
  --jq '{number,title,state,isPullRequest:has("pull_request")}'
```

## 11. Initialize the fleet ledger

Do not initialize until both App tokens have been minted successfully, the rulesets and environment read back
exactly, the control issue is bound, and a newly refreshed plan has explicit authority.

Mint installation tokens only at use time. A token must be scoped to the App's one selected repository and should
have the shortest practical lifetime. Never retain it in evidence. Prove role separation before the first write:

- the ordinary App can write another permitted journal ref but is refused on the fleet ref;
- the cutover App can create/update the fleet ref but cannot delete or rewind it;
- neither App can update or delete an existing phase tag;
- only the cutover App can create a phase tag;
- the shared App is refused on the fleet ref.

Use disposable test refs under the protected selectors where possible and delete only refs whose deletion is
explicitly allowed. If exact production selectors make a safe disposable test impossible, prove authorization via
GitHub's ruleset evaluation/readback and make the expected-parent initialization the first write.

The initial fleet commit must encode `OperatingV1`, bind the exact fleet identity and manifest, and be created with
an expected-absent parent precondition. Create `refs/heads/fsgg/v2/journal/cutover/d5` at that commit using the
cutover App. Create the corresponding protected phase tag only after rereading the branch and verifying its exact
object id. Never force-update either ref.

The exact commit and tag payloads belong to the frozen epoch wire contract and its journal adapter; do not invent a
second JSON shape in an operator script. If the production writer command that emits those canonical bytes is not
present and qualified, stop here. Manual `git commit`, `git push --force`, and hand-written JSON are not acceptable
substitutes.

## 12. Enable monitoring

Monitoring is a required dimension of `InstalledProductionProtection`, not optional follow-up. Install a scheduled
read-only job that runs the same complete capture surface and compares it with the last accepted observation. It
must cover:

- repository identity and revision;
- complete ruleset pages and every ruleset detail;
- effective rules on the fleet branch;
- classic branch-protection overlap;
- fleet head and all phase tags with object ids;
- environment reviewers, self-review, admin bypass, and branch policy;
- both App installations, permissions, and complete selected-repository sets;
- bound control issue identity;
- continuity, raw-set digest, normalized-set digest, and observation freshness.

Any unknown, pagination gap, changed App permission, additional selected repository, unexpected bypass, missing
integrity rule, rewind, deletion, moved tag, environment weakening, or unbound identity is red. Monitoring must not
repair automatically. It records a content-addressed alert, blocks cutover activity, and requires a fresh
observe/plan/authorize/apply/verify cycle.

Store routine high-volume observations in the approved private telemetry store rather than committing a Markdown
report for every tick. Retain in Git only stable schemas, operator documentation, sanitized acceptance evidence,
and exceptional incident reports.

## 13. Two-pass post-install conformance

After settings, custody, initialization, and monitoring are ready, take two new captures with no intervening
administrative activity:

```bash
python3 eng/capture-github-ledger-protection.py \
  --output "$operation_dir/postinstall-pass1.json"
python3 eng/capture-github-ledger-protection.py \
  --previous "$operation_dir/postinstall-pass1.json" \
  --output "$operation_dir/postinstall-pass2.json"
sha256sum "$operation_dir"/postinstall-pass*.json
```

The qualified implementation must classify the second capture as:

- `InstalledFleetProtection` once the exact provider composition is installed but one or more operational
  dimensions remain false; or
- `InstalledProductionProtection` only when settings, App custody, fleet initialization, and monitoring are all
  independently observed true.

`IncompleteOrUnknown` and `DriftOrTamper` are stop states. A stable normalized digest is necessary but not enough:
the verifier must also confirm the capture contains the real App ids, installation repository pages, control issue,
environment details, heads, tags, and operational dimensions.

Run the native contract gates from the exact candidate revision:

```bash
dotnet fsi eng/validate-github-ledger-protection.fsx -- .
dotnet fsi eng/validate-github-ledger-protection-provider.fsx -- .
```

Commit only sanitized, content-addressed acceptance evidence. Do not overwrite the historical pre-install captures
in a way that makes the earlier state appear to have been production-conformant.

## 14. Acceptance checklist

GS2-08.2 can be proposed for acceptance only when every item is evidenced:

- [ ] Fresh complete pre-state captured twice with matched continuity.
- [ ] One authorization receipt binds the exact refreshed plan and has not expired.
- [ ] Ordinary and cutover GitHub Apps are distinct and privately owned by `FS-GG`.
- [ ] Each App has only `contents: write` plus implicit metadata read.
- [ ] Each installation selects only `FS-GG/FS.GG.Coordination.Authority` with complete pagination.
- [ ] Both private keys are under separate recorded custody and absent from retained output.
- [ ] The shared App is excluded from the fleet ref and is no longer a production exception.
- [ ] All five active ruleset compositions and bypass lists match section 9 exactly.
- [ ] Effective-rule readback matches the combined creation/update/deletion/non-fast-forward contract.
- [ ] `fleet-cutover` environment values and reviewer ids match exactly.
- [ ] The ordinary control issue is bound and clearly non-authoritative.
- [ ] The fleet ledger is initialized by canonical expected-parent tooling in `OperatingV1`.
- [ ] The phase tag points to the verified commit and cannot be changed or deleted.
- [ ] Monitoring is installed, read-only, complete, content-addressed, and fail-closed.
- [ ] Two post-install captures match and have no gaps.
- [ ] Native qualification gates pass at the exact candidate.
- [ ] Independent verification classifies `InstalledProductionProtection`.
- [ ] A native acceptance receipt binds the candidate tree, evidence, and provider observation.

## 15. Failure, rollback, and break-glass

Before `OpenV2`, rollback is allowed only through the frozen epoch protocol. Administrative rollback must preserve
evidence and must not silently weaken protection.

If installation fails before the initial ledger write:

1. Stop all planned writes.
2. Capture the partial provider state.
3. Disable newly created rulesets only if the approved recovery plan says to; do not edit them ad hoc.
4. Suspend/uninstall new Apps or revoke keys when custody is in doubt.
5. Restore the exact observed pre-state through a separately authorized plan.
6. Capture twice and prove the restoration before retrying.

If failure occurs after initialization, never delete or force-move the ledger branch or a phase tag. Record the
failure through the legal expected-parent transition. Before `OpenV2`, that is the receipted
`RollingBack(reason) -> OperatingV1(recovery)` path from an eligible state. After `OpenV2`, there is no v1 rollback;
repair moves forward through reviewed v2 releases.

For suspected key compromise, revoke the key first, freeze affected writes, preserve GitHub audit-log evidence,
rotate to a new key, and re-run full conformance. For a ruleset or ref tamper alert, do not let monitoring auto-heal;
capture both the observed state and audit trail before any repair.

Break-glass never means using a personal access token or organization-admin bypass to write the ledger. It means a
separately authorized, time-bounded recovery plan using a dedicated principal, complete pre/post observation, and
an explicit receipt.

## 16. Handoff packet for an implementation agent

Give an agent only:

- the approved operation receipt and expiry;
- current source revisions and desired-policy digest;
- App ids, installation ids, slugs, and selected-repository readback;
- secret-manager references or injected secret names, never PEM contents;
- ruleset ids, environment identity, and issue number;
- the two private pre-state capture paths and hashes;
- the allowed operation order and explicit stop conditions.

The agent must return provider response hashes, exact created/updated resource ids, initialization commit and tag
ids, two post-install capture hashes, normalized-set digests, gate results, conformance state, and a zero-secret
sanitized evidence bundle. It must not broaden repository selection, add permissions, invent a bypass, start a
permanent App server, enable webhooks, or treat the control issue as authority.

## 17. Current blocker summary

As of this manual's timestamp, the source-preparation work is complete but live GS2-08.2 installation is not. The
known blockers are the two real App identities and installations, private-key custody, a bound control issue,
administrative apply authority, fleet initialization, monitoring, post-install binding support in the capture and
conformance path, and native acceptance. This manual removes ambiguity about the human operation; it does not
claim those external actions have occurred.
