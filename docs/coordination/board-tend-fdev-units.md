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

## The 2026-11-02 option-scalar incident (#5206)

The first apply with the published0.114.3 engine returned three `send-unconfirmed` field outcomes. The original
provider bodies were not retained and remain Unknown. An offline reconstruction using the authenticated installed
assemblies reproduced all three complete requests; their `singleSelectOptionId` variables were declared `ID!`,
while the live read-only GitHub input schema reports `String`. Independent native readback found the original
Status preimages unchanged. The repaired renderer uses `String!`/`VString`. Selected field sends now retain
private response status, request/body hashes, a bounded64KiB body prefix and bounded allowlisted headers before
echo classification. Retention failure leaves the send uncertain. This does not change GR, C1 or O1.

Keep **the installed service in plan-only mode** until the corrected engine is qualified; stopping only its timer
cannot prevent a post-merge trigger from starting it. Preserve the original possible-send records. An unchanged
Status alone does not prove no effect, and no operator may delete or rewrite custody merely to unblock a retry.

The incident-specific `V2TendFieldRecovery.admit` API and checked `tools/tend-field-validation-settle.py` controller
provide a separate local no-effect settlement. This is restricted by source-pinned hashes to the three original
records, incident run, authenticated old runtime and independently reconstructed canonical requests. No private
card identities, paths, body contents or credentials are embedded in the source allowlist. Production admission
takes the actual Project3 writer lock, refreshes native schema and item/project/content/ref/field/preimage evidence,
reconstructs the entire known-invalid request and requires byte equality with the authenticated installed-renderer
proof. Arbitrary request JSON, a supplied readback or a Root string cannot admit another original.

The controller previews by default: exact qualified runtime/SDK hashes and private input modes are checked, with
no child process, provider read or certificate. Root supplies the reviewed qualified engine runtime directory,
original runtime directory, original custody and reconstructed-request artifacts, exact hashes, existing private
credentials, a fresh private output directory and the native #5206 admission-comment URL. Only explicit `--admit`
launches the typed read-only admission. Run it under the admitted one-CPU/2GiB bound. Its child is capped120seconds;
stdout/stderr/config stay0700/0600 private, and no mutation transport is ever invoked.

Success appends0600 evidence and a final certificate with O_EXCL and file/directory fsync beneath the private
custody root's `no-effect-settlements/<original-sha256>/`. The bundle joins exact original/request/runtime/schema/
fresh-readback hashes and Root's admission reference. A conflict, partial write, stale artifact or missing proof
keeps the original fenced. The original file is never replaced, renamed or marked target-verified. A valid
certificate is terminal for that original: subsequent `sweepOwn` neither re-reads nor rewrites it, including after
a later valid write changes the preimage. Missing/tampered evidence instead fences without rewriting the incident
original, even if its former target is now observed.

After settlement a corrected, separately admitted engine run derives a fresh plan and uses new run/request/custody
identities, adjacent reads, one send, retained response and native readback. It never resubmits the invalid original.
The normal coherent successor release and qualified published-engine adoption remain prerequisites; the checked
controller does not install an unpublished hotfix or replace published0.114.3 bytes. Root owns settlement and
activation/readback, separately from source integration. A local certificate establishes no native Status change.

## Related user units

### Done-retention writer (#5170)

The hosted `coord-board-archive` workflow stays a **counts-only read-only observer**. Its public logs and
Projects-read App token cannot host the guarded writer. `fsgg-board-archive.{service,timer}` are a separate daily
fdev user route, staged by `install.py archive`; this command never calls systemd, enables a timer, writes an
activation configuration or changes the hourly tend units. The Python archiver remains the only archive writer;
this does not enable A1 or any tend rule.

```
python3 -I deployment/fdev-board-tend/install.py archive \
  --release-dir "$HOME/.local/libexec/fsgg-board-archive/<reviewed-source-sha>" --dry-run
```

After review, omit `--dry-run` to stage the adapter/script (0400) and two units. Release files cannot be replaced,
even with `--force`; changed source needs a new release directory. `--force` only replaces rendered unit files.
No installed path points into a working checkout. Root reviews the exact staged hashes before any activation.

The service uses the existing fdev credentials file, with its 0600 owner check; it requests no new credentials
or grants. The child receives only the credential file's `GH_TOKEN` and the explicitly pinned `gh` executable,
never an inherited token/test override. `XDG_RUNTIME_DIR=%t`, the preserved `fsgg` runtime directory and the
absence of `PrivateTmp` preserve the archiver's per-card shared and legacy writer locks. The adapter does not
hold those locks outside the child. Type=exec, a 1860-second service bound, 1800-second child bound, one CPU and
1GiB cap bound the run; no Restart directive retries failures.

**Activation is separate and remains Root's effect, after the first real archive is accepted.** The service's
`--config` file must be an owner-owned regular 0600 file with exactly these keys:

| Key | Required value |
|---|---|
| `schema`, `enabled` | `fsgg.board-archive-config/1`, explicitly `true`; no configuration is installed by default |
| `maxArchive` | Root's positive per-run bound, at most 500; trigger is inclusive 200 active Done and retention is fixed 50, owner FS-GG/project 3/exact projectID are fixed in the adapter |
| `script`, `gh`, `binding`, `commitment`, `acceptedReceipt` | each exactly `{"path":"<absolute path>","sha256":"<64 hex>"}` |
| `readerVersion`, `readerFiles` | qualified installed reader version >=0.114.1 and a nonempty list of exact file pins for its executable/CLI/adapter closure |
| `stateDir`, `outDir`, `workdir` | absolute private runtime paths; cwd must equal the fixed non-checkout workdir |

The binding is the **current authoritative private enrollment binding**, not a discovered or historical file.
`commitment` pins its protected public enrollment commitment. Root refreshes and qualifies the pair immediately
before activation; changes to either identity or any reader file refuse until explicitly rebound. Updating the
global engine symlink does not change or qualify the timer reader. A newer global version is not evidence that
it is board-qualified. Missing reader files, stale hashes, unqualified version, stale accepted input pair or
unreadable private credentials refuse before the child starts.

The private `acceptedReceipt` is Root's accepted first-real-run evidence, not automatically fabricated by the
installer. Its exact shape is `{"schema":"fsgg.board-archive-accepted/1","accepted":true,"evidenceUrl":
"https://github.com/FS-GG/.github/issues/5170#issuecomment-<id>","inputs":{...}}`. `inputs` contains exactly
`projectId`, `keep`50, `triggerAt`200, `completeCycle`true, `maxArchive`, `readerVersion`, `readerFiles`, `scriptSha256`, `bindingSha256` and
`commitmentSha256`, all equal to the accepted activation inputs. The URL points to Root's native acceptance;
the local file retains the private identity join. The adapter verifies the pinned receipt and join locally;
it does not grant acceptance, reinterpret the comment or make an authorization decision from GitHub text.

All child stdout/stderr, card identities and fsynced custody stay in exclusive 0700 run directories with 0600
files. Journal messages contain only a generic result. No child log is published. Before starting a child, the
adapter creates and fsyncs `stateDir/in-progress.json`. A zero exit with complete settled custody (or a completed
empty plan) writes a private completion record before removing the latch. A nonzero exit, timeout, interruption,
malformed custody or pending intent keeps the latch. Every later timer activation then refuses before another
child; Root must resolve the original possible effects by readback before clearing it. Never prune unresolved
custody, clear the latch merely because time passed, or blindly resend. The receipt and latch are local
cooperative safeguards, not a boundary against other processes running as this user.

Before child launch the adapter also fsyncs the state/output directory ancestry and each new run entry in its
parent. It preprovisions and fsyncs the child's custody directory/file and private stdout/stderr entries, so
first-run directory creation cannot leave an otherwise durable latch or custody file unreachable after a crash.
Any preparation fsync failure prevents launch; a failure after latch creation preserves that hold. Successful
custody must use the archiver's exact schema and supported sequence: one run header, per-item reasoned skips or
immediately joined intent/archived pairs with complete identical identities, then one finished record. Duplicate,
unknown or malformed records refuse; the complete planned count must fit the configured bound before any send. Settlement requires every planned
item archived, zero preimage skips, and matching validated effects. A truncated prefix or skipped card keeps
the latch; Root reads original custody and native effects, then selects a fresh continuation explicitly, even
if the partial cycle has already reduced active Done below 200. No automatic resend or latch clearing occurs. An empty preprovisioned custody file is accepted only
after the pinned child exits0 and reports a completed empty plan.

Root's remaining sequence is: integrate reviewed ranking source; review the nonzero native plan; admit and
accept one real guarded archive with enrollment-reader readback; qualify this scheduling adapter and exact
activation pair; stage disabled and read back hashes/modes/units; then explicitly daemon-reload and enable
`fsgg-board-archive.timer`. Read back its next trigger and the first scheduled run's private custody/native
effects before accepting #5170's schedule promise. The timer is daily 04:43 UTC, Persistent=true (one catch-up,
no backlog replay). Disable the timer and set `enabled:false` as the kill switch, preserving all custody.

The November 3 user policy supersedes the original keep-200/strictly-over-200 policy. Original eight-card
acceptance and the old-policy scheduled result remain historical evidence; neither qualifies keep-50 operation.
Guarded older blockers or open/unrankable active Done rows block the scheduled cycle before writes and
report the held remainder; the adapter does not claim the target was met. Protected rows already inside
the newest 50 count toward that retained set.
The new accepted receipt binds both threshold and retention plus the complete-cycle requirement. Existing
activation inputs deliberately refuse under changed source until Root selects a new qualified receipt/config.

The existing 500-item board cap gives at most 450 archive candidates (500 minus 50). A scheduled cap of 450
covers every admissible complete plan; a smaller cap refuses before writes when insufficient. At most ten
initial GraphQL page requests plus three requests per candidate give a conservative 1360-request ceiling
(460 for a 200-Done/150-archive cycle). Each request retains its 120-second timeout, while the independent
1800-second child deadline stops the whole process group and retains unresolved custody. Thus slow execution
can stop for Root recovery; the bound is not a promise that every 450-card cycle finishes within 30 minutes.
A successful maximum cycle appends 902 fsynced custody records (run + 450 intent/readback pairs + finished),
plus fixed private adapter state/output entries. Output and custody are retained; no deletion/pruning is added.

Preflight is the offline recording-child/archiver fixture: positive exact pins, disabled/stale inputs,
environment/output privacy, actual shared-lock contention, successful settlement and failure/crash/timeout
blocking the next invocation. These directly exercise the effect boundary; no new abstract scheduler model or
browser/CLR run is needed. Run `bash tests/fdev-board-tend/run.sh` and the existing archive fixture under the
ordinary resource budget with disk-backed temporary storage. This is safeguard qualification, with no claim
of measured CI savings or native acceptance.

`fsgg-tmp-cleanup.{service,timer}` (hourly dead-run `/tmp` cleanup, #5240) are templated here and installed by their own
`install.py tmp-cleanup` effect; they share nothing with board tend at runtime. See
[fdev host hygiene](fdev-host-hygiene.md).

## Known limits

- The token is the credentials file's `GH_TOKEN`. Whether it carries every scope an eventual `--apply` needs is checked
  at the first apply admission, not in plan-only.
- The hosted Done-retention observer sends no writes. The sibling fdev writer remains inactive until Root accepts
  and activates it; tend still writes nothing to archive-eligible cards until its own design/acceptance changes.
- `Persistent=true` catches up one missed hourly run after fdev was down; it does not replay every missed hour.
