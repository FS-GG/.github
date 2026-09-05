---
schemaVersion: 1
workId: 3208-fsharp-roadmap-telemetry-projection
title: "Typed F# roadmap telemetry and projection automation"
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Typed F# roadmap telemetry and projection automation Specification

Prose status: specified

## User Value
Deterministic compiled telemetry and bounded roadmap closure replace agent-authored arithmetic, hashes, lifecycle state, and projection text.

## Scope
- SB-001: Implement and publish the `.github`-owned F# telemetry, lifecycle, critique, feedback, summary, and roadmap-close command family; migrate both skills and generated mirrors; and remove the four Python helpers after parity.
- SB-002: Preserve stable success bytes, refusal distinctions, privacy, model/tool provenance, server-order election, and current exit behavior across the migration.
- SB-003: Keep #3211, #3209, #3210, a future FS.GG.Coordination ownership transfer, semantic judgment, and the live #3068 repair-assertion change outside this child.

## Non-Goals
- NG-001: Do not claim or manually complete epic #3211 or implement its later qualification and roadmap-compiler children.
- NG-002: Do not copy a second telemetry/projection implementation into FS.GG.Coordination.
- NG-003: Do not automate semantic critique, finding materiality, policy exceptions, or merge authorization.
- NG-004: Do not race #3068 in its overlapping Kernel and coord-engine-e2e paths; merge-sequence those edits.

## User Stories
- US-001 (P1): As a roadmap operator, I can collect and validate phase telemetry through one packaged compiled authority without agent-authored arithmetic or hashes.
- US-002 (P1): As a roadmap driver, I can render and verify one bounded acceptance projection deterministically from accepted evidence without granting a write.
- US-003 (P1): As a receiver maintainer, I get one publish-before-adopt migration with frozen parity, generated mirrors, and no lingering Python business logic.
- US-004 (P2): As an accountable owner, I can keep unrelated non-required failures outside an already completed unit unless a typed materiality decision reopens it.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001] [FR-002]: Given frozen Codex and Claude records plus lifecycle comments, when the F# CLI collects, exports, seals, validates, summarizes, and re-exports, then successful CSV/JSON bytes and digests match the frozen Python oracle and a second pass is byte-identical.
- AC-002 [US-001] [FR-001] [FR-003]: Given malformed counters, edited comments, a fork, stale source, wrong model/tool identity, invented history, privacy leakage, or pending terminal usage, when the compiled reducer runs, then it returns the corresponding distinct typed refusal and non-green exit.
- AC-003 [US-002] [FR-004]: Given one accepted GS2 receipt and matching review/feedback/check/cycle evidence, when roadmap close inspect/render/verify runs twice, then only the marker-bounded unit block is emitted byte-identically and no filesystem or GitHub write occurs.
- AC-004 [US-002] [FR-004]: Given tampered evidence, a wrong head/gate/source, ambiguous markers, successor scope, or an unchecked unit without a receipt, when roadmap close runs, then it refuses without projecting prose.
- AC-005 [US-003] [FR-005] [FR-006]: Given F# parity is green, when the coherent set is published and receivers are verified, then both skills invoke the packaged F# commands, Claude mirrors are generated from Agents, and all four Python implementations and packaged references are absent at the accepted boundary.
- AC-006 [US-003] [FR-007]: Given clean receiver checkouts and both package feeds, when package verification runs, then the immutable package supplies all validators and no candidate-tree helper is selected.
- AC-007 [US-004] [FR-008]: Given an unrelated non-required failed check after unit completion, when closure evaluates it, then it emits a separately owned obligation and does not reopen the unit without a typed materiality decision.
- AC-008 [US-001] [FR-009]: Given public lifecycle output, when inspected for provenance, then it contains aggregates and stable digests but no raw conversation/runtime content or absolute local paths.

## Functional Requirements
- FR-001: Add closed F# domain types and pure reducers for runtime usage, lifecycle events and comment election, critique schema v3, feedback schema v2/audit, telemetry summaries, roadmap closure, and bounded projection. (Stories: US-001; Acceptance: AC-001, AC-002)
- FR-002: Expose `telemetry usage collect`, `telemetry lifecycle export-comments|seal-successor|validate`, `telemetry summarize`, and `roadmap close inspect|render|verify` through one coherent CLI family. (Stories: US-001, US-002; Acceptance: AC-001, AC-003)
- FR-003: Preserve the stable CSV header, compact canonical JSON, SHA-256 chain, token arithmetic, provider/model/tool binding, duration/history rules, GitHub server-order fork election, and established exit behavior. (Stories: US-001; Acceptance: AC-001, AC-002)
- FR-004: Roadmap rendering is pure and marker-bounded; it joins accepted receipt, candidate/merge/check/review/feedback/cycle identities and refuses tampering, ambiguity, successor authority, and unaccepted input. (Stories: US-002; Acceptance: AC-003, AC-004)
- FR-005: Freeze every current Python positive fixture and rejection mutation, require differential parity, and retain independent black-box inversion controls. (Stories: US-003; Acceptance: AC-005)
- FR-006: Publish the compiled coherent set before migrating pnext-item and work-roadmap callers; any compatibility launcher is logic-free, receiver-proven, and limited to one coherent release. (Stories: US-003; Acceptance: AC-005, AC-007)
- FR-007: Generate `.claude` mirrors from `.agents`; delete collect-runtime-usage.py, validate-lifecycle-log.py, validate-critique-state.py, validate-feedback-state.py, and every package/registry reference at the accepted compatibility boundary. (Stories: US-003; Acceptance: AC-005, AC-006)
- FR-008: Classify unrelated non-required failures as separately owned obligations and forbid automatic reopening without a typed materiality record. (Stories: US-004; Acceptance: AC-007)
- FR-009: Runtime adapters never export raw runtime content or absolute local paths; public records contain only aggregates, stable content digests, and runtime identifiers. (Stories: US-001; Acceptance: AC-008)
- FR-010: Keep semantic adequacy, novel finding disposition, exceptions, and external merge authorization as independently reviewed inputs rather than reducer outputs. (Stories: US-002, US-004; Acceptance: AC-003, AC-007)
- FR-011: Run warning-as-error builds, Core/CLI unit suites, engine E2E writes, skill/projection/package gates, clean-receiver smoke tests, two-pass replay, tamper controls, and final helper-absence search before delivery. (Stories: US-001, US-003; Acceptance: AC-001, AC-002, AC-005, AC-006)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 3208-fsharp-roadmap-telemetry-projection`.
