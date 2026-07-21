# ADR-0063: The scaffold materializer sources a skill from its owner repo, not a frozen provider template

- **Status:** Accepted
- **Date:** 2026-07-21
- **Affects:** **FS.GG.SDD** (owns the scaffold materializer — this is the change: `src/FS.GG.SDD.Cli/RegistrySkillManifest.fs` reads only the frozen *provider* manifest today); **.github** (`registry/skills.yml`'s `owner`/`source`/`materializes-when` fields already declare where every skill's bytes live — this makes that declaration the *delivery* authority; owns the `scope: driver` rows); **FS.GG.Game** (owner of the game `product-skills/` — becomes a delivery *source*, so no game skill need be frozen into a donor); **FS.GG.Rendering** (its frozen `--profile game` `template/product-skills/` copies are retired); **FS.GG.Templates** (the deferred consumer `game` provider is no longer composed).
- **Amends:** [ADR-0022](0022-extract-fs-gg-game-as-an-sdd-driven-component.md) §Decision 4 — the deferred *"consumer `game` scaffold provider"* sequel epic is **cancelled, not executed.** ADR-0022 §4 froze `dotnet new fs-gg-ui --profile game` and tracked *"two game-starter copies during the freeze … retired by the sequel provider epic."* Those copies are retired **here**, by the materializer sourcing FS.GG.Game's `product-skills/` directly — no second provider is stood up. §4's rejection of *live re-sourcing from within the rendering provider* stands (see Alternatives). The rest of ADR-0022 (the component cut, the majors, the ownership migration) is untouched.
- **Interacts with:** [ADR-0054](0054-workroadmap-delivery-fabric-a-github-authored-product-materialized-driver.md) — the `scope: driver` `workRoadmap` gap ([FS.GG.SDD#620](https://github.com/FS-GG/FS.GG.SDD/issues/620)) is the **same defect** as this one (an `owner: .github` skill the materializer cannot reach), and closes under this decision. [ADR-0017](0017-skill-registry-condition-aware-materialization.md) — the `owner`/`source`/`materializes-when` catalog fields this makes load-bearing for delivery. [ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) — a frozen donor copy is a *restatement* of the owner's bytes; sourcing from the owner **derives** delivery from the registry instead. [ADR-0062](0062-versioned-kit-package-replaces-byte-copy-sync.md) — the same "bytes come from a pinned package restore, not a cross-repo copy" shape; the two decisions likely share a delivery substrate (see Consequences). [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) — the content-addressed materialize-and-verify is **preserved**; only the *byte source* changes.
- **Decision-first ADR:** records the approach and cancels a deferred epic. The **implementation** — teaching the materializer to source owner-repo bytes, and retiring Rendering's frozen game copies — is a Coordination epic, filed and sequenced separately (ADR-0001). This ADR builds nothing.
- **Resolves:** [.github#1299](https://github.com/FS-GG/.github/issues/1299) (the game-skill instance).

## Context

`registry/skills.yml` gives every skill an `owner`, a `source:` path in that owner's tree, and a
`materializes-when:` predicate. A scaffold is expected to carry every skill whose predicate holds.
Two skills satisfy their predicate, are gated *into* their profile, and reach **zero** scaffolds:

- **`fs-gg-playtest`** — `owner: fs-gg-game`, `product`, `mirrored: false`,
  `materializes-when: "profile in [game, sample-pack]"`. Registered ([.github#1194](https://github.com/FS-GG/.github/issues/1194)),
  authored (FS.GG.Game#421 / WI-2), and added to the game-profile skill *union*
  (FS.GG.Rendering#923 / WI-6). Confirmed **absent** from `Rougue1` (`profile=game`), which is
  byte-current on every *other* game skill.
- **`workRoadmap`** — `owner: .github`, `scope: driver`, `materializes-when: "always"` on the
  `sdd` lane (ADR-0054, ADR-0056). Materializes into **zero** trees (FS.GG.SDD#620).

These are not two bugs. They are **one**: the materializer
(`src/FS.GG.SDD.Cli/RegistrySkillManifest.fs`) sources a skill's bytes **only** from the frozen
*provider template* it restores (`FS.GG.UI.Template`, cut from FS.GG.Rendering's
`template/product-skills/`). A skill whose bytes live in an owner repo that is *not* that provider —
`FS.GG.Game` for a game-owned `mirrored: false` skill, `.github` for a driver skill — has **no
channel** to a scaffold. This is the *"declared ∧ gated-in ∧ supplied-from-nowhere"* class: the
`owner`/`source` fields name where the bytes are, and nothing reads them for delivery.

The frozen-donor route cannot close it. FS.GG.Rendering deliberately does **not** carry
`fs-gg-playtest` — `registry/skills.yml` **refuses** `mirrored: true` for that row (the owner's
manifest declares no obligation; forging one is the FS.GG.Rendering#505 trap), and
`FrozenMirrorVerdict.fs` correctly records it (with `ai`/`ballistics`/`effects`/`physics`) as
`NoCounterpart`. A driver skill has no donor at all. So the gap is structural, not a missed copy.

ADR-0022 §4 already anticipated the game half: it froze Rendering's `--profile game` copies as a
**temporary bridge**, to be *"retired by the sequel provider epic."* That epic — a consumer `game`
provider sourcing FS.GG.Game — was never built. `fs-gg-playtest` is the **first casualty of the
deferral**: a *new* game-owned skill that never existed in the frozen set, so the bridge cannot carry
it and the fabric that would has not shipped.

## Decision

**The scaffold materializer sources each skill's bytes from the `owner`/`source` its
`registry/skills.yml` row already declares — not solely from a frozen provider template.**

For a row whose bytes do not live in the restored provider template — a `mirrored: false` product row
(`fs-gg-playtest`) or a `scope: driver` / `owner: .github` row (`workRoadmap`) — the materializer
reads the declared `source:` path from the **owner** repo's delivery and lays it into the scaffold
wherever `materializes-when` holds. The frozen provider template remains the source for skills that
*are* mirrored into it; nothing about the mirrored path changes.

This is **Route C** of [.github#1299](https://github.com/FS-GG/.github/issues/1299), chosen over
Route A (compose a bespoke `game` provider) and Route B (live re-source inside the rendering
provider). It **subsumes ADR-0022's deferred game provider** — game skills reach a game scaffold
without a second provider — and it **closes FS.GG.SDD#620** by the same mechanism, since a driver
row is just another owner-sourced row.

The registry becomes the single authority for *what is delivered and from where*: a new owner-repo
skill needs its `skills.yml` row and nothing else — no new provider, no frozen copy. That is
ADR-0058 applied to delivery: the frozen copy was a hand-maintained restatement of the owner's bytes;
this derives delivery from the row.

## Consequences

- **FS.GG.Rendering's frozen `--profile game` `template/product-skills/` copies are retired** — the
  "two game-starter copies during the freeze" ADR-0022 tracked as a `coherence:` row are collapsed
  here, by owner-sourcing, rather than by the provider epic ADR-0022 named.
- **No `game` provider is composed.** ADR-0022's deferred sequel epic is cancelled. FS.GG.Templates
  gains no `game.providers.yml`.
- **The load-bearing detail — and the acceptance bar — is *how* the materializer obtains owner-repo
  bytes at scaffold time.** The CLI does not have every owner repo checked out. It must reach
  `FS.GG.Game`'s and `.github`'s skill bytes through a *pinned, content-addressed* delivery, not a
  live `main` read — otherwise a scaffold's contents stop being reproducible from its provenance. This
  is where this decision meets **ADR-0062**: owner-repo skill bytes are a natural payload for the same
  versioned-package restore (`FS.GG.Kit`-shaped) that ADR-0062 chose for the coordination kit. The
  implementation epic should resolve the two together rather than invent a second delivery substrate.
  Getting this wrong trades a loud missing skill for a *silently stale or unreproducible* one.
- **The content-addressed materialize-and-verify (ADR-0014) is preserved** — only the byte *source*
  changes. `scaffold-provenance.json` gains the owner-sourced artifacts it omits today; the verify
  gate keeps a scaffold honest against the pinned bytes.
- **`FrozenMirrorVerdict.fs`'s `NoCounterpart` entries stay correct and stay.** They assert
  *"no rendering mirror exists"*, which remains true and is now simply orthogonal to delivery —
  delivery no longer depends on a mirror existing. The FS.GG.Rendering#505 / `mirrored: true` refusal
  is vindicated, not worked around.
- **Existing scaffolds need a backfill.** `Rougue1` and any shipped `sdd`-lane tree are missing the
  owner-sourced skills; the epic must say whether `fsgg-sdd upgrade` / re-vendor backfills them
  (FS.GG.SDD#620 asks the same question — answer it once).

## Alternatives considered

- **Route A — compose a consumer `game` scaffold provider** (ADR-0022 §4's sanctioned sequel).
  Sources FS.GG.Game's `product-skills/` for `profile in [game, sample-pack]` and retires the frozen
  copies. It *would* close `fs-gg-playtest`. Rejected as the primary because it **point-fixes one
  shadow**: it needs a bespoke provider *per owner repo*, and the driver case (`owner: .github`) has
  no sensible "provider" to be — leaving FS.GG.SDD#620 to a second, differently-shaped fix. Route C
  is A's intent generalized: the materializer sources the owner directly, so *every* owner-repo skill
  is covered by one mechanism, game and driver alike.
- **Route B — re-source the game profile live from FS.GG.Game inside the rendering provider.**
  **Explicitly rejected by ADR-0022 §4** (*"Re-sourcing Rendering's profile live from `FS.GG.Game` is
  explicitly rejected (≈half the deferred provider work)"*), and independently unsound: a live read of
  another repo's `main` at scaffold time makes a scaffold irreproducible from its provenance. Route C
  keeps the pinned, content-addressed contract (ADR-0014) that B abandons.
- **Do nothing / keep the freeze and accept the gap.** Record `fs-gg-playtest` as gated-in but
  undeliverable until some later epic. Rejected: the gap is already shipping (`Rougue1`), a second
  instance (`workRoadmap`) is open, and the freeze was always a bridge ADR-0022 committed to retiring.
