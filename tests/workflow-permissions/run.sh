#!/usr/bin/env bash
# Fixture for scripts/check-workflow-permissions.py (.github#478, epic #266).
#
# Offline. `gh` is stubbed on PATH and FAILS LIKE THE REAL ONE — a 404 is an answer, a rate limit is
# not — so the fail-closed legs exercise the transport rather than a convenience shim.
#
# Every negative leg asserts the REASON, not merely a non-zero exit. tests/feed-coherence/run.sh:10
# names the trap: the .github#266 vacuous-failure defect was a "must fail" test whose non-zero exit
# came from a path guard rather than from the thing under test — it would have passed against a gate
# that was broken in a completely different way.
#
# The headline leg is REGRESSION: the caller file FS.GG.Game actually shipped for all 119
# startup_failures (fetched from the parent of the FS.GG.Game#138 merge, byte for byte) is run
# against the REAL lockfile-sync.yml callee in this working tree. If this gate had existed, that is
# the run it would have red-lighted.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
TOOL="$HERE/../../scripts/check-workflow-permissions.py"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
FIX="$HERE/fixtures"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/workflow-permissions-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1
unset GITHUB_TOKEN GH_TOKEN || true   # the gate must not fall back to ambient credentials

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# ---------------------------------------------------------------------------------------------
# The `gh` stub. Serves a WORLD directory:
#   $WORLD/<owner>__<repo>/<workflow>.yml       a caller repo's .github/workflows
#   $WORLD/refs/<ref>/<callee>.yml              FS-GG/.github's callees at a pinned ref
#   $WORLD/<owner>__<repo>.unreachable          this repo answers like a rate limit, not a 404
#   $WORLD/<owner>__<repo>.invisible            the repo itself 404s (private/renamed/gone)
# A repo dir that is simply absent = the repo is visible but has no .github/workflows.
# ---------------------------------------------------------------------------------------------
STUB="$WORK/stub"; mkdir -p "$STUB"
cat > "$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
path=""
for a in "$@"; do case "$a" in repos/*) path="$a";; esac; done

notfound() { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
apifail()  { echo "gh: API rate limit exceeded for installation (HTTP 403)" >&2; exit 1; }

# repos/<owner>/<repo>/contents/.github/workflows/<file>[?ref=<ref>]  |  .../workflows  |  repos/<o>/<r>
rest="${path#repos/}"
repo="${rest%%/contents/*}"
slug="${repo//\//__}"

[ -e "$WORLD/$slug.unreachable" ] && apifail
[ -e "$WORLD/$slug.invisible" ]   && notfound

case "$rest" in
  */contents/.github/workflows)
    d="$WORLD/$slug"
    [ -d "$d" ] || notfound
    ls -1 "$d" 2>/dev/null || notfound
    ;;
  */contents/.github/workflows/*)
    file="${rest##*/contents/.github/workflows/}"
    ref="${file#*\?ref=}"; [ "$ref" = "$file" ] && ref=""
    file="${file%%\?*}"
    if [ -n "$ref" ]; then f="$WORLD/refs/$ref/$file"; else f="$WORLD/$slug/$file"; fi
    [ -f "$f" ] || notfound
    cat "$f"
    ;;
  *)
    [ -d "$WORLD/$slug" ] || [ -e "$WORLD/$slug.visible" ] || notfound
    echo "$repo"
    ;;
esac
STUB
chmod +x "$STUB/gh"

# run <world> <root> [args…]
run() {
  local world="$1" root="$2"; shift 2
  PATH="$STUB:$PATH" WORLD="$world" FSGG_PERMS_TRIES=1 FSGG_PERMS_RETRY_DELAY=0 \
    python3 "$TOOL" --root "$root" "$@" 2>&1
}

# expect <name> <want-rc> <needle> <world> <root> [args…] — the rc AND the reason must both match.
expect() {
  local name="$1" want="$2" needle="$3"; shift 3
  local out rc=0
  out="$(run "$@")" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

# A minimal roster naming exactly the repos a leg cares about.
roster() {  # roster <file> <full…>
  local f="$1"; shift
  { echo "schemaVersion: 2"; echo "repos:"; for r in "$@"; do echo "  - { id: x, full: $r }"; done; } > "$f"
}

# A synthetic FS-GG/.github working tree whose callee declares the given permissions.
callee_root() {  # callee_root <dir> <name> <permissions-yaml-block>
  local d="$1" name="$2" perms="$3"
  mkdir -p "$d/.github/workflows" "$d/registry"
  { echo "name: $name"; echo "on:"; echo "  workflow_call:"; printf '%s\n' "$perms"; \
    echo "jobs:"; echo "  x:"; echo "    runs-on: ubuntu-latest"; echo "    steps: [{ run: 'true' }]"; } \
    > "$d/.github/workflows/$name"
}

caller() {  # caller <file> <top-perms-block-or-empty> <callee@ref> [job-perms-block]
  local f="$1" top="$2" target="$3" jobperms="${4:-}"
  mkdir -p "$(dirname "$f")"
  { echo "name: caller"; echo "on: { pull_request: { branches: [main] } }"
    [ -n "$top" ] && printf '%s\n' "$top"
    echo "jobs:"; echo "  sync:"
    [ -n "$jobperms" ] && printf '%s\n' "$jobperms"
    echo "    uses: FS-GG/.github/.github/workflows/$target"
  } > "$f"
}

# =============================================================================================
# 1. REGRESSION — the real FS.GG.Game caller, the real callee, the real bug.
# =============================================================================================
W="$WORK/w-regression"; mkdir -p "$W/FS-GG__FS.GG.Game"
cp "$FIX/game-lockfile-sync.prefix.yml" "$W/FS-GG__FS.GG.Game/lockfile-sync.yml"
R="$WORK/r-regression"; mkdir -p "$R/registry"
cp -r "$REPO_ROOT/.github" "$R/.github"      # the REAL callees, as this PR ships them
roster "$R/registry/repos.yml" FS-GG/FS.GG.Game

expect "REGRESSION FS.GG.Game#137: the caller behind all 119 startup_failures is caught" \
  1 "packages: needs read, caller grants none" "$W" "$R"
expect "REGRESSION: and it says the run cannot start" \
  1 "startup_failure" "$W" "$R"

# The same repo AFTER FS.GG.Game#138 — the fix must actually satisfy the gate.
W2="$WORK/w-fixed"; mkdir -p "$W2/FS-GG__FS.GG.Game"
sed 's/^  contents: read.*/  contents: read\n  packages: read/' \
  "$FIX/game-lockfile-sync.prefix.yml" > "$W2/FS-GG__FS.GG.Game/lockfile-sync.yml"
expect "the FS.GG.Game#138 fix (adding packages: read) turns it green" 0 "ok:" "$W2" "$R"

# =============================================================================================
# 2. The permission algebra.
# =============================================================================================
RC="$WORK/r-callee"; callee_root "$RC" "cal.yml" $'permissions:\n  contents: read\n  packages: read'
mkdir -p "$RC/registry"; roster "$RC/registry/repos.yml" FS-GG/R

algebra() {  # algebra <name> <rc> <needle> <top> [jobperms]
  local name="$1" rc="$2" needle="$3" top="$4" jobperms="${5:-}"
  local w="$WORK/w-$RANDOM$RANDOM"; mkdir -p "$w/FS-GG__R"
  caller "$w/FS-GG__R/c.yml" "$top" "cal.yml@main" "$jobperms"
  expect "$name" "$rc" "$needle" "$w" "$RC"
}

algebra "exact grant passes"            0 "ok:" $'permissions:\n  contents: read\n  packages: read'
algebra "write satisfies a read need"   0 "ok:" $'permissions:\n  contents: write\n  packages: write'
algebra "read-all satisfies every need" 0 "ok:" "permissions: read-all"
algebra "write-all satisfies every need" 0 "ok:" "permissions: write-all"
algebra "a missing scope is caught"     1 "packages: needs read, caller grants none" \
        $'permissions:\n  contents: read'
algebra "an explicitly 'none' scope is caught" 1 "contents: needs read, caller grants none" \
        $'permissions:\n  contents: none\n  packages: read'
algebra "an explicit empty block grants nothing, and is not 'absent'" \
        1 "packages: needs read, caller grants none" "permissions: {}"
algebra "NO permissions block at all is an ERROR, not a skip" \
        1 "declares NO \`permissions:\` block" ""
algebra "a bare \`permissions:\` with no value is exit 3 (ambiguous), NOT a crash reported as a finding" \
        3 "present but has no value" $'permissions:'

# read < write. THE ordering of the level lattice, and until now nothing exercised it: every leg
# above uses a callee that needs only `read`, so a mis-ordered LEVELS map (write below read) would
# have passed the whole suite while reporting green on a caller that grants `read` to a callee
# demanding `write` — a real startup_failure, shipped green.
RW="$WORK/r-write"; mkdir -p "$RW/registry"
callee_root "$RW" "cal.yml" $'permissions:\n  contents: write'
roster "$RW/registry/repos.yml" FS-GG/R
WRW="$WORK/w-read-lt-write"; mkdir -p "$WRW/FS-GG__R"
caller "$WRW/FS-GG__R/c.yml" $'permissions:\n  contents: read' "cal.yml@main"
expect "read < write: a caller granting read to a callee that needs WRITE is caught" \
  1 "contents: needs write, caller grants read" "$WRW" "$RW"
WRW2="$WORK/w-write-ok"; mkdir -p "$WRW2/FS-GG__R"
caller "$WRW2/FS-GG__R/c.yml" $'permissions:\n  contents: write' "cal.yml@main"
expect "...and granting write satisfies it" 0 "ok:" "$WRW2" "$RW"

# Job-level precedence: the job's block WINS over the workflow's. Reading only the top-level block
# would report green on a job that narrows its own grant — and red on one that widens it.
algebra "a job-level block that NARROWS below the callee is caught" \
        1 "packages: needs read, caller grants none" \
        $'permissions:\n  contents: read\n  packages: read' \
        $'    permissions:\n      contents: read'
algebra "a job-level block that satisfies the callee passes, despite a bare top level" \
        0 "ok:" $'permissions:\n  contents: read' \
        $'    permissions:\n      contents: read\n      packages: read'

# =============================================================================================
# 2b. App-token installation grants. A request outside the pinned grant inventory makes GitHub
# refuse the ENTIRE mint before any later step runs, so it must be a pre-merge finding too.
# =============================================================================================
cat > "$RC/.github/workflows/app-token.yml" <<'YAML'
name: app token fixture
on: { workflow_dispatch: }
jobs:
  mint:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/create-github-app-token@v3
        with:
          app-id: 1
          private-key: x
          permission-issues: write
YAML
WAPP="$WORK/w-app-token"; mkdir -p "$WAPP/FS-GG__R"
caller "$WAPP/FS-GG__R/c.yml" $'permissions:\n  contents: read\n  packages: read' "cal.yml@main"
expect "an App-token request outside the pinned installation grants is caught before merge" \
  1 "issues: requests write, installation grants none" "$WAPP" "$RC" \
  --app-grants contents:read
expect "the same App-token request is green when the inventory grants it" \
  0 "ok:" "$WAPP" "$RC" \
  --app-grants contents:read,issues:write

# This is the inversion record: changing the real historical bad scope back into the workflow
# makes the gate red.  It proves the new relation, rather than merely exercising a synthetic parser.
cp .github/workflows/kit-auto-publish.yml "$RC/.github/workflows/kit-auto-publish.before.yml"
sed -i '/permission-issues: write/a\          permission-organization-packages: read' \
  "$RC/.github/workflows/kit-auto-publish.before.yml"
expect "INVERSION: pre-#2234 kit-auto-publish organisation-packages request red-lights" \
  1 "organization_packages: requests read, installation grants none" "$WAPP" "$RC" \
  --app-grants contents:write,issues:write,packages:read,pull_requests:write
rm "$RC/.github/workflows/kit-auto-publish.before.yml"

# =============================================================================================
# 3. Fail closed. "I could not check" is never green, and never a finding either.
# =============================================================================================
WU="$WORK/w-unreach"; mkdir -p "$WU/FS-GG__R"; : > "$WU/FS-GG__R.unreachable"
caller "$WU/FS-GG__R/c.yml" $'permissions:\n  contents: read' "cal.yml@main"
expect "an unreachable repo is exit 2 (RETRYABLE) — not green, not a finding" \
  2 "no verdict" "$WU" "$RC"

WI="$WORK/w-invis"; mkdir -p "$WI"; : > "$WI/FS-GG__R.invisible"
expect "a repo that 404s entirely is exit 2 — a private/renamed repo is not 'adopts nothing'" \
  2 "not readable" "$WI" "$RC"

# A visible repo with no .github/workflows adopts nothing — that is not a finding. But then the run
# has audited nothing at all, and examining nothing is a failure to audit, not a clean audit.
WE="$WORK/w-empty"; mkdir -p "$WE"; : > "$WE/FS-GG__R.visible"
expect "auditing ZERO caller/callee pairs is exit 3, never a vacuous green" \
  3 "audited 0 caller/callee pair(s)" "$WE" "$RC"

# =============================================================================================
# 4. The callee side, and ref resolution.
# =============================================================================================
WM="$WORK/w-missing-callee"; mkdir -p "$WM/FS-GG__R"
caller "$WM/FS-GG__R/c.yml" $'permissions:\n  contents: read' "ghost.yml@main"
expect "a caller naming a callee that does not exist is exit 3 (it cannot start either)" \
  3 "has no .github/workflows/ghost.yml" "$WM" "$RC"

# A pinned SHA resolves against the API at THAT ref, not against the local tree. Here the local
# callee needs packages:read but the pinned one does not — a caller granting only contents is fine.
SHA=5fed2838f9ed085ffca09f4cc18b4f7bc59c1294
WP="$WORK/w-pinned"; mkdir -p "$WP/FS-GG__R" "$WP/refs/$SHA"
caller "$WP/FS-GG__R/c.yml" $'permissions:\n  contents: read' "cal.yml@$SHA"
{ echo "on: { workflow_call: }"; echo "permissions:"; echo "  contents: read"; } \
  > "$WP/refs/$SHA/cal.yml"
expect "a SHA-pinned caller is judged against the callee AT THAT REF, not against local main" \
  0 "ok:" "$WP" "$RC"

# ...and the converse: the pinned callee needs MORE than the caller grants -> still caught.
WP2="$WORK/w-pinned-bad"; mkdir -p "$WP2/FS-GG__R" "$WP2/refs/$SHA"
caller "$WP2/FS-GG__R/c.yml" $'permissions:\n  contents: read' "cal.yml@$SHA"
{ echo "on: { workflow_call: }"; echo "permissions:"; echo "  contents: read"; echo "  packages: read"; } \
  > "$WP2/refs/$SHA/cal.yml"
expect "an under-granting SHA-pinned caller is still caught" \
  1 "packages: needs read" "$WP2" "$RC"

# A TAG is not the working tree either. Only @main is. The local callee here needs packages:read;
# the tagged one does not, so a caller granting only contents must pass — and would have been
# red-lighted for a scope its callee never declared if tags resolved locally.
WT="$WORK/w-tag"; mkdir -p "$WT/FS-GG__R" "$WT/refs/v1.2.0"
caller "$WT/FS-GG__R/c.yml" $'permissions:\n  contents: read' "cal.yml@v1.2.0"
{ echo "on: { workflow_call: }"; echo "permissions:"; echo "  contents: read"; } \
  > "$WT/refs/v1.2.0/cal.yml"
expect "a TAG-pinned caller is judged against the callee AT THAT TAG, not against local main" \
  0 "ok:" "$WT" "$RC"

WPX="$WORK/w-pinned-404"; mkdir -p "$WPX/FS-GG__R" "$WPX/refs"
caller "$WPX/FS-GG__R/c.yml" $'permissions:\n  contents: read' "cal.yml@$SHA"
expect "a callee that 404s at the pinned ref is exit 3, not a skip" \
  3 "at ref $SHA" "$WPX" "$RC"

RNC="$WORK/r-not-callable"; mkdir -p "$RNC/.github/workflows" "$RNC/registry"
{ echo "on: { push: }"; echo "jobs: { x: { runs-on: ubuntu-latest, steps: [{ run: 'true' }] } }"; } \
  > "$RNC/.github/workflows/cal.yml"
roster "$RNC/registry/repos.yml" FS-GG/R
WNC="$WORK/w-not-callable"; mkdir -p "$WNC/FS-GG__R"
caller "$WNC/FS-GG__R/c.yml" $'permissions:\n  contents: read' "cal.yml@main"
expect "a callee without \`on: workflow_call\` is exit 3 — that call cannot start either" \
  3 "does not declare \`on: workflow_call\`" "$WNC" "$RNC"

# =============================================================================================
# 5. Absence of adoption is NOT this gate's question (that is #269's).
# =============================================================================================
RTWO="$WORK/r-two"; mkdir -p "$RTWO/registry"
callee_root "$RTWO" "cal.yml"   $'permissions:\n  contents: read'
callee_root "$RTWO" "other.yml" $'permissions:\n  contents: write'
roster "$RTWO/registry/repos.yml" FS-GG/R
WNA="$WORK/w-nonadopter"; mkdir -p "$WNA/FS-GG__R"
caller "$WNA/FS-GG__R/c.yml" $'permissions:\n  contents: read' "cal.yml@main"
expect "a repo that calls ONE reusable workflow is not flagged for the one it does not call" \
  0 "ok:" "$WNA" "$RTWO"

# =============================================================================================
# 6. The gate's own shipped surface.
# =============================================================================================
if python3 - "$REPO_ROOT/.github/workflows/permission-coherence.yml" <<'PY'
import sys, yaml
d = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
perms = d.get("permissions")
assert isinstance(perms, dict) and perms.get("contents") == "read", f"top-level permissions: {perms}"
body = "".join(str(s.get("run", "")) for j in d["jobs"].values() for s in j.get("steps", []))
assert "check-workflow-permissions.py" in body, "the gate workflow never runs the gate"
assert "tests/workflow-permissions/run.sh" in body, "the gate workflow never runs this fixture"
assert "--app-grants" in body and "FSGG_APP_GRANTS" in body, "the gate never checks App-token grants"
live = d["jobs"].get("installation-grants-current", {})
assert live.get("timeout-minutes"), "live grant reconciliation has no timeout"
assert "permission-organization-administration" in str(live), "live grant reconciliation cannot read the installation"
PY
then ok "the shipped permission-coherence.yml runs caller and App-token checks plus live grant reconciliation"
else bad "the shipped permission-coherence.yml is not the shape this fixture asserts"
fi

echo
echo "workflow-permissions fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::workflow-permissions fixture FAILED"; exit 1; }
echo "workflow-permissions fixture — OK"
