#!/usr/bin/env bash
# Fixture for scripts/check-generator-list.py and scripts/generated-paths (.github#498, ADR-0044).
#
# Offline and credential-free: every subject reads the working tree and nothing else.
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap and this fixture is squarely in it: the gate has TWO no-verdict conditions (no roster
# tool, empty roster) that both exit 2, and a leg that only checked the code could not tell them apart
# — nor tell either from a gate that crashed on an unrelated TypeError.
#
# §4 IS THE ONE THAT MATTERS, and it is why this file is not just a gate fixture. #309's rule has two
# conditions — an artifact is generated AND CI-gated — and only the second makes a collision a rebase
# rather than a decision. The gate cannot check the second: deciding it by reading workflow `run:` text
# is unsound and this repo has measured it (check-paths-coherence.py — three hits against this repo,
# all three false, all three YAML comments; #683). So it is proven HERE, the only sound way there is:
# dirty the artifact, run its declared guard, and assert the guard goes red. If a guard ever stops
# reding on drift, its artifact stops being subtractable, and this leg is the only thing that would say
# so before `verify-paths` started silently swallowing real findings.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-generator-list.py"
PATHS_TOOL="$REPO_ROOT/scripts/generated-paths"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/generator-list-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# expect <name> <want-rc> <needle> <root> — the rc AND the reason must both match.
expect() {
  local name="$1" want="$2" needle="$3" root="$4"
  local out rc=0
  out="$(python3 "$GATE" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

# root <dir> — a synthetic tree with a scripts/ dir and a roster tool that lists <invocations>.
root() {
  local d="$1"; shift
  mkdir -p "$d/scripts"
  { echo '#!/usr/bin/env bash'
    echo 'case "${1:-}" in --roster) ;; *) exit 2 ;; esac'
    local inv
    for inv in "$@"; do printf 'echo %q\n' "$inv"; done
  } > "$d/scripts/generated-paths"
  chmod +x "$d/scripts/generated-paths"
  echo "$d"
}

# gen <root> <name> <body…> — a fake generator answering --list with the given stdout lines.
gen() {
  local d="$1" name="$2"; shift 2
  { echo '#!/usr/bin/env bash'
    local ln
    for ln in "$@"; do printf '%s\n' "$ln"; done
  } > "$d/scripts/$name"
  chmod +x "$d/scripts/$name"
}

echo "== §1  no verdict (exit 2) — the gate must never pass over nothing"

R="$WORK/no-tool"; mkdir -p "$R"
expect "no roster tool at all is NO VERDICT, never a pass" 2 "nothing to check, and that is not a pass" "$R"

R="$(root "$WORK/empty")"
expect "an EMPTY roster is NO VERDICT — every check below would pass over nothing (#266)" \
  2 "the roster is EMPTY" "$R"

echo
echo "== §2  a generator that cannot be asked (exit 1)"

R="$(root "$WORK/rc" "scripts/gen-a --list")"
gen "$R" "gen-a" 'exit 3'
expect "a generator whose --list EXITS NON-ZERO is a violation, and the rc is named" \
  1 "exited 3" "$R"

R="$(root "$WORK/silent" "scripts/gen-a --list")"
gen "$R" "gen-a" 'exit 0'
expect "a generator that lists NOTHING is a violation — 'I emit nothing' is not 'I could not say'" \
  1 "listed NOTHING" "$R"

echo
echo "== §3  a generator that answers badly (exit 1)"

R="$(root "$WORK/fields" "scripts/gen-a --list")"
gen "$R" "gen-a" 'printf "kind\tonly-two-fields\n"'
expect "a row that is not exactly 3 tab fields is refused" \
  1 "want exactly 3" "$R"

R="$(root "$WORK/missing" "scripts/gen-a --list")"
gen "$R" "gen-a" 'printf "kind\tno/such/file.txt\t\n"'
expect "a generator naming a path that DOES NOT EXIST is refused" \
  1 "which does not exist" "$R"

R="$(root "$WORK/abs" "scripts/gen-a --list")"
gen "$R" "gen-a" 'printf "kind\t/etc/passwd\t\n"'
expect "an ABSOLUTE path is refused — it could never match a PR's repo-relative file list" \
  1 "is not repo-relative" "$R"

R="$(root "$WORK/esc" "scripts/gen-a --list")"
gen "$R" "gen-a" 'printf "kind\t../outside.txt\t\n"'
expect "an ESCAPING path is refused" 1 "is not repo-relative" "$R"

echo
echo "== §4  the roster is the one hand-kept thing — so it is watched"

R="$(root "$WORK/forgot" "scripts/gen-a --list")"
gen "$R" "gen-a" 'printf "kind\tscripts/gen-a\t\n"'
gen "$R" "generate-orphan" 'printf "kind\tscripts/generate-orphan\t\n"'
expect "a scripts/generate-* that is NOT rostered is a violation (the forgotten-generator net)" \
  1 "looks like a generator and is NOT in" "$R"

R="$(root "$WORK/double" "scripts/gen-a --list" "scripts/gen-b --list")"
gen "$R" "gen-a" 'printf "kind\tshared.md\t\n"'
gen "$R" "gen-b" 'printf "kind\tshared.md\t<!-- BEGIN -->\n"'
touch "$R/shared.md"
expect "one path claimed as BOTH whole-file and region is refused — subtracting it would suppress real drift" \
  1 "suppress drift on a file somebody authors" "$R"

echo
echo "== §5  it can say YES — a rule only ever exercised on violations is one nobody can trust"

R="$(root "$WORK/good" "scripts/gen-a --list")"
gen "$R" "gen-a" 'printf "kind\tartifact.txt\t\n"'
touch "$R/artifact.txt"
expect "a well-formed roster PASSES" 0 "1 subtractable artifact(s)" "$R"

echo
echo "== §6  REGRESSION — the real repo, as this PR ships it"

expect "the REAL repo passes its own convention" 0 "subtractable artifact(s)" "$REPO_ROOT"

echo
echo "== §7  generated-paths: the subtraction set is the WHOLE-FILE rows only"

subs="$("$PATHS_TOOL")"
allrows="$("$PATHS_TOOL" --all)"

# The load-bearing distinction. A file with a generated REGION is prose somebody authored, so #309's
# rule never applied to it and drift on it is a TRUE finding. If this ever inverts, verify-paths would
# stop reporting overruns across the org's protocol documents — a far bigger hole than the noise #498
# is about.
if grep -q 'SKILL.md' <<<"$subs"; then
  bad "§7 a region-generated SKILL.md must NEVER be subtractable" "$subs"
else
  ok "§7 a region-generated file (SKILL.md) is NOT subtracted — it is authored"
fi

if grep -q 'docs/coordination/parallel-work.md' <<<"$subs"; then
  bad "§7 parallel-work.md is authored prose with one generated region — it must not be subtracted" "$subs"
else
  ok "§7 parallel-work.md is NOT subtracted — authored prose, one generated region"
fi

if grep -qx 'registry/repos.lock' <<<"$subs" && grep -qx 'dist/skill-union-assert.sh' <<<"$subs"; then
  ok "§7 both whole-file artifacts ARE subtracted"
else
  bad "§7 the whole-file artifacts must be subtracted" "$subs"
fi

# Every subtractable path must appear in --all with an empty marker: the two views cannot disagree.
while IFS= read -r p; do
  [ -n "$p" ] || continue
  if grep -qP "^[^\t]*\t\Q$p\E\t$" <<<"$allrows"; then
    ok "§7 $p is an empty-marker row in --all"
  else
    bad "§7 $p is subtracted but is not an empty-marker row in --all" "$allrows"
  fi
done <<<"$subs"

echo
echo "== §8  --list is PURE — asking a generator must never mutate the tree"

# This is not hygiene. `generate-skill-union-bundle` parsed args as `[ "${1:-}" = "--check" ]`, so
# EVERY unknown argument fell through to the write: asking it a question regenerated the bundle. The
# convention's first act was to walk into that.
before="$(cd "$REPO_ROOT" && git status --porcelain)"
"$PATHS_TOOL" >/dev/null
"$PATHS_TOOL" --all >/dev/null
"$PATHS_TOOL" --roster >/dev/null
after="$(cd "$REPO_ROOT" && git status --porcelain)"
if [ "$before" = "$after" ]; then
  ok "§8 asking every generator --list mutates nothing"
else
  bad "§8 asking --list CHANGED the tree" "$(diff <(echo "$before") <(echo "$after") || true)"
fi

echo
echo "== §9  an unknown argument is REFUSED by every generator, not fallen through to a write"

while IFS= read -r inv; do
  [ -n "$inv" ] || continue
  script="${inv%% *}"
  rc=0
  (cd "$REPO_ROOT" && ${inv% --list} --fsgg-not-an-arg >/dev/null 2>&1) || rc=$?
  if [ "$rc" -ne 0 ]; then
    ok "§9 $script refuses an unknown argument (exit $rc)"
  else
    bad "§9 $script ACCEPTED an unknown argument — a question could reach its write path"
  fi
done < <("$PATHS_TOOL" --roster)

echo
echo "== §10 GATED — the sound half of #309's two-condition test, proven by EXECUTION"

# guard <artifact> <guard-command…> — dirty the artifact, run its guard, assert RED, restore.
# Proven by running the guard, never by grepping a workflow for a mention of it (#683).
guard() {
  local artifact="$1"; shift
  local f="$REPO_ROOT/$artifact" rc=0
  if [ ! -f "$f" ]; then bad "§10 $artifact does not exist"; return; fi

  cp "$f" "$WORK/restore.bak"
  # Restore on ANY exit path: a fixture that strands a dirtied artifact would hand the next reader a
  # tree that lies. NOT `git stash` — the stash stack is SHARED across worktrees.
  # shellcheck disable=SC2064
  trap "cp '$WORK/restore.bak' '$f' 2>/dev/null || true; rm -rf '$WORK'" EXIT

  printf '\n# fsgg: drift injected by tests/generator-list/run.sh\n' >> "$f"
  (cd "$REPO_ROOT" && "$@" >/dev/null 2>&1) || rc=$?
  cp "$WORK/restore.bak" "$f"
  trap 'rm -rf "$WORK"' EXIT

  if [ "$rc" -ne 0 ]; then
    ok "§10 $artifact IS CI-gated — '$*' reds on drift (exit $rc), so a collision in it is a rebase"
  else
    bad "§10 $artifact is subtracted but '$*' does NOT red on drift — it is not gated, and subtracting it would swallow real findings"
  fi
}

guard "registry/repos.lock" scripts/repos.sh validate
guard "dist/skill-union-assert.sh" scripts/generate-skill-union-bundle --check

echo
echo "== §11 FAIL CLOSED — a generator that cannot answer subtracts NOTHING"

# The #266 condition aimed at this design. An absent, broken or silent generator must leave drift
# reported exactly as it is today. The opposite — reading "I could not say" as "nothing is generated",
# or worse as "everything is" — is the fail-open that disqualified #498's own ignore-file option.
# A real tree, with ONE rostered generator swapped for a name that does not exist — so the leg proves
# the surviving generators still answer, rather than merely proving an empty tree is empty.
BROKEN="$WORK/broken"
mkdir -p "$BROKEN"
cp -r "$REPO_ROOT/scripts" "$REPO_ROOT/registry" "$BROKEN/"
sed -i 's#^  "scripts/generate-projections --list"#  "scripts/nonexistent-generator --list"#' \
  "$BROKEN/scripts/generated-paths"
rc=0
out="$("$BROKEN/scripts/generated-paths" 2>"$WORK/err")" || rc=$?
if grep -q 'NOT subtracted' "$WORK/err"; then
  ok "§11 a generator that cannot be run subtracts nothing, and says so"
else
  bad "§11 a broken generator must warn that its artifacts are NOT subtracted" "$(cat "$WORK/err")"
fi
if [ -n "$out" ] && ! grep -q 'nonexistent' <<<"$out"; then
  ok "§11 the surviving generators still answer — one broken generator is not a broken repo"
else
  bad "§11 the other generators should still have answered" "$out"
fi

echo
echo "-------- $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
