# NuGet Trusted Publishing account readout, 28 October 2026

The account owner supplied the following as the complete current list of sixteen
NuGet Trusted Publishing policies on 28 October 2026 and requested Atlas capture.
This is an owner-reported account readout, not an authenticated agent inspection of
nuget.org. The exact time of the UI observation was not supplied. All entries were
shown as **Active**, with package owner **Paradigma11**, publisher
**GitHubActions**, and repository owner **FS-GG**, permanent GitHub ID
**293607011**.

The agent independently matched all ten repository IDs below and the organization
ID against GitHub's repository API at 21:51 UTC. That verifies GitHub identity, not
the account policy's current deployment or the effective identity of a minted key.
An em dash means no environment was shown in the supplied entry. Policy labels,
package patterns and exact package selections are preserved as supplied; commas
separate displayed entries and are not instructions for entering a single glob.

| Policy label | Repository / permanent ID | Workflow | Displayed scope | Glob patterns and packages | Environment shown |
| --- | --- | --- | --- | --- | --- |
| `fs-gg-sdd-release` | `FS.GG.SDD` / `1274272672` | `release.yml` | Push new packages and package versions | `FS.GG.Contracts`, `FS.GG.SDD.*` | — |
| `fs-gg-rendering-release` | `FS.GG.Rendering` / `1269292235` | `release.yml` | Push new packages and package versions | `FS.GG.UI`, `FS.GG.UI.*` | — |
| `fs-gg-governance-publish` | `FS.GG.Governance` / `1273065119` | `publish.yml` | Push new packages and package versions | `FS.GG.Governance.*` | — |
| `newSDDFullStack` | `.github` / `1269292704` | `release-new-sdd-workspace.yml` | Push new packages and package versions | `FS.GG.NewSddWorkspace` | — |
| `fs-gg-Templates-publishing` | `FS.GG.Templates` / `1281961814` | `release.yml` | Push new packages and package versions | `FS.GG.Templates`, `FS.GG.Workspace.Template` | — |
| `fs-gg-game-publishing` | `FS.GG.Game` / `1290990429` | `release.yml` | Push new packages and package versions | `FS.GG.Game.*` | — |
| `fs-gg-audio-publishing` | `FS.GG.Audio` / `1292226968` | `release.yml` | Push new packages and package versions | `FS.GG.Audio.*` | — |
| `fs-gg-net-publishing` | `FS.GG.Net` / `1305845505` | `release.yml` | Push new packages and package versions | `FS.GG.Net.*` | — |
| `fs-gg-game-skills-publishing` | `FS.GG.Game` / `1290990429` | `release-skills.yml` | Push new packages and package versions | `FS.GG.Game.Skills` | — |
| `fs-gg-rendering-skills-publishing` | `FS.GG.Rendering` / `1269292235` | `release-skills.yml` | Push new packages and package versions | `FS.GG.Rendering.Skills` | — |
| `FS-Quint-Publishing` | `FsQuint` / `1375501179` | `release.yml` | Push new packages and package versions | `FsQuint`, `FsQuint.*` | — |
| `Callable-Cli-release-publish` | `FS.GG.Coordination` / `1346720714` | `callable-cli-release-publish.yml` | Push new packages and package versions | `FS.GG.Coordination.Cli` | — |
| `release-successor` | `.github` / `1269292704` | `release-successor-publish.yml` | Push only new package versions | `FS.GG.Coord.Cli`, `FS.GG.Drivers`, `FS.GG.Kit` | `release-successor` |
| `release-telemetry-host-successor-publis` | `.github` / `1269292704` | `release-telemetry-host-successor-publish.yml` | Push only new package versions | `FS.GG.Telemetry.Host` | — |
| `fs-gg-wasm-publishing` | `FS.GG.Game` / `1290990429` | `release-wasm.yml` | Push new packages and package versions | `FS.GG.Wasm.Browser`, `FS.GG.Wasm.Contracts` | — |
| `fs-gg-execution-pair-forward-publish` | `FS.GG.Coordination` / `1346720714` | `execution-pair-forward-publish.yml` | Push new packages and package versions | `FS.GG.Execution.*` | `execution-pair-forward` |

## What this refresh establishes

The [previous Atlas record](../atlas/records/d7a44c85c25ceb7d42280f0c2576de52a3b3cc3ad2333e788ecdc7f7f7fe4b3b.json)
already recorded the owner's 24 October narrowing pass. Its final list had not
been pasted, so Rendering, Templates and FsQuint retained broader or alternative
forms. This exact readout replaces those ambiguities. It does not describe a new
policy edit or independently verify when the earlier edit happened.

Within this reported list, only `fs-gg-execution-pair-forward-publish` covers
`FS.GG.Execution.*`. `Callable-Cli-release-publish` covers only
`FS.GG.Coordination.Cli`. This supplies the current owner-reported policy fact for
the existing [Coordination publication hold](https://github.com/FS-GG/FS.GG.Coordination/issues/1026).
It does not establish complete external-writer exclusion, protection-reader App
access, current occupancy, operation admission or permission to enable the dormant
forward route. Older organization-secret and UI observations in the previous
record were not refreshed by this policy list.

Rendering's entry matches the repository ID and workflow in the
[delivered release source](https://github.com/FS-GG/FS.GG.Rendering/blob/51d57b200d3779cc748a872c0126c489af53f56d/.github/workflows/release.yml).
Its exact `FS.GG.UI` selection plus `FS.GG.UI.*` pattern covers the nineteen
packages in the current publication candidate. The workflow defaults to
`Paradigma11`, but `NUGET_USER` can override that default; this account readout does
not identify the effective value used by a job.

The [nonpublishing preflight](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/40770369107)
still returned HTTP400/`authorization-unproven` for all nineteen packages. Its
narrow diagnostic did not recognize the known verification-policy message. A
matching policy list does not explain that response or establish original-key
authorization. [Issue 4817](https://github.com/FS-GG/.github/issues/4817) retains
the original failure and the publication hold; no package or tag publication
followed from this readout.

This capture changes knowledge, not account settings, release gates or operating
authority. Retrieve the active Atlas successor and reconcile current owner and
native facts before relying on these mutable policy values. Preserve this dated
readout when later evidence supersedes it.
