# shellcheck shell=bash
# fsgg-coord-guards.sh — the coordination knowledge `scripts/fsgg-coord` holds, kept OUT of the kit.
#
# THIS FILE IS SOURCED, NEVER EXECUTED. `scripts/fsgg-coord` loads it at tier 2/2b — and ONLY there —
# then calls `dirty_guard` and `stale_guard`. It defines those two, the `upstream_drift` probe they
# share, and the verb partition they decide with. It has no shebang and no exec bit for that reason.
#
# WHY IT IS A SEPARATE FILE (.github#1586). `scripts/fsgg-coord` is a `coordination-kit` row
# (`registry/repos.yml`, `id: fsgg-coord`), so it is CONTENT: every byte of it is mirrored into all
# seven receivers, and every edit to it obliges a kit republish which stales all seven. That bill was
# measured — six times in one day (#1507, #1517, #1523, #1528, #1534, #1535), and #1535's worker had to
# bump `FS.GG.Kit` 0.8.0 → 0.8.1 and republish, moving six receivers from current to stale in one step.
#
# THREE OF THOSE SIX WERE THIS FILE'S SUBJECT. #1528 (the write-verb partition), #1534
# (`command-contract`'s write-ness) and #1535 (`next`'s chore lock) changed nothing but the verb sets
# and the prose around them. Nothing a receiver executes changed, because a receiver never reaches this
# code at all — see the next paragraph. They republished the kit to mirror a decision into seven repos
# that structurally cannot act on it.
#
# AND IT IS DEAD CODE IN EVERY RECEIVER, WHICH IS THE WHOLE ARGUMENT. `stale_guard` and `dirty_guard`
# are reached from exactly two places, both guarded by `[ -x "$(engine_path …)" ]` — tier 2 (this
# checkout's source build) and tier 2b (the shared checkout's). Only the repo that owns coord's SOURCE
# can have `src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine` under its toplevel, so a receiver
# resolves at tier 1/3/4 and never loads this file. The shim's own header has said so for its whole life
# ("the receivers' shape has no `src/` beside it to be stale against, and never reaches here"); what
# #1586 adds is that if it never runs there, it need not SHIP there.
#
# SO THE SPLIT IS BY DISTRIBUTION, NOT BY TASTE. What a receiver executes stays in the kit row and is
# now stable; what only `.github` executes lives here, has no kit row, and is therefore not kit content
# at all — `src/FS.GG.Kit/stage-kit.sh` stages exactly the `kit:` rows of `registry/repos.yml` and
# nothing else (ADR-0058: derive, don't restate), so a file with no row cannot be packed. Editing the
# partition is now a `.github` commit and nothing more: no republish, no seven-receiver fan-out.
# `tests/coord-engine-parity/shim.sh` §3f MEASURES that rather than asserting it.
#
# THE OTHER DOOR IS NOW SHUT TOO (.github#1615, 2026-07-28, ADR-0068).
#
# THIS PARAGRAPH USED TO SAY, and the wording is kept because it records what was true: *"WHAT THIS
# DOES NOT FIX, SAID PLAINLY (#1586's filer correction, and criterion 5 with it). The other three of
# the six — #1507, #1517, #1523 — were ENGINE changes, and they oblige a republish through a different
# door: `dist/dotnet/.config/dotnet-tools.json` pins the engine's version and IS a kit row… The fan-out
# is REDUCED by this change, not removed."*
#
# #1615 removed it. `coord-engine-manifest` is no longer a `kit:` row: the engine's version reaches
# receivers through Renovate's nuget manager (`/(^|/)dotnet-tools\.json$/` is one of its four shipped
# `managerFilePatterns`), so an engine bump edits no kit content, stales no `registry/repos.lock`, and
# obliges no republish. With the partition split above, BOTH doors are shut and #1586's criterion 5 —
# retired as unachievable between #1586 and #1615 — is meetable again.
#
# #1077's INVARIANT DID NOT GO WITH IT, and that was the substance of #1615 rather than a formality.
# "Receives the tool ⇒ can run the tool" used to be true BY CONSTRUCTION (two rows, one fabric,
# `repos.sh validate` refusing to separate them). It is now ASSERTED: `scripts/repos-audit.sh`'s
# engine-manifest sweep reads every `coordination-kit` receiver's own `.config/dotnet-tools.json` daily
# and reds unless it declares `fs.gg.coord.cli`. Strictly stronger — the old rule read only this repo's
# roster, so a receiver that deleted its manifest by hand stayed green forever.
#
# `tests/coord-engine-parity/shim.sh` §3f legs (c) and (d) MEASURE both halves rather than asserting
# them: (c) that the manifest has no kit row, (d) that the replacement sweep exists.
#
# AN ABSENT GUARD IS NOT A CLEAN BILL OF HEALTH. The obvious failure of a split like this is that the
# loaded half goes missing and the guards silently stop existing — #266's signature, which this file
# warns about three separate times below (`find -quit`, `status.showUntrackedFiles`, `--no-optional-locks`)
# and which it must not rebuild in its own loading. So the shim does not `[ -f ] && source`: it sources
# only where a source build RESOLVED, and if the file is not there it REFUSES (exit 69) rather than
# execing unguarded. A receiver is unaffected because a receiver never asks.

# IT DECIDES TWO THINGS ABOUT COORDINATION, AND ONLY BECAUSE NOTHING ELSE CAN (#929, #709). The verb
# PARTITION below names which verbs write shared state, so a STALE engine can be refused one; and an engine
# built from UNCOMMITTED source is warned about. That is coordination knowledge in a pipe that is meant to
# hold none, and the trade is deliberate: neither check can live in the engine, because the suspect engine IS
# the thing being checked — an engine built before the check existed would not run it, which is exactly
# how a fix for #914 sat in `src/` while the fleet ran without it. The resolver is the last place that
# sees both the artifact and the source before the exec.
#
# WHAT THE ENGINE CANNOT BE ASKED, AND WHY THE ANSWER IS A TEST (.github#1528). The knowledge duplicated
# here is a property of the engine, so the obvious repair is to DERIVE it — have the engine emit its own
# write surface (`command-contract` already emits the verb surface, by reflection over the `Command` union)
# and read that here instead of enumerating. It does not work, for the reason the paragraph above already
# gives, and it fails in the WORST direction. The engine that would answer is the STALE one: an engine built
# before a `writes` field existed emits no such field, the shim derives an EMPTY write-set from it, and
# every board write is permitted — on precisely the oldest artifacts, which are the dangerous ones. A guard
# that needs a current engine to decide whether the engine is current is circular, and it fails OPEN. (Two
# lesser reasons, both real: it puts a second `exec` of a suspect binary on the hot path of every single
# invocation, and a stale engine that hangs or crashes then takes the guard down with it.)
#
# So the enumeration stays, and what changes is that it can no longer be SILENTLY incomplete. The verbs are
# partitioned into three named sets that must, between them, cover the engine's whole command surface;
# `tests/coord-engine-parity/shim.sh` §3b reads `command-contract --json` off a freshly built engine and
# REDS if any verb it advertises is in none of them, or if any set names a verb the engine does not have.
# A verb added to the engine therefore arrives UNCLASSIFIED and fails the build until somebody decides
# about it. That is the whole fix: the list is still written by hand, but forgetting it is no longer quiet.
# §3c then drives every verb in every set against a stale fixture engine, so the partition is behaviour
# rather than a string — a verb moved between sets without meaning it reds too.
#
# The list was wrong when this was written, and in both directions, which is the argument for the gate
# rather than for a longer string: `set-paths` reached the same `Writes.widen` PATCH as `widen` through
# the same `updateTouchSet` helper and was absent; `room` (`Writes.createRoom` + `Writes.writeRoomRef`) and
# `reconcile` (`Board.boardWrite`) were absent; and `bootstrap`, which the filing believed created the
# project, is a READ (`Board.bootstrap`: *"Resolve the board and its field/option ids. Two GraphQL calls"*)
# that adding would have made a stale engine refuse for nothing.
#
# The two are one question asked at two joints — "is the code about to run the code this checkout says?"
# `stale_guard` compares the ARTIFACT to the source; `dirty_guard` compares that SOURCE to HEAD. Neither
# subsumes the other, and a build satisfies the first while defeating the second (see #709 below).
#
# `stale_guard` NOW ASKS "IS IT ON MAIN?" — IN ONE SCOPED DIRECTION, AND `dirty_guard` STILL DOES NOT
# (.github#1549). This paragraph used to say NEITHER did, and recorded the gap as deliberately unclosed:
# *"the honest comparison is against `origin/main`, which is a network read this resolver must not do on
# every invocation, and which would fire on every checkout that is merely BEHIND — the normal state of a
# shared checkout nobody fetches."* Both objections were right. Neither was fatal, and the bill for
# treating them as fatal came due: #1549 REPRODUCED #1507 — a `--json` token written into a live claim's
# `Paths:` line — ninety minutes after this repo closed #1507, with the fix (`ff088fe`, #1516) sitting on
# `main` the whole time. The shared checkout every worker's engine resolves through was 14 commits behind
# `origin/main`; its built engine was a FAITHFUL build of that older source. Artifact and source agreed.
# Both lagged `main`. The guard was silent, and it was silent *correctly* — it was asked the wrong
# question. A second worker hit the same defect on the same board the same day (#1549's first comment).
#
# So the question below is no longer "is this binary older than the source next to it?" alone. It is also
# "has the default branch moved past the commit this engine was built from, IN THE ENGINE'S OWN SOURCE
# TREES?" — and the two objections above are ANSWERED rather than dodged:
#
#   NO NETWORK, EVER. `upstream_drift` reads the ALREADY-FETCHED remote-tracking ref and never runs
#   `git fetch`. That is not a compromise on the honest comparison, it is how the honest comparison is
#   affordable: a linked worktree SHARES the common dir's refs, so every worker's own `git fetch origin
#   main` — which pnext-item §2 makes them run to branch off it — refreshes the SHARED checkout's
#   `origin/main` for free, from a different working tree, before they do any work. #1549's corroborating
#   comment measured exactly that and preferred it for exactly this reason.
#
#   NOT "MERELY BEHIND". The bar is drift under `src/FS.GG.Coord.{Cli,Core,GitHub}` — the trees whose
#   commits ship in the engine, scoped as `scripts/check-engine-freshness.py`'s ENGINE_SOURCE already
#   scopes them. A checkout behind on docs, workflows, skills or registry rows is not running different
#   code and is not stopped. "Halting every worker whenever `main` moves" was the thing to avoid, and
#   avoiding it is a question of SUBJECT, not of refusing to ask.
#
# WHAT IT COSTS, SAID PLAINLY, BECAUSE SOMEBODY WILL PAY IT MID-ITEM. This engine is edited often —
# `check-engine-freshness.py` measured 33 engine commits in the 8 hours after one release — so a worker
# whose item outlives an engine merge WILL be refused a `heartbeat` or a `done` and told to update and
# rebuild. That is a stall of about a minute, on a remedy that is local, cheap and theirs, and it is the
# same trade #929 already made for the mtime half. The alternative is #1507's defect recurring on a live
# claim, which is what happened while this comment said the gap was deliberately unclosed.
#
# `dirty_guard` is unchanged and still asks only about the local checkout, so half of the retired sentence
# survives verbatim: a worker who COMMITS kit WIP on a branch here defeats it, and that gap stays open.
# What does not survive is the claim that a shared checkout sitting behind `origin/main` is invisible to
# both — it was the observed bug, twice in one day, and it is now refused.

# THE ARTIFACT IS NOT A REF (#929). Tier 2 execs a BUILD OUTPUT, and nothing keeps it in step with the
# `src/` beside it: `git pull` does not rebuild it, `git status` cannot see it, and `--version` answers
# the same assembly version from every build ever made — `0.2.0.0` when this line was written, `0.3.0.0`
# today, and never once a per-commit answer, which is the whole point rather than a detail that rotted.
# (#1018 measured exactly that: a global tool and a current `src/` both said `0.3.0.0`, hours apart in
# build time.) So a worker can run — and WRITE TO THE BOARD WITH — code that is
# not in their own tree. Twice on 2026-07-16 a stale engine silently ignored `release --status`, putting
# a merged item back to `Ready`: #914's own fix, absent from an engine whose `src/` contained it.
#
# READS WARN, WRITES REFUSE. A stale read misinforms one worker; a stale board write corrupts state the
# whole fleet shares, and twice it did. Refusing every stale exec instead would halt every worker the
# moment anyone touches `src/`, on a repo whose premise is N workers in one checkout.
#
# ONLY THE SOURCE BUILD, AND STRUCTURALLY SO — BUT THE STRUCTURE HAD TO BE BUILT, AND #1018 IS THE BILL
# FOR ASSUMING IT. This paragraph read: "the receivers' shape has no `src/` beside it to be stale against,
# and never reaches here — tiers 1-3 `exec` first." Every clause was TRUE. The inference was not: it needs
# "a checkout WITH a `src/` beside the binary therefore resolves this tier", and under the old order it did
# not — a global tool exec'd first whether or not `src/` was there. So the one shape that CAN be stale was
# the one shape that never reached the guard, and the sentence justifying the scoping named the very thing
# defeating it ("tiers 1-3 `exec` first") as its reason to be safe. It described the code its author meant
# to write. It is true NOW because the source build IS tier 2, and only tiers 3/4 — the receivers' shape,
# which has no source to be stale against — sit below it. An explicit FSGG_COORD_ENGINE_BIN is untouched
# either way: it is an instruction, and honouring it is tier 1's rule.
#
# THE VERB IS ALWAYS `$1`, and that is the engine's rule rather than an assumption of ours: it dispatches
# on argv[0] and REFUSES a flag before the verb (`unknown command: --repo`). So reading `$1` is exactly as
# strict as the engine's own dispatch — there is no accepted invocation that hides a write verb elsewhere,
# which is what would make this guard fail OPEN. Matching any argv token instead would fail the other way,
# on values: `ready --status Done` is a read, and `Done` is a write verb's name.
#
# THE SETS ARE UNPADDED AND JOINED WITH ONE SPACE AT THE POINT OF USE, which is not a style choice. The
# match below is `*" $verb "*`, and `$verb` is EMPTY for a bare `scripts/fsgg-coord` (which the engine
# parses as `--help`). Two sets each carrying their own outer spaces would concatenate to a DOUBLE space,
# the empty verb's pattern would be `*"  "*`, and a bare help invocation would be refused as a board write.
# One space between, spaces added once around the whole, and no pattern for an empty verb can match.

# WRITES, UNCONDITIONALLY — every invocation of these can mutate state the whole fleet shares.
BOARD_WRITES="add adopt child claim done flush heartbeat release room say set-field set-paths take widen"

# WRITES ONLY UNDER A CONDITION — and the flat list above CANNOT say that, which is how it got wrong.
# These are refused all the same, verb-level and fail-closed, and they are held in a set of their own so
# the fact stays recorded rather than laundered into "these are write verbs":
#
#   reap        — writes only under `--apply`; the bare form is a DRY RUN (`Client.fs`, `if not opts.Apply`).
#   reconcile   — the same shape: `--apply` writes board columns via `Board.boardWrite`, bare is a dry run.
#   next        — writes under no flag at all. AFTER printing its answer it makes the #733 chore OFFER
#                 (`offerChoreAtNext` → `Chores.offer` → `Writes.claim`), which POSTs a claim marker to take
#                 the repo's chore lock. `.github`'s lock is #1033, so this fires in the repo that owns this
#                 shim. `batch` is the same decision uncapped and makes no offer, so it stays a read.
#
#                 AND THAT IS SETTLED, NOT PENDING (.github#1535). This row recorded a fact that `next`'s
#                 own contract contradicted: the recipe and the exit-code table named it as the READ to run
#                 when nothing is schedulable. #1535 decided BETWEEN declaring the write and moving the
#                 offer elsewhere, and declared it — `AtNext` is the only chore boundary that fires on a
#                 DRAINED board (`AfterDone` needs somebody to have finished an item, and on a board wedged
#                 behind cleared blockers nobody has), so moving it would delete the one boundary that can
#                 unwedge #1047's deadlock. The refusal below is therefore correct rather than a cost being
#                 tolerated, and the "it costs the fleet its diagnostic verb" worry #1528 recorded is not
#                 real: `batch --text -n 1` is this same decision minus the offer, it is in BOARD_READS, and
#                 it prints the identical ANSWER out of the engine's own single spellings — the headline from
#                 the one `nothingSchedulable` literal, the reasons and #428's banner from the one
#                 `sayPassedOver` (`--text` is part of the spelling — `batch` defaults to JSON, which prints
#                 `[]` and puts the reasons on stderr). NOT the same STREAM, and .github#1562 is why: it took
#                 `next`'s empty arm off the shared `printChosen` so the headline could go to STDERR, keeping
#                 `next`'s stdout the bare-ref machine contract, while `batch --text` still prints it to
#                 STDOUT through `printChosen`. Same words, same exit 0, different stream for that one
#                 line — which costs nothing to a caller READING the diagnostic, and everything to a
#                 caller CAPTURING stdout. Point a worker on a stale engine at that; and
#                 `tests/coord-engine-e2e/writes.sh` pins
#                 both halves against one board (`batch` leaves #1033's thread empty, `next` posts a marker
#                 to it), so this comment cannot quietly stop matching the engine.
#
# WHY VERB-LEVEL RATHER THAN ARGUMENT-AWARE. `--apply` is a bare token in the parser (no `--apply=X` form),
# so scanning argv for it would in fact be exact today — but it buys a permitted dry run at the price of a
# guard whose correctness depends on argv SHAPE, and the paragraph above already refuses that trade for the
# verb ("matching any argv token would fail on values"). The two directions are not symmetric: a missed
# `--apply` is a stale engine writing the board, and a spurious one is a refused dry run whose remedy is the
# `dotnet build` this guard already prints. `reap` has been refused verb-level for its whole life on exactly
# that reasoning, and `next` cannot be argument-aware at all — its write is gated on board state, not on a
# flag. Take the refusal. If a permitted stale dry run is ever wanted, the honest way is for the engine to
# declare per-invocation write-ness, not for this shim to re-implement the parser.
BOARD_WRITES_CONDITIONAL="next reap reconcile"

# READS — permitted (with a warning) on a stale engine, because a stale read misinforms ONE worker where a
# stale write corrupts what the fleet shares. This set exists so the partition is total: it is the half that
# makes "unclassified" detectable, and every member was checked against the code rather than its name.
#
#   No handler here reaches `Board.boardWrite`/`boardWriteBatch`/`setField`/`setFieldBatch`/`addItem`/`flush`
#   or any `Writes.*` mutation, and none APPLIES a writing handler — the only inter-handler write calls in
#   `Client.fs` are `adopt`→`claim`, `take`→`claim` and `widen`/`set-paths`→`updateTouchSet`, all above.
#   `board`/`bootstrap`/`field-id`/`option-id`/`item-id` are GraphQL QUERIES plus a per-user cache file under
#   `$XDG_CACHE_HOME/fsgg-coord` — local, not the board. `decide`/`lanes`/`facts` never touch the transport
#   at all; `whoami`/`followup`/`predicate` never touch the board (identity, a local queue, local registry
#   files). `scan` POSTs to `graphql`, which is how a GraphQL READ is spelled.
#
# `--help`, `-h` and `--version` are deliberately absent: they are not verbs, and the engine's
# `command-contract` excludes them for the same reason. They are exempted by GRAMMAR at the guard below
# ("begins with `-`, or is empty"), NOT by being listed here — because an unclassified token is REFUSED
# there, not permitted, so a list of flag spellings is a list that could be left short.
#
# ONE LINE, AND NO SUBSTITUTION IN IT — the parity gate lifts these three assignments straight out of this
# file and refuses any that is not a plain literal, so a `$…`/`$(…)`/continuation spelling would red §3b
# rather than be evaluated. Long, therefore, on purpose.
BOARD_READS="batch board bootstrap budget command-contract decide diff-audit driver facts field-id followup inbox issues item-id landable lanes lint option-id overlap predicate ready scan verify-paths who whoami"

# STAT THE .dll, NOT THE APPHOST. `fsgg-coord-engine` is the .NET apphost — a fixed ~78 KB native stub,
# BYTE-IDENTICAL across every build of every commit. The IL, which is the thing that goes stale, is in
# `fsgg-coord-engine.dll` beside it. A guard that measured the apphost would be green on this exact bug.
#
# THE CLI's .dll IS THE WHOLE GRAPH'S BUILD STAMP. Cli references Core and GitHub, so a change anywhere
# under `src/` forces its recompile (verified: touching Core/Protocol.fs alone moves it). So comparing
# every HAND-WRITTEN `src/**/*.fs` against it neither false-greens on a Core-only edit nor false-STALEs
# after a real build — the false-stale being the one that would halt the fleet.
#
# "HAND-WRITTEN" IS THE WORD THAT WAS MISSING, AND ITS ABSENCE WAS THE FALSE-STALE THE SENTENCE ABOVE
# CLAIMS TO PREVENT (.github#1572). MSBuild GENERATES `.fs` under `src/` — one pair per project per
# configuration: `<proj>/obj/<cfg>/net10.0/<proj>.AssemblyInfo.fs` and, beside it,
# `.NETCoreApp,Version=v10.0.AssemblyAttributes.fs`. They are `*.fs`, they live under `src/`, and an
# unfiltered `find` counted them. So building or testing in a configuration OTHER than the one this guard
# measures re-stamped them, and a plain `dotnet test` made the guard report STALE against a CURRENT
# Release binary over a tree with zero edited sources — REFUSING every board write at exit 69, citing an
# assembly-attribute stub no human wrote (measured on this host: `heartbeat` refused). It landed on
# precisely the worker following pnext-item §2, because running the tests is what re-stamps them; and the
# remedy it printed cleared it only until the next `dotnet test`, which teaches the fleet to ignore the
# one instruction REAL staleness depends on being obeyed. A comment asserting a property the code does
# not have is itself the defect (#1059's class), so both are repaired here rather than only the prose.
#
# The repair is to measure only files that are INPUTS to the build: `obj/` and `bin/` are OUTPUTS, and
# nothing under them is a source the engine could be behind. PRUNED, not filtered afterwards — the scan
# stays bounded, and `-prune` must sit on the LEFT of the `-o` or it prunes nothing.
#
# PORTABLE `find`, AND THAT IS NOT PEDANTRY — IT IS THIS GUARD'S OWN FAILURE MODE. The obvious spelling
# ends `-print -quit`, stopping at the first hit. `-quit` is GNU-only: BSD/macOS find REFUSES it, and
# since the probe must swallow stderr (a missing src/ is a legitimate no-op, not an error), the refusal
# is invisible — the probe yields nothing, "nothing" reads as "not stale", and the guard SILENTLY never
# fires. Measured: with a -quit-refusing find on PATH, a board write ran on a genuinely stale engine, no
# warning, no refusal. That is the #266 signature this guard exists to END, rebuilt inside the guard. So
# no `-quit`: `head -1` bounds it instead, and the scan is ~30 files. `\( … \) -type d -prune -o … -print`
# is the same POSIX vocabulary and adds no GNU extension, so the pruning cannot resurrect that hole.

# THE TREES WHOSE COMMITS SHIP IN THE ENGINE (.github#1549) — the same three
# `scripts/check-engine-freshness.py`'s ENGINE_SOURCE names, and deliberately so: #1549's criterion is
# drift "in the engine's source trees, as `check-engine-freshness.py` already scopes it". A commit
# outside them cannot change the IL the fleet execs, so counting it would halt board writes for noise.
#
# NARROWER THAN `dirty_guard`'s PATHSPEC, AND THE ASYMMETRY IS DECIDED RATHER THAN OVERLOOKED. That guard
# also watches `Directory.Build.props`, `Directory.Packages.props`, `global.json` and this shim, and those
# genuinely do reach the compiled engine. They are absent here because the two guards spend different
# money: `dirty_guard` WARNS, so a wide subject costs a line of stderr, while this one REFUSES every board
# write in the fleet, so a wide subject costs an outage on the morning somebody bumps a package pin on
# main. `check-engine-freshness.py` draws the same line for the same reason and REPORTS sub-threshold
# drift instead of reddening on it. Stated as a miss rather than left implied: a shared checkout behind
# ONLY on a package pin or an SDK band is not caught here, and `dirty_guard` will not see it either once
# it is committed upstream.
ENGINE_SOURCE_TREES="src/FS.GG.Coord.Cli src/FS.GG.Coord.Core src/FS.GG.Coord.GitHub"

# HAS THE DEFAULT BRANCH MOVED PAST THE COMMIT THIS ENGINE WAS BUILT FROM? (.github#1549)
#
# THE REF IS READ, NEVER FETCHED — see the header. A `git fetch` on every invocation is the cost this
# resolver refused for a year, and it is not needed: worktrees share the common dir's refs, so the fleet's
# own §2 fetches keep the shared checkout's `origin/*` current from the outside.
#
# THE DEFAULT BRANCH IS ASKED FOR, NOT ASSUMED. `refs/remotes/origin/HEAD` is the recorded answer (git
# clone writes it); `main` and `master` are the fallbacks, because a repo initialised locally and given a
# remote may never have had it written. Each candidate must RESOLVE TO A COMMIT before it is believed — a
# dangling symref is not a comparison point, and taking its name on faith would put the whole verdict
# behind a `rev-list` that silently fails.
#
# NO SUBJECT vs NO ANSWER, AND A REMOTE IS THE DIFFERENCE. A checkout with no `origin` has no recorded
# default branch to be behind — the parity fixtures, a `git init` scratch tree, an air-gapped clone — and
# that is the same shape as "no IL to measure against" above and `dirty_guard`'s "no HEAD, no baseline":
# nothing to assert, so say nothing. A checkout that HAS an `origin` and no resolvable default-branch ref
# is a different fact: the question was asked and could not be answered, and #1549's fourth criterion is
# explicit that an unanswerable staleness question must not read as freshness (#266). That is a
# `noverdict`, and the caller REFUSES writes on it. It costs a stall; the alternative cost a corrupt
# touch-set.
#
# AND THE MISS IS NAMED, because "no `origin`" is a slightly weaker claim than "no upstream": a checkout
# whose remote is called `upstream`, or `fs-gg`, is silent here. That is a deliberate floor rather than an
# oversight — every FS-GG checkout is a clone with `origin`, and probing every configured remote for a
# plausible default branch would put a heuristic where this file keeps concrete answers (`shared_toplevel`
# and `engine_path` are equally literal). If a fleet checkout ever stops using `origin`, this guard goes
# quiet and nothing says so.
#
# A PATHSPEC THAT MATCHES NOTHING COUNTS ZERO, AND ZERO READS AS FRESH — so it is checked, not assumed.
# This is the same fail-open `check-engine-freshness.py` guards with `_assert_exists`, and it is a live
# risk rather than a theoretical one: `ENGINE_SOURCE_TREES` is three hard-coded directory names, and a
# renamed or moved project would leave `rev-list --count` answering `0` forever, over nothing, silently —
# a guard reporting fresh about a subject it can no longer see. So a `HEAD` with NO commit under those
# trees is a `noverdict`, not a pass.
#
# MEASURED: ~5 ms for the whole probe on this repo (4 `git` calls, 20 runs), against `dirty_guard`'s ~3 ms
# on the same host. It is on the hot path of every tier-2 invocation, reads only already-fetched refs, and
# performs no network I/O.
#
# Prints ONE tab-separated line, or nothing at all:
#   behind<TAB><n><TAB><ref>   — <ref> carries <n> commits under the engine's source trees that $1 lacks
#   noverdict<TAB><why>        — the question could not be answered, which is not an answer
upstream_drift() {
  local top="$1" ref="" cand c n
  cand="$(git -C "$top" symbolic-ref --quiet refs/remotes/origin/HEAD 2>/dev/null)"
  for c in "$cand" refs/remotes/origin/main refs/remotes/origin/master; do
    [ -n "$c" ] || continue
    if git -C "$top" rev-parse --verify -q "$c^{commit}" >/dev/null 2>&1; then ref="$c"; break; fi
  done
  if [ -z "$ref" ]; then
    git -C "$top" config --get remote.origin.url >/dev/null 2>&1 || return 0
    printf 'noverdict\tit has an origin remote, but refs/remotes/origin/{HEAD,main,master} all fail to resolve\n'
    return 0
  fi
  git -C "$top" rev-parse --verify -q HEAD >/dev/null 2>&1 \
    || { printf 'noverdict\tit has no HEAD to compare against %s\n' "$ref"; return 0; }
  # WORD SPLITTING IS THE POINT: `$ENGINE_SOURCE_TREES` is a pathspec LIST, and one spelling of it is
  # what keeps this in step with the comment above rather than duplicating three literals.
  # shellcheck disable=SC2086
  if [ -z "$(git -C "$top" rev-list -1 HEAD -- $ENGINE_SOURCE_TREES 2>/dev/null)" ]; then
    printf 'noverdict\tno commit reachable from HEAD touches %s, so this guard would be measuring nothing\n' \
           "$ENGINE_SOURCE_TREES"
    return 0
  fi
  # shellcheck disable=SC2086
  n="$(git -C "$top" rev-list --count "HEAD..$ref" -- $ENGINE_SOURCE_TREES 2>/dev/null)"
  case "$n" in
    '' | *[!0-9]*)
      printf 'noverdict\tgit rev-list --count HEAD..%s over the engine source trees did not answer\n' "$ref"
      return 0 ;;
    0) return 0 ;;
  esac
  printf 'behind\t%s\t%s\n' "$n" "$ref"
}

# THE TWO QUESTIONS ARE ONE VERDICT, AND ONE MESSAGE. Both mean "the IL you are about to exec is not the
# code that decides this board", they have overlapping remedies (a pull subsumes a rebuild), and a worker
# who is told only the nearer one goes and rebuilds a checkout that is still behind. So `$detail`
# ACCUMULATES: whichever fired is reported, and if both fired the reader sees both, in the order a fix
# would be applied.
stale_guard() {
  local top="$1" dll="$2" verb="$3" newer built changed drift kind a b detail=""
  [ -f "$dll" ] || return 0   # no IL to measure against; the exec below fails honestly on its own

  # (1) IS THE ARTIFACT BEHIND THE SOURCE BESIDE IT? The mtime question (#929), over build INPUTS only.
  newer="$(find "$top/src" \( -name obj -o -name bin \) -type d -prune \
             -o -name '*.fs' -newer "$dll" -print 2>/dev/null | head -1)"
  if [ -n "$newer" ]; then
    # `date -r FILE` is GNU; BSD's `-r` takes SECONDS. A failure here must not blank the message, so fall
    # back to '?': the timestamps are evidence, but the VERDICT is what has to survive a foreign date.
    built="$(date -r "$dll" '+%Y-%m-%d %H:%M' 2>/dev/null || echo '?')"
    changed="$(date -r "$newer" '+%Y-%m-%d %H:%M' 2>/dev/null || echo '?')"
    # THE PATHS ARE ABSOLUTE, AND #931 IS WHY. At tier 2b `$top` is the SHARED checkout while the reader
    # is standing in a worktree, so the old relative `dotnet build src/FS.GG.Coord.Cli -c Release` named
    # the WRONG tree — it rebuilds the caller's own source, which was never the stale one, and leaves the
    # refusal in place. #1549's third criterion asks for the remedy to name which checkout to update; a
    # relative path cannot.
    detail="            engine built $built
            ${newer#"$top/"} changed $changed
            You are about to run code that is NOT in the checkout that built it (#929). Rebuild it:
              dotnet build $top/src/FS.GG.Coord.Cli -c Release"
  fi

  # (2) HAS `main` MOVED PAST IT? The merge question (.github#1549), scoped to the engine's own trees.
  drift="$(upstream_drift "$top")"
  IFS="$(printf '\t')" read -r kind a b <<<"$drift"

  # THE REMEDY IS `merge --ff-only`, NOT `pull --ff-only` (.github#1664). The checkout `$top` names —
  # the SHARED one, the only one this arm ever sends you to — is routinely on a DETACHED HEAD, and
  # `git pull --ff-only` there does not fast-forward. It exits 1 with "You are not currently on a
  # branch", having moved nothing. That is the worst failure available to THIS reader, because the
  # refusal it is attached to is fail-closed on every board write in the fleet (.github#1549): the
  # remedy runs, prints a git error about branches with no visible connection to engine staleness,
  # and the refusal is still standing — so the natural inference is that the GUARD is wrong, which
  # is how a fleet learns to route around its own escape hatch. `merge --ff-only` fast-forwards a
  # detached HEAD and a branch alike, and still refuses loudly on a DIVERGED tree — the one case
  # that must be escalated rather than merged. The `noverdict` arm needs no such care: `fetch` has
  # no opinion about detached HEADs.
  #
  # `merge` DOES NOT FETCH, AND THAT IS THE RIGHT AMOUNT OF WORK. Dropping `pull` drops an implicit
  # `git fetch`, so the remedy now brings the checkout up to the ref as of its LAST fetch rather than
  # to the true remote. That is exactly sufficient, because it is the same ref this guard measured:
  # `upstream_drift` reads `refs/remotes/origin/*` and performs no network I/O (this file's "no
  # network, ever" rule), so `merge --ff-only $b` clears precisely the drift that was counted, and
  # nothing here ever claimed to know about commits nobody has fetched. The arm that DOES need a
  # fetch — no resolvable default-branch ref at all — is `noverdict`, and it prints one.
  #
  # AND IT FAST-FORWARDS TO `$b`, THE REF THIS GUARD ACTUALLY MEASURED, rather than a literal
  # `origin/main`. `$b` is whatever `upstream_drift` resolved — `refs/remotes/origin/HEAD` first,
  # then `main`, then `master` — and it is already the ref named in the same sentence by
  # `BEHIND $b by $a commit(s)`. Hard-coding `origin/main` would print a remedy that repairs a
  # different ref than the one the count was taken against, and on a `master` checkout it would
  # name a ref that does not resolve at all.
  case "$kind" in
    behind)
      detail="${detail:+$detail
}            BEHIND $b by $a commit(s) under the engine's own source trees — the checkout
            this engine was built from does not contain them (.github#1549):
              $top
            You can be standing in a CURRENT worktree and still be about to exec an engine
            that is not. Update the checkout that OWNS this build, then rebuild it:
              git -C $top merge --ff-only $b
              dotnet build $top/src/FS.GG.Coord.Cli -c Release" ;;
    noverdict)
      detail="${detail:+$detail
}            Whether this engine is behind the default branch could NOT be decided —
            $a, in the checkout this engine was built from:
              $top
            An unanswerable staleness question is not freshness (#266, .github#1549), so board
            writes fail closed here rather than being permitted on a guess. Give that checkout
            a default-branch ref, then rebuild it:
              git -C $top fetch origin
              dotnet build $top/src/FS.GG.Coord.Cli -c Release" ;;
  esac

  [ -n "$detail" ] || return 0                                       # the engine is current — say nothing

  # ONE SPACE BETWEEN THE SETS, spaces added once around the whole — see the note above the sets. A padded
  # concatenation would put a DOUBLE space here and refuse the empty verb of a bare invocation.
  case " $BOARD_WRITES $BOARD_WRITES_CONDITIONAL " in
    *" $verb "*)
      die "REFUSED — '$verb' writes the board the whole fleet shares, and the engine is STALE.
$detail" ;;
  esac

  # AN UNCLASSIFIED VERB IS REFUSED, NOT PERMITTED — the runtime half of the partition, and the reason
  # `BOARD_READS` is a set rather than a comment. Without this, "not in the write set" means "permitted",
  # so a verb ADDED to the engine and not to this file runs on a stale engine silently: the parity gate
  # would red on the next CI run, but the fleet would already have run it. Falling the other way costs
  # nothing — the guard is only reached when the engine is genuinely STALE, and a non-flag first token
  # this file does not know is either a NEW verb (refuse: that is the whole bug) or a typo the engine
  # would refuse a moment later anyway.
  #
  # THE EXEMPTION ASKS THE GRAMMAR, NOT A LIST (#1507). `--help`, `-h`, `--version` and the empty argv of a
  # bare invocation are not verbs and are not in the engine's `command-contract`; a fourth spelling would
  # have to be added to any list that named them, and being left out of that list would refuse `--help` on
  # a stale engine. "Begins with `-`, or is empty" is the same question the engine's own dispatch asks
  # (it REFUSES a flag before the verb), so no list can fall out of date here.
  case " $BOARD_READS " in
    *" $verb "*) ;;
    *)
      case "$verb" in
        '' | -*) ;;
        *)
          die "REFUSED — '$verb' is a verb this resolver does not classify, and the engine is STALE.
            Neither BOARD_WRITES, BOARD_WRITES_CONDITIONAL nor BOARD_READS in this file names it, so
            whether it writes the board the fleet shares is UNKNOWN — and an unknown write is refused.
            If it is a new verb, classify it here (tests/coord-engine-parity/shim.sh §3b gates the set).
$detail" ;;
      esac ;;
  esac

  echo "fsgg-coord: WARNING — the engine is STALE, so it need not behave as the code you are reading does." >&2
  printf '%s\n' "$detail" >&2
  echo "            Board writes are refused until you do." >&2
}

# AND UNCOMMITTED SOURCE IS STILL NOT A REF (#709). `stale_guard`'s mtime half asks "is the ARTIFACT
# behind the source beside it?", and its .github#1549 half asks "has the DEFAULT BRANCH moved past the
# commit that source sits on?". Neither can ask the third question — "is that source COMMITTED at all?"
# — because an uncommitted edit is on no ref for a rev-list to find. So the two states the mtime half
# separates are still not the two that matter:
#
#   edited, NOT built   → .fs newer than .dll  → stale_guard fires.  The engine is the OLD, merged code.
#   edited AND built    → .dll newer than .fs  → stale_guard SILENT. The engine is somebody's WIP.
#
# The mtime half is loudest in the safe case and mute in the dangerous one. Building is what a worker
# does NEXT, so #929's window closes itself: the warning decays into silence exactly as the risk arrives.
# #1549's half does not help here either — WIP built on top of a current checkout is not BEHIND anything,
# so `upstream_drift` is silent by construction and correctly so. That is this guard's whole subject.
#
# THE RISK IS THIS REPO'S ALONE, AND IT IS THE WHOLE FLEET'S. Every worker on this board runs the
# SHARED checkout's engine. Under `src/` live the scheduler, the claim CAS and the board writer. So one
# worker editing the kit here, in violation of pnext-item §2, silently hands every other worker an
# untested build of the lock they are about to take. #709 observed it live: ` M scripts/fsgg-coord` in a
# checkout the reporter had not touched, discovered only because they diffed it against HEAD afterwards.
#
# WHY they all run it moved, and the guard's subject did not. This paragraph used to say "a worktree has
# no bin/, so the source build cannot resolve there (that is why §2's isolation does not isolate this)"
# — true when written, and #931's tier 2b is precisely the repair of it: a worktree caller now RESOLVES
# the shared build rather than dying 69 at it. The conclusion is unchanged and is now a property of the
# resolver rather than an accident of where `bin/` happens to be, so tier 2b passes $SHARED to this
# guard — the reader standing in a worktree is exactly the reader whose claims that WIP is deciding.
#
# WARN, NEVER REFUSE — and this is NOT #929's trade repeated with a new subject. #929 may refuse a
# board write because its remedy is local, cheap and yours: rebuild, and you are clear. Dirtiness has
# no such exit. The checkout is dirty because somebody ELSE is mid-edit, and nothing you can run will
# clean it — so refusing would let one worker's §2 violation halt every OTHER worker's board writes,
# a fleet-wide outage manufactured by the guard rather than the bug. It would also trap the kit's own
# author, who is the one person who must be able to test an engine that is by definition uncommitted.
# The filer reached the same answer (#709: "my weak preference is warn always, refuse never").
#
# NO HEAD, NO VERDICT. A checkout with no commit has no baseline to be dirty AGAINST, so there is
# nothing to assert — the same shape as `stale_guard`'s "no IL to measure against" above, and as
# `release`'s "a column we cannot read is not one we may overwrite" (#331). This is also what keeps the
# receivers' shape silent: tiers 3/4 never reach here at all.
#
# A LINKED WORKTREE IS THE SANCTIONED FLOW, SO IT IS EXEMPT — and getting this wrong would aim the
# guard at the one worker obeying the rule it enforces. pnext-item §2 orders you to work the item in a
# worktree; a kit author who does that and builds there makes the source build resolve THERE, against a tree
# their own correct edits have made dirty. Warning them would accuse them of the §2 violation they are
# in the middle of avoiding, and there is nothing to warn about: no other worker resolves an engine
# through their worktree, so the WIP is reachable by nobody but them. Only the MAIN checkout is shared,
# and only its dirt is everyone's problem. `--git-dir` == `--git-common-dir` is exactly that question
# (they diverge in a linked worktree and nowhere else), which is why this is a git question and not a
# guess about paths.
#
# `status --porcelain`, NOT `diff --quiet`: it sees STAGED work (a `git add`ed WIP is still not on
# main) and untracked files, which `diff` alone cannot. `bin/` and `obj/` are gitignored, so the build
# every worker performs is invisible to it — the difference between a guard and a permanent false
# positive. Measured at ~3ms.
#
# AND `-c status.showUntrackedFiles=normal`, BECAUSE THE LINE ABOVE WAS NOT TRUE ON ITS OWN (#1043).
# "it sees … untracked files" is a claim about the CALLER'S CONFIG, not about `status`: `--porcelain`
# is a FORMATTING flag and does not override `status.showUntrackedFiles`. Set it to `no` — the usual
# remedy for a slow `git status` on a big repo, and inherited from ~/.gitconfig, which the shared
# checkout does not control — and this probe returns EMPTY on a tree full of uncommitted WIP. "Empty"
# then reads as "clean" at the `[ -n "$dirty" ]` below, and the guard SILENTLY never fires: #266's
# signature, rebuilt inside the check written to end it. Measured against a real tier-2 checkout with an
# untracked `New.fs`: default warns `1 path(s)`, `showUntrackedFiles=no` says NOTHING AT ALL.
#
# This is the `-quit` trap immediately above, and the `--no-optional-locks` trap below, wearing a third
# hat — and it is why they are worth spelling out rather than deleting as pedantry. All three are the
# same bug: a probe whose failure is INVISIBLE, so its silence is indistinguishable from a clean tree.
# The author reasoned it out twice for `find` and for a git flag, and the config channel kept the hole.
#
# `normal`, NOT `all`: it preserves exactly today's counting — a wholly-untracked directory stays
# collapsed to one entry — so `$n path(s)` and the `head -5` truncation below are byte-identical on any
# machine that never set the option. This forces the ANSWER's scope; it does not widen it.
#
# AND `core.excludesFile` IS LEFT ALONE — DECIDED, not overlooked (#1043 asked; this is the answer). It is
# the obvious next knob: a global ignore also hides files from `status`, so why force one and not both?
# Because THEY ARE NOT SYMMETRIC, and the difference is the whole design of this guard. Forcing
# `showUntrackedFiles` can only ever reveal files that are genuinely uncommitted and NOT ignored — every
# path it adds is real dirt, so it cannot produce a false positive. Forcing `core.excludesFile=/dev/null`
# would resurrect whatever the WORKER chose to ignore globally: measured, a `Protocol.fs.swp` beside a real
# source file reports as `?? src/…/Protocol.fs.swp` and warns about an editor swapfile that never reaches
# the engine. That is a permanent false positive on somebody's editor, and this guard cannot afford one —
# a warning that fires when nothing is wrong is one the fleet learns to skim, and the next one is real.
# (The repo's OWN `.gitignore` — `bin/`, `obj/` — is unaffected by either knob: measured, `bin/artifact`
# stays invisible with `excludesFile` forced off, so the build every worker performs stays invisible too.)
# The trade is a far-fetched miss (a worker whose global ignore matches `*.fs`) against a plausible false
# alarm. Take the miss.
#
# AND PLAIN `status`, DELIBERATELY — no `--no-optional-locks`. It looks made for this caller, and it is
# the `-quit` trap above wearing a different hat: it is git 2.19+, and on anything older it is an
# unknown option, which this probe's necessary `2>/dev/null` would swallow — leaving `$dirty` empty,
# "empty" reading as "clean", and the guard SILENTLY never firing. The contention it would avoid does
# not exist: measured with `.git/index.lock` HELD, `status` still answered correctly and exited 0,
# because refreshing the index is *optional* and git simply skips the write. A real portability hole
# traded for an imaginary lock.
#
# SCOPED TO WHAT ACTUALLY ENDS UP IN THE ENGINE, which is more than `src/`. The root
# `Directory.Build.props`, `Directory.Packages.props` and `global.json` are imported implicitly by every
# project beneath them, so a dirty package pin or SDK band changes the compiled engine exactly as an
# edited `.fs` does — and #672 (the ADR-0032 shared-build-config rollout) is an epic whose whole subject
# is editing those files. Naming only `src/` would have left the guard blind to part of its own subject:
# #266's signature, rebuilt inside the check written to end it. A pathspec that matches nothing is
# lenient (exit 0, no output), so naming a file this repo may not have costs nothing.
dirty_guard() {
  local top="$1" dirty n detail
  git -C "$top" rev-parse --verify -q HEAD >/dev/null 2>&1 || return 0
  [ "$(git -C "$top" rev-parse --git-dir 2>/dev/null)" \
    = "$(git -C "$top" rev-parse --git-common-dir 2>/dev/null)" ] || return 0
  dirty="$(git -C "$top" -c status.showUntrackedFiles=normal status --porcelain -- \
             src scripts/fsgg-coord Directory.Build.props Directory.Packages.props global.json 2>/dev/null)"
  [ -n "$dirty" ] || return 0
  n="$(printf '%s\n' "$dirty" | wc -l | tr -d ' ')"
  detail="$(printf '%s\n' "$dirty" | head -5 | sed 's/^/              /')"
  [ "$n" -gt 5 ] && detail="$detail
              … and $((n - 5)) more"
  echo "fsgg-coord: WARNING — this engine is built from UNCOMMITTED code (#709)." >&2
  echo "            $n path(s) that end up INSIDE the engine are modified in this SHARED checkout:" >&2
  printf '%s\n' "$detail" >&2
  echo "            Somebody is working the kit here, which pnext-item §2 forbids. Every worker on this" >&2
  echo "            board runs THIS scripts/fsgg-coord, so that WIP — the scheduler, the claim CAS, the" >&2
  echo "            board writer — is deciding your claims too. If it is yours: move it to a worktree." >&2
  return 0
}
