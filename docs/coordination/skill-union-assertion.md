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
3. **matches-manifest** *(only with `--manifest`)* — its content digest equals the digest the
   producer's [skill-manifest](#the-manifest-and-the-canonical-digest) declares (*drifted*), and
   no root carries a skill the manifest does not declare (*dangling*).

Checks 1–2 are **self-contained** — they need nothing but the product tree, so they enforce today
(the highest-value, previously-unchecked property). Check 3 activates the moment a producer ships a
manifest with per-skill digests ([FS.GG.SDD#60](https://github.com/FS-GG/FS.GG.SDD/issues/60) /
[FS.GG.Rendering#43](https://github.com/FS-GG/FS.GG.Rendering/issues/43), ADR-0014 P0/P2). This is
**publish-before-flip**: the mechanism lands and can enforce cross-root identity now; the manifest
cross-check wires in when the manifest exists.

## The manifest and the canonical digest

The producer manifest is JSON — ADR-0014's `{ id, scope, sha256, body }` per skill:

```json
{ "roots": [".claude/skills", ".codex/skills", ".agents/skills"],
  "skills": [
    { "id": "cross-repo-coordination", "scope": "process", "sha256": "<tree-hash>" },
    { "id": "fs-gg-ui-render",          "scope": "product", "sha256": "<tree-hash>" }
  ] }
```

The `sha256` is a **deterministic content tree hash** so it survives multi-file skills
(`SKILL.md` + `references/**`): `sha256` over the C-locale-sorted stream of `"<relpath>\n<sha256 of
that file>\n"` for every regular file under the skill dir. The assertion exposes this exact
algorithm as a **reference generator** so producers never drift from the checker:

```sh
scripts/skill-union-assert.sh --digest .claude/skills/<id>   # prints the canonical digest
```

A producer's manifest **must** emit `sha256` with this generator (or a byte-equivalent
reimplementation, per ADR-0014 §6's content-parity requirement).

## Usage

Directly:

```sh
scripts/skill-union-assert.sh --product <product-dir> \
  [--roots ".claude/skills .codex/skills .agents/skills"] \
  [--manifest <manifest.json>]
```

`AGENT_SKILL_ROOTS` (env) overrides the default root set — ADR-0014's "one declared constant":
adding a runtime root is a one-line change, no per-repo source edits. Exit `0` = the roots are the
byte-identical union; non-zero = at least one violation, each printed with its class
(`[partitioned]` / `[divergent]` / `[dangling]` / `[drifted]`).

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
```

The [FS.GG.Templates composition gate](https://github.com/FS-GG/FS.GG.Templates/issues/49)
(roadmap **T3.2**) is the first caller — it invokes this for the orchestrated **and** standalone
lanes, replacing the current "grep for the failure string and skip" (ADR-0014 F2, consumer half).

## Self-test

[`tests/skill-union/run.sh`](../../tests/skill-union/run.sh) — run in CI by
[`skill-union-selftest.yml`](../../.github/workflows/skill-union-selftest.yml) — builds throwaway
product trees and proves the assertion **passes** on a coherent union and **fails** on a divergent,
partitioned, dangling, and manifest-drifted root. This is the acceptance evidence for #111.

## Where this sits

- **Produced** by the P1 SDD `mirror`/`verify` library and the P2 fs-gg-ui single-materialize step
  (ADR-0014) — they write the roots and self-verify at the source.
- **Consumed** here — asserted again where products are composed, so a non-identical set fails a
  gate instead of shipping green.
- Flips **enforcing** and the `skill-mirror-verified` coherence id to `coherent: true` at roadmap
  **P4**, closing the [#47](https://github.com/FS-GG/FS.GG.Templates/issues/47) chain.
