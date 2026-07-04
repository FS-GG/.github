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

- **2026-07-04** — RE-DIGEST `fs-gg-sdd-getting-started` (owner github; SDD#119 closing SDD#115, ADR-0017): reconciled the one process row whose canonical body changed. SDD#119 corrected a stale in-body count in `fs-gg-sdd-getting-started/SKILL.md` ("15"→"16" `fs-gg-sdd-*` process skills — the set had grown 15→16 with `fs-gg-sdd-troubleshooting` in SDD#108), regenerating SDD's emitted process manifest `.agents/skills/skill-manifest.json`. Updated the row's `sha256` (`a6ea0b8…`→`498a888…`) to match that emitted manifest — **registry = manifest = bytes** preserved. The other 15 process digests are unchanged. No `schemaVersion` bump, no set/count change (still 16), no `materializes-when` change (`always`), no coherence-state change. Projection `docs/registry/compatibility.md` needs no edit (it names the count, not per-skill digests, and the count is unchanged).

- **2026-07-04** — PROCESS ROWS RECONCILED FROM SDD's EMITTED MANIFEST — 15→16, no longer provisional (owner github; SDD#111 closing cross-repo #109, epic #163, ADR-0017): the process half is now producer-emitted, not digested-from-canonical-bodies. Reconciled all process rows against SDD's emitted process manifest `.agents/skills/skill-manifest.json` (schema `skill-manifest` v1; regenerable via `fsgg-sdd registry skill-manifest --write|--check`, drift-guarded against `SeededSkills.skillNames` + authored `SKILL.md` digests). The set grew **15→16**: **added** `fs-gg-sdd-troubleshooting` (`sha256 03c6564…`, `scope: process`, `always`; new in SDD#108), and **re-digested** four bodies changed by features 070/071 after the .github#168 provisional snapshot — `fs-gg-sdd-checklist` (`01d54e8…`→`3965baf…`), `fs-gg-sdd-clarify` (`6900858…`→`6934313…`), `fs-gg-sdd-lifecycle` (`a6f2f22…`→`628e1d5…`), `fs-gg-sdd-tasks` (`17c26ba…`→`97f7a8d…`). The other 11 process digests are unchanged (byte-verified against the emitted manifest). Each `sha256` is the same `Fsgg.SkillMirror` canonical-body digest the registry declares, so **registry = manifest = bytes** for the process half. All 16 remain `materializes-when: always` (canonical grammar). This CLEARS blocker (1) of coherence `skill-registry-published`; the enforcing flip now waits ONLY on Rendering's predicate-grammar alignment (FS.GG.Rendering#77). No `schemaVersion` bump. Projection `docs/registry/compatibility.md` (§Skill registry) + `dependencies.yml` (`github ← sdd` edge, `skill-registry` surface/validator, `skill-registry-published` note/resolved_by/tracking) updated in the same change.

- **2026-07-04** — CATALOG CREATED (`schemaVersion: 1`) — PRODUCT-COMPLETE, PROCESS-PROVISIONAL (owner github; .github#168, epic #163, ADR-0017): first cut of `registry/skills.yml`. **12 product skills** (owner `fs-gg-rendering`) reconciled verbatim from Rendering's published producer manifest `template/skill-manifest/skill-manifest.json` (Feature 238 / FS.GG.Rendering#76, closes Rendering#71) — authoritative `sha256` + emission condition; `materializes-when` recorded in the **ADR-0017 canonical grammar**, NORMALIZED from the C-style form the manifest currently ships (grammar-alignment tracked as FS.GG.Rendering#77). The `fs-gg-project` seam (ADR-0017 §C2) is recorded honestly: `materializes-when: lifecycle == spec-kit` + `supplied-by: {spec-kit: fs-gg-rendering, sdd: none}` — under the `sdd` lane it is legitimately unsupplied (condition false), not a blanket-tolerated gap. **15 process skills** (owner `fs-gg-sdd`, `materializes-when: always`) are **PROVISIONAL**: their `sha256` is computed from SDD's **canonical** `.claude/skills/fs-gg-sdd-*/SKILL.md` bodies (the bytes `SeededSkills.fs` seeds — which deliberately excludes the product-internal `fs-gg-sdd-project`), pending SDD emitting its own process producer manifest. (The `SkillManifest` contract TYPES landed via SDD#60 / spec `057-skill-manifest-contract` — types only; the process-manifest EMISSION is a separate SDD deliverable that follows P1 and is not yet authored.) Every predicate parses + evaluates under the gate evaluator (`scripts/skill-union-assert.sh`), and a `profile=game, lifecycle=sdd, feedback=false` scaffold reproduces the ADR-0017 investigation exactly: 8 of 12 product skills materialize, the other 4 (`fs-gg-testing`, `fs-gg-samples`, `fs-gg-feedback-capture`, `fs-gg-project`) are justified off-lane absences. The typed `Fsgg.Registry` validator + the enforcing flip (`skill-registry-published` → coherent) are P2-blocked; see `dependencies.yml`.
