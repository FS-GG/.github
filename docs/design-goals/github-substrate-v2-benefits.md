---
title: Why GitHub Substrate v2
category: Design
categoryindex: 4
index: 33
description: Concrete benefits sought from the typed GitHub Substrate v2 migration.
---

# Why GitHub Substrate v2

GitHub Substrate v2 aims to replace convention-heavy coordination with a typed,
testable model while preserving GitHub as the system that records issues, pull
requests, commits, reviews, runs, and merges.

The concrete benefits are:

- one semantic source instead of synchronized body text, Project fields, scripts,
  and agent interpretations;
- explicit issue → proposal → pull request → evidence → accepted-revision lineage;
- semantic diffs and merge checks that detect stale bases, renamed identities,
  changed assumptions, and conflicting declarations;
- model checking, simulation, counterexamples, and implementation replay for the
  parts whose risk justifies them;
- impact-selected CI, so unrelated prose or implementation changes do not trigger
  every expensive verification tier;
- deterministic migration, rollback, provenance, and post-operation verification;
- the same lifecycle and evidence vocabulary across framework and product repos;
  and
- smaller agent discretion at mutation boundaries because typed decisions can
  refuse incomplete or contradictory inputs.

These are acceptance goals, not claims about the current production system. The
[fleet-cutover roadmap](../github-substrate-v2-roadmap.md) keeps v1 authoritative
until the protected v2 transition and retirement gates succeed.
