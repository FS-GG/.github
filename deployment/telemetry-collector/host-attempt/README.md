# V2-HOST-01.8 attempt producer

This directory contains the qualification-local typed producer for one bounded
V2-HOST-01.8 adoption attempt. It is source preparation only. Protected
workflow admission, acquired private inputs, credentials, dispatch, artifact
custody, native acceptance, and cleanup readback remain root-owned.

`HostAttempt` validates the protected placement snapshot, byte-exact workflow
rendered from the trusted public template with the fixed public-input acquisition
step, and the producer/runtime/adapter closure. The producer adapter is selected
by a separate absolute source path rather than read from the Sandbox placement.
It calls the unchanged
published `HostBinding` CLI as a dedicated process. The binding CLI is the sole
authority for recipe, profile, source-pin, producer, binding, and derived
admission identities. `AttemptOperation` derives admission into memory and
drives the real reducer one action at a time over fixed mechanism callbacks.
Every effect consumes a fresh closure check. The operation drives typed secret
absence and complete inactive-run readbacks before finalization. Native
disposition, secret absence, and owned-run retirement remain separate.

The authoritative binding render and admission derivation remain dedicated
calls to the unchanged published `HostBinding` CLI. Every refusal, including
`process-cleanup-unknown`, is terminal; a later process cannot settle the prior
authority scope. Effect-time checks
reopen the exact Git heads, trees, worktree status, workflow, profile, source
pins, all four fixed payload files, their Git blobs, binding binary, runtime,
and adapter bytes behind the authoritative result without reconstructing its
binding or admission formulas.

`host_attempt_transport.py` is a thin fixed transport. It accepts exactly one
normal action per invocation. Before a secret,
dispatch, branch, cancellation, or deletion command, it exclusively persists
a may-have-effect intention. It accepts no arbitrary command, URL, repository,
workflow, environment, secret name, or run identity. Effect admission enters
only through bounded stdin and is never written into a context or result.
Remote output is returned
as an observation candidate; the adapter never classifies native success or
cleanup completion.

The concrete F# `AcquiredAttemptMechanism` computes observations from raw HTTP and fixed inherited root-channel records and calls `AttemptOperation.run`. Root-owned protected workflow integration must still bind those channels to its acquired private custody and authority. The Python transport remains a fixed mechanism and is not a policy coordinator.

## Preserved bounds

- aggregate operation budget: 2,700 seconds from one monotonic clock
- `HostBinding` adapter call: 90 seconds; its internal process scope remains 80 seconds
- runtime/SDK probe: 5 seconds
- discovery: 90 seconds with the existing 2-second polling interval
- watch: 45 minutes with the existing 45-second polling interval
- fixed API calls: 60, 45, or 30 seconds by role
- artifact download: 120 seconds
- adapter stdout/stderr caps: 1 MiB and 64 KiB

The operation retains any elapsed time from a resumed state in one absolute
deadline. It samples that deadline before and after every blocking callback and
passes a remaining-time/cancellation window to every acquisition. Each mechanism
timeout is clamped to remaining aggregate time. Output pipes and stdin are
read incrementally with caps before accumulation and child process groups are
settled within the same deadline after timeout/cap refusal. Retries do not replenish the aggregate
budget. A lost response preserves its effect obligation and cannot authorize a
second dispatch.

A run listing supplies bounded repository/workflow/ref/placement and prior-run
facts. A singleton remains only a candidate until a distinct root-owned-run
observation joins its nonce and identity. Native evidence also joins exact
run, nonce, profile, operation, placement, and binding identities. Secret
absence requires a complete HTTP 200 listing and proves only secret absence;
it does not prove run or runtime retirement.

## Local qualification

Use a private output directory and a retained package cache. The F# test gate
requires a clean checkout of published recipe
`8ad0da67004d670c6803f34755dfe759a7fc84e7` and its separately compiled,
unchanged `HostBinding.dll`:

```sh
dotnet build tests/HostAttempt.Tests.fsproj -c Release --artifacts-path "$OUTPUT" -p:RestoreLockedMode=true
dotnet build HostAttempt.fsproj -c Release --artifacts-path "$MUTATED_OUTPUT" \
  -p:RestoreLockedMode=true \
  -p:DefineConstants=HOST_ATTEMPT_REMOVE_PROVIDER_IDENTITY_GUARD
HOST_ATTEMPT_RECIPE_ROOT="$RECIPE_ROOT" \
HOST_ATTEMPT_BINDING_DLL="$HOST_BINDING_DLL" \
HOST_ATTEMPT_PRODUCER_DLL="$OUTPUT/bin/HostAttempt/release/HostAttempt.dll" \
HOST_ATTEMPT_MUTATED_PRODUCER_DLL="$MUTATED_OUTPUT/bin/HostAttempt/release/HostAttempt.dll" \
  dotnet test tests/HostAttempt.Tests.fsproj -c Release --no-build --artifacts-path "$OUTPUT"
python3 -m unittest discover -s tests -p test_transport.py
quint typecheck HostAttempt.qnt
quint test HostAttempt_test.qnt --backend typescript --match '^(success|earlyAdmission|stickyLateAck|lostDispatch|ambiguity|candidateNotOwned|cancellationUnknown|deadlineUnknown|secretOnlyCleanup|successThenDeadline|refusedOwnedCancellation|falseOwnershipRefusal)$'
```

Generate the seven bounded correspondence ITFs with the TypeScript backend from the named correspondence
modules in `HostAttempt.qnt`, then run the locked correspondence project with
`HOST_ATTEMPT_MODEL` and `HOST_ATTEMPT_ITF_ROOT`. FsQuint 0.1.0 compares those
traces with transitions produced by `AttemptReducer.applyModelAction`, verifies
the loaded reducer assembly against the selected production pin, and requires an
actual isolated production guard mutation to diverge.

The model is a finite executable check. Its symbolic identities do not prove
Git bytes, process death, remote run exclusivity, credentials, or native
results; those remain concrete implementation and root adoption obligations.

## Concrete acquired caller

`HostAttempt run --request <file> --output <fresh-file>` is the production F#
entrypoint. Its closed request embeds the existing public preparation request,
selected producer and adapter digests, one invocation and lease identity, and
the three fixed channel names `auth-metadata-v1`, `owned-run-v1`, and
`native-verifier-v1`. It accepts no serialized reducer state, credential,
private path, callback, provider URL, native-success flag, or arbitrary command.

Root launches the process with an exclusive mode-0600 lease directory in
`HOST_ATTEMPT_LEASE_ROOT` and inherited descriptors named by
`HOST_ATTEMPT_AUTH_FD`, `HOST_ATTEMPT_OWNERSHIP_FD`, and
`HOST_ATTEMPT_VERIFIER_FD`. Each descriptor carries a four-byte big-endian
length followed by at most 1 MiB of strict JSON. Messages bind the invocation,
destination, monotonic request sequence, requested role or exact run, nonce,
and acquired custody result. The verifier message carries both the canonical
result bytes and the acquired custody receipt bytes, each joined to its own
SHA256. Reads use cancellable inherited-pipe acquisition and share one callback
deadline across provider and channel reads. SIGTERM becomes a sticky operation
failure so an already owned run and secret obligations still receive bounded
cleanup.
Missing, interrupted, late, reordered, mismatched, or malformed messages fail
closed. The source protocol assumes root controls these inherited descriptors;
root still must bind them to its authorized credential, ownership, artifact,
unseal, and canonical verifier machinery.

The concrete mechanism reopens the placement and calls the unchanged
HostBinding for each fresh effect check. It compares the actual loaded producer
and adapter bytes with the prepared selection, rehashes the selected dotnet
host through preparation, and rechecks every location-bearing managed assembly
already loaded when the mechanism is constructed. Assemblies loaded later and
root's sealed layout remain an explicit protected integration observation; the
source does not claim an OS-immutable closure. Before dispatch it obtains a
complete HTTP 200 baseline for the fixed workflow/ref/event query. The query
uses a fixed 90-second `created` interval captured before baseline and retains
the predecessor bound of 20 records with `per_page=20&page=1`. Discovery uses
that same interval; a `Link: rel="next"`, count mismatch, more than 20 records,
duplicate id, malformed record, non-200, or
body above 1 MiB is incomplete and refused. F# computes the baseline exclusion,
candidate set, exact provider run join, secret absence, terminal run state, and
canonical verifier-result joins. Python retains fixed OS/HTTP/process framing
only.

Both fixed secret roles stream their bounded values on stdin. The `gh secret
set` argv deliberately omits `--body`; no value or literal dash appears in
argv. Every effect intention is durably created before its child starts. The
final readback is an atomic F# projection of the returned state rather than a
no-op transport command. The adjacent mode-0600 acquired trace records every
canonical reducer milestone, its actual returned state and emitted actions;
correspondence compares that concrete sequence with the canonical success ITF.
Source fixtures use public synthetic raw HTTP and
framed channel records. They do not qualify root custody or native execution.
The test-only `HOST_ATTEMPT_REMOVE_PROVIDER_IDENTITY_GUARD` compilation is
isolated from the selected production DLL. It proves that removing the actual
provider run identity guard changes the compiled caller outcome for identical
wrong-id input; production has no runtime switch for that mutation.
