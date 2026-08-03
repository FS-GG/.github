---
name: cut-nuget-release
description: Use when asked to release every stale FS-GG NuGet producer. Audit package-affecting changes, choose coherent versions, publish in dependency order, verify both feeds, and reconcile the registry.
---

# cut-nuget-release (FS-GG)

Cut the **whole owed release train**, not merely the packages whose project files already carry an
unpublished version. This is an operator workflow: it runs from the `FS-GG/.github` checkout with all
rostered repositories available as siblings.

Read [publishing-and-deployment](../publishing-and-deployment/SKILL.md) before acting. It owns feed,
coherent-set, versioning, gate, and registry rules; this skill owns the cross-repo audit and execution
loop.

## Operator tooling

Use the repository-owned F# scripts for repeatable evidence:

- `dotnet fsi scripts/release-train-audit.fsx -- --root . --fetch --output <audit.json>` discovers
  rostered checkouts, evaluated packable projects, package references, release workflows, repository
  state, a candidate baseline tag, and changed files. Pass `--siblings-root` when `.github` is an
  isolated worktree. Exit 1 means reviewable findings; exit 3 means no verdict.
- `dotnet fsi scripts/release-train-workflows.fsx -- --root . --output <workflows.json>` checks
  release workflows before tags exist. It catches checkout-free `gh release` calls without repository
  context, missing NuGet OIDC permission, feed-order inversions, and repacking after publication starts.
- `dotnet fsi scripts/release-train-verify.fsx -- --manifest <plan.json> --output <verify.json>`
  polls both feeds, downloads each expected package, compares payload entries while excluding
  nuget.org's `.signature.p7s`, and verifies the tag commit. Its manifest requires `name`,
  `repositoryPath`, `tag`, `commit`, and `packages: [{ "id": ..., "version": ... }]`. Its v2
  report carries the commit-bound `subjectCommit`, successful observation conclusion, and separate
  `gitHubAvailable` / `nuGetAvailable` facts; pass `--allow-partial` to record a one-feed observation
  as a finding instead of waiting for both feeds.
- `release-train-status.fsx` summarizes `--audit`, `--workflows`, and repeated `--verification`
  evidence files. Repeat `--verification` for each coherent set; pass `--registry complete` only
  after merged canonical truth is verified.
- `release-train-state.fsx` is the durable coordinator over those reports. Start it with
  `inspect --run <run.json> --audit <audit.json> --workflows <workflows.json> --registry registry/dependencies.yml`.
  Use `plan --run <run.json>` to receive its one typed next action, `advance --release-id <id>` only to record an
  explicit `release-owed`, `semver-effect`, or `human-blocker` decision with its subject commit and
  evidence plus `--workflow-receipt <json>` for a successful producer run. The receipt must contain
  `releaseId`, `subjectCommit`, `workflowRun`, and `conclusion: "success"`; the coordinator stores its
  digest and refuses a stale or mismatched receipt on every resumed command, and
  `verify --run <run.json> --verification <verify.json>` to import package evidence. Use
  `import --run <run.json> --receipt <json>` for a successful, SHA-bound `consumer-embedding`,
  `propagation`, or `canonical-registry` receipt; these are the only transitions that can satisfy the
  consumer pin, downstream propagation, and merged-registry predicates. A canonical-registry receipt
  must preserve the path and dependency-topology fingerprint captured by `inspect`; it cannot redirect
  a run to another local registry file.
  A `human-escalation` action for `org-only`, `public-only`, or `disagree` is terminal: inspect the
  immutable artifacts and record the human decision; do not retry publication from the coordinator.

Keep the release-run JSON file and its evidence as the resumable run ledger. Rerun commands to refresh live evidence. These scripts
are accelerators, not policy authorities: inspect and improve them when repository-specific behavior
warrants it. In particular, the audit's newest reachable tag is only a baseline candidate; select the
latest successful tag separately for every coherent set and override the candidate in the human audit.
Never weaken a failed check merely to make the train proceed.

## Definition of done

The run is complete only when:

- every producer is classified as **current**, **release owed**, or **blocked**, with evidence;
- every owed coherent set has passed its producer gates and been released from merged `main`;
- every expected package/version resolves from both GitHub Packages and nuget.org;
- the two feeds received the same workflow-produced package bytes, never separately repacked bytes;
- every dynamically staged manifest row intended for product materialization exists in the published
  package, the current consumer pins that package version, and a fresh consumer workspace contains the
  expected bytes;
- every packaged tool/shim manifest names a published runtime version containing the source changes its
  guidance expects;
- dispatch/Renovate propagation has been observed or its failure reported;
- `registry/dependencies.yml` and its generated projections record the published truth on merged
  `.github/main`.

Do not call a run complete because tags exist, a workflow is green, one feed resolves, or the central
registry says the old version is coherent.

## 1. Establish ground truth

1. Run `release-train-audit.fsx`, retain its JSON, and cross-check its roster against
   `scripts/repos.sh list --all`. Fail loudly if a rostered sibling checkout is missing. Include
   `.github` itself: it is a package producer. The F# auditor parses the authoritative roster without
   depending on ambient PyYAML, but it does not replace `repos.sh validate`.
2. Fetch `main`, tags, and release workflow state in every checkout without discarding local changes.
   Work in fresh branches/worktrees; never release unmerged working-tree bytes.
3. Discover packable projects, package IDs, coherent sets, current project versions, release workflows,
   tag patterns, and workflow inputs from each repository. Do not copy a static package inventory from
   this document; repositories and `registry/dependencies.yml` are authoritative.
4. Query both feeds for every discovered package ID. Treat NuGet indexing as eventually consistent and
   retain the exact versions returned as audit evidence.
5. Find the latest **successful published version/tag for each coherent set**, then inspect every commit
   from that release point through current `main`.
6. Follow every derived package-content edge. For packages whose `Pack` target runs a stager or reads a
   generated manifest, compare the released tag's manifest and staged file closure with `main`; do not
   limit the diff to the package project directory. Classify a new materialized row, changed digest,
   changed executable bit, or changed staged relative path as package-affecting even when the package
   project and version property are unchanged.
7. Audit runtime distribution separately from guidance distribution. Compare the latest published tool
   tag with current runtime sources, then inspect every checked-in tool manifest/shim that receivers
   obtain. A current skill/kit beside a stale runtime manifest is a release gap, not a current producer.

The fifth step is mandatory. A project version equal to the feed, a stale registry row, or a recent
release date does **not** prove currency. Rendering and Game have both accumulated package-affecting
work while those shortcuts still looked current.

## 2. Decide which releases are owed

Classify changes by their effect on the packed artifact, not by commit subject alone:

- **Release owed:** runtime/API changes, pack inputs, templates, bundled skills, build targets/props,
  package metadata that changes the `.nupkg`, or fixes required to make those changes releasable.
- **No release:** changes wholly outside every package's inputs, such as operator-only docs or CI edits
  that do not alter packing or package behavior.
- **Blocked:** artifact-affecting work exists but release gates, version policy, dependency ordering,
  credentials, or required owner decisions prevent a valid publication.

Inspect `git diff <release>..main`, project `Pack`/`Content` items, and the release workflow. Never infer
"docs-only" from filenames without checking whether documentation, skills, templates, or readmes are
packed.

For dynamically staged content packages, inspect all three states explicitly:

1. **authored source:** current skill/template/content trees and their generated manifest;
2. **published package:** the latest feed version's manifest and complete staged file set;
3. **consumer embedding:** the package version pinned by the current CLI/template that materializes it.

If any state is behind the preceding one, the release train includes that producer or consumer. A
published package is not live in product workspaces until the materializing consumer embeds it.
Conversely, fetching current guidance from a moving source does not make an older packaged runtime
current.

Choose the smallest valid SemVer bump under the producer's own policy:

- patch for compatible fixes with no additive public contract;
- minor on the `0.x` line for additive public API/capability or materially expanded packaged behavior;
- the policy-required breaking bump for incompatible public contract changes;
- deterministic/schema-derived versions where the producer defines them.

One coherent set gets one version. Existing separately versioned packages or tag lanes remain separate.
Never reuse or overwrite a published version.

Produce an audit table before publishing: repository, coherent set, latest release, unreleased range,
classification, proposed version, evidence, and dependency constraints.

## 3. Build the release train

Derive ordering from package references, template pins, the dependency registry, and dispatch edges.
Release producers before consumers that must embed or validate their new versions. For example, if a
Rendering template pins a new Game set, Game must be published and the pin landed before Rendering is
released.

For each owed producer, in dependency order:

1. Create a release issue/PR using the repository's established convention. Bump all version sources,
   lockfiles, API manifests, package baselines, packed documentation, template pins, and release notes
   required by that repository. Leave unrelated work untouched.
2. Run the focused local pack/tests plus package-content and API-compatibility checks. A successful
   `dotnet pack` alone is insufficient.
3. Push the PR, wait for all required checks, review failures from their logs, fix them, and merge only
   when green. Re-read merged `main` before tagging.
4. Run `release-train-workflows.fsx` across the train and resolve every error before creating tags.
   Warnings name manual inspection boundaries and remain part of the evidence.
5. Use the producer's own release machinery. Inspect its workflow before acting:
   - if release-tag automation owns tag creation, dispatch it and let it create the ordered tags;
   - otherwise create the exact annotated/lightweight tag form the repository already uses and push it;
   - for multiple tag namespaces, preserve the workflow's required order and version relationships.
6. Confirm that each expected tag caused a release run. GitHub suppresses push events when more than
   three tags are pushed at once; dispatch any missing workflow runs explicitly where supported rather
   than assuming tags imply publication.
7. Wait for the complete publish job, including producer gates, org-feed push, nuget.org OIDC login and
   push, and downstream dispatch. Stop the train on a failed or partial coherent-set publication and
   repair it before releasing dependants.
8. For packages that materialize tools, skills, templates, or build assets into another workspace, run
   a real fresh-consumer acceptance after the consumer release. Assert the expected IDs, versions,
   file closure, digests/modes where applicable, and executable runtime version from the created
   workspace. Also exercise the documented update/retrofit path on an existing workspace; if no
   supported path can bring it current, classify and file that as a propagation blocker rather than
   claiming the release is live.

Never publish local packages manually to get around a failed workflow. The workflow-produced,
gate-verified `.nupkg` is the release artifact for both feeds.

## 4. Verify publication

Create the verifier manifest from the reviewed coherent-set inventory, then run
`release-train-verify.fsx`. For every expected package ID/version:

1. Verify GitHub Packages resolves it.
2. Poll nuget.org until it resolves or a bounded timeout establishes a real indexing/publish failure.
3. Confirm the workflow pushed the same artifact set to both feeds. Compare payload-entry hashes, not
   whole-archive hashes: nuget.org adds `.signature.p7s`, so signed and unsigned archive hashes normally
   differ even when their package payloads are byte-identical. If feed downloads are unavailable, use
   the workflow's byte-identical single-pack evidence and report that verification boundary explicitly.
4. Record the successful workflow URL/run id and tag SHA. Tag SHA must be the merged release commit.

Count expected versus observed packages for each coherent set. A green run that published 17 of an
expected 18 is a failed release.

## 5. Reconcile the central registry

After all producer packages are live:

1. Observe the producer dispatch that opens/updates the `.github` feed-autofix PR. If it does not arrive,
   run the repository's sanctioned registry reconciliation rather than hand-editing generated output.
2. Review every changed contract row. Complete human-owned source `version`, `package-version`, tag,
   provenance, compatibility range, and coherence notes that the feed-only bot cannot infer.
3. Regenerate all declared projections and run their documented non-writing checks. Use
   `scripts/generated-paths` to discover whole-file generated artifacts and
   `scripts/generated-paths --roster` to discover their generators. Do not assume every generator
   accepts `--check`; for example, lock freshness is enforced by `scripts/repos.sh validate` while
   `scripts/repos.sh relock` is write-only.
4. Run feed coherence against live feeds, registry validation, and contract-coherence checks.
5. Merge the registry PR on green, fetch `.github/main`, and verify the canonical rows contain the exact
   versions just published.

Do not flip the registry before publication. Feed first, registry second is the publish-before-flip
rail.

## 6. Report

Report:

- released coherent sets and versions;
- package count per set and confirmation of both feeds;
- producer PRs, tags, and successful workflow runs;
- the merged registry PR and final canonical versions;
- fresh-workspace and existing-workspace propagation evidence for materialized package/tool content;
- repositories classified current/no-release and why;
- blockers or verification limits, if any.

If any expected package, feed, projection, or registry merge remains outstanding, say **release train
incomplete** and name the exact remaining work.
