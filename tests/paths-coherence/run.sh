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
# 7. FAIL CLOSED (#266). Examining nothing is a failure to audit, not a clean audit — and this is
#    the leg that matters most, because a broken trigger reader finds zero pairs and reports green
#    over a repo full of them.
# =============================================================================================
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
