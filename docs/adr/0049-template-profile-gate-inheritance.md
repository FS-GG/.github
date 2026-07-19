# ADR-0049: A Governance gate can bind to a scaffold template-profile, and every product of that profile inherits it

- **Status:** Accepted
- **Date:** 2026-07-19
- **Affects:** FS-GG/FS.GG.Governance (config schema + enforcement — owner); FS-GG/FS.GG.Templates (the `fs-gg-governance` overlay and the `game` profile); every product scaffolded on a bound profile.
- **Decides:** the Governance half of the "gameplay must be headlessly tested" initiative — how the per-FR obligation of [ADR-0048](0048-fr-level-classification-keys-a-per-requirement-evidence-obligation.md) reaches **every** game and cannot be silently dropped.

## Context

The gameplay obligation must apply to **every product scaffolded on the `game` profile**, be
**block-on-ship**, and be **org-owned** — not re-decided per product. Governance today cannot do the
"every product / org-owned" part:

- **Policy is local and copied.** The `fs-gg-governance` overlay (`FS.GG.Templates/templates/
  fs-gg-governance/`) *copies* `capabilities.yml` / `policy.yml` / … into each product's own
  `.fsgg/`. There is no org- or profile-level policy a product inherits, and products **diverge**:
  FS.GG.Game has already dropped the `evidence` domain from its copied gate set. A gate a product
  can delete from its own `.fsgg/` has no org-wide teeth.
- **`policy.yml` "profiles" are the wrong axis.** They are the enforcement-strictness dial
  (`light | standard | strict | release`, `Enforcement.fs` `recognizeProfile`) — how hard the same
  gates bite — not a way to attach a gate to a family of products.
- **The `game` "profile" carries no binding.** It is a scaffold-provider starter
  (`FS.GG.Templates/providers/rendering.providers.yml`); it selects what scaffolds, then drops a
  copy of the overlay. `Surface.TemplateProfile` exists but only **records provenance** — which
  template a root was instantiated from — and binds nothing.

What *is* supported is the gate mechanism itself: a `checks:` entry with
`maturity: block-on-ship` / `tier: focusedTests` in `capabilities.yml`, enforced by
`maturityFloor`/`profileTighten` in `Enforcement.fs`. The missing piece is **inheritance** — one
gate, declared once, that binds to a profile and reaches every product of it.

## Decision

1. **Profile-scoped gate binding.** A gate may be declared once in an **org-owned** reference set
   and bound to a scaffold template-profile (e.g. `game`). At enforcement time the engine consults
   the profile-bound gates for the product's `TemplateProfile` **in addition to** its local
   `capabilities.yml`. `TemplateProfile` — today provenance-only — becomes the **load-bearing key**
   the inherited gates are looked up by.
2. **Inherited gates are a floor a product cannot lower.** A product's local `.fsgg/` edits cannot
   remove or downgrade an inherited gate — closing the divergence hole the audit found. Local gates
   still add product-specific surfaces on top; profile-bound gates are the org-owned floor beneath.
3. **The gameplay gate is the first profile-bound gate.** The per-classified-FR readiness count from
   [ADR-0048](0048-fr-level-classification-keys-a-per-requirement-evidence-obligation.md) binds to
   the `game` profile at `maturity: block-on-ship`.

## Consequences

- Governance owns the profile→gate map and the enforcement change (`deriveEffectiveSeverity` now
  unions inherited gates with local ones). This **retires the "policy is purely local" invariant**
  the overlay assumed → a versioned **contract change** to the governance config / governance-handoff
  surface: `registry/dependencies.yml` + `docs/registry/compatibility.md`, **publish-before-flip**.
- Once the `game` binding lands, every game inherits the gate immediately. Combined with the
  non-relaxable synthetic gate and no-waiver posture, a game lacking a non-synthetic test for a
  classified gameplay FR becomes **unshippable**. Flipping the binding on therefore **must** follow
  a proof that a reference game passes the full chain end-to-end — sequenced on the Coordination
  board, not decided here.
- The strictness dial still composes: an inherited `block-on-ship` gate tightens under
  `strict`/`release` exactly like a local one (`maturityFloor` − `profileTighten`).
- The overlay keeps copying local, product-specific gates; only the org-owned floor moves to
  inheritance. Existing products gain the floor without re-scaffolding.

## Alternatives considered

- **Bake the gate into the copied reference gate set** (no inheritance). Cheapest, no schema change.
  **Rejected**: copy-and-drift — the audit found products already diverge, and a gate a product can
  delete has no org-wide teeth. This is the "seeded, drift-accepted" path; the org chose org-owned.
- **A full org-level policy-inheritance hierarchy** (products inherit an org policy wholesale).
  **Rejected as over-scoped**: the need is one profile→gate binding, not a general inheritance
  system. Start minimal; generalize only if a second consumer appears.
- **Enforce via CI convention outside Governance.** **Rejected**: puts teeth outside the model that
  `ship` already hands off to, creating two sources of truth for what blocks a merge.
