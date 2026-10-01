# Persistent independent telemetry receiver source preparation

This directory prepares an inert receiver rooted in immutable Telemetry Host
0.2.1 and the protected Coordination manager's actual installation `/2`
contract. `inspect`, and every command without `--apply`, prints a plan and
performs no forward effect. Nothing starts a container, opens a listener,
initializes a Host store, enrolls a producer, installs a sidecar, grants provider
access, or activates capture.

One private container anchor, `/receiver/private`, owns `host.json`, credentials,
store, evidence, the service lock and the collector Codex home. The manager's
real constraint is therefore satisfied: Codex home and evidence are descendants
of the Host configuration parent. A single native-source volume is mounted
writable into the development container at `/producer/native-source` and
read-only into the collector at `/receiver/private/codex-home`; the development
container receives no other receiver mount. The producer spool is a separate
writable mount.

The inspect result contains inert exact argument vectors for the published Host
`init`, `enroll-producer` and `status` commands, Coordination
`install-native-collector --installation-version 2`, and the manager's
`backup-stopped-host` route. Initialization creates only prepared config,
CSPRNG credential and source-reference bytes. The real manager later creates
`host.json.native-collector.json` and its adjacent receipt. This source never
substitutes a repository-authored sidecar for those outputs.

Effectful fixture operations use a held owner-private parent descriptor, random
staging directory, identity checked rename and bounded cleanup. Every lexical
ancestor must be non-symlink; the immediate custody parent must be mode 0700 and
owned by the caller. POSIX container paths must already be canonical, and
private/development mount targets may not overlap or traverse aliases.

Recovery assembly requires the actual manager sidecar and receipt, a sealed
read-only nonempty native source, and a nonblocking exclusive lock on the exact
Host service-lock inode. The store is copied only while that lock remains held;
a live Host lock refuses. The recovery unit includes config, credentials,
manager sidecar/receipt, the quiesced store, evidence, bound source references,
and the retained native-source bytes. Restore preserves that source, recreates
only the future producer spool empty, and remains inactive. This is preparatory
source behavior; the later private operation must still verify the service
identity, stopped writers, image digest, manager binary, installed receipt,
container replacement and restored Host readback.

```sh
python3 deployment/telemetry-collector/persistent/receiver.py inspect \
  --profile deployment/telemetry-collector/persistent/profile.json
```
