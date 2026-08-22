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
# rebuild. It is the same trade #929 already made for the mtime half, and the alternative is #1507's
# defect recurring on a live claim, which is what happened while this comment said the gap was
# deliberately unclosed.
#
# THIS PARAGRAPH USED TO PRICE THAT AT *"a stall of about a minute, on a remedy that is local, cheap and
# theirs"*, FULL STOP, AND THAT CLAIM IS TRUE OF ONLY ONE OF THE TWO REGIMES THIS REPO RUNS IN
# (.github#2581). The wording is retired rather than deleted, because what it got wrong is worth keeping
# on the record:
#
#   WHEN THE WORKER OWNS THE REMEDY it is exactly right. A solo worker, or a lane that may rebuild the
#   checkout it resolved through, runs the two printed commands and is clear in about a minute.
#
#   UNDER HOST-SERIALISED REPAIR IT IS FALSE, AND THE COST IS UNBOUNDED. Several lanes land at once, so
#   the SHARED checkout's rebuild is a fleet-wide action the host serialises; workers are instructed to
#   hold the claim, run no repair, build nothing, and REPORT. The remedy printed below is then not
#   theirs, "about a minute" becomes a wait on another actor with no bound at all, and the one verb that
#   would preserve the claim through that wait — `heartbeat` — is refused by this same guard. Measured
#   twice in one session on 2026-08-14: .github#2549 (lease created 22:10:05Z, expired before
#   `done --flip`, `heartbeat` refused as "EXPIRED and cannot be renewed in place") and .github#2563
#   (claimed 22:55:59Z, dead at 00:55:59Z, the item reported as "not currently held by anyone"). Both
#   recoveries cost a re-claim, which changed the claim generation, which reddened `claim-generation`
#   until `delivery` was re-issued — a SECOND `delivery` call, visible in the record forever.
#
# A comment asserting a property the code does not have is itself a defect (#1059's class), and pricing a
# fleet-wide stall as a one-minute local chore is that shape: a reader auditing this guard concludes the
# cost is bounded when in the regime that actually bit twice it is not.
#
# WHAT DID *NOT* CHANGE IS THE REFUSAL. .github#2581 asked whether a STALE engine may write a lease
# renewal, and the answer is NO, on measurement rather than caution: `Writes.heartbeat` PATCHes the
# marker comment, a PATCH rewrites the WHOLE body, and the body is recomposed by `markerBody` out of the
# RUNNING BUILD's idea of the grammar — `worker=`, `lease=`, `renewed=`, then `session=`, `prev=` and
# `pathRepo=` emitted only if that build knows about them. An older engine therefore SILENTLY DROPS from
# a LIVE claim any field it predates, which is not hypothetical: the same rewrite lost `prev=` (#550) and
# lost `session=` (#1149), after which `twinSession` could no longer catch a same-id twin and two workers
# ended on one item. A lease renewal is also the write whose corruption is hardest to notice, because it
# is *expected* to leave the marker looking unchanged. So `heartbeat` stays in `BOARD_WRITES` below and
# stays refused; what .github#2581 changes is that the refusal now names a recovery route the blocked
# worker can actually take (see `stale_guard`'s own .github#2581 block).
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
#
# `op-lock` IS ONE WORD HERE FOR `room`'s REASON: the engine advertises `op-lock acquire` and `op-lock
# release`, and this guard dispatches on `$1`, so the only token it can ever see is the namespace.
# `tests/coord-engine-parity/shim.sh` §3b projects the engine's surface through `awk '{print $1}'` for
# exactly that reason, and holds this set in bijection with it.
#
# AND IT IS A WRITE EVEN THOUGH ITS SUBJECT IS OFF-BOARD. Both halves POST or DELETE an `fsgg:claim`
# marker on a receiver's closed `[op-lock]` issue through the same `Writes.claimScoped` CAS `claim` uses.
# `who` and `reap` never see that issue (ADR-0041, on a third subject), but the fleet does: a stale engine
# taking a dispatch grant is a stale engine deciding who may dispatch against a receiver, which is the
# `.github#1858` failure with an extra step. Refuse it exactly as `claim` is refused.
BOARD_WRITES="add adopt child claim comment done flush heartbeat intake op-lock release review room say set-field set-paths take widen"

# WRITES ONLY UNDER A CONDITION — and the flat list above CANNOT say that, which is how it got wrong.
# These are refused all the same, verb-level and fail-closed, and they are held in a set of their own so
# the fact stays recorded rather than laundered into "these are write verbs":
#
#   reap        — writes only under `--apply`; the bare form is a DRY RUN (`Client.fs`, `if not opts.Apply`).
#   reconcile   — the same shape: `--apply` writes board columns via `Board.boardWrite`, bare is a dry run.
#   delivery    — the bare lifecycle inspection is read-only; `--apply` may issue its guarded merge
#                 (`Client.delivery` → `Writes.mergeAtHead`) after consuming a fresh receipt.
#   delivery-route — `validate` and `show` only read evidence; `record` appends the receipt ledger.
#                     The shim refuses the whole verb on stale code because missing the record arm would
#                     let a stale engine write an authoritative route decision.
#   graphql     — complete typed reads except `archive-items`, which executes the archive mutation. The
#                 verb-level guard cannot infer its subcommand safely, so the write-capable verb is refused.
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
BOARD_WRITES_CONDITIONAL="delivery delivery-route graphql next reap reconcile"

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
#   files). `scan` POSTs to `graphql`, which is how a GraphQL READ is spelled. `body-edits` (.github#2477)
#   is the same shape as `scan`: one `userContentEdits` GraphQL QUERY through `Reads.contentEditProvenance`,
#   never a mutation, and it writes no board field, cache, or claim. `packet` (.github#2737) never reaches
#   the transport at all: `Program.fs` routes `PacketCmd` straight to `PacketApplication.run` and NEVER to
#   `Client` — unlike `intake`, whose `apply` half does — so it reads one local file and decides. Its place
#   here is load-bearing rather than tidy: an unclassified verb is REFUSED on a stale engine, and this verb
#   is the pre-flight a finder runs precisely when the fleet is in the state that makes engines go stale, so
#   leaving it out would deny the validator to the reader it exists for.
#
# `--help`, `-h` and `--version` are deliberately absent: they are not verbs, and the engine's
# `command-contract` excludes them for the same reason. They are exempted by GRAMMAR at the guard below
# ("begins with `-`, or is empty"), NOT by being listed here — because an unclassified token is REFUSED
# there, not permitted, so a list of flag spellings is a list that could be left short.
#
# ONE LINE, AND NO SUBSTITUTION IN IT — the parity gate lifts these three assignments straight out of this
# file and refuses any that is not a plain literal, so a `$…`/`$(…)`/continuation spelling would red §3b
# rather than be evaluated. Long, therefore, on purpose.
BOARD_READS="batch board body-edits bootstrap budget command-contract cycle decide diff-audit driver facts field-id followup inbox issues item-id landable lanes lint option-id overlap packet predicate ready scan verify-paths who whoami"

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
# `src/FS.GG.Coord.Cli.Kernel` joined the list at .github#2725, and it is a SEPARATE ENTRY rather
# than a widening of the `src/FS.GG.Coord.Cli` one on purpose. These trees are matched as git
# pathspecs, so `src/FS.GG.Coord.Cli` already covers a directory whose name merely starts with it
# only by accident of prefix matching — and relying on that would also sweep in a future
# `src/FS.GG.Coord.Cli.Tests`. The engine is now two projects and the list says two projects.
ENGINE_SOURCE_TREES="src/FS.GG.Coord.Cli src/FS.GG.Coord.Cli.Kernel src/FS.GG.Coord.Core src/FS.GG.Coord.GitHub"

# WHAT A CHECKOUT WOULD HAVE HAD TO EDIT TO CHANGE THE ENGINE IT BUILDS (.github#2653) — the subject of
# `authored_engine_source` below, and deliberately NEITHER of the two pathspecs above and below it.
#
# WIDER THAN `ENGINE_SOURCE_TREES`, AND THE ASYMMETRY RUNS THE OPPOSITE WAY FROM THAT ONE'S. The note
# beside those trees narrows them because a wide subject there costs an OUTAGE — every extra path is one
# more way to refuse the whole fleet. Here every extra path makes the answer "yes, authored", and "yes"
# is the branch that keeps today's behaviour exactly. The two failure directions are not symmetric: a
# MISS silently hands a kit author the shared engine and discards the edits they are testing, which is
# precisely what `scripts/fsgg-coord:209-213` forbids; a FALSE POSITIVE leaves a worker with the
# resolution they already have. So this question is answered on the side that cannot discard work, and
# the root props/`global.json` — implicitly imported by every project beneath them, and genuinely
# compiled into the engine — are in.
#
# NARROWER THAN `dirty_guard`'s PATHSPEC BY EXACTLY ONE ENTRY: `scripts/fsgg-coord` is absent. That
# guard watches the shim because a reader of THIS repo cares whether the coordination tool is committed;
# but the shim is bash that is `exec`'d, not F# that is compiled, so editing it cannot make a BUILD
# carry anything. Including it would mean this file's own author — editing the shim, engine untouched —
# was told their incidental build was deliberate, which is this row's defect wearing the repair's hat.
#
# AND NARROWER THAN ALL OF `src/`, WHICH IS THE OTHER OBVIOUS SPELLING. `src/FS.GG.Kit` and
# `src/FS.GG.Drivers` do not compile into `fsgg-coord-engine`, and counting them would re-refuse exactly
# the kit-editing workers .github#2642 reported — the population this row exists for.
ENGINE_BUILD_INPUTS="$ENGINE_SOURCE_TREES Directory.Build.props Directory.Packages.props global.json"

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
# risk rather than a theoretical one: `ENGINE_SOURCE_TREES` is four hard-coded directory names since
# .github#2725, and a renamed or moved project would leave `rev-list --count` answering `0` forever, over
# nothing, silently — a guard reporting fresh about a subject it can no longer see. So a `HEAD` with NO
# commit under those trees is a `noverdict`, not a pass.
#
# THE `noverdict` ARM ABOVE IS NOT THE WHOLE ANSWER, AND SAYING SO IS THE POINT. It fires only when NO
# commit reachable from HEAD touches ANY of the four trees — i.e. when the whole list has gone blind. One
# renamed project out of four leaves the other three matching, so the probe stays confident and simply
# stops seeing the renamed tree. That residue is caught statically, in `tests/coord-guards/run.sh` §12,
# and deliberately NOT here: this function is on the hot path of every tier-2 invocation, and a
# missing-tree REFUSAL here would cost the whole fleet an outage over a directory rename — the same
# asymmetry the note beside the constant itself draws.
#
# MEASURED: ~5 ms for the whole probe on this repo (4 `git` calls, 20 runs), against `dirty_guard`'s ~3 ms
# on the same host. It is on the hot path of every tier-2 invocation, reads only already-fetched refs, and
# performs no network I/O.
#
# Prints ONE tab-separated line, or nothing at all:
#   behind<TAB><n><TAB><ref>   — <ref> carries <n> commits under the engine's source trees that $1 lacks
#   noverdict<TAB><why>        — the question could not be answered, which is not an answer
# THE DEFAULT BRANCH, RESOLVED ONCE (.github#2653). This was inline in `upstream_drift` and is now a
# function because a SECOND caller arrived — `authored_engine_source` below needs the same ref to take a
# merge-base against. Two copies of this loop would be one edit away from measuring DRIFT against
# `refs/remotes/origin/HEAD` and AUTHORSHIP against `refs/remotes/origin/main`, which on a repo where
# those differ is two questions about two different branches reported as one verdict. The file already
# makes this argument for `engine_path` in the shim ("ONE spelling, used by both tiers below"), and the
# reason is the same one: a fallback that probes a different thing than the primary resolves a different
# answer than the one just measured.
#
# Prints the resolved ref and returns 0; prints nothing and returns 1 when none of the three candidates
# resolves to a commit. The BEHAVIOUR is byte-identical to the loop it replaces — each candidate must
# resolve to a commit before it is believed, because a dangling symref is not a comparison point.
default_branch_ref() {   # $1 = checkout
  local top="$1" cand c
  cand="$(git -C "$top" symbolic-ref --quiet refs/remotes/origin/HEAD 2>/dev/null)"
  for c in "$cand" refs/remotes/origin/main refs/remotes/origin/master; do
    [ -n "$c" ] || continue
    if git -C "$top" rev-parse --verify -q "$c^{commit}" >/dev/null 2>&1; then printf '%s' "$c"; return 0; fi
  done
  return 1
}

# DOES THIS CHECKOUT AUTHOR ENGINE BUILD INPUTS OF ITS OWN? (.github#2653)
#
# WHY THE QUESTION EXISTS. `scripts/fsgg-coord`'s tier 2a prefers a source build under the CALLER's own
# toplevel, and its reasoning is on the record at `:209-213`: *"A worker who builds in their own worktree
# gets THEIR build — that is the kit author's whole workflow, and preempting it would hand them the
# shared engine and silently discard the edits they are testing."* That sentence is right, and its
# unstated premise is that a worktree build EXPRESSES INTENT to use it as the engine. The premise is
# false for a build nobody asked for: `tests/skill-registry` and `tests/skill-quality` build the engine
# transitively, and `scripts/generate-driver-manifest --write` REFUSES without one — so a worker whose
# item touches no engine source is *required* to create the artifact and then poisoned by it, because a
# feature branch is behind `main` under the engine's source trees by construction the moment any engine
# commit lands after it was cut. Measured five times across three agents in ONE board-driver run on
# 2026-08-15 (.github#2653, and its comments): four on .github#2576 — one of them blocking `heartbeat` —
# one while critiquing .github#2571, and two on .github#2642. Every one self-repaired by deleting
# `bin`/`obj`; every one first spent a diagnosis the tool does not offer.
#
# AND SINCE .github#2653's SECOND HALF, THE ARTIFACT IS NO LONGER CREATED AT ALL — which is where the
# operator's chosen mechanism landed (`.github#2653#issuecomment-5308188901`), and this function is now
# the SECOND line of defence rather than the only one. `scripts/build-gate-engine` builds the engine to
# an explicit `--artifacts-path` OUTSIDE the caller's checkout, `scripts/check-skill-quality` delegates
# to it, and `tests/engine-build-siting/run.py` refuses any harness under `scripts/` or `tests/` that
# builds the engine anywhere tier 2a probes. So the incidental build this function classifies should not
# exist in the first place. It is kept, and kept fail-safe, for the two populations that outlive the
# harness fix: a worker who ran `dotnet build` by hand while chasing something else, and any future
# tool — inside this repo or not — that produces the artifact before anyone thinks to declare it. The
# two repairs are complements, not alternatives: this one makes an incidental artifact HARMLESS, the
# harness fix stops one being MADE, and the row asked for both (criteria 2 and 4).
#
# SO ASK THE CHECKOUT, NOT THE ARTIFACT. Intent lives in the command that ran, which nothing here can
# see. What IS visible, exactly and cheaply, is whether this checkout holds engine build inputs that
# exist NOWHERE UPSTREAM — content of its own. Where it does, `:209-213`'s sentence has a subject and its
# conclusion holds. Where it does not, the build is a faithful rebuild of code the shared checkout also
# has, there is nothing of the worker's to discard, and the sentence does not reach the case.
#
# CONTENT, NEVER MTIMES, AND .github#1572 IS WHY. That incident measured a `dotnet test` in another
# configuration re-stamping MSBuild-GENERATED `.fs` files and manufacturing a false STALE over a tree
# with zero edited sources — refusing every board write and citing an assembly-attribute stub no human
# wrote. Timestamps answer "was something written", never "did somebody mean it". .github#2653's own
# second criterion asks for this distinction to be explicit rather than inferred from mtimes, in as many
# words. `git status` and `git diff` answer about CONTENT, and no build can move either.
#
# TWO HALVES, BECAUSE ONE MISSES THE CASE THAT MATTERS MOST. Committed work is invisible to `status`,
# and uncommitted work is invisible to `diff <base> HEAD` — and the kit author mid-edit, whose engine
# source is dirty and whose branch is otherwise clean, is exactly the reader `:209-213` protects.
#
# `-c status.showUntrackedFiles=normal` IS LOAD-BEARING, for `dirty_guard`'s .github#1043 reason
# repeated here rather than assumed: `--porcelain` is a FORMATTING flag and does not override a `no`
# inherited from `~/.gitconfig`, so without it a tree full of new, untracked engine sources answers
# EMPTY — and empty would read as "authored nothing", which silently swaps the engine under the one
# person who must not have it swapped. `bin/`/`obj/` are gitignored, so the build itself stays invisible
# to this probe, which is the difference between a question and a permanent "yes".
#
# THE BASELINE IS THE MERGE-BASE, NOT THE DEFAULT BRANCH ITSELF, AND THAT IS THE WHOLE POINT. Comparing
# `HEAD` against `origin/main` directly answers "does this tree differ from main?", which is TRUE for
# every branch that is merely BEHIND — i.e. for exactly the population this row is about. The merge-base
# answers the question actually being asked: has THIS BRANCH authored engine content since it was cut?
# Behind-ness is `upstream_drift`'s subject and is deliberately not re-asked here.
#
# AND EVERY PROBE IS ASKED FOR ITS EXIT STATUS, NOT ONLY FOR ITS OUTPUT (.github#2653, review round 1,
# repair 1). The first draft read `status --porcelain` through a command substitution and tested only
# whether the STRING was empty — so `git status` FAILING and the tree being CLEAN produced the identical
# answer, and the failure then fell through to probe 2, which is tree-to-tree, needs no index, and keeps
# working. The result was `return 1`: "authors nothing", and the engine swapped out from under the
# caller. That is the fail-OPEN direction, on precisely the reader the paragraph below calls the one
# `:209-213` protects — the kit author mid-edit, whose only evidence of authorship is the dirt probe 1
# cannot see.
#
# It is reachable without exotic permissions and was reproduced, not reasoned about: a truncated or
# corrupt `.git/index`, or a `GIT_INDEX_FILE` leaked from a git hook, each exit 128 with EMPTY stdout
# while `merge-base` and `diff --quiet <base> HEAD` both still answer 0. Measured on a linked-worktree
# fixture — healthy: probe1 rc=0, probe2 rc=0; corrupt index: probe1 rc=**128**, probe2 rc=0. (A stale
# `index.lock` does NOT do this: `status` still exits 0, so it is not the same hazard.)
#
# THE IDIOM CAME FROM `dirty_guard`, WHERE IT IS SAFE, AND THAT IS THE WHOLE LESSON. That guard reads
# `status --porcelain` the same way and only ever WARNS, so a swallowed failure costs one missing line of
# stderr. Here the same two lines decide which engine executes. A borrowed idiom keeps its shape and
# loses its justification; the file's own §3g leg g7 now measures this one rather than trusting it.
#
# EVERY UNANSWERABLE PROBE RETURNS "AUTHORED", i.e. today's resolution. .github#1549's fourth criterion
# settled that an unanswerable staleness question is not freshness; its counterpart here is that an
# unanswerable INTENT question is not permission to swap the engine under the caller. No `origin` (the
# parity fixtures, a `git init` scratch tree, an air-gapped clone), no merge-base (unrelated histories),
# a `git` that errors: each takes the branch that changes nothing.
#
# MEASURED: 3 `git` invocations, ~6 ms on this repo, and paid ONLY inside `scripts/fsgg-coord`'s tier-2a
# branch — which requires an executable source build under the caller's own toplevel, so no receiver can
# reach it and no invocation that resolves at tiers 1/3/4 pays anything.
#
# Returns 0 (authored — prefer this checkout's build) or 1 (incidental).
#
# AND IT RECORDS *WHICH* ANSWER IT GAVE, BECAUSE "YES" AND "COULD NOT TELL" ARE NOT THE SAME SENTENCE.
# The refusal below reports why the caller's own build was preferred, and a message that told a reader
# with no `origin` remote that their checkout "authors engine source of its own" would be asserting a
# fact it never observed — this file's own #266 shape, three guards deep. `AUTHORED_ENGINE_SOURCE_REASON`
# is therefore an OUT-PARAMETER for the message path only: `dirty`, `committed`, `unanswerable`, `none`.
# Deliberately not a return code — the four states collapse to two DECISIONS, no caller branches on the
# distinction, and encoding it in the exit status would invite one to. Re-deriving it at the message
# site instead would mean a second copy of the predicate, which is what `default_branch_ref` above
# exists to prevent.
AUTHORED_ENGINE_SOURCE_REASON=none
authored_engine_source() {   # $1 = checkout
  local top="$1" ref base dirt
  AUTHORED_ENGINE_SOURCE_REASON=none
  # WORD SPLITTING IS THE POINT — `$ENGINE_BUILD_INPUTS` is a pathspec LIST, and one spelling of it is
  # what keeps this in step with the comment above it rather than duplicating six literals.
  #
  # ASSIGN FIRST, THEN TEST THE STATUS, THEN TEST THE STRING — three separate facts, and collapsing the
  # first two is repair 1's defect. `local dirt="$(…)"` would not do: `local` is itself a command and
  # its OWN exit status wins, so the assignment's failure would be masked exactly as it was before.
  # shellcheck disable=SC2086
  dirt="$(git -C "$top" -c status.showUntrackedFiles=normal status --porcelain -- $ENGINE_BUILD_INPUTS 2>/dev/null)" \
    || { AUTHORED_ENGINE_SOURCE_REASON=unanswerable; return 0; }
  if [ -n "$dirt" ]; then
    AUTHORED_ENGINE_SOURCE_REASON=dirty; return 0
  fi
  AUTHORED_ENGINE_SOURCE_REASON=unanswerable
  ref="$(default_branch_ref "$top")" || return 0
  base="$(git -C "$top" merge-base HEAD "$ref" 2>/dev/null)" || return 0
  [ -n "$base" ] || return 0
  # `--quiet` implies `--exit-code`, and its THREE outcomes are read as three: 0 = identical, 1 = the
  # trees differ, anything else = the question failed. The last two take the same DECISION — leave the
  # caller's own build in place — and are still recorded apart, because only one of them is an
  # observation the refusal may quote back to the reader. `if ! git …` would fold them together and
  # report a difference nobody measured.
  # shellcheck disable=SC2086
  git -C "$top" diff --quiet "$base" HEAD -- $ENGINE_BUILD_INPUTS 2>/dev/null
  case "$?" in
    0) AUTHORED_ENGINE_SOURCE_REASON=none;         return 1 ;;
    1) AUTHORED_ENGINE_SOURCE_REASON=committed;    return 0 ;;
    *) AUTHORED_ENGINE_SOURCE_REASON=unanswerable; return 0 ;;
  esac
}

upstream_drift() {
  local top="$1" ref="" n
  ref="$(default_branch_ref "$top")"
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
#
# THE VERDICT IS A FUNCTION NOW, AND THE POLICY IS SEPARATE FROM IT (.github#2653). This body used to be
# the first half of `stale_guard`. It was split out because a SECOND caller needs the answer without the
# consequence: `engine_shadows_shared` below must know whether the SHARED checkout's engine is current
# before tier 2a may fall through to it, and `stale_guard` answers that question only by `die()`ing.
# Splitting it is the alternative to asking a second, similar-looking question somewhere else — which is
# how a guard ends up refusing on one measurement and swapping on another. This function decides; it
# never warns, never refuses, and never reads `$verb`.
#
# Prints the accumulated detail, or nothing at all when the engine is current.
stale_detail() { # $1 = top, $2 = engine .dll
  local top="$1" dll="$2" newer built changed drift kind a b detail=""
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
  # the SHARED one at tier 2b, or the CALLER'S OWN source build at tier 2 (.github#2471; this arm does
  # NOT only ever send you to the shared one, whatever an earlier version of this comment claimed) — is
  # routinely on a DETACHED HEAD, and `git pull --ff-only` there does not fast-forward. It exits 1 with
  # "You are not currently on a
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

  printf '%s' "$detail"                        # empty means the engine is current — the caller says nothing
}

# MAY TIER 2a FALL THROUGH TO TIER 2b? (.github#2653)
#
# THE ONE QUESTION `scripts/fsgg-coord`'s TIER 2a ASKS BEFORE PREFERRING THE CALLER'S OWN BUILD, and the
# whole of this row's repair. It is true — meaning "this build is an incidental artifact that would
# SHADOW a better engine" — only when all four hold:
#
#   1. this checkout authors no engine build inputs of its own, so there is nothing of the worker's own
#      for the fall-through to discard (`authored_engine_source`, and its long note above);
#   2. a shared checkout exists and is a DIFFERENT tree than this one, so there is somewhere to fall to;
#   3. it carries an executable engine; and
#   4. that engine is CURRENT by the identical `stale_detail` question this file refuses on.
#
# (4) IS WHAT MAKES THIS CHANGE MONOTONE, AND IT IS NOT BELT-AND-BRACES. Without it, a worker whose own
# build is current and whose SHARED checkout is stale would go from working to refused — and that is not
# a hypothetical population, it is exactly .github#2581's, where the shared checkout's repair is
# host-serialised and unbounded. Repairing one refusal by manufacturing another in the neighbouring row
# is not a repair. With it, the property is stateable in one line: RESOLUTION CAN ONLY EVER MOVE TO AN
# ENGINE THAT ANSWERS "CURRENT" — never to a worse one, never to a missing one, and never at all unless
# the caller authored nothing.
#
# EVERY REFUSAL PATH RETURNS 1, i.e. "keep tier 2a", i.e. today's resolution exactly. There is no branch
# here whose failure mode is a swap.
#
# `shared_toplevel` AND `engine_path` COME FROM THE SHIM, which defines both before it sources this file
# and which is this file's only caller — the same arrangement the .github#2402 block below already
# relies on. Their cost is paid only after (1) has already answered "incidental", so the kit author —
# the reader `scripts/fsgg-coord:209-213` protects — never pays for a `git worktree list` at all.
engine_shadows_shared() {   # $1 = the caller's own toplevel, $2 = its engine binary
  local top="$1" shared shared_bin
  authored_engine_source "$top" && return 1
  shared="$(shared_toplevel 2>/dev/null)"
  [ -n "$shared" ] || return 1
  [ "$shared" != "$top" ] || return 1
  shared_bin="$(engine_path "$shared")"
  [ -x "$shared_bin" ] || return 1
  [ -z "$(stale_detail "$shared" "${shared_bin}.dll")" ] || return 1
  return 0
}

# THE POLICY HALF: what to DO about a verdict `stale_detail` returned. Reads warn, writes refuse, and an
# unclassified verb is refused — unchanged, and unchanged deliberately: .github#2653 changes which
# checkout tier 2a hands this guard, never what the guard decides once it is asked.
stale_guard() { # $1 = top, $2 = engine .dll, $3 = verb, $4 = the verb's first argument
  local top="$1" dll="$2" verb="$3" detail
  detail="$(stale_detail "$top" "$dll")"
  [ -n "$detail" ] || return 0                                       # the engine is current — say nothing

  # .github#2402, LIVE: the shared checkout was `Already up to date`, and the refusal still stood,
  # because the stale build lived under the WORKER'S OWN worktree (tier 2a) — a `dotnet build` run
  # earlier in the item, before #2470/#2467 landed. Every clause above already names `$top` by absolute
  # path (#931), so the checkout to fix was never mis-named. What was missing is the ONE fact that would
  # have stopped the worker repairing the checkout that was never wrong: whether `$top` IS the shared
  # checkout, or a build the worker's OWN worktree grew independently of it. Answer that here, once,
  # rather than at every call site above — `shared_toplevel` is already defined by the time this file is
  # sourced (`guards`, this file's one caller, is invoked after it), and paying its one `git worktree
  # list` here costs nothing extra: this branch runs only when a refusal is ALREADY being composed, the
  # slow path by construction, never on the silent common case measured in the file's header comment.
  local shared why
  shared="$(shared_toplevel 2>/dev/null)"
  if [ -n "$shared" ] && [ "$shared" != "$top" ]; then
    detail="$detail

            THIS IS NOT THE SHARED CHECKOUT. The shared one — the checkout every OTHER worker's
            engine resolves through — lives at:
              $shared
            and may already be current; running its remedy fixes nothing here. The build above is a
            SEPARATE source build under your OWN checkout, most likely a linked worktree where
            \`dotnet build\` ran directly (tier 2a, .github#931) — it shadows the shared engine rather
            than being it, and only rebuilding (or deleting the stale bin/obj under, then rebuilding)
            THIS checkout — $top — clears the refusal. Do NOT merge \`origin/main\` into a feature
            branch to fix this either: that moves a head an independent critic may already have
            confirmed, which is authoring, not landing."

    # AND WHY IT WAS PREFERRED, WHICH SINCE .github#2653 IS A DECIDED FACT RATHER THAN AN ACCIDENT.
    #
    # Before that row, a tier-2a build won simply by existing, so the block above could say no more
    # than "most likely a linked worktree where `dotnet build` ran directly" — a guess about how the
    # artifact got there, offered to a reader who had not run `dotnet build` at all and whose gate
    # harness had. Now exactly two states reach this point, `engine_shadows_shared` decides between
    # them, and the reader is told which one they are in — because the two have DIFFERENT remedies and
    # the generic `merge --ff-only` line above is written for neither of them without qualification.
    #
    # THAT LINE IS THE SELF-CONTRADICTION THIS BLOCK NOW SUPERSEDES, and it was measured in this row's
    # own hermetic reproduction rather than reasoned about: `stale_guard`'s `behind` arm prints
    # `git -C <worktree> merge --ff-only <ref>` and this very block then forbids it four lines later.
    # A remedy a message contradicts inside one screen is the failure .github#1664 already refuses here
    # — it runs, prints something unrelated to engine staleness, changes nothing, and teaches the fleet
    # to route around its own escape hatch. The wording above is kept (it is correct for a checkout
    # whose head is free to move, which is most of them) and is qualified here rather than deleted.
    if authored_engine_source "$top"; then
      case "$AUTHORED_ENGINE_SOURCE_REASON" in
        dirty)     why="it has UNCOMMITTED work under them" ;;
        committed) why="its branch has COMMITTED work under them that the default branch does not" ;;
        *)         why="that question could NOT be answered here — no default-branch ref, no merge-base,
            or a git probe that failed — and an unanswerable intent question is not permission to
            swap the engine under you (.github#1549's fourth criterion, applied to intent)" ;;
      esac
      detail="$detail

            WHY YOUR OWN BUILD WAS PREFERRED: this checkout is treated as AUTHORING engine build
            inputs — $ENGINE_BUILD_INPUTS — because
            $why.
            That is the kit author's case, and tier 2a preferred your build DELIBERATELY (.github#2653,
            and \`scripts/fsgg-coord\`'s note at :209-213): the shared engine would not carry those
            edits, so falling through to it would discard the very thing you are testing. The remedy is
            genuinely yours and it is in THIS checkout.
            IF YOUR HEAD IS STILL FREE TO MOVE, the two commands above are it. IF IT IS NOT — a branch
            an independent critic may already have confirmed — they are NOT, and this paragraph
            supersedes them: build a current engine in a checkout you own and name it explicitly with
            FSGG_COORD_ENGINE_BIN, which is honoured before any guard runs."
    else
      detail="$detail

            WHY YOUR OWN BUILD WAS PREFERRED: this checkout authors NO engine build inputs of its own,
            so since .github#2653 an incidental build here is passed over for the shared checkout's
            engine — and it was not passed over, which means that engine is absent or is itself stale.
            You are not being refused for the build in your worktree; you are being refused because
            there is no current engine anywhere for it to be passed over FOR. Deleting the bin/obj
            above therefore fixes nothing on its own. Either name a current engine you built in a
            checkout you own with FSGG_COORD_ENGINE_BIN, which is honoured before any guard runs, or
            wait for the shared checkout at $shared to be brought current — which, on a board running
            several lanes, is an action the host serialises rather than one you own."
    fi
  fi

  # WHO OWNS THE REMEDY, AND WHAT TO DO WHEN IT IS NOT YOU (.github#2581).
  #
  # THE BLOCK ABOVE ANSWERS "IS THIS THE SHARED CHECKOUT?" IN ONE DIRECTION ONLY. It speaks when $top is
  # NOT shared; when $top IS shared it says nothing, and that silence is the case a tier-2b worker
  # actually hits. Everything printed so far then reads as one instruction — go and repair $top — on the
  # one checkout a worker under host-serialised repair is told to hold. So the guard printed a remedy the
  # reader must not run, while the route that WOULD have worked sits three tiers above it in the same
  # resolver and was never mentioned. That is #266's class one level up: a diagnostic naming a remedy it
  # structurally cannot deliver to this reader. .github#2549 and .github#2563 each lost a lease to it.
  #
  # THE ROUTE IS TIER 1, AND IT IS NOT A LOOPHOLE. An explicit FSGG_COORD_ENGINE_BIN is an INSTRUCTION
  # (the shim's first branch, taken before TOP is even computed, so no guard runs), and pointing it at a
  # CURRENT engine does not weaken anything: the engine that writes the board is not stale, so #929 and
  # #1507 are not in play at all. This does not exempt a stale engine from anything — see the
  # .github#2581 paragraph in this file's header for why a stale engine may NOT write a lease renewal.
  #
  # THE ROUTE SAYS *BUILD*, AND THE COST IS STATED RATHER THAN MITIGATED. Tier 1 skips BOTH guards by
  # design, so it runs whatever it is given, stale or dirty, and says nothing. A check here would
  # contradict "an instruction, not a hint", so the text tells the reader to build the engine they name.
  #
  # COMPOSED ONLY WHEN A REFUSAL IS ALREADY BEING RAISED, and appended at the die() sites rather than to
  # $detail — a READ still runs on a stale engine, and handing its warning a recovery route for a
  # refusal that did not happen is the noise that teaches a fleet to skim this guard.
  local recovery=""
  if [ -n "$shared" ] && [ "$shared" = "$top" ]; then
    recovery="

            THIS *IS* THE SHARED CHECKOUT — the one every OTHER worker's engine resolves through, so
            repairing it is a FLEET-WIDE action. On a board running several lanes at once that repair is
            SERIALISED BY THE HOST, and workers are routinely told to hold the claim, build nothing, and
            report. IF THAT IS YOUR INSTRUCTION, THE REMEDY ABOVE IS NOT YOURS TO RUN — and waiting for
            it is not free, because this guard also refuses the verb that would renew your lease."
  fi

  # THE REF IS THE ONE THIS GUARD MEASURED, or none at all. `$b` is set only by the `behind` arm, and
  # naming a literal `origin/main` on the other arms would print a remedy against a ref the count was
  # never taken over — the same mistake the `merge --ff-only $b` note above refuses.
  #
  # AND IT IS RE-READ HERE RATHER THAN INHERITED, BECAUSE .github#2653 MOVED THE MEASUREMENT OUT OF THIS
  # FUNCTION. When this block was first written, `stale_guard` computed the drift itself and `$b` was
  # simply still in scope. `stale_detail` is now a separate function AND is called through `$(…)` — a
  # SUBSHELL — so nothing it assigns can reach this scope at all: neither a `local` nor the
  # `AUTHORED_ENGINE_SOURCE_REASON` out-parameter idiom this file uses two guards above, which works only
  # because `authored_engine_source` is called directly. Left inheriting, `$b` reads EMPTY here forever
  # and the `else` arm below wins unconditionally — the runnable command this whole block exists to print
  # becomes unreachable text, silently, with no error anywhere. The guard would still refuse correctly and
  # still print a recovery; it would just never print the one that repair 1 was about. This is the same
  # class as #266 one level down: a message asserting a route it structurally cannot deliver.
  #
  # RE-INVOKING `upstream_drift` IS NOT A SECOND COPY OF THE PREDICATE — it is the same function, asked
  # again, and it is the only thing in this file that can answer. It performs no network I/O (this file's
  # "no network, ever" rule; it reads `refs/remotes/origin/*` and counts), and it is paid ONLY on the path
  # where a refusal is already being composed — the slow path by construction, alongside the
  # `git worktree list` the .github#2402 block above already pays here.
  #
  # AND THE `behind` GATE IS KEPT ON THE RE-READ, so the failure direction is the safe one. If this second
  # read disagrees with the one `stale_detail` took — a ref that moved between them, a probe that stopped
  # answering — `$b` goes empty and the reader gets the generic `else` arm. A degraded remedy is the
  # correct outcome; printing a ref this guard did not measure is the outcome that is not.
  #
  # shellcheck disable=SC2034   # `a` is the drift COUNT: read to consume the field, reported by `$detail`.
  local kind a b=""
  IFS="$(printf '\t')" read -r kind a b <<<"$(upstream_drift "$top")"
  [ "$kind" = behind ] || b=""
  #
  # AND THE DIRECTORY IS PER-INVOCATION, NEVER A FIXED PATH (.github#2581, review round 0, repair 1).
  # The first draft of this block named a literal `/tmp/fsgg-engine-current`, and that fails in EXACTLY
  # the regime the paragraph above it describes. Several workers blocked at once is not an edge case
  # here — it is the stated premise — and the SECOND of them to run the printed command gets
  # `fatal: '/tmp/fsgg-engine-current' already exists`, rc 128: a git error with no visible connection to
  # engine staleness, leaving the refusal standing. That is the failure this module already refuses to
  # ship three paragraphs above, in the .github#1664 note — a remedy that runs, prints an unrelated error
  # and changes nothing "is how a fleet learns to route around its own escape hatch".
  #
  # WORSE, THE TWO LINES AFTER THE FAILED `worktree add` STILL RAN, so a leftover directory from an
  # earlier session was silently adoptable as "current": they would build and export whatever ref that
  # leftover happens to be detached at, and tier 1 then execs it with BOTH guards skipped by design. A
  # path whose NAME asserts currency it cannot keep is #929/#1507's hazard reintroduced by the remedy
  # meant to avoid it. `mktemp -d` answers both halves — the directory is fresh and unique per reader, so
  # there is nothing to collide with and nothing to adopt — and the `&&` chain makes the tail fail closed,
  # so no reader can end up exporting a bin built from a tree the command did not just create.
  if [ -n "$b" ]; then
    recovery="$recovery

            KEEP YOUR CLAIM WITHOUT TOUCHING THAT CHECKOUT. A CURRENT engine you build YOURSELF lifts
            this refusal for every verb, immediately, with no shared-checkout repair and no host:
              eng=\"\$(mktemp -d \"\${TMPDIR:-/tmp}/fsgg-engine-XXXXXX\")\" &&
                git worktree add --detach \"\$eng\" $b &&
                dotnet build \"\$eng/src/FS.GG.Coord.Cli\" -c Release &&
                export FSGG_COORD_ENGINE_BIN=\"\$eng/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine\"

            A FRESH DIRECTORY PER READER IS THE POINT, not tidiness: under host-serialised repair several
            workers are blocked in the SAME window by construction, so a fixed path would hand the second
            one a collision instead of an engine, and a leftover one would hand it a stale engine named
            'current'. The chain is \`&&\` for the same reason — if the checkout fails, nothing after it
            runs. When you no longer need it: \`git worktree remove --force \"\$eng\"\`."
  else
    recovery="$recovery

            KEEP YOUR CLAIM WITHOUT TOUCHING THAT CHECKOUT. A CURRENT engine you build YOURSELF lifts
            this refusal for every verb, immediately, with no shared-checkout repair and no host: check
            out the default branch into a directory you own, build it there, and name it:
              dotnet build <your-own-checkout>/src/FS.GG.Coord.Cli -c Release
              export FSGG_COORD_ENGINE_BIN=<your-own-checkout>/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
  fi

  recovery="$recovery

            BUILD the engine you name. An explicit bin is honoured BEFORE any guard runs, so it will run
            whatever you point it at — stale or dirty — and warn about neither.
            The other route is tier 2a, \`dotnet build\` in your OWN worktree, but only while your head is
            still free to move: clearing drift there means YOUR HEAD must contain it, and rebasing a
            branch an independent critic may already have confirmed is authoring, not landing."

  # AND FOR THE LEASE-RENEWAL VERB, THE CONSEQUENCE THE GENERIC REFUSAL HIDES. Every other refused write
  # can simply be retried later; this one cannot, because the thing it renews is what "later" is spent
  # from. Keyed on $verb, exactly as the delivery-route read arm below is — no new verb set, and the
  # partition is untouched.
  if [ "$verb" = "heartbeat" ]; then
    recovery="$recovery

            AND THIS REFUSAL CAN OUTLIVE THE LEASE IT IS STANDING ON (.github#2581). 'heartbeat' is the
            verb that renews your claim, so being refused it spends the very thing you need in order to
            act once you are unblocked. An EXPIRED lease cannot be renewed in place — the holder must
            RE-CLAIM, which changes the claim generation, which reds 'claim-generation' until 'delivery'
            is re-issued. Measured twice in one session: .github#2549 and .github#2563. Waiting is
            therefore the one response that cannot work here. Take the route above."
  fi

  # `delivery-route` has both evidence reads and a receipt-ledger write.  Its verb remains in the
  # conditional-write partition, but these two grammar-anchored read arms may diagnose on stale code;
  # `record` and every unknown subcommand remain fail-closed below.
  if [ "$verb" = "delivery-route" ] && { [ "${4:-}" = "validate" ] || [ "${4:-}" = "show" ]; }; then
    echo "fsgg-coord: WARNING — the engine is STALE, so it need not behave as the code you are reading does." >&2
    printf '%s\n' "$detail" >&2
    echo "            Board writes are refused until you do." >&2
    return 0
  fi

  # ONE SPACE BETWEEN THE SETS, spaces added once around the whole — see the note above the sets. A padded
  # concatenation would put a DOUBLE space here and refuse the empty verb of a bare invocation.
  case " $BOARD_WRITES $BOARD_WRITES_CONDITIONAL " in
    *" $verb "*)
      die "REFUSED — '$verb' writes the board the whole fleet shares, and the engine is STALE.
$detail$recovery" ;;
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
$detail$recovery" ;;
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
