# FSC-00 executable and call-site census

Date: 2026-09-25. Owner: `.github`. Status: source census baseline for the parallel F# automation track; no replacement or receiver activation is authorized by this report.

## Method and replay

The [read-only census command](../../scripts/fsc-census.py) enumerates `git ls-files -s` at each checkout, reads source shebangs and executable bits, parses tracked workflow and composite-action YAML with `yaml.BaseLoader`, records their `run:` bodies and `uses:` references, extracts existing `scripts/...` references, and reads `.config/dotnet-tools.json` pins when present. Lexical interpreter hints identify inline Python and shell commands without claiming reachability. It emits the full per-file, per-step, and per-reference JSON inventory. A malformed workflow or composite action, or an executable with an unknown interpreter, produces an `issues` entry and exit 2. The [fixture](../../tests/fsc-census/run.py) covers tracked template assets, extensionless launchers, inline workflow and action Python, Bash steps, nested action references, receiver tool pins, untracked exclusion, and malformed YAML refusal. The recorded source checkouts were clean at measurement; the eight sibling checkouts are local snapshots, not claims about live default branches or installed receiver bytes.

```bash
uv run --with pyyaml==6.0.3 python3 scripts/fsc-census.py --root . > /tmp/fsc00-dotgithub.json
uv run --with pyyaml==6.0.3 python3 tests/fsc-census/run.py
# Repeat --root for each named sibling checkout; inspect .issues before using a count.
# For a local authenticated org/roster comparison, keep the API snapshot OUT of the repository:
umask 077
gh api --paginate 'orgs/FS-GG/repos?per_page=100&type=all' \
  | jq -s 'add | map({full_name, visibility, archived})' > /tmp/fsc00-org-repos.json
uv run --with pyyaml==6.0.3 python3 scripts/fsc-census.py --root . \
  --org-repos-json /tmp/fsc00-org-repos.json > /tmp/fsc00-org-reconciliation.json
```

One warm local run over `.github` took 0.702 seconds and emitted 735,428 JSON bytes (Python `perf_counter`, command completion, no hosted CI cost claim). The output is intentionally generated on demand instead of committing a snapshot that could be mistaken for current receiver state.

## Measured source snapshots

Counts are tracked source files by interpreter family. Python includes `.py` and Python extensionless launchers; shell includes `.sh` and shell extensionless launchers; F# means `.fsx`; other includes Node/PowerShell/other shebangs. A workflow step is a parsed `run:` step, not proof that the step was reachable or ran. `Script steps` means run bodies containing an exact tracked `scripts/...` token; it can miss indirect and `src/...` calls. All nine scans returned zero parser/interpreter issues. The `.github` row is the fresh `origin/main` worktree used for this PR; sibling rows are clean local checkouts at the stated HEADs.

| Owner checkout | HEAD | Tracked files | Python | Shell | F# | Other | Workflows | Run steps | `uses:` refs | Script steps | Coord CLI pin |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| `.github` | `2e553e41e58e` | 1,895 | 276 | 168 | 5 | 2 | 141 | 529 | 407 | 150 | no local manifest |
| SDD | `cb89bcafb95e` | 2,118 | 1 | 34 | 4 | 3 | 7 | 51 | 46 | 22 | 0.90.0 |
| Templates | `273c92189602` | 539 | 14 | 95 | 3 | 30 | 13 | 51 | 47 | 5 | 0.90.0 |
| Audio | `c48cfec74468` | 199 | 6 | 8 | 0 | 1 | 9 | 40 | 32 | 6 | 0.91.4 |
| Game | `d399c36f35a2` | 440 | 8 | 28 | 4 | 0 | 12 | 69 | 70 | 25 | 0.90.0 |
| Rendering | `66836abdaef8` | 4,086 | 14 | 39 | 75 | 14 | 21 | 88 | 114 | 37 | 0.90.0 |
| Governance | `9a32eb839381` | 2,895 | 1 | 15 | 6 | 1 | 6 | 56 | 56 | 6 | 0.91.4 |
| Coordination | `bf893ceaad63` | 2,192 | 40 | 45 | 94 | 9 | 12 | 87 | 139 | 0 | 0.91.4 |
| Net | `8c2855fba8fc` | 119 | 1 | 6 | 0 | 0 | 5 | 20 | 14 | 2 | 0.90.0 |
| S.I.R. (later local scan; 34 issues) | `80e1ac932886` | 2,105 | 17 | 89 | 5 | 131¹ | 3 | 85 | 140 | 31 | 0.87.0 |

The `.github` inventory contains 21 extensionless scripts, including [`fsgg-coord`](../../scripts/fsgg-coord), [`skill-view`](../../scripts/skill-view), [`generate-projections`](../../scripts/generate-projections), and [`fsgg-surface-impact`](../../scripts/fsgg-surface-impact). A follow-up scan on this draft includes `.github/actions/setup-policy-python/action.yml`: its composite action has one Bash `run:` body invoking Python and one nested `uses:` reference. The original workflow-only scan omitted both. The tracked-file scan also includes template assets by path and shebang, but copying or generating them into a receiver is a separate effect that this source scan does not prove. There is no `.github/.config/dotnet-tools.json`; the manifest in receivers must be inspected at its owner. A zero `.py` count is never an absence proof for shell, `.fsx`, extensionless or generated tool paths. Source-file counts include tests, so they are not counts of production commands or F# port obligations.

`registry/repos.yml` currently has 10 rows: the original nine-checkout baseline plus `EHotwagner/S.I.R.`. A later read-only scan of a clean local S.I.R. checkout added the tenth row. ¹Its `Other` count includes 34 tracked `SKILL.md`/YAML files with executable mode and no shebang; the census reported all 34 as unknown interpreter issues and exited 2. Those files need owner disposition before that row can be called clean.

## Authenticated organization and roster reconciliation

An authenticated GitHub organization repositories API read on 2026-09-25 returned 17 FS-GG repositories, none archived. The same checkout's `registry/repos.yml` has nine of those in `repos:` plus external `EHotwagner/S.I.R.`; `outside-fabric:` explicitly names public [`FS-GG/FsQuint`](https://github.com/FS-GG/FsQuint). Seven organization repositories have neither a roster nor an outside-fabric entry. The four public members of that unclassified set are [`FS.GG.Coordination.Authority`](https://github.com/FS-GG/FS.GG.Coordination.Authority), [`FS.GG.Coordination.Authority.Sandbox`](https://github.com/FS-GG/FS.GG.Coordination.Authority.Sandbox), and the two public synthetic SVG workspace qualification repositories. Three additional unclassified repositories are private; their identifiers and the authenticated API snapshot are intentionally kept out of this public report. The optional `--org-repos-json` output gives an exact local mapping for a reader authorized to inspect that snapshot.

| Population | Count | Census meaning |
| --- | ---: | --- |
| Org repositories in `repos:` | 9 | Source checkouts measured in the original baseline; membership alone does not prove an installed receiver. |
| External repository in `repos:` | 1 | S.I.R. is user-owned, role `non-participant`, and has no org-fabric receiver declaration. Its later source scan had 34 issues. |
| Org repositories in `outside-fabric:` | 1 | FsQuint has an explicit recorded exclusion from the coordination fabric. |
| Org repositories with no roster disposition | 7 | Four public and three private; classify their receiver status with owner evidence before claiming closure. |
| Archived org repositories | 0 | No archived row in this API snapshot. |

The 17 organization repositories and the external S.I.R. row are distinct populations. This reconciliation identifies roster omissions and exclusions; it does not classify all seven unrostered repositories as receivers or prove installed artifacts, runtime pins, or cutover readiness.

## Owner boundaries and first port candidates

| Classification | Source and observed call site | FSC disposition |
| --- | --- | --- |
| Direct policy port | `.github` [`check-paths-coherence.py`](../../scripts/check-paths-coherence.py) and [workflow](../../.github/workflows/paths-coherence.yml). | FSC-03 may scaffold its parser and tests now. Rule (a) parity waits for accepted [split-guard repair #3698](https://github.com/FS-GG/.github/pull/3698); the live Python gate remains. |
| Direct owner port | Templates [`check-provider-floors.py`](https://github.com/FS-GG/FS.GG.Templates/blob/273c92189602/scripts/check-provider-floors.py), [`generate-effective-providers.py`](https://github.com/FS-GG/FS.GG.Templates/blob/273c92189602/scripts/generate-effective-providers.py); `composition.yml`, `release.yml`, `tests/effective-providers/run.sh`, and `tests/composition/stages/04-verify.sh` call them. | FSC-05 F# provider source can run in parallel. Preserve descriptor enumeration, floor/registry refusals and generated block bytes; leave receiver activation to Templates. |
| Product-specific policy | Audio [`stage-skills.py`](https://github.com/FS-GG/FS.GG.Audio/blob/c48cfec74468/src/FS.GG.Audio.Skills/stage-skills.py), Game [`stage-skills.py`](https://github.com/FS-GG/FS.GG.Game/blob/d399c36f35a2/src/FS.GG.Game.Skills/stage-skills.py), Rendering [`stage-skills.py`](https://github.com/FS-GG/FS.GG.Rendering/blob/66836abdaef8/src/FS.GG.Rendering.Skills/stage-skills.py). Rendering's `verify-package.sh` calls its stager. | FSC-06 separate owner parity; Audio's source-closure repair #312 is a prerequisite for using its old implementation as a safe oracle. Do not combine differing BOM/CRLF policies. |
| Typed shared primitive | Rendering [`nuget-client-archive.py`](https://github.com/FS-GG/FS.GG.Rendering/blob/66836abdaef8/scripts/nuget-client-archive.py) is invoked by `release.yml`; manifest/hash/archive and generated registry work also lives in SDD and `.github`. | FSC-04/08 can extract narrow primitives while release and registry policy stay with their owners. Require archive member/mode/digest and installed-byte controls. |
| Protected effect adapter | Coordination [`callable-cli-isolated-operation.py`](https://github.com/FS-GG/FS.GG.Coordination/blob/bf893ceaad63/eng/callable-cli-isolated-operation.py), [`github-ledger-operation.py`](https://github.com/FS-GG/FS.GG.Coordination/blob/bf893ceaad63/eng/github-ledger-operation.py), [`github-v1-admission-provider-transport.py`](https://github.com/FS-GG/FS.GG.Coordination/blob/bf893ceaad63/eng/github-v1-admission-provider-transport.py). | FSC-07 characterization only until the GS2 owner qualifies an adapter. Keep credential custody, unknown-effect and readback rules; no effect-path rewrite follows from this census. |
| Launcher / independent oracle / generated receiver | `.github` [`fsgg-coord`](../../scripts/fsgg-coord) executes the F# engine; [tests](../../tests/) and shell launchers cover environment/setup and independent refusals; `registry/repos.yml` names package receivers. | Keep launchers and independent black-box checks where they carry distinct behavior. Inventory each generated receiver from its exact package and workflow before retirement. F# source does not make a shell launcher or Python attack test obsolete. |

## Remaining proof before a port or deletion

This static census inventories tracked source and parsed workflow/composite-action step bodies; it does **not** discover dynamically assembled commands, downloaded actions, non-composite action entrypoints, job condition reachability, workflow dispatch history, installed package members, generated workspaces, runtime interpreter versions, or every consumer of a published tool. Its lexical reference list can include comments and miss indirect calls. Those require each owner's clean-install, runtime and receiver evidence before any old path is removed. The current table does not grade the observed 0.90.0/0.91.4 pins as drift: compare them to each receiver's accepted contract and latest stable package first. The seven unclassified FS-GG repositories are outside the original nine-checkout source baseline and remain unmeasured as receivers by this PR; the script can be run against clean owner snapshots without changing this report's source claims.

FSC-00 is therefore a reproducible **source/call-site baseline**, not completion of the installed receiver census or a GS2 gate. The parallel FSC-05 provider source and FSC-03 parser scaffold can use these heads as inputs, then refresh against their own accepted base and record exact old/new corpus outcomes before proposing any workflow or package flip.
