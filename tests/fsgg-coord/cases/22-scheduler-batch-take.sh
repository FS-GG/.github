#!/usr/bin/env bash
# case: scheduler batch take
# tier: smoke
# covers: batch take overlap say inbox widen
#
# Lifted VERBATIM from the fsgg-coord monolith. The world it runs against — fixtures, the counting
# `gh` stub, the seeders, the ADR-0027 parallel-work board and its pre-existing claims — comes from
# lib/harness.sh, which is the monolith's own prelude. Nothing here was rewritten to make it pass.
set -euo pipefail
CASE_NAME="22-scheduler-batch-take"
. "$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)/lib/harness.sh"

# ---- overlap --active: a candidate against everything in flight --------------------------------
assert_contains "overlap --active: disjoint candidate clears every live claim" "DISJOINT" \
  "$(pw overlap 'FS.GG.SDD#73' --active 2>/dev/null || true)"
ov="$(pw overlap 'FS.GG.SDD#71' --active 2>&1 || true)"
assert_contains "overlap --active: names the colliding item AND its worker" "OVERLAP — FS.GG.SDD#71 ⇄ FS.GG.SDD#42 (worker finch-a3f" "$ov"
assert_contains "overlap --active: shows the conflicting subtrees" "src/Audio/Mixer  ⇄  src/Audio" "$ov"
assert_fails "overlap --active: exits non-zero on a collision" pw overlap 'FS.GG.SDD#71' --active

# ---- batch: the scheduler ----------------------------------------------------------------------
# #42 (in flight) owns src/Audio, so #71 is unschedulable. #72 declares nothing. #73 overlaps #70,
# which was chosen first. That leaves #70 and #74 — a disjoint pair, safe to hand to two workers.
batch_json="$(pw batch --repo sdd --json 2>/dev/null)"
assert_eq "batch: picks a maximal DISJOINT set" '["FS.GG.SDD#70","FS.GG.SDD#74"]' "$(jq -c '.' <<<"$batch_json")"
batch_err="$(pw batch --repo sdd 2>&1 >/dev/null)"
assert_contains "batch: says why it skipped in-flight overlap"   "#71 — overlaps in-flight work" "$batch_err"
assert_contains "batch: says why it skipped an undeclared item"  "#72 — no 'Paths:' declared" "$batch_err"
assert_contains "batch: says why it skipped a batch-mate clash"  "#73 — overlaps batch member FS.GG.SDD#70" "$batch_err"
assert_eq "batch -n 1: honours the requested width" '["FS.GG.SDD#70"]' \
  "$(pw batch --repo sdd -n 1 --json 2>/dev/null | jq -c '.')"

# ---- take: pick + claim in one step -------------------------------------------------------------
took="$(as smew-f31 take --repo sdd 2>/dev/null)"
assert_contains "take: claims the first schedulable item" "claimed FS.GG.SDD#70 by worker smew-f31" "$took"
assert_eq "take: the claim marker is really there" "smew-f31" "$(workers_on 70)"
# With #70 now held by smew, a second worker takes the next disjoint item rather than idling.
took2="$(as brant-g07 take --repo sdd 2>/dev/null)"
assert_contains "take: a second worker gets a DIFFERENT, disjoint item" "claimed FS.GG.SDD#74 by worker brant-g07" "$took2"

# ---- say / inbox: the channel -------------------------------------------------------------------
as finch-a3f say 'FS.GG.SDD#42' --to smew-f31 'I own src/Audio until Friday.' >/dev/null 2>&1
as finch-a3f say 'FS.GG.SDD#42' 'Broadcast to whoever is here.' >/dev/null 2>&1
inbox="$(as smew-f31 inbox --repo sdd 2>/dev/null)"
assert_contains "inbox: delivers a message addressed to this worker" "I own src/Audio until Friday." "$inbox"
assert_contains "inbox: delivers a broadcast (to=*)"                 "Broadcast to whoever is here." "$inbox"
assert_contains "inbox: says which item the message rode in on"      "FS.GG.SDD#42" "$inbox"
assert_eq "inbox: the cursor advanced -> nothing new on a second read" "no new messages for worker smew-f31." \
  "$(as smew-f31 inbox --repo sdd 2>/dev/null)"
assert_eq "inbox: a worker does not see its OWN messages" "no new messages for worker finch-a3f." \
  "$(as finch-a3f inbox --repo sdd 2>/dev/null)"
as finch-a3f say 'FS.GG.SDD#42' --to smew-f31 'One more.' >/dev/null 2>&1
assert_contains "inbox --peek: shows new mail" "One more." "$(as smew-f31 inbox --repo sdd --peek 2>/dev/null)"
assert_contains "inbox --peek: does NOT advance the cursor" "One more." "$(as smew-f31 inbox --repo sdd 2>/dev/null)"

# ---- widen: re-declare mid-flight, and TELL whoever it now collides with ------------------------
widen="$(as brant-g07 widen 'FS.GG.SDD#74' --paths 'docs/adr/**, src/Audio/**' 2>&1 || true)"
assert_contains "widen: rewrites the declared touch-set"          "widened FS.GG.SDD#74 → Paths: docs/adr/**, src/Audio/**" "$widen"
assert_contains "widen: re-checks against in-flight claims"        "now collides with FS.GG.SDD#42 (worker finch-a3f)" "$widen"
assert_contains "widen: notifies the worker it collided with"      "notified worker finch-a3f on FS.GG.SDD#42" "$widen"
assert_eq "widen: the notification is a real message on THEIR item" "1" \
  "$(jq '[.[] | select(.body | test("fsgg:msg")) | select(.body | test("to=finch-a3f"))] | length' "$STORE/comments-42.json")"
assert_contains "widen: the new touch-set persisted to the issue body" "Paths: docs/adr/**, src/Audio/**" \
  "$(jq -r '.body' "$STORE/issue-74.json")"
assert_eq "widen: it replaced the Paths line, it did not append a second one" "1" \
  "$(jq -r '.body' "$STORE/issue-74.json" | grep -c '^Paths:')"
# On a DIFFERENT item, so the assertions above are not perturbed by a second notification.
assert_fails "widen: a collision exits non-zero" as brant-g07 widen 'FS.GG.SDD#73' --paths 'src/Audio/**'


# The "nothing to hand out" path must not trip the empty-array expansion, and still exits 0. Every
# claim this fixture has made by now is in FS.GG.SDD, so that is the queue where everything schedulable
# is claimed or overlapping — stand there.
#
# This used to be a BARE take, and its name said "board-wide (no --repo)". That mode was the #480
# defect, not a feature: with no --repo the scan reached across the whole org, so a bare `take` in the
# `.github` checkout claimed FS.GG.Game#141 and printed a worktree command against `.github`'s
# origin/main. A `take` now always has a scope — the checkout, or an explicit --repo — so the test
# says where it is standing instead of relying on the absence of a scope.
# #585 AMENDS #480: "the empty queue exits cleanly (0)" is reversed. `take` claimed NOTHING, and its
# exit code must SAY so — a worker loop (`take && work_it`) must not proceed on nothing. Nothing-startable
# is EX_NONE (5), NOT 0; the command still runs cleanly (no error), it just reports "no item" honestly.
rc_empty=0; as_at "$CO_SDD" teal-e55 take >/dev/null 2>&1 || rc_empty=$?
assert_eq "take: a nothing-startable queue exits EX_NONE (5), not 0 — nothing was claimed [#480→#585]" "5" "$rc_empty"
# It must say why PER ITEM, in `batch`'s own words. This assertion used to accept the fixed sentence
# "no schedulable item — every candidate is blocked, claimed, overlapping, or undeclared", which named
# four causes without observing any of them (#440) — so the test's own name was the thing it failed to
# check. The reason now has to be one `batch` actually found.
take_empty="$(as_at "$CO_SDD" teal-e55 take 2>&1 >/dev/null || true)"   # #585: EX_NONE now, so guard set -e
assert_contains "take: says WHY there is nothing to hand out" "passed over:" "$take_empty"
assert_contains "take: ...naming a real, observed reason rather than a guessed list" \
  "already claimed by worker" "$take_empty"


harness_report
