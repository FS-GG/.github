# Skill context-budget rollout evidence

- **Date:** 2026-07-26
- **Roadmap:** [Skill context budget and progressive disclosure](2026-07-25-223627-skill-context-budget-and-progressive-disclosure-roadmap.md)
- **Board item:** [FS-GG/.github#1416](https://github.com/FS-GG/.github/issues/1416)
- **Producer change:** [FS-GG/.github#1439](https://github.com/FS-GG/.github/pull/1439), merged as `5658f3a34aa374da99f2b601be0ef0fdfc500395`

## Release order and feed evidence

The rollout kept the publish-before-flip order:

1. `FS.GG.Coord.Cli` **0.11.0** published from `coord-engine/v0.11.0`.
   [Run 30182566864](https://github.com/FS-GG/.github/actions/runs/30182566864) passed the
   engine tests, real-package install/decision smoke, GitHub Packages push, OIDC login, and
   nuget.org push. The release makes M5's machine-readable `command-contract` reachable by
   receivers.
2. After 0.11.0 was readable on both feeds, `FS.GG.Kit` **0.6.0** published from
   `kit/v0.6.0`. [Run 30182740247](https://github.com/FS-GG/.github/actions/runs/30182740247)
   passed directory staging, materialization, tamper rejection, real-consumer restore, and both
   feed pushes. Its materialized `.config/dotnet-tools.json` pins the engine at 0.11.0.
3. `FS.GG.Drivers` stayed at **0.6.0**. That published package already contains the final M3/M4
   directory payload and policy metadata; M5 changed the coordination engine and gates, not driver
   bytes. Republishing unchanged content would create a false release.

The GitHub Packages and nuget.org `.nupkg` files were downloaded independently for both releases.
After excluding nuget.org's repository-added `.signature.p7s`, their member lists and every member
byte compared equal. The public Kit payload was also opened directly and its engine pin read back as
0.11.0.

The package registry then converged atomically in
[FS-GG/.github#1443](https://github.com/FS-GG/.github/pull/1443), merge `f651d2a8`. An atomic update
was required because the aggregate feed gate compares every package-bearing row at once; the same
transaction recorded Audio 0.5.0, Game 0.10.1, Net 0.3.2, and Coord.Cli 0.11.0. On the merged
revision, the generated projection covered all 24 coherence identifiers and 20 contract-version
literals, and the live feed gate matched every package-bearing contract.

The later Rendering 0.19.1 and Net 0.4.0 publications necessarily opened one more publish-before-flip
window. [FS-GG/.github#1448](https://github.com/FS-GG/.github/pull/1448), merge `7d15a88a`,
closed it in one aggregate transaction: the template version/tag and its shipped Audio/Game consumer
edges advanced together, alongside the Net coherent set. The final live-feed check was green again
before this report was proposed.

## Receiver rollout

Every receiver was re-pinned to Kit 0.6.0 and materialized from the public package. The local
materializer reported 68 files for coordination-only receivers and 70 for build-config receivers.
`scripts/coordination-sync --check` then verified every declared file against canonical, including
relative resource paths and executable modes.

| Receiver | PR | Result |
|---|---:|---|
| FS.GG.SDD | [#698](https://github.com/FS-GG/FS.GG.SDD/pull/698) | merged `a1c7eff7`; three roots, tool pin, and build config coherent |
| FS.GG.Rendering | [#1055](https://github.com/FS-GG/FS.GG.Rendering/pull/1055) | merged `f56e897f`; three roots, tool pin, and build config coherent |
| FS.GG.Governance | [#315](https://github.com/FS-GG/FS.GG.Governance/pull/315) | merged `1046db7a`; three roots, tool pin, and build config coherent |
| FS.GG.Templates | [#296](https://github.com/FS-GG/FS.GG.Templates/pull/296) | merged `989f7f80`; three roots and tool pin coherent |
| FS.GG.Game | [#500](https://github.com/FS-GG/FS.GG.Game/pull/500) | merged `b1ec9a96`; three roots, tool pin, and build config coherent |
| FS.GG.Audio | [#207](https://github.com/FS-GG/FS.GG.Audio/pull/207) | merged `2df9e1da`; three roots and tool pin coherent |
| FS.GG.Net | [#27](https://github.com/FS-GG/FS.GG.Net/pull/27) | merged `4bcae80f`; three roots and tool pin coherent |

Rendering's first Kit PR correctly remained red while its independently owned template payload still
pointed at older Audio and Game packages. Rendering
[#1056](https://github.com/FS-GG/FS.GG.Rendering/pull/1056) then shipped both consumer updates together
as `FS.GG.UI.Template` **0.19.1**, tags `v0.19.1` and `fs-gg-ui-template/v0.19.1`, from merge
`db9bc7cf`. [Run 30184202520](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/30184202520)
passed the pre-tag validators and both-feed publication. Independent downloads from GitHub Packages
record `1066978230` and nuget.org compared byte-identical after excluding only nuget.org's added
signature; the public payload pins Audio 0.5.0 and Game 0.10.1.

After the final merge, fresh archives of all seven `origin/main` revisions were checked rather than
trusting the PR worktrees. Every archive carried Kit 0.6.0 and Coord.Cli 0.11.0, and every
`coordination-sync --check` passed the complete declared directory, byte, and executable-mode
contract across `.claude`, `.codex`, and `.agents`.

The producer manifests were regenerated with `scripts/generate-driver-manifest --write`.
`registry/driver-skill-manifest.json` and
`registry/coordination-kit-skill-manifest.json` were already byte-current. The central registry was
then reconciled from the live SDD, Rendering, and Game producer checkouts with
`scripts/fsgg-skill-registry-check --write --now 2026-07-26`: zero digest, predicate, mirror,
ownership, or row changes were required. A second read-only pass reported
`registry = manifest = bytes`.

## Fresh host sessions

Fresh startup/session probes used the materialized `.github` tree and the re-materialized SDD tree
as a representative product workspace.

### Claude Code

Claude Code startup debug loaded project skills directly from the declared `.claude/skills` root:

| Workspace | Project skills discovered | Description-shortening warnings |
|---|---:|---:|
| `.github` | 11 | 0 |
| `FS.GG.SDD` | 32 | 0 |

The installed CLI had no usable Claude login, so startup/catalog evidence was collected but an
inference turn was not fabricated. Startup still completed skill discovery before the authentication
failure and recorded the exact project root and counts. The `.github` directory names were:
`check-board`, `cross-repo-coordination`, `cut-nuget-release`, `drive-board`,
`intra-repo-parallel-work`, `lane-steward`, `pnext-item`, `publishing-and-deployment`,
`spectre-console`, `work-board`, and `work-roadmap`.

### Codex

Fresh non-interactive Codex sessions emitted no FS-GG description-shortening warning.

- In `.github`, the implicit catalog exposed the seven eligible skills:
  `check-board`, `cross-repo-coordination`, `intra-repo-parallel-work`, `lane-steward`,
  `pnext-item`, `publishing-and-deployment`, and `spectre-console`. The high-impact driver/release
  skills were absent from implicit matching as intended.
- In SDD, the catalog exposed 21 FS-GG skills: the four coordination skills eligible in that
  workspace plus 17 `fs-gg-sdd-*` process skills. The session selected `check-board` for a stale-board
  diagnosis and loaded its `SKILL.md`; it selected nothing for a generic future-release sentence.
- A literal `$drive-board` inside a raw `codex exec` prompt is text, not the application's structured
  skill-selection attachment, and therefore did not make an explicit-only skill available. This is a
  host-interface boundary, not evidence that implicit matching should be re-enabled. The supported
  structured selector was exercised by the initiating `$drive-board` request for this rollout, while
  the semantic fixture independently requires the selector for every explicit-only positive case.

The generated catalog report measured 2,132 description characters in each authored root. For a
Codex installation exposing both `.codex` and `.agents`, the conservative effective authored metadata
was **5,461 / 6,000 characters**, leaving **539 characters** of reserved headroom. Runtime duplicate
suppression changes exposure, never the materialized directories.

## Routing and progressive-disclosure evidence

`scripts/check-skill-quality` passed all 11 skills and reported:

- valid frontmatter, names, optional metadata, directory mirrors, resource links, and executable modes;
- catalog budgets and the 539-character Codex effective headroom;
- every documented `fsgg-coord` command/flag against the 0.11.0 parser contract;
- semantic separation of dangerous command pairs;
- current generated facts; and
- forward routing for coordination diagnosis, one-item work, parallel board driving, release,
  roadmap, and Spectre diagnosis.

The forward fixtures require selectors for explicit-only drivers, require positive implicit routing
without selectors for advisory skills, and include negative broad-word prompts that must select
nothing. Representative runtime probes agreed for `check-board` and the negative release sentence.
Progressive disclosure kept the initial route to `SKILL.md`; detailed board mechanics, contract/release
coordination, and worker-host loops remain named references loaded only by the path that needs them.

## Architecture record

[ADR-0065](../adr/0065-one-agent-skill-root-contract.md) and
[the architecture map](../architecture.md) now distinguish:

- **transport/parity:** complete skill directories, including references, `agents/openai.yaml`, and
  executable resources, must be byte-identical in `.claude`, `.codex`, and `.agents`; from
- **runtime exposure:** a host catalogs its supported root and may suppress a duplicate catalog entry
  without deleting or desynchronizing a transported mirror.

This explicitly supersedes ADR-0014's earlier `SKILL.md`-body-only manifest wording while retaining
its one-owner and content-addressed-materialization decisions.
