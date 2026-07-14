#!/usr/bin/env bash
# case: issue boundary adversarial
# tier: full
# covers: claim release verify-paths take
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="24-issue-boundary-adversarial"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# --- state the monolith inherited from an earlier section; stated explicitly here ---
cat >"$FIXTURES/pr-7.json" <<'JSON'
{"head":{"ref":"item/70-scene-graph"},"number":7}
JSON
cat >"$FIXTURES/pr-files-7.json" <<'JSON'
[{"filename":"src/Scene/Graph.fs"},{"filename":"tests/Scene/GraphTests.fs"}]
JSON
cat >"$FIXTURES/pr-8.json" <<'JSON'
{"head":{"ref":"item/70-scene-graph"},"number":8}
JSON
cat >"$FIXTURES/pr-files-8.json" <<'JSON'
[{"filename":"src/Scene/Graph.fs"},{"filename":"src/Audio/Mixer.fs"},{"filename":"README.md"}]
JSON
cat >"$FIXTURES/pr-9.json" <<'JSON'
{"head":{"ref":"chore/no-linked-issue"},"number":9}
JSON
cat >"$FIXTURES/pr-files-9.json" <<'JSON'
[{"filename":"README.md"}]
JSON
cat >"$FIXTURES/pr-10.json" <<'JSON'
{"head":{"ref":"item/72-no-touch-set-declared"},"number":10}
JSON
cat >"$FIXTURES/pr-files-10.json" <<'JSON'
[{"filename":"src/Whatever.fs"}]
JSON

# ---- the ISSUE side of the boundary: the harness must be able to tell the repos apart (#494) -------
# Everything above asserts the repo of a PR read, because #479 had to teach the stub to log it. Every
# ISSUE read stayed blind: the store was keyed by number alone, so `paths_of` reading FS.GG.SDD#70 and
# FS.GG.Rendering#70 got the SAME fixture back, and a cross-repo defect on the issue side could not be
# written down as a failing test at all. Not merely untested — UNTESTABLE, which is how a class of bug
# survives a suite that looks thorough (#266's thesis, one level down).
#
# So the store is now repo-QUALIFIED, and these two are different issues that happen to share a number.
# Their touch-sets DIVERGE deliberately: PR 7 touches src/Scene/**, which SDD#494 covers and
# Rendering#494 does not. One PR, one number, two repos — and therefore two different honest verdicts.
# A stub that cannot tell the repos apart cannot produce both, which is precisely the point.
# Seeded CLOSED: these two exist only to be READ as touch-sets. An open issue joins every subsequent
# open-issue candidate scan in its repo, and a fixture that quietly changes what OTHER tests see is
# the wrong way to pay for this one.
seed_issue_in FS-GG/FS.GG.SDD       494 "Scene work (SDD)"        "src/Scene/**, tests/Scene/**" closed
seed_issue_in FS-GG/FS.GG.Rendering 494 "Audio work (Rendering)"  "src/Audio/**"                 closed

assert_contains "#494: SDD's #494 declares the PR's files — OK" "FSGG-PATHS OK" \
  "$(pw verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS-GG/FS.GG.SDD#494' 2>&1 || true)"
# The payload betrays the repo now: same PR, same number, other repo, opposite verdict. Under the old
# number-keyed store BOTH of these read SDD's body and BOTH printed OK — the fixture passing green on
# a subject it never looked at, exactly the way #479 did.
assert_contains "#494: ...and Rendering's #494 — same number, other repo — does NOT: DRIFT" \
  "FSGG-PATHS DRIFT" "$(pw verify-paths --pr 7 --repo FS-GG/FS.GG.Rendering --issue 'FS-GG/FS.GG.Rendering#494' 2>&1 || true)"

# Acceptance 1: a test can assert WHICH REPO an issue read was addressed to — from the request.
: >"$GH_LOG"
pw verify-paths --pr 7 --repo FS-GG/FS.GG.Rendering --issue 'FS-GG/FS.GG.Rendering#494' >/dev/null 2>&1 || true
assert_contains "#494: an issue read names the repo it was addressed to" \
  "issue-get FS-GG/FS.GG.Rendering 494" "$(cat "$GH_LOG")"
assert_eq "#494: ...and the same-numbered issue next door is never read" \
  "0" "$(grep -c 'issue-get FS-GG/FS.GG.SDD 494' "$GH_LOG" || true)"

# Requirement 2: a lookup that misses must never fall back to a same-numbered issue in another repo.
# #74 exists ONLY in SDD (touch-set docs/adr/**). Asked for as Rendering#74, the honest answer is 404 —
# not SDD's body. A stub that served it would let a repo-confused `paths_of` come back CONFIDENT and
# WRONG, with a touch-set it had no business seeing.
raw74="$(env PATH="$STUB:$PATH" gh api repos/FS-GG/FS.GG.Rendering/issues/74 2>&1 || true)"
case "$raw74" in
  *"docs/adr"*) bad "#494: a wrong-repo read is never served the same-numbered issue next door" \
                    "served SDD's #74 body to a Rendering read: $raw74" ;;
  *"404"*)      ok  "#494: a wrong-repo read is never served the same-numbered issue next door" ;;
  *)            bad "#494: a wrong-repo read is never served the same-numbered issue next door" \
                    "expected a 404, got: $raw74" ;;
esac
# ...and the OTHER miss stays a loud stub bug (exit 4), because a test that forgot to seed its subject
# is not the same failure as a client that asked the wrong repo, and must not be quietly rendered as one.
rawmiss="$(env PATH="$STUB:$PATH" gh api repos/FS-GG/FS.GG.SDD/issues/6553 2>&1 || true)"
assert_contains "#494: an unseeded issue is still a loud STUB bug, not a 404" "no issue fixture" "$rawmiss"

# A failure injection names a SUBJECT, and a subject is a repo AND a number. Now that SDD#494 and
# Rendering#494 both exist, `GH_FAIL_ISSUE_GET=494` can no longer say WHICH one it means — and a stub
# that let one number arm an injection in two repos would be conflating them again, one layer below
# the bug this item is about. The qualified form aims.
inj_sdd="$(env PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET='FS-GG/FS.GG.Rendering#494' \
             gh api repos/FS-GG/FS.GG.SDD/issues/494 --jq '.title' 2>&1 || true)"
inj_rnd="$(env PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET='FS-GG/FS.GG.Rendering#494' \
             gh api repos/FS-GG/FS.GG.Rendering/issues/494 --jq '.title' 2>&1 || true)"
assert_contains "#494: a qualified injection fires on the repo it names" "502" "$inj_rnd"
assert_contains "#494: ...and leaves the same-numbered issue next door alone" "Scene work (SDD)" "$inj_sdd"
# ...and the bare form still means "that number, in whatever repo has it" — the legacy spelling every
# injection above this line uses, and which must keep working.
assert_contains "#494: the bare injection form still fires regardless of repo" "502" \
  "$(env PATH="$STUB:$PATH" GH_FAIL_ISSUE_GET=494 gh api repos/FS-GG/FS.GG.SDD/issues/494 2>&1 || true)"

# Acceptance 2: a deliberately-introduced cross-repo confusion in `paths_of` turns this fixture RED.
# This is the assertion that proves the leg is COVERED rather than merely coverable — a test suite can
# be repo-aware everywhere and still never exercise the confusion. The mutant clamps `paths_of`'s repo
# argument to FS.GG.SDD, i.e. reintroduces #479 on the issue side, and nothing else.
mutant="$WORK/fsgg-coord-repo-blind"
awk '{ print }
     /^paths_of\(\)/ { print "  set -- \"$1\" FS.GG.SDD \"$3\"   # MUTANT (#494): repo-blind paths_of" }' \
  "$COORD" >"$mutant"
assert_eq "#494: the mutant really is repo-blind (it clamps paths_of to one repo)" \
  "1" "$(grep -c 'MUTANT (#494)' "$mutant" || true)"
: >"$GH_LOG"
mut494="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$mutant" \
            verify-paths --pr 7 --repo FS-GG/FS.GG.Rendering --issue 'FS-GG/FS.GG.Rendering#494' 2>&1 || true)"
# ...and it flipped for the RIGHT reason. A mutant that merely errored its way to a different verdict
# would prove nothing; the request log has to show it actually read the OTHER repo's issue.
assert_contains "#494: ...and the mutant is caught reading the WRONG repo's issue" \
  "issue-get FS-GG/FS.GG.SDD 494" "$(cat "$GH_LOG")"
# The honest verdict on Rendering#494 is DRIFT (above). The repo-blind mutant reads SDD's touch-set
# instead and reports OK — confidently green, on a subject it never looked at. THAT is the flip a test
# must be able to see, and could not before this change.
case "$mut494" in
  *"FSGG-PATHS OK"*)
    ok "#494: a repo-blind paths_of flips DRIFT -> OK, so the fixture goes RED" ;;
  *)
    bad "#494: a repo-blind paths_of flips DRIFT -> OK, so the fixture goes RED" \
        "the mutant's verdict did NOT change — the cross-repo leg is still not exercised: $mut494" ;;
esac

# The refusal must not overshoot into a false alarm on refs that AGREE. Repo names are case-insensitive
# on GitHub, and `--repo` takes a registry short-id everywhere else in this tool — both must still pass.
assert_contains "verify-paths: a registry short-id --repo agrees with the issue's repo" "FSGG-PATHS OK" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
       verify-paths --pr 7 --repo sdd --issue 'FS-GG/FS.GG.SDD#70' 2>&1)"
assert_contains "verify-paths: a differently-cased --repo is not a conflict" "FSGG-PATHS OK" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
       verify-paths --pr 7 --repo FS-GG/fs.gg.sdd --issue 'FS-GG/FS.GG.SDD#70' 2>&1)"
assert_contains "verify-paths: a bare-repo --issue (owner defaults) is not a conflict" "FSGG-PATHS OK" \
  "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
       verify-paths --pr 7 --repo FS-GG/FS.GG.SDD --issue 'FS.GG.SDD#70' 2>&1)"

# The boundary from the OTHER side, which the report did not name: GitHub lets a PR close an issue in
# a DIFFERENT repo, so the closing-reference fallback can hand back a cross-repo ref all by itself —
# no `--issue` flag involved, and straight back over the line the check above refuses to cross. PR 11's
# branch is not `item/<n>-…`, so resolution falls through to that query.
cat >"$FIXTURES/pr-11.json" <<'JSON'
{"head":{"ref":"chore/closes-another-repo"},"number":11}
JSON
cat >"$FIXTURES/pr-files-11.json" <<'JSON'
[{"filename":"src/Scene/Graph.fs"}]
JSON
cat >"$FIXTURES/pr-closes-11.json" <<'JSON'
{"data":{"repository":{"pullRequest":{"closingIssuesReferences":{"nodes":[
  {"number":70,"repository":{"nameWithOwner":"FS-GG/FS.GG.Rendering"}}]}}}},
 "rateLimit":{"cost":1,"remaining":4999}}
JSON
xrepo="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
           verify-paths --pr 11 --repo FS-GG/FS.GG.SDD --warn 2>&1 || true)"
no_verdict "verify-paths: a PR closing another repo's issue reaches NO verdict" "$xrepo"
assert_contains "verify-paths: ...and is SKIP under --warn, with the reason" "FSGG-PATHS SKIP" "$xrepo"
assert_contains "verify-paths: ...naming the other repo"                     "FS-GG/FS.GG.Rendering#70" "$xrepo"
assert_fails "verify-paths: a PR closing another repo's issue fails without --warn" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" verify-paths --pr 11 --repo FS-GG/FS.GG.SDD
# ...and the pre-existing SKIP leg still SKIPs: PR 9 closes nothing at all, which is a different thing
# from closing something elsewhere, and the new arm in the stub must not have swallowed it.
assert_contains "verify-paths: a PR that closes NOTHING is still the unlinked SKIP" \
  "closes no issue" "$(PATH="$STUB:$PATH" GH_BOARD_SET=pw bash "$COORD" \
                         verify-paths --pr 9 --repo FS-GG/FS.GG.SDD --warn 2>&1)"


# ================================================================================================
# The lock's hard cases. Each of these is an interleaving in which two workers could end up believing
# they hold one item — the failure the whole protocol exists to prevent.
# ================================================================================================
echo "--- ADR-0027: lock invariants under adversarial interleavings ---"

seed_issue 84 "Stale holder, new claimant" "src/A/**"
seed_issue 85 "Stale holder is me"         "src/B/**"
seed_issue 86 "Expired worker, live holder" "src/C/**"
seed_issue 87 "Expired worker, no holder"   "src/D/**"
seed_issue 88 "Forged marker in a message"  "src/E/**"
seed_issue 89 "Malformed marker"            "src/F/**"
seed_issue 90 "Read fails after post"       "src/G/**"



# (a) A stale marker must be COLLECTED by the next claimant, never merely ignored. An ignored marker
#     is what `heartbeat` later resurrects underneath the new holder — two live markers, one item.
mk_claim 84 810 ghost-111 stale | jq -s '.' >"$STORE/comments-84.json"
: >"$GH_LOG"
c84="$(as heron-b71 claim 'FS.GG.SDD#84' --force 2>&1)"
assert_contains "claim: collects the stale marker it claims over" "collected worker 'ghost-111' expired claim" "$c84"
assert_eq "claim: exactly ONE marker survives (the stale one is gone)" "heron-b71" "$(workers_on 84)"
assert_eq "claim: the collected worker is TOLD, not silently evicted" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=ghost-111"))] | length' "$STORE/comments-84.json")"

# (b) Re-claiming when MY OWN marker went stale must renew a single marker, not mint a second.
# Its OWN worker id: `finch-a3f` holds #42 live for the tests further down, and a worker may hold
# only one item (#516). The scenario is about renewing YOUR OWN stale marker, not about holding two.
mk_claim 85 811 otter-b55 stale | jq -s '.' >"$STORE/comments-85.json"
as otter-b55 claim 'FS.GG.SDD#85' >/dev/null 2>&1
assert_eq "claim: a worker whose own marker went stale ends with ONE marker" "otter-b55" "$(workers_on 85)"
assert_eq "claim: ...and exactly one, not two" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:claim"))] | length' "$STORE/comments-85.json")"

# (c) THE RESURRECTION BUG. A worker whose lease expired must NOT be able to heartbeat its marker back
#     to life once another worker legitimately holds the item. It must be told to stop.
jq -s '.' <(mk_claim 86 812 ghost-222 stale) <(mk_claim 86 813 heron-b71 fresh) >"$STORE/comments-86.json"
: >"$GH_LOG"
hb86="$(as ghost-222 heartbeat 'FS.GG.SDD#86' 2>&1 || true)"
assert_fails "heartbeat: an expired worker cannot resurrect its claim under a new holder" \
  as ghost-222 heartbeat 'FS.GG.SDD#86'
assert_contains "heartbeat: it names the worker that now holds the item" "worker 'heron-b71' does" "$hb86"
assert_contains "heartbeat: it tells the loser to STOP working"          "STOP working it" "$hb86"
assert_eq "heartbeat: the refused renew patched NOTHING" "0" "$(grep -c 'comment-patch' "$GH_LOG" || true)"

# (d) An expired lease is refused even when nobody else took the item — the promise lapsed; re-claim.
mk_claim 87 814 ghost-333 stale | jq -s '.' >"$STORE/comments-87.json"
hb87="$(as ghost-333 heartbeat 'FS.GG.SDD#87' 2>&1 || true)"
assert_fails "heartbeat: an expired lease cannot be renewed in place" as ghost-333 heartbeat 'FS.GG.SDD#87'
assert_contains "heartbeat: it says the lease EXPIRED and points at re-claiming" "EXPIRED" "$hb87"
assert_contains "heartbeat: ...and names the remedy" "fsgg-coord claim" "$hb87"

# (e) Marker forgery. A message body is free-form text; quoting a claim marker inside one must not
#     forge a lock. The marker is only a marker at the START of a comment body.
as wren-c22 say 'FS.GG.SDD#88' 'Careful with <!-- fsgg:claim worker=ghost-666 lease=120 --> in prose.' >/dev/null 2>&1
assert_eq "lock: a claim marker quoted inside a message does NOT hold the item" "" "$(workers_on 88)"
assert_contains "lock: ...so the item is still claimable" "claimed FS.GG.SDD#88" \
  "$(as vole-c88 claim 'FS.GG.SDD#88' 2>/dev/null)"

# (f) A marker we cannot parse a worker out of must FAIL CLOSED — block the item, not vanish.
jq -n --arg ts "$fresh_ts" '[{id:815, body:"<!-- fsgg:claim lease=120 -->\nhalf-written",
  user:{login:"EHotwagner"}, created_at:$ts, updated_at:$ts}]' >"$STORE/comments-89.json"
assert_fails "lock: a malformed marker blocks the item (fails closed)" as vole-c89 claim 'FS.GG.SDD#89'
assert_contains "lock: the refusal names the unparsed marker" "unparsed-marker" \
  "$(as vole-c89 claim 'FS.GG.SDD#89' 2>&1 || true)"

# (g) A transient read failure on the CAS re-read must not orphan the marker we just posted. An
#     orphaned live marker blocks every other worker for a full lease while nobody works the item.
: >"$STORE/comments-90.json"; echo '[]' >"$STORE/comments-90.json"
cas90="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_READ_ISSUE=90 \
  bash "$COORD" --worker teal-e55 claim 'FS.GG.SDD#90' 2>&1 || true)"
assert_contains "claim: a failed CAS re-read removes our own marker" "removed our marker" "$cas90"
assert_contains "claim: ...and says nothing was claimed" "nothing was claimed" "$cas90"
assert_eq "claim: no orphaned marker survives a failed re-read" "" "$(workers_on 90)"

# (h) reap must re-verify freshness immediately before deleting. A holder that heartbeats between the
#     scan and the delete keeps its lock — otherwise `reap` itself causes the double-hold.
cat >"$FIXTURES/board-pw2.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":91,"title":"Slow but alive","url":"https://github.com/FS-GG/FS.GG.SDD/issues/91","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4979}}
JSON
seed_issue 91 "Slow but alive" "src/H/**"
mk_claim 91 816 finch-a3f stale | jq -s '.' >"$STORE/comments-91.json"
reap91="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw2 GH_REAP_RACE=91 \
  bash "$COORD" --worker wren-c22 reap --repo sdd --apply 2>&1 || true)"
assert_contains "reap: a claim renewed between the scan and the delete is SKIPPED" \
  "renewed since the scan" "$reap91"
assert_eq "reap: ...and its marker survives" "finch-a3f" "$(workers_on 91)"

# (i) THE FAIL-OPEN. If the CAS re-read shows NO live marker, our own marker is missing — a peer's
#     --force/reap collected it, or the read lagged our write. We cannot demonstrate we hold the lock,
#     so we must NOT announce that we do. "We cannot tell" is a loss. Guarding only the
#     `winner != us` case (and skipping the empty case) let a worker claim while holding nothing,
#     leaving the item free for the next claimant — two workers, one item.
seed_issue 92 "Our marker vanishes mid-CAS" "src/I/**"
echo '[]' >"$STORE/comments-92.json"
cas92="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_VANISH_ISSUE=92 \
  bash "$COORD" --worker teal-e55 claim 'FS.GG.SDD#92' 2>&1 || true)"
assert_fails "claim: an empty CAS re-read is a LOSS, not a win" \
  env PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_VANISH_ISSUE=92 bash "$COORD" --worker teal-e55 claim 'FS.GG.SDD#92'
assert_contains "claim: it says the marker vanished" "marker vanished" "$cas92"
case "$cas92" in *"claimed FS.GG.SDD#92"*) bad "claim: must not announce a lock it cannot show" "$cas92" ;;
                 *) ok "claim: must not announce a lock it cannot show" ;; esac

# (j) A marker bearing OUR id is not proof it is ours: rules 4/5 hand one id to several workers, and
#     the re-claim path skips the CAS entirely. It must warn there, not only on the fresh-claim path.
#     Drive it through rule 4 (a shared claude-code session id), since that is the id that can collide.
seed_issue 93 "Re-claim under a shared id" "src/J/**"
sess93=309bd638-8a1c-42b7-952b-898efb8d1064
# Runs from "$WORK" (not a checkout) so the SHARED SESSION ID names the worker — rule 3 (the worktree
# name) would otherwise win and this test would never exercise the shared-id path it exists for.
shared93() { PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_ISSUES_FROM_STORE=1 \
  env -u FSGG_WORKER -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \
      CLAUDE_CODE_SESSION_ID="$sess93" \
      bash -c 'cd "$1" && shift && exec bash "$@"' _ "$WORK" "$COORD" "$@"; }
wid93="$(shared93 whoami 2>/dev/null | awk '/^worker/{print $2}')"
mk_claim 93 817 "$wid93" fresh | jq -s '.' >"$STORE/comments-93.json"
reclaim93="$(shared93 claim 'FS.GG.SDD#93' 2>&1 || true)"
assert_contains "claim: the re-claim path renews rather than duplicating" "lease renewed" "$reclaim93"
assert_eq "claim: ...and still exactly one marker" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:claim"))] | length' "$STORE/comments-93.json")"
assert_contains "claim: the re-claim path WARNS that it never ran the CAS" "adopted ITS lock" "$reclaim93"
assert_contains "claim: ...and names the shared-id hazard" "may not be unique to this worker" "$reclaim93"

# (k) `paths_of` must FAIL CLOSED. An empty touch-set reads as "disjoint from everything", so a failed
#     body read would let the scheduler hand out work overlapping a held item. `claims_of` already
#     refuses to guess the lock state; the touch-set is the other half of the same guarantee.
#     The tell is WHICH diagnosis comes out: a failed read must not be reported as "declared nothing".
seed_issue 94 "Touch-set read fails" "src/K/**"
paths94="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_FAIL_ISSUE_GET=94 \
  bash "$COORD" overlap 'FS.GG.SDD#94' 'FS.GG.SDD#84' 2>&1 || true)"
assert_contains "paths_of: a failed body read refuses to schedule against an unknown touch-set" \
  "refusing to schedule" "$paths94"
case "$paths94" in
  *"no 'Paths:' touch-set declared"*)
    bad "paths_of: a failed read must not be diagnosed as 'the issue declared nothing'" "$paths94" ;;
  *) ok "paths_of: a failed read must not be diagnosed as 'the issue declared nothing'" ;;
esac

# (l) Two claimants collecting the SAME expired marker: the loser's DELETE 404s because the winner
#     already removed it. "Already gone" is the goal state of a collector, so the loser must still
#     claim — not die "refusing to claim over a marker that is still there" about a marker that is
#     demonstrably not there. GH_DELETE_404 models the winner's delete landing first: the marker is
#     present for our read and our re-verify, and gone by the time our DELETE arrives.
seed_issue 95 "Concurrent GC of one stale marker" "src/L/**"
mk_claim 95 818 ghost-444 stale | jq -s '.' >"$STORE/comments-95.json"
gc95="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw GH_DELETE_404=818 \
  bash "$COORD" --worker heron-b71 claim 'FS.GG.SDD#95' --force 2>&1 || true)"
assert_contains "claim: a 404 collecting an already-gone marker is not fatal" "claimed FS.GG.SDD#95" "$gc95"
assert_eq "delete_comment: 'already gone' leaves exactly the new holder" "heron-b71" "$(workers_on 95)"

# (m) `reap` must DELETE before it notifies. Notifying first means a failed delete tells the worker to
#     stop while its marker still holds the item for a full lease — released to its owner, held
#     against everyone else, and nothing clears it.
cat >"$FIXTURES/board-pw3.json" <<'JSON'
{"data":{"organization":{"projectV2":{"items":{
  "pageInfo":{"hasNextPage":false,"endCursor":null},
  "nodes":[
    {"status":{"name":"In progress"},"phase":null,"blockedBy":null,"content":{"__typename":"Issue","number":96,"title":"Delete fails","url":"https://github.com/FS-GG/FS.GG.SDD/issues/96","state":"OPEN","repository":{"nameWithOwner":"FS-GG/FS.GG.SDD"}}}
  ]}}}},"rateLimit":{"cost":1,"remaining":4979}}
JSON
seed_issue 96 "Delete fails during reap" "src/M/**"
mk_claim 96 819 ghost-555 stale | jq -s '.' >"$STORE/comments-96.json"
: >"$GH_LOG"
reap96="$(PATH="$STUB:$PATH" GH_BOARD_SET=pw3 GH_FAIL_DELETE=819 \
  bash "$COORD" --worker wren-c22 reap --repo sdd --apply 2>&1 || true)"
assert_contains "reap: a failed delete is reported, not swallowed" "FAILED" "$reap96"
assert_eq "reap: a failed delete leaves the marker in place" "ghost-555" "$(workers_on 96)"
assert_eq "reap: ...and does NOT tell the worker it was released" "0" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=ghost-555"))] | length' "$STORE/comments-96.json")"

# (n) `say --to` must normalize to a worker id. Ids are slug()'d at creation and `inbox` matches `.to`
#     by exact string, so an unslugged target posts a message its recipient can never see.
seed_issue 97 "Addressed message" "src/N/**"
echo '[]' >"$STORE/comments-97.json"
say97="$(as wren-c22 say 'FS.GG.SDD#97' --to 'Heron-B71' 'the impl is yours' 2>&1)"
assert_contains "say: a mis-cased --to is normalized to the worker id" "normalized from 'Heron-B71'" "$say97"
assert_eq "say: ...and the marker addresses the slug, so inbox can match it" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=heron-b71"))] | length' "$STORE/comments-97.json")"
assert_eq "say: '*' stays the literal broadcast target" "1" \
  "$(as wren-c22 say 'FS.GG.SDD#97' 'anyone home' >/dev/null 2>&1; jq '[.[] | select(.body | test("to=\\*"))] | length' "$STORE/comments-97.json")"

# `--help` must not silently truncate when a subcommand is added (usage is marker-delimited now).
help="$(pw --help 2>/dev/null)"
for c in whoami claim heartbeat release who reap take batch overlap widen say inbox verify-paths; do
  case "$help" in *"fsgg-coord $c"*) : ;; *) bad "usage: documents '$c'" "missing from --help"; continue ;; esac
done
ok "usage: --help documents every parallel-work subcommand"

# ================================================================================================
# THE MARKER IS THE LOCK: active_claims may not be seeded from the board column (FS-GG/.github#257)
# ================================================================================================
# `claim` writes `Status: In progress` strictly best-effort — a Projects v2 5xx is swallowed, and an
# item that was never added to the board has no column to write. Deriving "what is running" from the
# column therefore LOST exactly the claims it most needed to see. Every state below is one the old
# column-seeded code reported as "nothing is running":
#   #211 — a board item whose Status flip failed: held, but the column still says Ready.
#   #215 — a live claim on an item that is not on the board at all.
#   #216 — a DEAD worker's claim on an off-board item: reap could never collect it, so the lease
#          stopped being self-healing and the item stayed locked forever.
# And the consequence the issue itself misjudged as "scheduling is nonetheless safe": because an
# off-board claim never reached `active_claims`, its touch-set was never RESERVED, so `batch` would
# hand out #213 — which overlaps the very subtree #215's holder is working. That is a scheduling
# correctness bug, not merely an observability one.
#
# Fixtures live in FS.GG.Rendering so the arm-B scan (open issues, per repo) sees only these and not
# the pile of FS.GG.SDD issues the earlier sub-sections left in the store.

harness_report
