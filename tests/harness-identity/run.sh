#!/usr/bin/env bash
# Fixture for scripts/check-harness-identity.py — the harness-identity-ladder gate (.github#1817).
#
# THE FAILURE LEGS ARE THE POINT (#266, `scripts/lint-shell.sh`'s exit-3 precedent, echoed in
# tests/worker-id-attractor/run.sh's own header). A gate that has only ever been observed green over
# the tree it was written against has not been proven to be able to say NO — three shell harnesses and
# two F# fixtures carried exactly this defect, silently, until something happened to consult the
# identity they left undecided. So this fixture:
#
#   1. builds tiny SYNTHETIC surfaces for each of the two legitimate shapes (AC 2 — blanket unset, and
#      the per-invocation twin) and asserts both are green;
#   2. builds synthetic DEFECTIVE surfaces — a `--worker` invocation naming no engine marker at all
#      (must not fire: it is prose, not code), one that names the engine and decides nothing, and an F#
#      fixture with the `runQueue` shape — and asserts each finding names the right file:line and gives
#      the right reason;
#   3. replays the REAL shipped tree, verbatim, and requires it green — the gate's own real subject,
#      not a synthetic stand-in (AC 4: the three shell harnesses named in #1817's table, plus the F#
#      fixtures this item's own PR fixed, must pass it as they now stand);
#   4. MUTATES a copy of that real tree to reintroduce the two historical regressions — the unset line
#      `writes.sh` needed, and the identity scrub `runQueue` needed — and requires the gate catch both,
#      by file and by name (AC 3);
#   5. asserts the fail-closed no-verdict: no `tests/` directory, and a `tests/` with not one shell or
#      F# file in it, are exit 3, never a silent green.
set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-harness-identity.py"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/harness-identity-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

export PYTHONDONTWRITEBYTECODE=1

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

expect() {  # expect <name> <want-rc> <needle> <root> [args…]
  local name="$1" want="$2" needle="$3" root="$4"; shift 4
  local out rc=0
  out="$(python3 "$GATE" --root "$root" "$@" 2>&1)" || rc=$?
  if [ "$rc" -ne "$want" ]; then
    bad "$name (exit $rc, want $want)" "$out"
  elif [ -n "$needle" ] && ! grep -qF "$needle" <<<"$out"; then
    bad "$name (exit $want, but not for the stated reason: want '$needle')" "$out"
  else
    ok "$name"
  fi
}

# =============================================================================================
# 1. SYNTHETIC — the two legitimate shapes are both GREEN.
# =============================================================================================

mk_synth() { mkdir -p "$1/tests/synth"; }

# 1a. Blanket unset, once, before every `--worker` call.
UNSET_OK="$WORK/unset-ok"; mk_synth "$UNSET_OK"
cat > "$UNSET_OK/tests/synth/run.sh" <<'EOF'
#!/usr/bin/env bash
ENGINE="/bin/true"
unset CLAUDE_CODE_SESSION_ID OPENCODE_SESSION_ID FSGG_AGENT_SESSION_ID FSGG_AGENT_HARNESS
export FSGG_WORKER=""
"$ENGINE" claim FS.GG.SDD#1 --worker vole-418
"$ENGINE" release FS.GG.SDD#1 --worker vole-418
EOF
expect "shape (a) blanket unset before every --worker call is green" 0 "OK" "$UNSET_OK"

# 1b. Per-invocation twin — CLAUDE_CODE_SESSION_ID and FSGG_WORKER decided together, no blanket unset
#     anywhere in the file. AC 2 is explicit that a blanket "must unset" rule would be WRONG here.
TWIN_OK="$WORK/twin-ok"; mk_synth "$TWIN_OK"
cat > "$TWIN_OK/tests/synth/run.sh" <<'EOF'
#!/usr/bin/env bash
ENGINE="/bin/true"
env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID CLAUDE_CODE_SESSION_ID="$1" FSGG_WORKER="$2" \
  "$ENGINE" claim FS.GG.SDD#1 --worker "$2"
EOF
expect "shape (b) the per-invocation twin, with NO blanket unset anywhere, is green" 0 "OK" "$TWIN_OK"

# =============================================================================================
# 2. SYNTHETIC — the defective shapes are RED, or rightly not a subject at all.
# =============================================================================================

# 2a. `--worker` in PROSE — a comment, and a mention with no engine marker on the line — must not fire.
PROSE_OK="$WORK/prose-ok"; mk_synth "$PROSE_OK"
cat > "$PROSE_OK/tests/synth/run.sh" <<'EOF'
#!/usr/bin/env bash
# every leg below names a worker with `--worker <id>`, and the lock verbs refuse a disagreement.
echo 'Resolution order: --worker <id> -> $FSGG_WORKER -> worktree.'
EOF
expect "a --worker MENTION with no engine marker (prose, or generated doc content) is not a subject" \
  0 "OK" "$PROSE_OK"

# 2b. The live defect, shell shape: `--worker` naming the engine, deciding nothing.
BAD_SHELL="$WORK/bad-shell"; mk_synth "$BAD_SHELL"
cat > "$BAD_SHELL/tests/synth/run.sh" <<'EOF'
#!/usr/bin/env bash
ENGINE="/bin/true"
"$ENGINE" claim FS.GG.SDD#1 --worker vole-418
EOF
expect "REGRESSION shape: --worker naming \$ENGINE with NO unset and NO twin is caught" \
  1 "tests/synth/run.sh:3" "$BAD_SHELL"
expect "...and says WHY: never decided the identity ladder" \
  1 "never decided the identity" "$BAD_SHELL"

# 2c. The live defect, F# shape: `runQueue` BEFORE #1817's fix — an (args: string list) binding that
#     sets FSGG_COORD_CACHE, passes --worker, and scrubs nothing.
mkdir -p "$WORK/fs-bad/tests/synth"
cat > "$WORK/fs-bad/tests/synth/Fixture.fs" <<'EOF'
module Fixture =
    let private runQueue (transport: X) (args: string list) : int * string * string =
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
        let opts = options args
        0, "", ""

    let leg () = runQueue transport [ "take"; "--worker"; "otter-9c21" ]
EOF
expect "REGRESSION shape (F#): FSGG_COORD_CACHE isolated, ladder never mentioned, is caught" \
  1 "tests/synth/Fixture.fs" "$WORK/fs-bad"
expect "...and names the missing variables" \
  1 "FSGG_WORKER, CLAUDE_CODE_SESSION_ID, OPENCODE_SESSION_ID, FSGG_AGENT_SESSION_ID" "$WORK/fs-bad"

# 2d. The scrubbed F# shape — same fixture, `runIn`'s idiom — is green.
mkdir -p "$WORK/fs-ok/tests/synth"
cat > "$WORK/fs-ok/tests/synth/Fixture.fs" <<'EOF'
module Fixture =
    let private runIn (dir: string) (transport: X) (args: string list) : int * string =
        let identityVars =
            [ "FSGG_WORKER"; "CLAUDE_CODE_SESSION_ID"; "OPENCODE_SESSION_ID"; "FSGG_AGENT_SESSION_ID" ]
        for v in identityVars do
            Environment.SetEnvironmentVariable(v, null)
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
        let opts = options args
        0, ""

    let leg () = runIn "/tmp/x" transport [ "take"; "--worker"; "otter-9c21" ]
EOF
expect "the scrubbed F# shape (runIn's idiom) is green" 0 "OK" "$WORK/fs-ok"

# 2e. An F# fixture that isolates FSGG_COORD_CACHE through an (args: string list) binding but is NEVER
#     handed a `--worker` argv anywhere in the file — `LandableNotOpenTests.fs`'s `runLandable` shape —
#     must not be a subject at all, even though it "already knows it isolates process-global state".
mkdir -p "$WORK/fs-noworker/tests/synth"
cat > "$WORK/fs-noworker/tests/synth/Fixture.fs" <<'EOF'
module Fixture =
    let private runLandable (transport: X) (args: string list) : int * string =
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
        let opts = options args
        0, ""

    let leg () = runLandable transport [ "landable"; "801"; "--repo"; "FS.GG.SDD" ]
EOF
expect "an (args: string list)+FSGG_COORD_CACHE fixture never handed --worker is not a subject" \
  0 "OK" "$WORK/fs-noworker"

# =============================================================================================
# 3. THE REAL SHIPPED TREE, replayed verbatim, must be GREEN (AC 4).
# =============================================================================================
REAL="$WORK/real"; mkdir -p "$REAL/tests"
cp -r "$REPO_ROOT/tests/coord-engine-e2e" "$REAL/tests/"
cp -r "$REPO_ROOT/tests/coord-engine-parity" "$REAL/tests/"
mkdir -p "$REAL/tests/FS.GG.Coord.Cli.BoardOps.Tests"
cp "$REPO_ROOT/tests/FS.GG.Coord.Cli.BoardOps.Tests/ApplicationServiceTests.fs" "$REAL/tests/FS.GG.Coord.Cli.BoardOps.Tests/"

expect "the SHIPPED tree (writes.sh, run.sh, shim.sh, ApplicationServiceTests.fs) is green as it stands" \
  0 "OK" "$REAL"

# =============================================================================================
# 4. THE TWO HISTORICAL REGRESSIONS, replayed by MUTATING a copy of the real, shipped files.
# =============================================================================================

# 4a. writes.sh WITHOUT its ladder unset — the #1646 shape, the first of the three-harness table.
REAL_A="$WORK/real-writes-unfixed"; mkdir -p "$REAL_A/tests"
cp -r "$REPO_ROOT/tests/coord-engine-e2e" "$REAL_A/tests/"
sed -i '/^unset CLAUDE_CODE_SESSION_ID OPENCODE_SESSION_ID FSGG_AGENT_SESSION_ID FSGG_AGENT_HARNESS$/d' \
  "$REAL_A/tests/coord-engine-e2e/writes.sh"
expect "REGRESSION REPLAY: writes.sh with its unset line removed is caught" \
  1 "tests/coord-engine-e2e/writes.sh" "$REAL_A"

# 4b. `runQueue`, reverted to its pre-#1817 shape (FSGG_COORD_CACHE scrubbed, identity ladder not) —
#     the live instance this item's own PR fixed.
REAL_B="$WORK/real-runqueue-unfixed"; mkdir -p "$REAL_B/tests/FS.GG.Coord.Cli.BoardOps.Tests"
python3 - "$REPO_ROOT/tests/FS.GG.Coord.Cli.BoardOps.Tests/ApplicationServiceTests.fs" \
          "$REAL_B/tests/FS.GG.Coord.Cli.BoardOps.Tests/ApplicationServiceTests.fs" <<'PYEOF'
import re, sys
src, dst = sys.argv[1], sys.argv[2]
text = open(src, encoding="utf-8").read()
# Cut the block from runQueue's own `let private runQueue` down to its closing `Directory.Delete`,
# and rewrite it back to the pre-#1817 shape: FSGG_COORD_CACHE/FSGG_KIT_ROOT scrubbed, identity not.
start = text.index("    let private runQueue (transport")
end = text.index("    let private busyQueue ()")
unfixed = '''    let private runQueue (transport: Fake.Recorder) (args: string list) : int * string * string =
        let dir = Path.Combine(Path.GetTempPath(), "fsgg-1525-" + Guid.NewGuid().ToString "n")
        let previousCache = Environment.GetEnvironmentVariable "FSGG_COORD_CACHE"
        let previousKitRoot = Environment.GetEnvironmentVariable "FSGG_KIT_ROOT"
        let stdout = Console.Out
        let stderr = Console.Error
        use capturedOut = new StringWriter()
        use capturedErr = new StringWriter()

        try
            Directory.CreateDirectory dir |> ignore
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", dir)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", dir)
            Console.SetOut capturedOut
            Console.SetError capturedErr

            let opts = options args

            let code =
                match opts.Command with
                | Options.Take -> Client.take (context transport) opts
                | Options.Next -> Client.next (context transport) opts
                | Options.BatchCmd -> Client.batch (context transport) opts
                | other -> failwithf "this fixture drives take/next/batch only, got %A" other

            Console.Out.Flush()
            Console.Error.Flush()
            code, capturedOut.ToString(), capturedErr.ToString()
        finally
            Console.SetOut stdout
            Console.SetError stderr
            Environment.SetEnvironmentVariable("FSGG_COORD_CACHE", previousCache)
            Environment.SetEnvironmentVariable("FSGG_KIT_ROOT", previousKitRoot)

            try
                Directory.Delete(dir, true)
            with _ ->
                ()

'''
open(dst, "w", encoding="utf-8").write(text[:start] + unfixed + text[end:])
PYEOF
expect "REGRESSION REPLAY: runQueue reverted to its pre-#1817 shape is caught" \
  1 "runQueue\` sets FSGG_COORD_CACHE" "$REAL_B"

# =============================================================================================
# 5. FAIL-CLOSED — no `tests/`, or a `tests/` with not one shell/F# file, is NO VERDICT (exit 3).
# =============================================================================================
NOTESTS="$WORK/no-tests"; mkdir -p "$NOTESTS"
expect "no tests/ directory at all is a permanent no-verdict, not a silent green" \
  3 "is not a directory" "$NOTESTS"

EMPTYKIND="$WORK/empty-kind"; mkdir -p "$EMPTYKIND/tests/synth"
echo "not shell, not F#" > "$EMPTYKIND/tests/synth/notes.md"
expect "a tests/ with no shell or F# file at all is a permanent no-verdict" \
  3 "NOT ONE shell or F# file" "$EMPTYKIND"

echo
echo "harness-identity fixture: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
