# Code quality & architecture review — FS-GG/.github

- **Date:** 2026-07-02 (~12:20 UTC), at commit `ab6d928` (main)
- **Scope:** the whole repo — scripts/ (4 shell scripts), .github/workflows/ (6), tests/skill-union/, registry/dependencies.yml + docs/registry/compatibility.md, dist/dotnet/ shared build config, renovate.json + default.json, the vendored .claude//.agents/ skills, the issue template, and the ~60-file docs/ corpus (architecture, ADRs, coordination, consumer, planning history).
- **Method:** direct read of all executable surfaces (scripts, workflows, fixtures, props) plus three parallel deep-read passes (registry+projection, docs architecture, config/skills/dist), cross-checked against the live registry state (fsgg-contracts 1.4.0, fs-gg-ui-template 0.1.61-preview.1, ADR-0014 P4 complete).

---

## 1. Executive summary

This is a disciplined, unusually well-narrated coordination repo. The reusable-gate pattern (one script in `scripts/`, one thin `workflow_call` wrapper, one self-test fixture — used identically by contract-coherence, dispatch-sender, and skill-union-assert) is genuinely good architecture, and the skill-union slice (script + fixture + workflow + doc) is fully consistent post-#120. `docs/architecture.md` is current with the registry to within one stale literal, and a programmatic link scan across all docs found **zero broken relative links**.

The problems are almost all instances of four systemic themes:

1. **Prose outweighs data, and only data is gated.** Of the 112 KB registry, roughly 3–5% is machine-meaningful; exactly **one scalar** (`fsgg-contracts.version`) is asserted against external reality. Everything else — `package-version`, `coherent:` flags, the entire compatibility.md projection, every comment — is convention-maintained, and the drift found (missing projection row, self-contradicting entries, falsified "dormant" docs) is exactly what that permits. The org's own philosophy is "gates, not discipline," but most of its own artifacts run on discipline.
2. **N-copies drift.** The quickstart exists in three places and two have already drifted on the feed story; `0.1.61-preview.1` appears ~20 times across two files; the repo list is duplicated between the registry and `apply-labels.sh`.
3. **Guards that look armed but aren't.** The coherence gate validates with a CLI pinned three minors behind current; the shared build config's `FsggApiGate` opt-in cannot fire via its own documented adoption path (MSBuild evaluation-order bug); PublicApiAnalyzers is a silent no-op on the org's F# projects.
4. **The repo doesn't fully dogfood its own conventions.** Its skill roots are 2-of-3 and unguarded by any CI; its `renovate.json` doesn't extend its own org preset.

Severity counts: **5 high, 13 medium, ~15 low**. Nothing found is an active correctness bug in a gate that currently runs (the FsggApiGate bug is in an opt-in path no adopter has exercised yet, which is itself why it survived).

---

## 2. High-severity findings

### H1. `dist/dotnet/Directory.Build.props:55–66` — FsggApiGate can never fire via its documented adoption path
The file instructs adopters to set `FsggApiGate` in `Directory.Build.local.props`, but that import is the **last** element (line 81) and PropertyGroup conditions evaluate top-to-bottom — so when the gate's PropertyGroup (line 56) is evaluated, `$(FsggApiGate)` is still empty: `EnablePackageValidation` and the RS00xx `WarningsNotAsErrors` demotion are skipped. ItemGroups evaluate after properties, so the PublicApiAnalyzers `PackageReference` **is** injected. Net effect for a repo enabling `advisory` in local.props: analyzer without demotion → under `TreatWarningsAsErrors` the RS00xx diagnostics break the build — the exact failure the header promises cannot happen ("adoption never breaks a build") — and `required` mode never gets `EnablePackageValidation`. It only works if `FsggApiGate` arrives as an env var. **Fix:** move the gate PropertyGroup below the local.props import (or make it target-time).

### H2. `.github/workflows/contract-coherence.yml:96` — the typed validator is frozen at 0.2.1
The gate installs `FS.GG.SDD.Cli --version 0.2.1` while the registry records 0.5.0 as current. The registry schema has since grown (`minimum-fsgg-sdd`, `package-tag`, `profiles`, skill-manifest-era fields), so additive-tolerance in a three-minors-old validator is doing all the work — the "typed validator" gate progressively degrades toward a YAML-parses check. No comment or issue tracks advancing the pin. **Fix:** advance to 0.5.0 and add a registry/coherence note (or a Renovate rule) that couples the pin to CLI releases.

### H3. Registry `fsgg-contracts` entry contradicts itself in three places (`registry/dependencies.yml:27–64`)
`version: "1.4.0"` (line 27), but the `surface` block still says `Fsgg.ContractVersion (= 1.2.0)` (line 41) and omits `Fsgg.SkillMirror`; the `notes` block asserts in present tense "package-version == version == feed(newest) == SDD source == **1.1.0**" (lines 46–51) against `package-version: "1.4.0"` (line 28); and lines 30–34 still say "published to a local folder feed until the H4 feed exists" while line 61 in the same entry says the feed has been live since 2026-06-28. Only the `version` scalar is gate-coupled; every prose claim drifts red-free.

### H4. compatibility.md projection is missing a coherence row and contradicts itself
`governance-cli-handoff-consumer-published` (registry:613–642, `coherent: true`) has **no row** in the compatibility.md coherence table (13 of 14 ids projected). This is a recurring class — the registry's own changelog records that `agent-skill-mirror` "was never projected" and had to be back-filled. Also: compatibility.md:25 (scaffold-provenance) still says contract 1.3.0 is "unpublished until the P1 release" while the adjacent row records 1.4.0 published; the projection's fs-gg-ui-template coherence row stops at 0.1.60 while its contract row says 0.1.61 (and the registry's own coherence row stops at 0.1.56 — three different "currents" across the two files). No gate checks the projection.

### H5. Consumer-facing docs still teach the retired org-feed mechanism
ADR-0012/0013 made all packages resolve from public nuget.org, and `docs/consumer/getting-started.md` was updated — but three high-visibility copies were not:
- `profile/README.md:106–108` (the **org landing page**): "add nuget.pkg.github.com/FS-GG to NuGet.config if restore can't find them";
- `docs/TestSpecTutorial.md:198–202` (the recommended first-run path from consumer/index.md): same retired instruction;
- `docs/consumer/versioning-and-updates.md:21` and `which-products.md:27–29`: still describe "reference the projects / `dotnet pack` to a local feed" as Rendering's install path — versioning-and-updates contradicts itself four lines later.

Related (same class): `docs/coordination/auto-update-fabric.md:19–36` and `docs/coordination/README.md:123–124` still declare the dispatch/Renovate fabric "**dormant until #21**" although the registry records #21 done, the dispatch smoke-tested, and both producer halves fired in production. The only remaining `coherent: false` reason (no green FS.GG.* Renovate sweep) is not what the doc says.

---

## 3. Medium-severity findings

### Scripts & tests
- **M1. `scripts/sync-build-config.sh:94–109` — data-loss edge in `--adopt`.** If a hand-authored `Directory.Build.props` exists **and** its `.local.props` already exists, the script prints "skip adopt (exists)" but then falls through to `cp`, overwriting the hand-authored file which was never renamed anywhere — its content is silently lost. `--adopt` should skip the copy (or hard-fail) in that branch.
- **M2. `tests/skill-union/run.sh:37–46` — `expect_fail` accepts any non-zero exit.** A misconfiguration `die` (exit 2 — e.g. a fixture path typo making a root absent) counts as an expected failure even though the intended violation class was never exercised; the `grep '::error::\['` is display-only. Assert the specific class (grep the expected `[partitioned]`/`[divergent]`/`[dangling]`/`[drifted]` tag, and/or distinguish exit 1 vs 2).
- **M3. Workflow-input interpolation inconsistency.** `skill-union-assert.yml:70–77` and `contract-coherence.yml:167` interpolate `${{ inputs.* }}` directly into `run:` scripts, while `architecture-map.yml:32–37` deliberately env-passes with an explicit injection comment. `workflow_call` inputs come from trusted caller workflows, so exposure is low, but the org's own stated pattern is env-passing — apply it uniformly.

### Registry & projection
- **M4. The `updated:` mega-line (`registry/dependencies.yml:6`).** The value is `"2026-07-02"`; everything after `#` is a single **~42 KB YAML comment** (≈37% of the file) — a 26-entry reverse-chronological changelog joined by `" | "`, mostly undated, with inconsistent entry markers. It is invisible to the validator, rewrites wholesale on every update (unreviewable diffs; guaranteed merge conflicts for the parallel sessions this org runs), and duplicates git history/PR titles. The entries follow a loose `HEADER (owner; refs): body` grammar, so mechanical conversion to a structured `changelog:` list or `registry/CHANGELOG.md` is feasible.
- **M5. Schema inconsistency across registry entries.** `range` on 2/11 contracts, `package-version` on 3, `notes` on 1; one-off fields (`package-tag`, `bundles`, `docs`, `minimum-fsgg-sdd`, `root-buildable`, `profiles`, `behavior-break`); `version` means *framework pin* for fs-gg-ui-template but *source contract version* for fsgg-contracts. Coherence rows similarly ad hoc (`enforcement` on 2, `unified_by` on 1; `registry-validator-typed` has neither `resolved_by` nor `tracking`). A validator over this shape must treat nearly every field as optional, capping what the typed gate can check.
- **M6. Stale present-tense prose in coherence rows.** `cross-repo-auto-update.impact` (registry:606–608) still says 19 packages / Contracts 1.0.1 / 0.1.52; `nuget-org-published` (:736–739) claims "current" versions Contracts 1.2.0 / SDD.Cli 0.4.0 / 0.1.58 — all superseded within the same file. Both are projected verbatim into compatibility.md.

### Build config & automation
- **M7. `dist/dotnet/Directory.Build.props:69–77` — PublicApiAnalyzers is a Roslyn (C#/VB) analyzer and does not run under the F# compiler.** The org is F#-centric, so the RS0016–RS0041 / PublicAPI.Shipped.txt half of the api-breaking-change gate is a silent no-op on the very libraries it names (FS.GG.Contracts, FS.GG.UI.*); only the `EnablePackageValidation`/ApiCompat half is effective. An adopter following the staged advisory→required instructions gets no analyzer signal and no explanation. (The registry's own #20 note records this correction — the props never caught up.)
- **M8. `default.json:62–64` — annotation-manager regex fragility.** (a) It permits exactly one line between the `renovate: datasource=… depName=…` annotation and the value line, while the description says "line(s)"; a blank or second comment line silently kills the match. (b) The lazy `[^\n]*?` before `(?<currentValue>\d[A-Za-z0-9.+-]*)` captures the **first digit-run on the value line** — a `sha256:` key or `net8.0` earlier on the line would be pinned as the "version" and Renovate would update garbage. Anchor the value capture to a delimiter.
- **M9. `renovate.json:3–5` — the repo doesn't extend its own preset.** Only `config:recommended`; none of `github>FS-GG/.github`'s rules (dashboard, semantic commits, FS.GG.* routing/grouping) apply to this repo, with no comment explaining the asymmetry. Impact is low today (no FS.GG.* PackageReferences here) but the dist/dotnet pins are managed under vanilla semantics, inconsistent with the org story.

### Skills & docs structure
- **M10. Skill roots: 2-of-3 and unguarded.** `.claude/skills` ≡ `.agents/skills` (byte-identical, verified) but `.codex/skills` doesn't exist, so running the repo's own assertion with its own defaults against itself dies on "configured root is absent". ADR-0011/0014 scope the invariant to scaffolded products, so this is arguably out of scope — but **no CI asserts even the two vendored roots** (the selftest only runs synthetic fixtures); today's byte-identity holds by manual discipline. Cheap fix: a CI step running `skill-union-assert.sh --product . --roots ".claude/skills .agents/skills"`.
- **M11. ADR housekeeping.** ADR-0003's status is "Proposed" in the README index but "Accepted" in the file; the withdrawn ADR-0010 leaves a numbering gap with no tombstone (file deleted in PR #100 — the README says "number sequentially" and then jumps 0009→0011); ADR-0011 carries no "implementation superseded by ADR-0014" marker despite ADR-0014:6 saying "where the two disagree, 0014 wins" (contrast 0012/0013, which do this correctly).
- **M12. `docs/index.md` presents executed plans as current, and indexes none of the living strata.** The ten 2026-06-2x planning docs (implementation-plan, transition-and-boundaries, etc.) are written in future tense about work that has since shipped, with no Status/historical banner; meanwhile docs/adr/, docs/coordination/, docs/build/, docs/registry/compatibility.md, and docs/reports/ are reachable only via deep links or not indexed at all (the 2026-06-30 topologies analysis is effectively an orphan). A newcomer cannot tell record from plan from instruction.
- **M13. `docs/consumer/who-drives-the-lifecycle.md:82–84`** still describes the pre-ADR-0011 two-root skill model (`.claude/` + `.agents/`, no `.codex/`, no byte-identical-union framing).

---

## 4. Low-severity findings (abridged)

- `skill-union-assert.sh`: `--digest` is handled (line 59) but absent from the usage block the `--help` prints; `sed -n '2,44p' "$0"` help is brittle to header edits; `is_co_tenant`/`MANIFEST_IDS` iterate unquoted space-joined lists (fine for real ids, fragile in principle); declared-absent count checks root[0] only (behaviorally fine — check 1 partitions first — but not literally "absent everywhere" as the doc phrases it).
- `new-sdd-fullstack.sh`: no preflight that `fsgg-sdd` exists on PATH; `dotnet new install` errors discarded to /dev/null then guessed at ("feed not reachable?").
- `apply-labels.sh`: repo list duplicates the registry's `repos:` section.
- `sync-build-config.sh`: validates XML for `.props` but never validates `dotnet-tools.json` as JSON; `MARKER` grep treats the marker as a regex (harmless today).
- **No LICENSE file** in the repo (public org repo distributing build config and scripts).
- `Directory.Build.props:34`: `RestoreLockedMode` is fail-open when `packages.lock.json` is absent in CI — documented as bootstrap convenience, but a deleted lockfile silently disables the gate; a warning when `GITHUB_ACTIONS && !Exists(...)` would close it.
- `Directory.Packages.props:9`: comment cites the wrong NuGet codes (duplicate `PackageVersion` is NU1506, not NU1504/NU1011).
- `default.json`: `fileMatch` is the pre-Renovate-40 name (auto-migrated with deprecation noise); `"\\.props$"` subsumes the two `Directory\.Build.*` patterns (dead weight). The FsGgUiVersion manager regexes themselves are correct.
- `.github/ISSUE_TEMPLATE/cross-repo-request.yml:35`: example contract id `fs-skia-ui-version` predates the ADR-0003 rename (`fs-gg-ui-version`). Labels wiring matches `apply-labels.sh` exactly.
- `.claude/skills/spectre-console/SKILL.md`: vendored verbatim from Governance — says "this repo" and cites `src/FS.GG.Governance.HumanRender/*` paths that don't exist here (unavoidable under byte-identical mirroring, but dangling for readers).
- `registry:301` says "the authoritative current pin value is the row summary above" — that summary still says 0.1.50 (contract entry: 0.1.58). `registry:147–154` still frames the orchestrator-axis flip as pending Templates#47/#43 (superseded by #49/#51, already re-cohered). compatibility.md has an orphaned paragraph (lines 69–73) referring to a row 12 rows earlier; `fs-gg-ui-template` is both a contract id and a coherence id, making anchors ambiguous.
- `docs/architecture.md:199`: the one stale literal — `ContractVersion "1.2.0"` (should be 1.4.0; line 388 of the same file already says ≥ 1.4.0).
- `docs/coordination/auto-update-fabric.md:51`: illustrative pin `0.1.50-preview.1`, several releases old.

---

## 5. What's good (worth preserving deliberately)

- **The reusable-gate pattern.** Script + `workflow_call` wrapper + self-test fixture, replicated three times with identical anatomy. Low coupling, one authority, callers wire one `uses:` block. The skill-union slice (211-line script, 11-case fixture proving each failure class, doc, workflow) is the best artifact in the repo and fully consistent with the shipped producer semantics post-#120.
- **Fail-closed dormancy.** dispatch-sender was authored before its App credentials existed and fails loud with a pointer to the provisioning issue — the right way to stage infrastructure.
- **Publish-before-flip discipline** (FR-007) is applied consistently through the registry history; version flips are traceable to green release runs.
- **Injection awareness where it matters most**: architecture-map passes the attacker-controlled PR body via env with an explicit comment; the label/body opt-out escape hatch is a well-judged "loud but never in the way" gate.
- **`coherence.yml`'s `github-ref: ${{ github.sha }}` self-gate** — validating the commit under test rather than stale main, with the bootstrap rationale documented, while consumers keep the `main` default.
- **sync-build-config's marker/adopt model** — byte-exact drift check, hand-authored-file protection, and the #29 XML well-formedness guard at both the script and the gate.
- **Zero broken relative links** across ~60 docs, and `docs/architecture.md` genuinely reconciled to a registry state that changed three times in the last 48 hours.

---

## 6. Recommendations, prioritized

1. **Fix the two real code bugs:** the FsggApiGate evaluation-order bug (H1 — move the PropertyGroup below the local.props import) and the `--adopt` overwrite edge (M1). Both are small, testable diffs.
2. **Advance the validator pin** (H2) to CLI 0.5.0 and couple it: either a Renovate annotation (the preset's own annotation manager can manage it once M8 is fixed) or a coherence row asserting gate-pin == published CLI.
3. **Gate the projection.** A ~20-line CI step can assert every registry coherence id has a compatibility.md row and that contract-row version literals match (H4). This converts the recurring "never projected" class into a red check.
4. **Restructure the changelog** (M4): freeze the `updated:` comment, move entries to `registry/CHANGELOG.md` (or a structured `changelog:` list), and make the protocol "prepend one dated entry" — restores reviewable diffs and kills the parallel-session merge-conflict magnet.
5. **One feed-story sweep** (H5): profile/README, TestSpecTutorial, versioning-and-updates, which-products, auto-update-fabric "dormant" banner, coordination/README — all to the nuget.org/post-#21 reality. Then reduce the quickstart to one canonical copy (consumer/getting-started.md) that the others link.
6. **Registry hygiene pass** (H3, M5, M6): correct the fsgg-contracts surface/notes prose, refresh or date-stamp stale `impact` prose, and document (or normalize) the per-entry field vocabulary so the typed validator can tighten.
7. **Dogfood**: run skill-union-assert on this repo's own two roots in CI (M10); extend the org preset in renovate.json or comment why not (M9); add a LICENSE.
8. **Docs signposting** (M11–M13): status banners on the stratum-4 planning docs, an index that lists the living doc sets, ADR-0003 status fix, ADR-0010 tombstone, ADR-0011 "amended by 0014" marker.
9. **Consider what H2+M5 imply together:** the typed-validator investment only pays off if the schema is versioned and the pin advances with it. A `registry-schema` contract entry (owner: sdd, consumer: .github's own gate) would make the registry's schema itself a governed contract — the one contract in the system that currently isn't.

---

*Review artifacts: three parallel deep-read passes over registry/projection, docs corpus, and config/skills/dist, plus direct review of all scripts, workflows, and fixtures. Findings verified against file contents at commit `ab6d928`.*
