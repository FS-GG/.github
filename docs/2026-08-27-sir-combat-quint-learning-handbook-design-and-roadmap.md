---
title: S.I.R. Combat in Quint handbook design and roadmap
category: Design
categoryindex: 4
index: 46
description: Design, information architecture, link contract, roadmap, and milestones for a comprehensive S.I.R. combat and Quint learning handbook.
date: 2026-08-27
status: proposed
document-type: design-and-roadmap
---

# S.I.R. Combat in Quint handbook design and roadmap

**Design timestamp:** 2026-08-27T10:31:04+02:00

**Status:** Proposed

**Design owner:** FS.GG cross-project specification work

**Intended publication owner:** S.I.R.

**Primary audience:** S.I.R. maintainers learning Quint, combat-system reviewers, and formal-model authors

This document designs a comprehensive, wiki-like handbook that teaches Quint by deriving an executable
model from the familiar S.I.R. combat domain. The finished handbook will also be a durable guide to
S.I.R. combat intent, rule identity, state, arithmetic, transitions, properties, runtime correspondence,
and known abstraction boundaries. It will use one navigable document with multiple reading paths, a
complete table of contents, pervasive links from controlled vocabulary to canonical definitions, and an
alphabetical definition index.

This repository records the cross-project design. It does not become the owner of S.I.R. combat
semantics. The completed handbook and its authoritative literate Quint belong in S.I.R.; this document
governs their shape and delivery.

## Table of contents

- [1. Purpose](#1-purpose)
- [2. Goals](#2-goals)
- [3. Non-goals](#3-non-goals)
- [4. Source and authority model](#4-source-and-authority-model)
- [5. Audience and learning outcomes](#5-audience-and-learning-outcomes)
- [6. Product shape](#6-product-shape)
- [7. Proposed handbook contents](#7-proposed-handbook-contents)
- [8. Chapter design](#8-chapter-design)
- [9. Definition-link contract](#9-definition-link-contract)
- [10. Definition index design](#10-definition-index-design)
- [11. Executable-example contract](#11-executable-example-contract)
- [12. Traceability and correspondence](#12-traceability-and-correspondence)
- [13. Quality and acceptance criteria](#13-quality-and-acceptance-criteria)
- [14. Roadmap](#14-roadmap)
- [15. Milestones](#15-milestones)
- [16. Risks and mitigations](#16-risks-and-mitigations)
- [17. Maintenance model](#17-maintenance-model)
- [18. Open design questions](#18-open-design-questions)
- [19. Definition index](#19-definition-index)

## 1. Purpose

The project will produce a handbook provisionally named **S.I.R. Combat in Quint: From Design
Decisions to Executable Models**. It has two equal purposes:

1. teach Quint progressively through a domain the learner already understands; and
2. document the bounded S.I.R. physical-combat rule corpus and the reasoning that shaped its model.

The handbook will connect four forms of understanding that are often separated:

```text
combat intent
  -> architecture and rule decisions
  -> Quint types, functions, actions, runs, and properties
  -> runtime correspondence and evidence
```

The learner should be able to move in either direction: from a combat term to the model that defines
it, or from a Quint declaration to its domain meaning and production evidence.

## 2. Goals

### G1. Teach Quint through progressive disclosure

The handbook will begin with a single deterministic damage calculation, then revisit the same attack
at increasing levels of formal depth: model data, pure arithmetic, typed records, state transition,
invariant, execution trace, mutation, and runtime correspondence.

### G2. Document the complete bounded combat corpus

The handbook will cover all sixteen stable rule identities in the Q4 S.I.R. combat registry:

- two content facts;
- three pure formulas;
- one registered external-algorithm contract;
- nine focused transitions; and
- one aggregate attack-resolution transition.

### G3. Demonstrate multiple modeling domains and granularities

The material will show Quint used for catalogue structure, fixed-point numerical semantics, algebraic
domain types, external contracts, state machines, reachability examples, safety properties, trace
interpretation, and implementation correspondence.

### G4. Preserve traceability

Every modeled rule and property will trace back to a design or rule source and forward to an
executable example, runtime correspondence point, or explicit non-coverage statement.

### G5. Make definitions one navigation step away

Every controlled Quint keyword, model symbol, S.I.R. combat concept, rule identifier, stat, unit, and
named property used outside its own definition will link to its canonical definition. The handbook will
end with a complete alphabetical definition index.

### G6. Remain executable

Code presented as executable Quint must be extracted from, or checked against, the authoritative
literate model. Tutorial snippets may be intentionally partial only when they are labeled as such and
link to the complete executable declaration.

## 3. Non-goals

- The handbook will not replace the S.I.R. combat architecture or its stable rule identities.
- It will not reproduce the `FS.GG.Game.Core.Los.lineOfSightBy` supercover implementation in Quint.
- It will not claim the generated `.qnt` projection is an independent source of authority.
- It will not turn the existing runtime interpreter into generated gameplay code.
- It will not model unrelated S.I.R. application behavior merely to make the handbook appear complete.
- It will not treat sampled execution as exhaustive proof.
- It will not hide modeling assumptions, finite bounds, or known correspondence gaps.
- It will not become a general Quint language manual; general syntax is explained only where the
  combat model gives it concrete meaning.

## 4. Source and authority model

The handbook will explicitly distinguish sources by the question each one answers.

| Layer | Source | Question answered | Authority rule |
|---|---|---|---|
| Cross-project language direction | [ADR-0077](adr/0077-quint-first-typed-specification-authority.md) | Why Quint is the target Typed SDD authoring language | Governs FS.GG integration direction |
| Migration architecture | [Quint-first Typed SDD migration design](coordination/2026-08-25-quint-first-typed-sdd-migration-design.md) | How Quint becomes a supported product authority | Governs producer and migration boundaries |
| S.I.R. corpus architecture | S.I.R. `docs/adr-0001-executable-rules-corpus.md` | Why executable, explainable, stable rule identities exist | Governs corpus intent and identity |
| Combat design | S.I.R. `docs/combat-resolution.md` | What physical combat means | Governs domain intent |
| Q4 scope and decisions | S.I.R. `work/352-quint-q4-sir-adoption/` | What the complete adoption includes and how it is abstracted | Governs Q4 scope and granularity |
| Literate model | S.I.R. `docs/rules/sir-combat.md` | What the bounded model says and executes | Authoritative Q4 Quint source |
| Generated model | S.I.R. generated `sir-combat.qnt` | What the Quint toolchain consumes | Projection; never edited directly |
| Runtime | S.I.R. simulation and combat-rule modules | What production executes | Correspondence subject, not a second model authoring source |
| Evidence | S.I.R. conformance, replay, mutation, and readiness artifacts | What has been observed and checked | Scoped evidence only |

When two layers disagree, the handbook must expose the disagreement. It must not silently rewrite a
design decision to match an implementation defect or overstate a model beyond its declared boundary.

## 5. Audience and learning outcomes

### Primary learner

The primary learner understands S.I.R. combat but is new to Quint. The material may assume familiarity
with attacks, cover, armor, health, wounds, and suppression. It must not assume familiarity with formal
specification, state-transition systems, reachability, invariants, or counterexamples.

### Secondary readers

- A combat maintainer looking for the source and effect of a rule.
- A reviewer checking whether an ADR or Q4 decision has a faithful model representation.
- An implementer comparing Quint behavior with the F# runtime.
- A formal-model author evaluating Quint across different abstraction levels.

### Learning outcomes

After following the main path, a reader should be able to:

1. explain the difference between a pure definition, state variable, action, run, witness, and invariant;
2. predict and execute representative S.I.R. combat examples;
3. translate a prose combat decision into entities, operations, guards, updates, and properties;
4. justify why a behavior is represented as data, a pure function, an external contract, or an action;
5. read a Quint execution trace in combat-domain language;
6. diagnose an injected defect from a failed property or counterexample;
7. identify what the model does not prove; and
8. follow a rule from its stable identifier through model declaration and production evidence.

## 6. Product shape

The initial product is one self-contained Markdown handbook in S.I.R. A single file provides reliable
table-of-contents navigation, stable local anchors, offline readability, and easy searching while the
content matures.

The document will present three reading paths:

| Reading path | Reader goal | Recommended order |
|---|---|---|
| Learn Quint | Build formal-modeling skill progressively | orientation -> representative attack -> foundations -> guided walkthroughs -> counterexamples |
| Understand combat | Use the handbook as domain documentation | combat overview -> rule catalogue -> focused mechanics -> rule index |
| Review traceability | Audit authority and correspondence | source hierarchy -> ADR translation -> property matrix -> runtime evidence -> limits |

The structure may later split into several generated or cross-linked pages, but the first complete
edition must remain usable as one document. A future split must preserve every stable definition anchor
through redirects or generated compatibility anchors.

## 7. Proposed handbook contents

### Part I: Orientation

1. What this handbook is.
2. How to use the three reading paths.
3. Sources of authority and their precedence.
4. Toolchain setup and a first successful Quint run.
5. The complete attack-resolution pipeline at a glance.

### Part II: S.I.R. combat domain

6. Physical-combat design boundary.
7. The sixteen-rule catalogue.
8. Rule dependency and explanation-order maps.
9. Combat state, inputs, observations, and units.
10. What the bounded Q4 model includes and excludes.

### Part III: Decisions to model

11. Extracting entities, operations, assumptions, and properties from design text.
12. Mapping rule kinds to Quint constructs.
13. Choosing state shape and action granularity.
14. Fixed-point Q4 arithmetic and rounding.
15. The external line-of-sight contract boundary.
16. Atomic aggregate consequences versus focused pure helpers.

### Part IV: Quint foundations through combat

17. Modules, types, variants, records, sets, and lists.
18. Constants, model data, and the rule catalogue.
19. Pure functions and deterministic damage calculations.
20. Variables, initialization, and cohesive combat state.
21. Guards, actions, primed assignments, and disabled transitions.
22. Nondeterministic steps and possible combat histories.
23. Runs, witnesses, invariants, simulations, and bounded verification.

### Part V: Guided walkthroughs

24. Representative rifle damage: `25 x 1.0 x 0.8 = 20`.
25. A miss causes neither damage nor suppression.
26. Wound thresholds at damage 24, 25, and 50.
27. Health reaching zero and incapacitation.
28. Cover impact, destruction, permeability, and the current collision.
29. Suppression eligibility and five-point recovery.
30. Faction-neutral collateral consequences.
31. Registered external line-of-sight behavior.

### Part VI: Formal reasoning in practice

32. Choosing an example, witness, or invariant.
33. Reading an execution trace.
34. Reading and minimizing a counterexample.
35. Mutation laboratory.
36. Dead actions, accidental stuttering, and terminal states.
37. What sampled runs establish and what exhaustive checks add.

### Part VII: Production correspondence

38. Mapping Quint records and operations to S.I.R. runtime subjects.
39. Literate authority and deterministic `.qnt` extraction.
40. Exact and sampled ITF replay.
41. First-divergence reporting.
42. Observed-red controls and restored-green evidence.
43. Safely changing a combat rule.

### Part VIII: Reference

44. Complete rule reference.
45. Quint declaration reference.
46. Traceability matrix.
47. Command reference.
48. Known limits and future experiments.
49. Exercises and solutions.
50. Alphabetical definition index.

## 8. Chapter design

Each guided chapter will use the same learning cycle:

```text
domain expectation
  -> source decision
  -> modeling choice
  -> predict
  -> execute
  -> inspect
  -> mutate
  -> explain
```

Each rule-focused chapter will contain:

1. **Domain meaning** — what the mechanic means in S.I.R.
2. **Source decision** — the architecture, Q4 decision, or stable rule that authorizes it.
3. **Modeling question** — what must be represented and what may be abstracted.
4. **Quint representation** — linked declarations and a small executable excerpt.
5. **Prediction prompt** — a result the learner calculates before execution.
6. **Execution** — an exact command and expected outcome.
7. **Trace reading** — the state changes translated back into combat language.
8. **Property** — the example, witness, or invariant that captures the intended behavior.
9. **Mutation** — one deliberate defect and the evidence expected to detect it.
10. **Claim boundary** — what the successful check establishes and what it does not.
11. **Correspondence** — the runtime subject and evidence associated with the rule.
12. **Further exercise** — a small learner-authored variation.

The representative rifle attack is the narrative spine. The handbook will revisit the same attack as
catalogue data, fixed-point arithmetic, a pure result, a state transition, a property-bearing trace, and
a runtime correspondence case. This avoids introducing a new domain every time a new Quint construct is
taught.

## 9. Definition-link contract

### 9.1 Controlled vocabulary

The phrase **all defined concepts link to their definition** applies to these controlled classes:

| Class | Examples | Canonical destination |
|---|---|---|
| Quint keyword | `module`, `type`, `pure`, `val`, `def`, `var`, `action`, `run`, `import`, `nondet` | Quint-language entry in the definition index |
| Quint operator or notation | primed assignment, `all`, `any`, `then`, `expect`, implication | notation entry in the definition index |
| Model type | `CombatState`, `AttackInput`, `Observation`, `Wound` | declaration reference entry |
| Model value or variable | `combat`, `last`, `SCALE`, `ruleCatalogue` | declaration reference entry |
| Model function or action | `damageForAttack`, `resolveConsequences`, `step` | declaration reference entry |
| Property or scenario | `boundedCombatState`, `representativeDamageIsTwenty` | property/run reference entry |
| Stable rule identifier | `COMBAT-DAMAGE-001`, `COMBAT-TRACE-002` | complete rule reference entry |
| Combat concept | trace ratio, armor retention, cover integrity, suppression, incapacitation | combat glossary entry |
| Stat or bounded quantity | health, damage, range cells, suppression delta | stat definition entry |
| Unit or encoding | Q4 raw integer, scale 10,000, percentage-like ratio, cells, hit points | unit definition entry |
| Evidence concept | witness, invariant, counterexample, ITF trace, correspondence | formal-method definition entry |

Ordinary prose words are not controlled vocabulary merely because they have dictionary meanings. A term
enters the controlled vocabulary when the handbook assigns it domain, model, language, or evidentiary
semantics.

### 9.2 Linking rule

Outside a definition's own heading or index entry, every occurrence of a controlled term must be a
Markdown link to its canonical definition anchor. This is stronger than a first-use-only glossary rule.
It allows a learner to enter the handbook at any section without searching backward for context.

Exceptions are deliberately narrow:

- Markdown headings that are themselves the canonical definition.
- The canonical spelling column of the definition index.
- Executable code fences, because Markdown links would invalidate the code.
- Machine-generated command output and counterexample traces, which must remain byte-faithful.

Every code fence or raw trace containing controlled symbols must be followed by a **Definitions used**
line or compact table linking each newly introduced symbol and concept. Repeated symbols in adjacent
steps may be grouped only when the group remains visible without scrolling past the example.

### 9.3 Anchor rules

- Every definition receives an explicit, stable, lowercase HTML anchor.
- Anchors use semantic names, not section numbers: `def-combat-state`, not `section-17-2`.
- Stable rule IDs use lowercase identifiers: `rule-combat-damage-001`.
- Quint symbols use a namespace prefix: `qnt-damage-for-attack`.
- Stats use `stat-`; concepts use `concept-`; properties use `property-`; commands use `cmd-`.
- Renaming a heading must not rename its existing anchor.
- When a concept is superseded, its anchor remains and points to the superseding definition.

### 9.4 Link text rules

- Links use the canonical domain or symbol spelling rather than “here” or “this definition.”
- Code symbols remain formatted as code inside the link label where the renderer supports it.
- Rule IDs remain uppercase in visible text.
- A stat link includes its unit when ambiguity is possible, such as “suppression delta (points).”
- A numeric literal that carries domain meaning links through its named stat or threshold, rather than
  linking arbitrary numbers. For example, damage `25` links to the minor-wound threshold definition;
  a section number does not.

### 9.5 Link validation

The handbook delivery will include a link-audit step that checks:

1. every local fragment resolves;
2. every definition index entry points to exactly one canonical anchor;
3. no two definitions claim the same anchor;
4. every controlled symbol in the declaration inventory appears in the definition index;
5. every rule ID in the model catalogue appears in the rule reference and definition index;
6. every controlled term found outside exempt regions is linked; and
7. links from code-fence companion tables resolve.

The preferred implementation is a Markdown-AST-aware audit fed by a small checked vocabulary manifest.
A regular-expression-only check is insufficient because it cannot reliably distinguish code, headings,
links, generated output, and ordinary prose.

## 10. Definition index design

The handbook will end with one alphabetical index rather than several disconnected glossaries. Each
entry will contain:

| Field | Meaning |
|---|---|
| Canonical term | Exact visible spelling |
| Kind | Keyword, notation, type, variable, function, action, property, rule, concept, stat, unit, or evidence term |
| Definition | Concise domain-aware explanation |
| Declared at | Link to the primary explanatory section or Quint declaration |
| Related terms | Links to dependencies, opposites, or commonly confused concepts |
| Runtime correspondence | Link or source path when the term has a production counterpart |

Aliases such as “HP” and “health” receive separate searchable entries, but only one canonical definition.
The alias entry links to the canonical entry and explains whether the names are exactly synonymous.

The index will be generated or audited against inventories derived from the authoritative model:

- every type;
- every top-level value and constant;
- every state variable;
- every pure function;
- every action;
- every invariant, witness, and run;
- every stable rule ID;
- every named stat and unit used by the model; and
- every handbook-defined modeling concept.

## 11. Executable-example contract

Examples fall into four labeled classes:

| Class | Meaning | Required evidence |
|---|---|---|
| Executable excerpt | Extracted from the authoritative model without semantic edits | Source anchor and successful typecheck/run receipt |
| Executable exercise | Complete learner-editable module or test | Exact command and expected result |
| Illustrative fragment | Partial syntax used to explain one idea | Explicit “not standalone” label and link to full declaration |
| Deliberately broken mutation | Expected to fail for a named reason | Expected failing property and restored-green counterpart |

Every executable walkthrough uses the sequence **predict -> run -> observe -> explain**. Mutation chapters
extend it to **predict failure -> run -> locate first divergence -> repair -> rerun**.

The handbook will keep scenario-style `run` declarations separate from the main state-machine module,
matching the production literate model. It will use sampled `quint run` for the ordinary learning loop.
Any exhaustive bounded model-checking example must be labeled separately with its bounds, toolchain,
expected cost, and narrower claim.

## 12. Traceability and correspondence

The handbook will maintain a complete traceability matrix with one row per modeled obligation:

| Source decision | Stable rule | Quint declaration | Scenario/property | Runtime subject | Evidence | Coverage note |
|---|---|---|---|---|---|---|

Minimum coverage includes:

- all sixteen stable combat rules;
- all Q4 modeling decisions `DEC-001` through `DEC-007`;
- representative damage 20;
- wound boundaries 24, 25, and 50;
- zero-health incapacitation;
- suppression eligibility and five-point recovery;
- cover destruction, permeability, and current-collision blocking;
- faction-neutral collateral consequences;
- valid trace ratios and the external line-of-sight boundary;
- rule-catalogue size, identity, dependency, and explanation order; and
- exact and sampled runtime replay correspondence.

An empty runtime-evidence cell is allowed only when the coverage note says why the item is abstract,
deferred, externally implemented, or deliberately outside the correspondence boundary.

## 13. Quality and acceptance criteria

The first complete edition is accepted only when:

1. the document has a working top-level table of contents and stable section anchors;
2. each of the three reading paths is complete and can be followed without hidden prerequisites;
3. all sixteen rules have domain definitions, Quint mappings, and traceability rows;
4. all promised Quint construct classes are demonstrated using combat examples;
5. every guided walkthrough has a prediction, executable step, explanation, mutation, and claim boundary;
6. all executable examples pass and every negative control fails for its intended reason;
7. the definition-link audit reports no missing anchors, unindexed controlled terms, or unlinked controlled occurrences;
8. the final alphabetical definition index covers the model inventory and handbook vocabulary;
9. code-fence companion definition links cover every introduced symbol;
10. runtime correspondence claims cite actual S.I.R. sources or evidence;
11. sampled and exhaustive claims are clearly distinguished;
12. external algorithm boundaries and non-goals are explicit;
13. the generated `.qnt` is identified as a projection, not another authoring source; and
14. a Quint beginner can complete the representative-attack path using only the handbook and pinned toolchain instructions.

## 14. Roadmap

The work proceeds from authority inventory to a runnable learning spine, then broadens into complete
reference coverage. This keeps the handbook useful and testable before every reference entry is written.

```text
M0 authority inventory
  -> M1 linked handbook skeleton
  -> M2 representative attack learning spine
  -> M3 complete combat-rule walkthroughs
  -> M4 formal reasoning and mutation laboratory
  -> M5 runtime correspondence and evidence
  -> M6 definition index and link enforcement
  -> M7 review, publication, and maintenance handoff
```

## 15. Milestones

### - [ ] M0 — Authority and vocabulary inventory

**Outcome:** a checked source map and controlled-vocabulary inventory.

Deliverables:

- inventory of the S.I.R. ADR, combat architecture, Q4 decisions, model, runtime, and evidence;
- inventory of sixteen rule IDs and their dependencies;
- inventory of Quint declarations and properties;
- initial stat, unit, combat-concept, and formal-method vocabulary;
- explicit list of scope exclusions and unresolved source disagreements.

Exit criteria:

- every planned handbook claim has an identified authority class;
- every model declaration has a planned index kind; and
- no unresolved disagreement changes the proposed state shape or action granularity.

### - [ ] M1 — Linked handbook skeleton

**Outcome:** the publication file exists with its complete hierarchy and navigation.

Deliverables:

- front matter and title;
- table of contents;
- three reading-path maps;
- stable anchors for all planned definitions;
- empty traceability matrix with all mandatory rows;
- initial alphabetical definition index;
- checked vocabulary manifest and link-audit prototype.

Exit criteria:

- every table-of-contents link resolves;
- every controlled term in the skeleton links; and
- the document builds in the S.I.R. documentation pipeline.

### - [ ] M2 — Representative attack learning spine

**Outcome:** a Quint beginner can follow one attack end to end.

Deliverables:

- attack pipeline overview;
- facts, Q4 arithmetic, trace, retention, expected damage, and rounding;
- `CombatState`, `AttackInput`, and `Observation` explanations;
- representative-damage action and run;
- prediction prompts, trace reading, and one negative mutation;
- runtime correspondence for the representative attack.

Exit criteria:

- `25 x 1.0 x 0.8 = 20` is explained at every modeling layer;
- all shown executable code runs under the pinned toolchain; and
- the learner can explain why the model uses raw scale-10,000 integers.

### - [ ] M3 — Complete combat-rule walkthroughs

**Outcome:** every stable combat rule is documented and executable at its appropriate granularity.

Deliverables:

- catalogue and dependency documentation;
- wound, incapacity, suppression, recovery, cover, collateral, penetration, and aggregate-resolution chapters;
- external line-of-sight contract chapter;
- a rule-reference entry and traceability row for every rule;
- exercises at beginner, intermediate, and advanced levels.

Exit criteria:

- sixteen of sixteen rules have complete reference coverage;
- every focused transition is visible through a pure helper, action, observation, or property; and
- no chapter invents a runtime-visible intermediate state.

### - [ ] M4 — Formal reasoning and mutation laboratory

**Outcome:** the handbook teaches how to learn from execution and failure.

Deliverables:

- examples versus witnesses versus invariants;
- nondeterministic trace interpretation;
- counterexample-reading workflow;
- mutation cases for thresholds, bounds, suppression, cover, collateral, and catalogue integrity;
- restored-green results for every deliberate defect;
- clear sampled-versus-exhaustive claim language.

Exit criteria:

- every major action has reachable execution evidence;
- every required invariant references model state; and
- each mutation fails through its named detection route before repair.

### - [ ] M5 — Runtime correspondence and evidence

**Outcome:** model claims connect to production behavior without merging their authorities.

Deliverables:

- Quint-to-F# correspondence map;
- literate-source and generated-projection explanation;
- exact and sampled ITF replay walkthroughs;
- first-divergence example;
- evidence and observed-red control reference;
- safe rule-change workflow.

Exit criteria:

- every production claim cites a runtime subject and evidence;
- missing correspondence is explicitly classified; and
- the handbook never describes simulation output as proof of implementation equivalence.

### - [ ] M6 — Complete definition index and enforced linkability

**Outcome:** every controlled term is one click from its definition.

Deliverables:

- complete alphabetical definition index;
- aliases and related-term links;
- declaration and rule inventories reconciled with the index;
- Markdown-AST link audit integrated with documentation qualification;
- negative controls for missing links, duplicate anchors, and absent index entries.

Exit criteria:

- zero unresolved internal links;
- zero unindexed controlled terms;
- zero unlinked controlled occurrences outside documented exemptions; and
- all deliberate link defects are detected.

### - [ ] M7 — Review, publication, and maintenance handoff

**Outcome:** the handbook is published as maintained S.I.R. documentation.

Deliverables:

- domain review;
- Quint language and modeling review;
- beginner walkthrough review;
- rendered-document inspection;
- last-verified toolchain and source identities;
- update checklist and owner handoff.

Exit criteria:

- all acceptance criteria pass;
- reviewers approve the domain and model boundaries;
- the S.I.R. docs build is green; and
- the maintenance trigger is documented beside the authoritative model.

## 16. Risks and mitigations

| Risk | Consequence | Mitigation |
|---|---|---|
| One document becomes too large | Readers lose the learning path | Preserve three explicit paths, repeated chapter templates, local navigation, and stable anchors |
| Link density harms readability | Prose becomes visually noisy | Link canonical terms without verbose link labels; validate rendered appearance; keep ordinary prose outside controlled vocabulary |
| Definition links drift | Wiki behavior becomes unreliable | Stable explicit anchors, checked vocabulary manifest, AST-aware audit, and negative controls |
| Tutorial copies model code | Examples drift from authority | Extract executable excerpts or test them against the literate source |
| Domain prose overstates the bounded model | Readers mistake abstraction for the whole game | Put claim boundaries and non-coverage statements in every walkthrough |
| Model mirrors implementation defects | Formalization legitimizes a bug | Trace to design sources first and surface disagreements instead of silently reconciling them |
| External line-of-sight is modeled twice | Two competing authorities emerge | Keep only its input/result contract and registered implementation fingerprint in Quint |
| Sampled runs are described as proof | Evidence is over-trusted | Label the checking mode, bounds, explored traces, and exact property for every receipt |
| Index is manually incomplete | Symbols become undiscoverable | Derive inventories from the authoritative model and compare them with the checked vocabulary manifest |
| Generated pages break stable anchors | Existing links rot | Preserve compatibility anchors or redirects during any future page split |

## 17. Maintenance model

The handbook must be reviewed whenever any of these change:

- a stable combat rule is added, superseded, or changes dependencies;
- a modeled stat, threshold, unit, type, action, observation, or property changes;
- the external algorithm symbol or fingerprint changes;
- the atomicity boundary of a runtime operation changes;
- the Quint toolchain or Typed SDD profile changes materially;
- the state/observation correspondence mapping changes;
- a new mutation or counterexample reveals a missing explanation; or
- a definition anchor must be superseded.

The normal change order is:

1. review the design or rule decision;
2. update the authoritative literate Quint;
3. update executable examples and properties;
4. regenerate projections;
5. run model and correspondence checks;
6. update handbook narrative, traceability, and definitions;
7. run link, docs, and observed-red qualification; and
8. record the new last-verified identities.

## 18. Open design questions

These questions do not block the design skeleton but must be closed before M1 exits:

1. What is the final S.I.R. handbook filename and navigation category?
2. Will the controlled-vocabulary manifest live beside the handbook or be generated from Typed SDD products?
3. Which Markdown AST implementation will enforce linked occurrences in the S.I.R. documentation pipeline?
4. Should exercise solutions be collapsible inline sections or a separate appendix within the same file?
5. Which exhaustive checks, if any, are appropriate for the beginner path rather than an advanced appendix?
6. Which runtime source links can use stable generated API references and which require commit-pinned repository links?

## 19. Definition index

This index defines the design vocabulary used by this roadmap. The completed handbook will replace this
seed with the full generated or audited index described in [Section 10](#10-definition-index-design).

<a id="def-authoritative-literate-model"></a>
### Authoritative literate model

The human-reviewed Markdown source whose named Quint blocks own the model. The extracted `.qnt` file is
a deterministic projection of this source.

<a id="def-claim-boundary"></a>
### Claim boundary

An explicit statement of exactly what a model execution, invariant check, correspondence run, or other
piece of evidence establishes and what remains outside its scope.

<a id="def-controlled-vocabulary"></a>
### Controlled vocabulary

The checked set of Quint language terms, model declarations, stable rule IDs, S.I.R. concepts, stats,
units, properties, and evidence concepts whose occurrences must link to canonical definitions.

<a id="def-correspondence"></a>
### Correspondence

A checked relationship between an abstract Quint state or observation and the input, output, or state of
the real S.I.R. combat interpreter. Correspondence is not implied merely because the two descriptions
look similar.

<a id="def-counterexample"></a>
### Counterexample

An execution trace showing a reachable state or transition that violates a stated property under the
model and bounds being checked.

<a id="def-definition-anchor"></a>
### Definition anchor

A stable fragment identifier attached to the canonical definition of a controlled term, such as
`#def-counterexample` or `#rule-combat-damage-001`.

<a id="def-executable-excerpt"></a>
### Executable excerpt

A code example extracted from the authoritative model, or mechanically checked against it, that can be
executed with the documented pinned toolchain.

<a id="def-external-algorithm-contract"></a>
### External algorithm contract

A formal boundary that constrains the valid inputs, result, observables, identity, and assumptions of an
algorithm implemented outside Quint without copying that implementation into the model.

<a id="def-invariant"></a>
### Invariant

A state property intended to hold in every reachable model state within the stated execution or
verification scope.

<a id="def-itf-trace"></a>
### ITF trace

An Informal Trace Format representation used to exchange model states and transitions with replay or
correspondence tooling.

<a id="def-model-granularity"></a>
### Model granularity

The chosen level at which behavior is represented: catalogue data, pure computation, external contract,
focused transition, aggregate transition, or multi-step state machine.

<a id="def-mutation"></a>
### Mutation

A deliberate semantic defect inserted to demonstrate that a named property, correspondence check, or
qualification route becomes red for the intended reason.

<a id="def-observed-red"></a>
### Observed red

Recorded evidence that a deliberately introduced relevant defect causes the expected validation route to
fail before the correct subject is restored and rechecked.

<a id="def-primed-assignment"></a>
### Primed assignment

Quint action notation assigning a variable's value in the next state, conventionally written as a name
followed by a prime, such as `combat'`.

<a id="def-q4-fixed-point"></a>
### Q4 fixed-point

The S.I.R. model encoding in which a real-like value is stored as a signed integer scaled by 10,000, so
`1.0` is represented by `10000` and `0.8` by `8000`.

<a id="def-representative-attack"></a>
### Representative attack

The canonical tutorial scenario in which rifle damage 25, full trace ratio 1.0, and armor retention 0.8
produce rounded damage 20 before state consequences are applied.

<a id="def-restored-green"></a>
### Restored green

Evidence that the correct subject passes its checks again after a deliberate mutation has produced the
expected observed-red result.

<a id="def-rule-identity"></a>
### Rule identity

A globally unique, stable machine-readable identifier naming an enduring S.I.R. rule concept, such as
`COMBAT-DAMAGE-001`.

<a id="def-sampled-run"></a>
### Sampled run

A finite set of executable Quint traces explored by simulation. It supplies concrete behavioral evidence
but is not an exhaustive proof over the state space.

<a id="def-stat"></a>
### Stat

A named combat quantity with defined semantics, domain, and unit, such as health points, suppression
points, damage, cover integrity, range in cells, or a Q4 retention ratio.

<a id="def-traceability-matrix"></a>
### Traceability matrix

A table linking a source decision or requirement to its stable rule, Quint declaration, executable
property or scenario, runtime subject, evidence, and coverage qualification.

<a id="def-witness"></a>
### Witness

A positively stated target condition used to determine whether sampled execution reaches a meaningful
state, commonly to show that an action or behavior is not dead.
