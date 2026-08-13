---
schemaVersion: 1
workId: 2380-feedback-report-materialization
title: "scaffold materialization: why fs-gg-feedback-report is absent from a workspace-template product tree"
stage: charter
changeTier: tier1
status: chartered
policyPointers:
  - .fsgg/sdd.yml
  - .fsgg/agents.yml
  - .fsgg/policy.yml
  - .fsgg/capabilities.yml
  - .fsgg/tooling.yml
---

# scaffold materialization: why fs-gg-feedback-report is absent from a workspace-template product tree Charter

## Identity
- Work id: `2380-feedback-report-materialization`
- Lifecycle stage: charter
- Status: chartered

## Principles
- **The deliverable is an established cause, not a fix.** `.github#2380` says in its own words that
  the root cause is *not established*, and names establishing it as acceptance criterion 1. This
  repository does not own the materializer, so the honest product of this work is a measured
  explanation plus rows filed where the cause lives — not a change to a mechanism `.github` cannot
  reach.
- **Refuting a stated candidate is a result.** `#2380` offered two candidate mechanisms. Reporting
  which of them the evidence kills, and why, is worth more than confirming whichever one is easiest
  to make true. A candidate is refuted by measurement, not by preference.
- **Measure the producer, do not infer it from the consumer.** The absence was observed in a product
  tree; the cause lives in whatever wrote — or was never asked to write — that tree. Every claim
  about emission is read from the emitting artifact (`.template.config/template.json`, a producer
  manifest, a provider declaration, the tree's own `scaffold-provenance.json`), never inferred from
  the shape of what is missing.
- **A predicate is only meaningful against the vocabulary it is evaluated in.** `materializes-when`
  is not a free-standing truth; it is evaluated against one scaffold's `effectiveParameters`. A
  predicate over a parameter that a given tree does not carry has an answer, and that answer is a
  fact about the *pairing*, not about the skill.
- **Run the evaluator; do not reason about it.** Where this repository ships a reference evaluator
  for the predicate grammar (`skill-union-assert.sh --eval-when`), claims about how a predicate
  evaluates are produced by executing it against the real artifact, not by reading its source and
  narrating the expected outcome.
- **Do not manufacture a conclusion.** Where the evidence genuinely cannot separate two mechanisms,
  say so and record what measurement would separate them. Where it can, say which one it kills.

## Scope Boundaries
- In scope: the investigation and its record — this SDD package under
  `work/2380-feedback-report-materialization` and `readiness/2380-feedback-report-materialization`;
  read-only measurement of `FS-GG/FS.GG.Rendering`, `FS-GG/FS.GG.Templates`, `FS-GG/FS.GG.SDD` and
  the one measured product tree `EHotwagner/S.I.R.`; and filing rows at the established cause in the
  repositories that own it.
- Out of scope, deliberately and per `.github#2366` SB-005/SB-006 and this item's own delivery-route
  re-affirmation: changing the scaffold materializer, any `dotnet new` template, any producer
  manifest, or anything under another repository's checkout. Remediation of already-scaffolded trees
  is staged consumer-side work, not a single-repo edit.
- Out of scope: re-materializing or otherwise touching `EHotwagner/S.I.R.`, which is user-owned and
  not org-administered. Whether it is remediated is a stated decision this package routes, not one
  this package executes.
- Out of scope for this item's declared `Paths:`: `registry/skills.yml`, `scripts/skill-union-assert.sh`,
  and `registry/repos.yml`. Defects established in those files are filed, not edited here.
- Keep SDD lifecycle ownership separate from optional Governance enforcement.

## Policy Pointers
- SDD policy comes from `.fsgg/sdd.yml` and `.fsgg/agents.yml`.
- Governance files are optional compatibility pointers and are not evaluated by this command.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd specify --work 2380-feedback-report-materialization`.
