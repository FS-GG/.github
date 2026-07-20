# ADR-0058: Adopt one governing principle — *derive, don't restate; gate capabilities, not declarations*

- **Status:** Accepted
- **Date:** 2026-07-20
- **Affects:** FS-GG/.github (owns the ADR process, `registry/`, and the coherence gates this principle judges); every FS-GG repo (the principle is the test each future cross-repo ADR must pass, and the lens on each new gate/projection/scope).
- **Interacts with:** [ADR-0044](0044-generated-artifacts-are-derived-from-their-generators.md) (this principle, already applied at the generated-artifact seam — the proof that it is implementable, not a slogan); [ADR-0037](0037-schema-growth-is-publish-before-flip.md) and [ADR-0015](0015-register-the-registry-schema-as-a-governed-contract.md) (the publish-before-flip rail the principle would shorten); [ADR-0054](0054-workroadmap-delivery-fabric-a-github-authored-product-materialized-driver.md) / [ADR-0057](0057-operator-scope-a-github-authored-never-materialized-skill-class.md) (the scope-class accretion the principle is meant to slow — see the companion policy, below); [ADR-0034](0034-typed-coordination-engine.md) (fail-closed gates, which this principle keeps rather than removes).
- **Companion:** the operational rule that enforces this principle on *taxonomy* growth — freeze the coordination taxonomy, require two instances before a new class — is filed as its own decision (P5, `.github#1263`). This ADR states the principle; that one applies it to scope classes.
- **Source:** [docs/reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md](../reports/2026-07-20-cross-repo-coordination-overhead-root-cause.md) §4, §7 P6.

## Context

The 2026-07-20 root-cause analysis measured that ~76% of a representative 24h of org-wide commits
were coordination/registry/release bookkeeping or ADR authoring, and that the registry CHANGELOG has
run 2–7 entries **every day for 18 days**. The report traced five distinct friction engines — a
hand-flipped registry, a split schema/validator, the F# positional-record ABI tax, byte-copy kit
fan-out, and a coordination taxonomy that accretes gates faster than it retires them — to **one**
underlying defect (§4):

> The org models reality as hand-maintained **projections**, then builds a **gate per projection** to
> catch the drift the projection made possible — and rolls out each new capability by adding *another*
> projection and *another* gate rather than by deriving from a single source.

This is not a new observation. The 2026-07-12 throughput audit stated it one layer down — *"a check
that passes when its subject is missing is worse than no check"* — and its owned fixes landed. The
gap the org keeps hitting is not **diagnosis**; it is **restraint**: in the same eight days that two
loops were fixed, four new coordination surfaces were added, and net complexity rose. Each individual
ADR was well-argued; nothing in the process asked the one question that would have caught the pattern
across them — *does this add a projection we must then hand-maintain?*

The gates themselves are the org's best invention and are **not** the problem. Fail-closed coherence
gates (ADR-0034) demonstrably prevent double-claims, fake-ready flips, and silent drift. The problem
is that **half of what they gate is a copy the org is obligated to keep typing** — a value the gate
already computes on every run and then asserts against a human-typed literal instead of emitting.

## Decision

**Adopt one governing principle, and make it the explicit test every new cross-repo ADR must pass:**

> **Derive, don't restate. Gate capabilities, not declarations.**
> Do not add a projection you must then hand-maintain; if a fact has an authoritative home, read it —
> never copy it.

Concretely, this obliges three things:

1. **Every ADR that proposes a new gate, projection, registry field, or scope class must contain a
   sentence naming what it derives from, or arguing why the fact has no authoritative home.** An ADR
   that introduces a hand-maintained copy of a fact that lives elsewhere must justify the copy as the
   exception it is — not present it as the default.

2. **Prefer a generator to an assert-equality gate.** When a gate already computes the authoritative
   value (queries the feed, reads a source `<Version>`, reads a producer manifest), the value should
   be *emitted* at generation time, leaving nothing to flip and nothing to drift. The fail-closed gate
   is retained on the **semantic** fields only — ownership edges, coherence intent, scope meaning —
   which are genuine declarations with no upstream to derive from.

3. **Gate the capability, not its declaration.** A check should fail when the thing behind the
   declaration does not work, not when a literal was mistyped. ADR-0044 already does this for generated
   artifacts (it asks the generator what it emits); the §6a incident in the source report is a live
   proof point — a real provider bug was caught by `scaffold.providerWroteSddTree`, a guard that fired
   because the scaffold *failed to emit the skill tree*, not because a registry literal drifted.

This principle is **the lens, not a mandate to rewrite anything today.** It does not by itself change
the registry, retire a gate, or land any of the report's P1–P4 changes; each of those remains a
candidate that must be argued on its own. What this ADR changes is the **test** those arguments are
held to.

### It is implementable, because it is already implemented once

The strongest evidence that this is a rule and not a slogan is [ADR-0044](0044-generated-artifacts-are-derived-from-their-generators.md):
a generated artifact is derived from its generator, `verify-paths` subtracts what the generators emit,
and "I could not ask what is generated" fails closed rather than reading as "nothing is generated."
That is this principle, applied at one seam and shipped. Adopting it as the general test asks the rest
of the system to converge on the seam that already works.

### The declined mechanism is the principle passing its own test

The source report (§6a) recorded a genuine new fact — a cross-repo blocker was first filed against the
wrong repo, and the re-root-cause hop cost real time — and then **declined** to mint a "detect the true
owner at filing time" mechanism, because the system self-corrected in ~2h via an ordinary worker and
the new mechanism would have been exactly the accretion this principle exists to slow. Recording a
temptation and declining it is this ADR working before it was written; the obligation above is to make
that reflex the default rather than a one-time act of discipline.

## Consequences

- **Every future cross-repo ADR carries a derive-or-justify sentence.** The `template.md` house rules
  gain one line to this effect (landed with this ADR). Reviewers have a single, cheap question to ask.
- **The bias shifts from "add a gate" to "remove the drift."** When a proposal's answer to "what does
  this derive from?" is "a human keeps it in sync," that is now a flag to design out the copy, not a
  routine registry row.
- **No gate is weakened.** Fail-closed remains the rule; the principle *narrows* what gets a
  hand-maintained projection, it does not relax what a gate does when it fires. Semantic declarations
  (ownership, intent, meaning) are unaffected — they have no upstream and are meant to be authored.
- **This is a decision record, not a rewrite order.** Merging it records the principle as the org's
  stated test; the concrete debts it judges (P1 generate derived registry fields, P2 collapse the
  schema split, P3 the ABI tax, P4 the kit package, P5 the taxonomy freeze) are tracked separately on
  the Coordination board and argued on their own merits.
- **It can be withdrawn.** If the principle proves to obstruct a legitimate declaration-first design,
  the honest move is a superseding ADR, not a quiet exception.

## Alternatives considered

- **Do nothing; rely on per-ADR review.** This is the status quo, and the report measured its result:
  every ADR was individually reasonable and the aggregate still accreted. A pattern that only becomes
  visible across records is not caught by a process that reviews one record at a time. Rejected — the
  whole value of a stated principle is that it is the cross-record question no single review asks.
- **Encode the principle as a CI gate rather than an ADR.** Tempting, and exactly the move the
  principle warns against: a gate that tries to detect "this ADR restates a derivable fact" would be a
  new projection of a judgement, hand-maintained, drifting — the anti-pattern wearing the costume of
  its own cure. The principle is a *review test*, which is where judgement belongs; ADR-0044 already
  supplies the one place a *mechanical* derivation check is cheap and total.
- **Fold the principle into the P5 taxonomy-freeze policy and skip a standalone ADR.** P5 is narrower
  — it governs scope/grammar/predicate *classes*. The principle governs projections and gates *in
  general* (the registry literals of P1, the schema split of P2). Collapsing the general rule into the
  specific policy would leave P1–P4 without the frame they are meant to be judged against. Kept
  separate; cross-linked in both directions.
- **State it as several principles rather than one sentence.** The report deliberately reduced five
  engines to one defect; a single sentence is what a reviewer can hold in their head and apply on
  sight. A checklist of five would be another taxonomy to maintain. Rejected in favour of one line.
