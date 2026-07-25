# Skill context-budget and progressive-disclosure roadmap

- **Created:** 2026-07-25 22:36:27 CEST (20:36:27 UTC)
- **Owner:** `FS-GG/.github`
- **Scope:** the 11 `.github`-authored FS-GG skills and the machinery that mirrors, packages,
  materializes, validates, and discovers them
- **Governing decisions:** ADR-0011, ADR-0014, ADR-0058, ADR-0062, ADR-0065
- **Status:** implementation roadmap

## 1. Outcome

Reduce the initial Codex skill-catalog footprint and the context loaded after a skill activates,
without weakening the FS-GG three-root contract:

> Every shipped skill remains present and byte-equivalent under `.claude/skills`, `.codex/skills`,
> and `.agents/skills`. A context-budget improvement may change what a host exposes at runtime, but
> it may not delete a mirror, allow the mirrors to diverge, or make one root less capable.

The current 11 descriptions occupy 8,901 characters in one root and 17,802 characters across the two
copies this Codex environment exposes. The proposed concise descriptions total about 2,118 characters
per root, a 76% reduction before any host-side duplicate suppression. The larger structural gain is
progressive disclosure: `pnext-item` is 1,582 lines, `check-board` 918, and
`intra-repo-parallel-work` 562, while the current kit transport copies only `SKILL.md`. The roadmap
therefore treats short metadata, whole-directory transport, and thin triggered bodies as one change,
not three unrelated edits.

## 2. Findings this roadmap pays

| Class | Finding | Consequence |
|---|---|---|
| Catalog budget | The same skill names are visible from multiple synchronized roots, and descriptions contain procedure, provenance, and composition detail. | Codex shortens descriptions under its 2% skills-list budget, reducing trigger quality. |
| Metadata validity | `workBoard` and `workRoadmap` are not lowercase hyphen-case; `workBoard` also exceeds the 1,024-character description limit. | The skill set does not conform to the Agent Skills naming/metadata contract. |
| Correctness | `lane-steward` prescribes additive `widen` for a narrowing operation. | The old glue token survives, so the proposed repair cannot increase lane capacity. |
| Current truth | `publishing-and-deployment` advertises preview/local-feed behavior while its live sections say stable/no fallback, and retains a later “nuget.org wiring pending” migration section. | A triggered release skill can give mutually incompatible instructions. |
| Progressive disclosure | The distributor and digest contract treat `SKILL.md` as the unit; no skill ships `references/`, `scripts/`, or `agents/openai.yaml`. | Detailed reference material is forced into the always-loaded triggered body. |
| Portability | Several skills use Claude-specific `Agent tool`, `isolation: "worktree"`, and slash-command language even when Codex loads the same bytes. | A synchronized body is not equally executable on every intended host. |
| Validation | Existing gates test many embedded recipes but not aggregate catalog cost, trigger behavior, metadata validity, relative links, or command semantics. | All mirrors can be byte-identical and still be identically expensive or wrong. |

## 3. Design constraints

1. **Three roots remain one capability.** The mirror gate covers the complete skill directory, not
   only `SKILL.md`. A reference, script, executable bit, or `agents/openai.yaml` present in one root
   must be present and equivalent in the other two.
2. **One authored source, three generated/materialized destinations.** Never repair a mirror by hand.
   The source path may vary by skill owner, but generation and validation use one declared file list
   and one digest algorithm.
3. **Descriptions are routing metadata.** Front-load the goal and trigger boundary; move procedure,
   composed-skill detail, ADR citations, and safety explanation into the body.
4. **Triggered bodies orchestrate; references explain.** Keep the minimum workflow, stop conditions,
   and resource routing in `SKILL.md`. Load command contracts, incident rationale, examples, and
   variant-specific guidance only when that branch is reached.
5. **Deterministic truth belongs in code.** Classification, normalization, reconciliation, and state
   mutation already modeled by `fsgg-coord` must not be reimplemented in shell or `jq` inside prose.
6. **Live state is derived.** Package counts, channels, versions, feed roles, board fields, and exit
   contracts come from registries or typed engine facts. ADRs retain history; operating skills retain
   only current instructions.
7. **Runtime exposure is separate from physical mirroring.** A host may suppress a duplicate discovery
   path through supported configuration, but the three checked-in/materialized roots remain complete
   and synchronized.
8. **Catalog capacity is managed, not assumed.** A new skill must pass the aggregate effective-budget
   gate before admission. When it does not fit, consolidate it with an existing skill, shorten routing
   metadata, or make invocation explicit; never accept host-side truncation as normal operation.

## 4. Target skill shape

```text
<root>/skills/<skill>/
├── SKILL.md                 concise workflow and resource router
├── agents/
│   └── openai.yaml          display metadata, invocation policy, dependencies
├── references/
│   ├── command-contract.md  exact verbs, exit codes, and wire shapes
│   ├── operations.md        branch-specific procedures
│   └── rationale.md         non-obvious incidents that still affect decisions
└── scripts/                 only deterministic helpers not owned by fsgg-coord
```

Each skill need not contain every directory. The materializer copies the declared directory tree
verbatim to all three roots, and the digest covers paths, bytes, and executable modes. References stay
one level below `SKILL.md`; the body says exactly when to read each one.

## 5. Milestones

The milestones are ordered so correctness and catalog relief land before the larger transport
migration. Each top-level checkbox is independently reviewable and has an explicit exit condition.

- [ ] **M0 — Correct unsafe and invalid guidance**

  - Replace `lane-steward`'s narrowing recipe with `scripts/fsgg-coord set-paths`; keep `widen` only
    for additive expansion.
  - Reconcile `publishing-and-deployment` to the live stable-channel, dual-publish, no-local-fallback
    state. Move superseded nuget.org rollout text to the ADR/history corpus.
  - Correct `spectre-console` terminology: `.NET String.Length` counts UTF-16 code units, not bytes;
    distinguish code units, ANSI characters, and display cells.
  - Repair genuinely broken relative links and replace source-tree-only skill links with explicit
    skill/resource routing where the target is conditionally materialized.
  - Add focused regression checks for the `set-paths` narrowing verb and mutually exclusive publishing
    claims.
  - **Exit:** no known instruction contradicts the shipped command contract or live registry state;
    every correction is byte-identical in all three roots.

- [ ] **M1 — Make routing metadata concise, valid, and measurable**

  - Replace all 11 descriptions with 120–220-character, trigger-first descriptions. Target no more
    than 2,500 aggregate description characters per root.
  - Rename `workBoard` → `work-board` and `workRoadmap` → `work-roadmap` through a declared migration
    that updates producer manifests, registry rows/digests, links, scaffold expectations, and receiver
    materialization atomically. Do not leave permanent alias skills that double the catalog.
  - Add a validator for Agent Skills name/directory rules, description type/length, and forbidden empty
    or placeholder metadata.
  - Add a catalog-budget report that computes both per-root authored cost and effective Codex exposure
    when multiple synchronized roots are discoverable. Set a failing ceiling with explicit headroom
    for names and paths.
  - Reserve and document growth headroom, then make the same gate part of the existing skill-addition
    machinery. Every new skill must contribute its name, description, and per-discovered-root
    multiplier before manifests or registries are updated.
  - Add representative positive, indirect, incomplete, negative, and boundary trigger fixtures for
    every description.
  - **Exit:** all metadata validates; the three roots are identical; the effective catalog stays below
    the chosen ceiling without Codex shortening any FS-GG description.

- [ ] **M2 — Promote the skill directory to the transport and digest unit**

  - Extend the kit/driver manifest shape from one `SKILL.md` digest to a deterministic whole-directory
    manifest: normalized relative path, content digest, and executable-mode bit for every managed file.
  - Update `coordination-sync`, `FS.GG.Kit`, `new-sdd-workspace`, refresh/retrofit paths, and
    `skill-union-assert` to materialize and compare full skill directories across
    `.claude/skills`, `.codex/skills`, and `.agents/skills`.
  - Preserve publish-before-flip: ship readers that accept the old and new manifest shapes, publish
    them, then emit the new shape; retire the old shape only after every receiver is re-pinned.
  - Add fixtures for a missing reference, divergent reference bytes, stale executable mode, extra
    undeclared file, and one-root-only `agents/openai.yaml`.
  - Ensure generators enumerate every output so touch-set verification treats the three materialized
    trees as derived artifacts.
  - **Exit:** changing any managed file under one skill changes its declared digest; all three roots
    receive and verify the complete, byte-equivalent directory.

- [ ] **M3 — Refactor the coordination skills for progressive disclosure**

  - `check-board`: implement or expose a typed `fsgg-coord reconcile`/chore operation for mechanical
    findings; retain dry-run/apply policy and human-judgement boundaries in a short `SKILL.md`.
  - `pnext-item`: keep the end-to-end worker state machine in the body; move exit tables, REST recipes,
    filing detail, release obligations, and incident rationale into routed references. Replace any
    duplicated mechanical classification with engine commands.
  - `intra-repo-parallel-work`: keep claim/worktree/touch-set invariants in the body; move generated
    protocol facts and extended rationale into references emitted from the typed core.
  - `cross-repo-coordination`: route mailbox/board operations, contract changes, and coherent releases
    to separate references so a simple cross-repo request does not load release-train guidance.
  - `drive-board`, `work-board`, and `work-roadmap`: extract their shared host loop—fresh worker,
    bounded concurrency, ground-truth verification, and termination—into generated references while
    leaving each ledger/scope distinction explicit.
  - Keep every `SKILL.md` below 500 lines and target fewer than 5,000 triggered tokens; document any
    justified exception.
  - **Exit:** representative tasks load only the body plus the references needed for that path, and
    forward tests produce behavior equivalent to the pre-split workflows.

- [ ] **M4 — Make synchronized skills host-portable and policy-aware**

  - Replace hard-coded Claude/Codex orchestration syntax with a host-capability branch: use the
    available subagent mechanism, request isolated worktrees when supported, and preserve the distinct
    worker-id/claim invariant on every host.
  - Document explicit invocation using the host's supported skill selector instead of treating
    `/skill` as universally executable.
  - Add `agents/openai.yaml` to every applicable skill with concise UI metadata and declared
    dependencies.
  - Set `allow_implicit_invocation: false` for high-impact autonomous drivers and release trains where
    user intent should be explicit (`cut-nuget-release`, `drive-board`, `work-board`, and
    `work-roadmap`). Keep diagnostic/advisory skills implicitly matchable where safe.
  - Define the supported runtime-exposure configuration for Codex installations that discover both
    `.agents` and `.codex`. Suppression may disable a duplicate catalog entry, never remove or desync
    the mirror.
  - **Exit:** the same mirrored bytes give actionable instructions on Claude and Codex; high-impact
    workflows do not activate merely because a prompt contains a broad word such as “release” or
    “roadmap.”

- [ ] **M5 — Add semantic skill quality gates**

  - Validate frontmatter, directory names, whole-tree parity, relative resource links, executable
    modes, catalog budget, and optional metadata in one local/CI entry point.
  - Extract every documented `fsgg-coord` invocation and verify its verb/options against the engine's
    machine-readable command contract.
  - Add semantic assertions for dangerous verb pairs (`widen` versus `set-paths`, dry-run versus apply,
    report versus mutation) rather than checking strings alone.
  - Add a current-truth gate for generated publishing/board/version facts so historical ADR prose
    cannot leak back into the operating skill.
  - Forward-test the major trigger classes in fresh contexts without leaking the expected answer:
    coordination diagnosis, one worker loop, parallel fan-out, release train, markdown roadmap, and
    Spectre CI diagnosis.
  - Make the standard local test bootstrap install or clearly provision PyYAML and invoke fixture
    wrappers consistently, so a missing development dependency is not confused with a skill failure.
  - **Exit:** a duplicate/oversized description, invalid name, missing resource, semantic command
    mismatch, stale generated fact, or trigger regression fails before distribution.

- [ ] **M6 — Roll out, re-pin, and prove the budget improvement**

  - Publish the contract/materializer/Kit releases required by M2–M5 in dependency order.
  - Re-pin and re-materialize every coordination-kit receiver; regenerate producer manifests and the
    central skill registry; confirm three-root directory parity on each receiver.
  - Start fresh Claude and Codex sessions in `.github` and representative product workspaces. Record
    discovered skill names, description truncation warnings, effective metadata characters, and the
    resources loaded for representative tasks.
  - Confirm explicit-only skills remain selectable, implicit skills trigger on positive fixtures, and
    negative fixtures do not trigger.
  - Update the architecture/ADR record with the final directory-level transport and runtime-exposure
    distinction; supersede, do not erase, earlier `SKILL.md`-only assumptions.
  - **Exit:** all receivers carry the same complete three-root skill directories; Codex emits no
    FS-GG-caused description-shortening warning under the supported baseline; triggered workflows
    remain behaviorally correct.

## 6. Sequencing

```text
M0 correctness ───────► M1 metadata/budget ───────────────┐
                                                         │
M2 whole-directory transport ─► M3 progressive split ────┼─► M5 semantic gates ─► M6 rollout
                                  │                      │
                                  └─► M4 host policy ─────┘
```

- M0 and the description-only portion of M1 can land immediately under the existing `SKILL.md`
  transport.
- The two renames in M1 require manifest/registry coordination and may share the compatibility work
  with M2.
- M3 and `agents/openai.yaml` delivery in M4 require M2's whole-directory transport.
- M5 begins early with metadata and command-semantic checks, then expands as each resource class lands.
- M6 is publish-before-flip: readers/materializers first, package publication second, registry and
  receiver re-pins last.

## 7. Definition of done

1. All 11 skills have concise, trigger-first descriptions and specification-valid names.
2. The complete directory for every shipped skill is present and equivalent under all three runtime
   roots; parity covers bytes, paths, and executable modes.
3. No large skill body carries reference material that can be loaded conditionally instead.
4. Mechanical coordination truth is implemented once in the typed engine and projected into skills,
   never independently re-derived in prose.
5. High-impact autonomous skills require explicit invocation; advisory skills retain accurate implicit
   triggers.
6. CI fails on catalog-budget regressions, mirror drift, invalid metadata, broken resources, stale live
   facts, semantic command mistakes, and trigger-boundary regressions.
7. Fresh supported Codex sessions no longer shorten FS-GG descriptions, while Claude and Codex retain
   the same synchronized skill capability.
8. Adding a skill through the supported machinery either preserves the reserved catalog headroom and
   complete three-root directory transport or fails with an actionable consolidation/explicit-only
   decision; it cannot silently consume the margin or ship only `SKILL.md`.

## 8. Risks and controls

- **Tree-digest migration:** old consumers understand only the `SKILL.md` digest. Use an additive
  manifest version and publish compatible readers before flipping emitters.
- **Rename churn:** aliases would preserve compatibility but permanently spend catalog budget. Prefer a
  single coordinated rename with explicit release notes and receiver re-materialization.
- **Over-splitting:** moving a safety rule into an obscure reference can make it inert. Keep invariants
  and stop conditions in `SKILL.md`; move detail and rationale, then forward-test the split.
- **Generated-history loss:** incident rationale explains non-obvious guards. Preserve it in focused
  references or ADRs and link it from the rule it justifies.
- **Host divergence:** a Claude-only or Codex-only fix violates the shared-body premise. Test the same
  materialized bytes on both hosts before rollout.
- **False budget confidence:** measuring one root understates environments that expose two. Report
  authored per-root cost and effective discovered cost separately.
- **Unbounded catalog growth:** concise descriptions delay but do not eliminate the fixed host budget.
  Enforce admission headroom on every added skill and periodically consolidate overlapping trigger
  surfaces.

## 9. Suggested issue decomposition

| Milestone | Primary repo | Suggested issue boundary |
|---|---|---|
| M0 | `.github` | Skill correctness/current-truth repair with focused recipe gates |
| M1 | `.github` plus skill producers | Description budget, metadata validator, trigger fixtures, coordinated driver renames |
| M2 | `.github`, SDD/Contracts, Kit consumers | Directory manifest/digest contract, materializer, receiver parity |
| M3 | `.github` | One issue per large coordination skill or tightly coupled skill family |
| M4 | `.github` plus host integration owners | Host-neutral orchestration and `agents/openai.yaml` policy |
| M5 | `.github` | Unified skill-quality gate and forward-test corpus |
| M6 | `.github` plus every receiver | Release train, registry flip, re-materialization, runtime evidence |

Every implementation issue should declare the authored source paths it changes. The three runtime
mirrors are generated outputs and must be regenerated together; they are one semantic change, never
three independently owned edits.
