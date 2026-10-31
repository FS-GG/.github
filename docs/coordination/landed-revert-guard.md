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
- **Edit versus revert.** Per file, each added line anywhere in the result whose text the file never held in the
  window offsets one dropped landed line. Anything left over is a drop: a pure deletion, a restored older version,
  or text the file already held. A line that moves within its file is not dropped. A refactor that rewrites a
  section and adds its replacement elsewhere in the same file nets out; one new line beside a stale restore, or a
  `# removed` comment after a deletion, hides one line and no more.
- **Mirrors.** Findings on byte-identical files with the same dropped lines (the skill roots hold copies of each
  other) count once toward the bound, and the output says which paths were folded together. Copies that differ
  are counted separately.
- **Resurrection.** A file the window deleted or renamed away, and that the result has again, counts its
  restored lines the same way.
- **Credit is visible.** The output states, per file, the lines dropped, how many new lines elsewhere in the
  file offset them, and the net: "30 landed line(s) dropped, offset by 5 new line(s) elsewhere in the file, 25
  net". When the dropped count before credit is 30 or more but the net is under the bound, the check passes with
  a warning that says so, because the credit alone is what kept it under.
- **Bound.** The check refuses at 30 undeclared net dropped lines and warns below that. A restored or deleted binary
  counts as the whole bound.

## Declaring a revert

Name the commits in a commit message of the branch, or in the PR body:

```text
<!-- fsgg:declared-revert/v1 reverts=dd7665f6,#4968:src/**,#4976:tests/producer-fence-attacks/run.py -->
```

A token is a commit SHA (7 to 40 hex) or `#PR` (the squash commit whose subject ends `(#PR)`), optionally
followed by `:path-glob`. Each dropped line is attributed with `git blame` and is covered only when a token names
the commit that introduced it, within that token's scope. The job summary prints the lines each declaration
covered and flags one that covers nothing. The refusal prints the marker in its scoped form, one
`<sha>:<path>` token per file (per directory past eight files), ready to paste.

An edit to the PR body re-runs the job (about ten seconds), and the body is read from that run's event payload,
so a declaration added to the body is read live. A manual re-run replays the old payload; edit the body or push
a commit instead. A marker in a commit message also works and lands in the squash message.

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
3. If `no-silent-revert` is a required context, the red job blocks the merge, and `main` protection has
   `enforce_admins: true` with no bypass actors, so there is no administrator merge. The real override is a
   change to the protection itself, made by Root (the integrator) or a repository owner with admin rights, never
   by the PR author. Record who did it, why and when in a PR comment, and restore it straight after the merge.
   Do not edit the allowlist or the workflow to get around a cap.

### The two overrides

Prefer the first: it touches one context and leaves every other protection as it is. Both need repository admin
rights. These are the REST calls for branch protection; they are documented here and were not run when this was
written, so read the state back after each one.

**A. Remove the context from the required list for one merge, then put it back.** Both calls build the full
`checks` array from a live read, so the other contexts and their app pins (`app_id` 15368) are carried over
as they are now, and `strict` keeps its current value (`false` at the time of writing). Nothing is typed in by
hand that could go stale.

```bash
API=repos/FS-GG/.github/branches/main/protection/required_status_checks

# remove (the PR can now merge with the job red)
gh api "$API" -q '{strict: .strict, checks: (.checks | map(select(.context != "no-silent-revert")) | map({context, app_id}))}' \
  | gh api -X PATCH "$API" --input -
gh api "$API" -q '.checks[].context'          # readback: no-silent-revert absent, the other eight present

# ... merge the PR ...

# restore, with the app pin
gh api "$API" -q '{strict: .strict, checks: ((.checks | map({context, app_id})) + [{context: "no-silent-revert", app_id: 15368}] | unique_by(.context))}' \
  | gh api -X PATCH "$API" --input -
gh api "$API" -q '.checks[] | select(.context == "no-silent-revert")'   # readback: app_id 15368
```

While the context is off the list, every PR on `main` is unprotected by it, not only the one being merged, so keep
the window to the single merge.

**B. Relax `enforce_admins` for one merge, then enforce it again.** Broader, because an administrator then
merges past every required context, not just this one. Use it only when A cannot apply.

```bash
gh api -X DELETE repos/FS-GG/.github/branches/main/protection/enforce_admins     # relax
gh api repos/FS-GG/.github/branches/main/protection/enforce_admins -q .enabled   # readback: false

# ... an administrator merges the PR ...

gh api -X POST repos/FS-GG/.github/branches/main/protection/enforce_admins       # restore
gh api repos/FS-GG/.github/branches/main/protection/enforce_admins -q .enabled   # readback: true
```

Whether a cap override deserves an automated, owner-only path is a repository-settings decision that belongs to
the integrator.

## Making the check required (the flip)

Owner action, and a repository setting, so this PR does not do it. The restore command above is also the flip:
it adds `no-silent-revert` with `app_id` 15368 to the existing `checks` array and leaves `strict` as it is.
Run it once the warning band has been reviewed and the job has a green run on the base, and read it back with
`gh api repos/FS-GG/.github/branches/main/protection/required_status_checks -q '.checks[] | select(.context == "no-silent-revert")'`.

## Protecting the check (proposed, not applied)

A PR can edit the workflow and the script it runs from the base, but those edits only take effect after merge.
To make that merge a deliberate one, a repository owner could add a CODEOWNERS entry and require code-owner
review. This is a repository setting and is proposed here only:

```text
/.github/workflows/landed-revert-guard.yml   @FS-GG/<owners>
/scripts/check-landed-reverts.py             @FS-GG/<owners>
/scripts/lib/gate.py                         @FS-GG/<owners>
/registry/landed-revert-allowlist.json       @FS-GG/<owners>
```

`tests/landed-reverts/run.py` already pins the base-read behaviour: it runs the workflow's own step against a head
that replaces the script with `sys.exit(0)` and requires the stale tree to be refused anyway, and the mutation
corpus has a leg that makes the step run the head's script.
