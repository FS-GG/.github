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
[delivered release source](https://github.com/FS-GG/FS.GG.Rendering/blob/0efc4e5d3356983b2fedc12558fd015053eb1a1c/.github/workflows/release.yml).
Its exact `FS.GG.UI` selection plus `FS.GG.UI.*` pattern covers the nineteen
packages in the current publication candidate. The workflow defaults to
`Paradigma11`, but `NUGET_USER` can override that default; this account readout does
not identify the effective value used by a job.

The earlier [nonpublishing preflight](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/40770369107)
returned HTTP400/`authorization-unproven` for all nineteen packages without
recognizing the known verification-policy message. Its original result is retained.

A subsequent [fresh nonpublishing run, attempt 1](https://github.com/FS-GG/FS.GG.Rendering/actions/runs/40781521738)
at executor `0efc4e5d3356983b2fedc12558fd015053eb1a1c` completed on 28 October
at 22:41 UTC. The exact-account guard passed before NuGet login, which also passed.
That establishes the effective `Paradigma11` login input for this invocation; it
does not inspect secret values, identify the matched server policy or prove
package authorization. This runtime observation narrows the earlier account
uncertainty without changing the owner's policy readout.

All nineteen original-key authorization GETs returned HTTP400 with the same
closed classification: `authorization-unproven:m-html:e-unspecified:s-bounded:r-unmatched`.
The responses declared HTML media type, had readable bounded payloads with no
declared charset, and did not match the recognized policy text. The classification
does not establish HTML document syntax or identify the response producer. Earlier
credential-free GETs at 21:22 UTC returned text/html with plain text matching
[Gallery’s missing-key response](https://github.com/NuGet/NuGetGallery/blob/c25bab040e19d24593fc0c298a0cedb51bf57dcb/src/NuGetGallery.Services/Authentication/Providers/ApiKey/ApiKeyAuthenticationHandler.cs#L57).
The current classification does not prove a proxy, firewall, account-policy or
Gallery defect. No raw response body, header
value or key was retained. The original scope artifact `12858125231` is 789 bytes,
SHA256 `7b95eebea76b4a39f7e9ef85c7de65394bc076fb4f5d2029e67180d0767bba74`;
all five original artifacts and their executor binding were independently checked.

[Issue 4817](https://github.com/FS-GG/.github/issues/4817) retains the original
failures and publication hold. The remaining question is the authenticated
HTTP400 response with HTML media type and supported original-key authorization contract. No repeat
probe, derived-key substitution or gate waiver follows from these observations.
No tag or package publication ran; a matching policy and successful login remain
separate from the required authorization result.

This capture changes knowledge, not account settings, release gates or operating
authority. Retrieve the active Atlas successor and reconcile current owner and
native facts before relying on these mutable policy values. Preserve this dated
readout when later evidence supersedes it.
