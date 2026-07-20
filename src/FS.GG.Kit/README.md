# FS.GG.Kit

The FS-GG **coordination kit** as one versioned package (ADR-0062).

The kit — the shared coordination skills, the `fsgg-coord` client shim, and the engine tool
manifest — used to reach every receiver by **byte-identical file copy**: one hub change opened one
sync PR in each of N receivers (the report measured 80–116 sync commits per repo per ten days). This
package replaces that fan-out. A receiver **references** `FS.GG.Kit` at a pinned version and picks up
a change through the same Renovate + dispatch fabric every other FS-GG package already uses, exactly
as the `fsgg-coord` CLI already did (ADR-0034 §4.4).

## Derived, not restated (ADR-0058)

The package carries **no committed list** of kit files. At pack time `stage-kit.sh` reads the `kit:`
rows of `registry/repos.yml` — the one manifest `scripts/coordination-sync` also reads, through the
same `scripts/repos.sh` reader — and stages exactly that content-addressed set. Add or retire a kit
row and this package follows with no edit; the packaged kit and the legacy byte-copy fabric cannot
diverge while both exist.

## What a consumer gets

Referencing the package auto-imports `build/FS.GG.Kit.props` and `build/FS.GG.Kit.targets`, which
**materialize** the kit onto disk (a package reference is not a file, but the agent harness loads
skills as real files — ADR-0011 — and the client must be executable):

| kit member | materialized to |
|---|---|
| each skill's `SKILL.md` | `<skill-root>/<name>/SKILL.md`, for each root in `FsggKitSkillRoots` |
| `fsgg-coord` client | `scripts/fsgg-coord` (made executable) |
| engine tool manifest | `.config/dotnet-tools.json` |

Every copy is **content-addressed**: the materialize verifies each file's SHA-256 against the digest
recorded in the package (`kit/kit-manifest.tsv`, the same digest that writes `registry/repos.lock`)
and **fails the build** on a missing or mismatched file (ADR-0014). A silently missing skill — the
one failure mode worse than a loud sync PR — cannot happen.

### Knobs (set before the package reference, or in a `Directory.Build.props`)

| property | default | meaning |
|---|---|---|
| `FsggKitReceiverRoot` | referencing project's dir | repo root the kit materializes into |
| `FsggKitSkillRoots` | `.claude/skills;.agents/skills` | skill roots to materialize into |
| `FsggKitMaterializeOnBuild` | `true` | materialize as part of the build; `false` to run `-t:FsggKitMaterialize` explicitly |

## Status

This package is PR #1 of the [.github#1262](https://github.com/FS-GG/.github/issues/1262) migration:
it stands up the producer for the `coordination-kit` capability and its materialize contract, and
changes nothing for receivers yet — the byte-copy fabric is untouched and still authoritative.
Sequenced follow-ups (ADR-0062): the publish workflow, the per-receiver switch to a package
reference, folding in the `dist/dotnet` build-config half, and retiring the
`*-propagate` / `*-selftest` / `*-coherence` workflow family once no receiver byte-copies the kit.
