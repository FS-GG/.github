#!/usr/bin/env bash
# End-to-end: the COMPILED `fsgg-coord-engine predicate` — the ADR-0050 registry-predicate oracle,
# call-site A (.github#1202) — over LOCAL fixture files, no token, no network.
#
# The verdict LOGIC is unit-tested pure in `RegistryPredicateTests` (16 cases). This is the other half:
# the real binary's FILE edge — reading `registry/skills.yml` off `$FSGG_REGISTRY`, locating and parsing
# the owning producer's manifest under `$FSGG_REPOS_ROOT`, and the exit-code contract (0 agrees / 3
# contradicts / 4 unknown, the `landable` shape). The motivating instance is #1194: a request to set
# `fs-gg-playtest mirrored: true` must be REFUTED from the owner's manifest, which declares `false`.
#
# It also pins the ASSERTION FORM to the parser: the three `### <label>` headings the engine anchors on
# must be exactly the labels `.github/ISSUE_TEMPLATE/cross-repo-request.yml` renders. If the form and the
# parser drift, the filing-time check silently stops reading the assertion (#266's shape), so they are
# gated equal here.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE="${FSGG_COORD_ENGINE_BIN:-$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

if [ ! -x "$ENGINE" ]; then
  echo "FAIL  the engine must be built first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2
  echo "      (looked at: $ENGINE)" >&2
  exit 1
fi

# ---- fixtures --------------------------------------------------------------------------------------
FIX="$(mktemp -d)"
trap 'rm -rf "$FIX"' EXIT

mkdir -p "$FIX/registry"
cat >"$FIX/registry/skills.yml" <<'YAML'
schemaVersion: 1
skills:
  - { id: fs-gg-playtest,  scope: product, owner: fs-gg-game, source: FS.GG.Game/template/product-skills/fs-gg-playtest/SKILL.md,  sha256: abc, mirrored: false, materializes-when: "profile in [game, sample-pack]" }
  - { id: fs-gg-game-core, scope: product, owner: fs-gg-game, source: FS.GG.Game/template/product-skills/fs-gg-game-core/SKILL.md, sha256: def, mirrored: true }
  - { id: fs-gg-audio,     scope: product, owner: fs-gg-game, source: FS.GG.Game/template/product-skills/fs-gg-audio/SKILL.md,     sha256: ghi }
YAML

mkdir -p "$FIX/.repos/FS.GG.Game/template/skill-manifest"
cat >"$FIX/.repos/FS.GG.Game/template/skill-manifest/skill-manifest.json" <<'JSON'
{ "skills": [
  { "id": "fs-gg-playtest",  "mirrored": false },
  { "id": "fs-gg-game-core", "mirrored": true },
  { "id": "fs-gg-audio" }
] }
JSON

export FSGG_REGISTRY="$FIX/registry/skills.yml"
export FSGG_REPOS_ROOT="$FIX/.repos"

# run <expected-exit> <expected-first-word> <desc> -- <engine args...>
run() {
  local want_rc="$1" want_word="$2" desc="$3"; shift 3; [ "$1" = "--" ] && shift
  local out rc
  out="$("$ENGINE" "$@" 2>/dev/null)"; rc=$?
  local word; word="$(printf '%s' "$out" | head -n1)"
  if [ "$rc" = "$want_rc" ] && [ "$word" = "$want_word" ]; then
    ok "$desc ($word, exit $rc)"
  else
    bad "$desc" "wanted '$want_word' exit $want_rc, got '$word' exit $rc"
  fi
}

# ---- verdicts --------------------------------------------------------------------------------------
run 0 agrees      "owner declares mirrored:false, request false -> agrees"        -- predicate fs-gg-playtest mirrored false
run 3 contradicts "the #1194 instance: request mirrored:true, owner false -> contradicts" -- predicate fs-gg-playtest mirrored true
run 0 agrees      "game-core mirrored:true -> agrees"                             -- predicate fs-gg-game-core mirrored true
run 4 unknown     "manifest omits mirrored (Silent) -> unknown, never false"     -- predicate fs-gg-audio mirrored true
run 4 unknown     "no registry row -> unknown"                                    -- predicate no-such-skill mirrored true
run 4 unknown     "unsupported field -> unknown (abstain, not a guess)"          -- predicate fs-gg-playtest materializes-when x

# A typed (capital/padded) field still finds the manifest's lower-kebab key — else a real #1194
# refutation silently downgrades to unknown (the case-sensitive-lookup bug).
run 3 contradicts "a capitalised field name still refutes (Mirrored -> mirrored)" -- predicate fs-gg-playtest Mirrored true

# The env overrides below SAVE and RESTORE rather than run in a `( … )` subshell: a subshell's `ok`/
# `bad` increments never reach the parent counters, so a FAILURE inside one would not fail the gate —
# the #266 shape this whole feature exists to refuse, in its own test.

# A MALFORMED owner value — a string `"false"`, not a JSON bool — is UNKNOWN, never believed as a
# refutation (mirror_of / .github#658). Point a fresh fixture at a manifest that declares it that way.
BADFIX="$(mktemp -d)"
mkdir -p "$BADFIX/FS.GG.Game/template/skill-manifest"
cat >"$BADFIX/FS.GG.Game/template/skill-manifest/skill-manifest.json" <<'JSON'
{ "skills": [ { "id": "fs-gg-playtest", "mirrored": "false" } ] }
JSON
GOOD_REPOS="$FSGG_REPOS_ROOT"
export FSGG_REPOS_ROOT="$BADFIX"
run 4 unknown "a non-bool owner mirrored (string) is unknown, never a false agree" -- predicate fs-gg-playtest mirrored false
run 4 unknown "a non-bool owner mirrored (string) is unknown, never a false refute" -- predicate fs-gg-playtest mirrored true
export FSGG_REPOS_ROOT="$GOOD_REPOS"
rm -rf "$BADFIX"

# fail-closed when registry/ is absent (a receiver — ADR-0042 carve-out)
GOOD_REG="$FSGG_REGISTRY"
export FSGG_REGISTRY="$FIX/registry/DOES-NOT-EXIST.yml"
run 4 unknown "registry absent -> unknown (fail closed, ADR-0042)" -- predicate fs-gg-playtest mirrored true
export FSGG_REGISTRY="$GOOD_REG"

# fail-closed when the producer checkout is absent
export FSGG_REPOS_ROOT="$FIX/.no-repos"
run 4 unknown "owner checkout absent -> unknown (fail closed)" -- predicate fs-gg-playtest mirrored true
export FSGG_REPOS_ROOT="$GOOD_REPOS"

# ---- the CONTRADICTS payload the workflow reads ----------------------------------------------------
J="$("$ENGINE" predicate fs-gg-playtest mirrored true --json 2>/dev/null)"
if printf '%s' "$J" | grep -q '"verdict":"contradicts"' \
   && printf '%s' "$J" | grep -q '"ownerValue":"false"' \
   && printf '%s' "$J" | grep -q '"note":"'; then
  ok "contradicts --json carries ownerValue and note (the auto-comment payload)"
else
  bad "contradicts --json payload" "$J"
fi

# ---- stdin: the filing-time path (a rendered cross-repo-request body) -------------------------------
BODY="$(printf '### From (requesting repo / agent)\n\nFS.GG.Game\n\n### Asserted registry id\n\nfs-gg-playtest\n\n### Asserted registry field\n\nmirrored\n\n### Asserted registry value\n\ntrue\n\n### Priority\n\nnormal\n')"
out="$(printf '%s' "$BODY" | "$ENGINE" predicate 2>/dev/null)"; rc=$?
if [ "$rc" = 3 ] && [ "$(printf '%s' "$out" | head -n1)" = contradicts ]; then
  ok "stdin: a filed request asserting mirrored:true -> contradicts (exit 3)"
else
  bad "stdin filing-time refutation" "exit $rc: $out"
fi

# a request with NO structured assertion is a no-op, exit 0
out="$(printf '### Request\n\nplease publish X\n' | "$ENGINE" predicate 2>/dev/null)"; rc=$?
[ "$rc" = 0 ] && ok "stdin: no assertion -> no-op (exit 0)" || bad "stdin no-op" "exit $rc: $out"

# ---- the form <-> parser drift gate ----------------------------------------------------------------
FORM="$REPO_ROOT/.github/ISSUE_TEMPLATE/cross-repo-request.yml"
for label in "Asserted registry id" "Asserted registry field" "Asserted registry value"; do
  if grep -qF "label: $label" "$FORM"; then
    ok "form declares the '$label' input the parser anchors on"
  else
    bad "form is missing the '$label' label the engine parses" "the oracle would silently stop reading the assertion (#266)"
  fi
done

echo "registry-predicate: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error title=registry-predicate::the ADR-0050 oracle e2e FAILED"; exit 1; }
