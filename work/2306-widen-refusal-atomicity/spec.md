---
schemaVersion: 1
workId: 2306-widen-refusal-atomicity
title: Widen Refusal Atomicity
stage: specify
changeTier: tier1
status: specified
publicOrToolFacingImpact: true
---

# Widen Refusal Atomicity Specification

Prose status: specified

## User Value
A `widen` or `set-paths` call that must refuse (for example on `OVERLAP`) leaves the item's declared `Paths:` byte-identical to what they were before the call, so a caller that gates on the non-zero exit code — which is what every worker is instructed to do — holds a true belief about the item's declaration, and the scheduler's overlap guarantee is not defeated by the very refusal meant to protect it.

## Scope
- SB-001: Refusal atomicity for `widen` in `FS.GG.Coord.Core/TouchSet.fs` / `TouchSet.fsi` and the CLI call site in `FS.GG.Coord.Cli/Client.fs`.
- SB-002: All-or-nothing semantics for a partially-colliding widen request (one requested path collides, another does not), stated explicitly as the chosen contract in `TouchSet.fsi`, not left implicit.
- SB-003: The same failure mode checked in `set-paths`, which writes the same `Paths:` field, and fixed or explicitly cleared with evidence.
- SB-004: One-off recovery of `.github#2248`'s already-inflated declaration (and any other item inflated the same way) to the paths it legitimately holds, with its delivery-route receipt re-affirmed afterward.
- SB-008: `tests/FS.GG.Coord.Cli.Tests/ApplicationServiceTests.fs`'s pinned REST-call count for an OVERLAP `widen` (currently 7, counting the body PATCH the defect makes) is corrected to the post-fix count, because that count is itself an assertion of the defect this item removes. Widened into this item's `Paths:` (disjoint widen, verdict recorded) rather than left for a second item, since the fix and the assertion it invalidates are one change.

## Non-Goals
- SB-005: Do not change the overlap-detection rule itself (`#353`'s guarantee that two claims must not hold one path) — only the mutation-on-refusal defect around it.
- SB-006: Do not implement Governance enforcement or later lifecycle commands here.
- SB-007: Recovery of `.github#2248` is a one-off board-data repair, not a generalized reconciliation tool; item-body edits and delivery-route re-affirmation for that repair are host actions, not part of the code change.

## User Stories
- US-001 (P1): As a worker calling `widen` or `set-paths`, I want a refused call to leave the item's declared `Paths:` unchanged, so that gating on the exit code is a reliable signal about the item's declaration.
- US-002 (P2): As a coordinator, I want `.github#2248`'s declaration reduced to what it legitimately holds and its delivery-route receipt re-affirmed, so the board's overlap reasoning reflects reality rather than refused-widen residue.

## Acceptance Scenarios
- AC-001 [US-001] [FR-001]: Given an item with a current declared `Paths:`, when a `widen` request collides on every requested path, then the call exits non-zero and the item's `Paths:` body is byte-identical to its value before the call.
- AC-002 [US-001] [FR-002]: Given an item with a current declared `Paths:`, when a `widen` request names two or more paths and at least one (but not all) collides, then the whole call refuses and writes nothing — no disjoint subset is committed — and `TouchSet.fsi` states this all-or-nothing choice explicitly as the contract, not merely as incidental behavior.
- AC-003 [US-001] [FR-003]: Given `set-paths` writes the same `Paths:` field as `widen`, when a `set-paths` request would produce a colliding declaration, then it is refused with no mutation, exercising the same shared refusal path (or the absence of the defect there is demonstrated and explicitly recorded).
- AC-004 [US-002] [FR-004]: Given `.github#2248`'s declaration was inflated by refused widens to include paths it never legitimately held, when the repair is applied, then its `Paths:` is reduced to the paths it legitimately holds and its delivery-route receipt is re-affirmed against the corrected body.
- AC-005 [US-001] [FR-005]: Given the fix is in place, when a genuinely disjoint widen request is made (no requested path collides with any held claim), then the call still succeeds and still writes the expanded `Paths:` — the fix does not turn a legitimate widen into a no-op.

## Functional Requirements
- FR-001: A `widen` request that refuses because every requested path collides commits zero declaration changes; proven by asserting the item body's `Paths:` line is byte-identical before and after the refused call, not merely that the exit code is non-zero. (Stories: US-001; Acceptance: AC-001)
- FR-002: A `widen` request that refuses because only some requested paths collide refuses the entire call and writes nothing (all-or-nothing), and `TouchSet.fsi` documents this as the chosen semantics rather than leaving it to be inferred from the implementation. (Stories: US-001; Acceptance: AC-002)
- FR-003: `set-paths` is checked for the identical failure mode (writing the field before or independent of the overlap verdict) and is fixed to be atomic on refusal, or the check's negative result is recorded explicitly with the evidence that established it. (Stories: US-001; Acceptance: AC-003)
- FR-004: `.github#2248`'s declared `Paths:` is reduced, via a one-off host-performed body edit, to the paths it legitimately holds, and its delivery-route receipt is re-affirmed against the corrected body's revision; any other item found inflated the same way is identified and reported for the same repair. (Stories: US-002; Acceptance: AC-004)
- FR-005: A genuinely disjoint `widen` request (no collision on any requested path) still succeeds and still writes the expanded declaration; the atomicity fix changes only the refusal path, not the success path. (Stories: US-001; Acceptance: AC-005)

## Ambiguities
No material ambiguities recorded.

## Public Or Tool-Facing Impact
- `TouchSet.fsi` is published coord-engine surface that receiver repos pin (per the recorded delivery-route rationale); FR-002's all-or-nothing semantics is a public-contract decision stated in that signature file, not an internal-only change.
- `FS.GG.Coord.Cli/Client.fs`'s `widen`/`set-paths` command surfaces are tool-facing: their exit-code-and-no-mutation contract is what every worker script already depends on.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd clarify --work 2306-widen-refusal-atomicity`.
