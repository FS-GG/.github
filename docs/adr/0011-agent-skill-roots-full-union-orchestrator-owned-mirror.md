# ADR-0011: Every agent-skill root carries the full skill union; `fsgg-sdd` owns the mirror

- **Status:** Accepted — **implementation superseded by [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md)** (2026-07-01): this ADR's five *invariants* stand, but where the two disagree on *mechanism* ADR-0014 wins — it replaces the four hand-maintained mirror mechanisms described here with one shared, content-addressed materialize-and-verify.
- **Date:** 2026-07-01
- **Affects:** FS.GG.SDD, FS.GG.Rendering (the `fs-gg-ui` template), FS.GG.Templates (provider pin), `.github` (registry)
- **Amended by:** [ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5 — **EXECUTED 2026-07-28 ([#1636](https://github.com/FS-GG/.github/issues/1636)). Decision 1's root set is now TWO: `.claude/skills`, `.agents/skills`.** `.codex/skills` is **retired**; §Context's *"the generic agent convention"* was never a third runtime — `.agents/skills` is Codex's own second native root, re-measured on Codex CLI 0.145.0 for that change. The authoritative record of the flip is [ADR-0065](0065-one-agent-skill-root-contract.md) §Decision + §Retiring a root, which names the ordered set and how a root leaves it; this note does not restate the mechanism. **This record otherwise stays IN FORCE** — its five invariants (byte-identical union in every root; `fsgg-sdd` as sole mirror authority; providers confined to `.agents/skills/`; strict `isSddTree`; materialized copies, never symlinks) are untouched by the retirement and still govern, over a two-root set. §Context's symlink rejection is **re-affirmed**, on a new measurement (ADR-0067 §6).
- **Amendment history — this note was 20 hours late, and that is recorded rather than tidied away ([#1703](https://github.com/FS-GG/.github/issues/1703)):** until 2026-07-28 the field above read *"direction only … its three-root union is still the rule today … nothing is retired until the phase that lands the replacement amends this record in the same change."* [#1690](https://github.com/FS-GG/.github/pull/1690) landed the retirement and amended [ADR-0065](0065-one-agent-skill-root-contract.md) and index rows 88/114/130 — but not this record — so that precondition went **unmet**, and this record went on telling every reader who opened it that three roots were the rule for a day after they were not. `adr-coherence` stayed green throughout: it gates amendment-link *symmetry*, and the link here was never one-sided. The leg that would have seen it (EXECUTION-STATE AGREEMENT, `scripts/check-adr-coherence.py`) lands with this amendment.

## Context

A scaffolded product carries three agent-skill roots, one per agent runtime:
`.claude/skills/` (Claude Code), `.codex/skills/` (Codex), and `.agents/skills/` (the
generic agent convention). The org requirement is that **these runtimes are
interchangeable** — a user on Claude, Codex, or a generic agent must see the *same* skills.
That means every root must hold the **full union** of skills produced for the product:
the SDD `fs-gg-sdd-*` process skills **and** the provider's `fs-gg-*` UI skills.

Today no producer writes all three roots, and the producers overlap in exactly one:

- **SDD** seeds the 15 `fs-gg-sdd-*` process skills into `.claude/skills/` **and**
  `.codex/skills/` (`SeededSkills.fs:64-68`) — never `.agents/`.
- **The `fs-gg-ui` provider** emits its 8 UI skills into `.agents/skills/` **and**
  `.claude/skills/` — never `.codex/` (FS.GG.Rendering Feature 219 / FR-001,
  `Feature219EmitFrameworkSkillsTests.fs:138-154`).

So `.codex/` is missing the UI skills, `.agents/` is missing the process skills, and the
two producers collide in `.claude/skills/`. SDD's scaffold guard treats the whole
`.claude/skills/` + `.codex/skills/` prefix as SDD-owned (`isSddTree`,
`HandlersScaffold.fs:53-62`, Feature 051 / FR-011) and rejects the provider's `.claude/`
write as an intrusion (`scaffold.providerWroteSddTree`), so a full-stack scaffold against
`FS.GG.UI.Template::0.1.58-preview.1` is **blocked** (FS-GG/FS.GG.Templates#47,
FS-GG/FS.GG.SDD#55).

A **symbolic-link** design (one canonical dir, per-root symlinks) was considered and
rejected: generated products ship `build.cmd`/`fake.cmd` (Windows is in scope) and git
does not materialize symlinks on Windows unless `core.symlinks=true` — otherwise the link
checks out as a plain text file, silently breaking the skill; `dotnet new` templates and
SDD's `WriteFile` effects emit real files, not symlinks; and `scaffold-provenance.json`
stores **sha256 digests of file bodies** with a content drift guard, which a symlink has
no body to satisfy. "One source of truth" is preserved without symlinks by keeping one
canonical body per skill in its producer and **materializing** the union.

## Decision

1. **Full union, every root.** `.claude/skills/`, `.codex/skills/`, and `.agents/skills/`
   MUST each contain the **byte-identical union** of all skills produced for the product
   (SDD process skills ∪ provider UI skills). The three runtimes are interchangeable.

   > **2026-07-27 ([ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5):**
   > *"the three runtimes"* is **false** — there are **two**. Every in-tree reader of
   > `.agents/skills` across `.github` and all seven receivers is an FS-GG verifier, not an agent
   > runtime, and Codex resolves `.agents/skills` natively with no configuration (measured, Codex
   > CLI 0.145.0). `.codex/skills` carries no runtime the other two roots do not.
   >
   > **AMENDED 2026-07-28 ([ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5,
   > executed by [#1636](https://github.com/FS-GG/.github/issues/1636); this note corrected by
   > [#1703](https://github.com/FS-GG/.github/issues/1703)).** The clause above is **history**: the
   > enumerated root set is now the **two** of [ADR-0065](0065-one-agent-skill-root-contract.md)
   > §Decision — `.claude/skills`, `.agents/skills` — and `.codex/skills` is retired. The clause's
   > *rule* is untouched and still governs: whatever roots are declared, each MUST hold the
   > byte-identical union, and the runtimes are interchangeable. Only the enumeration changed. The
   > sentence deleted here read *"this clause still governs today … nothing is retired before its
   > replacement is proven"*; it was written on 2026-07-27 and falsified on 2026-07-28, and its
   > precondition — amend this record in the same change — was **not** met by
   > [#1690](https://github.com/FS-GG/.github/pull/1690).

2. **`fsgg-sdd` is the sole mirror authority.** As the orchestrator (ADR-0008), the CLI —
   after invoking the provider — computes the union and **materializes real files** into
   all three roots. There is one canonical body per skill (embedded in its producer);
   the roots are copies, not symlinks. The mirrored files are recorded in
   `scaffold-provenance.json` and the shape/drift guard asserts the three roots are equal.

   > **AMENDED 2026-07-28 (same amendment as Decision 1 — [ADR-0067](0067-resolve-dont-copy-one-skill-source-two-runtime-roots-a-generated-view.md) §5,
   > executed by [#1636](https://github.com/FS-GG/.github/issues/1636)).** Read *"all three roots"*
   > here, and in §Consequences' FS.GG.SDD bullet, as *"every declared root"* — the declared set is
   > now two. The clause's rule — one mirror authority, computed destinations, equal roots — is
   > untouched. The *mechanism* was already superseded by
   > [ADR-0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) (see §Status).

3. **Providers are confined to `.agents/skills/`.** A provider's product output for skills
   is `.agents/skills/` only; it MUST NOT write `.claude/skills/` or `.codex/skills/`. The
   SDD intrusion guard therefore **stays strict** — its current block is correct once
   providers stop writing the SDD-owned roots.

4. **The `fs-gg-ui` template drops its `.claude/` skill emission.** FS.GG.Rendering
   Feature 219 changes from "emit each UI skill to `.agents/` **and** `.claude/`" to
   "emit to `.agents/` only"; the orchestrator fans them out.

## Consequences

- **FS.GG.Rendering (→ FS.GG.Templates#47):** change Feature 219 so the `fs-gg-ui`
  template emits UI skills into `.agents/skills/` only; update its emission-matrix test;
  re-release the `fs-gg-ui-template` coherent set. My earlier "template must emit only to
  `.agents/`" instinct on #47 is the right end-state — but as a *consequence of the
  orchestrator owning the mirror*, not because `.claude/` is SDD-exclusive.
- **FS.GG.SDD (→ FS.GG.SDD#55):** add the orchestrator fan-out to `scaffold` (and
  `refresh`/`upgrade`): read the provider's produced `.agents/skills/` set, union it with
  the seeded `fs-gg-sdd-*` bodies, and write byte-identical copies into all three roots
  (reusing the no-clobber `AgentGuidanceTarget` semantics); record them in provenance;
  extend the shape/drift guard from "claude≡codex" to "claude≡codex≡agents = union". Keep
  `isSddTree` strict (this supersedes the #55-as-filed "loosen the guard" option). The
  fan-out changes the CLI's seeded-artifact surface, so it advances the orchestrator-axis
  minimum CLI version (ADR-0008) — sequence the CLI release before the clean scaffold.
- **`.github` (registry):** add coherence id `agent-skill-mirror` (`coherent:false`, a
  standing request) tracking #47 + #55; this ADR records the decision.
- **Ordering:** both halves must ship — the SDD fan-out (CLI) and the Rendering template
  change — before a full-stack scaffold is clean end-to-end. Track the sequence on the
  Coordination board (Phase `P2 SDD` + `P4 Templates`).
- **Interim workaround (unchanged):** on the current pin, delete the leaked
  `.claude/skills/fs-gg-{elmish,keyboard-input,layout,scene,skiaviewer,styling,symbology,ui-widgets}`
  and re-run `fsgg-sdd doctor`.

<!-- Follow-up: reconcile docs/architecture.md (the agent-skill-root ownership picture)
after the registry `agent-skill-mirror` entry lands. -->
