# Dispatch observation

The canonical [work-board](../SKILL.md) governs current behavior. Historical pilot/default,
packet/return/index and reporting-cadence instructions below describe retained diagnostic contracts
only: they are optional and cannot restore roadmap scheduling, duplicate current-status/closure
upkeep or an active legacy workflow. Current scope and source/effect admission take precedence.


Telemetry is prospectively on by default for repository-owned dispatches, with bounded best-effort
collection. Missing tokens, a full inbox or unavailable collection do not block otherwise authorized routine
development. Preserve available native events in existing private logs, report the attributed gap once and
reconcile asynchronously where genuine identities permit; never fabricate a begin token or completed-turn
baseline. The begin/started/finish sequence below applies where collection is available. Live-store mutation,
publication recovery and operations whose correctness depends on telemetry state keep their own admission
boundary. Record exact store identity with schema/status evidence; no additional CLI, service or ledger.

When collection is available, at driver entry use the installed
`fsgg-coord-engine skill roadmap-telemetry begin` command
with the feature, item, attempt, selected model and effort. Pass a stable `--original-item` for a root member
when distinct roadmap items belong to one canonical original; otherwise the item is its own original. Bind the
non-self root mapping through the protected `docs/coordination/telemetry-original-item-assignments.json`
registry before dispatch; the adapter verifies the protected GitHub revision. Bind the
native agent id with `started` immediately after every `spawn_agent`. For a returned collaboration task
path such as `/root/worker_name`, pass the final component `worker_name` as the native agent id. The current
reader accepts 1–128 letters, digits, underscores or hyphens and resolves exactly one child whose parent
thread identities match and whose `agent_path` ends with `/worker_name`. `CODEX_THREAD_ID` is a distinct
thread UUID used for the host metadata join; keep it separate from the native agent id. Missing or ambiguous
child matches remain usage gaps. Before every child or `followup_task`, run
another `begin` with the parent's token/attempt and the correct `child` or `follow-up` relation, then bind and
close it the same way. For a follow-up to an existing owner, use that owner's previous dispatch token and
attempt as the parent, so the collector captures its completed-turn baseline and excludes earlier usage.
A missing baseline remains unknown; using the programme root token cannot substitute for that baseline. After each child becomes terminal, run `finish` with its real outcome; close the driver's
root observation before the driver itself returns. Pass feature/item/attempt identities to
`tools/routine-delivery.py` so its CI assignment is discovered and created privately by default. Missing host
configuration or publication remains advisory to delivery but must be reported once as an attributed coverage
gap, never silently omitted.

When local CLR execution is held, the canonical delivery helper accepts
`--telemetry-unavailable local-clr-resource-hold` for otherwise authorized delivery. This explicit
advisory route skips compiled configuration discovery, assignment creation and CI observation, retains
`telemetryHealth: unavailable`, and emits one bounded JSON coverage-gap record to stderr with the exact
repository, PR, head and final delivery outcome. A complete feature/item/attempt trio and optional parent
attempt may be retained as audit identity only; it creates no assignment or authenticated usage join.
The audit input is limited to 2048 bytes. This route rejects watch mode, explicit telemetry assignment,
store, config or engine, publisher event/config/deadline options, and nonempty `FSGG_TELEMETRY_CONFIG`
before native calls. Preserve the stderr record in existing private native logs and reconcile the gap
asynchronously. The flag establishes no missing-config claim or resource admission; ordinary exact-head
checks, coherent validation, merge refusal, native readback and publication boundaries still apply.
Default collection behavior is unchanged.

The native collaboration tool exposes no usage hook. The adapter records expected population, lineage,
requested model/effort and outcome, then joins completed child turns through read-only Codex host metadata when
`CODEX_THREAD_ID` is available. After late usage or a follow-up, run `usage-reconcile` for the affected terminal
attempt. Treat unmatched or incomplete native usage as unknown; hosts without a joinable parent retain
`native-collaboration-usage-unsupported`. Never infer counters from dispatch metadata.

Use the existing runtime/provider usage and CI/operation collection. Preserve original item/attempt,
feature, effective policy, model/effort, triggers, start/queue/end times, useful work/tests, administration,
reruns/repair causes, native outcomes and coverage gaps. Keep raw private evidence in its existing
retention/access boundary. Collect child-final usage after the child actually finishes when supported.
Do not fabricate counters, expose private content or make an agent narrate each event.

The logging completeness and historical thresholds below apply only to a selected telemetry/economic
contract or recorded human-selected policy. Ordinary delivery does not wait for full capture or Unknown
coverage; add no model-maintained threshold ledger or mandatory measurement campaign.
Effects dependent on telemetry state retain their actual admission.

Comprehensive automatic logging is an enablement requirement, not a claim that this skill installs a
collector. Inspect the existing observer's actual fields. In particular, a broad 20% overhead result is
not the narrow 10% bureaucracy measurement. Missing collection or attribution stays unknown: preserve
available native logs, report the wiring gap once and route its repair through the owning work. Observer
loss does not block otherwise valid delivery or prove compliance. Do not reconstruct unavailable usage
or run a model turn merely to poll checks; use the native/helper watcher and bounded machine polling.

Apply unified section 7.4 to usable whole-item measurements, including follow-up corrections:

- Ceiling **10%**, recovery target near **5%**; useful test execution is excluded from bureaucracy.
- Count each distinct item exceeding 10% once per intervention epoch. Good items, retries, new workers,
  new PRs and calendar boundaries do not reset the counter. Do not count startup prefixes as whole items.
- Start one aggressive overhead intervention at **15 cumulative distinct breaches above 10%**, or
  immediately on **any item above 25%**. Exactly 25% alone is not the severe trigger.
- While it is open, fold further breaches into that intervention. Delete or repair the measured causes;
  do not add a new process/report for every breach. Attribute the intervention's own full cost.
- Reset only after the fix is deployed and its claimed reduction is verified. Preserve prior epochs and
  item lineage; repeated observations of old breaches do not launch a new intervention.

If an intervention becomes due when the ready window is exhausted, open the single intervention first
and prioritize its measured causes in the same horizon-planning pass. Use the existing Sol-medium worker
for a bounded repair within its authority. A threshold alone does not summon Astra or another parallel
worker; unaffected valid delivery can continue. Avoid competing feature and overhead replanning loops.

Use the existing mechanical observer where it supports these definitions. Missing support must remain an
explicit implementation gap, not an invented automated counter or a model-maintained receipt ledger.
Missing measurements cannot certify compliance. Substantive feature planning is design work; repeated
administrative replanning is overhead. Technical and authority incidents keep their actual immediate response.

Where the private event-publication receipt has been explicitly activated, successful completed-root and
post-terminal root observation drains request a bounded dashboard refresh through the canonical roadmap adapter.
Treat its health as advisory and report it without delaying delivery; a failed or unchanged refresh does not alter
the recorded outcome. This event path makes no daemon or recurring-service assumption.

## Native context diagnostics

When native session JSONL is available, use `scripts/context.py report` under the
[context contract](context.md). Select and pin only relevant parent/worker records and an explicit UTC
window; keep raw records private. Native compaction events and token counters are session observations,
not estimates from bytes. Expose missing baselines, counter resets, omitted sessions and incomplete tails.
Cached input is still context; cumulative input counts repeated inference history. This read-only projection
does not repair observation tokens, reconcile usage or establish authenticated item/dispatch attribution.
Keep the original collector gap alongside these available diagnostics; do not label all native context
observations unknown merely because the separate item-attribution path is unavailable.

## Operational collection boundary

Readiness is not collection health. For an admitted useful owner window, preserve the
prospective begin/start lineage; record actual open/closing activity revisions at their
observed times, then finish and bounded usage-reconcile after terminal. Missing tokens or
pre-dispatch completed-turn baselines remain UNKNOWN; never substitute the programme root.
Activity/review/complication evidence uses closed `{kind,digest}` objects, not strings.
Attempt reviews require an admitted terminal invocation; item reviews require the complete
settled expected population. Usage repeats exact native identities/counters once, never
apportioned by elapsed time. Use the necessary exact-head delivery for CI collection.

The prepared adapter canonically parses a new full batch before durable publication intent.
A retained malformed intent stays immutable unless a separately qualified exact local
engine returns its closed pre-publication-IO parser rejection bound to the original payload,
destination and executable identity. Unknown/transport/foreign failures retain pending bytes;
never clear state manually, forge a receipt or change identity to evade the hold. Root admits
disposable proof, exact retained recovery and installation independently of source preparation.

Report each metric's selected population/window/source cutoff, event/observation/receipt
clocks, report time, lineage/attribution coverage and omissions. Historical gaps, zero rows,
configready, helper success and updated dashboards cannot certify operational recovery or
cost/efficiency compliance. See `docs/reference/local-telemetry-store.md` for the existing
commands and concrete recipe. No automatic reviewer, provider interception or backfill follows.

## Finite original-operation attention

The additive `observe-operation` source route observes one explicitly admitted, already-running
GitHub Actions run and attempt. Use it only after its source, coherent publication, physically
verified installed caller and instruction discovery have been qualified for the selected scope.
Source availability alone does not activate the route. Root retains operation/resource admission,
Status reconciliation and full acceptance. The observer performs bodyless REST GETs; it does not
schedule, dispatch, retry, cancel, release reservations, mutate planning or evaluate merge readiness.

Prepare one private `fsgg.operation-observation-input/1` JSON object with exactly `schema`, `identity`
and `seconds` (1–600). `identity` has exactly `issue` (`OWNER/REPO#NUMBER`), `slice`, `attempt`, `owner`,
`sourceRevision` (40 lowercase hex), `issueUpdatedAt` (exact native value), `issueBodySha256` (SHA256
of the exact UTF8 native body), `repository` (`OWNER/REPO`), `pullRequest`, `head` (40 lowercase hex),
`runId`, `runAttempt`, `populationRevision` and `unknowns` (the retained original Unknown identifiers).
These are attributed root inputs, never authentication or a new execution grant. Preserve all original
effects and reservations, including obligations outside the selected run. Missing identity is a gap;
do not reconstruct unavailable originals or replace them with a fresh attempt.

Invoke the genuine qualified installed caller once for the admitted attention window:

```bash
fsgg-coord-engine observe-operation start --input /PRIVATE/operation-input.json --cursor /PRIVATE/original-observer.json
```

The private parent directory must already exist and remain in the original operation's custody.
`GITHUB_TOKEN`, falling back to `GH_TOKEN`, supplies read credentials. The live adapter uses
`https://api.github.com`; other hosts and non-Linux clock continuation are unsupported. It binds the
owning issue's native body digest/update revision before and after each sample, PR head barriers,
selected run/attempt barriers, two complete job-population sweeps, and every job's original run/attempt/head. Empty or incomplete jobs,
ambiguous native PR association, count drift, missing continuation, duplicate fields/identities and
unsupported native states produce attention rather than success. Jobs are the complete selected-run
population, at most128 across two explicit100-item pages; this is not all PR checks or required checks.

The invocation retains pending observations internally and prints one JSON result, at most4096bytes,
when a terminal observation, PR control change, identity/population drift, native-read gap, interruption
or deadline requires attention. Unchanged ticks and ordinary pending job progress cause no model callback or stdout record. The result
contains the original PR/head/run/attempt, stable attention identity, semantic revision, remaining
request budget, retained Unknown count and private cursor/evidence references. `completed` describes
native selected-run/job observations only. Readiness remains `not-evaluated`; no observation releases
Unknowns or establishes source/publication/installed/native acceptance.

The original window is at most600seconds including a500ms cleanup reserve, at most384 requests,
30seconds per request and4MiB total accepted evidence/result output. Each accepted response page is
at most256KiB before parsing; the reused transport may transiently acquire up to4MiB before this
adapter rejects an oversized page. This differs from the Python watcher's256KiB acquisition bound.
Ordinary samples cost eight GETs for up to100jobs, or ten for101–128jobs, with a maximum5second
internal wait. Owning issue revisions and source timestamps are not renewed by local observation.
Filesystem calls and the hosting process are not a separately qualified process-tree supervisor.

Resume the same cursor and exact input after interruption:

```bash
fsgg-coord-engine observe-operation resume --input /PRIVATE/operation-input.json --cursor /PRIVATE/original-observer.json
```

The checksum-protected cursor retains Linux boot/tick clock identity, original absolute work/cleanup
deadlines, remaining request/output budgets, population and semantic revision. Its checksum detects
accidental corruption, not hostile modification or authority. Missing, corrupt, changed-clock,
identity-drifted or expired custody refuses before native IO. Every request is charged and persisted
before sending; a crash can lose allowance but cannot renew it. Terminal attention replays its stable
identity within the original window. This does not promise exactly-once crash delivery. An attention is terminal for its original invocation; resume replays that result. A new
`start` refuses an existing cursor. Never delete custody to bypass expiry or a refusal.

Private files are created with owner read/write permissions and one process holds the cursor lock.
The cursor is replaced through a flushed temporary file; directory durability and disk-loss recovery
are not established by this source. On cancellation, an in-flight read may consume its remaining
bounded allowance before the interruption result. A hard-killed observer leaves the original cursor,
Unknowns and reservations intact. Root reconciles the original operation independently.

Rollback keeps the existing `tools/routine-delivery.py --watch-checks` route for an independently
admitted remaining observation. That helper cannot consume this cursor or renew its deadline. After
an observer failure, retain its original custody and use fresh bounded native readback; do not start
a fresh600second watcher for the same expired window. Report actual parent/worker/reviewer usage,
coverage omissions and native request costs. Zero unchanged-tick model invocations follows from the
finite code path; measured whole-family savings and installed acceptance require their own evidence.
