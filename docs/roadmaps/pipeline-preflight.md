# Proportionate pipeline preflight

Identity: **PIPELINE-PREFLIGHT-01**. Owner: `.github`, with consumer repository owners.
Status: standard and initial skill/helpers implemented; staged distribution and production adoption pending.
Authority: [ADR-0086](../adr/0086-proportionate-pipeline-preflight.md).

## Outcome

Every new or materially changed moderately complex FS-GG pipeline gets an early, proportionate
preflight decision. Engineers and agents reuse static checks or models to catch likely costly mistakes,
without spending more on optional verification than its expected value. Existing complex pipelines
are adopted on change or when measured waste justifies a targeted retrofit. No flag-day migration.

The standard requires a short assessment, not one new model per pipeline. The process may conclude
“existing checks suffice” or “defer custom model.” Mandatory safety and qualification remain intact.

## Delivered foundation

- [x] Establish static/model/defer routes and an explicit effort/latency reassessment boundary.
- [x] Author the `pipeline-preflight` skill with costs, usability and failure-path guidance.
- [x] Supply a read-only economics calculator and actual-YAML dependency-order checker.
- [x] Retain one generic implementation; refer to the public FsQuint F# example for process/trace use.
- [x] Exercise one-off, recurring, uncertain and false-block-dominated estimates; reject invalid inputs.
- [x] Exercise actual `coord-engine.yml` dependency ordering and reject removal of its prerequisite.
- [x] Prove the helper's CLI permits the valid plan and blocks malformed and known-bad plans before
  a stand-in workload marker. This is a local control, not a deployed production gate.

The source lives in `.agents/skills/pipeline-preflight` and its required identical Claude mirror.
It is initially repository-native; no new package or fleet version claim is made. The existing
skill-quality CI runs the helper tests. The real-YAML check binds `engine` to `change-completeness`;
it does not model conditions, job success, retries or external Actions behavior.

## Rollout, in value order

| Stage | Owner | Exit evidence and investment boundary |
|---|---|---|
| P1: evaluate one frequent pipeline | Pipeline owner + `.github` | Select from live run/defect history, not job count alone. Compare existing validator, focused test and model. Record reuse horizon, setup/maintenance estimate, warm/cold latency and a first-use effort cap in its existing PR. Spend at most a short inspection to select the pilot; unknown data is explicit. |
| P2: qualify the smallest useful check | Pipeline owner; FsQuint only if a generic API gap exists | Bind to actual workflow inputs, retain independent requirements, demonstrate a concrete historic/injected defect, a nonvacuous good case and no workload launch on failure/unknown. Run shadow comparison before making an unfamiliar model enforcing. Stop or reduce scope at the agreed cap. |
| P3: distribute the proven skill/helper | `.github` | Add the skill to the existing coordination-kit authority and regenerate inventories/digests; use the normal coherent publication and receiver update path. Verify one receiver's installed skill, helper, links and pins. Do not hand-copy source or create a new updater. Batch with a warranted release where that avoids disproportionate release overhead. |
| P4: adopt on change | Each FS-GG repository owner | For each touched complex pipeline, record static/model/defer, actual binding, expected savings/risk rationale and usability evidence. Required enforcement changes are normal reviewed workflow changes. A shared policy or installed skill alone is not adoption evidence. |
| P5: review and retire poor investments | Pipeline owner | Use ordinary observations of execution overhead, false blocks, actionable defects, maintenance and changed-file effort. Retain, simplify or remove optional checks through review. No separate daily service or blocking telemetry requirement. |

Do not enlarge this work into a generic GitHub Actions interpreter. The static helper accepts literal
`needs` graphs only. Dynamic workflow, cache, retry, cancellation and publish semantics need a bounded
model of the selected behavior, or an explicit out-of-scope decision. Quint sample bounds remain
visible; exhaustive verification is a separate choice, never inferred from simulation success.

## Economic acceptance

Report estimates as ranges. Charge only avoidable future work, include false-block investigation,
and price engineering/agent effort separately from billed runners. Count reuse only for identified
consumers within a stated horizon. Never use model construction as its own evidence of value.

The checked-in examples deliberately use explicit illustrative rates:

- One run, 12 hours of setup at 60/hour, 30 runner minutes at 3/hour: even perfect detection cannot
  justify the investment as compute savings. The helper reports a negative best-case benefit.
- 10,000 runs, 30 minutes of setup, one hour of maintenance, 2–5% relevant defect probability and
  80% detection: a small check can be worth a pilot at those assumed rates. These are not observed
  FS-GG failure rates or claimed savings.
- If the lower bound loses money and the upper bound wins, report uncertainty; reduce the experiment
  or collect cheap observations rather than declaring a positive return.

Actual setup effort and cold/warm measurements accompany the pilot. Default heuristics are 30 minutes
for a first model and warm overhead below min(60 seconds, 2% of pipeline wall time). These are adjustable
planning bounds, not evidence thresholds or a bypass for required checks. Stop when the optional
investment no longer makes sense, including when a one-off run is cheaper than building the guard.

## Usability acceptance

A fresh receiver can discover the skill and run one documented command without source vendoring.
Prerequisites are explicit; tool provisioning is reused and content/version pinned. Diagnostics name
the failing property/job and explain unsupported inputs. A workflow edit does not require updating
copied plans, multiple independent pins or a separate approval checklist. Good, bad and unavailable-tool
controls use a cheap workload marker, not repeated long CI runs. Report time to first useful result,
not just the solver's internal time.

## Completion boundary

The foundation is reviewable now. P1–P5 are intentionally staged adoption, not claimed complete fleet
coverage. Track actual consumer adoption in the existing consumer PRs and link them here. This roadmap
creates no new required check across the fleet by itself, and changes no release, coherence, reuse or
operational activation authority.

## Foundation validation — 2026-09-18

Thirteen helper tests pass, including the actual workflow mutation, invalid estimates, uncertain
returns, cycles, duplicate YAML keys, unsupported dependency expressions, absent PyYAML and CLI
launch controls. Skill metadata, mirrored roots, references, trigger fixtures, discovery budgets,
ADR coherence and workflow path coverage pass their existing validators.

Ten fresh CLI processes against the actual `coord-engine.yml` took 58.4 ms for the first run,
50.1 ms median and 59.1 ms maximum on this Linux checkout with Python/PyYAML already available.
The thirteen-test helper suite took 0.20 seconds. These observations include Python startup and
YAML parsing, but exclude provisioning, CI scheduling and cold receiver setup. They establish that
the static helper is inexpensive locally; P1–P3 still owe consumer setup and end-to-end measurements.
