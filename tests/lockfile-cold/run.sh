#!/usr/bin/env bash
# Fixture for the COLD locked restore — the failure-leg test ADR-0031 and #429 have been missing.
#
# WHY THIS EXISTS. ADR-0031 ratifies that any restore which WRITES (`--force-evaluate`) or ENFORCES
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
# fail-open (leg 3 proves it), and a cold restore IS the fix for that — ADR-0031 §Decision 1 stands.
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
#
# Legs 2 and 3 are the pair. Leg 2 alone would be satisfied by a gate that is merely broken; leg 3
# shows the SAME lock file passing the moment the restore stops being cold, which is the defect.
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
NuGet's behaviour may have changed — if warm validation now consults the feed, say so in ADR-0031
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
           "the mktemp -d global-packages folder is gone — the restore is warm again (ADR-0031)"
  grep -qE 'dotnet nuget locals http-cache --clear' "$SYNC_WF" \
    && ok "LEG 4: ...and CLEARS THE HTTP CACHE (relocating NUGET_PACKAGES alone is not cold)" \
    || bad "LEG 4: ...and CLEARS THE HTTP CACHE (relocating NUGET_PACKAGES alone is not cold)" \
           "without this the HTTP cache serves stale .nupkg bytes — the exact trap #429 hit"
  if grep -qE '^\s*cache:\s*true' "$SYNC_WF"; then
    bad "LEG 4: ...and does NOT re-enable setup-dotnet's package cache" \
        "'cache: true' is back in lockfile-sync.yml — that restores a warm package folder and undoes ADR-0031"
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

echo "lockfile-cold fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::lockfile-cold fixture FAILED"; exit 1; }
echo "lockfile-cold fixture — OK"
