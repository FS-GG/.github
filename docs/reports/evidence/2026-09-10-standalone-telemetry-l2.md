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
RSS after quiescence, and 100 caller-observed cold CLI durable submissions
between 60 and 64 KiB after warm-up. It measures the installed package's Store
assembly directly for 100 warmed in-process durable admissions, records
application drains outside the admission interval, and retains a separate 100
sample accumulating-pending series. Its bounded JSON separates these timings
and filesystem/process evidence from physical power-loss, Main installation,
and public release.

The coherent release manifest binds the Dashboard asset tree and the prepared
runtime qualification artifact alongside exact source and package identities.
Public qualification requires a manifest-verified source and normalized payload
binding, records both the prepared and repository-served archive hashes, and
performs 20 fresh credential-free nuget.org restores. The release workflow
downloads the served package, verifies the repository-signed bytes against the
immutable manifest, repeats the installed dashboard/uninstall/reinstall journey,
and measures size and public cold restore against the preserved 0.87.0 baseline.

## Accepted source and first release attempt

The L2 source merged as `017e51662ccea17f46f2bcf780e86e39cac5619e`
after all 79 required checks passed. Its first coherent 0.88.0 release attempt,
[run 34437714529](https://github.com/FS-GG/.github/actions/runs/34437714529),
stopped during preparation before a manifest, draft, tag, or publisher effect.
The genuinely eligible local runner measured dashboard startup at 438.744 ms
median / 451.465 ms p95 and peak idle RSS of 81,719,296 bytes. Both passed.
It measured the prior fixture's whole cold CLI, accumulating-pending submission
path at 665.433 ms median / 892.810 ms p95, honestly failing the then-misapplied
100 ms target. The source-free package fixture consequently reported 41 passing
checks and one failing check.

The replacement qualification preserves that failure as evidence and applies
the recorded R0 split: warmed in-process Store admission remains bounded at 100
ms p95, while the complete one-shot CLI path is bounded at 1,000 ms p95. Both
use the Store assembly carried by the installed package on an assessor-qualified
filesystem. Drain and accumulating-backlog timings remain separately visible.
This container's overlay filesystem is still refused and supplies no production
qualification or physical power-loss claim.

The optional Host 0.1.0 package was subsequently published to both verified
feeds by [release run 34440033463](https://github.com/FS-GG/.github/actions/runs/34440033463)
and tagged as
[`telemetry-host/v0.1.0`](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.1.0).
The shared Store repair in this change belongs to the already-declared Host
0.1.1 source patch; 0.1.1 remains unpublished. Host publication and Main
deployment remain separate from coherent 0.88.0.

L2 remains unchecked until an accepted exact repair source is published as coherent
0.88.0, both feeds serve the manifest-bound bytes, and the audience-accessible
qualified public-package journey succeeds. Main installation and the optional
Host release/deployment remain separate operations owned by H2/H3.
