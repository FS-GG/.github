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
#
# They assert exit 3, not 2: a roster that will not parse is a PERMANENT no-verdict, and a caller must
# be able to tell it from a rate limit without grepping prose (#335). Exit 2 is reserved for the
# retryable flavour — legs (4), (6), (8), (9) below.

# (1) enumerator dies (malformed registry) -> misconfig, NOT "every declared receiver is wired".
# No `wire`/`unwired` setup: the audit must die at the roster, before it ever reaches the gh stub.
BADREG="$WORK/bad.yml"; printf 'schemaVersion: 1\nrepos: [ {id: x,\n' > "$BADREG"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$BADREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'cannot enumerate receivers' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "unreadable roster -> exit 3 (permanent), names the enumeration failure" \
  || bad "unreadable roster must fail closed, permanently" "rc=$rc: $out"

# (2) enumerator succeeds but yields no receivers at all -> vacuous pass is an error
EMPTYREG="$WORK/empty.yml"; cat > "$EMPTYREG" <<'YAML'
schemaVersion: 1
updated: 2026-07-04
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github, role: authority, receives: [labels] }
YAML
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$EMPTYREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'audited 0 receiver-capability pair' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "audited nothing -> exit 3 (permanent), not a vacuous OK" \
  || bad "empty audit must fail closed, permanently" "rc=$rc: $out"

# (2b) a bad invocation is a permanent no-verdict too. `${2:?…}` exited 1 — indistinguishable from
#      "a declared receiver is unwired", so a typo'd flag reported itself as the finding this gate
#      exists to produce. Nothing asserted the exit code of a usage error, so nothing noticed.
for badarg in "--registry" "--nonesuch"; do
  out="$(PATH="$STUB:$PATH" bash "$AUDIT" $badarg 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 3 ] && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
    && ok "usage error ('$badarg') -> exit 3, never 1 (a wiring gap)" \
    || bad "usage error must not masquerade as a wiring gap" "arg=$badarg rc=$rc: $out"
done

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
if not any("cancelled()" in str(s.get("if", "")) and "audit.outputs.rc" in str(s.get("if", ""))
           and runs(s, r"^\s*exit 1\s*$") for s in steps):
    bad.append("no catch-all step: an exit code no `if:` enumerates would leave the job green")

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
