# Standalone telemetry package fixture

Run `bash tests/standalone-telemetry-package/run.sh /absolute/FS.GG.Coord.Cli.VERSION.nupkg /absolute/evidence.json`.

The fixture installs only that local package into an isolated tool directory, runs from a generated
workspace with no source checkout, and records bounded evidence. Local activation always uses the
production durability assessor. On an unqualified filesystem such as a container overlay, the green
result is a verified refusal with no configuration or store writes. On an eligible Linux x64 filesystem,
the same fixture activates an explicit local association, observes a synthetic native command, drains and
checks applied receipts, then proves uninstall left the store unchanged.

Package growth is compared with the preserved public-source `FS.GG.Coord.Cli` 0.87.0 probe
(`b1ea5d2bb87b6eeeec59c172e50fb824fe7da891235d66910557acc7eb18ff58`, 30,958,077 packed
bytes and 125,906,226 installed bytes). The fixture enforces the roadmap's 10 MiB packed-growth and
30 MiB installed-growth limits. These cheap size checks do not make an L2 performance claim.

Python is used by this development fixture to inspect the package and SQLite evidence. It is not copied
into the installed tool and is not a runtime prerequisite of `FS.GG.Coord.Cli`. Remote HTTPS transport is
qualified by the H1 receiver fixture and is intentionally not duplicated here.
