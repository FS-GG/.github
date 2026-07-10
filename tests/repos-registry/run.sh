#!/usr/bin/env bash
# Fixture for scripts/repos.sh — the validator/query for registry/repos.yml (the org repo roster,
# ADR-0019). Proves validate PASSES on a well-formed roster and FAILS on each violation class the
# gate must catch (unknown capability, duplicate id, bad role, not-exactly-one-authority, authority
# receiving the kit, kit digest drift, missing kit source, malformed repo name), that `list` returns
# the right repos for a capability, that `digest` follows the dir->SKILL.md / file rule, and — as the
# CI guard on the real file — that the checked-in registry/repos.yml validates. Mirrors
# tests/skill-union/run.sh and tests/fsgg-coord/run.sh: throwaway trees under a temp dir, no network.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPOS_SH="$HERE/../../scripts/repos.sh"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/repos-registry-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

# A throwaway root whose kit sources exist, so digest checks have something real to hash.
ROOT="$WORK/root"
mkdir -p "$ROOT/.claude/skills/demo-skill" "$ROOT/scripts"
printf '# demo skill\n' > "$ROOT/.claude/skills/demo-skill/SKILL.md"
printf '#!/usr/bin/env bash\necho hi\n' > "$ROOT/scripts/democlient"
SKILL_SHA="$(sha256sum "$ROOT/.claude/skills/demo-skill/SKILL.md" | cut -d' ' -f1)"
CLIENT_SHA="$(sha256sum "$ROOT/scripts/democlient" | cut -d' ' -f1)"

BASE="$WORK/base.yml"
cat > "$BASE" <<YAML
schemaVersion: 1
updated: 2026-07-04
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }
  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels, coordination-kit] }
kit:
  - { id: demo-skill, kind: skill,  source: .claude/skills/demo-skill, sha256: $SKILL_SHA }
  - { id: democlient, kind: client, source: scripts/democlient,        sha256: $CLIENT_SHA }
YAML

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# variant <name> <sed-expr> — copy BASE, apply one mutation, echo the path
variant() { local f="$WORK/$1.yml"; sed "$2" "$BASE" > "$f"; printf '%s' "$f"; }

# expect_pass <name> <registry>
expect_pass() {
  local n="$1" reg="$2" out
  if out="$(bash "$REPOS_SH" validate --registry "$reg" --root "$ROOT" 2>&1)"; then ok "$n"
  else bad "$n" "$out"; fi
}
# expect_fail <name> <expected-exit> <substr> <registry>
expect_fail() {
  local n="$1" want="$2" substr="$3" reg="$4" out rc=0
  out="$(bash "$REPOS_SH" validate --registry "$reg" --root "$ROOT" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then bad "$n" "expected failure, passed"; return; fi
  if [ "$rc" -ne "$want" ]; then bad "$n" "expected exit $want, got $rc: $out"; return; fi
  case "$out" in *"$substr"*) ok "$n — $(printf '%s' "$out" | grep -m1 "$substr" | sed 's/::error::repos-registry: //')" ;;
                 *) bad "$n" "exit $rc but missing substring '$substr': $out" ;; esac
}

echo "repos-registry fixture"

# --- happy path ---
expect_pass "valid roster passes" "$BASE"

# --- violation classes (all exit 1) ---
expect_fail "unknown receives capability"     1 "unknown capability" "$(variant unknowncap  's/coordination-kit\]/bogus-cap]/')"
expect_fail "duplicate repo id"               1 "duplicate"          "$(variant dupid       's/id: sdd,/id: .github,/')"
expect_fail "invalid role"                    1 "role"               "$(variant badrole     's/role: framework/role: banana/')"
expect_fail "not exactly one authority"       1 "exactly one"        "$(variant twoauth     's/role: framework/role: authority/')"
expect_fail "authority receives the kit"      1 "must not RECEIVE"   "$(variant authkit     's/full: FS-GG\/.github,   role: authority, receives: \[labels\]/full: FS-GG\/.github,   role: authority, receives: [labels, coordination-kit]/')"
expect_fail "kit digest drift"                1 "digest"             "$(variant digestdrift "s/$SKILL_SHA/0000000000000000000000000000000000000000000000000000000000000000/")"
expect_fail "kit source missing"              1 "source missing"     "$(variant nosource    's/source: scripts\/democlient/source: scripts\/nope/')"
expect_fail "kit id is not kebab/dotted"      1 "kit id"             "$(variant badkitid    's/id: demo-skill,/id: Demo Skill,/')"
expect_fail "malformed repo full name"        1 "FS-GG"              "$(variant badfull     's/full: FS-GG\/FS.GG.SDD/full: GH\/FS.GG.SDD/')"

# --- misconfiguration (exit 2) ---
expect_fail "empty repos[] is misconfig"      2 "empty"              "$(variant emptyrepos  's/^repos:/repos: []\n_repos:/')"

# --- list + digest ---
list_kit="$(bash "$REPOS_SH" list --receives coordination-kit --registry "$BASE")"
[ "$list_kit" = "FS-GG/FS.GG.SDD" ] && ok "list --receives coordination-kit -> the one receiver" \
  || bad "list --receives coordination-kit" "got: $list_kit"
list_labels="$(bash "$REPOS_SH" list --receives labels --field id --registry "$BASE" | tr '\n' ',')"
[ "$list_labels" = ".github,sdd," ] && ok "list --receives labels --field id -> both, in order" \
  || bad "list labels ids" "got: $list_labels"
[ "$(bash "$REPOS_SH" digest "$ROOT/.claude/skills/demo-skill")" = "$SKILL_SHA" ] \
  && ok "digest skill dir -> sha256 of SKILL.md" || bad "digest skill dir"
[ "$(bash "$REPOS_SH" digest "$ROOT/scripts/democlient")" = "$CLIENT_SHA" ] \
  && ok "digest file -> sha256 of file" || bad "digest file"

# --- kit query (coordination-propagate builds its PR title from this) ---
kit_ids="$(bash "$REPOS_SH" kit --registry "$BASE" | tr '\n' ',')"
[ "$kit_ids" = "demo-skill,democlient," ] && ok "kit -> item ids, in roster order" \
  || bad "kit ids" "got: $kit_ids"
kit_kinds="$(bash "$REPOS_SH" kit --field kind --registry "$BASE" | tr '\n' ',')"
[ "$kit_kinds" = "skill,client," ] && ok "kit --field kind" || bad "kit kinds" "got: $kit_kinds"
kit_srcs="$(bash "$REPOS_SH" kit --field source --registry "$BASE" | tr '\n' ',')"
[ "$kit_srcs" = ".claude/skills/demo-skill,scripts/democlient," ] && ok "kit --field source" \
  || bad "kit sources" "got: $kit_srcs"
rc=0; bash "$REPOS_SH" kit --field bogus --registry "$BASE" >/dev/null 2>&1 || rc=$?
[ "$rc" -eq 2 ] && ok "kit --field bogus -> misconfig (exit 2)" || bad "kit bad field" "got exit $rc"

# --- kit --kind (coordination-sync derives its distributed skill set from this) ---
kit_skills="$(bash "$REPOS_SH" kit --kind skill --registry "$BASE" | tr '\n' ',')"
[ "$kit_skills" = "demo-skill," ] && ok "kit --kind skill -> only the skill rows" \
  || bad "kit --kind skill" "got: $kit_skills"
kit_clients="$(bash "$REPOS_SH" kit --kind client --registry "$BASE" | tr '\n' ',')"
[ "$kit_clients" = "democlient," ] && ok "kit --kind client -> only the client rows" \
  || bad "kit --kind client" "got: $kit_clients"
kit_srcs_skill="$(bash "$REPOS_SH" kit --field source --kind skill --registry "$BASE" | tr '\n' ',')"
[ "$kit_srcs_skill" = ".claude/skills/demo-skill," ] && ok "kit --field source --kind skill" \
  || bad "kit --field/--kind compose" "got: $kit_srcs_skill"
# No --kind is "every row" — coordination-propagate's title depends on it staying unfiltered.
kit_all="$(bash "$REPOS_SH" kit --registry "$BASE" | tr '\n' ',')"
[ "$kit_all" = "demo-skill,democlient," ] && ok "kit without --kind -> every row" \
  || bad "kit unfiltered" "got: $kit_all"
# An unknown kind is a usage error, not an empty list: silence here would distribute nothing, green.
rc=0; bash "$REPOS_SH" kit --kind bogus --registry "$BASE" >/dev/null 2>&1 || rc=$?
[ "$rc" -eq 2 ] && ok "kit --kind bogus -> misconfig (exit 2), not an empty list" \
  || bad "kit bad kind" "got exit $rc"

# The title the propagate workflow renders. Guards the `paste -sd` delimiter-cycling trap: a
# multi-char delimiter list would yield "demo-skill,democlient" with a SPACE before the last item.
kit_title="$(bash "$REPOS_SH" kit --registry "$BASE" | paste -sd, - | sed 's/,/, /g')"
[ "$kit_title" = "demo-skill, democlient" ] && ok "kit -> comma-joined PR title" \
  || bad "kit title join" "got: $kit_title"

# The real roster must name a kit, or the propagate title would render empty.
[ -n "$(bash "$REPOS_SH" kit)" ] && ok "the checked-in roster declares kit items" || bad "real roster kit empty"

# --- a missing flag value is misconfiguration (exit 2), never a finding (exit 1) — #341 ---
# `${2:?…}` let bash exit 1, the code reserved for "I checked the roster, and it is invalid". A
# caller reading the `# Exit:` contract could not tell a typo'd command line from a broken registry.
#
# expect_usage <name> <subcommand> <args…> — asserts the exit code AND the diagnostic. Pinning the
# `::error::repos-registry: <sub>:` prefix is half the point: exit 2 with a raw bash message (which
# still contains "needs a value") would annotate nothing in Actions and name no subcommand.
expect_usage() {
  local n="$1"; shift
  local sub="$1" out rc=0
  out="$(bash "$REPOS_SH" "$@" 2>&1)" || rc=$?
  if [ "$rc" -eq 1 ]; then bad "$n" "exit 1 — a usage error reported itself as a roster finding"; return; fi
  if [ "$rc" -ne 2 ]; then bad "$n" "expected exit 2 (misconfig), got $rc: $out"; return; fi
  case "$out" in "::error::repos-registry: $sub: "*"needs a value"*) ok "$n" ;;
                 *) bad "$n" "exit 2, but not a prefixed '$sub: <flag> needs a value': $out" ;; esac
}
# Every flag of every subcommand, three ways it can lack a value. `--field`/`--registry` are shared
# across subcommands, so each is asserted under each — that is what pins the prefix.
for spec in "list:--receives --field --registry" \
            "kit:--field --kind --registry" \
            "validate:--registry --root"; do
  sub="${spec%%:*}"
  for flag in ${spec#*:}; do
    # absent: `repos.sh list --receives`
    expect_usage "$sub $flag (absent value) -> exit 2"        "$sub" "$flag"
    # empty-but-present: an unset variable upstream, which is how this reaches a caller in practice
    expect_usage "$sub $flag '' (empty value) -> exit 2"      "$sub" "$flag" ""
    # the next flag swallowed as the value: must blame $flag, not the token two args later
    expect_usage "$sub $flag --registry (flag as value) -> exit 2" "$sub" "$flag" --registry
  done
done
# `digest` takes a positional, not a flag; it already routes through die(). Guard it against a
# regression that makes the same class of mistake.
rc=0; bash "$REPOS_SH" digest >/dev/null 2>&1 || rc=$?
[ "$rc" -eq 2 ] && ok "digest (no path) -> misconfig (exit 2)" || bad "digest no path" "got exit $rc"

# --- CI guard on the real, checked-in roster ---
if bash "$REPOS_SH" validate >/dev/null 2>&1; then ok "the checked-in registry/repos.yml validates"
else bad "real registry/repos.yml validates" "$(bash "$REPOS_SH" validate 2>&1)"; fi

# --- CI guard on the gate itself (#266) ---
# `validate` above is the only thing asserting the kit digests, and it runs only when this workflow's
# `paths:` filter matches. A kit source outside that filter is therefore never digest-checked, and
# nothing reports it: the gate reports green because it never ran. Assert every kit source (and, for
# skills, the .agents mirror that carries the same bytes) is covered, on BOTH triggers.
uncovered="$(python3 - "$REPO_ROOT" <<'PY'
import sys, yaml, re, pathlib
root = pathlib.Path(sys.argv[1])
wf  = yaml.safe_load((root / ".github/workflows/repos-registry-selftest.yml").read_text())
reg = yaml.safe_load((root / "registry/repos.yml").read_text())

# YAML 1.1 reads a bare `on:` as the boolean True; a quoted "on": stays a string. Accept either
# rather than KeyError-ing, and say so plainly if the key is gone — this assertion exists precisely
# because a gate that cannot find its subject must fail loudly, not vanish.
triggers = wf.get(True, wf.get("on"))
if not isinstance(triggers, dict):
    sys.exit("selftest workflow has no readable `on:` block — cannot check its paths: filter")

def matches(path, pat):                       # GitHub glob: ** spans /, * does not
    rx = "".join(r"[^/]*" if p == "*" else ".*" if p == "**" else re.escape(p)
                 for p in re.split(r"(\*\*|\*)", pat))
    return re.fullmatch(rx, path) is not None

def probes(item):
    # `digest` hashes a dir's SKILL.md and a file's own bytes, so those are the paths whose edits
    # stale the sha256. Skills are mirrored byte-for-byte into .agents/, so an edit lands in either.
    if item["kind"] != "skill":
        return [item["source"]]
    claude = f"{item['source']}/SKILL.md"
    return sorted({claude, claude.replace(".claude/", ".agents/", 1)})

gaps = []
for trigger in ("pull_request", "push"):
    if trigger not in triggers:
        # The gate cannot run on this event at all — the widest possible coverage gap.
        gaps.append(f"{trigger}: trigger absent — the fixture never runs on {trigger}")
        continue
    # A trigger that is null, or carries no `paths:`, is unfiltered: every path fires it, so every
    # kit source is trivially covered. Only an explicitly empty list matches nothing.
    cfg = triggers.get(trigger) or {}
    if "paths" not in cfg:
        continue
    pats = cfg["paths"]
    if not pats:
        gaps.append(f"{trigger}: paths: is empty — matches nothing")
        continue
    for item in reg["kit"]:
        for probe in probes(item):
            if not any(matches(probe, p) for p in pats):
                gaps.append(f"{trigger}: kit '{item['id']}' source {probe}")
print("\n".join(gaps))
PY
)"
if [ -z "$uncovered" ]; then ok "every kit source is covered by the selftest paths: filter"
else bad "kit source ungated — an edit to it skips the digest check" "$uncovered"; fi

echo "repos-registry fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::repos-registry fixture FAILED"; exit 1; }
echo "repos-registry fixture — OK"
