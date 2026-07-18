# ADR-0026: Committed compact ship verdict — the merge-boundary answer survives in git history (extends 0018)

- **Status:** Accepted (ratified 2026-07-18) — ratified retroactively; it **shipped**, and was still
  marked `Proposed` while the org depended on it. FS.GG.SDD landed the decision in
  [#186](https://github.com/FS-GG/FS.GG.SDD/pull/186) (`feat(092): fsgg-sdd ship — committed compact
  ship verdict`, merged 2026-07-08, closing [#177](https://github.com/FS-GG/FS.GG.SDD/issues/177)) and
  published it in **v0.9.0**; four later PRs build on it (#199 `refresh`, #399 evidence, **#422** the
  ADR-0035 fail-closed verify leg). The **Depends on** hazard below is discharged: ADR-0035 is Accepted
  and its unobserved-pass-fails-closed leg landed (#422, 2026-07-14). The `readiness/*/` → `readiness/*/*`
  pattern fix this ADR records is live in SDD's `init` seed and every repo's adoption path — so ADR-0018's
  amendment banner describes released code, not an unratified claim ([#1157](https://github.com/FS-GG/.github/issues/1157)).
- **Date:** 2026-07-08 (proposed) · 2026-07-18 (accepted)
- **Affects:** **FS.GG.SDD** (owner — emit `ship-verdict.json`; amend the `init` `.gitignore` seed + its drift guard; extend the catalog-derived taxonomy doc), **Rendering / Governance / Templates / Game / Audio** (adopt the amended fragment; no cleanup, the change is additive), **.github** (this ADR)
- **Depends on:** [ADR-0035](0035-observed-run-receipts.md) (Accepted 2026-07-14) — **this ADR MUST NOT
  LAND ALONE.** ADR-0035 §"Interaction with ADR-0026" states the hazard directly: *"A verdict that
  certifies unverifiable claims is worse once it is permanent. **Landing 0026 without this decision
  durably records green verdicts that mean nothing.**"* Committing the merge-boundary answer to git
  history is only worth doing if the obligations it certifies were **observed** — satisfied by a run
  SDD *read*, not by a `pass` an agent *typed*. The compaction below is sound (`sourcesDigest` binds
  the verdict to its authored inputs); what ADR-0035 supplies is that the inputs were *verified* at
  all. Sequence 0035 first, or the durability this ADR buys is durability for a lie.

## Context

[ADR-0018](0018-transient-durable-sdd-artifact-taxonomy.md) split SDD lifecycle artifacts into
**durable** (authored sources — commit) and **regenerable** (per-run generated views — ignore
by directory *role*, never a per-feature re-inclusion list). It shipped: `fsgg-sdd init` seeds a
no-clobber `.gitignore` whose regenerable rule is `readiness/*/`, pinned byte-exactly by a drift
guard, and projected into SDD's `docs/reference/artifact-taxonomy.md`. It retired Rendering's
98-exception whack-a-mole and its 2,053 committed readiness files.

That decision was right, and it has one edge it did not weigh. The FS.GG.Audio greenfield
bootstrap ([#235](https://github.com/FS-GG/.github/issues/235), epic; Audio feedback **§3.7**)
found:

> `readiness/**` (incl. `verify.json`, `ship.json`, `governance-handoff.json`) is `.gitignore`d,
> so the **`shipReady` verdict at a given commit is not in history**. "Was `003` ship-ready when
> it merged?" is answerable only by re-running the tool, not by reading git.

`ship.json` is SDD's **merge-boundary** artifact — the one generated view whose value is tied to
a specific commit. Every other `readiness/<id>/` view answers *"what is true now?"*, which
regeneration answers perfectly. The ship verdict answers *"what was true at merge?"*, which
regeneration **cannot** answer: re-running `fsgg-sdd ship` on today's tree reports today's
disposition, not the one the merge was made on. Re-running it against a historical checkout
requires the era's authored sources *and* an era-compatible CLI. For a merge-boundary artifact
this is a genuine audit gap.

The naive fix — un-ignore `readiness/<id>/ship.json` — reintroduces exactly what 0018 cured.
A real `ship.json` is **279 lines / ~7 KB**, of which **~39%** is a `sources[]` array of per-file
SHA-256 digests and a further **~21%** the `evidenceDispositions` / `generatedViews` /
`governanceCompatibility` inventories — **~59% pure inventory**. Committing that per work item is a
footprint of the same order as the 35.4k lines 0018 removed. The audit question does not need it:
it needs the verdict, and proof of *which inputs* produced it.

## Decision

Introduce the class 0018 left implicit — **durable generated**: a compact, byte-stable,
drift-guarded generated artifact that **is** committed. This is not new machinery; SDD already
ships two (`docs/release/release-readiness.json`, `.agents/skills/skill-manifest.json`).
Exactly one lifecycle view joins them.

1. **SDD emits a compact ship verdict.** `fsgg-sdd ship` additionally writes
   `readiness/<id>/ship-verdict.json` — a projection of `ship.json` carrying only the
   commit-bound answer, targeted at **≤ 20 lines**:

   - `schemaVersion` *(Stable)*, `workId`, `stage`, `status` / `readiness`
   - `disposition.state` and `disposition.blockingFindingIds`
   - `verificationReadiness.status`
   - `generator` (the CLI/artifacts version that produced the verdict)
   - **`sourcesDigest`** — one aggregate SHA-256 over the canonical `sources[]` digest list

   `sourcesDigest` is what makes the compaction sound: it binds the verdict to the exact
   authored inputs without carrying their inventory, so a later reader can prove the committed
   verdict corresponds to the committed sources. It adds and drops no *facts* relative to
   `ship.json`; it drops *inventory*. `ship.json` remains regenerable and ignored.

   **Scope: the ship verdict only.** Audio §3.7 names `verify.json` and `governance-handoff.json`
   alongside `ship.json`, and this ADR deliberately commits neither. `verify.json` answers *"what
   is true now?"* — regeneration answers it exactly, so it carries no commit-bound fact to
   preserve. `governance-handoff.json` is the *optional, downstream* artifact of a boundary
   FS.GG.Governance owns (effective evidence freshness and gate enforcement are Governance
   concerns, not SDD's); committing an SDD-side copy of it would put a second source of truth for a
   Governance verdict into SDD's history. Revisit only if a concrete audit question needs them.

2. **The `.gitignore` fragment gains one role-based exception — not a re-inclusion list.**
   0018 §2's prohibition is on *per-feature* exceptions (`!specs/<feature>/readiness/**`, once
   per feature, forever). One rule keyed on the artifact's **role**, constant in the number of
   work items, does not reintroduce it. The regenerable stanza becomes:

   ```gitignore
   # Per-work-item readiness views are regenerable — ignore by role (ADR-0018).
   # Exception: the compact merge-boundary verdict is durable-generated (ADR-0026).
   readiness/*/*
   !readiness/*/ship-verdict.json
   ```

   **The `readiness/*/` → `readiness/*/*` change is load-bearing, not cosmetic.** Git cannot
   re-include a file whose parent directory is excluded — it never descends into an excluded
   directory, so a `!readiness/*/ship-verdict.json` negation under the *directory* pattern
   `readiness/*/` is **silently inert** and the verdict stays ignored. Excluding the directory's
   *contents* (`readiness/*/*`) keeps the parent traversable so the negation can fire. Nested
   views (`agent-commands/<target>/…`) remain ignored: `readiness/*/*` matches the
   `agent-commands` directory itself, which git then does not descend into. Verified against git
   before this ADR was written; the SDD implementation must carry a test that a scaffolded
   workspace commits `ship-verdict.json` and **nothing else** under `readiness/<id>/`.

3. **Cardinality is bounded and stated.** Exactly **one** committed file per work item (~15
   lines), against the ~12 generated files per `readiness/<id>/` (6 top-level views + 3 per agent
   target, per the release catalog). Rendering's 98 work items would
   carry ~1.5k committed lines instead of the 35.4k that 0018 removed — the audit trail at ~4%
   of the footprint that motivated 0018.

4. **It is a first-class generated view, not a hand-kept summary.** `ship-verdict.json` is
   produced only by `fsgg-sdd ship`, regenerated by `fsgg-sdd refresh`, byte-stable (FR-008:
   no clock, path, or ANSI), listed in `docs/release/release-readiness.json` with
   `sourceArtifact.kind: generatedView`, and — because the taxonomy doc derives its regenerable
   list from that catalog by drift guard — the catalog entry must carry the marker that places it
   in the **durable** table instead. SDD owns that mechanism choice (an explicit
   `durableGenerated` flag on the catalog entry is the obvious shape); this ADR fixes the
   *requirement*: the taxonomy doc stays catalog-derived and must not become a hand-maintained
   second source of truth.

## Consequences

- **FS.GG.SDD (producer, head of chain).** A feature delivering: the `ship-verdict.json` view +
  its schema/catalog entry; the amended `init` `.gitignore` seed (whole-file, no-clobber,
  `AgentGuidanceTarget`) and its **byte-exact drift-guard update**; the taxonomy-doc durable-table
  entry and its drift guard; and the negation test from §2. Tracked as the child issue of #235.
- **The seed text change touches every consuming repo's adoption path.** The seed is no-clobber,
  so existing repos are *not* rewritten on `fsgg-sdd init` re-run — they adopt the amended
  fragment by hand from `docs/reference/artifact-taxonomy.md`, exactly as 0018 prescribed.
  Until a repo adopts it, its verdicts stay ignored: the change is **additive and degrades to
  0018's behavior**, so no repo breaks by not adopting.
- **The producer repo's own adoption is a different shape, and is not skippable.** FS.GG.SDD
  dogfoods through Spec Kit, so its readiness views land at `specs/<id>/readiness/<work-id>/`, and
  its `.gitignore` carries `specs/*/readiness/` — the *directory* pattern, so the §2 inert-negation
  trap applies there verbatim (`specs/*/readiness/*/*` + `!specs/*/readiness/*/ship-verdict.json`).
  Its root `readiness/` additionally holds hand-pinned durable proofs that must stay committed and
  untouched. The seeded fragment and the producer's own rule are therefore **two adoptions of one
  decision**, not one file; #177 must land both or SDD will not dogfood the artifact it ships.
- **`fsgg-sdd doctor`** gains a natural (later, optional) check: a workspace whose `.gitignore`
  still carries the 0018-era `readiness/*/` rule is drifted from the coherent set. Not required
  by this ADR.
- **Governance's audit record is untouched.** Its committed evidence is the record of record and
  was never in scope for 0018's sweep; this ADR only *adds* a committed artifact. Governance adopts
  the amended fragment as an SDD consumer like any other repo, and its `governance-handoff.json`
  boundary is explicitly out of scope (Decision §1).
- **The audit question becomes answerable from git alone.** `git log -p -- readiness/003-*/ship-verdict.json`
  answers "was `003` ship-ready when it merged, and over which inputs?" with no CLI, no network,
  and no era-compatible toolchain.
- **Not a governed cross-repo contract.** `ship-verdict.json` is an SDD-internal generated view
  like its siblings (`AdditiveOptional`, no `contractVersion`); it is not registered in
  `registry/dependencies.yml` and crosses no repo boundary. Only `governance-handoff.json` carries
  a cross-repo `contractVersion`.
- **Supersedes nothing.** 0018's taxonomy, role-based rule, and per-feature-exception prohibition
  all stand; this ADR extends it with the one class the merge boundary needs — the same way
  ADR-0014 extended 0011 and ADR-0017 extended 0014.

<!-- No change to repos, boundaries, coherent-set axes, or the §5 contract picture: this adds a
committed generated view inside an existing repo-local artifact tree and registers no contract.
docs/architecture.md needs no reconciliation. -->
