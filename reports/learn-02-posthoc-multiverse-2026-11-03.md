# LEARN-02 posthoc sensitivity appendix — 2026-11-03

This is **posthoc discovery**, requested after inspection of the original historical results.
The [maintained executable](../scripts/learn-history-multiverse.py) ran a finite grid over the frozen
53-history private rows. It preserves 34,680 comparison cells, including 11,610 unsupported cells,
24 grouped prediction evaluations and 12 descriptive cluster-bootstrap evaluations in the
[complete aggregate JSON, gzip compressed](learn-02-posthoc-multiverse-2026-11-03.json.gz).
The existing [historical report](learn-02-hardness-history-2026-11-03.md) and its original results
remain unchanged. This appendix supplies additional discovery, not independent validation,
causal effects, a selected predictor or evidence to activate routing.

## Scope and coverage

| Context | Histories | Parent components | Open | Native closed | Native completed | Not planned |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Observed dispatch/runtime | 34 | 7 | 16 | 18 | 18 | 0 |
| Creation/calendar | 53 | 22 | 22 | 31 | 27 | 4 |

The all-observed cohort retains every open history. Calendar observed duration combines closure
durations and administratively censored ages; its rank association describes that mixed quantity,
not time to eventual completion. Calendar-closed and dispatch-to-close endpoints explicitly count
open histories as missing. Native-completed is a separate selected diagnostic; it does not delete
the four not-planned closures from the creation all-closure comparison. The runtime cohort contains
no not-planned closure and cannot establish robustness to those outcomes.

Runtime response envelopes and 1h/8h/24h gap-capped proxies cover all34 runtime histories;
envelope through native closure and dispatch-to-close cover18. Verified active execution covers0.
Worker-profile labels cover29 of34 runtime histories, leaving5 unknown. Gap/MAD diagnostics cover34
of53 creation histories; unknown diagnostics are retained during diagnostic deletion. Explicit stop
labels are absent, and every requested stop-deletion cell is retained as unsupported. Gap caps and
MAD exclusions use outcome information and are descriptive sensitivities, never causal baseline
controls or verified pauses.

Before the first private run, the source fixed two contexts, three closure cohorts, ten predictors
(H, text-word length, D1–D8), four control sets and nine context-specific endpoints. All predictors
use the corresponding retained creation or observed-dispatch snapshot. The control sets are none;
text length; creation month plus repository and exact unit-header indicators; and their combination.
Header indicators distinguish Delivery, Outcome, neither exact header and unknown; they do not
adjudicate historical work types. The era field is creation month, including in the dispatch context.

The grid includes full cohorts and deletion of each observed parent, repository, unit-header signal,
worker profile including unknown, and creation month; deletion of any MAD-flagged history; deletion
of any history changed by the1h gap cap; the unsupported explicit-stop deletion; and deletion of
all tied largest parents. This yields27 runtime and50 creation scenarios. Anonymous group indices
are deterministic within each context and have no exported identity map. Identical resulting cells
and overlapping cohorts remain in the output; 34,680 cells are not 34,680 independent tests.

## Material findings

| Descriptive comparison | n / components | H rank association | Text rank association |
| --- | --- | ---: | ---: |
| Runtime envelope, all observed | 34 / 7 | 0.097 | −0.419 |
| Runtime envelope, native completed | 18 / 5 | −0.041 | −0.380 |
| Creation calendar closure, all native closures | 31 / 18 | 0.323 | 0.168 |
| Creation calendar closure, native completed | 27 / 14 | 0.277 | −0.029 |

For the full runtime cohort, controlling text changes H's descriptive rank residual correlation
to0.166; controlling creation month/repository/header changes it to0.051, and adding text to those
controls gives0.299. These are different conditional descriptions of the same seven components,
not an estimate that confounding has been removed. The unadjusted H correlation across supported
runtime-envelope deletion scenarios spans−0.153 to0.240. Text remains inversely associated in
these unadjusted deletions (−0.621 to−0.269), but an association need not improve group prediction.

Whole-parent bootstrap resampling (199 draws, seed527603) gives descriptive95% percentile ranges
of[−0.275,0.278] for full runtime H and[−0.590,−0.176] for text. In the18 completed runtime histories,
the ranges widen to[−0.366,0.380] and[−0.649,0.299]. Few independent components, selection and this
posthoc search limit their coverage interpretation. They are neither multiplicity-adjusted intervals
nor equivalence tests. Partial-rank cells have no uncertainty intervals; sparse or constant residuals
remain unsupported rather than receiving a fitted estimate.

Creation H's all-closure correlation drops from0.323 to0.033 with text/month/repository/header
controls. Among native-completed histories it changes from0.277 to−0.071. Creation D8 is a
potential discovery signal: its calendar-closure correlation is0.524 over31 histories, or0.434 among
27 native-completed histories; corresponding fully controlled residual correlations are0.527 and
0.477. The supported unadjusted all-closure deletion range is0.373 to0.589. This is an association
with selected historical calendar duration, not verified compute, task difficulty or a causal cost.
D8 is constant in the runtime cohort, so runtime D8 effects are unsupported. Creation D7's
all-closure correlation is0.527 but drops to0.276 in the completed subset and changes sign in some
deletions. No dimension or subgroup is promoted to a winner.

Fixed ridge prediction uses alpha10, log1p duration, leave-parent-component-out folds and
training-fold standardization. Mean and median baselines use exactly the same complete rows and
folds as all feature models. No penalty tuning or model selection occurs. Full-cohort, tied-largest
parent, MAD and1h-cap deletions are evaluated for each closure cohort; insufficient support is
retained. The source and aggregate retain all six models and all folds.

| Runtime envelope model | All34 pooled log MAE | All34 equal-parent log MAE | Completed18 pooled log MAE |
| --- | ---: | ---: | ---: |
| Training mean | 1.277 | 1.570 | 1.098 |
| Training median | 1.204 | 1.514 | 1.060 |
| H | 1.304 | 1.614 | 1.141 |
| Text | 1.136 | 1.527 | 1.220 |
| Text plus H | 1.155 | 1.571 | 1.248 |
| D1–D8 | 1.498 | 1.808 | 1.775 |

Removing all tied largest runtime parents leaves18 histories in five components: pooled errors
are1.065 for median,1.169 for H,1.101 for text and1.528 for the vector. Thus text's full-cohort
pooled advantage over median reverses under equal-parent weighting, parent deletion and completion
restriction. H and the vector do not improve the full runtime baselines.

Creation all-closure pooled errors are1.860 for median,1.825 for H,1.799 for text,1.755 for text
plus H and1.986 for the vector. Among native-completed histories they are1.241,1.272,1.418,1.370
and1.673. This composition sensitivity is a reason to investigate closure mechanisms. It is not
evidence that unsuccessful cases should be removed to improve a predictor. These group folds reuse
discovery data and are not an untouched final test.

## Follow-up hypotheses sealed after this discovery

The following are candidate research questions frozen by this appendix **after** inspecting the
results. Testing them requires independent data or separately preserved future outcomes, explicit
sampling and endpoint definitions, and controls chosen before viewing those outcomes. Re-running
these rows cannot confirm them.

1. Does creation-time D8 predict independently adjudicated task duration within comparable
   month/repository/unit strata when D8 varies, and does that relation survive verified execution
   clocks and unsuccessful/open outcomes? Do not transport the runtime-constant dimension.
2. Does text length describe assignment/documentation or censoring composition rather than stable
   generalization across parent components? Test parent-weighted predictions against both mean and
   median baselines, with baseline scope and pre-work provenance adjudicated independently.
3. Are apparent creation-calendar gains associated with closure mechanisms or historical cohort
   differences? Predefine competing outcome labels and retain open/not-planned histories; do not
   retrospectively select successful closures or infer causes from these correlations.

No candidate establishes a threshold, a magic sample size, model/effort invariance, an equivalence
claim from a nonsignificant test or routing activation. A future design needs power/support and
quality endpoints appropriate to its actual construct.

## Reproduction and validation

The run used CPython3.15.1, NumPy2.5.4 and SciPy1.18.1 in the existing pinned private environment.
Numerical thread variables are set to1 before imports; the single process has a240CPU-second and
2GiB address-space limit. No network, native API, transcript reads, pilot results or original inputs
were changed. The source directly consumes the frozen derived rows and checks unique identities.
The first run passed synthetic controls and finished in6.45seconds. A coverage clarification then
preserved truthful n/missingness for unsupported stop cells and added missing diagnostic coverage,
deterministic compressed output and additional synthetic checks. The second run finished
in6.47seconds with identical numerical results; reported maximum RSS was1,560,616KiB. No execution
failure occurred. Both output directories remain private (0700, files0600); neither was overwritten.

```bash
PY=/home/developer/.fsgg-scratch/5276/stats-venv/bin/python
"$PY" scripts/learn-history-multiverse.py --self-test
"$PY" scripts/learn-history-multiverse.py \
  --rows /home/developer/.fsgg-scratch/5276/statistics-6/PRIVATE-rows.json \
  --output /home/developer/.fsgg-scratch/5276/multiverse-REPRO-NEW
```

Choose a fresh output directory; existing outputs are rejected. Synthetic controls verify a known
monotonic signal, constant-dimension rejection, collinear residual rejection, missing endpoint
accounting, open retention, grouped training/test counts, positive predictive signal, identical
constant-feature/mean predictions, seed reproducibility, tied-largest deletion and stop unsupported
status. The full run reports23,070 descriptive cells,4,940 row/cluster failures,3,910 residual/degree
failures,1,680 predictor-also-controlled failures and1,080 missing-stop failures. No p-values were
computed. The grid retains every failed/unsupported comparison rather than silently filtering it.

SHA256 of frozen private rows: `30dc3771dc3b1add81e0ed00158fb0c2d0607fdc8789ae8cba9b7628a9b35229`.
The aggregate records this hash and its exact script hash. The compressed JSON is deterministic
for identical source/input/environment and excludes machine-dependent runtime timings. It contains
no individual identifiers, repository identities, baseline text or profile identities. Decompress
with Python's standard `gzip` module to inspect all JSON cells. Numerical comparisons and gzip
bytes are checked across a fresh rerun before delivery.

Atlas `framework/learn-02-historical-validity` was retrieved first; all three cited immutable source
hashes were verified. Its scoped original findings remain applicable. The new creation-D8 discovery
and outcome-composition sensitivity need a reviewed immutable appendix citation before a reusable
Atlas extension; the #5276 owner retains that capture gap. This appendix does not supersede the
original diagnostic, operational authority or held-out acceptance requirements.

The initial report’s tied-largest sensitivity prose is corrected in this same source revision: runtime parent sizes are 1, 1, 3, 6, 7, 8, 8; removing both tied largest groups leaves 18 items/five groups. Earlier wording said one 16-item group and six remaining groups. Code and numerical errors already used the correct two-group removal; no evidence was rewritten.
