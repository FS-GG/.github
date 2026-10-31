# The landed-revert guard

`no-silent-revert` ([`landed-revert-guard.yml`](../../.github/workflows/landed-revert-guard.yml)) refuses a
pull request whose merge would drop content that landed on its base recently, unless the PR says so.
[`scripts/check-landed-reverts.py`](../../scripts/check-landed-reverts.py) is the whole check; its docstring
holds the definitions. Delivered under [.github#4730](https://github.com/FS-GG/.github/issues/4730) after
#4977's squash silently deleted about 2,500 lines that #4968 had delivered.

## What it measures

- **Result.** The real squash result, `git merge-tree --write-tree base head`, not the head tree.
- **Window.** "Recently landed" means added to the base within the lookback window before the branch point:
  100 first-parent commits or 7 days of commit date, whichever reaches further. The window is anchored on the
  branch point, not on the merge base, because #4977's branch was built on #4968's own squash and the merge
  base therefore already held the deleted lines.
- **Edit versus revert.** Inside a hunk that drops landed lines, each added line whose text the file never held
  in the window offsets one dropped line. Anything else is a drop: a pure deletion, a restored older version, or
  text the file already held. A line that moves within its file is not dropped. Consolidating 50 landed lines
  into 20 new ones therefore counts the 30 that vanished.
- **Resurrection.** A file the window deleted or renamed away, and that the result has again, counts its
  restored lines the same way.
- **Bound.** The check refuses at 30 undeclared dropped lines and warns below that. A restored or deleted binary
  counts as the whole bound.

## Declaring a revert

Name the commits in a commit message of the branch, or in the PR body when the PR is opened:

```text
<!-- fsgg:declared-revert/v1 reverts=dd7665f6,#4968:src/**,#4976:tests/producer-fence-attacks/run.py -->
```

A token is a commit SHA (7 to 40 hex) or `#PR` (the squash commit whose subject ends `(#PR)`), optionally
followed by `:path-glob`. Each dropped line is attributed with `git blame` and is covered only when a token names
the commit that introduced it, within that token's scope. The job summary prints the lines each declaration
covered and flags one that covers nothing. A body edit does not re-run CI (CI load); to declare late, push a
commit whose message carries the marker.

Globs are path-segment globs: `*` and `?` never cross `/`, and a whole `**` segment spans directories.

## What the base controls

The workflow runs the script, `scripts/lib/gate.py` and `registry/landed-revert-allowlist.json` as they are on
the **base** commit, never as the PR head has them. A PR therefore cannot exempt itself by editing the
allowlist or weaken the check by editing the script; its edits take effect after merge. The allowlist holds
path globs with a mandatory reason; a glob made only of wildcards is refused. The workflow file itself is the
repository's writer-trust boundary (`.fsgg/routine-development.json`): a writer who can edit workflows can
disable any check.

## Override

The check never turns a no-verdict green by itself, and nothing in a PR can switch it off.

**Exit 1 (refused).** Declare the revert as above, or rebuild the branch on the current base if the revert was
not intended. This is the normal path and needs no maintainer.

**Exit 3 (no verdict) from the branch.** A shallow clone, a head that does not merge cleanly, an unreadable
object, or histories with no common ancestor. Fix the branch (update it on the base) or the job (its checkout
must be full depth) and push. There is nothing to override.

**Exit 3 from a cap.** The message names the cap: `pr-commits` (2000), `paths` (20000), `candidates` (1500),
`blame-files` (200), `history-versions` (300) or `window-commits` (3000). A cap hit means the PR is larger than
the check can reason about, not that it is clean. The human route:

1. A maintainer, preferably not the PR author, runs the check from a full clone against the live base:
   `python3 scripts/check-landed-reverts.py --base origin/main --head <head-sha> --max-<cap> <n>`. Each cap
   may be raised to at most ten times its default; a larger value is itself a no-verdict.
2. If it exits 0, or exits 1 with every drop declared and the declarations read as intended, the maintainer
   posts the output as a PR comment.
3. If `no-silent-revert` is a required context, only the repository's existing administrator merge can pass the
   red job. That merge, and its reason in the PR comment, is the override. It is the maintainer's decision and
   is not automated; do not edit the allowlist or the workflow to get around a cap.

Whether the job should be required, and whether a cap override deserves an automated, maintainer-only path,
are repository-settings decisions that belong to the integrator.
