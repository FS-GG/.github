#!/usr/bin/env bash
# Fixture for scripts/skills-digest-assert.sh — the cross-repo "registry = manifest = bytes" gate
# (.github#247). Proves the audit passes a coherent registry and fails loudly on each way the
# invariant can rot: a stale sha256, a source that was renamed/moved away, an owner that disagrees
# with its source root, and a FS.GG.Rendering frozen copy that has drifted from the fs-gg-game
# canonical body (ADR-0022 §6). Also proves the two deliberate NON-failures: a missing frozen copy is
# the P6 end-state (not drift), and --no-mirror skips the two-copies check entirely.
#
# Drives the audit against a temp registry and a PATH-shim `gh` serving canned repo-contents blobs.
# No network. Mirrors tests/repos-audit/run.sh (gh stub) + tests/surface-impact/run.sh (temp registry).

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
AUDIT="$HERE/../../scripts/skills-digest-assert.sh"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/skills-digest-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT
STUB="$WORK/bin"; mkdir -p "$STUB"
export FIX="$WORK/fix"; mkdir -p "$FIX"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# gh stub: serves `gh api -H ... repos/<owner>/<repo>/contents/<path>?ref=<ref>` from
# $FIX/<slug>/<path>, slug = repo full with '/' -> '__'.
#
# It reproduces REAL `gh api` failure shapes, because the audit's whole safety property is telling
# them apart. Verified against gh 2.96.0:
#   * 404 -> the JSON error body on STDOUT, `gh: Not Found (HTTP 404)` on STDERR, exit 1.
#   * 403 rate limit -> JSON body on STDOUT, `gh: ... (HTTP 403)` on STDERR, exit 1.
# A stub that merely exited 1 with no output (the first draft) made the fail-OPEN mirror bug and the
# fail-CLOSED "renamed, moved, or deleted" misdiagnosis both invisible — every assertion still passed.
#
# $STUB_FAIL_TARGET = "<slug>:<path>" forces the rate-limit shape for exactly one repo+path, so a
# transient failure can be injected into the mirror loop without disturbing the row loop.
cat > "$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -euo pipefail
arg=""
for a in "$@"; do case "$a" in repos/*) arg="$a";; esac; done
[ -n "$arg" ] || { echo "stub gh: no repos/ path in args" >&2; exit 1; }
raw="${arg#repos/}"
repo="${raw%%/contents/*}"          # FS-GG/FS.GG.Game
tail="${raw#*/contents/}"           # <path>?ref=<ref>
path="${tail%%\?*}"
slug="${repo//\//__}"

if [ -n "${STUB_FAIL_TARGET:-}" ] && [ "$slug:$path" = "$STUB_FAIL_TARGET" ]; then
  printf '{"message":"API rate limit exceeded for installation ID 1.","status":"403"}'
  echo "gh: API rate limit exceeded for installation ID 1. (HTTP 403)" >&2
  exit 1
fi

f="$FIX/$slug/$path"
if [ ! -f "$f" ]; then
  printf '{"message":"Not Found","documentation_url":"https://docs.github.com/rest","status":"404"}'
  echo "gh: Not Found (HTTP 404)" >&2
  exit 1
fi
cat "$f"
STUB
chmod +x "$STUB/gh"

# Place a body at <repo>:<path> in the stub filesystem.
put() {  # <repo-full> <path> <content>
  local slug="${1//\//__}" dir
  dir="$FIX/$slug/$(dirname "$2")"; mkdir -p "$dir"
  printf '%s' "$3" > "$FIX/$slug/$2"
}
rmfile() { local slug="${1//\//__}"; rm -f "$FIX/$slug/$2"; }
sha_of() { local slug="${1//\//__}"; sha256sum "$FIX/$slug/$2" | cut -d' ' -f1; }

GAME="FS-GG/FS.GG.Game"; REND="FS-GG/FS.GG.Rendering"
ALPHA_SRC="template/product-skills/alpha/SKILL.md"
BETA_SRC="template/product-skills/beta/SKILL.md"

# Seed a coherent world: `alpha` owned by fs-gg-game (with a byte-identical frozen copy in
# Rendering), `beta` owned by fs-gg-rendering.
seed() {
  rm -rf "$FIX"; mkdir -p "$FIX"
  put "$GAME" "$ALPHA_SRC" $'---\nname: alpha\n---\nthe alpha body\n'
  put "$REND" "$ALPHA_SRC" $'---\nname: alpha\n---\nthe alpha body\n'   # frozen copy (ADR-0022 §6)
  put "$REND" "$BETA_SRC"  $'---\nname: beta\n---\nthe beta body\n'
}

# Write a registry whose digests are taken from the stub filesystem, with optional overrides.
# mkreg [alpha_sha] [beta_sha] [alpha_owner]
mkreg() {
  local asha="${1:-$(sha_of "$GAME" "$ALPHA_SRC")}"
  local bsha="${2:-$(sha_of "$REND" "$BETA_SRC")}"
  local aowner="${3:-fs-gg-game}"
  cat > "$WORK/skills.yml" <<YAML
schemaVersion: 1
updated: "2026-07-08"
parameters: [profile]
skills:
  - { id: alpha, scope: product, owner: $aowner, source: FS.GG.Game/$ALPHA_SRC, sha256: $asha, materializes-when: always }
  - { id: beta,  scope: product, owner: fs-gg-rendering, source: FS.GG.Rendering/$BETA_SRC, sha256: $bsha, materializes-when: always }
YAML
}

run() { PATH="$STUB:$PATH" bash "$AUDIT" --registry "$WORK/skills.yml" "$@"; }

echo "skills-digest fixture"

# 1. a coherent registry passes, and reports the frozen copy as checked
seed; mkreg
if out="$(run 2>&1)"; then
  printf '%s' "$out" | grep -q '1 frozen copy/copies compared — 0 drifted' \
    && ok "coherent registry -> audit passes, frozen copy checked" \
    || bad "coherent registry (frozen copy not counted)" "$out"
else bad "coherent registry" "$out"; fi

# 2. a stale sha256 fails, names the row, and prints both digests
seed; mkreg "0000000000000000000000000000000000000000000000000000000000000000"
out="$(run 2>&1)" && rc=0 || rc=$?
real="$(sha_of "$GAME" "$ALPHA_SRC")"
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'alpha: registry sha256 is stale' \
    && printf '%s' "$out" | grep -q "$real"; } \
  && ok "stale sha256 -> fails, names the row and both digests" || bad "stale sha256" "rc=$rc: $out"

# 3. a source that was renamed/moved away fails as missing (not as a digest mismatch)
seed; mkreg; rmfile "$REND" "$BETA_SRC"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'beta: source not found'; } \
  && ok "renamed/deleted source -> fails as missing" || bad "missing source" "rc=$rc: $out"

# 4. an owner that disagrees with its source root fails (row claims rendering, sources from Game)
seed; mkreg "" "" "fs-gg-rendering"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "alpha: owner 'fs-gg-rendering' disagrees"; } \
  && ok "owner disagrees with source root -> fails" || bad "owner mismatch" "rc=$rc: $out"

# 5. a drifted frozen copy fails the ADR-0022 §6 two-copies invariant (registry itself is coherent)
seed; put "$REND" "$ALPHA_SRC" $'---\nname: alpha\n---\nthe DRIFTED alpha body\n'; mkreg
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'two-copies invariant is broken'; } \
  && ok "drifted frozen copy -> fails the two-copies invariant" || bad "mirror drift" "rc=$rc: $out"

# 5b. ...and the row-level audit still reports the registry as coherent (the failure is the mirror)
{ printf '%s' "$out" | grep -q '2 row(s) — 2 coherent, 0 violation(s)'; } \
  && ok "mirror drift is reported separately from row coherence" || bad "mirror drift accounting" "$out"

# 6. NO frozen copy is the ADR-0022 P6 end-state, not drift -> passes, counts zero mirrors
seed; rmfile "$REND" "$ALPHA_SRC"; mkreg
if out="$(run 2>&1)"; then
  printf '%s' "$out" | grep -q '0 frozen copy/copies compared — 0 drifted' \
    && ok "no frozen copy (P6 end-state) -> passes, self-retiring" || bad "P6 end-state count" "$out"
else bad "P6 end-state" "$out"; fi

# 7. --no-mirror skips the two-copies check even when the frozen copy has drifted
seed; put "$REND" "$ALPHA_SRC" $'---\nname: alpha\n---\nthe DRIFTED alpha body\n'; mkreg
if out="$(run --no-mirror 2>&1)"; then
  ok "--no-mirror skips the two-copies check"
else bad "--no-mirror" "$out"; fi

# 8. a source with no repo-directory prefix is a misconfigured row, not a fetch attempt
seed
cat > "$WORK/skills.yml" <<YAML
schemaVersion: 1
skills:
  - { id: rootless, scope: product, owner: fs-gg-game, source: SKILL.md, sha256: dead, materializes-when: always }
YAML
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "rootless: source 'SKILL.md' has no repo-directory prefix"; } \
  && ok "source without a repo prefix -> fails as misconfigured" || bad "rootless source" "rc=$rc: $out"

# 9. an unparseable / empty registry fails closed as misconfig (exit 2), never silently passes
printf 'schemaVersion: 1\nskills: []\n' > "$WORK/skills.yml"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q "non-empty top-level 'skills' array"; } \
  && ok "empty registry -> fails closed (exit 2)" || bad "empty registry" "rc=$rc: $out"

# --- infrastructure failures must never be mistaken for registry facts -------------------------
# A rate limit, 5xx, DNS blip or expired token is NOT a coherence signal. The audit must die(2) and
# say so, rather than (a) claiming the producer deleted a skill, or (b) silently skipping a check.

# 10. a transient gh failure on a SOURCE -> exit 2, and explicitly NOT "renamed, moved, or deleted"
seed; mkreg
out="$(STUB_FAIL_TARGET="FS-GG__FS.GG.Game:$ALPHA_SRC" run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'infrastructure failure' \
    && printf '%s' "$out" | grep -q 'HTTP 403' \
    && ! printf '%s' "$out" | grep -q 'renamed, moved, or deleted'; } \
  && ok "transient gh error on a source -> die(2), not a fabricated 'deleted' diagnosis" \
  || bad "transient error on source" "rc=$rc: $out"

# 11. a transient gh failure on the FROZEN COPY -> exit 2, NOT a green run that skipped the check.
#     This is the fail-open case: `|| continue` would read a 403 as "P6 retired the copy". The
#     canonical body has genuinely drifted here, so a skip would also hide a real violation.
seed; put "$REND" "$ALPHA_SRC" $'---\nname: alpha\n---\nthe DRIFTED alpha body\n'; mkreg
out="$(STUB_FAIL_TARGET="FS-GG__FS.GG.Rendering:$ALPHA_SRC" run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'infrastructure failure' \
    && ! printf '%s' "$out" | grep -q 'OK — registry = manifest = bytes'; } \
  && ok "transient gh error on the frozen copy -> die(2), never a green skip (fail-open guard)" \
  || bad "transient error on frozen copy" "rc=$rc: $out"

# 12. a row missing a required scalar is a SCHEMA problem (exit 2), not a "stale digest" claim
seed
cat > "$WORK/skills.yml" <<YAML
schemaVersion: 1
skills:
  - { id: alpha, scope: product, owner: fs-gg-game, source: FS.GG.Game/$ALPHA_SRC, materializes-when: always }
YAML
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'non-null id, owner, source and sha256' \
    && ! printf '%s' "$out" | grep -q 'sha256 is stale'; } \
  && ok "row missing sha256 -> misconfig (exit 2), not a coherence lie" || bad "row missing sha256" "rc=$rc: $out"

# 13. a ZERO-BYTE source is real content: it must digest (to e3b0c442…) and compare, never be
#     reported as missing. Declaring the empty digest makes it coherent.
EMPTY_SHA="e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
# Truncate the frozen copy too, so the mirror stays byte-identical and this case isolates the
# zero-byte digest behaviour rather than tripping the two-copies check.
empty_both() { : > "$FIX/${GAME//\//__}/$ALPHA_SRC"; : > "$FIX/${REND//\//__}/$ALPHA_SRC"; }
seed; empty_both; mkreg "$EMPTY_SHA"
if out="$(run 2>&1)"; then
  ok "zero-byte source digests as content (not 'not found')"
else bad "zero-byte source (coherent)" "$out"; fi

# ...and a zero-byte source whose declared digest is wrong is STALE, not missing
seed; empty_both; mkreg "0000000000000000000000000000000000000000000000000000000000000000"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "alpha: registry sha256 is stale" \
    && printf '%s' "$out" | grep -q "$EMPTY_SHA"; } \
  && ok "zero-byte source with a wrong digest -> stale, not 'not found'" || bad "zero-byte stale" "rc=$rc: $out"

echo "skills-digest fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::skills-digest fixture FAILED"; exit 1; }
echo "skills-digest fixture — OK"
