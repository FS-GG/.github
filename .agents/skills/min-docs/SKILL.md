---
name: min-docs
description: Use for prose-only designs, roadmaps, reports, and handoffs that change no functionality, API, operator recipe, schema, or contract.
---

# min-docs

Land non-functional prose with effort proportional to its risk. The purpose of this skill is to stop a
design note or report from inheriting the implementation workflow merely because it is tracked in Git.

## Qualify the change

Use this route only when every changed file is prose or a prose index entry and the change does not alter:

- product behavior, public API guidance, tutorials, examples users are expected to execute, or compatibility;
- shell/CLI snippets, operator runbooks, incident procedures, or other copied-and-executed recipes;
- an ADR's accepted authority, a schema, registry, manifest, policy, workflow, generated block, or projection;
- test expectations, build inputs, package/release facts, security controls, or required evidence; or
- documentation whose content is parsed by a gate or generator.

A timestamped design, roadmap, analysis, assessment, report, or handoff normally qualifies when it only
records a proposal or reasoning. A discoverability link to that document qualifies too. If content changes
what a person or machine is required to do, use the repository's ordinary documentation/change process.

## Make the smallest coherent change

- Put a new timestamped document under the repository's existing `docs/` location and naming convention.
- Add at most the smallest existing index/discoverability link needed to find it.
- Do not create an issue, board row, branch hierarchy, SDD artifact set, ADR, checklist, review ledger, or
  generated projection solely to land eligible prose.
- Do not update architecture or contract projections when the prose explicitly changes neither.
- Preserve unrelated working-tree changes.

## Validate narrowly

Run only checks that can find a defect in the changed prose before opening the PR:

1. `git diff --check`;
2. verify relative links in the changed Markdown files resolve locally;
3. validate required frontmatter or formatting when this repository has a focused command for it; and
4. run a targeted parser/recipe check only when the changed text contains the surface that parser governs.

Do not run a repository-wide or change-derived suite locally merely because a broad workflow path includes
`docs/**`. Required CI remains the independent backstop. If the repository has an actually targeted docs
build that is cheap, run it; do not install or build an unrelated product toolchain to validate prose.

Record interrupted or deliberately skipped broad validation honestly. Do not describe it as passing.

## Land directly

When the user asked to commit, open a PR, or merge:

1. confirm the branch starts at current `origin` default and contains only the eligible prose change;
2. use one focused `docs:` commit and one PR;
3. state the narrow validation and that the change is non-functional;
4. add `architecture-map: unaffected` only when that statement is true under the repository's rules;
5. request no independent reviewer unless branch protection or the user requires one; and
6. merge at the first green required-check state, using squash when that is the repository convention.

Never bypass required checks or branch protection. If auto-merge is unavailable, monitor only the required
merge predicate rather than reproducing CI locally. A relevant red is fixed in the same PR. An unrelated or
infrastructure red is reported with its run and classification; it does not authorize a bypass.

## Stop condition

The route is complete when the prose is merged and the protected default branch contains it. If the change
ceases to be prose-only, stop using this skill and switch to the workflow appropriate to the newly affected
surface.
