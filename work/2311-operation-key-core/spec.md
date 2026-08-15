---
schemaVersion: 1
workId: 2311-operation-key-core
title: Operation Key And Closed Vocabulary In The Pure Core
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Operation Key And Closed Vocabulary In The Pure Core Specification

Prose status: specified

## User Value

A worker implementing any later slice of the GitHub-native executor fencing design has one closed,
pure vocabulary for consequential operations and one total digest composition over
`(item, gen, receiver, op)`, so idempotence is keyed the same way at every call site instead of being
re-spelled per site.

`.github#1858`'s replacement-plan step 1 asks for a GitHub-hosted operation identity. The design
(`docs/reports/2026-08-04-github-native-executor-fencing-design.md` §3) splits that into two questions
with two different answers: **mutual exclusion** is answered by the *subject* — one lock issue per
receiver, §4.1 — and **idempotence** by the *opkey*, recorded in the effect receipt (§4.3) and checked
by the receiver, so *"a repeat of the same `(item, gen, receiver, op)` finds a receipt and collapses."*
Slices 2–6 all consume that key. It does not exist yet, and until it does each of them would spell its
own — which is precisely the `#485` shape (*"one rule computed in two places agrees at first and drifts
later"*) applied to the fencing protocol's central identifier.

This slice is ordered first because it is pure: no IO, no transport, no board contact. Nothing here
fences anything on its own.

## Scope

- SB-001: `src/FS.GG.Coord.Core/Operation.fs` and `src/FS.GG.Coord.Core/Operation.fsi` — the closed
  `Operation` vocabulary and its wire spelling, the `OpKey` type, and the digest composition over
  `(item, gen, receiver, op)`.
- SB-002: The two explicit-compile-list project files that must name the new sources:
  `src/FS.GG.Coord.Core/FS.GG.Coord.Core.fsproj` and
  `tests/FS.GG.Coord.Core.Tests/FS.GG.Coord.Core.Tests.fsproj`. Both enumerate every source file with
  `<Compile Include=…/>` and carry no globs, so a new file that is not listed is not compiled.
- SB-003: `tests/FS.GG.Coord.Core.Tests/OpKeyTests.fs`, covering composition, closed-vocabulary
  exhaustiveness, one negative case per component, and the purity of the compiled reference graph.

## Non-Goals

- SB-004: **The opkey is never written into a CAS marker.** §4.1 is emphatic that the write path
  *"gains no code, no prefix, no field, no parameter"*, and that `pathRepo=` is deliberately not reused
  to smuggle it — that field has a defined meaning (`Marker.PathRepo`,
  `src/FS.GG.Coord.GitHub/Reads.fsi:44`) and *"overloading a parsed field with a second meaning is the
  drift class this design refuses everywhere else."*
- SB-005: **No IO, no transport, no board contact, and no fencing behaviour.** The dispatch grant
  (§4.1), the merge election (§4.2), the effect receipt (§4.3) and the merge gate (§6) are slices 2–6.
  This slice changes no existing decision anywhere in the engine.
- SB-006: **No existing Core module is edited**, `Delivery.fs`/`Delivery.fsi` included. They are outside
  the declared touch-set. The consequence for the SHA-256-hex primitive is recorded as a decision in
  the clarification record rather than silently absorbed.
- SB-007: Do not implement later lifecycle commands or Governance enforcement in this specification.

## User Stories

- US-001 (P1): As the implementer of a later fencing slice, I can name a consequential operation from a
  closed vocabulary and compose its operation key from `(item, gen, receiver, op)` in one call, so that
  two slices keyed on the same operation produce the same key by construction.
- US-002 (P1): As a reader of `FS.GG.Coord.Core`'s signature file, I can tell for each exported item
  which question it answers — exclusion or idempotence — so that I do not reach for the key where the
  subject is the answer, or the reverse.
- US-003 (P1): As a reviewer, I can rely on the vocabulary being closed by the *compiler*: adding a
  fourth operation breaks every consumer that does not handle it, rather than silently failing to
  dedupe at the one call site that was missed.
- US-004 (P1): As a reviewer, I can see that the digest genuinely depends on all four components,
  established over the pre-image by construction rather than by a handful of examples that a constant
  function would also pass.
- US-005 (P2): As a maintainer of the ADR-0034 purity boundary, I can rely on this slice's purity being
  a property of the compiled reference graph, so a later edit that reaches for a GitHub type reds a test
  instead of passing an inspection.

## Acceptance Scenarios

- AC-001 [US-001] [FR-003]: Given the same `(item, gen, receiver, op)` values, when `OpKey.compose` is
  called twice with them, then the two keys are equal, and the key is a lowercase 64-character hex
  SHA-256 digest.
- AC-002 [US-004] [FR-003]: Given a baseline tuple, when exactly one of the four components is changed
  and the other three are held fixed, then the resulting key differs from the baseline — for each of
  the four components independently, and for every case of the operation vocabulary.
- AC-003 [US-004] [FR-003]: Given the set of tuples formed by varying each component over several
  values, when their pre-images are rendered, then all pre-images are pairwise distinct and all
  resulting keys are pairwise distinct — the injectivity claim established over the whole set, not over
  a chosen pair.
- AC-004 [US-004] [FR-003]: Given a component value that itself contains the pre-image's field
  separator, when it is composed, then it cannot be made to collide with a different tuple whose fields
  are split differently — separator injection is refused at construction rather than hashed.
- AC-005 [US-003] [FR-002]: Given the `Operation` vocabulary, when a total function over it is written
  without a wildcard arm, then every case is named; and when a case is removed from that function, the
  project fails to compile. The vocabulary admits no unknown literal at runtime: there is no
  `parse: string -> Operation` that could return a case for an unrecognized spelling.
- AC-006 [US-002] [FR-001]: Given `src/FS.GG.Coord.Core/Operation.fsi`, when it is read, then each
  exported type and function carries a doc comment naming the invariant it holds and which question —
  exclusion or idempotence — it answers.
- AC-007 [US-005] [FR-004]: Given the compiled `FS.GG.Coord.Core` assembly, when its referenced
  assemblies are enumerated at runtime, then no reference names `FS.GG.Coord.GitHub`, any transport, or
  any HTTP/GitHub client library — the check reads the reference graph, not the source text.
- AC-008 [US-001] [FR-005]: Given each assertion this slice adds, when the behaviour it asserts is
  inverted, then the suite reds and the exact mutation and observed red are recorded on the pull
  request.
- AC-009 [US-001] [FR-006]: Given any of the following, when it is offered to composition, then the
  call returns a typed refusal naming the offending component rather than a key: an item in the board's
  `<repo>#N` shorthand (`item` is spelled `owner/repo#N` and never the shorthand GitHub's grammar does
  not parse, `.github#2107`); a receiver that is not `owner/repo`; a generation that is not a
  server-assigned decimal comment id, the engine's `released` sentinel included; a blank component; and
  a component carrying a control character.

## Functional Requirements

Each requirement is one physical line, because the checklist coverage scan reads one physical line and does not join continuations.

- FR-001: `OpKey` and the operation vocabulary are exported from `FS.GG.Coord.Core` through an `.fsi` that names, for each exported item, the invariant it carries and which question it answers — exclusion or idempotence. (Stories: US-002; Acceptance: AC-006)
- FR-002: The operation vocabulary is a closed discriminated union with no catch-all case and no `string -> Op` parse that could admit an unknown literal, so adding a case is a compile error at every consumer. (Stories: US-003; Acceptance: AC-005)
- FR-003: The digest composition is a total, deterministic function of `(item, gen, receiver, op)`: equal inputs give equal keys, and any one component differing gives a different key, proved by construction over the pre-image rather than by example. (Stories: US-001, US-004; Acceptance: AC-001, AC-002, AC-003, AC-004)
- FR-004: No file in this slice references a transport, a GitHub type, or `IGitHubTransport`, demonstrated from the compiled assembly's own reference graph rather than by source inspection. (Stories: US-005; Acceptance: AC-007)
- FR-005: Every assertion added here is inverted at authoring time and its red recorded. (Stories: US-001; Acceptance: AC-008)
- FR-006: Composition is total: every input that cannot yield a well-formed key is refused as a typed value the caller must handle, never silently hashed — a blank or control-character-bearing component, an `item` in the board's `<repo>#N` shorthand, a receiver that is not `owner/repo`, and a generation that is not a server-assigned decimal comment id are all refusals. (Stories: US-001; Acceptance: AC-009)

## Ambiguities

- AMB-001: The design says *"Reuse `Delivery.digest` rather than writing a second one"* (§3.3), but
  `Delivery.digest` is `private` (`src/FS.GG.Coord.Core/Delivery.fs:99`) and `Delivery.fs`/`.fsi` are
  outside this slice's declared touch-set. What does this slice do about the SHA-256-hex primitive?
- AMB-002: `gen` is defined as *"the comment id of the winning `fsgg:claim` marker"*, and the engine
  substitutes the literal `released` when there is no marker (`Client.fs`). May a key be composed on
  `released`?
- AMB-003: The pre-image is `item \n gen \n receiver \n op`. What stops a component that itself
  contains a newline from making two different tuples render one pre-image?
- AMB-004: How is "adding a case is a compile error at every consumer" tested, given a test cannot
  compile a hypothetical fourth case?
- AMB-005: What exactly does the reference-graph check assert, and what is its inversion?
- AMB-006: Should `OpKey`'s representation be hidden so `compose` is the only producer?

## Public Or Tool-Facing Impact

- `FS.GG.Coord.Core` gains a new public module. The assembly is `IsPackable=false` and is consumed only
  by `FS.GG.Coord.Cli` in-repo, and `src/FS.GG.Coord.Core` is not a `kit:` source in
  `registry/repos.yml`, so no published kit payload changes and no coherent-set version bump is
  implied by this slice on its own.
- This specification is an SDD lifecycle artifact and command-report contract input.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2311-operation-key-core`.
