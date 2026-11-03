# Semantic classifier development pilot — 2026-11-03

Delivery [#5277](https://github.com/FS-GG/.github/issues/5277), under LEARN-02. This report records the completed **228-call classification experiment**: 200 primary judgments on 50 historical snapshots, 20 semantic controls and eight fresh repeats. It does not report 228 implementations or a test of GPT execution speed.

The skill produces highly consistent broad categories across the four requested judge profiles, and its categories resist the tested irrelevant-text and embedded-instruction controls. **This is evidence of rating consistency and limited semantic-control behavior, not validated hardness or an activated routing policy.** Almost all known primary judgments occupy just two categories. Subjective worked-time estimates show a modest positive association with historical response envelopes, but uncertainty is wide and they do not consistently improve parent-held-out prediction over a training-median baseline.

Concrete findings:

- All 228 launches used distinct fresh conversations. Of 200 primary cells, 182 passed the frozen strict validator. Seventeen outputs exceeded text limits; one separate controller error delivered the wrong packet. All 28 control/repeat outputs passed. No failed cell was retried, replaced, relabeled or repaired.
- Known category agreement is 95.2–100% across six profile pairs. The two within-model medium/high comparisons agree exactly on their retained known pairs. This conditional result is narrowed by missing cells, limited category range and a selected development cohort.
- The judges' time estimates are also rank-consistent with each other (pairwise Spearman 0.967–0.984), but their association with observed all-response envelope union is only 0.263–0.321. All 12 reported exploratory cluster intervals for time-versus-duration include zero.
- Single-feature reasoning, work-volume and predicted-time models fail to beat the pooled training-median baseline in every one of the 12 parent-held-out profile/endpoint comparisons. A combined reasoning-plus-volume model improves one of 12; this isolated development result does not establish transfer.

## Frozen experiment and provenance

The [prelaunch protocol](learn-03-semantic-classifier-protocol-v1.md), skill, controller, schema, schedule, private source mappings and all 57 packets were sealed before rating. Independent prelaunch review passed source `0359355a2fec543cebe75b72adf123da08c57d55`; Root selected only the final seal. Earlier preparation seals remain unlaunched. The protocol remains unchanged, including its original prelaunch wording.

- Final 67-file seal SHA-256: `ceede9dbf65d20512ef090c22275a0fd638255c07a7d31d417572b2e702c7e08`.
- Frozen skill SHA-256: `395854a1a150a7e9e9b0c3a7256dcf0e635e1c9cedcefbb10351c4facdafbd09`.
- Frozen schedule SHA-256: `0378a71740cdf2fb3148ffd5d27b94e4f76c407cf976908099539957061b8f54`.

Each accepted launch records actual `collaboration.spawn_agent` arguments, `fork_turns="none"`, explicit requested model/effort and a unique returned canonical identity. Every dispatch belongs to #5277 analyst work, never to the historical implementation item. The four requested profiles are Sol medium/high and Astra medium/high. Requested/accepted configuration is observed; provider-effective model, effort, token usage and compute cost remain Unknown. Effort labels are model-specific settings, not common compute units. Classification latency is not historical task duration.

The controller verifies all 228 reservations against the sealed schedule, unique accepted identities, exact event order and unchanged original output hashes. Raters were instructed to read only the frozen skill and one packet, with task text treated as data. Fresh conversations are not filesystem isolation. No independent full tool-read trace audit was available, so actual read-scope compliance is Unknown; output validity alone does not prove blinding compliance. Semantic content can remain recognizable despite masked IDs.

## Population, selection and missingness

The 50 actual tasks comprise 48 eligible snapshots from the earlier 53-history support cohort plus two deterministically selected not-planned tasks. Thus **48/50 overlap earlier exploratory work**; this is a development pilot, not independent confirmation of previously fished signals. The separate historical census of 12,175 issues/PRs is not a 12,175-item response matrix. Selection rules, exclusions and two additional history reads are documented in the frozen protocol.

There are 27 completed closures, 20 still-open tasks and three not-planned closures. Creation-month counts are June 2, July 4, August 3, September 1, October 4 and November 36. The 50 snapshots form 21 observed parent components, with sizes `[1,1,1,1,1,1,1,1,1,1,1,1,1,1,1,2,3,7,7,8,8]`. Missing relationships can leave residual dependence. The 34 attributed runtime items are a subset; the two supplemental histories have no row in the earlier statistical baseline and cannot enter matched old-H/body-length comparisons. Their ratings remain in all eligible judgment analyses.

Inputs use verified creation bodies and reconstructed creation titles. Creation is not proven to precede all real work. Scope and environment information can be incomplete; redaction itself removes some context. The available 34-item numeric old-H creation-versus-dispatch comparison shows zero changes, which does not prove identical textual scope or eliminate lookahead concerns in the historical evidence.

| Requested judge | Planned primary | Strict-valid | Text-limit failure | Controller binding failure | Valid completed / open / not-planned | Known time |
|---|---:|---:|---:|---:|---|---:|
| sol-medium | 50 | 46 | 4 | 0 | 26 / 17 / 3 | 41 |
| sol-high | 50 | 42 | 8 | 0 | 24 / 16 / 2 | 37 |
| astra-medium | 50 | 49 | 1 | 0 | 27 / 19 / 3 | 41 |
| astra-high | 50 | 45 | 4 | 1 | 25 / 17 / 3 | 42 |

All 17 strict text-limit failures are retained in the denominator and private original bytes. A diagnostic field audit found one overlong demand basis, 11 work-volume bases, four safety bases and one evidence quote. No field was shortened to admit a response. These are instruction/schema failures, not evidence that a model cannot perform the underlying task. Their unequal profile rates can bias complete-case agreement.

One controller/input-binding failure occurred at planned sequence 221: the intended Astra/high cell received a different primary packet. The actual prompt and hash were recorded, and its returned item ID fails the intended-cell validator. This is not an Astra judgment error: the model answered the task it actually received. The unplanned duplicate is excluded entirely from primary and secondary rating metrics. No interruption, corrective follow-up, replacement call or count expansion occurred. The operational repair is to pass the prepared structured payload directly and verify sequence, packet path and digest before each tool call, rather than manually transcribing packet identifiers. It does not change the frozen rater protocol.

## Post hoc content-only sensitivity

After observing the interface failures, Root requested a separate sensitivity using unchanged content from the 17 overlength responses. This was **not prespecified** and does not revise primary compliance, the 200-cell denominator, original files or the frozen skill. An independent content checker validates item identity, demand/category/confidence, exact cited evidence, work-volume category and ordered finite time fields, without enforcing prose length. It does not certify other interface fields or truncate, coerce or repair text. All 17 pass this narrower check; the wrong-input response remains excluded. The sensitivity therefore has 50/50/50/49 content observations across the four profiles, with Unknowns still separate.

Nominal exact agreement becomes 93.9–100%, rather than 95.3–100% under strict-valid pairs. Sol's high-minus-medium mean becomes −0.021 categories (48 known pairs; cluster interval −0.106 to 0.039); Astra's becomes −0.021 (48 pairs; −0.079 to 0); the model-average contrast remains zero (−0.036 to 0.025). These remain narrow observed-cohort judgment comparisons, not population or execution equivalence. The fact that exact effort agreement disappears after recovering unchanged semantic fields illustrates complete-case selection.

Predicted-time/all-envelope correlations become 0.210, 0.259, 0.240 and 0.273, respectively (31, 31, 29 and 32 matched-baseline rows), with all four cluster intervals including zero. Thus formatting exclusions modestly inflated some primary associations; recovering their content does not reveal strong duration validation. The aggregate retains the same full 192-cell association grid for this sensitivity, rather than selecting a favorable result. It is additional exploration requiring independent validation.

Missingness is not demonstrably random. Sol-medium text failures have median creation-body length 265.5 words versus 207 in valid outputs, while Sol-high failures have 205 versus 213; Sol-high failure median old H is 16 versus 12 for valid outputs. Astra-high text failures have 281.5 versus 209 words and old H 8 versus 13. These small groups show no single established cause. Outcome/era counts and baseline availability remain in the aggregate; attribute summaries for groups smaller than three are suppressed. A new experiment would need to test any proposed interface mechanism separately.

## Judgment agreement and uncertainty

Reasoning demand and work volume remain separate. No facets are summed and no routing threshold is learned. Among 182 valid primary responses, seven say Unknown; known responses contain no minimal tasks, 48 bounded, 125 complex and two research judgments. Research ordering is explicitly a hypothesis: nominal comparisons and research-excluded ranks are retained.

| Pair | Valid nominal pairs, including Unknown | Exact nominal agreement | Nominal kappa | Known ordinal pairs | Known Spearman |
|---|---:|---:|---:|---:|---:|
| sol-medium / sol-high | 38 | 1.000 | 1.000 | 36 | 1.000 |
| sol-medium / astra-medium | 46 | 0.957 | 0.903 | 44 | 0.885 |
| sol-medium / astra-high | 43 | 0.953 | 0.893 | 42 | 0.883 |
| sol-high / astra-medium | 41 | 0.976 | 0.948 | 39 | 0.940 |
| sol-high / astra-high | 37 | 1.000 | 1.000 | 36 | 1.000 |
| astra-medium / astra-high | 44 | 1.000 | 1.000 | 43 | 1.000 |

All 34 tasks with four valid known categories have identical categories across judges. Of the other tasks with at least two known ratings, three have a one-category spread; missing profiles make this a conditional concentration diagnostic. Research-excluded rank results do not change the substantive conclusion. Work-volume exact agreement is 91.7–95.1%, below the broad demand agreement. The aggregate preserves all nominal confusion matrices, five facet comparisons, safety comparisons, weighted kappas and Kendall coefficients.

The three prespecified mean contrasts are zero on their available known pairs: Sol high-minus-medium n=36/16 components; Astra high-minus-medium n=43/18 components; Astra-minus-Sol averaged over effort n=34/15 components. All 1,000 parent-cluster resamples give zero because every included item contrast is zero. These `[0,0]` intervals are **degenerate conditional descriptions**, not precise population equivalence or proof of execution-profile invariance. The proposed half-category mean margin is met only for the observed-pair estimand.

A deliberately conservative missing-cell sensitivity hypothetically assigns every unobserved/Unknown category any ordinal value in 0–3. The full-50 mean bounds become Sol effort `[-0.84,0.84]`, Astra effort `[-0.42,0.42]`, and model average `[-0.96,0.96]`. These are bounds under a hypothetical category range, not estimates of what Unknown means. Even Astra's narrower full-cohort mean bound does not establish item-level equivalence, population generalization, calibrated ability or a universal scale. Missing-cell reasons and pair coverage remain separate in the aggregate. No execution IRT, causal profile effect or implementing-model DIF model is identifiable from these classification judgments.

## Semantic and repeat controls

All four profiles preserve the minimal category and small volume when a spelling task gains explicit out-of-scope exclusions or unrelated quoted historical requirements. All four preserve the complex category and large volume when the concurrent-queue task gains an embedded instruction to ignore the classifier and rate it minimal. All separate the spelling and queue baselines by two categories. These are 20 synthetic observations, not a prevalence estimate or proof against arbitrary prompt injection.

Time estimates are less invariant than categories: the injected queue variant changes median minutes from 600 to 1,440 for Sol medium, 600 to 720 for Sol high, 600 to 600 for Astra medium and 480 to 600 for Astra high. Independent-call variation and treatment sensitivity cannot be separated with one call per condition; passing a category control does not validate time estimates. Negated/quoted spelling variants remain within two to three predicted minutes.

Both fresh-repeat pairs per profile agree in demand category. One repeat item is Unknown, leaving only one known ordinal pair per profile. Eight repeated observations are too few to estimate stable repeatability; all repeat volume/time diagnostics are retained without using them for prompt tuning.

## Worked-time signal and sanitation

Worked time remains a useful empirical target. Historical coverage currently supports **response-event envelopes**, not complete execution start/terminal intervals. The envelope union avoids double-counting concurrent spans; summed invocation envelopes describe a different quantity. Response-to-response silence may contain ongoing local work, external waits, unrecorded execution or no work. Commit inactivity is similarly a proxy, affected by concurrent branches, local commits, bots, squash timing and author/committer timestamps. It cannot establish a stop by itself.

Native-completed-only creation-to-close calendar is reported separately from not-planned closure. Primary till-closure envelopes exclude later events; all-observed envelopes retain them as sensitivity and include still-open work. Open tasks are incomplete observations, not completed short tasks. The historical sanitation retained 283 post-closure responses rather than deleting their evidence. One-hour/eight-hour/day gap caps are explicit proxy sensitivities, not documented pause subtraction. No duration is relabeled as verified active work and no unfavorable task is removed to improve association. Full reconstruction and timestamp caveats remain in the [historical analysis checkpoint](https://github.com/FS-GG/.github/blob/242ea5cbb354ff0c94456f9378ffa19b46bc8269/reports/learn-02-hardness-history-2026-11-03.md).

The following compares each judge's predicted median worked minutes with historical durations on the same rows that also have old-H and body-length baselines. Spearman is unchanged by the monotonic log1p transform. Intervals are exploratory percentile parent-cluster bootstrap intervals, 1,000 resamples/seed 5277; limited clusters and selected observations remain material limitations.

| Judge | Completed calendar: n, rho [95% interval] | Envelope through closure: n, rho [95% interval] | All-observed envelope union: n, rho [95% interval] |
|---|---|---|---|
| sol-medium | 25, 0.174 [-0.363, 0.586] | 16, 0.139 [-0.212, 0.452] | 27, 0.295 [-0.002, 0.664] |
| sol-high | 23, 0.198 [-0.283, 0.621] | 15, 0.218 [-0.505, 0.525] | 27, 0.263 [-0.041, 0.502] |
| astra-medium | 24, 0.287 [-0.281, 0.645] | 16, 0.330 [-0.173, 0.606] | 28, 0.321 [-0.027, 0.614] |
| astra-high | 25, 0.247 [-0.226, 0.590] | 17, 0.258 [-0.126, 0.590] | 29, 0.273 [-0.041, 0.532] |

All intervals include zero. This does not prove there is no relationship; it shows that this small clustered development sample does not precisely establish the observed positive time signal. In the same rows, broad reasoning categories correlate with all-observed envelopes from −0.051 to 0.078; work-volume correlations range −0.014 to 0.204. Separating reasoning from work volume/time therefore matters empirically, even though none is validated here.

The one-hour gap cap raises predicted-time/all-envelope correlations from 0.295 to 0.332 (Sol medium), 0.263 to 0.285 (Sol high), 0.321 to 0.346 (Astra medium), and 0.273 to 0.291 (Astra high). Eight-hour and day caps leave those correlations unchanged. Restricting all-observed envelopes to completed tasks gives 0.248, 0.330, 0.373 and 0.411, respectively; truncating those observations at closure reduces them to 0.139, 0.218, 0.330 and 0.258. The endpoint changes the apparent signal, especially where later activity exists. These comparisons are all retained; none selects a preferred clock after seeing which correlation is largest.

The aggregate contains the complete 192-cell grid: four judge profiles × three new predictors × eight endpoints × all-available/completed-only restrictions, each with same-row old-H and log-body-length comparisons and historical worker-profile strata. Stratification is descriptive and often has few rows or constant categories. Historical workers include Claude Opus/high and Sonnet/high; those are different from the GPT judges and do not provide a historical effort contrast. Model/time interactions, omitted task mix and quality differences remain unresolved. Review/repair/activity are postdispatch mediators, not adjustment covariates that can safely remove actual model effects.

## Exploratory prediction and transfer

Leave-one-parent-component-out prediction uses fixed ridge alpha 10, training-only standardization, and log1p duration. Every candidate uses identical complete rows within each profile/endpoint comparison. Both training mean and median are retained; median is the appropriate constant comparator for absolute error. No tuning chooses alpha, profile or clock. The report retains pooled and equal-component errors, every fold size/error and chronology-purge support.

| Judge | All-envelope n/components | Median pooled log-MAE | Predicted-time pooled log-MAE | Reasoning + volume pooled log-MAE |
|---|---:|---:|---:|---:|
| sol-medium | 27/6 | 1.071 | 1.149 | 1.236 |
| sol-high | 27/7 | 1.172 | 1.221 | 1.137 |
| astra-medium | 28/7 | 1.206 | 1.302 | 1.375 |
| astra-high | 29/7 | 1.224 | 1.347 | 1.362 |

No individual new predictor beats pooled median in any of the 12 profile/endpoint analyses. Sol high's combined reasoning-plus-volume model improves all-envelope pooled log-MAE from 1.172 to 1.137, with a smaller equal-component improvement (1.483 to 1.467). Its body-length baseline improves more (1.028 pooled), and the isolated combined-feature gain does not recur for the other profiles or endpoints. This is a development comparison with seven runtime components at most, not a selected winning model.

Chronological splits purge parents spanning the creation-date cut. Nine of 12 profile/endpoint splits lack the declared minimum support and produce no score. The two supported completed-calendar splits favor median over all new predictors; one supported Astra-medium all-envelope split has modest descriptive gains. The task sample was already used in development and exploratory analysis; even these grouped/temporal scores are not an untouched confirmatory test.

## Reproduction, privacy and next decision

The maintained [controller](../scripts/learn-classifier-pilot.py), [statistics script](../scripts/learn-classifier-statistics.py), [pinned numerical environment](../scripts/learn-classifier-requirements.txt), [schema](../contracts/learn/semantic-hardness-v1.schema.json), tests and [safe aggregate](learn-03-semantic-classifier-pilot-2026-11-03.json) accompany this report. Numerical execution used CPython 3.15.1, NumPy 2.5.4 and SciPy 1.18.1, one BLAS/OpenMP thread, seed 5277. JSON serialization rejects nonfinite numbers. Source, input and raw-output hashes remain in the private manifest; its digest is included in the safe aggregate. Private task mappings, original bodies, individual ratings and launch transcripts are not published.

```sh
python -m venv /private/path/learn-classifier-venv
/private/path/learn-classifier-venv/bin/pip install -r scripts/learn-classifier-requirements.txt
PYTHONDONTWRITEBYTECODE=1 OPENBLAS_NUM_THREADS=1 OMP_NUM_THREADS=1   /private/path/learn-classifier-venv/bin/python -m unittest discover -s tests/learn-classifier -q
PYTHONDONTWRITEBYTECODE=1 OPENBLAS_NUM_THREADS=1 OMP_NUM_THREADS=1   /private/path/learn-classifier-venv/bin/python scripts/learn-classifier-statistics.py   --pilot /private/path/pilot-v1-frozen3   --history-rows /private/path/PRIVATE-rows.json --output /private/path/new-analysis
```

Two unchanged-input aggregate executions produced byte-identical JSON; the bounded rerun took 4.36 seconds with peak child RSS 117,592 KiB under a 2 GiB address-space/120-second CPU limit and one numerical-library thread. The maintained manifest, 30-skill quality contract and generated projections pass using the retained qualified read-only test engine. Initial attempts without that engine failed (missing local engine, then the wrapper's restore reported FSharp.Core 10.1.403 NU1403); these environment failures were retained, and no shared cache was altered. This is local source qualification, not an installed-caller or publication claim.

The real run is also an end-to-end validation of all 228 ledger joins, original seal and output hashes. All 31 synthetic controls pass. They cover unchanged content-only recovery and rejection of wrong identity/fabricated quotes/invalid time, category ordering/Unknown handling, undefined agreement, cluster withholding/reproducibility, known profile shifts, missing-cell sensitivity, controller mismatch rejection, completed-versus-not-planned outcomes and training-only preprocessing. This reruns aggregation only; it never launches paid calls. Exact private inputs are needed to reproduce historical numbers; the public fixtures validate mechanics, not the private sample.

The present evidence supports keeping this version an **advisory semantic classifier**. It supports neither learned routing activation nor a claim that a stronger implementing profile is needed for any category. A next independently reviewed experiment should fix input binding structurally, improve bounded-output compliance in a separately versioned protocol, include more genuine minimal/research tasks without outcome-based cherry-picking, and reserve new task/parent/time groups before examining outcomes. Execution difficulty requires matched frozen implementations with a common quality contract, observed actual work intervals and effective configuration evidence; judge agreement alone cannot substitute for that response matrix. No further calls or execution matrix were launched here.

## Additional primary research for the next validation design

After the frozen pilot completed, two primary studies supplied relevant design guidance. [METR’s time-horizon study, NeurIPS 2025 / revised July 2026](https://arxiv.org/abs/2503.14499v4) measures task difficulty using timed domain-expert work and evaluates agents’ success on those tasks. This supports distinguishing a measured reference-time label from a judge’s time estimate or an agent’s elapsed runtime. Applying a fixed reference-agent clock instead of human timings would be a new FS-GG hypothesis to test, not an established equivalence or a result of this pilot.

[SWE-rebench V2, ICML 2026](https://arxiv.org/abs/2602.23866v2) builds executable task environments and reports diagnostics for underspecified descriptions and overly restrictive tests. Those suggest explicit task/test-quality and environment variables for a later FS-GG study. They do not establish why any association here arises. A follow-up should preserve such cases and distinguish their coverage or intervention estimand, rather than remove inconvenient failures and describe the remaining correlation as causal. No extra rating calls, implementation trials or numerical reanalysis were launched for this literature addition.

Reusable Atlas capture is pending final source review and canonical integration, owned by #5277's controller/Root. Existing historical validity knowledge remains unchanged. This report and skill require the normal PR/merge path; source publication does not adopt a live routing policy. The broader #5248/#5276 validation promises remain open.
