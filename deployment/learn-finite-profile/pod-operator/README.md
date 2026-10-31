# Pod operator: gate layer r1

`learn-pod-operator-r1.py` is the **gate layer** of the pod operator described in
`docs/coordination/2026-10-30-learn-finite-executor-pod-design.md` (sections 3, 4, 10 and 14.1). Refs FS-GG/.github#4647. Source only: it
installs and starts nothing, and **this revision cannot start a pod**. Root admits any install and every run.

## What this revision is, and is not

It decides whether a pod op may start and records the decision in a `home-learn-pod-operator/1` RESULT. It does not contain the images, the
role entrypoint, finalizer r5, the loop-volume and container prechecks, or the pod start; those are later PRs, and their pins are empty here:

- `image_ids` and `finalizer_sha` are empty in the production configuration, so every real run refuses (`no image ids are pinned`);
- `--precheck-only` never exits 0 while `pod_precheck_implemented` is false, so Root cannot take a claim from it by mistake;
- `--run` stops after the gates and the claim verification, **before the `input-read` marker**, so a run of this revision consumes no claim.

The ordering contract below is implemented in `begin_input_access` and tested through an injected start function.

## Gates, in order (the first refusal wins)

1. **Identity.** Runs only as uid 30034; exact arguments (`--precheck-only|--run --id --argv-json --source-identity`); image ids and the
   finalizer pin are pinned in the file.
2. **Pins.** Every pinned image id is present locally by Id; the finalizer hashes to its pin through a pinned descriptor chain.
3. **Slice.** `user-30034.slice` is exactly 6 GiB and 1280 pids; a missing drop-in fails closed.
4. **Shared lock.** `~/.local/state/learn-operator.lock`, the **same file and the same flock semantics as operator r6**
   (`O_RDWR|O_CREAT|O_NOFOLLOW|O_CLOEXEC` 0600, exclusive non-blocking `flock` retried every 0.5 s for 120 s, then `operator lock busy`),
   held to process exit. The test holds it with r6's own code and requires the refusal, and the reverse.
5. **Symmetric exclusion.** Refuse while **any `learn-op-*` reader op** (a precheck op included) **or any `learn-pod-*` unit** is active or
   transitioning (`failed`, `activating`, `deactivating`, `reloading`), a failed or transitioning `learn-fin-*` unit, or a leftover
   `learn-pod-*` pod, or a `learn-op-*` container whose op unit is not active (the same orphan sweep as r6). A failed census (units, pods or
   containers) refuses. This is the other direction of operator r6's exclusion.
6. **Custody.** An existing custody or evidence directory for `<id>` or `<id>-pre`, or a unit named for the id, refuses.
7. **Acknowledgements.** Operator r6's `retirement_gate` and `read_ack_pinned`, **verbatim**, against the persistent ack directory.
8. **Evidence size.** Operator r6's `evidence_usage`, verbatim, with its 32 GiB cap.
9. **Admission.** The admission reader of `learn_seal_claim.py`, **verbatim**, with its pinned chain (`open_dir_chain`, `_check_dir_fd`). The
   admission Root wrote must match the argv hash, the source identity, **this file's own sha256**, the finalizer pin and the image ids; it must
   be unexpired and live at most 600 s.
10. **Headroom.** Slice memory headroom at least 4.5 GiB, pids headroom at least 1000, MemAvailable at least 4 GiB, custody disk at least 1 GiB.

`--run` adds: **claim** (the live claim names this op and source identity, and its sealed manifest on disk has the sha256 the claim records;
only metadata is read, the sealed copy of the input is never opened); **nonce** (`runNonceSha256` = sha256 of a fresh nonce is recorded in the
RESULT before anything can start); **input** (`begin_input_access`).

## The ordering contract

`begin_input_access` does, in this order, and a refusal at a step stops there:

1. verify the claim (a refusal creates nothing);
2. create the `input-read` marker `~/.local/state/learn-input/<op-id>/input-read` (directory 0700, file `O_EXCL` 0600, never removed; the file, its directory and that directory's parent are fsynced before the next step);
3. set `gates.inputMounted` to true;
4. mount the input.

So the marker exists before the first read or mount, `inputMounted` is already true when the mount runs, a run that dies during the mount
cannot show `false`, and a second attempt for the same op id refuses at the marker. The marker is owned by this operator; the custody
directory is owned by the finalizer (see the claim tooling's README).

## Contract with the claim tooling

The RESULT has `schema` `home-learn-pod-operator/1`, `mode`, `opId`, `operatorSha256`, an integer `exitCode`, `gates.inputMounted`, a
`containers` list and, for `--run`, `runNonceSha256`. The tests feed it to the claim tooling's own `_precheck_ok` and `_evidence_contract`.

## Verbatim blocks

The blocks marked `VERBATIM` are copied byte for byte from `learn_seal_claim.py` and operator r6. The test compares their source with the
originals, so they cannot drift. The pod operator is a separate single-file script run with `-I` and cannot import them.

## Tests

`python3 tests/learn-pod-operator/run.py` drives `main()` with injected dependencies (a scripted `run`, the effective uid, the clock, the nonce
and the pod start) against temporary fixtures. It runs the admission vectors A01 to A18 against **both** the original reader and the copy and
requires the same answers. A mutation run of 58 mutants is recorded in the PR.
