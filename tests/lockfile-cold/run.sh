#!/usr/bin/env bash
# Fixture for the COLD locked restore — the failure-leg test ADR-0032 §5 and #429 have been missing.
#
# WHY THIS EXISTS. ADR-0032 §5 ratifies that any restore which WRITES (`--force-evaluate`) or ENFORCES
# (`--locked-mode`) a packages.lock.json must be COLD: an empty NUGET_PACKAGES *and* a cleared HTTP
# cache. Both halves shipped (FS.GG.SDD's `locked-restore` action; this repo's lockfile-sync.yml,
# .github#453). NOTHING ASSERTED IT. No test failed if someone reinstated `cache: true`, dropped the
# `mktemp -d` NUGET_PACKAGES, or removed the `http-cache --clear` — the gate would quietly go back to
# comparing a record against a record and to being GREEN on a lock file no fresh clone can restore.
# That is #429's fails-open state, and per epic #266's ratified rule — A GATE THAT CANNOT FAIL ON ITS
# SUBJECT IS NOT A GATE — the failure leg has to be exercised, not asserted in prose. (.github#460,
# absorbing #459.)
#
# ⚠️ CORRECTION (ADR-0032, .github#471). This header used to say FSharp.Core 10.1.301 "was re-published
# under the same version". IT WAS NOT. There have always been TWO different .nupkg files at that
# id+version — the one the .NET SDK bundles (…/sdk/10.0.301/FSharp/library-packs/, 3,051,664 B) and the
# one nuget.org serves (3,066,660 B) — so the lock's contentHash is a function of WHICH SOURCE resolved
# the package, not of WHEN. CI resolves the SDK's copy; a dev box whose NuGet config excludes
# library-packs resolves nuget.org's.
#
# EVERY LEG BELOW STILL HOLDS, and none of them is about FSharp.Core: they are properties of NuGet's
# warm-vs-cold validation, exercised against a package this fixture builds itself. A warm folder IS a
# fail-open (leg 3 proves it), and a cold restore IS the fix for that — ADR-0032 §Decision 5 stands.
# What the old narration got wrong was WHY the org's lock files diverged, and a fixture that teaches a
# false cause is worse than one that teaches none. Cold is not hermetic: the SDK's library-packs folder
# is injected by MSBuild and a fresh NUGET_PACKAGES does not bypass it. That is ADR-0032's problem, and
# it is NOT what this fixture tests.
#
# WHAT IT PROVES, against a real `dotnet restore` and NO NETWORK. A hand-built .nupkg in a local feed
# stands in for any package; the mechanism under test is NuGet's, not any particular package's:
#
#   LEG 1  cold  + the feed's contentHash   -> RESTORES.  A gate that rejects everything is not a
#                                              passing gate; the check has to be DISCRIMINATING.
#   LEG 2  cold  + a stale contentHash      -> NU1403.    THE FAILURE LEG. Today, warm, this passes.
#   LEG 3  WARM  + a stale contentHash, and -> RESTORES.  #429 ITSELF, REPRODUCED: a poisoned local
#          a .nupkg.metadata poisoned to                  record makes a lock file that no fresh
#          agree with it                                  clone can restore go GREEN. This is why
#                                                         coldness is the property, not a preference.
#   LEG 4  the workflow still IS cold                     Static guard: the three properties that
#                                                         make leg 2 possible cannot be quietly
#                                                         removed from lockfile-sync.yml.
#   LEG 5  the ADR-0032 source report still               Static guard (#504): an ::error with an
#          FAILS CLOSED                                   `exit 0` after it is a message, not a gate.
#   LEG 6  ...and that report actually RUNS,   -> the report step, LIFTED OUT OF THE YAML and executed
#          in every state, under the shell        under GitHub's own `bash -e {0}`, against a package
#          GitHub really gives it                 folder that is compliant (PASS), regressed (FAIL),
#                                                 absent (FAIL — no evidence), empty (PASS — Templates
#                                                 resolves nothing), and unreadable (FAIL — a broken
#                                                 read is not a pass either).
#   LEG 7  the ADR-0032 MECHANISM, against    -> the property the whole ADR rests on, EXECUTED against a
#          the real SDK — and then the           real restore. In the SDK's default state the folder
#          SHIPPED CONFIG, against a real        serves FSharp.Core (the defect is real, so the test has
#          restore under it                      a subject); with the property set it is gone from the
#                                                source list; and a probe that INHERITS the shipped
#                                                dist/dotnet/Directory.Build.props gets the same
#                                                exclusion — so the fix is live, not merely present.
#                                                Legs 5/6 test the REPORTER and fabricate their
#                                                evidence; nothing tested the property itself.
#
# Legs 2 and 3 are the pair. Leg 2 alone would be satisfied by a gate that is merely broken; leg 3
# shows the SAME lock file passing the moment the restore stops being cold, which is the defect.
#
# Leg 7 is the one that grounds the rest. Legs 5 and 6 prove the org REPORTS a library-packs
# resolution; leg 7 proves the org PREVENTS one — and that the thing it is preventing is real. A suite
# that only ever fabricates its evidence can be perfectly green about a mechanism that has silently
# stopped working (.github#473 step 3).
#
# Legs 5 and 6 are the other pair, and #639 is why leg 6 exists. Leg 5 GREPS the report step; it went on
# passing while that step could not pass AT ALL — errexit, which GitHub reimposes on a script that opted
# out of it, killed the step on `grep`'s no-match, and no-match is the ADR-0032 PASS. So the gate was red
# on the good state, and a repo went red for ADOPTING the invariant. A guard that only reads the source
# of a gate cannot tell you the gate runs. Leg 6 runs it.
#
# The trap that cost .github#429 an hour, encoded here so no one re-treads it: NUGET_PACKAGES=$(mktemp -d)
# IS NOT A COLD CACHE. It relocates global-packages but NOT the HTTP cache, which happily serves stale
# .nupkg bytes. Every cold leg below clears the http-cache too — and leg 3 is what you get if you don't.

set -euo pipefail

HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$HERE/../.."
SYNC_WF="$ROOT/.github/workflows/lockfile-sync.yml"

WORK="$(mktemp -d "${TMPDIR:-/tmp}/fsgg-lockfile-cold.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

command -v dotnet >/dev/null 2>&1 || { echo "::error::dotnet not found — this fixture drives a real restore."; exit 1; }

# ---- the local feed: one hand-built .nupkg, so the fixture is hermetic and offline -----------------
# Built as a zip, not with `dotnet pack`: the subject is NuGet's hash validation, and a package we
# assemble ourselves is one whose bytes — and therefore whose contentHash — nothing outside this
# fixture can change. One feed, one copy, one hash. That isolation is what lets the legs below say
# something about NuGet rather than about the state of the world's package sources (ADR-0032).
FEED="$WORK/feed"; PROJ="$WORK/proj"; mkdir -p "$FEED" "$PROJ"
python3 - "$FEED" <<'PY'
import zipfile, sys, os
feed = sys.argv[1]
nuspec = '''<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
  <metadata>
    <id>Fsgg.ColdProbe</id>
    <version>1.0.0</version>
    <authors>FS-GG</authors>
    <description>Fixture package for the cold-restore failure-leg test (.github#460).</description>
  </metadata>
</package>'''
ct = ('<?xml version="1.0" encoding="utf-8"?><Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">'
      '<Default Extension="nuspec" ContentType="text/xml"/><Default Extension="dll" ContentType="application/octet-stream"/></Types>')
with zipfile.ZipFile(os.path.join(feed, 'Fsgg.ColdProbe.1.0.0.nupkg'), 'w') as z:
    z.writestr('Fsgg.ColdProbe.nuspec', nuspec)
    z.writestr('lib/net10.0/Fsgg.ColdProbe.dll', b'\x00')
    z.writestr('[Content_Types].xml', ct)
PY

# `<clear />` on BOTH packageSources and packageSourceMapping: a machine-level NuGet.config (the dev
# container has one) otherwise leaks in a source mapping that excludes our feed, and the fixture would
# fail for a reason that has nothing to do with what it tests.
cat > "$PROJ/nuget.config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="local" value="../feed" /></packageSources>
  <packageSourceMapping>
    <clear />
    <packageSource key="local"><package pattern="*" /></packageSource>
  </packageSourceMapping>
</configuration>
EOF
# net10.0, not netstandard2.0: the latter resolves NETStandard.Library from a feed, and this fixture
# has exactly one package in exactly one feed. The SDK's targeting pack keeps the graph at one node.
cat > "$PROJ/probe.csproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="Fsgg.ColdProbe" Version="1.0.0" /></ItemGroup>
</Project>
EOF

LOCK="$PROJ/packages.lock.json"
STALE_HASH="ABCDEFGHijklmnop1234567890abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789ab=="

set_hash() {  # set_hash <file> <hash>   — rewrite the lock's contentHash
  python3 - "$1" "$2" <<'PY'
import json, sys
d = json.load(open(sys.argv[1]))
d['dependencies']['net10.0']['Fsgg.ColdProbe']['contentHash'] = sys.argv[2]
json.dump(d, open(sys.argv[1], 'w'), indent=2)
PY
}
cold_restore() {  # a GENUINELY cold restore: fresh global-packages AND a cleared HTTP cache
  local pkgs; pkgs="$(mktemp -d "$WORK/pkgs.XXXXXX")"
  dotnet nuget locals http-cache --clear >/dev/null 2>&1 || true
  ( cd "$PROJ" && NUGET_PACKAGES="$pkgs" dotnet restore --locked-mode 2>&1 )
}

# ---- generate the lock file the way lockfile-sync.yml does: COLD --force-evaluate ------------------
GEN_PKGS="$(mktemp -d "$WORK/gen.XXXXXX")"
dotnet nuget locals http-cache --clear >/dev/null 2>&1 || true
( cd "$PROJ" && NUGET_PACKAGES="$GEN_PKGS" dotnet restore --force-evaluate >/dev/null 2>&1 ) \
  || { echo "::error::could not generate the lock file — fixture setup failed."; exit 1; }
[ -f "$LOCK" ] || { echo "::error::no packages.lock.json was written."; exit 1; }
FEED_HASH="$(python3 -c "import json;print(json.load(open('$LOCK'))['dependencies']['net10.0']['Fsgg.ColdProbe']['contentHash'])")"
cp "$LOCK" "$WORK/good.lock.json"
[ -n "$FEED_HASH" ] && [ "$FEED_HASH" != "$STALE_HASH" ] \
  && ok "setup: a cold --force-evaluate wrote the FEED's contentHash (${FEED_HASH:0:12}…)" \
  || bad "setup: a cold --force-evaluate wrote the FEED's contentHash"

# ---- LEG 1: cold + the feed's hash must RESTORE (the check is discriminating, not just red) --------
if cold_restore >/dev/null 2>&1; then
  ok "LEG 1: a CORRECT lock file passes a cold --locked-mode restore"
else
  bad "LEG 1: a CORRECT lock file passes a cold --locked-mode restore" \
      "the gate rejects a correct lock — this is #429's fails-CLOSED half, and it blocks every fix"
fi

# ---- LEG 2: THE FAILURE LEG — cold + a stale hash must be REJECTED --------------------------------
set_hash "$LOCK" "$STALE_HASH"
leg2="$(cold_restore || true)"
if printf '%s' "$leg2" | grep -q 'NU1403'; then
  ok "LEG 2: a lock file a cold restore CANNOT satisfy is REJECTED (NU1403)"
else
  bad "LEG 2: a lock file a cold restore CANNOT satisfy is REJECTED (NU1403)" \
      "THE GATE WENT GREEN ON A BROKEN LOCK FILE. This is the whole subject of #429/#266: a gate that
cannot fail on its subject is not a gate. Output:
$leg2"
fi

# ---- LEG 3: #429 ITSELF — warm + a poisoned local record makes the SAME lock file pass -------------
# Populate a warm global-packages folder, then poison the package's .nupkg.metadata so its recorded
# contentHash agrees with the STALE lock. NuGet then validates the lock against that local record —
# a record against a record — and restores happily. Nothing about the feed changed; only the coldness.
WARM="$(mktemp -d "$WORK/warm.XXXXXX")"
( cd "$PROJ" && NUGET_PACKAGES="$WARM" dotnet restore --force-evaluate >/dev/null 2>&1 ) || true
META="$WARM/fsgg.coldprobe/1.0.0/.nupkg.metadata"
if [ -f "$META" ]; then
  python3 - "$META" "$STALE_HASH" <<'PY'
import json, sys
m = json.load(open(sys.argv[1])); m['contentHash'] = sys.argv[2]
json.dump(m, open(sys.argv[1], 'w'), indent=2)
PY
  set_hash "$LOCK" "$STALE_HASH"
  if ( cd "$PROJ" && NUGET_PACKAGES="$WARM" dotnet restore --locked-mode >/dev/null 2>&1 ); then
    ok "LEG 3: the SAME broken lock file passes a WARM restore — #429, reproduced"
  else
    bad "LEG 3: the SAME broken lock file passes a WARM restore — #429, reproduced" \
        "the warm path rejected it, so this fixture no longer demonstrates WHY the restore must be cold.
NuGet's behaviour may have changed — if warm validation now consults the feed, say so in ADR-0032
rather than deleting this leg."
  fi
else
  bad "LEG 3: the SAME broken lock file passes a WARM restore — #429, reproduced" \
      "no .nupkg.metadata at $META — the warm folder was not populated, so the leg proved nothing"
fi
cp "$WORK/good.lock.json" "$LOCK"

# ---- LEG 4: the three properties that MAKE the restore cold are still in the workflow --------------
# Leg 2 can only fail-red because lockfile-sync.yml restores cold. Nothing stops a future edit from
# reinstating `cache: true`, dropping the fresh NUGET_PACKAGES, or removing the http-cache clear — and
# the behavioural legs above would keep passing (they build their own coldness) while PRODUCTION went
# quietly back to comparing a record against a record. So assert the workflow itself, by name.
if [ -f "$SYNC_WF" ]; then
  grep -qE 'NUGET_PACKAGES="\$\(mktemp -d\)"' "$SYNC_WF" \
    && ok "LEG 4: lockfile-sync.yml restores into a FRESH NUGET_PACKAGES" \
    || bad "LEG 4: lockfile-sync.yml restores into a FRESH NUGET_PACKAGES" \
           "the mktemp -d global-packages folder is gone — the restore is warm again (ADR-0032 §5)"
  grep -qE 'dotnet nuget locals http-cache --clear' "$SYNC_WF" \
    && ok "LEG 4: ...and CLEARS THE HTTP CACHE (relocating NUGET_PACKAGES alone is not cold)" \
    || bad "LEG 4: ...and CLEARS THE HTTP CACHE (relocating NUGET_PACKAGES alone is not cold)" \
           "without this the HTTP cache serves stale .nupkg bytes — the exact trap #429 hit"
  if grep -qE '^\s*cache:\s*true' "$SYNC_WF"; then
    bad "LEG 4: ...and does NOT re-enable setup-dotnet's package cache" \
        "'cache: true' is back in lockfile-sync.yml — that restores a warm package folder and undoes ADR-0032 §5"
  else
    ok "LEG 4: ...and does NOT re-enable setup-dotnet's package cache"
  fi
else
  bad "LEG 4: the workflow is asserted" "no $SYNC_WF"
fi

# ---- LEG 5: the ADR-0032 source report is still FAIL-CLOSED (.github#504) --------------------------
# Cold (legs 1-4) is not enough on its own: a cold restore that resolves FSharp.Core from the SDK's
# bundled library-packs folder still writes a machine-DEPENDENT contentHash, because that folder ships
# a different .nupkg than nuget.org at the same id+version. lockfile-sync's "Report which source served
# each package" step is what catches it, and it was deliberately ADVISORY (exit 0) for as long as some
# repo had not yet synced DisableImplicitLibraryPacksFolder — failing then would have redded a repo
# whose lock was correct FOR IT.
#
# Every F# repo has now synced, so #504 flipped that step to exit 1. This is the same class of guard as
# leg 4, and it exists for the same reason: nothing stops a future edit from putting the `exit 0` back,
# and every behavioural leg above would keep passing while production quietly resumed COMMITTING
# machine-dependent lock files. Assert the exit code, by name.
if [ -f "$SYNC_WF" ]; then
  report_step="$(awk '/name: Report which source served each package/,/name: Commit and push if changed/' "$SYNC_WF")"

  if printf '%s' "$report_step" | grep -qE '^\s*echo "::error title=lock file is not machine-independent'; then
    ok "LEG 5: the ADR-0032 source report FAILS (::error) on a library-packs resolution"
  else
    bad "LEG 5: the ADR-0032 source report FAILS (::error) on a library-packs resolution" \
        "it warns instead of failing — a library-packs hash would be COMMITTED again (#504/ADR-0032)"
  fi

  # The ::error alone is NOT the gate: an annotation followed by `exit 0` is a message, and the job
  # would go on to COMMIT the machine-dependent lock anyway. So assert ADJACENCY — the statement
  # immediately after that annotation must be `exit 1`. (Counting `exit 1`s in the step would not do:
  # the no-evidence branch above has one, so a count >= 2 stays satisfied even if THIS branch is
  # flipped back to exit 0 — passing the guard while the hole it guards is wide open.)
  after_error="$(
    printf '%s\n' "$report_step" \
      | grep -A1 -E '^\s*echo "::error title=lock file is not machine-independent' \
      | sed -n '2p' | tr -d '[:space:]'
  )"
  if [ "$after_error" = 'exit1' ]; then
    ok "LEG 5: ...and EXITS NON-ZERO right after it, so the machine-dependent lock is never pushed"
  else
    bad "LEG 5: ...and EXITS NON-ZERO right after it, so the machine-dependent lock is never pushed" \
        "the statement after the ::error is '${after_error:-<nothing>}', not 'exit 1' — an annotation the job walks straight past is not a gate; 'Commit and push' still runs"
  fi
else
  bad "LEG 5: the source report is asserted" "no $SYNC_WF"
fi

# ---- LEG 6: the ADR-0032 report is RUN — in every state, under the shell GitHub actually gives it ---
# LEGS 4 AND 5 ARE GREPS, AND THEY BOTH STAYED GREEN WHILE THE STEP THEY GUARD COULD NOT PASS. They
# assert that the report still SAYS the right things; neither can see whether it still DOES them, and
# it did not. The step declares no `shell:`, so GitHub runs it under its default `bash -e {0}` — while
# the script opened `set -uo pipefail`, deliberately omitting the `-e` that was being reimposed over its
# head. `grep -l` exits 1 on NO MATCH, which here is the PASS (it IS the ADR-0032 invariant); xargs maps
# that to 123, pipefail promotes it out of the command substitution, and errexit killed the step before
# the `-z` test could read the empty result it had just computed. The `ok:` branch was dead code: the
# gate went RED on a repo precisely for ADOPTING ADR-0032, and Renovate PRs stopped landing behind it
# (#639, found from FS.GG.Governance#158; the rollout #504 drives was walking every repo onto it).
#
# So this leg EXECUTES the real step — lifted out of the YAML, under the shell GitHub would hand it —
# against a synthetic package folder in each state, and asserts the exit code AND the branch taken. It
# READS the `shell:` key rather than assuming the default, so the equally-valid fix (naming a shell that
# genuinely has no `-e`) passes here too: what this pins is the BEHAVIOUR, not the mechanism.
#
# No dotnet, no network — the step's only input is a tree of .nupkg.metadata files, and the source it
# reads from them is a string. Building that tree by hand is what lets the leg assert the ONE state a
# real restore on this runner cannot produce on demand: a COMPLIANT one.
STEP="$WORK/report-step.sh"
REPORT_SHELL=""
if [ -f "$SYNC_WF" ] && command -v jq >/dev/null 2>&1; then
  REPORT_SHELL="$(
    python3 - "$SYNC_WF" "$STEP" <<'PY'
import shlex, sys, yaml
wf = yaml.safe_load(open(sys.argv[1]))
steps = [s for j in wf["jobs"].values() for s in j.get("steps", [])
         if str(s.get("name", "")).startswith("Report which source served each package")]
if len(steps) != 1:
    sys.exit("expected exactly one ADR-0032 report step, found %d" % len(steps))
step = steps[0]
script = step["run"]
# Actions would interpolate `${{ … }}` before bash ever saw it; we cannot, and bash would die on the
# `${{` with a syntax error that says nothing about why. Refuse with a message that does. (Today the
# step uses none — its inputs arrive as env vars, which is what keeps it drivable like this.)
if "${{" in script:
    sys.exit("the report step now uses a ${{ }} expression; this leg cannot render it — pass the value "
             "in as an env var, or teach this extractor to substitute it")
open(sys.argv[2], "w").write(script)
# GitHub's default shell for `run:` on Linux is `bash -e {0}` — ERREXIT ON — unless the step names one.
# Reproduce that decision here rather than hard-coding it, so this leg keeps testing what production
# runs even if someone fixes #639 the other way (by declaring a shell without -e).
declared = step.get("shell")
if declared is None:
    argv = ["bash", "-e"]
else:
    toks = shlex.split(declared)
    if len(toks) < 2 or toks[0] != "bash" or toks[-1] != "{0}":
        sys.exit("this leg can only drive a `bash … {0}` shell, not %r" % declared)
    argv = toks[:-1]
print(" ".join(argv))
PY
  )" || REPORT_SHELL=""
fi

mk_pkgs() {  # mk_pkgs <dir> <source recorded for fsharp.core>   — a NUGET_PACKAGES folder, by hand
  rm -rf "$1"
  mkdir -p "$1/fsharp.core/10.1.301" "$1/fsgg.coldprobe/1.0.0"
  printf '{"version":2,"contentHash":"h1==","source":"%s"}\n' "$2" \
    > "$1/fsharp.core/10.1.301/.nupkg.metadata"
  printf '{"version":2,"contentHash":"h2==","source":"https://api.nuget.org/v3/index.json"}\n' \
    > "$1/fsgg.coldprobe/1.0.0/.nupkg.metadata"
}
run_report() {  # run_report <NUGET_PACKAGES>  — step output on stdout, step's exit code as the return
  # $REPORT_SHELL is an argv ("bash -e"), so it MUST word-split. That is the point of it.
  # shellcheck disable=SC2086
  NUGET_PACKAGES="$1" GITHUB_REPOSITORY="FS-GG/FS.GG.Fixture" $REPORT_SHELL "$STEP" 2>&1
}

if [ -z "$REPORT_SHELL" ]; then
  bad "LEG 6: the ADR-0032 report step can be driven" \
      "could not extract it from $SYNC_WF (or jq is missing) — the behavioural legs below did not run"
else
  mk_pkgs "$WORK/adr32-compliant" "https://api.nuget.org/v3/index.json"
  mk_pkgs "$WORK/adr32-regressed" "/usr/share/dotnet/sdk/10.0.301/FSharp/library-packs"

  # 6a. THE LEG #639 WAS MISSING. Every package from a feed = the ADR-0032 invariant holding = PASS.
  rc=0; out="$(run_report "$WORK/adr32-compliant")" || rc=$?
  if [ "$rc" -eq 0 ] && printf '%s\n' "$out" | grep -q '^ok: no package was served by an SDK-bundled folder source\.'; then
    ok "LEG 6: a COMPLIANT package folder PASSES the report (exit 0, and the ok: branch is REACHED)"
  else
    bad "LEG 6: a COMPLIANT package folder PASSES the report (exit 0, and the ok: branch is REACHED)" \
        "exit=$rc — THE GATE IS RED ON THE GOOD STATE. A repo that adopts ADR-0032 cannot sync a lock
file, so its Renovate PRs never land (#639). exit=123 means grep's no-match — the SUCCESS signal — is
being read as a failure again: check that the \`grep -lF\` no-match is tolerated where it happens, and
that the \`set\` line names every option the step actually runs with. Output:
$out"
  fi

  # 6b. ...and the gate still BITES. 6a alone is satisfied by a step that always exits 0.
  rc=0; out="$(run_report "$WORK/adr32-regressed")" || rc=$?
  if [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q '::error title=lock file is not machine-independent'; then
    ok "LEG 6: ...and a library-packs resolution still FAILS it (exit $rc, ::error) — #504 intact"
  else
    bad "LEG 6: ...and a library-packs resolution still FAILS it (exit $rc, ::error) — #504 intact" \
        "exit=$rc — a machine-dependent lock file would be COMMITTED (ADR-0032, #504). Output:
$out"
  fi

  # 6c. NO EVIDENCE IS NOT A PASS (epic #266). The step's own subject is the package folder; without
  #     one it cannot see its subject, and a check that cannot fail on its subject is not a check.
  rc=0; out="$(run_report "$WORK/adr32-absent")" || rc=$?
  if [ "$rc" -ne 0 ] && printf '%s\n' "$out" | grep -q 'has no evidence to read'; then
    ok "LEG 6: ...and NO package folder at all FAILS it too — no evidence is not a pass (#266)"
  else
    bad "LEG 6: ...and NO package folder at all FAILS it too — no evidence is not a pass (#266)" \
        "exit=$rc — the gate went GREEN without reading its subject. Output:
$out"
  fi

  # 6d. THE TEMPLATES CASE, and it is NOT the same as 6c. A package folder that EXISTS but holds no
  #     packages is what FS.GG.Templates restores (its lock is `"net10.0": {}`), and lockfile-sync.yml
  #     says in prose that it "resolves nothing and passes trivially" — a claim nothing executed. The
  #     distinction is load-bearing: 6c must fail (no folder = no evidence) while this must pass (a
  #     folder, honestly empty = nothing resolved from library-packs). Anyone hardening 6c's guard into
  #     "at least one .nupkg.metadata or die" would red Templates on every Renovate PR, and without this
  #     leg every other assertion here would stay green while they did it.
  mkdir -p "$WORK/adr32-empty"
  rc=0; out="$(run_report "$WORK/adr32-empty")" || rc=$?
  if [ "$rc" -eq 0 ] && printf '%s\n' "$out" | grep -q '^ok: no package was served by an SDK-bundled folder source\.'; then
    ok "LEG 6: ...and an EMPTY-but-present folder passes trivially — Templates resolves nothing"
  else
    bad "LEG 6: ...and an EMPTY-but-present folder passes trivially — Templates resolves nothing" \
        "exit=$rc — a repo with NO packages cannot sync its lock file. That is FS.GG.Templates, on every
Renovate PR it will ever get. An empty package folder is not missing evidence (6c) — it is evidence of
nothing resolved, which is the ADR-0032 PASS. Output:
$out"
  fi

  # 6e. A BROKEN READ IS NOT A PASS EITHER. A folder whose .nupkg.metadata cannot be read must go RED,
  #     whichever line delivers that — and this leg pins the OUTCOME, deliberately, without asserting
  #     which one does. Worth being straight about what it does and does not prove: it passes against
  #     the `xargs … || true` repair too, because a folder grep cannot read also breaks the `jq`
  #     inventory ABOVE and the step dies there first. So this leg is not the argument for reading
  #     grep's exit code; the argument is that today's fail-closed here is an accident of ORDERING,
  #     owed to a step called "report", and a gate should not depend on an unrelated line running
  #     first. What the leg guarantees is that if anyone ever rearranges that, the red is still red.
  mk_pkgs "$WORK/adr32-unreadable" "/usr/share/dotnet/sdk/10.0.301/FSharp/library-packs"
  chmod 000 "$WORK/adr32-unreadable/fsharp.core/10.1.301/.nupkg.metadata"
  rc=0; out="$(run_report "$WORK/adr32-unreadable")" || rc=$?
  chmod 644 "$WORK/adr32-unreadable/fsharp.core/10.1.301/.nupkg.metadata"   # so the EXIT trap can rm it
  if [ "$rc" -ne 0 ]; then
    ok "LEG 6: ...and a package folder it CANNOT READ fails closed (exit $rc) — not silently 'ok'"
  else
    bad "LEG 6: ...and a package folder it CANNOT READ fails closed (exit $rc) — not silently 'ok'" \
        "THE GATE WENT GREEN WITHOUT READING ITS SUBJECT. A library-packs resolution was sitting in that
folder and the step reported 'ok'. This is the fails-open shape of #266, and the reason grep's exit code
must be read (>=2 = could not see) rather than tolerated wholesale. Output:
$out"
  fi
fi

# ---- LEG 7: the ADR-0032 MECHANISM itself, against the REAL SDK ------------------------------------
# Everything above tests the REPORTER — the step that reads .nupkg.metadata and says which source
# served each package. Every one of those legs FABRICATES its evidence (see mk_pkgs: it writes
# `"contentHash":"h1=="` and a hand-typed `source` string). That is right for testing a reporter, and
# it is why they are fast and hermetic. But it means that until this leg, NOTHING in the org executed
# the thing ADR-0032 actually rests on: that DisableImplicitLibraryPacksFolder REMOVES the SDK's
# bundled folder from the restore source list. The property was asserted in a 50-line comment in
# dist/dotnet/Directory.Build.props and verified by no test (.github#473 step 3, "verify the invariant,
# not the hash").
#
# It is not a documented public MSBuild property — it is a condition inside the SDK's own
# Microsoft.FSharp.NetSdk.targets. So the SDK is free to rename it. If it ever does, EVERY existing
# guard still passes: the drift check passes (the file still contains the string), legs 5/6 pass (they
# never restore), and a dev box still resolves nuget.org (see the mapping note below). The org would
# find out when locked-mode restore went NU1403 across six repos at once, with nothing pointing at the
# SDK. This leg is what turns that into one red assertion naming the cause.
#
# THE FIXTURE HAS NO packageSourceMapping, AND THAT IS THE WHOLE DESIGN. library-packs is injected as
# a RestoreAdditionalProjectSources entry, NOT as a configured source — so under ANY active mapping it
# matches no pattern and serves nothing, and the folder is excluded whether or not the property is set.
# A mapping would therefore make leg 7b pass for a reason that has nothing to do with ADR-0032. That is
# not hypothetical: it is exactly why this bug survived so long. The dev container's user-level
# NuGet.Config has such a mapping, so the defect REPRODUCES ONLY WHERE THERE IS NONE — i.e. in CI — and
# a developer who went looking for it locally found a clean restore and moved on (.github#471). An
# empty <packageSourceMapping/> is the CI configuration, and it is the only one that can see this bug.
# (Note the fixture above deliberately DOES map — it is testing something else. Do not unify them.)
#
# The feed is EMPTY, so the SDK's folder is the only thing that could serve FSharp.Core. That makes the
# observable a plain yes/no — restore succeeds or it cannot find the package — with no contentHash
# anywhere in the assertion. Deliberate: the acceptance is the INVARIANT ("one source, on any machine"),
# not today's hash. A leg pinned to excLf2zM… would go red on the next FSharp.Core bump while the
# invariant it claims to protect still held.
#
# AND THIS IS THE ONE COLD LEG THAT DOES NOT CLEAR THE HTTP CACHE — deliberately, so do not "fix" it.
# The header above says every cold leg clears it, and every leg that can reach a remote feed must. Leg
# 7's only sources are two LOCAL folders (the empty feed, and the SDK's library-packs), and the NuGet
# HTTP cache does not sit in front of a folder source: there is no remote fetch here to serve stale.
# Coldness is structural in this leg rather than enforced, which is why a fresh NUGET_PACKAGES per
# restore is all it takes.
ADR32="$WORK/adr32-mechanism"; mkdir -p "$ADR32/feed" "$ADR32/proj"
cat > "$ADR32/proj/nuget.config" <<'EOF'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources><clear /><add key="empty" value="../feed" /></packageSources>
  <packageSourceMapping><clear /></packageSourceMapping>
</configuration>
EOF
# FSharp.Core is left IMPLICIT — the SDK injects the reference, which is the code path under test.
cat > "$ADR32/proj/probe.fsproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><Compile Include="P.fs" /></ItemGroup>
</Project>
EOF
echo 'module P' > "$ADR32/proj/P.fs"

adr32_restore() {  # adr32_restore <property value>   — 'false' reproduces the SDK's default state
  # Sets ADR32_RC (restore exit code), ADR32_SRC (the source that served FSharp.Core, if any) and
  # ADR32_LEAKED (yes/no — did the SDK's bundled folder serve it).
  #
  # `|| ADR32_RC=$?` rather than a bare call then `$?`: leg 7b's restore is SUPPOSED to fail, and under
  # `set -e` a bare failing restore kills the fixture before the assertion can read the code — the
  # whole leg then vanishes from the output and the suite still exits 0 on the legs that ran.
  #
  # The property is always PASSED, never omitted, so there is no empty array to expand: `"${arr[@]}"`
  # on an empty array is an unbound-variable error under `set -u` before bash 4.4, and a fixture that
  # dies on the runner's bash version rather than on its subject is worse than no fixture. 'false' is
  # not an approximation of unset either — the SDK's own guard is `!= 'true'` (Microsoft.FSharp.NetSdk
  # .targets), so false IS the default code path, exactly.
  local pkgs
  pkgs="$(mktemp -d "$WORK/adr32.XXXXXX")"
  rm -rf "$ADR32/proj/obj"
  ADR32_RC=0
  ( cd "$ADR32/proj" && NUGET_PACKAGES="$pkgs" dotnet restore \
      "-p:DisableImplicitLibraryPacksFolder=$1" >/dev/null 2>&1 ) || ADR32_RC=$?
  ADR32_SRC="$(jq -r .source "$pkgs"/fsharp.core/*/.nupkg.metadata 2>/dev/null || true)"
  # `case`, not `printf | grep -q`: under `set -o pipefail` (line 64) grep -q exits on first match and
  # can SIGPIPE the printf, making the pipeline 141 — a spurious RED in the one suite that has to be
  # believed. A pattern match needs no pipeline and cannot race.
  case "$ADR32_SRC" in *library-packs*) ADR32_LEAKED=yes ;; *) ADR32_LEAKED=no ;; esac
}

# 7a. THE SUBJECT EXISTS. In the SDK's default state the folder really is injected and really does
#     serve FSharp.Core. This is the anti-#266 half of the pair, and it is not ceremony: without it,
#     7b passes vacuously on any machine where library-packs is absent or already excluded — a green
#     tick over a subject that was never there, which is the exact shape of the failure this repo keeps
#     finding. If THIS leg goes red, the SDK stopped injecting the folder and ADR-0032's fix has become
#     a no-op that nothing needs; that is a finding, not a pass.
adr32_restore false
if [ "$ADR32_RC" -eq 0 ] && [ "$ADR32_LEAKED" = yes ]; then
  ok "LEG 7: in the SDK's DEFAULT state it serves FSharp.Core from library-packs — the defect is real"
else
  bad "LEG 7: in the SDK's DEFAULT state it serves FSharp.Core from library-packs — the defect is real" \
      "exit=$ADR32_RC source='$ADR32_SRC' — the SDK did NOT resolve FSharp.Core from its bundled folder, so
this fixture has no subject and leg 7b below proves nothing. Either the SDK stopped injecting
\$(_FSharpCoreLibraryPacksFolder) (check Microsoft.FSharp.NetSdk.targets — ADR-0032 may now be moot), or
a packageSourceMapping leaked in and excluded the folder on its own, in which case 7b is passing for the
wrong reason. Do not silence this leg: it is the one that says the other one means something."
fi

# 7b. AND THE PROPERTY SHUTS IT OFF. Same project, same empty feed, property set: the folder is gone
#     from the source list, so nothing can serve FSharp.Core and the restore fails to find it. This is
#     dist/dotnet/Directory.Build.props's claim, executed — "a restore that can reach no other source
#     fails NU1101 instead of quietly falling back to the SDK copy".
adr32_restore true
if [ "$ADR32_RC" -ne 0 ] && [ "$ADR32_LEAKED" = no ]; then
  ok "LEG 7: ...and DisableImplicitLibraryPacksFolder REMOVES it — the ADR-0032 mechanism works"
else
  bad "LEG 7: ...and DisableImplicitLibraryPacksFolder REMOVES it — the ADR-0032 mechanism works" \
      "exit=$ADR32_RC source='$ADR32_SRC' — the property did NOT exclude the SDK's bundled folder. ADR-0032
rests entirely on it, and it is an SDK-INTERNAL condition, not a documented MSBuild property: the most
likely cause is that the SDK renamed or dropped it. Every other guard in the org is blind to this — the
drift check only proves the string is still in dist/dotnet/Directory.Build.props, and legs 5/6 above
fabricate their evidence rather than restoring. Until this is fixed, every repo's lock file is once
again a function of the SDK patch level, and the divergence will surface as NU1403 on a CORRECT lock
file (.github#429, #471, ADR-0032)."
fi

# 7c. ...AND THE FILE WE ACTUALLY SHIP IS WHAT DOES IT. This is the leg that matters, and it is the
#     reason 7a/7b are not enough on their own: they drive the property with `-p:`, so they prove the
#     SDK HONOURS it — not that the org SENDS it to anyone. Delete the PropertyGroup from
#     dist/dotnet/Directory.Build.props and 7a/7b are still perfectly green.
#
#     A grep for the element would close that hole only against a literal deletion, and it FAILS OPEN
#     against every more interesting way to break this: a `Condition` on the PropertyGroup that
#     evaluates false, a later import that overrides the value, or the block commented out while the
#     element text survives in the prose around it — this file's comments already name the property
#     repeatedly, and grep cannot tell a live PropertyGroup from a quoted one. So this leg does not
#     read the config. It RESTORES UNDER IT, exactly as an adopting repo does: the probe inherits the
#     shipped Directory.Build.props and Directory.Packages.props, and declares FSharp.Core the way
#     every real FS-GG project declares it (an explicit PackageReference; central package management
#     supplies the version, and suppresses the SDK's implicit reference). If the property is set AND
#     live, the SDK's folder is not a source and FSharp.Core cannot be found. Anything that makes it
#     inert — however cleverly — puts library-packs back in the source list and reds this leg.
ADR32C="$WORK/adr32-shipped"; mkdir -p "$ADR32C/feed" "$ADR32C/proj"
cp "$ROOT/dist/dotnet/Directory.Build.props" "$ROOT/dist/dotnet/Directory.Packages.props" "$ADR32C/proj/"
cp "$ADR32/proj/nuget.config" "$ADR32C/proj/nuget.config"
cat > "$ADR32C/proj/probe.fsproj" <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
  <ItemGroup><Compile Include="P.fs" /></ItemGroup>
  <ItemGroup><PackageReference Include="FSharp.Core" /></ItemGroup>
</Project>
EOF
echo 'module P' > "$ADR32C/proj/P.fs"
SHIPPED_PKGS="$(mktemp -d "$WORK/adr32c.XXXXXX")"
SHIPPED_RC=0
( cd "$ADR32C/proj" && NUGET_PACKAGES="$SHIPPED_PKGS" dotnet restore >/dev/null 2>&1 ) || SHIPPED_RC=$?
SHIPPED_SRC="$(jq -r .source "$SHIPPED_PKGS"/fsharp.core/*/.nupkg.metadata 2>/dev/null || true)"
case "$SHIPPED_SRC" in *library-packs*) shipped_leaked=yes ;; *) shipped_leaked=no ;; esac

if [ "$SHIPPED_RC" -ne 0 ] && [ "$shipped_leaked" = no ]; then
  ok "LEG 7: ...and a probe INHERITING the shipped org config excludes it too — the fix is live, not just present"
else
  bad "LEG 7: ...and a probe INHERITING the shipped org config excludes it too — the fix is live, not just present" \
      "exit=$SHIPPED_RC source='$SHIPPED_SRC' — a project that inherits dist/dotnet/Directory.Build.props STILL
resolved FSharp.Core from the SDK's bundled folder. Legs 7a/7b above pass, so the SDK honours the property;
what is broken is our file. Either the PropertyGroup is gone, or it is INERT — a Condition that evaluates
false, an override after the import, or the block commented out. Every repo that syncs this config will
commit a lock file whose contentHash is a function of the SDK patch level, and it will surface as NU1403 on
a CORRECT lock file, six repos away, with nothing naming the cause (ADR-0032, .github#429, #473)."
fi

echo "lockfile-cold fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::lockfile-cold fixture FAILED"; exit 1; }
echo "lockfile-cold fixture — OK"
