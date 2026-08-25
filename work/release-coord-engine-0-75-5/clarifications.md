---
schemaVersion: 1
workId: release-coord-engine-0-75-5
title: Coherent coordination release 0.75.5
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/release-coord-engine-0-75-5/spec.md
publicOrToolFacingImpact: true
---

# Coherent coordination release 0.75.5 Clarifications

## Source Specification
- work/release-coord-engine-0-75-5/spec.md

## Clarification Questions
No clarification questions recorded.

## Answers
No clarification answers recorded.

## Decisions
- DEC-001: Select stable `0.75.5`. Live checks on 2026-08-25 found coherent `0.75.4` source/packages,
  a promoted immutable `coherent-set/v0.75.4`, and no `0.75.5` component or coherent-set tag. Existing
  `0.75.4` identities remain immutable and are never repacked or retagged.
- DEC-002: Classify the release as PATCH on the stable `0.x` line. Fresh engine analysis reports two
  unreleased compatible lifecycle defect repairs (`087b6d3d`, `2dd09d83`) and zero commits touching
  `src/FS.GG.Coord.Core/Protocol.fs`; package identities, commands, flags, schemas, and exits remain.
- DEC-003: Keep the three existing release workflow filenames and tag namespaces. Their Trusted
  Publishing policies and receiver resolution are external bindings, and the saga already owns exact
  coherent membership, shared source SHA, prepare-once storage, and GitHub-first dual-feed order.
- DEC-004: Preserve publish-before-flip ordering. Preparation advances the source version and bounded
  release notes. Registry package facts, compatibility projections, and distributed pins advance to
  `0.75.5` only after both feeds serve and verify that version.
- DEC-005: Treat source preparation, saga publication, feed verification, feed-derived reconciliation,
  immutable promotion, clean install, and final freshness as one issue's explicit obligations. The
  claim remains live until those facts converge; upload or workflow conclusion alone is insufficient.
- DEC-006: Typed SDD P5 remains deferred until OperatingV2. This release ships already-merged P0-P4
  behavior and does not alter provider or receiver defaults during cutover preparation.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work release-coord-engine-0-75-5`.
