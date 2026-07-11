# ADR-0030: Creation-time scaffolding self-updates the CLI by default — a bounded carve-out to ADR-0009

- **Status:** Accepted
- **Date:** 2026-07-11
- **Affects:** .github (the `new-sdd-workspace` scaffolder + ADR owner), FS.GG.SDD (the `fsgg-sdd` CLI it self-updates)

## Context

[ADR-0009](0009-cli-single-orchestrator-detect-and-remediate.md) settled a deliberate policy: the
`fsgg-sdd` CLI is the single orchestration and enforcement surface, but it **never silently
self-updates or silently rewrites consumer artifacts**. Every command *detects* drift read-only and
*remediates* only through an explicit, diff-reviewed `fsgg-sdd upgrade`. That policy rests on three
invariants — **reproducibility/determinism** (the same command must produce the same output on
different days), **declarative truth** (the coherent set lives in the registry / provider
descriptor, diffable and gate-able in a PR — not inside a self-mutating executable), and
**consumer ownership** (a scaffolded project is the developer's own source; nothing may clobber it
behind their back).

The `new-sdd-workspace` scaffolder ([ADR-0016](0016-retire-templates-local-new-fullstack-single-scaffolder.md),
[ADR-0020](0020-platform-workspace-component-vocabulary.md) vocabulary) is a **creation-time**
tool, not an in-project command. It already fetches the newest provider descriptor (`--ref main`)
by default, so the *declarative* input is current. But the scaffold is actually produced by the
**installed `fsgg-sdd` binary**, which the consumer may never have updated. An old CLI on the
newest pin silently seeds the *old* artifact surface (the `fs-gg-sdd-*` process skills,
`.fsgg/early-stage-guidance.md`) and can misread a newer descriptor — the exact staleness ADR-0008
named and ADR-0009 chose to *detect-and-warn* rather than *fix*. The upshot: `new-sdd-workspace`
produced workspaces that were only as current as whatever CLI happened to be on the machine, and
the only remedy was to remember to `dotnet tool update` first, or pass the post-scaffold
`--upgrade` to reconcile the freshly-made project.

Read literally, ADR-0009 forbids the obvious fix (self-update the CLI first). But its three
invariants were written about **in-project** invocations against an **existing** consumer. At
**creation** time they don't bind the same way:

- **Reproducibility.** There is no prior run to reproduce — the developer is asking the tool to
  *create* something new, and "give me the newest coherent set" is the overwhelmingly common
  intent. Reproducibility is still *available on demand*: `--ref <tag>` pins the declarative input,
  and (below) `--pinned` pins the tool.
- **Declarative truth.** Self-updating the CLI does not move truth into the executable. The
  registry and provider descriptor remain the source of truth; the CLI is still only their
  *consulter*. Updating the consulter to the newest published build changes nothing about where
  truth lives or how it is gated.
- **Consumer ownership.** At creation there is no consumer artifact yet — nothing to clobber. The
  self-update touches the *tool* (a global dotnet tool the developer installed to run this), never
  a project's owned `.fsgg/` state.

So the fix ADR-0009 forbade for in-project commands is not just safe but *correct* for the
create-a-new-workspace step.

## Decision

`new-sdd-workspace` **self-updates the `fsgg-sdd` CLI to the newest published build BEFORE it
scaffolds, by default.** Concretely:

1. **A new step 2 — "update fsgg-sdd" — runs before `fsgg-sdd scaffold`.** It runs
   `dotnet tool update --global FS.GG.SDD.Cli` from an isolated `nuget.config` pointed at the org
   GitHub Packages feed. The scaffold (step 3) then executes with the newest tooling, so a fresh
   workspace is always produced by the current coherent set.

2. **Best-effort and non-blocking.** `FS.GG.SDD.Cli` is dual-published (ADR-0012): anonymously on
   nuget.org **and** — possibly a newer build — on the org GitHub Packages feed, whose reads are all
   authenticated (FS.GG.Templates#82). The update reuses the governance overlay's feed ladder: with
   a `read:packages` token (`FSGG_PACKAGES_TOKEN` / `GH_TOKEN` / `GITHUB_TOKEN`) it tries the org
   feed first and falls back to nuget.org; **with no token it updates from nuget.org anonymously**,
   so the common tokenless run still gets current tooling. On an offline/failed update the step
   **warns and scaffolds with the installed CLI** — creation is never blocked on a feed hiccup. The
   preflight already requires `fsgg-sdd` on PATH; this only makes it *newer*.

3. **An explicit opt-out preserves reproducibility.** `--pinned` skips the self-update and
   scaffolds with the installed CLI. `--pinned --ref <tag>` is the fully reproducible, pinned
   invocation — the determinism ADR-0009 protects, now an explicit *choice* rather than the
   accidental default.

4. **Scope is exactly the creation step.** This carve-out applies *only* to `new-sdd-workspace`
   self-updating the tool it uses to create a new workspace. **ADR-0009 stands unchanged for every
   in-project `fsgg-sdd` verb**: `scaffold`/`doctor`/`refresh`/etc. against an existing project
   still detect-and-warn (interactive) or fail closed (CI), and still never silently self-update or
   rewrite consumer-owned artifacts. The post-scaffold `--upgrade` remains available for the
   reconcile-an-existing-project case (now largely redundant on a fresh scaffold).

One line: **at creation, currency is the default and reproducibility is opt-in (`--pinned`); in an
existing project, ADR-0009's detect-and-remediate is unchanged.**

## Consequences

- **`.github`** (this repo): `scripts/NewSddWorkspace` gains the step-2 self-update, a `--pinned`
  flag (CLI + interactive wizard "currency" prompt + preview), and updated help/README. The
  orchestration renumbers to fetch → update → scaffold → governance → doctor → (upgrade).
- **FS.GG.SDD** (CLI producer): no code change — the CLI is *consumed* newer, not modified. Its
  release cadence and the `minimum-fsgg-sdd` coherent-set axis (ADR-0008) are unchanged; the
  self-update simply lands on or ahead of that minimum.
- **Consumers**: `new-sdd-workspace <dir> <name>` now yields a workspace built by current tooling
  with no "did I update my CLI?" footgun. Reproducible/air-gapped/pinned builds pass `--pinned`
  (with `--ref <tag>`), and CI callers that must not mutate the runner's global tool should pass
  `--pinned` too.
- **Refines [ADR-0009](0009-cli-single-orchestrator-detect-and-remediate.md)**, does not reverse it:
  it carves a bounded creation-time exception and leaves the in-project policy — and all three of
  ADR-0009's invariants for existing consumers — intact. `docs/architecture.md` §4.2/§5 note the
  carve-out.
- **Trade-off accepted:** the default `new-sdd-workspace` run is no longer byte-reproducible across
  days (the newest CLI floats). This is the intended behaviour for *creation*; anyone who needs a
  frozen scaffold says so explicitly with `--pinned`.
