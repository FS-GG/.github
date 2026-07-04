# Skill registry changelog

Reverse-chronological log of changes to [`skills.yml`](skills.yml) — the FS-GG org skill
registry (the authoritative catalog of process + product skills, ADR-0017). Its human projection
is [`../docs/registry/compatibility.md`](../docs/registry/compatibility.md) (§Versioned contracts →
`skill-registry`, §Coherence state → `skill-registry-published`).

**Protocol.** Mirrors [`CHANGELOG.md`](CHANGELOG.md) (the `dependencies.yml` log): every change to
`skills.yml` **prepends one dated entry** at the top of the Entries list below (newest first) and
sets the file's `updated:` date to match. Entries follow the same loose
`HEADER (owner; refs): body` grammar — name the skill id(s)/scope touched, the owner, and the
issue/ADR refs. `skills.yml` is **generated/reconciled from the producer skill-manifests**, so an
entry should say which producer manifest (and version/PR) a row was reconciled from.

`skills.yml` is a governed contract (`skill-registry`, ADR-0015): schema growth (a new field or a
tightened rule) is a tracked `contract-change` — bump `skills.yml` `schemaVersion` + the
`skill-registry.version` in `dependencies.yml` in the same PR that teaches `Fsgg.Registry`, and
advance the gate's `FS.GG.SDD.Cli` pin.

## Entries

<!-- Prepend new entries here, newest first:
- **YYYY-MM-DD** — HEADER (owner; refs): body
-->

- **2026-07-04** — CATALOG CREATED (`schemaVersion: 1`) — PRODUCT-COMPLETE, PROCESS-PROVISIONAL (owner github; .github#168, epic #163, ADR-0017): first cut of `registry/skills.yml`. **12 product skills** (owner `fs-gg-rendering`) reconciled verbatim from Rendering's published producer manifest `template/skill-manifest/skill-manifest.json` (Feature 238 / FS.GG.Rendering#76, closes Rendering#71) — authoritative `sha256` + emission condition; `materializes-when` recorded in the **ADR-0017 canonical grammar**, NORMALIZED from the C-style form the manifest currently ships (grammar-alignment tracked as FS.GG.Rendering#77). The `fs-gg-project` seam (ADR-0017 §C2) is recorded honestly: `materializes-when: lifecycle == spec-kit` + `supplied-by: {spec-kit: fs-gg-rendering, sdd: none}` — under the `sdd` lane it is legitimately unsupplied (condition false), not a blanket-tolerated gap. **15 process skills** (owner `fs-gg-sdd`, `materializes-when: always`) are **PROVISIONAL**: their `sha256` is computed from SDD's **canonical** `.claude/skills/fs-gg-sdd-*/SKILL.md` bodies (the bytes `SeededSkills.fs` seeds — which deliberately excludes the product-internal `fs-gg-sdd-project`), pending SDD emitting its own producer manifest (spec `057-skill-manifest-contract`, Draft; FS.GG.Contracts types via SDD#60). Every predicate parses + evaluates under the gate evaluator (`scripts/skill-union-assert.sh`), and a `profile=game, lifecycle=sdd, feedback=false` scaffold reproduces the ADR-0017 investigation exactly: 8 of 12 product skills materialize, the other 4 (`fs-gg-testing`, `fs-gg-samples`, `fs-gg-feedback-capture`, `fs-gg-project`) are justified off-lane absences. The typed `Fsgg.Registry` validator + the enforcing flip (`skill-registry-published` → coherent) are P2-blocked; see `dependencies.yml`.
