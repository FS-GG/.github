# Economics and ergonomics

The assessment is standard; custom model construction is conditional on value. Use a brief PR
paragraph for an obvious decision. Detailed estimates are for uncertain or costly investments,
not a new prerequisite for every commit. This follows
[ADR-0081](https://github.com/FS-GG/.github/blob/main/docs/adr/0081-adaptive-qualification-cadence-from-observed-cost-and-defect-yield.md).

## Compare the marginal alternatives

Compare existing static checks, a focused test and a model. Count only defects the additional
check could catch *earlier* and only work actually prevented. A counterexample is evidence of
capability, not evidence that such defects occur frequently. Never infer the probability from
one success or from injected failures. Do not count all canceled parallel jobs if they already ran.

The helper consumes explicit planning estimates in one currency:

```
per-run benefit = defect probability × detection probability × avoidable runner minutes × runner cost/minute
per-run cost = preflight runner minutes × runner cost/minute + false-block probability × triage cost
net benefit = horizon runs × (per-run benefit − per-run cost) − setup cost − horizon maintenance cost
```

Setup and maintenance costs include engineering/agent time at declared rates, tool provisioning,
model construction, CI integration, review, diagnosis and upkeep. Runtime overhead includes new
jobs and dependency installation, not only the solver's printed duration. Cache-cold runs may need
a weighted runtime estimate. Parallel runner minutes are not elapsed wall time. Developer wait
and incident impact can be important but are reported separately unless explicitly priced without
double-counting. The calculator does not monetize them implicitly.

Give low/high probability bounds and identify the source and horizon of the estimates. The
calculator labels all inputs as estimates and reports net benefit at both endpoints plus the
break-even run count. It returns **uncertain** when the decision changes across the range, and
**insufficient-data** when a needed input is absent. Even a positive result is a candidate for a
bounded pilot, not a claim of realized savings. A nonpositive best case rejects a savings claim.
A rare high-impact control may still be required independently of economics.

## Keep the workflow usable

Before adoption, observe a fresh checkout and a warm rerun. Record, in the same PR:

- One command; prerequisites and downloads are explicit, shared and versioned.
- Time to first useful diagnostic, setup time and warm/cold end-to-end latency.
- Counterexample names the real job/input and the relevant property; unsupported input explains
  the missing capability instead of emitting a generic red status.
- A normal workflow edit changes one plan and its independent requirements only when intended;
  no parallel copied algorithm, repeated tool pin, approval form or hand-written receipt chain.
- A failing preflight blocks only its dependent expensive work; unrelated feedback stays available.
- The good, bad and unavailable-tool cases are reproducible without actually running the long suite.

Stop an optional prototype when its agreed cap is exhausted or its best-case benefit is below
remaining effort. Record what was learned and use the cheaper alternative. Do not use sunk effort
as a reason to continue. Reuse across repositories counts only when concrete consumers are identified.
