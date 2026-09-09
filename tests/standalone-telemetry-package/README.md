# Standalone telemetry package fixture

Run `bash tests/standalone-telemetry-package/run.sh /absolute/FS.GG.Coord.Cli.VERSION.nupkg /absolute/evidence.json`.

The fixture installs only that local package into an isolated tool directory, runs from a generated F#
console workspace whose product project has no telemetry references, and records bounded evidence. Local activation always uses the
production durability assessor. On an unqualified filesystem such as a container overlay, the green
result is a verified refusal with no configuration or store writes. On an eligible Linux x64 filesystem,
the same fixture activates an explicit local association, observes a synthetic native command, drains and
checks applied receipts, then proves uninstall left the store unchanged.

Every run also activates the installed workspace adapter against a synthetic receiver over real localhost
TLS. A private test CA is supplied through `SSL_CERT_FILE`, so the normal platform chain and `localhost`
hostname checks remain active. The receiver covers a matching applied receipt, a dropped response after
acceptance followed by same-ID lookup recovery, a CLI restart with pending spool state, authentication and
receipt-mismatch retention, and the 4 KiB response bound. Its persisted state proves one logical obligation
per accepted batch. This is transport qualification against a synthetic receiver; it is not Main access or
a durable-disk claim.

Package growth is compared with the preserved public-source `FS.GG.Coord.Cli` 0.87.0 probe
(`b1ea5d2bb87b6eeeec59c172e50fb824fe7da891235d66910557acc7eb18ff58`, 30,958,077 packed
bytes and 125,906,226 installed bytes). The fixture enforces the roadmap's 10 MiB packed-growth and
30 MiB installed-growth limits. These cheap size checks do not make an L2 performance claim.

Python and OpenSSL are development-fixture tools used to inspect evidence and host the synthetic receiver.
They are not copied into the installed tool and are not runtime prerequisites of `FS.GG.Coord.Cli`.
