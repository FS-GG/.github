---
schemaVersion: 1
workId: 2968-new-sdd-workspace-0-10-1-release
title: New Sdd Workspace 0 10 1 Release
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2968-new-sdd-workspace-0-10-1-release/spec.md
publicOrToolFacingImpact: true
---

# New Sdd Workspace 0 10 1 Release Clarifications

## Source Specification
- work/2968-new-sdd-workspace-0-10-1-release/spec.md

## Clarification Questions
No clarification questions recorded.

## Answers
No clarification answers recorded.

## Decisions
- **DEC-001** [FR-001] [AC-001]: The authoritative release source is exactly `264725f374e3f05da46d7c3089462076a1f9bf7a`; the SDD preparation commit is evidence about that source and does not move the tag target.
- **DEC-002** [FR-002] [AC-002]: Publication is host-only and ordered GitHub Packages first, then nuget.org, using one prepared package payload without repacking.
- **DEC-003** [FR-003] [AC-003]: Feed verification compares canonical archive contents excluding only feed-added `.signature.p7s`, confirms nuspec repository commit, and performs a clean isolated tool install/version check.
- **DEC-004** [FR-004] [AC-004]: Registry/docs reconciliation is a post-publication continuation sequenced after `.github#2941`; it is deliberately absent from this preparation branch.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
No blocking ambiguity remains.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2968-new-sdd-workspace-0-10-1-release`.
