# Bounded process assessment preparation

V2-EFF-01.3 source preparation uses [the offline helper](../../tools/process-efficiency-assessment.py)
and the V2-EFF-01.1 sidecar contracts. It performs no model call, native observation, store mutation,
model execution or public publication. The completion adapter separately sends a bounded advisory reconciliation request to the existing canonical store. The owning milestone remains incomplete until an actual
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
are retained in a separate bounded reference collection excluded from that digest; their usage references remain available and their cost remains in
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
A nonblocking exclusive lock polls only until the caller-supplied monotonic deadline; no new
execution budget is created. Inventory is bounded to 128 state records, 257 directory entries and
4 MiB; each write and read is bounded to 64 KiB. Fsynced atomic replacement and directory fsync
serialize transitions. Capacity or deadline exhaustion refuses the affected operation. Duplicate
schedule/start notifications retain one invocation. One initial snapshot plus two automatic evidence revisions per explicit
subject/epoch are admitted, sharing the budget across native and provisional scopes; further snapshots return an explicit partial refusal without allocating another journal file.
The canonical queue remains the durable authority for that disposition. This does not
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

## Proposed existing-caller protocol

The canonical request kind and state transitions require the store owner's admitted contract; this
protocol describes their necessary join and does not activate a new kind. A request is distinct from
an assessment output: before caller selection it has no model execution provenance or model alias.

1. The existing completion/delivery owner reconciles a missed notification from current outcome
   identities in the existing store. It persists one request for the subject, substantive evidence
   digest and analysis policy. Native scope requires the actual complete admitted population;
   incomplete source delivery uses provisional scope. No observer state is forged to admit a review.
2. The current parent Codex orchestrator reads one bounded canonical pending request and its immutable
   exporter snapshot. It selects the existing runtime route, model/effort, tokenizer and enforceable
   input/output/deadline limits before claiming execution. Unsupported limits, missing root token or
   unavailable provider/exporter become an explicit unavailable disposition. The engine does not
   unconditionally launch a provider and no permanent consumer service is introduced.
3. The canonical compare-and-set claim commits claimed state and
   the selected attempt/dispatch custody before a single invocation. The private journal retains the
   exact packet. For native collaboration the parent uses the existing prospective
   `begin -> spawn_agent -> started` observation route. An existing explicitly selected `codex-exec`
   route instead uses its inherited invocation context and owns its observation. Never use both.
4. The analyst returns bounded private structured output plus a draft existing item review when native
   admission is possible. The parent observes actual termination, records `finish` and reconciles
   completed native usage. Requested model metadata and messages do not prove token totals. The
   parent validates the output before publishing a review through the existing root-token route.
5. An applied receiver/store readback resolves the actual existing admitted item review. Deterministic
   finalization may attach that reference and the observed analyst usage without another model turn.
   Analyst-authored review/usage records are retained separately as bounded `analysisRecords`; they
   resolve admission and accounting references while remaining excluded from the substantive trigger
   digest. Native ready still requires a witnessed outcome epoch; null remains pending/partial.
   A provisional assessment never invokes the native item-review route.
6. Submit the exact validated assessment bytes through the existing enrolled producer ingestion and
   outbox/retry route, preserving the request identity and receiver disposition. Canonical accepted
   state requires supported readback; a durable submission or lost response is not applied. Restart
   reconciles claimed custody and uncertain submission effects before any further invocation. Failed,
   interrupted and malformed analysis retains its usage and an explicit disposition with no automatic
   model retry. Late substantive evidence may request the remaining budgeted revision; analyst-only
   arrivals update deterministic cost and admission joins without recursively scheduling analysis.

The inspected native collaboration tool itself exposes no usage hook, and the inspected runtime
launcher does not establish enforcement of this analyst policy's three numerical limits. Runtime
selection must therefore qualify those bounds rather than assume the prompt enforces them. A request,
private journal, source merge or model response alone never establishes canonical completion or cost.


## Canonical notification and retained input

Successful completed-root drains and later terminal-root observation/usage drains call the enrolled
`telemetry efficiency analysis reconcile` route with the configured store, repository and item.
The adapter gives this advisory subprocess five seconds and an 8 KiB output allowance. A successful
exit reports only that reconciliation returned; it does not claim enqueue, analysis or completion.
The store derives authority and current outcome membership and idempotently retains its selected
request. This hook launches no model. Missing enrollment or endpoint leaves an explicit advisory
failure, with no raw private error or packet copied into adapter output.

The consumer reads `inspect --request-id` and restores `packetBase64` against `rawDigest` before
claiming. Those exact private bytes contain the evidence-packet subject, coverage, omissions and
records; they are never rebuilt from public dashboard data. Each record carries the canonical
reference with its content digest as well as the normalized evidence reference. The store validates
these joins in the snapshot transaction. The current admitted packet codec accepts integer JSON
numbers only; fractional source payloads produce explicit unavailable or omitted/partial coverage.
Metric quantities remain separate. Local restoration detects byte or reference disagreement
but does not establish admission. Explicit enrolled enqueue supplies the packet through a separate
`--packet` file; regular completion notifications use reconciliation instead.

The existing process-review payload does not name an analyst claim or invocation. Reconciliation
therefore defers new analysis while the current request is claimed or unresolved, including when
the root publishes the analyst's draft review. Successful settlement binds the accepted assessment's
exact item-review reference to the genuine claim and runtime invocation. Finalization resolves the
separately published review revision through actual receipt/readback and adds its exact reference;
the retained preclaim review stays legitimate evidence. ID-only finding references spanning multiple
review revisions are refused as ambiguous. A result pointing to a preclaim review does not establish
that the analyst generated it. Subsequent reconciliation
uses that canonical association to exclude the generated review from substantive evidence. Newer
review time, item identity or requested reviewer metadata alone does not establish authorship. Failed
output without an accepted association remains unknown/provisional and cannot cause automatic retry.

## Single-invocation pilot qualification

The selected pilot is measured, with input and output token support marked `observed-only` and a
caller-owned wall deadline. The thresholds remain 8,000 total input tokens, 1,500 total output tokens
and 60 seconds, including required context and non-visible reasoning. Native collaboration exposes
no token-limit or hard termination knob; the inspected Codex exec route exposes structured output
and actual usage events but no output-token cap. A small visible prompt or schema is not a token cap.
Automatic execution remains unavailable wherever those hard token bounds cannot be enforced.

Before root execution, retain one genuine pending request, exact inspect bytes, generated prompt and
full output schema privately. `pilot_input` supplies separate stdin and `--output-schema` bytes
so the schema is not duplicated in the prompt; both still contribute to actual input context. Select and record the actual model/effort and limit support, then claim
with exact revision/digest and prospective dispatch custody. Bind actual invocation only after the
existing observer publishes the genuine start. Use one invocation and no automatic retry. Do not
retrofit an earlier worker or fill unknown epochs, observations or usage from metadata.

The existing runtime launcher waits without a deadline. A pilot therefore requires a qualified
supervisor that uses the same caller deadline for launch, observation and shutdown, records actual
process termination, and can stop the complete local child tree. Sending cancellation alone does not
prove termination. A timeout or lost start remains an unknown effect, consumes budget and cannot
be retried automatically. The helper supplies `run_selected_caller` for one root-selected launcher, with an owned POSIX
process group, finite input/output capture and closure reserve inside the original deadline. Its
synthetic held-child controls qualify local custody only; escaped processes and remote provider
effects remain unknown. Native usage must still be joined separately before settlement. Root reviews the concrete request and supervision route before
execution. Actual complete usage is checked against the unchanged thresholds before result settlement,
and admitted review plus receiver readback remains necessary for any canonical native result.
