# Dedicated telemetry collector container

This recipe prepares a disabled, dedicated collector from the immutable
`telemetry-host/v0.2.1` release. It never reads Main, SSH keys, host homes,
Podman sockets, or an existing telemetry store.

Strict hosted qualification creates two rootless containers. A disposable
development container writes a fresh controlled source into a new private
native-source mount. The collector sees that original mount read-only and sees
separate configuration, store, and evidence mounts. The development container
cannot see those receiver-owned mounts. Both containers use read-only images,
no network, no capabilities, no new privileges, a numeric non-root user, finite
resources, and no host namespace fallback.

The verdict is `controlled-topology-passed` and explicitly records
`nativeAccessQualified=false`, `modelSupportObserved=false`,
`captureApplied=false`, and `activationAuthorized=false`. The source is freshly
produced inside the development container; it is not an imported rollout or a
claim about Codex native evidence.

Production preparation additionally requires a real receiver-owned
`fsgg.telemetry.host-config/2`, fresh schema-12 store, CSPRNG credential,
`native-collector` grant, enrollment, executable pin, and private Codex home and
evidence custody. The receiver owner issues that local grant. Account access,
observed model support, durable dispatch/source bindings, complete population,
shared costs, and experiment enrollment remain separate gates.

Run source checks with `bash tests/telemetry-collector-container/run.sh`. After
Host 0.2.1 is published, dispatch the dedicated qualification workflow with the
exact source and three served asset digests. Other versions, placeholders,
missing capture `/2`, incomplete publication proof, and changed bytes refuse.
