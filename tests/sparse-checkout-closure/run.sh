#!/usr/bin/env bash
# Fixture for scripts/check-sparse-checkout-closure.py (.github#1522, epic #266).
#
# THE LEGS THAT MATTER ARE THE RETRO-APPLICATIONS. #1522 asks for a gate that would have caught
# #1510 and #1515, and the only way to know that is to put the PRE-FIX shapes back and watch it red.
# So the first two legs below take the REAL workflow files out of this repo, replace the sparse
# block with the exact text that was there before #1514 / #1518, and grade the result. They are not
# synthetic re-creations from memory: if `skill-union-assert.yml` is restructured, the mutation
# stops matching and the leg fails loudly rather than grading a file that no longer exists.
#
#   #1510  skill-union-assert.yml    enumerated the script + ONE of its two load-time libs.
#   #1515  lock-range-coherence.yml  enumerated one file. Not broken, but the same shape.
#
# THE UNANCHORED LEG IS NOT DECORATION. `scripts/` instead of `/scripts/` fixes the enumeration bug
# and still selects six extra directories under the skill bundles (72 paths vs 66). A gate blind to
# it would bless the wrong pattern, and the anchoring was a real finding in #1514's review — so it
# gets a leg of its own, and a passing gate must be able to tell the two forms apart.
#
# The green legs earn their place too: a rule only ever exercised on violators is one nobody can
# trust, because nothing proves it can say "yes" (the #628 shape).
set -euo pipefail
export PYTHONDONTWRITEBYTECODE=1

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
# reason pattern is REQUIRED on every failing leg.
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

# ---- TREE BUILDERS -----------------------------------------------------------------------------

# A tree that looks enough like FS-GG/.github for rule (4) to resolve: a real origin remote and a
# real scripts/ directory. Without the remote the gate cannot tell which checkouts point at the tree
# it is holding, and every leg would silently drop to three rules.
newroot() { # newroot <name> -> echoes the path
  local d="$WORK/$1"
  mkdir -p "$d/.github/workflows" "$d/scripts/lib"
  : > "$d/scripts/skill-union-assert.sh"
  : > "$d/scripts/check-lock-ranges.py"
  : > "$d/scripts/lib/args.sh"
  git init -q -b main "$d"
  git -C "$d" remote add origin https://github.com/FS-GG/.github.git
  echo "$d"
}

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
cp "$REPO_ROOT/.github/workflows/skill-union-assert.yml" "$R/.github/workflows/"
must_pass "#1510 workflow as it stands today (/scripts/) is green" "$R"
replace_block "$R/.github/workflows/skill-union-assert.yml" "$FIXED_BLOCK" "$PREFIX_1510"
must_fail "#1510 pre-fix shape (script + 1 of 2 libs) is caught" "ENUMERATES A FILE" "$R"
must_fail "#1510 pre-fix shape is also caught as unanchored" "is NOT ANCHORED" "$R"

# ---- RETRO-APPLICATION: #1515, the instance that was LATENT ---------------------------------------
R="$(newroot retro-1515)"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$R/.github/workflows/"
must_pass "#1515 workflow as it stands today (/scripts/) is green" "$R"
replace_block "$R/.github/workflows/lock-range-coherence.yml" "$FIXED_BLOCK" "$PREFIX_1515"
must_fail "#1515 pre-fix shape (one named file) is caught" "ENUMERATES A FILE" "$R"

# ---- THE UNANCHORED REFINEMENT --------------------------------------------------------------------
# Fixes the enumeration and is still wrong. A gate that greens here blesses the wrong pattern.
R="$(newroot unanchored)"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$R/.github/workflows/"
replace_block "$R/.github/workflows/lock-range-coherence.yml" "$FIXED_BLOCK" "$UNANCHORED"
must_fail "unanchored 'scripts/' is caught even though it is a directory" "is NOT ANCHORED" "$R"

R="$(newroot unanchored-nested)"
cp "$REPO_ROOT/.github/workflows/lock-range-coherence.yml" "$R/.github/workflows/"
replace_block "$R/.github/workflows/lock-range-coherence.yml" "$FIXED_BLOCK" "$UNANCHORED"
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
must_pass "a checkout with no sparse-checkout is a full clone, not a subject" "$R"

R="$(newroot green-cone)"
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
          sparse-checkout: src/FS.GG.Contracts
          sparse-checkout-cone-mode: true
YAML
must_pass "cone mode is exempt: git reads those as rooted directory prefixes" "$R"

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
must_pass "a SHA-pinned actions/checkout is still graded, and passes when anchored" "$R"

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
must_fail "a misspelt directory fetches an empty tree and is caught" "names no directory" "$R"

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
must_pass "KNOWN GAP: an existing but wrong directory passes (this gate asserts shape, not closure)" "$R"

# ---- FOREIGN REPOSITORIES: three rules, and the gate says so --------------------------------------
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
must_pass "a foreign repo's directories are not existence-checked" "$R"
if python3 "$GATE" --root "$R" 2>&1 | grep -qF "existence of its directories was NOT checked"; then
  ok "and the gate SAYS the existence check did not run, rather than implying it did"
else
  bad "the foreign-repo leg passed silently — an unstated reduction in reach is the #266 shape"
fi

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
must_no_verdict "a negated pattern is refused, not graded as a directory" "negated sparse pattern" "$R"

R="$(newroot refuse-empty)"
w "$R" .github/workflows/empty.yml <<'YAML'
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
          sparse-checkout: "   "
          sparse-checkout-cone-mode: false
YAML
must_no_verdict "a declared-but-empty sparse-checkout is an empty TREE, not a full clone" "EMPTY TREE" "$R"

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
          sparse-checkout-cone-mode: "maybe"
YAML
must_no_verdict "an unreadable cone-mode is refused; it decides what the patterns MEAN" "unreadable" "$R"

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
must_no_verdict "grading ZERO steps is a failure to audit, not a clean audit" "collapsed" "$R"

# ---- THE FOLDED-SCALAR FAIL-OPEN (the bug sparse_set.py's first draft shipped) ---------------------
# `sparse-checkout: >` joins its lines with a SPACE, so the runner receives ONE pattern containing a
# space, which matches nothing and fetches an empty tree. A hand parser reports two clean patterns
# and goes green. Reading it the way actions/checkout does is what makes this land as a finding.
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
          repository: FS-GG/.github
          path: dotgithub
          sparse-checkout: >
            /scripts/
            /scripts/lib/
          sparse-checkout-cone-mode: false
YAML
must_fail "a FOLDED block scalar collapses to one space-joined pattern and is caught" "names no directory" "$R"

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
must_fail "a glob is not a directory and cannot be read off the file" "glob metacharacter" "$R"

# ---- THE SUBJECT ITSELF ------------------------------------------------------------------------------
# The gate must be green on the real repository. Not a re-derivation of the legs above: this is the
# tree CI actually grades, and criterion 3 of #1522 asks for it explicitly.
must_pass "the real FS-GG/.github tree is green" "$REPO_ROOT"

echo
echo "sparse-checkout-closure fixture: $pass passed, $failcount failed."
[ "$failcount" -eq 0 ] || exit 1
