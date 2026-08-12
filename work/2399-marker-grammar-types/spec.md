---
schemaVersion: 1
workId: 2399-marker-grammar-types
title: Independent-review marker grammar types
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Independent-review marker grammar types Specification

Prose status: specified

## User Value
A reader or implementer of the independent-review marker protocol gets a total, typed answer for whether a marker occurrence in a comment body is live, quoted, or competing, instead of a hand-projected prose rule re-implemented per call site.

## Scope
- SB-001: Replace the QuotedMarkerRule prose string in Protocol.fsi with a typed MarkerAnchor/MarkerOccurrence/Marker model and an occurrences function; declare an anchor for every marker in the family; regression-cover the 83758ec3 and d86ded2a defect classes; emit the grammar via facts --json for .github#2369. .github#2392's receipt-binding defect is out of scope; the emitted wire bytes must not change.

## Non-Goals
- SB-002: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories
- US-001 (P1): As a user, I can A reader or implementer of the independent-review marker protocol gets a total, typed answer for whether a marker occurrence in a comment body is live, quoted, or competing, instead of a hand-projected prose rule re-implemented per call site.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given Independent-review marker grammar types is available, when the user exercises it, then they can A reader or implementer of the independent-review marker protocol gets a total, typed answer for whether a marker occurrence in a comment body is live, quoted, or competing, instead of a hand-projected prose rule re-implemented per call site.

## Functional Requirements
- FR-001: Protocol.MarkerContract carries no QuotedMarkerRule string field; the quoted-versus-competing rule is a total function 'occurrences: Marker -> string -> MarkerOccurrence list' tested directly; every marker declares a MarkerAnchor and an AnywhereInBody anchor requires a justifying comment; tests against occurrences cover the 83758ec3 and d86ded2a regressions and fail if the anchor is widened; scripts/fsgg-coord facts --json emits the marker field grammar; a before/after diff of facts --json proves the emitted bytes are unchanged. (Stories: US-001; Acceptance: AC-001)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2399-marker-grammar-types`.
