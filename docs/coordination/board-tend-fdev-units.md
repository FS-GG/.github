# `board-v2 tend` on fdev: pinned units, guarded activation and ongoing observation

Refs #5136. This is the operating note for [board-reconciler design](2026-10-31-board-reconciler-design.md) §8 and
§12 PR 8. The design decides what the reconciler does; this note says how it is run on fdev, what is pinned, what
an install changes and how current qualification and continuing observations are judged. The source is
`deployment/fdev-board-tend/`; `tests/fdev-board-tend/` renders and exercises all of it offline.

## What runs

| Unit | When | What |
|---|---|---|
| `fsgg-board-tend.timer` | hourly at minute 07, `Persistent=true`, up to 120 s random delay | starts `fsgg-board-tend.service` |
| `fsgg-board-tend.service` | the timer, and the post-merge trigger below | `fsgg-board-tend-run.py run --mode <installed mode>`, which runs `fsgg-coord-engine board-v2 tend` |
| `fsgg-board-tend-daily.timer` | 00:30 UTC | starts the daily record service |
| `fsgg-board-tend-daily.service` | the daily timer | `fsgg-board-tend-run.py summarize --date yesterday`: reads the private run index, writes the daily record. Local and read-only; it posts nothing |

Minute 07 is away from the drift observer's :23 cron, its :53 fdev dispatch and the 04:43 archive.

### What is pinned

- **`Environment=FSGG_KIT_ROOT=<kit root>`.** `board-v2 tend` reads the contract marker `fsgg:board-tend-contract/v1`
  from `lifecycle-contract.md` under `FSGG_KIT_ROOT`, else the process's git toplevel. The hardened wrapper requires `--workdir` and `--expect-kit-sha256`, checks both mirror hashes at every run,
  and rejects a kit root or workdir inside a checkout (#5206). The kit root is
  `~/.local/share/fs-gg/board-tend/kit-<version>/`: exactly the two contract mirrors of the released tag, written
  read-only and verified against the released contract's sha256 (`install.py kit-root`). It is never a checkout.
- **`WorkingDirectory=~/.local/share/fs-gg/board-tend`.** A fixed directory that is not a git checkout. The wrapper
  refuses to run if it was started anywhere else, so even an unset `FSGG_KIT_ROOT` finds no toplevel and keeps every
  gated rule refused.
- **The engine path is the versioned one** (`~/.local/share/fs-gg/tools/coord-<version>/fsgg-coord-engine`), not the
  moving `~/.local/bin/fsgg-coord-engine` link, and the kit root must carry the same `<version>`. An engine update
  (`update-fsgg-coord-cli`) therefore changes nothing under the timer until Root re-runs the installer for the new
  release. The wrapper probes the real executable with `--version` before any board command; its three release
  components must match `coord-<version>` and `kit-<version>` (an assembly revision of zero is accepted).
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
- **Config/state homes are explicit.** The installer captures its `XDG_CONFIG_HOME` and `XDG_STATE_HOME`
  (or HOME defaults), with optional absolute `--config-home`/`--state-home` overrides. Both units render the same
  paths into their environment and wrapper arguments; the wrapper refuses disagreement. The engine enable file
  must be `fsgg/board-tend.json` under that config home. The merge gate uses the same XDG state home for its marker:
  Root must launch the gate with the installed home pins, including when custom paths are used. A wrong-mode
  state directory refuses loudly in the journal; the wrapper does not silently repair or bypass it.
- **Plan-only is the installer default.** Only explicit `--mode apply` adds the engine's `--apply`. Moving to apply is a separate Root
  effect: re-run `install.py units --mode apply --force`, which refuses unless the enable file already exists with
  mode 0600 and names at least one qualified rule. Deleting the enable file, or `"apply": []`, is still the kill switch.

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
2. Under the wrapper run lock, the pending marker is atomically renamed to `pending-post-merge.claimed` and
   its directory fsynced before work. Only that claimed name is deleted after a successful run; a concurrent
   trigger writes a fresh pending name that is never unlinked by the completed run. A crash or failed run keeps
   the claim, which bypasses the minimum interval on recovery.
3. A new pending marker observed at the end causes another pass (up to three); one arriving during final claim
   deletion remains pending for the next activation. If the unit is active, `systemctl start` is a no-op, but it
   cannot erase the durable pending marker. Failed runs keep both existing claim and any concurrent pending marker.
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
satisfied; each rule is still enabled by Root alone, one at a time, after the current qualification below. The ungated rules
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
  --engine-path ~/.local/share/fs-gg/tools/coord-0.114.2/fsgg-coord-engine \
  --expect-kit-sha256 819a6cb22859455265ed2223f9ea6dbb85dfe82a98277a420c2a58539355a901 --enable
```

`units` writes exactly `~/.config/systemd/user/fsgg-board-tend{,-daily}.{service,timer}` and
`~/.local/libexec/fsgg-board-tend-run.py`, creates the working directory if absent, and touches no system path.
An existing different file is replaced only with `--force`.

An engine update is a new release: populate `kit-<new>`, then re-run `units --force` with the new paths. The old kit
root and engine stay for rollback, as `update-fsgg-coord-cli` already retains old engines.

Rollback is `systemctl --user disable --now fsgg-board-tend.timer fsgg-board-tend-daily.timer`, then deleting the
five files; nothing else was written.

## Current qualification and continuing observation

On 2026-11-02 the user explicitly requested permanent automatic board reconciliation. That instruction replaces
this note's previous elapsed seven-day plan-only/advisory waits with **current qualification before activation**.
It does not waive native guards, unresolved effects, rule-specific prerequisites, exact-source review or installed
readback. #5206 supplies the wrapper/installer hardening required before applying gated rules. Root owns the live
configuration and activation; a source candidate or installer default is not evidence that automation is enabled.

Before activation Root checks the exact installed wrapper/engine/kit identities, the offline refusal and marker
race/recovery controls, a complete current native plan and the chosen rule subset against its native preconditions.
The published engine and a separately reviewed/tested local wrapper candidate retain distinct identities until
normal source PR/CI integration completes. A failed population read stays failed/Unknown and grants no write.
Elapsed days or a successful read alone do not qualify an unsafe rule.

The current authorization permits Root to enable the qualified subset together and keep its timers permanently
active. It does not require enabling every nameable rule. Keep any rule with an unsatisfied input, unsafe adjacent
read or unresolved effect disabled; do not broaden the set as a side effect of changing the wrapper. Root's native
acceptance records the chosen set and its qualification. The enable file still controls allowed rules and the bound;
missing/invalid/empty activation keeps the engine plan-only. Disabling the timers and clearing apply rules is the
kill switch, preserving original custody and pending markers.

| Rule | Technical qualification before enable |
|---|---|
| W1h, T1f | Current fills have empty preimages and match the native hierarchy; proposal posting separately needs the HMAC key and its admission. |
| S1 | Closed native content, non-Done current Status and the settle window/adjacent re-read all qualify. |
| S3, S3b | Strict linked-PR evidence and current non-Blocked/non-Done preimages qualify; preserve merge-gate writes. |
| S9, S9r | Open card with empty Status for the Backlog fill; S9r additionally needs Root-authored intended-fields evidence. |
| T1i | Current Roadmap parity and disagreement evidence qualify; it remains held while this is unknown or outside the accepted threshold. |
| S4 | No linked PR of any state and settled In review qualify. |
| O1 | Adjacent child and parent state must qualify the roll-up; absent required child re-reads are a hold. |
| W1d, T1d | Current native declarations qualify every proposed fill. |
| RM1 | Requires its authoritative enrollment input; no removal without it. |
| GR | Enforced gate and complete resolved gate custody qualify replay. |
| C1 | Required delivery-marker enforcement, amended installed contract and current would-close review must all qualify; no remaining release/install/readback promise may close. |

C1's current review retains the actual closure guards: populated Delivery/Work level, In review, strictly linked
PRs all merged with none open, last merge beyond the existing 48-hour settle, `fsgg:delivery-completes/v1`, and
no remaining Done-when obligations. No Outcome or Blocked card may close. A missing comparison or unmet native
gate remains a hold; the user authorization replaces elapsed waiting rather than making those assertions true.

Every run and the 00:30UTC daily record remain private: complete plan, engine summary, exit code, apply-mode
and applied/closed counts, distinct/stable effects, refused inputs and T1i comparison. The daily summarizer counts
malformed plans as unreadable and excludes them from aggregates instead of crashing or treating them as empty.
Both daily units are verified with the main units by the offline fixture. The generated comment body names only
FS-GG/.github references; other references stay in private JSON. The observer itself posts nothing.

Root monitors scheduled/post-merge outcomes, request/runtime costs and pending markers continuously. No
unresolved send, incomplete plan or unreadable custody can become success through a retry. Failed reads and
safeguard refusals are retained and investigated; hold the affected rule or activation when its safety is Unknown.
This source change performs no installation, timer activation or board write.

## Related user units

`fsgg-tmp-cleanup.{service,timer}` (hourly dead-run `/tmp` cleanup, #5240) are templated here and installed by their own
`install.py tmp-cleanup` effect; they share nothing with board tend at runtime. See
[fdev host hygiene](fdev-host-hygiene.md).

## Known limits

- The token is the credentials file's `GH_TOKEN`. Whether it carries every scope an eventual `--apply` needs is checked
  at the first apply admission, not in plan-only.
- The Done-retention archiver is a hosted workflow, outside the lock; until it moves or A1 replaces it, the reconciler
  writes nothing to archive-eligible cards (design §8).
- `Persistent=true` catches up one missed hourly run after fdev was down; it does not replay every missed hour.
