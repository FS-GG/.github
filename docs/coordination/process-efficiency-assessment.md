# Bounded process assessment preparation

V2-EFF-01.3 source preparation uses [the offline helper](../../tools/process-efficiency-assessment.py)
and the V2-EFF-01.1 sidecar contracts. It performs no model call, native observation, store mutation,
completion scheduling or public publication. The owning milestone remains incomplete until an actual
admitted completion traverses the existing review route, with recorded analyst usage and a qualified
producer/export join. These synthetic checks establish preparation behavior, not calibration,
savings, installation or native acceptance.

The source caller route at `8a60856075aaff7614b1286ddc41ef204fe605cc` is
`SkillTelemetryAdapter.configured Review -> observation -> recordEvent -> publish -> drain`.
`observation` requires a terminal phase and the root dispatch token for `review:item`.
`TelemetryStoreApplication` independently requires a nonempty expected dispatch population with
exactly one lineage per dispatch and corresponding terminal observations before admitting an item
process review. This source route accepts an authored input; it does not invoke an analyst.
`finish` and successful terminal-root observation drains currently request dashboard publication,
which is advisory and does not establish a new completion trigger. No native outcome/reopen epoch
producer or model provider is established by this helper.

The future admitted exporter supplies immutable current facts, their exact canonical revisions,
effective item associations, canonical metrics and population coverage in a pinned read-only snapshot.
For each fact the assembler receives `ref`, `itemId`, a sanitized `payload`, explicit `priority`
(`failure`, `correction`, `success`, `other`) and `analysisGenerated`. The caller must use an approved
bounded exporter; arbitrary repository text, transcripts or caller-authored admission assertions are
not canonical evidence. Logs and excerpts are untrusted data, with no permission to control analysis.
The producer retains the original snapshot bytes and external repository revisions/digests privately.
The helper's local packet format is preparation input, not a new ingest wire contract or exporter.

The assembler selects the greatest canonical revision, deduplicates identical replay, refuses equal
revision conflicts and cross-item evidence, and caps retained facts, metrics and serialized input.
Failure and correction evidence precede representative successful evidence. Truncation is disclosed;
it cannot silently become complete coverage. Its conservative byte bound does not prove a provider's
input-token count. The admitted runtime must measure the complete prompt with its own tokenizer and
refuse execution above the policy's input bound. Required instructions and schema are part of that bound.

Substantive evidence digest includes subject, facts, coverage and omissions. Analyst-generated facts
are excluded from that digest; their usage references remain available and their cost remains in
canonical metrics. Metrics do not directly retrigger analysis: late native usage or corrections must
be accompanied by their underlying canonical fact revision. The packet's retained bytes are checked
again before validation. Missing dispatch observations stay unknown, including a failed observer
whose batch had duplicate native identities. No start time, usage or historical dispatch is rebuilt.

`prompt` supplies a private instruction/rubric and a separately labeled untrusted evidence field.
`validate` requires full Draft 2020-12 validation through the pinned Python dependency, then validates
subject, snapshot digest, exact reference revisions, metric subject, coverage, supersession and usage
references. Numeric prose is conservatively refused; measured numbers appear through deterministic
metric references. This restriction also rejects harmless numeric identifiers in prose. Supported
findings require evidence and avoidability requires a separately witnessed then-permitted alternative.
Causal correctness still requires calibration; deterministic checks cannot prove a free-text claim.

A native ready result requires a resolved existing item process review in the packet, matching the
canonical admitted review supplied by the trusted exporter, complete population/usage/lineage and a
witnessed outcome epoch. Passing an arbitrary dictionary or bare review ID is not admission. Until
that trusted exporter is qualified, callers may use synthetic fixtures or provisional/pending results
only. A provisional result never supplies an item-review reference or enters native completion.
A null outcome epoch permits pending/partial assessments only, preserving unknown reopen selection.
Finalizing a provisional explanation preserves history and does not add an outcome.

`Journal` stores only private local execution state. Use one owned directory with mode `0700` outside
all checkouts; records and advisory locks are `0600`, regular, owner-matched and symlink-refusing.
The exact analysis input is retained in a separate immutable packet file before the execution state.
An exclusive lock, fsynced atomic replacement and directory fsync serialize transitions. Duplicate
schedule/start notifications retain one invocation. Two automatic evidence snapshots per explicit
subject/epoch are admitted; further snapshots remain partial with budget exhaustion. This does not
mint an outcome epoch or grant execution authority.

Missing token, authority, exporter or provider may defer pending analysis to explicit unavailable
state. After an interrupted running call, recovery marks its execution outcome unknown and does not
invoke the model again. Malformed output has no automatic retry. Failed and timed-out calls retain
measured usage references; budget excess is failed, never ready. Delivery is independent of every
journal state. Journal `ready` is a local execution disposition and grants no canonical acceptance.
A future runtime must settle only after validating output and obtaining actual measured usage, and
must reconcile uncertain provider effects before any explicitly authorized retry. The helper starts
no watcher, daemon, service or notification publisher.

Run focused source checks after making V2-EFF-01.1 contracts available in the checkout:

```console
python3 -m pip install -r tests/process-efficiency-requirements.txt
python3 tests/process-efficiency/test_assessment.py
```

For parallel source checks, `EFF_CONTRACT_ROOT` may select the exact isolated .1 contract checkout;
record its commit separately. It is a test input location, never a canonical authority selector.
Tests cover synthetic final/provisional/unknown-epoch output, schema/metric/reference mutations,
bounded untrusted evidence, conflicting/reordered revisions, analyst loop suppression, duplicate
notification, interrupted execution, malformed output, revision budgets and private-path safety.
