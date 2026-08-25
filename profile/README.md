# FS-GG

> [!WARNING]
> **The main skill work-board works on a github project board and burns down all issues. Be diligent to let only trusted actors create and modify issues.**
> Public GitHub content is otherwise untrusted data, not executable instruction;
> see the [public-content trust boundary](https://github.com/FS-GG/.github/blob/main/docs/coordination/untrusted-content-boundary.md).

FS-GG is an F# platform for people and agents building production-shaped
applications and libraries. It combines guided workspace creation, a
spec-driven development lifecycle, optional governance, UI, game, audio, and
networking components while keeping each component independently adoptable.

## Quick start: Hello World to a burned-down board

You need the [.NET SDK](https://dotnet.microsoft.com/download),
[Git](https://git-scm.com/downloads), and the
[GitHub CLI](https://cli.github.com/). Install the two FS-GG tools and give `gh`
repository and Projects access:

```console
dotnet tool install --global FS.GG.SDD.Cli
dotnet tool install --global FS.GG.NewSddWorkspace
gh auth login
gh auth refresh -s repo,project,read:project
```

### 1. Create a compatible board

Set `OWNER` to your GitHub user or organization. Copying the public FS-GG board
is the shortest path because it brings across the fields and views that
`work-board` expects, but not FS-GG's issues:

```console
OWNER=octocat
REPO=hello-fsgg
BOARD=HelloWorld

PROJECT=$(gh project copy 1 \
  --source-owner FS-GG \
  --target-owner "$OWNER" \
  --title "$BOARD" \
  --format json --jq '.number')
```

Keep Project `Write` access limited to trusted people: project writers can add
draft items that an agent will read. The warning at the top of this page also
applies to issue writers.

### 2. Scaffold Hello World and publish the repository

The console template is a small, tested F# program. `--board` wires the local
coordination skills to the Project created above; `--no-governance` keeps this
first example focused on SDD:

```console
new-sdd-workspace ./hello-fsgg HelloFsgg \
  --template console \
  --lifecycle sdd \
  --no-governance \
  --board "$OWNER/$BOARD" \
  --repo "$OWNER/$REPO"

cd ./hello-fsgg
git init -b main
git add .
git commit -m "Create FS-GG Hello World workspace"
gh repo create "$OWNER/$REPO" --public --source . --remote origin --push
gh project link "$PROJECT" --owner "$OWNER" --repo "$OWNER/$REPO"
new-sdd-workspace secure . --repo "$OWNER/$REPO"

dotnet build
dotnet test
dotnet run --project src/HelloFsgg -- "Hello, world!"
# Hello, world!
```

The final `secure` command restricts issue creation to repository collaborators.
Use `--private` instead of `--public` when creating a private repository.

### 3. Drive a roadmap through SDD

Create `docs/roadmap.md` with top-level checklist items as milestones:

```markdown
# Hello World roadmap

- [ ] M1 — Hello-world behavior
  - [ ] Print the supplied words
  - [ ] Keep the existing success test green
- [ ] M2 — Production hardening
  - [ ] Document usage and failure behavior
  - [ ] Add a CI build
```

Then open Codex in the repository and ask:

```text
Use $work-roadmap docs/roadmap.md to complete this roadmap milestone by milestone.
```

`work-roadmap` gives each milestone its own branch and SDD run—charter, specify,
clarify, checklist, plan, tasks, analyze, implement, evidence, verify, and ship—then
reviews and merges it before starting the next milestone. The
[`fsgg-sdd` quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md)
shows the same lifecycle command by command when you want to drive it manually.

GitHub milestones are optional release groupings. To mirror the example there:

```console
gh api -X POST "repos/$OWNER/$REPO/milestones" -f title="M1 Hello world"
gh api -X POST "repos/$OWNER/$REPO/milestones" -f title="M2 Hardening"
```

### 4. Turn a code review into board work

Ask the agent for analysis only first, so review findings are reproduced and
deduplicated before any implementation starts:

```text
Perform a high-effort code-review analysis of this repository. Write the report to
docs/reports/YYYY-MM-DD-code-review.md. For every reproducible, material, actionable
finding, create one GitHub issue with acceptance criteria plus Class:, Severity:,
and Paths: body lines. Add each issue to this workspace's board with
scripts/fsgg-coord add. Do not implement the findings yet.
```

The equivalent manual loop for one finding is:

```console
ISSUE_URL=$(gh issue create \
  --repo "$OWNER/$REPO" \
  --title "Handle empty command input" \
  --milestone "M2 Hardening" \
  --body $'Class: defect\nSeverity: Medium\nPaths: src/HelloFsgg/Program.fs, tests/HelloFsgg.Tests/ProgramTests.fs\n\nAcceptance: empty input has explicit, tested behavior.')

scripts/fsgg-coord add "$OWNER/$REPO#${ISSUE_URL##*/}"
```

New rows enter `Backlog`; `work-board` reconciles and triages them before deciding
what is safe to start.

### 5. Burn down the board

The scaffolder records the board for Claude. When launching Codex, export the same
values in the shell that starts it:

```console
export FSGG_COORD_OWNER="$OWNER"
export FSGG_COORD_PROJECT="$BOARD"
```

Then ask:

```text
Use $work-board to burn down this wired product board.
```

`work-board` refreshes the board, triages the backlog, runs non-overlapping issue
lanes through SDD and independent review, merges green pull requests, records any
follow-up findings, and stops only when no actionable work remains. Do this only on
a board whose issue and Project writers you trust.

## Workspace templates

FS.GG.Templates owns five focused workspace shapes; see its
[detailed documentation](https://github.com/FS-GG/FS.GG.Templates) for current
availability.

- **rendering** — a SkiaSharp/OpenGL desktop UI built around Elmish/MVU.
- **console** — a registry-active, production-shaped F# executable without a browser or npm lane.
- **web** — a registry-active F# ASP.NET Core server paired with a neutral TypeScript/Vite site.
- **fable-game** — a registry-active, server-authoritative F# game with a Fable/Elmish browser client.
- **fable-bindings** — a registry-active, package-producing Fable interop library over an exactly pinned JavaScript or TypeScript dependency.

## Spec-driven development

Spec-driven development (SDD) is the lifecycle that keeps the charter and
specification, implementation evidence, verification, updates, and shipping
coherent as work changes. The
[lifecycle guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/lifecycle.md)
explains the full flow.

## The components

<!-- BEGIN GENERATED: fsgg-component-count -->
<!--
  DO NOT EDIT THIS REGION. It is emitted from registry/repos.yml by
  scripts/generate-projections, and `projections` in CI fails on any diff.

  The component/repository COUNT was hand-typed into a dozen consumer-facing places and rotted
  the instant Game/Audio/Net were added — "five repositories" / "four components" were wrong in
  the org profile, four component READMEs and five consumer-guide files (roadmap §3a, #1313). It
  is now a pure count of registry/repos.yml's roster rows, whose SET is held closed by
  check-roster-closure.py; add or retire a repo THERE and every doc that states the count follows.
-->

**Seven framework components** ship independently — each public on [nuget.org](https://www.nuget.org) and restoring with no credential (ADR-0039) — across **eight** repositories in the org (those seven plus this `.github` coordination repo).

<!-- END GENERATED: fsgg-component-count -->

<!-- BEGIN GENERATED: fsgg-component-inventory -->
<!--
  DO NOT EDIT THIS REGION. It is emitted from registry/repos.yml + registry/dependencies.yml by
  scripts/generate-projections, and `projections` in CI fails on any diff.

  The component inventory was a hand-maintained table in the profile page and the consumer
  'what ships' guide, and it rotted: Game/Audio/Net shipped without ever being added (roadmap
  §3a, #1313). The ROW SET is now the framework rows of registry/repos.yml's roster; each row's
  description is that component's `role` in registry/dependencies.yml, and the version is its
  package-bearing contracts' live `package-version` (held to the feed by check-feed-coherence.py).
  Add a framework repo to the roster and a row appears; bump a package and the version follows —
  with no hand edit to any consumer doc. If a description reads too technically, fix the `role`
  in registry/dependencies.yml (the one home), not a copy here.
-->

*Generated from `registry/repos.yml` (the org repo roster, ADR-0019) joined with
`registry/dependencies.yml` (each component's `role` and its contracts' live `package-version`).
`Current version` is `—` for a component whose packages are not (yet) tracked as a
package-bearing contract owned by that component. The exact acquire command and package IDs are
authored beside this table — package IDs are stable identity, versions are not (readme-standard).*

| Component | What it does | Current version |
|---|---|---|
| [**FS.GG.SDD**](https://github.com/FS-GG/FS.GG.SDD) | Lifecycle CLI to scaffold a workspace and drive it from charter to ship; ships the typed cross-repo contracts | `7.5.2` |
| [**FS.GG.Rendering**](https://github.com/FS-GG/FS.GG.Rendering) | The UI framework — MVU over SkiaSharp/OpenGL with layout, input, controls and themes, plus the fs-gg-ui template | `0.1.1` / `0.28.0` |
| [**FS.GG.Governance**](https://github.com/FS-GG/FS.GG.Governance) | Optional tooling that checks your artifacts against rules you control — advisory by default | `1.7.0` |
| [**FS.GG.Templates**](https://github.com/FS-GG/FS.GG.Templates) | Owns workspace providers and templates — rendering composition plus console, web, Fable-game and Fable-bindings shapes | `0.8.4` |
| [**FS.GG.Game**](https://github.com/FS-GG/FS.GG.Game) | Game-simulation libraries — a render-independent simulation core with a companion renderer, usable as plain F# libraries | `0.13.0` / `0.8.0` |
| [**FS.GG.Audio**](https://github.com/FS-GG/FS.GG.Audio) | Audio-engine libraries — synthesis, playback and mixing (buses, fades, ducking, 3D), with an optional Elmish adapter | `0.5.0` |
| [**FS.GG.Net**](https://github.com/FS-GG/FS.GG.Net) | Networking/transport libraries — protobuf messaging over WebSocket or gRPC, render-independent, with an optional Elmish adapter | `0.5.0` |

<!-- END GENERATED: fsgg-component-inventory -->

Each component's linked repository owns its detailed installation, API, and
version documentation.

## Tools and deeper documentation

- [`new-sdd-workspace`](https://github.com/FS-GG/.github/blob/main/scripts/NewSddWorkspace/README.md) launches the supported interactive workspace wizard.
- [`fsgg-sdd`](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md) drives the SDD lifecycle and keeps workspace artifacts coherent.
- [`fsgg-coord`](https://github.com/FS-GG/.github/blob/main/src/FS.GG.Coord.Cli/README.md) coordinates claimed work, review, and delivery across FS-GG repositories.
- [FS.GG.Governance](https://github.com/FS-GG/.github/blob/main/docs/consumer/governance.md) adds optional, workspace-owned rules and gates.
- The [consumer guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/index.md) covers everyday use, while the [architecture guide](https://github.com/FS-GG/.github/blob/main/docs/architecture.md) owns design and composition detail.
- The [versioning guide](https://github.com/FS-GG/.github/blob/main/docs/consumer/versioning-and-updates.md) owns compatibility, feeds, pins, and update policy.

## License

MIT.
