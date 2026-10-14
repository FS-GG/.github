# Provisioned development common base

This is source preparation for [V3-ENV-01](../../docs/roadmaps/2026-10-13-provisioned-podman-execution.md).
It reconstructs a reviewable Arch recipe from the protected donor decision; it does not restore the
missing, previously unreviewed five-file draft. No common image, complete acquired-input lock,
publication or installed receiver has been accepted. `inputs.json` is a descriptive source specification;
its absent acquired inputs and unresolved trust/closure obligations deliberately prevent build readiness.
The host's installed packages and existing SDK401 consumer image are not these portable inputs.

The fixed Linux amd64 donor is the platform manifest in `inputs.json`. Its index, config and layers are
historical metadata from the [protected handoff](../../docs/handoffs/2026-10-13-095258-unified-roadmap-handoff.md).
[Docker Official Images source](https://github.com/docker-library/official-images/blob/cf21f1e1b466b6e2f4b3140a063d6dd76ce79db7/library/archlinux)
joins its tag to archlinux-docker source revision `1e7d9a2d495ffe76934a775b6bd68ac892a14c1b`.
The upstream Dockerfile bytes were not inspected because access was denied. Metadata does not prove
possession or trust of the image/package payloads today.

## Acquire and freeze before assembly

Use the existing admitted acquisition route to acquire and verify the exact donor. Inspect its actual
installed package database, pacman/libalpm, keyring, trust policy and hooks. In a fresh disposable instance
of that donor, pin only `core` and `extra` to
`https://archive.archlinux.org/repos/2026/10/12/$repo/os/x86_64`. Use the maintained package manager's
full-system transaction; the preparation command is:

```text
pacman --config <reviewed-snapshot-config> --noconfirm -Syuw --needed \
  podman crun conmon containers-common shadow passt fuse-overlayfs git ca-certificates python
```

The operation owner must retain genuine complete transaction output, provider choices,
replacements/removals, package versions/sizes/digests, repository database bytes and every package
signature/trust result. Retain hook provenance and the expected complete final `pacman -Q` inventory.
An additions-only list or `--print` output is insufficient. Do not run host pacman or implement an ALPM
resolver. Do not substitute hashes for trusted package signatures.

`pacman.conf` contains the candidate `Required DatabaseOptional` and `LocalFileSigLevel = Required`
policy. Reconcile it with the actual donor before any build admission; a stricter donor policy must not
be weakened silently. The historical missing `core.db.sig` remains a retained failure. Database-signature
availability and donor policy remain unresolved; this candidate configuration is not accepted trust
evidence. Build-local keyring initialization uses packaged Arch trust anchors. Generated local private
keys are removed before delivery; licenses and the complete installed inventory are retained.

Prepare this operation-owned context only from genuinely verified acquisition outputs:

```text
Containerfile
pacman.conf
acquired/
  SHA256SUMS
  package-inventory.expected
  repositories/core/os/x86_64/core.db[.sig]
  repositories/extra/os/x86_64/extra.db[.sig]
  repositories/<repo>/os/x86_64/<selected-package-archive>[.sig]
```

`SHA256SUMS` covers the complete staged repository/package/signature population and expected inventory;
paths are relative to `acquired/`. The maintained admission seam must validate complete population,
safe relative regular-file paths, identity, signature/trust reconciliation and the acquired lock against
the actual context before launch. Before the transaction, the Containerfile rejects unsafe checksum
paths and non-regular repository inputs, and compares checksum rows with every staged file plus the
expected final inventory. Missing, extra or duplicate rows refuse before hash verification. This checks
complete coverage of the supplied population; it does not authenticate a supplied checksum list or
prove that the supplied population is the complete maintained transaction. Missing COPY inputs or
empty required inputs refuse before the transaction. Pacman still performs the full offline dependency,
conflict, replacement, scriptlet and hook transaction; the complete resulting inventory must match the
frozen expectation. An incomplete package set cannot be repaired over a network.

## Build and qualify through the maintained seam

Coordination owns operation identity, original deadlines, custody, helper accounting, storage and
cleanup. Its `eng/portable-workspace-image.py` is currently specific to the portable Python image;
it needs a reviewed explicit common-base input/profile extension before this native route can run.
This source supplies no replacement launcher or publication wrapper. The selected build options are:

```text
podman <admitted-explicit-profile-and-storage-options> build \
  --pull=never --network=none --timestamp=0 --platform=linux/amd64 --format=oci \
  --file <verified-context>/Containerfile <verified-context>
```

These are command specifications, not executable admission. Native acquisition, build, export/load,
runtime tests and publication require their concrete existing admission. Qualify genuine package
inventory, actual offline helper execution, config/platform/user identity, OCI export/reload and scoped
cleanup. Keep engine-store and exported-manifest identities separate. No performance, containment,
determinism or other-machine assertion follows from the recipe or its text controls.

The image has user `32768:32768`, a real ordinary home/work directory and empty entrypoint/command.
It contains engine/helper/source/Python qualification tools, with no .NET, Node, TypeScript, Rust, Go
or browser SDK. It supplies neither host subordinate mappings, `/dev/fuse`, delegated cgroups nor
privileges. Installing fuse-overlayfs does not grant FUSE access. Host-backed allocation and independent
teardown are launcher/provisioner obligations, not Containerfile instructions.

## Cold inputs and selected consumers

Default acceptance requires fresh graphroot/runroot, workspace, home/config, caches, temporary files
and named volumes. The verified immutable donor/archive bundle is an explicit input; account acquisition
separately. Final assembly and receiver commands use network none. Optional bounded verified caches
are an opt-in mode and cannot satisfy cold acceptance. Do not clear host caches or claim physically cold
page-cache state. No writable engine store, credentials, socket or automatic service is embedded.

The first framework pilot is Coordination's exact clean source using a separately declared .NET
10.0.400 linux-x64 profile, matching the selected Coordination source's `global.json` with
`rollForward: disable`. The consumer pin controls this pilot; another receiver's SDK401 profile does not
satisfy it. Its portable SDK archive and Arch native dependency closure remain unacquired;
existing SDK consumer lanes supply no common-base or profile acceptance. Templates/SDD own
toolchain materialization and exact digest selection.

The first ordinary product receiver is Templates' `examples/language-routes/typescript-todo` with
separate Node 24.8.0/TypeScript 5.9.2 pins. Qualify its actual compile/unit/domain/storage journey and
no-host-.NET behavior. Existing Playwright Noble browser acceptance remains separate; do not transplant
its native dependency closure into Arch. Public acquisition and clean installed journeys follow actual
OCI publication through the authorized release route, whose common-base permissions/integration remain
pending. New workspace selection is opt-in and digest-pinned; existing workspaces retain settings and
their selected digest until an explicit retaining upgrade with predecessor rollback.

Run inert source controls with `python3 -B tests/provisioned-podman/test_source_contract.py`.
They check the declared source/input boundaries without acquiring inputs or invoking Podman/pacman;
they do not prove transaction correctness, native custody, provisioning, publication or installed adoption.
