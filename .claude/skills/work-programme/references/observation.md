# Dispatch observation


Telemetry is prospectively on by default for repository-owned dispatches. At driver entry, use the installed
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
