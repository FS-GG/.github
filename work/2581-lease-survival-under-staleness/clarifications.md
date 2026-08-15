---
schemaVersion: 1
workId: 2581-lease-survival-under-staleness
title: Lease Survival Under Staleness
stage: clarify
changeTier: tier1
status: clarified
sourceSpec: work/2581-lease-survival-under-staleness/spec.md
publicOrToolFacingImpact: true
---

# Lease Survival Under Staleness Clarifications

## Source Specification
- work/2581-lease-survival-under-staleness/spec.md

## Clarification Questions
- CQ-001 [AMB:AMB-001] blocking answered: May a STALE engine write a lease renewal at all (`#929`, `#1507`)?
- CQ-002 [AMB:AMB-002] blocking answered: Why not a grace period on the lease when the refusal itself is what blocked the worker?
- CQ-003 [AMB:AMB-003] blocking answered: Why not have the host repair reset the lease as part of unblocking?
- CQ-004 [AMB:AMB-004] blocking answered: If not an exemption, what mechanism actually keeps the claim alive — and is it a fix or a workaround?
- CQ-005 [AMB:AMB-005] blocking answered: Does the recovery route reopen `#929`/`#709` through a different door?

## Answers

- CQ-001 answer: **No, and this is measured rather than argued.** `Writes.heartbeat`
  (`src/FS.GG.Coord.GitHub/Writes.fs:811-828`) does not append a timestamp — it PATCHes the marker
  comment, and a PATCH rewrites the WHOLE body. The body is recomposed by `markerBody`
  (`Writes.fs:93-121`) out of the running build's own idea of the grammar:
  `worker=`, `lease=`, `renewed=`, and then `session=`, `prev=` and `pathRepo=` emitted conditionally.
  An engine whose build predates any of those fields cannot emit it, so it silently DROPS it from a
  claim that is currently LIVE. That is not a hypothetical: the same rewrite dropped `prev=` (`#550`),
  losing the column the claim had overwritten, and dropped `session=` (`#1149`), after which
  `twinSession` could no longer catch a same-id twin and **two workers ended on one item** — the
  double-hold the CAS exists to prevent. `Writes.fs:816-822` records both in as many words.

  A stale engine is by construction one whose build predates commits on `main`. Permitting it to rewrite
  a live claim's marker is precisely the `#929`/`#1507` trade `stale_guard` exists to refuse, and it is
  worst on this particular write: a lease renewal is *expected* to leave the marker looking the same, so
  a field silently vanishing from it is the least likely corruption to be noticed. The lesser argument
  for the exemption — that a renewal asserts "still working" rather than a state transition, and is
  therefore the least semantically loaded board write — is true about the *assertion* and false about
  the *mechanism*, and the mechanism is what corrupts.

- CQ-002 answer: The lease is a TIMER, and `.github#976` already ratified that the fleet does not make
  that clock outage-aware — `Protocol.fs:706` states it: *"it cannot see a REST outage, and `heartbeat`
  is REST, so an outage on the lock's budget spends a lease nobody can renew … What answers instead is
  evidence."* A staleness grace period is the same request with a different cause, and it fails the same
  way: the clock cannot distinguish a worker blocked by a refusal from a worker that has stopped
  existing, so the grace would be granted to both, and `reap`'s proof-of-life gate (`#581`) is the
  mechanism that already tells those apart. Worse, a grace keyed on "a refusal happened" would have to be
  RECORDED somewhere a later reader trusts — which is a board write, which is the thing being refused.

- CQ-003 answer: It makes lease survival depend on the very actor the worker is blocked on. The measured
  complaint in `.github#2549` and `.github#2563` is not that the repair is slow, it is that the wait is
  **unbounded and owned by somebody else**; adding "and your lease is restored when they get to it" does
  not bound it, it adds a second thing to wait for. It also leaves the worker with nothing to do between
  the refusal and the host's attention, which is exactly the window both incidents died in. Rejected as a
  mechanism; retained as a courtesy the host may still perform.

- CQ-004 answer: **Give the blocked worker a CURRENT engine they own, and say so at the point of
  refusal.** The route already exists and is three tiers above the guard in the same resolver: an
  explicit `FSGG_COORD_ENGINE_BIN` is tier 1, handled at `scripts/fsgg-coord:156-159` *before* `TOP` is
  computed, so it never reaches `guards` and never consults staleness —
  `tests/coord-engine-parity/shim.sh:280-286` asserts exactly that ("an explicit
  FSGG_COORD_ENGINE_BIN is honoured silently — an instruction, not a hint"). A worker can therefore
  `git worktree add --detach` the ref this guard measured, build the engine there, export the path, and
  renew its lease through an engine that is CURRENT — touching no shared state, moving no head that an
  independent critic may already have confirmed, and needing no host action.

  So the root cause is not "the guard refuses `heartbeat`". It is that the refusal composes exactly ONE
  remedy — `git -C $top merge --ff-only $b` then `dotnet build $top/...` — and at tier 2b `$top` is the
  SHARED checkout, the one checkout the worker was instructed to hold. A diagnostic that names a remedy
  it structurally cannot deliver to this reader, while the remedy that works sits in the same file and is
  never printed, is `#266`'s class. This item repairs the diagnostic, not the partition.

  **It is a fix and not a workaround, on this file's own standard**, which is that a rule enforced by
  whoever happens to remember it decays (`#570`): tier 2b exists at all because `#931` refused to leave
  "run coord from the shared checkout" as a tribal note that every bitten worker carried privately. The
  same argument applies unchanged here — the route being technically available and never printed is what
  cost `.github#2549` and `.github#2563` their leases.

- CQ-005 answer: Partly, and it is named rather than hidden. Tier 1 bypasses `dirty_guard` as well as
  `stale_guard`, by design ("an instruction, not a hint"), so a worker who points it at a *stale* engine
  gets no warning at all. That is why the printed route says to BUILD a current engine at the ref the
  guard just measured rather than to point at any engine lying around, and why the route names
  `--detach` — a rebase of the item branch would clear the refusal at tier 2a but move a head that may
  already carry a critic's confirmation, which the guard's own `.github#2402` block already forbids.
  The residual exposure is a worker who deliberately names an engine they did not build; that exposure
  is tier 1's pre-existing contract and is not introduced here.

## Decisions

- DEC-001 [CQ-001] [AMB:AMB-001]: **NO EXEMPTION. A STALE ENGINE MAY NOT WRITE A LEASE RENEWAL.**
  `heartbeat` stays in `BOARD_WRITES` and stays refused, because `Writes.heartbeat` rewrites the whole
  marker body from `markerBody`'s current grammar and an older build drops fields it cannot emit —
  demonstrated twice already, on `prev=` (`#550`) and on `session=` (`#1149`, a double-hold). REJECTED —
  **move `heartbeat` to a fourth, exempt set**: it would also be laundering, because the engine's own
  `command-contract` reports `writes: always` for it and `tests/coord-engine-parity/shim.sh:384-393`
  asserts the shim's write membership equals that contract, so the set would have to state something
  false to hold the exemption. REJECTED — **argument-aware exemption** (permit the renewal but not other
  writes on the same verb): `heartbeat` has no flag to key on, and the file already refuses guards whose
  correctness depends on argv shape (`fsgg-coord-guards.sh:225-233`).
- DEC-002 [CQ-002] [AMB:AMB-002]: **NO LEASE GRACE PERIOD.** The clock stays staleness-unaware, on
  `.github#976`'s ratified reasoning; evidence (`#581`'s open-PR proof of life), not the timer, is what
  separates a blocked worker from an abandoned one.
- DEC-003 [CQ-003] [AMB:AMB-003]: **NO HOST-SIDE LEASE RESET AS THE MECHANISM.** It does not bound the
  wait; it adds a dependency to it.
- DEC-004 [CQ-004] [AMB:AMB-004]: **THE REFUSAL BECOMES REGIME-AWARE AND PRINTS THE SELF-SERVICE
  RECOVERY ROUTE.** Three parts, all inside `stale_guard`'s already-slow refusal path: (a) when `$top`
  IS the shared checkout, say so and say that it may be host-owned and must not be repaired by a worker
  told to hold — the mirror of the `.github#2402` block that today speaks only when `$top` is NOT
  shared; (b) print the tier-1 route (`git worktree add --detach`, build, export
  `FSGG_COORD_ENGINE_BIN`) and the tier-2a alternative with its head-movement precondition; (c) for the
  lease-renewal verb, name the lease consequence — this refusal can outlive the lease, and an expired
  lease cannot be renewed in place (`Protocol.fs:704`).
- DEC-005 [CQ-005] [AMB:AMB-005]: **THE ROUTE SAYS "BUILD", NOT "POINT AT SOMETHING".** Tier 1's
  guard-free contract is retained unchanged and its cost is stated in the file rather than mitigated by
  a new check, which would contradict "an instruction, not a hint".
- DEC-006 [CQ-004] [AMB:AMB-004]: **THE `:134-138` JUSTIFICATION IS REGIME-QUALIFIED, NOT DELETED.** The
  sentence records a real trade and stays; what is added is which regime makes it true, and the
  statement that under host-serialised repair the remedy is not the worker's and the stall is unbounded.
  A comment asserting a property the code does not have is a defect in its own right (`#1059`'s class),
  and this file already repairs two of its own on that basis.

## Accepted Deferrals
No accepted deferrals recorded.

## Remaining Ambiguity
None. All five blocking ambiguities are resolved by DEC-001 through DEC-006 above.

## Lifecycle Notes
- Next lifecycle action: `fsgg-sdd checklist --work 2581-lease-survival-under-staleness`.
