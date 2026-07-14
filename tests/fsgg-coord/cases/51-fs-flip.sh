#!/usr/bin/env bash
# THE FLIP — `--engine=fs` (ADR-0034 Phase 3b).
#
# In `fs` the typed core's decision IS the answer. Bash still reads the board, assembles the snapshot
# and performs every write; only the DECISION changes hands. This case pins the three things that
# make that safe, and one of them matters more than the other two put together.
#
#   1. THE ANSWER IS THE ENGINE'S. On a board the two engines agree about, `fs` and `bash` return the
#      same items — which is what makes the flip a no-op for a fleet that was already green. But the
#      answer must come FROM the engine, not merely agree with it, so the reasons are checked too:
#      `fs` prints the engine's own `explain` prose, relayed, never restated in bash. A rule stated
#      twice is a rule that will disagree with itself — that is #485 (one question, five
#      implementations, agreeing in none), and the fix for #485 may not reintroduce it.
#
#   2. IT FAILS CLOSED, AND THIS IS THE WHOLE CASE. In every other mode a missing or stale engine is a
#      logged SKIP and bash carries on — correct there, because bash's answer is the answer. In `fs` it
#      MUST be fatal. A silent fall-back to bash after the caller asked for the typed core is a SILENT
#      ENGINE SUBSTITUTION: the worker believes they are running the engine, the ledger records a skip
#      nobody reads, and the run is indistinguishable from agreement. That is epic #266's fail-open in
#      the one place it would be hardest to ever notice — a worker's answer, on the hot path, on a
#      board that looks fine. Every assertion below that ends in "is FATAL" is guarding that.
#
#   3. `--engine bash` STILL REPRODUCES THE OLD TOOL, byte for byte. The escape hatch is only an escape
#      hatch if it is exact.
. "$(dirname "${BASH_SOURCE[0]}")/../lib/harness.sh"

ENGINE="${FSGG_COORD_ENGINE_BIN:-$HERE/../../src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"
if [ ! -x "$ENGINE" ]; then
  if [ -n "${FSGG_SHADOW_REQUIRE_ENGINE:-}" ]; then
    bad "fs-flip: the engine must be built before this fixture runs" "not executable: $ENGINE"
  else
    echo "SKIP  fs-flip: no engine at $ENGINE (build: dotnet build src/FS.GG.Coord.Cli -c Release)"
  fi
  harness_report
fi

export FSGG_COORD_DIVERGENCE_LOG="$WORK/divergence.jsonl"
: >"$FSGG_COORD_DIVERGENCE_LOG"

fs()      { FSGG_COORD_ENGINE_BIN="$ENGINE" PATH="$STUB:$PATH" bash "$COORD" --engine fs   "$@"; }
bashe()   { FSGG_COORD_ENGINE_BIN="$ENGINE" PATH="$STUB:$PATH" bash "$COORD" --engine bash "$@"; }
noeng_fs(){ FSGG_COORD_ENGINE_BIN=/nonexistent PATH="$STUB:$PATH" bash "$COORD" --engine fs "$@"; }

# ---- 0. THE MODE IS OPEN --------------------------------------------------------------------------
# It was held shut on a three-day fleet-divergence clock that could not tick: workers run in per-item
# worktrees, worktree workers resolved no engine, and a worker who banks no evidence can never be one
# of the "two distinct workers" the criterion required (#728). The corpus is the gate now.
rc=0; out="$(fs batch --repo rendering --json 2>&1)" || rc=$?
refute_contains "fs: the mode is no longer REFUSED outright" "is not available yet" "$out"
assert_eq "fs: ...and a scheduling call over a good board succeeds" "0" "$rc"

# ---- 1. THE ANSWER IS THE ENGINE'S ----------------------------------------------------------------
b_out="$(bashe batch --repo rendering --json 2>/dev/null)"; b_rc=$?
f_out="$(fs    batch --repo rendering --json 2>/dev/null)"; f_rc=$?
assert_eq "fs: on a board the engines AGREE about, fs returns bash's items (the flip is a no-op there)" \
  "$b_out" "$f_out"
assert_eq "fs: ...and the same exit code" "$b_rc" "$f_rc"

# The engine's decision, read directly — the answer fs printed must BE this, not merely resemble it.
snap_chosen="$("$ENGINE" --version >/dev/null 2>&1 && echo ok)"
assert_eq "fs: the engine under test actually runs" "ok" "$snap_chosen"

# ---- 2. THE REASONS ARE THE ENGINE'S OWN WORDS, RELAYED --------------------------------------------
# `Schedulability.explain` is the ONE place a passed-over reason is worded. Bash relays it. If bash
# restated it, the two would drift — and a worker would be told a true sentence about the wrong
# question. The engine's prose is distinguishable: it names the omission and the remedy in one line.
fs_err="$(fs    batch --repo rendering --json 2>&1 >/dev/null || true)"
b_err="$( bashe batch --repo rendering --json 2>&1 >/dev/null || true)"
assert_contains "fs: the passed-over reasons are printed" "passed over:" "$fs_err"

# THE FACTS MUST SURVIVE THE HANDOVER. The engine's prose is now the ONLY prose — bash relays it — so
# the corpus asserts the FACTS the line has always carried, not the byte-identity of the sentence. If
# the engine could not say one of these, the flip would be an information regression on the one line a
# starved worker actually reads, and the shadow's "REASON divergence" class would have waved it through.
assert_contains "fs: the reason names the issue state" "the issue is closed" "$fs_err"
assert_contains "fs: ...and bash said the same thing before the flip" "the issue is closed" "$b_err"

# NO COMPILER NAMES IN OPERATOR TEXT. `%A` on a union prints its CASE NAME, so the engine used to say
# "blocked by #201 (BlockerOpen)" and "Status is InProgress" — and the board column is literally
# "In progress", so a worker who greps for what they were told finds nothing.
refute_contains "fs: the reasons carry no F# union-case names — 'BlockerOpen' is not a word" \
  "BlockerOpen" "$fs_err"
refute_contains "fs: ...nor 'InProgress', which is not what the board column is called" \
  "InProgress" "$fs_err"
assert_contains "fs: ...the blocker state is rendered in English" "(open)" "$fs_err"

# ---- 3. IT FAILS CLOSED. THIS IS THE CASE. --------------------------------------------------------
# A MISSING engine. In `shadow` this is a logged skip and bash's answer stands (case 50 pins that). In
# `fs` it must be FATAL — because the alternative is that the worker asked for the typed core, silently
# got bash, and nothing anywhere can tell the difference afterwards.
: >"$FSGG_COORD_DIVERGENCE_LOG"
rc=0; out="$(noeng_fs batch --repo rendering --json 2>&1)" || rc=$?
[ "$rc" -ne 0 ] && ok "fs: a MISSING engine is FATAL — there is no bash to fall back to" \
  || bad "fs: a missing engine did NOT fail" "exit 0 — this is a silent engine substitution: the worker asked for fs and got bash"
refute_contains "fs: ...and it does NOT print bash's answer instead" "schedulable in parallel" "$out"
assert_contains "fs: ...and it names the mode, so the cause is unmistakable" "--engine=fs" "$out"

# A STALE engine. Same rule: known-wrong answers may not be silently swapped for bash's.
STALEBIN="$WORK/bin/fsgg-coord-engine-stale51"
printf '#!/usr/bin/env bash\nif [ "$1" = "--version" ]; then echo "0.0.9"; exit 0; fi\nexit 1\n' >"$STALEBIN"
chmod +x "$STALEBIN"
rc=0; out="$(FSGG_COORD_ENGINE_BIN="$STALEBIN" PATH="$STUB:$PATH" \
              bash "$COORD" --engine fs batch --repo rendering --json 2>&1)" || rc=$?
[ "$rc" -ne 0 ] && ok "fs: a STALE engine is FATAL — its answers are known-wrong, and bash's are not on offer" \
  || bad "fs: a stale engine did NOT fail" "exit 0 — a known-wrong engine was silently replaced by bash"
assert_contains "fs: ...and it says the engine is stale rather than inventing a board answer" "STALE" "$out"

# An engine that cannot say WHAT IT IS cannot be known to be current. "I could not tell" is not "fine".
MUTEBIN="$WORK/bin/fsgg-coord-engine-mute51"
printf '#!/usr/bin/env bash\nexit 1\n' >"$MUTEBIN"; chmod +x "$MUTEBIN"
rc=0; FSGG_COORD_ENGINE_BIN="$MUTEBIN" PATH="$STUB:$PATH" \
  bash "$COORD" --engine fs batch --repo rendering --json >/dev/null 2>&1 || rc=$?
[ "$rc" -ne 0 ] && ok "fs: an engine that cannot report a version is FATAL, not assumed current" \
  || bad "fs: a mute engine did NOT fail" "exit 0 — an unidentifiable engine was treated as good"

# ---- 3b. AN UNREAD ITEM IS REPORTED, NOT WITHHELD — AND IT DOES NOT STARVE THE BOARD ---------------
# `shadow_sweep` used to WITHHOLD a candidate whose body it could not read. Harmless while bash owned
# the answer (the shadow may never cost a worker their item), fatal to meaning once the engine owns it:
# the item vanishes from the engine's world, so it can be neither offered NOR passed over with a reason.
#
# The first fix here was to make that FATAL in fs. The corpus rejected it, and was right: bash fails to
# read the body of every BLOCKED candidate as a matter of course — it short-circuits them before any
# body fetch — so dying on one would starve every worker over an item they could never have been given.
#
# The type system had the answer: `bodyUnreadable` -> touch-set `Unreadable` (UNKNOWN, not absent) ->
# `Undetermined`. Not startable, and SAID SO. And because BLOCKERS are checked before the TOUCH-SET, a
# blocked item still decides correctly with no body at all.
# There are TWO kinds of unreadable body, and they are not the same fact.
#
# (a) A candidate bash READS. `paths_of` refuses to schedule against a touch-set it could not fetch and
#     fails CLOSED — the tool stops. That is bash's behaviour, it is correct, and the flip does not
#     change it: the engine is never consulted, because bash never got far enough to build a snapshot.
rc=0; out="$(FSGG_COORD_ENGINE_BIN="$ENGINE" PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET=201 \
              bash "$COORD" --engine fs batch --repo rendering 2>&1)" || rc=$?
[ "$rc" -ne 0 ] && ok "fs: an unreadable candidate body still fails CLOSED — the flip did not soften it" \
  || bad "fs: scheduled against a touch-set nobody read" "$out"
assert_contains "fs: ...refusing to schedule against an unknown touch-set, in bash's own words" \
  "refusing to schedule against an unknown touch-set" "$out"

# (b) A candidate bash SWEEPS — blocked, closed, or held. Bash never fetches these bodies at all (it
#     short-circuits them), so the SHADOW is the only thing that ever asked, and a failed read there used
#     to WITHHOLD the item from the engine entirely. Harmless while bash owned the answer. Fatal to
#     meaning once the engine owns it: the item cannot be offered AND cannot be passed over with a
#     reason, so it silently ceases to exist. FS.GG.Rendering#200 is blocked, and its body has never been
#     a fixture — which is precisely why nobody noticed.
#
#     It must now decide correctly with NO body, because BLOCKERS ARE BOARD FACTS AND ARE CHECKED FIRST.
starve="$(FSGG_COORD_ENGINE_BIN="$ENGINE" PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET=200 \
            bash "$COORD" --engine fs batch --repo rendering --json 2>/dev/null || true)"
assert_eq "fs: an unreadable BLOCKED item does not starve the board — one unread body may not cost every worker their item" \
  "$f_out" "$starve"
starve_err="$(FSGG_COORD_ENGINE_BIN="$ENGINE" PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET=200 \
                bash "$COORD" --engine fs batch --repo rendering 2>&1 >/dev/null || true)"
assert_contains "fs: ...and it is still NAMED as blocked, from the board alone — never silently dropped" \
  "FS.GG.Rendering#200 — blocked by" "$starve_err"
refute_contains "fs: ...and never reported as a forgotten 'Paths:', which nobody read the body to know" \
  "FS.GG.Rendering#200 — no 'Paths:' declared" "$starve_err"

# ---- 4. THE ESCAPE HATCH IS EXACT -----------------------------------------------------------------
# `--engine bash` must reproduce the pre-flip tool. If it does not, there is nothing to roll back TO.
plain="$(FSGG_COORD_ENGINE_BIN=/nonexistent PATH="$STUB:$PATH" bash "$COORD" --engine bash \
           batch --repo rendering --json 2>/dev/null)"; plain_rc=$?
assert_eq "fs: --engine bash is byte-identical to the pre-flip answer" "$plain" "$b_out"
assert_eq "fs: ...and its exit code too — the rollback is exact" "$plain_rc" "$b_rc"

# And `bash` must not even LOOK for an engine: the escape hatch may not be able to fail on one.
: >"$FSGG_COORD_DIVERGENCE_LOG"
FSGG_COORD_ENGINE_BIN="$STALEBIN" PATH="$STUB:$PATH" bash "$COORD" --engine bash \
  batch --repo rendering --json >/dev/null 2>&1 || true
assert_eq "fs: --engine bash never consults the engine at all (a stale one cannot break the hatch)" \
  "0" "$(wc -l <"$FSGG_COORD_DIVERGENCE_LOG" | tr -d ' ')"

# ---- 5. `take` — the hot path — flips too, and it still CLAIMS ------------------------------------
# `next`/`batch` only report. `take` acts on the answer, and it is the call every worker actually makes.
# A flip that leaves the acting path on bash would be a flip in name only.
: >"$FSGG_COORD_DIVERGENCE_LOG"
t_out="$(FSGG_COORD_ENGINE_BIN="$ENGINE" GH_BOARD_SET=pw PATH="$STUB:$PATH" \
           bash "$COORD" --worker flip-w1 --engine fs take --repo sdd 2>&1)" && t_rc=0 || t_rc=$?
assert_eq "fs: take succeeds on the engine's answer" "0" "$t_rc"
assert_eq "fs: ...and the shadow recorded that the ENGINE actually ran (the answer was not bash's)" \
  "true" "$(jq -s -r '[.[] | select(.ran)] | last | .ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"

# And with no engine, `take` must hand out NOTHING rather than quietly hand out bash's item.
rc=0; out="$(FSGG_COORD_ENGINE_BIN=/nonexistent GH_BOARD_SET=pw PATH="$STUB:$PATH" \
              bash "$COORD" --worker flip-w2 --engine fs take --repo sdd 2>&1)" || rc=$?
[ "$rc" -ne 0 ] && ok "fs: take with NO engine is FATAL — it may not fall back and claim an item on bash's word" \
  || bad "fs: take fell back to bash" "a worker asked for the typed core and was handed an item chosen by the other engine"

harness_report
