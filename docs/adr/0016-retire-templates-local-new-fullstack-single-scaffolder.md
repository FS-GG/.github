# ADR-0016: Retire the Templates-local `new-fullstack.sh`; `new-sdd-fullstack.sh` is the sole full-stack scaffolder

- **Status:** Accepted
- **Date:** 2026-07-03
- **Affects:** FS.GG.Templates, .github

## Context

Two scripts scaffolded a full-stack FS.GG product, and they diverged:

- `FS.GG.Templates/scripts/new-fullstack.sh` — the original composition wrapper. Requires a
  **Templates checkout**: it reads the repo-local `providers/rendering.providers.yml` and
  carries a `--source <path-or-nuget-id>` override that rewrites the descriptor's `source:`
  line in place for the local/unpublished **dev-repack** flow.
- `.github/scripts/new-sdd-fullstack.sh` — the clone-free successor chosen when
  [ADR-0010 was withdrawn](README.md) in favour of "the clone-free `scripts/new-sdd-fullstack.sh`
  working through existing machinery" ([#100](https://github.com/FS-GG/.github/pull/100)). It
  fetches the provider descriptor over the network, needs no checkout, and drives the same
  published `fsgg-sdd` machinery ([ADR-0009](0009-cli-single-orchestrator-detect-and-remediate.md)).

Keeping both invited the mistake ADR-0010's withdrawal was meant to end: a consumer picking the
checkout-bound path when the checkout-free one is canonical. The
[2026-07-02 Templates review](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/reports/2026-07-02-code-quality-and-architecture-review.md)
already flagged `new-fullstack.sh`'s dead `<rendering-source>` argument (F1) as evidence the
older wrapper had drifted.

The one thing `new-fullstack.sh` still *uniquely* did was serve as the executor for the
Templates composition test (`tests/composition/stages/05-build.sh`), which needs a **hermetic,
LOCAL-`providers.yml`** scaffold — something the network-fetching `new-sdd-fullstack.sh` cannot
provide. So the wrapper could not simply be deleted; the test had to absorb its steps.

## Decision

1. **One scaffolder.** `.github/scripts/new-sdd-fullstack.sh` is the sole full-stack scaffolder.
   `FS.GG.Templates/scripts/new-fullstack.sh` is **retired** (deleted), along with its dev-only
   `--source` in-place descriptor override.

2. **The composition test owns its steps.** `tests/composition/stages/05-build.sh` carries the
   ADR-0002 composition-by-scaffold steps inline (register the provider-pinned descriptor →
   `fsgg-sdd scaffold` → governance overlay *after*), in a `set -e` subshell that preserves the
   old script's fail-fast. This keeps the hermetic, local-`providers.yml` composition coverage the
   test exists for, with no external wrapper.

3. **No versioned contract moves.** The `scaffold-provider` descriptor, the `fs-gg-ui-template`
   pin, and the governance overlay are unchanged. This is a tooling/consolidation change, **not** a
   `contract-change` — the registry is not touched.

## Consequences

- **FS.GG.Templates** deletes `scripts/new-fullstack.sh`, inlines its steps into the composition
  test, and repoints its docs (`README.md`, `docs/design.md`) at `new-sdd-fullstack.sh`. The
  dev-repack flow keeps its own script (`scripts/dev-repack-ui-feed.sh`); only the `--source`
  *scaffold* override retires with the wrapper (the composition test never passed it).
- **.github** repoints consumer/architecture docs (`docs/architecture.md`,
  `docs/consumer/which-products.md`) at `new-sdd-fullstack.sh` — which also removes the dead
  `<rendering-source>` third argument the older invocation advertised (review F1) — and drops the
  now-dangling back-reference from `new-sdd-fullstack.sh`'s own header. `docs/architecture.md` is
  reconciled as part of this decision (per the template footer).

## Update (2026-07-04) — reimplemented as the `FS.GG.NewSddFullstack` dotnet tool

The single-scaffolder decision stands; only its *implementation* changed. `scripts/new-sdd-fullstack.sh`
was rewritten as an F# / Spectre.Console console app (`scripts/NewSddFullstack`, package
`FS.GG.NewSddFullstack`, command `new-sdd-fullstack`) that shows a rich per-step progress UI and a
"what worked / what didn't" summary, and adds the `fsgg-sdd` preflight + captured `dotnet new install`
diagnostics the shell version lacked (flagged in the 2026-07-02 review). It is **packed as a dotnet
tool and published to the org GitHub Packages feed**, so the clone-free install ADR-0010's withdrawal
relied on is preserved: `dotnet tool install --global FS.GG.NewSddFullstack`, then the same
`new-sdd-fullstack <target> <product>` command. This makes `.github` a package producer with its own
`release-new-sdd-fullstack.yml` — the one exception to "product repos own release workflows," since the
tool lives here. The tool has no consumers and no coherence edges, so it is **not** a registry contract
(`dependencies.yml` unchanged).
- The dated `2026-07-02` Templates review is left as a historical record; its F1 finding is
  resolved by this deletion.
- Tracked on the Coordination board via a `cross-repo` issue in FS.GG.Templates (the target repo
  for the deletion + test rewire); no registry PR accompanies it.

## Update (2026-07-04b) — renamed `new-sdd-fullstack` → `new-sdd-workspace`

The single-scaffolder decision still stands; only the tool's **name** changed. "Fullstack" was
adopted when the tool produced one app shape, but the interactive-wizard work added a `--profile`
axis (`game` / `app` / `headless-scene` / `governed` / `sample-pack`) and governance is opt-out
(`--no-governance`) — so the tool no longer produces a single "full-stack" thing. It scaffolds a
**workspace** — precisely the term [ADR-0020](0020-platform-workspace-component-vocabulary.md)
reserves for "what you scaffold with the platform" (a generated repo carrying an app, the `.fsgg/`
lifecycle, agent skills, and optional governance). The name now matches the vocabulary.

- **Renamed:** command `new-sdd-fullstack` → **`new-sdd-workspace`**; package
  `FS.GG.NewSddFullstack` → **`FS.GG.NewSddWorkspace`**; project dir `scripts/NewSddFullstack` →
  `scripts/NewSddWorkspace`; workflow `release-new-sdd-fullstack.yml` →
  `release-new-sdd-workspace.yml` (tag trigger `new-sdd-workspace/v*`).
- **Package identity is immutable** (a NuGet id cannot be renamed in place). This is a **new**
  package id, not a move: `FS.GG.NewSddWorkspace` starts at `0.3.0-preview.1` (version continuity
  preserved — same tool, same maturity). The old id's published preview builds
  (`FS.GG.NewSddFullstack` ≤ `0.1.1-preview.1`, tags `new-sdd-fullstack/v0.1.0-preview.1`,
  `…/v0.1.1-preview.1`) are **superseded** and should be **delisted/deprecated** on the org
  GitHub Packages feed (org-owner action). Harm is nil: the tool is org-internal, preview-only,
  has no consumers and no coherence edges, so — as recorded above — it remains **not** a registry
  contract (`dependencies.yml` unchanged).
- **Publish is a separate, deliberate step:** no `new-sdd-workspace/v*` tag is cut by this rename.
  The first release under the new id happens when an owner pushes that tag (or runs the workflow
  with `publish=true`).
- **Cross-repo sweep:** any sibling-repo docs that still say `new-sdd-fullstack` (e.g.
  FS.GG.Templates README/design, repointed here per the 2026-07-03 consequences) need the same
  rename — filed as a `cross-repo` heads-up: [FS.GG.Templates#95](https://github.com/FS-GG/FS.GG.Templates/issues/95).
