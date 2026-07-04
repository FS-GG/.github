# Repo roster changelog

Reverse-chronological log of changes to [`repos.yml`](repos.yml) — the FS-GG org repo roster (the
single authoritative list of framework repos the org fabrics iterate, ADR-0019). Its human
projection is [`../docs/registry/repos.md`](../docs/registry/repos.md).

**Protocol.** Mirrors [`CHANGELOG.md`](CHANGELOG.md) (the `dependencies.yml` log) and
[`skills.CHANGELOG.md`](skills.CHANGELOG.md): every change to `repos.yml` **prepends one dated entry**
at the top of the Entries list below (newest first) and sets the file's `updated:` date to match.
Entries follow the same loose `HEADER (owner; refs): body` grammar — name the repo id(s) and/or the
`receives` capability touched, the owner, and the issue/ADR refs. Adding or retiring a repo, or
changing a repo's `receives`, is a changelog-worthy roster change; bumping the kit `sha256` after a
skill/client edit is too.

`repos.yml` is validated by [`../scripts/repos.sh validate`](../scripts/repos.sh) (schema, the
single-authority invariant, the `receives` vocabulary, and content-addressed kit digests).

## Entries

- **2026-07-04** — Coordination-kit `cross-repo-coordination` sha256 bumped (@.github; ADR-0003, ADR-0019): the kit `SKILL.md` carried the pre-rename contract id `fs-skia-ui-version`; corrected to `fs-gg-ui-version` (ADR-0003) in both skill roots, re-digesting the kit `460542a2…` → `9ff9ed86…`. Propagates to `coordination-kit` receivers; unblocks Governance's ADR-0003 rename guard (Governance#90).
- **2026-07-04** — Roster registry established (@.github; ADR-0019, slice 1): seed `repos.yml` with
  the five framework repos (`.github` authority + `sdd`/`rendering`/`governance`/`templates`), the
  `labels` + `coordination-kit` capabilities populated, and the coordination kit
  (`cross-repo-coordination` skill + `fsgg-coord` client) declared content-addressed. Migrated
  `scripts/apply-labels.sh` off its hardcoded array to `repos.sh list --receives labels`. The
  `build-config` / `lockfile-sync` / `contract-coherence` capabilities are reserved vocabulary,
  populated when each fabric is migrated in a follow-up PR. Kit sync + coherence gate land in slice 2.
