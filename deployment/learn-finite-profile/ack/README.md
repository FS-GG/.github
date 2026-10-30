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
| chain checked from `/` | chain checked from `/srv/bulk/services` (`/srv/bulk` is developer-owned), plus the source directory must be the same inode as the mounted view |
| `--ack`, `--remove` | `--ack`, `--remove`, and a read-only `--check` that exits 0 only when the ack is a root-owned regular file, closed to group and other writers, on a root-safe chain, with the given sha256 |
| writes only | `--ack` also reports whether the mounted view shows the same bytes |

Unchanged: the content is exactly `<sha256>\n`, written `O_CREAT|O_EXCL|O_NOFOLLOW`, root:root 0644; the writer does not read the
record; `--remove` unlinks only a regular root-owned file.

The admitted ack content for `a0-20261022t192443z-f1-pre` is `3db3a5f3…7210e` (#4516 comment 6360174387). Whether and when to
write it is Root's admission; this PR installs nothing. The write needs this script from a root-owned exec copy, the bind
mounted, and the operator that reads the new directory (r6).

Cooperative: `developer` has full sudo and can edit or delete any acknowledgement. The chain and same-inode checks catch a
mistake or a swapped directory; they do not defend against Root.

Gates: `python3 tests/learn-ack-writer/run.py`.
