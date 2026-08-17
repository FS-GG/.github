#!/usr/bin/env bash
# Fixture for scripts/check-paths-coherence.py (.github#880, epic #266).
#
# Offline, and it needs no stub: the gate is static. It reads the working tree and nothing else — no
# API, no network, no credentials — so there is no transport to fake and no retryable verdict to
# exercise. That is a property worth asserting rather than assuming, so §6 proves the gate never
# touches the network by checking it imports no transport module at all.
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: the .github#266 vacuous-failure defect was a "must fail" test whose non-zero exit
# came from a path guard rather than from the thing under test — it would have passed against a gate
# that was broken in a completely different way. Here that trap is especially live, because THREE
# distinct conditions exit 3 (unparsable, uncomparable, audited-nothing) and a leg that only checked
# the code could not tell them apart.
#
# The headline leg is REGRESSION: the REAL adr-coherence.yml and skill-registry-coherence.yml from
# this working tree, with their drift put back exactly as it was on main. If this gate had existed,
# that is the run it would have red-lighted — and the same files, as this PR ships them, pass.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-paths-coherence.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/paths-coherence-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# expect <name> <want-rc> <needle> <root> — the rc AND the reason must both match.
expect() {
  local name="$1" want="$2" needle="$3" root="$4"
  local out rc=0
  out="$(python3 "$TOOL" --root "$root" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

# root <dir> — a synthetic working tree with an empty workflows dir.
root() { mkdir -p "$1/.github/workflows"; echo "$1"; }

# wf <file> <pr-paths-yaml> <push-paths-yaml> — a workflow declaring both filters.
wf() {
  local f="$1" pr="$2" push="$3"
  { echo "name: w"
    echo "on:"
    echo "  pull_request:"
    echo "    paths:"
    printf '%s\n' "$pr"
    echo "  push:"
    echo "    branches: [main]"
    echo "    paths:"
    printf '%s\n' "$push"
    echo "jobs:"
    echo "  j:"
    echo "    runs-on: ubuntu-latest"
    echo "    steps: [{ run: 'true' }]"; } > "$f"
}

# undrift <src> <dst> <pattern>… — copy a real workflow with the named pattern(s) DELETED from its
# `push:` block only, reconstructing the drift exactly as it sat on main. Asserts it actually changed
# something, so a fixture that silently stops reproducing the bug fails instead of passing.
undrift() {
  local src="$1" dst="$2"; shift 2
  python3 - "$src" "$dst" "$@" <<'PY'
import sys
src, dst, targets = sys.argv[1], sys.argv[2], sys.argv[3:]
lines = open(src, encoding="utf-8").read().splitlines(keepends=True)
out, in_push, removed = [], False, []
for ln in lines:
    if ln.startswith("  push:"):
        in_push = True
    elif in_push and ln[:3].strip() and not ln.startswith("    ") and not ln.startswith("  -"):
        in_push = False  # a new key at the `on:` level ends the push block
    if in_push and any(f'"{t}"' in ln for t in targets) and ln.strip().startswith("-"):
        removed.append(ln.strip()); continue
    out.append(ln)
# Assert WHICH targets went, not merely HOW MANY. A count would be satisfied by one target matching
# twice and another not at all — reconstructing a drift that is not the one this leg is named for,
# and then "proving" the gate catches it.
got = {t for t in targets if any(f'"{t}"' in r for r in removed)}
assert got == set(targets), f"undrift removed {removed}; wanted exactly {targets} — fix the fixture"
assert len(removed) == len(targets), f"undrift removed {len(removed)} lines for {len(targets)} targets"
open(dst, "w", encoding="utf-8").write("".join(out))
PY
}

# =============================================================================================
# 1. REGRESSION — the real files, the real bug, from the real working tree.
# =============================================================================================
R="$(root "$WORK/regression")"
undrift "$REPO_ROOT/.github/workflows/adr-coherence.yml" \
        "$R/.github/workflows/adr-coherence.yml" \
        "tests/adr-coherence/**" ".github/workflows/adr-coherence.yml"
expect "REGRESSION #880: adr-coherence.yml's real drift is caught" \
  1 "the \`push\` copy omits" "$R"
expect "REGRESSION: and it NAMES the fixture the push copy dropped" \
  1 "tests/adr-coherence/**" "$R"

R2="$(root "$WORK/regression2")"
undrift "$REPO_ROOT/.github/workflows/skill-registry-coherence.yml" \
        "$R2/.github/workflows/skill-registry-coherence.yml" \
        "tests/skill-registry/**"
# The needle names the DIRECTION, not just the pattern: `tests/skill-registry/**` alone would match
# either "the `push` copy omits…" or "the `pull_request` copy omits…", so the leg would pass even if
# undrift's in_push tracking inverted and it had stripped the wrong block.
expect "REGRESSION #880: skill-registry-coherence.yml's real drift is caught" \
  1 "the \`push\` copy omits 'tests/skill-registry/**'" "$R2"

# The fix must actually satisfy the gate: the files as this PR ships them.
R3="$(root "$WORK/regression-fixed")"
cp "$REPO_ROOT/.github/workflows/adr-coherence.yml" \
   "$REPO_ROOT/.github/workflows/skill-registry-coherence.yml" "$R3/.github/workflows/"
expect "...and both files, as this PR ships them, pass" 0 "ok:" "$R3"

# THE WHOLE SHIPPED SURFACE. Every workflow this PR ships agrees with itself. This is the leg that
# makes a green fixture mean something: a gate that passes on synthetic YAML while the real tree
# drifts is not a state this fixture can reach.
expect "the entire shipped .github/workflows tree satisfies its own gate" 0 "ok:" "$REPO_ROOT"

# =============================================================================================
# 2. The rule.
# =============================================================================================
RD="$(root "$WORK/drift")"
wf "$RD/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
expect "two lists that disagree are caught" 1 "the \`push\` copy omits 'b/**'" "$RD"

RD2="$(root "$WORK/drift-other-way")"
wf "$RD2/.github/workflows/w.yml" '      - "a/**"' '      - "a/**"
      - "b/**"'
expect "...and drift in the OTHER direction is caught too" \
  1 "the \`pull_request\` copy omits 'b/**'" "$RD2"

RS="$(root "$WORK/same")"
wf "$RS/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "b/**"
      - "a/**"'
expect "identical as SETS is coherent — order alone is not drift" 0 "ok:" "$RS"

# =============================================================================================
# 3. The precondition: only a workflow declaring BOTH filters has two copies that can disagree.
# =============================================================================================
RP="$(root "$WORK/pr-only")"
{ echo "name: w"; echo "on:"; echo "  pull_request:"; echo "    paths: ['a/**']"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RP/.github/workflows/w.yml"
cp "$RS/.github/workflows/w.yml" "$RP/.github/workflows/pair.yml"  # so pairs_seen > 0
expect "a PR-only workflow is not drift" 0 "ok:" "$RP"

RU="$(root "$WORK/push-only")"
{ echo "name: w"; echo "on:"; echo "  push: { branches: [main], paths: ['a/**'] }"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RU/.github/workflows/w.yml"
cp "$RS/.github/workflows/w.yml" "$RU/.github/workflows/pair.yml"
expect "a push-only workflow is not drift" 0 "ok:" "$RU"

RN="$(root "$WORK/null-pr")"
# `pull_request:` with a NULL value means EVERY PR, not "no PR trigger" — coherence.yml is in this
# state. It declares no paths:, so it is not half of a pair.
{ echo "name: w"; echo "on:"; echo "  pull_request:"; echo "  push: { branches: [main] }"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RN/.github/workflows/w.yml"
cp "$RS/.github/workflows/w.yml" "$RN/.github/workflows/pair.yml"
expect "a null pull_request: (every PR, no filter) is not drift" 0 "ok:" "$RN"

# =============================================================================================
# 4. `on:` has three legal spellings, and all three must be RECOGNISED AS LEGAL.
#
#    Read the caption carefully, because an earlier draft of it over-claimed and the over-claim is
#    worth more than the legs. It said these legs prove the #879 normalisation — that reading only
#    the mapping form "silently SKIPS the other two". THAT IS NOT WHAT THEY PIN, and it cannot be:
#    neither `on: pull_request` nor `on: [push, pull_request]` can carry a `paths:` key at all, so
#    for THIS rule "normalise it" and "skip it" are the same observable behaviour. A reviewer
#    replaced the list/str branches with `return {}` and all of §4 stayed green — the legs were
#    green for a property they did not test, which is precisely the vacuous assertion
#    tests/feed-coherence/run.sh:10 warns about.
#
#    What they DO pin is the `else: raise` next door: `triggers()` refuses an `on:` it cannot read,
#    so without the str/list branches a perfectly legal `on: [push, pull_request]` would be REFUSED
#    and take the whole audit to exit 3. Delete those branches and these legs go red. That is a real
#    property, and it is the one written here now.
# =============================================================================================
RB="$(root "$WORK/bare-string")"
{ echo "name: w"; echo "on: pull_request"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RB/.github/workflows/w.yml"
cp "$RS/.github/workflows/w.yml" "$RB/.github/workflows/pair.yml"
expect "on: pull_request (bare string) is a legal spelling — recognised, not refused" 0 "ok:" "$RB"

RL="$(root "$WORK/list-form")"
{ echo "name: w"; echo "on: [push, pull_request]"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RL/.github/workflows/w.yml"
cp "$RS/.github/workflows/w.yml" "$RL/.github/workflows/pair.yml"
expect "on: [push, pull_request] (list form) is a legal spelling — recognised, not refused" 0 "ok:" "$RL"

RT="$(root "$WORK/on-int")"
{ echo "name: w"; echo "on: 42"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RT/.github/workflows/w.yml"
expect "an \`on:\` that is none of the three spellings is REFUSED, not guessed" \
  3 "cannot tell what triggers the workflow" "$RT"

# =============================================================================================
# 5. The escape hatch — and the two ways it must not become a hole.
# =============================================================================================
RA="$(root "$WORK/allow")"
wf "$RA/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
sed -i '1i # paths-coherence: allow-divergence — b/** is authored only on PRs' \
  "$RA/.github/workflows/w.yml"
expect "a SIGNED divergence is allowed" 0 "diverges on purpose" "$RA"

RA5="$(root "$WORK/allow-no-sep")"
wf "$RA5/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
sed -i '1i # paths-coherence: allow-divergence b/** is authored only on PRs' \
  "$RA5/.github/workflows/w.yml"
expect "the separator is optional — a reason with no dash still signs the marker" \
  0 "diverges on purpose" "$RA5"

RA2="$(root "$WORK/allow-unsigned")"
wf "$RA2/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
sed -i '1i # paths-coherence: allow-divergence' "$RA2/.github/workflows/w.yml"
expect "an UNSIGNED marker is a finding, not a licence" 1 "with NO reason" "$RA2"

# A marker with no reason is a VERDICT (the gate looked and found something definite), so it is
# exit 1. Exit 3 would tell the developer "I could not check" and hand them the no-verdict summary,
# which names three causes and none of them is theirs — #266's conflation running backwards.
RA6="$(root "$WORK/unsigned-is-not-noverdict")"
wf "$RA6/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
sed -i '1i # paths-coherence: allow-divergence' "$RA6/.github/workflows/w.yml"
expect "...and it is exit 1 (a verdict), NOT exit 3 (could not check)" 1 "with NO reason" "$RA6"

# `search()` reads only the FIRST marker, so a header comment DOCUMENTING the bare form above a
# file's real signed marker made the gate call it unsigned — a confidently wrong verdict against a
# file that did exactly what the gate asked. Every marker is considered.
RA7="$(root "$WORK/allow-documented-then-signed")"
wf "$RA7/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
sed -i '1i # The hatch is spelled:\n# paths-coherence: allow-divergence\n# ...with a reason. Ours:\n# paths-coherence: allow-divergence — b/** is authored only on PRs' \
  "$RA7/.github/workflows/w.yml"
expect "a documented bare form ABOVE a signed marker does not make the file unsigned" \
  0 "diverges on purpose" "$RA7"

# A MENTION of the marker is not a USE of it. This is not hypothetical: the first draft of this gate
# matched the marker text anywhere in the file, and the first thing it licensed was
# paths-coherence.yml itself — whose FINDING step prints the marker to tell a developer how to use
# it. The gate read its own documentation as a signed divergence, and the shipped-surface leg above
# is what caught it. #683's lesson: a parser cannot tell a mention from a use unless you make it.
RA4="$(root "$WORK/allow-mentioned")"
wf "$RA4/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
cat >> "$RA4/.github/workflows/w.yml" <<'YAML'
  doc:
    runs-on: ubuntu-latest
    steps:
      - run: |
          echo 'diverging on purpose? sign it: # paths-coherence: allow-divergence — <why>'
YAML
expect "a MENTION of the marker in a run: block does not license anything" \
  1 "the \`push\` copy omits 'b/**'" "$RA4"

# THE FAIL-OPEN THE `^[ \t]*#` ANCHOR ALONE DID NOT CLOSE, and the reason the marker is now located
# with PyYAML rather than with a cleverer regex. A SHELL comment inside a `run: |` block is
# `^[ \t]*#` too — indistinguishable from a YAML comment by any regex, because the difference is not
# in the characters, it is in which construct owns the line. This exact workflow scored exit 0
# against draft 2, licensing real drift.
RA8="$(root "$WORK/allow-shell-comment")"
wf "$RA8/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
cat >> "$RA8/.github/workflows/w.yml" <<'YAML'
  doc:
    runs-on: ubuntu-latest
    steps:
      - run: |
          # paths-coherence: allow-divergence — a SHELL comment, not a YAML one
          echo hi
YAML
expect "a SHELL comment inside a run: block licenses NOTHING (it is block-scalar text, not a comment)" \
  1 "the \`push\` copy omits 'b/**'" "$RA8"

RA9="$(root "$WORK/allow-heredoc")"
wf "$RA9/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
cat >> "$RA9/.github/workflows/w.yml" <<'YAML'
  doc:
    runs-on: ubuntu-latest
    steps:
      - run: |
          cat <<'EOF'
          # paths-coherence: allow-divergence — quoted prose in a heredoc
          EOF
YAML
expect "...and neither does the marker quoted inside a heredoc" \
  1 "the \`push\` copy omits 'b/**'" "$RA9"

# The mirror: a REAL YAML comment at the same indent as block-scalar text still works. The line
# filter must exclude opaque regions, not merely indented lines.
RA10="$(root "$WORK/allow-indented-yaml-comment")"
wf "$RA10/.github/workflows/w.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
sed -i '2i\      # paths-coherence: allow-divergence — an indented, real YAML comment' \
  "$RA10/.github/workflows/w.yml"
expect "an INDENTED real YAML comment still signs the marker" 0 "diverges on purpose" "$RA10"

RA3="$(root "$WORK/allow-stale")"
wf "$RA3/.github/workflows/w.yml" '      - "a/**"' '      - "a/**"'
sed -i '1i # paths-coherence: allow-divergence — nothing diverges here any more' \
  "$RA3/.github/workflows/w.yml"
expect "a STALE marker on a coherent workflow is caught — it would permit a real drift later" \
  1 "licenses a divergence that does not exist" "$RA3"

# =============================================================================================
# 6. Shapes that make set-equality UNSOUND are refused, not skipped. Neither is live in this repo
#    today; the gate must not quietly start being wrong the day one appears.
# =============================================================================================
RI="$(root "$WORK/ignore")"
{ echo "name: w"; echo "on:"; echo "  pull_request:"; echo "    paths: ['a/**']"
  echo "  push:"; echo "    branches: [main]"; echo "    paths-ignore: ['docs/**']"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RI/.github/workflows/w.yml"
expect "paths: facing paths-ignore: is REFUSED — the two are not comparable" \
  3 "INVERTS selection" "$RI"

# A GATE MAY ONLY REFUSE WHAT IT WAS ASKED TO JUDGE. A workflow declaring ONLY `paths-ignore:` has
# no allow-list, is not half of a pair, and is outside the rule — but the first draft refused it on
# sight, and because the refusal aborts the whole run, ONE such file took the entire audit to exit 3
# and every real drift in the repo went unreported. `paths-ignore:` is an ordinary Actions feature;
# the first person to add one would have blocked CI with a diagnostic naming three causes, none of
# them theirs. This leg asserts the drift is still FOUND with such a file sitting next to it.
RI2="$(root "$WORK/ignore-only")"
{ echo "name: docs"; echo "on:"; echo "  pull_request:"; echo "    paths-ignore: ['docs/**']"
  echo "  push: { branches: [main], paths-ignore: ['docs/**'] }"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RI2/.github/workflows/docs.yml"
wf "$RI2/.github/workflows/real-drift.yml" '      - "a/**"
      - "b/**"' '      - "a/**"'
expect "a paths-ignore-ONLY workflow is out of scope and does not abort the audit" \
  1 "real-drift.yml" "$RI2"

RG="$(root "$WORK/negated")"
wf "$RG/.github/workflows/w.yml" '      - "a/**"
      - "!a/vendor/**"' '      - "!a/vendor/**"
      - "a/**"'
expect "a negated (!) pattern is REFUSED — order is significant, so set-equality lies" \
  3 "makes ORDER significant" "$RG"

RE="$(root "$WORK/empty-paths")"
{ echo "name: w"; echo "on:"; echo "  pull_request:"; echo "    paths: []"
  echo "  push: { branches: [main], paths: ['a/**'] }"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RE/.github/workflows/w.yml"
expect "an empty paths: list is refused" 3 "is not a non-empty list" "$RE"

RY="$(root "$WORK/bad-yaml")"
printf 'name: w\non: [\n  unterminated\n' > "$RY/.github/workflows/w.yml"
expect "a workflow that will not parse is NO VERDICT, not a clean audit" \
  3 "not parsable as YAML" "$RY"

# =============================================================================================
# 6b. RULE (b) — COVERAGE. A filter naming a project must select what that project is BUILT FROM.
#
#     Sub-class (b) of #880's class: both copies agree perfectly and are both wrong the same way, so
#     rule (a) is satisfied and the gate stays green. It regenerated within three hours of #880
#     closing (#930), which is why it is a rule and not a sixth hand-repair.
# =============================================================================================

# proj <root> <dir> [<include>…] — an MSBuild project, with the given ProjectReference includes.
# The gate reads the project GRAPH and nothing else, so a fixture project needs no sources: these
# five lines are the entire subject.
proj() {
  local r="$1" d="$2"; shift 2
  mkdir -p "$r/$d"
  local name; name="$(basename "$d")"
  { echo '<Project Sdk="Microsoft.NET.Sdk">'
    if [ "$#" -gt 0 ]; then
      echo '  <ItemGroup>'
      for inc in "$@"; do echo "    <ProjectReference Include=\"$inc\" />"; done
      echo '  </ItemGroup>'
    fi
    echo '</Project>'; } > "$r/$d/$name.fsproj"
}

RB="$(root "$WORK/cover-miss")"
proj "$RB" "src/A" "../B/B.fsproj"
proj "$RB" "src/B"
wf "$RB/.github/workflows/w.yml" '      - "src/A/**"' '      - "src/A/**"'
expect "a filter naming a project but omitting what it REFERENCES is caught" \
  1 "nothing in the filter selects 'src/B'" "$RB"

RB2="$(root "$WORK/cover-ok")"
proj "$RB2" "src/A" "../B/B.fsproj"
proj "$RB2" "src/B"
wf "$RB2/.github/workflows/w.yml" '      - "src/A/**"
      - "src/B/**"' '      - "src/A/**"
      - "src/B/**"'
expect "...and covering it satisfies the rule" 0 "ok:" "$RB2"

# CLOSURE, not just direct references. A→B→C with C uncovered is the same fail-open one hop further
# out, and it is the shape the real instance has: coord-engine names Cli, Cli→GitHub→Core.
#
# B IS COVERED BY A PATTERN THAT DOES NOT NAME IT (`**/B/**` has an empty literal prefix), and that
# is the whole construction. The obvious fixture — naming `src/B/**` alongside `src/A/**` — passes
# even when the closure walk is cut to direct references only: B becomes its OWN declared subject, C
# is B's DIRECT reference, and the finding fires without ever crossing the A→B→C hop. It asserted
# transitivity and pinned nothing. Measured: cutting `stack.extend(...)` left it green.
RB3="$(root "$WORK/cover-transitive")"
proj "$RB3" "src/A" "../B/B.fsproj"
proj "$RB3" "src/B" "../C/C.fsproj"
proj "$RB3" "src/C"
wf "$RB3/.github/workflows/w.yml" '      - "src/A/**"
      - "**/B/**"' '      - "src/A/**"
      - "**/B/**"'
expect "the rule follows the CLOSURE — a TRANSITIVE reference is an input too" \
  1 "nothing in the filter selects 'src/C'" "$RB3"

# ACTIONS' GLOBBING, NOT fnmatch's: `*` does not cross `/`. `src/*` selects `src/B` and NOT
# `src/B/B.fsproj`, so it does not cover B — a push to B's source would not trigger the workflow.
# Translating `*` to `.*` (fnmatch's reading, and the obvious shortcut) makes the gate believe the
# dependency is covered and stay green: wrong in the fail-OPEN direction, which is the one that
# costs. This leg is the difference between the two readings.
RBG="$(root "$WORK/cover-glob")"
proj "$RBG" "src/A" "../B/B.fsproj"
proj "$RBG" "src/B"
wf "$RBG/.github/workflows/w.yml" '      - "src/A/**"
      - "src/*"' '      - "src/A/**"
      - "src/*"'
expect "a single \`*\` does not cross \`/\` — \`src/*\` does not cover \`src/B/B.fsproj\`" \
  1 "nothing in the filter selects 'src/B'" "$RBG"

# ...and the mirror of that leg: `**` DOES cross `/`, so `src/nested/**` covers a project any depth
# below it. Both halves need pinning — one reading of `*` is too loose and fails open, and a `**`
# that stopped crossing `/` is too tight and would red a dependency that IS covered. A gate that
# reds correct work is the one workers learn to ignore (#698).
RBG2="$(root "$WORK/cover-glob-deep")"
proj "$RBG2" "src/A" "../nested/B/B.fsproj"
proj "$RBG2" "src/nested/B"
wf "$RBG2/.github/workflows/w.yml" '      - "src/A/**"
      - "src/nested/**"' '      - "src/A/**"
      - "src/nested/**"'
expect "\`**\` DOES cross \`/\` — \`src/nested/**\` covers a project nested below it" \
  0 "ok:" "$RBG2"

# ---- the three false positives the rule's narrowness is measured to prevent -----------------
#
# Each of these fires if "declares a project" is read loosely, and each would red a workflow whose
# subject it is not. A gate that cries wolf on the happy path teaches "FAILED is noise" (#698).

# A CATCH-ALL IS NOT A DECLARATION. This is test-selector-selftest.yml's real shape: it filters on
# `tests/**`, which incidentally selects three .Tests projects it never builds — it runs a shell
# script that greps. Reading that as a declaration drags `src/**` onto it.
RB4="$(root "$WORK/cover-catchall")"
proj "$RB4" "tests/T" "../../src/A/A.fsproj"
proj "$RB4" "src/A"
wf "$RB4/.github/workflows/w.yml" '      - "tests/**"' '      - "tests/**"'
expect "a CATCH-ALL that incidentally selects a project does not declare it as a subject" \
  0 "ok:" "$RB4"

# NAMING ONE FILE IS NOT DECLARING THE PROJECT. recipe-landable.yml watches
# `src/FS.GG.Coord.Cli/Options.fs` because it GREPS it. Watching a file you read is not building the
# project it lives in.
RB5="$(root "$WORK/cover-onefile")"
proj "$RB5" "src/A" "../B/B.fsproj"
proj "$RB5" "src/B"
wf "$RB5/.github/workflows/w.yml" '      - "src/A/Options.fs"' '      - "src/A/Options.fs"'
expect "naming ONE FILE of a project does not declare the project as a build subject" \
  0 "ok:" "$RB5"

# A pattern covering the whole tree needs no obligation — it selects every closure by construction.
RB6="$(root "$WORK/cover-wide")"
proj "$RB6" "src/A" "../B/B.fsproj"
proj "$RB6" "src/B"
wf "$RB6/.github/workflows/w.yml" '      - "src/**"' '      - "src/**"'
expect "a filter covering the whole tree is closed by construction" 0 "ok:" "$RB6"

# ---- rule (b) is NOT the pairing rule, and must not inherit its scope -----------------------
# A one-sided filter is out of (a)'s scope by design, and can still omit a real project.
RB7="$(root "$WORK/cover-onesided")"
proj "$RB7" "src/A" "../B/B.fsproj"
proj "$RB7" "src/B"
{ echo "name: w"; echo "on:"; echo "  push:"; echo "    branches: [main]"
  echo "    paths:"; echo '      - "src/A/**"'
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RB7/.github/workflows/w.yml"
# A SECOND, PAIRED workflow, present only to keep rule (a)'s own vacuity guard quiet: a tree with no
# pair at all is exit 3 by design (§7), and this leg is about (b), not about that refusal.
wf "$RB7/.github/workflows/pair.yml" '      - "docs/**"' '      - "docs/**"'
expect "a ONE-SIDED filter is out of (a)'s scope and still answerable to (b)" \
  1 "the \`push\` filter names 'src/A'" "$RB7"

# ---- rule (b)'s escape hatch ---------------------------------------------------------------
RB8="$(root "$WORK/cover-hatch")"
proj "$RB8" "src/A" "../B/B.fsproj"
proj "$RB8" "src/B"
wf "$RB8/.github/workflows/w.yml" '      - "src/A/**"' '      - "src/A/**"'
sed -i '1i # paths-coherence: allow-uncovered src/B — B is a stub with no compiled surface' \
  "$RB8/.github/workflows/w.yml"
expect "a SIGNED allow-uncovered licenses the omission" 0 "ok:" "$RB8"

RB9="$(root "$WORK/cover-hatch-unsigned")"
proj "$RB9" "src/A" "../B/B.fsproj"
proj "$RB9" "src/B"
wf "$RB9/.github/workflows/w.yml" '      - "src/A/**"' '      - "src/A/**"'
sed -i '1i # paths-coherence: allow-uncovered src/B' "$RB9/.github/workflows/w.yml"
expect "an UNSIGNED allow-uncovered is a finding — it is neither a decision nor a typo" \
  1 "with NO reason" "$RB9"

# THE HATCH NAMES THE PATH, and excusing one omission must not excuse the next (#496). A blanket
# exemption would render identically to a workflow nobody had thought about.
RB10="$(root "$WORK/cover-hatch-narrow")"
proj "$RB10" "src/A" "../B/B.fsproj" "../C/C.fsproj"
proj "$RB10" "src/B"
proj "$RB10" "src/C"
wf "$RB10/.github/workflows/w.yml" '      - "src/A/**"' '      - "src/A/**"'
sed -i '1i # paths-coherence: allow-uncovered src/B — deliberate' "$RB10/.github/workflows/w.yml"
expect "the hatch excuses the path it NAMES and no other" \
  1 "nothing in the filter selects 'src/C'" "$RB10"

# THE MENTION/USE DISCIPLINE, one layer down — a `#` inside a `run: |` is SHELL text, not a YAML
# comment. Honouring it would license a real omission from a line that is not a comment at all.
# This is the trap rule (a)'s hatch took two goes to escape; (b)'s hatch inherits the fix, and this
# leg is what keeps it inherited.
RB11="$(root "$WORK/cover-hatch-shell")"
proj "$RB11" "src/A" "../B/B.fsproj"
proj "$RB11" "src/B"
{ echo "name: w"; echo "on:"
  echo "  pull_request:"; echo "    paths:"; echo '      - "src/A/**"'
  echo "  push:"; echo "    branches: [main]"; echo "    paths:"; echo '      - "src/A/**"'
  echo "jobs:"; echo "  j:"; echo "    runs-on: ubuntu-latest"; echo "    steps:"
  echo "      - run: |"
  echo "          # paths-coherence: allow-uncovered src/B — inside a run block, not a YAML comment"
  echo "          true"; } > "$RB11/.github/workflows/w.yml"
expect "a hatch inside a \`run:\` block is SHELL TEXT and does not license anything" \
  1 "nothing in the filter selects 'src/B'" "$RB11"

# ---- REGRESSION: the real instance, from the real working tree ------------------------------
#
# #930's named instance. coord-engine.yml filtered on Cli/** and Core/** and NOT GitHub/**, while
# Cli references GitHub — so a PR touching only src/FS.GG.Coord.GitHub did not run the engine's own
# gate. Rule (a) certified it: "both triggers agree", perfectly, on a list omitting the subject.
RB12="$(root "$WORK/cover-regression")"
mkdir -p "$RB12/src" "$RB12/tests"
for d in src/FS.GG.Coord.Cli src/FS.GG.Coord.Cli.Kernel src/FS.GG.Coord.Core src/FS.GG.Coord.GitHub tests/FS.GG.Coord.Cli.Tests tests/FS.GG.Coord.Cli.Kernel.Tests; do
  mkdir -p "$RB12/$d"
  cp "$REPO_ROOT/$d/$(basename "$d").fsproj" "$RB12/$d/"
done
undrift "$REPO_ROOT/.github/workflows/coord-engine.yml" \
        "$RB12/.github/workflows/coord-engine.yml" \
        "src/FS.GG.Coord.GitHub/**"
expect "REGRESSION #930: coord-engine.yml's real coverage gap is caught" \
  1 "nothing in the filter selects 'src/FS.GG.Coord.GitHub'" "$RB12"

# ...and the SAME tree, with the file as this PR ships it, passes. The fix is the subject of the
# assertion, not just the bug.
RB13="$(root "$WORK/cover-regression-fixed")"
for d in src/FS.GG.Coord.Cli src/FS.GG.Coord.Cli.Kernel src/FS.GG.Coord.Core src/FS.GG.Coord.GitHub tests/FS.GG.Coord.Cli.Tests tests/FS.GG.Coord.Cli.Kernel.Tests; do
  mkdir -p "$RB13/$d"
  cp "$REPO_ROOT/$d/$(basename "$d").fsproj" "$RB13/$d/"
done
cp "$REPO_ROOT/.github/workflows/coord-engine.yml" "$RB13/.github/workflows/"
expect "...and coord-engine.yml, as this PR ships it, passes" 0 "ok:" "$RB13"

# =============================================================================================
# 6c. RULE (c) — A GATE SCRIPT'S DECLARED SURFACE (#996).
#
#     (b)'s shape over a different graph: the workflow's `paths:` names the script, the script's AST
#     supplies the closure. The declaration is `PATHS_SUBJECT`, and it is opt-in — a script that
#     declares nothing is silent, so the rule cannot blanket-red an unmigrated workflow (#698).
# =============================================================================================

# gate <root> <path> <PATHS_SUBJECT expr> [extra-lines…] — a gate script that declares a surface.
# The expression is written verbatim, so a leg can pin the AST reader's folding as well as the rule.
gate() {
  local r="$1" f="$2" expr="$3"; shift 3
  mkdir -p "$r/$(dirname "$f")"
  { echo '#!/usr/bin/env python3'
    echo '"""a fixture gate."""'
    for line in "$@"; do echo "$line"; done
    echo "PATHS_SUBJECT = $expr"
    echo 'print("ok")'; } > "$r/$f"
}

RC="$(root "$WORK/subject-miss")"
gate "$RC" "scripts/check-x.py" '("docs", "src")'
mkdir -p "$RC/docs" "$RC/src"
wf "$RC/.github/workflows/w.yml" '      - "docs/**"
      - "scripts/check-x.py"' '      - "docs/**"
      - "scripts/check-x.py"'
expect "a filter naming a gate SCRIPT but omitting what it READS is caught" \
  1 "nothing in the filter selects 'src'" "$RC"

RC2="$(root "$WORK/subject-ok")"
gate "$RC2" "scripts/check-x.py" '("docs", "src")'
mkdir -p "$RC2/docs" "$RC2/src"
wf "$RC2/.github/workflows/w.yml" '      - "docs/**"
      - "src/**"
      - "scripts/check-x.py"' '      - "docs/**"
      - "src/**"
      - "scripts/check-x.py"'
expect "...and covering the declared surface satisfies the rule" 0 "ok:" "$RC2"

# EXTENSIONLESS PYTHON IS STILL A GATE SCRIPT (#1639). The executable declaration is the shebang,
# not the fact that arbitrary text can happen to parse as Python. This pair pins both halves: the
# extensionless/shebang form attaches and finds the omission, while the same valid Python without a
# shebang remains outside rule (c).
RC2a="$(root "$WORK/subject-extensionless-python")"
gate "$RC2a" "scripts/check-x" '("docs", "src")'
mkdir -p "$RC2a/docs" "$RC2a/src"
wf "$RC2a/.github/workflows/w.yml" '      - "docs/**"
      - "scripts/check-x"' '      - "docs/**"
      - "scripts/check-x"'
expect "an extensionless PYTHON-SHEBANG gate attaches to rule (c)" \
  1 "nothing in the filter selects 'src'" "$RC2a"

RC2b="$(root "$WORK/subject-extensionless-no-shebang")"
mkdir -p "$RC2b/scripts" "$RC2b/docs" "$RC2b/src"
printf 'PATHS_SUBJECT = ("docs", "src")\nprint("valid Python, not an executable declaration")\n' \
  > "$RC2b/scripts/check-x"
wf "$RC2b/.github/workflows/w.yml" '      - "docs/**"
      - "scripts/check-x"' '      - "docs/**"
      - "scripts/check-x"'
expect "a shebang-less extensionless file does NOT attach merely because it parses as Python" \
  0 "ok:" "$RC2b"

# THE CONSTANT IS COMPOSED, AND THE READER MUST FOLD IT. This is not a convenience: a reader that
# only took literals would force `PATHS_SUBJECT` to be a RETYPED copy of the surface, free to drift
# from the constants beside it — #865, the cure reintroducing the disease. Every real declaration in
# this repo is a sum of the script's own walk bounds, so a reader that cannot fold `+` and names
# would silently see NO declaration and rule (c) would audit nothing.
RC3="$(root "$WORK/subject-composed")"
gate "$RC3" "scripts/check-x.py" 'DOCS + CODE + (DECL,)' 'DOCS = ("docs",)' 'CODE = ("src",)' 'DECL = ".roots"'
mkdir -p "$RC3/docs" "$RC3/src"; touch "$RC3/.roots"
wf "$RC3/.github/workflows/w.yml" '      - "docs/**"
      - "src/**"
      - "scripts/check-x.py"' '      - "docs/**"
      - "src/**"
      - "scripts/check-x.py"'
expect "a COMPOSED PATHS_SUBJECT is folded — names and \`+\`, not just literals" \
  1 "nothing in the filter selects '.roots'" "$RC3"

# A FILE IS NOT A DIRECTORY, and the remedy must be one that works. `.roots` is a file; telling
# somebody to add `.roots/**` is advice matching nothing, and the gate would keep reding after they
# did exactly as told.
expect "...and the fix it names for a FILE is the file, not a bogus \`/**\`" \
  1 "Add a pattern covering '.roots'." "$RC3"

# PATHS_SUBJECT's filesystem disposition decides the probe, not whether an entry happens to exist
# as a file. Each leg starts green with the one correct filter shape, then removes that entry from
# both filters and requires the resulting finding. That is a mutation check of all three states:
# existing directory, existing file, and a file that may appear later (#1873).
RC3a="$(root "$WORK/subject-existing-directory-mutation")"
gate "$RC3a" "scripts/check-x.py" '("docs",)'
mkdir -p "$RC3a/docs"
wf "$RC3a/.github/workflows/w.yml" '      - "docs/**"
      - "scripts/check-x.py"' '      - "docs/**"
      - "scripts/check-x.py"'
expect "an existing DIRECTORY subject is covered by a recursive filter" 0 "ok:" "$RC3a"
sed -i '/"docs\/\*\*"/d' "$RC3a/.github/workflows/w.yml"
expect "MUTATION: removing an existing DIRECTORY subject reds" \
  1 "nothing in the filter selects 'docs'" "$RC3a"

RC3b="$(root "$WORK/subject-existing-file-mutation")"
gate "$RC3b" "scripts/check-x.py" '(".roots",)'
touch "$RC3b/.roots"
wf "$RC3b/.github/workflows/w.yml" '      - ".roots"
      - "scripts/check-x.py"' '      - ".roots"
      - "scripts/check-x.py"'
expect "an existing FILE subject is covered by its exact filter" 0 "ok:" "$RC3b"
sed -i '/"\.roots"/d' "$RC3b/.github/workflows/w.yml"
expect "MUTATION: removing an existing FILE subject reds" \
  1 "nothing in the filter selects '.roots'" "$RC3b"

RC3c="$(root "$WORK/subject-may-not-exist-mutation")"
gate "$RC3c" "scripts/check-x.py" '("future-config.json",)'
wf "$RC3c/.github/workflows/w.yml" '      - "future-config.json"
      - "scripts/check-x.py"' '      - "future-config.json"
      - "scripts/check-x.py"'
expect "a MAY-NOT-EXIST subject is covered by its exact future-file filter" 0 "ok:" "$RC3c"
sed -i '/"future-config.json"/d' "$RC3c/.github/workflows/w.yml"
expect "MUTATION: removing a MAY-NOT-EXIST subject reds" \
  1 "nothing in the filter selects 'future-config.json'" "$RC3c"

# OPT-IN: a script declaring nothing is out of scope and SILENT. A rule that red every unmigrated
# workflow would be a rule everyone turns off on day one (#698).
RC4="$(root "$WORK/subject-none")"
mkdir -p "$RC4/scripts" "$RC4/src"
printf '#!/usr/bin/env python3\nprint("no declaration here")\n' > "$RC4/scripts/check-x.py"
wf "$RC4/.github/workflows/w.yml" '      - "scripts/check-x.py"' '      - "scripts/check-x.py"'
expect "a script that declares NO surface is out of scope, not a finding" 0 "ok:" "$RC4"

# THE LINKAGE IS THE EXACT NAME, per (b)'s narrowness. `scripts/**` is a workflow watching a
# directory, not naming a gate — reading it as one would impose EVERY declaring script's surface on
# any workflow that watches scripts/.
RC5="$(root "$WORK/subject-catchall")"
gate "$RC5" "scripts/check-x.py" '("docs", "src")'
mkdir -p "$RC5/docs" "$RC5/src"
wf "$RC5/.github/workflows/w.yml" '      - "scripts/**"' '      - "scripts/**"'
expect "a CATCH-ALL over scripts/ does not name a gate — no surface is imposed" 0 "ok:" "$RC5"

# ---- the AST reader REFUSES rather than guesses (#266) --------------------------------------
RC6="$(root "$WORK/subject-unfoldable")"
gate "$RC6" "scripts/check-x.py" 'compute_surface()' 'def compute_surface(): return ("docs",)'
mkdir -p "$RC6/docs"
wf "$RC6/.github/workflows/w.yml" '      - "scripts/check-x.py"' '      - "scripts/check-x.py"'
expect "a PATHS_SUBJECT the reader cannot fold is NO VERDICT, never a guess" \
  3 "not a literal, a module-level constant" "$RC6"

# ONE BINDING, AT MODULE LEVEL, OR NO VERDICT. Both shapes below were read WRONG by the first draft,
# which scanned only `tree.body` and took the first hit — and each fails in the direction this reader
# exists to prevent.
#
# Bound twice: the reader took the first and Python takes the last, so the gate would have checked a
# surface the script does not walk — confidently, which is the one thing it may not do.
RC6b="$(root "$WORK/subject-rebound")"
gate "$RC6b" "scripts/check-x.py" '("docs",)' 'X = 1'
printf 'PATHS_SUBJECT = ("src",)\n' >> "$RC6b/scripts/check-x.py"
mkdir -p "$RC6b/docs" "$RC6b/src"
wf "$RC6b/.github/workflows/w.yml" '      - "docs/**"
      - "scripts/check-x.py"' '      - "docs/**"
      - "scripts/check-x.py"'
expect "PATHS_SUBJECT bound TWICE is refused — the first is not the value the script uses" \
  3 "is bound 2 time(s)" "$RC6b"

# Bound conditionally: invisible to a body-only scan, so it read as "declares nothing" and rule (c)
# SILENTLY stopped applying. A skip is how a coherence gate fails open (#266).
RC6c="$(root "$WORK/subject-conditional")"
mkdir -p "$RC6c/scripts" "$RC6c/docs" "$RC6c/src"
{ echo '#!/usr/bin/env python3'
  echo 'import os'
  echo 'if os.environ.get("X"):'
  echo '    PATHS_SUBJECT = ("docs",)'
  echo 'else:'
  echo '    PATHS_SUBJECT = ("src",)'; } > "$RC6c/scripts/check-x.py"
wf "$RC6c/.github/workflows/w.yml" '      - "docs/**"
      - "scripts/check-x.py"' '      - "docs/**"
      - "scripts/check-x.py"'
expect "a CONDITIONAL PATHS_SUBJECT is refused, never read as 'declares nothing'" \
  3 "0 of them at module level" "$RC6c"

RC7="$(root "$WORK/subject-forward-ref")"
gate "$RC7" "scripts/check-x.py" 'LATER'
echo 'LATER = ("docs",)' >> "$RC7/scripts/check-x.py"   # defined BELOW the declaration
mkdir -p "$RC7/docs"
wf "$RC7/.github/workflows/w.yml" '      - "scripts/check-x.py"' '      - "scripts/check-x.py"'
expect "a name defined BELOW PATHS_SUBJECT is refused, not read as empty" \
  3 "is not a module-level literal defined above it" "$RC7"

# THE GATE NEVER EXECUTES ITS SUBJECT. It runs on PRs that are editing these very scripts, so
# importing one would run whatever the PR wrote, at gate time. A script whose import would BLOW UP
# must still be read.
RC8="$(root "$WORK/subject-no-exec")"
gate "$RC8" "scripts/check-x.py" '("docs",)' 'raise SystemExit("if you are reading this, the gate EXECUTED me")'
mkdir -p "$RC8/docs"
wf "$RC8/.github/workflows/w.yml" '      - "docs/**"
      - "scripts/check-x.py"' '      - "docs/**"
      - "scripts/check-x.py"'
expect "the reader PARSES and never executes — a script that would die on import still reads" \
  0 "ok:" "$RC8"

# ---- REGRESSION: both real instances, from the real working tree -----------------------------
#
# #996's instance 7. worker-id-attractor.yml omitted `src/**` while check-worker-id-attractor.py
# WALKS src/ (its CODE_SURFACE) — so a PR reintroducing the `command not found` regression #569
# wrote that gate to catch, touching only src/, did not run it.
RC9="$(root "$WORK/subject-regression-attractor")"
mkdir -p "$RC9/scripts" "$RC9/docs" "$RC9/src"; touch "$RC9/.agent-skill-roots"
cp "$REPO_ROOT/scripts/check-worker-id-attractor.py" "$RC9/scripts/"
undrift "$REPO_ROOT/.github/workflows/worker-id-attractor.yml" \
        "$RC9/.github/workflows/worker-id-attractor.yml" "src/**" "scripts/**"
expect "REGRESSION #996: worker-id-attractor.yml's real omission of its CODE_SURFACE is caught" \
  1 "nothing in the filter selects 'src'" "$RC9"

# ...and the instance the rule was NOT written against. check-recipe-pagination.py reads
# `.agent-skill-roots` to decide which roots to scan — its own docstring says a second copy of that
# list would be "the #266 fail-open this gate exists to close". Its TRIGGER omitted the file, so a
# root added there did not re-run it and the new root's recipes went unaudited.
RC10="$(root "$WORK/subject-regression-pagination")"
mkdir -p "$RC10/scripts" "$RC10/.claude/skills" "$RC10/.agents/skills"; touch "$RC10/.agent-skill-roots"
cp "$REPO_ROOT/scripts/check-recipe-pagination.py" "$RC10/scripts/"
undrift "$REPO_ROOT/.github/workflows/recipe-pagination.yml" \
        "$RC10/.github/workflows/recipe-pagination.yml" ".agent-skill-roots"
expect "REGRESSION #996: recipe-pagination.yml omitting the file that DECIDES its roots is caught" \
  1 "nothing in the filter selects '.agent-skill-roots'" "$RC10"

# =============================================================================================
# 7. FAIL CLOSED (#266). Examining nothing is a failure to audit, not a clean audit — and this is
#    the leg that matters most, because a broken trigger reader finds zero pairs and reports green
#    over a repo full of them.
# =============================================================================================

# RULE (b)'s VACUITY LEG, AND IT LIVES HERE RATHER THAN IN THE GATE — see the long comment at the
# gate's own #266 guard. "Projects exist but no workflow names one" is a LEGITIMATE tree (a repo
# whose workflows all filter on catch-alls), so the gate may not refuse it; three legs above are
# exactly that shape. The exposure is still real: if the prefix reader went blind, subjects would
# drop to zero and the gate would print the same green it prints when everything is covered.
#
# So assert it where the repo IS known: the shipped tree declares subjects, and the number is not
# zero. A reader that stops seeing them fails HERE, against real files, instead of passing quietly.
out="$(python3 "$TOOL" --root "$REPO_ROOT" 2>&1)" || true
n="$(sed -n 's/.*agree; \([0-9]*\) declared project subject(s).*/\1/p' <<<"$out")"
if [ -n "$n" ] && [ "$n" -gt 0 ] 2>/dev/null; then
  ok "the shipped tree declares $n project subject(s) — rule (b) is auditing something"
else
  bad "rule (b) audited NOTHING on the shipped tree — the prefix reader is blind (#266)" "$out"
fi

# RULE (c)'s VACUITY LEG, and it guards a footgun the rule creates. (c) attaches to a workflow that
# names its script EXACTLY, so deleting that one pattern silently unlinks the rule — `scripts/**`
# still triggers the workflow, and the coverage it was enforcing quietly stops being enforced. That
# is not hypothetical: this PR's own first repair of worker-id-attractor.yml replaced the script
# pattern with `scripts/**` and dropped the count from 4 to 3, unnoticed but for this number.
#
# So the shipped count is asserted, and asserted EXACTLY. ">0" would have been satisfied by that
# broken repair. If you migrate another gate, this number goes up and this line is the one to edit.
#
# 4 -> 5: repo-filter-monopoly.yml (.github#979) declares a PATHS_SUBJECT and names its gate script
# exactly, so rule (c) attaches to it and the census counts it. The tripwire fired on the very first
# CI run of that gate, which is the leg working as designed rather than a cost of it.
#
# 5 -> 6: recipe-followup.yml (.github#1073) names scripts/check-recipe-followup.py exactly, the same way
# recipe-landable.yml names its own gate — so rule (c) attaches and the census counts the new surface.
#
# 6 -> 7: sparse-checkout-closure.yml (.github#1522) names scripts/check-sparse-checkout-closure.py
# exactly and that gate declares a PATHS_SUBJECT, so rule (c) attaches. This leg fired on the new
# gate's first local run — the tripwire doing its job, exactly as it did for repo-filter-monopoly.
#
# 7 -> 8: repos-audit-selftest.yml (.github#1529) ALSO names scripts/check-sparse-checkout-closure.py
# exactly — the second workflow to name that one script, and the first surface here that is not a
# workflow running its gate. #1529 gave that gate's rule roster-wide reach by having repos-audit.sh
# IMPORT it, and the selftest must re-run when the imported rule changes or the sharing buys reach at
# the price of a gate that never runs. So rule (c) attaches to an importer, and the census counts it.
# The selftest carries a signed `allow-uncovered .github/workflows` for exactly that reason: the
# script is named there as a library, not as a gate over this tree. This leg fired on the first CI
# run of that PR — the tripwire doing its job for the third time.
# 8 -> 9: ignored-author-coherence.yml (.github#1538) names scripts/check-ignored-author-coherence.py
# exactly and that gate declares a PATHS_SUBJECT — `(.github/workflows, default.json)`, the two halves
# of the comparison it makes — so rule (c) attaches and the census counts it. This leg fired on that
# gate's first CI run, the tripwire doing its job for the fourth time.
# 9 -> 10: skillmirror-freshness.yml (.github#1546) names scripts/check-skillmirror-freshness.py
# exactly and that gate declares a PATHS_SUBJECT — `(tests/skill-union/skillmirror.fixtures.json,)`,
# the conformance table whose freshness IS its whole subject — so rule (c) attaches and the census
# counts it. This leg fired on that gate's first local run, the tripwire doing its job for the fifth
# time; the entry it demanded was then DELETED again to watch rule (c) name the missing pattern, so
# the coverage is measured in both directions rather than inferred from a green.
# 10 -> 11: preset-repo-scope-coherence.yml (.github#1552) names
# scripts/check-preset-repo-scope-coherence.py exactly and that gate declares a PATHS_SUBJECT —
# `(default.json, registry/repos.yml)`, the two halves of the comparison it makes — so rule (c)
# attaches and the census counts it. This leg fired on that gate's first CI run, the tripwire doing
# its job for the sixth time; `registry/repos.yml` was then deleted from the workflow's filter to
# watch rule (c) name the missing pattern, so the coverage is measured in both directions rather
# than inferred from a green.
# 11 -> 12 on 2026-07-29 (#1802): `scripts/check-pin-coherence.py` declared a PATHS_SUBJECT, so rule
# (c) attaches to `pin-coherence.yml` and the census counts it. THE TRIPWIRE FIRED AS DESIGNED — this
# leg went red on that PR's first CI run, which is the seventh time it has caught a surface changing.
# 12 -> 13 on 2026-07-29 (#1923): `scripts/dashboard-tick.py` — the kit/engine push half — declares
# a PATHS_SUBJECT of `(ROSTER_SCRIPT,)`, composed from the constant it actually shells out to for the
# receiver set, so rule (c) attaches to `dashboard-tick-selftest.yml` and the census counts it. THE
# TRIPWIRE FIRED AS DESIGNED — this leg went red on that PR's first CI run after the declaration
# landed, the EIGHTH time it has caught a surface changing, and it caught it before the workflow
# reached `main`.
# 13 -> 14 on 2026-07-29 (#1750): `scripts/check-retirement-order-coherence.py` declares a
# PATHS_SUBJECT of `(ORDER, ROSTER, ROOTS)` — the retirement order it grades, the roster it reads the
# receiver set from, and `scripts/skill-view`, whose `DEFAULT_ROOTS` decides which roots every receiver
# is graded against — so rule (c) attaches to `retirement-order-coherence.yml` and the census counts it.
# THE TRIPWIRE FIRED AS DESIGNED — this leg went red on that PR's first CI run, the NINTH time it has
# caught a surface changing, and again before the workflow reached `main`.
# 14 -> 18 on 2026-07-29 (#1639): extensionless Python gates now attach structurally through their
# Python shebang. `scripts/generate-driver-manifest` declares one PATHS_SUBJECT and is named exactly
# by drivers-package.yml, skill-quality.yml, skill-registry-coherence.yml, and
# skill-roots-selfcheck.yml, so all FOUR workflow/script surfaces join the census at once. Three
# filters were widened to cover the declaration; skill-registry-coherence was the already-complete
# control. This is the first census move caused by fixing the linkage itself rather than adding a
# declaration.
# 18 -> 19 on 2026-07-29 (#1762): engine-release-notes.yml names the new release-notes checker,
# which declares the engine project as its structural subject.
# 19 -> 20 on 2026-08-13 (#2521): skillmirror-redrive.yml names `scripts/skillmirror-redrive.py`,
# which declares a PATHS_SUBJECT of `(TABLE, ORACLE, ORACLE_BODY)` — the conformance table it re-pins
# and both halves of the derivation it drives — so rule (c) attaches and the census counts it. The
# coverage was measured in both directions before this number moved: `tests/skill-union/skillmirror-
# oracle.fsx` was deleted from that workflow's two filters, rule (c) named it as the omission, and it
# was restored.
# The number is the point: it is a census, not a threshold, so it moves only with a reviewed change
# that adds or removes a declaration, and a workflow that silently STOPS naming its gate still reds.
s="$(sed -n 's/.*closure; \([0-9]*\) declared gate script surface(s).*/\1/p' <<<"$out")"
if [ "${s:-0}" = "20" ]; then
  ok "the shipped tree links $s gate script surface(s) — rule (c) is auditing all of them"
else
  bad "rule (c) links ${s:-0} gate script surface(s), want exactly 20 — a workflow stopped naming its gate (#996)" "$out"
fi

RZ="$(root "$WORK/no-pairs")"
{ echo "name: w"; echo "on: { workflow_dispatch: }"
  echo "jobs: { j: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RZ/.github/workflows/w.yml"
expect "a tree where NO workflow declares both filters is NO VERDICT, not OK" \
  3 "examining nothing is a failure to audit" "$RZ"

RQ="$(root "$WORK/empty-dir")"
expect "an empty workflows dir is refused" 3 "contains no workflow files" "$RQ"

expect "a root with no .github/workflows at all is refused" \
  3 "is not a directory" "$WORK/nonexistent"

# =============================================================================================
# 8. The exit-code contract is only worth anything if the gate cannot reach the network: exit 2
#    ("no verdict, retryable") is absent from its vocabulary, which is a lie if it can make a call.
# =============================================================================================
if python3 - "$TOOL" <<'PY'
import ast, sys
banned = {"urllib", "http", "socket", "requests", "subprocess", "ssl", "ftplib", "telnetlib"}
tree = ast.parse(open(sys.argv[1], encoding="utf-8").read())
names = []
for node in ast.walk(tree):
    if isinstance(node, ast.Import):
        names += [a.name for a in node.names]
    elif isinstance(node, ast.ImportFrom) and node.module:
        names.append(node.module)
for n in names:
    assert n.split(".")[0] not in banned, f"the gate imports {n} — it is not static"
PY
then ok "the gate imports no transport (urllib/http/socket/requests/subprocess) — so exit 2 is a verdict it can never mean"
else bad "the gate imports a transport module — it can reach the network, and its exit-code contract is a lie"
fi

# =============================================================================================
# 9. The gate's own shipped surface.
# =============================================================================================
if python3 - "$REPO_ROOT/.github/workflows/paths-coherence.yml" <<'PY'
import sys, yaml
d = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
perms = d.get("permissions")
assert isinstance(perms, dict) and perms.get("contents") == "read", f"top-level permissions: {perms}"
body = "".join(str(s.get("run", "")) for j in d["jobs"].values() for s in j.get("steps", []))
assert "check-paths-coherence.py" in body, "the gate workflow never runs the gate"
assert "tests/paths-coherence/run.sh" in body, "the gate workflow never runs this fixture"
for jid, j in d["jobs"].items():
    assert isinstance(j.get("timeout-minutes"), int), f"job {jid} does not bound itself"
# Every exit code the gate can return must have a step that classifies it. An unclassified code is
# how "I could not check" gets read as "I checked, and it's fine" (#266).
ifs = "".join(str(s.get("if", "")) for j in d["jobs"].values() for s in j.get("steps", []))
for rc in ("'0'", "'1'", "'3'"):
    assert rc in ifs, f"no step classifies exit {rc}"
assert "inconclusive" in body.lower(), "no step catches an exit code the workflow does not understand"
PY
then ok "the shipped paths-coherence.yml declares contents: read, bounds its jobs, runs both the gate and this fixture, and classifies every exit code"
else bad "the shipped paths-coherence.yml is not the shape this fixture asserts"
fi

echo
echo "paths-coherence fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::paths-coherence fixture FAILED"; exit 1; }
echo "paths-coherence fixture — OK"
