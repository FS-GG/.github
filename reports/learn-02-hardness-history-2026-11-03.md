# Historical hardness validity and time evidence — 3 November 2026

Delivery [#5276](https://github.com/FS-GG/.github/issues/5276), parent [#5248](https://github.com/FS-GG/.github/issues/5248).
This is a reproducible initial historical analysis, **not a completed validation or routing activation**.
The full Delivery remains open for independent labels, broader qualified outcomes and held-out evaluation.

The existing hardness rubric has **weak, uncertain duration associations in the recovered cohort** and
has **not established validity across models or effort levels**. Expanded grouped prediction also finds no improvement from H or the full dimension vector over simple baselines; a text-length signal is sensitive to the largest parent cluster. Time worked until satisfactory completion
is a valuable empirical hardness signal. This report calculates the strongest duration proxies actually
supported by the retained records, preserves stopped/failed/open work, and quantifies the effect of pauses
and feature timing. It does not substitute calendar age or silent commit gaps for measured active work.

The most useful exploratory findings are:

- H and the full D1–D8 vector fail to beat the training-median baseline in parent-group prediction.
- Text length's pooled advantage reverses with equal-parent weighting or removal of the largest parent cluster.
- D8 is constant at 3 across all 34 runtime items; D7 is nearly constant. This cohort cannot validate their full scale.
- 33 of 34 runtime items were created in November, while most historical issues were created in July; broad historical transfer is untested.

The broad census contains 12,175 native records. The actual timeline cohort is 34 issues and 74 attributed
invocations, not 12,175 matched experiments. Observed worker model variation exists; worker effort variation
does not. Both worker groups use observed `high` effort. Medium-effort observations belong to analysts,
so an effort effect for comparable worker tasks is not estimable from these data.

## Evidence populations and completeness

| Evidence population | Actual coverage | What it establishes |
| --- | --- | --- |
| Accessible native inventory | 27 repositories; 3,397 issues; 8,778 PRs | All accessible connection pages terminated; independent repository totals reconcile |
| Initial source/history audit | 27 records: 20 issues, 7 PRs | Complete requested body/comment connections; selected event types, not all event types |
| Additional attributed histories | 34 issues; 33 new to initial audit | Combined 53 unique issue histories plus 7 PR histories |
| Legacy item usage exports | 34 identities | Selected attributed usage, not model-response matrix |
| Recovered invocation telemetry | 74 invocations, 12,169 responses, 34 issues | Exact invocation attribution and observed response timestamps/profiles |
| Local admitted transcript timing scan | 74 files; 53,759 JSONL records; 234,690,624 bytes streamed | Zero parse gaps; no `system/turn_duration` events in this specific format |
| Qualified matched cross-profile anchors | 0 established | Frozen input/start/context/environment/quality equality remains unproven |
| Verified active execution intervals | 0 established in inspected sources | Continuous worked time remains Unknown; observed envelopes are available |

Inventory cutoff was 2026-11-03 07:11:12.761953 UTC; collection ended 07:14:15.150636 UTC.
No inventory rows were updated after that cutoff. The runtime timestamp read was observed at 07:57:04 UTC,
but all attributed response timestamps lie between 1 November 10:46:53 and 2 November 08:28:49 UTC,
before the inventory cutoff. These historical response records have partial population and unknown
terminal coverage. Source completeness is not task-lifetime completeness.

The inventory retained native completed, not-planned, duplicate, open, reopened, merged and closed-unmerged
states. It does not infer that every native completed closure passed the same independent quality contract.
The 53-history issue sample contains 27 completed closures, four not-planned closures and 22 open issues.
The runtime cohort contains 18 native-completed closed and 16 open issues; no closed runtime case has a missing reason or not-planned/duplicate reason. Open observations remain in the observed-envelope
analysis and are right-censored for calendar completion; completed-only associations are explicitly conditional.

## Actual duration results

The frozen scorer is the unmodified #5273 candidate at commit
`0125a4f889d01ef90cffbf8c8b42956b31127782`, script SHA-256
`dcf00f20b5502049964f7efbb3aee4b07457887fb1a07ea49b184d55681ca24d`.
For the 34 runtime issues, features come from the latest qualified native body revision at the **first
observed dispatch**, not the completion body. All 34 qualify under the audited native whole-body snapshot
adapter. First observed dispatch is not proof of first actual work. Titles are excluded from this scorer;
a future title predictor must reconstruct rename history separately.

An observed invocation envelope extends from its first to last response timestamp. Issue-level union counts
overlap once; summed invocation envelopes count concurrent agents separately. Neither proves continuous
execution. The primary available *till native closure* measure truncates observed envelopes at final native
closure and uses the 18 closed cases. All-observed results retain 283 responses after closure, which may
reflect review, in-flight completion, continuation or lifecycle ambiguity; their cause is not inferred.
Final closure avoids the separate known first-closure/reopen error, but native closure is still not a
harmonized quality endpoint.

| Baseline H versus duration | n | Parent clusters | Spearman rho | Kendall tau | Median duration |
| --- | ---: | ---: | ---: | ---: | ---: |
| Response-envelope union through native closure | 18 | 5 | 0.090 | 0.035 | 2.16 h |
| All observed response-envelope union, open retained | 34 | 7 | 0.097 | 0.048 | 2.64 h |
| Sum of invocation envelopes | 34 | 7 | 0.072 | 0.041 | 4.23 h |
| First observed dispatch to native closure | 18 | 5 | 0.217 | 0.117 | 2.32 h |
| Hypothetical 1 h response-gap cap, union | 34 | 7 | 0.184 | 0.107 | 2.54 h |
| Hypothetical 8 h gap cap, union | 34 | 7 | 0.097 | 0.048 | 2.64 h |
| Hypothetical 24 h gap cap, union | 34 | 7 | 0.097 | 0.048 | 2.64 h |
| Creation-body H versus calendar, all closures | 31 | 18 | 0.323 | 0.223 | 4.60 h |
| Creation-body H versus calendar, native completed only | 27 | 14 | 0.277 | 0.162 | 4.98 h |

The creation/calendar association's empirical parent-cluster bootstrap 95% interval is
**[-0.047, 0.666]** for Spearman and **[-0.022, 0.516]** for Kendall, using 500 resamples and seed 5276.
The native-completed-only creation/calendar result is rho **0.277**, 95% interval **[-0.062, 0.615]**, retaining the four not-planned closures separately in the all-closure diagnostic and the outcome registry. The 18-case runtime till-closure result already consists entirely of native completed issues. Native completion remains a stronger status proxy, not uniform accepted quality. These are exploratory intervals conditional on the selected sample and observed native parent links,
not population-valid confidence guarantees. Supplier/duplicate/dependency relations are incompletely
observed. Most runtime endpoints have fewer than eight parent clusters: the executable audit guard
withholds their nominal bootstrap intervals rather than presenting 34 issues or 12,169 responses as
independent samples. Eight is an explicit small-sample guard, not a universal sufficiency theorem.
No asymptotic Spearman p-value is reported; [SciPy documents the small-sample limitation](https://docs.scipy.org/doc/scipy/reference/generated/scipy.stats.spearmanr.html).
The bootstrap samples whole parent components; `paired=True` alone would not create a cluster bootstrap.

Using current bodies instead changes runtime-envelope rho from **0.097 to 0.175**; three of 34 H totals
changed after the observed boundary. Creation versus first-observed-dispatch totals happen to agree in
this cohort. Current-body association is a retrospective sensitivity check, not predispatch prediction.
This is one concrete reason not to calibrate on final accepted issue bodies.

Actual categorical policy is High at H≥13 **or D3≥2 or D5≥2**, Medium at H≥7 otherwise, Low otherwise.
Its observed groups are Low n=1, Medium n=6, High n=27, with median all-observed envelopes 2.33, 2.63,
and 2.69 hours respectively. These imbalanced selected groups do not validate the 7/13 boundaries or
technical overrides. Ten synthetic controls test the literal thresholds, overrides and safety floor;
three semantics controls additionally expose construct sensitivity described below.

The three handcrafted semantic probes keep the same inert spelling-edit obligation. Its minimal text
scores **H=1, Low, no safety floor**. Appending explicit out-of-scope exclusions scores **H=19, High,
safety floor true**; appending a quoted historical example that adds no requirement scores **H=18, High,
no safety floor**. This demonstrates sensitivity to irrelevant/negated context in these examples, not
population prevalence or a validated human truth set. The actual lexical rubric follows its rules in
all ten controls, but literal policy conformance does not establish construct validity. A versioned
successor would need outcome-blinded obligation/paraphrase/negation adjudication and held-out evidence;
the frozen rubric is preserved unchanged here.

## Model comparison and profile provenance

| Observed worker profile | Issues | Parent clusters | H range | H versus worker-envelope rho | Kendall tau |
| --- | ---: | ---: | --- | ---: | ---: |
| `claude-opus-5-5/high` | 13 | 5 | 10–19 | -0.064 | -0.013 |
| `claude-sonnet-5-5/high` | 16 | 4 | 6–16 | 0.156 | 0.114 |

Observed model comes from `assistant.message.model`; observed effort comes from the assistant root's
`effort` field through the maintained telemetry adapter. Neither is inferred from prompts, model names
or defaults. Every one of the 74 invocations has a single non-null observed model and effort across its
responses. These are provider transcript observations, not proof of an immutable backend revision or
comparable compute budgets. The historical database field `requested_model` actually holds launch-resolved
model; original requested effort is absent for all 74. Requested assignment and provider-observed analyses
therefore remain separate estimands. The three independent retained Codex launch packets establish only
a requested `gpt-6-astra/high` assignment and do not supplement this Claude response matrix with effective
model evidence.

Across all roles, Opus/high has 26 invocations, Opus/medium two, and Sonnet/high 46. Roles are 42 workers,
23 critics and nine analysts. A naive same-issue graph connects three profiles through four multi-profile
issues, but roles/stages differ. Worker-only graph has two disconnected components and no cross-profile
same-issue worker pair. Even a same-issue pair would require equal frozen inputs, starting commit, environment,
context and quality scoring before becoming a matched anchor. Medium effort occurs only in analysts.

Exploratory predictive association does not require identical-task anchors. The two worker groups share
H values 11, 13, 14 and 16. On these 16 cases in five parent clusters, a Huber fit of log(1+envelope seconds)
on centered H, profile and their interaction gives an interaction coefficient **0.0187 per H point**.
The common-support H slopes are 0.1819 and 0.2006. This is a small, task-mix-confounded descriptive fit;
its uncertainty is not estimable reliably under the declared cluster guard. It establishes neither
profile equivalence nor a difference. No worker effort comparison is supported. Free item/profile latent
invariance remains unidentified, and no scalar, DIF or causal routing model is fitted around that gap.

## Stops, tails and sanitation

Broad native calendar diagnostics use every accessible issue and PR, with open age retained separately:

| Population | n | Median | p90 | p99 | Maximum | Log-duration MAD flags |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Closed issues, all reasons | 3,043 | 2.50 h | 1.80 d | 16.19 d | 78.20 d | 156 |
| Open issues, age at cutoff | 354 | 3.00 d | 72.09 d | 127.43 d | 127.76 d | 16 |
| Closed PRs, merged and unmerged | 8,762 | 13.58 min | 1.03 d | 18.12 d | 74.92 d | 1,319 |
| Open PRs, age at cutoff | 16 | 22.88 h | 1.84 d | 1.87 d | 1.87 d | 3 |

Flags use absolute deviation from median log(1+seconds) greater than three **unscaled** MADs, without a
normality assumption. Both short and long extremes count. Flags never delete cases or establish that
work stopped. The mixture of automated PRs, task types, repositories and eras requires stratified follow-up;
these broad tails are a diagnosis of why pooled elapsed-time calibration would be misleading. The 31-case creation-H/calendar result also mixes historical work types and must not be interpreted as a homogeneous current Delivery population. Older Outcomes can span child work and administrative closure lag; current body type labels are retrospective signals, not historical unit equivalence. A separate four-tail native read confirms that [SDD #928](https://github.com/FS-GG/FS.GG.SDD/issues/928) is an Outcome accepted after eight children, while [SDD #935](https://github.com/FS-GG/FS.GG.SDD/issues/935) explicitly remains an existing Delivery with a complete source-to-installed promise. Thus an initial “old tails are Outcomes” explanation is rejected: long Deliveries remain valid observations, and an exact-header regex misses prose classifications. No tail is excluded on that basis.

The largest runtime envelope is 16.08 hours on an open item; its 1 h gap-cap scenario is 7.54 hours.
The next is 12.02 hours on a closed item, reduced to 4.48 hours under that scenario. Both remain in every
applicable analysis. Two all-observed envelope cases are MAD-flagged, but the longest cases are not
necessarily the flagged ones because the distribution is broad. Descriptive median-centered log-envelope residuals flag four of 26 cases across three eligible repository strata, three of 33 in the one eligible creation-month stratum, and two of 29 across two worker-profile strata (minimum five rows per stratum). These are separate diagnostic views, not adjusted causal effects. Gap caps assign at most the first cap-length
portion of each inter-response gap; placement is a convention and concurrency can alter unions. Silence
can mean thinking, a long tool call, review, suspension or missing telemetry. The adjusted values are
sensitivity scenarios, not cleaned active time. No positive active-time lower bound follows from isolated
timestamps; genuine execution start/terminal intervals would be needed.

A documented example is [Rendering #1261](https://github.com/FS-GG/FS.GG.Rendering/issues/1261): an explicit
supplier-park declaration at 00:25:36 UTC and a Ready watermark at 03:19:56 delimit 10,460 seconds of
reported waiting-to-readiness. Subtracting this interval from its 16,571-second calendar duration gives
6,111 seconds **as a documentary scenario**, not measured work. Across the 31 closed history cases this
changes rho from 0.323 to 0.325; bootstrap interval remains broad [-0.046, 0.671]. A subsequent review blocker
is preserved and not automatically subtracted. Ready is not execution resume, and other agents could have
worked meanwhile. The issue also reopened: using first instead of final closure loses 443 seconds.

The seven inspected PRs contain 25 commits. Twelve author/committer timestamps differ; eight author dates
and four committer dates fall outside the PR window. A 909,821-second maximum observable gap dominates
calendar spans. Across these PRs, the authored-time 1 h/8 h/24 h gap-cap totals are 25,979/72,815/130,415
seconds, versus 953,836 raw calendar seconds. These are a small selected PR sensitivity audit, not estimates
of active work. Squash timing, bots, concurrent branches/repositories, unpushed commits and clock errors
all weaken the inference from commit silence. [Flint, Chauhan and Dyer's 2022 empirical study](https://arxiv.org/abs/2209.04511)
documents dirty Git timestamps. A model can itself cause more repair, review or commits: postdispatch
activity is an outcome or mediator, not automatically a baseline confounder to regress away.

Failed/stopped controls include Governance PR374's four changes-required declarations and closed-unmerged
escalation, stale Renovate abandonment, explicit supplier waits, reopening and deliberately closed canaries.
Native review objects alone miss comment-based review rounds: the failed-review control has zero native
review objects. No failed case is dropped to improve correlation. Exact public controls and the structured
sanitation design follow in the source-audit appendix.

## Expanded exploratory relationships

The continuation executes all 12 baseline features against five duration endpoints, plus feature
co-occurrence, worker-profile strata, rank residuals, broad census strata and grouped prediction.
All 60 baseline-feature/endpoint comparisons are retained in the aggregate JSON, including undefined
constant-feature correlations. These are exploratory analyses of an already inspected cohort, with
multiple comparisons and no untouched final test. No p-values, selected significant features or tuned
routing threshold are reported. Subsequent feature selection must use a separate training/tuning process
and genuinely held-out evaluation.

### Individual dimensions and clock sensitivity

| Baseline feature | All observed envelope rho, n=34 | Through native closure rho, n=18 | 1 h gap-cap rho, n=34 |
| --- | ---: | ---: | ---: |
| H total | 0.097 | 0.090 | 0.184 |
| D1 scope | 0.058 | 0.117 | 0.102 |
| D2 coupling | -0.021 | 0.135 | 0.085 |
| D3 invariants | -0.166 | -0.041 | -0.152 |
| D4 safety | 0.217 | -0.075 | 0.264 |
| D5 numeric work | 0.011 | 0.296 | 0.077 |
| D6 discovery | 0.190 | -0.145 | 0.186 |
| D7 review | -0.134 | -0.023 | -0.125 |
| D8 live evidence | Undefined | Undefined | Undefined |
| Baseline body word count | -0.419 | -0.112 | -0.374 |
| Baseline body character count | -0.401 | -0.108 | -0.358 |
| Safety floor | 0.050 | -0.187 | 0.088 |

D8 equals **3 for all 34 items**. D7 equals 2 for 30 items, 1 for one, and 3 for three. There is no
observed D8 contrast and little D7 contrast. Undefined correlation is not evidence of no effect. D1–D6
cover 0–3, but their combinations are not balanced. D4 and safety floor correlate 0.935; D3 and D7 correlate
0.514; H correlates 0.671 with D2 and 0.595 with D4. Word and character counts correlate 0.996, so treating
them as independent explanatory evidence would double-count nearly the same feature.

The stronger inverse text-length association is present within both observed worker models: rho -0.692
for Opus/high and -0.447 for Sonnet/high. It weakens to -0.112 in the closure-truncated cohort. Better
specified work, task allocation, repository conventions, censoring or telemetry coverage could explain
it; the data do not establish that adding text makes work faster. After rank-residualizing H and duration
against baseline word count and repository indicators, partial rank correlation is 0.069 (34 rows,
design rank 9, residual df 24, but only seven parent clusters). Corresponding values for D1–D7 are 0.007,
0.008, -0.134, 0.138, 0.186, 0.019 and -0.225; D8 remains undefined. This descriptive adjustment is not a
causal model or stable multivariable effect estimate. Postdispatch review and activity are excluded from
these controls.

The broader 31 closed creation-body histories give a different pattern: D7/calendar rho 0.527 and
D8/calendar rho 0.524, compared with D8's complete lack of variation in the runtime cohort. Their H/calendar
rho is 0.323 and word-count/calendar rho 0.168. This contrast exposes cohort selection, range and task-type
sensitivity; it does not reconcile the two clocks or validate the dimensions. The full creation-body
matrix is retained too.

Observed H-level medians are not monotone: H=8, 13 and 16 each have five cases, with envelope medians
4.76, 1.53 and 7.39 hours. Other levels have one to four cases. These sparse summaries challenge an assumed
smooth relationship but cannot distinguish genuine nonlinearity from sample/task mix. The actual Low/
Medium/High boundaries and overrides remain frozen; neither these summaries nor the tiny Low group
justify selecting replacement cutoffs.

### Grouped prediction and influence

Seven folds leave one observed parent component out at a time. Ridge models use fixed alpha=10;
standardization is fitted only on each training fold. The response is log(1+observed-envelope seconds).
Both a training **mean** baseline (aligned with squared-error loss) and a training **median** baseline
(aligned with absolute-error loss) are included. All candidate models are reported, with no tuning or
selected winner. Per-fold counts and losses are retained without private identities.

| Predictor | Pooled log MAE | Pooled log RMSE | Equal-parent mean log MAE |
| --- | ---: | ---: | ---: |
| Training mean only | 1.277 | 1.635 | 1.570 |
| Training median only | 1.204 | 1.622 | 1.514 |
| H total | 1.304 | 1.659 | 1.614 |
| Baseline log word count | 1.136 | 1.546 | 1.527 |
| D1–D8 | 1.498 | 1.820 | 1.808 |
| D4 and D8 risk/evidence controls | 1.274 | 1.631 | 1.558 |
| D1, D2, D3, D5, D6 demand candidates | 1.477 | 1.790 | 1.811 |

H and the full dimension vector do not improve these fixed exploratory models over the baselines.
Word count improves pooled error, but its equal-parent MAE is worse than the median baseline. Removing
the largest parent component—16 of the 34 items—and repeating grouped evaluation on the remaining
18 items/six groups reverses the pooled advantage: word-count MAE is **1.101 versus median baseline 1.065**.
H gives 1.169 and D1–D8 gives 1.528. This sensitivity is retained alongside the full-cohort result; the large
component is not removed from primary results. The apparent word-count advantage is therefore not a
robust generalization claim.

A separate outcome-blind temporal split at the median observed dispatch time purges every parent component
that crosses the boundary. It leaves two training groups/nine rows, two test groups/seven rows and three
purged groups. This does not meet the declared minimum of three training groups/ten rows and two test
groups/five rows; no temporal prediction score is reported. These minimum guards do not imply that larger
samples automatically provide reliable validation. A definitive grouped-and-temporal test remains open.

### Selection, broad strata and process signals

The runtime cohort covers 34/3,397 issues: 33 were created in November and one in October, versus a census
dominated by 1,938 July issues. November coverage is 33/211 (15.6%); June–September coverage is zero.
Open issues are overrepresented: 16/354 (4.52%) versus 18/3,043 closed issues (0.59%). The runtime sample
is selected by retained attribution, not a random sample of the issue population. Extrapolating its
correlations to all historical work would be unsupported.

Closed-issue calendar medians by creation month are June 1.42 h, July 2.18 h, August 6.03 h, September
0.69 h, October 9.22 h and November 3.83 h; open ages are reported separately. These are different
follow-up windows and mixtures of work, not model performance trends. Repository medians also differ:
Coordination 0.72 h, Game 1.24 h, Rendering 2.38 h, SDD 2.56 h and the hub 4.13 h. The aggregate preserves
all eligible public repository strata and pools private repositories. Current exact Delivery headers
appear on 273 issues, only 119 closed; their closed median is 4.48 h, versus 2.47 h among closed issues
without that exact header. These retrospective text labels are incomplete and must not be promoted to
historical class truth. In particular, long prose-classified Deliveries remain in the population.

Bot and user PRs have almost identical closed medians, about 13.6 minutes, but very different p90 values:
7.31 days for bots versus 0.83 days for users. This illustrates why medians alone miss stopped/abandoned
or dependency-wait tails. Native completed issue closures have median 2.57 h, not-planned 0.89 h and
duplicate 1.58 h, with wide tails preserved. No early unsuccessful closure is counted as efficient success.

Postdispatch diagnostic correlations with all-observed envelope time are comment count **0.633**, unique
cross-referenced PR count **0.664**, invocation count **0.412**, critic invocation count **0.301**, and
response count **0.793**. These describe workflow activity; some are mechanically related to how the
clock was constructed. Cross-references are not strict implementation/closing PR joins. They may indicate
review, repair or broader coordination but cannot establish which, and are not available as frozen
predispatch predictors. The 34 issue histories have no detected structured review declarations or reopen
events; this is missing support for issue-level variation, not proof that reviews/repairs did not occur
on their PRs. The separate known failed-review PR control remains preserved. Better strict PR/outcome
joins are the next historical source improvement, not license to reinterpret references as accepted work.


## Reproducible source and remaining work

The maintained scripts are `scripts/learn-history-evaluate.py` (inventory/history/provenance/support),
`learn-history-fetch.py` (bounded explicit-node read-only fetch), `learn-history-telemetry.py` and SQL
(attribution/profile support), `learn-history-response-timestamps.py` and SQL (timestamp normalization),
and `learn-history-statistics.py` (NumPy/SciPy duration, rank, robust fit and cluster resampling).
`learn-history-explore.py` adds complete feature/endpoint matrices, co-occurrence, stratified diagnostics,
rank residuals, fixed grouped prediction, influence and chronological support checks.
`requirements-learn-history.txt` pins actual NumPy 2.5.4 and SciPy 1.18.1; the run used CPython 3.15.1.
The evaluator is offline. SQL requires the maintained telemetry schema and is executed with a read-only
SQLite connection. Metadata outputs use private permissions and new output paths. Source rows, paths,
transcript identities and private repository names are excluded from this report.

Reproduction with existing private custody paths supplied by the operator:

```bash
uv venv --python python3.15 "$ANALYSIS_VENV"
uv pip install --python "$ANALYSIS_VENV/bin/python" -r scripts/requirements-learn-history.txt
OPENBLAS_NUM_THREADS=1 OMP_NUM_THREADS=1 "$ANALYSIS_VENV/bin/python" -m unittest discover -s tests/learn-history -q
OPENBLAS_NUM_THREADS=1 OMP_NUM_THREADS=1 "$ANALYSIS_VENV/bin/python" scripts/learn-history-statistics.py \
  --inventory-dir "$PRIVATE_INVENTORY" --histories "$PRIVATE_INITIAL_AUDIT" \
  --histories "$PRIVATE_DURATION_HISTORIES" --first-dispatches "$PRIVATE_DISPATCHES" \
  --response-times "$PRIVATE_RESPONSE_TIMES" --scorer "$FROZEN_SCORER" \
  --seed 5276 --bootstrap 500 --output "$NEW_PRIVATE_OUTPUT"
OPENBLAS_NUM_THREADS=1 OMP_NUM_THREADS=1 "$ANALYSIS_VENV/bin/python" scripts/learn-history-explore.py \
  --statistics-dir "$NEW_PRIVATE_OUTPUT" --inventory-dir "$PRIVATE_INVENTORY" \
  --output "$NEW_PRIVATE_EXPLORATION_OUTPUT"
```

Validation: 63 synthetic/adversarial controls pass, covering chronology, source completeness, body boundaries, duplicate/ambiguous identity, profile support, interval unions, gap caps, cluster resampling and lack of overlap. Python compilation passes. The full-dataset schema candidate is JSON-parsed; it has not been validated against a complete populated dataset or a JSON Schema metaschema. The committed aggregate evidence contains no private identities; individual inputs remain private. Each output includes a
hash manifest digest; immutable private custody supplies source-file hashes. The original collector's
three failures remain, including one unknown cause and two diagnosed Go virtual-address allocation
failures. A first statistical diagnostic failed to normalize API parent URLs to HTML URLs, incorrectly
splitting clusters; it is retained privately and superseded by the corrected run used here. This materially
changed confidence handling. No original evidence or failed run was rewritten. Final statistical source-manifest digest: `0b2c88828b89e7554fe61fd77669e5c8ee75bf40d913659ba6646f67c40b6b05`. Exploration source-manifest digest: `c3e4674dfd34716a5494bb4ab7ccbd637e3b445807f59f2f5f14f494f5087c9e`. The aggregate JSON files alongside this report preserve exact unrounded results.

Next work should expand outcome-blinded source qualification, independent task-demand labels, declared
quality scoring and evidence-backed execution/pause intervals. Use stratified historical samples and
frozen chronological/grouped heldouts, not a random split of related issues. The minimum controlled anchor
proposal and falsifiable Rasch/2PL/DIF alternatives below are **proposals only**; no paid replay matrix,
prospective campaign, model routing or safety-floor change has been launched. A versioned full-dataset
schema candidate accompanies the report; current script output schemas are explicitly smaller audit
projections, not a claim that all candidate fields have been populated or validated.


# Design, source audit and remaining validation

Delivery [#5276](https://github.com/FS-GG/.github/issues/5276), under [#5248](https://github.com/FS-GG/.github/issues/5248).
Status: initial executable historical analysis and design candidate, 2026-11-03; broad held-out validation remains. This extends the [owning design](../docs/roadmaps/learn-02-delivery-classification-and-routing.md).
It does not alter the rubric, routing floors, native cards or accepted #5249 history.

## Primary question: does hardness remain valid across model and effort profiles?

The user's priority is **construct validity and cross-profile measurement invariance**. Worked-time-to-completion is a central empirical signal, analyzed independently of the matched-anchor quality endpoint. Native calendar time and observed response envelopes are imperfect measurements of that signal, with explicit sensitivity and coverage below. A classifier that predicts long calendar
lead time but measures supplier waits, policy risk or repository conventions has not established task
hardness. Thousands of distinct tasks do not identify how the same task behaves across different profiles.

Operational target `fsgg.hardness-construct/1`: a task's difficulty is the demand it places on an agent's
ability to satisfy a **fixed quality contract**, from a **fixed starting state and information set**, under
a declared execution protocol. A profile is the exact model version, requested effort/budget, observed
effective configuration where available, runtime, tools and context protocol. Budget is part of the profile;
provider labels such as “high” are not a common numeric scale. Final acceptance after unlimited escalation
measures a workflow's eventual success, not one worker profile's ability.

Keep three constructs separate: cognitive/engineering task demands; consequence and safety constraints;
and execution conditions/cost. V1's D4 safety and D8 live evidence requirements remain routing constraints,
not proof of cognitive difficulty. D1 scope and D2/D3/D5/D6 technical demands are candidate explanatory
features, not validated latent dimensions. D7 review risk mixes demand and process and needs separate
validation. Preserve the complete original D1–D8 vector and policy result, but evaluate a demand-feature
model, a risk/process-only model and their combination. A high safety floor can remain justified even
when the task is easy. No fitted latent factor is named “novelty” or “invariant difficulty” merely because
its predictor correlates with that rubric field.

### Directly relevant evidence and how it changes this design

[RADAR v3](https://arxiv.org/html/2509.25426v3), dated 10 March 2026 and marked ICLR2026, is the closest
model-budget formulation. It fits query difficulty/discrimination and a separate scalar ability for each
model-budget configuration using a common-query binary response matrix. This design adopts that separation
and comparable-response requirement. Its single-turn benchmark results do not validate a long-running
repository-workflow rubric. Cost scalarization is deferred until quality predictions are validated.

[IRT-Router](https://aclanthology.org/2025.acl-long.761.pdf), ACL2025, uses multidimensional abilities and
query loadings, evaluating queries across 20 LLMs. It motivates the multidimensional alternative below;
model-description embeddings do not replace observed profile ability. [tinyBenchmarks](https://arxiv.org/html/2402.14992v2),
26 May 2024 version and ICML2024 paper, learns item representations from existing common-item responses,
then estimates a new model using selected item responses. It motivates anchor calibration, frozen item
parameters and separate unseen-model tests. Its 100-item result is not a sample-size guarantee for FS-GG.

The [family-DIF study v2](https://arxiv.org/html/2609.00482v2), 7October2026, is a preprint accepted to
the NeurIPS2026 TAE workshop, not a main-conference result. It uses held-out owners and composition-matched
controls: high overall rank correlation can coexist with near-tie reversals. Here it motivates reporting
pairwise reversals, blueprint-preserving task weights and profile-family holdouts alongside aggregate fit.
Its low-DIF diagnostic subset is not automatically a scale-linking anchor set.

The [EDM2026 short paper](https://educationaldatamining.org/edm2026/proceedings/2026.EDM.short-papers.88/index.html)
compares uniform and ability-dependent DIF and uses placebo groups plus expert examination. This design
requires corresponding null controls and content adjudication, rather than treating every flagged
interaction as a discovered task property. [Multilingual-IRT v1](https://arxiv.org/html/2606.15643v1),
14June2026 preprint, separates shared item parameters from group-specific deviations under identifying
constraints. It motivates centered profile deviations and parameter-recovery checks; language-specific
empirical gains do not establish that FS-GG effort groups have the same structure.

### The historical support gate comes before model fitting

Construct a bipartite graph of **qualified frozen-task anchors** and **observed profile versions**.
An edge is an independently assessed attempt on that exact anchor, with response/quality evidence.
A worker, critic, repair worker and integrator on one Delivery are different roles/stages, not repeated
answers to the same task. A later attempt using a previous patch or review feedback is an informed
continuation, not an independent replicate. Same issue ID, similar title or shared parent cannot create
an anchor edge. Unknown start commit, context, tool environment or quality rule blocks a matched-anchor
claim; retain such attempts in the historical workflow cohort.

Report candidate and qualified edge counts, missingness, profiles, anchors per profile pair, connected
components, component sizes, degree distributions, rank of the constrained design matrix and influential
bridge anchors. Test loss of identifiability when single bridge tasks are removed. Connectedness is
necessary for comparing free profile effects, but is not sufficient for stable estimates, reliable DIF
or causal interpretation. A prior can produce numbers in disconnected components without identifying
their relative scale. Single-response task leaves, all-pass/all-fail profiles and disjoint task allocation
require explicit uncertainty/separation diagnostics. Item-feature models can extrapolate across them;
that extrapolation must not be reported as demonstrated measurement invariance.

**Actual evidence in inspected retained packets:** three distinct issue-body snapshots, three launch
observations, one requested profile (`gpt-6-astra/high`), and zero common-task cross-profile pairs.
Those packets do not supply a qualified matched-response matrix with identical starting commits,
environments and independently scored bounded attempts. The retained `checked-join`, `telemetry-all` and `telemetry-dispatches` files each contain 34item-level
rows. Direct schema inspection finds counts/first-dispatch/token fields and no explicit model, effort or
runtime fields; the retained `telemetry-anchors` array is empty. These exports establish usage attribution,
not matched-profile responses. Underlying invocation/profile evidence has now been recovered in the executable analysis above: 74 attributed invocations and three observed profiles, with worker-only profile components disconnected. The old aggregate export did not itself contain this evidence. The native census supplies 12,175 work records,
not 12,175 psychometric responses. Therefore cross-model/effort validity is currently **inconclusive** in
these inspected sources. This is a source-specific result, not a claim no usable anchor exists anywhere.

### Models to compare, with falsifiable assumptions

Let `Y[j,p,r]` be a prespecified binary quality pass for frozen task `j`, profile `p` and independent
replicate `r`. Preserve criterion-level scores and failure category alongside the binary endpoint.
External infrastructure failures remain explicit; report both an all-attempt operational endpoint and a
predefined assessment of task response where scoring is possible. Never silently drop failed launches,
review failures, cancellations or missing judgments to improve accuracy.

Start with profile-only base rates and repository/task-stratum/text-length baselines. The explanatory
Rasch baseline is `logit(P(Y=1)) = theta[p] - b[j]`, with `b[j] = beta0 + x[j]' beta + u[j]` and
centered profile ability. Fix the logistic scale, center random deviations and record the reference
population. For unseen tasks, evaluate predictions using frozen pre-attempt features, integrating or
omitting the unobserved task residual as declared; never estimate their difficulty from their test outcomes
and then call their prediction held out. Profile ability may be partially pooled by model and effort,
but model-by-effort interactions remain possible and higher effort is not constrained to be better.

Next compare an invariant2PL alternative `P(Y=1)=sigmoid(a[j]*(theta[p]-b[j]))`, with positive discrimination
and fixed location/scale conventions. Then fit, only when support permits, a parsimonious multidimensional
model `logit(P)=a[j]'theta[p]-b[j]` and a regularized DIF model with centered task-by-family/profile/effort
intercepts and, where identifiable, slope deviations. Use few prespecified dimensions and training-only
shrinkage selection. A saturated task-by-profile model with one observation per cell is not a valid
invariance test. Unknown model-family membership remains Unknown rather than being inferred from names.

Three claims must be tested separately:

1. **Rubric validity:** does the prespecified demand vector predict criterion-quality outcomes better than
   profile/type/repository/text-length baselines, and do outcome-blinded experts agree on what it measures?
2. **Parameter invariance:** are common task response functions adequate across supported profiles after
   accounting for ability, or do reproducible DIF terms materially improve held-out quality prediction?
3. **Ordering invariance:** does ordering tasks by predicted success/demand remain practically stable over
   supported profiles and task mixtures? Unequal discrimination can produce task-order crossings even in
   invariant2PL; crossing alone is not proof of DIF. A scalar difficulty threshold is not a guarantee of
   one universal easiest-to-hardest order.

Local independence conditional on the latent model is a working assumption, not a fact: repeated seeds,
shared context, scaffold, acceptance checks and model ancestry can induce dependence. Use original-task
clusters and profile families in uncertainty analysis. Diagnose residual dependence, calibration failures,
latent dimensionality and prior/seed sensitivity. Fix latent rotations with an explicit loading convention
or report only identified predictions; do not interpret arbitrary MIRT coordinates as named skills.

### Primary validation and equivalence decisions

Prespecify equal quality criteria, severity weighting, allowed assistance and attempt budget before fitting.
For the matched-profile invariance question, the primary endpoint is bounded-attempt quality pass, with acceptance criteria and blocking findings scored blind to profile and rubric score where practical. Worked-time-to-completion remains a central independent empirical endpoint; calendar age and tokens cannot substitute for it. Report Brier score and log loss, calibration intercept/slope and uncertainty per supported
profile, task stratum and effort; provide ordinal task-rank uncertainty, Kendall concordance and pairwise
rank reversals. Report rubric ablations and interactions instead of only a pooled correlation.

Use four distinct tests: unseen task clusters; held-out time/eras; held-out model or family; and held-out
effort configurations. Freeze feature extraction, dimensions, anchor choice and calibration on training/
tuning only. For a new profile, distinguish true zero-shot prediction from **anchor-calibrated transfer**:
its calibration anchor responses may estimate ability, but disjoint evaluation-task responses may not.
A categorical profile effect alone cannot predict an unseen profile. An unseen effort label is not an
interpolated budget without a justified within-model budget mapping. Report unsupported holdouts, not
fabricated leave-profile-out scores. Related original work stays in one task partition.

Invariance is an equivalence question. Report confidence/credible intervals for conditional probability
contrasts and interaction effects; `p>0.05` is not evidence of equivalence. Proposed planning margins are
5 percentage points of quality-pass probability for ordinary classes and a separately reviewed stricter
margin for high-consequence classes; these are decision proposals, not established tolerances. Compare
on common ability/task support, with simultaneous or multiplicity-adjusted intervals for the prespecified
contrast family. If an interval crosses either equivalence boundary, return inconclusive; if it lies
materially outside, report non-invariance for that scope. Bootstrap whole task clusters; with too few
profiles/families, restrict inference to the measured fixed profiles rather than claiming family-wide
coverage. Sparse counts cannot be repaired by thousands of rows from a different profile.

Also compare DIF-driven task subsets against same-size task-type/demand-matched random subsets, with
weights preserving the declared task blueprint. Use profile-disjoint selection where support permits.
Negative controls include matched pseudo-groups within one actual profile (only where exchangeable),
simulated no-DIF matrices with the actual missingness pattern, and an injected-interaction positive control.
Check parameter and uncertainty recovery across seeds. Report discordant items for blinded expert review.
Do not remove genuine high-DIF tasks to make the global score look stable; preserve them and report
conditional or vector hardness when a shared scalar is inadequate.

### Method decisions and rejected shortcuts

| Method | Evidence/assumptions needed | Decision for #5276 |
| --- | --- | --- |
| Explanatory Rasch / hierarchical logistic | Comparable quality labels, connected anchor support, identifying constraints and enough variation | First fitted validity baseline after the support gate; presently no qualified fit |
| RADAR-style profile2PL | Common task responses, constrained scalar scale, tested monotonic response assumption | Primary scalar alternative; adopt model-budget identity, defer routing/cost optimization |
| IRT-Router-style MIRT | Enough task/profile overlap for additional dimensions; stable constrained fits | Compare low-dimensional version if scalar fit fails; no semantic ability claims from arbitrary rotations |
| Centered DIF / effort interactions | Matched ability support, replicated information, negative controls and held-out improvement | Required challenge to a global scale, not a decorative secondary analysis |
| tinyBenchmarks-style compression | Prior response matrix and stable item parameters on separate training profiles | Possible later anchor economy; cannot infer anchors from a list of unique issue completion times |
| Population model-rank correlation | Common task mixture and quantified pairwise uncertainty | Diagnostic only; high correlation cannot certify local ranking or profile invariance |
| SurvivalBoost / competing-risk calibration | Qualified event reasons and censoring model, validated probability calibration | Secondary calendar/workflow modeling only; cannot establish hardness construct validity |
| Censored conformal intervals | Appropriate exchangeability/weighted-shift and censoring assumptions | Deferred; source access was blocked and assumptions are unestablished; no automatic coverage claim |
| Anytime-valid or cross-domain OPE | Identified actions/rewards/support and required logging/transport assumptions | Deferred; unknown historical propensities are not repaired by estimator choice |
| Worked-time/duration association | Explicit clock provenance, censoring and pause sensitivity, baseline features | Central descriptive evidence now; alone cannot establish cross-profile construct invariance |
| Score-to-recommended-route agreement | Same deterministic policy produces both | Literal conformance only; not empirical validity |

Secondary source status: [SurvivalBoost](https://proceedings.mlr.press/v258/alberge25a.html) is AISTATS2025;
[competing-risk calibration](https://arxiv.org/abs/2602.00194) v1 is dated 30 January 2026 and lists AISTATS2026.
[Anytime-valid OPE v3](https://arxiv.org/abs/2210.10768v3) is dated 15 August 2024;
[cross-domain OPE](https://proceedings.iclr.cc/paper_files/paper/2025/hash/87eb265e8898a7e245d61f01cea4d906-Abstract-Conference.html)
is ICLR2025. Only their stated scope was inspected here; detailed assumptions must be reviewed before
implementation. The censored-conformal primary page was inaccessible, so it supplies no adopted method.

### Minimum useful anchor proposal if historical overlap remains insufficient

First recover any existing **genuinely comparable** attempts, preserving every missingness/selection reason.
If none supports the necessary contrasts, report that historical validity is inconclusive and prepare a
separately reviewed replay/calibration proposal. No paid profile matrix or prospective trial is authorized
by this design. A minimal diagnostic starting design is24diverse frozen tasks, each evaluated under the
same four profiles (two model versions × two effort settings), with three fresh independent attempts per
cell:288attempts. This can expose gross interactions; it is not powered by assertion to establish the
proposed 5-point equivalence margins. Two model versions support only those fixed-profile comparisons,
not population-level family invariance.

Choose anchors from the historical task blueprint across demand and safety strata, including hard/failing
work, not only successful portable examples. Freeze input/acceptance hashes, source and dependency commits,
tool/image/environment digest, context, assistance policy, stop budget and evaluator before any attempt.
Randomize execution order; use isolated state without prior answers or critic feedback. Treat replay
contamination and historical public-code exposure as explicit limitations. Keep repair-after-feedback and
first-attempt endpoints separate. Use training-only simulations across plausible effect sizes, pass rates,
missingness and clustering to decide the additional anchors/profiles/repeats needed for an agreed margin.
Reserve disjoint task anchors for final evaluation and for new-profile calibration. Larger model/family
coverage is required before leave-family-out conclusions.

For this priority, the next implementation acceptance is: versioned construct and quality contract;
provenance-qualified anchor/profile/response tables; executed connectivity/identifiability report;
outcome-blinded rubric fidelity audit; reproducible scalar-versus-DIF/model comparisons and negative
controls; per-profile calibration/equivalence/ordering results on valid heldouts **or an explicit
inconclusive result with a sized evidence-gap proposal**. Timeline sanitation and original failure
controls remain required support. A “valid across profiles” label or learned routing change is withheld
until those assertions are independently reviewed. This priority adds to #5276 acceptance; it does not
weaken the original full historical-evaluation requirements or parent #5248.

## Available history and actual collection

A read-only enumeration found **3,397 issues and 8,778 PRs**, 12,175 unique database IDs and node IDs,
across all 27 repositories exposed by the authenticated FS-GG organization connection. Its repository
connection terminated; each repository's `state=all`, creation-ordered REST connection terminated below
100 rows. All 27 collected totals equal independent GraphQL issue-plus-PR counts. This establishes the
accessible native population, not deleted/transferred/inaccessible records or comparable task experiments.
No GitHub search result cap was involved.

Collection started at 2026-11-03T07:11:12.761953Z and ended at 07:14:15.150636Z. Creation time defines the
population cutoff; mutable state/body fields were observed at each row's recorded retrieval time. These
are not an atomic historical state snapshot. Reconstruct outcome state at a common cutoff from events
before evaluation, and flag mutations during the scan. The inventory consumed 144 invocations, including
three failed local reads, and retained about 43.9 MB of whitelisted native issue/PR metadata and current
text in private custody. An additional 27 single-object reads supplied the initial audit below; 34 further explicit history reads recovered the attributed duration cohort. With nine initial scope/probe calls, the bounded stage used 214 native API invocations in total. There was
no native write, publication or policy activation.

| Public repository | Issues | PRs |
| --- | ---: | ---: |
| .github | 1,648 | 3,628 |
| FS.GG.Rendering | 526 | 982 |
| FS.GG.SDD | 386 | 782 |
| FS.GG.Coordination | 188 | 935 |
| FS.GG.Game | 228 | 591 |
| FS.GG.Templates | 150 | 705 |
| FS.GG.Governance | 105 | 381 |

The remaining accessible repositories are included in the totals. Private repository identities, rows
and text stay private; the aggregate inventory includes 38 private issues and 218 private PRs.

The issue population retains 2,918 native completed closures, 112 not-planned closures, 13 duplicate
closures and 354 open issues, seven currently marked reopened. PRs include 7,545 merged, 1,217 closed
unmerged and 16 open. Only 273 issue bodies contain the exact current `Work level: Delivery` string;
seven contain the exact Outcome string. Those are text signals, not historical type or current-board
classification. There are 988 native parent links and only nine non-null native issue types. Historical
issues therefore need quality strata instead of blanket relabeling as current typed Deliveries.

The 3,043 closed issues have median creation-to-final-native-closure time 0.1043 days, p90 1.7976 days,
p99 16.1944 days and maximum 78.1987 days (nearest-rank-index diagnostic). These closed-only calendar
figures include non-successful closure and omit open cases from the quantiles; they estimate neither
active work nor overall completion performance. Creation months are June114, July1,938, August675,
September196, October263 and November211: chronological splits will have substantial distribution drift.

Retained telemetry adds a different, narrow population. The accepted corrected join still has 74 lineage
rows, 74 distinct assignments, zero ambiguous invocations and 34 attributed identities; the retained
expanded analysis uses 32 explicit Deliveries. Three local `*-first` dispatch packets exist. They record
requested/Root-observed launch configuration, not automatically provider-effective model identity or
complete lifetime usage. A separate local spool contains 130 JSON files, mostly 128 receipts; file count
is not execution coverage. No new remote runtime collection was undertaken in this stage.

The collector initially applied a 2 GiB virtual-address limit inherited by `gh`. Three reads failed;
retained stderr diagnoses Go allocation failure on the latter two. The first exit code is retained but
its stderr was not captured, so its cause remains Unknown. The diagnosed failures are local collector
failures, not HTTP failures or workload outcomes. The manifest retains failures and resumed page boundaries. The corrected collector uses one Go execution thread and
a small managed-heap target; actual retained data remains far below the 512 MiB storage ceiling. No
failed page was silently treated as an empty final page.

## What the native audit establishes

A deterministic identity-hash selection sampled creation month, open/not-planned state and current
Delivery/Outcome text strata, plus merged/unmerged PRs and three named controls. Overlap yields 27
records: 20 issues and seven PRs. This is a source-feasibility audit, not a representative performance
estimate. Selection memberships and algorithm are retained privately. Every requested edit/comment/
filtered-timeline/commit/review/closing-reference connection terminated with `hasNextPage=false`.
The audit read 65 comments and 30 edit rows. Filtered timeline `totalCount` includes unrequested event
kinds on sampled PRs: 239 total versus 187 returned selected events. Store both values; do not claim
all-event timeline completeness from this filtered query.

- [Rendering#1261](https://github.com/FS-GG/FS.GG.Rendering/issues/1261) has two close events and a reopen.
  Its first and final close differ by443 seconds. Comments explicitly record supplier blocking, readiness,
  review changes-required and a delivery-completion receipt. A Ready marker is planning evidence, not
  an execution start. The accepted receipt and native closure are distinct timestamps.
- [Governance PR374](https://github.com/FS-GG/FS.GG.Governance/pull/374) contains review declarations
  with indices0,1,2,3, all changes-required, followed by escalation and closure without merge.
  `reviews.totalCount` is zero. Preserve four review observations, seven visible commits and the failed
  attempt; neither zero GitHub review objects nor a closed PR means no review or successful delivery.
- [Rendering PR234](https://github.com/FS-GG/FS.GG.Rendering/pull/234) is an abandoned dependency update
  with a title rename and explicit stale closure. In contrast, [#5202](https://github.com/FS-GG/.github/issues/5202)
  records intentionally closed unmerged qualification canaries. Unmerged closure alone cannot distinguish
  failure, supersession and successful disposable qualification.
- Nineteen audit records have no edit rows and null `lastEditedAt`. All eight edited records expose
  creation-time origin rows, no deleted edits, and a newest `diff` byte-equal to the retrieved body.
  Their actual format is whole-body text, despite the native schema's generic “summary of changes”
  description. A versioned adapter can qualify this observed snapshot format; it must not blindly treat
  arbitrary diffs, deleted edits or truncated chains as original text. No reverse patch replay was needed
  for these records. Unrecognized formats remain Unknown until a verified replay adapter exists.
- The retained #5249 corpus has34 history files, six edited. All six newest diffs equal their retained
  current bodies and all six earliest edits coincide with creation. No contradiction to those retained
  body reconstructions was found. Creation before observed dispatch still does not prove creation before
  the first actual work; the existing uncertainty remains.
- Applying unchanged rubric v1 to earliest versus latest native text in the eight edited audit records
  changes three hardness totals/dimension sets and one recommended profile. This demonstrates why the
  current body cannot replace historical inputs; it is not predictive validation.

An executable offline control retained all three close/reopen transitions, rejected truncated timelines,
deduplicated duplicate transport events, counted review index0 correctly, preserved unmerged failure,
and kept active execution duration null. Author/role authenticity and full accepted review-round totals
still need the outcome-adjudication layer; a discovered comment marker alone is not trusted authority.

## Versioned dataset and unit contract

Proposed successor dataset identifier: `fsgg.learn-history/2`; the initial `/1` design is retained.
Version2 adds explicit construct, frozen-task, profile, response, anchor-support and validity-result relations
before historical workflow relations. The schema candidate is described below. The immutable release manifest binds extraction
code/rubric/adapter versions, source hashes, collection window, API query and page bounds, repository
visibility, failed reads, retry lineage and exclusions. Raw rows remain private and append-only; derived
sanitation records reference raw evidence rather than overwriting it. No new operational ledger is needed.

| Relation | Required identity and fields |
| --- | --- |
| `construct` | target population; fixed quality contract/budget; demand versus risk/process fields; requested/effective-profile target; declared equivalence margins and decision authority |
| `frozen_task` | original unit; input/start-commit/environment/context/quality/protocol hashes; role/stage; prior-attempt information; completeness and missing reasons |
| `profile` | exact requested and effective model/effort separately; runtime/budget units; model family evidence; tools/context protocol; source timestamps |
| `response` | unique bounded attempt/replicate; frozen task and profile; criterion scores/pass/failure; assistance/external failure; blinded evaluator; source and observability |
| `anchor_support` | qualified versus candidate graph; connected components/pairwise anchors; design rank/bridge sensitivity; missingness; identifiability decision |
| `validity_result` | exact construct/model/contrast; task/profile holdouts; calibration versus test anchors; effect/interval/margin; per-profile quality/calibration/rank metrics; inconclusive/non-invariant/equivalent decision |
| `source` | source ID; repository/node/database ID; API/query/page/cursor; observedAt; source event time; payload hash; complete/deleted/denied/truncated flags; visibility |
| `unit` | original issue node ID; historical unit kind and provenance; eligibility stratum; createdAt; original scope; supersession/duplicate links; censor cutoff; dataset inclusion reason |
| `boundary` | unit; boundary kind (`creation`, `observed_dispatch`, `verified_dispatch`); timestamp or bounds; supporting source IDs; first-work-known flag |
| `feature_snapshot` | unit/boundary; body/title revision hash; knowledge timestamp; reconstruction adapter; completeness; D1–D8 and missingness; rubric version; prospective/retrospective/current-scope provenance |
| `relation` | source/target; typed parent, supplier, duplicate, continuation or strict PR-delivery relation; validAt; evidence; certainty; ambiguous flag |
| `event` | native/provider event ID; unit/attempt/PR/invocation; event time and observedAt; kind; outcome; source IDs; original failure retained |
| `interval` | start/end or separate bounds; active execution, blocked supplier, queue, explicit stop, handoff, CI, review, unobserved; evidence and coverage; overlaps; duration nullable |
| `outcome` | first native closure, each reopen/reclose, accepted completion, not-planned/abandoned, review/repair/CI/regression endpoints; time/bounds; label method; maturity; coverage; censoring |
| `assignment` | invocation ID; item/role; requested model/effort, Root-recorded model/effort and provider-effective fields separately; evidence and ambiguity; propensity/support nullable |
| `split` | unit; relationship-component ID and edge policy; train/tune/test/quarantine; feature/label cutoffs; reason; test-access manifest |
| `adjudication` | immutable source references; original candidate and corrected label; reason; reviewer; schema version; uncertainty; affects which derived rows |

The primary work unit is the original scoped issue obligation. A Delivery can have multiple PRs,
worker/reviewer invocations, repairs and acceptance phases; these do not become independent observations.
Historical task/bug issues form their own stratum. Parent Outcomes, long-lived coordination containers,
automated update PRs and unresolved unit types are retained separately. Do not exclude a unit for failing,
waiting, being reopened or lacking usage. PR-only work can support a distinct PR-lifecycle analysis, not
be silently pooled with issue obligations. Splitting reused issue scope requires explicit original native
scope evidence; repairs retain their original unit. Record shared-PR allocation ambiguity rather than
multiplying a PR's reviews/tokens across each linked issue.

## Sanitation rules

1. Deduplicate source events by stable identity plus revision/hash; reject conflicting duplicate facts.
   Distinguish transport retries from real repeat attempts. A cross-reference is a candidate join only.
   Verify closing relations, explicit delivery markers or accountable native acceptance; record multiple
   valid PRs, superseded attempts and unresolved ambiguity. Current closing references can disappear or
   change, so retain historical closers and body/force-push events where available.
2. Reconstruct bodies at the chosen boundary. Qualify an unedited current body only with complete empty
   edit history and null last-edit time. For the observed whole-body edit format require full pagination,
   nondeleted/null-free content, verified newest-body equality and an origin at creation. Inspect adjacent
   revisions against native history. Patch-shaped or incomplete chains need a tested replay adapter or
   yield Unknown. Never substitute an after-boundary revision. Either exclude title features or reverse
   complete rename history. Current labels/types/parent links are also not automatically pre-boundary.
3. Separate creation-time prognosis from dispatch-time effort prediction. An issue created during work
   can validly predict later native closure from creation, but cannot establish pre-work hardness.
   Frozen dispatch packets offer stronger boundaries where actual invocation evidence joins. Quoted
   dispatch strings, repair descriptions and retrospective assessment bodies never prove launches.
4. Preserve each closure and reopen. Native state at a horizon is reconstructed through transitions;
   current state/reason is not back-projected. An event's closure reason or accepted-completion status
   remains Unknown without contemporaneous evidence. Reopened/mistaken closure never deletes prior work.
   Abandonment/not-planned is a competing outcome, not success or innocuous censoring.
5. Compute calendar elapsed time, execution wall time and summed agent time separately. Union verified
   execution intervals for wall time; retain overlapping workers and aggregate agent-time separately.
   A process/session envelope is not proof of continuously active compute. Native silence, nights,
   weekends, comment gaps, commit gaps and Ready/In-progress state do not establish a stop or work start.
   Supplier/stop declarations record what was observed; unknown endpoints stay bounded or null.
   Never call calendar time minus inferred idle time “active work.”
6. Retain failure/cancellation/timeout/escalation/session exhaustion, repair commits and review observations.
   Join CI by repository, run ID, attempt, head and PR; preserve earlier heads, force-pushed history and
   reruns. Visible current PR commits/checks alone cannot prove full CI history. Review rounds require
   trusted structured declarations or human adjudication; REQUEST_CHANGES count alone is insufficient.
   Missing provider usage stays null, including for failed or unattended attempts.
7. Track label observability. Open work is administratively censored at collection cutoff for completion
   time, while native state at a fully observed fixed horizon is a valid outcome. Recent acceptance has
   an immature30-day regression window. “No observed regression” requires a stated ascertainment method;
   absence of a linked bug cannot certify absence of regressions.

## Secondary workflow validation and sanitation sensitivity

Freeze these choices before inspecting held-out outcome labels. This is retrospective validation of a
newly specified rubric on historical work, not proof the rubric existed or could have been deployed then.
Named calibration examples and their related clusters are discovery data, excluded from confirmatory
claims. The feasibility audit above is also development data.

Build relationship components from original-task continuations, duplicates, shared PRs, parent-work
clusters and supplier/dependency clusters. Keep each component in one partition. Distinguish an incidental
citation from an actual dependency. Report giant components and component-size distribution; quarantine
boundary-spanning components for the primary temporal test rather than splitting related work. A second
sensitivity run can use a broader dependency closure. Do not silently break clusters to increase sample size.

Proposed calendar boundaries, fixed before outcome modeling: training creation dates June–July2026;
tuning August; primary test September–October3, whose30-day follow-up matures by the collection cutoff.
October4 onward is a separate later validation window with endpoint-specific maturity (e.g.7-day labels
only through October27); unobserved windows stay censored. Use only training labels available by the
training freeze, then tuning labels available by the model-freeze date. Purge unresolved cross-boundary
clusters and report their coverage. The date ranges can be revised once using feature-only unit/cluster
counts if unusable, with the revision retained before test labels are examined. June–July dominates the
inventory, so also report repository/era drift and leave-repository-out sensitivity, without blending
unqualified historical categories into current Delivery results.

Compare these small transparent predictors on the same eligible units:

- intercept/base-rate model and training-set duration median;
- repository plus unit stratum and creation era;
- body length and known pre-boundary scope counts;
- fixed rubric total/class, then a regularized model with eight dimensions plus missingness indicators.

Fit preprocessing, imputation, feature selection, calibration and penalties exclusively inside training
and time/group-respecting tuning. Keep feature missingness explicit; do not fit an eight-dimensional
cost model on the32 partial-cost observations and describe it as broad validation. Inspect whether the
rubric improves on repository/type/text-length baselines; route saturation alone is not accuracy.

Secondary broad workflow endpoint: probability of native closed state at7days, reconstructed with reopens. This
has complete observable labels for mature creation cohorts regardless of whether closure was successful.
Report Brier/log loss, calibration and threshold performance, not only correlation. A separate adjudicated
subset measures accepted completion at7/30days and material findings/failed acceptance. Use review-count
error on verified review labels, with explicit missingness. For time-to-event analysis retain open cases
and competing terminal outcomes; use cumulative-incidence/competing-risk or discrete-time models when
reason-at-event evidence qualifies them. Closed-only log-duration error is supplementary and labeled
conditional on observed closure. Partial token prediction remains a separately identified target until
whole-unit attribution coverage is proven.

Report paired changes against baselines with component-level bootstrap intervals (e.g.1,000 draws),
component and row counts, tail error and per-stratum coverage; sparse strata get descriptive results,
not misleading precise significance. A small number of independent components limits uncertainty even
when the row count is large. For sampled adjudicated outcomes, use documented inclusion probabilities
for population-weighted summaries; convenience controls carry no population weight. Do not use the same
holdout repeatedly to choose thresholds or policy versions.

Run a sanitation sensitivity grid: all native calendar time; only verified-execution intervals; any
defensible interval lower/upper bounds; unresolved waits retained as Unknown; all failures/reopens versus
clear accepted-only diagnostics; strict versus broad work clusters; strong versus qualified historical
feature provenance. Guessed inactivity gaps (for example1h/8h/24h) may only be an explicitly hypothetical
stress test and must never become the reported active-time outcome. Show how coverage and conclusions
change; no default trimming, successful-only filtering or winsorization hides recovery cost.

Validate rubric fidelity separately through outcome-blinded human adjudication of historical text and
safety classes, including negation, omitted obligations and implicit hazards. No gold hardness label is
manufactured from slow closure or high token use. Negative controls include post-completion text rejection,
wrong-item invocation, repeated lineage, edited title leakage, round0, missing stop, duplicate/ambiguous
PR joins and a high-degree parent cluster. Positive controls include the inspected reopened and abandoned
examples above and verified retained dispatch packets. Audit a stratified120-issue/40-PR sample spanning
repositories, eras, quality strata, waits, reopens, closed-unmerged and open records, with at least 40
independently double-adjudicated units; retain disagreements. These are proposed bounded sample sizes,
not a claim of statistical power or already-completed labels.

Historical routing comparisons can assess which classes concentrate failures, long observed execution
or review burden and where coverage is missing. Requested model choice is confounded by task difficulty,
operator judgment, runtime/era and reviewer support. With unknown behavior propensities, support and
provider-effective identity, neither IPW nor a doubly robust estimator establishes causal model savings.
No cheaper model is recommended merely because its selected historical tasks were faster. A report can
propose rubric changes with held-out evidence and uncertainty; safety floors and Root/human overrides
remain. Prospective exploration and activation remain outside this Delivery's historical evaluation.

## Next implementation and delivery

The historical-evaluation owner retains #5276. After Root coordinates the disjoint implementation scope,
extend the delivered provenance, native-history, anchor-support, telemetry and statistical scripts with qualified quality labels and a cluster/time split generator. The current scripts already execute historical support and duration analyses; full held-out validation remains. Reuse
`learn-dispatch.py` for fixed scoring and the corrected telemetry join; avoid duplicating #5273 card work.
Turn the actual controls above into fixtures without exporting private rows or texts. Review source and
sampling/label rules independently before freezing test access.

Before another bulk collection, use retained sources to seek comparable anchors and diagnose profile
support. The proportionate additional 34 explicit history reads were authorized and completed. Further broad collection requires a fresh bounded scope. A later separately bounded collection
may acquire full body/title/event/comment/relationship history for eligible issues, then strictly joined
PR histories and eligible CI attempts; prioritize fields that resolve the validity evidence gaps. Batched GraphQL
reads need full nested cursor follow-up; split each collection into at most250requests/15minutes and
retain a manifest across windows. Measure bytes per sample before extending beyond the existing512MiB
private cap; stream whitelisted facts and preserve raw-source pointers/hashes rather than exporting
logs. If the full history exceeds that cap, retain the census plus a predeclared stratified sample and
report its sampling weights and missing relationships. A truncated or inaccessible source remains a gap.

Remaining acceptance starts with the primary construct/profile-support/invariance assertions above, then
broad historical feature/outcome qualification, independent label audit, executable
extraction/sanitation/evaluation code, leakage-free held-out results, source review, aggregate report and
practical policy recommendation. None is satisfied merely by this inventory. Parent #5248 additionally
retains its distinct telemetry adoption and prospective-learning assertions. No prospective campaign or bandit is started here. The #5273 scorer/projection helpers are hub source executables, not packaged CLI commands or an assumed workspace receiver. Any adoption must name the accepted protected checkout and exact source hashes; that discovery/installation clarification remains with #5273.

Atlas retrieval verified `framework/learn-02-hardness-routing` and its three immutable source hashes.
Reusable capture proposed: native review objects are incomplete for FS-GG review-round outcomes; failed
PR histories and closure/reopen events must survive sanitation. Capture awaits an immutable reviewed
source citation for this new report; the #5276 owner carries that gap. The existing Atlas record is not
silently superseded because no contrary result to its scoped diagnostic was established.

## Method references

The following primary sources support the methodology, not empirical claims about FS-GG:

- [Grouped and temporal validation](https://scikit-learn.org/stable/modules/cross_validation.html):
  related observations must not span folds, and future observations must not train earlier predictions.
- [Dignam and Kocherginsky, competing risks](https://pmc.ncbi.nlm.nih.gov/articles/PMC2654314/):
  event-specific risk and cumulative incidence answer different questions when outcomes compete.
- [Dudík, Langford and Li, doubly robust policy evaluation](https://arxiv.org/abs/1103.4601):
  logged policy and reward assumptions matter for offline action comparisons.
