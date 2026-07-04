# The skill registry (design)

> **Status:** design proposal for [ADR-0017](../adr/0017-skill-registry-condition-aware-materialization.md).
> Nothing here is wired yet — this is the reviewable design before any generator, validator, or
> gate change ships. It extends [ADR-0014](../adr/0014-skill-vendoring-one-manifest-one-materialize-verify.md)
> (one manifest per producer, content-addressed) and the
> [skill-union assertion](skill-union-assertion.md) (the consumer gate).

## Why a registry

Today the skill universe is a superset catalog with no recorded emission condition (ADR-0017 §C1–C3):

- a scaffolded product ships a manifest declaring skills it does not materialize, with no per-entry
  reason — indistinguishable from corruption;
- a **genuine** supply gap (`fs-gg-project`: declared `scope:product`, gated `spec-kit` in Rendering,
  excluded by SDD's `SeededSkills`) is invisible, because "declared ∧ absent" is unconditionally
  tolerated;
- there is no single authority listing the full process + product set, so "does this product have
  the right skills?" cannot be checked.

The registry is the single authoritative catalog; `materializes-when` is the field that turns
"absent" from *unverifiable* into *checkable*.

## `registry/skills.yml` — schema

Mirrors `registry/dependencies.yml`: `schemaVersion`, an `updated:` date, a governed schema
(contract `skill-registry`, ADR-0015), a `docs/registry/` projection, a `CHANGELOG.md`. **Generated
and reconciled from the producer manifests** — never hand-authored bytes.

```yaml
schemaVersion: 1
updated: "2026-07-04"

# The scaffold parameters materializes-when predicates may reference, and where they are read at
# scaffold time (scaffold-provenance.json → effectiveParameters).
parameters: [profile, lifecycle, feedback, designSystem]

skills:
  # ---- process skills (SDD; always emitted) ----
  - id: fs-gg-sdd-charter
    scope: process
    owner: fs-gg-sdd
    source: FS.GG.SDD/src/FS.GG.SDD.Commands/.../SeededSkill.fs-gg-sdd-charter
    materializes-when: always
  # … the 15 fs-gg-sdd-* skills, all `always` …

  # ---- product skills (Rendering; profile/lifecycle/feedback-gated) ----
  - id: fs-gg-scene
    scope: product
    owner: fs-gg-rendering
    source: FS.GG.Rendering/template/product-skills/fs-gg-scene/SKILL.md
    sha256: 37ef7800bcc6de7c9bd17e0594942e76fe543e379279dd54a5e6066f052a0da7
    materializes-when: "profile in [app, headless-scene, governed, sample-pack, game]"

  - id: fs-gg-elmish
    scope: product
    owner: fs-gg-rendering
    sha256: 30406f466a2a45341a2def8ddaa3bfe4627cf3aece83e0efef03282e1e0a450b
    materializes-when: "profile in [app, sample-pack, game]"

  - id: fs-gg-layout          # + keyboard-input, styling, ui-widgets
    scope: product
    owner: fs-gg-rendering
    sha256: 85c55e26ee42bd8d54533abcf6cb955d53e02651901be3c0e91bfe92dac92d62
    materializes-when: "profile in [app, game]"

  - id: fs-gg-testing
    scope: product
    owner: fs-gg-rendering
    sha256: b7661998e340c06c7d2fb3d44214e780de7c4cff7720fc730f7a39860cb6a01d
    materializes-when: "profile == governed"

  - id: fs-gg-samples
    scope: product
    owner: fs-gg-rendering
    sha256: 5fb78dd43379d13f84526a70dfd01000544e6c99bbf164c510d160da06b1ce31
    materializes-when: "profile == sample-pack and lifecycle == spec-kit"

  - id: fs-gg-feedback-capture
    scope: product
    owner: fs-gg-rendering
    sha256: e6c16f33a3f9b06995378952ba07408cee8a9f4eb4a9ce18bf2142602c40f499
    materializes-when: "feedback == true and lifecycle == spec-kit"

  # The seam ADR-0017 §C2 / FS.GG.SDD#53 must resolve: Rendering gates it to spec-kit, SDD's
  # SeededSkills excludes it, so the sdd lane supplies it from NOWHERE. `supplied-by` records the
  # intended owner per lane; the validator fails if the named owner does not in fact emit it.
  - id: fs-gg-project
    scope: product
    owner: fs-gg-rendering
    sha256: 3dc0b55b9681c99485c39cb0eabd1effaba70061d8a9e5eb57f270061c4aa16d
    materializes-when: "lifecycle == spec-kit"
    supplied-by: { spec-kit: fs-gg-rendering, sdd: UNRESOLVED }   # ← the defect, made explicit
```

`sha256` values above are the current canonical `SKILL.md` bodies (verified during the ADR-0017
investigation); `materializes-when` predicates are the literal `template.json` `sources[].condition`
expressions, normalized. Process-skill `sha256`s come from SDD's manifest once it publishes one.

## The `materializes-when` predicate

A small boolean expression over `parameters`: `==`, `!=`, `in [..]`, `and`, `or`, `true`/`false`,
and the literal `always`. Evaluated against a scaffold's `scaffold-provenance.json`
`effectiveParameters`. Kept intentionally tiny — it must be evaluable in both the typed validator
(`Fsgg.Registry`) and the shell gate (`skill-union-assert.sh`) without a real expression engine.

## Condition-aware union gate

`skill-union-assert.sh` gains an optional `--params <provenance.json>`. When present, for each
declared skill it evaluates `materializes-when` and adds two verdict classes to the existing four:

| declared? | condition | materialized? | verdict |
|---|---|---|---|
| yes | true  | yes | check `sha256` → `[drifted]` if mismatch, else **ok** |
| yes | true  | **no**  | **`[missing]`** — FAIL (new; catches `fs-gg-project`) |
| yes | false | yes | **`[unexpected]`** — FAIL (new; materialized off-profile) |
| yes | false | no  | legitimate — *justified* off-profile absence (was: blanket-tolerated) |
| no  | —     | yes | `[dangling]` unless `--co-tenants` admits it (unchanged) |

Without `--params` the gate keeps today's superset semantics exactly, so adoption is opt-in per
caller. `[partitioned]`/`[divergent]` (cross-root checks 1–2) are independent of conditions and
unchanged.

## Add / manage / remove — the lifecycle

| Action | Steps |
|---|---|
| **Add** | author `SKILL.md` in the owning repo → add a `skills.yml` row (`id`, `scope`, `owner`, `source`, `sha256`, `materializes-when`) → owning generator emits it into `.agents/skills/` (mirrored) → validator asserts registry = manifest = materialized bytes, condition-aware → prepend a `CHANGELOG.md` entry. A row without an emitting source fails the validator; an emitted skill without a row fails as `[dangling]`. |
| **Change** | edit `SKILL.md` → regenerate `sha256` (`skill-union-assert.sh --digest …`) → update the row + producer manifest in the same change → validator asserts all three agree. Changing a condition updates `materializes-when` only. |
| **Remove** | delete the row + `SKILL.md` + the manifest entry in one change → validator asserts no dangling (present-but-undeclared) and no `[missing]` (declared-true-but-absent) in either direction. |

The gate's two-directional fail-closed (`[missing]` for declared→disk, `[dangling]` for
disk→declared) makes a **half-done** add or remove red, which is the property C1/C2 lacked.

## Governance & rollout

- `skill-registry` is a governed contract (ADR-0015): typed validator in `Fsgg.Registry`, schema
  growth = tracked `contract-change` (bump `skill-registry.version` + `schemaVersion`, advance the
  gate's `FS.GG.SDD.Cli` pin).
- Rollout is sequenced on the Coordination board, not a flag day: **(P1 Rendering)** manifest gains
  `materializes-when`; **(P2 SDD)** process manifest + resolve the `fs-gg-project` seam;
  **(.github)** `registry/skills.yml` + `skill-registry` contract + gate `--params`; **(P4 Templates)**
  composition gate passes provenance; then flip the `skill-mirror-verified` coherence id enforcing.
- Relationship to existing surfaces: this **extends** ADR-0014's manifest (additive field) and the
  skill-union assertion (adds classes) — it does not replace either. `dependencies.yml` stays the
  contract registry; `skills.yml` is the *skill* registry, cross-referenced by the
  `skill-mirror-verified` / `fsgg-sdd-orchestrator-axis` coherence rows.
