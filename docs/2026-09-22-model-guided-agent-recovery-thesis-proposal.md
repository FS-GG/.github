---
title: Model-guided recovery of actor-based AI agent workflows
category: Design
categoryindex: 4
description: A bounded computer science thesis proposal using Quint, FsQuint, and the FS.GG Akka.NET orchestration host.
---

# Model-guided recovery of actor-based AI agent workflows

**Status:** Thesis proposal, 2026-09-22. This document proposes research and a prototype; it does not authorize a production writer, change a roadmap gate, or claim that the proposed protocol is implemented.

## Research question and contribution

Can a formally specified parent/child delegation protocol, implemented on the existing single-host Akka.NET orchestrator, preserve assignment safety and recover useful progress when agent processes crash, results arrive late or twice, and external effects have uncertain outcomes?

The proposed contribution is a **bounded delegation protocol and its implementation correspondence**. It would combine (1) a Quint state model, (2) an F# implementation at the existing actor and durable-journal seams, (3) FsQuint replay against those production decisions, and (4) repeatable fault-injection measurements. An LLM's output is treated as untrusted, nondeterministic input; the thesis verifies the surrounding protocol rather than the semantic quality of generated code.

The work extends a specific gap. [Coordination's completed Choreo study](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/choreo-akka-fsharp-trace-correspondence.md) models Host, journal, runner, and GitHub provider messages for one hosted-writer lifecycle. Its [qualification decision](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/architecture/choreo-qualification.md) establishes bounded formal and production-replay evidence, not a general proof of Akka or an agent delegation protocol. The [accepted O3 scope](https://github.com/FS-GG/FS.GG.Coordination/blob/main/docs/roadmaps/o3-controlled-adoption.md) retains one ordinary subscription slot and serial adoption. The proposed parent/child protocol is therefore new research scope, not a reimplementation of O3 or the completed Choreo work. [FsQuint](https://github.com/FS-GG/FsQuint) already supplies a reusable trace reader and replay API; its existence makes an independent correspondence experiment feasible.

Three questions structure the evaluation:

1. **Safety:** Under the declared failure model, do model checks and implementation replay prevent two current owners for one child, completion by a revoked generation, an effect before durable intent, and a second effect while the first outcome is unknown?
2. **Recovery:** After a parent or child process restart, can the system resume or reach an explicit terminal refusal without losing the assignment identity or silently treating unknown outcomes as absent? Progress claims state the necessary fairness and eventual-response assumptions.
3. **Verification yield and cost:** Which injected faults are detected by conventional example tests versus Quint-generated traces and production-seam replay, and what additional runtime and maintenance effort does the latter require?

## Bounded protocol and prototype

One host owns one parent assignment with at most two children and one exclusive external-effect resource. Each child has an immutable assignment ID, parent ID, attempt ID, generation, finite deadline, and declared capability. The parent may request, start, cancel, and collect children. The Host records durable intent and authorizes dispatch; an actor supervises each execution session; the journal and native provider readback settle effects. A restart reconstructs the current generation from durable records before dispatch resumes. Late results remain evidence but cannot change a newer generation's decision.

The model includes request, admission, durable append, dispatch, result, cancellation, timeout, restart, reconciliation, and terminal-refusal actions. It distinguishes **unknown**, **proved absent**, and **applied** external outcomes. The F# prototype reuses Coordination's existing `ExecutionSessionActor`, pure decision code, and PostgreSQL journal boundaries where their current contracts permit it. Any required new transition is versioned and checked as a protocol change. [FS.GG.SDD](https://github.com/FS-GG/FS.GG.SDD) provides the authored specification and lifecycle context; [FsQuint](https://github.com/FS-GG/FsQuint) provides trace decoding and comparison, not the domain model or an automatic proof of the implementation.

The experimental workload uses deterministic stub agents so a fault schedule can be replayed exactly. One optional live coding-agent run checks adapter compatibility; it is not used to estimate failure rates. No cluster, cross-host federation, untrusted-contributor credential boundary, automatic GitHub publication, or general scheduler is part of the prototype. The [unified roadmap's cooperative client/master design](2026-09-07-154210-fs-gg-unified-development-roadmap.md#85-cooperative-orchestrators-a-project-master-assigns-jobs-to-contributor-clients) remains future work.

## Experimental method

Freeze the model, test corpus, fault schedules, tool versions, and measurement definitions before comparing results. Retain the same assignment and effect identities across retries. Record both positive runs and controls deliberately violating each property.

| Experiment | Inputs | Result recorded |
|---|---|---|
| Bounded model exploration | One or two children; bounded messages, generations, restarts, and provider outcomes | Explored states and bounds, invariant verdicts, counterexamples, and any unexamined transitions |
| Trace-to-implementation replay | Genuine Quint traces through FsQuint and the F# Host/actor/journal seams | First divergent action and state projection; passing traces are evidence only for their selected mapping and bounds |
| Fault injection | Crash before/after durable append, parent or child restart, duplicate and late result, cancellation race, lost provider response, transient journal failure | Forbidden-effect count, duplicate-effect count, recovery/refusal outcome, and time to settled state |
| Verification comparison | Same predeclared seeded mutants and historical defects, if reproducible | Faults detected by ordinary tests alone and by tests plus model/replay; false alarms, diagnosis effort, CI duration |
| Fault-free overhead | Same single-child workload on the existing supported serial route and the prototype, with comparable durable storage and provider stub | Dispatch-to-terminal latency, journal writes, memory and CPU; no reliability claim from this unequal-capability comparison |

The primary safety outcome is **zero invariant violations in the explored model and replayed fault corpus**. This is an experiment criterion, not a prediction or unbounded correctness claim. At least one injected fault must be caught by each major invariant, so a vacuous model cannot pass. Recovery results report the proportion settled or explicitly refused within a declared timeout; liveness is not claimed when an external provider may remain unavailable forever. Report median and tail latency with sample counts and confidence intervals where repetitions support them. Preserve incomplete, timed-out, and failed runs in the denominator.

Akka.NET's ordinary messages are [at most once](https://getakka.net/articles/concepts/message-delivery-reliability.html); persistent or reliable delivery still permits duplicates under some recovery schedules. Actor serialization is therefore a useful execution mechanism, not a proof of exactly-once external effects. Durable intent, stable identities, reconciliation, and native readback remain separate obligations. Quint's [simulator samples executions](https://quint.sh/docs/what-does-quint-do); only the declared finite model-checking bounds support exhaustive claims.

## Deliverables and schedule

| Weeks | Reviewable result |
|---|---|
| 1–3 | Literature and existing-system review; frozen research questions, baseline, fault taxonomy, and data-handling plan |
| 4–6 | Quint delegation model, invariants, bounded exploration, counterexamples, and negative controls |
| 7–10 | F# actor/journal prototype and FsQuint correspondence driver, with deterministic stub agents |
| 11–13 | Fault-injection harness, conventional-test baseline, mutation corpus, and reproducible measurements |
| 14–16 | Analysis, limitations, thesis text, and replication package with pinned versions and commands |

The replication package contains the model, source revision, trace corpus, fault seeds, test harness, result schema, scripts, and aggregate results. It excludes credentials, private prompts, raw agent transcripts, and private telemetry identities. Repository changes, package releases, host installation, and live effects require their normal owners and protected gates; the thesis proposal grants none of them.

## Interpretation limits

A finite model can omit a real failure mode; a replay adapter can map two different implementation states to one model state; injected faults may not match operational frequency; and one host or provider cannot establish distributed or cross-provider reliability. The thesis must publish the chosen abstraction, projection, bounds, fault seeds, negative controls, and observed divergences. A result may conclude that the added model/replay cost is unjustified for this bounded protocol; that is a valid finding. The [operations-research harness design](coordination/2026-08-31-operations-research-first-agent-orchestration-design.md) remains a broader product proposal, while this thesis asks whether one specific delegated lifecycle can be made inspectably recoverable.
