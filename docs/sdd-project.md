---
title: SDD project
category: FS.GG
categoryindex: 6
index: 6
description: Scope and boundaries for the separate FS.GG.SDD lifecycle project.
---

# SDD project

FS.GG.SDD is the spec-driven development lifecycle product for FS.GG. It owns
the authoring and structured artifact flow around project charter, specify,
clarify, checklist, plan, tasks, evidence declarations, normalized work model,
generated views, and agent guidance.

It is separate from FS.GG.Governance. Governance owns the optional rule engine,
evidence freshness, route/profile logic, and gate enforcement. SDD may emit
artifacts that Governance can inspect, but SDD must remain useful without
Governance installed.

## Scope

FS.GG.SDD may own:

- lifecycle artifact schemas;
- normalized work-model generation;
- generated SDD views;
- lifecycle CLI commands;
- Claude and Codex command/skill generation;
- migration helpers for existing Spec Kit projects;
- optional readiness artifacts consumed by Governance.

It should not own:

- rule-engine evaluation;
- gate enforcement policy;
- rendering package identities;
- rendering templates or design-system decisions;
- product-specific docs URLs or release choices.

## Development stance

Develop FS.GG.SDD with standard Spec Kit and the F# constitution. Markdown is an
authoring surface; schema-versioned structured artifacts are the machine
contract.

The repository starts scaffold-only. Product code should arrive through feature
specs, beginning with the artifact model and normalized work model before
lifecycle commands.
