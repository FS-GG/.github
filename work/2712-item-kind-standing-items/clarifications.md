---
schemaVersion: 1
workId: 2712-item-kind-standing-items
title: Item Kind and the standing-item reducer exemption
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2712-item-kind-standing-items/spec.md
publicOrToolFacingImpact: true
---

# Item Kind and the standing-item reducer exemption Clarifications

## Source Specification
- work/2712-item-kind-standing-items/spec.md

## Clarification Questions
- Q-001 [AMB-001]: Is the exemption derived inside `lifecyclePolicyIntent`, or above the watermark read?
- Q-002 [AMB-002]: What does a body declaring more than one `Kind:` value resolve to?
- Q-003 [AMB-003]: What does the engine do when the body could not be read, so no kind is observable?

## Answers
- A-001 [Q-001]: Above the watermark read, as a required argument to the reducer itself. `.github#2690`'s PR body suggests the intent function; its independent critic's probe 3 measured the opposite and the packet is the authority.
- A-002 [Q-002]: `work` dominates.
- A-003 [Q-003]: The engine behaves exactly as it does today for a body it could not read, and this is stated as a bounded residual rather than silently closed.

## Decisions
- DEC-001 [AMB:AMB-001] The exemption is a required positional argument to `LifecycleProjection.reduce`/`advance` and is decided *before* the persisted watermark is consulted — never an arm inside `lifecyclePolicyIntent`, and never minted from a `Status` write. Both alternatives are refuted by measurement, not by preference. An exemption minted from a `Status` write would be an `IntentRecord` on a watermark and would inherit `Client.fs:2492`'s freeze (a watermark's mere existence suppresses policy re-derivation), so a row whose `Kind` later changed could never leave its exemption. An exemption derived inside `lifecyclePolicyIntent` is suppressed by *any* pre-existing watermark, and since `.github#2690` every `add`-filed row has one: independent critic `avocet-e644`'s probe 3 on PR #2718 measured exactly this for the sibling `Class`-derived arm — a `Class: decision` row carrying the `Backlog` receipt `add` now mints settles at `Backlog` and never reaches `Blocked`, while the identical row with no receipt reaches `Blocked`. Making the kind a *parameter* rather than an *intent* is what makes the exemption structurally unreachable by any receipt: the watermark carries `Intent`, never `Kind`, and the kind is re-read from the item's body on every pass.
- DEC-002 [AMB:AMB-002] `work` dominates when a body declares more than one `Kind:` value, and the search is ordered rather than taking the first line — `Class.fromBody`'s shape exactly, with its dominance inverted for the same underlying rule. There the strongest claim is "something is broken NOW" and must not be quietly downgraded; here the strongest claim is "this row is ordinary work", because exemption is the powerful and irreversible-looking outcome and an ambiguous declaration must resolve toward the reading that keeps the row *under* the machinery, never toward the one that removes it. A body that says both `Kind: work` and `Kind: register` is a body nobody can trust to be exempt.
- DEC-003 [AMB:AMB-003] A body that could not be read yields `Kind = None`, and `None` means `Work` at the reducer — which is exactly today's behaviour for that row and therefore introduces no new failure. This is deliberately NOT closed by letting the board `Kind` column govern: ADR-0066 and `Schedulability.fs:126` both refuse to let a lagging projection decide, and `Class` collapses "no declaration" with "body unread" for the same reason. The residual is stated rather than hidden: a standing row whose body could not be read is not *known* to be standing, and the engine treats it as it does today (`TouchSet.Unreadable` → `Deferred` → `Backlog`). Closing it needs a fact the scan can carry that a failed body read cannot destroy, which is a separate cause and not this row's.

## Accepted Deferrals
- DEF-001: The watermark freeze itself (`Client.fs:2492`) is not repaired here — packet `.github#2691` `5309171168`, incremented by `5309263730`. This work routes *around* it rather than through it, which is why DEC-001 is a parameter and not an intent.

## Remaining Ambiguity
None. All three blocking ambiguities are resolved by decision above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2712-item-kind-standing-items`.
