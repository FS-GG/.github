# GS2-09.7 Q4 seed admission authorizer

Status: **authorizer and native reader source prepared; executor bridge absent**.
This packet does not dispatch a workflow, change an environment, mint a token,
or write the sandbox journal. The Q4 executor remains fail closed because the
pins in `gs2-09-7-seed-bootstrap-admission.py` remain empty.

## Independent native owner boundary

The authorizer uses only the existing `fleet-v1-admission-owner` environment.
Readback on 2026-09-28 established this exact profile:

- environment ID `22582241959`;
- required reviewer `EHotwagner` (user ID `1645484`), rule `66492942`, with
  self review permitted;
- five-minute wait timer, rule `66492943`;
- custom branch rule `66492944`, whose only policy is exact branch `main`,
  policy ID `60823087`;
- administrator bypass enabled. The decision still requires the native owner
  and timer approval records, so a bypassed run does not qualify.

The new workflow is a manual, first-attempt, exact-main producer. It has only
`contents: read` and `actions: read`. It does not receive the sandbox App key,
an installation token, or any writer credential. The workflow reads an exact
executor request artifact and native GitHub records, then uploads one sanitized
decision. It does not reuse the production OperatingV1 authorization or any
`operation/79` decision.

The authorizer requires the native approval list to contain one approval by the
named owner and one GitHub wait-timer approval for this environment. It also
reads the current environment and main branch policy. A changed owner, missing
timer, self-review setting drift, policy drift, duplicate approval, wrong run,
or non-main producer refuses before a decision is written.

## Executor request interface

The later integration bridge must upload exactly one regular file named
`seed-admission-request.json` in an Actions artifact named:

```text
gs2-09-7-seed-admission-request-<phase>-<executor-run-id>-<executor-run-attempt>
```

The dispatch supplies the exact artifact ID, archive `sha256:` digest, executor
run ID, run attempt and `prepare` or `final` phase. The artifact file is
canonical JSON with this envelope:

```json
{
  "schema": "fsgg.gs2-09-7.seed-admission-request/1",
  "phase": "prepare|final",
  "subject": {}
}
```

The request artifact must come from the exact Q4 workflow run and attempt on
`main`; its Actions metadata, archive digest, workflow path and head SHA must
agree with the subject. Extra members, duplicate JSON keys, noncanonical bytes,
expired artifacts, rerun drift and failed completed runs refuse.

`prepare` has only the existing `fsgg.gs2-09-7.seed-prepare-decision/1`
subject. Its operation is exactly `prepare-only-no-effect`. It binds the source
manifest and approved source artifact digests and may only permit credential
preparation. It contains no token digest, journal ref or Git object OID.

`final` has the existing `fsgg.gs2-09-7.seed-bootstrap-decision/1` subject. It
binds the mint proof and token digests, S2 declaration digest, S1 blob/tree/commit
OIDs, the nonce-derived seed journal ref, and `expectedOldOid: null`. Its only
operation is `genesis-nonce-seed-journal`; issue and Project effects, updates of
an existing ref, later migration effects and any other operation refuse.

Neither request schema has a field for a raw token. Strict field equality makes
adding one a refusal.

## Decision and read API

The producer uploads exactly one file named
`gs2-09-7-seed-admission-decision.json` as:

```text
gs2-09-7-seed-admission-decision-<phase>-<executor-run-id>-<executor-run-attempt>
```

The record binds the exact request artifact ID, archive digest, canonical
request digest, executor run/attempt, subject, authorizer workflow SHA and
run/attempt, environment/rule/policy IDs, and sanitized owner and timer
approvals. It expires no later than ten minutes after issue. It contains the
token digest for `final`, never the token.

`scripts/gs2-09-7-seed-admission-native-read.py` exposes:

- `verify_decision(...)`, which verifies the completed successful producer
  run, an integration-pinned exact authorizer workflow SHA, native approvals
  and current policy, the sole repository artifact with the decision name,
  the archive digest,
  exactly one safe member, canonical decision bytes, exact expected subject
  and live bounded expiry;
- `NativeDecisionPort(decision, phase)`, whose `describe()` and
  `read_decision(executor_run_id, executor_run_attempt)` methods implement the
  port consumed by `gs2-09-7-seed-bootstrap-admission.py`.

The sanitized artifact is intentionally readable with Actions read access.
Isolation comes from write custody: candidate code and the Q4 executor have no
write permission to the authorizer run or its artifact, while the authorizer
has no token or provider write credential. Artifact deletion, replacement,
multiple artifacts with the decision name, changed source, approval drift or
expiry causes refusal. Readability is not represented as false.

## Timing and integration boundary

Prepare and final are two distinct authorizer runs. Each retains the native
five-minute timer and produces a fresh decision valid for at most ten minutes.
The integration bridge must therefore stage the operation:

1. publish the prepare request, dispatch and approve its authorizer run, read
   the fresh prepare decision, then perform credential preparation only;
2. after mint proof and S2/S1 OIDs exist, publish the final request, dispatch
   and approve a second authorizer run, read the fresh final decision, then
   attempt the one expected-absent journal CAS before that decision expires.

The Q4 executor job introduced by #3904 currently has a ten-minute timeout. It
cannot safely wait through both independent five-minute timers plus mint,
sealing, readback and CAS. The bridge must use separately bounded stages/runs
or raise the executor timeout while retaining both timers and each decision's
fresh ten-minute expiry. It must also preserve any minted token across the
stage boundary only in protected host custody and revoke or expire it on every
refusal path.

This packet deliberately does not edit the protected Q4 workflow. The remaining
integration packet must publish the two request artifacts, dispatch/wait/read
the authorizer runs, construct the native reader port, install the currently
empty admission pins, grant the executor `actions: read` without Actions write,
and route only the accepted prepare/final results into
the executor. Q5/Q6 migration acceptance, the production Authority journal,
and all issue or Project mutations remain separate.
