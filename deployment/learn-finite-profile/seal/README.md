# Input sealing, claim and admission tooling

`learn_seal_claim.py` is the root-only tool behind sections 4 and 10 of
`docs/coordination/2026-10-30-learn-finite-executor-pod-design.md` (Refs FS-GG/.github#4647). It is source only: Root
admits an install and every run. It reads and writes nothing until a Root-admitted command runs it, and its tests use
temporary trees.

| Command | Purpose |
|---|---|
| `manifest` | Take manifest M1/M2/M3 of the two `spool/` subtrees: path, apparent size, sha256, `mtime_ns`, `ctime_ns`, inode, mode. Refuses symlinks, fifos, devices, more than 64 files or 16 MiB, and paths outside the base. |
| `quiesce` | Prove the source is still: timer inactive, no open file under the subtrees (`/proc` scan), M1 equals M2 in every field, at least 300 s apart. |
| `seal` | Copy into `inputs/<snapshot-id>/` (root:root, 0755/0644, mtimes preserved), verify each copy, take M3 and require M3 = M1, then write the sealed manifest `learn-input-snapshot/1`. |
| `claim` | Create `claims/<source-identity>.claim` with `O_EXCL`. Needs the operator RESULT of a passing `--precheck-only` that shows the input untouched. |
| `void` | Retire a claim whose op never mounted the input (below). The claim is renamed, never deleted. |
| `supersede` | Replace a consumed claim with a generation+1 claim for a new op, on a recorded Root decision (below). |
| `admit`, `check-admission` | Write and verify `admissions/<op-id>.admit`: argv hash, source identity, operator, finalizer and image pins, expiry of at most 600 s. |
| `status` | Show the live claim and its history for a source identity. |

Source identity: sha256 of the canonical list of the subtree paths and, per file, relative path, size, sha256 and
`mtime_ns`. It is independent of where the copy is sealed, so re-sealing the same source cannot produce a second claim.

All writes go through `/srv/bulk/services/usr-local-libexec-fsgg-learn` (the in-VM bind is read-only). The chain from
`/srv/bulk/services` must be root-owned, unlinked and not group/other-writable, and the directory must be the same
inode as the mounted view, so a directory swapped after the mount is refused.

## Void and supersede

A claim is **voidable** only if all three hold:

1. the op's custody directory has no `input-read` marker;
2. the operator RESULT has `gates.inputMounted == false`, a `containers` list, and no container with a mount whose
   `destination` is `/in`;
3. the finalizer record (`learn-op-*/1`) has `inputRead == false` and `containersStartedWithInput == []`.

The void record states what this proves (the input was never mounted into a container of that op) and what it does not
(that the input was never read). Items 2 and 3 are a contract for the pod operator and finalizer r5; this tool refuses
a claim's void unless both fields are present with those values.

Any other claim is **consumed**. A fresh claim at the same name would hit `EEXIST`, so `supersede` moves the consumed
claim to `<source-identity>.claim.consumed-<old-op>`, after writing a supersede record
`<source-identity>.supersede-<old>-to-<new>.json` (old claim hash, old finalizer record hash, Root decision URL, reason),
then creates the successor claim (`generation + 1`, `supersedes: <old-op>`). A voidable claim cannot be superseded; void
it. If the successor write fails after the rename, `status` reports `supersede-incomplete` and nothing is deleted.

## Cooperative

`developer` has full sudo and can edit or remove any file this tool writes. The records detect mistakes and a compromised
`fsgg-learn-qual` or run container. They do not defend against Root.
