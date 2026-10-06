# Process efficiency telemetry: prior art

Research date: **2026-10-06**. Supports **V2-EFF-01** in the
[V2 roadmap](../github-substrate-v2-roadmap.md#v2-eff-01--process-efficiency-telemetry--2026-10-06).
The [design](../designs/process-efficiency-telemetry.md) and
[implementation roadmap](../roadmaps/process-efficiency-telemetry.md) apply these findings.

## Question and method

How can a delivery dashboard distinguish useful engineering, necessary process, avoidable
bureaucracy, retries, repair and waiting, while explaining the problems behind them? The desired
unit is an accountable work item, including its unsuccessful attempts and shared costs. A merged
PR, a large token total, or a green test result alone cannot answer that question.

This review searched primary research, author-hosted papers, specifications and product documentation
across delivery performance, developer experience, reliability, process mining, agent observability,
evaluation, provenance and cost accounting. The table records the evidence and the proposed local
application separately. It is a design synthesis, not a systematic literature review or a product
benchmark. No reviewed platform was installed or measured in FS-GG.

The ACM pages for SPACE and DevEx returned access errors; the accessible Microsoft Research account
and author-hosted DevEx paper supplied the evidence used here. MAST refers to the paper's October
2025 revision, not its smaller initial study. Living documentation may change after this review;
implementation must pin any adopted wire convention. Historical experimental results below do not
estimate the benefit of this proposal.

## Evidence and design decisions

| Prior art and primary source | What it contributes | Application to FS-GG and limits |
|---|---|---|
| **DORA, software delivery metrics**, current guide reviewed in 2026: [guide](https://dora.dev/guides/dora-metrics/) | Five measures cover throughput and instability: change lead time, deployment frequency, failed deployment recovery time, change fail rate and deployment rework rate. Context matters when comparing services. | Pair efficiency with accepted outcomes and downstream quality. Keep production deployment rework distinct from generic agent retries or PR revisions. Do not call local attempt metrics DORA metrics, combine unrelated products into a league table, or optimize a single score. |
| **SPACE**, Forsgren et al., 2021: [author institution](https://www.microsoft.com/en-us/research/publication/the-space-of-developer-productivity-theres-more-to-it-than-you-think/) | Productivity has multiple dimensions, including performance, activity, collaboration and flow. Individual activity counts are inadequate proxies. | Display outcome, resource use, delay, quality and collaboration together. Message count, tokens and commits are diagnostic quantities, not productivity scores. Avoid ranking people or rewarding the creation of many small items. |
| **DevEx**, Noda et al., 2023: [author-hosted paper](https://www.michaelagreiler.com/wp-content/uploads/2024/06/DevEx-WhatDrivesProductivity.pdf) | Feedback loops, cognitive load and flow explain friction; system measurements and developer feedback provide different evidence. | Expose slow feedback, repeated context recovery and interrupted work. Human feedback can supplement machine observations. Token length is not measured human cognitive load, and an agent summary is not a developer satisfaction survey. |
| **Google SRE, eliminating toil**: [workbook](https://sre.google/workbook/eliminating-toil/) | Repetitive, reactive work with little enduring value differs from engineering that creates lasting improvements. Not all operational work is toil. | Separate necessary assurance and coordination from avoidable process work. A reusable diagnostic tool can be useful work even though it changes no product feature. Google's organizational toil target is not a threshold for this programme. |
| **Google SRE, postmortem culture**: [workbook](https://sre.google/workbook/postmortem-culture/) | Useful incident analysis identifies contributing factors and actionable improvements without assigning personal blame. | Completion assessments describe observed problems, evidence, recovery and a small number of owned improvements. A report count is not a success metric. Give ordinary items short summaries; do not require a full incident ceremony on every merge. |
| **GitLab Value Stream Analytics**: [official documentation](https://docs.gitlab.com/user/group/value_stream_analytics/) | Event pairs define stage duration; completed-stage calculations, blocked-time treatment and overlapping stages affect interpretation. | Preserve repeated stage visits and open-item age instead of collapsing everything into one start/end pair. State exclusions. Tests inside review do not create additional elapsed time that can be added to review duration. |
| **GitHub Actions metrics**: [official documentation](https://docs.github.com/en/actions/how-tos/administer/view-metrics) | Usage and performance views expose runner consumption, execution, queueing and failures. | Import runner and queue facts as separate dimensions. Actions data does not reveal whether a retry fixed a product defect, repeated a policy check, or recovered infrastructure. Native run identity remains necessary for deduplication. |
| **OCEL 2.0**, object-centric event logs: [specification overview](https://www.ocel-standard.org/specification/overview/) and [paper](https://arxiv.org/abs/2403.01975) | Events relate to multiple objects rather than requiring every event to belong to one flattened case. | Relate item, attempt, operation, PR, CI run and release explicitly. A shared build may serve several items without being charged in full to each. Adopt the identity model, not a new graph database or a wholesale OCEL transport migration. |
| **OpenTelemetry tracing**: [trace API](https://opentelemetry.io/docs/specs/otel/trace/api/) | Parent-child context and span links represent different relationships, including links across traces. | Preserve causal links across asynchronous work and handoffs. Telemetry spans support observation; they do not confer execution authority or establish business completion. Trace loss remains visible rather than proving inactivity. |
| **OpenTelemetry GenAI conventions**: [official convention repository](https://github.com/open-telemetry/semantic-conventions-genai) | Shared conventions cover generative AI observation surfaces, with an independently evolving specification. | Use an adapter for model and tool observations, pin its revision and document stability. Do not freeze a speculative attribute into the canonical store or assume every provider exposes identical usage fields. |
| **Langfuse annotation queues**: [official documentation](https://langfuse.com/docs/evaluation/evaluation-methods/annotation-queues) | Expert scoring of observations supports evaluation, agreement checks and judge calibration. | Store assessments separately from raw events and retain their author, rubric and revision. Use sampled calibration and disputed cases, not a mandatory human annotation queue for each delivered item. |
| **LangSmith complex-agent evaluation**: [official documentation](https://docs.langchain.com/langsmith/evaluate-complex-agent) | End-to-end results and agent trajectories answer different evaluation questions. | Evaluate whether the item met acceptance and whether the route was efficient. A different valid sequence of tools is not automatically process failure. Route checks need semantic expectations, not one supposedly ideal transcript. |
| **Phoenix evaluation**: [official documentation](https://arize.com/docs/phoenix/evaluation/how-to-evals) | Code-based and model-based evaluation have different roles and can attach assessments to observations. | Compute numbers and validate evidence references deterministically; let a model explain meaning and propose causes. Record the evaluator's own consumption. An external evaluation service is not required for the initial implementation. |
| **Anthropic, demystifying agent evaluations**, 2026: [engineering article](https://www.anthropic.com/engineering/demystifying-evals-for-ai-agents) | Tasks, trials, transcripts and outcomes are distinct; complementary graders and evaluation harness quality matter. | Preserve each failed trial while counting the delivered item once. Distinguish product, harness and environment failures. A final successful outcome must not erase unsuccessful work or prove the route was efficient. |
| **LLM-as-a-judge**, Zheng et al., 2023: [paper](https://arxiv.org/abs/2306.05685) | Model judgments can exhibit position, verbosity and self-enhancement biases and reasoning limitations. | Calibrate the completion analyst on held-out evidence, allow unknown causes, and retain corrections. Self-reported confidence is not a calibrated probability. Do not import agreement rates from a different benchmark as local validation. |
| **MAST**, Cemri et al., 2025: [paper](https://arxiv.org/abs/2503.13657) | The revised study develops 14 failure modes in three broad groups around system design, inter-agent alignment and task verification. | Seed orchestration, handoff and verification categories, then add local custody, CI, bookkeeping and telemetry problems. Multiple factors can contribute. Published category frequencies do not describe FS-GG. |
| **AI Agents That Matter**, Kapoor et al., 2024: [paper](https://arxiv.org/abs/2407.01502) | Accuracy, cost, reproducibility and evaluation design need joint attention; extra computation can improve apparent success. | Compare accepted outcomes and total attempt cost together. Retried successes must retain their original cost. Cost reductions count as improvements only while required quality and acceptance remain intact. |
| **METR, early 2025 developer study**: [paper](https://metr.org/Early_2025_AI_Experienced_OS_Devs_Study-paper.pdf) | A controlled study of experienced developers working in familiar repositories illustrates why measured outcomes can differ from perceived speed. | Use observed time and outcomes alongside qualitative summaries. Its developer, repository and model population is specific; it does not establish a universal effect of AI assistance. |
| **METR, 2026 measurement update**: [update](https://metr.org/blog/2026-02-24-uplift-update/) | Selection into tasks and tools, noncompletion, changing work and concurrent agents complicate time estimates and causal conclusions. | Include abandoned and open items, stratify comparisons and distinguish human effort, agent effort and elapsed time. Before/after dashboard differences are associations unless the evaluation design supports a causal claim. |
| **W3C PROV-O**, 2013 recommendation: [specification](https://www.w3.org/TR/prov-o/) | Provenance distinguishes entities, activities, attribution, derivation and revision. | Corrections explicitly supersede prior attribution while retaining the original record and evidence. A new record under another item is not a repair unless the old one is excluded by a supported revision relation. No RDF infrastructure is implied. |
| **FinOps FOCUS 1.3**, cost and usage dataset: [specification](https://focus.finops.org/docs/specification/v1-3/datasets/cost-and-usage/) | Cost and usage require explicit quantities, units and different cost bases. | Keep observed usage, estimated cost, billed cost, currency and price version separate. Allocation of a shared cost is not a new charge. This review uses the named 1.3 specification for concepts, not a claim that it is the latest version. |
| **Google, flaky tests**, 2016: [engineering article](https://testing.googleblog.com/2016/05/flaky-tests-at-google-and-how-we.html) | Inconsistent results under the same code can impose investigation and retry costs, while retries and quarantine can also hide real failures. | Record input and environment comparability before labeling a failure flaky. A fail followed by a pass is insufficient to establish a root cause. Retain genuine defects discovered by repeated checks and never weaken required checks to improve efficiency figures. |

## Synthesis

The strongest common pattern is a layered system: native facts establish what happened; deterministic
reducers calculate quantities; an evidence-linked assessment explains likely causes; a dashboard
connects those explanations to outcomes and possible improvements. Combining these layers into an
unqualified model-generated efficiency score would lose both auditability and useful uncertainty.

The design therefore adopts six decisions:

1. **Measure outcomes and total effort together.** Include unsuccessful work, shared effort and
   downstream repair. Keep product delivery separate from publication and installed adoption.
2. **Make process purpose explicit.** Necessary validation, coordination and diagnosis can be valuable.
   Avoidability is a separate assessment that needs evidence and a feasible alternative.
3. **Represent relationships and revisions.** An event can concern several objects. Corrections must
   preserve history and change current attribution without duplicating delivered outcomes or costs.
4. **Generate a bounded completion assessment.** The analyst explains findings from a fixed evidence
   snapshot and contributes no fabricated durations, charges, success facts or new authority.
5. **Show uncertainty and unfinished populations.** Unknown usage, missing attempts, pending reviews
   and aging work remain visible. Partial reports are useful if their limitations are explicit.
6. **Evaluate the measurement system itself.** Calibration, privacy checks and analyst cost belong in
   the pilot. The programme should not create more review bureaucracy than the information can justify.

## Options considered

| Option | Benefit | Disposition |
|---|---|---|
| Add more CI counts to the current dashboard | Cheap and directly observable | Necessary facts, but insufficient for causes, meaningful work or agent coordination cost. |
| Publish raw agent transcripts and ask a model for one score | Fast prototype | Reject: privacy, prompt injection, unsupported causality, missing denominators and poor auditability. |
| Adopt an external observability suite as the new authority | Mature tracing and evaluation interfaces | Defer: the existing store already has reviews, activities, complications and usage attribution. Add export adapters only for a demonstrated consumer need. |
| Build a new process-mining service | Rich event analysis | Defer: retain object relations and repeated stage visits in the existing system first. A second service would add operating cost before the local questions are answered. |
| Extend existing facts, reviews and the Pages projection | Reuses lineage, delivery and privacy boundaries | Selected. Deliver one real item explanation early, then calibrate the aggregate metrics and expand adoption. |

## Evidence still needed locally

This research does not establish current waste, retry frequency, which cause dominates, or how much a
proposed improvement would save. The next evidence is a labeled sample of real item histories,
producer coverage checks, cost reconciliation, and an end-to-end completion assessment on the installed
route. The implementation roadmap owns that work. Product documentation demonstrates available
mechanisms, not that a particular tool will outperform the existing stack here.
