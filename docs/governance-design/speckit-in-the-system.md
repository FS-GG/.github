---
title: Spec Kit in the system
category: Governance design
categoryindex: 7
index: 7
description: How the Spec Kit workflow is expressed in the governance system — a spec-kit adapter, the phase checks as reified rules, run-modes mapped to phases, and the constitution as the dial.
---

# Spec Kit in the system

Spec Kit is the **harness** — the authored, agent-run work loop
(`constitution → specify → clarify → plan → tasks → analyze → implement →
merge`). The governance system is the **observer** attached to it. Governance
never generates `spec.md`, `plan.md`, or `tasks.md`; it reads them as artifacts,
asserts facts, and runs rules. That keeps the anti-goal ("do not replace authored
Spec Kit artifacts") structural, not aspirational.

Expressing Spec Kit in the system is therefore four things: a spec-kit
[adapter](adapters.md), the phase checks as reified [rules](rule-edsl.md),
[run-modes](routing-and-modes.md) mapped to phases, and the constitution as the
configuration dial.

## The spec-kit adapter

Artifacts become `ArtifactRef`s; phases and task states become facts in the
[kernel](kernel.md).

```fsharp
type SpecKitArtifact =
    | Constitution | Spec | Plan | Research | DataModel
    | Contracts | Quickstart | Tasks | TaskDeps
// toRef : SpecKitArtifact -> ArtifactRef

type Phase = Constitution | Specify | Clarify | Plan | Tasks | Analyze | Implement | Merge

type SpecKitFact =
    | PhaseReached     of Phase                       // supplied (e.g. .specify/feature.json)
    | ArtifactPresent  of SpecKitArtifact
    | TaskState        of taskId: string * EvidenceState
    | TaskDependsOn     of taskId: string * dep: string
    | SkillBound       of taskId: string * skillId: string
    | ConstitutionArea of area: string * filled: bool
```

The stateless kernel handles the stateful lifecycle by treating the current phase
as a *supplied fact*. Rules guard on it — `whenPhase Plan check` contributes only
once the feature has reached `Plan`. The `tasks.deps.yml` topology becomes
`TaskDependsOn` facts, and the evidence / synthetic-taint model runs the kernel
over them: `EvidenceGraph` is a derivation, not a bespoke engine.

## Phase checks as reified rules

The previous design's monolithic `analyze` pass and its pile of always-blocking
gates become a catalog of reified rules, each declaring *who decides*
(`CheckTier`) and *whether it blocks* (`Severity`).

| Spec Kit check | CheckTier | Default severity |
| --- | --- | --- |
| `tasks.md` ↔ `tasks.deps.yml` consistency, acyclic, ids / skills resolve | Deterministic | Advisory |
| Constitution areas filled, non-placeholder (Constitution Check) | Deterministic | Advisory → blocking at merge |
| Surface baselines / contract currency | Deterministic | Advisory → blocking at merge |
| Evidence not synthetic (the EvidenceAudit verdict) | Deterministic | Blocking, **at merge only** |
| Does `plan.md` address every requirement in `spec.md`? | AgentReviewed | Advisory |
| Are the tasks complete and ordered for the plan? | AgentReviewed | Advisory |
| Is the feature in scope / worth doing? | HumanOnly | — |

```fsharp
let planSatisfiesSpec =
    rule "plan-satisfies-spec" AgentReviewed Spec
        (whenPhase Plan
            (Opaque ("plan-covers-spec", fun _ -> Unknown "judgement")))
    |> asking "Does plan.md address every requirement in spec.md? List gaps."
// advisory: REPORTS gaps during plan; it does not block planning

let tasksGraphWellFormed =
    rule "tasks-graph" Deterministic Tasks
        (allOf [ everyTaskHasDeps; depsResolve; acyclic; skillIdsResolve ])
// advisory while authoring; promoted to blocking only behind the merge fence
```

This is the fix for the old `analyze`-pass opacity: each check renders to a
sentence and explains itself, instead of one pass emitting a wall of output.

## Run-modes mapped to phases

The inner-loop phases run in `Inner` mode (advisory only). **Merge is the single
fence** that flips to `Gate` mode, recomputes from the base branch, and lets
blocking rules bite.

| Phase | Run mode | What governance does |
| --- | --- | --- |
| constitution | Inner | authoring the dial (below); advisory "areas filled?" |
| specify / clarify | Sandbox → Inner | advisory well-formedness; nothing blocks; sandbox while drafting |
| plan | Inner | advisory Constitution Check; *anticipate* which fences the change will hit; report surface decisions |
| tasks | Inner | deterministic-but-advisory graph well-formedness |
| analyze | Inner | the whole rule catalog runs, all advisory — a report, not a gate |
| implement | Inner | task states update; taint recomputed; surface diffs reported |
| **merge** | **Gate** | the fence: recompute from base; blocking rules enforced |

```fsharp
let mergeFence =
    fence "feature-merge" (fun c -> c.Phase = Merge)
        [ constitutionComplete   |> blocking
          evidenceNotSynthetic   |> blocking     // no [S]/[S*] reaching main
          contractsCurrent       |> blocking     // generated views not drifted
          fencedSurfacesVerified |> blocking ]
```

So everything before merge *informs*; only merge *enforces*. The old pain —
plan-time and even documentation edits triggering heavy machinery — is gone. The
escape hatch composes naturally: `Sandbox` during `specify` / `clarify` lets you
throw a spec around with zero friction, and you still cannot land it un-checked,
because merge recomputes independently.

## The constitution is the dial

In the previous design the constitution was prose plus a separate guidance check.
Here the **constitution phase is where the fences and severities are authored** —
the constitution configures which surfaces are fenced and which rules block at
merge. The "complete enforcement ↔ light" dial described in
[Lessons and anti-goals](lessons.md) *lives in `constitution.md`*, as a small,
reviewable, single place to opt back into hard guarantees for the few things that
warrant them. The Constitution Check then just verifies the dial was filled in
honestly.

## Evidence model

The `tasks.md` states are the [kernel's evidence facts](kernel.md): `Real`,
`Synthetic`, `Failed`, `Skipped`, and the computed `AutoSynthetic` taint that
flows down the `TaskDependsOn` graph. `EvidenceGraph` validates and computes
propagation; the merge fence's `evidenceNotSynthetic` rule is the blocking verdict
that keeps synthetic-only work from reaching the base branch. Disclosure is
mandatory and no flag changes a verdict — honesty about evidence is enforced
separately from the freedom to iterate, which the run-mode hatch provides.

## A feature, end to end

```text
constitution  Inner    author fences + severities; advisory completeness check
specify       Sandbox  draft spec.md freely; advisory well-formedness
clarify       Inner    open questions resolved (advisory)
plan          Inner    plan.md authored; advisory: plan-covers-spec, anticipate fences
tasks         Inner    tasks.md + tasks.deps.yml; advisory: graph well-formed
analyze       Inner    full rule catalog runs as a REPORT across all tiers
implement      Inner    tasks done; states + taint update; surface diffs reported
merge         Gate     recompute from base; blocking rules enforced or merge refused
```

## The one deliberate behavior change

Nothing blocks before merge. If a phase should hard-stop earlier — say, refuse to
leave `tasks` with a cyclic graph — that is a one-line `|> blocking` plus a fence
on that phase: available, opt-in, and visible in the constitution. The default,
though, is that the inner loop informs and only the boundary enforces.

## Status

Design only. See [the index](index.md) for overall status and the source material
this refactors.
