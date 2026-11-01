# Propagating the routine-eligibility producer closure to receivers

Every receiver that runs `routine-eligibility` carries the same five files, byte for byte and mode for mode:

| path | role |
|---|---|
| `.fsgg/routine-development.json` | the policy the validator loads from the live base |
| `.github/workflows/routine-eligibility.yml` | the `pull_request_target` workflow |
| `scripts/check-claim-generation.py` | the validator |
| `scripts/check-routine-eligibility-envelope.py` | the trusted envelope (live base, push parent) |
| `scripts/lib/gate.py` | the validator's shared library |

They are adopted **all five together**: the envelope and the workflow are versioned as a pair (#5191 review F2), and a
mixed old/new set breaks. A receiver that carries an older set is judged by that set, because `pull_request_target`
loads the workflow from the receiver's own base. Receivers that predated #4994 therefore refused every moved base
(Game#755: `fetched base ... differs from event base`) until they adopted the fix.

## Route

The transport is the existing `FS.GG.Drivers` workspace payload (`workspace/workspace-files.json` plus
`workspace/files/<path>`), generated from `src/FS.GG.Drivers/workspace-inventory.json`. That inventory carries other
files too; these five are a subset of its entries, and `tests/routine-closure/run.sh` asserts the five are present, not
that they are the whole inventory. A coherent-set release publishes new bytes; each receiver then takes them in one PR.

```bash
# restore FS.GG.Drivers <version> (nuget.org) and unpack it, then, from a receiver worktree on a routine/ branch:
python3 <.github>/scripts/routine-closure.py apply . --payload <unpacked>/workspace
python3 <.github>/scripts/routine-closure.py check . --payload <unpacked>/workspace --history <.github full clone>
```

`apply` validates all five files against the payload manifest (sha256 and executable bit) before it writes any. It keeps
every original in memory, stages the five as temporaries next to their destinations, and replaces them; if any step
fails it restores every file already replaced (removing one that did not exist, and any directory it created), removes
every temporary, and exits 2. A leftover `.routine-closure.*` temporary would be committed by a receiver's
`git add -A`, which is why cleanup runs in a `finally`. It refuses a closure path that is a directory or a symlink and
any symlinked parent directory, before touching anything. It prints the source identity and the five digests first.
Do not copy files by hand.

## Drift check

`check` exits 1 when a receiver file is absent, differs, has the wrong executable bit, is a symlink or sits below a
symlinked parent (CI reads the base blob, which would be the link text, not this file), or the set is mixed. It also
exits 1 on a **stale repo-local pin**: a 64-hex sha256 (any letter case) elsewhere in the receiver's tracked text that
equals an earlier revision of one of the five files. Receivers pin the workflow digest in their own registries, and the
pin makes the adoption fail a receiver check that has nothing to do with routine delivery:

- FS.GG.Coordination `evidence/github-substrate-v2/gs2-06-7/execution-pins-extension.json` (the immutable execution pin
  validator refuses the new workflow until this entry is rehashed);
- FS.GG.Rendering `eng/release/*-workflow-fences.json` (original-tag settlement census; re-pinning one for an unsettled
  original effect rebinds that effect's custody, so the adoption waits for that settlement).

Limits, stated so they are not mistaken for coverage:

- **History is the oracle.** `check` recognises a pin only if its digest is a revision of one of the five paths in the
  history clone (`--source`, or `--history` with `--payload`). A receiver-local digest that is in no `.github`
  history, such as the pre-adoption copies FS.GG.Game and FS.GG.Audio carried, is invisible to it.
- A payload digest that is not a revision in the history clone is exit 2: the history does not belong to the payload,
  so no verdict on pins is given. A shallow or absent history is exit 2 unless `--allow-shallow`, which prints
  `stale-pin detection OFF`.
- Files that cannot be scanned (not UTF-8, over 8 MiB, symlinks) are listed as `skipped`, never silently ignored.

**Waivers.** A receiver declares a historical record in its own `.fsgg/routine-closure-waivers.json`:

```json
{"schema": "fsgg.routine-closure-waivers/1",
 "waivers": [{"path": "evidence/x.json", "sha256": "<64 lowercase hex>", "reason": "why this is a record"}]}
```

A waiver covers one digest in one file. Another old digest in the same file, or the same digest in another file, still
fails; a waiver that matches nothing is reported as `unused-waiver`; a malformed declaration is exit 2. The declaration
is part of the receiver's tree, so it appears in the receiver's PR diff. `sweep REPO...` clones each repository shallowly
with `gh` and runs `check`, which reads that repository's own declaration.

## Acceptance for an adoption

1. `check` is clean for the receiver against the published payload.
2. The receiver's own CI is green on the adoption PR (a derived pin edit is part of the PR, never a separate one).
3. After merge, a canary: a fast-forward push onto a routine PR, and a PR whose base has moved, each pass
   `routine-eligibility` in the receiver. The PR that adopts the closure cannot prove this itself: it is still judged by
   the old set.

Behavioral controls: `bash tests/routine-closure/run.sh`.
