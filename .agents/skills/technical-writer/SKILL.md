---
name: technical-writer
description: Use as the default technical-writing skill for drafting or editing concise, evidence-grounded FS.GG documentation; use its lightweight landing route only for non-functional prose.
---

# Technical writer

Write brief, technical, informative prose for FS.GG. Optimize for new contributors: provide the
context needed to understand the subject and define project-specific terms on first use.

This skill governs writing quality for all technical prose. The type and effect of the document
determine its validation and delivery workflow.

## Ground claims in evidence

- Inspect the relevant source files, configuration, tests, existing documentation, and decisions
  before describing the system.
- Support factual claims with repository evidence. Link to the most direct source when a link helps
  the reader verify or continue from the claim.
- Distinguish current behavior, proposals, assumptions, and open questions.
- State material uncertainty or missing evidence. Do not present an inference as an established fact.
- Omit claims that cannot be supported or clearly identified as assumptions.

## Write concise technical prose

- Lead with the document's purpose, result, or decision. Omit ceremonial introductions and
  meta-commentary about the writing process.
- Use active voice, concrete nouns, and short paragraphs. Prefer precise statements over qualifiers.
- Use sentence-case headings that describe the section's actual content. Avoid generic headings when
  a specific heading would help the reader scan the document.
- Use bullet lists for independent facts, tables for comparisons or exact mappings, and numbered
  lists only when order matters.
- Use short, descriptive inline-link text. Do not expose raw URLs or use a full page title when a
  shorter label identifies the target.
- Add metadata such as date, status, owner, scope, or decision only when it helps that document type.
- Do not use rhetorical openings, repetitive conclusions, emotional framing, grandeur, marketing
  language, decorative adjectives, emojis, collective "we," or direct-address "you."

When editing existing prose, preserve its meaning but freely restructure it. Remove repetition,
filler, vague claims, unnecessary headings, and content that does not help the intended reader.
Retain detail that affects correctness, scope, tradeoffs, or action.

## Match the document to its purpose

Choose the smallest structure that makes the content usable. Do not impose one universal template.
For example, a decision needs its context and consequences, a report needs findings and evidence,
and a runbook needs prerequisites, ordered actions, verification, and recovery information. Include
only sections supported by the subject and requested deliverable.

## Choose the delivery route

Apply the writing guidance above to every technical-writing task. Use the lightweight route below
only when every changed file is prose or a prose index entry and the change does not alter:

- product behavior, public API guidance, tutorials, executable examples, or compatibility;
- shell or CLI instructions, operator runbooks, incident procedures, or other copied-and-executed
  recipes;
- an ADR's accepted authority, a schema, registry, manifest, policy, workflow, generated block, or
  projection;
- test expectations, build inputs, package or release facts, security controls, or required evidence;
  or
- documentation parsed by a gate or generator.

A design, roadmap, analysis, assessment, report, or handoff normally qualifies when it records only
a proposal or reasoning. A discoverability link to that document qualifies too. If the content
changes what a person or machine is required to do, follow the repository's ordinary change process
and run validation appropriate to that surface.

## Land eligible non-functional prose

Make the smallest coherent change:

- Follow the repository's existing documentation location and naming conventions.
- Add only the index or discoverability links needed to find the document.
- Do not create an issue, board row, branch hierarchy, SDD artifact set, ADR, checklist, review
  ledger, or generated projection solely to land eligible prose.
- Do not update architecture or contract projections when the prose changes neither.
- Preserve unrelated working-tree changes.

Validate only what can find a defect in the changed prose:

1. Run `git diff --check`.
2. Verify that relative links in changed Markdown files resolve locally.
3. Run focused frontmatter, formatting, or documentation checks when the repository provides them.
4. Run a targeted parser or recipe check only when the changed text contains the surface it governs.

Do not run a repository-wide suite merely because a broad workflow includes `docs/**`. Required CI
remains the independent backstop. Run a targeted documentation build when it is relevant and cheap;
do not install or build an unrelated product toolchain for non-functional prose. Report skipped or
interrupted validation accurately.

When the user asks to commit, open a pull request, or merge eligible prose:

1. Confirm the branch starts at the current default branch and contains only the intended change.
2. Use one focused `docs:` commit and one pull request.
3. State the narrow validation and identify the change as non-functional.
4. Add `architecture-map: unaffected` only when that statement is true under repository rules.
5. Request no independent reviewer unless branch protection or the user requires one.
6. Merge at the first green required-check state, using squash when that is the repository convention.

Never bypass required checks or branch protection. Fix a relevant failure in the same pull request.
Report an unrelated or infrastructure failure with its run and classification; it does not authorize
a bypass. Do not commit, publish, open a pull request, or merge unless the user requested that action.

The writing task is complete when the requested artifact is coherent, evidence-grounded, and checked
in proportion to its effect. A requested landing task is complete only when the protected default
branch contains the change.
