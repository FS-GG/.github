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

Version `0.1.2` is independently versioned and uses the release tag
`telemetry-host/v0.1.2` when published. It is not part of the Kit, Drivers, and coordination CLI
coherent release set. Source delivery does not mean the package has been
published or activated on Main.

`historical-export` compares one schema 8 or 9 source store with one schema 9
target using independent read-only SQLite transactions. It validates every
indexed canonical fact, preserves newer target revisions, skips identical
facts, refuses kind and equal-revision content conflicts, and writes a fresh
private directory of deterministic `fsgg.telemetry.ingest/1` payloads plus a
closed manifest. The snapshots are consistent per store; they are not a single
cross-database atomic instant.

```text
fsgg-telemetry-host historical-export \
  --source-root /absolute/legacy-store \
  --target-root /absolute/host-store \
  --workspace main-fsharp-dev \
  --producer fsharp-dev-main \
  --stream coordination \
  --output /absolute/fresh-private-directory
```

The command never reads a credential and never writes either store. Submit the
manifest's payloads in order through the existing authenticated workspace
client, and require each exact receipt to become `applied`. A target race can
instead produce the existing terminal `semantic-conflict`; take a new preview
and resume from its new generation. A final preview must emit zero payloads.
This carries current canonical facts. It does not recreate superseded legacy
revisions or their original batch grouping.
