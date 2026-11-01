# `board-v2 tend` on fdev: units, enable file and the 7-day observation

Refs #5136. This is the operating note for [board-reconciler design](2026-10-31-board-reconciler-design.md) §8 and
§12 PR 8. The design decides what the reconciler does; this note says how it is run on fdev, what is pinned, what
an install changes and how the 7 plan-only days are recorded and judged. The source is
`deployment/fdev-board-tend/`; `tests/fdev-board-tend/` renders and exercises all of it offline.

## What runs

| Unit | When | What |
|---|---|---|
| `fsgg-board-tend.timer` | hourly at minute 07, `Persistent=true`, up to 120 s random delay | starts `fsgg-board-tend.service` |
| `fsgg-board-tend.service` | the timer, and the post-merge trigger below | `fsgg-board-tend-run.py run --mode plan-only`, which runs `fsgg-coord-engine board-v2 tend` |
| `fsgg-board-tend-daily.timer` | 00:30 UTC | starts the daily record service |
| `fsgg-board-tend-daily.service` | the daily timer | `fsgg-board-tend-run.py summarize --date yesterday`: reads the private run index, writes the daily record. Local and read-only; it posts nothing |

Minute 07 is away from the drift observer's :23 cron, its :53 fdev dispatch and the 04:43 archive.

### What is pinned

- **`Environment=FSGG_KIT_ROOT=<kit root>`.** `board-v2 tend` reads the contract marker `fsgg:board-tend-contract/v1`
  from `lifecycle-contract.md` under `FSGG_KIT_ROOT`, else the process's git toplevel. A timer started inside
  another checkout could therefore enable gated rules from a stale or edited contract (#5188 F1). The kit root is
  `~/.local/share/fs-gg/board-tend/kit-<version>/`: exactly the two contract mirrors of the released tag, written
  read-only and verified against the released contract's sha256 (`install.py kit-root`). It is never a checkout.
- **`WorkingDirectory=~/.local/share/fs-gg/board-tend`.** A fixed directory that is not a git checkout. The wrapper
  refuses to run if it was started anywhere else, so even an unset `FSGG_KIT_ROOT` finds no toplevel and keeps every
  gated rule refused.
- **The engine path is the versioned one** (`~/.local/share/fs-gg/tools/coord-<version>/fsgg-coord-engine`), not the
  moving `~/.local/bin/fsgg-coord-engine` link, and the kit root must carry the same `<version>`. An engine update
  (`update-fsgg-coord-cli`) therefore changes nothing under the timer until Root re-runs the installer for the new
  release.
- **`RuntimeMaxSec=900`** and `Type=exec` (the cap does not apply to a oneshot service). A killed run leaves the
  post-merge marker in place.
- **The writer lock** is the engine's own `flock(2)` `LOCK_EX|LOCK_NB` on
  `$XDG_RUNTIME_DIR/fsgg/board-writer-PVT_kwDOEYAWY84Bldpa.lock`, taken only under `--apply`. The unit pins
  `XDG_RUNTIME_DIR=%t`, creates the directory 0700 and keeps it between runs (`RuntimeDirectory=fsgg`,
  `RuntimeDirectoryPreserve=yes`). There is deliberately no `PrivateTmp`: during the transition the writers also take
  the legacy lock at the literal `/tmp` path.
- **The child environment is scrubbed.** The wrapper passes the engine `HOME`, `PATH`, `LANG`, the `XDG_*` directories,
  `DOTNET_ROOT`, the pinned `FSGG_KIT_ROOT` and `GH_TOKEN` read from the 0600 credentials file (the file the
  board-drift dispatcher already uses). Nothing else is inherited, in particular not `GITHUB_TOKEN` and not `TMPDIR`.
- **Plan-only is the unit's mode.** `--apply` is never on the command line. Moving to apply is a separate Root
  effect: re-run `install.py units --mode apply --force`, which refuses unless the enable file already exists with
  mode 0600 and names at least one rule. Deleting the enable file, or `"apply": []`, is still the kill switch.

### Private output

Everything is under `~/.local/state/fsgg/board-tend-out/` (0700, files 0600, `UMask=0077`):

```
runs/<UTC stamp>-<n>/plan.json        the engine's fsgg.board-tend-plan/1 plan (wouldApply, wouldClose, t1iComparison ...)
runs/<UTC stamp>-<n>/engine.out|.err  the engine's summary line and stderr
runs/<UTC stamp>-<n>/meta.json        mode, trigger, exit code, marker disposition, engine version, kit root, lock path
index.jsonl                           one line per run or skipped run
last-run.json                         the latest meta.json
daily/<date>.json|.md                 the daily record and its ready-to-post comment body
```

The newest 600 run directories are kept (a week is about 170 hourly runs plus the post-merge runs).

## The post-merge trigger

After a confirmed merge in `--board-gate enforce` mode, `tools/routine-delivery.py` writes the marker
`~/.local/state/fsgg/board-tend/pending-post-merge` (atomic, fsynced, a new inode each time) and runs
`systemctl --user start --no-block fsgg-board-tend.service`. Only the checkout whose `origin` is FS-GG/.github can
reach this: it is gated exactly like `enforce` (the distributed receiver copy never enforces). It is best effort and
reported on stderr only; a failure never changes the merge result or its schema.

The wrapper never lets a post-merge run be swallowed:

1. A timer run inside the 5-minute minimum interval is skipped, **unless the marker exists**, which always runs.
2. The marker is cleared only by a run that **started after it was written and exited 0**. The wrapper compares the
   marker's identity (inode, mtime, size) before and after the run; a marker rewritten during the run, or one that
   appeared during a timer run, is not cleared, and the wrapper runs once more (up to 3 passes, then leaves the
   marker for the next run).
3. A failed run keeps the marker. If the unit is already active, `systemctl start` is a no-op, and the active run's
   own end-of-run check covers the new marker.
4. A second wrapper started by hand while one runs holds nothing, exits 0 and keeps the marker.

## The enable file

`~/.config/fsgg/board-tend.json`, mode 0600, owned by the user, not a symlink. Missing, invalid or any other mode
means plan-only. The shipped file is empty:

```json
{
  "apply": [],
  "propose": false,
  "maxItemsPerRun": 100
}
```

| Key | Type | Meaning |
|---|---|---|
| `apply` | list of rule ids | The rules the engine may write with. Empty or absent: plan-only. Unknown ids and unknown keys refuse the whole file. |
| `propose` | boolean | Post new proposals once each to #4985 (needs the placeholder key `~/.config/fsgg/board-drift-hmac-key`, mode 0600, D9). Default false. |
| `maxItemsPerRun` | integer 0 to 500 | Write budget per run. Default 100. |

Nameable rule ids: `W1h`, `W1d`, `T1d`, `T1i`, `T1f`, `RM1`, `S1`, `S3`, `S3b`, `S4`, `S9`, `S9r`, `O1`, `C1` and the
gate replay `GR`. Of these, S1, S3, S3b, S4, S9, S9r, O1, C1, W1d and T1d are contract-gated: the engine drops them
unless every installed `lifecycle-contract.md` under the pinned kit root declares `fsgg:board-tend-contract/v1`.
The 0.114.2 contract (sha256 `819a6cb22859455265ed2223f9ea6dbb85dfe82a98277a420c2a58539355a901`) does, so the gate is
satisfied; each rule is still enabled by Root alone, one at a time, after the observation below. The ungated rules
are W1h, T1i, T1f and `GR`; the recommended first unattended set is `{"apply": ["W1h","T1f"], "propose": true,
"maxItemsPerRun": 100}`. RM1 stays out until the engine is given its enrollment input (it is inert today).

## Install, in three separate effects

Each command writes one kind of thing, has `--dry-run`, refuses to run as root and refuses any path outside `$HOME`.

```
# 1. The kit root: the two released contract mirrors, verified, read-only.
python3 -I deployment/fdev-board-tend/install.py kit-root --git-dir <clone of FS-GG/.github> \
  --ref coherent-set/v0.114.2 --expect-sha256 819a6cb22859455265ed2223f9ea6dbb85dfe82a98277a420c2a58539355a901 \
  --dest ~/.local/share/fs-gg/board-tend/kit-0.114.2

# 2. The empty enable file (never replaces an existing one).
python3 -I deployment/fdev-board-tend/install.py enable-file

# 3. The four user units and the wrapper; --enable also runs daemon-reload and enables both timers.
python3 -I deployment/fdev-board-tend/install.py units \
  --kit-root ~/.local/share/fs-gg/board-tend/kit-0.114.2 \
  --engine ~/.local/share/fs-gg/tools/coord-0.114.2/fsgg-coord-engine --enable
```

`units` writes exactly `~/.config/systemd/user/fsgg-board-tend{,-daily}.{service,timer}` and
`~/.local/libexec/fsgg-board-tend-run.py`, creates the working directory if absent, and touches no system path.
An existing different file is replaced only with `--force`.

An engine update is a new release: populate `kit-<new>`, then re-run `units --force` with the new paths. The old kit
root and engine stay for rollback, as `update-fsgg-coord-cli` already retains old engines.

Rollback is `systemctl --user disable --now fsgg-board-tend.timer fsgg-board-tend-daily.timer`, then deleting the
five files; nothing else was written.

## The 7-day plan-only observation

Day 0 is the first timer run. Seven full UTC days are recorded before any rule is enabled; the earliest enable is
the end of day 7. The enable file stays empty throughout and the unit stays plan-only, so no day can write to the
board.

**Recorded every day, automatically, in the private directory:**

- every run's `plan.json` (the full would-apply and would-close lists, the T1i comparison, deferrals, proposals and
  refusals) and engine summary line, under `runs/`;
- `daily/<date>.json` at 00:30 UTC: runs, post-merge runs, exit codes, skipped runs, plans unreadable, apply-mode
  runs and plans that applied or closed anything (both must be 0), the distinct would-apply effects by rule with how
  many were present in every run of the day, the would-close items with the same stability count, the T1i
  disagreement rate (last, minimum, maximum), distinct proposals and refusals;
- `daily/<date>.md`: the same as a comment body that names only `FS-GG/.github` references (any other repository may
  be private; its count is given and the references stay in the private JSON).

**Posted daily by the owner of this Delivery**, as a fresh comment on #5136 headed by the
`fsgg:board-tend-observation/v1` marker line the `.md` already carries. A comment is created, never edited by
recency; a correction is a new comment. Proposals to #4985 are the engine's own, only once `propose` is enabled.

**Health criteria for the week** (any failure restarts the count for the affected day, and a pattern goes to Root):

1. Every day has at least 20 successful scheduled runs plus every post-merge run; no exit code 3; no unreadable plan;
   apply-mode runs 0 and plans that applied or closed anything 0.
2. Each run costs at most 20 requests and 60 seconds (day-0 reference: 12 requests, about 22 seconds, 279 cards).
3. The post-merge marker never survives two consecutive successful runs.

**Criteria for enabling each rule** (Root decides; each is a separate edit of the enable file, one rule at a time,
with the next rule's observation starting after the previous one has run a clean day):

| Rule | Enable when |
|---|---|
| W1h, T1f | The 7 days' would-apply fills show an empty `preimage` in every entry (absent-only), and Root has checked the distinct entries against the native hierarchy. Start with `"propose": true` only after the HMAC key file is in place. |
| T1i | The T1i disagreement rate is at most 5% on each of the 7 days. Day 0 reads 73 of 222 (32.9%), so it stays plan-only until that changes. |
| GR | The merge gate has run in enforce mode and its custody directory holds no uncertain or malformed record. It cannot be observed in plan-only, so it joins after the first rules. |
| S3, S3b | Every distinct would-apply was checked as a strictly linked, non-Blocked, non-Done card; no entry would undo a merge-gate write; no flapping (an effect present in one run and absent in the next without a native change). |
| S4 | No would-apply names a card with any linked PR (open, draft, merged, loose); every entry is In review past `reviewSettleHours`. |
| O1 | Every distinct roll-up matches Root's manual roll-up (day 0 shows 4); none starts from Blocked, In review or Done. |
| S9, S9r | Every Backlog fill is an open card with an empty Status; every S9r Ready is a Root-authored intended-fields line. |
| W1d, T1d | Every declaration-branch fill equals the card's own declared line. |
| S1 | Every would-apply is a closed card, Status not Done, past the settle window, which the built-in workflow did not write. |
| RM1 | Not in this window: needs the enrollment input first. |
| C1 | **Its own gates, all three.** (a) A 7-day would-close observation (below). (b) The merge gate's delivery-marker check (#5161) is required, not advisory (`--delivery-marker required`), which is Root's switch after its 7 advisory days, no earlier than 2026-11-08. (c) The contract marker is read as Amended by the pinned kit root (it is, for 0.114.2). |

**C1's would-close review.** During the 7 days Root's manual sweep applies the same conditions, and the daily
would-close list is compared with it: every would-close item is one the sweep would also close (a Delivery,
populated Work level, In review, all strictly linked PRs merged, none open, last merge at least 48 hours old,
declaring `fsgg:delivery-completes/v1`, no remaining release, install or readback step in its `## Done when`); any
item the sweep would not close is a defect to repair before C1; any swept item the reconciler lacks is explained. No
Outcome and no Blocked card may appear. Day 0 shows 0 would-close, because the declaration did not exist for earlier
merges, so the week may legitimately end with few or no items. If fewer than 3 distinct would-close items have been
compared by day 7, Root either extends the observation or waives the count; this note does not assume which.

## Known limits

- The token is the credentials file's `GH_TOKEN`. Whether it carries every scope an eventual `--apply` needs is checked
  at the first apply admission, not in plan-only.
- The Done-retention archiver is a hosted workflow, outside the lock; until it moves or A1 replaces it, the reconciler
  writes nothing to archive-eligible cards (design §8).
- `Persistent=true` catches up one missed hourly run after fdev was down; it does not replay every missed hour.
