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
finishes it. Different values are refused. While any supersede is unfinished, **a different supersede is refused too** (it would
strand the first); the refusal names the supersede to resume.

## Void

A claim is **voidable** only if all of these hold:

1. the `input-read` marker for the op does not exist;
2. the operator RESULT is from a `--run` (a `--precheck-only` result is not evidence about a run), has a non-zero integer
   `exitCode`, `gates.inputMounted == false`, a `containers` list, and no container with a mount whose `destination` is `/in`;
3. the finalizer record (`learn-op-*/1`) has `inputRead == false` and `containersStartedWithInput == []`;
4. the finalizer record is bound to the RESULT by the same `runNonceSha256`, the RESULT's `operatorSha256` equals the
   admission's, and the finalizer record's `finalizerSha256` equals the admission's;
5. the call carries a Root corroboration reference (a GitHub comment URL);
6. optionally, `--operator-exit-code N`: Root's own capture of the operator process exit status from the shell. When given it
   must equal the RESULT's `exitCode`, and it is recorded in the void record.

Void is **fail-closed**: a missing or odd field in any artifact blocks it.

**A void is attested by artifacts the operator account writes.** The RESULT, the finalizer record and the marker are all
written by `fsgg-learn-qual` and read by Root; this tool makes them consistent with each other and with the admission, but
it does not authenticate them and does not detect a compromised operator account. Only Root's independent corroboration,
recorded as a reference and **not verified by this tool**, strengthens it. A void proves "never mounted", not "never read".
The void record says all of this.

Any other claim is **consumed**. In particular, **a `--run` that created the `input-read` marker or mounted `/in` and then
failed early, for any reason, cannot be voided**: only `supersede` on a Root decision re-opens the source. (A run that failed
before the marker, with artifacts that meet every condition above, is voidable.)

`supersede` is only for a consumed claim and requires **positive evidence**: the marker exists, the finalizer record says
`inputRead` is exactly `true`, or it lists a non-empty `containersStartedWithInput`. A missing field, a record that only names
the op, or `inputRead: false` is not evidence of consumption, so such a claim is voided, not superseded (supersede is
fail-closed in the opposite direction from void).
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

## Contract for the pod operator and finalizer r5 PRs

The fields this tool reads are a contract. The pod operator PR must:

- create the `input-read` marker **before** the first read or mount of the input, and only after verifying the claim;
- set `gates.inputMounted` in its RESULT **before** it mounts, so a run that dies during the mount cannot show `false`;
- emit `exitCode` (an integer) and `runNonceSha256` in its RESULT, and `operatorSha256`, `mode` (`--run` or `--precheck-only`),
  `opId`, `schema` `home-learn-pod-operator/1`, and a `containers` list whose entries carry `mounts[].destination`.

The finalizer r5 PR must record in its `learn-op-*/1` record: `opId`, `inputRead`, `containersStartedWithInput`,
`runNonceSha256` (the same nonce) and `finalizerSha256`. The design document records the same contract.

## Admission reader

`check_admission` enforces the lifetime cap (600 s), `issuedUtc <= now`, `expiresUtc > issuedUtc`, time zones and the pins. The
`check-admission` subcommand reads only, so the pod operator's account can run it. The pod operator is a separate single-file
script run with `-I` and cannot import this module, so it must carry this logic verbatim. **The copy includes the pinned reader,
not only the field checks:** `open_dir_chain` (a chain of `O_DIRECTORY|O_NOFOLLOW` `openat` calls from the chain start, each directory
`fstat`-checked on its descriptor for owner, directory type and group/other write), the `O_NOFOLLOW` open of the admission file
through the parent descriptor, its owner, regular-file, mode and size (at most 65536 bytes) check on the file descriptor, and the
refusal reasons below. A path-based `lstat` walk followed by a path-based open is not equivalent: it can be raced by a rename under
a developer-owned directory, and it follows a symlinked file. The conformance set for that copy is
this list, implemented in `tests/learn-seal-claim/run.py` (the test checks that every id is listed here):

| Id | Vector | Expected |
|---|---|---|
| A01 | matching admission, unexpired | passes |
| A02 | argv hash differs | refused: `argvJsonSha256 differs` |
| A03 | source identity differs | refused: `sourceIdentity differs` |
| A04 | operator pin differs | refused: `operatorSha256 differs` |
| A05 | finalizer pin differs | refused: `finalizerSha256 differs` |
| A06 | image id set differs | refused: `imageIds differs` |
| A07 | now past `expiresUtc` | refused: `has expired` |
| A08 | no admission file | refused: absent or unreadable |
| A09 | lifetime of one year, written by hand | refused: lifetime exceeds 600 seconds |
| A10 | lifetime 601 s | refused: lifetime exceeds 600 seconds |
| A11 | lifetime exactly 600 s | passes |
| A12 | `issuedUtc` after now | refused: dated in the future |
| A13 | `expiresUtc` before `issuedUtc` | refused: expires before it was issued |
| A14 | times without a time zone | refused: no time zone |
| A15 | unparsable `issuedUtc` | refused: no valid issue or expiry time |
| A16 | `issuedUtc` and `expiresUtc` missing | refused: no valid issue or expiry time |
| A17 | admission directory world-writable | refused: not root-safe |
| A18 | path chain owned by another uid | refused: not root-safe |

## Quiesce

The drain timer and service are measured with `runuser -u developer -- systemctl --user is-active`. If they cannot be
measured, the caller's `--timer-asserted-stopped yes` is recorded as an assertion (`timerEvidence: asserted-by-caller`), never
as a measurement; a measured active unit is never overridden. The gap between M1 and M2 is self-reported by the manifest
command, so `seal` also checks the real elapsed time since M1 against the clock and re-checks the timer and the open files.

## Cooperative

`developer` has full sudo and can edit or remove any file this tool writes. The records detect mistakes; they do not defend
against Root. All writes go through `/srv/bulk/services/usr-local-libexec-fsgg-learn`, behind a root-owned chain check from
`/srv/bulk/services` and a same-inode check against the mounted view.

### Pinned directory descriptors

`/srv/bulk` is developer-owned, so a path check followed by path-based writes can be raced by renaming a directory between the
two. Each write function (`seal`, `claim`, `void`, `supersede`, `admit`) now pins the source directory once: every directory
from `/srv/bulk/services` down is opened with `O_DIRECTORY|O_NOFOLLOW` through `openat`, `fstat`-checked on its descriptor
(root-owned, not group/other-writable), and compared by device and inode with the mounted view. The operation then runs on
`/proc/self/fd/<fd>`, a path that resolves to the pinned directory whatever happens to the name afterwards; everything below it
is root-owned, mode-checked and not writable by the developer. An internal call that already carries a pinned path (the claim
inside `supersede`) does not pin again, so one operation cannot straddle two directories. Results report the path the operator
named, not the `/proc` path. `check_admission` reads through the same kind of pinned chain from its chain start. This needs
`/proc` mounted; the tool refuses otherwise. A path that carries the pinned prefix must be exactly `/proc/self/fd/<n>` with `<n>` an
open directory descriptor of this process (`assert_pinned_root`); a closed or non-directory descriptor, a sub-path, `..` or a
non-numeric tail refuses, so the pass-through for internal calls cannot be reached with a path the tool did not pin. It is the same pattern as the acknowledgement writer r2 (#5009).
