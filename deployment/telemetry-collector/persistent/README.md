# Persistent independent telemetry receiver source preparation

This directory prepares an inert, owner-private receiver rooted in immutable
Telemetry Host 0.2.1 and the existing Coordination installer `/2` contract.
`inspect`, and every command without `--apply`, only prints a plan. Nothing here
starts a container, opens a listener, grants provider access, activates capture,
or reaches Main.

The profile binds the published Host package and release metadata, Linux x64
runtime image, stable numeric identity, Coordination manager source, native
reader executable and operation profile. Initialization creates separate
`receiver/` and `producer/` roots. A development container may receive only
`producer/native-source` and `producer/spool`; it must never receive the
configuration, credentials, sidecar, store, or evidence mounts listed as
`developmentForbidden` by `inspect`.

An explicitly applied initialization atomically creates mode 0700 directories,
mode 0600 files, one CSPRNG collector credential, a config `/2` grant and an
installation `/2` sidecar. Exact replay verifies every recorded byte. Changed
pins, ownership, modes, links, collisions, partial initialization, and content
drift refuse. The generated sidecar is input to the existing protected
`install-native-collector` command; preparation does not invoke that command.

Backup covers the acknowledged store plus configuration, credentials, sidecar,
evidence and native-source references. Restore validates every recorded byte,
recreates producer write directories empty, and remains inactive. Root-owned
qualification must later supply the permanent placement, TLS material, actual
image identity, private owner readback, installed manager binary, real receiver
restart/recovery, provider admission and genuine capture evidence.

```sh
python3 deployment/telemetry-collector/persistent/receiver.py inspect \
  --profile deployment/telemetry-collector/persistent/profile.json
```
