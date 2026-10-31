# Operator r6

`learn-operator-r6.py` is operator r5 (`318edb4e…`, imported at `baseline/libexec/learn-operator-r5.py`) plus a small reviewed
diff. Refs FS-GG/.github#4647; design: `docs/coordination/2026-10-30-learn-finite-executor-pod-design.md` section 10 and
section 14.1. Source only: it installs and runs nothing. Root installs it from a root-owned copy and admits every run.

Against r5 the diff is 12 hunks, 20 lines removed and 107 added (pinned by the test). Everything else is r5 byte for byte,
including `IMAGE_ID` (reader image r4), `FIN` and `FIN_SHA` (finalizer r4), the seccomp pin, the limits, the units, the headroom
gates, the precheck and the custody paths.

## What r6 changes

1. **The acknowledgement is read from the persistent bind.** `ACK_DIR` is `/usr/local/libexec/fsgg-learn/retirement-acks`, written by
   the acknowledgement writer r2. r5 read `/etc/fsgg/learn/retirement-acks`, which fdev's root disk loses on every image upgrade.
   The read is `read_ack_pinned`: the path is walked from `/` with `O_DIRECTORY|O_NOFOLLOW` `openat`, every directory is
   `fstat`-checked on its descriptor (directory, root-owned, not group/other-writable), and the file is opened
   `O_NOFOLLOW|O_NONBLOCK` and checked (regular, root-owned, not group/other-writable, at most 4096 bytes). A FIFO, a symlink or a
   foreign-owned link in the chain acknowledges nothing and cannot block the operator. The content must still equal the sha256 of
   the failure record.
2. **Pod and reader exclusion is enforced.** The sweep lists `learn-pod-*` units as well. A failed or transitioning pod unit is
   busy and refuses like any other unit; an **active** pod unit refuses this op (`exclusion refused: a pod op is active: …`) and
   the RESULT records `gates.exclusion.activePods`. The reverse direction (the pod operator refusing while a reader op or another
   pod is active) belongs to the pod operator and is tested in its own PR.
3. **The RESULT binds to the finalizer record.** The real-op RESULT carries `runNonceSha256` = sha256 of the run nonce **before** the
   units start, so the finalizer record's `runNonceSha256` binds to it even if the start fails. `mode`, `opId`, `operatorSha256` and
   an integer `exitCode` were already in the RESULT; the tests pin them. `operatorRevision` is `r6`.
4. **An existing custody or evidence directory refuses the run** (`op id already used`), as in r5; the check is now the function
   `used_ids` and has its own tests for `<id>` and `<id>-pre` in both roots.

5. **The census is taken again immediately before the start.** The operator lock serialises our own operators only, so between the first
   census and the start a pod op or another reader op could have become active. `recheck_sweep` runs `list-units` once more after the
   precheck-only exit and before the nonce is drawn and `start_real` is called, and refuses (`exclusion refused at the start`, `sweep
   refused at the start`, `concurrency refused at the start`, or `unit census failed at the start`). This op's own precheck units are not
   counted against it. The result is recorded as `gates.startCensus`.

## What r6 does not contain, on purpose

The reader profile mounts no input. The `input-read` marker, `gates.inputMounted` ordering before a mount, the admission-file gate
and the `home-learn-pod-operator/1` RESULT schema are contract points of the **pod operator** (its gate layer is the next PR). Adding
a marker or an `inputMounted` ordering to a profile that has no input would be inventing a behaviour.

## Install order (Root)

The acknowledgement for `a0-20261022t192443z-f1-pre` (content `3db3a5f3…7210e`) must exist in the persistent directory, written by
the writer r2, **before any r6 run**, including `--precheck-only`: the retirement gate refuses otherwise. r5 keeps reading `/etc`
until it is replaced.

## Gates to re-qualify for r6 (Root; the Home harness r3b is not in custody)

A27 and A27b (the acknowledgement gate), the A0 summary (`failures == acked`, `unacked == []`), exclusion in both directions
(this PR covers the reader side), A29 (the two-op cap, unchanged), and a smoke of A0 r4 9/9 because the operator hash changes.
A1 to A26, A28/A28b, A30 and A31 do not touch the changed code.

## Tests

`python3 tests/learn-operator-r6/run.py` runs the shipped bytes: the changed functions are extracted by AST and tested in
isolation, and the whole script is executed end to end through its refusal gates with a scripted fake of `subprocess.run`, after
substituting the machine paths and pins for temporary fixtures (every substitution must match exactly once). The success path of the
precheck needs a running container runtime and is not exercised here; that is the A0 smoke above. A mutation run of 26 mutants of
the changed code (the drift guard disabled so that line counts cannot kill a mutant) is recorded in the PR.
