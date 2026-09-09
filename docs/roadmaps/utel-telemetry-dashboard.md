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
- [ ] **UTEL-DASH-04 — Deployed dashboard and host feed activation.** Merge the source, observe the Pages deployment and browser-read the public site. Activate the recurring host publisher only after an operator supplies the exact existing approved host configuration. Until then, the page reports the local feed as unconfigured; no replacement store is initialized.
- [x] **UTEL-DASH-05 — Safe completed-item and merged-delivery projections.** A bounded schema-7 adapter reads only the exact configured store in a query-only WAL transaction. It requires the latest native delivered outcome, the canonical derived `completed` population and no pending dirty reduction, groups attempts and follow-ups by original item, and publishes only explicitly approved aliases, categories, repositories, evidence links and notes. A separate bounded public scan lists merged pull requests as delivery evidence without treating them as completed items or effort.
- [x] **UTEL-DASH-06 — Completed-item explorer.** The dashboard provides a searchable, sortable, deep-linked item list with role-level invocation spans, completed-turn tokens by approved requested/observed model, effort and accounting scope, separate CI measurements, reducer-owned budget assessments and observed complication signals. Unknown and known-partial coverage remain visible; the UI says that activity phases, causes, repair time and repair tokens are not recorded rather than inferring them.
- [x] **UTEL-DASH-07 — Item feed refresh and routine source delivery.** The least-privilege Pages workflow collects the bounded merged-delivery feed beside Actions, composes the versioned dashboard contract, and keeps deployment behind the native main boundary. Source privacy, projection, malformed-state, desktop and mobile checks bind the routine exact head.

## Metric semantics and privacy boundary

Token input includes cached and cache-write components, so the UI does not add those components to input. Aggregates spanning incompatible provider/accounting scopes are not presented as priced or provider-comparable usage. All-time host totals ignore dashboard date filtering because the safe host payload contains no item dates. Local CI runner seconds sum jobs; wall, queue and category values are summed per-item union projections, not fleet wall time and not a partition of runner seconds. Native public Actions duration uses only supported start/update timestamps and has no local item, bureaucracy, queue or runner attribution.

The canonical reducer alone owns budget verdicts. The dashboard states the 5% target, more-than-10% ceiling, 15-distinct-item or any-more-than-25% intervention trigger, useful-test exclusion and deployed-plus-verified reset rule. Unknown dimensions remain unknown. Model, effort, price and human-time breakdowns stay absent when the host cannot provide them.

No raw log, SQLite store, WAL, assignment, source identity, private item/invocation/thread identity, provider/model/scope/epoch identity, diagnostic text or credential is written to GitHub. Item labels and keys, category aliases, FS-GG repository links, and bounded repair or complication notes appear only when the operator explicitly approves them in the private label file. The host observation timestamp is part of the immutable host snapshot and is not reset by a Pages build.

The schema-7 SQLite reader is isolated inside the source-local host adapter because the current engine CLI has no item-detail export. It verifies the engine-owned status first, opens the canonical database with `mode=ro`, requires WAL and schema 7, bounds rows and VM work, and never initializes, drains, migrates or writes. A future engine-owned versioned item-detail export should replace this adapter boundary.

### Producer handoff: missing activity and repair evidence

The current store records runtime process/thread/turn events and measurement corrections. It does not record planning, implementation, review or repair activity, nor causal repair/complication evidence or attributable repair time/tokens. A producer-owned extension should keep activity stage orthogonal to bureaucracy classification, retain original-item identity, carry typed trigger/cause/evidence plus attempt/activity references, and state its attribution basis and mixed/unknown coverage. Repair or review does not automatically mean administrative overhead, useful test execution remains excluded, and a model turn must not inherit a whole activity or cost category merely from its parent role. This dashboard will consume a future engine-owned versioned export; this source PR does not add a schema migration or duplicate completion reducer.

## Host activation recipe

Do not run this recipe until `~/.config/fs-gg/telemetry.json` (or `FSGG_TELEMETRY_CONFIG`) names the operator-approved existing store and installed engine. Keep the token environment file mode `0600`.

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

After inspecting a dry run, install idempotently with `systemctl --user daemon-reload`, `systemctl --user enable --now fsgg-telemetry-dashboard.timer`, and read back with `systemctl --user status fsgg-telemetry-dashboard.timer` plus `journalctl --user -u fsgg-telemetry-dashboard.service`. The environment file contains `GITHUB_TOKEN=…`; it must grant only repository contents write. A failed run leaves the previous branch commit intact.

The label file must be a regular non-symlink file with mode `0600`. Its closed `fsgg.telemetry.dashboard-labels/1` object contains `items`, `models`, `efforts`, and `scopes`. Each private original item maps to a unique public key, label, FS-GG evidence URL, approved FS-GG repositories, and bounded notes whose `evidenceUrl` is public. Scope keys combine the private provider and accounting scope as `provider|accounting-scope`; public scope aliases must be unique. Unmapped evidence is counted but private values are never emitted.

## Verification

`tests/telemetry-dashboard` covers the closed nested schemas, identity/free-text sentinels, numeric/time/enum/byte refusal, unknown versus zero, canonical budget boundary consumption, epoch handling, completion revision and dirty-state gates, grouped deliveries, role/time/token boundaries, concurrent ref conflict, page drift deduplication and browser populated/empty/malformed/XSS/keyboard/mobile/chart-equivalent behavior. Source checks and synthetic browser fixtures are separate from real-data evidence. The scheduled public collectors supply current live Actions and merged-delivery observations after merge; the configured private host journey remains pending under UTEL-DASH-04.
