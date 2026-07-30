#!/usr/bin/env bash
# Selftest for scripts/check-gate-finding-history.py (.github#1582).
#
# THE GATE THIS GUARDS IS AN AUDIT OF THE AUDIT FABRIC, so a version of it that could not fail would be
# the funniest possible entry on the very list it exists to produce. That is not a joke about this
# fixture; it is its specification. Ten checks that could not fail were found in this org on
# 2026-07-27/28 (#1644, #1715, FS.GG.Audio#212, FS.GG.Rendering#1120, #1710, #1768, #1772, #1740,
# #1784, #1799) and not one was found by the check itself. So this file has TWO parts, and the second
# is the one that matters:
#
#   PART A — INPUT MUTATION. Planted corpora, one per verdict class, each asserted to produce exactly
#            that verdict and exactly that exit code; plus a clean corpus asserted to produce NO
#            finding, which is what stops the gate inventing one (#238: a red naming the wrong file is
#            a false accusation).
#
#   PART B — SOURCE MUTATION, i.e. the proof that PART A IS NOT A TAUTOLOGY. Each leg of part A is a
#            claim that the gate reds for a REASON. Part B removes each reason from a COPY of the gate
#            and requires part A to go red against the mutant. A mutant that survives part A means part
#            A was asserting something that was true regardless — #1715's finding, where two headline
#            invariants became tautologies on a half-view, and #1740's, where `isNarrowing = true`
#            passed all 462 assertions.
#
# #1784'S BUG IS EXPLICITLY DESIGNED OUT. That item's mutation harness reported "1 leg fired" when its
# anchor had failed to match — it could not distinguish *didn't fire* from *didn't run*. So every
# mutation here is applied by an exact literal replacement that COUNTS its occurrences and hard-fails
# unless it replaced exactly one. A mutation that does not apply is a fixture bug and is reported as
# one; it is never allowed to look like a mutant that was caught.
#
# #1772'S BUG IS ALSO DESIGNED OUT: this drives the REAL gate, as a subprocess, by path. There is no
# second copy of the classification rules here for the fixture to drift against.
#
# Offline and credential-free. Classification drives planted corpora through the real gate; acquisition
# drives the real `--fetch` path against a recorded Actions REST transcript replayed by a local server.
# The transcript pins URL filters, paging shapes, jobs, annotations, and evidence loss without claiming
# a network mock proves GitHub is reachable.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
GATE="${GATE_UNDER_TEST:-$ROOT/scripts/check-gate-finding-history.py}"
PY="${PYTHON:-python3}"
export PYTHONDONTWRITEBYTECODE=1

# A FIXED `now`, so every age in every leg is arithmetic rather than a race. A fixture whose verdicts
# depend on wall-clock time is a fixture that reds on a slow runner.
NOW="2026-07-28T12:00:00Z"

WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

PASSES=0
FAILURES=0

ok()  { PASSES=$((PASSES + 1)); printf '  ok   %s\n' "$1"; }
bad() { FAILURES=$((FAILURES + 1)); printf '  FAIL %s\n' "$1"; [ $# -gt 1 ] && printf '       %s\n' "$2"; return 0; }

# expect <label> <expected-exit> <needle-or-empty> <corpus-file> [extra gate args...]
expect() {
  local label="$1" want="$2" needle="$3" corpus="$4"; shift 4
  local out rc
  set +e
  out="$("$PY" "$GATE" --corpus "$corpus" --now "$NOW" "$@" 2>&1)"
  rc=$?
  set -e
  if [ "$rc" -ne "$want" ]; then
    bad "$label (wanted exit $want, got $rc)" "$(printf '%s' "$out" | head -6 | tr '\n' '|')"
    return 0
  fi
  if [ -n "$needle" ] && ! printf '%s' "$out" | grep -qF -- "$needle"; then
    bad "$label (exit $want, but the output never said '$needle')" "$(printf '%s' "$out" | head -6 | tr '\n' '|')"
    return 0
  fi
  ok "$label"
}

# ------------------------------------------------------------------------------------------------
# Corpus builder. One place, so a leg differs from its neighbour ONLY in the field under test — a
# hand-written corpus per leg is how two legs end up accidentally testing the same thing.
# ------------------------------------------------------------------------------------------------
corpus() { # corpus <out-file> <python-expression-over-`d`>
  "$PY" - "$1" "$2" <<'PY'
import json, sys
out, expr = sys.argv[1:3]

def wf(name, *, total=50, findings=3, runs=None, **extra):
    row = {"name": name, "path": f".github/workflows/{name}.yml", "state": "active",
           "totalRuns": total, "evaluatedRuns": total, "redRunCount": findings,
           "redRuns": [{"runId": 1000 + i, "conclusion": "failure",
                        "createdAt": "2026-07-28T10:00:00Z", "headSha": f"f{i:06d}",
                        "evidence": "finding", "detail": "fixture finding marker"}
                       for i in range(findings)],
           "defaultBranchRuns": runs if runs is not None else [
               {"conclusion": "success", "createdAt": "2026-07-28T11:00:00Z", "headSha": "aaaaaaa"}]}
    row.update(extra)
    return row

def set_red(row, kinds):
    row["redRunCount"] = len(kinds)
    row["redRuns"] = [{"runId": 2000 + i, "conclusion": "failure",
                       "createdAt": "2026-07-28T10:00:00Z", "headSha": f"r{i:06d}",
                       "evidence": kind, "detail": f"fixture {kind}"}
                      for i, kind in enumerate(kinds)]

def red(hours, sha="bbbbbbb"):
    import datetime
    t = datetime.datetime(2026, 7, 28, 12, 0, tzinfo=datetime.timezone.utc) - datetime.timedelta(hours=hours)
    return {"conclusion": "failure", "createdAt": t.strftime("%Y-%m-%dT%H:%M:%SZ"), "headSha": sha}

def green(hours, sha="ccccccc"):
    import datetime
    t = datetime.datetime(2026, 7, 28, 12, 0, tzinfo=datetime.timezone.utc) - datetime.timedelta(hours=hours)
    return {"conclusion": "success", "createdAt": t.strftime("%Y-%m-%dT%H:%M:%SZ"), "headSha": sha}

def other(hours, conclusion, sha="ddddddd"):
    import datetime
    t = datetime.datetime(2026, 7, 28, 12, 0, tzinfo=datetime.timezone.utc) - datetime.timedelta(hours=hours)
    return {"conclusion": conclusion, "createdAt": t.strftime("%Y-%m-%dT%H:%M:%SZ"), "headSha": sha}

# The BASELINE is deliberately all-EXERCISED: every leg below is the baseline plus exactly one planted
# defect, so a leg that reds proves the defect reds — not that the scaffolding does.
d = {"schema": 3, "fetchedAt": "2026-07-28T12:00:00Z", "runWindow": 30,
     "repos": [{"repo": "FS-GG/fixture", "defaultBranch": "main",
                "workflows": [wf("alpha"), wf("beta")]}]}
W = d["repos"][0]["workflows"]
exec(expr, {"d": d, "W": W, "wf": wf, "set_red": set_red,
            "red": red, "green": green, "other": other})
json.dump(d, open(out, "w", encoding="utf-8"), indent=2)
PY
}

echo "== PART A: the clean corpus must produce NO finding =="

# #1811 acquisition increment: this is the REAL --fetch path against a replay of a recorded Actions
# API transcript, not a urllib mock and not a second implementation of fetch.
if [ -z "${GFH_MUTANT:-}" ]; then
  bash "$HERE/fetch-run.sh"
fi

# THE MOST IMPORTANT LEG IN THE FILE, and it is the green one. Everything below asserts the gate CAN
# red; this asserts it does not red at random. Without it, `sys.exit(1)` would pass every other leg.
corpus "$WORK/clean.json" 'pass'
expect "an all-exercised corpus is exit 0" 0 "EXERCISED: 2" "$WORK/clean.json"
expect "...and names every verdict class that produced nothing, rather than omitting it" \
  0 "NEVER-FOUND: 0" "$WORK/clean.json"

echo "== PART A: each verdict class reds on its own planted defect =="

corpus "$WORK/neverfound.json" 'set_red(W[0], [])'
expect "a gate with 50 runs and 0 reds is NEVER-FOUND (finding)" 1 "NOT ONE red" "$WORK/neverfound.json"

corpus "$WORK/neverran.json" \
  'W[0].update(totalRuns=0, evaluatedRuns=0, defaultBranchRuns=[], triggers=["push", "pull_request"]); set_red(W[0], [])'
expect "a gate with a self-starting trigger and no runs is NEVER-RAN (finding)" 1 "has never executed" "$WORK/neverran.json"

# THE FALSE-ACCUSATION LEG (#238). A `workflow_call`-only workflow has no runs of its own BY
# CONSTRUCTION — it executes inside its callers. The first version of this gate called all seven of
# this repo's reusables "never executed", which is loud, confident and wrong.
corpus "$WORK/reusable.json" \
  'W[0].update(totalRuns=0, evaluatedRuns=0, defaultBranchRuns=[], triggers=["workflow_call"]); set_red(W[0], [])'
expect "a workflow_call-only workflow is NOT accused of never running" 3 "REUSABLE-ELSEWHERE: 1" "$WORK/reusable.json"
expect "...and it is UNMEASURED, not clean — exit 3, never 0" 3 "false accusation" "$WORK/reusable.json"

corpus "$WORK/mixedtrig.json" \
  'W[0].update(totalRuns=0, evaluatedRuns=0, defaultBranchRuns=[], triggers=["workflow_call", "schedule"]); set_red(W[0], [])'
expect "a reusable that ALSO has a self-starting trigger and never ran IS a finding" \
  1 "schedule" "$WORK/mixedtrig.json"

# AN UNRECOGNISED TRIGGER MUST OVER-REPORT, NOT EXCUSE. If GitHub adds an event this set has never
# heard of, a dead workflow must stay visible rather than be quietly filed as "reusable".
corpus "$WORK/unknowntrig.json" \
  'W[0].update(totalRuns=0, evaluatedRuns=0, defaultBranchRuns=[], triggers=["some_future_event"]); set_red(W[0], [])'
expect "an unknown trigger is treated as self-starting (fail loud, not silent)" 1 "NEVER-RAN: 1" "$WORK/unknowntrig.json"

corpus "$WORK/notriggers.json" 'W[0].update(totalRuns=0, evaluatedRuns=0, defaultBranchRuns=[]); set_red(W[0], [])'
expect "zero runs with NO trigger data is an unanswered question, not a finding" \
  2 "cannot be distinguished" "$WORK/notriggers.json"

corpus "$WORK/standingred.json" 'W[0]["defaultBranchRuns"] = [red(1), red(20), red(40), green(60)]'
expect "a 40h unbroken default-branch red is STANDING-RED (finding)" 1 "STANDING-RED: 1" "$WORK/standingred.json"
expect "...and the finding states the age" 1 "40.0h" "$WORK/standingred.json"

corpus "$WORK/lowsample.json" 'W[0].update(totalRuns=3, evaluatedRuns=3); set_red(W[0], [])'
expect "3 runs and 0 reds is LOW-SAMPLE — a NO VERDICT (3), never green" 3 "this is unmeasured" "$WORK/lowsample.json"

corpus "$WORK/skipped-sample.json" 'W[0].update(totalRuns=725, evaluatedRuns=32); set_red(W[0], [])'
expect "725 retained but 32 evaluated runs are the sample, not the skips" 1 "32 evaluated run(s) out of 725 retained" "$WORK/skipped-sample.json"

corpus "$WORK/skipped-low.json" 'W[0].update(totalRuns=725, evaluatedRuns=3); set_red(W[0], [])'
expect "skip-dominated history below MIN_RUNS is LOW-SAMPLE, never NEVER-FOUND" 3 "3 evaluated run(s) out of 725 retained" "$WORK/skipped-low.json"

corpus "$WORK/unread.json" 'W[0]["unread"] = "HTTP 403 rate limited"'
expect "an unreadable workflow is UNREAD — exit 2, never green (#266)" 2 "HTTP 403" "$WORK/unread.json"

corpus "$WORK/fallover.json" 'set_red(W[0], ["fallover", "fallover", "fallover"])'
expect "a crash-only red history is FALLEN-OVER, never EXERCISED" \
  1 "FALLEN-OVER: 1" "$WORK/fallover.json"

corpus "$WORK/evidence-unread.json" 'set_red(W[0], ["unread"])'
expect "an unread red run is preserved per-run and makes the workflow UNREAD" \
  2 "neither EXERCISED nor classified as fallover-only" "$WORK/evidence-unread.json"
expect "...and the default report names that unread run rather than summing it away" \
  2 "run 2000 [unread]" "$WORK/evidence-unread.json"

corpus "$WORK/evidence-expired.json" 'set_red(W[0], ["expired"])'
expect "a retained run whose annotations expired gets its own named verdict" \
  3 "EVIDENCE-EXPIRED: 1" "$WORK/evidence-expired.json"

corpus "$WORK/evidence-ambiguous.json" 'set_red(W[0], ["ambiguous"])'
expect "readable red prose without either marker is not guessed into a flattering bucket" \
  3 "EVIDENCE-AMBIGUOUS: 1" "$WORK/evidence-ambiguous.json"

corpus "$WORK/mixed-evidence.json" 'set_red(W[0], ["finding", "fallover", "unread"])'
expect "one confirmed finding establishes EXERCISED while other per-run states remain recorded" \
  0 "EXERCISED: 2" "$WORK/mixed-evidence.json"
expect "...and an unread sibling run remains visible even beside a confirmed finding" \
  0 "run 2002 [unread]" "$WORK/mixed-evidence.json"

corpus "$WORK/unreadrepo.json" 'd["repos"][0] = {"repo": "FS-GG/dark", "unread": "HTTP 404"}'
expect "an unreadable REPO is one UNREAD row, not zero rows" 2 "whole repo" "$WORK/unreadrepo.json"

echo "== PART A: the boundaries of each rule =="

corpus "$WORK/freshred.json" 'W[0]["defaultBranchRuns"] = [red(10), green(30)]'
expect "a 10h red is under the 24h threshold — not yet wallpaper" 0 "EXERCISED: 2" "$WORK/freshred.json"

corpus "$WORK/edgered.json" 'W[0]["defaultBranchRuns"] = [red(24), green(30)]'
expect "a red at exactly 24h IS standing (the threshold is inclusive)" 1 "STANDING-RED: 1" "$WORK/edgered.json"

# NON-VERDICT RUNS MUST NOT LAUNDER A RED. A cancelled run is not a green, and treating it as one is
# how a month of red gets reported as "went green on Tuesday".
corpus "$WORK/cancelled.json" \
  'W[0]["defaultBranchRuns"] = [red(1), other(2, "cancelled"), red(40), green(60)]'
expect "a cancelled run does NOT break a red streak" 1 "STANDING-RED: 1" "$WORK/cancelled.json"

corpus "$WORK/inprogress.json" \
  'W[0]["defaultBranchRuns"] = [other(0, None), red(1), red(40), green(60)]'
expect "an in-progress run (null conclusion) does NOT break a red streak" 1 "STANDING-RED: 1" "$WORK/inprogress.json"

# ...AND A REAL GREEN MUST. The inverse of the leg above, and it is what stops the rule from being
# "any red anywhere in the window is a standing red", which would fire on everything.
corpus "$WORK/greenbreaks.json" 'W[0]["defaultBranchRuns"] = [green(1), red(40), red(50)]'
expect "a green DOES break the streak — a fixed gate is not standing red" 0 "EXERCISED: 2" "$WORK/greenbreaks.json"

corpus "$WORK/truncated.json" 'W[0]["defaultBranchRuns"] = [red(1), red(40)]'
expect "a streak filling the whole window reports a LOWER bound, not an exact age" \
  1 "LOWER bound" "$WORK/truncated.json"

echo "== PART A: precedence — an admission can never be summed away by greens =="

corpus "$WORK/prec-unread.json" 'W.append(wf("gamma", unread="HTTP 500"))'
expect "one UNREAD among greens is exit 2, NOT 0" 2 "UNREAD: 1" "$WORK/prec-unread.json"

corpus "$WORK/prec-low.json" 'W.append(wf("gamma", total=2, findings=0))'
expect "one LOW-SAMPLE among greens is exit 3, NOT 0" 3 "LOW-SAMPLE: 1" "$WORK/prec-low.json"

corpus "$WORK/prec-both.json" 'W.append(wf("gamma", unread="HTTP 500")); set_red(W[0], [])'
expect "a real finding outranks an admission (exit 1), and the admission is still printed" \
  1 "UNREAD: 1" "$WORK/prec-both.json"

echo "== PART A: an empty or incoherent subject is a NO VERDICT, never a pass (#266, #1784) =="

corpus "$WORK/norepos.json" 'd["repos"] = []'
expect "a corpus with no repos is a NO VERDICT" 3 "nothing to audit" "$WORK/norepos.json"

corpus "$WORK/noworkflows.json" 'd["repos"][0]["workflows"] = []'
expect "a repo with zero workflows and no read error is a NO VERDICT" 3 "are not the same fact" "$WORK/noworkflows.json"

corpus "$WORK/badschema.json" 'd["schema"] = 99'
expect "an unknown corpus schema is a NO VERDICT" 3 "half-understands" "$WORK/badschema.json"

corpus "$WORK/incoherent.json" 'W[0].update(totalRuns=3, evaluatedRuns=3); set_red(W[0], ["finding"] * 9)'
expect "redRunCount > totalRuns is an incoherent corpus, not a busy gate" 3 "incoherent corpus" "$WORK/incoherent.json"

corpus "$WORK/nocounts.json" 'W[0].pop("totalRuns")'
expect "a row claiming to be read but carrying no counts degrades to UNREAD, not to green" \
  2 "no usable run counts" "$WORK/nocounts.json"

corpus "$WORK/badtime.json" 'W[0]["defaultBranchRuns"] = [{"conclusion": "failure", "createdAt": "not-a-time", "headSha": "e"}]'
expect "an unparsable timestamp is a NO VERDICT, not an age of zero" 3 "cannot parse timestamp" "$WORK/badtime.json"

printf '' > "$WORK/empty.json"
expect "an empty corpus FILE is a NO VERDICT" 3 "empty measurement" "$WORK/empty.json"

echo "== PART A: --json carries the same verdict as the exit code =="

corpus "$WORK/json.json" 'set_red(W[0], [])'
expect "--json reports the same finding" 1 '"verdict": "NEVER-FOUND"' "$WORK/json.json" --json
expect "--json carries the exit code it returned" 1 '"exit": 1' "$WORK/json.json" --json
expect "--markdown renders the same finding" 1 "NEVER-FOUND — 1" "$WORK/json.json" --markdown

corpus "$WORK/minruns.json" 'W[0].update(totalRuns=3, evaluatedRuns=3); set_red(W[0], [])'
expect "--min-runs is honoured: a lower floor turns LOW-SAMPLE into a real finding" \
  1 "NEVER-FOUND: 1" "$WORK/minruns.json" --min-runs 2
expect "--min-runs 0 is refused — a floor of nothing measures nothing" \
  3 "must be >= 1" "$WORK/minruns.json" --min-runs 0

# ------------------------------------------------------------------------------------------------
# PART B — SOURCE MUTATION. Is part A load-bearing, or is it 30 assertions that were true anyway?
# ------------------------------------------------------------------------------------------------
if [ -z "${GFH_MUTANT:-}" ]; then
  echo "== PART B: part A must FAIL against a gate with each rule removed =="

  # THE MUTANT MUST BE ABLE TO IMPORT WHAT THE REAL GATE IMPORTS. The gate does
  # `sys.path.insert(0, dirname(__file__))` so it can `from lib.gate import …`; a mutant copied to a
  # temp directory inserts THAT directory instead and the import fails. Every mutant then dies at load
  # with exit 1 — indistinguishable, to a naive harness, from a mutant part A caught.
  #
  # THAT IS NOT A HYPOTHETICAL. It is what this file did on its first green run: all eleven mutants
  # below reported "caught" and NOT ONE of them had executed a single line of classification code. An
  # audit of the audit fabric whose own mutation proof could not fail would have been the eleventh
  # entry on the list this item exists to produce. It was found by the control leg below, which is why
  # the control leg is not optional.
  export PYTHONPATH="$ROOT/scripts${PYTHONPATH:+:$PYTHONPATH}"

  # apply <name> <find-literal> <replace-literal> — replaces EXACTLY ONE occurrence or dies.
  #
  # THE COUNT IS THE POINT (#1784). A mutation whose anchor no longer matches produces a mutant
  # identical to the original, which then sails through part A — and a harness that does not count
  # occurrences reports that as "the fixture caught nothing to catch". It cannot tell *didn't fire*
  # from *didn't run*. This one refuses to run the leg at all unless it changed exactly one thing.
  # NOTE THE ABSENCE OF A TRAILING `printf` HERE, AND IT IS THE WHOLE LESSON. The first draft of this
  # function ended with `printf '%s' "$dest"` to return the path — which made the FUNCTION's exit code
  # that of the `printf`, i.e. always 0, so `if ! mutant="$(apply …)"` never fired and a rotted anchor
  # produced a missing file that the mutant run then failed to execute... and a failed run reads as a
  # caught mutant. That is #1784 reproduced inside the harness written to avoid it, and it was found by
  # deliberately injecting an anchor that does not exist. The destination is now computed by the caller
  # from `$name`, so this function does nothing but succeed or fail.
  apply() {
    local find="$2" repl="$3" dest="$WORK/mutant-$1.py"
    "$PY" - "$GATE" "$dest" "$find" "$repl" <<'PY'
import sys
src, dest, find, repl = sys.argv[1:5]
text = open(src, encoding="utf-8").read()
n = text.count(find)
if n != 1:
    sys.stderr.write(f"MUTATION ANCHOR MATCHED {n} TIMES, WANTED EXACTLY 1: {find!r}\n")
    sys.exit(2)
open(dest, "w", encoding="utf-8").write(text.replace(find, repl, 1))
PY
  }

  # survive <name> <find> <replace> <why> — the mutant must NOT survive part A.
  #
  # THREE OUTCOMES, NOT TWO, AND THAT IS THE POINT. A mutant run that exits non-zero is only a CATCH if
  # part A actually ran and actually reported a failed leg. A run that dies before part A — a missing
  # mutant file, a syntax error, a harness typo — also exits non-zero, and counting that as a catch is
  # precisely #1784's "reported '1 leg fired' when the anchor had failed to match": it cannot tell
  # *didn't fire* from *didn't run*. So the mutant's output is inspected for the summary line part A
  # prints on its way out, and for at least one `FAIL`. Anything else is a HARNESS error and is
  # reported as one.
  survive() {
    local name="$1" find="$2" repl="$3" why="$4" out rc
    if ! apply "$name" "$find" "$repl"; then
      bad "mutation '$name' did not apply — the anchor has rotted. That is NOT a caught mutant."
      return 0
    fi
    set +e
    out="$(GATE_UNDER_TEST="$WORK/mutant-$name.py" GFH_MUTANT=1 bash "$HERE/run.sh" 2>&1)"
    rc=$?
    set -e
    if ! printf '%s' "$out" | grep -qE '^[0-9]+ passed, [0-9]+ failed$'; then
      bad "mutant '$name' never completed part A — this is a HARNESS failure, not a catch" \
          "$(printf '%s' "$out" | tail -4 | tr '\n' '|')"
      return 0
    fi
    if [ "$rc" -eq 0 ] || ! printf '%s' "$out" | grep -q '  FAIL '; then
      bad "MUTANT SURVIVED: $name — $why" "part A passed a gate with that rule removed, so part A does not test it"
      return 0
    fi
    ok "mutant '$name' is caught ($why)"
  }

  # THE UNMUTATED CONTROL — the leg that makes every leg below mean something.
  #
  # A mutation harness reports "caught" when part A fails against the mutant. That inference is only
  # valid if part A PASSES against an unmutated copy carried through the identical machinery: same
  # copy step, same path, same interpreter, same environment. If it does not, then "part A failed" is
  # explained by the machinery rather than by the mutation, and every catch below is worthless — which
  # is exactly the state this file shipped in until this leg was written.
  #
  # This is the negative control inverted, and it is the generalisation of #1784: that harness could
  # not tell *didn't fire* from *didn't run*, and the only way to tell is to prove the apparatus can
  # produce a PASS before trusting it to produce a FAIL.
  control_out=""; control_rc=0
  if ! apply "control" 'NAME = "check-gate-finding-history"' 'NAME = "check-gate-finding-history"  # control'; then
    bad "the unmutated CONTROL could not even be written — part B is inoperative"
  else
    set +e
    control_out="$(GATE_UNDER_TEST="$WORK/mutant-control.py" GFH_MUTANT=1 bash "$HERE/run.sh" 2>&1)"
    control_rc=$?
    set -e
    if [ "$control_rc" -ne 0 ]; then
      bad "THE UNMUTATED CONTROL FAILED PART A — every 'caught' below is an artefact of the harness, not a catch" \
          "$(printf '%s' "$control_out" | tail -3 | tr '\n' '|')"
    else
      ok "the unmutated control passes part A — a FAIL below is attributable to the mutation"
    fi
  fi

  survive "no-findings" \
    'FINDING_VERDICTS = frozenset({"STANDING-RED", "FALLEN-OVER", "NEVER-FOUND", "NEVER-RAN"})' \
    'FINDING_VERDICTS = frozenset()' \
    "a gate that classifies nothing as a finding must fail part A"

  survive "fallover-is-exercised" \
    '            verdict="FALLEN-OVER",' \
    '            verdict="EXERCISED",' \
    "a crash-only gate rounded into EXERCISED — the upper-bound defect from #1812"

  survive "unread-is-green" \
    'if any(r["verdict"] == "UNREAD" for r in rows):
        return ExitCode.NO_VERDICT_RETRYABLE' \
    'if False:
        return ExitCode.NO_VERDICT_RETRYABLE' \
    "collapsing UNREAD into green is the exact #266 fail-open"

  survive "unmeasured-is-green" \
    'if any(r["verdict"] in UNKNOWN_VERDICTS for r in rows):
        return ExitCode.NO_VERDICT_PERMANENT' \
    'if False:
        return ExitCode.NO_VERDICT_PERMANENT' \
    "an unmeasured gate reported as measured"

  survive "no-red-threshold" \
    'if age_h >= red_hours and evidence_counts["finding"] > 0:' \
    'if age_h >= 1e18 and evidence_counts["finding"] > 0:' \
    "a standing red that can never be recognised"

  survive "every-red-is-standing" \
    'if age_h >= red_hours and evidence_counts["finding"] > 0:' \
    'if age_h >= 0 and evidence_counts["finding"] > 0:' \
    "every fresh red misreported as wallpaper — the false-accusation direction (#238)"

  survive "cancelled-breaks-streak" \
    '        # else: a non-verdict run — skipped entirely, and NOT allowed to end the streak.' \
    '        else:
            break' \
    "a cancelled run laundering a standing red into a fixed one"

  survive "green-does-not-break-streak" \
    '        if conclusion in PASS_CONCLUSIONS:
            ended_on_a_pass = True
            break' \
    '        if conclusion in PASS_CONCLUSIONS:
            ended_on_a_pass = True' \
    "a repaired gate still reported as standing red"

  survive "empty-corpus-passes" \
    'if not isinstance(repos, list) or not repos:' \
    'if not isinstance(repos, list):' \
    "#1784's shape exactly — 'ok: all 0 …' at exit 0"

  survive "min-runs-ignored" \
    'if evaluated < min_runs:' \
    'if evaluated < 0:' \
    "a 1-run gate reported as proven never to fire"

  survive "empty-workflow-list-passes" \
    'raise GateError(
                f"{repo}: the corpus records zero workflows and no read error.' \
    'pass  # noqa
        if False:
            raise GateError(
                f"{repo}: the corpus records zero workflows and no read error.' \
    "a repo whose workflow list failed to load reported as a repo with no workflows"

  survive "incoherent-corpus-accepted" \
    'if red_count > evaluated:' \
    'if red_count > 1e18:' \
    "a corpus that cannot be true, classified anyway"

  survive "reusable-is-never-ran" \
    '        if not self_starting:' \
    '        if False:' \
    "seven reusable workflows accused of never running — the bug this gate actually shipped with"

  survive "everything-is-reusable" \
    'if t not in NON_SELF_STARTING_TRIGGERS)' \
    'if t in NON_SELF_STARTING_TRIGGERS)' \
    "a genuinely dead workflow excused as 'reusable' — the silent direction of the same rule"

  survive "no-triggers-is-never-ran" \
    '        if trig is None:' \
    '        if False:' \
    "deciding the never-ran/reusable question by coin toss when the data is absent"
fi

echo
echo "$PASSES passed, $FAILURES failed"
[ "$FAILURES" -eq 0 ]
