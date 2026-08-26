---
title: Implementation status
category: Design
categoryindex: 4
index: 38
description: What is qualified, shipped, pending, and explicitly not yet the default.
---

# Implementation status

As of 2026-08-26, the Quint-first direction is accepted and its Q1 experiment is
qualified, but the target architecture described in this guide is not yet the
production workspace lifecycle.

| Area | Current state |
|---|---|
| Quint-first authority direction | Accepted in [ADR-0077](../adr/0077-quint-first-typed-specification-authority.md) |
| Literate source/profile/tool boundary | Q1 qualified and recorded in the [accepted amendment](../coordination/2026-08-26-adr-0077-q1-qualification-amendment.md) |
| Production Typed SDD backend | Still `fsharp-specification-v1`, manifest v1 |
| Quint backend publication and migration | Pending in [FS.GG.SDD #924](https://github.com/FS-GG/FS.GG.SDD/issues/924) |
| One lifecycle for every workspace | Pending and blocked on #924 in [FS.GG.SDD #927](https://github.com/FS-GG/FS.GG.SDD/issues/927) |
| Quint-backed ADR representation | Planned in [`.github` #3006](https://github.com/FS-GG/.github/issues/3006) |
| Registry and workspace UI adoption | Pending published producer/consumer evidence in [`.github` #2995](https://github.com/FS-GG/.github/issues/2995) |
| GitHub Substrate v2 fleet cutover | Separate staged program; v1 remains authoritative until its protected transition |

Today, `none`, `sdd`, `typed-sdd`, and legacy `spec-kit` retain their shipped
meanings, and an omitted lifecycle still defaults to `sdd`. Existing F# authority
must remain inspectable and reproducible throughout migration. No provider,
registry, consumer, or workspace default may claim Quint support before the exact
producer artifacts are published and downstream package-only qualification passes.

Follow the [migration design](../coordination/2026-08-25-quint-first-typed-sdd-migration-design.md)
and [GitHub Substrate v2 roadmap](../github-substrate-v2-roadmap.md) for live sequencing.
