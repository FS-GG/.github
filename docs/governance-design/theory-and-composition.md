---
title: Theory and composition
category: Governance design
categoryindex: 7
index: 8
description: The theoretical footing for the reified rule algebra and adapter composition — Data Types à la Carte, applicative inspectability, the phase-machine-emits-facts layering, and explicit deterministic cross-domain combinators.
---

# Theory and composition

This design is not ad hoc. The reified rule algebra and the way adapters compose
are a closed-union specialization of well-established results, and naming them
both justifies the design and tells us exactly where its costs are. The findings
below were drawn from primary sources (cited at the end) and verified
adversarially.

## The composition is Data Types à la Carte

Composing independent rule domains into one project by taking a coproduct of
their fact types — `ProjectFact = Design of … | SpecKit of … | Software of …` —
and lifting each adapter's rules into it is exactly Wouter Swierstra's
*Data Types à la Carte*: independent syntaxes compose "by taking the coproduct of
their signatures," and the per-domain "rule lifting" is the paper's automatic
injection (`:<:`).

Two consequences carry straight over:

- **Each interpreter is one algebra (a catamorphism), and the algebra for a
  coproduct is the case-split of the per-domain algebras.** So `eval`, `render`,
  `hash`, and `explain` (see [the rule eDSL](rule-edsl.md)) are textbook folds,
  and each one is assembled modularly from per-domain pieces. Adding an
  interpreter is cheap and local.
- **The cost is the Expression Problem trade-off.** DTalC keeps the coproduct
  *open* (third parties add domains) at the price of type-class injection
  machinery. We deliberately keep it *closed*: a single, reviewable composition
  root. Adding an interpreter stays trivial; adding a new domain is a central
  edit to `ProjectFact`. That is the right trade for us — interpreters churn,
  domains are few and deliberate — but it must be stated, not hidden.

The DTalC analogy is conceptual, not a strict isomorphism: DTalC's coproduct is
over *functors* with inferred injection, while our `ProjectFact` is an ordinary
closed sum whose injections are hand-written. That hand-written **lifting
boilerplate is real and unavoidable** in the closed design (see mitigation
below).

## Keep the algebra applicative, never monadic

The algebra must stay inspectable: `hash`, `render`, and `explain` have to work
*without executing* the check. Capriotti & Kaposi (*Free Applicative Functors*)
make the dividing line precise — applicative structure is fixed a priori and so
is statically analysable, whereas a monadic computation "cannot be examined
without executing it."

Therefore: **no `bind` / data-dependent sequencing inside a `Check`.** The
combinators stay applicative (`All` / `Any` / `Not` / `Implies` over independent
sub-checks). A concrete reified discriminated union is trivially inspectable, so
this is a property we keep by discipline rather than derive from a library. The
`Opaque` escape hatch is the single exception, and it is already flagged as
non-inspectable and barred from the `Deterministic` tier.

## Two layers: a phase machine that emits facts

"Composable state machines" are a real and rich body of work — statechart
hierarchy and orthogonal/parallel regions (Harel, as realized in XState), Mealy
machines as a `Category` and an `Arrow` (sequential and product composition with
state threading), and process-algebra (CSP/CCS) parallel composition with
explicit synchronization. But all of it buys *temporal, stateful sequencing*, and
the check algebra is **stateless** — a fold over facts. So they belong to a
different layer.

The clean architecture is two layers with a one-way edge between them:

```text
phase lifecycle (a small state machine)         stateless check algebra
  specify -> clarify -> ... -> merge      --->   folds over the emitted facts
  owns "where are we", transitions,              owns "is it OK here"
  orthogonal tracks (design / surface)           (Atom/All/Any/Not/Implies)
                         \                       /
                          emits PhaseReached + progress facts
```

The state machine owns transitions and any orthogonality (a feature's
design-track and surface-track as parallel regions). It **emits `PhaseReached`
and progress facts**; the check algebra consumes them via `whenPhase` guards (see
[Spec Kit in the system](speckit-in-the-system.md)). Do not fold state into the
check AST, and do not fold checks into the machine. This is the principled split,
not merely a convenient one: the machine produces facts; the algebra is a pure
function of facts.

## Cross-domain coupling is an explicit, deterministic combinator

The most useful rules span adapters (a design task that must carry a recorded
review). Process algebra is a direct warning here: synchronization must be an
*explicit, alphabet-parameterized operator*, and full compositionality of
behavioural equivalence needs well-behaved rule formats — i.e. **ad-hoc
cross-domain coupling breaks compositionality.**

So cross-adapter coupling is expressed two disciplined ways, never as arbitrary
glue:

1. **In the AST**, via `Implies` over the coproduct's facts:
   `taskTouches "design/**" ==> hasRecordedReview`.
2. **At combine time**, via a fixed, order-independent precedence — the model
   Cedar uses for authorization: *a blocking `forbid` always wins; default
   allow-unless-fenced.* This keeps the merged verdict deterministic and
   independent of rule order.

The set of cross-domain rules stays small, named, and listed in the composition
root, so it is the one surface code review guards.

## Ordering and confluence

A governance verdict must not depend on the order rules happen to be written or
evaluated in. The design is **order-independent by intent**, and most layers are
confluent by construction; the residual hazards are a short, known list, each with
a standard mitigation.

### Confluent by construction

- **Fixed-point derivation** is forward chaining with **monotonic** rules (rules
  only add facts), so it has a *unique least fixed point* regardless of order —
  the Datalog guarantee.
- **`All` / `Any` verdicts** use Kleene three-valued logic, which is commutative
  and associative.
- **Route gate sets** are unions (deduped) and **tiers** are a `max`.
- **Merge combination** uses deterministic precedence (a blocking result always
  wins; default allow-unless-fenced), chosen precisely so it is commutative.
- **Taint propagation** is a transitive closure over a DAG (`EvidenceGraph`
  rejects cycles), so it is a least fixed point too.

### Hazard 1 — negation-as-failure (the important one)

A rule that fires on the *absence* of a fact is non-monotonic, and its outcome
depends on what has been derived so far:

```fsharp
// HAZARD: fires on the absence of a recorded verdict
let reviewRule facts =
    match recordedVerdict facts ruleId key with        // negation-as-failure
    | Some v -> [ recordedReviewFact ruleId v ]
    | None   -> [ reviewRequestFact { Id = ruleId; (* … *) } ]
// If another rule could DERIVE a recordedReviewFact in the same run, whether the
// request is emitted depends on which fired first — non-confluent.
```

Mitigation: **stratify.** Facts that other rules negate-check live in a lower
stratum — supplied before evaluation, never derived inside the same fixed point:

```fsharp
let supplied = readEvidenceStore () @ phaseMachine.emit ()   // stratum 0 (facts)
let result   = FixedPoint.evaluate identify rules supplied   // stratum 1 reads them
```

(Note: `Check.Not` is *not* this hazard — it negates an evaluated sub-verdict,
which is total and order-free. Only rule-level "is this fact absent?" tests are
non-monotonic.)

### Hazard 2 — provenance and reason order

The fact *set* is confluent, but `eval` reports the *first* failing clause, so
reordering a commutative node changes the message even though the verdict is
identical:

```fsharp
eval facts (All [ contrast; spacing ])   // Fail "contrast 3.1:1 < 4.5:1"
eval facts (All [ spacing; contrast ])   // Fail "spacing 6px off-scale"
```

Mitigation: collect *every* failing reason (or tie-break by `RuleId`), not the
first:

```fsharp
| All cs ->
    let vs = List.map (eval facts) cs
    match List.choose failReason vs with
    | []      -> if List.exists isUncertain vs then firstUncertain vs else Pass
    | reasons -> Fail (String.concat "; " (List.sort reasons))   // order-free
```

### Hazard 3 — hashing commutative nodes

`hash (All [a; b])` should equal `hash (All [b; a])`; they are the same check, so
they must share a cache key or you get spurious re-review. Canonicalize the
commutative nodes by sorting, but keep positional hashing where order is
meaningful:

```fsharp
let rec hash = function
    | Atom p       -> h ("atom", p.Name, p.Args, p.Reads)        // positional: args ARE ordered
    | All cs       -> h ("all", List.sort (List.map hash cs))    // sorted: commutative
    | Any cs       -> h ("any", List.sort (List.map hash cs))    // sorted: commutative
    | Not c        -> h ("not", hash c)
    | Implies(a,b) -> h ("implies", hash a, hash b)              // positional: a==>b ≠ b==>a
    | Opaque(n,_)  -> h ("opaque", n)
```

### Hazard 4 — dedup and `identify`

If `identify : 'fact -> FactId` is not injective on value-bearing facts, dedup's
"keep which?" becomes order-dependent. Mitigation: an injective `identify`, with
dedup keeping the first under a total order and asserting collisions are equal.

### Hazard 5 — never positional rules

The classic firewall/iptables failure mode is "first matching rule decides," where
order is load-bearing. It is forbidden here; combination is always by deterministic
precedence:

```fsharp
// HAZARD (forbidden): first match wins — order-dependent
let verdict = rules |> List.tryPick (fun r -> matchRule r change)

// MITIGATION: deterministic precedence — commutative, order-free
let verdict =
    let vs = rules |> List.map (evalRule change)
    if   List.exists isBlockingFail vs then Blocked        // forbid trumps permit
    elif List.forall isPass vs         then Allowed
    else NeedsReview
```

This is also the process-algebra warning made concrete: express cross-domain
coupling as `Implies` plus precedence, never as "run A then B."

### The constraint

Two disciplines keep the whole system confluent: **keep the rule set stratified**
(no negation over still-being-derived facts), and **canonicalize hashing and
provenance for commutative nodes**. All *intended* ordering is quarantined in the
phase [state machine](speckit-in-the-system.md), which is explicitly sequential and
emits facts; the check algebra over those facts stays an order-free fold.

## Taming lifting boilerplate

The honest cost of the closed coproduct is hand-written injections. Keep them to
one place with small `inject` helpers and single-case active patterns, so adapter
rules read domain-agnostically and lift once — near-DTalC ergonomics, no
computation expression:

```fsharp
// one place per domain; everything else stays domain-agnostic
let private liftDesign (r: Rule<DesignSystemFact>) : Rule<ProjectFact> =
    r |> Rule.contramapFacts (function Design f -> Some f | _ -> None)

// single-case active pattern for readable matching at the root
let (|Design|_|) = function Design f -> Some f | _ -> None
```

## A future interpreter the design already enables

Because the algebra is inspectable, a later fold can analyse a *rule set* rather
than evaluate a single change — Cedar ships exactly this, compiling policies to
SMT to ask "can this ever pass?", "is this rule shadowed?", "is the set
contradictory?". We do not build it now, but the reified design reserves the slot:
it is one more algebra over the same `Check`.

## Prior art that validates the whole bet

Shipping policy engines made the same core choices, which is the strongest
external confirmation:

- **Cedar** (AWS authorization, OOPSLA 2024) reifies policies as data, evaluates
  them with a separate engine, runs *multiple interpreters* (enforcement plus an
  SMT analyser) over the same reified policies, and combines them deterministically
  ("forbid trumps permit, default deny").
- **OPA / Rego** is a declarative, Datalog-derived policy language that separates
  *what* a query returns from *how* it executes.

Both are direct prior art for "reify rules as data, fold several interpreters over
them, combine deterministically" — our design at industrial scale.

## Honest caveats

- The DTalC mapping is conceptual; we trade open extensibility for a closed root
  and accept the lifting boilerplate.
- "Monadic DSLs cannot be analysed" is slightly absolute (limited analysis via
  dummy values exists), but moot — our hand-written DU is inspectable regardless.
  The applicative literature *supports* our encoding; it does not dictate it.
- The no-computation-expression constraint is backed by community practice
  (Validus and FParsec both choose operators over CEs) rather than a published
  benchmark of F# CE error-message degradation. We keep the constraint because it
  matches how the best F# EDSLs are actually built, while acknowledging it is a
  judgement call.

## References

- Wouter Swierstra, *Data Types à la Carte*, JFP 2008 —
  <https://www.cs.tufts.edu/~nr/cs257/archive/wouter-swierstra/DataTypesALaCarte.pdf>
- Paolo Capriotti & Ambrus Kaposi, *Free Applicative Functors*, MSFP 2014 —
  <https://arxiv.org/pdf/1403.0749>
- *Cedar: A Language for Expressive, Fast, Safe Authorization*, OOPSLA 2024 —
  <https://arxiv.org/pdf/2403.04651>; Cedar Analysis (SMT) —
  <https://aws.amazon.com/blogs/opensource/introducing-cedar-analysis-open-source-tools-for-verifying-authorization-policies/>
- Open Policy Agent / Rego language —
  <https://www.openpolicyagent.org/docs/policy-language>
- XState (statechart hierarchy + parallel regions) —
  <https://github.com/statelyai/xstate>, <https://stately.ai/docs/parallel-states>
- Mealy machines as Category/Arrow —
  <https://hackage.haskell.org/package/machines-0.1.2/docs/src/Data-Machine-Mealy.html>;
  effectful Mealy machines — <https://arxiv.org/pdf/2410.10627>
- Introduction to process algebra (CCS/CSP/ACP parallel composition,
  synchronization, SOS) —
  <https://www.pst.ifi.lmu.de/Lehre/fruhere-semester/sose-2013/formale-spezifikation-und-verifikation/intro-to-pa.pdf>
- Validus (CE-free F# validation operators) —
  <https://github.com/pimbrouwers/Validus>
- F# active patterns —
  <https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/active-patterns>
