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
rehash "$R"; commit "re-green"; git -C "$R" tag -f green >/dev/null
printf '\n' >> "$R/docs/coordination/v1-writer-census.json"; commit "touch the census file itself"
expect "editing the census file itself runs the check" 0 "(the census or its checker)" ""
mkdir -p "$R/notes"; printf '#!/usr/bin/env bash\ntrue\n' > "$R/notes/zz-new-tool"; chmod +x "$R/notes/zz-new-tool"; commit "add an executable outside every population prefix"
out="$(advise)" || true
grep -qF "notes/zz-new-tool (executable" <<<"$out" && ok "adding an executable runs the check (the structural check counts the executable population)" || bad "a new executable must trigger the check" "$out"
SINK="git pu""sh origin HEAD:refs/heads/zz"   # spelled in two pieces: this fixture must not be a census writer source itself
# --- Population: a NEW non-executable writer is drift even though no census row names it yet. -------
git -C "$R" reset -q --hard green
mkdir -p "$R/.github/workflows"
printf 'name: zz-new\non: workflow_dispatch\njobs:\n  j:\n    runs-on: ubuntu-latest\n    steps:\n      - run: %s\n' "$SINK" > "$R/.github/workflows/zz-new-writer.yml"
commit "add a NON-executable workflow carrying a publish command"
[ "$(git -C "$R" ls-tree HEAD .github/workflows/zz-new-writer.yml | cut -d' ' -f1)" = "100644" ] \
  && ok "(setup) the new workflow really is mode 100644" || bad "(setup) the new workflow must be 100644"
expect "a new 100644 workflow with a sink is touching, and the structural check refuses it" 1 "in the structural check's population" ""
out="$(advise)" || true
grep -q "REFUSED" <<<"$out" && ok "... and the refusal comes from the structural check, not from the advisory's own opinion" || bad "the structural check must have refused the new writer" "$out"
git -C "$R" reset -q --hard green
printf '#!/usr/bin/env bash\n%s\n' "$SINK" > "$R/scripts/zz-new-writer"
commit "add a NON-executable script under scripts/ carrying a publish command"
expect "a new 100644 script under scripts/ is touching" 1 "scripts/zz-new-writer (in the structural check's population" ""
# BUILD_DECLARATIONS is imported from the checker, not copied. Every real declaration is also a census row (so
# the "census-tracked" reason wins), hence this leg drives touching() directly with a declaration set only the
# imported name could supply, and shows a prefix-less, row-less path is touching because it is DECLARED.
out="$(cd "$R" && python3 - <<'PY'
import importlib.util
spec = importlib.util.spec_from_file_location("adv", "scripts/check-v1-writer-census-advisory.py")
adv = importlib.util.module_from_spec(spec); spec.loader.exec_module(adv)
prefixes, declarations = adv.population_rules(__import__("pathlib").Path("."))
real = sorted(declarations)
print("imported:", len(prefixes), "prefixes,", len(real), "declarations")
print("declared:", adv.touching(set(), [("Zz.Declared.props", "M", "100644")], (), {"Zz.Declared.props"}))
print("not declared:", adv.touching(set(), [("Zz.Other.props", "M", "100644")], (), {"Zz.Declared.props"}))
print("real prefixes cover workflows:", adv.touching(set(), [(".github/workflows/zz.yml", "A", "100644")], prefixes, declarations) != [])
PY
)"
grep -q "^declared: .*Zz.Declared.props (in the structural" <<<"$out" && grep -q "not declared: \[\]" <<<"$out" && grep -q "real prefixes cover workflows: True" <<<"$out" \
  && ok "BUILD_DECLARATIONS and POPULATION_PREFIXES are imported and each decides touching" || bad "population rules must come from the checker" "$out"

# --- Renames, deletions, and the default base. -------------------------------------------------------
git -C "$R" reset -q --hard green
mkdir -p "$R/notes"; printf '#!/usr/bin/env bash\ntrue\n' > "$R/notes/tool.sh"; commit "park a file outside every prefix"
git -C "$R" tag -f parked >/dev/null
git -C "$R" mv notes/tool.sh scripts/tool.sh; commit "rename it INTO the population"
out="$(advise)" || true
grep -qF "scripts/tool.sh (in the structural check's population" <<<"$out" \
  && ok "a rename into the population is judged on its NEW path (a rename-detecting diff would read the old one and no-op)" \
  || bad "a rename must be read as delete + add" "$out"
git -C "$R" reset -q --hard green
git -C "$R" rm -q .github/workflows/coord-engine.yml; commit "delete a census-tracked file"
out="$(advise)" || true
grep -qF ".github/workflows/coord-engine.yml (deleted, census-tracked)" <<<"$out" \
  && ok "deleting a census-tracked file is touching (its row would dangle)" || bad "a tracked deletion must trigger" "$out"
git -C "$R" reset -q --hard green
git -C "$R" rm -q README.md; commit "delete an untracked file outside the population"
expect "deleting an untracked file outside the population is a no-op" 0 "no-op" "REFUSED"
git -C "$R" reset -q --hard green
printf 'inert\n' > "$R/scripts/zz-ephemeral.txt"; commit "add an untracked file INSIDE the population"
git -C "$R" rm -q scripts/zz-ephemeral.txt; commit "delete it again"
expect "deleting an untracked file INSIDE the population leaves no row dangling: no-op" 0 "no-op" "REFUSED"
git -C "$R" reset -q --hard green
printf '\n# default-base edit\n' >> "$R/.github/workflows/coord-engine.yml"; commit "edit a tracked file"
out="$( (cd "$R" && python3 scripts/check-v1-writer-census-advisory.py) 2>&1 )" || rc=$?
grep -q "rehash needed" <<<"$out" && ok "called with NO --base/--head the defaults (HEAD^1..HEAD) see the PR's change" || bad "the default base must be HEAD^1" "$out"
out="$( (cd "$R" && python3 scripts/check-v1-writer-census-advisory.py --base HEAD --head HEAD) 2>&1 )" || true
grep -q "no-op" <<<"$out" && ok "... and an empty range (base == head) is a no-op, so the default really is what judged the leg above" || bad "empty range must be a no-op" "$out"

mkdir -p "$WORK/bare"; (cd "$WORK/bare" && git init -q && git commit -q --allow-empty -m a && git commit -q --allow-empty -m b)
rc=0; out="$(cd "$WORK/bare" && python3 "$R/scripts/check-v1-writer-census-advisory.py" --root "$WORK/bare" --base HEAD~1 --head HEAD 2>&1)" || rc=$?
[ "$rc" -eq 3 ] && grep -q "no verdict" <<<"$out" && ok "an unreadable census is exit 3 (no verdict), never a no-op" || bad "unreadable census must be exit 3" "rc=$rc $out"
rc=0; out="$(cd "$R" && python3 scripts/check-v1-writer-census-advisory.py --base does-not-exist --head HEAD 2>&1)" || rc=$?
[ "$rc" -eq 3 ] && grep -q "no verdict" <<<"$out" && ok "an unreadable change set is exit 3 (no verdict)" || bad "unreadable change set must be exit 3" "rc=$rc $out"

echo; echo "census-advisory fixture: passed=$pass failed=$failcount"
[ "$failcount" -eq 0 ]
