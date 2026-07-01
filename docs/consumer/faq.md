---
title: FS-GG consumer FAQ & troubleshooting
category: FS.GG
categoryindex: 6
index: 18
description: Common questions and failure modes for people building products with FS-GG.
---

# FAQ & troubleshooting

## Do I have to use all four products?

No. Each ships and runs on its own. Adopt only what your goal needs — see
[Which products do I need?](which-products.md). The common case is Rendering plus
the SDD lifecycle, with governance added later (or never).

## Do I have to use governance?

No. SDD builds, installs, and runs the full lifecycle through `ship` with no
governance present, and rendering never depends on it. Governance only *inspects*
your artifacts; it never becomes a build dependency. If you adopt it and later
find it heavy, remove the `.fsgg` governance files and keep building. See
[Adopting governance](governance.md).

## Why isn't there a single "full-stack" `dotnet new` template?

Because `dotnet new` templates cannot depend on or include another template. A
one-shot full-stack template could only exist by **vendoring** a copy of the
rendering payload — which goes stale the moment Rendering changes. That staleness
is exactly what broke the old monolith. FS-GG composes at scaffold time instead:
`fsgg-sdd scaffold` installs the live, version-pinned rendering template, so
there's no fork to drift. See [Which products do I need?](which-products.md#the-full-stack-path).

## `fsgg-sdd scaffold --provider rendering` says the provider isn't found

The provider is resolved from your project's `.fsgg/providers.yml`, and SDD
embeds no provider itself. Register the reference `rendering` provider first by
copying the descriptor from
[FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates) into
`./MyApp/.fsgg/providers.yml`, then re-run scaffold. For the skeleton with no
runtime template, use `fsgg-sdd init`.

## `scaffold` exited non-zero — what do the codes mean?

`fsgg-sdd scaffold` separates causes: exit `1` is malformed user input (unknown
provider, unsupported contract version, missing required parameter, target
collision); exit `2` is a provider defect (provider failed, engine unavailable,
provider wrote into SDD-owned trees). An incomplete scaffold is never reported as
complete, so a non-zero code means nothing half-built was passed off as done.

## `dotnet run` builds but no window opens

The live windowed viewer needs a GL/X11 session. The renderer's offscreen and
deterministic test paths run headless, but the interactive viewer needs a display
(for example `DISPLAY=:1` on Linux). See
[Rendering · usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md).

## My terminal output has no colors / looks plain

That's by design. The `--rich` projection degrades to plain text with zero ANSI
when output is non-interactive or redirected, or when color is disabled
(`NO_COLOR`, `TERM=dumb`). For a rich rendering, run at an interactive terminal
with color enabled; for stable machine output, use the default/`--json`
projection. See [Output, automation & CI](automation.md).

## How do I script against command output?

Parse the **JSON** projection (the default), not the human text. JSON is the
contract held stable by the schema; plain and rich are projections whose wording
can change. `fsgg-sdd ship --json | jq -e '.ready == true'` is the right shape.
See [Output, automation & CI](automation.md).

## How do I keep everything up to date?

`dotnet tool update --global FS.GG.SDD.Cli` for the CLIs, `dotnet new update` for
template packages, and your normal dependency tooling for `FS.GG.UI.*` package
references. Pin versions in anything you ship and read each product's versioning
policy before a bump. See [Versions, feeds & updates](versioning-and-updates.md).

## Where's the authoritative doc for X?

The consumer guide is the map; each product owns the details:

- Rendering / packages → [usage](https://github.com/FS-GG/FS.GG.Rendering/blob/main/docs/usage.md)
- Lifecycle / `init` → `ship` → [quickstart](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/quickstart.md)
- Install / versions → [installation](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/release/installation.md)
- Governance → [adopting governance](https://github.com/FS-GG/FS.GG.SDD/blob/main/docs/adopting-governance.md) · [design](https://github.com/FS-GG/FS.GG.Governance/blob/main/docs/governance-design/index.md)
- Composition → [FS.GG.Templates](https://github.com/FS-GG/FS.GG.Templates)

## I want to contribute to FS-GG itself, not just use it

This guide is for consumers. For contributing to a product, read that repo's
`DEVELOPING.md` / `CONTRIBUTING` and the cross-repo
[decision record](../index.md). Cross-repo coordination is issue-based and tracked
on the org Coordination board, described in the
[top-level `.github` README](https://github.com/FS-GG/.github).
