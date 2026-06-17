---
title: The inference kernel
category: Governance design
categoryindex: 7
index: 3
description: The domain-neutral core — facts, rules, fixed-point evaluation, provenance, the CheckTier arbitration model, and the evidence / synthetic-taint model.
---

# The inference kernel

The kernel is a pure, domain-neutral reasoner. It takes asserted facts and rules,
derives new facts to a fixed point, and records why each derived fact holds. It
contains no software, design, or workflow vocabulary; a domain supplies all of
that through its `'fact` type and its [adapter](adapters.md).

## Facts and rules

```fsharp
type FactId = FactId of string
type RuleId = RuleId of string

/// A justification step — what produced a derived fact.
type ProvenanceStep =
    { Rule: RuleId
      Inputs: FactId list
      Note: string }

type FactAssertion<'fact> =
    { Id: FactId
      Value: 'fact
      Provenance: ProvenanceStep list }   // empty for supplied (asserted) facts

type FactSet<'fact> = FactAssertion<'fact> list

/// A rule maps the current fact set to zero or more derived facts.
/// Rules are ordinary typed F# functions — there is no external rule language.
type Rule<'fact> =
    { Id: RuleId
      Description: string
      Apply: FactSet<'fact> -> FactAssertion<'fact> list }
```

A domain instantiates `'fact` with its own closed union of fact kinds. The kernel
never inspects the contents; it only schedules rules and tracks provenance.

## Fixed-point evaluation

Evaluation is forward chaining to quiescence: apply every rule, add any new
derived facts, repeat until nothing new appears. Because rules only *add* facts
(monotonic) and the fact space is bounded per run, evaluation terminates.

```fsharp
type EvaluationResult<'fact> =
    { Facts: FactSet<'fact>          // supplied + derived
      Rounds: int }

module FixedPoint =
    /// `identify` gives each fact a stable id (for dedup + provenance);
    /// `rules` fire until no new fact is produced.
    val evaluate :
        identify: ('fact -> FactId) ->
        rules:    Rule<'fact> list ->
        supplied: FactSet<'fact> ->
        EvaluationResult<'fact>
```

This is a small production system in the
[forward-chaining](https://en.wikipedia.org/wiki/Forward_chaining) /
[Datalog](https://en.wikipedia.org/wiki/Datalog) tradition. Provenance is a
[reason-maintenance](https://en.wikipedia.org/wiki/Reason_maintenance) record:
every conclusion knows the rule and inputs that justified it, which is what makes
the system explainable rather than oracular.

## The arbitration model: `CheckTier`

The central idea is that not every rule can — or should — be decided the same
way. Each rule declares *who is competent to judge it*.

```fsharp
type CheckTier =
    | Deterministic   // a machine decides; pure, runs every time, reproducible
    | AgentReviewed   // an AI agent decides; verdict recorded as evidence
    | HumanOnly       // a person decides; the kernel blocks and asks
```

This is the bridge between classical rule evaluation and an agent harness:

- **Deterministic** rules never call a model. If a linter or a type check can
  prove it, do not ask an agent.
- **AgentReviewed** rules emit a typed review request, are answered by an agent
  at the edge, and have their verdict frozen into an evidence artifact so the
  pipeline stays reproducible even though the judge is stochastic. The request
  is keyed by content hash, so an agent is only re-consulted when the inputs
  actually change.
- **HumanOnly** rules escalate; the kernel returns a blocker and never decides.

`CheckTier` is orthogonal to `Severity` (whether failure blocks); see
[Routing, severity, and run modes](routing-and-modes.md).

## Verdicts

Verdicts are three-valued so that "an agent must still decide this" composes
cleanly with deterministic results.

```fsharp
type Verdict =
    | Pass
    | Fail      of reason: string
    | Uncertain of reason: string   // e.g. an AgentReviewed clause not yet answered
```

`Uncertain` is not failure. It is the kernel saying a competent judge has not yet
ruled — which routing turns into a review request, not a block.

## The evidence model

Facts about work are often backed by evidence of varying quality. The kernel
tracks evidence state and propagates a *taint* when a conclusion rests on
something synthetic or unverified.

```fsharp
type EvidenceState =
    | Pending            // [ ]  not started
    | Real               // [X]  done, backed by real evidence
    | Synthetic          // [S]  done, but only synthetic / placeholder evidence
    | Failed             // [F]
    | Skipped            // [-]  with written rationale
    | AutoSynthetic      // [S*] COMPUTED, never written: depends on (auto-)synthetic
```

The taint rule is transitive over the dependency graph:

```text
effective(t) =
    Synthetic       if declared(t) = Synthetic
    AutoSynthetic   if declared(t) = Real AND any dependency is (Auto)Synthetic
    declared(t)     otherwise
```

`AutoSynthetic` flows down the whole chain and clears automatically once the
root-cause `Synthetic` input is upgraded to `Real`. This is ordinary
transitive-closure dataflow, and it generalises well beyond software: a research
finding resting on simulated data, or an essay argument resting on an unverified
citation, is exactly an `AutoSynthetic` conclusion.

Disclosure is mandatory and a bypass flag never changes a verdict — it logs a
justification but leaves the result intact. Honesty about evidence is enforced
separately from the freedom to develop, which the
[run-mode escape hatch](routing-and-modes.md) provides.

## Pure core, effects at the edge

The kernel is pure: it touches no filesystem, no git, no network, and no model.
It reads facts and returns facts. All effects — reading artifacts, dispatching an
agent, recording a verdict, running a check — happen in a thin interpreter at the
edge, in the classic
[functional-core / imperative-shell](https://www.destroyallsoftware.com/talks/boundaries)
shape. The loop is sense (gather facts) → plan (run the kernel) → act (interpret
effects), with the only nondeterminism (agent calls) pushed to the boundary and
reified as recorded evidence.
