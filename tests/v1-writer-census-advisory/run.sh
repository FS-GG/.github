#!/usr/bin/env bash
# Fixture for scripts/check-v1-writer-census-advisory.py (Refs #4730, option 2).
#
# It runs the REAL checker against a throwaway clone of this repository and drives three PR shapes through
# it, each as a commit on top of a baseline whose census is first brought current (so the legs do not
# depend on whatever drift trunk happens to carry the day this runs):
#   1. a census-tracked file edited WITHOUT a rehash   -> exit 1, names the file, says how to rehash
#   2. the same edit WITH the row rehashed             -> exit 0, structural check ran and passed
#   3. a PR that touches nothing the census tracks     -> exit 0, "no-op", the structural check did NOT run
# plus the edges: the census file itself, a newly added executable, an unreadable census (3), a bad base (3).
# Every negative leg asserts the REASON, not just a non-zero exit (tests/feed-coherence/run.sh:10, #266).
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
TOOL_SRC="$ROOT/scripts/check-v1-writer-census-advisory.py"
WORK="$(mktemp -d "${TMPDIR:-/tmp}/census-advisory-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
export PYTHONDONTWRITEBYTECODE=1 GIT_AUTHOR_NAME=t GIT_AUTHOR_EMAIL=t@e.invalid GIT_COMMITTER_NAME=t GIT_COMMITTER_EMAIL=t@e.invalid

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

git clone -q --no-hardlinks --no-checkout "$ROOT" "$WORK/repo"
R="$WORK/repo"
git -C "$R" checkout -q --detach "$(git -C "$ROOT" rev-parse HEAD)"
# The working copy of the code under test, committed or not.
cp "$TOOL_SRC" "$R/scripts/check-v1-writer-census-advisory.py"
cp "$ROOT/scripts/check-v1-writer-census.py" "$R/scripts/check-v1-writer-census.py"

# rehash <root> — recompute every census row's sha256 from the file (the author's step, mechanised for the
# fixture only: it is how the baseline is made current, and what leg 2 does for the one edited row).
rehash() {
  python3 - "$1" "${2:-}" <<'PY'
import hashlib, json, sys
root, only = sys.argv[1], sys.argv[2]
p = f"{root}/docs/coordination/v1-writer-census.json"
t = open(p).read()
def walk(n):
    if isinstance(n, dict):
        if "path" in n and "sha256" in n: yield n
        for v in n.values(): yield from walk(v)
    elif isinstance(n, list):
        for v in n: yield from walk(v)
for r in walk(json.loads(t)):
    if only and r["path"] != only: continue
    h = hashlib.sha256(open(f"{root}/{r['path']}", "rb").read()).hexdigest()
    if h != r["sha256"]:
        assert t.count(r["sha256"]) == 1
        t = t.replace(r["sha256"], h)
open(p, "w").write(t)
PY
}
commit() { git -C "$R" add -A && git -C "$R" commit -q -m "$1"; }
advise() { (cd "$R" && python3 scripts/check-v1-writer-census-advisory.py --base HEAD~1 --head HEAD 2>&1) || return $?; }
expect() {  # <name> <want-rc> <needle> <must-not-contain-or-empty>
  local name="$1" want="$2" needle="$3" never="${4:-}" out rc=0
  out="$(advise)" || rc=$?
  if [ "$rc" -ne "$want" ]; then bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF -- "$needle" <<<"$out"; then bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  elif [ -n "$never" ] && grep -qF -- "$never" <<<"$out"; then bad "$name (output must not contain '$never')" "$out"
  else ok "$name"; fi
}

# Baseline: the census made current, committed.
rehash "$R"; commit "baseline: census current"
(cd "$R" && python3 scripts/check-v1-writer-census.py --structural >/dev/null 2>&1) \
  && ok "baseline: the structural check passes on the prepared clone (the legs below start from green)" \
  || bad "baseline census is not green; the legs below would prove nothing"

# 1. A census-tracked file edited without a rehash.
printf '\n# an edit nobody rehashed\n' >> "$R/.github/workflows/coord-engine.yml"; commit "edit tracked file"
expect "tracked file edited WITHOUT a rehash is flagged" 1 "rehash needed" ""
expect "the flag names the file and that it is census-tracked" 1 ".github/workflows/coord-engine.yml (census-tracked)" ""
expect "the flag says what to review before rehashing" 1 "Review the file's diff for a new sink, writer or permission" ""

# 2. The same edit, row rehashed.
rehash "$R" ".github/workflows/coord-engine.yml"; commit "rehash the row"
expect "the rehashed PR passes, and the structural check really ran" 0 "the structural check passes with this PR's changes" ""

# 3. A PR that touches nothing the census tracks.
printf '\nA harmless note.\n' >> "$R/README.md"; commit "touch an untracked file"
expect "an untouched PR is a no-op and does NOT run the structural check" 0 "no-op" "PASS (structural"
# ... even when the census is red, because the advisory never judges what the PR did not touch.
printf '\n# drift on trunk\n' >> "$R/.github/workflows/coord-engine.yml"; git -C "$R" commit -q -am "drift lands elsewhere"
printf '\nAnother harmless note.\n' >> "$R/README.md"; commit "touch an untracked file again"
expect "an untouched PR stays a no-op while the census is red elsewhere (it judges only what the PR touched)" 0 "no-op" "REFUSED"

# Edges.
rehash "$R"; commit "re-green"
printf '\n' >> "$R/docs/coordination/v1-writer-census.json"; commit "touch the census file itself"
expect "editing the census file itself runs the check" 0 "(the census or its checker)" ""
printf '#!/usr/bin/env bash\ntrue\n' > "$R/scripts/zz-new-tool"; chmod +x "$R/scripts/zz-new-tool"; commit "add an executable"
out="$(advise)" || true
grep -qF "scripts/zz-new-tool (executable" <<<"$out" && ok "adding an executable runs the check (the structural check counts the executable population)" || bad "a new executable must trigger the check" "$out"
mkdir -p "$WORK/bare"; (cd "$WORK/bare" && git init -q && git commit -q --allow-empty -m a && git commit -q --allow-empty -m b)
rc=0; out="$(cd "$WORK/bare" && python3 "$R/scripts/check-v1-writer-census-advisory.py" --root "$WORK/bare" --base HEAD~1 --head HEAD 2>&1)" || rc=$?
[ "$rc" -eq 3 ] && grep -q "no verdict" <<<"$out" && ok "an unreadable census is exit 3 (no verdict), never a no-op" || bad "unreadable census must be exit 3" "rc=$rc $out"
rc=0; out="$(cd "$R" && python3 scripts/check-v1-writer-census-advisory.py --base does-not-exist --head HEAD 2>&1)" || rc=$?
[ "$rc" -eq 3 ] && grep -q "no verdict" <<<"$out" && ok "an unreadable change set is exit 3 (no verdict)" || bad "unreadable change set must be exit 3" "rc=$rc $out"

echo; echo "census-advisory fixture: passed=$pass failed=$failcount"
[ "$failcount" -eq 0 ]
