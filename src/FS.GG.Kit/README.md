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
| every file in each skill directory | `<skill-root>/<name>/<relative-path>`, for each root in `FsggKitSkillRoots` |
| `fsgg-coord` client | `scripts/fsgg-coord` (made executable) |
| `fsgg-coord-report` client | `scripts/fsgg-coord-report` (made executable; local session ledger) |
| engine tool manifest | `.config/dotnet-tools.json` |
| `skill-view` client | `scripts/skill-view` (made executable) |
| `skill-view`'s libraries | `scripts/lib/args.sh`, `scripts/lib/roots.sh` |

Every copy is **content-addressed and mode-addressed**: the materialize verifies each file's SHA-256
and executable bit against the package (`kit/kit-manifest.tsv`), removes undeclared files from managed
skill directories, and **fails the build** on a missing or mismatched
file (ADR-0014). A silently missing skill — the one failure mode worse than a loud sync PR — cannot
happen. For the coordination kit the digest is the same one that writes `registry/repos.lock`, so the
package and the byte-copy fabric cannot diverge.

## `kit/kit-manifest.tsv` receiver contract (v1)

Receivers may read the packed manifest as UTF-8, one TAB-separated row per file. Version 1 has exactly
five columns: `kind`, package-relative path, destination, lowercase SHA-256, and `true|false`
executable. `kind` is exactly one of `skill`, `client`, `config`, or `build-config`. The destination is
receiver-relative for `client`, `config`, and `build-config`; a `skill` destination is relative to a
configured skill root. Consumers must not infer a skill root from that field, nor rely on row order or
package-relative layout beyond these columns. A new kind or column layout is a contract change and is
guarded before publication.

## The build-config capability (opt-in)

The package also carries the `build-config` capability — the byte-identity set `scripts/sync-build-config.sh`
distributes: `dist/dotnet/Directory.Build.props` and `Directory.Packages.props`, derived from that
script's `FILES` list. It is **off by default**: build-config reaches only four receivers
(sdd/rendering/governance/game) — not templates/audio/net — and `.github` imports rather than copies
it. A receiver that today `receives: build-config` sets `FsggKitMaterializeBuildConfig=true`; the files
then materialize to the **repo root** and are committed, exactly as the `sync-build-config` byte-copies
are — this is the write arm that replaces that copy, not a live per-build input. `global.json` stays
**unmanaged** (`.github#903`, per-repo SDK bands are legitimate) and is not carried. Build-config has no
`repos.lock` row — that capability uses the ADR-0036 pin model, so "behind" is a version-pin decision
(which `FS.GG.Kit` a receiver references), not drift. Repo-specific overrides live in
`Directory.Build.local.props` / `Directory.Packages.local.props`, which the materialize never touches.

> **For the receiver switch (not this slice):** the per-receiver adoption must add the
> **adopt/marker safety** `sync-build-config.sh` has (`.github#387`: refuse to clobber a *hand-authored*
> `.props`, route it through an imported `*.local.props`), and should run the build-config materialize
> **explicitly** (`-t:FsggKitMaterialize` with `FsggKitMaterializeOnBuild=false`) rather than on every
> build. Tracked on [.github#1262](https://github.com/FS-GG/.github/issues/1262).

### Knobs (set before the package reference, or in a `Directory.Build.props`)

| property | default | meaning |
|---|---|---|
| `FsggKitReceiverRoot` | referencing project's dir | repo root the kit materializes into |
| `FsggKitSkillRoots` | `.claude/skills;.agents/skills` | skill roots to **materialize into**. Since 0.15.0 this is no longer the same thing as the runtime root set — see `FsggKitViewSkillRoots` |
| `FsggKitRetiredSkillRoots` | `.codex/skills` | roots the contract has retired — the materializer removes the kit's own skill directories from each, so a receiver never hand-deletes a mirror (ADR-0065 as amended by ADR-0067 §5) |
| `FsggKitViewSkillRoots` | *(empty)* | roots that are **still runtime roots** but whose content is a locally **generated view** (`scripts/skill-view generate`), not a materialized copy. The materializer does not copy into one, removes the copies an older package put there, and then **asserts every kit skill is visible** and fails the build if not (ADR-0067 §8, ADR-0065 §Retiring a root, `.github#1696`) |

The three root properties are three **dispositions**, and a root may appear in at most one — the
materializer refuses a root named twice rather than picking. The **runtime** root set (what
`.agent-skill-roots`, `Fsgg.Schemas.agentSkillRoots`, `coordination-sync` and `KitDigest` must agree
on) is `FsggKitSkillRoots` ∪ `FsggKitViewSkillRoots`: moving a root from the first to the second keeps
that union constant, which is exactly why it is not a root retirement.

`FsggKitCheckSkillView` runs automatically after every materialize and is also invocable on its own —
`dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitCheckSkillView` — for a receiver that wants
§8's assertion on **every** pull request rather than only on kit-bump PRs.
| `FsggKitMaterializeOnBuild` | `true` | materialize as part of the build; `false` to run `-t:FsggKitMaterialize` explicitly |
| `FsggKitMaterializeBuildConfig` | `false` | also materialize `Directory.Build.props` + `Directory.Packages.props` to the repo root |

## Status

Part of the [.github#1262](https://github.com/FS-GG/.github/issues/1262) migration (ADR-0062). Landed:
the producer (#1274), the publish workflow (#1276), and this `build-config` fold. It changes nothing for
receivers yet — the byte-copy fabric is untouched and still authoritative. Remaining: the per-receiver
switch to a package reference, and retiring the `*-propagate` / `*-selftest` / `*-coherence` workflow
family once no receiver byte-copies the kit.
