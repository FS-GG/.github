---
name: pipeline-preflight
description: Use when designing or changing an FS-GG CI pipeline with interacting jobs, shared state, reuse or costly qualification. Select and validate a proportionate preflight, including its economics and usability.
---

# Pipeline preflight

For a moderately complex or more complex pipeline, assess preflight before starting costly
qualification. Complexity means interacting behavior: fan-out/fan-in, conditional jobs,
shared artifacts or caches, cancellation, retries, evidence reuse, or publication. A simple
linear restore/build/test chain normally needs existing static checks, not a new model.

## Choose the smallest useful check

Inspect the actual workflow and called scripts. Name the defect to catch and the expensive
work it could avoid. Reuse existing validators and models first. Select:

- **Static:** configuration, dependency ordering, required inputs and artifact declarations.
- **Model:** a small Quint state machine when order, failure, retry, cancellation or shared
  state matters beyond static checks. Keep requirements independent of the execution plan.
- **Defer custom modeling:** a one-off or low-value change where setup and maintenance outweigh
  likely benefit. State the reason in the existing PR or plan; retain existing required checks.

Record the choice, scope, expected reuse, effort cap and validation in the existing work
artifact. Do not create a board item, approval cycle or telemetry service just for this assessment.
Cost estimates cannot waive required formal, release, security or authority checks.

## Bound the investment

Read [economics and ergonomics](references/economics.md) before building a custom check.
Use `python3 <this-skill>/scripts/preflight.py assess <estimate.json>` when arithmetic helps.
It is an advisory sensitivity calculator, not approval to run, skip or cancel a gate.
Missing inputs are unknown, not zero. Separate engineering/agent effort, billed runner minutes,
critical-path latency and maintenance; convert costs only with explicit rates.

Timebox the first reusable model to 30 minutes as a starting heuristic, including setup and
local diagnosis. At that limit, reduce scope or stop and reassess; do not silently spend hours
chasing a prototype. Choose a different cap when recorded reuse or defect impact warrants it.
For a single 30-minute pipeline run, prefer a short inspection and existing checks over a
12-hour custom model. A safety obligation may justify greater effort, but label it as risk
control, not demonstrated CI savings.

## Implement and qualify

For literal GitHub Actions dependencies, the helper's `graph` command checks the **actual YAML**
against independently stated ordering requirements. See [examples](references/examples.md).
It requires PyYAML in the existing environment; never installs dependencies itself. It refuses
unsupported dependency expressions rather than calling an unexamined graph safe. It checks
ordering only, not job success, conditions, reusable-workflow internals or GitHub expression semantics.

For Quint, use the available Quint modeling/language guidance and the public
[FsQuint CI example](https://github.com/FS-GG/FsQuint/tree/main/examples/CiPreflight).
Model only the relevant interaction. Bound samples, steps, process time and output; include a
reachable completion scenario and a deliberately broken plan that violates the intended property.
A simulation pass is sampled evidence, not exhaustive verification or proof that tests will pass.
Bind the model to real workflow inputs: derive or compare the relevant plan and maintain separate
requirements. A hand-maintained toy model without a drift check cannot gate a production workflow.

Put the selected check before the expensive work. Prove a known-bad input prevents workload
launch, good input permits it, and missing tools, timeout, malformed input and unknown outcomes
cannot look like a pass. Preserve existing enforcement and ADR-0084 coherent-run obligations;
this preflight is not a cancellation, reuse, merge or publication authority.

Measure cold setup and warm execution separately, first useful diagnosis, false blocks and
changed-file effort. Provide one local command with actionable errors. A practical starting
warm budget is the smaller of 60 seconds and 2% of the avoided run's critical path; justify
exceptions, and include CI scheduling/setup overhead when evaluating the actual end-to-end gate.
Keep model and workflow changes together. Reassess when the pipeline or usage changes; retire
or simplify a low-yield optional check through normal review. Report only measured savings.
