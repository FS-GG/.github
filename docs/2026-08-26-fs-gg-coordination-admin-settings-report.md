# FS.GG.Coordination administrator settings report

**Observed:** 2026-08-26T01:42:00+02:00  
**Repository:** [`FS-GG/FS.GG.Coordination`](https://github.com/FS-GG/FS.GG.Coordination)  
**Program:** [GitHub Substrate v2 roadmap](github-substrate-v2-roadmap.md)  
**Boundary:** [ADR-0078](adr/0078-github-substrate-v2-new-only-coordination-authority.md)  
**Ratification PR:** [FS-GG/.github#3002](https://github.com/FS-GG/.github/pull/3002)

This is the administrator/operator projection of the complete settings work required by GS2-01,
GS2-04, GS2-06, GS2-08, and the fleet cutover. It says where each control lives, when it may be applied,
and what evidence closes it. The protected epoch and accepted receipts remain authority; a checked box in
this report is not authority.

The repository was created early by explicit user instruction at initial `main` commit
`ce22e4d10f2efae7aa09018521487b598c082350`. It is inert: no v2 writer, App route, environment, secret,
webhook, or event subscription was enabled by bootstrap.

## 1. Current observed state

| Surface | Observed state | Direct settings page |
|---|---|---|
| Repository | Public; default branch `main`; current user has Admin | [General](https://github.com/FS-GG/FS.GG.Coordination/settings) |
| Merge policy | Merge commits, squash, and rebase all allowed; auto-merge off; branch deletion off | [Pull Requests / General](https://github.com/FS-GG/FS.GG.Coordination/settings) |
| Security | Dependency graph/vulnerability alerts enabled (alerts endpoint returned 204); automated Dependabot security updates, secret scanning, push protection, validity checks, and non-provider patterns disabled | [Code security](https://github.com/FS-GG/FS.GG.Coordination/settings/security_analysis) |
| Actions | Enabled; all actions allowed; SHA pinning not required; workflow token defaults to `write`; workflows may approve pull-request reviews | [Actions / General](https://github.com/FS-GG/FS.GG.Coordination/settings/actions) |
| Rulesets | None | [Rules / Rulesets](https://github.com/FS-GG/FS.GG.Coordination/settings/rules) |
| Environments | None | [Environments](https://github.com/FS-GG/FS.GG.Coordination/settings/environments) |
| Webhooks | None | [Webhooks](https://github.com/FS-GG/FS.GG.Coordination/settings/hooks) |
| Organization custom properties | No organization property schema observed | [Custom properties](https://github.com/organizations/FS-GG/settings/custom-properties) |
| Organization teams | No teams observed | [Teams](https://github.com/orgs/FS-GG/teams) |
| Coordination Project | Project 1 exists | [Project](https://github.com/orgs/FS-GG/projects/1) · [Settings](https://github.com/orgs/FS-GG/projects/1/settings) |
| GitHub Apps | `fs-gg-cross-repo-dispatch` and Renovate are selected-repository installations; inclusion of this new repository is not yet proven | [Installed Apps](https://github.com/organizations/FS-GG/settings/installations) |

Re-observe every row before applying it. A stale screenshot or this timestamp cannot authorize a write.

## 2. Safe now: inert repository hygiene

These changes do not activate v2 coordination or constrain an as-yet-uncreated check name.

- [ ] In [Code security](https://github.com/FS-GG/FS.GG.Coordination/settings/security_analysis), enable
  automated Dependabot security updates, secret scanning, push protection, validity checks, and
  non-provider patterns wherever GitHub exposes them for this public repository. Dependency graph and
  vulnerability alerts are already enabled; verify rather than toggle them blindly.
- [ ] In [Actions / General](https://github.com/FS-GG/FS.GG.Coordination/settings/actions), set the
  workflow-token default to **Read repository contents and packages permissions** and disable **Allow
  GitHub Actions to create and approve pull requests**. A workflow may never approve its own change;
  later write permissions belong only on the smallest named job.
- [ ] In [General](https://github.com/FS-GG/FS.GG.Coordination/settings), enable auto-merge and
  automatically delete head branches after merge.
- [ ] Select **squash merge** as the only ordinary merge method. Disable merge commits and rebase merges.
  Keep the automatically generated squash commit title/body attributable to the pull request.
- [ ] Keep Issues enabled. Disable the repository wiki and repository Projects feature; organization
  Project 1 is the coordination projection. Do not disable Discussions if later accepted as a human-only
  surface, but it is not coordination authority.
- [ ] Keep the repository public and `main` as default. Do not rename, archive, transfer, or make it a
  template.

After applying, preserve a fresh REST/GraphQL receipt containing the exact values and repository node id.
The receipt belongs to GS2-01.1 evidence; prose confirmation alone is insufficient.

## 3. Apply with the GS2-01 bootstrap PR

These settings depend on files, principals, or check-run identities created by the bootstrap PR. Apply
them only after that PR exposes the named subject, then re-read the live setting.

### 3.1 Access and ownership

- [ ] Create organization team `coordination-maintainers` at
  [FS-GG teams](https://github.com/orgs/FS-GG/teams) and grant **Maintain**, not Admin, to the repository.
  Organization owners retain the administrative root; routine contributors do not receive Admin.
- [ ] If non-maintainer contributors need direct triage, create `coordination-triage` with **Triage**.
  Do not grant Write merely to let automation run.
- [ ] Add repository `CODEOWNERS` for protocol/specification, adapters, workflows, release configuration,
  and epoch files. Rulesets—not CODEOWNERS prose—enforce the review requirement.
- [ ] Record every team/repository permission and the absence of unexpected outside collaborators under
  [Collaborators and teams](https://github.com/FS-GG/FS.GG.Coordination/settings/access).

### 3.2 `main` ruleset

Create one active branch ruleset at
[Rulesets](https://github.com/FS-GG/FS.GG.Coordination/settings/rules) targeting the default branch:

- [ ] block branch deletion and non-fast-forward updates;
- [ ] require pull requests and resolved review conversations;
- [ ] require CODEOWNERS review for protected areas;
- [ ] require the bootstrap aggregate checks for deterministic build, compiler/unit tests,
  dependency/security review, package/install smoke, and evidence-manifest validation;
- [ ] require the branch to be up to date or use merge queue once the exact aggregate check is proven on
  both `pull_request` and `merge_group`;
- [ ] permit no personal bypass. A later App bypass is restricted to the protected cutover/release
  environment and exact operation, never general repository administration;
- [ ] require signed commits only if the selected CI/bot identities can satisfy it without a bypass that
  weakens the rule; otherwise record the unsupported control and rely on immutable source/tag receipts.

Do not guess required check names. First observe their exact GitHub check-run `name` and App identity from
the bootstrap PR, then bind those values into the settings plan and receipt.

### 3.3 Tag and release rules

- [ ] Add active tag rulesets for released v2 package tags and protected fleet phase tags. Block update
  and deletion; restrict creation to the qualified release/cutover identity.
- [ ] Create `release` under
  [Environments](https://github.com/FS-GG/FS.GG.Coordination/settings/environments) only after the release
  workflow exists. Require human approval, prevent self-review where supported, restrict deployment
  branches/tags, and use OIDC or short-lived credentials.
- [ ] Never store a long-lived feed or GitHub administrative token when OIDC/App installation exchange is
  available. Record secret **names and grants**, never values.
- [ ] Enable immutable releases/tags, SBOM, attestations, dependency submission/review, one-pack
  publication, and byte-identical supported-feed verification as the platform and package feeds permit.

### 3.4 Actions policy

- [ ] Replace `allowed_actions=all` with the organization-approved allow-list after every bootstrap action
  is pinned immutably. Require full commit SHA pinning only after the tree contains no moving action refs.
- [ ] Set workflow-token default permission to read-only. Grant `contents`, `issues`, `pull-requests`,
  `checks`, `packages`, `id-token`, or administration permissions only in the smallest named job that uses
  them.
- [ ] Disable fork approval shortcuts and untrusted pull-request secret access. Never execute
  contributor-controlled code with `pull_request_target` write credentials.
- [ ] Retain Actions logs/artifacts long enough for Q0-Q10 evidence and record the chosen retention.

## 4. Organization registration

Apply these changes through reviewed source or an exact organization-admin plan; do not create an
unversioned parallel taxonomy in the UI.

- [ ] Add `FS-GG/FS.GG.Coordination` and its owner/release topology to `.github/registry/repos.yml`, the
  architecture map, dependency/contract registries, receiver roster, and repository membership policy.
- [ ] Add it to [Coordination Project 1](https://github.com/orgs/FS-GG/projects/1) with the ratified Phase,
  Repo Scope, Workstream, contract, dates, and dependency projections. Project fields are projections,
  not epoch or completion authority.
- [ ] Define native organization issue types only after GS2-05.1 ratifies the taxonomy. The eventual type
  set replaces—not duplicates—legacy `Class`/`Kind` body and Project metadata. Organization issue-type
  administration is under [Organization settings](https://github.com/organizations/FS-GG/settings).
- [ ] Define the desired repository-profile custom properties only after GS2-06.1 generates their schema.
  Apply them at [Custom properties](https://github.com/organizations/FS-GG/settings/custom-properties),
  then set the exact values on all eight FS-GG fleet repositories and this producer.
- [ ] At [Installed Apps](https://github.com/organizations/FS-GG/settings/installations), add the new
  repository to Renovate and, only when its registered dispatch contract is qualified, to
  `fs-gg-cross-repo-dispatch`. Compare exact installation permissions to the model; do not select all
  repositories for convenience.

Q0 rejects a continuously hosted App/webhook boundary from the cutover critical path. Do not create an
App host, ingress, webhook, runtime secret, deployment, or production subscription under GS2-01.9. A
future event accelerator requires a separate accepted operational decision and cannot authorize state.

## 5. GS2-08 protected epoch controls

After the epoch wire contract is frozen and every v1 write class has a published bridge:

- [ ] Create the dedicated cutover ledger ref and non-deletable phase-tag rulesets. Each transition must
  be an expected-parent commit bound to the exact manifest; the control issue is projection only.
- [ ] Create the protected `fleet-cutover` environment. Require protected human approval, prevent
  self-review where supported, restrict branches/tags, and limit App bypass to the exact epoch writer.
- [ ] Create the cutover-control issue and tamper/rewind audit, but give neither issue text nor scheduled
  output authority over the Git ledger.
- [ ] Keep the legal sequence exact:
  `OperatingV1 -> Preparing(manifest) -> FreezeRequested(manifest) -> Frozen(snapshot) ->
  SwitchedV2(candidate) -> VerifiedV2(evidence) -> OpenV2(acceptance) -> RetiringV1(deletion) ->
  OperatingV2(report)`.
- [ ] Before `OpenV2`, permit only the receipted
  `RollingBack(reason) -> OperatingV1(recovery)` path from `Preparing` through `VerifiedV2`. After
  `OpenV2`, delete rollback assets and recover only through reviewed forward v2 releases.

## 6. Fleet repository changes

The fleet is `.github`, SDD, Rendering, Governance, Templates, Game, Audio, and Net. The cutover manifest
must carry one independently verified row per repository. Use these settings roots:

| Repository | Settings |
|---|---|
| `.github` | [settings](https://github.com/FS-GG/.github/settings) |
| `FS.GG.SDD` | [settings](https://github.com/FS-GG/FS.GG.SDD/settings) |
| `FS.GG.Rendering` | [settings](https://github.com/FS-GG/FS.GG.Rendering/settings) |
| `FS.GG.Governance` | [settings](https://github.com/FS-GG/FS.GG.Governance/settings) |
| `FS.GG.Templates` | [settings](https://github.com/FS-GG/FS.GG.Templates/settings) |
| `FS.GG.Game` | [settings](https://github.com/FS-GG/FS.GG.Game/settings) |
| `FS.GG.Audio` | [settings](https://github.com/FS-GG/FS.GG.Audio/settings) |
| `FS.GG.Net` | [settings](https://github.com/FS-GG/FS.GG.Net/settings) |

For each row, inspect and eventually apply:

- [ ] exact v2 tool, kit, workflow, and package pins;
- [ ] desired custom properties and native issue type/field/relation policy;
- [ ] aggregate required checks on both pull requests and merge groups;
- [ ] immutable Actions and reusable-workflow references;
- [ ] least App installation permissions and repository selection;
- [ ] branch/tag rulesets, merge policy, bypass, and temporary freeze restriction;
- [ ] release environment, OIDC, immutable tags/releases, SBOM, attestation, and feed policy where used;
- [ ] scheduled complete-audit repair and any qualified non-authoritative event acceleration;
- [ ] open claim/review/delivery/release/queue/dependency-update disposition;
- [ ] migration and rollback receipts; and
- [ ] post-`OpenV2` absence of every v1 writer, parser, field, credential, schedule, exception, and cache.

Do not apply fleet switch settings during preparation. GS2-10 produces plans; GS2-11 freezes; GS2-12
applies while closed; GS2-13 opens exactly once and then retires v1.

## 7. Human approval points

Only these protected decisions require an administrator/human to authorize an irreversible or
fleet-affecting transition:

1. **Freeze grant (GS2-11.2):** approve the exact manifest and expected parent before
   `FreezeRequested(manifest)`.
2. **Verification acceptance (GS2-12.10):** independent Q8 evidence must support
   `VerifiedV2(evidence)`; failure chooses repair or rollback while closed.
3. **Point of no return (GS2-13.2):** approve exact candidate, Q0-Q8 roll-up, risks, rollback state, and
   operational ownership before `OpenV2(acceptance)`. After this approval v1 never resumes.

The operator must be shown the exact digest, current ledger parent, candidate, settings diff, independent
review records, and consequence before approval. A general “go ahead” or Project status is insufficient.

## 8. Evidence required after every settings batch

Each apply operation records:

- exact subject and supported/unsupported API surface;
- principal and least permission used, without secret values;
- observed pre-state, desired state, typed plan digest, and approval where required;
- per-operation result including partial, indeterminate, or lost-response classification;
- authoritative post-write reread, pagination/completeness evidence, and raw-byte digest;
- rollback or forward-repair instruction appropriate to the current epoch; and
- independent review/qualification record bound to the resulting artifact and settings fingerprints.

If permission disappears, GitHub returns an unsupported surface, a read is incomplete, or the post-state
cannot be verified, stop. Never convert “could not inspect” into “already configured.”
