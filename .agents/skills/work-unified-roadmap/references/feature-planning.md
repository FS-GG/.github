# Feature-planner assignment

Read this only when a new major feature needs a plan or an existing feature needs a material replan or
near-term horizon expansion. The planner is `gpt-6-astra` with effort `high`; implementation is a separate
`gpt-5.6-sol` medium worker. Planning does not authorize implementation or protected effects.

## Give the planner a bounded evidence packet

Include the user's feature outcome, its unified stage, relevant original design sections, owning
repository locations, current default revisions, existing plans/PRs/tests and known failures. Include
applicable contracts and the actual implementation/operation authority already provided by the user.
Do not require an organization-wide board census or every historical roadmap to plan one feature.

Ask the planner to inspect actual prior work before proposing new work. Classify relevant capabilities
as implemented, independently demonstrated, missing or uncertain; a merged source change alone does not
prove installed behavior. Reuse existing models, components, accepted units and feature identities.
Research online only where current primary sources help decide a material unknown. Do not quietly
promote assumptions into requirements or silently remove a requested capability.

The planner may ask about a consequential unresolved product decision while continuing independent
analysis. Routine implementation choices are made using context and judgment, without creating an
approval round. Findings outside the selected feature remain links or explicit deferred questions.

## Return one short executable feature subroadmap

Aim for a few pages and roughly 3–7 meaningful milestones when that fits the feature; those are sizing
guides, not gates or reasons to split/bundle unrelated work. Detail only the next few ready slices. Keep
the uncertain remainder as an outcome outline without executable checkboxes.

The document should establish:

- A stable feature identity, owning repository, unified stage, requested outcome and completion examples.
- Prior work to reuse, with the exact source/evidence references that matter and explicit gaps.
- Scope, dependencies, key assumptions and decisions that would invalidate the current plan.
- Ready milestones with stable IDs, outcome, prerequisites, likely scope, process route and meaningful
  acceptance examples. Separate routine source delivery from any protected publication or operation.
- Later outline work and the concrete evidence needed to make its next portion executable.
- The observation source and known logging/attribution gaps, preserving the unified accounting definitions.

For new work, a ready milestone can use this compact form:

```markdown
- [ ] FEATURE-01 — Observable result — route: routine
  Depends on: applicable completed prerequisite, or none.
  Scope: affected component and owning repository.
  Acceptance: meaningful behavior and focused verification.
```

Use `routine` or `routine implementation; strict operation` only when the current owning policy admits
it. Otherwise identify the applicable protected/existing route; the worker verifies eligibility again
against the actual change. A small diff does not establish routine eligibility.

For an existing registered unit such as GS2, reference its current owning roadmap, contract and evidence
without creating a second mutable checkbox. A feature may combine new milestones with these references,
but there must be one source of completion authority per unit.

Return the proposed path, the complete Markdown, the first executable window, unresolved decisions and
any reason the window cannot yet run. Persist it in the authorized workspace if the parent assigned a
path; otherwise return the draft for the parent to place. Do not create issues, claims, planning-only PRs,
new policy registries or a second orchestration service solely to produce the plan.

## Replanning contract

A feature begins with one fresh planner. A valid active subroadmap does not need another planner because
a session restarted, a PR changed head or a test failed. When its detailed window is exhausted, extend
only the next useful window using the newly observed results. A substantive invalidated assumption,
dependency or scope change warrants replanning; preserve delivered work and the original cost lineage.

The parent passes the accepted bounded window to the Sol-medium worker with the installed `work-roadmap`
path and applicable authority. Reading or creating this subroadmap does not mean every feature in the
Unified Roadmap has been planned, activated or implemented.
