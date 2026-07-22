---
name: cut-nuget-release
description: Cut every currently owed FS-GG NuGet release across the org, including repositories with source work since their last package even when project versions or the central registry still look current. Use when asked to cut NuGet releases, release all stale FS-GG packages, run a package release train, or determine and publish every unreleased producer. Audits package-affecting commits, chooses coherent SemVer bumps, lands producer release PRs in dependency order, invokes each repository's own release machinery, verifies byte-identical packages on GitHub Packages and nuget.org, and reconciles the central dependency registry. Runs from the FS-GG operator workspace where all rostered repositories are sibling checkouts.
---

# cut-nuget-release (FS-GG)

Cut the **whole owed release train**, not merely the packages whose project files already carry an
unpublished version. This is an operator workflow: it runs from the `FS-GG/.github` checkout with all
rostered repositories available as siblings.

Read [publishing-and-deployment](../publishing-and-deployment/SKILL.md) before acting. It owns feed,
coherent-set, versioning, gate, and registry rules; this skill owns the cross-repo audit and execution
loop.

## Definition of done

The run is complete only when:

- every producer is classified as **current**, **release owed**, or **blocked**, with evidence;
- every owed coherent set has passed its producer gates and been released from merged `main`;
- every expected package/version resolves from both GitHub Packages and nuget.org;
- the two feeds received the same workflow-produced package bytes, never separately repacked bytes;
- dispatch/Renovate propagation has been observed or its failure reported;
- `registry/dependencies.yml` and its generated projections record the published truth on merged
  `.github/main`.

Do not call a run complete because tags exist, a workflow is green, one feed resolves, or the central
registry says the old version is coherent.

## 1. Establish ground truth

1. From `.github`, read the live roster with `scripts/repos.sh list --all`; fail loudly if a rostered
   sibling checkout is missing. Include `.github` itself: it is a package producer.
2. Fetch `main`, tags, and release workflow state in every checkout without discarding local changes.
   Work in fresh branches/worktrees; never release unmerged working-tree bytes.
3. Discover packable projects, package IDs, coherent sets, current project versions, release workflows,
   tag patterns, and workflow inputs from each repository. Do not copy a static package inventory from
   this document; repositories and `registry/dependencies.yml` are authoritative.
4. Query both feeds for every discovered package ID. Treat NuGet indexing as eventually consistent and
   retain the exact versions returned as audit evidence.
5. Find the latest **successful published version/tag for each coherent set**, then inspect every commit
   from that release point through current `main`.

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
4. Use the producer's own release machinery. Inspect its workflow before acting:
   - if release-tag automation owns tag creation, dispatch it and let it create the ordered tags;
   - otherwise create the exact annotated/lightweight tag form the repository already uses and push it;
   - for multiple tag namespaces, preserve the workflow's required order and version relationships.
5. Confirm that each expected tag caused a release run. GitHub suppresses push events when more than
   three tags are pushed at once; dispatch any missing workflow runs explicitly where supported rather
   than assuming tags imply publication.
6. Wait for the complete publish job, including producer gates, org-feed push, nuget.org OIDC login and
   push, and downstream dispatch. Stop the train on a failed or partial coherent-set publication and
   repair it before releasing dependants.

Never publish local packages manually to get around a failed workflow. The workflow-produced,
gate-verified `.nupkg` is the release artifact for both feeds.

## 4. Verify publication

For every expected package ID/version:

1. Verify GitHub Packages resolves it.
2. Poll nuget.org until it resolves or a bounded timeout establishes a real indexing/publish failure.
3. Confirm the workflow pushed the same artifact set to both feeds. Where feed downloads are available,
   compare package hashes; otherwise use the workflow's byte-identical single-pack evidence and report
   that verification boundary explicitly.
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
- repositories classified current/no-release and why;
- blockers or verification limits, if any.

If any expected package, feed, projection, or registry merge remains outstanding, say **release train
incomplete** and name the exact remaining work.
