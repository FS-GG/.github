# FS.GG.Drivers

The `.github`-authored **driver** skill bytes (`scope: driver`) as one versioned package.

`.github` authors a product-materialized skill class (ADR-0054) — a skill that runs inside a
**scaffolded product tree**, not in `.github` itself. The first is
[`work-roadmap`](https://github.com/FS-GG/.github/tree/main/.claude/skills/work-roadmap)
(ADR-0053/#1224). This package is how those bytes reach a scaffold.

## Why a package

`fsgg-sdd scaffold` runs on an **offline** inner loop, and generic SDD is contractually barred from
embedding any cross-repo path or source (`FS.GG.SDD/CLAUDE.md`, scaffold FR-002/SC-005). So the SDD CLI
cannot reach into `.github` at scaffold time. Instead:

1. `.github` **publishes** the driver bytes + `driver-skill-manifest.json` as this versioned package
   (ADR-0054 §Byte-transport, resolving [#1300](https://github.com/FS-GG/.github/issues/1300)).
2. `FS.GG.SDD.Cli` **pins** it and **restores** it at CLI build/publish time — online.
3. At **scaffold time** — offline — the CLI **materializes** the driver into the product tree's skill
   roots from the bytes it already carries, verifying each against the manifest `sha256`
   ([ADR-0014](https://github.com/FS-GG/.github/blob/main/docs/adr/0014-skill-vendoring-one-manifest-one-materialize-verify.md)).

This is the [ADR-0062](https://github.com/FS-GG/.github/blob/main/docs/adr/0062-versioned-kit-package-replaces-byte-copy-sync.md)
`FS.GG.Kit` pattern, one consumer over (SDD CLI → scaffolds, not Renovate → framework repos), and the
delivery substrate [ADR-0063](https://github.com/FS-GG/.github/blob/main/docs/adr/0063-scaffold-materializer-sources-skills-from-the-owner-repo.md)
directs owner-authored skills onto.

## What it ships

```
drivers/driver-skill-manifest.json     the delivered set + whole-directory manifests
drivers/skills/<id>/<relative-path>    every file for each `scope: driver` row (e.g. work-roadmap)
build/FS.GG.Drivers.props              a consumer handle: $(FsggDriversContentDir) → the content root
```

Only `scope: driver` rows carry bytes. A `scope: operator` row
([ADR-0057](https://github.com/FS-GG/.github/blob/main/docs/adr/0057-operator-scope-a-github-authored-never-materialized-skill-class.md),
e.g. `drive-board`) is `.github`-authored but materialized **nowhere**, so it is listed in the manifest
(the emitter's single output) but its bytes are deliberately not delivered — its `materializes-when: false`
gates it out of every tree.

## Consuming it

There is **no consumer materialize target** in this package, by design: the materialize is the SDD CLI's,
at scaffold time. `build/FS.GG.Drivers.props` exposes `$(FsggDriversContentDir)` so the CLI's build can
locate the packed bytes; the CLI reads `driver-skill-manifest.json`, and for each row whose
`materializes-when` holds, lays the complete `skills/<id>/` directory into the scaffold's skill roots
and verifies its file set, bytes, and executable modes
against the recorded `sha256`. See ADR-0063 for the materializer design.

## Deriving, not restating

The delivered set lives in exactly one authored place —
[`registry/driver-skill-manifest.json`](https://github.com/FS-GG/.github/blob/main/registry/driver-skill-manifest.json),
emitted by `scripts/generate-driver-manifest` from the authored `SKILL.md` bodies (ADR-0058). `stage-drivers.py`
reads that manifest at pack time and stages exactly its `scope: driver` rows; a driver added or retired
needs no edit to this package. `verify-package.sh` proves the packed set derives from the manifest, packs
every driver member, and fails loud on a byte that does not match its recorded `sha256`.
