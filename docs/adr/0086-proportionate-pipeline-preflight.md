# ADR-0086: Proportionate preflight for complex pipelines

- **Status:** Accepted
- **Date:** 2026-09-18
- **Decision owners:** FS-GG accountable programme owner
- **Affects:** new and materially changed FS-GG CI pipelines

## Context

A small executable model can expose a scheduling or state-management defect before an expensive
qualification run. Modeling also costs engineering and agent time, provisioning, maintenance and
additional CI latency. A twelve-hour model for a one-off thirty-minute run is normally a poor
investment. Making every workflow use Quint would replace wasted compute with wasted preparation.

The owner requested a standard process with a skill and reusable code, including an efficiency and
usability sanity check. Existing [ADR-0081](0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md)
keeps economics advisory and preserves mandatory qualification. Existing
[ADR-0084](0084-semantic-reuse-never-cancels-coherent-validation.md) governs coherent execution and reuse.
This decision adds a design-time preflight choice; it changes neither existing authority boundary.

## Decision

For a moderately complex or more complex pipeline, assess preflight before the first costly run
of a new or materially changed design. Complexity is behavioral: fan-out/fan-in, conditions,
shared artifacts/caches, retries, cancellation, evidence reuse or publication. A simple linear
restore/build/test pipeline normally uses its existing checks. Assess substantive workflow/script,
job graph, matrix, cache identity and failure-policy changes; a wording-only edit needs no repeat.

The standard is a **proportionate assessment and chosen check**, not mandatory Quint everywhere:

1. Reuse the cheapest existing static check or focused test that detects the relevant defect.
2. Use a bounded Quint model when state or interleavings justify it. Derive or compare the model's
   relevant plan against the real workflow; state intended properties independently. Include a
   completion witness and an injected defect. A toy or stale model cannot certify a real pipeline.
3. Defer custom modeling when expected reuse, avoidable work and risk do not justify construction.
   Record the choice briefly in the existing PR or plan; no separate approval or artifact ceremony.
   Existing mandatory checks still apply. A high-impact obligation can justify investment independently
   of compute savings; label that rationale accurately.

Before investing, estimate horizon/reuse, setup and maintenance, defect/detection uncertainty,
false blocks and avoidable work. Keep engineer/agent effort, runner minutes and wall latency separate;
compare money only with explicit rates and do not infer savings from an injected counterexample.
Missing telemetry prevents a savings claim, not routine delivery. No mandatory ROI service or form.

A 30-minute first-model timebox and a warm preflight budget of the smaller of 60 seconds or 2% of
pipeline wall time are starting heuristics, not universal gates. Include cold provisioning, new job
startup, actual integration and repair time in the investment decision. At the agreed cap, simplify,
stop or explicitly revise the estimate; sunk cost alone never justifies continuing. A one-off run
normally gets short inspection and existing checks rather than new infrastructure.

A selected enforcing preflight runs before its dependent expensive workload. Good inputs must allow
launch; known-bad inputs and missing tools, timeouts or uninterpretable outcomes must not. A pass
asserts only the checked scope, not application correctness or exhaustive state-space coverage.
Counterexamples must identify relevant jobs/state. One local command, bounded execution and explicit
prerequisites are the usability baseline. Existing required checks, publication authority, semantic
reuse and coherent-run obligations remain independently enforced.

## Ownership and rollout

`.github` owns the process skill, economics and static dependency helper. FsQuint owns generic F#
trace/tool integration and its runnable example; reuse its versioned package, not copied algorithms.
Repositories own workflow inputs, domain models, independent properties and acceptance decisions.

Apply this standard prospectively; prioritize existing pipelines by recurrence, avoidable waste and
interaction risk rather than mass-retrofitting all repositories. The
[adoption roadmap](../roadmaps/pipeline-preflight.md) distinguishes available source, pilot evidence,
packaged skill distribution and actual receiver adoption. A merged skill is not fleet deployment.

## Consequences

Pipeline checks gain a common selection and validation process without requiring a model or new CI
job for every change. Optional checks must justify their integration and upkeep. Static and modeled
evidence remain scoped, and staged adoption avoids mistaking a published standard for fleet coverage.
