# The skill-union assertion

A single reusable check — [`scripts/skill-union-assert.sh`](../../scripts/skill-union-assert.sh),
wrapped by the [`skill-union-assert.yml`](../../.github/workflows/skill-union-assert.yml)
(`workflow_call`) workflow — that any consumer CI calls to prove a scaffolded product's
agent-skill roots are the **byte-identical union** of process + product skills. It is the
**consumer-side arm** of [ADR-0014](../adr/0014-skill-vendoring-one-manifest-one-materialize-verify.md)'s
content-addressed design, delivered by [.github#111](https://github.com/FS-GG/.github/issues/111)
(epic [#110](https://github.com/FS-GG/.github/issues/110), roadmap phase **P3.G3.1**). It mirrors
the [contract-coherence gate](contract-coherence-gate.md) and the
[dispatch-sender](auto-update-fabric.md) reusable pattern: one script authored in FS-GG/.github,
one thin `workflow_call` wrapper, wired into a caller with a single `uses:` block.

## Why

ADR-0014's four-repo audit (finding **F2**) found the skill-vendoring apparatus verified
**presence only** — `doctor`/`upgrade` checked `Option.isSome`, the composition gate asserted
*nothing* about the roots, and `scaffold-provenance` carried no digest. So a root that exists but
has **drifted bytes**, a provider skill **missing from one root**, or a `.codex` that **diverges**
from `.claude` were all invisible. The apparatus that exists to guarantee "the three roots are the
byte-identical union" never checked that they were. This assertion is that missing check, made
reusable so *every* lane (orchestrated `fsgg-sdd` **and** standalone template) asserts it where
skills are consumed.

## What it checks

Over the configured `AGENT_SKILL_ROOTS` (default ADR-0011's three: `.claude/skills`,
`.codex/skills`, `.agents/skills`), for **every** skill in the union it asserts:

1. **present** — the skill directory exists in **every** root (a miss is a *partitioned* root);
2. **byte-identical** — its bytes are identical across all roots (a diff is a *divergent* root);
3. **matches-manifest** *(only with `--manifest`)* — if the producer's
   [skill-manifest](#the-manifest-and-the-canonical-digest) declares it, its `SKILL.md` digest
   equals the declared digest (*drifted*); if the manifest does **not** declare it, it must match
   a `--co-tenants` glob (*dangling* otherwise).
4. **condition-aware** *(only with `--manifest` **and** `--params`)* — evaluates each declared
   skill's `materializes-when` against the scaffold's `effectiveParameters`
   ([the condition-aware manifest](#condition-aware-check-4----params-adr-0017), ADR-0017), adding
   *missing* (declared ∧ condition true ∧ absent everywhere) and *unexpected* (present ∧ condition
   false). Without `--params` the gate keeps the check-3 superset semantics exactly.

Checks 1–2 are **self-contained** — they need nothing but the product tree. Check 3 cross-checks
the manifests the producers actually ship ([FS.GG.SDD#61](https://github.com/FS-GG/FS.GG.SDD/issues/61) /
[FS.GG.Rendering#43](https://github.com/FS-GG/FS.GG.Rendering/issues/43), ADR-0014 P0–P2), in
**their** semantics — aligned by [.github#120](https://github.com/FS-GG/.github/issues/120):
`Fsgg.SkillMirror` (FS.GG.Contracts 1.4.0) is ADR-0014's "one implementation", so the assertion
follows it, not vice versa. Check 4 is the ADR-0017 tightening — see below.

## The manifest and the canonical digest

The producer manifest is JSON — ADR-0014's `{ id, scope, sha256, body }` per skill:

```json
{ "roots": [".claude/skills", ".codex/skills", ".agents/skills"],
  "skills": [
    { "id": "cross-repo-coordination", "scope": "process", "sha256": "<SKILL.md body sha256>" },
    { "id": "fs-gg-ui-render",          "scope": "product", "sha256": "<SKILL.md body sha256>" }
  ] }
```

**Digest.** The `sha256` is the **canonical-body sha256 of the skill's `SKILL.md` only** — the
algorithm `Fsgg.SkillMirror` ships (byte-equivalent to `sha256sum SKILL.md`, verified in
[.github#120](https://github.com/FS-GG/.github/issues/120)). Multi-file skills (`SKILL.md` +
`references/**`) are covered by the **cross-root identity** of checks 1–2, not by the digest.
The assertion exposes the algorithm as a **reference generator** so producers and checker never
drift:

```sh
scripts/skill-union-assert.sh --digest .claude/skills/<id>   # prints the canonical digest
```

**Set semantics.** The manifest is a **superset catalog** — an *upper bound*, not an exact set.
Producers declare every skill they can emit, but emission is lifecycle/profile-conditioned, so:

- **declared ∧ present** → the digest must match (else `[drifted]`);
- **declared ∧ absent from every root** → legitimate (skipped, surfaced in the summary count);
- **declared ∧ present in only some roots** → still `[partitioned]` (check 1);
- **present ∧ undeclared** → `[dangling]`, **unless** the id matches a `--co-tenants` glob —
  the roots legitimately hold process skills from co-tenant producers the product manifest
  doesn't own (e.g. `--co-tenants "fs-gg-sdd-* speckit-*"` for the sdd / spec-kit lanes).

The one gap this leaves: **declared ∧ absent from every root is *blanket*-tolerated** — the manifest
records no reason a skill was skipped, so a genuinely-dropped skill is indistinguishable from an
intentional off-profile omission. That is exactly how a supply failure (`fs-gg-project`) shipped
unnoticed. Check 4 closes it.

## Condition-aware (check 4) — `--params` (ADR-0017)

[ADR-0017](../adr/0017-skill-registry-condition-aware-materialization.md) makes absence *checkable*
by recording the **emission condition** on each manifest entry — an optional `materializes-when`
predicate (absent ⇒ `always`):

```json
{ "skills": [
    { "id": "fs-gg-scene",   "scope": "product", "sha256": "…", "materializes-when": "profile in [app, headless-scene, governed, sample-pack, game]" },
    { "id": "fs-gg-project", "scope": "product", "sha256": "…", "materializes-when": "lifecycle == spec-kit" }
  ] }
```

Pass the scaffold's own parameters with **`--params <scaffold-provenance.json>`** (read from
`.effectiveParameters`) and the assertion evaluates each declared skill's predicate, turning
"declared ∧ absent" from blanket-tolerated into **justified**. Two classes are added to the four
above:

| declared? | condition | materialized? | verdict |
|---|---|---|---|
| yes | true  | yes | check `sha256` → `[drifted]` if mismatch, else **ok** |
| yes | true  | **no**  | **`[missing]`** — FAIL (new; catches the dropped `fs-gg-project`) |
| yes | false | **yes** | **`[unexpected]`** — FAIL (new; materialized off-profile) |
| yes | false | no  | legitimate — *justified* off-profile absence (was: blanket-tolerated) |
| no  | —     | yes | `[dangling]` unless `--co-tenants` admits it (unchanged) |

`[partitioned]` / `[divergent]` (the cross-root checks 1–2) are independent of conditions and
unchanged. **Without `--params` the gate keeps today's superset semantics exactly**, so adoption is
opt-in per caller — no consumer is forced to change at once. `--params` **requires** `--manifest`
(the conditions live on the manifest entries; `--params` only supplies the values). The
[FS.GG.Templates composition gate](https://github.com/FS-GG/FS.GG.Templates/issues/49) is the first
caller to pass provenance, enforcing `[missing]`/`[unexpected]` in both lanes.

**The predicate language** is deliberately tiny — evaluable in both the shell gate and the typed
validator (`Fsgg.Registry`) without a real expression engine, so the two never drift:
`always` · `<param> == <value>` · `<param> != <value>` · `<param> in [<v>, <v>, …]` · clauses joined
by `and` / `or` (`and` binds tighter; no parentheses). Values and params are bare tokens
(`[A-Za-z0-9_-]`, plus `true`/`false`); a param absent from the provenance reads as empty. The
authoritative catalog of ids + conditions is the org skill registry `registry/skills.yml` (ADR-0017),
generated from the producer manifests.

## Usage

Directly:

```sh
scripts/skill-union-assert.sh --product <product-dir> \
  [--roots ".claude/skills .codex/skills .agents/skills"] \
  [--manifest <manifest.json>] \
  [--co-tenants "fs-gg-sdd-* speckit-*"] \
  [--params <scaffold-provenance.json>]        # enables the condition-aware check 4 (needs --manifest)
```

`AGENT_SKILL_ROOTS` (env) overrides the default root set — ADR-0014's "one declared constant":
adding a runtime root is a one-line change, no per-repo source edits. Exit `0` = the roots are the
byte-identical union; non-zero = at least one violation, each printed with its class
(`[partitioned]` / `[divergent]` / `[dangling]` / `[drifted]` / `[missing]` / `[unexpected]`).

## Adoption — wiring it into a consumer repo's CI

```yaml
permissions:
  contents: read
jobs:
  skill-union:
    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main
    with:
      product-path: "path/to/scaffolded/product"
      # roots: ".claude/skills .codex/skills .agents/skills"   # AGENT_SKILL_ROOTS, if non-default
      # manifest: "path/to/skill-manifest.json"                # enables the digest cross-check
      # co-tenants: "fs-gg-sdd-* speckit-*"                    # undeclared co-tenant ids to admit
      # params: ".fsgg/scaffold-provenance.json"               # enables [missing]/[unexpected] (needs manifest)
```

The [FS.GG.Templates composition gate](https://github.com/FS-GG/FS.GG.Templates/issues/49)
(roadmap **T3.2**) is the first caller — it invokes this for the orchestrated **and** standalone
lanes, replacing the current "grep for the failure string and skip" (ADR-0014 F2, consumer half).

## Self-test

[`tests/skill-union/run.sh`](../../tests/skill-union/run.sh) — run in CI by
[`skill-union-selftest.yml`](../../.github/workflows/skill-union-selftest.yml) — builds throwaway
product trees and proves the assertion **passes** on a coherent union (including a
superset-catalog manifest with declared-but-absent ids and `--co-tenants`-admitted process
skills) and **fails** on a divergent (`SKILL.md` *and* `references/**`), partitioned, dangling,
and manifest-drifted root, and that `--digest` equals the producers' `sha256sum SKILL.md`. For the
condition-aware check (ADR-0017) it additionally proves — with a `--params` provenance — a
`[missing]` (declared ∧ true ∧ absent, the `fs-gg-project` case), an `[unexpected]` (present ∧
false), a **justified** absence + compound-true present that **pass**, that the *same* manifest
**without** `--params` keeps the superset semantics (declared-absent tolerated), and that
`--params` without `--manifest` is a misconfiguration (exit 2). This is the acceptance evidence for
#111 (semantics aligned by #120) and #164 (ADR-0017 check 4).

## Where this sits

- **Produced** by the P1 SDD `mirror`/`verify` library and the P2 fs-gg-ui single-materialize step
  (ADR-0014) — they write the roots and self-verify at the source.
- **Consumed** here — asserted again where products are composed, so a non-identical set fails a
  gate instead of shipping green.
- Flips **enforcing** and the `skill-mirror-verified` coherence id to `coherent: true` at roadmap
  **P4**, closing the [#47](https://github.com/FS-GG/FS.GG.Templates/issues/47) chain.
