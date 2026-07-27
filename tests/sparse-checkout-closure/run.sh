#!/usr/bin/env bash
# Fixture for scripts/check-sparse-checkout-closure.py (.github#1522, epic #266).
#
# THE LEGS THAT MATTER ARE THE RETRO-APPLICATIONS. #1522 asks for a gate that would have caught
# #1510 and #1515, and the only way to know that is to put the PRE-FIX shapes back and watch it red.
# So the first legs below take the REAL workflow files out of this repo, replace the sparse block
# with the exact text that was there before #1514 / #1518, and grade the result. They are not
# synthetic re-creations from memory: each is preceded by a `must_pass` on the unmutated file, so the
# anchor text is proven to be the file's current content, and if either workflow is restructured
# `replace_block` aborts the run rather than grading a file that no longer exists.
#
#   #1510  skill-union-assert.yml    enumerated the script + ONE of its two load-time libs.
#   #1515  lock-range-coherence.yml  enumerated one file. Not broken, but the same shape.
#
# THE UNANCHORED LEG IS NOT DECORATION. `scripts/` instead of `/scripts/` fixes the enumeration bug
# and still drags in six extra directories under the skill bundles. A gate blind to it would bless
# the wrong pattern, and the anchoring was a real finding in #1514's review.
#
# THE UNGRADED LEGS ARE THE OTHER HALF. A cone-mode fetch of a repository the gate cannot resolve has
# NOTHING asserted about it. The gate's first draft printed `ok` over that shape and claimed in its
# summary that the patterns were anchored literal directories — #1510's shape one repo across,
# reported green. So there are legs asserting the gate says UNGRADED and does not count it.
#
# The green legs earn their place too: a rule only ever exercised on violators is one nobody can
# trust, because nothing proves it can say "yes" (the #628 shape).
set -euo pipefail

# `set -e` does NOT reach inside `$( )` without this: `newroot` is always called as `R="$(newroot …)"`,
# and without inherit_errexit a failing `git init` or `git remote add` inside it is invisible — the
# function runs on and echoes a path anyway. Measured: with every `git remote add` failing, this
# fixture reported 22 passed / 0 failed while rule (4) was silently disabled in every tree.
shopt -s inherit_errexit

export PYTHONDONTWRITEBYTECODE=1
# A fixture that runs `git init` must not be able to write into somebody else's repository. With
# GIT_DIR exported — true inside every git hook, `git rebase --exec` and `git bisect run` — an
# unguarded `git init "$d"` re-inits THAT repo and `git remote add` lands in it. The config vars also
# neutralise `init.templateDir` and `core.hooksPath` from the developer's global config.
unset GIT_DIR GIT_WORK_TREE GIT_INDEX_FILE
export GIT_CONFIG_GLOBAL=/dev/null GIT_CONFIG_SYSTEM=/dev/null

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-sparse-checkout-closure.py"
[ -f "$GATE" ] || { echo "FAIL  gate not found at $GATE"; exit 1; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/sparse-closure-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# A non-zero exit alone does NOT prove the gate failed for the reason claimed (epic #266), so the
# reason pattern is REQUIRED — and exit 3 is rejected, so a crash or a refusal cannot be laundered
# into a catch.
must_fail() { # must_fail <name> <reason-substring> <root>
  local n="$1" pat="$2" root="$3" out rc=0
  out="$(python3 "$GATE" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then bad "$n (expected a non-zero exit, got 0)" "$out"
  elif [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -qF -- "$pat"; then ok "$n"
  elif [ "$rc" -eq 1 ]; then bad "$n (found a problem, but not the claimed one: no '$pat')" "$out"
  else bad "$n (expected exit 1 FINDING, got $rc)" "$out"; fi
}

must_no_verdict() { # must_no_verdict <name> <reason-substring> <root>
  local n="$1" pat="$2" root="$3" out rc=0
  out="$(python3 "$GATE" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne 3 ]; then bad "$n (expected exit 3 NO VERDICT, got $rc)" "$out"
  elif printf '%s' "$out" | grep -qF -- "$pat"; then ok "$n"
  else bad "$n (no verdict, but not for the claimed reason: no '$pat')" "$out"; fi
}

must_pass() { # must_pass <name> <root>
  local n="$1" root="$2" out rc=0
  out="$(python3 "$GATE" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then ok "$n"; else bad "$n (expected exit 0, got $rc)" "$out"; fi
}

must_say() { # must_say <name> <substring> <root>  -- a PASSING run that makes a specific claim
  local n="$1" pat="$2" root="$3" out rc=0
  out="$(python3 "$GATE" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne 0 ]; then bad "$n (expected exit 0, got $rc)" "$out"
  elif printf '%s' "$out" | grep -qF -- "$pat"; then ok "$n"
  else bad "$n (passed, but never said '$pat')" "$out"; fi
}

# ---- TREE BUILDERS -----------------------------------------------------------------------------

# A tree that looks enough like FS-GG/.github for rule (4) to resolve: a real origin remote, and a
# real scripts/ directory STAGED, because rule (4) asks git's index rather than the filesystem (an
# untracked directory is one the runner would not receive).
newroot() { # newroot <name> -> echoes the path
  local d="$WORK/$1"
  mkdir -p "$d/.github/workflows" "$d/scripts/lib"
  : > "$d/scripts/skill-union-assert.sh"
  : > "$d/scripts/check-lock-ranges.py"
  : > "$d/scripts/lib/args.sh"
  git init -q -b main "$d"
  git -C "$d" remote add origin https://github.com/FS-GG/.github.git
  git -C "$d" add -A
  # Assert the two things every leg silently depends on. Without the origin, rule (4) is disabled and
  # the tree grades on three rules while the leg names claim four.
  git -C "$d" remote get-url origin >/dev/null
  git -C "$d" ls-files --error-unmatch scripts/lib/args.sh >/dev/null
  echo "$d"
}

# Stage whatever the leg wrote after `newroot`, so the workflow file is tracked too. (Rule 4 only
# reads the index for the FETCHED directory, but keeping the tree consistent avoids surprises.)
seal() { git -C "$1" add -A; }

replace_block() { # replace_block <file> <old> <new>
  python3 - "$1" "$2" "$3" <<'PY'
import pathlib, sys
path, old, new = pathlib.Path(sys.argv[1]), sys.argv[2], sys.argv[3]
text = path.read_text(encoding="utf-8")
if old not in text:
    sys.stderr.write(f"replace_block: anchor text not found in {path}\n")
    sys.exit(2)
path.write_text(text.replace(old, new, 1), encoding="utf-8")
PY
}

w() { # w <root> <relpath>  -- body on stdin
  mkdir -p "$(dirname "$1/$2")"
  cat > "$1/$2"
}

# The shape both workflows carry today, and the two shapes they carried before #1514 / #1518.
FIXED_BLOCK='          sparse-checkout: |
            /scripts/'
PREFIX_1510='          sparse-checkout: |
            scripts/skill-union-assert.sh
            scripts/lib/args.sh'
PREFIX_1515='          sparse-checkout: scripts/check-lock-ranges.py'
UNANCHORED='          sparse-checkout: |
            scripts/'

# ---- TOOLING ------------------------------------------------------------------------------------
# A missing YAML parser is a FAILED LEG, never a skip. This fixture's whole subject is a YAML
# structure; a run that cannot parse YAML has not exercised the gate, and reporting that as a pass
# is the same fail-open the gate exists to close (#266). Do not make this skippable.
if ! python3 -c "import yaml" >/dev/null 2>&1; then
  bad "PyYAML is missing, so NOT ONE leg of this fixture can run" \
      "install it with: python3 -m pip install -r requirements-test.txt (CI uses .github/actions/setup-policy-python)"
  echo
  echo "sparse-checkout-closure fixture: $pass passed, $failcount failed."
  exit 1
fi

# ---- RETRO-APPLICATION: #1510, the instance that FIRED -------------------------------------------
R="$(newroot retro-1510)"
cp "$REPO_ROOT/.github/workflows/skill-union-assert.yml" "$R/.github/workflows/"; seal "$R"
must_pass "#1510 workflow as it stands today (/scripts/) is green" "$R"
replace_block "$R/.github/workflows/skill-union-assert.yml" "$FIXED_BLOCK" "$PREFIX_1510"
must_fail "#1510 pre-fix shape (script + 1 of 2 libs) is caught" "ENUMERATES A FILE" "$R"
must_fail "#1510 pre-fix shape is also caught as unanchored" "is NOT ANCHORED" "$R"

# ---- RETRO-APPLICATION: #1515, the instance that was LATENT ---------------------------------------
R="$(newroot retro-1515)"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$R/.github/workflows/"; seal "$R"
must_pass "#1515 workflow as it stands today (/scripts/) is green" "$R"
replace_block "$R/.github/workflows/lock-range-coherence.yml" "$FIXED_BLOCK" "$PREFIX_1515"
must_fail "#1515 pre-fix shape (one named file) is caught" "ENUMERATES A FILE" "$R"

# ---- THE UNANCHORED REFINEMENT --------------------------------------------------------------------
# Fixes the enumeration and is still wrong. A gate that greens here blesses the wrong pattern.
R="$(newroot unanchored)"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$R/.github/workflows/"; seal "$R"
replace_block "$R/.github/workflows/lock-range-coherence.yml" "$FIXED_BLOCK" "$UNANCHORED"
must_fail "unanchored 'scripts/' is caught even though it is a directory" "is NOT ANCHORED" "$R"
must_fail "the unanchored finding explains the any-depth match" "AT ANY DEPTH" "$R"

# ---- SHAPES THAT MUST STAY GREEN (a rule that cannot say yes is untrustworthy) ---------------------
R="$(newroot green-full-clone)"
w "$R" .github/workflows/full.yml <<'YAML'
name: full
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: other
          sparse-checkout: |
            /scripts/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_pass "a checkout with no sparse-checkout is a full clone, not a subject" "$R"

# The IDEAL end state: every cross-repo checkout is a full clone, so the class is permanently
# foreclosed. An earlier draft's vacuity guard redded exactly this — a gate arguing for its own
# subject's survival.
R="$(newroot green-all-full-clones)"
w "$R" .github/workflows/full.yml <<'YAML'
name: full
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
YAML
seal "$R"
must_pass "a tree whose cross-repo checkouts are ALL full clones is the ideal end state, not a red" "$R"

R="$(newroot green-pinned-sha)"
w "$R" .github/workflows/pinned.yml <<'YAML'
name: pinned
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@11bd71901bbe5b1630ceea73d27597364c9af683
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /scripts/
            /scripts/lib/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_pass "a SHA-pinned actions/checkout is still graded, and passes when anchored" "$R"

# ---- THE CASE-VARIANT SUBJECT (a step that silently left the set) ----------------------------------
R="$(newroot casing)"
w "$R" .github/workflows/casing.yml <<'YAML'
name: casing
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: clean
          sparse-checkout: |
            /scripts/
          sparse-checkout-cone-mode: false
      - uses: actions/Checkout@v7
        with:
          repository: FS-GG/.github
          path: sneaky
          sparse-checkout: scripts/lib/args.sh
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_fail "actions/Checkout (capital C) is the real action and is still graded" "ENUMERATES A FILE" "$R"

R="$(newroot repo-casing)"
w "$R" .github/workflows/repocase.yml <<'YAML'
name: repocase
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: fs-gg/.GitHub
          path: dotgithub
          sparse-checkout: |
            /nope/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_fail "a case-variant repository: still resolves to the audited repo, so rule (4) runs" "selects no tracked path" "$R"

# ---- CONE MODE: EXEMPT FROM (1)-(2), BUT NEVER SILENTLY --------------------------------------------
R="$(newroot cone-local)"
w "$R" .github/workflows/cone.yml <<'YAML'
name: cone
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: scripts
          sparse-checkout-cone-mode: true
YAML
seal "$R"
must_pass "cone mode is exempt from anchoring and the trailing slash" "$R"

# In cone mode a FILE can be named — git reads it as an empty directory. Only rule (4) sees that, so
# this leg proves the exemption is safe exactly when the tree is resolvable.
R="$(newroot cone-file-local)"
w "$R" .github/workflows/cone.yml <<'YAML'
name: cone
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: scripts/lib/args.sh
          sparse-checkout-cone-mode: true
YAML
seal "$R"
must_fail "a FILE named in cone mode selects an empty directory, and rule (4) catches it" "selects no tracked path" "$R"

# ...and when the tree is NOT resolvable, nothing sees it. That must be said, never printed `ok`.
# This is #1510's own shape one repository across — the case the gate's first draft greened.
R="$(newroot cone-foreign)"
w "$R" .github/workflows/cone.yml <<'YAML'
name: cone
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/FS.GG.SDD
          path: sdd
          sparse-checkout: |
            scripts/assert.sh
            scripts/lib/args.sh
          sparse-checkout-cone-mode: true
YAML
seal "$R"
must_say "cone mode against an unresolvable repo is reported UNGRADED, not ok" "UNGRADED" "$R"
must_say "...and the summary counts it as ungraded rather than folding it into the green claim" \
         "1 step(s) UNGRADED" "$R"
cone_foreign_out="$(python3 "$GATE" --root "$R" 2>&1 || true)"
if printf '%s' "$cone_foreign_out" | grep -qE '^  ok '; then
  bad "the cone+foreign step printed an 'ok' line — the gate is claiming work it did not do" \
      "$cone_foreign_out"
else
  ok "and no 'ok' line is printed for a step nothing was asserted about"
fi

# The cone-mode DEFAULT is the single most consequential constant in the gate: it decides whether
# rules (1)-(2) apply to an author who wrote nothing. Every other leg spells the flag out, so
# flipping the default would fail none of them.
R="$(newroot cone-default)"
w "$R" .github/workflows/default.yml <<'YAML'
name: default
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: scripts
YAML
seal "$R"
must_pass "an OMITTED sparse-checkout-cone-mode defaults to cone (actions/checkout's default)" "$R"

R="$(newroot cone-quoted)"
w "$R" .github/workflows/quoted.yml <<'YAML'
name: quoted
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /scripts/
          sparse-checkout-cone-mode: "false"
YAML
seal "$R"
must_pass "a QUOTED boolean is what every with: value already is — accepted, not refused" "$R"

# ---- THE GAPS RULE (4) CLOSES, AND THE ONE IT DOES NOT --------------------------------------------
R="$(newroot missing-dir)"
w "$R" .github/workflows/typo.yml <<'YAML'
name: typo
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /scirpts/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_fail "a misspelt directory selects nothing and is caught" "selects no tracked path" "$R"

# An UNTRACKED directory is one the runner would never receive. `is_dir()` says yes; git says no,
# and git is the one that will be doing the fetching.
R="$(newroot untracked-dir)"
mkdir -p "$R/artifacts/nested"
: > "$R/artifacts/nested/thing.txt"
w "$R" .github/workflows/untracked.yml <<'YAML'
name: untracked
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /artifacts/
          sparse-checkout-cone-mode: false
YAML
git -C "$R" add .github
must_fail "an UNTRACKED directory exists on disk but is not fetchable — rule (4) asks git" \
          "selects no tracked path" "$R"

R="$(newroot dotdot)"
w "$R" .github/workflows/dotdot.yml <<'YAML'
name: dotdot
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /../outside/scripts/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_fail "a '..' component cannot climb out of the repository and is caught" "\`..\` component" "$R"

# The gap, asserted so it is a KNOWN limit rather than a surprise: an existing but wrong directory
# satisfies every rule. If a future closure gate closes this, THIS leg is the one that must flip.
R="$(newroot wrong-but-real-dir)"
w "$R" .github/workflows/wrong.yml <<'YAML'
name: wrong
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /scripts/lib/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_pass "KNOWN GAP: an existing but wrong directory passes (this gate asserts shape, not closure)" "$R"

# ---- FOREIGN REPOSITORIES: the syntactic rules still apply, and the gate says which did not --------
R="$(newroot foreign)"
w "$R" .github/workflows/foreign.yml <<'YAML'
name: foreign
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/FS.GG.Audio
          path: audio
          sparse-checkout: |
            /nowhere-near-this-tree/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_say "a foreign repo's directories are not existence-checked, and the gate says so" \
         "rule (4) did not run" "$R"

R="$(newroot foreign-enumerating)"
w "$R" .github/workflows/foreign.yml <<'YAML'
name: foreign
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/FS.GG.Audio
          path: audio
          sparse-checkout: tools/build.sh
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_fail "a foreign repo still gets the syntactic rules" "ENUMERATES A FILE" "$R"

# ---- SHAPES REFUSED RATHER THAN SKIPPED (exit 3) ---------------------------------------------------
R="$(newroot refuse-negation)"
w "$R" .github/workflows/neg.yml <<'YAML'
name: neg
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /scripts/
            !/scripts/lib/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_no_verdict "a negated pattern is refused, not graded as a directory" "negated sparse pattern" "$R"

empty_n=0
for spelling in 'sparse-checkout: "   "' 'sparse-checkout: ""' 'sparse-checkout: []' 'sparse-checkout:'; do
  empty_n=$((empty_n + 1))
  R="$(newroot "refuse-empty-$empty_n")"
  w "$R" .github/workflows/empty.yml <<YAML
name: empty
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          $spelling
          sparse-checkout-cone-mode: false
YAML
  seal "$R"
  must_no_verdict "[$spelling] a key supplying no pattern is refused — all spellings alike" \
                  "supplies no pattern" "$R"
done

R="$(newroot refuse-cone)"
w "$R" .github/workflows/badcone.yml <<'YAML'
name: badcone
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /scripts/
          sparse-checkout-cone-mode: "${{ inputs.cone }}"
YAML
seal "$R"
must_no_verdict "an unevaluated cone-mode expression is refused; it decides what the patterns MEAN" \
                "unreadable" "$R"

R="$(newroot refuse-vacuous)"
w "$R" .github/workflows/nothing.yml <<'YAML'
name: nothing
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
YAML
seal "$R"
must_no_verdict "NO cross-repo checkout at all means the wrong tree, not a clean audit" "collapsed" "$R"

# One unreadable step must not suppress the real findings in the rest of the tree.
R="$(newroot refusal-does-not-mask)"
w "$R" .github/workflows/a-bad.yml <<'YAML'
name: abad
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: scripts/check-lock-ranges.py
          sparse-checkout-cone-mode: false
YAML
w "$R" .github/workflows/z-negated.yml <<'YAML'
name: zneg
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            !/scripts/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
# Captured first, never piped: the gate exits 3 here, and under `pipefail` a pipeline would carry
# that exit code regardless of what grep found.
masking_out="$(python3 "$GATE" --root "$R" 2>&1 || true)"
if printf '%s' "$masking_out" | grep -qF "ENUMERATES A FILE"; then
  ok "a refusal in one workflow does not hide a real finding in another"
else
  bad "a single refused step swallowed a genuine finding elsewhere in the tree" "$masking_out"
fi

# ---- THE FOLDED-SCALAR FAIL-OPEN (the bug sparse_set.py's first draft shipped) ---------------------
# `sparse-checkout: >` joins its lines with a SPACE, so the runner receives ONE pattern containing a
# space, which matches nothing. A hand parser reports two clean patterns and goes green. Caught by
# the whitespace rule rather than by rule (4), so it holds for an unresolvable repo too.
R="$(newroot folded)"
w "$R" .github/workflows/folded.yml <<'YAML'
name: folded
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/FS.GG.Audio
          path: audio
          sparse-checkout: >
            /scripts/
            /scripts/lib/
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_fail "a FOLDED block scalar collapses to one space-joined pattern, caught even for a foreign repo" \
          "contains whitespace" "$R"

# ---- GLOBS ------------------------------------------------------------------------------------------
R="$(newroot glob)"
w "$R" .github/workflows/glob.yml <<'YAML'
name: glob
on: [push]
jobs:
  a:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: |
            /scripts/*.py
          sparse-checkout-cone-mode: false
YAML
seal "$R"
must_fail "a glob is not a directory and cannot be read off the file" "glob metacharacter" "$R"

# ---- THE SHARED READING ITSELF (.github#1530) -------------------------------------------------------
# Everything above grades the block THROUGH the gate. `scripts/lib/sparse.py` is where the reading now
# lives, and it is read by a second caller (tests/lock-range-coherence/sparse_set.py) whose fixture
# exercises a different subject entirely. A rule asserted only through its callers is a rule that can
# drift the day a caller stops reaching it — which is how the two pre-hoist copies disagreed about a
# bare `sparse-checkout:` without either fixture noticing. So it is asserted directly, once, here.
lib_out=""; lib_rc=0
lib_out="$(python3 "$HERE/sparse_lib_selftest.py" 2>&1)" || lib_rc=$?
if [ "$lib_rc" -eq 0 ]; then
  ok "scripts/lib/sparse.py's own selftest: $(printf '%s' "$lib_out" | tail -1)"
else
  bad "scripts/lib/sparse.py's own selftest failed (exit $lib_rc)" "$lib_out"
fi

# ---- THE SUBJECT ITSELF ------------------------------------------------------------------------------
# The tree CI actually grades. Criterion 3 of #1522 asks for it explicitly.
must_pass "the real FS-GG/.github tree is green" "$REPO_ROOT"
must_say "...and rule (4) really ran there, rather than being silently disabled" \
         "fully-graded cross-repo checkout(s)" "$REPO_ROOT"

echo
echo "sparse-checkout-closure fixture: $pass passed, $failcount failed."
[ "$failcount" -eq 0 ] || exit 1
