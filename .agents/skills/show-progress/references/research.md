# Prior art and design decisions

Research checked 2026-10-13. This is an original synthesis of the linked sources, not
a copied standard. Government acquisition guidance informs baseline discipline; local
software/agent evidence must supply the actual numbers. These sources do not validate
any FS-GG percentage or ETA. Method choices below are local reporting decisions.

| Primary source | Relevant finding | Application and limit |
|---|---|---|
| [GAO Schedule Assessment Guide, GAO-16-89G (2015)](https://www.gao.gov/products/gao-16-89g), especially practices 6, 8–10 in the [full guide](https://www.gao.gov/assets/gao-16-89g.pdf) | Dependencies, resources, schedule risk, actual progress and retained baselines determine credible dates. | Forecast the remaining dependency path and explain slippage. Elapsed job totals are not a programme schedule. |
| [GAO Cost Estimating and Assessment Guide, GAO-20-195G (2020)](https://www.gao.gov/products/gao-20-195g) | Reliable estimates define scope, technical baseline, breakdown, assumptions, data, uncertainty and updates against actuals. | Retain original/previous/current effort estimates and explain changes. Unknown actual effort cannot support a complete actual-plus-remaining total. |
| [NASA EVM tutorial](https://www.nasa.gov/ocfo/ppc-corner/evm/tutorial/) | Accomplished work, planned value and actual expenditure answer different questions; variance analysis includes cause, impact and corrective action. | Separate acceptance progress from effort consumption and forecast change. Do not claim EVM or calculate its indices without its inputs. |
| [GAO Agile Assessment Guide, GAO-24-105506, revised December 2023](https://www.gao.gov/products/gao-24-105506) and [metrics guidance](https://www.gao.gov/assets/d24105506.pdf) | Incremental software delivery needs monitoring suited to its workflow; a combination of measures avoids optimizing velocity at the expense of quality. | Show accepted outcomes, flow, quality/complications and adoption. Commits, test counts and story points alone do not measure completed outcomes. |
| [The Kanban Guide, May 2025](https://kanbanguides.org/the-kanban-guide/2025.5/) | Defines WIP, throughput, age and cycle time; an SLE combines elapsed time with a probability, grounded in history where available. | Declare start/finish boundaries and the cohort. A provisional best guess remains visibly different from a historical probabilistic forecast. |
| [HM Treasury optimism-bias guidance (2013)](https://www.gov.uk/government/publications/green-book-supplementary-guidance-optimism-bias) | Estimates should be corrected using experience from comparable projects and the current project's characteristics. | Cross-check internal estimates against local reference classes. Do not transplant generic construction uplifts into software estimates. |
| [Jørgensen and Grimstad, software-effort estimation and misleading information (2007)](https://web-backend.simula.no/sites/default/files/publications/Simula.SE.112.pdf) | Empirical work finds that irrelevant anchors such as client budgets can bias effort estimates; additional analysis does not reliably remove the influence. | Derive the current remaining-work estimate from evidence before comparing it with a desired deadline or earlier chat guess. Retain the difference rather than forcing agreement. |
| [ProKanban: Forecasting Through Epic Uncertainty (2026)](https://prokanban.org/blog/forecasting-epics) | Recommends moving from analogies to known work and throughput, then refreshing forecasts as scope, pace and WIP change. | Use bottom-up/reference-class estimates before comparable throughput exists. Its worked percentile choices are examples, not guarantees for this programme. |
| [ProKanban: Forecasting in the Real World (2025)](https://prokanban.org/blog/trustingmcs) | Examines Monte Carlo claims against real workflow data and cautions against unsupported generalizations. | Test model relevance and expose sensitivity. More simulations do not repair a biased cohort or missing dependencies. |
| [Hyndman and Athanasopoulos, Forecasting: Principles and Practice, chapter 5](https://otexts.com/fpp3/toolbox.html) | Distributional forecasts require uncertainty evaluation; rolling-origin evaluation uses only observations available at each historical forecast date. | Store forecasts unchanged, then evaluate future errors and interval coverage. Distinguish prediction intervals from judgement ranges. |
| [NIST/SEMATECH: censoring](https://www.itl.nist.gov/div898/handbook/apr/section1/apr131.htm) | Unfinished observations contain partial event-time information rather than observed completion times. | Keep open work and failed/cancelled attempts visible. Survival-analysis use is an inference from the statistical principle and needs suitable censoring assumptions. |
| [Forsgren et al., The SPACE of Developer Productivity (2021)](https://www.microsoft.com/en-us/research/publication/the-space-of-developer-productivity-theres-more-to-it-than-you-think/) | Developer productivity is multidimensional and cannot be reduced to individual activity or a single measure. | Treat PR/test/agent activity as explanatory context. This report estimates readiness; it does not score worker productivity. |

## Selected approach

Combine evidence-based acceptance units, explicit effort re-estimation and conditional
schedule forecasts. Use empirical distributions only for a comparable workflow with
documented data; otherwise use a clearly labelled scenario range. Keep the original
baseline, permit readiness to fall when scope grows, and explain unexpected work at the
item level. Keep report generation cheap by reusing existing owner evidence.

Rejected shortcuts: a universal “code done = 90%” rule; linear extrapolation from a guessed
percentage; counting every PR as equal delivered value; converting model tokens or elapsed
chat time into labor; promising statistical confidence without a distribution; deriving
programme dates by adding independent item percentiles; and silently dropping blocked or
unfinished items from the denominator. A desired date is a constraint, not evidence.

## Research maintenance

Revisit this synthesis when the reporting model or available evidence changes. Prefer
maintained primary sources, inspect the actual relevant content, and record publication
and access dates. Update the method and report its revision when changing assumptions;
do not reinterpret older reports silently. Extensive research is needed for method design,
not repeated for every routine progress snapshot.
