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

Version `0.1.0` is independently released under tag
`telemetry-host/v0.1.0`. It is not part of the Kit, Drivers, and coordination CLI
coherent release set. Source delivery does not mean the package has been
published or activated on Main.
