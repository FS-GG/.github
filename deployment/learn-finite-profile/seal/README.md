# Input sealing, claim and admission tooling

`learn_seal_claim.py` is the root-only tool behind sections 4 and 10 of
`docs/coordination/2026-10-30-learn-finite-executor-pod-design.md` (Refs FS-GG/.github#4647). It is source only: Root
admits an install and every run. Its tests use temporary trees.

| Command | Purpose |
|---|---|
| `manifest` | Take manifest M1/M2/M3 of the two pinned `spool/` subtrees: path, apparent size, sha256, `mtime_ns`, `ctime_ns`, inode, mode. Refuses symlinks, fifos, devices, more than 64 files or 16 MiB. |
| `quiesce` | Prove the source is still: the drain timer and service **measured** inactive, no open file under the subtrees (`/proc` scan), M1 equals M2 in every field, at least 300 s apart. |
| `seal` | Re-check the timer and the open files, require 300 s since M1 by the clock, validate every M1 path, copy into `inputs/<snapshot-id>/` (root:root, 0755/0644, mtimes preserved), verify each copy, take M3 and require M3 = M1, then write the sealed manifest `learn-input-snapshot/1`. A failure removes the whole snapshot directory. |
| `claim` | Create `claims/<source-identity>.claim` with `O_EXCL`. Needs the sealed manifest `inputs/<snapshot-id>.manifest.json` with that sha256 and source identity, and the operator RESULT of a passing `--precheck-only` that shows the input untouched. |
| `void` | Retire a claim whose op never mounted the input (below). The claim is renamed, never deleted. |
| `supersede` | Replace a consumed claim with a claim of the next generation for a new op, on a recorded Root decision. Resumable. |
| `admit` | Write `admissions/<op-id>.admit`: argv hash, source identity, operator, finalizer and image pins, lifetime of at most 600 s. |
| `check-admission`, `status` | **Read only; no root needed** (any uid that can read the mounted view). |

## The source is pinned

The base directory and exactly the two subtrees `passive-home-canonical-v1/spool` and `passive-prospective-0106-20261018/spool`
are constants (Root decision, #4647). The CLI takes no base or subtree argument, and a manifest with any other subtree set is
refused, so a different selection cannot produce an identity. The source identity is the sha256 of the base and, per file,
relative path, size, sha256 and `mtime_ns`; re-sealing the same source gives the same identity.

## Claim state and generation

State and the next generation are derived from the history on disk: the live claim, `.claim.consumed-<op>`,
`.claim.voided-<op>` and the supersede records. A claim is refused while a supersede is unfinished, meaning an intent whose
successor claim does not exist, or a consumed claim with no successor. After a successor is voided, the state is `voided` and
the next claim continues the generation (it does not restart at 1).

`supersede` writes its intent first, then renames the old claim, then creates the successor. After a crash at either step,
`status` reports `supersede-pending` and `resumeWith`; running `supersede` again with the same decision, reason and evidence
finishes it. Different values are refused.

## Void

A claim is **voidable** only if all of these hold:

1. the `input-read` marker for the op does not exist;
2. the operator RESULT is from a `--run` (a `--precheck-only` result is not evidence about a run), has a non-zero integer
   `exitCode`, `gates.inputMounted == false`, a `containers` list, and no container with a mount whose `destination` is `/in`;
3. the finalizer record (`learn-op-*/1`) has `inputRead == false` and `containersStartedWithInput == []`;
4. the finalizer record is bound to the RESULT by the same `runNonceSha256`, the RESULT's `operatorSha256` equals the
   admission's, and the finalizer record's `finalizerSha256` equals the admission's;
5. the call carries a Root corroboration reference (a GitHub comment URL).

**A void is attested by artifacts the operator account writes.** The RESULT, the finalizer record and the marker are all
written by `fsgg-learn-qual` and read by Root; this tool makes them consistent with each other and with the admission, but
it does not authenticate them and does not detect a compromised operator account. Only Root's independent corroboration,
recorded as a reference and **not verified by this tool**, strengthens it. A void proves "never mounted", not "never read".
The void record says all of this.

Any other claim is **consumed**. `supersede` is only for a consumed claim: it needs a signal that the input was read or
mounted (the marker, `inputRead != false` or a non-empty `containersStartedWithInput`); a voidable claim must be voided.
The supersede record keeps the old claim hash, the old finalizer record hash, the decision reference and the reason. The
decision reference must be a GitHub issue or pull-request comment URL; it is format-checked only, and **Root must verify the
comment**.

## Who owns what on disk

- **Custody directory** `~/.local/state/learn-custody/<op-id>/`: created by the **finalizer** with `exist_ok=False`, and the
  operator refuses a run whose custody directory already exists. This tool never writes there.
- **`input-read` marker** `~/.local/state/learn-input/<op-id>/input-read`: created by the **pod operator** (uid 30034,
  directory 0700, `O_EXCL`) immediately before the first read or mount of the input and only after verifying the claim. The
  finalizer records whether it exists. This tool only reads it. It is deliberately outside the custody directory, so neither
  side has to create inside a directory the other owns.
- **Claims, voids, supersedes, admissions, sealed snapshots**: written by this tool as root through the persistent source path.

The operator RESULT and finalizer record fields above are a contract for the pod operator and finalizer r5 PRs.

## Admission reader

`check_admission` enforces the lifetime cap (600 s), `issuedUtc <= now`, `expiresUtc > issuedUtc`, time zones and the pins. The
`check-admission` subcommand reads only, so the pod operator's account can run it. The pod operator is a separate single-file
script run with `-I` and cannot import this module, so it must carry this logic verbatim; the vectors in
`tests/learn-seal-claim/run.py` are the conformance set for that copy.

## Quiesce

The drain timer and service are measured with `runuser -u developer -- systemctl --user is-active`. If they cannot be
measured, the caller's `--timer-asserted-stopped yes` is recorded as an assertion (`timerEvidence: asserted-by-caller`), never
as a measurement; a measured active unit is never overridden. The gap between M1 and M2 is self-reported by the manifest
command, so `seal` also checks the real elapsed time since M1 against the clock and re-checks the timer and the open files.

## Cooperative

`developer` has full sudo and can edit or remove any file this tool writes. The records detect mistakes; they do not defend
against Root. All writes go through `/srv/bulk/services/usr-local-libexec-fsgg-learn`, behind a root-owned chain check from
`/srv/bulk/services` and a same-inode check against the mounted view.
