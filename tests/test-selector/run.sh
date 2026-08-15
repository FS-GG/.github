#!/usr/bin/env bash
# Fixture for scripts/test — the suite selector (.github#860, epic #266).
#
# Offline and hermetic. The selector is static apart from `git`: it reads workflows and a diff, and
# touches no API, no network, no credentials. So the synthetic roots here are real git repos, and the
# selector drives them exactly as it drives this one — there is no transport to fake.
#
# WHAT IS ACTUALLY UNDER TEST, and it is not "does it list some suites". The selector's whole claim is
# that it DERIVES the change → suite mapping from the workflows' `paths:` filters rather than keeping
# a second copy that drifts (#520, #587/#599, #710, #724). That claim has exactly two failure modes:
#
#   1. It selects too FEW — and then it reports green having run the wrong suites. This is the one
#      that matters, because it is silent. Every refusal leg below exists to make an unreadable
#      filter LOUD rather than let it under-select.
#   2. It selects too MANY — merely slow, and self-evident.
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: the #266 vacuous-failure defect was a "must fail" test whose non-zero exit came from
# a path guard rather than from the thing under test — it would have passed against a gate broken in a
# completely different way. Here that trap is wide open, because exit 3 is also what you get from a
# typo in a fixture path.
#
# THE HEADLINE LEG is `negation is refused in EVERY mode and at ANY position`. The first cut of the
# selector compiled patterns lazily, inside the selection loop — which stops at the first MATCH. So an
# unreadable pattern sitting after a matching one was never looked at, and `--all` (which never
# selects) never looked at any pattern at all. The refusal existed and could not fire: a check that
# only sometimes runs is not a check. These legs pin it in both modes and in trailing position.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/test"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/test-selector-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM=/dev/null

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# expect <name> <want-rc> <needle> <root> [args…] — the rc AND the reason must both match.
expect() {
  local name="$1" want="$2" needle="$3" root="$4"; shift 4
  local out rc=0
  out="$(python3 "$TOOL" --root "$root" "$@" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

# selects <name> <root> <yes|no> <needle> [args…] — is <needle> among the selected suites?
selects() {
  local name="$1" root="$2" want="$3" needle="$4"; shift 4
  local out rc=0
  out="$(python3 "$TOOL" --root "$root" --list --base HEAD "$@" 2>&1)" || rc=$?
  if [ "$rc" -ne 0 ]; then bad "$name (exit $rc)" "$out"; return; fi
  if grep -qF "$needle" <<<"$out"; then
    [ "$want" = yes ] && ok "$name" || bad "$name (selected '$needle', should NOT have)" "$out"
  else
    [ "$want" = no ] && ok "$name" || bad "$name (did NOT select '$needle')" "$out"
  fi
}

# root <name> — a synthetic working tree that is a real git repo.
root() {
  local d="$WORK/$1"
  mkdir -p "$d/.github/workflows"
  git -c init.defaultBranch=main init -q "$d"
  git -C "$d" config user.email f@x; git -C "$d" config user.name f
  echo "$d"
}
# suite <root> <path> — a suite script on disk, so the on-disk check is satisfied.
suite() { mkdir -p "$(dirname "$1/$2")"; printf '#!/usr/bin/env bash\ntrue\n' > "$1/$2"; }
# seal <root> — commit everything, so a later edit is the only diff vs HEAD. An empty root has
# nothing to commit; that is a legitimate fixture state (the zero-workflows leg), not an error.
seal() { git -C "$1" add -A; git -C "$1" -c commit.gpgsign=false commit -qm x >/dev/null 2>&1 || true; }
# touchf <root> <path> — an edit the selector should see.
touchf() { printf '\n# edit\n' >> "$1/$2"; }

echo "── derivation: which trigger gates a PR"

# A workflow with a paths: filter selects only on a match.
r="$(root paths)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths: ["src/**"]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/alpha/run.sh" }] } }
EOF
suite "$r" tests/alpha/run.sh; mkdir -p "$r/src" "$r/docs"
echo x > "$r/src/a.fs"; echo x > "$r/docs/a.md"; seal "$r"
touchf "$r" src/a.fs
selects "a matching change selects the workflow's suite" "$r" yes "tests/alpha/run.sh"
git -C "$r" checkout -- src/a.fs
touchf "$r" docs/a.md
selects "a non-matching change does not" "$r" no "tests/alpha/run.sh"
git -C "$r" checkout -- docs/a.md

# `pull_request:` NULL — the coherence.yml shape. This is the load-bearing distinction: a null value
# means EVERY PR, not "no PR trigger". Read it backwards and the always-on gates vanish silently.
r="$(root nullpr)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
  push:
    branches: [main]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/always/run.sh" }] } }
EOF
suite "$r" tests/always/run.sh; echo x > "$r/unrelated.txt"; seal "$r"
touchf "$r" unrelated.txt
selects "'pull_request:' with a NULL value gates every PR" "$r" yes "tests/always/run.sh"

# `pull_request: {types: […]}` with no paths — the closing-keywords.yml shape. Also every PR.
r="$(root typespr)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    types: [opened, edited]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/always/run.sh" }] } }
EOF
suite "$r" tests/always/run.sh; echo x > "$r/unrelated.txt"; seal "$r"
touchf "$r" unrelated.txt
selects "a pull_request carrying only types: gates every PR" "$r" yes "tests/always/run.sh"

# `on:` has three legal spellings and all of them must be read. The mapping form is the only one
# this repo uses today, which is exactly why the other two are worth pinning: reading only the
# mapping form did not REFUSE a list or a string, it silently concluded the workflow gated nothing
# and dropped its suites. Under-selection, arrived at by being fussy about globs and credulous about
# triggers — and invisible, because the output is a shorter list that looks just as plausible.
r="$(root onlist)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: [push, pull_request]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/listy/run.sh" }] } }
EOF
suite "$r" tests/listy/run.sh; echo x > "$r/f.txt"; seal "$r"; touchf "$r" f.txt
selects "'on: [push, pull_request]' gates every PR" "$r" yes "tests/listy/run.sh"

r="$(root onstr)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: pull_request
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/stringy/run.sh" }] } }
EOF
suite "$r" tests/stringy/run.sh; echo x > "$r/f.txt"; seal "$r"; touchf "$r" f.txt
selects "'on: pull_request' as a bare string gates every PR" "$r" yes "tests/stringy/run.sh"

# ...and the mirror: a list form that does NOT name pull_request still gates no PR.
r="$(root onlistpush)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: [push]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/nope/run.sh" }] } }
EOF
suite "$r" tests/nope/run.sh; echo x > "$r/f.txt"; seal "$r"; touchf "$r" f.txt
selects "'on: [push]' gates no PR" "$r" no "tests/nope/run.sh"

# An `on:` that is none of the three spellings is refused, not guessed at.
r="$(root onbogus)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: 5
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/x/run.sh" }] } }
EOF
suite "$r" tests/x/run.sh; seal "$r"
expect "an unreadable 'on:' is no verdict, not a silent zero" 3 "cannot tell what triggers" "$r" --list --all

# A push-only workflow does not judge a PR, so it selects nothing.
r="$(root pushonly)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  push:
    paths: ["src/**"]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/pushed/run.sh" }] } }
EOF
suite "$r" tests/pushed/run.sh; mkdir -p "$r/src"; echo x > "$r/src/a.fs"; seal "$r"
touchf "$r" src/a.fs
selects "a push-only workflow does not gate a PR" "$r" no "tests/pushed/run.sh"
selects "...but --all runs its suite anyway" "$r" yes "tests/pushed/run.sh" --all

echo
echo "── glob semantics: GitHub's, not the shell's"

r="$(root glob)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths: ["deep/**", "flat/*", "exact.txt"]
jobs:
  a: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/deep/run.sh" }] }
EOF
suite "$r" tests/deep/run.sh
mkdir -p "$r/deep/a/b" "$r/flat/sub"
echo x > "$r/deep/a/b/c.txt"; echo x > "$r/flat/one.txt"; echo x > "$r/flat/sub/two.txt"
echo x > "$r/exact.txt"; echo x > "$r/exact.txt.bak"; seal "$r"

touchf "$r" deep/a/b/c.txt
selects "'deep/**' crosses a slash" "$r" yes "tests/deep/run.sh"
git -C "$r" checkout -- deep/a/b/c.txt

touchf "$r" flat/one.txt
selects "'flat/*' matches one segment" "$r" yes "tests/deep/run.sh"
git -C "$r" checkout -- flat/one.txt

touchf "$r" flat/sub/two.txt
selects "'flat/*' does NOT cross a slash" "$r" no "tests/deep/run.sh"
git -C "$r" checkout -- flat/sub/two.txt

touchf "$r" exact.txt
selects "an exact path matches itself" "$r" yes "tests/deep/run.sh"
git -C "$r" checkout -- exact.txt

touchf "$r" exact.txt.bak
selects "an exact path is anchored — it does not match a longer name" "$r" no "tests/deep/run.sh"
git -C "$r" checkout -- exact.txt.bak

echo
echo "── the shapes this repo actually has"

# A reusable callee's suites belong to its caller's filter. coherence.yml -> contract-coherence.yml
# is the live edge; the callee runs no suite today, so without this leg the resolution is untested.
r="$(root reusable)"
cat > "$r/.github/workflows/caller.yml" <<'EOF'
name: caller
on:
  pull_request:
    paths: ["src/**"]
jobs: { c: { uses: ./.github/workflows/callee.yml } }
EOF
cat > "$r/.github/workflows/callee.yml" <<'EOF'
name: callee
on: { workflow_call: }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/reused/run.sh" }] } }
EOF
suite "$r" tests/reused/run.sh; mkdir -p "$r/src"; echo x > "$r/src/a.fs"; seal "$r"
touchf "$r" src/a.fs
selects "a reusable callee's suite is attributed to the caller's filter" "$r" yes "tests/reused/run.sh"

# A workflow_call-only workflow gates no PR of its own.
r="$(root callonly)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { workflow_call: }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/lonely/run.sh" }] } }
EOF
suite "$r" tests/lonely/run.sh; echo x > "$r/f.txt"; seal "$r"; touchf "$r" f.txt
selects "a workflow_call-only workflow gates no PR" "$r" no "tests/lonely/run.sh"

# One suite named by two workflows runs ONCE. The real tree does this: coord-engine.yml invokes the
# same project twice (the test step and the non-vacuity re-run).
r="$(root dedupe)"
for n in one two; do cat > "$r/.github/workflows/$n.yml" <<EOF
name: $n
on:
  pull_request:
    paths: ["src/**"]
jobs:
  j:
    runs-on: ubuntu-latest
    steps:
      - { run: "bash tests/same/run.sh" }
      - { run: "bash tests/same/run.sh --no-build" }
EOF
done
suite "$r" tests/same/run.sh; mkdir -p "$r/src"; echo x > "$r/src/a.fs"; seal "$r"
touchf "$r" src/a.fs
n="$(python3 "$TOOL" --root "$r" --list --base HEAD | grep -c 'bash tests/same/run.sh' || true)"
[ "$n" -eq 1 ] && ok "a suite named by two workflows is listed once" \
               || bad "a suite named by two workflows is listed once (got $n)"

# A consolidated policy runner is an indirection, not an excuse for the selector to lose its suites.
# Expand the same neutral inventory, including the optional self_test the production runner executes.
r="$(root policy-runner)"
mkdir -p "$r/policy"
cat > "$r/.github/workflows/policy.yml" <<'EOF'
name: policy
on: { pull_request: { paths: ["src/**"] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "python3 scripts/policy-runner.py run all" }] } }
EOF
cat > "$r/policy/subjects.json" <<'EOF'
{"schema_version":1,"subjects":[{"id":"both","self_test":["bash","tests/policy-self/run.sh"],"command":["bash","tests/policy-command/run.sh"]}]}
EOF
suite "$r" tests/policy-self/run.sh; suite "$r" tests/policy-command/run.sh
mkdir -p "$r/src"; echo x > "$r/src/a.fs"; seal "$r"; touchf "$r" src/a.fs
selects "policy-runner expansion includes a subject command" "$r" yes "tests/policy-command/run.sh"
selects "policy-runner expansion includes its optional self_test" "$r" yes "tests/policy-self/run.sh"

printf '{broken' > "$r/policy/subjects.json"
expect "an unreadable policy inventory is no verdict" 3 "unreadable" "$r" --list --all
printf '{"schema_version":1,"subjects":[]}' > "$r/policy/subjects.json"
expect "an empty policy inventory is no verdict" 3 "non-empty schema-v1" "$r" --list --all
printf '{"schema_version":1,"subjects":[{"id":"bad","command":[]}]}' > "$r/policy/subjects.json"
expect "a malformed policy subject is no verdict" 3 "invalid command" "$r" --list --all

echo
echo "── fails closed (#266): 'I could not tell' is never spelled like 'nothing to run'"

# THE HEADLINE. Negation must be refused in EVERY mode and at ANY position. The trailing position is
# the one the first cut let through: selection stops at the first match, so nothing ever read it.
r="$(root negate)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths: ["src/**", "!src/vendor/**"]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/x/run.sh" }] } }
EOF
suite "$r" tests/x/run.sh; mkdir -p "$r/src"; echo x > "$r/src/a.fs"; seal "$r"; touchf "$r" src/a.fs
expect "a negated pattern is refused (selecting)" 3 "does not implement" "$r" --list --base HEAD
expect "a negated pattern is refused (--all, which never selects)" 3 "does not implement" "$r" --list --all
expect "a negated pattern is refused when it TRAILS a matching one" 3 "!src/vendor/**" "$r" --list --base HEAD

# paths-ignore on pull_request INVERTS selection — refuse.
r="$(root ignore)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths-ignore: ["docs/**"]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/x/run.sh" }] } }
EOF
suite "$r" tests/x/run.sh; seal "$r"
expect "pull_request paths-ignore is refused" 3 "INVERTS selection" "$r" --list --all

# ...but a push-only paths-ignore cannot change what a PR runs. Refusing it would be a FALSE alarm
# that breaks the tool for everyone — the opposite failure, and just as real.
r="$(root ignorepush)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths: ["src/**"]
  push:
    paths-ignore: ["docs/**"]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/x/run.sh" }] } }
EOF
suite "$r" tests/x/run.sh; seal "$r"
expect "a push-only paths-ignore is NOT a false refusal" 0 "" "$r" --list --all

# A workflow naming a suite that is not on disk means discovery is wrong. Never 0.
r="$(root missing)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { pull_request: { paths: ["src/**"] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/gone/run.sh" }] } }
EOF
seal "$r"
expect "a suite named but not on disk is no verdict" 3 "not on disk" "$r" --list --all

# Discovering nothing is a failure to discover, not a clean tree.
r="$(root empty)"; seal "$r" 2>/dev/null || true
expect "zero workflows is no verdict" 3 "examining nothing" "$r" --list --all

r="$(root nosuites)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { pull_request: { paths: ["src/**"] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "echo hi" }] } }
EOF
seal "$r"
expect "workflows that invoke no suite at all is no verdict" 3 "discovers nothing" "$r" --list --all

r="$(root broken)"
printf 'name: w\non: { pull_request:\n  bad indent [[[\n' > "$r/.github/workflows/w.yml"
seal "$r"
expect "an unparsable workflow is no verdict" 3 "will not parse" "$r" --list --all

r="$(root emptypaths)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { pull_request: { paths: [] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/x/run.sh" }] } }
EOF
suite "$r" tests/x/run.sh; seal "$r"
expect "an empty paths: list is no verdict" 3 "present but empty" "$r" --list --all

r="$(root basegone)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { pull_request: { paths: ["src/**"] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/x/run.sh" }] } }
EOF
suite "$r" tests/x/run.sh; seal "$r"
expect "a base ref that does not resolve is no verdict" 3 "does not resolve" "$r" --base nope/nope

echo
echo "── selecting nothing is a VERDICT, and it says so"

# The #266 shape, one layer up: "0 suites selected" and "all suites passed" must not be the same
# output. Both are exit 0 — so the words are the only thing distinguishing them.
r="$(root nothing)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { pull_request: { paths: ["src/**"] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/x/run.sh" }] } }
EOF
suite "$r" tests/x/run.sh; mkdir -p "$r/docs"; echo x > "$r/docs/a.md"; seal "$r"
touchf "$r" docs/a.md
expect "a change gating no suite says so, in as many words" 0 "no suite is gated by this change" "$r" --base HEAD

echo
echo "── it actually runs, and reports the exit code faithfully"

r="$(root runs)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { pull_request: { paths: ["src/**"] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/red/run.sh" }] } }
EOF
mkdir -p "$r/tests/red"
# A suite that prints a GREEN summary and exits non-zero — the unpinned flake #860 records, and the
# exact shape that teaches people to stop trusting exit codes. The runner must believe the code.
printf '#!/usr/bin/env bash\necho "86 passed, 0 failed"\nexit 1\n' > "$r/tests/red/run.sh"
mkdir -p "$r/src"; echo x > "$r/src/a.fs"; seal "$r"; touchf "$r" src/a.fs
expect "a suite printing a green summary but exiting 1 is FAILED" 1 "exit 1" "$r" --base HEAD

r="$(root green)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { pull_request: { paths: ["src/**"] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/ok/run.sh" }] } }
EOF
suite "$r" tests/ok/run.sh; mkdir -p "$r/src"; echo x > "$r/src/a.fs"; seal "$r"; touchf "$r" src/a.fs
expect "a passing suite is PASS, exit 0" 0 "1 passed, 0 failed" "$r" --base HEAD

echo
echo "── unwired suites are reported, not papered over"

r="$(root unwired)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on: { pull_request: { paths: ["src/**"] } }
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/wired/run.sh" }] } }
EOF
suite "$r" tests/wired/run.sh; suite "$r" tests/orphan/run.sh; seal "$r"
out="$(python3 "$TOOL" --root "$r" --list --all)"
grep -qF "UNWIRED" <<<"$out" && grep -qF "tests/orphan" <<<"$out" \
  && ! grep -qF "tests/wired/" <<<"$(sed -n '/UNWIRED/,$p' <<<"$out")" \
  && ok "a tests/ dir no workflow references is reported unwired" \
  || bad "a tests/ dir no workflow references is reported unwired" "$out"

echo
echo "── regression: the REAL working tree"

# Derivation over the shipped workflows. If this repo's real filters stop being readable, that is a
# finding here rather than a surprise in someone's inner loop.
out="$(python3 "$TOOL" --root "$REPO_ROOT" --list --all 2>&1)" && rc=0 || rc=$?
if [ "$rc" -ne 0 ]; then
  bad "the real tree's workflows are readable (exit $rc)" "$out"
else
  n="$(grep -cE '^  (bash|dotnet) ' <<<"$out" || true)"
  # A floor, not an equality: suites get added, and a fixture that must be edited for every new
  # suite is a fixture people learn to edit without reading. Zero, or near it, is the bug.
  [ "$n" -ge 25 ] && ok "the real tree derives $n suites (floor 25)" \
                  || bad "the real tree derives only $n suites — discovery is broken" "$out"
fi

# The side effect #860 asks for, pinned against the real tree: tests/merge-guard is wired to no
# workflow and CI has never run it. When somebody wires it, this leg goes red and should be deleted.
out="$(python3 "$TOOL" --root "$REPO_ROOT" --list --all 2>&1 || true)"
if grep -qF "tests/merge-guard" <<<"$out"; then
  ok "tests/merge-guard is reported unwired (delete this leg when it gets a workflow)"
else
  bad "tests/merge-guard is no longer reported unwired — wired now? then delete this leg" "$out"
fi

# Every suite the real workflows name is on disk. This is the on-disk check aimed at the real tree:
# a workflow invoking a suite that was renamed or deleted is a gate that cannot run.
python3 "$TOOL" --root "$REPO_ROOT" --list --all >/dev/null 2>&1 \
  && ok "every suite the real workflows name exists on disk" \
  || bad "a real workflow names a suite that is not on disk"

# This fixture must itself be wired, or it is the very class it reports.
if [ -f "$REPO_ROOT/.github/workflows/test-selector-selftest.yml" ] \
   && grep -qF "tests/test-selector/run.sh" "$REPO_ROOT/.github/workflows/test-selector-selftest.yml"; then
  ok "this fixture is wired to a workflow — it is not the class it reports"
else
  bad "tests/test-selector/ is not wired to any workflow: the fixture reports unwired suites and is one"
fi

echo
echo "── wiring: --assert-wired turns the unwired REPORT into a verdict (.github#2537)"

# #860 REPORTED unwired suites and nothing ever refused one, so "unwired" was a state work drifted into
# in silence — `tests/review-critic-succession-wire/`, which is itself a gate-inversion proof, sat
# uninvoked from the day it landed. These legs are that refusal's own evidence that it can fire.

# A stray FAILS, and names the directory rather than only exiting non-zero.
r="$(root assertwired)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths: ["src/**"]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/wired/run.sh" }] } }
EOF
suite "$r" tests/wired/run.sh; suite "$r" tests/orphan/run.sh; seal "$r"
expect "an unexplained unwired suite FAILS --assert-wired, by name" 1 "UNWIRED    tests/orphan/" \
       "$r" --assert-wired

# THE SAME ROOT, one `run:` step later. This is what makes the leg above a measurement rather than a
# refusal that fires whatever the tree looks like: the only thing that changed is the wiring.
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths: ["src/**"]
jobs:
  j:
    runs-on: ubuntu-latest
    steps:
      - run: "bash tests/wired/run.sh"
      - run: "bash tests/orphan/run.sh"
EOF
expect "wiring that same suite clears the refusal" 0 "0 unexplained" "$r" --assert-wired

# Python bytecode caches are generated by successful fixture imports during this selector. They are
# ignored build products, never suites; adding one must not make wiring depend on whether Python ran first.
mkdir -p "$r/tests/__pycache__"
printf 'bytecode' >"$r/tests/__pycache__/fixture.pyc"
expect "an ignored Python cache is not misclassified as an unwired suite" 0 "0 unexplained" \
       "$r" --assert-wired

# THE VACUITY TRAP, and .github#2534 measured this exact shape live: a gate that scanned an empty
# corpus passed green. Every set --assert-wired compares is empty over a tests/ with no subdirectories,
# so it would be at its most confident having examined nothing. It must refuse instead. (Reaching this
# state needs a suite at `tests/run.sh` itself — a suite path with no directory component — which is
# the one way `known` is non-empty while `tests/` has no subdirectory.)
r="$(root assertwired-flat)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths: ["src/**"]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/run.sh" }] } }
EOF
suite "$r" tests/run.sh; seal "$r"
expect "an empty tests/ corpus is REFUSED, never passed" 3 "would pass having examined nothing" \
       "$r" --assert-wired

# An exemption that is no longer true fails too — the dangerous direction, because a stale entry would
# silently re-bless its directory if somebody unwired it again.
r="$(root assertwired-stale)"
cat > "$r/.github/workflows/w.yml" <<'EOF'
name: w
on:
  pull_request:
    paths: ["src/**"]
jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: "bash tests/merge-guard/run.sh" }] } }
EOF
suite "$r" tests/merge-guard/run.sh; seal "$r"
expect "a DELIBERATELY_UNWIRED entry for a suite that is wired now is STALE" 1 "STALE      tests/merge-guard/" \
       "$r" --assert-wired

# THE REAL TREE. This is the leg that reds when somebody deletes the `run:` step that invokes a fixture
# from its workflow: the suite drops out of derivation, DELIBERATELY_UNWIRED does not explain it, and
# this fails. It lives HERE rather than in the fixture being protected because a check that a workflow
# edit can delete cannot detect that workflow edit — and this workflow triggers on `.github/workflows/**`,
# so it runs on the very PR that would do it.
expect "the real tree has no unexplained unwired suite" 0 "0 unexplained" "$REPO_ROOT" --assert-wired

out="$(python3 "$TOOL" --root "$REPO_ROOT" --assert-wired 2>&1 || true)"

# No dead entries: every DELIBERATELY_UNWIRED key names a directory that is actually here. `--assert-wired`
# treats an absent one as INERT rather than failing, because it must also run over synthetic roots that
# have none of them; this tree's own "no dead text" rule belongs here, where the tree is the subject.
if grep -q "INERT" <<<"$out"; then
  bad "DELIBERATELY_UNWIRED names a directory that is not on disk — delete the dead entry" "$out"
else
  ok "every DELIBERATELY_UNWIRED entry names a directory that exists"
fi

# .github#2537's own subject, pinned by name. The general leg above would catch this too; this one says
# which suite and why, so the next reader does not have to re-derive it from a count.
if grep -qF "tests/review-critic-succession-wire" <<<"$out"; then
  bad "tests/review-critic-succession-wire is unwired again — it is the gate-inversion proof for critic succession, and .github#2537 wired it into coord-engine.yml" "$out"
else
  ok "tests/review-critic-succession-wire is still wired to a workflow (.github#2537)"
fi

echo
echo "test-selector fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::test-selector fixture FAILED"; exit 1; }
echo "test-selector fixture — OK"
