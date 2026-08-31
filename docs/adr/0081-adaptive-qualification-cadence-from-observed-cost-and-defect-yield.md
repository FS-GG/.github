# ADR-0081: Adapt qualification cadence from observed cost and defect yield

- **Status:** Accepted
- **Date:** 2026-08-31
- **Amends:** [ADR-0080](0080-scoped-child-qualification-comprehensive-milestone-closure.md)
- **Affects:** qualification policy in every FS-GG roadmap-owning component

## Context

ADR-0080 separates scoped child qualification from comprehensive parent closure. That removes the
largest fixed cost, but it does not by itself prevent a scoped gate set from accumulating expensive,
low-yield operations. Corpus size, failure modes, tool performance, and integration risk all change with
the product. A cadence chosen once eventually becomes ceremony rather than a response to evidence.

An expensive operation that almost never finds a unique defect may be more useful at the cumulative
closure boundary. Conversely, a cheap or high-yield operation belongs close to every change. Neither
decision is permanent: the cost of delayed detection and the blast radius of the missed defect matter as
much as raw runner minutes.

## Decision

Qualification cadence is an adaptive policy control, not a fixed checklist.

1. **Review gate economics approximately daily.** A scheduled control point—target cadence 24 hours,
   tolerant of ordinary scheduler delay—aggregates a rolling observation window for every declared gate:
   executions, wall time and runner cost, cache/reuse rate, failures, unique actionable defects, false or
   infrastructure failures, detection delay, and the later comprehensive run that would or would not have
   caught the same defect. Missing or statistically sparse observations remain explicit; zero observations
   never mean zero risk.

2. **Record a recommendation, not an automatic weakening.** The control point emits an immutable,
   source-bound recommendation to retain, increase, or reduce cadence, with its sample window, confidence,
   cost saved, expected detection delay, blast-radius class, and policy version. A cadence change lands as a
   reviewed versioned policy update. Mutable labels, workflow inputs, and a single green or red run cannot
   change authority.

3. **Move low-yield expensive work outward when the evidence supports it.** A gate may run more sparsely
   when its marginal cost is high, its unique defect yield is low, delayed detection is tolerable, and the
   comprehensive parent closure exercises an equivalent or stronger check. Sparse operation must still run
   on its semantic-subject drift, its declared sampling cadence, and parent closure.

4. **Move valuable checks inward again.** High unique defect yield, increasing failure rate, cheap execution,
   long closure intervals, poor closure equivalence, or high-impact delayed detection recommends equal or
   greater frequency. A prior reduction is not precedent against restoration.

5. **Hard boundaries cannot be optimized away.** Parent closure, freeze, release, cutover, `OpenV2`,
   rollback-authority, and other production-authority candidates remain comprehensive. Formal-input drift
   continues to execute canonical formal qualification. Security, corruption, irreversible mutation, and
   similarly high-blast-radius controls may declare a minimum cadence that telemetry cannot reduce.

6. **Comprehensive results calibrate the policy.** A closure defect missed by sparse child qualification is
   charged to the responsible cadence decision and subject declaration. The next daily review consumes that
   evidence and may immediately recommend moving the check inward. This feedback loop is part of closure,
   not optional reporting.

The first implementation belongs to `FS.GG.Coordination` and applies generically. GS2-04 supplies the first
observations; it does not receive a special threshold or permanent cadence.

## Consequences

Runner use follows demonstrated value rather than accumulated habit. Some errors will be discovered later at
parent closure by design; the recommendation must price that delay and may only accept it where the blast
radius and closure equivalence justify the tradeoff.

Daily review is approximate, not a new availability dependency. A missed scheduled review leaves the last
versioned policy in force and reports stale telemetry; it never silently relaxes a gate. Small samples produce
an `insufficient-data` recommendation rather than false certainty.

## Alternatives considered

**Fix one cadence for each gate forever.** Rejected because cost, yield, corpus, and integration risk evolve.

**Automatically disable checks below a yield threshold.** Rejected because rare high-impact failures, sparse
samples, and correlated closure failures make a raw threshold unsafe.

**Measure only duration.** Rejected because a slow high-yield check can be valuable and a fast noisy check can
still waste more review attention than it saves.

**Review cadence only at parent closure.** Rejected because the feedback interval can become too long while the
corpus and runner cost continue to grow.
