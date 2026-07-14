#!/usr/bin/env bash
# Fixture for scripts/sync-build-config.sh — the org-shared .NET build-config distributor (ADR-0006).
#
# The regression under guard is .github#387: apply/adopt used to run the refuse-and-adopt safety net
# only for *.props, so a hand-authored .config/dotnet-tools.json fell through to an unconditional `cp`
# and was silently clobbered — data loss. This fixture pins that the manifest is now fail-closed the
# same way the .props files are, WITHOUT weakening the .props behaviour it was modelled on.
#
# It drives the REAL script against the REAL canonical source (dist/dotnet/) and a throwaway consumer
# dir, so it can never pass on a source that stopped shipping one of the managed files — and it never
# retypes the logic it checks. No network, no repo state beyond a mktemp'd target.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
SCRIPT="$REPO_ROOT/scripts/sync-build-config.sh"
SRC="$REPO_ROOT/dist/dotnet"
MANIFEST=".config/dotnet-tools.json"
PROP="Directory.Build.props"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

echo "sync-build-config fixture — script='$SCRIPT'"

# The source of truth must actually carry the three managed files, or every assertion below is vacuous.
for f in "$PROP" "Directory.Packages.props" "$MANIFEST"; do
  [ -f "$SRC/$f" ] || { bad "canonical source ships $f" "missing $SRC/$f"; }
done

WORK="$(mktemp -d "${TMPDIR:-/tmp}/sync-build-config-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# fresh_target -> prints a brand-new empty consumer dir under $WORK. Uses mktemp rather than a
# counter: the callers capture it via `$(fresh_target)`, whose subshell would swallow any counter
# increment and hand every test the SAME directory, leaking state between assertions.
fresh_target() { mktemp -d "$WORK/repo-XXXXXX"; }

# run <mode-flag-or-empty> <target> -> rc; captures combined output in $OUT
run() {
  local flag="$1" target="$2" rc=0
  if [ -n "$flag" ]; then OUT="$(bash "$SCRIPT" "$flag" "$target" 2>&1)" || rc=$?
  else                    OUT="$(bash "$SCRIPT"        "$target" 2>&1)" || rc=$?; fi
  return "$rc"
}

HAND='{ "version": 1, "isRoot": true, "tools": { "my-repo-tool": { "version": "1.0.0", "commands": ["mine"] } } }'

# --- apply on a clean repo writes every managed file --------------------------------------------
t="$(fresh_target)"; rc=0; run "" "$t" || rc=$?
if [ "$rc" -eq 0 ] && diff -q "$SRC/$MANIFEST" "$t/$MANIFEST" >/dev/null 2>&1 \
   && diff -q "$SRC/$PROP" "$t/$PROP" >/dev/null 2>&1; then
  ok "apply on an empty repo writes the canonical manifest and props"
else
  bad "apply on an empty repo" "rc=$rc"$'\n'"$OUT"
fi

# --- --check is green on a freshly synced repo, red once the manifest drifts ---------------------
rc=0; run "--check" "$t" || rc=$?
[ "$rc" -eq 0 ] && ok "check is green right after a sync" || bad "check after sync" "rc=$rc"$'\n'"$OUT"

printf '%s\n' "$HAND" > "$t/$MANIFEST"
rc=0; run "--check" "$t" || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q "DRIFT (differs): $MANIFEST"; then
  ok "check reports DRIFT once the manifest is hand-edited"
else
  bad "check on a drifted manifest" "want rc!=0 + DRIFT line; rc=$rc"$'\n'"$OUT"
fi

# --- THE BUG (#387): apply must not clobber a differing manifest SILENTLY -------------------------
# The manifest is fully managed with no *.local override, so re-sync must still overwrite it (that is
# the documented update path) — refusing would break re-sync. The fix is to make the overwrite LOUD:
# apply overwrites (exit 0, so a bulk re-sync stays green) but WARNS, naming the file.
t="$(fresh_target)"; mkdir -p "$t/.config"; printf '%s\n' "$HAND" > "$t/$MANIFEST"
rc=0; run "" "$t" || rc=$?
if [ "$rc" -eq 0 ] \
   && printf '%s' "$OUT" | grep -qi "WARNING: overwriting $MANIFEST" \
   && diff -q "$SRC/$MANIFEST" "$t/$MANIFEST" >/dev/null 2>&1; then
  ok "apply warns (not silently) before overwriting a differing manifest, and still re-syncs it (#387)"
else
  bad "apply must warn — not silently clobber — a differing manifest (#387)" \
      "rc=$rc (want 0)"$'\n'"manifest now: $(cat "$t/$MANIFEST")"$'\n'"--- output ---"$'\n'"$OUT"
fi

# --- apply on an already-canonical manifest is a QUIET no-op (no spurious warning) ----------------
t="$(fresh_target)"; mkdir -p "$t/.config"; cp "$SRC/$MANIFEST" "$t/$MANIFEST"
rc=0; run "" "$t" || rc=$?
if [ "$rc" -eq 0 ] \
   && diff -q "$SRC/$MANIFEST" "$t/$MANIFEST" >/dev/null 2>&1 \
   && ! printf '%s' "$OUT" | grep -qi "WARNING: overwriting $MANIFEST"; then
  ok "apply is a quiet no-op on an identical manifest (no spurious overwrite warning)"
else
  bad "apply on a canonical manifest should be a quiet no-op" "rc=$rc"$'\n'"$OUT"
fi

# --- adopt backs the hand-authored manifest up to *.local.json, then writes canonical ------------
t="$(fresh_target)"; mkdir -p "$t/.config"; printf '%s\n' "$HAND" > "$t/$MANIFEST"
rc=0; run "--adopt" "$t" || rc=$?
if [ "$rc" -eq 0 ] \
   && diff -q "$SRC/$MANIFEST" "$t/$MANIFEST" >/dev/null 2>&1 \
   && diff -q <(printf '%s\n' "$HAND") "$t/.config/dotnet-tools.local.json" >/dev/null 2>&1; then
  ok "adopt moves the hand-authored manifest to dotnet-tools.local.json and writes canonical"
else
  bad "adopt should preserve the manifest as *.local.json" \
      "rc=$rc"$'\n'"local.json: $(cat "$t/.config/dotnet-tools.local.json" 2>&1)"$'\n'"$OUT"
fi

# --- adopt refuses when both a hand-authored manifest and its *.local.json already exist ---------
t="$(fresh_target)"; mkdir -p "$t/.config"
printf '%s\n' "$HAND" > "$t/$MANIFEST"
printf '%s\n' '{ "version": 1, "isRoot": true, "tools": {} }' > "$t/.config/dotnet-tools.local.json"
rc=0; run "--adopt" "$t" || rc=$?
if [ "$rc" -ne 0 ] \
   && printf '%s' "$OUT" | grep -qi "REFUSING to adopt $MANIFEST" \
   && diff -q <(printf '%s\n' "$HAND") "$t/$MANIFEST" >/dev/null 2>&1; then
  ok "adopt refuses (and preserves both) when the manifest and its *.local.json both exist"
else
  bad "adopt should fail-closed when *.local.json is already taken" "rc=$rc"$'\n'"$OUT"
fi

# --- regression: the *.props protection this was modelled on still holds -------------------------
# A hand-authored .props (no marker) must still be refused on apply. Guards against a refactor that
# generalised the manifest at the expense of the case it was copied from.
t="$(fresh_target)"; printf '%s\n' '<Project><!-- hand authored --></Project>' > "$t/$PROP"
rc=0; run "" "$t" || rc=$?
if [ "$rc" -ne 0 ] \
   && printf '%s' "$OUT" | grep -qi "REFUSING to overwrite hand-authored $PROP" \
   && grep -q "hand authored" "$t/$PROP"; then
  ok "apply still refuses a hand-authored .props (no marker) — .props path unregressed"
else
  bad ".props refusal must be unchanged" "rc=$rc"$'\n'"$OUT"
fi

# --- #633: the drift message must name a command that EXISTS where the reader is standing ---------
#
# The one gate whose entire job is to report "you never re-synced" used to answer with
# "Re-run: scripts/sync-build-config.sh <repo>" — a repo-relative path that exists in NO receiver.
# This script lives in FS-GG/.github; every adopter's CI checks THIS repo out into a scratch dir and
# runs it against a SEPARATE checkout of the repo under test, so the person reading the red gate is
# standing in a repo where that path is `No such file or directory`.
#
# The assertion is behavioural, not a wording match — wording drifts, and a test that pins prose just
# gets updated to match whatever prose is there. So: TAKE the command the failure prints, RUN it, and
# require that it turns the gate green. A path that exists nowhere cannot pass that.
t="$(fresh_target)"
run "" "$t" || bad "#633 setup: apply must succeed before we can drift it" "$OUT"   # sync it clean...
printf '%s\n' "$HAND" > "$t/$MANIFEST"                                              # ...then drift it
rc=0; run "--check" "$t" || rc=$?
DRIFT_OUT="$OUT"

# The message assertions below only grep text, so without this leg a --check that stopped FAILING —
# printing the DRIFT lines and the remediation but exiting 0 — would pass every one of them, and the
# fixture would report green on a gate that no longer gates.
[ "$rc" -ne 0 ] \
  && ok "check still exits non-zero on drift (the message legs below are not grepping a green run)" \
  || bad "check must exit non-zero on drift (#633)" "rc=$rc"$'\n'"$DRIFT_OUT"

if printf '%s' "$DRIFT_OUT" | grep -qE 'Re-run: scripts/sync-build-config\.sh'; then
  bad "drift remediation must not name a path that exists in no receiver (#633)" \
      "The receiver-relative path is back. 'scripts/sync-build-config.sh' does not exist in the repo
being checked — this script is only ever present in FS-GG/.github."
else
  ok "drift remediation does not resurrect the receiver-relative path (#633)"
fi

# The absolute form it prints must be a real, runnable script — not a path assembled from prose.
#
# Anchored to `^  /` — a line that IS an absolute command — rather than the first path-shaped match
# anywhere in the output. The message also contains the clone form, whose script path lives under a
# scratch dir; an unanchored match would latch onto that the moment the two forms are reordered, and
# on a machine where that dir happened to exist the fixture would drive a STALE clone as its source
# of truth and compare it against the working tree. Anchoring keeps the parse pinned to the one line
# that is meant to be a resolved, runnable path.
#
# `|| true`: under `set -e`, a grep that matches nothing would kill the fixture here — which is
# precisely the regression case — so the remaining legs (and the summary, and the ::error::
# annotation) would never print. A missing match must be a FAIL, not an abort.
suggested="$(printf '%s' "$DRIFT_OUT" | grep -oE '^  /[^[:space:]]*/sync-build-config\.sh' | head -n1 | sed 's/^  //' || true)"
if [ -n "$suggested" ] && [ -f "$suggested" ]; then
  ok "drift remediation names this script by its resolved, existing path (#633)"
else
  bad "drift remediation must name the script's resolved path (#633)" \
      "parsed: '${suggested:-<none>}'"$'\n'"--- output ---"$'\n'"$DRIFT_OUT"
fi

# ...and FOLLOWING it must actually re-sync the repo. This is the leg that cannot rot: the message is
# only correct if the command it prints fixes the very thing it is complaining about.
# `bash "$suggested"` deliberately runs the path the MESSAGE named, not "$SCRIPT" — that is the whole
# assertion. The re-check afterwards goes through the usual helper.
if [ -n "$suggested" ] && [ -f "$suggested" ]; then
  rc=0; bash "$suggested" "$t" >/dev/null 2>&1 || rc=$?
  if [ "$rc" -eq 0 ] && run "--check" "$t"; then
    ok "following the printed remediation re-syncs the repo and turns the gate green (#633)"
  else
    bad "the command the drift message prints must make --check green (#633)" \
        "ran: bash '$suggested' '$t' -> rc=$rc"$'\n'"--- re-check ---"$'\n'"$OUT"
  fi
fi

# The other reader — staring at a red gate in a receiver's CI, with no .github checkout to hand — is
# given a form that does not assume one. (The resolved path above is a runner-local scratch dir to
# them, so it is exactly the reader the original message stranded.)
if printf '%s' "$DRIFT_OUT" | grep -q 'git clone --depth 1 https://github.com/FS-GG/.github.git'; then
  ok "drift remediation also gives a form runnable with no .github checkout to hand (#633)"
else
  bad "drift remediation must serve the receiver-CI reader, who has no .github checkout (#633)" \
      "$DRIFT_OUT"
fi

# ...and that form must survive being run TWICE. `git clone` into a fixed path fails the second time
# ("destination path already exists and is not an empty directory"), which strands the reader with two
# drifted repos — or the one who simply retries after a typo. A remediation that works only once is
# the same defect this item is fixing, one step further along.
if printf '%s' "$DRIFT_OUT" | grep -q 'mktemp -d' \
   && ! printf '%s' "$DRIFT_OUT" | grep -qE 'clone .*/tmp/[a-z0-9._-]+ ?\\?$'; then
  ok "the clone form clones into a fresh dir, so it is re-runnable (#633)"
else
  bad "the clone remediation must not hardcode a fixed destination — it fails on the 2nd run (#633)" \
      "$DRIFT_OUT"
fi

# The population this message is about to meet is FIRST-TIME ADOPTERS (#561 rolls a managed file out
# to four repos that never carried one). For them a plain re-sync REFUSES any file they hand-authored
# before the org managed it, and exits 1 — so a message that names only the plain form sends them into
# a second wall. It must name --adopt.
if printf '%s' "$DRIFT_OUT" | grep -q -- '--adopt'; then
  ok "drift remediation names --adopt, the path a first-time adopter actually needs (#633)"
else
  bad "drift remediation must name --adopt for the hand-authored/first-adoption case (#633)" \
      "A receiver with a hand-authored Directory.Build.props follows this message, hits
'REFUSING to overwrite hand-authored ...', and is stuck. That is the #561 rollout population."$'\n'"$DRIFT_OUT"
fi

# --- global.json: distributed, but NOT YET enforced. The ordering rule, as a gate (.github#536) ----
#
# `--check` treats a MISSING managed file as DRIFT, and the drift check is REQUIRED in adopting repos.
# So a name added to FILES before the receivers carry that file merge-freezes every one of them — they
# go red on a check they cannot make green from their own PR.
#
# That is not a hypothetical: #499 moved the source of truth and FS.GG.SDD could not merge ANYTHING
# for hours (.github#379). Four of the five consumers have no global.json today, so putting it in FILES
# now would do the same to four repos at once.
#
# The rule — receivers adopt first, the shared config enforces last — was written in a comment, and a
# comment is not a gate (#266). So it is a test. These two legs are a TRIPWIRE, and they are meant to
# be DELETED, not worked around: when Game / Rendering / SDD / Governance have all adopted, the item
# that adds "global.json" to FILES removes this block in the same PR. Until then, a well-meaning
# "finish the job" edit reds here, with the reason, instead of freezing the org.
[ -f "$SRC/global.json" ] \
  && ok "global.json: the canonical SDK pin is distributed (present under dist/dotnet/)" \
  || bad "global.json: canonical SDK pin missing from dist/dotnet/" \
         "#536 distributes the SDK pin; without the source file, step 3 (adding it to FILES) can never be taken."

if grep -qE '^\s*"global\.json"' "$SCRIPT"; then
  bad "global.json: is NOT in FILES yet — receivers must adopt FIRST" \
      "$(printf '"global.json" was added to sync-build-config.sh FILES, but 4 of 5 consumers (Game, Rendering, SDD, Governance) have no global.json.\n--check reports a MISSING managed file as DRIFT, and that check is REQUIRED in those repos — so this change MERGE-FREEZES all four, exactly as .github#499 froze FS.GG.SDD (#379).\n\nIf the receivers HAVE now adopted: delete this tripwire block in the same PR. That is the intended way to remove it.\nIf they have not: land the per-repo adoption items first (see .github#536).')"
else
  ok "global.json: correctly NOT in FILES yet — adding it before the receivers adopt would freeze 4 repos"
fi

# =================================================================================================
# THE PROVENANCE PIN (.github#592) — --check measures against the receiver's PIN, not against main.
#
# The defect: every receiver's gate.yml checks .github out at `ref: main` and runs --check, so the
# verdict was a function of ANOTHER REPO'S MOVING BRANCH at the moment CI happened to run. A receiver
# could not make the check green from its own PR, and any edit to dist/dotnet/ red-lit every open PR
# in every adopting repo. It fired twice: #499 (froze FS.GG.SDD for hours) and #536 — where a change
# to an XML COMMENT was enough to redden a green, in-flight PR.
#
# These legs need a .github whose history the fixture CONTROLS — the real one cannot be moved forward
# under a test. So make_org() builds a throwaway one carrying the REAL script and the REAL dist/dotnet/,
# and commits it. The script under test is therefore still the real script, driven against a source of
# truth that can be made to move exactly as #536 moved it.
# =================================================================================================

if ! command -v git >/dev/null 2>&1; then
  bad "pin legs (#592) need git" "git is not on PATH; the pin is resolved out of .github's object store"
else

# make_org -> prints a fresh throwaway FS-GG/.github (real script + real dist/dotnet/), committed.
make_org() {
  local org; org="$(mktemp -d "$WORK/org-XXXXXX")"
  mkdir -p "$org/scripts" "$org/dist/dotnet"
  cp "$SCRIPT" "$org/scripts/sync-build-config.sh"
  cp -R "$SRC/." "$org/dist/dotnet/"
  git -C "$org" init -q
  git -C "$org" config user.email 'fixture@example.invalid'
  git -C "$org" config user.name  'sync-build-config fixture'
  git -C "$org" config commit.gpgsign false
  git -C "$org" add -A
  git -C "$org" commit -q -m 'org baseline'
  printf '%s' "$org"
}

# org_run <org> <flag-or-empty> <target> -> rc; combined output in $OUT.
# Runs the copy of the script INSIDE that org checkout, so it resolves its pin from that history.
org_run() {
  local org="$1" flag="$2" target="$3" rc=0
  if [ -n "$flag" ]; then OUT="$(bash "$org/scripts/sync-build-config.sh" "$flag" "$target" 2>&1)" || rc=$?
  else                    OUT="$(bash "$org/scripts/sync-build-config.sh"        "$target" 2>&1)" || rc=$?; fi
  return "$rc"
}

# upstream_comment_edit <org> — reproduce #536: change an XML COMMENT in a managed file, nothing else.
# Inserted before the closing root tag so the file stays well-formed; the script REFUSES to distribute
# malformed XML (#29), which would make these legs vacuous in a way that still looked like a pass.
upstream_comment_edit() {
  local props="$1/dist/dotnet/Directory.Build.props" tmp; tmp="$(mktemp)"
  awk '/^<\/Project>/ { print "  <!-- upstream comment-only edit: this is .github#536 -->" } { print }' \
    "$props" > "$tmp"
  cmp -s "$props" "$tmp" && { rm -f "$tmp"; return 1; }   # a no-op edit would make every leg below vacuous
  mv "$tmp" "$props"
  git -C "$1" add -A
  git -C "$1" commit -q -m 'comment-only edit to the shared build config (#536)'
}

PIN_REL=".config/fsgg-build-config.sha"

# --- a clean sync records the pin ----------------------------------------------------------------
ORG="$(make_org)"; A="$(git -C "$ORG" rev-parse HEAD)"
t="$(fresh_target)"; rc=0; org_run "$ORG" "" "$t" || rc=$?
if [ "$rc" -eq 0 ] && [ -f "$t/$PIN_REL" ] && grep -qx "$A" "$t/$PIN_REL"; then
  ok "apply records the .github commit it distributed in $PIN_REL (#592)"
else
  bad "apply must pin the receiver to the commit it synced from (#592)" \
      "rc=$rc; want $A"$'\n'"pin: $(cat "$t/$PIN_REL" 2>&1)"$'\n'"$OUT"
fi

# --- THE REGRESSION: .github moves, and the receiver's PR stays GREEN -----------------------------
# This is #592 in one assertion. Pre-fix, this comment-only upstream edit turned the receiver RED on a
# required check it could not make green from its own branch — which is what froze FS.GG.SDD twice.
if upstream_comment_edit "$ORG"; then
  B="$(git -C "$ORG" rev-parse HEAD)"
  rc=0; org_run "$ORG" "--check" "$t" || rc=$?
  if [ "$rc" -eq 0 ]; then
    ok "an upstream dist/dotnet/ change does NOT red-light a pinned receiver (#592 — the whole item)"
  else
    bad "a pinned receiver must stay GREEN when .github@main moves (#592)" \
        "This is the merge freeze: #499 and #536 both red-lit finished, green PRs in repos that had
changed nothing. The receiver's files still match its pin ($A); .github merely moved to $B.
rc=$rc"$'\n'"$OUT"
  fi

  # ...and it must SAY it is behind. Green-and-silent would hide staleness completely; the deal #592
  # strikes is that staleness becomes visible and bot-remediated, not that it becomes invisible.
  if [ "$rc" -eq 0 ] && printf '%s' "$OUT" | grep -qi "BEHIND the org baseline"; then
    ok "a behind-but-coherent receiver is told so, loudly, while staying green (#592)"
  else
    bad "being behind must be reported, not silently tolerated (#592)" "$OUT"
  fi

  # --- ...while a REAL hand-edit is still RED, and is now named correctly -------------------------
  # The check keeps the only job it can honestly do: catching a defect the PR author can actually fix.
  printf '%s\n' '<Project><!-- hand-edited by the receiver --></Project>' > "$t/Directory.Build.props"
  rc=0; org_run "$ORG" "--check" "$t" || rc=$?
  if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q "DRIFT (differs): Directory.Build.props"; then
    ok "a hand-edited managed file is still RED against the pin (#592 does not gut the check)"
  else
    bad "a hand-edited managed file must fail the check (#592)" "rc=$rc"$'\n'"$OUT"
  fi

  # The accusation must be the RIGHT one. Pre-#592 a red here usually meant "you are behind"; it cannot
  # mean that any more, and a message that still says so sends the author hunting an upstream change
  # that is not the cause.
  if printf '%s' "$OUT" | grep -qi "HAND-EDITED"; then
    ok "the red names a hand-edit — not 'you are behind', which is now green (#592)"
  else
    bad "a pinned red must tell the author it is a LOCAL edit they can fix from their branch (#592)" "$OUT"
  fi
else
  bad "#592 fixture setup: the upstream comment edit was a no-op" \
      "Directory.Build.props has no '</Project>' line to insert before — the legs above would be vacuous."
fi

# --- LEGACY MODE: an UNPINNED receiver behaves exactly as it did before ---------------------------
# This is what makes the rollout incapable of freezing anyone: until the propagate bot writes a pin,
# every receiver keeps today's semantics. If this leg ever flips green, the migration has silently
# stopped checking the repos that have not been pinned yet.
t2="$(fresh_target)"
cp "$SRC/Directory.Build.props" "$t2/"; cp "$SRC/Directory.Packages.props" "$t2/"
mkdir -p "$t2/.config"; cp "$SRC/$MANIFEST" "$t2/$MANIFEST"      # synced content, but NO pin file
rc=0; org_run "$ORG" "--check" "$t2" || rc=$?
if [ "$rc" -ne 0 ] && printf '%s' "$OUT" | grep -q "DRIFT (differs): Directory.Build.props"; then
  ok "an UNPINNED receiver still compares against main — legacy behaviour preserved (#592 rollout)"
else
  bad "an unpinned receiver must keep the pre-#592 compare-against-main behaviour" \
      "Otherwise the not-yet-pinned repos stop being checked at all. rc=$rc"$'\n'"$OUT"
fi

# --- an UNRESOLVABLE pin FAILS OPEN, loudly — ON A RECEIVER THAT IS BEHIND -------------------------
#
# THE SETUP IS THE ASSERTION. An earlier version of this leg synced the receiver to B (== the org's
# current baseline) and THEN corrupted its pin — so the degraded comparison happened to find the files
# identical to main and exited 0 for a reason that had nothing to do with failing open. It asserted the
# property without ever exercising it.
#
# The case that actually bites is a receiver that is BEHIND (files at A, org at B) whose pin cannot be
# resolved — because a merely-behind receiver does NOT match main, so a fallback that compares against
# main RED-LIGHTS it. One transient `git fetch` blip in CI is enough to reach this state, and reddening
# an innocent PR on a required check is the exact merge freeze (#499/#536) this whole item removes.
#
# So: pin a BEHIND receiver to a commit that never existed, and require GREEN.
t3="$(fresh_target)"
cp "$SRC/Directory.Build.props" "$t3/"; cp "$SRC/Directory.Packages.props" "$t3/"
mkdir -p "$t3/.config"; cp "$SRC/$MANIFEST" "$t3/$MANIFEST"
# ^ the REAL dist/dotnet, i.e. the org baseline BEFORE the fixture's upstream edit -> this receiver is
#   coherent-but-behind relative to $ORG's HEAD (B), which carries the extra comment.
printf '%s\n' "$(printf '0%.0s' $(seq 40))" > "$t3/$PIN_REL"   # a 40-hex SHA that is not a commit
rc=0; org_run "$ORG" "--check" "$t3" || rc=$?
if [ "$rc" -eq 0 ] && printf '%s' "$OUT" | grep -qi "could not be resolved"; then
  ok "an unresolvable pin on a BEHIND receiver passes with a warning — a fetch blip cannot freeze a repo (#592)"
else
  bad "an unresolvable pin must fail OPEN, even when the receiver is BEHIND (#592)" \
      "The receiver is coherent-but-behind and its pin cannot be read. Falling back to a compare against
main reddens it — which is the merge freeze this item exists to end, re-entered through a network blip.
Without a readable baseline, BEHIND and HAND-EDITED are indistinguishable and have opposite verdicts:
refuse to guess, and pass. rc=$rc (want 0)"$'\n'"$OUT"
fi

# ...and it must be honest that it did not actually check anything, rather than printing a green that
# looks like a pass. An unevaluated gate reporting an ordinary success is #266's whole family.
if printf '%s' "$OUT" | grep -qi "ADVISORY"; then
  ok "the unevaluatable check says so — it does not pass itself off as a clean verdict (#592)"
else
  bad "an unresolvable pin must report ADVISORY, not a silent pass (#592)" "$OUT"
fi

# --- a DIRTY dist/dotnet/ must not be pinned ------------------------------------------------------
# apply copies the WORKING TREE; a pin names a COMMIT. Syncing from a checkout with uncommitted changes
# under dist/dotnet/ and stamping it with HEAD would hand the receiver files that match no commit — and
# --check would then accuse that innocent receiver of the hand-edit the OPERATOR made upstream.
printf '%s\n' '  <!-- uncommitted -->' >> "$ORG/dist/dotnet/Directory.Packages.props"   # never committed
t4="$(fresh_target)"; rc=0; org_run "$ORG" "" "$t4" || rc=$?
if [ "$rc" -eq 0 ] && [ ! -f "$t4/$PIN_REL" ]; then
  ok "a sync from a DIRTY dist/dotnet/ writes no pin — a pin must not be a false claim (#592)"
else
  bad "syncing from an uncommitted dist/dotnet/ must not write a pin (#592)" \
      "It would claim these files came from HEAD when they did not, and --check would then report the
receiver as hand-edited. rc=$rc; pin: $(cat "$t4/$PIN_REL" 2>&1)"$'\n'"$OUT"
fi
git -C "$ORG" checkout -q -- dist/dotnet/Directory.Packages.props

fi   # git available

echo "sync-build-config fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::sync-build-config fixture FAILED"; exit 1; }
echo "sync-build-config fixture — OK"
