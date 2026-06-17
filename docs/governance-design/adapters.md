---
title: Domain adapters
category: Governance design
categoryindex: 7
index: 6
description: How a domain plugs into the kernel — the design-system adapter and its rule catalog, plus research, essay, and engineering sketches that demonstrate generality.
---

# Domain adapters

An adapter is everything domain-specific: the facts a domain asserts, the
artifacts it inspects, and the probes its rules call. The kernel and the
[rule eDSL](rule-edsl.md) are reused unchanged. The adoption bar is that a domain
can govern itself **without** copying another domain's vocabulary or layout.

## What an adapter supplies

1. A closed `'fact` union for the domain.
2. A mapping from its artifacts onto the structural `ArtifactRef`.
3. A set of probes — the atomic, inspectable predicates its rules compose.
4. A rule catalog: each rule a `Check`, a `CheckTier`, and a `Severity`.
5. A set of fences naming its high-stakes surfaces (for routing).

Nothing else. Inference, arbitration, evidence, rendering, hashing, explanation,
severity, and run modes all come from the kernel.

## Design-system adapter (the first adapter)

The design-system adapter governs adherence to a design language (the worked
example is Ant Design). Its facts cover selected policy, design rules,
deterministic verdicts, recorded reviews, and blockers. Its artifacts map onto
`ArtifactRef`:

```fsharp
type DesignArtifactRef =
    | TokenDocument
    | GeneratedTokenSurface
    | RenderedCapture
    | InteractionStateSpec
    | PagePatternSpec
// toRef : DesignArtifactRef -> ArtifactRef   (Kind + stable Key)
```

Probes are data, each naming what it reads:

```fsharp
let surfaceMatches generated canonical =
    let g, c = toRef generated, toRef canonical
    probe "surface-matches" [ g; c ] [ ArtifactArg g; ArtifactArg c ]
          (fun facts -> (* compare generated token surface to canonical *) Met)

let contrastMeets policy surface =
    let s = toRef surface
    probe "contrast-meets" [ s ] [ LiteralArg (string policy); ArtifactArg s ]
          (fun facts -> (* WCAG / Ant ratio check *) Met)
```

### Rule catalog and tier mapping

The catalog assigns each rule a tier (who decides) and a severity (whether it
blocks). Most are advisory; the deterministic, contract-bearing ones are the few
that block.

| Rule | Tier | Default severity |
| --- | --- | --- |
| Token drift (generated surface matches source) | Deterministic | Blocking |
| Colour / contrast policy | Deterministic | Blocking |
| Spacing scale | Deterministic | Advisory |
| Control-height defaults | Deterministic | Advisory |
| Token surface gate (public surface) | Deterministic | Blocking |
| Intent coverage (intent is consumed) | Deterministic | Advisory |
| Visual-state resolution | Deterministic | Advisory |
| Rendered control matches spec intent | AgentReviewed | Advisory |
| "Four values" (natural / certain / meaningful / growing) | AgentReviewed | Advisory |
| Page-pattern correctness | AgentReviewed | Advisory |
| Colour as information, not decoration | AgentReviewed | Advisory |
| Motion restraint | AgentReviewed | Advisory |
| Elevation / overlay layering | AgentReviewed | Advisory |
| Adopting a new policy (e.g. Material) | HumanOnly | Blocking |

```fsharp
let tokenDrift =
    rule "token-drift" Deterministic LocalPolicy
        (surfaceMatches GeneratedTokenSurface TokenDocument)
    |> blocking

let colourIsInformational =
    rule "colour-informational" AgentReviewed AntSpec
        (Opaque ("colour-conveys-information",
                 fun _ -> Unknown "requires visual judgement"))
    |> asking "Does colour carry information here, or is it decoration?"
```

The deterministic rules are reified down to probes (so they render and hash); the
judgement rules use the `Opaque` hatch, which automatically keeps them out of the
`Deterministic` tier and routes them to an agent reviewer whose prompt is the
rule's `Question`.

## Generality: other domains, same kernel

The mapping is clean across domains, which is the evidence that the abstraction
sits at the right altitude. In each case only the facts, artifacts, and probes
change.

### Research adapter

- **Facts:** datasets, methods, claims, results, citations.
- **Deterministic:** unit / schema checks, citation completeness, figures rebuild
  reproducibly from source.
- **AgentReviewed:** does the stated claim follow from the data; method soundness.
- **HumanOnly:** ethics approval; novelty / submission decision.
- **Evidence taint:** a claim resting on simulated data is `AutoSynthetic` and the
  taint flows to every result that depends on it.

### Essay adapter

- **Facts:** sections, sources, the thesis, claims.
- **Deterministic:** spelling, link rot, citation format, length bounds.
- **AgentReviewed:** argument coherence; does the evidence support the thesis;
  tone consistency.
- **HumanOnly:** the final voice / publish decision.

### Engineering adapter

- **Facts:** requirements, components, load cases, analyses, sign-offs.
- **Deterministic:** dimensional / units analysis, code-standard lint.
- **AgentReviewed:** design soundness; failure-mode reasoning.
- **HumanOnly:** safety sign-off.

## Adoption bar

A domain counts as a real adopter only if it can define its own fact domain, run
useful checks and explanations, and keep its normal workflow — *without* copying
another domain's layout or vocabulary. The kernel is not a platform until at
least two unrelated domains adopt it cheaply. If adoption requires a domain to be
shaped like the design-system adapter, the kernel is not yet generic and the
boundary must move. See [Governance project](../governance-project.md) for the
adoption stance.
