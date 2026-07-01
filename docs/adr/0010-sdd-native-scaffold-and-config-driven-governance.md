# ADR-0010: SDD-native scaffold — inline provider-source, explicit currency, config-driven governance default

- **Status:** Accepted
- **Date:** 2026-07-01
- **Affects:** FS.GG.SDD (CLI producer + policy owner), FS.GG.Templates (provider-descriptor source), .github (registry/ADR owner)

## Context

Today `fsgg-sdd scaffold --provider rendering` requires the provider descriptor to
**already** be registered in `<root>/.fsgg/providers.yml` — SDD ships no providers
([ADR-0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md)).
Consumers get it either by a manual `curl` (the tutorial) or via FS.GG.Templates'
`scripts/new-fullstack.sh` composition script. Both hurt:

- The manual step is easy to skip, and skipping it yields a `scaffold.providerUnknown`
  block (nothing scaffolded) — a real papercut.
- `new-fullstack.sh` needs a **Templates repo checkout** (it reads sibling files) and
  **always bundles Governance**, so it's not a lightweight, governance-optional path.

The goal is a **native, one-command** scaffold that (a) registers the provider with no
manual step or repo clone, (b) can track the current coherent set and update when behind,
and (c) can include Governance **by default** — *without* reversing the invariants the
system rests on:

- **[ADR-0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md):** SDD embeds no rendering (or governance) identity; composition is by scaffold, not vendoring.
- **[ADR-0009](0009-cli-single-orchestrator-detect-and-remediate.md):** the CLI never silently auto-updates or rewrites consumer artifacts; remediation is explicit and diff-reviewed, to keep scaffolds reproducible.
- **The one-way rule:** the lifecycle never *requires* Governance to build, test, or ship.

## Decision

1. **Inline `--provider-source`.** Add `fsgg-sdd scaffold --provider-source <source>`,
   which **fetches + registers** the provider descriptor inline (writes
   `<root>/.fsgg/providers.yml`), removing the manual `curl` / Templates clone. SDD stays
   identity-free: the **source is supplied by the caller or by config** — a local path, a
   NuGet id, a `github>owner/repo@<ref>`, or a URL. A **moving ref** (e.g. `@main`) fetches
   the current coherent set; a **tag** pins it (reproducible). The resolved source + ref are
   recorded in `scaffold-provenance`.

2. **Currency is explicit (upholds ADR-0009).** "Update if necessary" is **not** a silent
   per-run online fetch. For a *new* scaffold, currency is the caller's source choice
   (moving ref vs. pinned tag). For an *existing* project, `fsgg-sdd doctor` checks the
   descriptor/pin against the published coherent set **read-only**, and `fsgg-sdd upgrade`
   refreshes it **behind a confirmed diff**. No command silently mutates a consumer's pinned
   descriptor — reproducibility is preserved.

3. **Config-driven defaults, including Governance.** A user/project-level config (e.g.
   `.fsgg/sdd.yml` and/or a user config) may set a **default provider source** and an
   **"apply governance overlay by default"** flag (with a governance source). This makes
   Governance **on-by-default for the configuring user/project** while keeping it
   architecturally optional and fully removable: SDD hardcodes no rendering or governance
   identity, and the one-way rule holds (the lifecycle still never *requires* Governance).
   An explicit `--no-governance` (or config off) opts out; the governance overlay is applied
   as a post-scaffold step (not via the provider), so it does not trip the SDD-owned `.fsgg/`
   write guard.

Reframed: **one command, tracks current, governance on by default — but "current" stays
explicit (a moving-ref source, or `doctor`/`upgrade`), and "governance default" stays
*your* config, not a hardcoded CLI dependency.**

## Consequences

- **FS.GG.SDD:** implement `--provider-source` inline fetch/register (source grammar: local
  path, NuGet id, `github>owner/repo@ref`, URL); extend `doctor`/`upgrade` to cover the
  provider descriptor's currency against the coherent set; add config keys for the default
  provider source, the governance-default flag, and a governance source; stamp the resolved
  source/ref into `scaffold-provenance`. All additive.
- **FS.GG.Templates:** remains the descriptor source of truth; `new-fullstack.sh` becomes a
  thin wrapper over the native path (or is retired). The `__RENDERING_TEMPLATE_SOURCE__`
  placeholder drift (the committed descriptor is concretely pinned, so a supplied source is
  currently ignored) should be fixed so a caller-supplied source takes effect.
- **.github:** no cross-repo contract-surface change — the `scaffold-provider` contract
  (`.fsgg/providers.yml` on disk) is unchanged; the CLI just writes it for you. Registry and
  coherence rows are unaffected. The consumer docs (`getting-started`, `TestSpecTutorial`)
  move to the native path once shipped.
- **Relationship to prior ADRs:** *preserves* ADR-0002 (sources supplied, no embedded
  identity), ADR-0009 (no silent auto-update; explicit `doctor`/`upgrade`), and the one-way
  rule (governance default is config-driven and removable, never required). It supersedes the
  manual-registration UX in the docs once implemented.
- **Trade-off accepted:** "stay current" is one explicit command (or a moving-ref source),
  not magic — the deliberate cost of keeping scaffolds reproducible and Governance optional.

<!-- If this decision changes the shape of the system (repos, boundaries, the
coherent-set axes, the contract picture), reconcile docs/architecture.md as part of
resolution — after the registry update. See docs/coordination/README.md#system-overview--the-architecture-map. -->
