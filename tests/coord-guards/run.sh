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
# $1 = root, $2 = "behind" (the .github#2581 shape) or "current" (the happy path).
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
  mkdir -p "$shared/src/FS.GG.Coord.Cli" "$shared/src/FS.GG.Coord.Core" "$shared/src/FS.GG.Coord.GitHub"
  printf 'bin/\nobj/\n'  >"$shared/.gitignore"
  printf '// cli\n'      >"$shared/src/FS.GG.Coord.Cli/Program.fs"
  printf '// core v1\n'  >"$shared/src/FS.GG.Coord.Core/Protocol.fs"
  printf '// github\n'   >"$shared/src/FS.GG.Coord.GitHub/Writes.fs"

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

  if [ "$mode" = behind ]; then
    # ONE COMMIT UNDER THE ENGINE'S OWN SOURCE TREES — not under docs or workflows, which
    # `ENGINE_SOURCE_TREES` deliberately does not count.
    printf '// core v2\n' >"$shared/src/FS.GG.Coord.Core/Protocol.fs"
    git -C "$shared" commit -q -a -m 'engine v2'
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
if printf '%s' "$HB_ERR" | grep -q 'git worktree add --detach /tmp/fsgg-engine-current refs/remotes/origin/main'; then
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

# ---- 5. THE ROUTE IS EXECUTED, AND THE CLAIM IS RENEWABLE (.github#2581 acceptance 1) -------------
# The whole item reduces to this leg. Same fixture, same behind-ness, same shared checkout untouched,
# same head — and the worker's own CURRENT engine named through tier 1. If this is red, the refusal is
# advertising a route that does not work, which is worse than printing nothing.
#
# AND IT IS GREEN AGAINST THE PRE-REPAIR MODULE TOO — SAID PLAINLY, BECAUSE THAT IS THE FINDING, NOT A
# WEAKNESS OF THE LEG. Tier 1 always worked; .github#2581's defect was never that the route was broken,
# it was that the refusal named the ONE checkout the blocked worker must not touch and never mentioned
# this one. So the legs that go red under inversion are §2, §3, §4 and §9 (the text and the
# justification), and this leg is a REGRESSION guard on the property they now advertise: the moment the
# route stops working, the refusal starts lying.
RECOVER_ERR="$ROOT/.recover.err"
RECOVER_OUT="$( cd "$BEHIND/wt" && FSGG_COORD_ENGINE_BIN="$MYENGINE" "$SHIM" heartbeat "$FIXREF" 2>"$RECOVER_ERR" )"
RECOVER_RC=$?
RECOVER_ERR_TXT="$(cat "$RECOVER_ERR")"
if [ "$RECOVER_RC" -eq 0 ] \
   && printf '%s' "$RECOVER_OUT" | grep -q 'MY CURRENT ENGINE RAN: heartbeat' \
   && [ -z "$RECOVER_ERR_TXT" ]; then
  ok "the recovery route WORKS: the same blocked worker renews through a CURRENT engine it built — exit 0, no staleness verdict consulted, shared checkout untouched"
else
  bad "the printed recovery route must actually lift the refusal" "rc=$RECOVER_RC out=$RECOVER_OUT err=$RECOVER_ERR_TXT"
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
# `scripts/fsgg-coord-guards.sh` was named by NO workflow `paths:` filter anywhere in the repository.
# Since .github#1586 moved the partition and both guards into it, a PR editing only that file started
# `coord-engine.yml` on no path — so §3b and §3c, whose entire subject IS that file, were silent on
# exactly the pull requests that change it (.github#2551's class).
CE="$REPO_ROOT/.github/workflows/coord-engine.yml"
n="$(grep -c '"scripts/fsgg-coord-guards.sh"' "$CE" 2>/dev/null || true)"
if [ "${n:-0}" -ge 2 ]; then
  ok "coord-engine.yml names scripts/fsgg-coord-guards.sh in both its pull_request and push paths filters ($n occurrences) — the parity gate now runs on the PRs whose subject it is"
else
  bad "coord-engine.yml must select a change to the guard module in BOTH trigger filters" "occurrences=${n:-0}"
fi

CG="$REPO_ROOT/.github/workflows/coord-guards.yml"
if [ -f "$CG" ] && grep -q 'tests/coord-guards/run.sh' "$CG" \
   && grep -q '"scripts/fsgg-coord-guards.sh"' "$CG" && grep -q '"tests/coord-guards/\*\*"' "$CG"; then
  ok "coord-guards.yml runs this suite and is selected by a change to the guard module, the shim, or this suite"
else
  bad "a workflow must run tests/coord-guards/run.sh and be selected by the paths this suite is about" "$CG"
fi

echo "coord-guards: $pass passed, $failcount failed"
[ "$failcount" -eq 0 ]
