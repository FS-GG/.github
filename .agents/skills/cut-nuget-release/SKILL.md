---
name: cut-nuget-release
description: Use when asked to release every stale FS-GG NuGet producer. Audit packed inputs, prepare each coherent set once, publish exact manifest-bound bytes to both feeds, promote, smoke, and reconcile consumer pins.
---

# cut-nuget-release (FS-GG)

Read [publishing-and-deployment](../publishing-and-deployment/SKILL.md) before acting. It owns versioning,
feed, coherent-set, and registry rules. This skill applies those rules through the repository's current
pack-once release saga.

## Invariants

- Release only merged, exact-`main` bytes.
- Never repack in a package publisher. `release-saga-prepare.yml` is the sole pack site.
- Never reuse a version, move a release tag, overwrite an asset, or bypass OIDC/trusted publishing.
- The prepared `release-manifest.json` binds every package byte, package id/version, and source SHA.
- Package publishers download those exact assets, initialize their durable journals, and resume
  idempotently after a failure.
- Promote only after every package journal is complete and both feeds expose the complete coherent set
  with payload identity.
- Publish first; update canonical registry pins only after both feeds are observed.

## 1. Establish what is owed

Fetch `main` and tags in every rostered producer without disturbing local changes. Compare each current
pack input—including staged skills, templates, targets, generated manifests, and runtime sources—with
the latest successfully published tag. Classify each producer as current, release owed, or blocked.
Derive dependency order from package references, registry contracts, consumer pins, and dispatch edges.

For the hub coherent set, FS.GG.Coord.Cli, FS.GG.Kit, and FS.GG.Drivers always receive one fresh version.
Choose the smallest valid SemVer bump under current policy; never infer currency from a project version
or registry row alone.

## 2. Prepare once

Land version, lockfile, release-note, package-input, and manifest changes through a normal reviewed PR.
On exact merged `main`, dispatch `.github/workflows/release-saga-prepare.yml` with the fresh version
and source SHA. Require its Core/CLI/package gates to pass and inspect the draft
`coherent-set/v<VERSION>` release:

- exactly the expected packages were packed once;
- `release-manifest.json` binds their sizes and SHA-256 digests to the exact merged source SHA;
- the draft contains no reused or overwritten assets;
- all three coherent tags will resolve to that same source SHA.

A preparation run is the reversible rehearsal boundary. Package-publisher workflows have no dry-run or
local-pack mode.

## 3. Publish and resume

Create/push the coherent tags in the ordering required by the saga, or use the sanctioned recovery
dispatch with the exact prepared source SHA. Each of `release-coord-engine.yml`,
`release-kit.yml`, and `release-drivers.yml` must:

1. download the prepared manifest and package bytes;
2. run `scripts/release-saga-ci.sh init` for its package and exact source SHA;
3. publish the prepared package to GitHub Packages;
4. record the GitHub observation in its immutable package journal;
5. authenticate to nuget.org through OIDC trusted publishing;
6. publish the same prepared payload and record the public observation.

On failure, repair the cause and rerun the same publisher against the same manifest. The journal must
make completed steps idempotent and reject identity, source, version, or byte drift.

## 4. Promote and verify

Dispatch `.github/workflows/release-saga-promote.yml` only after all three journals exist. Require it to
verify the manifest, source SHA, coherent tags, complete journals, both feeds, and monotonic channel
ordering before promotion. Then:

- download each package from both feeds and compare payload entries, excluding nuget.org's
  `.signature.p7s`;
- install the public coord engine in a clean tool home and run command-contract plus safe live reads;
- materialize Kit/Drivers in a clean consumer and verify expected files, modes, digests, and versions;
- update consumer pins and `registry/dependencies.yml` through the sanctioned generated projections;
- merge the registry/pin PR, fetch exact `main`, and rerun feed, source, pin, and coherent-set checks.

## Definition of done

Done means the complete expected set is available from both feeds, payload-identical, promoted, adopted,
and pinned on merged `main`; all package/tag/source identities agree; fresh-consumer smoke passes; and
no stale bot PR/branch remains. A tag, one green publisher, one feed, or an updated registry alone is
not completion.

Report exact version, source SHA, PRs, tags, workflow runs, journals, feed observations, promotion,
consumer smoke, and merged registry/pin evidence. If any part remains, report the release as incomplete.
