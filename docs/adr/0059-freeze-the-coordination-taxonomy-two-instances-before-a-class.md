# ADR-0059: Freeze the coordination taxonomy — two real instances before a new class

- **Status:** Accepted
- **Date:** 2026-07-20
- **Affects:** FS-GG/.github (owns `registry/skills.yml`, the coordination `Protocol.fs` where scope classes, body-grammar families, and predicate surfaces are defined, and this policy); FS.GG.SDD (owns `Fsgg.Registry`, which learns any vocabulary a new class would add) — the policy governs *when* those two repos may grow a taxonomy, not *how*.
- **Interacts with:** [ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) (the governing principle this operationalizes for *taxonomy* growth specifically — this ADR is its first concrete application); [ADR-0054](0054-workroadmap-delivery-fabric-a-github-authored-product-materialized-driver.md) and [ADR-0057](0057-operator-scope-a-github-authored-never-materialized-skill-class.md) (the `driver` and `operator` scope classes this policy proposes to collapse — see the *proposed first application*, below); [ADR-0017](0017-skill-registry-condition-aware-materialization.md) (the `materializes-when` predicate language a delivery field would replace two classes with); [ADR-0034](0034-typed-coordination-engine.md) (the typed engine whose `Protocol.fs` is the surface this freezes).
- **Source:** [docs/reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md](../reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md) §3E, §7 P5.

## Context

The 2026-07-20 root-cause analysis measured a decision cadence that now **exceeds** the feature
cadence: ten ADRs authored in two days (0048–0057), **two of them (0054, 0057) solely to add one scope
enum value each** — `driver` for `workRoadmap`, `operator` for `drive-board` — and 0056 then re-keying
0053/0054 again. Each was individually well-argued. The aggregate is the §3E friction engine: the
coordination taxonomy accretes classes, body-grammar families (`Paths:`, `Blocked by:`, `Rooms:`,
block/chore sentinels, the registry assertion triple), and predicate surfaces **faster than it retires
them**, so filing one cross-repo request now means ~9 form fields across 3–5 fence-aware grammars and a
4-condition flip gate.

The tell is **one skill per class**. A taxonomy whose classes each have exactly one instance is not a
taxonomy — it is a list of special cases wearing a schema. `driver` (ADR-0054) has one rider,
`workRoadmap`; `operator` (ADR-0057) has one rider, `drive-board`. Both are `.github`-authored skills
that differ only in *where they are delivered* — a product tree (`driver`) versus nowhere (`operator`).
That is a **one-field difference** modeled as **two classes**.

[ADR-0058](0058-adopt-one-governing-principle-derive-dont-restate.md) states the org's governing
principle: *derive, don't restate; do not add a projection you must then hand-maintain.* A scope class
minted to catalog a single skill in place is exactly such a projection — a new parsed surface added
rather than an existing one reused. This ADR applies that principle to the specific case of taxonomy
growth, where the cheapest correct move is almost always **not to grow**.

## Decision

**Adopt a restraint rule on the coordination taxonomy:**

1. **Two-instances rule.** No new **scope class**, **body-grammar family**, or **predicate surface** is
   minted until **at least two real, present cases** demand it. One anticipated case is not two; a
   second *hypothetical* is not a case. Until the second real instance exists, the first is carried by
   the **most general existing surface** that fits, and the ADR says which one and why.

2. **Re-home before you classify.** When a skill needs a home, prefer **re-homing it to a producer
   repo** (which adds zero schema growth — the producer already owns a catalog) over **minting a class
   to catalog it in place**. A new class is the option of last resort, taken only when re-homing is
   ruled out on ownership or delivery grounds *and* the two-instances bar is met.

3. **Reuse a field before you add a surface.** When two cases differ along one axis, model the axis as
   a **field on one class**, not as two classes. A field is data the existing parser already reads; a
   class is a new surface every filer, gate, and reader must now account for.

This is a **policy**, not code. It adds no scope class, no grammar, no predicate, and no gate — which is
the point, and is itself the cheapest thing ADR-0058 asks for (it introduces no projection to
hand-maintain). It changes what a *future* ADR must clear before it may grow the taxonomy.

### Proposed first application (to be executed by a follow-up, not by this ADR)

The `driver` (ADR-0054) and `operator` (ADR-0057) classes are the motivating pair, and the two-instances
rule reads on them directly: each has **one** instance, and they differ along **one** axis — delivery
target. The rule's own prescription is therefore to **collapse them into one class** — a
`.github`-authored, *delivery-varies* class carrying a **delivery field** (`product-tree` | `nowhere`,
the two values ADR-0054's product-materialized target and ADR-0057's `materializes-when: "false"`
already encode) — rather than keep two classes with one rider apiece.

**This ADR proposes that collapse; it does not perform it.** Executing it would amend ADR-0054 and
ADR-0057, retype a `registry/skills.yml` field, and grow `Fsgg.Registry`'s vocabulary — a
`skill-registry` schema change that rides the publish-before-flip rail (ADR-0037, SDD first; ADR-0015
§3) and must record the reciprocal amendment markers in **both** amended records (README house rule 3).
That is its own unit of work, sequenced on the Coordination board, and is deliberately **not** bundled
here: a policy ADR that also rewrites two accepted ADRs is two stories, and the collapse should be
argued and accepted on the strength of *this* rule, not slipped in beneath it. If this policy is
accepted, the collapse becomes a filed follow-up; if it is not, no accepted ADR was disturbed.

## Consequences

- **A future taxonomy-growth ADR must show its second instance, or justify the general surface it
  reused instead.** This is a single question a reviewer asks, complementing ADR-0058's derive-or-justify
  line: *is there a second real case, or is this one skill wearing a schema?*
- **The decision cadence slows toward the feature cadence.** The two ADRs the report singled out
  (0054, 0057) are precisely the ones this rule would have deferred until `drive-board` gave `driver` a
  second sibling — at which point one *delivery-varies* class would have absorbed both with no second
  ADR.
- **No surface is removed by this ADR.** The existing classes stand until the proposed collapse is
  separately accepted and executed. Nothing in flight breaks.
- **It can be withdrawn.** If restraint proves to block a genuinely needed class — a real second
  instance arrives and the general surface cannot carry it — the rule is *satisfied*, not obstructed:
  the ADR that adds the class simply cites its two instances. If the rule itself proves wrong, a
  superseding ADR retires it.

## Alternatives considered

- **Do nothing; trust per-ADR argument.** The status quo. The report measured its outcome: every one
  of 0048–0057 was well-argued and the taxonomy still grew one-instance class by one-instance class,
  because no single ADR review asks the cross-ADR question *"is this the second case, or the first?"*
  Rejected for the same reason ADR-0058 was adopted — the pattern is invisible to one-at-a-time review.
- **A hard freeze — no new classes at all.** Simpler to state, wrong in substance: some second
  instances are real and deserve a class. A blanket ban would push genuine taxonomy needs into abuse of
  an ill-fitting general surface, which is its own drift. The two-instances bar admits real growth while
  refusing speculative growth. Rejected in favour of the graduated rule.
- **Collapse `driver`+`operator` here, in this ADR.** Tempting — the pair is the whole motivation — but
  it conflates *stating the rule* with *executing its first consequence*, and executing it rewrites two
  accepted ADRs (0054/0057) with the bidirectional-amendment obligations that entails. Bundling them
  would make this PR two stories and would let the collapse ride on the policy's acceptance rather than
  being argued on its own. Split out as a proposed follow-up instead.
- **Encode the two-instances rule as a CI gate on ADRs.** The same trap ADR-0058 named: a gate that
  tries to count "real instances" of a proposed class is a new projection of a judgement, hand-maintained
  and drifting. Counting instances is a review judgement, not a mechanical fact. Kept as a review rule.
