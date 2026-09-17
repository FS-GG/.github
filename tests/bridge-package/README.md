# GS2-08.7 P1 bridge package probe

This probe qualifies an explicitly named local `FS.GG.Coord.Cli` package candidate. It never selects a
version, reads a package feed, uses credentials, or calls a live provider. `run.sh` packs the current source
only when no package path is supplied; release and publication workflows are outside this window.

```console
bash tests/bridge-package/run.sh
bash tests/bridge-package/run.sh /absolute/path/FS.GG.Coord.Cli.0.89.0.nupkg
```

The harness writes a candidate binding in a temporary directory. The binding covers the exact archive and
payload digests, source commit/tree, installed assembly digests, accepted GS2-08 receipt identities, and the
unchanged release-workflow/Kit/Drivers identities. Installation uses a one-package local source, an empty
NuGet configuration, private caches, and an isolated tool directory. The installed command is executed there;
the durable fence probe loads only assemblies extracted from the supplied package and checks their runtime
locations.

The provider fixture binds only to an ephemeral loopback port. It proves that the production CLI still refuses
mutation while admission composition is unavailable and records zero provider mutations. Separate packed-
assembly checks prove eligible `OperatingV1` and `Preparing` dispatch, refused `Frozen` dispatch, and durable
applied settlement that prevents a duplicate send. The result does not install production admission, qualify a
public feed, publish an artifact, adopt a receiver, or claim Q4 live-provider evidence.
