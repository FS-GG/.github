# UTEL-DASH — Public telemetry dashboard

Owner: `.github`

Backlink: [Unified Development Roadmap v3](2026-10-07-unified-development-roadmap-v3.md) — V2-EFF-01.2–.6

Roadmap position: **V2-EFF-01.2–.6 — telemetry, dashboard and receiver owners**

## Outcome

Publish a useful, privacy-bounded GitHub Pages dashboard for FS-GG delivery telemetry. Public GitHub Actions and merged-delivery data remain useful on their own. An approved private host may add closed aggregates and bounded item detail only through explicit public aliases, category mappings, links and notes; missing or stale host evidence stays visible and never becomes zero or compliant.

## Delivered source repair — 2026-10-06

[PR4259](https://github.com/FS-GG/.github/pull/4259) merged at
`da4c66c76b9a5e9cc7caf6041001cafefd95a138` after exact-head coherent qualification.
The canonical snapshot reader now accepts store schemas 8–13; label materialization and
same-version two-store aggregation accept 10–13. Unknown schema 14, mixed store versions,
incomplete snapshots and overlapping identities remain refused. Five additional public PR
selectors describe eligible delivered work without changing canonical completion or runtime
coverage gates. All 83 pure dashboard tests, six actual two-UID privacy checks and the hosted
dashboard/path-coherence checks passed. Milestone projections now trigger the existing Pages refresh.

The earlier manual Pages refresh restored recent merged-delivery entries. The merge-triggered
[Pages run 37419167012](https://github.com/FS-GG/.github/actions/runs/37419167012) then passed;
HTTP 200 readback bound the deployed source to `da4c66c` and retained the same four completed
cards and host revision. The active publisher remains `main-legacy`; the current store
has not replaced its feed. The genuine legacy canonical source and publisher access are still
unresolved, so a supported aggregate and newly published completed rows remain pending.
Partial or unknown runtime usage stays explicit; source delivery does not establish live feed
repair, publisher cutover or whole-feature completion.

## Current operation — 2026-09-21

Private completed-item proof combines native delivery with schema-10 terminal, usage, and lineage readback and requires zero unexplained gaps. The protected public-selector registry is default-deny: only explicitly approved selectors that resolve to a completed delivered canonical item can authorize a public row. An approved public row aggregates token usage across all genuine attempts for that item, including retries and follow-ups; missing usage remains an explicit coverage gap. See the [publication handoff](../reference/telemetry-publication-handoff.md#approved-label-rotation-after-cutover).

The milestones below retain their historical statements as evidence recorded through 2026-09-09, including schema-7/schema-8 references and the then-observed zero eligible published completed items. They do not describe the current schema or public population.

## Milestones

- [x] **UTEL-DASH-01 — Safe aggregate feed.** The one-shot host adapter discovers only the canonical approved configuration, calls the engine's read-only public export, reconciliation, CI, budget, health and store projections, and removes item, invocation, provider, model, scope, epoch and free-text identities. It publishes a closed sub-1-MiB snapshot to the dedicated `telemetry-data` branch with an optimistic non-force ref update; conflict or collection failure retains the last good commit. Dry-run output is inspectable.
- [x] **UTEL-DASH-02 — Interactive responsive dashboard.** The static local-asset site shows source freshness and exact provenance, a capped recent Actions sample, workflow health, outcomes, durations and a searchable run table. It renders host usage, coverage and canonical budget state when present, with accessible text equivalents, keyboard controls, mobile layouts, light/dark themes and explicit empty/malformed/stale states.
- [x] **UTEL-DASH-03 — Automatic public refresh and routine source delivery.** A least-privilege Pages workflow tests candidate source without deployment, refreshes up to 1,000 deduplicated newest public runs on main/schedule/manual events, binds the source and immutable host-data revisions, and deploys only the static artifact. The Actions pages form a bounded, timestamp-fenced, multi-page sample of latest observed run attempts rather than a complete historical inventory.
- [x] **UTEL-DASH-04 — Deployed dashboard and host feed activation.** The approved physical host uses the exact merged engine and an empty privacy-conservative alias allowlist. Its digest-bound setup published and immutably verified the initial feed, installed a host-native marker-owned user timer, and authorized the closed event receipt without placing credentials in either unit or receipt. The timer is enabled and active and its first recurrence completed successfully. A real completed-root terminal hook then published and immutably verified [public feed commit `2edf246…`](https://github.com/FS-GG/.github/commit/2edf24678840c47a7a8a1306cf9947755007d07d); [Pages run `34355973095`](https://github.com/FS-GG/.github/actions/runs/34355973095) succeeded, and an independent HTTP 200 browser read bound the deployed `host/3` projection to exact source `209410f…`, that host commit and public revision `851ab857…`. No replacement store was initialized, and publication failure remains advisory to primary delivery.
- [x] **UTEL-DASH-05 — Safe completed-item and merged-delivery projections.** A bounded schema-7 adapter reads only the exact configured store in a query-only WAL transaction. It requires the latest native delivered outcome, the canonical derived `completed` population and no pending dirty reduction, groups attempts and follow-ups by original item, and publishes only explicitly approved aliases, categories, repositories, evidence links and notes. A separate bounded public scan lists merged pull requests as delivery evidence without treating them as completed items or effort.
- [x] **UTEL-DASH-06 — Completed-item explorer.** The dashboard provides a searchable, sortable, deep-linked item list with role-level invocation spans, completed-turn tokens by approved requested/observed model, effort and accounting scope, separate CI measurements, reducer-owned budget assessments and observed complication signals. Unknown and known-partial coverage remain visible; older snapshots state that activity phases, causes, repair time and repair tokens are unavailable rather than inferring them.
- [x] **UTEL-DASH-07 — Item feed refresh and routine source delivery.** The least-privilege Pages workflow collects the bounded merged-delivery feed beside Actions, composes the versioned dashboard contract, and keeps deployment behind the native main boundary. Source privacy, projection, malformed-state, desktop and mobile checks bind the routine exact head.
- [x] **UTEL-DASH-08 — Versioned coherent engine detail.** [#3381](https://github.com/FS-GG/.github/pull/3381) first consumed schema-8 activity, attribution, typed complication and process-review detail safely. The completed source window preserves `item-detail/1` and adds explicit `/2` item or bulk selection with bounded completion, dirty state, native outcomes, invocation timing and clock provenance, exact provider/accounting-scope usage, CI, budget and process inputs from one read-only connection and explicit WAL transaction. A canonical private revision excludes observation time and separately labelled inbox state; the engine supplies a gzip representation of at most four MiB of exact canonical bytes as the sole snapshot representation, while the wire envelope remains bounded to one MiB. A differently implemented adapter verifies rather than recreates the digest serialization, limits decompression, and does not duplicate the private payload. Deterministic concurrent-correction, non-ASCII/escaping/numeric-shape, real-size envelope and stable/change-sensitive revision tests provide source acceptance. Exact native turn rows retain invocation identity inside the private boundary so the public projection can deduplicate and join the complete root/child/follow-up population.
- [x] **UTEL-DASH-09 — Present process detail through the engine-only projection.** The completed-item drilldown continues to show overlapping activity spans, exact attribution, typed complications and safe review metadata, but the host adapter now makes one `/2 --all` engine call and contains no SQLite import, SQL, table names or migration-version policy. Privacy filtering derives and validates a separate public-payload revision without publishing the private snapshot digest. It sums an item total only when every expected native dispatch has one valid lineage row, the same invocation population is admitted, started and terminal, every invocation has usage, no runtime gap exists, and all rows share one compatible accounting basis. Otherwise compatible observed subtotals remain visible while the total is `not-proven`, with an explicit coverage boundary and unknown-remainder flag. Schema-8, scoped usage, complete and incomplete nested lineage, completion/dirty, malformed/bounded input and recursive privacy tests provide source acceptance.
- [x] **UTEL-DASH-10 — Safe publisher setup entry point.** `publisher-setup` defaults to validation and preview with zero network, credential or systemd effects; it requires exact-`0600` non-symlink labels and reports counts, public revision, destination and planned actions. Install-only writes marker-owned inert user units idempotently and refuses conflicts or active replacement. The unit pins the resolved absolute engine and explicitly selects bounded environment-or-`gh auth` credential acquisition without embedding a token. Activation is separately selected and bound to the exact label digest; it publishes once, verifies whitespace-wrapped GitHub base64 as immutable file bytes, public revision and current branch ref, and only then attempts recurrence. A host without a user systemd bus retains the verified publication result and reports recurrence as unavailable rather than claiming activation. Fake-transport and temporary-systemd tests provide source acceptance. Actual recurring operation and browser verification remain UTEL-DASH-04.
- [x] **UTEL-DASH-11 — Runtime coverage and wrapped transport repair.** Completed-item coverage retains every engine-reported expected runtime dispatch, including `collaboration-spawn-agent`, in the population denominator. It reports `complete`, `partial`, or `unknown` from exact usage evidence; unsupported runtimes remain unknown, and only a fully linked, admitted, started, terminal, gap-free population under one compatible accounting basis proves a complete total. The UI labels aggregate usage as observed and exposes item coverage in each summary without turning a missing remainder into zero. The bounded decoder accepts a fully sized one-MiB payload with ASCII line wrapping while rejecting invalid alphabet, decoded oversize, and excessive transport bytes. Mixed-runtime, zero-usage, partial, complete, ambiguity, legacy payload, browser, wrapping and size regressions provide source evidence. The live feed retains zero eligible published completed items and unsupported collaboration usage as explicit coverage gaps.
- [x] **UTEL-DASH-12 — Private event-publication activation.** The setup path can create a fixed private exact-`0600`, non-symlink, closed activation receipt only under the additional `--authorize-event-publication` flag and only after verified initial publication. It binds the exact config bytes, resolved engine, label file and digest, destination, and credential source without retaining a token. Existing systemd activation does not silently authorize this mode; preview remains effect-free and changed bindings refuse before effects.
- [x] **UTEL-DASH-13 — Bounded advisory publication after telemetry events.** `publisher-event` revalidates the receipt and bound private inputs under bounded local serialization, builds one engine-owned snapshot and validates the current public host/3 payload. It preserves the current observation time for semantic comparison, so observation time alone makes no GitHub write; a meaningful change uses the existing optimistic non-force publish and immutable verification. Completed-root terminal drains and successful post-terminal root observation drains request this refresh through the canonical adapter. Missing activation, contention and failures are advisory, privacy-safe health and never delay or alter primary delivery. Source acceptance uses fixtures only; actual receipt activation, observed event publication and browser read are recorded under UTEL-DASH-04.
- [x] **UTEL-DASH-14 — Automatic browser and public-source refresh.** An open dashboard fetches its same-origin bounded snapshot immediately and approximately every minute while visible, with an overdue check on visibility return, one timeout-bounded request and no overlap. A candidate replaces application state only after render-required nested validation; failure retains the last good snapshot and reports browser-check time separately from source observation age. Refresh preserves filters, sorting, expansion, deep links, focus, scroll and theme, while canonical removals and unavailable host state clear old rows and cards. The Pages workflow retains pull-request verification plus existing push and manual deployment paths and moves its scheduled public-source collection to GitHub's five-minute minimum at an offset; queue, build, deployment and cache time remain outside that best-effort cadence.
- [ ] **UTEL-DASH-15 — Source delivered with operational completion unestablished.** Prepare a separate approved native-source delivery view in `dashboard-host/4`. Latest delivered outcomes may be visible while their original group remains open; these rows do not enter completed-item counts or the public-item registry. Publish only approved labels, repository-scoped PR links and observed delivery timestamps, with no inferred runtime, usage or cost. Withhold dirty or ambiguous groups and superseded outcomes. Keep `/1`–`/3` consumer compatibility and public revision binding. Land compatible consumers before any `/4` producer activation; qualify a coherent successor release and explicitly adopt its immutable installed exporter and activation digest. Source preparation alone does not establish release, installation or public adoption.

The UTEL-DASH-15 source candidate passed 99 Python tests, a genuine two-UID handoff with all six isolation checks, and all 13 Chromium browser tests, including incomplete-accounting rows, malformed-feed fallback, keyboard access and mobile layout. The initial browser qualification stopped at a mismatched executable-size bound; that failed attempt remains retained. The corrected bounded run passed with actual Python, Node and Chromium mapping observations and clean process retirement. These checks qualify the source candidate; producer release and installed public adoption remain open.


## Metric semantics and privacy boundary

Token input includes cached and cache-write components, so the UI does not add those components to input. Aggregates spanning incompatible provider/accounting scopes are not presented as priced or provider-comparable usage. All-time host totals ignore dashboard date filtering because the safe host payload contains no item dates. Local CI runner seconds sum jobs; wall, queue and category values are summed per-item union projections, not fleet wall time and not a partition of runner seconds. Native public Actions duration uses only supported start/update timestamps and has no local item, bureaucracy, queue or runner attribution.

The canonical reducer alone owns budget verdicts. The dashboard states the 5% target, more-than-10% ceiling, 15-distinct-item or any-more-than-25% intervention trigger, useful-test exclusion and deployed-plus-verified reset rule. Unknown dimensions remain unknown. Model, effort, price and human-time breakdowns stay absent when the host cannot provide them.

No raw log, SQLite store, WAL, assignment, source identity, private item/invocation/thread identity, provider/model/scope/epoch identity, diagnostic text or credential is written to GitHub. Item labels and keys, category aliases, FS-GG repository links, and bounded repair or complication notes appear only when the operator explicitly approves them in the private label file. The host observation timestamp is part of the immutable host snapshot and is not reset by a Pages build.

The schema-8 engine owns the versioned dashboard read model. The source-local host adapter consumes only `fsgg.telemetry.item-detail/2`; it never opens the store or owns SQL, tables, migrations or native SQLite compatibility. The engine bounds selected items, each relation and serialized bytes, and refuses incompatible or incomplete reads without initialization, migration, drain or repair.

<a id="producer-handoff-missing-activity-and-repair-evidence"></a>

### Producer detail contract and remaining handoff

The engine preserves `fsgg.telemetry.item-detail/1` for existing callers. `/2` provides the complete bounded dashboard selection in one snapshot, including the private identifiers, summaries, review prose, evidence and digests needed for the engine-owned join. The host adapter selects only the public fields named above. Native total zero does not establish zero cost or complete capture.

The `/2` engine export removes the adapter's former unchanged-table reads by supplying completion, invocation spans, scoped native usage and a snapshot revision together. Activity stage stays orthogonal to bureaucracy classification: repair or review does not automatically mean administrative overhead, useful validation remains excluded, and no model turn inherits a whole activity or cost category from its parent role. This dashboard change adds no schema migration or duplicate completion reducer.

## Host activation recipe

Do not activate until `~/.config/fs-gg/telemetry.json` (or `FSGG_TELEMETRY_CONFIG`) names the operator-approved existing store and installed engine and an operator has approved the exact label digest. Keep the token environment file mode `0600`.

Preview without credentials, publication or service effects:

```sh
python3 tools/telemetry-dashboard.py publisher-setup --labels /absolute/private/telemetry-dashboard-labels.json
```

`--install-only` may then install the reported inert user units. Recurring activation additionally requires `--activate --approve-labels <sha256> --authorize-recurring-publication`; it verifies the initial immutable publication before enabling the timer. Add `--authorize-event-publication` to that exact activation only when the operator also authorizes event refreshes. The latter creates `telemetry-dashboard-event-activation.json` beside the discovered config after verification; the fixed closed receipt has schema `fsgg.telemetry.dashboard-event-activation/1` and fields `configDigest`, `engine`, `labelsPath`, `labelsDigest`, `destination` (`repository`, `branch`, `path`) and `credentialSource`. It contains no token. A changed config, engine, label file or destination invalidates/refuses the activation. Source delivery never runs these operational modes.

```ini
# ~/.config/systemd/user/fsgg-telemetry-dashboard.service
[Unit]
Description=Publish the FS-GG safe telemetry dashboard snapshot

[Service]
Type=oneshot
ExecStart=/usr/bin/python3 /private/persistent/releases/SOURCE/tools/telemetry-dashboard.py host-snapshot --config /private/persistent/host/telemetry.json --producer-executable /private/persistent/bin/fsgg-telemetry-engine --labels /private/persistent/shared/telemetry-dashboard-labels.json --credential-source environment-or-gh-auth --repo FS-GG/.github --branch telemetry-data --path host.json --output %t/fsgg-telemetry-dashboard-host.json
```

```ini
# ~/.config/systemd/user/fsgg-telemetry-dashboard.timer
[Unit]
Description=Refresh the FS-GG safe telemetry dashboard snapshot

[Timer]
OnBootSec=3m
OnUnitActiveSec=15m
Persistent=true
Unit=fsgg-telemetry-dashboard.service

[Install]
WantedBy=timers.target
```

The setup command installs equivalent definitions idempotently and resolves the configured engine, tool and Python executable to absolute paths. Credential acquisition is bounded to an inherited environment token or `gh auth`; neither the token nor an environment file is embedded in the unit. The credential must grant only repository contents write. A conflict or failed publication leaves the previous branch commit intact, and failed immutable verification leaves recurrence inactive.

The label file must be a regular non-symlink file with mode `0600`. Its closed `fsgg.telemetry.dashboard-labels/1` object contains `items`, `models`, `efforts`, and `scopes`. Each private original item maps to a unique public key, label, FS-GG evidence URL, approved FS-GG repositories, and bounded notes whose `evidenceUrl` is public. Scope keys combine the private provider and accounting scope as `provider|accounting-scope`; public scope aliases must be unique. Unmapped evidence is counted but private values are never emitted.

### Host-native recreation inventory

The permanent installation must live in a private persistent host root, not a container overlay. It consists of one immutable directory named by the exact merged source revision (published engine, dashboard tool and its defaults helper), a stable engine wrapper, exact-`0600` labels, separate exact-`0600` host/container configs, and the config-adjacent event activation receipt, health file and serialization lock. The host user-manager owns the marker-generated service and timer plus the `timers.target.wants` link. The snapshot written below the user runtime directory is disposable output, not durable state.

The host requires a working user systemd manager, Python, the .NET runtime used by the pinned engine, and an authenticated `gh` CLI/keyring session. No token belongs in GitHub, a unit, a receipt or a command line. A maintenance container may bind the private root and user bus, but the permanent unit must use host-visible absolute paths and must never call `podman exec` or depend on a container name.

Recreation copies or restores the immutable release and private inputs into the persistent root, runs `publisher-setup` in preview mode, binds approval to the reported label digest, then runs the separately authorized activation with recurring and event publication enabled. Verification requires `systemctl --user is-enabled` and `is-active` for the timer, a successful oneshot result, immutable branch/file/revision verification, and an HTTP 200 Pages read whose source and host revisions match the deployed artifacts. To revoke publication, disable the timer, remove only marker-owned unit files and their wants link, reload the user manager, and remove the event activation receipt. Preserve the private store and immutable evidence unless a separate retention decision explicitly authorizes their deletion.

## Verification

`tests/telemetry-dashboard` covers the closed nested schemas, identity/free-text sentinels, numeric/time/enum/byte refusal, unknown versus zero, canonical budget boundary consumption, epoch handling, completion revision and dirty-state gates, grouped deliveries, mixed-runtime population and usage coverage, role/time/token boundaries, event receipt safety and binding, semantic no-op suppression, bounded contention, concurrent ref conflict, wrapped base64 transport, page drift deduplication and browser populated/empty/malformed/XSS/keyboard/mobile/chart-equivalent behavior. Deterministic browser-clock fixtures cover repeated changed and unchanged snapshots, nested rejection with last-good retention, timeout and recovery, visible/hidden lifecycle, non-overlap and interaction-state preservation. Source checks and synthetic browser fixtures are separate from real-data evidence. The public collectors supply current live Actions and merged-delivery observations. Exact-host adoption, recurring publication, event receipt activation, a real terminal-triggered verified publication and the matching deployed-page readback supply UTEL-DASH-04's operational evidence.


## Current publisher migration — 2026-10-06

The installed producer's raw snapshot exceeded its 10,000-row CI-step bound on the current
canonical store. The compact `/3` successor retains the existing 4 MiB canonical and 1 MiB
envelope bounds while deriving CI summaries in the same read transaction. Raw CI runs remain
available for complication counts; `/2` remains unchanged. Native qualification passed all
171 tests, including transaction consistency and row/byte limits. The Python consumer passed
91 tests, and its exact source passed six genuine producer/publisher UID-isolation checks.

The selected destination is `telemetry-data-current/host.json`. The legacy store is not a
migration dependency. Coherent 0.98.0 publication is verified, and the existing installation's
payload and runtime closure are accepted without a global switch. The backed-up canonical
store now uses schema 13 with all 55 tables preserved. Supported Governance #444 correction
joins 204 targets to GOV-423-C3, preserves the original raw facts and all 144 native outcomes,
and verifies one audit entry and an idempotent retry. These are local migration and correction
receipts, not public dashboard adoption.

The supported drain accepted both pending spool batches, with no replay or quarantine, and
verified zero remaining in 2.857 seconds with clean custody and no resource or storage failure.
A subsequent empty drain verified zero accepted, replayed, quarantined and remaining batches.
The settled schema 13 sanitized export has five eligible approved completed groups, zero pending
batches and no dirty items. [Feed commit `279f195…`](https://github.com/FS-GG/.github/commit/279f19561559238b49bed73f5956cd9881296f59)
verified immutable bytes, payload revision and the current `telemetry-data-current` branch.
The 35,759-byte payload has SHA256 `7b92ceaac1ae997307116533a45ce73bf10d2e8c80cb944c653ec5bfa5f797de`
and public revision `4b2708f0ec95012121da3b77304682a225d15f5fa9bbc448f8a48985fe2b015a`.
Governance runtime coverage remains unknown, so the corrected CI attribution does not itself
establish an eligible completed group. Activation subsequently verified [feed commit `7a4f81d…`](https://github.com/FS-GG/.github/commit/7a4f81d15a5d1cbb765ce78d8a8e5f4bfc13c2a5)
at public revision `3a65e4c90b6ed798ca43368f7dfb387ae3aaf3cbe646fa8e8ce23a490711a7d3`
and authorized the event publisher, with six approved labels and five eligible published groups.
It settled in 8.000 seconds with clean custody and no resource failure. User units are installed
inert; timer recurrence is unavailable because the user bus is absent.

[Selector PR #4266](https://github.com/FS-GG/.github/pull/4266) merged at
`d97ad7eb485979afbc4fabfe8282971d8c4261c9`. [Pages run 37447862730](https://github.com/FS-GG/.github/actions/runs/37447862730)
passed test, build and deployment. HTTP 200 deployed readback at 10:15:51 UTC on 2026-10-06
joined the exact source, feed commit `7a4f81d…`, public revision `3a65e4c…` and immutable host
payload, with five published/eligible groups and zero dirty or unmapped groups. The current
canonical-feed migration and deployed page are accepted; the legacy cards are no longer the
selected feed.

The active event receipt refreshes publication after completed-root skill-finish/postterminal
records. CI reconciliation does not emit those events, so CI-only updates are not automatically
published through this path. The supported CI-to-event refresh proposal remains pending.
The existing Pages schedule collects public sources on GitHub's best-effort cadence; it does not
repair missing producer events. Timer recurrence without a user bus and complete Governance
runtime coverage remain open. This migration does not close those limits or whole UTEL.

## Development process health dashboard — proposed 2026-10-12

Status: user-approved product direction; proposed implementation, not delivered behavior.

Owner: existing telemetry, dashboard and receiver owners under V2-EFF-01.2–.6.

### Purpose

Answer whether development is healthy or pathological, how much effort reaches accepted outcomes, how much is coordination or repeated work, and what evidence explains anomalies. The dashboard supports diagnosis and intervention rather than treating activity, green checks or merged source as sufficient evidence of progress.

The user prioritizes slow delivery, repeated rework, excessive coordination, execution cost and unreliable results, in that order. A busy programme with passing tests may still receive a Pathological verdict when accepted delivery stalls and repeated preparation dominates. Conversely, a difficult feature or justified validation is not pathological merely because it is slow or expensive.

### Opening screen

1. **Overall verdict:** Healthy, Strained, Pathological or Insufficient evidence. Show confidence, observation freshness, coverage, changes since the comparison window and the evidence responsible for the verdict. Do not average dimensions into a score that conceals a serious problem.
2. **Delivery:** accepted outcomes alongside unfinished work accumulating, aging work and time between meaningful acceptance stages. Distinguish source delivery, publication, installation, adoption and verified operational completion where applicable. Retain useful intermediate milestones without counting them as whole outcomes.
3. **Effort breakdown:** the seven categories below, with their measurement basis and unknown remainder. Show elapsed delay, agent-hours and compute cost separately.
4. **Top five anomalies:** ranked by impact on delivery, with Watch or Investigate severity. Make the remaining anomalies searchable; do not turn the opening screen into an unbounded inbox.
5. **Recommended interventions:** each links to its evidence, expected benefit, tradeoff, confidence and an observable criterion for success.

Agent activity appears below these diagnostic views. Empty, malformed, unsupported, partial and stale data have explicit states. A refreshed browser must not make old source observations appear current.

### Effort and measurement semantics

Use these primary effort categories:

| Category | Meaning |
| --- | --- |
| Product implementation | Work that changes or prepares the intended product outcome. |
| Necessary validation | Validation with an evidenced relationship to a relevant risk or acceptance obligation. |
| Tooling and infrastructure repair | Repairing the development or execution capability needed to do the work. |
| Coordination and approvals | Handoffs, scheduling, review coordination and authorization work. |
| Repeated work | Repetition caused by failed attempts, lost context, reopened decisions or invalidated preparation. |
| External waiting | Delay awaiting CI capacity, services, dependencies or another external condition. |
| Unknown or unattributed | Effort or delay that cannot be assigned defensibly from available observations. |

Keep primary categories distinct from activity labels and causal tags. For example, a repeated validation attempt can retain its validation activity, repetition cause and policy-required flag without charging its cost twice. Version classifications and retain their supporting observations, uncertainty and correction history. Do not infer bureaucracy from an agent's role or a step's name alone.

**Required by policy** is a separate attribute, not an exemption from scrutiny. Flag duplicated, disproportionate or poorly justified required checks when evidence supports that diagnosis. Recommendations may propose a policy review, but they do not authorize skipping existing checks or alter canonical budget/completion verdicts. Any eventual change to accepted policy or reducer semantics needs its normal implementation and validation route.

Show elapsed time and active effort separately. Ten agents active for an hour can contribute ten observed agent-hours while the elapsed interval is one hour; neither is automatically ten compute-hours. Use actual supported measurement semantics and label invocation spans, waiting, token usage and monetary cost by their respective basis. Missing activity instrumentation is not evidence of inactivity. Unknown prices, incompatible accounting scopes and incomplete lineage must not become fabricated cost totals.

For parallel work, distinguish aggregate effort from the elapsed union of observed intervals. Do not sum overlapping durations into programme wall time or imply that category delay totals form an exclusive partition when they overlap. Queue delay can be low-cost but high-impact; surface both facts.

### Health assessment and anomaly detection

Expose separate delivery, churn, coordination, cost and reliability indicators beneath the overall verdict. Explain which indicators drive the verdict and why confidence is sufficient or insufficient. A favorable measured subset cannot conceal a material unknown remainder or stale evidence. Conversely, absence of evidence cannot prove pathology.

Compare observations against the same programme's recent history, comparable completed work and explicit targets. Show baseline population, window, sample size and relevant scope differences. Version the assessment rules and keep configurable thresholds inspectable. Initial numeric thresholds are a calibration task, not facts already established by this proposal.

Detect at least these patterns:

- Delivery stalls despite continuing activity; unfinished work grows or ages without accepted outcomes.
- Repeated failures, repeated preparation refusals or repeated attempts that do not resolve a common cause.
- Re-review of unchanged source, equivalent artifact rebuilds and repeated qualification without an evidenced invalidation reason.
- Reopened decisions, excessive handoffs, context recovery and work repeatedly redirected before delivery.
- Coordination or execution-tool repair consuming disproportionate effort relative to product work and accepted outcomes.
- Passing checks or merged source followed by missing publication, adoption or operational verification.
- Cost growth without corresponding delivery, with coverage and accounting limitations made explicit.
- Stale statuses, missing events, incomplete usage attribution and contradictory reported/native outcomes.

Necessary retries and independent qualification remain visible with their justification. Detect a suspicious repetition without automatically declaring it waste. Distinguish observations from causal hypotheses and provide confidence for the latter.

Use **Watch** for early signals and **Investigate** for sustained or well-supported problems. Group repeated notifications for the same incident. Rank primarily by delivery impact, using the agreed priorities as an explicit tie-breaker rather than presenting an opaque score.

### Overhead chain and investigation

The overhead chain is the default drill-down for stalled work and accepted outcomes. Present a timeline with parallel lanes and a compact effort/delay breakdown. Link implementation, validation, source delivery, publication, adoption and final acceptance through genuine recorded identities and dependencies.

Include original attempts, failures, retries, reviews, handoffs, preparation, waiting and outstanding obligations. Highlight repeated steps and show whether their inputs, outputs, source or acceptance basis changed. Preserve original failure and operation identities; do not infer a retry, causal dependency or owner transfer solely from nearby timestamps or similar text.

An anomaly opens in this order:

1. What happened and which outcome is affected.
2. Measured effort and elapsed delay, including unknown portions.
3. Repeated steps and relevant input/output changes.
4. Suspected cause, alternatives and confidence.
5. Supporting evidence and the complete chronological chain.
6. Possible intervention and how to evaluate it.

The deepest view exposes events, attempts, agents, tools, source changes, checks, resource usage, handoffs and original evidence links. Raw logs are one additional action away, within the private access boundary. Name affected components and process rules; do not assign personal blame where the cause is systemic or uncertain.

Support both **what was known then** and **current interpretation**. Retain the earlier evidence cutoff, rule/configuration version, verdict and coverage rather than silently rewriting the historical assessment. Corrections can improve the current interpretation without inventing historical knowledge.

### Views, controls and notifications

- Default to the last 24 hours, with current-session, seven-day and whole-programme windows and the preceding comparable period. Label session identity and timezone.
- Filter by programme, repository, outcome, lane, agent, tool, phase and time. Opening an anomaly applies its relevant filters visibly, with a clear reset. Retain deep links and normal interaction state during refresh.
- Keep thresholds, baseline selection and classification adjustments editable through an authorized configuration path. Record the actor, old/new values, timestamp, reason and assessment version; show how a change affects the verdict. Preserve raw observations.
- Start with advisory recommendations. No automatic pausing, reprioritization, budget changes, release approvals or policy bypasses. Any later operational controls require a separately designed explicit-user-action path and existing authorization checks.
- Keep routine signals in the dashboard. Optional external notifications cover newly sustained Investigate conditions, meaningful deterioration or decisions requiring the user. Do not notify on every retry or repeated unchanged condition. Delivery channels and configuration remain implementation choices, with notifications disabled until configured.
- Preserve keyboard access, readable text equivalents, mobile layouts, light/dark themes and explicit empty/error states.

### Data and privacy boundaries

Reuse the canonical telemetry engine and its read-only, versioned projections. Preserve canonical completion and budget authority. The dashboard is a consumer, not another store reader, migration owner or completion reducer.

The existing GitHub Pages dashboard has a restrictive approved-public projection. Its current policy excludes raw logs and private item, invocation, thread and provider identities. Full diagnostic detail therefore belongs in an authorized private/local view or equivalently protected surface. Public output may expose only explicitly approved aggregates, aliases, links and notes under the existing rules. Do not weaken those rules or upload private chains/logs merely to satisfy the new UI.

Select the minimum data contract needed for evidence-backed classification, causal links, history and coverage. Missing producers should yield a useful partial view with explicit gaps; they must not trigger fabricated event joins or block unrelated delivery. Private evidence access must enforce its own authorization independently of a public deep link.

Instrument the dashboard itself: collection cost and duration, refresh failures, freshness, missing events, attribution gaps, projection size and reporting overhead. Its collection/reporting work belongs in the same process-efficiency accounting where genuine attribution is available. Rate-limit and bound collection; reuse unchanged evidence and avoid a new reporting bureaucracy.

### Implementation roadmap

These are proposed phases within the existing dashboard/efficiency outcome. They do not reopen completed historical milestones or establish authority to activate a feed or change policy.

| Phase | Scope and dependencies | Acceptance |
| --- | --- | --- |
| 1. Evidence and measurement contract | Inventory existing engine/receiver/dashboard projections. Define outcome stages, event and attempt joins, effort categories, causal tags, required-by-policy metadata, coverage, clocks and historical configuration identity. Resolve the private diagnostic/public projection architecture before implementing protected detail. | Versioned minimal contract; fixtures covering missing lineage, incompatible cost bases, overlap, stale data, corrections and unsupported classifications. No duplicate completion/budget authority or public privacy expansion. |
| 2. Overhead-chain projection | Build bounded engine-owned read models for delivered and stalled outcomes, preserving original failures, parallel spans, handoffs and unresolved obligations. Add evidence navigation and observation-time/current-interpretation joins. Depends on phase 1. | Independent reconstruction of representative chains agrees with original evidence; elapsed union and effort totals do not double-count; missing causes remain unknown; historical views reproduce their recorded evidence cutoff. |
| 3. Health and anomaly assessment | Implement explainable dimensional indicators, overall verdict, confidence, baselines, Watch/Investigate states, ranking and versioned configuration. Required validation remains open to scrutiny without bypassing policy. Depends on phases 1–2. | Scenario corpus distinguishes busy-but-stalled work, justified expensive validation, repeated unchanged checks, external waiting, insufficient evidence and healthy delivery. Every verdict and anomaly has inspectable reasons and provenance; no unexplained percentage or score substitutes for evidence. |
| 4. Diagnostic interface | Implement the opening screen, top-five anomalies, delivery/effort views, parallel overhead timeline, filters, private evidence drill-down and historical comparison. Retain useful supported public aggregates. Depends on phases 2–3; UI shells can proceed earlier against the settled contract. | Browser tests cover navigation, filter reset/deep links, refresh-state preservation, accessibility, mobile layout, stale/malformed/partial states and private/public separation. User scenarios reach the original evidence behind a verdict without exposing private content publicly. |
| 5. Advisory interventions and operational options | Add evidence-backed recommendations, recorded threshold/classification changes, comparison of pre/post-intervention behavior and optional deduplicated notifications. Include collection and reporting overhead. Depends on a usable phase 4 diagnosis. | Recommendations state benefit, tradeoff, confidence and success criteria. Configuration changes are auditable, notifications remain bounded and opted in, and no automatic process mutation occurs. Dashboard overhead and unavailable measurements are visible. |
| 6. Coherent delivery, calibration and live acceptance | Complete the existing release, installation, schema/store compatibility, supported-provider enrollment and matching feed/dashboard deployment obligations. Run a bounded pilot across representative healthy and problematic work, calibrate thresholds and compare findings with independent evidence review. | Published and installed components correspond to the qualified source; real browser/readback and privacy checks pass; pilot findings, false alarms, missed detections, coverage limits and collection cost are recorded. Relevant source/publication/adoption/activation outcomes are accepted separately before whole-feature closure. |

No numerical delivery dates or savings percentages are asserted without a scoped implementation estimate and baseline. Start the earliest meaningful end-to-end slice with one real outcome and its chain, then extend coverage. Do not grow surrogate control suites while deferring genuine private-view, installed-feed and operational acceptance indefinitely.


Landing this proposal does not implement the dashboard, change existing policy, authorize a feed cutover or establish native acceptance. It extends V2-EFF-01.2–.6 within the existing programme; whole-feature completion remains open.


### PH-1 source candidate — 2026-10-12

The shared authenticated dashboard now projects `private-dashboard/2` with bounded
`process-health/1` evidence. It preserves genuine recorded invocation, activity and
CI identities, witnessed parent relations and source-delivery references; absent
canonical revisions, digests, operational stages and diagnostic measurements remain
unknown. The private overview exposes outcome selection, clock-separated parallel
steps, explicit historical context and seven unmeasured effort categories. Its
initial verdict is **Insufficient evidence**. Existing public efficiency summaries
precede activity; public health remains unavailable without an approved assessment.

Incremental compilation and 61 focused projection cases pass against unchanged
locked package inputs. Four hosted-server security cases and 12 local-server
security cases pass. Three local Chromium journeys remain omitted because the
local browser custody profile refused detached children. The existing hosted
workflows remain the required private/public browser source qualification route. The first browser attempts retained the missing
native-library failure and the subsequent detached-process custody refusal; neither
establishes UI acceptance. Candidate installation, a workspace-authorized real
outcome journey and richer canonical assessment remain separate acceptance gates.
Accepted installed read-only discovery confirms that the legacy store has no
workspace binding, and the currently selected host configuration has no workspace
association. These facts cannot authorize a private session. Prospective enrollment
uses the existing canonical route with a separate store and explicit configuration;
legacy observations cannot be relabeled. Detailed evidence is disclosed on demand;
malformed refresh retains the last valid view, and scope/session changes clear it.
The existing host candidate artifact retains two fixture-only screenshots, each
bounded to one MiB, for independent desktop/mobile visual review. Whole-feature completion
and UTEL-DASH-15 remain open.
