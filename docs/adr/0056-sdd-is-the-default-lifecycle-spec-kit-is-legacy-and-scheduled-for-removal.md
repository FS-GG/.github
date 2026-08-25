# ADR-0056: `sdd` is the default lifecycle for every vendored product; `spec-kit` is legacy, frozen, and scheduled for removal

> **P4 additive amendment (2026-08-25, `.github#2925`):** `typed-sdd` is now a fourth explicit
> lifecycle value beside `spec-kit|sdd|none`. This does not amend the default decision below:
> omission still means `sdd`. Typed SDD preserves its exact token and uses the published
> FS.GG.SDD `1.4.0-preview.1` canonical F# backend; it is neither an alias nor a fallback. Changing
> the omitted value remains the separate P5 evidence-gated cross-repository decision.

- **Status:** Accepted
- **Date:** 2026-07-20
- **Affects:** FS.GG.Rendering (owns `fs-gg-ui` `.template.config/template.json` — the `lifecycle` symbol default and the raw-template guard); FS.GG.SDD (owns `fsgg-sdd scaffold` and its default, and the migration path); FS-GG/.github (owns `registry/dependencies.yml` `fs-gg-ui-template`, `registry/skills.yml` + the `workRoadmap` driver, this ADR, and governance); FS.GG.Game / FS.GG.Rendering (own `fs-gg-feedback-capture` + the Spec Kit `after_*` hook machinery on the removal path); FS.GG.Templates (composition/provider pins); FS.GG.Governance (constitution/overlay on the now-default lane).
- **Amends:**
  - [ADR-0002](0002-composition-by-scaffold-lifecycle-parameter-governance-populated.md) §Decision.2 — flips the `lifecycle` template-parameter **default** from `spec-kit` to `sdd`, and reclassifies the `spec-kit` lane from "byte-identical-to-today default" to legacy-and-scheduled-for-removal. ADR-0002 §Decision.1 (composition-by-scaffold) and §Decision.3 (governance populated-by-default) are unchanged. *(The reciprocal `Amended by ADR-0056` marker is added to ADR-0002 in the resolving PR — a one-sided link is the corpus's most common defect.)*
  - [ADR-0053](0053-roadmap-driven-milestone-loop-disposable-sdd-subagents.md) §6 and [ADR-0054](0054-workroadmap-delivery-fabric-a-github-authored-product-materialized-driver.md) — dissolves the `workRoadmap` **capture coupling** (see Consequences): with `fs-gg-feedback-capture` frozen and scheduled for removal (Decision 3), the driver's composed predicate collapses to `fs-gg-feedback-report`'s `always`, ADR-0053 §6's refuse-if-incomplete clause re-keys onto report, and the loop's feedback step becomes report-only. Each carries the reciprocal `Amended by: ADR-0056` marker (.github#1247).
- **Interacts with:** [ADR-0004](0004-constitution-ownership-for-lifecycle-sdd-products.md) (constitution ownership on the `sdd` lane, now the default path).

## Context

ADR-0002 introduced `lifecycle` as an `fs-gg-ui` template parameter (`spec-kit | sdd | none`) and made
**`spec-kit` the default** on the explicit grounds that it is *"byte-identical to today's output"* —
i.e. the default was chosen to preserve the pre-decomposition monolith, not because it is where the org
is going. It is not. Since ADR-0002 the platform has moved decisively to **CLI-orchestrated composition**
(`fsgg-sdd scaffold --provider rendering`; the 2026-06-27 architecture report; the retirement of the
`fs-gg-fullstack` vendored monolith), and the org's own tooling already defaults to and dogfoods the
`sdd` lane. The default now contradicts the direction.

The concrete symptom that surfaced this: a freshly scaffolded `sdd`-lane product (Rouge1, `profile: game`)
carries the **entire** SDD lifecycle skill set (`fs-gg-sdd-*`, all 16) **and** `fs-gg-feedback-report`,
yet does **not** carry `/workRoadmap` — the driver built to run exactly that lifecycle. It is absent for
one transitive reason: `workRoadmap`'s materialization predicate (ADR-0054) ANDs its composed producers,
and one of them — `fs-gg-feedback-capture` — is **Spec Kit hook machinery** (`after_*` hooks wired into
`.specify/extensions/feedback/`, per its own SKILL.md), so it is gated to `lifecycle == spec-kit` and
cannot fire off that lane. The SDD-lifecycle driver is therefore denied to the lane literally named for
the SDD lifecycle, because the org still treats the Spec Kit lane as the first-class one.

`spec-kit` is not merely another lane — it is the surface of a whole gated fabric the org is retiring:
`.specify/`, the constitution emission, the Spec Kit `after_*` hooks, `fs-gg-feedback-capture`, and the
`spec-kit`-gated `fs-gg-samples`. Keeping it the default keeps that fabric first-class and keeps every
downstream (registry predicates, the `workRoadmap` gate, docs, tests) organized around it.

## Decision

**Reverse ADR-0002 §Decision.2. `sdd` becomes the default `lifecycle` for every vendored product, and
`spec-kit` becomes a legacy lane — frozen to new investment and scheduled for removal.**

1. **Default flips on both surfaces.** The `fs-gg-ui` raw template
   (`FS.GG.Rendering/.template.config/template.json` `lifecycle.defaultValue`) flips `spec-kit → sdd`,
   and `fsgg-sdd scaffold` defaults to `sdd`. There is one default, and it is `sdd` — the raw template
   and the scaffolder no longer disagree about what "default" means.

2. **The raw-template `sdd` default is guarded, so the flip is safe for standalone consumers.** On the
   raw template, `--lifecycle sdd` emits product-only output and *expects an external SDD owner to
   re-supply the lifecycle scaffolding* (byte-identical to `none` at template level). A standalone
   `dotnet new fs-gg-ui` user who takes the new default but never runs `fsgg-sdd` would otherwise get a
   silently lifecycle-less tree. The flip therefore ships **with** a guard: a post-scaffold notice on the
   `sdd` lane ("lifecycle scaffolding not yet supplied — run `fsgg-sdd`, or pass `--lifecycle none` if
   this is deliberate") **and** a fail-closed readiness/doctor check that stays red until the lifecycle
   is re-supplied or `none` was chosen. `sdd` warns; `none` is silent — the guard keys on the *chosen
   intent*, which is the only thing that distinguishes the two identical template outputs. The default is
   not shipped without this guard.

3. **`spec-kit` is frozen and scheduled for removal.** No new investment lands on the `spec-kit` lane.
   On a published timeline (the removal date is a board `Target`, set by a human, not invented here), the
   following are removed together: the `spec-kit` choice from the `lifecycle` symbol (leaving `sdd | none`),
   `.specify/` + constitution emission on that lane, the Spec Kit `after_*` hook machinery, the
   `spec-kit`-gated `fs-gg-samples`, and `fs-gg-feedback-capture`.

4. **Existing `spec-kit` workspaces are grandfathered — with a deadline.** No product is force-migrated.
   Existing `spec-kit` trees keep working until the removal milestone. Because removal *is* scheduled
   (Decision 3), grandfathering is a grace period, not a permanent exemption: a migration path
   (`fsgg-sdd`-assisted re-supply, or re-scaffold) ships **before** the removal date, and the deadline is
   published so no grandfathered product is surprised.

## Consequences

- **This is a versioned cross-repo contract change.** The `fs-gg-ui-template` contract's default lane is
  a surface in `registry/dependencies.yml`; the flip is a `contract-change`, resolved publish-before-flip
  (FR-007) with the registry + `docs/registry/compatibility.md` projection updated and the architecture
  map reconciled if the coherent-set picture moves.

- **The `workRoadmap` capture coupling dissolves as a *consequence*, not a separate change.** Once
  `fs-gg-feedback-capture` is on the removal path, `workRoadmap`'s composed predicate (ADR-0054) can no
  longer AND against a skill that is going away. `workRoadmap`'s step 4 becomes **report-only** (capture
  records, where they exist on a legacy `spec-kit` tree, are still read by the report; nothing is
  *invoked* by the driver), its predicate collapses to `fs-gg-feedback-report`'s `always`, and it finally
  materializes on the `sdd` lane it was built for. ADR-0053 §6's "refuse-if-incomplete" clause must be
  re-keyed onto report rather than the pair. These are edits to `registry/skills.yml`, the
  `driver-skill-manifest.json`, `workRoadmap`'s SKILL.md, and ADR-0053/ADR-0054 — the last two will carry
  the reciprocal amendment markers when executed.

- **`fs-gg-samples` shares the removal fate.** It is `profile == sample-pack and lifecycle == spec-kit`;
  removing the lane strands it. Its resolution (re-gate to `sdd`, or retire) is in scope for the removal
  work, not deferred.

- **Constitution ownership on the default path routes through SDD.** With `sdd` the default, the default
  workspace's constitution/lifecycle scaffolding is the SDD-owned, re-supplied set (ADR-0004), not the
  template-emitted Spec Kit constitution. ADR-0004 is unchanged in substance but now governs the *common*
  case rather than an opt-in; governance-populated-by-default (ADR-0002 §3) is unaffected.

- **The org stops maintaining two first-class lanes.** Docs, tests, registry predicates, and skill gates
  reorganize around `sdd` as the norm and `spec-kit` as the deprecated exception. This is the point of
  the change: one default, one direction, one fabric to keep coherent.

- **Standalone consumers carry a new obligation** (Decision 2's guard): FS.GG.Rendering owns emitting the
  notice and the readiness check. Without it, the accepted hazard (silently lifecycle-less trees) ships;
  with it, the hazard is a red doctor and a printed instruction.

## Alternatives considered

- **Flip only the `fsgg-sdd scaffold` default; leave the raw template on `spec-kit`.** Lower blast radius
  and no standalone-consumer hazard — but it *forks* "the default" in two (scaffolder says `sdd`, raw
  template says `spec-kit`), which is the exact confusion that surfaced this ADR, and it leaves every
  standalone consumer defaulted onto the lane we are retiring. Rejected: the goal is one org-wide default.
  The hazard it avoids is instead *mitigated* by Decision 2's guard.

- **Deprecate `spec-kit` but keep it indefinitely (no removal).** Cheapest, and back-compatible forever —
  but it keeps the entire Spec Kit hook fabric (`.specify/`, `after_*`, `fs-gg-feedback-capture`,
  `fs-gg-samples`) alive and first-classable, so the coherence cost the org pays for two lanes never goes
  away and `workRoadmap`'s capture coupling stays a live constraint rather than dissolving. Rejected in
  favor of a scheduled removal with a grandfather grace period.

- **Status quo (ADR-0002 default stands).** Rejected: the platform already composes via CLI and dogfoods
  `sdd`; a default that preserves the pre-decomposition monolith now misrepresents the org's direction to
  every new consumer, and reproduces the Rouge1 symptom (an SDD product that cannot get its own SDD driver)
  on every scaffold.

<!-- §5 contract picture: this amends a decision, flips a versioned contract default (`fs-gg-ui-template`),
and schedules removal of a gated lane and its dependent skill rows (`fs-gg-feedback-capture`, `fs-gg-samples`)
plus the widening of the `workRoadmap` driver predicate. It adds no new repo, boundary, or coherent-set axis;
it retires a lane within an existing axis. Reconcile docs/architecture.md if the removal moves the §5 picture. -->
