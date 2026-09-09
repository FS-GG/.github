# UTEL-DASH — Public telemetry dashboard

Owner: `.github`

Backlink: [Unified Development Roadmap §9.8](../2026-09-07-154210-fs-gg-unified-development-roadmap.md#98-feature-parts-and-subroadmap-index)

Roadmap position: **Simplified baseline and v2 policy binding / V0**

## Outcome

Publish a useful, privacy-bounded GitHub Pages dashboard for FS-GG delivery telemetry. Public GitHub Actions data remains useful on its own. An approved private host may add only closed aggregate projections; missing or stale host evidence stays visible and never becomes zero or compliant.

## Milestones

- [x] **UTEL-DASH-01 — Safe aggregate feed.** The one-shot host adapter discovers only the canonical approved configuration, calls the engine's read-only public export, reconciliation, CI, budget, health and store projections, and removes item, invocation, provider, model, scope, epoch and free-text identities. It publishes a closed sub-1-MiB snapshot to the dedicated `telemetry-data` branch with an optimistic non-force ref update; conflict or collection failure retains the last good commit. Dry-run output is inspectable.
- [x] **UTEL-DASH-02 — Interactive responsive dashboard.** The static local-asset site shows source freshness and exact provenance, a capped recent Actions sample, workflow health, outcomes, durations and a searchable run table. It renders host usage, coverage and canonical budget state when present, with accessible text equivalents, keyboard controls, mobile layouts, light/dark themes and explicit empty/malformed/stale states.
- [x] **UTEL-DASH-03 — Automatic public refresh and routine source delivery.** A least-privilege Pages workflow tests candidate source without deployment, refreshes up to 1,000 deduplicated newest public runs on main/schedule/manual events, binds the source and immutable host-data revisions, and deploys only the static artifact. The Actions pages form a bounded, timestamp-fenced, multi-page sample of latest observed run attempts rather than a complete historical inventory.
- [ ] **UTEL-DASH-04 — Deployed dashboard and host feed activation.** Merge the source, observe the Pages deployment and browser-read the public site. Activate the recurring host publisher only after an operator supplies the exact existing approved host configuration. Until then, the page reports the local feed as unconfigured; no replacement store is initialized.

## Metric semantics and privacy boundary

Token input includes cached and cache-write components, so the UI does not add those components to input. Aggregates spanning incompatible provider/accounting scopes are not presented as priced or provider-comparable usage. All-time host totals ignore dashboard date filtering because the safe host payload contains no item dates. Local CI runner seconds sum jobs; wall, queue and category values are summed per-item union projections, not fleet wall time and not a partition of runner seconds. Native public Actions duration uses only supported start/update timestamps and has no local item, bureaucracy, queue or runner attribution.

The canonical reducer alone owns budget verdicts. The dashboard states the 5% target, more-than-10% ceiling, 15-distinct-item or any-more-than-25% intervention trigger, useful-test exclusion and deployed-plus-verified reset rule. Unknown dimensions remain unknown. Model, effort, price and human-time breakdowns stay absent when the host cannot provide them.

No raw log, SQLite store, WAL, assignment, source identity, item identity, provider/model/scope/epoch identity, diagnostic text or credential is written to GitHub. The host observation timestamp is part of the immutable host snapshot and is not reset by a Pages build.

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
ExecStart=/usr/bin/python3 tools/telemetry-dashboard.py host-snapshot --repo FS-GG/.github --branch telemetry-data --path host.json --output %t/fsgg-telemetry-dashboard-host.json
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

## Verification

`tests/telemetry-dashboard` covers the closed nested schemas, identity/free-text sentinels, numeric/time/enum/byte refusal, unknown versus zero, canonical budget boundary consumption, epoch filtering, concurrent ref conflict, page drift deduplication and browser populated/empty/malformed/XSS/keyboard/mobile/chart-equivalent behavior. Source checks and synthetic browser fixtures are separate from real-data evidence. The scheduled public collector supplies current live Actions observations after merge; the configured private host journey remains pending under UTEL-DASH-04.
