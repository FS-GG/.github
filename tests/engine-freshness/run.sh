#!/usr/bin/env bash
# Fixture for scripts/check-engine-freshness.py — the gate that asks whether the engine's SOURCE has
# outrun the version the fleet can restore (.github#1075, epic #266).
#
# The gate exists because a check that passes when its subject is missing manufactures confidence, and
# because the subject here is invisible to every version comparison in the repo: this engine's
# `<Version>` moves only at RELEASE time, so `version == package-version` is precisely the state the
# bug lives in. The gate counts COMMITS instead. So this fixture spends most of its length on the
# FAILURE legs: it proves the gate goes red when the wire surface has drifted, and ERRORS — never
# "no drift" — when the feed is unreadable, the tag is absent, or a measured path has moved.
#
# Every negative leg asserts the REASON, not just a non-zero exit — the .github#266 vacuous-failure
# defect (SDD#299) was a "must fail" test whose non-zero exit came from a path guard rather than from
# the thing under test. `must_fail` therefore takes a required pattern.
#
# Throwaway git trees under a temp dir, no network (the gate's --fixture flag serves a canned feed).
# Mirrors tests/feed-coherence/run.sh.

set -euo pipefail

# The suite runs the gate by path, which would otherwise litter scripts/__pycache__ into a repo that
# has no .gitignore.
export PYTHONDONTWRITEBYTECODE=1

# `--fixture` is locked to this harness: the gate refuses a canned feed unless this is set, so a
# stray `--fixture` in CI fails rather than silently reporting green. See the gate's docstring.
export FSGG_ENGINE_FIXTURE_OK=1

HERE="$(cd "$(dirname "$0")" && pwd)"
GATE="$HERE/../../scripts/check-engine-freshness.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/engine-freshness-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# A fixture git repo INHERITS ~/.gitconfig unless every relevant knob is pinned (.github#709: a
# global `status.showUntrackedFiles` silently switched a guard off, and the suite stayed green on the
# author's machine while reddening on everyone else's). So identity, the initial branch, GPG signing
# and the pager are all pinned here rather than assumed — a leg that depends on the runner's dotfiles
# is a leg that proves nothing.
git_() { git -c user.name=fixture -c user.email=fixture@example.com -c commit.gpgsign=false \
             -c init.defaultBranch=main -c core.pager=cat -c tag.gpgsign=false "$@"; }

# Build a synthetic engine repo: the three source trees the gate measures, plus the wire-surface file.
# `$1` = repo dir.
make_repo() {
  local r="$1"
  mkdir -p "$r"
  git_ -C "$r" init -q
  mkdir -p "$r/src/FS.GG.Coord.Cli" "$r/src/FS.GG.Coord.Core" "$r/src/FS.GG.Coord.GitHub"
  echo "module Protocol"  > "$r/src/FS.GG.Coord.Core/Protocol.fs"
  echo "module Client"    > "$r/src/FS.GG.Coord.Cli/Client.fs"
  echo "module Reads"     > "$r/src/FS.GG.Coord.GitHub/Reads.fs"
  echo "unrelated"        > "$r/README.md"
  git_ -C "$r" add -A
  git_ -C "$r" commit -qm "engine 0.3.0"
  git_ -C "$r" tag "coord-engine/v0.3.0"
}

# Append a commit touching $2 (a path under the repo $1), with subject $3.
touch_commit() {
  local r="$1" path="$2" subject="$3"
  echo "// $subject" >> "$r/$path"
  git_ -C "$r" add -A
  git_ -C "$r" commit -qm "$subject"
}

feed() { # $1 = file, $2... = versions
  local f="$1"; shift
  local vs=""
  for v in "$@"; do vs="$vs\"$v\","; done
  printf '{"FS.GG.Coord.Cli": [%s]}' "${vs%,}" > "$f"
}

run() { # $1 = repo, $2 = feed json  -> stdout+stderr, exit code in $rc
  set +e
  out="$(python3 "$GATE" --repo "$1" --fixture "$2" 2>&1)"
  rc=$?
  set -e
}

must_pass() { # $1 = label, $2 = required stdout pattern
  if [ "$rc" -ne 0 ]; then bad "$1 (expected exit 0, got $rc)" "$out"; return; fi
  if ! grep -q -- "$2" <<<"$out"; then bad "$1 (exit 0 but did not say: $2)" "$out"; return; fi
  ok "$1"
}

must_fail() { # $1 = label, $2 = required reason pattern
  if [ "$rc" -eq 0 ]; then bad "$1 (expected non-zero, got 0)" "$out"; return; fi
  if ! grep -q -- "$2" <<<"$out"; then bad "$1 (failed, but not for the stated reason: $2)" "$out"; return; fi
  ok "$1"
}

echo "== check-engine-freshness fixture =="

# ---------------------------------------------------------------------------------------------
# 1. GREEN: the feed's newest version is tagged, and nothing has landed since.
# ---------------------------------------------------------------------------------------------
R="$WORK/clean"; make_repo "$R"
F="$WORK/feed-clean.json"; feed "$F" 0.1.0 0.2.0 0.3.0
run "$R" "$F"
must_pass "a tag with no commits after it is CLEAN" "no engine commits since coord-engine/v0.3.0"

# ---------------------------------------------------------------------------------------------
# 2. THE ACCEPTANCE CASE: commits after the tag, touching the wire surface => RED.
# ---------------------------------------------------------------------------------------------
R="$WORK/wire"; make_repo "$R"
touch_commit "$R" "src/FS.GG.Coord.Core/Protocol.fs" "protocol: a new take exit code"
F="$WORK/feed-wire.json"; feed "$F" 0.3.0
run "$R" "$F"
must_fail "a wire-surface commit after the tag is RED" "WIRE SURFACE has outrun the feed"
# The message must name the remedy, not merely the fault: a gate that reds without naming the fix is
# one the next worker routes around.
if grep -q 'push the matching coord-engine/v<version> tag' <<<"$out"; then
  ok "the RED names the remedy (bump + tag)"
else
  bad "the RED names the remedy (bump + tag)" "$out"
fi

# ---------------------------------------------------------------------------------------------
# 3. Drift that does NOT touch the wire surface: REPORTED, never red. This is the leg that keeps the
#    gate from being red-by-design between releases — the failure mode that gets a gate ignored.
# ---------------------------------------------------------------------------------------------
R="$WORK/internal"; make_repo "$R"
touch_commit "$R" "src/FS.GG.Coord.Cli/Client.fs" "cli: an internal refactor"
touch_commit "$R" "src/FS.GG.Coord.GitHub/Reads.fs" "github: another internal change"
F="$WORK/feed-internal.json"; feed "$F" 0.3.0
run "$R" "$F"
must_pass "internal-only drift is GREEN" "none touching the wire surface"
# ...but it must still be VISIBLE. "Below the bar" and "nothing here" must not render identically.
if grep -q 'cli: an internal refactor' <<<"$out" && grep -q '2 unreleased engine commit' <<<"$out"; then
  ok "internal-only drift is still REPORTED in full"
else
  bad "internal-only drift is still REPORTED in full" "$out"
fi

# ---------------------------------------------------------------------------------------------
# 4. A commit OUTSIDE the engine's source trees is not drift at all.
# ---------------------------------------------------------------------------------------------
R="$WORK/outside"; make_repo "$R"
touch_commit "$R" "README.md" "docs: unrelated to the engine"
F="$WORK/feed-outside.json"; feed "$F" 0.3.0
run "$R" "$F"
must_pass "a commit outside the engine trees is not drift" "no engine commits since"

# ---------------------------------------------------------------------------------------------
# 5. A TAG IS NOT A PUBLISH. The comparison point is the FEED's newest, never the newest tag: a tag
#    that was cut but never published must not be believed. (The fs-gg-ui-template PHANTOM 0.9.1
#    precedent: three tags cut, zero packages.) Here v0.4.0 is tagged but the feed still serves
#    0.3.0 — the gate must measure from v0.3.0 and SEE the drift, not from v0.4.0 and report clean.
# ---------------------------------------------------------------------------------------------
R="$WORK/phantom"; make_repo "$R"
touch_commit "$R" "src/FS.GG.Coord.Core/Protocol.fs" "protocol: shipped in the phantom tag"
git_ -C "$R" tag "coord-engine/v0.4.0"
F="$WORK/feed-phantom.json"; feed "$F" 0.3.0          # 0.4.0 tagged, never published
run "$R" "$F"
must_fail "a PHANTOM tag is not believed — drift is measured from the FEED" "since coord-engine/v0.3.0"

# ---------------------------------------------------------------------------------------------
# 6. FAIL CLOSED — the feed's newest version has no tag. "I cannot name the released commit" is an
#    ERROR, never "no drift".
# ---------------------------------------------------------------------------------------------
R="$WORK/notag"; make_repo "$R"
F="$WORK/feed-notag.json"; feed "$F" 0.9.9
run "$R" "$F"
must_fail "an untagged feed version is an ERROR, not 'current'" "has no tag"

# ---------------------------------------------------------------------------------------------
# 7. FAIL CLOSED — the feed has no such package / zero versions / only prereleases.
# ---------------------------------------------------------------------------------------------
R="$WORK/feedbad"; make_repo "$R"
printf '{"Some.Other.Package": ["1.0.0"]}' > "$WORK/feed-absent.json"
run "$R" "$WORK/feed-absent.json"
must_fail "a package absent from the feed is an ERROR" "not on the org feed"

printf '{"FS.GG.Coord.Cli": []}' > "$WORK/feed-empty.json"
run "$R" "$WORK/feed-empty.json"
must_fail "a feed serving zero versions is an ERROR" "zero versions"

F="$WORK/feed-pre.json"; feed "$F" 0.4.0-preview.1
run "$R" "$F"
must_fail "a feed with only prereleases is an ERROR" "no stable version"

run "$R" "$WORK/does-not-exist.json"
must_fail "an unreadable fixture is an ERROR" "cannot read fixture"

# ---------------------------------------------------------------------------------------------
# 8. FAIL CLOSED — a measured path has MOVED. A hard-coded path that silently measures nothing is
#    the exact fails-open shape this gate exists to refuse, so its absence must red.
# ---------------------------------------------------------------------------------------------
R="$WORK/moved"; make_repo "$R"
git_ -C "$R" rm -q "src/FS.GG.Coord.Core/Protocol.fs"
git_ -C "$R" commit -qm "protocol: moved elsewhere"
F="$WORK/feed-moved.json"; feed "$F" 0.3.0
run "$R" "$F"
must_fail "a wire-surface file that has moved is an ERROR" "does not exist at"

R="$WORK/notree"; make_repo "$R"
git_ -C "$R" rm -q -r "src/FS.GG.Coord.GitHub"
git_ -C "$R" commit -qm "github: tree removed"
F="$WORK/feed-notree.json"; feed "$F" 0.3.0
run "$R" "$F"
must_fail "a missing engine source tree is an ERROR" "does not exist at"

# ---------------------------------------------------------------------------------------------
# 9. THE FIXTURE HOOK IS LOCKED. A --fixture that works outside this harness is a way to turn the
#    gate into a no-op, which is the defect class above.
# ---------------------------------------------------------------------------------------------
R="$WORK/lock"; make_repo "$R"
F="$WORK/feed-lock.json"; feed "$F" 0.3.0
set +e
out="$(env -u FSGG_ENGINE_FIXTURE_OK python3 "$GATE" --repo "$R" --fixture "$F" 2>&1)"; rc=$?
set -e
must_fail "--fixture is REFUSED without the harness opt-in" "Refusing to run"

# The live path with no token must ERROR rather than skip.
set +e
out="$(env -u GITHUB_TOKEN -u GH_TOKEN python3 "$GATE" --repo "$R" 2>&1)"; rc=$?
set -e
must_fail "a missing token is an ERROR, not a skip" "not skip it"

# ---------------------------------------------------------------------------------------------
# 10. FAIL CLOSED — git itself unreadable.
# ---------------------------------------------------------------------------------------------
mkdir -p "$WORK/notgit"
F="$WORK/feed-notgit.json"; feed "$F" 0.3.0
run "$WORK/notgit" "$F"
must_fail "a non-repo is an ERROR" "failed"

echo
echo "engine-freshness fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || exit 1
