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

The exact accepted repair candidate then ran on the eligible GitHub runner in
[run 34441677513](https://github.com/FS-GG/.github/actions/runs/34441677513).
The installed-package journey passed all 42 checks on ext4. Dashboard startup
measured 392.562 ms median / 398.006 ms p95, idle RSS was 81,780,736 bytes,
and whole cold CLI submission measured 335.443 ms median / 344.447 ms p95.
The package-carried Store assembly measured warmed admission at 14.337 ms
median / 23.670 ms p95 and separately measured application drain at 11.921 ms
median / 18.016 ms p95. The 100-sample accumulating-pending series remained
visible at 22.541 ms median / 34.050 ms p95, with successive 20-sample window
medians of 13.450, 18.572, 22.361, 23.810 and 25.080 ms. The candidate package
SHA-256 was
`5e5370aaec487e2a4140f4e1874da1dc1dcdd30f4710dd04259e5ff8a093184c`;
the retained evidence binds the actual PR merge checkout
`f2686068d330a6276eafee03bfdcff0e2ace3d4c`, makes no public-release or Main
claim, and does not infer physical power-loss behavior.

The optional Host 0.1.1 package was subsequently published to both verified
feeds by [release run 34446544380](https://github.com/FS-GG/.github/actions/runs/34446544380)
and tagged as
[`telemetry-host/v0.1.1`](https://github.com/FS-GG/.github/releases/tag/telemetry-host/v0.1.1).
Its accepted isolated rootless Podman deployment rehearsal is H2 evidence;
Main activation remains separate from coherent 0.88.0 and belongs to H3.

After the security repair was accepted at
`d77fe195bc4e133ace8763c2158834410ca72bc0`, release preparation
[run 34467100539](https://github.com/FS-GG/.github/actions/runs/34467100539)
produced coherent content
`sha256:81ebba64b1b707c03a77c19007026f5f925f2529e2f90de30366033019ada525`.
The prepared Coord, Drivers and Kit archives are respectively
`bfd104b66f312a520039351d9e2bd762f6df2c7968e4ec30ff867fc4b812b6d9`,
`8009aede8dc8e8088f01d0789df62d3ce9e6c6e0a01ab193d2a86143db0e72f8`
and `2808bdc8acfa07382a12840761a62d25af18f23e90425237f6774750d16f0c9d`.
Their normalized payload identities are
`sha256:663c0ddf632c4d1d9d80e1a3ea7bd6db24a9c953720a527727cb41dd37f576e9`,
`sha256:96117aec07a683b572eba103b77427c53bd7835c44d0c7d6acf6d01de91b8328`
and `sha256:843c30efaf338f8fea8cce2cbe61347bdd3d51fbb2c699a35bf0091a0eabcf7c`.
The 42-check prepared-package journey passed on eligible ext4 storage. It
measured startup at 411.211 ms median / 418.655 ms p95, idle RSS at 81,952,768
bytes, cold CLI submission at 346.224 ms median / 358.970 ms p95, warmed Store
admission at 12.474 ms median / 20.975 ms p95, and accumulating-pending
admission at 21.884 ms median / 35.159 ms p95. This is prepared-package
evidence: the initial component publishers timed out waiting for nuget.org
propagation after upload. Exact resume coordinator
[34468719144](https://github.com/FS-GG/.github/actions/runs/34468719144)
reused those artifacts; Coord, Drivers and Kit runs `34468800782`,
`34468798694` and `34468796930` verified both feeds without repacking.

The immutable
[`coherent-set/v0.88.0`](https://github.com/FS-GG/.github/releases/tag/coherent-set/v0.88.0)
was promoted by [run 34468803766](https://github.com/FS-GG/.github/actions/runs/34468803766)
at 2026-09-10T11:06:33Z. The final manifest SHA-256 is
`35b8ba8ffc8477d3785ca0b7e6c1466b7abaacb88554b3879244fe6476483889`
and the stable-channel SHA-256 is
`dab6836cd8b81a994a36d6cc4a2e158583a11281fd51a905a97f2b939da3ddba`.
Nuget.org's repository-signed outer archives are
`0f65d9ee6f566ef2a3142b25ea24069bdd05723b8758dfe64025bdfa9a94d9c0`
for Coord,
`ffd185adc80c085ead272e9ea34fa059e1861a3a95816c7f775ae45909435068`
for Drivers and
`68558090da73c3f198158df56d97924f5c12babca68b94003566b8ca747fad1c`
for Kit; normalization recovered the prepared payload identities above.

The public-only Coord journey passed all 43 checks on eligible ext4 storage,
including install, activation, scoped Chromium dashboard, receipt recovery,
uninstall, reinstall and named-history readback. Runtime startup measured
411.980 ms median / 419.989 ms p95, peak idle RSS was 82,030,592 bytes, and
cold CLI submission measured 349.203 ms median / 360.618 ms p95. Warmed Store
admission measured 14.126 ms median / 21.384 ms p95; application drain measured
12.535 ms median / 17.571 ms p95; accumulating-pending admission measured
22.484 ms median / 34.176 ms p95. Twenty credential-free nuget.org restores
measured 1,768.329 ms median / 2,169.416 ms p95. The signed package was
31,189,633 bytes and installed to 126,954,196 bytes, increases of 231,556 and
1,047,970 bytes over public 0.87.0. The generic journey document deliberately
retains its scoped `publishedRelease:false`; the manifest-bound runtime evidence
sets `publicRelease:true`, and the external feed verification plus channel
promotion establish publication.

L2 is complete. Main installation and physical power-loss qualification remain
separate H3 operations.
