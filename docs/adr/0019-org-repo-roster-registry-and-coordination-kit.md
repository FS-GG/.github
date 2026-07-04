# ADR-0019: Org repo roster registry + coordination-kit distribution

- **Status:** Proposed
- **Date:** 2026-07-04
- **Affects:** FS.GG.SDD, FS.GG.Rendering, FS.GG.Governance, FS.GG.Templates, .github

## Context

The FS-GG org fabrics — the shared labels, `sync-build-config`, `lockfile-sync`, the
`contract-coherence` gate, the `dispatch-sender`, `skill-union-assert`, and the
`NewSddFullstack` scaffolder — all iterate the *same* set of framework repos. That set was
**redeclared by hand in ~10 places** (`scripts/apply-labels.sh`, the workflows above, the
scaffolder sources). Adding or retiring a repo meant editing all of them, and they drifted.

Separately, the [`cross-repo-coordination`](../../.claude/skills/cross-repo-coordination/SKILL.md)
skill grew a helper — [`scripts/fsgg-coord`](../../scripts/fsgg-coord), a GraphQL-budget-thrifty
client for the Coordination board (see [graphql-budget.md](../coordination/graphql-budget.md)). The
skill and its client are needed **wherever coordination happens** — the framework repos + `.github`
— but they lived only in `.github`, with no distribution path to the other participants. The
[product skill registry](0017-skill-registry-condition-aware-materialization.md) (`skills.yml` +
`materializes-when` + the mirror) solves the analogous problem for **scaffolded products**, but its
target is generated apps, not the framework repos, so it is the wrong fabric for this audience.

Both problems are the same missing primitive: **an authoritative, governed list of the framework
repos and what each participates in**, plus a distribution/coherence fabric over it — the
participant-side analog of what `skills.yml` is for skills.

## Decision

1. **`registry/repos.yml` is the single authoritative roster** of framework repos — a sibling of
   `registry/dependencies.yml` and `registry/skills.yml`, with the same governance (`schemaVersion`,
   `updated:`, a changelog `registry/repos.CHANGELOG.md`, a `docs/registry/repos.md` projection).
   Each repo carries a `receives:` capability list from a controlled vocabulary (`labels`,
   `coordination-kit`, `build-config`, `lockfile-sync`, `contract-coherence`). `receives` is to a
   repo what `materializes-when` is to a skill: it gates PARTICIPATION per fabric.
2. **`.github` is the authority/producer.** It holds the canonical fabrics and the coordination kit
   and mirrors them out — the analog of `fsgg-sdd` for product skills. The authority repo is the
   SOURCE of the coordination kit, never a receiver of it.
3. **Every fabric reads the roster** instead of hardcoding the list, via `scripts/repos.sh list
   --receives <cap>`. Migration is incremental (one fabric per PR); `apply-labels.sh` is migrated
   first (this ADR's slice 1).
4. **The roster is validated in CI** by `scripts/repos.sh validate` (shell + jq; YAML via `yq` or
   `python3`+pyyaml) — deliberately self-contained in `.github`, not the SDD-owned typed validator,
   to avoid a circular dependency. It checks schema, the single-authority invariant, the `receives`
   vocabulary, and that the **content-addressed kit** (`sha256` of each kit source) matches the tree.
5. **The coordination kit** (the `cross-repo-coordination` skill + the `fsgg-coord` client) is
   declared in `repos.yml` and distributed to every `coordination-kit` receiver by a **pull** fabric
   (a reusable `.github` workflow each participant calls, matching `sync-build-config`/`skill-union`),
   with a `coordination-coherence` gate asserting each holds the current kit bytes. `fsgg-coord`
   stays a **self-contained script carried in the kit** — not an `fsgg-sdd` subcommand — so no
   participant needs `fsgg-sdd` installed to drive the board. *(Kit sync + coherence gate: slice 2.)*

## Consequences

- Adding/retiring a repo is **one `repos.yml` row + a changelog line + re-sync**, gated by the
  coherence check — never edits scattered across fabrics. The roster grows without code churn.
- The ~10 hardcoded lists collapse to one governed source. Migration is staged to keep blast radius
  small: `apply-labels.sh` now; `dispatch`/`lockfile-sync`/`contract-coherence`/`skill-union` in
  follow-up PRs that add the matching `receives` capability to the relevant rows.
- The coordination skill + client become present wherever the board is driven, distributed on the
  framework-repo fabric (not the scaffold mirror), which is the correct audience.
- New dependency surface is minimal: `repos.sh` needs `jq` + (`yq` or `python3`+pyyaml), both already
  present in CI and the repo's tooling ladder.
- Open question deferred to slice 2: whether `cross-repo-coordination` should additionally be added
  to `skills.yml` so scaffolded products that *do* file into the board receive it via the product
  mirror. This ADR scopes distribution to the framework repos only.
