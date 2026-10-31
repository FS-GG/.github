# Retirement acknowledgement writer r2

`learn-retirement-ack-r2.py` replaces r1 (`aadd55b1…`, imported at `baseline/libexec/learn-retirement-ack.py`). Refs
FS-GG/.github#4647; design: `docs/coordination/2026-10-30-learn-finite-executor-pod-design.md` section 10.

Why: the operator's retirement gate refuses to start while `a0-20261022t192443z-f1-pre` has a `learn-op-retirement-failure/1`
record and no acknowledgement. r1 wrote it under `/etc/fsgg/learn/retirement-acks`, which fdev's root disk loses on every image
upgrade (it was lost once already). r2 writes under `retirement-acks/` on the persistent bulk bind, where operator r6 (a later
PR) reads it.

| r1 | r2 |
|---|---|
| directory `/etc/fsgg/learn/retirement-acks` | `retirement-acks/` under `/srv/bulk/services/usr-local-libexec-fsgg-learn` (read back through the mounted view `/usr/local/libexec/fsgg-learn`) |
| chain checked by path from `/`, then written by path | every directory from `/srv/bulk/services` down is opened once with `O_DIRECTORY|O_NOFOLLOW` through `openat`, `fstat`-checked on the descriptor, compared by device and inode with the mounted view, and **all writes go through those descriptors** |
| `--ack`, `--remove` | `--ack`, `--remove`, and a read-only `--check` |
| digest given as `--record-sha256` | `--ack` still requires the admitted `--record-sha256`; `--record-file` is an optional cross-check (the two must agree); `--check` accepts either |
| writes only | after `--ack` the mounted view is read back; if it does not show the same bytes the file just written is **removed** and the run **refuses (exit 5)** |

## Pinned directory descriptors

`/srv/bulk` is developer-owned, so a path check followed by a path-based write can be raced by renaming a directory between
the two. r2 closes that window: the directory that was checked is the directory that is written, because the checked
descriptor is the only handle used. A directory swapped in at the path after the pin is not written to (tested), and a swap
before the pin is caught by the inode comparison with the mounted view. This is the pattern for the identical guard in
`learn_seal_claim.py`, which is hardened in a separate PR.

## `--check`

Read-only. The view path is walked from `/` with pinned descriptors, as the operator walks it, so an unsafe ancestor of the
view (not only of the view root) refuses. The acknowledgement must be a regular root-owned file, closed to group and other
writers, at most 4096 bytes, and its content must equal the digest.

## `--record-file` is a cross-check, not a source

`--ack` requires the admitted `--record-sha256`: what is written is the digest Root admitted, never one computed from a file
at write time. `--record-file` (a regular file, at most 16 MiB, opened `O_NOFOLLOW`) is optional and only compares: its
sha256 must equal `--record-sha256`, or nothing is written. For the read-only `--check`, either option is enough.

Unchanged: the content is exactly `<sha256>\n`, written `O_CREAT|O_EXCL|O_NOFOLLOW`, root:root 0644; `--remove` unlinks only a
regular root-owned file.

The admitted ack content for `a0-20261022t192443z-f1-pre` is `3db3a5f3…7210e` (#4516 comment 6360174387). Whether and when to
write it is Root's admission; this PR installs nothing. The write needs this script from a root-owned exec copy, the bind
mounted, and the operator that reads the new directory (r6).

Cooperative: `developer` has full sudo and can edit or delete any acknowledgement. The pinned chain and the same-inode check
catch a mistake or a swapped directory; they do not defend against Root.

Gates: `python3 tests/learn-ack-writer/run.py`. It includes cases that kill the mutants found in review: readback always true,
`--check` walking from the view root, and a missing `O_NOFOLLOW` in `read_regular`.
