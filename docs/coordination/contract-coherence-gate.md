# The contract-coherence gate

A single reusable GitHub Actions workflow — [`contract-coherence.yml`](../../.github/workflows/contract-coherence.yml)
(`workflow_call`) — that publishes the stable `contract-coherence / coherence` context.
Delivered by [.github#18](https://github.com/FS-GG/.github/issues/18) (H3, [epic #16](https://github.com/FS-GG/.github/issues/16) Pillar 3).

The context has two ownership-scoped paths ([#1787](https://github.com/FS-GG/.github/issues/1787)):

- In a **receiver**, it reports the stable required context and explicitly abstains from grading
  hub-owned data. There is no caller-owned assertion left here after #1262 retired the copied
  build-config drift check.
- In **FS-GG/.github itself**, it is the required authority gate over the registry and shared props.
  A broken authority subject therefore reds the repository that owns and can fix it.

## What the authority path checks

1. **Registry schema** — the **typed** `Fsgg.Registry` validator (`fsgg-sdd registry validate`,
   shipped in `FS.GG.Contracts` via the `FS.GG.SDD.Cli` tool) validates the org registry's real
   on-disk grammar (required fields, known repos, well-formed versions, no duplicate ids):
   `RegistryDocument.load` (YAML) + `validateDocument`, emitting `MissingField` /
   `UnknownComponent` / `MalformedVersion` / `DuplicateComponent` / `MalformedDocument`. This
   **replaced** the `scripts/validate-registry.py` stand-in once the typed validator reached
   parity ([.github#49](https://github.com/FS-GG/.github/issues/49); 4-segment version grammar
   fixed in [SDD#32](https://github.com/FS-GG/FS.GG.SDD/issues/32), CLI `0.2.1`).
2. **Source XML well-formedness** — guards the `.github#29` defect class (a malformed-but-verbatim
   `.props` that passes the byte-for-byte drift check yet breaks every adopter's restore).

Both are pure functions of files committed in `.github`, and both execute only when
`github.repository == 'FS-GG/.github'`. The receiver arm neither checks out nor grades the hub.
This closes #1584's defect shape in this gate: a commit to `registry/dependencies.yml` or
`dist/dotnet/*.props` cannot move the required verdict of a byte-identical Governance or Net PR.

### Live authority evidence

The split was exercised on [.github#2001](https://github.com/FS-GG/.github/pull/2001), not inferred
from the workflow text. [Run 30501475019](https://github.com/FS-GG/.github/actions/runs/30501475019)
was green and executed both authority assertions: `Assert build-config source .props are well-formed
XML` and `Validate registry schema (typed Fsgg.Registry validator)`.

The registry assertion was also mutation-proven in the temporary, unmerged
[.github#2002](https://github.com/FS-GG/.github/pull/2002). Removing the required `owner` from
`scaffold-provider` made [run 30501610358](https://github.com/FS-GG/.github/actions/runs/30501610358)
fail specifically at the typed registry step with `MissingField [scaffold-provider]`. The proof PR
was then closed and its branch deleted.

### What it deliberately no longer checks: `fsgg-contracts` pin drift

It used to assert, as check 2, that the registry's declared `fsgg-contracts` `version` equalled the
`FS.GG.Contracts` version read from **`FS-GG/FS.GG.SDD@main`'s source** — calling that, in its own
error message, *"the actual FS.GG.Contracts package version"*. That was two defects in one line:

- **It wedged the org on every Contracts bump.** The assertion coupled `FS-GG/.github@main`'s
  registry to `FS-GG/FS.GG.SDD@main`'s source, and **no PR spans both repos**. Bump SDD first and
  every caller reds the moment it merges; flip the registry first and every `.github` PR wedges
  instead. There was no landing order without a red window, at `enforce_admins` level — and since
  SDD calls this workflow too, a bare source bump wedged SDD's own merges
  ([FS.GG.SDD#432](https://github.com/FS-GG/FS.GG.SDD/issues/432)).
- **It was vacuously green.** `main` is not a release and a source tree is not a package. When
  SDD#426 grew the Contracts public surface under an unchanged `2.0.0`, the `.nupkg` on the feed and
  the source labelled `2.0.0` became different artifacts — and the gate printed
  `ok: ... == actual package version == 2.0.0`. That is [epic #266](https://github.com/FS-GG/.github/issues/266)'s
  signature: a confident verdict about a subject the code could not see.

**The coupling did not go away — it moved to where it cannot wedge anyone**, and it is now asserted
against the two subjects that are real, by two `.github`-local gates:

| gate | asserts | subject |
|---|---|---|
| [`source-coherence.yml`](../../.github/workflows/source-coherence.yml) | `version` == Contracts **source** SemVer | `FS.GG.SDD@main` |
| [`feed-coherence.yml`](../../.github/workflows/feed-coherence.yml) | `package-version` == newest **live on the feed** | the org NuGet feed |

Both are path-filtered PR + push + **daily schedule** (an SDD bump or a publish touches no file here,
so only a periodic read can see it), and both red only `.github` — the repo that owns the registry
and is the only one that can flip it. The `contracts-ref` input that fed the old check was removed
with it; no caller passed it.

**Do not add an assertion back here that depends on another repo's mutable `main`, or on the network
beyond this run's own restore.** A NuGet outage must not be able to wedge six repos' PRs — which is
also why `feed-coherence` reads the feed from `.github` and not from this workflow.

On the authority path, the typed validator is restored as a .NET tool from the org GitHub Packages
feed. `.github` therefore grants **`packages: read`** to its own `coherence.yml` caller (the
run-scoped `GITHUB_TOKEN` authenticates the restore — the package is public, but that feed still
requires a token). Receiver paths do not install the tool or read the feed. Existing caller grants
remain compatible; narrowing them is separate receiver-owned cleanup, not part of changing this
published context.

> **Resolved** (coherence id `registry-validator-typed`, [.github#49](https://github.com/FS-GG/.github/issues/49)):
> the schema check is now the typed `Fsgg.Registry` validator, not a Python stand-in. SDD#26 gave
> `Fsgg.Registry` a real `RegistryDocument.load` (YAML) + `validateDocument` + a `fsgg-sdd registry
> validate` CLI; [SDD#32](https://github.com/FS-GG/FS.GG.SDD/issues/32) (CLI `0.2.1`) fixed the last
> divergence (4-segment versions such as `1.2.1.1`), restoring byte-for-byte parity, and #49 wired
> the gate onto it. (The version-coupling this note also referred to has since moved out of this
> workflow entirely — see *What it deliberately no longer checks*, [.github#741](https://github.com/FS-GG/.github/issues/741).)

## Adoption — retaining the stable receiver context

The published job id remains `coherence`, so callers and branch protection keep the
`contract-coherence / coherence` context unchanged:

```yaml
permissions:
  contents: read
jobs:
  contract-coherence:
    uses: FS-GG/.github/.github/workflows/contract-coherence.yml@main
```

`.github` gates itself with [`coherence.yml`](../../.github/workflows/coherence.yml) (the local
`./.github/workflows/contract-coherence.yml@<ref>` form), grants `packages: read`, and passes
`github-ref: ${{ github.sha }}`. That is the authority arm: it grades the exact hub commit under test.
