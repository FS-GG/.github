# UTEL-DASH — Public telemetry dashboard

Owner: `.github`

Backlink: [Unified Development Roadmap §9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)

Roadmap position: **Simplified baseline and v2 policy binding / V0**

## Outcome

Publish a useful, privacy-bounded GitHub Pages dashboard for FS-GG delivery telemetry. Public GitHub Actions and merged-delivery data remain useful on their own. An approved private host may add closed aggregates and bounded item detail only through explicit public aliases, category mappings, links and notes; missing or stale host evidence stays visible and never becomes zero or compliant.

## Milestones

- [x] **UTEL-DASH-01 — Safe aggregate feed.** The one-shot host adapter discovers only the canonical approved configuration, calls the engine's read-only public export, reconciliation, CI, budget, health and store projections, and removes item, invocation, provider, model, scope, epoch and free-text identities. It publishes a closed sub-1-MiB snapshot to the dedicated `telemetry-data` branch with an optimistic non-force ref update; conflict or collection failure retains the last good commit. Dry-run output is inspectable.
- [x] **UTEL-DASH-02 — Interactive responsive dashboard.** The static local-asset site shows source freshness and exact provenance, a capped recent Actions sample, workflow health, outcomes, durations and a searchable run table. It renders host usage, coverage and canonical budget state when present, with accessible text equivalents, keyboard controls, mobile layouts, light/dark themes and explicit empty/malformed/stale states.
- [x] **UTEL-DASH-03 — Automatic public refresh and routine source delivery.** A least-privilege Pages workflow tests candidate source without deployment, refreshes up to 1,000 deduplicated newest public runs on main/schedule/manual events, binds the source and immutable host-data revisions, and deploys only the static artifact. The Actions pages form a bounded, timestamp-fenced, multi-page sample of latest observed run attempts rather than a complete historical inventory.
- [ ] **UTEL-DASH-04 — Deployed dashboard and host feed activation.** Merge the source, observe the Pages deployment and browser-read the public site. Activate the recurring host publisher only after an operator supplies the exact existing approved host configuration. The approved host now uses the exact merged engine and an empty privacy-conservative alias allowlist; the digest-bound activation published and immutably verified [public feed commit `b92610f…`](https://github.com/FS-GG/.github/commit/b92610fd6df47708573384378807e5d66adfc690). This host has no user systemd bus, so the inert units are installed but recurrence is explicitly unavailable. Keep this item open until recurrence runs on a suitable user-systemd host and the deployed page is browser-verified; no replacement store is initialized.
- [x] **UTEL-DASH-05 — Safe completed-item and merged-delivery projections.** A bounded schema-7 adapter reads only the exact configured store in a query-only WAL transaction. It requires the latest native delivered outcome, the canonical derived `completed` population and no pending dirty reduction, groups attempts and follow-ups by original item, and publishes only explicitly approved aliases, categories, repositories, evidence links and notes. A separate bounded public scan lists merged pull requests as delivery evidence without treating them as completed items or effort.
- [x] **UTEL-DASH-06 — Completed-item explorer.** The dashboard provides a searchable, sortable, deep-linked item list with role-level invocation spans, completed-turn tokens by approved requested/observed model, effort and accounting scope, separate CI measurements, reducer-owned budget assessments and observed complication signals. Unknown and known-partial coverage remain visible; older snapshots state that activity phases, causes, repair time and repair tokens are unavailable rather than inferring them.
- [x] **UTEL-DASH-07 — Item feed refresh and routine source delivery.** The least-privilege Pages workflow collects the bounded merged-delivery feed beside Actions, composes the versioned dashboard contract, and keeps deployment behind the native main boundary. Source privacy, projection, malformed-state, desktop and mobile checks bind the routine exact head.
- [x] **UTEL-DASH-08 — Versioned coherent engine detail.** [#3381](https://github.com/FS-GG/.github/pull/3381) first consumed schema-8 activity, attribution, typed complication and process-review detail safely. The completed source window preserves `item-detail/1` and adds explicit `/2` item or bulk selection with bounded completion, dirty state, native outcomes, invocation timing and clock provenance, exact provider/accounting-scope usage, CI, budget and process inputs from one read-only connection and explicit WAL transaction. A canonical private revision excludes observation time and separately labelled inbox state; the engine supplies a gzip representation of at most four MiB of exact canonical bytes as the sole snapshot representation, while the wire envelope remains bounded to one MiB. A differently implemented adapter verifies rather than recreates the digest serialization, limits decompression, and does not duplicate the private payload. Deterministic concurrent-correction, non-ASCII/escaping/numeric-shape, real-size envelope and stable/change-sensitive revision tests provide source acceptance. Exact native turn rows retain invocation identity inside the private boundary so the public projection can deduplicate and join the complete root/child/follow-up population.
- [x] **UTEL-DASH-09 — Present process detail through the engine-only projection.** The completed-item drilldown continues to show overlapping activity spans, exact attribution, typed complications and safe review metadata, but the host adapter now makes one `/2 --all` engine call and contains no SQLite import, SQL, table names or migration-version policy. Privacy filtering derives and validates a separate public-payload revision without publishing the private snapshot digest. It sums an item total only when every expected native dispatch has one valid lineage row, the same invocation population is admitted, started and terminal, every invocation has usage, no runtime gap exists, and all rows share one compatible accounting basis. Otherwise compatible observed subtotals remain visible while the total is `not-proven`, with an explicit coverage boundary and unknown-remainder flag. Schema-8, scoped usage, complete and incomplete nested lineage, completion/dirty, malformed/bounded input and recursive privacy tests provide source acceptance.
- [x] **UTEL-DASH-10 — Safe publisher setup entry point.** `publisher-setup` defaults to validation and preview with zero network, credential or systemd effects; it requires exact-`0600` non-symlink labels and reports counts, public revision, destination and planned actions. Install-only writes marker-owned inert user units idempotently and refuses conflicts or active replacement. The unit pins the resolved absolute engine and explicitly selects bounded environment-or-`gh auth` credential acquisition without embedding a token. Activation is separately selected and bound to the exact label digest; it publishes once, verifies whitespace-wrapped GitHub base64 as immutable file bytes, public revision and current branch ref, and only then attempts recurrence. A host without a user systemd bus retains the verified publication result and reports recurrence as unavailable rather than claiming activation. Fake-transport and temporary-systemd tests provide source acceptance. Actual recurring operation and browser verification remain UTEL-DASH-04.

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

`--install-only` may then install the reported inert user units. Activation additionally requires `--activate --approve-labels <sha256> --authorize-recurring-publication`; it verifies the initial immutable publication before enabling the timer. A changed label file invalidates the approval. Source delivery never runs these operational modes.

```ini
# ~/.config/systemd/user/fsgg-telemetry-dashboard.service
[Unit]
Description=Publish the FS-GG safe telemetry dashboard snapshot

[Service]
Type=oneshot
WorkingDirectory=/absolute/path/to/FS-GG/.github
EnvironmentFile=%h/.config/fs-gg/telemetry-dashboard.env
ExecStart=/usr/bin/python3 tools/telemetry-dashboard.py host-snapshot --labels %h/.config/fs-gg/telemetry-dashboard-labels.json --repo FS-GG/.github --branch telemetry-data --path host.json --output %t/fsgg-telemetry-dashboard-host.json
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

The setup command installs equivalent definitions idempotently and resolves the configured engine and Python executable to absolute paths. The environment file contains `GITHUB_TOKEN=…`; it must grant only repository contents write. A conflict or failed publication leaves the previous branch commit intact, and failed immutable verification leaves recurrence inactive.

The label file must be a regular non-symlink file with mode `0600`. Its closed `fsgg.telemetry.dashboard-labels/1` object contains `items`, `models`, `efforts`, and `scopes`. Each private original item maps to a unique public key, label, FS-GG evidence URL, approved FS-GG repositories, and bounded notes whose `evidenceUrl` is public. Scope keys combine the private provider and accounting scope as `provider|accounting-scope`; public scope aliases must be unique. Unmapped evidence is counted but private values are never emitted.

## Verification

`tests/telemetry-dashboard` covers the closed nested schemas, identity/free-text sentinels, numeric/time/enum/byte refusal, unknown versus zero, canonical budget boundary consumption, epoch handling, completion revision and dirty-state gates, grouped deliveries, role/time/token boundaries, concurrent ref conflict, page drift deduplication and browser populated/empty/malformed/XSS/keyboard/mobile/chart-equivalent behavior. Source checks and synthetic browser fixtures are separate from real-data evidence. The scheduled public collectors supply current live Actions and merged-delivery observations after merge; the configured private host journey remains pending under UTEL-DASH-04.
