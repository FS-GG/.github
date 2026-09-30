# Controlled Telemetry Host state qualification

`qualify_host_state.py` tests the published Telemetry Host's authenticated durable-state boundary. It
uses a private, disposable `fsgg.telemetry.host-config/2` installation and the Host's public CLI and
HTTPS API.

The qualification verifies the exact `0.2.1` package, release manifest, and both-feed publication
journal before extracting the Host runtime. It then:

- generates receiver-owned CSPRNG credentials for active and revoked `native-collector` principals;
- proves that an invalid grant generation cannot create the store;
- initializes the store and enrolls both declared principals;
- checks the public Host status projection reports the initialized store as ready;
- proves that undeclared and revoked bearer credentials receive HTTP 401;
- admits an empty controlled batch, waits for its durable receipt to become `applied`, and requires an
  exact replay to return the same acknowledgement;
- recreates the Host process over the retained store, requires the same receipt and acknowledgement,
  and confirms one lifetime receipt with no pending receipt; and
- removes the disposable config, credentials, extracted runtime, and state after writing a bounded
  private evidence receipt.

The empty batch exercises admission, durable history, idempotency, and recovery without creating a
native observation. A passing receipt therefore keeps `nativeAccessQualified`,
`modelSupportObserved`, `captureApplied`, and `activationAuthorized` false. Genuine Codex native
capture still requires the separately protected native collector installation, source custody, and
account authorization.

## Required inputs

Run this only on the supported Linux x64 qualification host with:

- the byte-exact published `FS.GG.Telemetry.Host` 0.2.1 package;
- its release manifest and both-feed publication journal plus all four expected digests;
- the repository containing `scripts/telemetry-host-release.py`;
- an absolute `dotnet` executable; and
- an ephemeral localhost server PFX, its private password file, and the matching CA certificate.

The state root and evidence file must not exist. The HTTPS certificate must authenticate
`localhost`. The caller selects a free loopback port and retains only the resulting evidence file.
The script never writes credential material, certificate passwords, request authorization headers,
or a payload body to evidence or command output.

The hosted qualification must invoke `python3 deployment/telemetry-collector/qualify_host_state.py`
with the required named arguments and require exit code zero, `controlled-production-state-passed`,
one lifetime receipt, zero controlled payload facts, and all four genuine-capture claims set to
false.
