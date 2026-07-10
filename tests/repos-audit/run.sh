#!/usr/bin/env bash
# Fixture for scripts/repos-audit.sh — the roster participation audit (ADR-0019 follow-up). Proves
# that a receiver which CALLS the reusable coordination-coherence.yml passes, a receiver that has
# workflows but does NOT call it fails, and a receiver with no workflows at all fails — driving the
# audit against a temp roster and a PATH-shim `gh` that serves canned repo-contents responses. No
# network. Mirrors tests/fsgg-coord/run.sh (gh stub) + tests/repos-registry/run.sh (temp roster).

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
AUDIT="$HERE/../../scripts/repos-audit.sh"
REPOS_SH="$HERE/../../scripts/repos.sh"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/repos-audit-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
STUB="$WORK/bin"; mkdir -p "$STUB"
export FIX="$WORK/fix"; mkdir -p "$FIX"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# A roster with two coordination-kit receivers; the fixture toggles which are wired via $FIX.
mkreg() { cat > "$1" <<YAML
schemaVersion: 1
updated: 2026-07-04
authority: FS-GG/.github
repos:
  - { id: .github,   full: FS-GG/.github,         role: authority, receives: [labels] }
  - { id: sdd,       full: FS-GG/FS.GG.SDD,       role: framework, receives: [labels, coordination-kit] }
  - { id: rendering, full: FS-GG/FS.GG.Rendering, role: framework, receives: [labels, coordination-kit] }
YAML
}
REG="$WORK/repos.yml"; mkreg "$REG"

# gh stub: `list` (dir) prints filenames from $FIX/<slug>.list; raw file read prints $FIX/<slug>/<file>.
# slug = repo full with '/' -> '__'.
#
# The stub FAILS like gh does, because the bug under test (#320) lives entirely in how the audit reads
# a failure. A stub that always exits 0 cannot even express "unreachable", so the old one silently
# modelled a missing workflows dir as an empty-but-successful listing — the exact conflation the audit
# was making. Now: no list file => `Not Found (HTTP 404)` on stderr, exit 1, like the real API.
#   $FIX/<slug>.fail       an HTTP status every call for that repo fails with
#   $FIX/<slug>.failtimes  countdown, so a transient class recovers on a later attempt (retry test)
#   $FIX/<slug>.failfile   only *file* reads fail; the directory still lists
#   $FIX/<slug>.gone       the repo itself 404s — private, renamed, or deleted
cat > "$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
# args: api [-H ...] <path> [--jq ...]
path=""; n=$#; args=("$@")
for ((i=1;i<n;i++)); do case "${args[i]}" in repos/*) path="${args[i]}";; esac; done
# Three request kinds: the repo probe, the workflows dir, and one workflow file.
case "$path" in
  */contents/.github/workflows)   kind=list; repo="${path#repos/}"; repo="${repo%%/contents/*}" ;;
  */contents/.github/workflows/*) kind=file; repo="${path#repos/}"; repo="${repo%%/contents/*}"
                                  file="${path##*/contents/.github/workflows/}" ;;
  *)                              kind=repo; repo="${path#repos/}" ;;
esac
slug="${repo//\//__}"

notfound() { echo "gh: Not Found (HTTP 404)" >&2; exit 1; }
apifail()  { echo "gh: API rate limit exceeded for installation (HTTP $1)" >&2; exit 1; }

# Injected failure. `.failtimes`, when present, counts down to zero and then lets the call through.
if [ -f "$FIX/$slug.fail" ]; then
  left=1; [ -f "$FIX/$slug.failtimes" ] && left="$(cat "$FIX/$slug.failtimes")"
  if [ "$left" -gt 0 ]; then
    [ -f "$FIX/$slug.failtimes" ] && echo $((left - 1)) > "$FIX/$slug.failtimes"
    apifail "$(cat "$FIX/$slug.fail")"
  fi
fi
# File reads only: the directory still lists, so the audit gets partway in before it loses the API.
[ "$kind" = file ] && [ -f "$FIX/$slug.failfile" ] && apifail 403

case "$kind" in
  repo) [ -f "$FIX/$slug.gone" ] && notfound   # invisible to this token: the API says 404, not 403
        echo "$repo" ;;                        # stands in for `--jq '.full_name'`
  list) [ -f "$FIX/$slug.list" ] || notfound   # no workflows dir at all — the real API 404s here
        cat "$FIX/$slug.list" ;;
  file) [ -f "$FIX/$slug/$file" ] || notfound
        cat "$FIX/$slug/$file" ;;
esac
STUB
chmod +x "$STUB/gh"

# Helpers to shape a repo's workflows in the stub. Each clears any injected failure first, so a
# fixture step never inherits the previous step's outage.
clearfail(){ local slug="${1//\//__}"; rm -f "$FIX/$slug.fail" "$FIX/$slug.failtimes" "$FIX/$slug.failfile" "$FIX/$slug.gone"; }
wire()   { clearfail "$1"; local slug="${1//\//__}"; mkdir -p "$FIX/$slug"; printf '%s\n' "coord.yml" > "$FIX/$slug.list";
           printf 'jobs:\n  x:\n    uses: FS-GG/.github/.github/workflows/coordination-coherence.yml@main\n' > "$FIX/$slug/coord.yml"; }
unwired(){ clearfail "$1"; local slug="${1//\//__}"; mkdir -p "$FIX/$slug"; printf '%s\n' "ci.yml" > "$FIX/$slug.list";
           printf 'jobs:\n  build:\n    runs-on: ubuntu-latest\n' > "$FIX/$slug/ci.yml"; }
noflows(){ clearfail "$1"; local slug="${1//\//__}"; rm -f "$FIX/$slug.list"; rm -rf "$FIX/$slug"; }
# 403 on every call for this repo (a rate limit), 403 only until `n` attempts have burned, or 403 on
# file reads only (the dir lists fine, so the audit gets partway in before it loses the API).
unreachable()    { wire "$1"; local slug="${1//\//__}"; echo 403 > "$FIX/$slug.fail"; }
transient()      { wire "$1"; local slug="${1//\//__}"; echo 403 > "$FIX/$slug.fail"; echo "${2:-1}" > "$FIX/$slug.failtimes"; }
unreadable_file(){ wire "$1"; local slug="${1//\//__}"; : > "$FIX/$slug.failfile"; }
# The repo 404s outright, as GitHub answers for one the token cannot see. Its workflows dir 404s too,
# which is indistinguishable from an empty one until you probe the repo.
invisible()      { noflows "$1"; local slug="${1//\//__}"; : > "$FIX/$slug.gone"; }

# TRIES=1 by default: no retry, no sleep, so the failure legs are fast and deterministic. The retry
# leg overrides it. The delay is always 0 — the fixture must never actually sleep.
run() { PATH="$STUB:$PATH" REPOS_AUDIT_TRIES="${TRIES:-1}" REPOS_AUDIT_RETRY_DELAY=0 \
          bash "$AUDIT" --registry "$REG" --repos-sh "$REPOS_SH" "$@"; }

echo "repos-audit fixture"

# both receivers wired -> pass
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
if out="$(run 2>&1)"; then ok "all receivers wired -> audit passes"; else bad "all wired" "$out"; fi

# one receiver not wired (has workflows, none call the reusable) -> fail, names it
wire FS-GG/FS.GG.SDD; unwired FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'FS.GG.Rendering receives'; } \
  && ok "unwired receiver -> audit fails and names it" || bad "unwired receiver" "rc=$rc: $out"

# receiver with no workflows dir at all -> fail
wire FS-GG/FS.GG.SDD; noflows FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'FS.GG.Rendering'; } \
  && ok "receiver with no workflows -> audit fails" || bad "no workflows" "rc=$rc: $out"

# the .github authority is not a coordination-kit receiver -> never audited (no false gap)
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(run 2>&1)"
printf '%s' "$out" | grep -q 'FS-GG/.github receives' \
  && bad "authority wrongly audited" "$out" || ok "authority .github is not audited"

# --- fails closed when the roster is unreachable or empty (#316, child (h) of #266) ---
# Both legs assert on the REASON string, not a bare exit code: a script that dies for an unrelated
# reason would otherwise satisfy a plain `rc != 0` and the fixture would stop testing its own claim.

# (1) enumerator dies (malformed registry) -> misconfig, NOT "every declared receiver is wired".
# No `wire`/`unwired` setup: the audit must die at the roster, before it ever reaches the gh stub.
BADREG="$WORK/bad.yml"; printf 'schemaVersion: 1\nrepos: [ {id: x,\n' > "$BADREG"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$BADREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'cannot enumerate receivers' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "unreadable roster -> exit 2, names the enumeration failure" \
  || bad "unreadable roster must fail closed" "rc=$rc: $out"

# (2) enumerator succeeds but yields no receivers at all -> vacuous pass is an error
EMPTYREG="$WORK/empty.yml"; cat > "$EMPTYREG" <<'YAML'
schemaVersion: 1
updated: 2026-07-04
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github, role: authority, receives: [labels] }
YAML
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$EMPTYREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'audited 0 receiver-capability pair' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "audited nothing -> exit 2, not a vacuous OK" \
  || bad "empty audit must fail closed" "rc=$rc: $out"

# (3) the guards did not break the healthy path: a real audit still reports what it examined
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 receiver-capability pair(s)'; } \
  && ok "healthy roster -> still passes, having audited 2 pairs" || bad "healthy path regressed" "rc=$rc: $out"

# --- an unreadable repo is "could not determine", never "not wired" (#320, child (i) of #266) ---
# The mirror of #316: that conflated *unreachable* with *empty* and went green; this conflates
# *unreachable* with *unwired* and goes red with a fabricated finding. Both never examined the subject.

# (4) a receiver we cannot read -> exit 2, named as undetermined, and NOT accused of a wiring gap.
#     Both receivers below are wired; a run that calls either one a gap has invented its finding.
wire FS-GG/FS.GG.SDD; unreachable FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
#     The reason must quote gh's own words: the fetchers are read through `$(…)`, so an error captured
#     into a plain variable dies with the subshell and the diagnostic silently comes back blank.
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'could not determine' \
    && printf '%s' "$out" | grep -q 'HTTP 403' \
    && ! printf '%s' "$out" | grep -q 'FS.GG.Rendering receives .* but no workflow calls' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "unreachable receiver -> exit 2 'could not determine', not a fabricated gap" \
  || bad "unreachable receiver must not be reported as unwired" "rc=$rc: $out"

# (5) the over-correction guard: a 404 IS an answer. A repo with no workflows dir is a real gap
#     (exit 1), not an outage — otherwise every genuine gap would hide behind "could not determine".
wire FS-GG/FS.GG.SDD; noflows FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'FS.GG.Rendering receives .* but no workflow calls' \
    && ! printf '%s' "$out" | grep -q 'could not determine'; } \
  && ok "404 (no workflows dir) is still a genuine gap, not an outage" \
  || bad "404 must stay a gap" "rc=$rc: $out"

# (6) the API dies partway: the dir lists, the file read 403s. Still undetermined, not a gap.
wire FS-GG/FS.GG.SDD; unreadable_file FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'could not determine: reading \.github/workflows/coord\.yml' \
    && printf '%s' "$out" | grep -q 'HTTP 403'; } \
  && ok "unreadable workflow file -> exit 2, names the file and quotes gh" \
  || bad "unreadable file must fail closed" "rc=$rc: $out"

# (7) a transient 403 is retried, not believed. One failure then success -> a clean pass, and the
#     countdown proves the retry was actually spent rather than the stub having served the first call.
wire FS-GG/FS.GG.SDD; transient FS-GG/FS.GG.Rendering 1
out="$(TRIES=3 run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && [ "$(cat "$FIX/FS-GG__FS.GG.Rendering.failtimes")" -eq 0 ] \
    && printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "transient 403 is retried and the audit still passes" \
  || bad "transient failure must be retried" "rc=$rc: $out"

# (8) undetermined outranks a real gap: a run that examined only some of the roster is not a verdict,
#     so it must not exit 1 and read as "the audit ran, here are the gaps".
#     The genuine gap must still be PRINTED, though — the exit code defers to the outage, the finding
#     does not. Without this leg an early `exit 2` inside the loop would silently eat the one
#     actionable result in the run, and the assertion above would not notice.
unwired FS-GG/FS.GG.SDD; unreachable FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'the audit is incomplete' \
    && printf '%s' "$out" | grep -q 'FS.GG.SDD receives .* but no workflow calls'; } \
  && ok "undetermined outranks a gap -> exit 2, but the gap is still reported" \
  || bad "undetermined must outrank a gap" "rc=$rc: $out"

# (9) a repo the token cannot see 404s exactly like an empty one. Believing that 404 is the whole bug
#     again, one status code across: a private/renamed/deleted receiver must be undetermined, never a
#     wiring gap. Only the repo probe can tell the two apart.
wire FS-GG/FS.GG.SDD; invisible FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'FS.GG.Rendering is not readable' \
    && ! printf '%s' "$out" | grep -q 'FS.GG.Rendering receives .* but no workflow calls'; } \
  && ok "invisible repo -> exit 2 'not readable', not a fabricated gap" \
  || bad "invisible repo must not be reported as unwired" "rc=$rc: $out"

echo "repos-audit fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::repos-audit fixture FAILED"; exit 1; }
echo "repos-audit fixture — OK"
