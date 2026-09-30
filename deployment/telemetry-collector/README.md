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
resources, and no host namespace fallback. Rootless `keep-id` maps the runner's
UID and GID to container user `32768:32768`, so 0600 receipts and 0700 state
remain readable and removable by the runner without a privileged ownership
rewrite.

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

Run source checks with `bash tests/telemetry-collector-container/run.sh`. The
published Host 0.2.1 is bound to source
`0145bd2c852847d00da8b3a0c35d27cd64a87781`. Dispatch the dedicated workflow with the
exact source and three served asset digests. Other versions, placeholders,
missing capture `/2`, incomplete publication proof, and changed bytes refuse.

| Served asset | SHA-256 |
|---|---|
| Package | `4847c15ab207a33556462873840ad4109cf1c1e16fd7a15d5fffae5a8162f589` |
| Manifest | `f16546855d4ce60060f762b6f7bed715cc115f9ef97cd7b55dad5294b2ea0816` |
| Publication journal | `bce45f3f4c0701031863c6b1eb398a99982b152f585467cb2e7b06c00987c58e` |

The linear hosted job also runs [controlled production state qualification](PRODUCTION-STATE-QUALIFICATION.md)
against the actual published Host process on the supported hosted filesystem.
It verifies receiver-owned grants, authentication refusals, durable receipt replay
and process recreation. This verdict is separate from container topology and
genuine native capture. Its empty batch creates zero observation facts. Ephemeral
TLS and credentials remain private and are removed from the owned work directory.
