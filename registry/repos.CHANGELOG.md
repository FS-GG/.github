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

- **2026-07-09** — Coordination-kit `fsgg-coord` sha256 bumped (@.github; #257, ADR-0021, ADR-0027): in-flight work is now found by its **claim marker**, not the board's `In progress` column. `claim` writes that column strictly best-effort — a Projects v2 5xx is swallowed, and an item never added to the board has no column at all — so `who`/`reap`/`inbox` went blind to real claims, and `batch` never RESERVED an off-board claim's `Paths:` touch-set, which let the scheduler double-book overlapping work. `active_claims` now unions the column (the only source that can report `UNCLAIMED`) with every open issue carrying a live marker, found via a paginated, uncached issue scan pruned soundly on `comments > 0`. `reap` also stopped reporting a board reset it never performed. Re-digested client `4844a2ca…` → `220e8822…`. Propagates to `coordination-kit` receivers via the `coordination-coherence` gate.
- **2026-07-06** — New framework repo `game` added to the roster (@.github; ADR-0022): seed the roster row `{ id: game, full: FS-GG/FS.GG.Game, role: framework, receives: [labels, coordination-kit] }` for the sixth platform component — the extracted game subsystem (`FS.GG.Game.Core` BCL-only sim + `FS.GG.Game.Render` Scene adapter). `game` participates in `labels` and the `coordination-kit` distribution like the other framework repos. The repo itself is created in epic phase P2; the row lands ahead of it per the publish-before-flip sequence. Phased plan: `docs/reports/2026-07-06-extract-fs-gg-game-component-sdd-driven.md`.
- **2026-07-06** — Coordination-kit gains skill `intra-repo-parallel-work`; `fsgg-coord` sha256 bumped (@.github; ADR-0021): added the **inner-repo sibling** of `cross-repo-coordination` — a protocol for running multiple workers in parallel on different items *inside one repo* via a **claim** lock (assignee + `Status: In progress`), one **git worktree** per item, and a declared **`Paths:` touch-set** with an overlap check. New kit skill row `intra-repo-parallel-work` (`a952f74a…`); `fsgg-coord` grew `claim`/`release`/`overlap` subcommands, re-digested `b49eebc5…` → `a0c44984…`. `coordination-sync` now materializes every `kind: skill` kit row (was cross-repo-only). Propagates to all `coordination-kit` receivers via the `coordination-coherence` gate.
- **2026-07-05** — Coordination-kit `cross-repo-coordination` + `fsgg-coord` sha256 bumped (@.github; ADR-0019): added an **earned done-stamp** — a `fsgg-coord done <issue> [--pr N] [--flip]` subcommand that verifies an item is finished (closing PR merged **and** board `Status: Done`) in one thrifty query and prints a greppable green `FSGG-DONE` / red `FSGG-NOT-DONE` two-line stamp. `--flip` sets `Status: Done` after re-confirming the merge and **rolls the completion up the parent-epic chain**, flipping+stamping each epic whose children are now all `Done` (transitive, bounded). Plus a *Signal an item is finished* skill section wiring it into the release dance's "Land + record" step. Re-digested skill `30946d20…` → `dcb8dfd3…` and client `3ac2eb42…` → `b49eebc5…`. Propagates to `coordination-kit` receivers.
- **2026-07-04** — Coordination-kit `cross-repo-coordination` sha256 bumped (@.github; ADR-0003, ADR-0019): the kit `SKILL.md` carried the pre-rename contract id `fs-skia-ui-version`; corrected to `fs-gg-ui-version` (ADR-0003) in both skill roots, re-digesting the kit `460542a2…` → `9ff9ed86…`. Propagates to `coordination-kit` receivers; unblocks Governance's ADR-0003 rename guard (Governance#90).
- **2026-07-04** — Roster registry established (@.github; ADR-0019, slice 1): seed `repos.yml` with
  the five framework repos (`.github` authority + `sdd`/`rendering`/`governance`/`templates`), the
  `labels` + `coordination-kit` capabilities populated, and the coordination kit
  (`cross-repo-coordination` skill + `fsgg-coord` client) declared content-addressed. Migrated
  `scripts/apply-labels.sh` off its hardcoded array to `repos.sh list --receives labels`. The
  `build-config` / `lockfile-sync` / `contract-coherence` capabilities are reserved vocabulary,
  populated when each fabric is migrated in a follow-up PR. Kit sync + coherence gate land in slice 2.
