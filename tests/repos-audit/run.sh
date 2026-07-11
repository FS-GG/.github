#!/usr/bin/env bash
# Fixture for scripts/repos-audit.sh — the roster participation audit (ADR-0019 follow-up). Proves
# that a receiver which CALLS the reusable coordination-coherence.yml passes, a receiver that has
# workflows but does NOT call it fails, and a receiver with no workflows at all fails — driving the
# audit against a temp roster and a PATH-shim `gh` that serves canned repo-contents responses. No
# network. Mirrors tests/fsgg-coord/run.sh (gh stub) + tests/repos-registry/run.sh (temp roster).
#
# It also covers the audit's SURFACE — .github/workflows/repos-audit.yml — because an exit code the
# workflow collapses is an exit code the operator never sees (#327). The workflow's own `run:` block
# is extracted and executed, as tests/touch-set-drift/run.sh does. Pure-stdlib + PyYAML.
#
# The audit reports four outcomes: 0 wired, 1 a gap, 2 no verdict (retryable), 3 no verdict
# (permanent). 2 and 3 were one code until #335, so the workflow told them apart by grepping the
# script's prose; the legs below pin the exit codes AND assert the workflow never reads that prose
# again — including the two crossed cases a grep gets wrong.

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
#
# The audit reads its mandate from `capabilities:` (#503), so a fixture roster has to declare one —
# and declaring a capability it roster no receivers for is now a hard failure, which is the whole
# point. The legs under "every capability is audited on its own" build their own rosters to exercise
# exactly that; this base one stays minimal and coherent: one capability, two receivers.
mkreg() { cat > "$1" <<YAML
schemaVersion: 3
updated: 2026-07-04
authority: FS-GG/.github
repos:
  - { id: .github,   full: FS-GG/.github,         role: authority, receives: [labels] }
  - { id: sdd,       full: FS-GG/FS.GG.SDD,       role: framework, receives: [labels, coordination-kit] }
  - { id: rendering, full: FS-GG/FS.GG.Rendering, role: framework, receives: [labels, coordination-kit] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml }
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
# wire_wf <repo> <wf>… — the repo's one workflow file calls each named AUTHORITY reusable workflow.
# The drift legs (#503) need a repo that calls a workflow it never declared, so which workflows a
# repo calls has to be a parameter, not the single hardcoded coordination-coherence.yml it was.
wire_wf() { clearfail "$1"; local slug="${1//\//__}"; shift; local i=0 wf
            mkdir -p "$FIX/$slug"; printf '%s\n' "coord.yml" > "$FIX/$slug.list"
            { printf 'jobs:\n'
              for wf in "$@"; do i=$((i+1))
                printf '  j%s:\n    uses: FS-GG/.github/.github/workflows/%s@main\n' "$i" "$wf"
              done; } > "$FIX/$slug/coord.yml"; }
wire()   { wire_wf "$1" coordination-coherence.yml; }
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
#
# They assert exit 3, not 2: a roster that will not parse is a PERMANENT no-verdict, and a caller must
# be able to tell it from a rate limit without grepping prose (#335). Exit 2 is reserved for the
# retryable flavour — legs (4), (6), (8), (9) below.

# (1) enumerator dies (malformed registry) -> misconfig, NOT "every declared receiver is wired".
# No `wire`/`unwired` setup: the audit must die at the roster, before it ever reaches the gh stub.
#
# WHICH enumerator hits the unreadable file first is an implementation detail — since #503 the audit
# reads `capabilities:` before any receiver roster, so it now dies there. Pinning the exact enumerator
# would make this leg fail on a reordering that changes nothing it cares about. What it must pin is
# the CLAIM: whatever could not be enumerated, an unreadable roster is not an empty one.
BADREG="$WORK/bad.yml"; printf 'schemaVersion: 1\nrepos: [ {id: x,\n' > "$BADREG"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$BADREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -qE 'cannot enumerate (audited capabilities|receivers)' \
    && printf '%s' "$out" | grep -q 'not the same as empty' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "unreadable roster -> exit 3 (permanent), names the enumeration failure" \
  || bad "unreadable roster must fail closed, permanently" "rc=$rc: $out"

# (2) enumerator succeeds but yields no receivers at all -> vacuous pass is an error. The guard is
#     per-capability now (#503), so it fails on the capability's OWN NAME rather than on an aggregate.
EMPTYREG="$WORK/empty.yml"; cat > "$EMPTYREG" <<'YAML'
schemaVersion: 3
updated: 2026-07-04
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github, role: authority, receives: [labels] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml }
YAML
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$EMPTYREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q "capability 'coordination-kit'" \
    && printf '%s' "$out" | grep -q '0 rostered receivers' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "audited nothing -> exit 3 (permanent), naming the capability, not a vacuous OK" \
  || bad "empty audit must fail closed, permanently" "rc=$rc: $out"

# (2a) the aggregate backstop still exists underneath the per-capability guard. Every capability can
#      individually and honestly record `receivers: none` — and the audit then examines no repo at
#      all, which is a gate reporting on the org's participation without looking at the org.
ALLNONE="$WORK/allnone.yml"; cat > "$ALLNONE" <<'YAML'
schemaVersion: 3
updated: 2026-07-04
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }
  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml, receivers: none, reason: nobody receives it in this fixture }
YAML
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$ALLNONE" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'audited 0 receiver-capability pair' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "every capability recording 'receivers: none' -> exit 3; each leg honest, the audit vacuous" \
  || bad "the aggregate backstop must survive the per-capability guard" "rc=$rc: $out"

# (2d) the audit's mandate comes from the roster, so a roster with no `capabilities:` block gives it
#      nothing to audit. That must fail closed — it is the state of registry/repos.yml BEFORE #503,
#      and reading it as "no capabilities, therefore nothing is wrong" is the fail-open one level up.
NOCAPS="$WORK/nocaps.yml"; cat > "$NOCAPS" <<'YAML'
schemaVersion: 3
updated: 2026-07-04
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }
  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels, coordination-kit] }
YAML
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$NOCAPS" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'declares no audited capabilities' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "a roster with no capabilities: block -> exit 3, not a vacuous OK" \
  || bad "a roster with no mandate must fail closed" "rc=$rc: $out"

# (2b) a bad invocation is a permanent no-verdict too. `${2:?…}` exited 1 — indistinguishable from
#      "a declared receiver is unwired", so a typo'd flag reported itself as the finding this gate
#      exists to produce. Nothing asserted the exit code of a usage error, so nothing noticed.
for badarg in "--registry" "--nonesuch"; do
  out="$(PATH="$STUB:$PATH" bash "$AUDIT" "$badarg" 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 3 ] && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
    && ok "usage error ('$badarg') -> exit 3, never 1 (a wiring gap)" \
    || bad "usage error must not masquerade as a wiring gap" "arg=$badarg rc=$rc: $out"
done

# (2c) `--help` must document the exit contract it actually implements. The old `--help` printed a
#      hardcoded line range that stopped one line short of the `Exit:` block, so it described
#      everything about this script except the codes a caller keys on — and nothing noticed, because
#      no test read it. A usage block that silently omits its own contract is the epic's rule applied
#      to documentation: the record of the behaviour stood in for the behaviour.
help_out="$(bash "$AUDIT" --help 2>&1)" && hrc=0 || hrc=$?
help_missing=""
for spec in "0 = every declared receiver is wired" "1 = at least one gap" \
            "2 = no verdict, RETRYABLE" "3 = no verdict, PERMANENT"; do
  printf '%s' "$help_out" | grep -qF "$spec" || help_missing="$help_missing
  missing: $spec"
done
{ [ "$hrc" -eq 0 ] && [ -z "$help_missing" ]; } \
  && ok "--help exits 0 and documents all four exit codes" \
  || bad "--help does not document the exit contract it implements" "rc=$hrc$help_missing"

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

# --- every capability is audited ON ITS OWN (#503, child of #266) --------------------------------
# The masking bug. The non-vacuity guard summed the examined pairs ACROSS capabilities, so one
# populated leg satisfied it for all of them: `coordination-kit` contributed six, `lockfile-sync` and
# `contract-coherence` each iterated nothing, and the audit printed "every declared receiver is
# wired" having examined one third of its own mandate. Meanwhile six repos had really adopted
# lockfile-sync and the roster never caught up — so the gate whose literal job is "is every declared
# receiver wired?" was structurally blind to a six-repo fabric (FS.GG.Game#137: its lockfile-sync
# caller startup_failed 119 consecutive times and no gate said a word).
#
# Both directions are asserted here, because fixing only the forward one leaves the roster free to rot
# again: a capability with no rostered receiver must fail ON ITS OWN NAME, and a repo that really
# wires a capability it never declared must be REPORTED rather than silently believed absent.

# helper: a roster declaring <caps-yaml> over the two-receiver repo set, with `receives` overridable.
mkreg2() { # $1 = file, $2 = sdd receives, $3 = rendering receives, $4… = capability rows
  local f="$1" sdd="$2" rend="$3"; shift 3
  { printf 'schemaVersion: 3\nupdated: 2026-07-04\nauthority: FS-GG/.github\nrepos:\n'
    printf '  - { id: .github,   full: FS-GG/.github,         role: authority, receives: [labels] }\n'
    printf '  - { id: sdd,       full: FS-GG/FS.GG.SDD,       role: framework, receives: [%s] }\n' "$sdd"
    printf '  - { id: rendering, full: FS-GG/FS.GG.Rendering, role: framework, receives: [%s] }\n' "$rend"
    printf 'capabilities:\n'
    printf '  %s\n' "$@"; } > "$f"
}

# (16) THE REGRESSION. Two capabilities; only one has rostered receivers. Under the summed guard this
#      exited 0 — six wired pairs, "every declared receiver is wired" — while lockfile-sync audited
#      nothing. It must now exit 3 and NAME lockfile-sync as the leg it could not audit.
MASKREG="$WORK/mask.yml"
mkreg2 "$MASKREG" "labels, coordination-kit" "labels, coordination-kit" \
  "- { id: coordination-kit, workflow: coordination-coherence.yml }" \
  "- { id: lockfile-sync,    workflow: lockfile-sync.yml }"
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MASKREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q "capability 'lockfile-sync'" \
    && printf '%s' "$out" | grep -q '0 rostered receivers' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "a capability with 0 rostered receivers fails on its OWN name, though a sibling has two" \
  || bad "the populated leg still masks the empty one (#503)" "rc=$rc: $out"

# (17) `receivers: none` is a RECORDED claim, and it holds: nobody wires the workflow, so the audit
#      passes — having actually scanned every repo for an adopter rather than skipping the leg.
NONEREG="$WORK/none.yml"
mkreg2 "$NONEREG" "labels, coordination-kit" "labels, coordination-kit" \
  "- { id: coordination-kit,   workflow: coordination-coherence.yml }" \
  "- { id: contract-coherence, workflow: contract-coherence.yml, receivers: none, reason: nobody adopted it yet }"
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$NONEREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'nobody adopted it yet' \
    && printf '%s' "$out" | grep -q 'The claim holds'; } \
  && ok "'receivers: none' + no adopter -> passes, and the log says the claim was CHECKED" \
  || bad "a recorded 'no receivers' claim must pass when true" "rc=$rc: $out"

# (18) ...and it is FALSIFIABLE, which is what stops it being a mute button. Rendering really calls
#      contract-coherence.yml while the roster records the capability as having no receivers. The
#      audit must go red and say the recorded claim is false — not skip the leg because a human once
#      wrote a reason down.
wire FS-GG/FS.GG.SDD; wire_wf FS-GG/FS.GG.Rendering coordination-coherence.yml contract-coherence.yml
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$NONEREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'FS.GG.Rendering calls' \
    && printf '%s' "$out" | grep -qi 'claim is now FALSE' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "'receivers: none' + a real adopter -> exit 1: the recorded claim is falsified, not trusted" \
  || bad "a 'no receivers' claim must not mute a real adopter" "rc=$rc: $out"

# (19) THE RECURRENCE GUARD. lockfile-sync's six adopters were real, and unrostered — which is why
#      `list --receives lockfile-sync` returned nothing and the audit believed the capability had no
#      receivers. The forward check CANNOT see this by construction: it starts from the declaration
#      that is missing. So the audit sweeps every rostered repo for a caller it did not expect.
DRIFTREG="$WORK/drift.yml"
mkreg2 "$DRIFTREG" "labels, coordination-kit, lockfile-sync" "labels, coordination-kit" \
  "- { id: coordination-kit, workflow: coordination-coherence.yml }" \
  "- { id: lockfile-sync,    workflow: lockfile-sync.yml }"
wire_wf FS-GG/FS.GG.SDD       coordination-coherence.yml lockfile-sync.yml
wire_wf FS-GG/FS.GG.Rendering coordination-coherence.yml lockfile-sync.yml   # adopted, never rostered
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$DRIFTREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS.GG.Rendering calls .*lockfile-sync\.yml" \
    && printf '%s' "$out" | grep -q "does not declare 'receives: lockfile-sync'" \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "an adopted-but-unrostered capability -> exit 1, naming the repo and the capability" \
  || bad "an unrostered adopter must not be invisible (#503)" "rc=$rc: $out"

# (20) ...and the guard must not fire on the AUTHORITY running its own workflow. .github calls
#      contract-coherence.yml on itself with a LOCAL `uses: ./.github/workflows/…`, which is not
#      roster participation. Matching it would make the authority a phantom adopter of every
#      capability it hosts — a fabricated finding, on every run, forever.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
mkdir -p "$FIX/FS-GG__.github"; printf '%s\n' "self.yml" > "$FIX/FS-GG__.github.list"
printf 'jobs:\n  self:\n    uses: ./.github/workflows/coordination-coherence.yml\n' > "$FIX/FS-GG__.github/self.yml"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && ! printf '%s' "$out" | grep -q 'FS-GG/.github calls'; } \
  && ok "the authority's local 'uses: ./…' self-call is not roster participation" \
  || bad "the authority must not be a phantom adopter of a workflow it hosts" "rc=$rc: $out"
rm -f "$FIX/FS-GG__.github.list"; rm -rf "$FIX/FS-GG__.github"

# --- the SURFACE keeps the distinction the script draws (#327, i-followup of #266) ---
# Every assertion above is about the script's exit code. The exit code is not what an operator reads:
# they read the run's failing step. `run: bash scripts/repos-audit.sh` renders 1 and 2 as one
# undifferentiated red X, so the script's careful "this result means nothing" is buried under a check
# shaped exactly like a real finding — and a red that routinely lies stops being read (#270).
#
# So assert on the workflow: the two outcomes must reach a reader as different things, an inconclusive
# run must not go green, and the `if:` predicates that classify rc must themselves be gated — an
# unenumerated exit code is the same fail-open one layer up.
shape="$(python3 - "$HERE/../.." <<'PY'
import sys, pathlib, re, yaml

root = pathlib.Path(sys.argv[1])
wf = yaml.safe_load((root / ".github/workflows/repos-audit.yml").read_text())
st = yaml.safe_load((root / ".github/workflows/repos-audit-selftest.yml").read_text())
steps = wf["jobs"]["audit"]["steps"]
bad = []

# Match COMMANDS, not text. A plain `"exit 1" in run` also matches the words in a comment or inside
# an echoed summary line, so it would keep passing over a step someone had quietly changed to exit 0
# — a gate that reports green about a subject it never looked at, which is the bug this file exists
# to stop. Strip comments, then anchor.
def cmds(step):
    return "\n".join(re.sub(r"#.*$", "", ln) for ln in step.get("run", "").splitlines())
def runs(step, pattern):
    return re.search(pattern, cmds(step), re.M) is not None

# The audit step must capture the exit code rather than let it decide the job.
audit = [s for s in steps if s.get("id") == "audit"]
if not audit:
    bad.append("no step with `id: audit` — nothing captures the audit's exit code")
elif not runs(audit[0], r'>>\s*"\$GITHUB_OUTPUT"'):
    bad.append("the audit step does not publish its rc to $GITHUB_OUTPUT; the raw exit code decides the job")

# One classifying step per outcome the script can produce, each keyed on that rc.
def classifier(rc):
    return [s for s in steps if f"steps.audit.outputs.rc == '{rc}'" in str(s.get("if", ""))]

for rc, must_fail in ((0, False), (1, True), (2, True), (3, True)):
    got = classifier(rc)
    if len(got) != 1:
        bad.append(f"exit {rc} is classified by {len(got)} step(s), want exactly 1")
        continue
    fails = runs(got[0], r"^\s*exit 1\s*$")
    if must_fail and not fails:
        bad.append(f"exit {rc}'s step does not fail the job — 'could not check'/'is broken' must not go green")
    if not must_fail and fails:
        bad.append(f"exit {rc}'s step fails the job, but exit {rc} is a clean audit")

# 1, 2 and 3 must be *distinguishable at a glance*: different failing step names, different
# annotations. 2 and 3 are both no-verdicts, so both must SAY so — but they must not say only that,
# because their remedies are opposites: re-run the workflow vs. commit a fix to the roster.
names = {rc: classifier(rc)[0].get("name", "") for rc in (1, 2, 3) if classifier(rc)}
if len(names) == 3:
    if len(set(names.values())) != 3:
        bad.append(f"the gap / retryable / permanent steps must not share a name: {names}")
    for rc in (2, 3):
        if "INCONCLUSIVE" not in names[rc].upper():
            bad.append(f"exit {rc}'s step name does not say it reached no verdict: {names[rc]!r}")
    if not all(runs(classifier(rc)[0], r"::error title=") for rc in (1, 2, 3)):
        bad.append("every classifying step must emit a titled ::error:: annotation")

# The retry must key on the EXIT CODE, not on the script's prose (#335). A `grep` of the audit's
# output re-creates the exact coupling this item removed: reword the diagnostic, and the workflow
# either stops retrying a rate limit or starts retrying an unparseable roster for 15 minutes.
if audit:
    body = cmds(audit[0])
    if re.search(r"grep[^\n]*could not determine", body):
        bad.append("the audit step greps the script's prose to decide whether to retry; key on the exit code (#335)")
    if not re.search(r'"\$rc"\s+-eq\s+2', body):
        bad.append("the audit step does not gate its retry on rc == 2; only the retryable no-verdict may be retried")
    if re.search(r'"\$rc"\s+-eq\s+3', body):
        bad.append("the audit step retries on rc == 3, which is the PERMANENT no-verdict — re-running cannot change it")

# The `if:` set is a scoping predicate. An rc it does not enumerate must still be caught, or a crashed
# audit matches no classifier and the job goes green having audited nothing.
catchall = [s for s in steps if "cancelled()" in str(s.get("if", "")) and "audit.outputs.rc" in str(s.get("if", ""))
            and runs(s, r"^\s*exit 1\s*$")]
if not catchall:
    bad.append("no catch-all step: an exit code no `if:` enumerates would leave the job green")
else:
    # ...and the catch-all's OWN enumeration must be exactly the set of classified codes. That list is
    # hand-maintained, which is the same fail-open one layer up. A code listed there with no
    # classifier matches NOTHING — not a classifier, and not the catch-all that excluded it — so the
    # job goes green having audited nothing. A classified code missing from it double-reports
    # instead, telling the operator the workflow does not understand an exit code it demonstrably
    # does. Neither shows up above, because both leave every individual step perfectly well-formed.
    m = re.search(r"fromJSON\('\[([^\]]*)\]'\)", str(catchall[0]["if"]))
    if not m:
        bad.append("the catch-all's `if:` does not enumerate rc values via fromJSON([...]); its scope cannot be checked")
    else:
        listed = set(re.findall(r'"(\d+)"', m.group(1)))
        # Derived from the steps, not probed over a numeric range: a bound would silently stop
        # checking above itself, which is the very thing being asserted against.
        classified = set(re.findall(r"steps\.audit\.outputs\.rc == '(\d+)'",
                                    "\n".join(str(s.get("if", "")) for s in steps)))
        for rc in sorted(listed - classified):
            bad.append(f"the catch-all enumerates exit {rc}, but no step classifies it: rc={rc} matches nothing and the job goes GREEN")
        for rc in sorted(classified - listed):
            bad.append(f"exit {rc} has a classifier the catch-all does not enumerate: rc={rc} fires both, reporting 'no exit code this workflow understands' about one it does")

# ...and this very assertion is only run when the selftest's paths: filter says so. If repos-audit.yml
# is outside it, the workflow can be gutted and nothing re-checks its shape: the gate never runs.
for trigger in ("pull_request", "push"):
    if ".github/workflows/repos-audit.yml" not in st[True][trigger]["paths"]:
        bad.append(f"repos-audit-selftest {trigger} paths: does not cover repos-audit.yml — this check would not run on an edit to it")

print("\n".join(bad))
PY
)"
[ -z "$shape" ] && ok "the workflow renders gap, both no-verdicts and crash as distinguishable outcomes, and retries by exit code" \
  || bad "repos-audit.yml collapses outcomes a reader must tell apart" "$shape"

# --- and the audit step's own `run:` block, EXECUTED (not just shaped) --------------------------
# The assertions above read YAML. They cannot see whether the retry actually re-runs, whether the rc
# it publishes is the second pass's, or whether the discarded pass's annotations leak into a green
# run. So extract the shipped block and run it, exactly as tests/touch-set-drift/run.sh does for its
# gate: a retyped copy would keep passing after someone edits the workflow.
STEP="$WORK/audit-step.sh"
python3 - "$HERE/../.." "$STEP" <<'PY'
import sys, pathlib, yaml
wf = yaml.safe_load((pathlib.Path(sys.argv[1]) / ".github/workflows/repos-audit.yml").read_text())
run = next(s["run"] for s in wf["jobs"]["audit"]["steps"] if s.get("id") == "audit")
assert "${{" not in run, "the audit step grew an Actions expression; this fixture would run different code than CI"
pathlib.Path(sys.argv[2]).write_text(run)
PY

# A stub audit whose exit code and output come from the fixture, counting how many times it ran.
# `bash -eo pipefail` is the runner's own shell for a `run:` block; anything else tests a different
# program. RETRY_AFTER is 0 — the fixture must never actually sleep.
SBOX="$WORK/sbox"; mkdir -p "$SBOX/scripts"
# Sets STEP_OUT / STEP_RC / PASSES. It must not be called inside `$(…)`: that is a subshell, and the
# variables would never reach the assertion.
step() { # $1..= per-pass "<rc>:<output>"
  local i=1 spec; : > "$SBOX/passes"; : > "$SBOX/gh_out"
  for spec in "$@"; do printf '%s\n' "${spec#*:}" > "$SBOX/out.$i"; echo "${spec%%:*}" > "$SBOX/rc.$i"; i=$((i+1)); done
  cat > "$SBOX/scripts/repos-audit.sh" <<'STUBSH'
n=$(( $(wc -l < "$SBOX/passes") + 1 )); echo x >> "$SBOX/passes"
cat "$SBOX/out.$n"
exit "$(cat "$SBOX/rc.$n")"
STUBSH
  ( cd "$SBOX" && SBOX="$SBOX" GITHUB_OUTPUT="$SBOX/gh_out" REPOS_AUDIT_RETRY_AFTER_S=0 \
      bash -eo pipefail "$STEP" ) > "$SBOX/stdout" 2>&1
  STEP_OUT="$(cat "$SBOX/stdout")"
  STEP_RC="$(sed -n 's/^rc=//p' "$SBOX/gh_out")"
  PASSES="$(wc -l < "$SBOX/passes")"
}

# (10) a clean audit runs once and publishes rc=0
step '0:repos-audit: OK — every declared receiver is wired'
{ [ "$STEP_RC" = 0 ] && [ "$PASSES" -eq 1 ]; } \
  && ok "step: a clean audit publishes rc=0 and does not retry" || bad "clean audit" "rc=$STEP_RC passes=$PASSES: $STEP_OUT"

# (11) a wiring gap is NOT transient: exit 1 is believed the first time, and reported.
step '1:::error::repos-audit: FS.GG.Game receives ... but no workflow calls'
{ [ "$STEP_RC" = 1 ] && [ "$PASSES" -eq 1 ] && printf '%s' "$STEP_OUT" | grep -q 'FS.GG.Game'; } \
  && ok "step: a wiring gap is not retried, and is reported" || bad "gap retried" "rc=$STEP_RC passes=$PASSES: $STEP_OUT"

# (12) an API no-verdict IS retried, and a clean second pass wins — with the first pass's ::error::
#      annotations SUPPRESSED. Replaying them would hang red annotations on a green run, which is the
#      same "a red that lies stops being read" failure this item is about, moved into the annotation list.
step '2:::error::repos-audit: could not determine wiring for 1 receiver-capability pair(s)' \
            '0:repos-audit: OK — every declared receiver is wired'
{ [ "$STEP_RC" = 0 ] && [ "$PASSES" -eq 2 ] \
    && ! printf '%s' "$STEP_OUT" | grep -q '::error::' \
    && printf '%s' "$STEP_OUT" | grep -q 'every declared receiver is wired'; } \
  && ok "step: a transient no-verdict is retried, and the discarded pass does not annotate" \
  || bad "transient no-verdict" "rc=$STEP_RC passes=$PASSES: $STEP_OUT"

# (13) a no-verdict that persists stays a no-verdict: rc=2 reaches the classifier.
step '2:::error::repos-audit: could not determine wiring for 1 receiver-capability pair(s)' \
            '2:::error::repos-audit: could not determine wiring for 1 receiver-capability pair(s)'
{ [ "$STEP_RC" = 2 ] && [ "$PASSES" -eq 2 ] && printf '%s' "$STEP_OUT" | grep -q 'could not determine'; } \
  && ok "step: a persistent no-verdict publishes rc=2" || bad "persistent no-verdict" "rc=$STEP_RC passes=$PASSES: $STEP_OUT"

# (14) the permanent no-verdict is exit 3, and is NOT retried. Its causes — a roster that will not
#      parse, a roster naming no receiver — are deterministic reads of a file in this checkout. A
#      second identical pass returns an identical answer, so retrying only holds a runner for the
#      delay and still goes red. rc=3 must survive to the classifier verbatim.
step '3:::error::repos-audit: cannot enumerate receivers of coordination-kit — repos.sh list failed.'
{ [ "$STEP_RC" = 3 ] && [ "$PASSES" -eq 1 ] && printf '%s' "$STEP_OUT" | grep -q 'cannot enumerate'; } \
  && ok "step: a permanent no-verdict (bad roster) publishes rc=3 and is not retried" \
  || bad "die() must exit 3 and not be retried" "rc=$STEP_RC passes=$PASSES: $STEP_OUT"

# (14b) the retry decision is made on the exit code alone. A permanent no-verdict whose text happens
#       to contain the old magic sentence must STILL not be retried — this is the regression the grep
#       caused, reproduced directly: prose is not the interface.
step '3:::error::repos-audit: could not determine wiring for 1 receiver-capability pair(s)'
{ [ "$STEP_RC" = 3 ] && [ "$PASSES" -eq 1 ]; } \
  && ok "step: rc=3 is not retried even when its text matches the old grep" \
  || bad "retry keyed on prose, not exit code" "rc=$STEP_RC passes=$PASSES: $STEP_OUT"

# (14c) ...and the converse: a retryable no-verdict whose text does NOT contain that sentence is still
#       retried. Under the grep, this run gave up after one pass on a live rate limit.
step '2:::error::repos-audit: the API said no' '0:repos-audit: OK — every declared receiver is wired'
{ [ "$STEP_RC" = 0 ] && [ "$PASSES" -eq 2 ]; } \
  && ok "step: rc=2 is retried regardless of its wording" \
  || bad "retryable no-verdict skipped because of its wording" "rc=$STEP_RC passes=$PASSES: $STEP_OUT"

# (15) an exit code nobody planned for still reaches the classifier, which fails closed on it.
step '127:'
{ [ "$STEP_RC" = 127 ] && [ "$PASSES" -eq 1 ]; } \
  && ok "step: an unplanned exit code is published verbatim for the catch-all" || bad "crash rc" "rc=$STEP_RC passes=$PASSES: $STEP_OUT"

echo "repos-audit fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::repos-audit fixture FAILED"; exit 1; }
echo "repos-audit fixture — OK"
