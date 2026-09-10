# Standalone telemetry L2 source evidence

Date: 2026-09-10. Scope: L2 source and release-path preparation. Public coherent
release and an audience-accessible exact-release journey remain protected,
separate operations.

## Delivered source boundary

`FS.GG.Coord.Cli` now provides `telemetry dashboard status|serve` against the
explicit L1 repository/workspace association. Status is read-only and an absent
configuration creates nothing. Serve requires an assessor-qualified local store,
revalidates the association on every query, reads the Store's workspace-scoped
snapshot, and projects only the closed private dashboard DTO.

The packaged foreground server binds `127.0.0.1` on an assigned port, prints a
cryptographically random one-use bootstrap URL, exchanges it for a bounded
HttpOnly same-site session, and stops on Ctrl-C or SIGTERM. Host, Origin, target,
method, content type, body/header/response size, concurrency, query time, and
shutdown behavior are bounded and fail closed. The common embedded UI has an
explicit local mode while preserving the Host access-key mode. Local logout and
expiry hide the Host credential form and require a new `serve` process because
the bootstrap capability cannot be replayed.

The CLI package references the host-neutral Dashboard library and retains no
Host, Akka, ASP.NET, Python, or Node runtime dependency. The independently
versioned Host advances to 0.1.1 after the separately released 0.1.0 source
because its shared Dashboard assembly bytes change; it remains outside the
three-package coherent release set.

## Qualification and release path

The source-free package fixture now checks read-only unconfigured and configured
dashboard status, drives the installed package's real UI through Chromium,
stops the foreground server with SIGTERM, uninstalls without changing private
history, reinstalls, and reads the retained history. Development Node/Chromium
is test tooling only. The runtime harness records at least 20 cold starts, Linux
RSS after quiescence, and 100 caller-observed durable submissions between 60 and
64 KiB. Its bounded JSON separates filesystem/process evidence from physical
power-loss, Main installation, and public release.

The coherent release manifest binds the Dashboard asset tree and the prepared
runtime qualification artifact alongside exact source and package identities.
Public qualification requires a manifest-verified source and normalized payload
binding, records both the prepared and repository-served archive hashes, and
performs 20 fresh credential-free nuget.org restores. The release workflow
downloads the served package, verifies the repository-signed bytes against the
immutable manifest, repeats the installed dashboard/uninstall/reinstall journey,
and measures size and public cold restore against the preserved 0.87.0 baseline.

## Current evidence and pending exit

Before the source PR, the compiled CLI build passed. The loopback suite passed
15 tests, including genuine Chromium logout and expiry journeys over the real
server; the shared projection/assets suite passed 15 tests. The package budget
and runtime harness unit suites passed 14 tests together. The source-free
package fixture passed 31 checks, including remote HTTPS interruption recovery,
while honestly refusing this container's overlay local-store profile. The
release-saga fixture remained green after adding supplemental evidence and
repository-signing normalization. This environment therefore cannot produce the
qualified local runtime result and makes no physical power-loss claim.

L2 remains unchecked until an accepted exact source is published as coherent
0.88.0, both feeds serve the manifest-bound bytes, and the audience-accessible
qualified public-package journey succeeds. Main installation and the optional
Host release/deployment remain separate operations owned by H2/H3.
