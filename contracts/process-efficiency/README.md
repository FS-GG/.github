# Process efficiency contract v1

V2-EFF-01.1 freezes an additive measurement contract and executable synthetic examples.
The normative vocabulary, mappings, formulas, population boundaries and policy aliases are in
[measurement-contract-v1.json](measurement-contract-v1.json). The two Draft 2020-12 schemas
are [assessment](assessment-v1.schema.json) and [metric](metric-v1.schema.json).
The [allocation input](allocation-input-v1.schema.json), [episode input](episode-input-v1.schema.json)
and [assessment input](assessment-input-v1.schema.json) freeze the concrete additive producer shapes.
These declarations do not activate an ingest kind, storage migration, analyst, public feed or receiver.

## Canonical joins and migration

The inspection is pinned to `8a60856075aaff7614b1286ddc41ef204fe605cc`, with file SHA-256 digests
in the contract. At that revision the store is schema **13**, ingest is `/1`, item detail is `/2`,
and the dashboard host export is `/3`. The separate source-delivery consumer's `/4` remains a
subsequent consumer integration. Coherent installed **0.98.0** and its supported cross-item CI
correction are already qualified; the owning dashboard roadmap records 204 targets moved, 144
native outcomes conserved and idempotent retry. This contract reuses that supported correction.
Synthetic fixture amount 144 is a resource amount, not a measurement of those real 144 outcomes.

Join source facts by stable identity **and exact canonical revision**, retaining source kind.
Outcome references identify repository/PR/merge commit/stage; an invocation references its admitted
attempt/item; CI allocations reference their original run/job and resource scope. Corrections use
`TelemetryCi` plan/apply/history and existing `current_*` effective-attribution views. No new event
or ingestion time overrides their authority. Conflicting equal revisions remain unknown.

The JSON source reference `revision` is a **nonnegative** canonical fact revision (legacy starts at
zero), not a Git SHA. Assessment revisions independently start at one. The evidence
snapshot retains the repository revision/digest identifying external input bytes. Source timing,
observation timing and projection timing remain separate. Budget epochs are not outcome epochs:
no native reopen epoch producer was found. `subject.outcomeEpoch` must be null unless an explicit
witnessed outcome/reopen sequence establishes it. A null epoch may receive pending/partial analysis,
but cannot establish ready final assessment or current reopened acceptance. Never default it to 1.
The synthetic schema samples explicitly posit witnessed epoch 1 and native admission.

Legacy activity categories map explicitly. `coordination` and `context-recovery` use legacy `other`
with the new explicit label; old records are not retrospectively relabeled. Native usage's `direct`
is an exact same-invocation join, not a productive-work classification. `mixed`/`unclassified` stays
unknown purpose without a supported conserved split. Old complications and prose reviews remain
valid and private; their broad labels do not automatically become supported causal findings.

The new profile IDs are **contract aliases** referencing existing exact policies. The V2 model
profile keeps the 10% objective, 20% ceiling, ten comparable baseline/candidate items and 95% usage
coverage. Its P/O definition includes the existing policy's planning, review, coordination, delivery,
shared-flow maintenance and 30-day attributed repair scope. The narrow UTEL profile keeps the 5%
target, strict >10% ceiling, >25% severe trigger, 15 distinct items and deployed-plus-verified reset.
Useful tests remain excluded under that profile's canonical reducer. An efficiency purpose is not
a budget verdict and may not relabel useful assurance to improve a ratio. These aliases change no policy.

No external trace adapter is selected. OCEL/OpenTelemetry/FOCUS remain conceptual references;
there is no implied external wire conformance. A future adapter must pin its actual convention.

## Inactive producer admission selected for .2

The new event kinds are exactly `efficiency-resource-allocation/1`,
`efficiency-problem-episode/1` and `efficiency-assessment/1`. Their `/1` suffix versions the
payload; the existing batch schema remains `fsgg.telemetry.ingest/1`. The adapter drops only the
standalone input's `schema` key before embedding its exact event into existing `events[]`.
The [request](analysis-request-input-v1.schema.json), [claim](analysis-claim-input-v1.schema.json)
and [settle](analysis-settle-input-v1.schema.json) shapes select a finite canonical analysis queue
under `efficiency-analysis-request/1`. Only engine-owned authenticated `telemetry efficiency analysis
enqueue|claim|attach-invocation|settle|inspect` commands may change its lifecycle; generic revision batches cannot claim
or settle it. Claim CAS checks expected revision/content digest and reserves original per-item epoch
budget across scopes before dispatch. Pending model/claim/invocation fields remain null. Claim binds
a witnessed expected dispatch; actual invocation admission arrives through the existing started
route after spawn. An unknown started effect consumes budget and cannot automatically retry.
The [attach action](analysis-attach-invocation-input-v1.schema.json) CAS binds that actual native
invocation with the same claim/owner/generation and consumes no new budget. Settlement binds the
admitted result; stale/conflicting results refuse. Claim separately discloses each limit as enforced,
unavailable or observed-only; an observed bounded call cannot establish universal hard token caps.

The selected .2 store extension is additive **schema 14**, with receiver-owned acceptance clocks;
no migration or write is activated by this milestone. The producer, store, collector and dashboard
may migrate together without backward compatibility. Historical facts, audit history and supported
CI correction remain preserved. The contract's `producerAdmission` specifies exact new-versus-existing joins.

An allocation is resource-global (`itemId: null`), with one identity per native resource/unit/scope;
its rational shares name effective canonical items and exclusive purposes. The original resource
amount is measured once, never supplied again as a new usage event. Its reference must match actual
native counters or witnessed CI intervals, provider/unit/scope and content digest. Existing producer
enrollment/association, operational grant/origin, root dispatch and native invocation remain the
admission authority. Provenance strings are claims, not grants. Unknown mixed evidence cannot acquire
supported purpose or avoidability. Shared ownership gaps preserve an unallocated remainder.

Supported correction selection enters the metric fingerprint with its revision/digest/effective item.
A stale or conflicting allocation after correction becomes unknown/unallocated while conserving the
original measured resource once. Rejected classification stays in audit; it neither loses cost nor
creates a second outcome. An episode contains structured findings and allocation references rather
than copied measured cost. Its attempt/activity/evidence/recovery joins must resolve at exact revisions.
Provisional episode/assessment scope joins known source delivery without forging a terminal attempt.
Final ready still requires existing root-admitted item review and complete native population.

Receiver `acceptedAt` records the actual applied receipt/transaction clock for new records. Historical
records without that clock retain null ingestion age. The .3 private journal fields are frozen separately
under `producerAdmission.durableAnalysisState`; that journal cannot confer canonical completion authority.

## Mandatory semantic validation

Schema validation is necessary but does not prove admission, attribution or causality. Consumers
must additionally enforce these joins and invariants:

- Every evidence, recovery, metric and supersession reference resolves at its declared revision,
  cutoff and subject. Stable revision conflicts refuse a complete accounting claim. The evidence
  digest resolves immutable retained bytes; syntactic digest validity alone is insufficient.
- Supported findings require evidence. Supported avoidability also requires a feasible alternative
  allowed by the then-current policy. Primary cause is exclusive and supported; hypotheses have
  unknown primary cause and cannot enter supported-cause/avoidable numerators. Contributing factors
  are nonexclusive counts and never multiply an episode's cost.
- `native-item` ready references an existing root-admitted item process review, complete expected
  dispatch lineage, terminal and qualified usage populations, and a witnessed outcome epoch.
  The extension cannot grant those facts. `provisional-delivery` has no item-review reference and
  permits pending/running/partial/failed/unavailable states. It cannot count as native completion.
- Idempotency hashes a UTF-8 compact JSON array in the contract's exact listed field order,
  `ensure_ascii=true`. Revision selection is canonical and append-only. Final review may supersede
  provisional explanation, preserving both histories and one source delivery. Reopen preserves prior
  epoch cost and history; it does not silently reset cohort expenditure.
- `work-mix` metrics require an explicit closed `purpose` dimension, including unknown; other
  metrics use `purpose: null`. `data-health` similarly requires explicit `healthDimension` for
  source/ingestion/publication age or producer population/lineage/pending analysis. Metric IDs are
  opaque and cannot supply those meanings.
- Amounts stay in separate resource units and accounting scopes. Native total is input + output;
  cached input and reasoning are subsets. Shared rational shares sum to at most one; the remainder
  is unallocated. Deduplicate original resource identities before allocating, and round only display.
  Work mix is exclusive; lifecycle repair, causes and analysis burden are intersecting views.
- A metric's numerator/denominator encodes an exact rational in its unit. A scalar integer has null
  denominator; a ratio requires a known positive denominator. Unknown ratio denominator stays null
  with partial/unknown status and an explicit reason. Zero accepted outcomes yields not-applicable,
  value null and observed costs retained. Unquantified missing usage is unknown, not zero.
- Estimated currency requires currency and price version; billed currency keeps observed bill
  provenance. Ratios/cohort charts disclose item IDs, work type, window, exclusions, open/abandoned
  exposure and cutoff. The same unit and population apply to numerator and denominator.
- Union witnessed touch and wait intervals inside the native item window. Span sums are observed
  span time, not elapsed partition or active effort. Empty observations are unknown, never inferred
  zero wait. Concurrent waits are not added as saved critical-path time; that claim needs dependency
  coverage. First-pass/retry claims disclose excluded unknown populations and observation replay.
- Measured numeric statements resolve deterministic metrics; analyst output has only metric refs.
  Analysis-generated facts are excluded from substantive trigger digests but included in cost.
  One invocation per snapshot, two automatic evidence revisions per epoch, 8,000 input tokens,
  1,500 output tokens and 60 seconds bound the selected analysis policy. Malformed output has no
  automatic model retry. Exhaustion/failure stays explicit and does not alter source delivery.
- Assessment content remains private. `.4` must supply an approved bounded structured publication
  policy; this schema grants no public prose approval. Unsupported wire versions retain the last
  valid feed with an incompatibility state rather than becoming an empty successful dataset.

## Executable examples

Run the source-only checks, with no .NET, store, provider or browser dependency:

```console
python3 tests/process-efficiency/test_contract.py
```

[Metric fixtures](../../tests/process-efficiency/metric-fixtures-v1.json) include twenty-three independently
hand-calculated cases, final/provisional schema samples, exact allocation fractions, overlapping
spans/waits, cancellations, same/changed-input retries, observation replay, zero acceptance, analyst
cost, incomplete/late usage, reopen history, equal-revision conflict and supported correction.
The Python oracle calculates totals from inputs rather than copying expected values. Historical
source-byte verification explicitly skips when a shallow checkout lacks the pinned object; the other
semantic cases still run, and retained exact-source qualification remains separately required. Mutation checks
reject over-allocation, conflicting identities, unknown evidence/metrics, fabricated measured fields,
unsupported taxonomy, incomplete final reviews and provisional ready state. Its bounded offline schema
checker covers only the vocabulary used here; production consumers use a full Draft 2020-12 validator
plus canonical semantic admission, not this fixture checker as an authority.

The [labeled corpus](../../tests/process-efficiency/labeled-corpus-v1.json) has **60 synthetic episodes**:
30 development, 30 reserved held-out, six examples in each of ten families. It establishes example
vocabulary and conservative boundaries, not independently adjudicated truth or achieved precision.
Do not tune `.5` on held-out outcomes or claim calibration/coverage savings from these authored labels.
Real missing usage remains missing; no historical session usage is reconstructed here.
