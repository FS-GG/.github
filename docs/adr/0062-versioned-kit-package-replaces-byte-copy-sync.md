# ADR-0062: A versioned kit package replaces the byte-copy sync fan-out

- **Status:** Accepted
- **Date:** 2026-07-20
- **Affects:** FS-GG/.github (owns the coordination kit, the `dist/dotnet` build config, `registry/repos.yml`'s `kit:` rows, and the `coordination-propagate` / `build-config-propagate` / `-selftest` workflow family); all kit receivers (the 8 framework repos) and build-config receivers (4) that today absorb sync PRs.
- **Interacts with:** [ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) (the governing principle — a byte-copy is a hand-maintained restatement of the hub's bytes in N repos); [ADR-0019](0019-org-repo-roster-registry-and-coordination-kit.md) (the coordination kit + roster this changes the *delivery* of); [ADR-0036](0036-the-build-config-drift-check-pins-its-source.md) (which already softened the build-config half — "behind" is green-with-notice, not a merge-freeze); [ADR-0034](0034-typed-coordination-engine.md) (the `fsgg-coord` client already ships as a `dotnet tool` shim — the precedent this generalizes to the rest of the kit).
- **Decision-first ADR:** records the approach and a recommendation. The **implementation** — item [.github#1262](https://github.com/FS-GG/.github/issues/1262) — is left open and unblocked; it proceeds once an option here is accepted. This ADR builds nothing.
- **Source:** [docs/reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md](../reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md) §3D, §7 P4.

## Context

Shared skills, the `fsgg-coord` client, and the `dist/dotnet` build config are distributed to
receivers by **byte-identical file copy**, content-addressed by digest. So *any* change to a managed
file — including a whitespace or comment edit — is a new digest that deterministically opens **one sync
PR in each of 7 (kit) or 4 (build-config) receivers.** The report measured **80–116 kit-sync commits
per repo over ten days**; two repos' *entire* representative 24h (FS.GG.Audio, FS.GG.Templates) was
absorbing one such wave and nothing else.

The hub is a single write point whose every commit **multiplies into N downstream commits.** ADR-0036
already removed the worst half of the pain — a receiver that is *behind* is now green-with-notice
rather than merge-frozen — but the **fan-out multiplication remains**: the mechanism is still N bespoke
sync PRs plus a hand-maintained `paths:` filter, per hub change.

This is ADR-0058's *derive, don't restate* in its most literal form: the hub's bytes are **restated**,
verbatim, in N repositories, and a gate (`*-propagate`, `-selftest`) is built to detect the drift the
copies make possible. The one part of the kit that already escaped this — the `fsgg-coord` client — did
so by becoming a **versioned `dotnet tool`** (ADR-0034 §4.4): receivers reference it, they do not carry
a copy of it. That is the shape the rest of the kit has not yet taken.

## Decision

**Decision — Option A: ship the kit and build config as one versioned artifact on the org feed.**

Package the coordination kit (shared skills) and the `dist/dotnet` build config as a versioned artifact
— call it `FS.GG.Kit` — published to the org feed like any other FS-GG package. Each repo **references**
it at a pinned version rather than carrying byte-copies of its files. A hub change is then **one
publish**; receivers pick it up through the **auto-update fabric that already exists** — Renovate opens
the bump PR, the producer-release dispatch fans the notification out — the *same* machinery every other
shared artifact already uses, instead of N bespoke sync PRs and a hand-maintained `paths:` filter.

Consequences of adopting it:

- The `coordination-propagate` / `build-config-propagate` / `-selftest` workflow family is **deleted** —
  there is no byte-copy to propagate or self-test.
- A whitespace edit to a kit file produces **one** commit (the publish) and N *Renovate* bumps that are
  ordinary dependency updates, not bespoke sync PRs — and a receiver may batch or schedule them like any
  other dependency, which the byte-copy fan-out cannot.
- The `fsgg-coord` shim precedent (ADR-0034) is generalized: the kit joins the client in being
  *referenced, not copied*.

**The one thing that must survive the change:** materialization. Some kit files (agent-skill roots) are
not just referenced — they must be **present on disk** in the receiver for the agent harness to load
them (ADR-0011). A package reference is not a materialized file. So Option A's implementation must keep a
**materialize step** (the receiver's build or a restore hook lays the packaged skills into the skill
roots) with the existing content-addressed *verify* (ADR-0014) — what changes is that the bytes come
from a pinned package restore, not a cross-repo file-copy PR. This is the load-bearing implementation
detail and the acceptance bar; getting it wrong would trade a loud sync PR for a silently missing skill.

## Consequences

- **A hub change stops multiplying into N bespoke PRs.** It becomes one publish + N ordinary Renovate
  bumps, which receivers control the cadence of.
- **The `*-propagate` / `-selftest` workflow family is retired**, and the hand-maintained `paths:`
  filter with it.
- **Materialization is preserved** via a restore/materialize step + the ADR-0014 verify; the skill roots
  are still real files on disk, sourced from a pinned package rather than a copy PR.
- **The kit gains a version.** "Which kit is this repo on?" becomes a pin, answerable like any dependency
  — and a receiver deliberately held back is a pin decision, not an invisible drift.
- **This ADR changes nothing until #1262 is worked.** It records the target; the migration is the item,
  and it is the largest of the P-series (a new package, a materialize step, and the deletion of a
  workflow family).

## Alternatives considered

- **Option B — keep byte-copy, cut the churn by batching.** Debounce kit changes into scheduled waves,
  or exclude whitespace-only digests, so fewer sync PRs open. This *reduces* the multiplication but does
  not remove it — the copies still exist, the `paths:` filter and `*-propagate` gates still exist, and a
  real change still fans out N ways. It is a mitigation of the symptom, not the *derive, don't restate*
  fix. Viable as a stopgap; not the target.
- **Option C — git submodule / subtree for the kit.** Puts one source of truth in each receiver by
  reference — but submodules are a notoriously sharp tool (detached HEADs, two-step updates, partial
  clones), and a subtree is a byte-copy with extra history. Neither integrates with the Renovate +
  dispatch fabric the org already runs for packages, so it would be a *second* update mechanism beside
  the one every other artifact uses. Rejected in favour of reusing the package fabric.
- **Status quo.** The measured 80–116 sync commits per repo per ten days is the reason this item exists.
  Rejected.
- **Fold into P1/P2.** Different mechanism (distribution vs registry derivation) and by far the largest
  implementation. Kept separate so it is sized and sequenced on its own.
