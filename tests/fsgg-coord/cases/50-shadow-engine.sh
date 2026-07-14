#!/usr/bin/env bash
# case: shadow engine
# tier: full
# covers: divergence lanes ledger
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="50-shadow-engine"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ==================================================================================================
# THE SHADOW (ADR-0034 Phase 2) — and the only three things it is allowed to do.
# ==================================================================================================
# The typed F# engine runs beside bash on every scheduling call, bash's answer is returned, and the
# disagreement is logged. Three properties, and every one of them is load-bearing:
#
#   1. IT MAY NOT CHANGE THE ANSWER. Byte-identical stdout and exit code, shadow on or off. If this
#      assertion ever fails, the shadow has stopped being an observer and become a participant — and
#      it will have done so on a tool that hands work to a live fleet.
#   2. A MISSING ENGINE IS A SKIP, NOT AN ERROR. This script is byte-copied into six repos with no
#      .NET on the coordination path until Phase 3. The shadow must not be able to red their CI.
#   3. ...AND THEREFORE IT MUST NOT BE ABLE TO GO SILENTLY VACUOUS. (1) and (2) have just specified a
#      component designed to do nothing when it goes wrong — which is epic #266's shape exactly. So
#      the log must record that it RAN and WHAT it compared, and `divergence` must refuse to call an
#      empty log green. A shadow nobody can prove ran is not evidence, and "zero divergence" would be
#      the most reassuring possible way to say "we never looked".
export FSGG_COORD_DIVERGENCE_LOG="$WORK/divergence.jsonl"
: >"$FSGG_COORD_DIVERGENCE_LOG"

# The engine, if this checkout has one built. CI builds it first and sets FSGG_SHADOW_REQUIRE_ENGINE=1,
# which turns "no engine" from a skip into a FAILURE — because a fixture that quietly skips the half of
# itself that does the actual comparing is the very thing this section exists to forbid.
ENGINE="${FSGG_COORD_ENGINE_BIN:-$HERE/../../src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"
if [ ! -x "$ENGINE" ]; then
  if [ -n "${FSGG_SHADOW_REQUIRE_ENGINE:-}" ]; then
    bad "shadow: the engine must be built before this fixture runs" "not executable: $ENGINE"
  else
    echo "SKIP  shadow: no engine at $ENGINE (build: dotnet build src/FS.GG.Coord.Cli -c Release)"
  fi
fi

# ---- 0. THE DEFAULT IS `auto`, AND THAT IS WHAT MAKES THE CLOCK START ------------------------------
# `--engine=bash` as the default was faithful to the roadmap ("defaulting off") and produced a harness
# that never runs: nothing in the fleet sets FSGG_COORD_ENGINE, so nothing ever compares, the log stays
# empty forever, and "zero divergence across the live fleet for three consecutive days" can never be
# met — because the clock never starts. An observer nobody switches on is a decoration.
#
# So: shadow WHEREVER AN ENGINE EXISTS, bash everywhere else. The presence of the engine IS the switch,
# which is the honest gate — a repo that has not got one cannot be broken by a thing that is not there.
: >"$FSGG_COORD_DIVERGENCE_LOG"
auto_noeng="$(PATH="$STUB:$PATH" FSGG_COORD_ENGINE_BIN=/nonexistent \
                bash "$COORD" batch --repo rendering --json 2>/dev/null)" && auto_rc=0 || auto_rc=$?
assert_eq "shadow: with NO engine, the default is plain bash — a receiver is untouched" \
  "0" "$(wc -c <"$FSGG_COORD_DIVERGENCE_LOG" | tr -d ' ')"
assert_eq "shadow: ...and it decided normally" "0" "$auto_rc"

# ---- 3. an empty log is NOT green. It is no-verdict, and no-verdict is non-zero. -------------------
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq 'shadow: `divergence` over an EMPTY log exits 3 (no-verdict), never 0' "3" "$rc"
assert_contains "shadow: ...and says so — an empty log is zero EVIDENCE, not zero divergence" \
  "zero EVIDENCE" "$(run divergence 2>&1 >/dev/null || true)"

# ---- 2. a missing engine is a logged SKIP, and bash is untouched -----------------------------------
: >"$FSGG_COORD_DIVERGENCE_LOG"
plain="$(run batch --repo rendering --json 2>/dev/null)" && plain_rc=0 || plain_rc=$?
noeng="$(FSGG_COORD_ENGINE_BIN=/nonexistent PATH="$STUB:$PATH" FSGG_COORD_ENGINE=shadow \
           bash "$COORD" batch --repo rendering --json 2>/dev/null)" && noeng_rc=0 || noeng_rc=$?
assert_eq "shadow: a MISSING engine does not change bash's answer"    "$plain" "$noeng"
assert_eq "shadow: ...nor its exit code (a receiver's CI must not red)" "$plain_rc" "$noeng_rc"
assert_eq "shadow: ...and the skip is RECORDED, not silent" \
  "false" "$(jq -s -r '.[-1].ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
assert_contains "shadow: ...naming the cause, so a silent no-op is impossible to mistake for agreement" \
  "no engine" "$(jq -s -r '.[-1].reason' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo "")"

if [ -x "$ENGINE" ]; then
  # ---- 1. THE ANSWER IS BASH'S. Byte-identical, shadow on or off. --------------------------------
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  shadowed="$(FSGG_COORD_ENGINE_BIN="$ENGINE" FSGG_COORD_ENGINE=shadow run batch --repo rendering --json 2>/dev/null)" \
    && sh_rc=0 || sh_rc=$?
  assert_eq "shadow: the shadowed answer IS bash's answer, byte for byte" "$plain" "$shadowed"
  assert_eq "shadow: ...and the exit code is bash's too" "$plain_rc" "$sh_rc"

  # ---- 3. NON-VACUITY. It ran, and it compared something. ----------------------------------------
  assert_eq "shadow: the run is RECORDED as having happened" \
    "true" "$(jq -s -r '[.[] | select(.ran)] | last | .ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  compared="$(jq -s -r '[.[] | select(.ran) | .compared] | add // 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo 0)"
  if [ "${compared:-0}" -gt 0 ]; then
    ok "shadow: it compared $compared item-verdict(s) — the comparison is not vacuous"
  else
    bad "shadow: it compared NOTHING" "a shadow that compares zero items reports zero divergence, which is indistinguishable from success"
  fi

  # ---- THE OBSERVER MAY NOT KILL THE CALLER --------------------------------------------------------
  # The sharpest edge in this whole design, and it took a review to see it.
  #
  # The shadow reads markers for the candidates bash SHORT-CIRCUITS (a blocked one never has its lock
  # read). Those reads go through `claims_of`, which is documented "or DIE" and means it — a lock
  # guessed from a failed read is the one thing a lock may never be. But `die` is `kill -s TERM $$`: it
  # takes down the TOP-LEVEL shell, and NO `|| true` can catch a signal.
  #
  # So one transient 5xx on a blocked candidate's comments would have aborted the entire tool. A worker
  # running `--engine shadow take` would get a hard failure and NO ITEM — on a run bash alone completes
  # without noticing. The observer would have changed the answer, which is the one thing it exists not
  # to do. Rendering#200 is blocked, so it is swept and never otherwise read; failing its comment read
  # reproduces exactly that.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  : >"$STORE/posted-FS-GG__FS.GG.Rendering-200"     # arms GH_FAIL_READ_ISSUE for that subject
  killed="$(FSGG_COORD_ENGINE_BIN="$ENGINE" FSGG_COORD_ENGINE=shadow \
              GH_FAIL_READ_ISSUE='FS-GG/FS.GG.Rendering#200' \
              run batch --repo rendering --json 2>/dev/null)" && killed_rc=0 || killed_rc=$?
  rm -f "$STORE/posted-FS-GG__FS.GG.Rendering-200"
  assert_eq "shadow: a DYING read inside the observer does not kill the tool (exit code is bash's)" \
    "$plain_rc" "$killed_rc"
  assert_eq "shadow: ...and bash's answer survives it intact" "$plain" "$killed"
  assert_eq "shadow: ...and the unobservable candidate is COUNTED, not silently dropped" \
    "true" "$(jq -s -r '([.[] | select(.ran) | .unobserved // 0] | add // 0) > 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"

  # ---- `--ignore-blocked` MUST NOT MANUFACTURE OUTCOME DIVERGENCES ------------------------------
  # The flag is a DIAGNOSTIC ("what WOULD be startable if the blockers cleared") and it relaxes the
  # blocker filter and nothing else. The shadow's premise is that both engines decide from the same
  # observations — so it must hand the engine the rule bash ENFORCED, not the rule bash knows.
  #
  # Before this was fixed, the snapshot still carried the blockers. The engine dutifully returned
  # `blocked-by` for every candidate bash had deliberately let through, and each one was logged as an
  # OUTCOME divergence — the RELEASE-BLOCKING class — while both engines were behaving exactly as
  # designed. A diagnostic flag would have poisoned the one signal that has to stay trustworthy, and
  # `--engine=fs` would have been held back by a disagreement that never existed.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  FSGG_COORD_ENGINE_BIN="$ENGINE" FSGG_COORD_ENGINE=shadow \
    run batch --repo rendering --include-backlog --ignore-blocked --json >/dev/null 2>&1 || true
  assert_eq "shadow: --ignore-blocked reports ZERO outcome divergences (the engine is told what bash ENFORCED)" \
    "0" "$(jq -s -r '[.[] | select(.ran) | .outcome] | add // 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  assert_eq "shadow: ...and it still actually compared the candidates, rather than dodging them" \
    "true" "$(jq -s -r '([.[] | select(.ran) | .compared] | add // 0) > 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"

  # ---- HOW A RECEIVER ACTUALLY GETS THE ENGINE — and both ways were broken ------------------------
  # Two resolution paths exist and NEITHER was exercised until now, because CI exports
  # FSGG_COORD_ENGINE_BIN and an explicit path SHORT-CIRCUITS the resolver by design. Every run below
  # therefore clears it. Without that, these assertions go green over the code they exist to cover —
  # which is the same shape as the bug they are testing for.
  #
  # A NOTE ON THE STUB BINARY. `cp "$ENGINE" "$STUB/..."` does NOT work: the built engine is a native
  # apphost that resolves its own .dll, FSharp.Core and runtimeconfig.json RELATIVE TO ITSELF, so a
  # lone copy of it is a binary that starts and immediately dies. That is exactly what happened, and
  # the leaked env var hid it. A wrapper script is the honest simulation of "a binary on PATH".
  engine_on_path() { printf '#!/usr/bin/env bash\nexec "%s" "$@"\n' "$ENGINE" >"$STUB/fsgg-coord-engine"; chmod +x "$STUB/fsgg-coord-engine"; }

  # (1) A LOCAL TOOL — the receivers' shape, and the one that would have made the whole distribution
  #     a no-op. `dotnet tool restore` installs a MANIFEST-scoped tool, and a local tool is
  #     deliberately NOT placed on $PATH: it answers to `dotnet tool run <command>` and nothing else.
  #     A resolver that only did `command -v fsgg-coord-engine` would find NOTHING there — the package
  #     ships, the manifest lands, the tool restores, and the shadow silently never runs. Shipped,
  #     installed, and inert.
  rm -f "$STUB/fsgg-coord-engine"          # no binary on PATH: force the local-tool path
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  LOCALTOOL="$WORK/localtool"; mkdir -p "$LOCALTOOL/.config"
  git -C "$LOCALTOOL" init -q >/dev/null 2>&1
  cat >"$LOCALTOOL/.config/dotnet-tools.json" <<'TOOLS'
{ "version": 1, "isRoot": true,
  "tools": { "fs.gg.coord.cli": { "version": "0.1.0", "commands": ["fsgg-coord-engine"] } } }
TOOLS
  # A `dotnet` implementing exactly the one contract the resolver depends on. If the resolver never
  # reaches for `dotnet tool run`, this is never called, the run records a SKIP, and the assertion
  # fails — which is what makes it bite.
  cat >"$STUB/dotnet" <<DOTNET
#!/usr/bin/env bash
if [ "\$1" = "tool" ] && [ "\$2" = "run" ] && [ "\$3" = "fsgg-coord-engine" ]; then
  shift 3; exec "$ENGINE" "\$@"
fi
echo "dotnet stub: unhandled: \$*" >&2; exit 3
DOTNET
  chmod +x "$STUB/dotnet"
  localout="$( cd "$LOCALTOOL" && PATH="$STUB:$PATH" FSGG_COORD_CACHE="$FSGG_COORD_CACHE" \
                 FSGG_COORD_ENGINE_BIN= bash "$COORD" batch --repo rendering --json 2>/dev/null || true )"
  assert_eq "shadow: a LOCAL tool (dotnet tool restore) resolves — it is NOT on PATH, and never will be" \
    "true" "$(jq -s -r '[.[] | select(.ran)] | last | .ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  assert_eq "shadow: ...and it compared, rather than shipping an inert tool" \
    "true" "$(jq -s -r '([.[] | select(.ran) | .compared] | add // 0) > 0' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  assert_eq "shadow: ...and STILL returned bash's answer" "$plain" "$localout"
  rm -f "$STUB/dotnet"

  # (2) A GLOBAL TOOL on PATH — `dotnet tool install -g`, or anything the operator put there.
  engine_on_path
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  viapath="$(FSGG_COORD_ENGINE_BIN= FSGG_COORD_ENGINE=shadow run batch --repo rendering --json 2>/dev/null || true)"
  assert_eq "shadow: the engine resolves off PATH (the shape the Phase 3 shim will use)" "$plain" "$viapath"
  assert_eq "shadow: ...and that run is recorded as RAN, not skipped" \
    "true" "$(jq -s -r '[.[] | select(.ran)] | last | .ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"

  # AND WITH NO ENV VAR AT ALL. This is the assertion the whole phase turns on: an engine on PATH and
  # nobody opting in to anything must still produce evidence, or the three-day clock never starts.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  autoout="$(FSGG_COORD_ENGINE_BIN= run batch --repo rendering --json 2>/dev/null || true)"
  assert_eq "shadow: an engine on PATH shadows BY DEFAULT — no env var, no flag, no ceremony" \
    "true" "$(jq -s -r '[.[] | select(.ran)] | last | .ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  assert_eq "shadow: ...and it STILL returns bash's answer, byte for byte" "$plain" "$autoout"

  # ...and `--engine bash` remains the escape hatch: never shadow, whatever is on PATH.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  offout="$(FSGG_COORD_ENGINE_BIN= run --engine bash batch --repo rendering --json 2>/dev/null || true)"
  assert_eq "shadow: --engine bash refuses to shadow even with an engine right there" \
    "0" "$(wc -c <"$FSGG_COORD_DIVERGENCE_LOG" | tr -d ' ')"
  assert_eq "shadow: ...and answers identically" "$plain" "$offout"
  rm -f "$STUB/fsgg-coord-engine"
fi

# ---- the classifier: OUTCOME and REASON are not the same news, and must not be summed -------------
# Fed by hand, because the point is the CLASSIFICATION, not the engines. An outcome divergence means
# the two disagree about whether an item may be HANDED OUT — that is how two workers end up in one
# file, and it blocks the flip. A reason divergence means they agree it is unschedulable and name a
# different fact; at Phase 2 that is EXPECTED (they check in a different order) and it is a decision
# to record, not a bug. Summing them would bury the first in the second, and "zero divergence for
# three days" would never go green for something that was never wrong.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"compared":4,"extraReads":2,"outcome":1,"reason":1,"unpaired":0,"divergences":[{"id":"FS.GG.SDD#1","class":"outcome","bash":"startable","engine":"held-by"},{"id":"FS.GG.SDD#2","class":"reason","bash":"blocked-by","engine":"held-by"}]}
{"ts":"2026-07-12T10:01:00Z","mode":"shadow","ran":false,"reason":"no engine on PATH"}
JSONL
# `|| true` is REQUIRED, and the reason is the contract under test: `divergence` exits 1 when the
# engines disagree about what may be handed out. Under `set -euo pipefail` an unguarded capture of a
# non-zero command kills the fixture — which is exactly what it did, silently truncating this whole
# section on the first run.
d="$(run divergence --json 2>/dev/null || true)"
assert_eq "shadow: OUTCOME divergences are counted apart"  "1" "$(jq -r '.outcome' <<<"$d")"
assert_eq "shadow: REASON divergences are counted apart"   "1" "$(jq -r '.reason'  <<<"$d")"
assert_eq "shadow: SKIPPED runs are counted, and are not agreement" "1" "$(jq -r '.skipped' <<<"$d")"
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: an OUTCOME divergence exits 1 — the flip is BLOCKED" "1" "$rc"

# ...and with the outcome divergence gone, a reason divergence alone must NOT block the flip.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"compared":4,"extraReads":0,"outcome":0,"reason":3,"unpaired":0,"divergences":[{"id":"FS.GG.SDD#2","class":"reason","bash":"blocked-by","engine":"held-by"}]}
JSONL
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: reason divergences alone exit 0 — they are a decision, not a defect" "0" "$rc"

# ---- A RUN THAT COMPARED NOTHING IS NOT AGREEMENT. ------------------------------------------------
# Found by running the shadow ONCE against the real board. The queue happened to hold no Ready item, so
# the shadow ran, compared ZERO verdicts, and `divergence` printed `green on OUTCOME` — a gate reporting
# green over a subject it never read, which is epic #266 EXACTLY, rebuilt inside the tool whose whole
# purpose is to retire it. It survived precisely as long as it took to run against real data once.
#
# `ran` is not the bar. `compared` is. A run over an empty candidate set agrees with every engine ever
# written, because it decided nothing — and three days of an empty queue is not three days of agreement.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"compared":0,"extraReads":0,"outcome":0,"reason":0,"unpaired":0,"divergences":[]}
JSONL
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: a run that compared ZERO verdicts is no-verdict (exit 3), NOT green" "3" "$rc"
assert_contains "shadow: ...and says why — an empty queue agrees with everything" \
  "compared ZERO" "$(run divergence 2>&1 >/dev/null || true)"

# ---- THE OTHER TWO WAYS TO DISAGREE, both of which used to score as AGREEMENT ---------------------
# `outcome` counts items on which the two engines ruled DIFFERENTLY. It therefore cannot see either of
# the states below, because in both of them the engine produced no per-item ruling to differ WITH — so
# `outcome` was 0, and a green sailed out of a run in which the engines could not have agreed less.

# (a) THE ENGINE REFUSED THE BATCH. An in-flight reservation whose touch-set is unmatchable reserves
#     NOTHING while occupying files (#273), so the engine refuses to schedule at all. `decisions` is
#     empty. If bash proceeded, that is the sharpest disagreement available — not a quiet one.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"engineVerdict":"red","compared":0,"extraReads":0,"unobserved":0,"outcome":0,"reason":0,"unpaired":6,"divergences":[]}
JSONL
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: an engine that REFUSED the batch is never green (it agreed to nothing)" "3" "$rc"

# (b) AN ITEM ONLY ONE ENGINE RULED ON. Under `-n` each engine stops at its own cap, so a different
#     EVALUATED SET is a divergence in the fold even when every shared verdict matches. `compared` now
#     counts PAIRS, not the union — the union is what let this read as a large, confident comparison.
cat >"$FSGG_COORD_DIVERGENCE_LOG" <<'JSONL'
{"ts":"2026-07-12T10:00:00Z","mode":"shadow","ran":true,"engineVerdict":"green","compared":5,"extraReads":0,"unobserved":0,"outcome":0,"reason":0,"unpaired":2,"divergences":[{"id":"FS.GG.SDD#9","class":"bash-only","bash":"startable","engine":null}]}
JSONL
rc=0; run divergence >/dev/null 2>&1 || rc=$?
assert_eq "shadow: an item ruled on by ONE engine only is RED — the folds evaluated different sets" "1" "$rc"
assert_contains "shadow: ...and the report says what was NOT compared, not just what was" \
  "NOT compared" "$(run divergence 2>/dev/null || true)"

# ==================================================================================================
# THE FLEET LEDGER (#634) — the shadow's evidence becomes an ARGUMENT, or it is not evidence.
# ==================================================================================================
# Everything above tests ONE MACHINE'S log. ADR-0034 §5 gates the cut-over on the LIVE FLEET, for three
# consecutive days — and until #634 that sentence was not a function anywhere: the log sat in a
# disposable cache dir, the rows did not say who wrote them, nothing collected them, and nothing
# computed the criterion. These assertions are the criterion, and every one of them is a way the fold
# could have reported a fleet that agreed when it had established nothing of the kind.
if [ -x "$ENGINE" ]; then
  mkissue 635 "FS-GG/.github"                       # the ledger
  LEDGERLOG="$WORK/ledger-local.jsonl"
  # The stub's `issue_guard` keys a fixture seeded by `mkissue <num>` under the BARE number, so the
  # comment store is `comments-635.json` — not the owner-qualified name. Pointing at the wrong file
  # would make every reset here a silent no-op, and rows would leak between the cases below.
  LEDGER_CF="$STORE/comments-635.json"

  # Days are relative to the REAL clock, because `--fleet` asks the engine about `date -u +%F`. The
  # coverage window for requiredDays=3 is [today-3, today-2, today-1] — today is PARTIAL and excluded.
  TODAY="$(date -u +%F)"
  D1="$(date -u -d "$TODAY -3 days" +%F)"
  D2="$(date -u -d "$TODAY -2 days" +%F)"
  D3="$(date -u -d "$TODAY -1 days" +%F)"

  led() { PATH="$STUB:$PATH" FSGG_COORD_LEDGER_REPO="FS-GG/.github" FSGG_COORD_LEDGER_ISSUE=635 \
            FSGG_COORD_DIVERGENCE_LOG="$LEDGERLOG" FSGG_COORD_ENGINE_BIN="$ENGINE" \
            bash "$COORD" "$@"; }

  # A row as the shadow writes one. `$1`=day `$2`=worker `$3`=compared `$4`=outcome `$5`=engine
  logrow() {
    printf '{"ts":"%sT09:00:00Z","mode":"auto","repo":"","worker":"%s","engine":"%s","ran":true,"engineVerdict":"green","compared":%s,"outcome":%s,"reason":0,"unpaired":0}\n' \
      "$1" "$2" "${5:-0.1.0}" "$3" "$4" >>"$LEDGERLOG"
  }
  markers() { jq '[ .[] | select(.body | test("^<!--\\s*fsgg:divergence\\s")) ] | length' "$LEDGER_CF" 2>/dev/null || echo 0; }

  # ---- publishing --------------------------------------------------------------------------------
  : >"$LEDGERLOG"; : >"$LEDGER_CF"; echo '[]' >"$LEDGER_CF"
  rc=0; led divergence --publish >/dev/null 2>&1 || rc=$?
  assert_eq 'ledger: an EMPTY local log publishes nothing and exits 3 — it is not evidence, and not success' \
    "3" "$rc"

  printf '{"ts":"%sT09:00:00Z","mode":"auto","worker":"w-a","engine":"none","ran":false,"reason":"no engine"}\n' "$D1" >"$LEDGERLOG"
  rc=0; led divergence --publish >/dev/null 2>&1 || rc=$?
  assert_eq 'ledger: a log of only SKIPPED runs publishes nothing (a shadow that never ran compared nothing)' \
    "3" "$rc"

  : >"$LEDGERLOG"
  logrow "$D1" w-alpha 12 0; logrow "$D2" w-beta 9 0; logrow "$D3" w-alpha 7 0
  led divergence --publish >/dev/null 2>&1 || true
  assert_eq 'ledger: publish posts one marker per (worker, day, engine)' "3" "$(markers)"

  # THE IDEMPOTENCE ASSERTION, AND IT IS THE ONE THAT MATTERS. A worker publishes after every loop. If
  # `--publish` APPENDED, the ledger would double-count its own evidence on every run — and `compared`
  # would climb toward the quorum without a single new verdict having been compared. The clock would be
  # advanced by re-reading the same day.
  led divergence --publish >/dev/null 2>&1 || true
  assert_eq 'ledger: re-publishing REWRITES the worker-day row — the ledger is a set of facts, not a log of them' \
    "3" "$(markers)"

  # AN ENGINE THAT IS NOT A VERSION NEVER REACHES THE LEDGER AT ALL (#644, #656).
  #
  # This started life as a near-miss test: the marker lookup matched by REGEX, so an engine version
  # `0.1.0` — whose dots are wildcards — would happily claim a stored row reading `0x1x0` and PATCH
  # somebody else's evidence out of existence. The lookup was fixed to compare captured fields for
  # EQUALITY, and it still does.
  #
  # But the real guard now sits one step earlier and is stronger: a row whose `engine` is not a VERSION
  # is not published at all. Before #644 the stamp was the engine's whole decision document, and those
  # rows are still in every worker's local log; folding one emits a marker whose `engine=` is a JSON blob
  # full of spaces and quotes, and `--fleet` REFUSES to compute a verdict over a marker it cannot parse.
  # ONE bad marker would make the ledger permanently unreadable for everybody.
  #
  # Evidence that cannot be attributed to a build is not evidence. Drop it here, where it can still be
  # dropped quietly — not in the ledger, where it cannot.
  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-alpha 12 0 "0x1x0"                          # not a version. Unattributable.
  printf '{"ts":"%sT09:00:00Z","mode":"auto","repo":"","worker":"w-blob","engine":[{"schema":"fsgg.coord.decision/1"}],"ran":true,"engineVerdict":"green","compared":9,"outcome":0,"reason":0,"unpaired":0}\n' "$D1" >>"$LEDGERLOG"
  led divergence --publish >/dev/null 2>&1 || true
  assert_eq 'ledger: an engine that is not a VERSION never reaches the ledger — one unparseable marker makes it unreadable for everybody' \
    "0" "$(markers)"

  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-alpha 12 0 "0.1.0"                          # ...and a real version does
  led divergence --publish >/dev/null 2>&1 || true
  assert_eq 'ledger: ...while a row that names a real build publishes normally' "1" "$(markers)"

  # ---- the fold ----------------------------------------------------------------------------------
  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-alpha 12 0; logrow "$D2" w-beta 9 0; logrow "$D3" w-alpha 7 0
  led divergence --publish >/dev/null 2>&1 || true
  rc=0; out="$(led divergence --fleet --engine-version 0.1.0 2>&1)" || rc=$?
  assert_eq 'ledger: 3 covered days x 2 workers, zero divergence -> GREEN (exit 0)' "0" "$rc"
  assert_contains 'ledger: ...and it says the criterion is MET' "criterion is MET" "$out"

  # A GAP IS NOT A CLEAN DAY. This is the assertion that a "three consecutive days" rule exists to make.
  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-alpha 12 0; logrow "$D3" w-beta 9 0          # D2 missing
  led divergence --publish >/dev/null 2>&1 || true
  rc=0; out="$(led divergence --fleet --engine-version 0.1.0 2>&1)" || rc=$?
  assert_eq 'ledger: a day nobody compared anything on is NO VERDICT (exit 3), never a clean day' "3" "$rc"
  assert_contains 'ledger: ...and it NAMES the uncovered day' "$D2" "$out"

  # ONE WORKER IS NOT A FLEET. A concurrency defect cannot appear in a log only one worker wrote, so a
  # one-worker log cannot be evidence that there is no concurrency defect.
  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-solo 12 0; logrow "$D2" w-solo 9 0; logrow "$D3" w-solo 7 0
  led divergence --publish >/dev/null 2>&1 || true
  rc=0; out="$(led divergence --fleet --engine-version 0.1.0 2>&1)" || rc=$?
  assert_eq 'ledger: three PERFECT days from ONE worker is NO VERDICT — a quorum of one is not a fleet' "3" "$rc"
  assert_contains 'ledger: ...and says why a single worker cannot prove the absence of a race' \
    "cannot contain a concurrency defect" "$out"

  # EVIDENCE DOES NOT TRANSFER ACROSS BUILDS. Agreement by 0.0.9 says nothing about 0.1.0, and the
  # shadow exists to prove the build we are about to trust.
  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-alpha 12 0 0.0.9; logrow "$D2" w-beta 9 0 0.0.9; logrow "$D3" w-alpha 7 0 0.0.9
  led divergence --publish >/dev/null 2>&1 || true
  rc=0; out="$(led divergence --fleet --engine-version 0.1.0 2>&1)" || rc=$?
  assert_eq 'ledger: a ledger full of ANOTHER build is NO VERDICT — republishing the engine restarts the clock' \
    "3" "$rc"
  assert_contains 'ledger: ...and says so, rather than looking like an empty ledger' \
    "another engine build" "$out"

  # RED, AND RED BEATS EVERYTHING. A divergence is a FACT; coverage and quorum are questions about how
  # hard we looked. Thin evidence may never downgrade a disagreement we actually observed.
  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-alpha 12 0; logrow "$D2" w-beta 9 2; logrow "$D3" w-alpha 7 0
  led divergence --publish >/dev/null 2>&1 || true
  rc=0; out="$(led divergence --fleet --engine-version 0.1.0 2>&1)" || rc=$?
  assert_eq 'ledger: an OUTCOME divergence anywhere in the window is RED (exit 1) — the flip is BLOCKED' "1" "$rc"
  assert_contains 'ledger: ...and names the worker and day that disagreed' "w-beta" "$out"

  # A divergence TODAY — outside the coverage window, on a day that is not over. It still blocks: a
  # fresh disagreement is a disagreement, and waiting for the day to close before believing it is the
  # fail-open reading of the one signal that may never fail open.
  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-alpha 12 0; logrow "$D2" w-beta 9 0; logrow "$D3" w-alpha 7 0; logrow "$TODAY" w-c 3 1
  led divergence --publish >/dev/null 2>&1 || true
  rc=0; out="$(led divergence --fleet --engine-version 0.1.0 2>&1)" || rc=$?
  assert_eq 'ledger: a divergence TODAY blocks a window that is otherwise three clean days' "1" "$rc"

  # AN UNREADABLE MARKER IS NOT AN ABSENT ONE. It might be the one carrying the divergence. Dropping it
  # would turn a broken publisher into a green fleet — #461's lesson (a failed scan reading as "nothing
  # is claimed"), reaching the ledger before it can bite.
  : >"$LEDGERLOG"; echo '[]' >"$LEDGER_CF"
  logrow "$D1" w-alpha 12 0; logrow "$D2" w-beta 9 0; logrow "$D3" w-alpha 7 0
  led divergence --publish >/dev/null 2>&1 || true
  jq '. + [{id: 9999, body: "<!-- fsgg:divergence worker=w-x day=GARBAGE -->", user: {login: "x"}, updated_at: "2026-07-13T00:00:00Z"}]' \
    "$LEDGER_CF" > "$LEDGER_CF.tmp" && mv "$LEDGER_CF.tmp" "$LEDGER_CF"
  rc=0; out="$(led divergence --fleet --engine-version 0.1.0 2>&1)" || rc=$?
  assert_eq 'ledger: an UNPARSEABLE marker voids the verdict (exit 3) — it is never silently dropped' "3" "$rc"
  assert_contains 'ledger: ...and says the unreadable row might be the one that diverged' "UNREADABLE" "$out"

  # NO ENGINE, NO VERDICT. The criterion is a function in the typed core; with no core there is no
  # answer — and an answer produced by a thing that is not there is the whole defect record.
  rc=0; out="$(PATH="$STUB:$PATH" FSGG_COORD_LEDGER_REPO="FS-GG/.github" FSGG_COORD_LEDGER_ISSUE=635 \
    FSGG_COORD_DIVERGENCE_LOG="$LEDGERLOG" FSGG_COORD_ENGINE_BIN=/nonexistent \
    bash "$COORD" divergence --fleet 2>&1)" || rc=$?
  assert_eq 'ledger: with NO engine there is no fleet verdict (exit 3), never a green one' "3" "$rc"
  assert_contains 'ledger: ...and it tells you how to get one' "dotnet tool install -g" "$out"

  # THE SHADOW ROW CARRIES ITS AUTHOR AND ITS BUILD (#634 legs 1 and 2). Without these the fold cannot
  # count fleet members at all: one worker's 500 runs and 500 workers' one run each render identically.
  : >"$FSGG_COORD_DIVERGENCE_LOG"
  FSGG_COORD_ENGINE_BIN="$ENGINE" FSGG_WORKER=stamped-1 GH_BOARD_SET=pw \
    PATH="$STUB:$PATH" bash "$COORD" next --repo sdd >/dev/null 2>&1 || true
  assert_eq 'shadow: every row records WHO produced it — the fleet cannot be counted otherwise' \
    "stamped-1" "$(jq -s -r '[.[] | select(.ran)] | .[-1].worker // "MISSING"' <"$FSGG_COORD_DIVERGENCE_LOG")"
  # A VERSION STRING, and the assertion has to SAY so. "non-empty" was the first form of this check, and
  # it passed for a week over a row stamped with the engine's entire DECISION DOCUMENT — because
  # `--slurpfile engine` already bound `$engine` and the `--arg engine` beside it silently lost the
  # collision. A JSON array is non-empty, so the test agreed. `--publish` groups by this field and
  # `--fleet` counts only the build under test, so the ledger would have matched nothing, forever.
  # Assert the TYPE and the SHAPE, not merely the presence.
  assert_eq 'shadow: ...and WHICH BUILD produced it — a version STRING, not the decision document' \
    "true" "$(jq -s -r '[.[] | select(.ran)] | (.[-1].engine | type == "string" and test("^[0-9]+\\.[0-9]+"))' \
      <"$FSGG_COORD_DIVERGENCE_LOG")"
fi

# ==================================================================================================
# LANES (#428) — the partition, the ceiling, and the preference that may never subtract.
# ==================================================================================================
if [ -x "$ENGINE" ]; then
  lanes_run() { PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_ENGINE_BIN="$ENGINE" bash "$COORD" "$@"; }

  out="$(lanes_run lanes --repo sdd 2>/dev/null || true)"
  assert_contains "lanes: prints the CEILING — how many workers this board can actually absorb" \
    "CEILING:" "$out"
  assert_contains "lanes: ...and names each lane by its lowest-numbered item, deterministically" \
    "lane FS.GG.SDD#" "$out"

  # THE THREE UNLANABLE STATES STAY APART (#496, #273). Two are a chore; one is correct and must never
  # be "fixed" — an agent that declared a touch-set for an epic would make the board worse and report
  # that it had improved it.
  j="$(lanes_run lanes --repo sdd --json 2>/dev/null || echo '{}')"
  assert_eq "lanes --json: an epic's \`Paths: none\` is NOT a chore" \
    "false" "$(jq -r '[.unlanable[] | select(.reason == "declared-none") | .chore] | first // "false"' <<<"$j")"
  assert_eq "lanes --json: a FORGOTTEN touch-set IS a chore — real work nobody can pick up" \
    "true" "$(jq -r '[.unlanable[] | select(.reason == "no-touch-set") | .chore] | first // "true"' <<<"$j")"

  # THE GLUE. A lane of N items is not N items of coupled work — it is a handful of over-broad
  # declarations. `splitsInto` is what narrowing one would actually BUY, and a token that buys nothing
  # is load-bearing and must be left alone.
  assert_eq "lanes --json: the glue only names tokens whose removal actually SPLITS the lane" \
    "true" "$(jq -r '[.partition[].glue[]?.splitsInto] | all(. > 1) // true' <<<"$j")"

  # A PREFERENCE MAY NEVER SUBTRACT, AND THIS PINS THE HAZARD IT MUST SURVIVE.
  #
  # `batch` fails CLOSED on a touch-set it could not read — correctly: it will not schedule against a
  # body it never saw, and that is the whole Red leg. But `-n 1` STOPS AT THE FIRST PICK and so never
  # reaches a later unreadable item. A lane preference needs the WHOLE startable set, and asking for it
  # walks straight into one.
  #
  # These two lines are the hazard, measured: on the SAME board, the CAPPED read returns an item and the
  # UNCAPPED read returns nothing at all. Without `take`'s fallback to the capped read, one unreadable
  # issue body anywhere would leave every worker with nothing — the preference costing them their item.
  #
  # If somebody later makes the uncapped read tolerate an unreadable candidate, THIS test goes red — and
  # that is the point: it says out loud that the fallback rests on this, so the assumption cannot rot
  # silently. (The fallback's own coverage is indirect but real: the entire `take` suite above passes
  # only because it is there.)
  capped="$(PATH="$STUB:$PATH" bash "$COORD" batch --repo governance --ignore-blocked -n 1 --json 2>/dev/null || true)"
  uncapped="$(PATH="$STUB:$PATH" bash "$COORD" batch --repo governance --ignore-blocked --json 2>/dev/null || true)"
  assert_contains "lanes: the CAPPED batch returns an item on a board with an unreadable touch-set" \
    "FS.GG.Governance#202" "$capped"
  assert_eq "lanes: ...and the UNCAPPED batch returns NOTHING — the hazard take's fallback exists for" \
    "0" "$(jq 'length' <<<"${uncapped:-[]}" 2>/dev/null || echo 0)"
fi

# ==================================================================================================
# PUBLISHING IS A SIDE EFFECT OF FINISHING, BECAUSE ASKING DID NOT WORK (#656).
# ==================================================================================================
# The kit ASKED every worker to run `divergence --publish` at the end of its loop — in the canonical doc
# and in both skill roots. Measured against a live fleet of 28 workers and 597 compared item-verdicts: it
# was run ZERO times, by anybody, including by the worker who wrote it. Asking is not a mechanism.
if [ -x "$ENGINE" ]; then
  mkissue 700 "FS-GG/.github"
  PUBLOG="$WORK/pub.jsonl"
  PUB_CF="$STORE/comments-635.json"

  pub() { PATH="$STUB:$PATH" FSGG_COORD_LEDGER_REPO="FS-GG/.github" FSGG_COORD_LEDGER_ISSUE=635 \
            FSGG_COORD_DIVERGENCE_LOG="$PUBLOG" FSGG_COORD_ENGINE_BIN="$ENGINE" bash "$COORD" "$@"; }
  pubmarks() { jq '[ .[] | select(.body | test("^<!--\\s*fsgg:divergence\\s")) ] | length' "$PUB_CF" 2>/dev/null || echo 0; }

  # AN UNATTRIBUTABLE ROW MAY NEVER REACH THE LEDGER — and this is the one that would have poisoned it.
  #
  # Before #644 the engine stamp was the engine's whole DECISION DOCUMENT, not a version. Those rows are
  # still in every worker's local log. Folding one emits a marker whose `engine=` is a JSON blob full of
  # spaces and quotes — and `--fleet` REFUSES to compute a verdict over a marker it cannot parse (an
  # unreadable row might be the one that diverged). So a SINGLE bad marker makes the ledger permanently
  # unreadable, for everybody. Evidence that cannot be attributed to a build is not evidence.
  : >"$PUBLOG"; echo '[]' >"$PUB_CF"
  printf '{"ts":"2026-07-13T09:00:00Z","mode":"auto","worker":"w-old","engine":[{"schema":"fsgg.coord.decision/1","verdict":"green"}],"ran":true,"engineVerdict":"green","compared":9,"outcome":0,"reason":0,"unpaired":0}\n' >>"$PUBLOG"
  printf '{"ts":"2026-07-13T09:05:00Z","mode":"auto","worker":"w-new","engine":"0.1.1.0","ran":true,"engineVerdict":"green","compared":4,"outcome":0,"reason":0,"unpaired":0}\n' >>"$PUBLOG"
  pub divergence --publish >/dev/null 2>&1 || true
  assert_eq 'ledger: a PRE-#644 row (engine = the decision document) is NOT published — one bad marker makes the ledger unreadable for everybody' \
    "1" "$(pubmarks)"
  assert_contains 'ledger: ...and the row that DID publish names a real build' \
    "engine=0.1.1.0" "$(jq -r '[.[] | select(.body | test("fsgg:divergence"))][0].body' "$PUB_CF" 2>/dev/null || echo "")"

  # PUBLISHED BY `done`, WITHOUT ANYBODY REMEMBERING TO.
  : >"$PUBLOG"; echo '[]' >"$PUB_CF"
  printf '{"ts":"2026-07-13T10:00:00Z","mode":"auto","worker":"w-done","engine":"0.1.1.0","ran":true,"engineVerdict":"green","compared":7,"outcome":0,"reason":0,"unpaired":0}\n' >>"$PUBLOG"
  rc=0; out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_LEDGER_REPO="FS-GG/.github" \
    FSGG_COORD_LEDGER_ISSUE=635 FSGG_COORD_DIVERGENCE_LOG="$PUBLOG" FSGG_COORD_ENGINE_BIN="$ENGINE" \
    FSGG_WORKER=w-done bash "$COORD" done 'FS.GG.SDD#42' --pr 7 --flip 2>&1)" || rc=$?
  assert_eq 'done --flip: publishes the shadow evidence as a side effect of finishing — no new step, nothing to remember' \
    "1" "$(pubmarks)"
  assert_contains 'done --flip: ...and the DONE-STAMP is still the headline' "FSGG-DONE" "$out"

  # AND IT MAY NEVER COST A WORKER THEIR DONE-STAMP. A publish that fails is bookkeeping that failed; it
  # is not a verdict on merged, green, rolled-up work. Point the ledger at an issue the stub does not
  # have: the publish dies, and `done` must not.
  : >"$PUBLOG"
  printf '{"ts":"2026-07-13T10:00:00Z","mode":"auto","worker":"w-done2","engine":"0.1.1.0","ran":true,"engineVerdict":"green","compared":7,"outcome":0,"reason":0,"unpaired":0}\n' >>"$PUBLOG"
  rc=0; out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_LEDGER_REPO="FS-GG/.github" \
    FSGG_COORD_LEDGER_ISSUE=999999 FSGG_COORD_DIVERGENCE_LOG="$PUBLOG" FSGG_COORD_ENGINE_BIN="$ENGINE" \
    FSGG_WORKER=w-done2 bash "$COORD" done 'FS.GG.SDD#42' --pr 7 --flip 2>&1)" || rc=$?
  assert_eq 'done --flip: a FAILED publish does not take the done-stamp down with it (exit 0)' "0" "$rc"
  assert_contains 'done --flip: ...the stamp still lands' "FSGG-DONE" "$out"
  assert_contains 'done --flip: ...and the failure is a NOTE, explicitly not a verdict on the work' \
    "Your work is DONE and stamped" "$out"
fi

# ==================================================================================================
# A STALE ENGINE IS WORSE THAN NO ENGINE (#655).
# ==================================================================================================
# A global `dotnet tool` does not self-update and the kit only ever said INSTALL, so the fleet went on
# shadowing with 0.1.0 long after it was superseded. And 0.1.0 is not merely old, it is WRONG: it strips
# the leading dot from every dotfile path (#649), so `.github/workflows/gate.yml` becomes a token that
# matches no file, conflicts with nothing, and the engine reports STARTABLE on an item a live claim is
# HOLDING. The shadow caught that 7 times on the live board in a day.
#
# A worker with no engine contributes nothing and says so. A worker with a superseded one contributes
# divergences from a build nobody should trust — noise that buries the real findings.

# THE FLOOR MAY NEVER EXCEED THE ENGINE THIS REPO BUILDS. Otherwise it locks EVERYBODY out of shadowing:
# every worker skips, the log stays empty forever, and the three-day clock can never start — a gate that
# fails so closed it can never open. Raising the floor is a deliberate act; shipping one nothing can
# satisfy is a silent outage.
FLOOR="$(grep -E '^ENGINE_MIN_VERSION=' "$COORD" | head -1 | sed -E 's/.*"([^"]+)".*/\1/')"
SHIPPED="$(grep -oE '<Version>[^<]+</Version>' "$HERE/../../src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj" \
           | head -1 | sed -E 's#</?Version>##g')"
if [ -n "$FLOOR" ] && [ -n "$SHIPPED" ]; then
  lowest="$(printf '%s\n%s\n' "$FLOOR" "$SHIPPED" | sort -V | head -1)"
  if [ "$lowest" = "$FLOOR" ] || [ "$FLOOR" = "$SHIPPED" ]; then
    ok "engine floor: the kit's floor ($FLOOR) is satisfiable by the engine this repo builds ($SHIPPED)"
  else
    bad "engine floor: the floor ($FLOOR) EXCEEDS the shipped engine ($SHIPPED) — every worker would skip, forever"
  fi
else
  bad "engine floor: could not read ENGINE_MIN_VERSION or the fsproj <Version>"
fi

if [ -x "$ENGINE" ]; then
  # A stale engine is a RECORDED SKIP, not an error — a receiver whose worker has an old tool must not
  # have its CI redded over it, and the skip must be recorded so that "we did not compare" can never be
  # mistaken for "they agreed" (#266).
  STALEBIN="$WORK/bin/fsgg-coord-engine-stale"
  printf '#!/usr/bin/env bash\nif [ "$1" = "--version" ]; then echo "0.0.9"; exit 0; fi\nexit 1\n' > "$STALEBIN"
  chmod +x "$STALEBIN"

  : >"$FSGG_COORD_DIVERGENCE_LOG"
  stale_out="$(FSGG_COORD_ENGINE_BIN="$STALEBIN" GH_BOARD_SET=pw PATH="$STUB:$PATH" \
    bash "$COORD" next --repo sdd 2>/dev/null)"; stale_rc=$?
  plain_out="$(FSGG_COORD_ENGINE_BIN=/nonexistent GH_BOARD_SET=pw PATH="$STUB:$PATH" \
    bash "$COORD" next --repo sdd 2>/dev/null)"; plain_rc=$?

  assert_eq "stale engine: it does NOT change bash's answer" "$plain_out" "$stale_out"
  assert_eq "stale engine: ...nor its exit code — a receiver's CI may never red over an old tool" \
    "$plain_rc" "$stale_rc"
  assert_eq "stale engine: ...and the skip is RECORDED, never silent (#266)" \
    "false" "$(jq -s -r '.[-1].ran' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null || echo MISSING)"
  assert_contains "stale engine: ...naming the version, so it cannot be mistaken for agreement" \
    "STALE" "$(jq -s -r '.[-1].reason // ""' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null)"
  assert_contains "stale engine: ...and telling the worker exactly how to fix it" \
    "dotnet tool update -g" "$(jq -s -r '.[-1].reason // ""' <"$FSGG_COORD_DIVERGENCE_LOG" 2>/dev/null)"

  # LANES IS DIFFERENT, AND STRICTER. In the shadow a stale engine merely produces evidence nobody should
  # count; in `lanes` its answer is USED. A mis-parsed dotfile token puts an item in the wrong lane, and a
  # wrong lane is not a bad suggestion — it is a corrupted lock, telling a worker two items are safe to
  # run together when they are not (#649, #273).
  rc=0; out="$(FSGG_COORD_ENGINE_BIN="$STALEBIN" GH_BOARD_SET=pw PATH="$STUB:$PATH" \
    bash "$COORD" lanes --repo sdd 2>&1)" || rc=$?
  assert_eq "stale engine: lanes REFUSES to partition the board (exit 3) — a wrong lane is a corrupted lock" \
    "3" "$rc"
  assert_contains "stale engine: ...and says why, rather than printing a partition nobody should trust" \
    "STALE" "$out"

  # An engine that cannot say what it is CANNOT be known to be current. "I could not tell" is not "it is
  # fine" (#266).
  MUTEBIN="$WORK/bin/fsgg-coord-engine-mute"
  printf '#!/usr/bin/env bash\nexit 1\n' > "$MUTEBIN"; chmod +x "$MUTEBIN"
  rc=0; FSGG_COORD_ENGINE_BIN="$MUTEBIN" GH_BOARD_SET=pw PATH="$STUB:$PATH" \
    bash "$COORD" lanes --repo sdd >/dev/null 2>&1 || rc=$?
  assert_eq "stale engine: an engine that cannot report a version is treated as STALE, not as current" \
    "3" "$rc"
fi

# ==================================================================================================
# A MANIFEST IS A DECLARATION, NOT AN INSTALLATION.
# ==================================================================================================
# Every receiver carries the engine in `.config/dotnet-tools.json`. NOTHING restored it. `dotnet tool run`
# on an unrestored manifest prints *Run "dotnet tool restore"...* to STDERR and exits 1 — so the version
# capture came back EMPTY, became `unknown`, and `unknown` is stale by design (#655). Every scheduling
# call in all six receivers skipped, blaming a stale engine and telling the worker to `dotnet tool update`
# a tool they had never installed.
#
# Measured on 2026-07-14, against the live divergence log: 187 of 239 shadow runs skipped, 186 of them
# with this exact reason (`engine unknown is STALE`). The evidence the three-day clock is made of was
# being thrown away at the source, in every repo except the one that builds from source.
if [ -x "$ENGINE" ]; then
  CO_MAN="$(mkcheckout manifest https://github.com/FS-GG/FS.GG.SDD.git)"
  mkdir -p "$CO_MAN/.config"
  jq -n '{version:1, isRoot:true, tools:{"fs.gg.coord.cli":{version:"0.1.1", commands:["fsgg-coord-engine"]}}}' \
    >"$CO_MAN/.config/dotnet-tools.json"

  # The real thing's behaviour, exactly: `tool run` FAILS until `tool restore` has been called, and the
  # complaint goes to stderr where a stdout capture cannot see it.
  export DOTNET_RESTORE_MARK="$WORK/restored.mark"
  export DOTNET_RESTORE_CALLS="$WORK/restore.calls"
  : >"$DOTNET_RESTORE_CALLS"
  cat >"$STUB/dotnet" <<EOF
#!/usr/bin/env bash
if [ "\$1" = tool ] && [ "\$2" = restore ]; then
  echo restore >>"\$DOTNET_RESTORE_CALLS"
  : >"\$DOTNET_RESTORE_MARK"
  exit 0
fi
if [ "\$1" = tool ] && [ "\$2" = run ] && [ "\$3" = fsgg-coord-engine ]; then
  shift 3
  if [ ! -f "\$DOTNET_RESTORE_MARK" ]; then
    echo 'Run "dotnet tool restore" to make the "fsgg-coord-engine" command available.' >&2
    exit 1
  fi
  exec "$ENGINE" "\$@"
fi
exit 1
EOF
  chmod +x "$STUB/dotnet"

  MANLOG="$WORK/manifest.jsonl"; : >"$MANLOG"
  rm -f "$DOTNET_RESTORE_MARK"
  man_out="$(cd "$CO_MAN" && PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_ENGINE_BIN= \
    FSGG_COORD_DIVERGENCE_LOG="$MANLOG" FSGG_COORD_PUBLISH_EVERY_MIN=0 \
    bash "$COORD" next --repo sdd 2>/dev/null)" || true

  assert_eq "unrestored manifest: the tool RESTORES the engine it was told to run, rather than skipping and blaming a stale build" \
    "1" "$(wc -l <"$DOTNET_RESTORE_CALLS" | tr -d ' ')"
  assert_eq "unrestored manifest: ...and the shadow actually RAN — this is the 139-of-147 that never became evidence" \
    "true" "$(jq -s -r '.[-1].ran' <"$MANLOG" 2>/dev/null || echo MISSING)"
  assert_eq "unrestored manifest: ...under the real version, never 'unknown' (which is stale by design, so it skips)" \
    "0.1.1.0" "$(jq -s -r '.[-1].engine' <"$MANLOG" 2>/dev/null || echo MISSING)"

  # A restore that CANNOT work must not put a network round-trip in the hot loop on every call — and it
  # must not go quiet either. It skips, and the skip names the REAL failure: declared-but-broken is not
  # the same fact as absent, and a skip that named the wrong one is what sent this fleet chasing a stale
  # tool it had never installed.
  printf '#!/usr/bin/env bash\nif [ "$1" = tool ] && [ "$2" = restore ]; then exit 1; fi\nif [ "$1" = tool ]; then echo "Run \\"dotnet tool restore\\"" >&2; exit 1; fi\nexit 1\n' >"$STUB/dotnet"
  chmod +x "$STUB/dotnet"
  BADLOG="$WORK/manifest-bad.jsonl"; : >"$BADLOG"
  rc=0; (cd "$CO_MAN" && PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_ENGINE_BIN= \
    FSGG_COORD_CACHE="$WORK/cache-badrestore" FSGG_COORD_DIVERGENCE_LOG="$BADLOG" \
    FSGG_COORD_PUBLISH_EVERY_MIN=0 bash "$COORD" next --repo sdd >/dev/null 2>&1) || rc=$?
  assert_eq "unrestored manifest: a restore that fails does NOT red the caller — a receiver's CI may never break over its own bookkeeping" \
    "0" "$rc"
  assert_contains "unrestored manifest: ...and the skip names the REAL failure (declared-but-broken), not a stale build the worker never had" \
    "DECLARED" "$(jq -s -r '.[-1].reason // ""' <"$BADLOG" 2>/dev/null)"
  rm -f "$STUB/dotnet"
fi

# ==================================================================================================
# THE EVIDENCE MUST LEAVE THE MACHINE EVEN WHEN `done` NEVER RUNS.
# ==================================================================================================
# #656 bolted the publish to `done` — "the one command every worker runs when it finishes an item". It is
# not: an item closed by a SQUASH-MESSAGE closing keyword (#681, #685, #693) is merged, closed and
# board-Done without `done` ever being called, and that worker's evidence never leaves the machine.
#
# The ledger is not EMPTY — it held 59 rows when this was written, 11 of them dated 2026-07-14 — so the
# hook does work for the workers that reach it. It is not COMPLETE. Every one of those rows carries
# `skipped=0`, because the only workers that publish are the ones whose engine resolved: `.github`, which
# builds from source. The five receivers contribute nothing. A hook on ONE path is a request that the path
# be taken, and the fleet gate ends up reading one repo and calling it the fleet.
if [ -x "$ENGINE" ]; then
  SHLOG="$WORK/shadowpub.jsonl"; : >"$SHLOG"
  echo '[]' >"$PUB_CF"
  SHCACHE="$WORK/cache-shadowpub"

  shpub() { PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_LEDGER_REPO="FS-GG/.github" \
              FSGG_COORD_LEDGER_ISSUE=635 FSGG_COORD_DIVERGENCE_LOG="$SHLOG" \
              FSGG_COORD_ENGINE_BIN="$ENGINE" FSGG_COORD_CACHE="$SHCACHE" \
              FSGG_WORKER=w-shadow bash "$COORD" "$@"; }

  shpub next --repo sdd >/dev/null 2>&1 || true
  assert_eq 'shadow publish: the SHADOW pushes its own evidence — no `done`, no closing keyword, nothing to remember' \
    "1" "$(pubmarks)"

  # THROTTLED: the hot scheduling loop may not pay the network on every call (ADR-0034 §5). The second
  # run inside the window must compare, log, and NOT write.
  shpub next --repo sdd >/dev/null 2>&1 || true
  assert_eq 'shadow publish: ...but at most once per window — the hot loop may not pay the network on every call' \
    "1" "$(pubmarks)"

  # ...and the window EXPIRING lets the next run carry the day up. Late is a property of this ledger;
  # lost is not.
  echo 0 >"$SHCACHE/divergence.published"
  shpub next --repo sdd >/dev/null 2>&1 || true
  assert_eq 'shadow publish: ...and once the window expires it publishes again, rewriting the row in place (idempotent)' \
    "1" "$(pubmarks)"
  assert_contains 'shadow publish: ...naming the worker whose evidence it is' \
    "worker=w-shadow" "$(jq -r '[.[] | select(.body | test("fsgg:divergence"))][0].body' "$PUB_CF" 2>/dev/null || echo "")"

  # A publish that CANNOT work is bookkeeping that failed. It may never cost a worker their item — the
  # shadow cannot change bash's answer, its exit code, or its life.
  echo 0 >"$SHCACHE/divergence.published"
  rc=0; out="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw FSGG_COORD_LEDGER_REPO="FS-GG/.github" \
    FSGG_COORD_LEDGER_ISSUE=999999 FSGG_COORD_DIVERGENCE_LOG="$SHLOG" FSGG_COORD_ENGINE_BIN="$ENGINE" \
    FSGG_COORD_CACHE="$SHCACHE" bash "$COORD" next --repo sdd 2>&1)" || rc=$?
  assert_eq 'shadow publish: a FAILED publish does not fail the scheduling call that carried it' "0" "$rc"
  assert_contains 'shadow publish: ...and stays silent — bookkeeping may not clutter the output that carries the verdict' \
    "FS.GG.SDD#" "$out"

  # A LOCK THAT IS HELD BUT NOT YET STAMPED IS HELD.
  #
  # The lock's staleness used to be aged from an `at` file written a moment AFTER the directory appeared.
  # A racer landing in that window found no file, aged the lock from epoch 0, concluded a LIVE lock was
  # ancient, deleted it, and published alongside the holder — the exact double-publish the lock exists to
  # prevent, and two racers folding the same log each CREATE a row for their (worker, day, engine).
  #
  # And it was not a rare interleaving: the throttle SYNCHRONISES the machine. Every worker shares one
  # `divergence.published` stamp, so when the window expires they arrive at the lock together.
  #
  # Aged from the directory's own mtime — which the kernel stamps inside `mkdir` — there is no window to
  # land in. This models the racer exactly: a held lock, freshly made, with nothing written inside it.
  echo 0 >"$SHCACHE/divergence.published"       # the window is OPEN, so only the lock can hold us back
  before="$(pubmarks)"
  mkdir -p "$SHCACHE/.divergence-publish.lock"  # ...held by a worker that has not stamped it yet
  shpub next --repo sdd >/dev/null 2>&1 || true
  assert_eq 'shadow publish: a lock held-but-unstamped is NOT stale — the racer backs off instead of publishing twice' \
    "$before" "$(pubmarks)"
  assert_eq 'shadow publish: ...and it did not delete the holder’s lock on its way past' \
    "yes" "$([ -d "$SHCACHE/.divergence-publish.lock" ] && echo yes || echo no)"

  # ...but a lock whose holder DIED must not wedge the ledger for every worker on the machine forever.
  # Backdate it past the 600s reaper and the next caller collects it and publishes.
  touch -d '2 hours ago' "$SHCACHE/.divergence-publish.lock" 2>/dev/null \
    || touch -t "$(date -v-2H +%Y%m%d%H%M 2>/dev/null || echo 202001010000)" "$SHCACHE/.divergence-publish.lock" 2>/dev/null || true
  echo 0 >"$SHCACHE/divergence.published"
  shpub next --repo sdd >/dev/null 2>&1 || true
  assert_eq 'shadow publish: ...while an ABANDONED lock is still collected — a dead holder may not wedge the ledger' \
    "$(( before + 1 ))" "$(pubmarks)"
fi


harness_report
