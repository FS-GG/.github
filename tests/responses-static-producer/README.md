# Responses installed/static producer

`tools/process-efficiency-responses-static.py` is a pure producer module imported
by the existing custody owner. It starts no process, opens no provider connection,
reads no credentials and grants no collector authority. Its pure tests use
synthetic receipts, including synthetic verifier results; they are not an
installed/static qualification.

`build_profile` composes the fixed current profile around exact managed-serialized
installed-file bytes. `profile_artifact` validates those bytes. `assemble` checks
one selected attempt's exact nine scenario receipts, strict TRX identity/result
joins, independent verifier replies, loaded-component metadata, unchanged
physical inventory digests and the original settled custody terminal. The owner
must supply genuine receipts and actual physical observations. Supplied JSON
correspondence alone cannot authenticate them.

The existing Runner finalizer calls `assemble` after child retirement, while the
same original deadline still has time remaining. It must not launch a new result
process, reset the timer, convert old results, or supply guessed custody flags.
The private terminal extension requires attemptId/sourceHead/originalWholeMilliseconds,
operationSpecificCaptureProduced=false, and installedFilesBeforeSha256/AfterSha256
from actual before/after inventory verification. Existing qualified, clean,
resource/storage/runtime and cleanup observations remain mandatory. The producer
refuses missing observations. The public result retains the existing closed
`fsgg.telemetry.responses-static-qualification/1` schema.

`consumerSha256` in the private selection/receipts binds the root-selected
consumer closure manifest (all selected compiled test consumers and replay
runtime), rather than pretending Core and Host tests use one identical assembly.
Each actual invocation/file must resolve in that retained manifest. The existing
custody owner binds these source/tool inputs before invocation. This module does
not manufacture signatures, receipts, or native evidence.

The new managed test leaf has three cases: serializer parity, physical installed
closure, and credential-role grammar. To execute the two qualification scenarios,
root supplies a private `FSGG_RESPONSES_STATIC_CONTEXT` JSON file:

```json
{"schema":"fsgg.telemetry.responses-static-context/1","attemptId":"SELECTED_ATTEMPT","profilePath":"ABSOLUTE_PRIVATE_PROFILE"}
```

The context and profile use existing Host private-file checks. Scenario evidence
is written with CreateNew/0600 beside that context; all other IO reads only the
explicit immutable product roots. Product assemblies actually loaded by the
consumer must have exactly the four paths declared in the profile. Inventory
bounds are the stricter shared contract (4 roots, 512 files, 200MiB, 1024 directories,
4096 entries). The physical closure scenario hashes every regular file, checks
all assembly identities, refuses links/extras, and emits actual loaded paths.
The owner repeats physical verification at the end before recording the terminal.

Without an explicit context, ordinary tests emit no scenario evidence. A passing
ordinary TRX alone cannot stand in for either positive installed scenario: the
producer also requires their context-bound metadata receipts. The credential
case exercises the shared production metadata predicate with synthetic paths and
alias/refusal mutations; it never reads the secret content, enrolls a producer,
or installs a Manager receipt. Supported Manager installation later independently
checks the actual credential associations and all current artifacts.

The verifier scenarios run the same pinned installed module on named synthetic
fixtures, retain exact raw capture/snapshot/result bytes and their invocation
receipts, and prove valid replay, changed-request refusal, and stale-snapshot
refusal. These synthetic fixture captures are not production operation-specific
captures. No queue, dispatch, provider operation, native usage or completion is
created by this static qualification.

Root owns the later finite <=60-second execution packet, artifact resolution,
project Compile insertion, source/consumer pinning, positive installation and
provider qualification. Existing 1080-second source runs remain separate.
