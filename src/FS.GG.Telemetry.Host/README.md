# FS.GG.Telemetry.Host

`FS.GG.Telemetry.Host` is the optional private telemetry service for explicitly
enrolled FS-GG workspaces. It is a framework-dependent .NET tool for the first
qualified production profile: Linux x64 with the .NET 10 and ASP.NET Core 10
runtimes.

Install the package with the .NET SDK, or let the operator installer stage the
verified standard tool payload for a runtime-only host. The command is
`fsgg-telemetry-host`.

The service never provisions, enrolls, migrates, or weakens storage assessment
while starting. Use the explicit provisioning and enrollment commands before
`preflight`, then run `serve` only after preflight accepts the private config,
credential references, TLS material, service lock, schema, capacity, and store.
`status` is read-only.

Browser access keys and producer credentials are separate principals. Secrets
belong in private files referenced by configuration; do not place them in
arguments, URLs, logs, package contents, or source control.

Prospective version `0.9.0` bundles Store14 and the bounded private unassigned-usage
display. It accepts partial native response observations through the existing
authenticated generic receipt API from local Codex rollouts
(`codex-local-rollout/1`) and Claude Code transcripts (`claude-local-transcript/1`),
each in its own native identity namespace; any other provenance is refused. Its manifest
selects Store14 only; no schema migration is implied. Item attribution, inventory,
terminal coverage and absent counters remain unknown. Passive observations stay
outside qualified item totals. Its fresh release requires a new candidate from the exact
current source. The retained `0.6.0` candidate and its uncertain tag intent remain
separate; this version does not retry or complete that publication.

Host is independently released under `telemetry-host/v<VERSION>`, separate from
the Kit, Drivers and coordination CLI coherent set. Published Host `0.8.0` and older
artifacts remain unchanged. Source delivery does not establish a new publication, installed service,
TLS trust, client enrollment or receiver durability.

## Read an inactive preparation

`fsgg-telemetry-host read-inactive-preparation --config ABS` validates the
Manager-created v3 sidecar, receipt, source references, verifier inventory and
frozen reader-profile bytes. It reads configuration as inert data before normal
Host configuration, credentials, locking or store initialization. Credential
contents and native session/capture records are never read; no executable runs.

The closed `fsgg.telemetry.inactive-preparation-read/1` result reports actual
validated input hashes. Source verification, snapshot origin and shared-cost
completeness remain `unknown`; capture qualification, native acceptance and
activation remain false. This source addition requires a protected successor
publication before served-code qualification; Host 0.3.0 does not contain it.

## Unassigned usage browser projection

The prepared browser composition reads item facts and up to 256 current,
authenticated unassigned passive observations in one workspace-authorized
transaction. Its item-detail/4 input produces private-dashboard/3 with an
allowlisted unassigned-usage/1 section. Existing item-detail/2 projection and
private-dashboard/2 consumers retain their format; the updated assets read both
versions. Raw native identities, source paths and payloads are excluded.

Counters remain per observation, with exact decimal-int64 strings and nullable
missing values. Capture, occurrence and receiver clocks stay separate. Coverage
is partial; item attribution and billed cost are unknown. These observations do
not enter item totals, and the display does not aggregate cumulative snapshots.
Source qualification does not publish a successor, change the frozen coherent
0.107 or Host 0.7 artifacts, or establish installed/native receiver acceptance.
