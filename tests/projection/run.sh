#!/usr/bin/env bash
# Fixture for scripts/check-projection.py — the gate that keeps docs/registry/compatibility.md a
# faithful projection of registry/dependencies.yml (.github#128 H4; hardened by .github#268, epic
# #266 "coherence gates that fail open").
#
# The bug this fixture exists to keep dead: the gate matched a registry version literal as a plain
# SUBSTRING of the whole projection row, so `0.4.0` was satisfied by `0.4.0-preview.1`, and a row
# left describing the prerelease passed a check for the stable version.
#
# WHAT CHANGED (#1081 option 2, DECIDED 2026-07-17 — ADR-0044 / #527). The version literal was split
# out of the hand-authored `Version` CELL of the "Versioned contracts" table into a MACHINE-OWNED,
# generated `## Contract version literals` region, so a registry flip regenerates green instead of
# demanding a prose edit `feed-autofix` is forbidden to make (#748). The gate now reads the literal
# from THAT region's `version` / `package-version` columns; the prose cell is no longer gated. So the
# fixture drives the new region — and adds two cases the split makes newly meaningful: the prose cell
# may now drift without failing, and the new `package-version` column is fail-closed like the rest.
#
# The two discriminating regressions below (`stale prerelease` and `wrong column`) are still the
# point of the file: BOTH pass under a substring test and MUST fail now.
#
# Throwaway trees under a temp dir, no network. Mirrors tests/repos-registry/run.sh.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
CHECK="$HERE/../../scripts/check-projection.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/projection-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# The contract-version producer is exercised from a throwaway repository-shaped tree. Copying the
# producer keeps the fixture off the live skill roots while still executing the exact shipped command.
# The two comparisons are the acceptance boundary: changing canonical skill bytes MUST move the
# version, while re-running after an unchanged-source regeneration MUST preserve it.
CONTRACT_REPO="$WORK/contract-repo"
mkdir -p "$CONTRACT_REPO/scripts" \
  "$CONTRACT_REPO/.agents/skills/demo" \
  "$CONTRACT_REPO/.claude/skills/demo"
git -C "$CONTRACT_REPO" init -q
cp "$REPO_ROOT/scripts/generate-projections" "$CONTRACT_REPO/scripts/generate-projections"
cp "$REPO_ROOT/scripts/fsgg-coord" "$CONTRACT_REPO/scripts/fsgg-coord"
printf '%s\n' 'demo contract' > "$CONTRACT_REPO/.agents/skills/demo/SKILL.md"
cp "$CONTRACT_REPO/.agents/skills/demo/SKILL.md" \
  "$CONTRACT_REPO/.claude/skills/demo/SKILL.md"

REG="$WORK/registry.yml"
cat > "$REG" <<'YAML'
schemaVersion: 2
contracts:
  - id: demo
    version: "0.4.0"
    package-version: "0.4.0"
    owner: sdd
    surface: "demo surface"
    consumers: []
  - id: quad
    version: "1.2.1.1"
    owner: governance
    surface: "four-segment version (ADR-0007)"
    consumers: []
  - id: bare
    version: "1"
    owner: governance
    surface: "schema-integer version (quoted, as the real registry quotes every version)"
    consumers: []
coherence:
  - { id: demo-coh, coherent: true,  owner: sdd }
  - { id: bad-coh,  coherent: false, owner: sdd }
YAML

# The well-formed projection. The gate reads the GENERATED `## Contract version literals` table (each
# literal in its own column); the `## Versioned contracts` table is human-owned prose the gate no
# longer inspects — it carries `PRIOR …` history exactly as the real file does.
BASE="$WORK/base.md"
cat > "$BASE" <<'MD'
# Compatibility

## Coherence state

| Id | Coherent? | Owner | Summary |
|---|---|---|---|
| `demo-coh` | ✅ yes | SDD | holds |
| `bad-coh` | ❌ no | SDD | standing request |

## Versioned contracts

| Contract | Version | Owner | Surface | Consumers |
|---|---|---|---|---|
| `demo` | 0.4.0 (source) / 0.4.0 (published) | SDD | demo surface. PRIOR 0.3.1-preview.1, 0.3.0-preview.1. | — |
| `quad` | 1.2.1.1 | Governance | four-segment | — |
| `bare` | 1 | Governance | schema-integer | — |

## Contract version literals

| Contract | Owner | version | package-version |
|---|---|---|---|
| `demo` | SDD | 0.4.0 | 0.4.0 |
| `quad` | Governance | 1.2.1.1 | — |
| `bare` | Governance | 1 | — |
MD

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# variant <name> <sed-expr> — copy BASE, apply one mutation, echo the path
variant() { local f="$WORK/$1.md"; sed "$2" "$BASE" > "$f"; printf '%s' "$f"; }

# expect_pass <name> <projection> [registry]
expect_pass() {
  local n="$1" proj="$2" reg="${3:-$REG}" out
  if out="$(python3 "$CHECK" "$reg" "$proj" 2>&1)"; then ok "$n"
  else bad "$n" "$out"; fi
}
# expect_fail <name> <substr> <projection> [registry]
expect_fail() {
  local n="$1" substr="$2" proj="$3" reg="${4:-$REG}" out rc=0
  out="$(python3 "$CHECK" "$reg" "$proj" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then bad "$n" "expected failure, gate passed:
$out"; return; fi
  if [ "$rc" -ne 1 ]; then bad "$n" "expected exit 1, got $rc: $out"; return; fi
  case "$out" in *"$substr"*) ok "$n" ;;
                 *) bad "$n" "exit 1 but missing substring '$substr':
$out" ;; esac
}
# substring_would_have_passed <projection> [registry] — emulate the OLD assertion (literal anywhere
# in the row of the literals table). Used to prove a case actually DISCRIMINATES, rather than failing
# for an unrelated reason.
substring_would_have_passed() {
  python3 - "${2:-$REG}" "$1" <<'PY'
import re, sys, yaml
doc = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
md = open(sys.argv[2], encoding="utf-8").read()
lines = md.splitlines()
start = next(i for i, l in enumerate(lines) if l.strip().lower() == "## contract version literals")
i = start + 1
while not lines[i].lstrip().startswith("|"):
    i += 1
rows = {}
for r in lines[i + 2:]:
    if not r.lstrip().startswith("|"):
        break
    m = re.match(r"\s*\|\s*`([^`]+)`", r)
    if m:
        rows[m.group(1)] = r
for c in doc.get("contracts") or []:
    row = rows.get(str(c.get("id")))
    if row is None:
        sys.exit(1)
    for f in ("version", "package-version"):
        v = c.get(f)
        if v is not None and str(v) not in row:
            sys.exit(1)   # old gate would have caught it too — not a discriminating case
sys.exit(0)               # old whole-row substring gate was GREEN here
PY
}
# expect_regression <name> <substr> <projection> [registry] — must fail NOW, and must have passed
# BEFORE.
expect_regression() {
  local n="$1" substr="$2" proj="$3" reg="${4:-$REG}"
  expect_fail "$n" "$substr" "$proj" "$reg"
  if substring_would_have_passed "$proj" "$reg"; then
    ok "$n — and the old substring test was green here (non-vacuous)"
  else
    bad "$n" "old substring test ALSO failed — this case does not discriminate, so it does not
pin the .github#268 regression. Pick a case the substring gate waved through."
  fi
}

echo "projection fixture"

contract_base="$("$CONTRACT_REPO/scripts/generate-projections" --contract-version)"
# Model the generator's write boundary: replace a canonical skill with freshly emitted, byte-identical
# content. Metadata may move; the contract bytes and therefore the version must not.
cp "$CONTRACT_REPO/.agents/skills/demo/SKILL.md" "$WORK/regenerated-skill.md"
cp "$WORK/regenerated-skill.md" "$CONTRACT_REPO/.agents/skills/demo/SKILL.md"
contract_regenerated="$("$CONTRACT_REPO/scripts/generate-projections" --contract-version)"
if [ "$contract_regenerated" = "$contract_base" ]; then
  ok "agent contract version is stable under unchanged-source regeneration"
else
  bad "agent contract version is stable under unchanged-source regeneration" \
    "baseline $contract_base, regenerated $contract_regenerated"
fi

mv "$CONTRACT_REPO/.claude/skills" "$CONTRACT_REPO/.claude/skills-missing"
contract_missing_out=""
contract_missing_rc=0
contract_missing_out="$("$CONTRACT_REPO/scripts/generate-projections" --contract-version 2>&1)" \
  || contract_missing_rc=$?
if [ "$contract_missing_rc" -ne 0 ] && [[ "$contract_missing_out" == *"root is missing or unreadable"* ]]; then
  ok "agent contract version refuses an unreadable skill root"
else
  bad "agent contract version refuses an unreadable skill root" \
    "exit $contract_missing_rc: $contract_missing_out"
fi
mv "$CONTRACT_REPO/.claude/skills-missing" "$CONTRACT_REPO/.claude/skills"

if [[ "$contract_base" =~ ^[0-9a-f]{64}$ ]]; then
  ok "agent contract version is one lowercase SHA-256 digest"
else
  bad "agent contract version is one lowercase SHA-256 digest" "$contract_base"
fi

printf '%s\n' 'canonical byte mutation' >> "$CONTRACT_REPO/.agents/skills/demo/SKILL.md"
contract_changed="$("$CONTRACT_REPO/scripts/generate-projections" --contract-version)"
if [ "$contract_changed" != "$contract_base" ]; then
  ok "agent contract version moves when a canonical skill root changes"
else
  bad "agent contract version moves when a canonical skill root changes" \
    "mutation survived with version $contract_changed"
fi

rm "$CONTRACT_REPO/.claude/skills/demo/SKILL.md"
rmdir "$CONTRACT_REPO/.claude/skills/demo"
empty_root_out=""
empty_root_rc=0
empty_root_out="$("$CONTRACT_REPO/scripts/generate-projections" --contract-version 2>&1)" \
  || empty_root_rc=$?
if [ "$empty_root_rc" -ne 0 ] && [[ "$empty_root_out" == *"root contains no files"* ]]; then
  ok "agent contract version refuses an empty skill root"
else
  bad "agent contract version refuses an empty skill root" "exit $empty_root_rc: $empty_root_out"
fi
mkdir -p "$CONTRACT_REPO/.claude/skills/demo"
cp "$CONTRACT_REPO/.agents/skills/demo/SKILL.md" \
  "$CONTRACT_REPO/.claude/skills/demo/SKILL.md"

FAKE_ENGINE="$WORK/contract-env-engine"
printf '%s\n' '#!/usr/bin/env bash' 'printf "%s\\n" "${FSGG_AGENT_CONTRACT_VERSION:-missing}"' > "$FAKE_ENGINE"
chmod +x "$FAKE_ENGINE"
shim_version="$(cd "$CONTRACT_REPO" && FSGG_COORD_ENGINE_BIN="$FAKE_ENGINE" scripts/fsgg-coord probe)"
producer_version="$("$CONTRACT_REPO/scripts/generate-projections" --contract-version)"
if [ "$shim_version" = "$producer_version" ]; then
  ok "coordination dispatch exports the sole producer's agent contract version"
else
  bad "coordination dispatch exports the sole producer's agent contract version" \
    "shim $shim_version, producer $producer_version"
fi

# --- happy path ---
expect_pass "well-formed projection passes" "$BASE"

# --- #1081 OPTION 2: the split itself -------------------------------------------------------
# The literal is read from the GENERATED region, not the prose cell — so the prose `Version` cell may
# drift (a human's judgement is theirs to update) without reddening the required gate. This is the
# behaviour the split BUYS, and the reason the bot's flip can go green without writing prose.
expect_pass "prose Version cell may drift — it is no longer gated" \
  "$(variant prosedrift 's#| `demo` | 0.4.0 (source) / 0.4.0 (published) |#| `demo` | 9.9.9 STALE PROSE |#')"

# --- THE .github#268 REGRESSIONS: green under substring, red now ---------------------------
# (b) the observed miss: registry moved to 0.4.0, the literal cell still names the prerelease. `0.4.0`
# is a substring of `0.4.0-preview.1`, so the old gate was green.
expect_regression "stale prerelease literal (0.4.0 vs 0.4.0-preview.1)" \
  "does not appear as a whole version token" \
  "$(variant staleprerelease 's#| `demo` | SDD | 0.4.0 |#| `demo` | SDD | 0.4.0-preview.1 |#')"

# the COLUMN discipline: the registry `version` literal sitting in the WRONG column (here it survives
# in `package-version`) must not satisfy the `version` check. A whole-row substring gate finds it and
# is green; reading the literal's own column reds.
expect_regression "version literal in the wrong column does not count (9.9.9 in version, 0.4.0 in package-version)" \
  "does not appear as a whole version token" \
  "$(variant wrongcol 's#| `demo` | SDD | 0.4.0 | 0.4.0 |#| `demo` | SDD | 9.9.9 | 0.4.0 |#')"

# a bare integer version must not be satisfied by a longer version that merely starts with it
expect_regression "bare version 1 vs cell 1.2.0" \
  "does not appear as a whole version token" \
  "$(variant bareprefix 's#| `bare` | Governance | 1 |#| `bare` | Governance | 1.2.0 |#')"

# a registry version must not be satisfied by a LONGER version it is a prefix of — the 4-segment
# (ADR-0007) shape of the same defect. Needs its own registry: it is the *registry* scalar that is
# the prefix here (1.2.1), matching inside the cell's 1.2.1.1.
REG_QUADPREFIX="$WORK/registry-quadprefix.yml"
sed 's#    version: "1.2.1.1"#    version: "1.2.1"#' "$REG" > "$REG_QUADPREFIX"
expect_regression "quad registry 1.2.1 vs cell 1.2.1.1" \
  "does not appear as a whole version token" \
  "$BASE" "$REG_QUADPREFIX"

# --- drift the old gate already caught (kept: they must not regress) -----------------------
expect_fail "outright wrong version"      "does not appear as a whole version token" \
  "$(variant wrongver  's#| `demo` | SDD | 0.4.0 | 0.4.0 |#| `demo` | SDD | 9.9.9 | 9.9.9 |#')"
expect_fail "quad 1.2.1.1 vs truncated cell 1.2.1" "does not appear as a whole version token" \
  "$(variant quadtrunc 's#| `quad` | Governance | 1.2.1.1 |#| `quad` | Governance | 1.2.1 |#')"
expect_fail "missing contract row"        "has no row"          "$(variant nocontract 's#^| `demo` | SDD | 0.4.0 | 0.4.0 |##')"
expect_fail "missing coherence row"       "has no row"          "$(variant nocoh      's#^| `demo-coh` |.*$##')"
expect_fail "coherent flag contradicts"   "registry says coherent=True" \
  "$(variant flagflip  's#| `demo-coh` | ✅ yes |#| `demo-coh` | ❌ no |#')"

# --- fail-closed corollaries (epic #266): a missing subject must not read as "checked, fine" ---
expect_fail "version column absent"       "has no 'version' column" \
  "$(variant novercol  's#| Contract | Owner | version | package-version |#| Contract | Owner | rev | package-version |#')"
expect_fail "package-version column absent" "has no 'package-version' column" \
  "$(variant nopkgcol  's#| Contract | Owner | version | package-version |#| Contract | Owner | version | pkg |#')"
expect_fail "Coherent? column absent"     "has no 'Coherent?' column" \
  "$(variant nocohcol  's#| Id | Coherent? | Owner | Summary |#| Id | Flag | Owner | Summary |#')"
# An emptied generated literal cell is not "checked, fine" — it reds as a stale region.
expect_fail "empty version cell reds"     "does not appear as a whole version token" \
  "$(variant emptycell 's#| `demo` | SDD | 0.4.0 | 0.4.0 |#| `demo` | SDD |  | 0.4.0 |#')"

# --- #1155: the version literal MUST be a quoted string, and `version` MUST be present -------
# An UNQUOTED `version: 1.10` is YAML-coerced to the float 1.1 BEFORE the gate sees it, and the generated
# region is emitted from the SAME coerced value — so BOTH sides read `1.1`, a token/substring check agrees
# with itself, and the dropped quote (1.10 -> 1.1 is a DIFFERENT version) passes green. Both regressions
# below model that: the registry carries the coerced value AND the projection cell carries `1.1`, so the
# old gate is green and only the type guard reds it. Guard as the sibling version-literal gates do
# (feed-coherence, source-coherence, emitted-contract-version). Registry mutation -> its own registry.
REG_UNQUOTED="$WORK/registry-unquoted.yml"
sed 's#    version: "0.4.0"#    version: 1.10#' "$REG" > "$REG_UNQUOTED"
expect_regression "unquoted version 1.10 (YAML-coerced to float 1.1) reds as not-a-string" \
  "not a quoted string" \
  "$(variant unquotedproj 's#| `demo` | SDD | 0.4.0 | 0.4.0 |#| `demo` | SDD | 1.1 | 0.4.0 |#')" \
  "$REG_UNQUOTED"

# A contract that OMITS `version` entirely must red — the sibling gates treat absence as drift; this
# gate used to skip it silently (`if val is None: continue`), passing green. `bare` is the only
# `version: "1"`, so deleting that line drops bare's version.
REG_NOVER="$WORK/registry-nover.yml"
sed '/^    version: "1"$/d' "$REG" > "$REG_NOVER"
expect_regression "contract missing a version literal reds"  "declares no 'version'" \
  "$BASE" "$REG_NOVER"

# --- the token boundary: punctuation ends a version, a segment does not ---------------------
# A trailing period is punctuation, not a fourth segment: the literal is still there, so green.
expect_pass "version with a trailing period still matches" \
  "$(variant sentence 's#| `demo` | SDD | 0.4.0 | 0.4.0 |#| `demo` | SDD | 0.4.0. | 0.4.0 |#')"
# ...but a `.` FOLLOWED BY a segment does continue the version, so `demo` 0.4.0 must not match 0.4.0.1.
expect_fail "version is not matched inside a longer 4-segment version" \
  "does not appear as a whole version token" \
  "$(variant fourseg 's#| `demo` | SDD | 0.4.0 | 0.4.0 |#| `demo` | SDD | 0.4.0.1 | 0.4.0 |#')"

# --- column indexing is only sound if escaped pipes stay inside their cell -------------------
# A `\|` in the Owner cell must not shift `version` by one column (that would be a spurious red).
expect_pass "escaped pipe in a cell does not shift columns" \
  "$(variant escpipe 's#| `demo` | SDD | 0.4.0 |#| `demo` | SDD \\| alias | 0.4.0 |#')"

# --- the gate on the real files (CI guard, mirrors tests/repos-registry/run.sh) -------------
expect_pass "checked-in registry projects cleanly" \
  "$REPO_ROOT/docs/registry/compatibility.md" "$REPO_ROOT/registry/dependencies.yml"

reported_version="$(PATH="${PATH}" python3 "$CHECK" \
  "$REPO_ROOT/registry/dependencies.yml" "$REPO_ROOT/docs/registry/compatibility.md")"
producer_version="$("$REPO_ROOT/scripts/generate-projections" --contract-version)"
if [[ "$reported_version" == *"agentContractVersion=$producer_version"* ]]; then
  ok "projection report records the sole producer's agent contract version"
else
  bad "projection report records the sole producer's agent contract version" "$reported_version"
fi

echo "projection fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
if [ -n "${FSGG_JUNIT_OUTPUT:-}" ]; then
  mkdir -p "$(dirname "$FSGG_JUNIT_OUTPUT")"
  if [ "$failcount" -eq 0 ]; then
    printf '<?xml version="1.0" encoding="UTF-8"?>\n<testsuite name="projection" tests="%s" failures="0" skipped="0"><testcase classname="projection" name="agent contract attribution"/></testsuite>\n' \
      "$pass" > "$FSGG_JUNIT_OUTPUT"
  else
    printf '<?xml version="1.0" encoding="UTF-8"?>\n<testsuite name="projection" tests="%s" failures="1" skipped="0"><testcase classname="projection" name="agent contract attribution"><failure message="projection fixture failed"/></testcase></testsuite>\n' \
      "$((pass + failcount))" > "$FSGG_JUNIT_OUTPUT"
  fi
fi
[ "$failcount" -eq 0 ] || { echo "::error::projection fixture FAILED"; exit 1; }
echo "projection fixture — OK"
