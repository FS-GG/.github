#!/usr/bin/env bash
# coord-guards — `stale_guard`'s UPSTREAM-DRIFT half, at TIER 2b, which nothing else measures
# (.github#2581).
#
# WHY A SECOND GUARD SUITE EXISTS AT ALL. `tests/coord-engine-parity/shim.sh` §3/§3c already drive the
# staleness guard hard, and they are not duplicated here. What they cannot reach is the SHAPE that cost
# two workers their leases: their fixture (`shim.sh:204-216`) is a single `git init` directory with NO
# remote, so `upstream_drift` takes its "no `origin`, nothing to be behind" arm and returns silently, and
# every staleness leg in that file is decided by the MTIME half alone. `.github#2549` and `.github#2563`
# were both the other half — a shared checkout BEHIND `origin/main` under the engine's own source trees,
# with a worker standing in a linked worktree, i.e. tier 2b. That combination had no fixture anywhere.
#
# So this file builds it: a main checkout with a real `origin` whose default branch carries one commit
# under `src/FS.GG.Coord.Core` that the checkout lacks, an engine binary NEWER than its own sources (so
# the mtime half is silent and the drift half is the only thing being measured), and a linked worktree
# that the caller stands in.
#
# FOUR SUCH CHECKOUTS SINCE .github#2725, differing only in WHICH tree the one upstream commit touches:
# `Core` (§1-§9), `Cli.Kernel` alone (§11), nothing the list names (§11's control), and no upstream commit
# at all (§8). The tree list is the subject of §11-§12, and one fixture cannot be the control for itself.
#
# HERMETIC. A `git init` fixture, a shell script standing in for the engine, no dotnet, no network, no
# token, and no board. Seconds.
#
# WHAT IT ASSERTS, AND THE ONE THING IT DOES NOT. It asserts that the refusal is unchanged in force
# (`heartbeat` is still refused, and so is every other write verb the module declares) and that the
# refusal now names the regime and a recovery route the blocked worker can take alone — and it EXECUTES
# that route rather than trusting the text, because naming a route nothing ever runs is the same defect
# .github#2581 is repairing one level down. It does NOT assert that a real `heartbeat` renews a real
# lease against a real board: that is `tests/coord-engine-e2e/writes.sh`'s subject and it needs the
# compiled engine. The claim measured here is exactly "the guard is no longer standing between the
# worker and the renewal", which is the claim .github#2581 makes.
#
# THE SHIM AND THE MODULE ARE COPIED INTO A SCRATCH `scripts/` DIRECTORY, and that is what makes the
# gate invertible. `scripts/fsgg-coord` resolves its guard module from its OWN directory
# (`${BASH_SOURCE[0]}`), so driving a copy is the only way to put a DIFFERENT module under the same
# shim — which is what `FSGG_GUARDS_UNDER_TEST` is for, and how every assertion below was demonstrated
# red against the pre-repair module. The copies are asserted byte-identical to their sources, so the
# indirection cannot quietly change the subject.
set -uo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
SHIM_SRC="$REPO_ROOT/scripts/fsgg-coord"
GUARDS_SRC="${FSGG_GUARDS_UNDER_TEST:-$REPO_ROOT/scripts/fsgg-coord-guards.sh}"

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

[ -x "$SHIM_SRC" ]  || { echo "FAIL  the shim is missing or not executable: $SHIM_SRC" >&2; exit 1; }
[ -f "$GUARDS_SRC" ] || { echo "FAIL  the guard module is missing: $GUARDS_SRC" >&2; exit 1; }

# A REF NO ENGINE CAN RESOLVE, for `tests/coord-engine-parity/shim.sh`'s reason (#1008): if the fixture
# isolation ever breaks, a write fails on the ref rather than landing on somebody's real work.
FIXREF="fixture/repo#999999"

ROOT="$(mktemp -d)"
cleanup() { rm -rf "$ROOT"; }
trap cleanup EXIT

# ---- the shim under test -------------------------------------------------------------------------
mkdir -p "$ROOT/scripts"
cp "$SHIM_SRC"   "$ROOT/scripts/fsgg-coord"
cp "$GUARDS_SRC" "$ROOT/scripts/fsgg-coord-guards.sh"
chmod +x "$ROOT/scripts/fsgg-coord"
SHIM="$ROOT/scripts/fsgg-coord"

if cmp -s "$SHIM_SRC" "$SHIM"; then
  ok "harness: the shim under test is byte-identical to scripts/fsgg-coord — the copy changes the module's location, never the resolver"
else
  bad "harness: the copied shim differs from scripts/fsgg-coord"
fi

# ---- the tier-2b fixture -------------------------------------------------------------------------
# $1 = root, $2 = the drift to plant upstream:
#   behind   — one commit under `src/FS.GG.Coord.Core` (the .github#2581 shape)
#   kernel   — one commit under `src/FS.GG.Coord.Cli.Kernel` ALONE (.github#2725; see §11)
#   outside  — one commit under a tree `ENGINE_SOURCE_TREES` deliberately does not name (§11's control)
#   current  — no upstream commit at all (the happy path)
#
# THE ORDER IS LOAD-BEARING. All git work happens first, then the sources are back-dated and the engine
# is stamped AFTER them — because `git reset --hard` rewrites working-tree files with fresh mtimes, and
# a fixture built the other way round would fire the MTIME half too and stop isolating the drift half.
fixture() {
  # SEPARATE STATEMENTS, NOT ONE `local` LIST: within a single `local` the earlier names are not yet in
  # scope, so `shared="$root/shared"` on the same line reads an unbound variable.
  local root="$1" mode="$2"
  local shared="$root/shared"
  local remote="$root/remote"
  local bindir
  # THE FIXTURE MIRRORS THE REAL PROJECT SET, INCLUDING `FS.GG.Coord.Cli.Kernel` — see §11. A fixture
  # missing a tree the list names cannot red when that tree stops being named.
  mkdir -p "$shared/src/FS.GG.Coord.Cli" "$shared/src/FS.GG.Coord.Cli.Kernel" \
           "$shared/src/FS.GG.Coord.Core" "$shared/src/FS.GG.Coord.GitHub" "$shared/docs"
  printf 'bin/\nobj/\n'  >"$shared/.gitignore"
  printf '// cli\n'      >"$shared/src/FS.GG.Coord.Cli/Program.fs"
  printf '// kernel v1\n' >"$shared/src/FS.GG.Coord.Cli.Kernel/Options.fs"
  printf '// core v1\n'  >"$shared/src/FS.GG.Coord.Core/Protocol.fs"
  printf '// github\n'   >"$shared/src/FS.GG.Coord.GitHub/Writes.fs"
  printf 'notes v1\n'    >"$shared/docs/notes.md"

  git -C "$shared" init -q
  # `symbolic-ref` rather than `init -b main`: the latter is git 2.28+, and this fixture must not make a
  # version assumption in the one place a silent failure would read as "no drift".
  git -C "$shared" symbolic-ref HEAD refs/heads/main
  git -C "$shared" config user.email fixture@example.invalid
  git -C "$shared" config user.name  'coord-guards fixture'
  git -C "$shared" add -A
  git -C "$shared" commit -q -m 'engine v1'

  git init -q --bare "$remote"
  git -C "$shared" remote add origin "$remote"
  git -C "$shared" push -q origin main

  # EACH DRIFT MODE PLANTS EXACTLY ONE UPSTREAM COMMIT AND TOUCHES EXACTLY ONE TREE, so a leg that reds
  # names the tree that caused it. `current` plants none.
  case "$mode" in
    behind)
      # ONE COMMIT UNDER THE ENGINE'S OWN SOURCE TREES — not under docs or workflows, which
      # `ENGINE_SOURCE_TREES` deliberately does not count.
      printf '// core v2\n' >"$shared/src/FS.GG.Coord.Core/Protocol.fs" ;;
    kernel)
      printf '// kernel v2\n' >"$shared/src/FS.GG.Coord.Cli.Kernel/Options.fs" ;;
    outside)
      printf 'notes v2\n' >"$shared/docs/notes.md" ;;
    current) : ;;
    *) echo "FAIL  fixture: unknown mode '$mode'" >&2; exit 1 ;;
  esac
  if [ "$mode" != current ]; then
    git -C "$shared" commit -q -a -m "upstream $mode"
    git -C "$shared" push -q origin main
    git -C "$shared" reset -q --hard HEAD~1
  fi
  git -C "$shared" fetch -q origin

  # THE CALLER STANDS IN A LINKED WORKTREE — pnext-item §2's mandated shape, and the reason tier 2b
  # exists (#931). It has no `bin/`, so tier 2a misses and the SHARED build is what resolves.
  git -C "$shared" worktree add -q --detach "$root/wt" HEAD

  bindir="$shared/src/FS.GG.Coord.Cli/bin/Release/net10.0"
  mkdir -p "$bindir"
  printf '#!/usr/bin/env bash\necho "ENGINE RAN: $*"\n' >"$bindir/fsgg-coord-engine"
  chmod +x "$bindir/fsgg-coord-engine"
  : >"$bindir/fsgg-coord-engine.dll"
  find "$shared/src" -name '*.fs' -exec touch -d '3 hours ago' {} +
  touch -d '2 hours ago' "$bindir/fsgg-coord-engine" "$bindir/fsgg-coord-engine.dll"
}

BEHIND="$ROOT/behind"; mkdir -p "$BEHIND"; fixture "$BEHIND" behind
CURRENT="$ROOT/current"; mkdir -p "$CURRENT"; fixture "$CURRENT" current
KERNEL="$ROOT/kernel"; mkdir -p "$KERNEL"; fixture "$KERNEL" kernel
OUTSIDE="$ROOT/outside"; mkdir -p "$OUTSIDE"; fixture "$OUTSIDE" outside

# A CURRENT ENGINE THE CALLER OWNS — what the recovery route tells a blocked worker to build. It is a
# separate file in a separate directory from the fixture's shared build, so a leg that reached the
# fixture engine by accident could not be mistaken for this one.
mkdir -p "$ROOT/mine"
MYENGINE="$ROOT/mine/fsgg-coord-engine"
printf '#!/usr/bin/env bash\necho "MY CURRENT ENGINE RAN: $*"\n' >"$MYENGINE"
chmod +x "$MYENGINE"

# $1 = fixture root, rest = argv. Leaves DRIVE_RC, DRIVE_OUT and DRIVE_ERR set.
#
# STDOUT IS CAPTURED SEPARATELY AND ASSERTED ON, NOT DISCARDED (#1008). "The guard refused" and "the
# guard never ran" are both silent on stderr; only the fixture engine's own `ENGINE RAN` line, which it
# prints to STDOUT, tells them apart — and a leg that never checked whose silence it heard is how #1008
# stayed green for the whole life of its bug.
DRIVE_RC=0; DRIVE_OUT=""; DRIVE_ERR=""
drive() {
  local root="$1"; shift
  local outfile="$ROOT/.stdout" errfile="$ROOT/.stderr"
  ( cd "$root/wt" && env -u FSGG_COORD_ENGINE_BIN "$SHIM" "$@" ) >"$outfile" 2>"$errfile"
  DRIVE_RC=$?
  DRIVE_OUT="$(cat "$outfile")"
  DRIVE_ERR="$(cat "$errfile")"
}

# ---- 1. THE FAILING CONDITION, REPRODUCED (.github#2581 acceptance 4, .github#2551) ---------------
# This leg is green BEFORE and AFTER the repair, and that is the point: it pins the behaviour the item
# must NOT change. A worker in a worktree, a shared checkout behind on the engine's source trees, and
# `heartbeat` — the verb that renews the claim — refused at exit 69 with the engine never reached.
drive "$BEHIND" heartbeat "$FIXREF"
HB_RC="$DRIVE_RC"; HB_ERR="$DRIVE_ERR"; HB_OUT="$DRIVE_OUT"
if [ "$HB_RC" -eq 69 ] && ! printf '%s' "$HB_OUT" | grep -q 'ENGINE RAN' \
   && printf '%s' "$HB_ERR" | grep -q "REFUSED"; then
  ok "the .github#2581 condition reproduces: worktree caller + shared checkout BEHIND on the engine source trees ⇒ 'heartbeat' REFUSED (exit 69), engine never reached"
else
  bad "the .github#2581 condition must reproduce as a refusal at exit 69" "rc=$HB_RC out=$HB_OUT err=$HB_ERR"
fi

# It must be the DRIFT half that fired, not the mtime half — otherwise this fixture is measuring the
# thing `tests/coord-engine-parity/shim.sh` already measures and the tier-2b shape is still untested.
if printf '%s' "$HB_ERR" | grep -q 'BEHIND refs/remotes/origin/main by 1 commit' \
   && ! printf '%s' "$HB_ERR" | grep -q 'You are about to run code that is NOT in the checkout that built it'; then
  ok "the verdict is the UPSTREAM-DRIFT half alone — the mtime half is silent, so this fixture measures the arm nothing else does"
else
  bad "the fixture must isolate the upstream-drift half" "err=$HB_ERR"
fi

# ---- 2. THE REFUSAL SAYS WHOSE REMEDY IT IS (.github#2581 acceptance 3) ---------------------------
# The two regimes demand opposite actions and the old text printed only one of them: "update and rebuild
# $top", where at tier 2b $top is the SHARED checkout — the one checkout a worker under host-serialised
# repair is instructed to hold.
for phrase in 'THIS *IS* THE SHARED CHECKOUT' 'SERIALISED BY THE HOST' 'NOT YOURS TO RUN'; do
  if printf '%s' "$HB_ERR" | grep -qF "$phrase"; then
    ok "the refusal names the host-owned regime: '$phrase'"
  else
    bad "the refusal must distinguish 'yours to fix' from 'the host's — hold and report'" "missing: $phrase"
  fi
done

# ---- 3. IT NAMES A ROUTE THE BLOCKED WORKER CAN TAKE ALONE ---------------------------------------
for phrase in 'KEEP YOUR CLAIM WITHOUT TOUCHING THAT CHECKOUT' \
              'git worktree add --detach' \
              'FSGG_COORD_ENGINE_BIN' \
              'BUILD the engine you name'; do
  if printf '%s' "$HB_ERR" | grep -qF "$phrase"; then
    ok "the refusal prints the self-service recovery route: '$phrase'"
  else
    bad "the refusal must name a recovery route that needs neither the shared checkout nor the host" "missing: $phrase"
  fi
done

# THE REF IN THE ROUTE IS THE ONE THIS GUARD MEASURED, never a hard-coded `origin/main`. A remedy
# against a different ref than the count was taken over is the mistake the `merge --ff-only $b` note in
# the module already refuses, and it would name a ref that does not resolve on a `master` checkout.
if printf '%s' "$HB_ERR" | grep -qF 'git worktree add --detach "$eng" refs/remotes/origin/main'; then
  ok "the recovery route names the ref the guard MEASURED (refs/remotes/origin/main), not a hard-coded literal"
else
  bad "the recovery route must be built against the ref upstream_drift resolved" "err=$HB_ERR"
fi

# ---- 4. THE LEASE CONSEQUENCE IS NAMED, AND ONLY FOR THE RENEWAL VERB ----------------------------
if printf '%s' "$HB_ERR" | grep -q 'OUTLIVE THE LEASE IT IS STANDING ON' \
   && printf '%s' "$HB_ERR" | grep -q '.github#2549'; then
  ok "the 'heartbeat' refusal names the lease consequence the generic write refusal hides, citing the two measured incidents"
else
  bad "the lease-renewal refusal must say that it can outlive the lease and that an expired lease cannot be renewed in place" "err=$HB_ERR"
fi

drive "$BEHIND" 'done' "$FIXREF"
if ! printf '%s' "$DRIVE_ERR" | grep -q 'OUTLIVE THE LEASE IT IS STANDING ON'; then
  ok "the lease line is scoped to the renewal verb — 'done' is refused without it, so the message stays about the reader's situation"
else
  bad "the lease-specific line must not be printed for verbs that are not the lease renewal" "err=$DRIVE_ERR"
fi

# ---- 5. AN OPAQUE EXPLICIT BINARY IS NOT AUTOMATICALLY TRUSTED ------------------------------------
# `MYENGINE` is deliberately outside any git checkout. It used to bypass every guard merely because the
# caller named it. The self-host contract makes that absence of provenance a refusal for write verbs.
RECOVER_ERR="$ROOT/.recover.err"
RECOVER_OUT="$( cd "$BEHIND/wt" && FSGG_COORD_ENGINE_BIN="$MYENGINE" "$SHIM" heartbeat "$FIXREF" 2>"$RECOVER_ERR" )"
RECOVER_RC=$?
RECOVER_ERR_TXT="$(cat "$RECOVER_ERR")"
if [ "$RECOVER_RC" -eq 69 ] \
   && printf '%s' "$RECOVER_ERR_TXT" | grep -q 'FSGG_SELF_HOST_RECEIPT' \
   && ! printf '%s' "$RECOVER_OUT" | grep -q 'MY CURRENT ENGINE RAN'; then
  ok "an opaque explicit engine cannot write without typed self-host authority"
else
  bad "an opaque explicit engine must fail closed before a write" "rc=$RECOVER_RC out=$RECOVER_OUT err=$RECOVER_ERR_TXT"
fi

# The inverse is equally load-bearing: once a DISTINCT stable verifier accepts the receipt and exact
# candidate path, the candidate receives the original write argv. The verifier is a fixture here; the
# real verifier's digest/bytes/version/head behavior is exercised by SelfHostCliTests.
SELF_HOST_RECEIPT="$ROOT/self-host.receipt"
SELF_HOST_STABLE="$ROOT/stable-engine"
SELF_HOST_LOG="$ROOT/stable.log"
printf '%s\n' 'typed receipt fixture' >"$SELF_HOST_RECEIPT"
cat >"$SELF_HOST_STABLE" <<'EOF'
#!/bin/sh
printf '%s\n' "$*" >"$FSGG_SELF_HOST_TEST_LOG"
[ "$1" = self-host ] && [ "$2" = verify ] && [ "$3" = "$FSGG_SELF_HOST_RECEIPT" ] && [ "$4" = "$FSGG_COORD_ENGINE_BIN" ]
EOF
chmod +x "$SELF_HOST_STABLE"
AUTHORIZED_OUT="$(cd "$BEHIND/wt" && \
  FSGG_SELF_HOST_TEST_LOG="$SELF_HOST_LOG" \
  FSGG_SELF_HOST_RECEIPT="$SELF_HOST_RECEIPT" \
  FSGG_COORD_STABLE_ENGINE_BIN="$SELF_HOST_STABLE" \
  FSGG_COORD_ENGINE_BIN="$MYENGINE" \
  "$SHIM" heartbeat "$FIXREF" 2>/dev/null)"
AUTHORIZED_RC=$?
if [ "$AUTHORIZED_RC" -eq 0 ] \
   && printf '%s' "$AUTHORIZED_OUT" | grep -q 'MY CURRENT ENGINE RAN: heartbeat' \
   && grep -q '^self-host verify ' "$SELF_HOST_LOG"; then
  ok "a distinct stable verifier authorizes the exact candidate before its write argv runs"
else
  bad "accepted typed self-host authority must admit the candidate write" "rc=$AUTHORIZED_RC out=$AUTHORIZED_OUT log=$(cat "$SELF_HOST_LOG" 2>/dev/null)"
fi

# AND THE SHARED CHECKOUT REALLY IS UNTOUCHED — the property that makes the route safe under
# host-serialised repair. Asserted rather than assumed: a route that quietly fast-forwarded the shared
# checkout would pass every leg above and be the exact thing the worker was told not to do.
if [ "$(git -C "$BEHIND/shared" rev-list --count HEAD..refs/remotes/origin/main -- \
          src/FS.GG.Coord.Cli src/FS.GG.Coord.Core src/FS.GG.Coord.GitHub)" = "1" ]; then
  ok "the shared checkout is still BEHIND after the recovery — the route repaired nothing the host owns"
else
  bad "the recovery route must not move the shared checkout"
fi

# ---- 5b. THE PRINTED COMMAND WORKS FOR *N* BLOCKED WORKERS AT ONCE (review round 0, repair 1) ------
# §5 above proves the ROUTE works; it says nothing about the COMMAND, because it exercises tier 1
# directly, once, from one worktree. That is exactly the gap the first draft of this change fell into: it
# composed a fixed `/tmp/fsgg-engine-current`, which is green here and `fatal: … already exists`, rc 128,
# for the SECOND of N simultaneously-blocked workers — in the very multi-lane, host-serialised regime the
# refusal's own text names two paragraphs earlier. More than one blocked worker is the stated premise
# here, not an edge case, so "the command works once" is not the claim this refusal makes.
#
# THE COMMAND IS EXTRACTED FROM THE REFUSAL, NEVER RETYPED. A leg that retypes what it believes the guard
# prints certifies the test author's memory, not the module.

# (a) STRUCTURAL — the `worktree add` target is not a fixed absolute literal.
ADD_LINE="$(printf '%s\n' "$HB_ERR" | grep -F 'git worktree add --detach' | head -1 | sed 's/^[[:space:]]*//')"
ADD_TARGET="$(printf '%s' "$ADD_LINE" | awk '{print $5}')"
case "$ADD_TARGET" in
  '' | /*)
    bad "the recovery route must not name a fixed absolute directory: the second blocked worker to run it verbatim gets 'already exists', and a leftover one from an earlier session is adoptable as 'current'" \
        "target=$ADD_TARGET line=$ADD_LINE" ;;
  *)
    ok "the recovery route's worktree target is per-invocation, not a fixed absolute path ($ADD_TARGET)" ;;
esac

# A FRESH directory, not merely a variable one. `mktemp -d` is what makes adoption impossible: tier 1
# execs whatever bin it is given with BOTH guards skipped by design, so a directory whose NAME asserts
# currency it cannot keep would reintroduce #929/#1507's hazard through the remedy meant to avoid it.
if printf '%s' "$HB_ERR" | grep -qF 'mktemp -d'; then
  ok "the recovery route creates a FRESH directory (mktemp -d) rather than reusing a named one, so nothing an earlier session left behind can be adopted as 'current' through tier 1"
else
  bad "the recovery route must create a fresh directory per invocation" "err=$HB_ERR"
fi

# (b) BEHAVIOURAL — the printed command, run verbatim, from two linked worktrees of ONE repository.
# The `dotnet build` line is the only substitution, because this suite is hermetic and builds no engine
# (see the file header): it becomes `true &&`, so the AND-chain, the `mktemp`, the `git worktree add` and
# the `export` all still run exactly as the refusal printed them. TMPDIR is redirected under $ROOT so the
# suite's own trap reclaims what the route creates.
git -C "$BEHIND/shared" worktree add -q --detach "$BEHIND/wt2" HEAD
mkdir -p "$ROOT/route-tmp"

ROUTE_SCRIPT="$(printf '%s\n' "$HB_ERR" \
  | sed -E -n '/(mktemp -d|git worktree add --detach)/,/export FSGG_COORD_ENGINE_BIN/p' \
  | sed 's/^[[:space:]]*//' \
  | sed 's|^dotnet build .*|true \&\&|')"

# A BLOCK NAMING AN ABSOLUTE PATH IS NOT EXECUTED, AND THAT REFUSAL *IS* THE RED. Running the pre-repair
# text verbatim would create `/tmp/fsgg-engine-current` on whatever machine runs this suite — a location
# shared by every process on it and outside the fixture entirely — and that shared-location property is
# exactly what is under test. So this shape fails these legs by declining to run, with the reason
# printed, rather than by leaking outside $ROOT to prove a point leg (a) has already proved.
ROUTE_BLOCKED=""
[ -n "$ROUTE_SCRIPT" ] || ROUTE_BLOCKED="the refusal printed no recovery command block to extract"
case "$ADD_TARGET" in
  /*) ROUTE_BLOCKED="the printed command names the absolute path $ADD_TARGET — shared by every worker on the machine and outside this fixture, so it is not run verbatim here; that shared path is the defect" ;;
esac

# WHAT THE READER ACTUALLY ENDS UP WITH is the EXPORTED bin, not an intermediate variable, so that is
# what is read back — a block that set a private variable correctly and exported something else would
# otherwise pass.
run_route() {
  ( cd "$1" && export TMPDIR="$ROOT/route-tmp" \
    && eval "$ROUTE_SCRIPT" >/dev/null 2>"$ROOT/.route.err" \
    && printf '%s' "${FSGG_COORD_ENGINE_BIN%/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine}" )
}

if [ -z "$ROUTE_BLOCKED" ]; then
  ROUTE_A="$(run_route "$BEHIND/wt")";  ROUTE_A_RC=$?; ROUTE_A_ERR="$(cat "$ROOT/.route.err")"
  ROUTE_B="$(run_route "$BEHIND/wt2")"; ROUTE_B_RC=$?; ROUTE_B_ERR="$(cat "$ROOT/.route.err")"

  # STDERR MUST CARRY NO FAILURE, not merely exit status zero. The pre-repair block was an UNCHAINED
  # list, so the second worker's `git worktree add` failed while the two lines after it still ran and
  # still exported a bin: rc 0, `fatal: … already exists` on stderr, and a directory it did not create.
  # Emptiness is the wrong test — `git worktree add` writes its ordinary "Preparing worktree …" line
  # there — so the assertion is the absence of a FAILURE, which is what the reader would have to notice.
  route_clean() { ! printf '%s' "$1" | grep -qiE 'fatal|error|already exists'; }
  if [ "$ROUTE_A_RC" -eq 0 ] && [ "$ROUTE_B_RC" -eq 0 ] \
     && route_clean "$ROUTE_A_ERR" && route_clean "$ROUTE_B_ERR" \
     && [ -n "$ROUTE_A" ] && [ -n "$ROUTE_B" ] && [ "$ROUTE_A" != "$ROUTE_B" ] \
     && [ -e "$ROUTE_A/.git" ] && [ -e "$ROUTE_B/.git" ]; then
    ok "the PRINTED command succeeds silently for TWO workers blocked in the same window, each from its own linked worktree of one repository, and each gets its OWN checkout"
  else
    bad "N simultaneously-blocked workers must each get a printed command that works" \
        "A rc=$ROUTE_A_RC dir=$ROUTE_A err=$ROUTE_A_ERR | B rc=$ROUTE_B_RC dir=$ROUTE_B err=$ROUTE_B_ERR"
  fi

  # AND WHAT EACH ONE GOT IS THE MEASURED REF — the route's whole promise is a CURRENT engine source
  # tree. A command that succeeded twice onto the wrong commit would satisfy the leg above and still
  # leave both workers building the engine they were already refused for.
  WANT="$(git -C "$BEHIND/shared" rev-parse refs/remotes/origin/main)"
  if [ -n "$ROUTE_A" ] && [ -n "$ROUTE_B" ] \
     && [ "$(git -C "$ROUTE_A" rev-parse HEAD 2>/dev/null)" = "$WANT" ] \
     && [ "$(git -C "$ROUTE_B" rev-parse HEAD 2>/dev/null)" = "$WANT" ]; then
    ok "both checkouts are at the ref the guard MEASURED ($WANT) — each blocked worker gets a genuinely current engine source tree, not merely a directory"
  else
    bad "each worker's checkout must be at the ref upstream_drift resolved" \
        "want=$WANT A=$ROUTE_A B=$ROUTE_B"
  fi
else
  bad "N simultaneously-blocked workers must each get a printed command that works" "$ROUTE_BLOCKED"
  bad "each worker's checkout must be at the ref upstream_drift resolved" "$ROUTE_BLOCKED"
fi

# ---- 6. NO STATE-TRANSITION WRITE IS WEAKENED (.github#2581 acceptance 5) -------------------------
# The verb list is READ OUT OF THE MODULE UNDER TEST, not restated here — a future edit that shortened
# the sets would otherwise leave this leg quietly measuring fewer verbs than the guard declares. Same
# anchored-literal extraction `tests/coord-engine-parity/shim.sh` §3b uses, so a spelling with a
# substitution in it reds rather than being evaluated.
PART="$(grep -E '^BOARD_(WRITES|WRITES_CONDITIONAL)="[^"$`(]*"$' "$ROOT/scripts/fsgg-coord-guards.sh")"
if [ "$(printf '%s\n' "$PART" | grep -c .)" -ne 2 ]; then
  bad "the module must declare BOARD_WRITES and BOARD_WRITES_CONDITIONAL as plain literals" "$PART"
else
  BOARD_WRITES=""; BOARD_WRITES_CONDITIONAL=""
  eval "$PART"
  wfail=0; refused=""
  # WORD SPLITTING IS THE POINT — the sets are space-separated lists.
  # shellcheck disable=SC2086
  for verb in $BOARD_WRITES $BOARD_WRITES_CONDITIONAL; do
    drive "$BEHIND" "$verb" "$FIXREF"
    if [ "$DRIVE_RC" -ne 0 ] && ! printf '%s' "$DRIVE_OUT" | grep -q 'ENGINE RAN' \
       && printf '%s' "$DRIVE_ERR" | grep -qi 'refused'; then
      refused="$refused $verb"
    else
      wfail=1
      bad "'$verb' writes shared state and MUST still be refused on a checkout behind on the engine trees" "rc=$DRIVE_RC out=$DRIVE_OUT err=$DRIVE_ERR"
    fi
  done
  [ "$wfail" -eq 0 ] && ok "every write verb the module declares is still refused under the .github#2581 condition —$refused"

  # NAMED EXPLICITLY AS WELL AS BY THE LOOP, because acceptance 5 names these five and the loop's
  # subject is whatever the module says: if a future edit moved one of them out of the write sets, the
  # loop above would happily certify the shorter list and only this leg would say so.
  sfail=0
  for verb in 'done' claim take widen set-paths; do
    drive "$BEHIND" "$verb" "$FIXREF"
    if [ "$DRIVE_RC" -eq 0 ] || printf '%s' "$DRIVE_OUT" | grep -q 'ENGINE RAN'; then
      sfail=1
      bad "acceptance 5: '$verb' is a state-transition write and must remain refused" "rc=$DRIVE_RC out=$DRIVE_OUT err=$DRIVE_ERR"
    fi
  done
  [ "$sfail" -eq 0 ] && ok "acceptance 5 by name: done, claim, take, widen and set-paths are all still refused — this row does not reopen #1507"
fi

# ---- 7. READS STILL WARN AND RUN, AND CARRY NO RECOVERY ROUTE ------------------------------------
# A stale read misinforms one worker; refusing it halts diagnosis for nothing. And the recovery route is
# composed at the refusal sites rather than into `$detail`, so a read that still ran does not print a
# remedy for a refusal that did not happen — a warning that says more than it needs to is one the fleet
# learns to skim, and the next one is real.
drive "$BEHIND" who
if [ "$DRIVE_RC" -eq 0 ] && printf '%s' "$DRIVE_OUT" | grep -q 'ENGINE RAN' \
   && printf '%s' "$DRIVE_ERR" | grep -qi 'stale' \
   && ! printf '%s' "$DRIVE_ERR" | grep -q 'KEEP YOUR CLAIM WITHOUT TOUCHING THAT CHECKOUT'; then
  ok "a READ still warns and still runs, and does NOT carry the recovery route — the refusal-only text stays out of the warning path"
else
  bad "a read must warn, run, and not print a recovery route for a refusal that did not happen" "rc=$DRIVE_RC out=$DRIVE_OUT err=$DRIVE_ERR"
fi

# ---- 8. THE HAPPY PATH IS SILENT (.github#2581 acceptance, regression) ----------------------------
# The new text lives inside the branch that composes a refusal. A stray unconditional line would fire on
# every worker after every legitimate build, which is the failure mode this whole module warns about.
drive "$CURRENT" heartbeat "$FIXREF"
if [ "$DRIVE_RC" -eq 0 ] && [ -z "$DRIVE_ERR" ]; then
  ok "a checkout that is NOT behind, with an engine newer than its source, is wholly silent — the new text manufactures no refusal and no warning"
else
  bad "the happy path must stay silent" "rc=$DRIVE_RC out=$DRIVE_OUT err=$DRIVE_ERR"
fi

# ---- 9. THE JUSTIFICATION IS REGIME-QUALIFIED (.github#2581 acceptance 2) -------------------------
# Anchored to the EXACT retired sentence rather than to the phrase inside it: the module deliberately
# quotes the old wording while retiring it (this file's whole tradition is to keep what was believed),
# so a grep for "local, cheap and theirs" would match the quotation and certify nothing.
RETIRED='That is a stall of about a minute, on a remedy that is local, cheap and theirs, and it is the'
if grep -qF "$RETIRED" "$ROOT/scripts/fsgg-coord-guards.sh"; then
  bad "the unqualified cost claim is still asserted as live prose at the guard's cost paragraph" "$RETIRED"
else
  ok "the unqualified 'local, cheap and theirs' cost claim is no longer asserted — it survives only as a quoted, retired wording"
fi
if grep -q 'UNDER HOST-SERIALISED REPAIR IT IS FALSE' "$ROOT/scripts/fsgg-coord-guards.sh"; then
  ok "the cost paragraph names the regime in which the claim is false, so a reader auditing this guard is not told the cost is bounded when it is not"
else
  bad "the cost paragraph must say which regime it describes"
fi

# ---- 10. THE SUBJECT IS REACHABLE FROM CI (.github#2581 SB-008) ----------------------------------
# The required completeness context must exist on every PR, so the PR trigger is intentionally unfiltered.
# The expensive successor still needs exact impact classification, and pushes retain their path filter.
# This proves a guard-only edit reaches both of those path-sensitive decisions without reintroducing the
# selectively silent PR trigger that `.github#2551` identified.
CE="$REPO_ROOT/.github/workflows/coord-engine.yml"
pr_trigger="$(sed -n '/^  pull_request:/,/^  push:/p' "$CE")"
push_trigger="$(sed -n '/^  push:/,/^  workflow_dispatch:/p' "$CE")"
if ! printf '%s' "$pr_trigger" | grep -q 'paths:' \
   && printf '%s' "$push_trigger" | grep -q '"scripts/fsgg-coord-guards.sh"' \
   && grep -q 'fsgg-coord-guards\\\.sh' "$REPO_ROOT/scripts/change-completeness"; then
  ok "coord-engine.yml runs change-completeness on every PR, while push filtering and the impact classifier both select the guard module"
else
  bad "coord-engine.yml must keep PR completeness unfiltered and retain guard-module selection in push + impact classification"
fi

CG="$REPO_ROOT/.github/workflows/coord-guards.yml"
if [ -f "$CG" ] && grep -q 'tests/coord-guards/run.sh' "$CG" \
   && grep -q '"scripts/fsgg-coord-guards.sh"' "$CG" && grep -q '"tests/coord-guards/\*\*"' "$CG"; then
  ok "coord-guards.yml runs this suite and is selected by a change to the guard module, the shim, or this suite"
else
  bad "a workflow must run tests/coord-guards/run.sh and be selected by the paths this suite is about" "$CG"
fi

# ---- 11. THE ENGINE IS TWO PROJECTS, AND A KERNEL-ONLY COMMIT DRIFTS (.github#2725) ---------------
# WHY THIS SECTION EXISTS AS A REPAIR AND NOT AS AN ADDITION. `.github#2725` added
# `src/FS.GG.Coord.Cli.Kernel` to `ENGINE_SOURCE_TREES`, and independent review measured that REVERTING
# that entry left this suite at 27/27 green: a load-bearing constant with no fixture behind it, which
# `independent-review.md` § Gate-inversion makes a material finding by definition. The consequence is
# not cosmetic — with the entry gone, a shared checkout behind on the Kernel ALONE produces no drift
# signal at all, so the guard hands the worker the shared engine and silently discards the very edits it
# is testing: the failure `scripts/fsgg-coord:209-213` forbids.
#
# GIT PATHSPECS MATCH PATH COMPONENTS, NOT STRING PREFIXES, which is why the entry is load-bearing at all
# and why `src/FS.GG.Coord.Cli` does not quietly cover it. That is also the module's own stated reason for
# listing the tree explicitly instead of leaning on prefix matching.
drive "$KERNEL" heartbeat "$FIXREF"
K_RC="$DRIVE_RC"; K_OUT="$DRIVE_OUT"; K_ERR="$DRIVE_ERR"
# HERE-STRINGS, NOT PIPELINES, in this section and §12: bash materialises the whole string before `grep`
# reads it, so `grep -q`'s early exit has no live writer to SIGPIPE. `check-pipefail-assertions.py`
# ratchets the pipeline spelling down and does not accept a raised baseline as the remedy.
if [ "$K_RC" -eq 69 ] && ! grep -q 'ENGINE RAN' <<<"$K_OUT" \
   && grep -qF 'BEHIND refs/remotes/origin/main by 1 commit' <<<"$K_ERR"; then
  ok "a shared checkout behind on src/FS.GG.Coord.Cli.Kernel ALONE is BEHIND: 'heartbeat' REFUSED (exit 69), engine never reached — the .github#2725 entry in ENGINE_SOURCE_TREES is load-bearing and now measured"
else
  bad "a commit under src/FS.GG.Coord.Cli.Kernel alone must count as engine drift: the Kernel is packed into the tool, so it changes the engine the fleet runs" \
      "rc=$K_RC out=$K_OUT err=$K_ERR"
fi

# THE CONTROL, WITHOUT WHICH THE LEG ABOVE PROVES NOTHING. A leg that reds on ANY upstream commit would
# pass with or without the Kernel entry and would be certifying "behind-ness", not the tree list. So the
# same fixture shape with its one upstream commit OUTSIDE every named tree must be wholly silent — and
# `docs/` is a tree this guard deliberately does not count, not an accident of the fixture.
drive "$OUTSIDE" heartbeat "$FIXREF"
if [ "$DRIVE_RC" -eq 0 ] && grep -q 'ENGINE RAN' <<<"$DRIVE_OUT" && [ -z "$DRIVE_ERR" ]; then
  ok "the same shape with its upstream commit OUTSIDE the engine trees (docs/) is silent and runs — so §11 is keyed on the TREE LIST, not on behind-ness as such"
else
  bad "a commit outside every tree ENGINE_SOURCE_TREES names must not produce a verdict" "rc=$DRIVE_RC out=$DRIVE_OUT err=$DRIVE_ERR"
fi

# ---- 12. EVERY TREE THE LIST NAMES STILL EXISTS (the second mutation) -----------------------------
# §11 catches "the entry was deleted". It does NOT catch "the project was renamed and the entry now names
# nothing" — two different mutations, and review measured that the sibling gates repaired on this row
# survive the second one. `upstream_drift` cannot be the one to catch it: its subject is on the hot path
# of every tier-2 invocation, and a missing-tree REFUSAL there costs an outage for the whole fleet, which
# is the module's own stated reason for keeping that pathspec narrow. A static assertion in this suite
# costs nothing and turns a rename from silent into red.
#
# THE LIST IS READ OUT OF THE MODULE UNDER TEST, never restated here — §6's anchored-literal extraction,
# for §6's reason: a leg that retypes the constant certifies the test author's memory.
TREES_DECL="$(grep -E '^ENGINE_SOURCE_TREES="[^"$`(]*"$' "$ROOT/scripts/fsgg-coord-guards.sh")"
if [ "$(printf '%s\n' "$TREES_DECL" | grep -c .)" -ne 1 ]; then
  bad "the module must declare ENGINE_SOURCE_TREES as a single plain literal" "$TREES_DECL"
else
  ENGINE_SOURCE_TREES=""
  eval "$TREES_DECL"
  missing=""; treecount=0
  # WORD SPLITTING IS THE POINT — the constant is a space-separated pathspec list.
  # shellcheck disable=SC2086
  for tree in $ENGINE_SOURCE_TREES; do
    treecount=$((treecount+1))
    [ -d "$REPO_ROOT/$tree" ] || missing="$missing $tree"
  done
  if [ "$treecount" -ge 2 ] && [ -z "$missing" ]; then
    ok "all $treecount trees ENGINE_SOURCE_TREES names exist in this repository — a renamed or removed engine project reds here instead of leaving rev-list counting 0 over nothing, forever, silently"
  else
    bad "every tree ENGINE_SOURCE_TREES names must exist: a pathspec matching nothing counts 0, and 0 reads as FRESH" \
        "count=$treecount missing:$missing"
  fi

  # AND THE KERNEL IS NAMED, BY NAME. The loop above is satisfied by any list whose entries exist,
  # including the pre-.github#2725 three — so without this the rename guard would also certify the
  # reverted list. This is the assertion §11 proves the CONSEQUENCE of.
  # `case`, not `grep`: a fixed-string containment test needs no process at all, let alone a pipeline.
  case " $ENGINE_SOURCE_TREES " in
  *" src/FS.GG.Coord.Cli.Kernel "*)
    ok "ENGINE_SOURCE_TREES names src/FS.GG.Coord.Cli.Kernel as its own entry, not by prefix accident" ;;
  *)
    bad "ENGINE_SOURCE_TREES must name src/FS.GG.Coord.Cli.Kernel explicitly — git pathspecs match components, so src/FS.GG.Coord.Cli does not cover it" \
        "list=$ENGINE_SOURCE_TREES" ;;
  esac
fi

echo "coord-guards: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
