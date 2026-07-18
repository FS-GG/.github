# Architecture Decision Records (cross-repo)

ADRs for decisions that span more than one FS-GG repo. Per-repo decisions live in that repo.
Use [template.md](template.md) for a new record; number sequentially.

**Four house rules**, learned the hard way:

1. **A cross-repo boundary is decided in an ORG ADR** ([ADR-0033](0033-fixed-step-double-buffer-is-a-simulation-primitive.md) §5).
   A repo-local ADR may *execute* such a decision, and must *cite* the org ADR that made it.
   (ADR-0033 exists because one boundary was decided repo-locally and an accumulator ended up split
   across two repos, where the hardened copy was not the one products used.)
2. **This index is NAVIGATION — one line per record.** The reasoning, the corrections, and the
   supersession *scope* live in the record itself, never only in the row. When corrections landed
   here instead of there, the Title column grew into a second copy of the corpus (rows reached 2,700
   characters) while readers who opened the actual record were the ones who got misled.
3. **An amendment has TWO ends.** If A supersedes or amends B, say so in **both** files — B's
   `**Status:**` banner *and* a dated note at the section that actually went stale.
   [ADR-0021](0021-parallel-intra-repo-work-claim-worktree-touchset.md) is the house form.
4. **Withdrawn numbers are retired, not reused.** A withdrawn record keeps a row, so the sequence
   stays gap-free to a reader.

All four are enforced by `adr-coherence` (`scripts/check-adr-coherence.py`) — the corpus is a
registry like any other, and this org gates its registries.

| ADR | Title | Status |
|---|---|---|
| [0001](0001-cross-repo-coordination-via-issues.md) | Cross-repo coordination via GitHub issues + a registry | Accepted |
| [0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md) | Composition by scaffold; `lifecycle` template parameter; governance populated by default | Accepted |
| [0003](0003-rename-fs-skia-ui-version-machinery-to-fs-gg-ui.md) | Rename the `fs-skia-ui` version machinery to `fs-gg-ui` (clean break) | Accepted · *executed* |
| [0004](0004-constitution-ownership-for-lifecycle-sdd-products.md) | SDD owns the `lifecycle=sdd` constitution, shipped at `.fsgg/constitution.md` | Accepted |
| [0005](0005-fsgg-slot-ownership-sdd-project-governance-governance.md) | `.fsgg/` slot ownership — SDD owns `project.yml`, Governance owns `governance.yml` | Accepted |
| [0006](0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md) | `.github` owns the org-shared .NET build config; `RestoreLockedMode` gates on `GITHUB_ACTIONS` | Accepted |
| [0007](0007-reference-gate-set-package-version-derivation.md) | `FS.GG.Governance.ReferenceGateSet` version-derivation rule | Accepted |
| [0008](0008-fsgg-sdd-cli-first-class-member-of-coherent-set.md) | The `fsgg-sdd` CLI is a first-class member of the coherent set (orchestrator axis) | Accepted |
| [0009](0009-cli-single-orchestrator-detect-and-remediate.md) | The `fsgg-sdd` CLI is the single orchestrator — detect-and-remediate, not silent auto-update | Accepted |
| ~~0010~~ | *SDD-native scaffold (inline `--provider-source`, explicit currency, config-driven governance default)* — declined ([#100](https://github.com/FS-GG/.github/pull/100)) in favour of a clone-free scaffolder working through existing machinery. Its withdrawal was **completed by [0016](0016-retire-templates-local-new-fullstack-single-scaffolder.md)**, and that scaffolder is now the `new-sdd-workspace` dotnet tool. | **Withdrawn** |
| [0011](0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md) | Every agent-skill root carries the full skill union; `fsgg-sdd` owns the mirror | Accepted |
| [0012](0012-dual-publish-to-nuget-org.md) | Dual-publish FS-GG packages to nuget.org (public) alongside the org GitHub Packages feed | Accepted |
| [0013](0013-trusted-publishing-oidc-for-nuget-org.md) | Publish to nuget.org via Trusted Publishing (OIDC), not a long-lived API key | Accepted |
| [0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) | Skill vendoring & mirroring — one manifest, one materialize-and-verify, content-addressed | Accepted |
| [0015](0015-register-the-registry-schema-as-a-governed-contract.md) | Register the registry schema as a governed contract (`registry-schema`) | Accepted |
| [0016](0016-retire-templates-local-new-fullstack-single-scaffolder.md) | Retire the Templates-local `new-fullstack.sh`; one sole scaffolder (now `new-sdd-workspace`) | Accepted |
| [0017](0017-skill-registry-condition-aware-materialization.md) | Org skill registry + condition-aware `materializes-when` on the manifest | Accepted |
| [0018](0018-transient-durable-sdd-artifact-taxonomy.md) | Transient vs durable SDD artifact taxonomy; regenerable output is gitignored by role | Accepted |
| [0019](0019-org-repo-roster-registry-and-coordination-kit.md) | Org repo roster registry (`registry/repos.yml`) + the mirrored coordination kit | Accepted |
| [0020](0020-platform-workspace-component-vocabulary.md) | Name the products precisely — **platform** (the org), **component** (one repo), **workspace** (what you scaffold) | Accepted |
| [0021](0021-parallel-intra-repo-work-claim-worktree-touchset.md) | Parallel intra-repo work — a claim lock, one git worktree per item, a declared `Paths:` touch-set | Accepted |
| [0022](0022-extract-fs-gg-game-as-an-sdd-driven-component.md) | Extract **FS.GG.Game** as the platform's sixth component (BCL-only `Game.Core`; `Scene.Geometry` Option D) | Accepted |
| [0023](0023-onboard-fs-gg-audio-as-an-sdd-driven-component.md) | Onboard **FS.GG.Audio** as the seventh component — render-independent, depends on no FS-GG component | Accepted |
| [0024](0024-wire-fs-gg-audio-into-the-game-scaffold-profile.md) | Wire FS.GG.Audio into the `game`/`sample-pack` profile on its own `$(FsGgAudioVersion)` axis; complete the extraction | Accepted |
| [0025](0025-first-class-shipped-surface-mutation-event.md) | First-class **shipped-surface mutation** — a changed `.fsi` baseline of a *published* surface is a governed event | Accepted |
| [0026](0026-committed-compact-ship-verdict.md) | **Committed compact ship verdict** — the merge-boundary answer survives in git history | Accepted |
| [0027](0027-worker-keyed-claim-lock-and-worker-channel.md) | The parallel-work lock is keyed on the **worker**, not the account — comment-order CAS, leases, a worker channel | Accepted |
| [0028](0028-keyboard-input-config-mechanism-policy-boundary.md) | Keyboard input-config boundary — **mechanism** (Rendering) vs **policy** (Game); the command id is an opaque token | Accepted |
| [0029](0029-game-owns-the-testspec-corpus.md) | The game TestSpec corpus is FS.GG.Game-owned; `.github` keeps pointer stubs | Accepted |
| [0030](0030-creation-time-scaffolding-self-updates-by-default.md) | Creation-time scaffolding self-updates the CLI by default — a bounded carve-out to 0009 | Accepted |
| [0031](0031-republished-package-is-a-named-failure.md) | ~~A silently re-published package is a NAMED failure~~ — **premise false**: `FSharp.Core` was never re-published. Surviving decisions folded into [0032](0032-the-lock-hash-must-not-depend-on-the-machine.md) §5 (cold restore) and §4 (never hand-write a `contentHash`). | **Withdrawn** |
| [0032](0032-the-lock-hash-must-not-depend-on-the-machine.md) | The lock file's `contentHash` must not depend on the machine — `FSharp.Core` resolves from nuget.org everywhere | Accepted |
| [0033](0033-fixed-step-double-buffer-is-a-simulation-primitive.md) | The fixed-step double buffer is a **simulation** primitive, owned by `FS.GG.Game.Core` — one accumulator in the org | Accepted |
| [0034](0034-typed-coordination-engine.md) | The coordination engine is a **typed core**; the tool is the model, and the docs are its projection | Accepted |
| [0035](0035-observed-run-receipts.md) | A test obligation is satisfied by a run SDD **read**, not by a `pass` an agent **typed** | Accepted |
| [0036](0036-the-build-config-drift-check-pins-its-source.md) | The shared-build-config drift check compares against a **pin**, not against `main` | Accepted |
| [0037](0037-schema-growth-is-publish-before-flip.md) | Schema growth is **publish-before-flip** — two ordered PRs; the validator gates on the declared `schemaVersion` | Accepted |
| [0038](0038-the-corpus-is-the-cut-over-gate.md) | The **defect corpus** is the cut-over gate; the shadow clock is demoted to telemetry | Accepted |
| [0039](0039-nuget-org-is-the-read-path.md) | **nuget.org is the read path; the org feed is the publish path** | Accepted |
| [0040](0040-port-the-io-layer.md) | The **IO layer is ported to F# too** — an `IGitHub` seam (HTTP adapter + call-counting fake) driven by the shell corpus through a configurable API base; each write's precondition lives in its type | Accepted |
| [0041](0041-the-chore-lock-is-the-item-cas-on-another-subject.md) | A **chore takes the item CAS, unchanged**, on a closed per-repo lock issue — `Writes.claim` is already substrate-general (comments only; the lease is a parameter; the board coupling is a callback), so the refactor #873 priced was never needed | Accepted |
| [0042](0042-the-chore-lock-ref-is-embedded-beside-the-roster.md) | The **chore-lock ref is embedded beside the roster**, keyed on owner and repo — `registry/repos.yml` is unreadable exactly where the queue has to work, because the shim ships to receivers without it | Accepted |
| [0043](0043-a-superseded-run-is-the-one-its-group-replaced.md) | A **superseded run is the one its group replaced** — the conclusion leaves the test: a re-run creates no run, so the `cancelled`-only clause never guarded the re-run-until-green it was written for, and what it kept was a metadata gate's stale failure | Accepted |
| [0044](0044-generated-artifacts-are-derived-from-their-generators.md) | **Generated artifacts are derived from their generators, not declared** — every generator answers `--list`, and `verify-paths` subtracts the whole-file ones, so the advisory stops firing on the behaviour #309 mandates; there is still only ONE declaration surface | Accepted |
| [0045](0045-machine-readable-sentinels-for-human-block-and-chore.md) | **A body-line sentinel says what an empty field cannot** — `Blocked on: human/decision\|action` refuses scheduling regardless of `Paths:` (so a decision item keeps its fix-scope and is still un-takeable, #918/#1081), and `Paths: any` splits a schedulable file-less chore from an unschedulable `Paths: none` epic; `lint` reds an unmarked `Blocked` park | Accepted |
| [0046](0046-one-exit-code-union-renumber-the-github-layer-collisions.md) | **One exit-code union, and the two GitHub-layer codes move off the verdict codes they collided with** — `ExOffboard 3→8`, `ExPartial 4→9` off `Red`/`NoVerdict`; one `FS.GG.Coord.ExitCode` with `toInt`, so a command's return set is enumerable and the generated `take`/`landable` tables are checked complete (#918) | Accepted |
| [0047](0047-client-fs-decomposition-seams-kit-digest-first.md) | **The Client.fs decomposition seams — extract the kit-digest advisory first** — the 4,557-line collision magnet (#979) gets its first `.fsi`-fronted cut, `KitDigest` (the two `repos.lock` advisories + their IO), shrinking Client.fs ~227 lines; the JSON-renderer seam is named and deferred; `declaredWarn` decouples by taking `Transport`, not `Client.Context` (#1164, #1158) | Accepted |

## Supersession map

Which record currently rules, and where a decision was amended. Every edge below is recorded in
**both** files (rule 3) — this table is a convenience, never the authority.

| Amended | § | By | What changed |
|---|---|---|---|
| [0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md) | D4 | [0004](0004-constitution-ownership-for-lifecycle-sdd-products.md) | Constitution ownership — the open P0 gate is closed: SDD owns it. |
| [0006](0006-org-shared-dotnet-build-config-and-unified-restore-locked-mode-gate.md) | D3 | [0036](0036-the-build-config-drift-check-pins-its-source.md) | `--check` compares against the receiver's committed pin, not `.github@main`. |
| [0009](0009-cli-single-orchestrator-detect-and-remediate.md) | — | [0030](0030-creation-time-scaffolding-self-updates-by-default.md) | Creation-time scaffolding self-updates; every in-project verb still does not. |
| [0011](0011-agent-skill-roots-full-union-orchestrator-owned-mirror.md) | impl | [0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) | The invariants stand; the four hand-maintained mirrors become one content-addressed materialize-and-verify. |
| [0012](0012-dual-publish-to-nuget-org.md) | §6 | [0013](0013-trusted-publishing-oidc-for-nuget-org.md) | Auth is Trusted Publishing (OIDC), not a long-lived key. |
| [0012](0012-dual-publish-to-nuget-org.md) | §1 | [0039](0039-nuget-org-is-the-read-path.md) | The **read path** moved to nuget.org; the org feed is no longer the coherence source of truth. |
| [0013](0013-trusted-publishing-oidc-for-nuget-org.md) | §5 | *(self, 2026-07-14)* | "Never a silent no-op" is scoped by `vars.NUGET_ORG_PUBLISH`: unset ⇒ the nuget.org leg is skipped silently. |
| [0014](0014-skill-vendoring-one-manifest-one-materialize-verify.md) | D1 | [0017](0017-skill-registry-condition-aware-materialization.md) | The manifest entry grows `materializes-when` / `supplied-by`; `registry/skills.yml` becomes the org catalog. |
| [0015](0015-register-the-registry-schema-as-a-governed-contract.md) | §3 | [0037](0037-schema-growth-is-publish-before-flip.md) | The "same change" procedure **cannot exist** — no PR spans two repos. Two ordered PRs instead. §1–2 stand. |
| [0018](0018-transient-durable-sdd-artifact-taxonomy.md) | — | [0026](0026-committed-compact-ship-verdict.md) | One role-based exception; the pattern becomes `readiness/*/*` (git will not descend into an excluded directory). |
| [0019](0019-org-repo-roster-registry-and-coordination-kit.md) | kit row | [0034](0034-typed-coordination-engine.md) | The `kit:` row becomes a shim over a `dotnet tool`. |
| [0021](0021-parallel-intra-repo-work-claim-worktree-touchset.md) | §1 | [0027](0027-worker-keyed-claim-lock-and-worker-channel.md) | The assignee lock was a **no-op** (N agents, one account). Comment-order CAS on `fsgg:claim` markers. Worktree + touch-set stand. |
| [0022](0022-extract-fs-gg-game-as-an-sdd-driven-component.md) | D1 | [0033](0033-fixed-step-double-buffer-is-a-simulation-primitive.md) | The double-buffered loop is simulation surface, and belongs with the accumulator in `Game.Core`. |
| [0022](0022-extract-fs-gg-game-as-an-sdd-driven-component.md) | open item | [0029](0029-game-owns-the-testspec-corpus.md) | FS.GG.Game owns the TestSpec corpus; `.github` keeps stubs. |
| [0027](0027-worker-keyed-claim-lock-and-worker-channel.md) | impl | [0034](0034-typed-coordination-engine.md) | Implementation only — **the comment-order CAS stands**. |
| [0027](0027-worker-keyed-claim-lock-and-worker-channel.md) | scheduler | [0038](0038-the-corpus-is-the-cut-over-gate.md) | **Blockers are checked before the touch-set** — a blocked item cannot start whatever its touch-set says. |
| [0031](0031-republished-package-is-a-named-failure.md) | **all** | [0032](0032-the-lock-hash-must-not-depend-on-the-machine.md) | **Withdrawn — premise false.** §1 (cold restore) and §3 (never hand-write a `contentHash`) survive, in 0032 §5 and §4. |
| [0034](0034-typed-coordination-engine.md) | §5 | [0038](0038-the-corpus-is-the-cut-over-gate.md) | The three-day shadow clock **could never tick**. The gate is the defect corpus; the shadow is telemetry. |
| [0034](0034-typed-coordination-engine.md) | §5 exit / §4.4 | [0040](0040-port-the-io-layer.md) | "Delete the bash implementation" was **not reachable** — the IO layer is ported, each write's precondition in its type; the shim's preconditions are named. |
| [0041](0041-the-chore-lock-is-the-item-cas-on-another-subject.md) | the config clause | [0042](0042-the-chore-lock-ref-is-embedded-beside-the-roster.md) | The lock's number is **not** in `registry/repos.yml` — the engine has no YAML reader, and must not: the shim ships to receivers without the roster. Embedded beside the roster instead. The substrate decision, and `absent ⇒ offer refuses`, stand. |
