# ADR-0014: Skill vendoring & mirroring — one manifest, one materialize-and-verify, content-addressed

- **Status:** Accepted
- **Date:** 2026-07-01
- **Affects:** FS.GG.SDD (orchestrator/CLI + `FS.GG.Contracts`), FS.GG.Rendering (`fs-gg-ui` template), FS.GG.Templates (composition gate), `.github` (this ADR, registry, roadmap)
- **Relationship:** **Extends and amends [ADR-0011](0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md).** ADR-0011's five *invariants* stand (byte-identical union in every root; single mirror authority; providers confined to `.agents/skills/`; strict `isSddTree`; materialized copies, not symlinks). ADR-0014 replaces its *implementation* — which fragmented into four hand-maintained mirror mechanisms and shipped **no content verification** — with one shared, content-addressed algorithm, and draws the missing product/dev-surface boundary. Where the two disagree on mechanism, ADR-0014 wins.
- **Extended by:** [ADR-0017](0017-skill-registry-condition-aware-materialization.md) — §Decision 1's manifest entry `{ id, scope, sha256 }` is **no longer the whole schema**: ADR-0017 adds `materializes-when` + `supplied-by`, and the org catalog it introduces ([`registry/skills.yml`](../../registry/skills.yml)) carries `owner`, `source` and `mirrored` on top. See §Decision 1.
- **Amended by:** [ADR-0065](0065-one-agent-skill-root-contract.md) — Decision 5's three-root default now applies to coordination-kit receivers as well as scaffolded products; ownership and lane-specific triggers remain separate.
- **Amended by:** [ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5 — **EXECUTED 2026-07-28 ([#1636](https://github.com/FS-GG/.github/issues/1636)). Decision 5's declared root set is now TWO: `.claude/skills`, `.agents/skills`.** `.codex/skills` is **retired** — it is Codex's other native root, not a third runtime's. The authoritative statement of the set, and of how a root leaves it, is [ADR-0065](0065-one-agent-skill-root-contract.md) §Decision + §Retiring a root; this note does not restate it. Decision 5's *design* is untouched and is what made the flip a one-line change: the roots are one declared constant and destinations are computed, so `FsggKitSkillRoots` moved from three entries to two and a `FsggKitRetiredSkillRoots` declaration carried the sweep. **Decision 6 is untouched and re-affirmed:** a *committed* symlink is still rejected, now on a measurement — under `core.symlinks=false` it checks out as a regular text file and both runtimes then exit 0 with zero skills and no diagnostic. See §Decision 5. Brought current by [#1703](https://github.com/FS-GG/.github/issues/1703): [#1690](https://github.com/FS-GG/.github/pull/1690) landed the flip and amended ADR-0065 only, so this field said *"direction only"* for a day after the direction had been taken.

## Context

The goal is simple: a scaffolded product's three agent-skill roots (`.claude/skills/`,
`.codex/skills/`, `.agents/skills/`) must each hold the **same, correct** set of skills —
the union of SDD *process* skills and provider *product* skills — so the runtimes are
interchangeable (ADR-0011 §1). ADR-0011 chose the right invariants. The **implementation**,
audited 2026-07-01 across all four repos, is where the fragility lives:

1. **Four divergent implementations of the same "materialize union → N roots" idea**, each
   hand-maintained and kept in sync by convention:
   - `fsgg-sdd scaffold` post-instantiation mirror (`HandlersScaffold.fs`),
   - `fsgg-sdd refresh` re-mirror (`HandlersRefresh.fs`),
   - `fsgg-sdd doctor`/`upgrade` expected-artifact set (`Drift.fs`),
   - and — new as of FS.GG.Rendering Feature 230 — the `fs-gg-ui` template's **standalone**
     self-mirror: **12 skill sources × 3 roots = 36 hand-written `template.json` twins.**

2. **No content verification anywhere.** `doctor`/`upgrade` drift checks only path
   **presence** (`HandlersDoctor.fs`, `Drift.fs`); the composition gate
   (`FS.GG.Templates` `tests/composition/run.sh`) asserts **nothing** about the skill roots
   (only that the product builds). `scaffold-provenance.json` records `{ Path; Owner }` and
   **no digest** — so ADR-0011's stated rationale that "provenance stores sha256 digests …
   a symlink can't satisfy" describes a schema that **does not exist**. A root that exists
   but has drifted bytes, a provider skill missing from one root, or a `.codex` that diverges
   from `.claude` are all **invisible**. The apparatus that exists to guarantee "the three
   roots are the byte-identical union" does not check that they are.

3. **Providers vendor their own developer skill-surface into products.** The `fs-gg-ui`
   template copies the Rendering repo's own `.agents/skills/` wholesale, including
   `fs-gg-product-*` aliases and framework wrappers whose bodies route to *repo-internal*
   paths (`../../../src/**`, `../../../template/product-skills/**`). Those paths don't exist
   in a scaffolded product, so a spec-kit product ships **~13 dangling skill wrappers** — and
   no guard catches it (parity checks the repo, not the product).

4. **Ownership is enforced downstream of the actor that violates it.** Skill destinations are
   hand-written per template source; the only thing stopping the template from writing an
   SDD-owned root is SDD's `isSddTree` guard, discovered *at scaffold time in another repo*.
   The result was **#47**: a one-line missing template condition amplified into a five-repo,
   three-reframing epic (Templates #47/#48, SDD #53/#55/#57, Rendering #227/#228/#229/#230,
   `.github` ADR-0011/#107), still open at time of writing, with a manual `rm`+`doctor`
   incoherence window.

The invariants are sound; the fragility is *implementation multiplicity* + *missing
verification* + *a leaky product boundary*. This ADR fixes those three.

## Decision

**Skills are content-addressed data with one canonical body each, materialized and verified by
one shared algorithm across every lane.**

1. **One skill manifest per producer.** Each producer (SDD; each provider) declares its skills
   in a machine-readable **skill manifest** — `id`, `scope` (`process` | `product`),
   `sha256` of the canonical body, and the body itself (or a resolvable in-package path). The
   manifest is the contract; the fan-out reads manifests, never ad-hoc directory scans or
   per-source `template.json` strings. A skill has **exactly one canonical body**; the roots
   are copies of it.

   > **Amendment (2026-07-14, [ADR-0017](0017-skill-registry-condition-aware-materialization.md)).**
   > **`{ id, scope, sha256 }` is NOT the current manifest schema** — do not build against it as
   > written. ADR-0017 §1 extends the entry with an optional **`materializes-when`** (a predicate over
   > the scaffold parameter set — `profile`, `lifecycle`, `feedback`, `designSystem`, …; absent ⇒
   > `always`) and an optional **`supplied-by`** for a skill that crosses a producer boundary. The
   > reason is a defect this ADR's superset catalog left open: emission is lifecycle/profile-
   > conditioned, so "declared ∧ absent from every root" had to be *unconditionally* tolerated — which
   > made a genuine supply gap (`fs-gg-project`, supplied by **neither** producer under
   > `lifecycle=sdd`) indistinguishable from a correct off-profile absence. Recording the condition
   > makes each absence *justified* rather than blanket-tolerated, and lets the union gate fail on the
   > real one.
   >
   > The org-level catalog ADR-0017 also introduces —
   > [`registry/skills.yml`](../../registry/skills.yml), owned by `.github` and registered as the
   > governed contract `skill-registry` under
   > [ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md) — carries `owner`,
   > `source` and (since [#658](https://github.com/FS-GG/.github/issues/658)) `mirrored` on top of
   > those. Everything this section decides — one manifest per producer, one canonical body per skill,
   > content-addressed, the manifest is the contract — **stands**; only the entry's field set grew.

2. **One `materialize-and-verify` library, in `FS.GG.Contracts`.** Two pure functions —
   `mirror(union, roots) → writes` and `verify(roots, union) → diagnostics` — implemented
   **once** and consumed by every lane:
   - **Orchestrated lane** (`fsgg-sdd` present): the CLI computes `union = SDD manifest ∪
     provider manifest(s)` and calls `mirror`/`verify`. `scaffold`, `refresh`, `doctor`, and
     `upgrade` all route through this one library — the three current SDD implementations
     collapse to one.
   - **Standalone lane** (spec-kit, no `fsgg-sdd`): the template ships its manifest plus a
     **single** thin, cross-platform materialize step (one template post-action / build
     target invoking the same algorithm — a vendored copy of the same `FS.GG.Contracts`
     logic), replacing Feature 230's 36 hand-written twins. One mechanism, two entry points.

3. **Content-addressed provenance + a real content-equality guard.** `scaffold-provenance.json`
   gains a per-skill `sha256` (a `scaffold-provenance` contract minor bump). `verify` asserts,
   for **every** skill in the union (process **and** product): (a) present in each configured
   root, (b) **byte-identical across roots**, (c) hash matches the manifest. This is what
   ADR-0011 §Consequences intended ("extend the guard to claude≡codex≡agents = union") and
   what P9 promised; ADR-0014 makes it real. `doctor` reports divergence; `upgrade`
   re-materializes to repair it; the **composition gate asserts it end-to-end**.

   > **Amendment (2026-07-29, [.github#1656](https://github.com/FS-GG/.github/issues/1656)) — the
   > canonical digest is defined over DECODED TEXT, and a body that does not decode is REFUSED, not
   > hashed.**
   >
   > The canonical digest this clause rests on is `Fsgg.SkillMirror.sha256`, whose signature is
   > `body: string -> string` — it takes text that a caller has already decoded. For a body that is
   > **not valid UTF-8** the caller's decoder substitutes `U+FFFD` *before* hashing, so the library
   > hashes something the file does not contain, and **two different files collide**: a `SKILL.md`
   > holding the single byte `0xFF` and one holding `0xFE` both digest to
   > `83d544ccc223c057d2bf80d3f2a32982c32c3c0db8e2674820da5064783fb097`. Under (c) above — "hash
   > matches the manifest" — that is a **fail-open on the producing side**: two distinct bodies are
   > recorded under one digest and nothing downstream can tell them apart.
   >
   > A **UTF-16/UTF-32 BOM** is a *separate* disagreement with the opposite polarity, tracked alongside
   > this one: `File.ReadAllText` detects those BOMs and decodes accordingly, while both shells
   > special-case only the UTF-8 BOM `EF BB BF` — so there the library is the permissive side and the
   > file decodes rather than mangling. Refusal does not fire on it; it is not the fail-open above.
   >
   > **Implemented decision: refuse.** A skill body whose bytes are not valid UTF-8 is rejected with its own
   > diagnostic and its own exit code — an unreadable body is **not** a digest mismatch and must not be
   > reported as one. The alternative considered and rejected was redefining the digest over **raw
   > bytes**: arguably the more principled definition for a content address, but it changes the digest
   > of *every* file, so every recorded manifest digest in every repo would need regenerating in one
   > coordinated act, and it is a behaviour change on the published `FS.GG.Contracts` surface.
   > **Refusing costs no digest change for any valid file, so no manifest migration anywhere.**
   >
   > **Where the refusal belongs.** At the authority — `Fsgg.SkillMirror` is this ADR's *one*
   > implementation (§Decision 2), so the shells and producers inherit the refusal through the callable
   > seam rather than each growing their own check. `sha256` itself cannot host it: by the time it is
   > called the bytes are already gone, so the refusal belongs at the **read seam** that decodes the
   > file, reached additively (a byte-level entry point) rather than by breaking
   > `val sha256: body: string -> string`. FS.GG.Contracts implements that additive byte-level seam;
   > the raw-byte shells therefore remain intentionally different on invalid input, which is refused
   > before a library digest could silently collide.
   >
   > **Measured before deciding, not after:** across all 756 tracked files in `.github` — including all
   > 39 `SKILL.md` — **zero** contain invalid UTF-8, zero carry a BOM and zero contain a CR. So the
   > refusal turns no currently-green tree red; it closes the fail-open at its actual edge. This
   > repository's own producers (`scripts/fsgg-skill-registry-check`, `scripts/generate-driver-manifest`)
   > hash **raw bytes** and so never had the collision; the divergence is the library's alone.

4. **A declared product/dev boundary + a no-dangling-route guard.** A producer ships **only**
   skills whose `scope: product` — never its internal developer surface. Emitted skill bodies
   must be **self-contained**: a guard rejects any shipped skill whose body references a path
   that does not exist in the product tree (kills the dangling-wrapper class). The
   `fs-gg-product-*` alias layer and `src/**`-routing framework wrappers stay in the repo,
   out of the product manifest.

5. **The root set is one declared constant.** `AGENT_SKILL_ROOTS` (currently `.claude`,
   `.codex`, `.agents`) is declared once in the contract and consumed everywhere. Adding or
   renaming a runtime root is a one-line contract change, not an N-place edit across four
   repos; `mirror`/`verify`/`doctor` all derive their targets from it. Destinations are
   **computed**, never hand-written per source — so a provider *cannot* accidentally target an
   SDD-owned root, and the `isSddTree` guard becomes a backstop rather than the primary
   defense.

   > **2026-07-27 ([ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5):**
   > the constant's *value* is now decided to become **two** roots in the end state — `.agents/skills`
   > and `.claude/skills`. This clause's own design is what makes that a one-line change rather than
   > an N-place edit.
   >
   > **AMENDED 2026-07-28 ([ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5,
   > EXECUTED by [#1636](https://github.com/FS-GG/.github/issues/1636); this note corrected by
   > [#1703](https://github.com/FS-GG/.github/issues/1703)).** The sentence deleted from the note above
   > read *"and the constant is unchanged today: the flip carries a package-publication and
   > receiver-re-materialization tail and lands with the mechanism, not before it (ADR-0067 §9)"*. It
   > was written 2026-07-27 and falsified 2026-07-28. The constant's value is now
   > **two**, `.claude/skills` and `.agents/skills`, and the parenthetical *"(currently `.claude`,
   > `.codex`, `.agents`)"* above reads as the three-root era it was written in. The tail the note
   > predicted is what the flip actually cost: the constant alone was not enough, because dropping a
   > root from the declared set makes the materializer stop walking it, so a **retired-root**
   > declaration was added beside it (`FsggKitRetiredSkillRoots`, `FS.GG.Kit` 0.15.0) to sweep the
   > receivers. That is a second constant, not a repeal of this clause: destinations are still
   > computed, never hand-written per source.

6. **Invariants retained from ADR-0011.** Single mirror authority per lane; providers write
   only `.agents/skills/` in the orchestrated lane; strict `isSddTree`; materialized copies
   (not symlinks — the Windows-git-symlink and `dotnet new`/`WriteFile`-emits-real-files
   reasons hold; the sha256 reason is now *true* rather than aspirational).

## Consequences

- **`FS.GG.SDD`:** add the `materialize-and-verify` library + manifest + `AGENT_SKILL_ROOTS` to
  `FS.GG.Contracts`; route `scaffold`/`refresh`/`doctor`/`upgrade` through it (delete the three
  bespoke fan-outs); add per-skill `sha256` to `scaffold-provenance` (contract minor bump);
  make drift **content-aware** and cover **provider** skills, not just seeded ones. A coherent
  CLI release advances the orchestrator-axis minimum (ADR-0008), sequenced publish-before-flip.
- **`FS.GG.Rendering`:** publish a **product skill manifest** (`scope: product` only); replace
  Feature 230's 36 `template.json` twins with the single shared materialize step; stop
  vendoring the repo's `.agents/skills/` dev surface; fix the ~13 dangling wrappers; scope the
  `effectiveNameLower.replaces:"product"` token so it stops rewriting the English word.
- **`FS.GG.Templates`:** the composition gate asserts the three roots are the **byte-identical
  union** (content, not presence) in every lane; re-pin to the new coherent CLI + template set
  (closes the #47 chain).
- **`.github`:** this ADR; the roadmap
  ([`docs/reports/2026-07-01-skill-vendoring-robustness-roadmap.md`](../reports/2026-07-01-skill-vendoring-robustness-roadmap.md));
  a registry coherence id **`skill-mirror-verified`** (`coherent: false`) tracking the rollout;
  the `scaffold-provenance` contract minor bump recorded in the registry.
- **Net:** four mirror implementations → one; presence-only → content-addressed verification in
  every lane; leaky product boundary → declared manifest + no-dangling guard; hand-written
  destinations → one computed root-set constant. Simpler (one algorithm, one manifest) **and**
  more robust (the invariant is now machine-checked where it's produced and where it's consumed).
- **Migration is additive and staged** (see the roadmap): the manifest + library land first and
  run *alongside* today's mechanisms behind the same outputs; verification flips from advisory
  to enforcing once green; the hand-maintained twins/fan-outs are deleted last.

<!-- Reconcile docs/architecture.md's composition/skill picture once `skill-mirror-verified`
flips coherent and the composition gate asserts the union end-to-end. -->
