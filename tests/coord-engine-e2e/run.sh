#!/usr/bin/env bash
# End-to-end: the COMPILED `fsgg-coord-engine`, over HTTP, against a hermetic fixture — no token, no network.
#
# `scan | decide` is the whole claim of ADR-0040: the engine reads its own board and decides, with no bash
# in the pipeline. That was proven once, by hand, against the live board — which CI cannot do (it needs a
# token). This makes it a gate: the binary talks to a fixture GitHub through the configurable API base
# (FSGG_GITHUB_API_BASE, ADR-0040 C1), and a correct engine returns exactly the one schedulable item the
# fixture's board contains.
#
# It is deliberately NOT a coverage test — the unit suites (edge 164) and the corpus (880) own that. It is
# the ONE test that exercises the real HttpTransport, the real argument parser, the real scan→decide
# composition, and the real exit codes, all in one process boundary, hermetically. If the engine could not
# reach GitHub at all, every other test would still be green and this is the only one that would not.
set -uo pipefail

# ---- WHY THIS SUITE NEVER PIPES A CAPTURED STRING INTO AN EARLY-EXITING READER (.github#2668) ------
#
# Under `pipefail` the spelling
#
#     printf '%s' "$out" | grep -q PATTERN        # <-- BANNED HERE. Read on.
#
# does not answer "does $out contain PATTERN?". `grep -q` exits the instant it matches, by contract.
# If `printf` still had bytes to write at that moment it takes SIGPIPE, the pipeline's status under
# `pipefail` becomes 141, and `if` reads 141 as "the pattern was NOT found" — for output that plainly
# contains it. The verdict is a function of how much the writer had left when the reader stopped, which
# is a buffer-size and scheduling fact, not a fact about the haystack.
#
# Measured on this repo's own engine, bash 5.3.15, Linux (.github#2668):
#
#   haystack                                   `printf … | grep -q`   `grep -q <<<"$hay"`   `case`
#   8,290 B  synthetic, needle on line 1        0     (30/30 runs)     0    (20/20)          0
#   16,564 B synthetic, needle on line 1        141   (30/30 runs)     0    (20/20)          0
#   22,956 B REAL: engine refusal + usage       141 AND 0, same host,  0    (20/20)          0
#                                               same string, run to run
#   264,782 B synthetic, needle on line 1       141   (30/30 runs)     0    (20/20)          0
#   4,236,264 B synthetic, needle on line 1     141   (30/30 runs)     0    (20/20)          0
#
# The 22,956 B row is the one that cost two legs of this suite: the engine prints its refusal and then
# its whole usage block, which lands in the size band where the race can go either way. The same
# assertion over the same bytes was observed both RED and GREEN on one host within minutes. It fails in
# the SAFE direction — a false RED, never a false green — but a suite that reds while printing its own
# passing evidence is one readers learn to skim, which is the failure #570 and #266 are about.
#
# So: assertions here go through `contains` / `matches` / `matches_i` below. They have no pipeline, so
# `pipefail` has nothing to mis-report. Leg 0 proves both directions of that at a size where the banned
# idiom is deterministically wrong, and then greps THIS FILE to keep the idiom from coming back.
#
# The class is repo-wide, and this file is only the first of it. Before this repair, 35 shell files
# under `tests/` set `pipefail` and between them carried 992 lines consuming the status of a pipeline
# that ends in an early-exiting reader (`grep -q`, `grep -m`, `head`); 983 of those, in 34 files,
# survive here. They are green today only because the outputs they read are small — a property of
# today's message lengths, not of the assertions. `.github#2681` carries that sweep.

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
ENGINE="${FSGG_COORD_ENGINE_BIN:-$REPO_ROOT/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# ---- assertion helpers: no pipeline, so no SIGPIPE, so no scheduling-dependent verdict -------------
# `contains NEEDLE HAYSTACK`  — fixed string, no regex metacharacters interpreted.
# `matches ERE HAYSTACK`      — extended regular expression.
# `matches_i ERE HAYSTACK`    — extended regular expression, case-insensitive.
# The `grep` forms read a here-string: bash materialises it as a file or a pipe it has already filled,
# so there is no live writer to kill and no pipeline status for `pipefail` to promote.
contains()  { case "$2" in *"$1"*) return 0 ;; *) return 1 ;; esac; }
matches()   { grep -qE  -- "$1" <<<"$2"; }
matches_i() { grep -qiE -- "$1" <<<"$2"; }

# ---- 0. the assertion machinery itself, before anything is asserted WITH it ------------------------
# This leg needs no engine and no fixture, so it runs first and fails loudly if the helpers rot.
#
# The haystack is a FIXED literal grown by doubling — deliberately not the engine's usage text, so that
# shortening the engine's help can never silently shrink this leg back under the threshold and stop it
# measuring anything. It comes out at 282,650 bytes — twelve times the 22,956 B that broke the two legs
# below, and well inside the band where `printf … | grep -q` is wrong on 30 runs out of 30.
BIG_UNIT='fsgg-2668-filler-line-kept-long-enough-to-grow-fast-0123456789abcdef'
big_filler="$BIG_UNIT"
while [ "${#big_filler}" -lt 262144 ]; do
  big_filler="$big_filler"$'\n'"$big_filler"
done
BIG_NEEDLE='fsgg-2668-needle-on-line-1'
BIG_ABSENT='fsgg-2668-needle-that-is-nowhere-in-this-haystack'
BIG_HAY="$BIG_NEEDLE"$'\n'"$big_filler"

# POSITIVE, both helpers. A needle on line 1 is the worst case: it is exactly when an early-exiting
# reader stops soonest, and therefore when the writer is most certain to still be writing.
if contains "$BIG_NEEDLE" "$BIG_HAY"; then
  ok "contains finds a line-1 needle in a ${#BIG_HAY}-byte haystack (no pipeline, no SIGPIPE)"
else
  bad "contains finds a line-1 needle in a ${#BIG_HAY}-byte haystack"
fi
if matches "$BIG_NEEDLE" "$BIG_HAY"; then
  ok "matches finds a line-1 needle in a ${#BIG_HAY}-byte haystack (here-string, not a pipe)"
else
  bad "matches finds a line-1 needle in a ${#BIG_HAY}-byte haystack"
fi

# NEGATIVE, both helpers. Without these two, a helper spelled `return 0` passes the positives — the
# repair would be a vacuous green, which is the mirror of the false red it exists to remove.
if contains "$BIG_ABSENT" "$BIG_HAY"; then
  bad "contains still says NO for a needle that is genuinely absent"
else
  ok "contains still says NO for a needle that is genuinely absent"
fi
if matches "$BIG_ABSENT" "$BIG_HAY"; then
  bad "matches still says NO for a needle that is genuinely absent"
else
  ok "matches still says NO for a needle that is genuinely absent"
fi

# THE IDIOM CANNOT COME BACK. Grep this file's own source for a pipeline whose last stage exits early
# and whose status is therefore unsafe to consume. Comment lines are exempt (the header quotes the
# banned spelling on purpose), as is any line tagged IDIOM-GUARD — used only by the guard itself and by
# its own negative probe, which must contain the banned idiom to prove the guard can say NO.
guard_re='\|[[:space:]]*(grep[^|]*-[A-Za-z]*q|head([[:space:]]|$))'   # IDIOM-GUARD
guard_probe='  if printf "%s" "$out" | grep -q NEEDLE; then'          # IDIOM-GUARD
guard_hits=""
while IFS= read -r ln || [ -n "$ln" ]; do
  case "$ln" in *IDIOM-GUARD*) continue ;; esac
  [[ $ln =~ ^[[:space:]]*# ]] && continue
  [[ $ln =~ $guard_re ]] && guard_hits="$guard_hits$ln"$'\n'
done < "${BASH_SOURCE[0]}"

if [ -z "$guard_hits" ]; then
  ok "no assertion in this suite consumes the status of an early-exiting pipeline"
else
  bad "an early-exiting pipeline's status is consumed here (see .github#2668)" "$guard_hits"
fi
# The guard must be able to say NO, or its green above means only that it matched nothing.
if [[ $guard_probe =~ $guard_re ]]; then
  ok "the idiom guard fires on the banned spelling (it is not vacuously green)"
else
  bad "the idiom guard fires on the banned spelling"
fi

if [ ! -x "$ENGINE" ]; then
  echo "FAIL  the engine must be built first: dotnet build src/FS.GG.Coord.Cli -c Release" >&2
  echo "      (looked at: $ENGINE)" >&2
  exit 1
fi

# Start the fixture; it prints its chosen port on its first stdout line.
SRV_OUT="$(mktemp)"
python3 "$HERE/fixture_server.py" >"$SRV_OUT" 2>/dev/null &
SRV_PID=$!
trap 'kill "$SRV_PID" 2>/dev/null; rm -f "$SRV_OUT" "${SNAP:-}" "${SNAP_ERR:-}" "${DEC:-}"; rm -rf "${CACHE_DIR:-}"' EXIT

# Wait for the port line (bounded — a fixture that never binds must fail, not hang).
PORT=""
for _ in $(seq 1 50); do
  PORT="$(head -n1 "$SRV_OUT" 2>/dev/null)"
  [ -n "$PORT" ] && break
  sleep 0.1
done
[ -n "$PORT" ] || { bad "the fixture server bound a port"; echo "coord-engine-e2e: 0 passed, 1 failed"; exit 1; }

export FSGG_GITHUB_API_BASE="http://127.0.0.1:$PORT"
export GITHUB_TOKEN="fixture-token"          # scan REQUIRES a token; the fixture ignores its value
export FSGG_COORD_OWNER="FS-GG"
export FSGG_COORD_PROJECT="Coordination"
CACHE_DIR="$(mktemp -d)"; export FSGG_COORD_CACHE="$CACHE_DIR"  # isolate the scan cache to this run
export FSGG_COORD_SCAN_TTL_SEC=0              # no caching — every leg reads the fixture
# Leg 5 owns this variable; every OTHER leg must be blind to it. Without this a maintainer who has
# `FSGG_CLAIM_LEASE_MIN` exported runs a different test than CI does — and if it is exported MALFORMED,
# assertions 1-6 fail with messages about scan and decide, naming neither the variable nor the shell.
unset FSGG_CLAIM_LEASE_MIN

# ---- 1. scan reaches the fixture and emits a well-formed snapshot ----------------------------------
# The snapshot is scan's STDOUT (the contract); the receipt is its stderr (commentary). Keep them apart —
# mixing the receipt into the document is exactly what would make `decide` choke, and this test must not
# make that mistake on the engine's behalf.
SNAP="$(mktemp)"
SNAP_ERR="$(mktemp)"
"$ENGINE" scan --repo FS.GG.SDD >"$SNAP" 2>"$SNAP_ERR"
scan_rc=$?
[ "$scan_rc" -eq 0 ] && ok "scan exits 0 against the fixture" \
  || bad "scan exits 0 against the fixture" "rc=$scan_rc: $(cat "$SNAP_ERR")"

# The snapshot is the wire contract `decide` consumes: it must carry our one item.
if grep -q '"number":42' "$SNAP" && grep -q '"schema":"fsgg.coord.snapshot/1"' "$SNAP"; then
  ok "scan emits the fsgg.coord.snapshot/1 document, carrying the board item"
else
  bad "scan emits the snapshot carrying the board item" "$(cat "$SNAP")"
fi

# ---- 2. Offline decide refuses to invent a live route decision -------------------------------------
DEC="$(mktemp)"
"$ENGINE" decide --text <"$SNAP" >"$DEC" 2>&1
dec_rc=$?
[ "$dec_rc" -eq 0 ] && ok "decide exits 0 (green) on the fixture snapshot" \
  || bad "decide exits 0 on the fixture snapshot" "rc=$dec_rc: $(cat "$DEC")"

if grep -q 'FS.GG.SDD#42' "$DEC" && grep -qi 'awaiting an explicit current delivery-route decision' "$DEC"; then
  ok "offline decide refuses to infer a delivery route from the snapshot"
else
  bad "offline decide refuses to infer a delivery route" "$(cat "$DEC")"
fi

# ---- 3. The live batch reads the source-bound receipt and schedules the item -----------------------
batch_out="$("$ENGINE" batch --repo FS.GG.SDD --text 2>&1)"
if contains 'FS.GG.SDD#42' "$batch_out" && matches_i 'schedulable in parallel' "$batch_out"; then
  ok "live batch reads the current route receipt and schedules FS.GG.SDD#42"
else
  bad "live batch reads the current route receipt and schedules FS.GG.SDD#42" "$batch_out"
fi

# ---- 4. the offline pipeline remains fail-closed, with no bash decision bypass --------------------
pipe_out="$("$ENGINE" scan --repo FS.GG.SDD 2>/dev/null | "$ENGINE" decide --text 2>&1)"
if matches_i 'awaiting an explicit current delivery-route decision' "$pipe_out"; then
  ok "scan | decide stays fail-closed without a live route read"
else
  bad "scan | decide stays fail-closed without a live route read" "$pipe_out"
fi

# ---- 5. it FAILS CLOSED with no token — an empty board is never invented ---------------------------
notok_out="$(env -u GITHUB_TOKEN -u GH_TOKEN "$ENGINE" scan --repo FS.GG.SDD 2>&1)"
notok_rc=$?
[ "$notok_rc" -ne 0 ] && ok "scan REFUSES without a token (never invents an empty board)" \
  || bad "scan refuses without a token" "rc=0: $notok_out"

# ---- 5. FSGG_CLAIM_LEASE_MIN is HONOURED — the variable seventeen documents promised ---------------
# `.github#1677`: this variable was named by ADR-0027, ADR-0038, `docs/coordination/parallel-work.md`,
# the generated `Protocol.fs` region and the KIT-MIRRORED skill copies it emits into every receiver —
# and read by NO code. It survived because it is UNFALSIFIABLE by inspection: an ignored environment
# variable looks exactly like an honoured one that happened to match the default. So the test may not
# assert the default and stop; it has to observe the lease CHANGE, and change to a value the default
# could not have produced. That is what "an env var with no test is how this happened" asks for.
#
# It belongs HERE and not in a unit suite: `Options.parse` is only half the claim. The other half is
# that the parsed value survives into the artifact the fleet actually reads — the snapshot's
# `leaseMinutes`, which is what every downstream staleness verdict is computed against. This leg runs
# the real binary, through the real parser, over the real transport, and reads the real document.
lease_default="$(env -u FSGG_CLAIM_LEASE_MIN "$ENGINE" scan --repo FS.GG.SDD 2>/dev/null)"
if matches '"leaseMinutes":[[:space:]]*120' "$lease_default"; then
  ok "unset FSGG_CLAIM_LEASE_MIN leaves the documented 120-minute default"
else
  bad "unset FSGG_CLAIM_LEASE_MIN leaves the 120-minute default" "${lease_default:0:200}"
fi

# 45 is the load-bearing number: it is not 120, so a snapshot carrying it CANNOT have come from the
# default, and this assertion fails on the pre-#1677 engine that ignored the variable.
lease_env="$(FSGG_CLAIM_LEASE_MIN=45 "$ENGINE" scan --repo FS.GG.SDD 2>/dev/null)"
if matches '"leaseMinutes":[[:space:]]*45' "$lease_env"; then
  ok "FSGG_CLAIM_LEASE_MIN=45 actually changes the lease the snapshot carries"
else
  bad "FSGG_CLAIM_LEASE_MIN=45 changes the snapshot's lease" "${lease_env:0:200}"
fi

# PRECEDENCE: an explicit flag beats the ambient environment. Both are set, and they disagree on
# purpose — an assertion where they agreed would pass whichever one won.
lease_both="$(FSGG_CLAIM_LEASE_MIN=45 "$ENGINE" scan --repo FS.GG.SDD --lease 30 2>/dev/null)"
if matches '"leaseMinutes":[[:space:]]*30' "$lease_both"; then
  ok "--lease 30 beats FSGG_CLAIM_LEASE_MIN=45 (flag > env > default)"
else
  bad "--lease beats FSGG_CLAIM_LEASE_MIN" "${lease_both:0:200}"
fi

# A MALFORMED value is REFUSED, not silently dropped back to 120. Falling back would rebuild the exact
# unfalsifiability #1677 is about: a typo'd value would produce no error and no change. The variable is
# `--lease` arriving down another channel, so it gets `--lease`'s contract — including its exit code.
bad_lease_out="$(FSGG_CLAIM_LEASE_MIN=abc "$ENGINE" scan --repo FS.GG.SDD 2>&1)"
bad_lease_rc=$?
if [ "$bad_lease_rc" -ne 0 ] && contains 'FSGG_CLAIM_LEASE_MIN' "$bad_lease_out"; then
  ok "a malformed FSGG_CLAIM_LEASE_MIN is REFUSED by name, never silently defaulted"
else
  bad "a malformed FSGG_CLAIM_LEASE_MIN is refused by name" "rc=$bad_lease_rc: ${bad_lease_out:0:200}"
fi

# A NON-POSITIVE value is refused too, and this leg exists to kill a specific mutant: relaxing the
# guard from `n > 0` to `n >= 0` keeps every assertion above green. A lease of 0 minutes would make
# every claim instantly reapable — `Snapshot.fs` refuses exactly that on the wire, and the env channel
# must not be the way round it.
zero_lease_out="$(FSGG_CLAIM_LEASE_MIN=0 "$ENGINE" scan --repo FS.GG.SDD 2>&1)"
zero_lease_rc=$?
if [ "$zero_lease_rc" -ne 0 ] && contains 'FSGG_CLAIM_LEASE_MIN' "$zero_lease_out"; then
  ok "FSGG_CLAIM_LEASE_MIN=0 is REFUSED — the env channel cannot mint an instantly-reapable lease"
else
  bad "FSGG_CLAIM_LEASE_MIN=0 is refused" "rc=$zero_lease_rc: ${zero_lease_out:0:200}"
fi

# WHITESPACE-ONLY is "not configured", not "malformed" — the value an unset shell variable expands to
# inside a quoted export. It must take the default, not hard-fail every command.
blank_lease="$(FSGG_CLAIM_LEASE_MIN='   ' "$ENGINE" scan --repo FS.GG.SDD 2>/dev/null)"
if matches '"leaseMinutes":[[:space:]]*120' "$blank_lease"; then
  ok "a whitespace-only FSGG_CLAIM_LEASE_MIN reads as UNSET, not as malformed"
else
  bad "a whitespace-only FSGG_CLAIM_LEASE_MIN reads as unset" "${blank_lease:0:200}"
fi

# The DIAGNOSTIC verbs answer even when the variable is garbage, because a misconfigured shell is
# exactly when an operator reaches for them — and `scripts/fsgg-coord` probes `--version` to decide
# whether the tool is restored, so gating it would turn one typo into a restore on every invocation.
help_rc=0;  FSGG_CLAIM_LEASE_MIN=abc "$ENGINE" --help    >/dev/null 2>&1 || help_rc=$?
vers_rc=0;  FSGG_CLAIM_LEASE_MIN=abc "$ENGINE" --version >/dev/null 2>&1 || vers_rc=$?
if [ "$help_rc" -eq 0 ] && [ "$vers_rc" -eq 0 ]; then
  ok "--help and --version still answer under a malformed FSGG_CLAIM_LEASE_MIN"
else
  bad "--help/--version answer under a malformed FSGG_CLAIM_LEASE_MIN" "help_rc=$help_rc vers_rc=$vers_rc"
fi

# ---- report ----------------------------------------------------------------------------------------
echo
echo "coord-engine-e2e: $((pass + failcount)) assertion(s), $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::coord-engine-e2e FAILED"; exit 1; }
echo "green — the engine reads its own board, over HTTP, hermetically."
