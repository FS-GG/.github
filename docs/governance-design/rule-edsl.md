---
title: The rule eDSL
category: Governance design
categoryindex: 7
index: 4
description: The reified Check algebra in plain F# — combinators, the four interpreters, the Opaque escape hatch, and the bridge back to the kernel.
---

# The rule eDSL

Rules should read like the sentences they enforce, while staying inspectable. The
answer is an **embedded** DSL: plain, valid F# with no parser, no external tool,
and no build step — consistent with the earlier "typed F# rules, not a new
external language" decision. An embedded DSL is not what that decision rejected;
a separate language with its own syntax and interpreter is.

The decision that matters is *embedded sugar* versus *reified data*:

- **Sugar** dresses an opaque `FactSet -> Verdict` lambda in nicer syntax. It
  reads well but the check cannot be rendered, hashed, or explained — so it
  breaks the generated contract and the provenance story.
- **Reified** makes the check itself a value in a closed algebra. It reads just
  as well *and* a single value can be evaluated, rendered, hashed, and explained.

This design is reified.

## The `Check` algebra

```fsharp
/// A stable, renderable, hashable handle to an artifact. An adapter maps its own
/// closed union (design tokens, essay sections, …) onto this. The mapping is the
/// only domain-specific thing the algebra touches.
type ArtifactRef = { Kind: string; Key: string }

type Outcome =
    | Met
    | Unmet   of reason: string
    | Unknown of reason: string

type ProbeArg =
    | ArtifactArg of ArtifactRef
    | LiteralArg  of string
    | NumberArg   of double

/// The only part an adapter supplies. It is itself data: Name + Args + Reads are
/// rendered and hashed; Eval is run. The function is never rendered or hashed —
/// only its declared shape is.
type Probe<'fact> =
    { Name:  string
      Reads: ArtifactRef list
      Args:  ProbeArg list
      Eval:  FactSet<'fact> -> Outcome }

/// The closed combinator algebra: inspectable, foldable, serialisable.
type Check<'fact> =
    | Atom    of Probe<'fact>
    | All     of Check<'fact> list
    | Any     of Check<'fact> list
    | Not     of Check<'fact>
    | Implies of Check<'fact> * Check<'fact>
    /// Escape hatch for the rare irreducible check. Carries a name but no
    /// inspectable structure — `Check.isReified` returns false, and the rule
    /// builder uses that to refuse Deterministic tier (forcing AgentReviewed
    /// or HumanOnly). Opacity is visible in the model, never silent.
    | Opaque  of name: string * (FactSet<'fact> -> Outcome)
```

The combinators are deliberately **applicative, never monadic**: there is no
`bind` or data-dependent sequencing inside a `Check`, so its structure is fixed a
priori and can be folded (`hash`, `render`, `explain`) *without executing* it.
That inspectability is the property the whole design rests on; see
[Theory and composition](theory-and-composition.md) for why (Free Applicative
Functors) and for how this algebra relates to *Data Types à la Carte*.

## The readable surface

Deliberately the lightest embedding — smart constructors, pipeline operators, and
[active patterns](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/active-patterns).
No [computation expression](https://learn.microsoft.com/en-us/dotnet/fsharp/language-reference/computation-expressions):
CEs degrade error messages and debugging and are only worth it when binding /
sequencing semantics are needed, which rule definition does not need.

```fsharp
let probe name reads args eval = Atom { Name = name; Reads = reads; Args = args; Eval = eval }
let allOf  = All
let anyOf  = Any
let not'   = Not
let (==>)  a b = Implies (a, b)
let (.&)   a b = All [a; b]
let (.|)   a b = Any [a; b]
```

## The four interpreters

The payoff: one algebra, folded four ways. Verdict, contract text, cache key, and
provenance all derive from the same value, so they cannot drift apart.

```fsharp
module Check =
    /// (a) verdict — three-valued, so AgentReviewed's Uncertain composes
    val eval      : FactSet<'fact> -> Check<'fact> -> Verdict
    /// (b) the human/agent-readable rendering that becomes the contract column
    val render    : Check<'fact> -> string
    /// (c) structural hash: folds Atom Name+Args+Reads; for Opaque, only the name
    val hash      : Check<'fact> -> string
    /// (d) proof tree of which probes met / unmet — feeds ProvenanceStep
    val explain   : FactSet<'fact> -> Check<'fact> -> Explanation
    /// (e) declared inputs — drives routing and the artifact half of the cache key
    val reads     : Check<'fact> -> ArtifactRef list
    /// is every node structural (no Opaque)? gates Deterministic tier
    val isReified : Check<'fact> -> bool
```

`eval` combines outcomes with
[Kleene three-valued logic](https://en.wikipedia.org/wiki/Three-valued_logic), so
that a sub-clause an agent must still judge (`Uncertain`) does not get silently
treated as pass or fail:

```fsharp
let rec eval facts = function
    | Atom p ->
        match p.Eval facts with
        | Met       -> Pass
        | Unmet r   -> Fail r
        | Unknown r -> Uncertain r
    | All cs ->
        let vs = List.map (eval facts) cs
        if   List.exists isFail vs      then firstFail vs
        elif List.exists isUncertain vs then firstUncertain vs
        else Pass
    | Any cs ->
        let vs = List.map (eval facts) cs
        if   List.exists isPass vs      then Pass
        elif List.exists isUncertain vs then firstUncertain vs
        else firstFail vs
    | Not c         -> negate (eval facts c)      // Uncertain stays Uncertain
    | Implies (a,b) -> eval facts (Any [ Not a; b ])
    | Opaque (_, f) -> outcomeToVerdict (f facts)
```

## The bridge back to the kernel

A `Rule` pairs a `Check` with its tier, spec source, and severity. `toRule`
turns it into a kernel `Rule<'fact>`, and this is where the `CheckTier`
arbitration plays out — and where content-hash caching of agent reviews falls out
for free, since `Check.hash` and `Check.reads` give both halves of the key.

```fsharp
type Rule<'fact> =
    { Id: RuleId
      Tier: CheckTier
      Spec: SpecSource
      Severity: Severity            // Advisory by default — see routing-and-modes
      Check: Check<'fact>
      Question: string option }     // the reviewer prompt, for AgentReviewed

let toRule (r: Rule<'fact>) : Kernel.Rule<'fact> =
    { Id = r.Id
      Description = Check.render r.Check
      Apply = fun facts ->
        match r.Tier with
        | Deterministic ->
            [ verdictFact r.Id (Check.eval facts r.Check) ]
        | AgentReviewed ->
            let key = combineHash (Check.hash r.Check)
                                  (artifactHashes facts (Check.reads r.Check))
            match recordedVerdict facts r.Id key with
            | Some v -> [ recordedReviewFact r.Id v ]            // cache hit, no agent call
            | None   -> [ reviewRequestFact
                            { Id = r.Id; Question = r.Question
                              SpecHash = Check.hash r.Check; ArtifactHash = key } ]
        | HumanOnly ->
            [ blockerFact r.Id NeedsHuman ] }
```

## Single-source contract generation

Because a check renders, the human- and agent-readable contract is a *fold of the
rules*, not a hand-maintained file:

```fsharp
let contract (rules: Rule<'fact> list) =
    rules |> List.map (fun r -> r.Id, r.Severity, Check.render r.Check)
```

The contract cannot drift from the selector because it *is* the selector,
rendered. This reuses the canonical-source → generated-view → currency-check
pattern from the prior art, now sourced from the rule checks themselves.

## Guardrails this buys

- `rule` refuses `Deterministic` tier when `not (Check.isReified check)`. An
  `Opaque` node cannot masquerade as machine-decidable; it is forced to
  `AgentReviewed` or `HumanOnly`. Opacity becomes a typed fact, not a silent leak.
- Because `'fact` is the only type parameter and artifacts are structural
  `ArtifactRef`s, the algebra and all four interpreters sit in the kernel with
  zero domain vocabulary. Each [adapter](adapters.md) supplies only its probe set
  and reuses everything else.
