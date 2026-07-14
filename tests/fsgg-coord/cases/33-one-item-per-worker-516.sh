#!/usr/bin/env bash
# case: one item per worker 516
# tier: full
# covers: claim widen batch
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="33-one-item-per-worker-516"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- #516: at most ONE item per worker. ---------------------------------------------------------
# The CAS is keyed on the ITEM, so it guarantees at most one worker per item. NOTHING guaranteed the
# converse — and the cost model assumes it. This is #419/ADR-0027 turned inside out: that family is N
# workers colliding on ONE id; this is one id holding N items, and "give every worker its own id" does
# nothing for it. A claim RESERVES A TOUCH-SET, so the second, unattended claim is a live lock on files
# nobody is editing — and in this repo `scripts/fsgg-coord` is exactly the contended path (#428).
#
# The worker who found it found it BY DOING IT: two `take`s, both succeeded, neither said a word.
seed_issue 870 "First item"  'src/A870/**'
seed_issue 871 "Second item" 'src/B871/**'
jq -n --arg ts "$fresh_ts" '[{id:870, body:"<!-- fsgg:claim worker=godwit-b49 lease=120 -->\nheld",
  user:{login:"bot"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-870.json"
c516_rc=0
c516="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker godwit-b49 claim 'FS.GG.SDD#871' 2>&1)" || c516_rc=$?
assert_eq "#516: a worker who already holds an item cannot silently claim a second" "1" "$c516_rc"
assert_contains "#516: ...and the refusal NAMES the item they hold" "FS.GG.SDD#870" "$c516"
assert_contains "#516: ...and says why it is not merely untidy (the touch-set stays reserved)" \
  "reserves a touch-set" "$c516"
assert_eq "#516: ...and the second item is NOT claimed" "" "$(workers_on 871)"
# --force is the deliberate override. A rule with no escape hatch gets worked around, not obeyed.
PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker godwit-b49 claim 'FS.GG.SDD#871' --force >/dev/null 2>&1 || true
assert_eq "#516: ...but --force still holds two, deliberately" "godwit-b49" "$(workers_on 871)"
# A DIFFERENT worker is of course unaffected — the rule is one item per WORKER, not one per repo.
: >"$STORE/comments-871.json"; echo '[]' >"$STORE/comments-871.json"
PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  bash "$COORD" --worker stoat-c71 claim 'FS.GG.SDD#871' >/dev/null 2>&1 || true
assert_eq "#516: a DIFFERENT worker claims freely — the rule is one item per worker, not per repo" \
  "stoat-c71" "$(workers_on 871)"

# ================================================================================================
# FS-GG/.github#273: an unmatchable touch-set token must never read as DISJOINT.
#
# The docs promised "globs"; the matcher implements exact paths + subtree containment. A token that
# keeps a wildcard after normalization (`**/x`, `src/*/x`) therefore matches NO file — and a token
# that matches nothing CONFLICTS WITH NOTHING. So the failure was OPEN: `overlap` printed DISJOINT,
# `batch` handed two workers items whose real files overlapped completely, and `widen` reported the
# cleanest possible answer for the worst possible reason. Every assertion below is one of the doors
# that fail-open walked through.
# ================================================================================================
echo "--- .github#273: an unmatchable touch-set is refused, never cleared ---"

seed_issue 300 "Leading globstar"  '**/packages.lock.json'
seed_issue 301 "Real lockfiles"    'src/Engine/packages.lock.json'
seed_issue 302 "Interior wildcard" 'src/*/lock.json'
seed_issue 303 "Honest scene"      'src/Scene/**'
seed_issue 304 "Honest audio"      'src/Audio/**'

# (1) THE BUG. #300 declares every lockfile via `**/`; #301 names one of those very files. Under the
#     old matcher `**/packages.lock.json` was a directory literally named `**`, so these two read as
#     DISJOINT and both workers were told to go ahead. It must now refuse instead — and, above all,
#     it must never say DISJOINT.
ov300="$(pw overlap 'FS.GG.SDD#300' 'FS.GG.SDD#301' 2>&1 || true)"
assert_contains "overlap: an unmatchable leading '**/' is named, not cleared" \
  "unmatchable touch-set token(s): **/packages.lock.json" "$ov300"
assert_contains "overlap: ...and the supported grammar is quoted" "There is no glob matcher" "$ov300"
case "$ov300" in *"DISJOINT —"*) bad "overlap: #300 vs #301 is NEVER reported DISJOINT" "the #273 fail-open: $ov300" ;;
                 *) ok "overlap: #300 vs #301 is NEVER reported DISJOINT" ;; esac
rc300=0; pw overlap 'FS.GG.SDD#300' 'FS.GG.SDD#301' >/dev/null 2>&1 || rc300=$?
assert_eq "overlap: an unmatchable token exits 2 (undeclared), not 0" "2" "$rc300"

# An interior wildcard is the same defect wearing a different hat, and it is refused on EITHER side.
ov302="$(pw overlap 'FS.GG.SDD#303' 'FS.GG.SDD#302' 2>&1 || true)"
assert_contains "overlap: an interior '*' is refused from the right-hand side too" \
  "unmatchable touch-set token(s): src/*/lock.json" "$ov302"

# (2) The shapes the matcher DOES honour must keep working — a fail-closed gate that rejects the
#     legal grammar is just a differently-broken scheduler.
ov303="$(pw overlap 'FS.GG.SDD#303' 'FS.GG.SDD#304' 2>&1 || true)"
assert_contains "overlap: a trailing '/**' pair is still DISJOINT" "DISJOINT —" "$ov303"
assert_contains "overlap: an exact path still conflicts with its own subtree" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#301' 'FS.GG.SDD#301' 2>&1 || true)"

# (3) `claim` refuses the lock on an item that declares nothing the matcher can reserve. Holding it
#     would make our files invisible to every other worker's overlap check.
cl300="$(as smew-f31 claim 'FS.GG.SDD#300' 2>&1 || true)"
assert_contains "claim: refuses an item whose touch-set can never match a file" \
  "can never match a file: **/packages.lock.json" "$cl300"
assert_eq "claim: ...and takes no marker while refusing" "" "$(workers_on 300)"
# An item with NO `Paths:` declares nothing and reserves nothing — it stays claimable, as before.
as teal-e55 claim 'FS.GG.SDD#72' >/dev/null 2>&1 || true
assert_eq "claim: an item with no 'Paths:' at all is still claimable (unchanged)" "teal-e55" "$(workers_on 72)"

# (4) `widen` must reject BEFORE it PATCHes, so a refused widen leaves the old declaration — which is
#     still reserving real files — intact on the issue body.
body303_before="$(jq -r '.body' "$STORE/issue-303.json")"
wd303="$(as brant-g07 widen 'FS.GG.SDD#303' --paths 'src/Scene/**, **/*.lock.json' 2>&1 || true)"
assert_contains "widen: refuses to write an unmatchable touch-set" "can never match a file" "$wd303"
assert_eq "widen: ...and the issue body is untouched (rejected before the PATCH)" \
  "$body303_before" "$(jq -r '.body' "$STORE/issue-303.json")"
case "$wd303" in *"DISJOINT —"*) bad "widen: an unmatchable widen never reports DISJOINT" "cleared: $wd303" ;;
                 *) ok "widen: an unmatchable widen never reports DISJOINT" ;; esac

# (5) The scheduler. A CANDIDATE with an unmatchable token is passed over with its reason (exactly as
#     an undeclared one is) — it must never fall through to `conflicts_between`, which would clear it
#     against every item on the board.
cat >"$FIXTURES/board-pw4.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":300,"title":"Leading globstar","url":"https://github.com/FS-GG/FS.GG.SDD/issues/300","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":301,"title":"Real lockfiles","url":"https://github.com/FS-GG/FS.GG.SDD/issues/301","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4978}}
JSON
pw4() { PATH="$STUB:$PATH" GH_BOARD_SET=pw4 GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
b4_out="$(pw4 batch --repo sdd --json 2>/dev/null)"
b4_err="$(pw4 batch --repo sdd 2>&1 >/dev/null || true)"
assert_eq "batch: schedules the honest item, and only it" '["FS.GG.SDD#301"]' "$(jq -c '.' <<<"$b4_out")"
assert_contains "batch: says WHY it passed over the unmatchable candidate" \
  "#300 — unmatchable 'Paths:' token(s): **/packages.lock.json" "$b4_err"

# (6) …and an IN-FLIGHT claim with an unmatchable token reserves NOTHING, so every candidate would
#     clear it. We cannot see that holder's real surface, so we schedule nothing at all — the same
#     refusal `paths_of` makes when it cannot read a touch-set.
mk_claim 300 840 ghost-666 fresh | jq -s '.' >"$STORE/comments-300.json"
b4h="$(pw4 batch --repo sdd 2>&1 || true)"
assert_contains "batch: REFUSES to schedule against a held item that reserves nothing" \
  "refusing to schedule against an unknown touch-set" "$b4h"
assert_contains "batch: ...and names the held item" "FS.GG.SDD#300" "$b4h"
assert_fails "batch: ...and exits non-zero rather than handing out work" pw4 batch --repo sdd
: >"$STORE/comments-300.json"; echo '[]' >"$STORE/comments-300.json"

# (7) `verify-paths` gets a fourth verdict. Under a broken touch-set every changed file is "outside"
#     it — technically true, and useless. Name the broken token instead of burying it under a file list.
cat >"$FIXTURES/pr-30.json" <<'JSON'
{"head":{"ref":"item/300-leading-globstar"},"number":30}
JSON
cat >"$FIXTURES/pr-files-30.json" <<'JSON'
[{"filename":"src/Engine/packages.lock.json"}]
JSON
vp30="$(pw verify-paths --pr 30 --repo FS-GG/FS.GG.SDD 2>&1 || true)"
assert_contains "verify-paths: an unmatchable touch-set is INVALID, not DRIFT" "FSGG-PATHS INVALID" "$vp30"
assert_contains "verify-paths: ...and names the token that matches nothing" "**/packages.lock.json" "$vp30"
case "$vp30" in *"FSGG-PATHS DRIFT"*) bad "verify-paths: INVALID is not dressed up as DRIFT" "$vp30" ;;
                *) ok "verify-paths: INVALID is not dressed up as DRIFT" ;; esac
assert_fails "verify-paths: an unmatchable touch-set exits non-zero" pw verify-paths --pr 30 --repo FS-GG/FS.GG.SDD
assert_contains "verify-paths --warn: reports INVALID but exits 0 (the advisory CI gate)" "FSGG-PATHS INVALID" \
  "$(pw verify-paths --pr 30 --repo FS-GG/FS.GG.SDD --warn 2>&1)"
pw verify-paths --pr 30 --repo FS-GG/FS.GG.SDD --warn >/dev/null 2>&1 \
  && ok "verify-paths --warn: INVALID exits 0" || bad "verify-paths --warn: INVALID exits 0" "non-zero exit under --warn"

# (8) The guard must not reintroduce the fail-open INSIDE ITSELF. `paths_of` fails closed by calling
#     `die` when the body read fails — but `die` is an `exit`, and an `exit` inside `$( )` kills only
#     the substitution subshell. Had `claim` passed `"$(paths_of …)"` as an ARGUMENT, it would print
#     "refusing to schedule against an unknown touch-set" and then claim the item anyway, exit 0.
seed_issue 305 "Body read explodes" 'src/Fine/**'
rc305=0
PATH="$STUB:$PATH" GH_ISSUES_FROM_STORE=1 GH_FAIL_ISSUE_GET=305 \
  bash "$COORD" --worker smew-f31 claim 'FS.GG.SDD#305' >/dev/null 2>&1 || rc305=$?
assert_eq "claim: a failed touch-set read FAILS CLOSED (the die is not swallowed by \$( ))" "1" "$rc305"
assert_eq "claim: ...and no marker was taken on the item it could not read" "" "$(workers_on 305)"

# (9) `widen` re-checks against in-flight claims. A LEGACY holder whose own tokens are unmatchable
#     reserves nothing and clears everything — so widen must refuse to answer rather than report the
#     cleanest possible DISJOINT. (`overlap --active` and `batch` guard this; widen did not.)
seed_issue 306 "Widener"        'src/Widen/**'
seed_issue 307 "Legacy holder"  '**/packages.lock.json'
mk_claim 307 841 ghost-777 fresh | jq -s '.' >"$STORE/comments-307.json"
wd306="$(as brant-g07 widen 'FS.GG.SDD#306' --paths 'src/Widen/**, src/More/**' 2>&1 || true)"
assert_contains "widen: refuses to re-check against a holder whose touch-set reserves nothing" \
  "in-flight claim(s) declare unmatchable touch-set token(s): FS.GG.SDD#307" "$wd306"
case "$wd306" in *"DISJOINT —"*) bad "widen: never clears against an unmatchable in-flight claim" "cleared: $wd306" ;;
                 *) ok "widen: never clears against an unmatchable in-flight claim" ;; esac
# The widen itself still LANDED — that is how a bad declaration gets fixed, including on a held item.
assert_contains "widen: ...but the re-declaration itself still landed" "Paths: src/Widen/**, src/More/**" \
  "$(jq -r '.body' "$STORE/issue-306.json")"

# Widening is precisely how a bad declaration gets repaired — including on an item you already hold.
# The screen must never leave a worker stuck: `#307` is claimed AND declares the unmatchable token,
# and it must still be able to widen out of it. (This passes for a reason independent of the `$self`
# exclusion — widen PATCHes before it re-checks, so by then #307's own paths are already the new,
# valid ones. The exclusion guards the read-after-write case where the re-read still serves the old
# body; that lag is not reproducible against this stub, so it is defence, not a tested path.)
wd307="$(as ghost-777 widen 'FS.GG.SDD#307' --paths 'src/Engine/packages.lock.json' 2>&1 || true)"
assert_contains "widen: an item held with a bad touch-set can widen out of it (the PATCH lands)" \
  "widened FS.GG.SDD#307 → Paths: src/Engine/packages.lock.json" "$wd307"
assert_contains "widen: ...and the repaired declaration persisted" "Paths: src/Engine/packages.lock.json" \
  "$(jq -r '.body' "$STORE/issue-307.json")"
: >"$STORE/comments-307.json"; echo '[]' >"$STORE/comments-307.json"

# (10) The CI gate must classify INVALID before its `else`, or the verdict lands in `skip` — which
#      DELETES the sticky comment and passes green, burying the finding. Assert the workflow's own
#      branch order, the one thing the fixture can check about it offline.
wf="$HERE/../../.github/workflows/touch-set-drift.yml"
assert_contains "touch-set-drift.yml: INVALID is classified, not absorbed by the else" \
  "verdict=invalid" "$(cat "$wf")"
assert_contains "touch-set-drift.yml: ...and rendered by its own comment branch, never the ✅ one" \
  'elif [ "$VERDICT" = "invalid" ]' "$(cat "$wf")"
inv_line="$(grep -n 'FSGG-PATHS INVALID' "$wf" | head -1 | cut -d: -f1)"
else_line="$(grep -n 'verdict=skip' "$wf" | head -1 | cut -d: -f1)"
if [ "$inv_line" -lt "$else_line" ]; then ok "touch-set-drift.yml: INVALID is tested BEFORE the skip fallback"
else bad "touch-set-drift.yml: INVALID is tested BEFORE the skip fallback" "INVALID at $inv_line, skip at $else_line"; fi

# (11) The IN-FLIGHT-claim guards, on the two commands that consult `active_claims`. These are the
#      `claims_with_unmatchable_paths` (jq) path, NOT the per-candidate `invalid_paths` (grep) path
#      exercised in (5)/(6) — a mutation that neuters the shared jq filter must turn these red, or the
#      two guards standing between a legacy holder and a double-booking are untested.
#      #308 is a legacy holder: claimed, in flight, declaring a token that reserves nothing.
seed_issue 308 "Legacy in-flight" '**/*.lock.json'
mk_claim 308 842 ghost-888 fresh | jq -s '.' >"$STORE/comments-308.json"

ov_act="$(pw overlap 'FS.GG.SDD#303' --active 2>&1 || true)"
assert_contains "overlap --active: refuses against an in-flight claim that reserves nothing" \
  "in-flight claim(s) declare unmatchable touch-set token(s): FS.GG.SDD#308" "$ov_act"
case "$ov_act" in *"DISJOINT —"*) bad "overlap --active: never clears against an unmatchable claim" "cleared: $ov_act" ;;
                  *) ok "overlap --active: never clears against an unmatchable claim" ;; esac
rc_act=0; pw overlap 'FS.GG.SDD#303' --active >/dev/null 2>&1 || rc_act=$?
assert_eq "overlap --active: ...and exits 2, not 0" "2" "$rc_act"

b_act="$(pw batch --repo sdd 2>&1 || true)"
assert_contains "batch: refuses when an IN-FLIGHT claim (not a candidate) reserves nothing" \
  "refusing to schedule against an unknown touch-set" "$b_act"
assert_contains "batch: ...and names that in-flight claim" "FS.GG.SDD#308" "$b_act"
assert_fails "batch: ...and hands out nothing" pw batch --repo sdd
: >"$STORE/comments-308.json"; echo '[]' >"$STORE/comments-308.json"

# ================================================================================================
# FS-GG/.github#277: a quoted `Paths:` line is not a declaration.
#
# #273's token was UNMATCHABLE — it reserved nothing, and once named it could be refused. This one is
# FABRICATED: every token is well-formed, so `invalid_paths` sees nothing wrong, and the item reserves
# the WRONG files with complete confidence. `paths_from_body` grepped the whole body, fences included,
# so an issue that quoted a `Paths:` line — in a repro, in a suggested `widen` — acquired it; and
# because every match was unioned, a real declaration plus a quoted one reserved both.
# ================================================================================================
echo "--- .github#277: a fenced or quoted 'Paths:' line is not a declaration ---"

# The #261 shape: the ONLY `Paths:` line is fenced, quoting another repo. Every token is valid, so the
# #273 guard clears it. It must now declare NOTHING — unschedulable beats mis-scheduled.
seed_issue_raw 310 "Quotes another repo" 'Worker A declared, in FS.GG.Rendering#186:

```
Paths: nuget.config, Directory.Build.local.props
```

The fix here lives in `scripts/fsgg-coord`.'
seed_issue 311 "Real coord work" 'scripts/fsgg-coord'

ov310="$(pw overlap 'FS.GG.SDD#310' 'FS.GG.SDD#311' 2>&1 || true)"
assert_contains "paths: an issue quoting a valid-token 'Paths:' line reserves nothing" \
  "no 'Paths:' touch-set declared" "$ov310"
case "$ov310" in *"DISJOINT —"*) bad "overlap: a fabricated touch-set is NEVER reported DISJOINT" "the #277 fail-open: $ov310" ;;
                 *) ok "overlap: a fabricated touch-set is NEVER reported DISJOINT" ;; esac
rc310=0; pw overlap 'FS.GG.SDD#310' 'FS.GG.SDD#311' >/dev/null 2>&1 || rc310=$?
assert_eq "overlap: a fenced-only declaration exits 2 (undeclared), not 0" "2" "$rc310"

# The union hazard: a real declaration must not be widened by a quoted one. #312 works on Scene only;
# it quotes Audio. It must conflict with Scene and stay DISJOINT from Audio.
seed_issue_raw 312 "Real + quoted" 'Paths: src/Scene/**

Compare with the audio item, which declares:

```
Paths: src/Audio/**
```'
assert_contains "paths: a body with a real and a quoted declaration takes the real one" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#312' 'FS.GG.SDD#70' 2>&1 || true)"
assert_contains "paths: ...and does NOT union in the quoted one" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#312' 'FS.GG.SDD#42' 2>&1 || true)"

# A quote that ESCAPES the strip (bare, at column 0) is where #277's suggested `head -1` and the
# union part ways — and where `head -1` would have reintroduced ADR-0021's own bug. Taking the first
# line reserves the QUOTE and DROPS the real declaration under it: two workers, told DISJOINT, both
# editing src/Scene. The union over-reserves instead, which is loud and costs only parallelism.
seed_issue_raw 313 "Bare quote above the real line" 'Paths: src/Audio/**   (quoting the other issue)

Paths: src/Scene/**'
assert_contains "paths: a bare quote ABOVE the declaration never drops it (no under-reserve)" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#313' 'FS.GG.SDD#70' 2>&1 || true)"
assert_contains "paths: ...and the over-reserved quote is reported as a real OVERLAP, not hidden" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#313' 'FS.GG.SDD#42' 2>&1 || true)"

# The other two block syntaxes markdown gives an author for quoting a line.
seed_issue_raw 314 "Tilde fence" '~~~
Paths: src/Audio/**
~~~

Paths: src/Scene/**'
assert_contains "paths: a '~~~' fence hides a quoted declaration too" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#314' 'FS.GG.SDD#42' 2>&1 || true)"
seed_issue_raw 315 "Indented block" 'Reproduction:

    Paths: src/Audio/**

Paths: src/Scene/**'
assert_contains "paths: a 4-space indented block is a code block, not a declaration" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#315' 'FS.GG.SDD#42' 2>&1 || true)"
# ...but the legal shapes keep working: a fenced block that quotes nothing, and a list-indented line.
seed_issue_raw 316 "Indented <4" '  Paths: src/Scene/**'
assert_contains "paths: a line indented 3 spaces or fewer is still a declaration" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#316' 'FS.GG.SDD#70' 2>&1 || true)"

# `widen` WRITES the line `paths_from_body` READS. If the reader skips fenced lines and the writer
# still patches the first `Paths:` anywhere, widen rewrites the quote inside the fence, the real
# declaration stands, and the tool reports a widen that changed nothing it will ever read.
as brant-g07 widen 'FS.GG.SDD#312' --paths 'src/Scene/**, src/Legacy/**' >/dev/null 2>&1 || true
body312="$(jq -r '.body' "$STORE/issue-312.json")"
assert_contains "widen: patches the REAL declaration, not the quoted one" \
  "Paths: src/Scene/**, src/Legacy/**" "$body312"
assert_contains "widen: ...and leaves the fenced quote untouched" "Paths: src/Audio/**" "$body312"
assert_contains "widen: ...and the reader now sees the widened set" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#312' 'FS.GG.SDD#43' 2>&1 || true)"

# The writer's other half: when the body's ONLY `Paths:` line is fenced, there is no declaration to
# patch — widen must APPEND one. Before the fix it overwrote the quote and the item still declared
# nothing, so a worker who "fixed" their touch-set stayed unschedulable with no idea why.
as brant-g07 widen 'FS.GG.SDD#310' --paths 'scripts/fsgg-coord' >/dev/null 2>&1 || true
body310="$(jq -r '.body' "$STORE/issue-310.json")"
assert_contains "widen: appends a declaration when the only 'Paths:' line is fenced" \
  "Paths: nuget.config, Directory.Build.local.props" "$body310"
assert_contains "widen: ...and the appended line is what the reader picks up" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#310' 'FS.GG.SDD#311' 2>&1 || true)"

# Because the reader UNIONS the surviving declarations, `widen` must replace the first and DROP the
# rest — otherwise a leftover line keeps reserving its old tokens and widen did not widen to what it
# printed. This is also how a body that accumulated a stray bare quotation gets repaired.
seed_issue_raw 320 "Two bare declarations" 'Paths: src/Audio/**

Paths: src/Scene/**'
as brant-g07 widen 'FS.GG.SDD#320' --paths 'src/Legacy/**' >/dev/null 2>&1 || true
body320="$(jq -r '.body' "$STORE/issue-320.json")"
assert_eq "widen: collapses duplicate declarations to exactly one" "1" \
  "$(printf '%s\n' "$body320" | grep -cE '^ {0,3}[Pp]aths:' || true)"
assert_contains "widen: ...and the survivor is the widened set" "Paths: src/Legacy/**" "$body320"
assert_contains "widen: ...so the reader no longer sees the dropped tokens" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#320' 'FS.GG.SDD#42' 2>&1 || true)"
assert_contains "widen: ...and does see the widened one" "OVERLAP" \
  "$(pw overlap 'FS.GG.SDD#320' 'FS.GG.SDD#43' 2>&1 || true)"

# An UNCLOSED fence swallows the rest of the body, so there is no declaration to patch and widen
# APPENDS — straight into the still-open fence, where the reader will never see it. widen would then
# print a confident `widened …` over an item that still declares nothing, on precisely the body a
# worker runs widen to repair. The fence is closed before the declaration is appended.
seed_issue_raw 318 "Unclosed fence" 'Here is the repro:

```
Paths: src/Audio/**'
as brant-g07 widen 'FS.GG.SDD#318' --paths 'src/Scene/**' >/dev/null 2>&1 || true
assert_contains "widen: closes an unterminated fence before appending the declaration" \
  "OVERLAP" "$(pw overlap 'FS.GG.SDD#318' 'FS.GG.SDD#70' 2>&1 || true)"
assert_contains "widen: ...and the appended declaration does not reserve the quoted set" "DISJOINT —" \
  "$(pw overlap 'FS.GG.SDD#318' 'FS.GG.SDD#42' 2>&1 || true)"

# `repl` reaches awk through the environment, not `-v`, which applies escape processing to its value.
# Under `-v` a token holding a backslash was PATCHed as something other than the string
# `die_on_invalid_paths` validated and widen echoed back — `src/a\tb` became a real tab, which the
# reader's `tr` then split into two tokens. The body must store exactly what was announced.
seed_issue_raw 319 "Backslash token" 'Paths: src/Old'
as brant-g07 widen 'FS.GG.SDD#319' --paths 'src/a\tb' >/dev/null 2>&1 || true
body319="$(jq -r '.body' "$STORE/issue-319.json")"
assert_contains "widen: a backslash in a token is stored verbatim (no awk -v escape processing)" \
  'Paths: src/a\tb' "$body319"
assert_eq "widen: ...and the stored line holds no literal tab" "0" \
  "$(printf '%s' "$body319" | grep -cP '\t' || true)"

# The scheduler: a fenced-only body is passed over exactly as an undeclared one is — with its reason.
cat >"$FIXTURES/board-pw5.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":317,"title":"Fenced only","url":"https://github.com/FS-GG/FS.GG.SDD/issues/317","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}},
    {"status":{"name":"Ready"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":311,"title":"Real coord work","url":"https://github.com/FS-GG/FS.GG.SDD/issues/311","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4977}}
JSON
seed_issue_raw 317 "Fenced only" 'Repro:

```
Paths: scripts/fsgg-coord
```'
pw5() { PATH="$STUB:$PATH" GH_BOARD_SET=pw5 GH_ISSUES_FROM_STORE=1 bash "$COORD" "$@"; }
b5_out="$(pw5 batch --repo sdd --json 2>/dev/null)"
b5_err="$(pw5 batch --repo sdd 2>&1 >/dev/null || true)"
assert_eq "batch: schedules the honestly-declared item, and only it" '["FS.GG.SDD#311"]' "$(jq -c '.' <<<"$b5_out")"
assert_contains "batch: says WHY it passed over the fenced-only candidate" \
  "#317 — no 'Paths:' declared (cannot schedule" "$b5_err"
# ...and calls it an OMISSION, not a design decision (#496). A fenced-only declaration is invisible to
# the scheduler, so this item really does declare nothing — and the worker reading this list is the
# person who can fix it. The sentinel is offered by name, so the fix is a copy-paste either way.
assert_contains "batch: names the fenced-only candidate as an OMISSION, and offers the sentinel" \
  "this is an OMISSION" "$b5_err"
# The fail-open in one line: #317's quote names the very file #311 declares. Had the quote been read,
# batch would have seen an OVERLAP it could not schedule — instead it must see no declaration at all.
case "$b5_out" in *317*) bad "batch: never schedules an item on a fabricated touch-set" "$b5_out" ;;
                  *) ok "batch: never schedules an item on a fabricated touch-set" ;; esac


harness_report
