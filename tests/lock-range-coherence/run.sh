#!/usr/bin/env bash
# Fixture for scripts/check-lock-ranges.py — the gate that asserts every project-reference range in the
# committed locks tracks the version the referenced project declares (.github#495, epic #266).
#
# The gate exists because `--locked-mode` structurally CANNOT see this field: a release bump that edits
# a version without regenerating the locks leaves every sibling project reference recording the old
# version, and every gate stays green. FS.GG.Audio carried six stale ranges through its whole v0.2.0
# cycle; FS.GG.Game's main has carried one since `release: v0.4.0` (Game#128) — a commit that bumped
# <Version> and touched no lock file at all.
#
# So this fixture spends most of its length on the FAILURE legs. It proves the gate goes red on an
# un-regenerated bump, on a single reintroduced stale range, on a version it cannot resolve, on a range
# shape it does not recognise, on a lock set with no project entries, on a repo with no locks and on one
# with no projects — and that it does NOT go red on a `+ci` build-metadata bump or on a consumption pin
# for a package the repo does not build (the two ways a guard like this cries wolf and gets deleted).
#
# Every negative leg asserts the REASON, not merely a non-zero exit — the epic #266 vacuous-failure
# defect is a "must fail" test whose non-zero exit comes from a path guard rather than from the thing
# under test. `must_fail` therefore takes a REQUIRED pattern.
#
# The three receivers declare a project's version three different ways, and a gate that only understood
# Audio's would be a gate only Audio could call. Each shape gets its own leg:
#
#   audio-shape       <Version>$(FsGgAudioVersion)</Version>   — an MSBuild property, centrally declared
#   rendering-shape   <Version>0.4.0-preview.1</Version>       — a per-project literal, prerelease
#   game-shape        (absent)                                 — inherited from Directory.Build.local.props
#
# Throwaway git repos under a temp dir; no network, no SDK, no restore. Each case builds a real git repo
# because the gate enumerates with `git ls-files` — the repo's own answer to what is in it.
# Mirrors tests/pin-coherence/run.sh.

set -euo pipefail

export PYTHONDONTWRITEBYTECODE=1

HERE="$(cd "$(dirname "$0")" && pwd)"
REPO_ROOT="$(cd "$HERE/../.." && pwd)"
GATE="$REPO_ROOT/scripts/check-lock-ranges.py"

[ -f "$GATE" ] || { echo "FAIL  gate not found at $GATE"; exit 1; }

WORK="$(mktemp -d "${TMPDIR:-/tmp}/lock-range-fixture.XXXXXX")"
trap 'rm -rf "$WORK"' EXIT

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# must_pass <name> <root> [args…]
must_pass() {
  local n="$1" root="$2"; shift 2
  local out rc=0
  out="$(python3 "$GATE" --root "$root" "$@" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then ok "$n"; else bad "$n (expected exit 0, got $rc)" "$out"; fi
}

# must_fail <name> <required-pattern> <root> [args…] — a non-zero exit alone does NOT prove the gate
# failed for the reason claimed, so the reason is asserted too.
must_fail() {
  local n="$1" pat="$2" root="$3"; shift 3
  local out rc=0
  out="$(python3 "$GATE" --root "$root" "$@" 2>&1)" || rc=$?
  if [ "$rc" -eq 0 ]; then
    bad "$n (expected a non-zero exit, got 0)" "$out"
  elif printf '%s' "$out" | grep -qF -- "$pat"; then
    ok "$n"
  else
    bad "$n (failed, but not for the claimed reason: no '$pat')" "$out"
  fi
}

# must_say <name> <pattern> <root> [args…] — a PASSING run whose output makes a specific claim.
must_say() {
  local n="$1" pat="$2" root="$3"; shift 3
  local out rc=0
  out="$(python3 "$GATE" --root "$root" "$@" 2>&1)" || rc=$?
  if [ "$rc" -ne 0 ]; then bad "$n (expected exit 0, got $rc)" "$out"
  elif printf '%s' "$out" | grep -qF -- "$pat"; then ok "$n"
  else bad "$n (passed, but did not report '$pat')" "$out"; fi
}

# ---- repo builders --------------------------------------------------------------------------------
# `w <root> <path> <<<content` writes a file and creates its parent. Nothing is committed until `seal`,
# which stages everything — the gate reads `git ls-files`, so an unstaged file is invisible to it, and
# that is the point: the gate checks what the repo COMMITTED, not what is lying in the tree.
w() { local root="$1" rel="$2"; mkdir -p "$root/$(dirname "$rel")"; cat > "$root/$rel"; }

newrepo() {
  local d="$WORK/$1"; mkdir -p "$d"
  git -C "$d" init -q -b main
  git -C "$d" config user.email fixture@fs.gg
  git -C "$d" config user.name fixture
  printf '%s' "$d"
}
seal() { git -C "$1" add -A; git -C "$1" -c commit.gpgsign=false commit -qm fixture; }

# A lock recording ONE project-reference range: <holder> -> <dep> : <range>.
lockfile() {
  local root="$1" rel="$2" holder="$3" dep="$4" range="$5"
  w "$root" "$rel" <<EOF
{
  "version": 2,
  "dependencies": {
    "net10.0": {
      "$holder": {
        "type": "Project",
        "dependencies": {
          "$dep": "$range",
          "FSharp.Core": "[10.1.301, )"
        }
      }
    }
  }
}
EOF
}

# ---- the three receiver shapes, each coherent -----------------------------------------------------

# AUDIO SHAPE: one central MSBuild property, referenced by every project. This is the shape Audio#52's
# original guard hard-coded — the gate must still handle it, but must not REQUIRE it.
audio="$(newrepo audio)"
w "$audio" Directory.Packages.props <<'EOF'
<Project>
  <PropertyGroup>
    <FsGgAudioVersion>0.2.0</FsGgAudioVersion>
  </PropertyGroup>
</Project>
EOF
w "$audio" src/Core/FS.GG.Audio.Core.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>FS.GG.Audio.Core</PackageId>
    <Version>$(FsGgAudioVersion)</Version>
  </PropertyGroup>
</Project>
EOF
w "$audio" src/Engine/FS.GG.Audio.Engine.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>FS.GG.Audio.Engine</PackageId>
    <Version>$(FsGgAudioVersion)</Version>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Core\FS.GG.Audio.Core.fsproj" />
  </ItemGroup>
</Project>
EOF
lockfile "$audio" tests/Engine.Tests/packages.lock.json fs.gg.audio.engine FS.GG.Audio.Core "[0.2.0, )"
seal "$audio"
must_pass "audio shape: \$(FsGgAudioVersion) resolves, range tracks it" "$audio"

# RENDERING SHAPE: a per-project literal, prerelease. There is no central version property at all — a
# gate keyed on one would find nothing here and would have to either red a healthy repo or pass vacuously.
rendering="$(newrepo rendering)"
w "$rendering" Directory.Build.local.props <<'EOF'
<Project>
  <PropertyGroup Label="Package">
    <Version>0.1.0-preview.1</Version>
  </PropertyGroup>
</Project>
EOF
w "$rendering" src/Scene/Scene.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>FS.GG.UI.Scene</PackageId>
    <Version>0.4.0-preview.1</Version>
  </PropertyGroup>
</Project>
EOF
w "$rendering" src/Canvas/Canvas.Lib.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>FS.GG.UI.Canvas</PackageId>
    <Version>0.4.0-preview.1</Version>
  </PropertyGroup>
</Project>
EOF
lockfile "$rendering" samples/CanvasDemo/packages.lock.json fs.gg.ui.canvas FS.GG.UI.Scene "[0.4.0-preview.1, )"
seal "$rendering"
must_pass "rendering shape: per-project literal, prerelease range" "$rendering"
# The project's own <Version> must WIN over the inherited Directory.Build.local.props default. If the
# inherited 0.1.0-preview.1 won, this repo would red — and Rendering's 196 real ranges with it.
must_say "rendering shape: the project's own <Version> beats the inherited default" "all 1" "$rendering"

# GAME SHAPE: no <Version> in the project at all — inherited from Directory.Build.local.props. This is
# the shape whose real repo is BROKEN today (Game#128 bumped the version and regenerated no lock).
game="$(newrepo game)"
w "$game" Directory.Build.local.props <<'EOF'
<Project>
  <PropertyGroup Label="Package">
    <Version>0.4.0</Version>
  </PropertyGroup>
</Project>
EOF
w "$game" src/Game.Core/FS.GG.Game.Core.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>FS.GG.Game.Core</PackageId>
  </PropertyGroup>
</Project>
EOF
w "$game" src/Game.Render/FS.GG.Game.Render.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>FS.GG.Game.Render</PackageId>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Game.Core\FS.GG.Game.Core.fsproj" />
  </ItemGroup>
</Project>
EOF
lockfile "$game" tests/Game.Render.Tests/packages.lock.json fs.gg.game.render FS.GG.Game.Core "[0.4.0, )"
seal "$game"
must_pass "game shape: version inherited from Directory.Build.local.props" "$game"

# ---- THE DEFECT: the bump that regenerated no lock ------------------------------------------------
# This is Game#128 and Audio 2c208f3, reproduced exactly: edit the version, leave the locks alone.
stale="$(newrepo stale)"
cp -r "$game/." "$stale/" 2>/dev/null || true
rm -rf "$stale/.git"; git -C "$stale" init -q -b main
git -C "$stale" config user.email fixture@fs.gg; git -C "$stale" config user.name fixture
w "$stale" Directory.Build.local.props <<'EOF'
<Project>
  <PropertyGroup Label="Package">
    <Version>0.5.0</Version>
  </PropertyGroup>
</Project>
EOF
seal "$stale"
must_fail "the un-regenerated bump is CAUGHT (0.4.0 lock vs 0.5.0 declared)" \
  "do not match the version the referenced project declares" "$stale"
must_fail "…and it NAMES the lock file it found the stale range in" \
  "tests/Game.Render.Tests/packages.lock.json" "$stale"
must_fail "…and it names BOTH versions, so the fix is obvious from the log" \
  "[0.4.0, )  (project declares [0.5.0, ))" "$stale"

# ---- +ci is NOT a bypass, and NOT a false positive ------------------------------------------------
# SemVer2 build metadata is not part of NuGet version identity, so a declared `0.2.0+ci` is written to
# the lock as `[0.2.0, )`. A raw string compare would red a perfectly correct bump — and a guard that
# cries wolf gets deleted. But the metadata must not launder a REAL mismatch either.
meta="$(newrepo meta)"
cp -r "$audio/." "$meta/" 2>/dev/null || true
rm -rf "$meta/.git"; git -C "$meta" init -q -b main
git -C "$meta" config user.email fixture@fs.gg; git -C "$meta" config user.name fixture
w "$meta" Directory.Packages.props <<'EOF'
<Project>
  <PropertyGroup>
    <FsGgAudioVersion>0.2.0+ci.42</FsGgAudioVersion>
  </PropertyGroup>
</Project>
EOF
seal "$meta"
must_pass "+ci build metadata is dropped, as NuGet drops it — 0.2.0+ci.42 matches [0.2.0, )" "$meta"

metabad="$(newrepo metabad)"
cp -r "$meta/." "$metabad/" 2>/dev/null || true
rm -rf "$metabad/.git"; git -C "$metabad" init -q -b main
git -C "$metabad" config user.email fixture@fs.gg; git -C "$metabad" config user.name fixture
w "$metabad" Directory.Packages.props <<'EOF'
<Project>
  <PropertyGroup>
    <FsGgAudioVersion>0.3.0+ci.42</FsGgAudioVersion>
  </PropertyGroup>
</Project>
EOF
seal "$metabad"
must_fail "…but +ci does not LAUNDER a real mismatch (0.3.0+ci vs a 0.2.0 lock)" \
  "do not match the version" "$metabad"

# ---- a consumption pin is NOT a project reference --------------------------------------------------
# Rendering's locks record `FS.GG.Audio.Core: [0.1.0, )` — the Audio PACKAGE, which Rendering consumes
# and does not build. Its currency is a different question with a different answer (#263 / pin-coherence),
# and flagging it here would be a false positive on every consumer in the org. The gate keys on the
# packages THIS repo's projects PRODUCE, which draws the line exactly.
consume="$(newrepo consume)"
cp -r "$rendering/." "$consume/" 2>/dev/null || true
rm -rf "$consume/.git"; git -C "$consume" init -q -b main
git -C "$consume" config user.email fixture@fs.gg; git -C "$consume" config user.name fixture
w "$consume" samples/CanvasDemo/packages.lock.json <<'EOF'
{
  "version": 2,
  "dependencies": {
    "net10.0": {
      "fs.gg.ui.canvas": {
        "type": "Project",
        "dependencies": {
          "FS.GG.UI.Scene": "[0.4.0-preview.1, )",
          "FS.GG.Audio.Core": "[0.1.0, )",
          "FSharp.Core": "[10.1.301, )"
        }
      }
    }
  }
}
EOF
seal "$consume"
must_pass "a consumption pin for a package this repo does not BUILD is ignored" "$consume"
must_say "…and it is not silently counted as a checked range either" "all 1" "$consume"

# ---- FAIL CLOSED (epic #266): nothing-to-check must never share an exit code with all-is-well -------

# Zero `"type": "Project"` entries — the lock schema moved, or the query stopped matching. Every range
# below would be vacuously fine, which is precisely the silent no-op this org keeps re-finding.
drift="$(newrepo drift)"
cp -r "$audio/." "$drift/" 2>/dev/null || true
rm -rf "$drift/.git"; git -C "$drift" init -q -b main
git -C "$drift" config user.email fixture@fs.gg; git -C "$drift" config user.name fixture
w "$drift" tests/Engine.Tests/packages.lock.json <<'EOF'
{
  "version": 2,
  "dependencies": {
    "net10.0": {
      "FSharp.Core": { "type": "Direct", "requested": "[10.1.301, )", "resolved": "10.1.301" }
    }
  }
}
EOF
seal "$drift"
must_fail "FAIL CLOSED: zero project entries is a schema drift, not a pass" \
  "ZERO" "$drift"

# A repo whose projects genuinely do not cross-reference (FS.GG.Game before Game.Render existed). Zero
# is legitimate — but only as a DECLARED decision, so the default still fails and --min-ranges 0 is the
# opt-in. The `"type": "Project"` entry is still present, so this is not the drift case above.
lonely="$(newrepo lonely)"
w "$lonely" Directory.Build.local.props <<'EOF'
<Project>
  <PropertyGroup><Version>0.4.0</Version></PropertyGroup>
</Project>
EOF
w "$lonely" src/Core/FS.GG.Solo.Core.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><PackageId>FS.GG.Solo.Core</PackageId></PropertyGroup>
</Project>
EOF
w "$lonely" tests/Core.Tests/packages.lock.json <<'EOF'
{
  "version": 2,
  "dependencies": {
    "net10.0": {
      "fs.gg.solo.core": {
        "type": "Project",
        "dependencies": { "FSharp.Core": "[10.1.301, )" }
      }
    }
  }
}
EOF
seal "$lonely"
must_fail "FAIL CLOSED: no sibling ranges reds by DEFAULT (min-ranges 1)" \
  "fewer than the 1 this repo declares" "$lonely"
must_pass "…and passes only when the caller DECLARES zero with --min-ranges 0" "$lonely" --min-ranges 0

# A version the gate cannot resolve. It exists to enforce a version; guessing one would be worse than
# any bug it catches.
unres="$(newrepo unres)"
w "$unres" src/Core/FS.GG.X.Core.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackageId>FS.GG.X.Core</PackageId>
    <Version>$(SomePropertyNobodyDefined)</Version>
  </PropertyGroup>
</Project>
EOF
w "$unres" src/App/FS.GG.X.App.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><PackageId>FS.GG.X.App</PackageId><Version>1.0.0</Version></PropertyGroup>
</Project>
EOF
lockfile "$unres" tests/App.Tests/packages.lock.json fs.gg.x.app FS.GG.X.Core "[1.0.0, )"
seal "$unres"
must_fail "FAIL CLOSED: an unresolvable \$(Prop) version is never guessed" \
  "does not resolve statically" "$unres"

# An unrecognised range shape. NuGet writes `[v, )` for a project reference; anything else means our
# understanding of the format is wrong, and an unrecognised shape is not a passing one.
shape="$(newrepo shape)"
cp -r "$audio/." "$shape/" 2>/dev/null || true
rm -rf "$shape/.git"; git -C "$shape" init -q -b main
git -C "$shape" config user.email fixture@fs.gg; git -C "$shape" config user.name fixture
lockfile "$shape" tests/Engine.Tests/packages.lock.json fs.gg.audio.engine FS.GG.Audio.Core "0.2.0"
seal "$shape"
must_fail "FAIL CLOSED: an unrecognised range shape is not a passing one" \
  "unrecognised range" "$shape"

# No locks at all — this org pins its restores (ADR-0006), so a repo with none is misconfigured, and a
# gate that checks zero lock files is not a gate.
nolocks="$(newrepo nolocks)"
w "$nolocks" src/Core/FS.GG.Y.Core.fsproj <<'EOF'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><PackageId>FS.GG.Y.Core</PackageId><Version>1.0.0</Version></PropertyGroup>
</Project>
EOF
seal "$nolocks"
must_fail "FAIL CLOSED: no committed packages.lock.json" "no committed packages.lock.json" "$nolocks"

# No projects — there is no version-of-truth to check anything against.
noproj="$(newrepo noproj)"
lockfile "$noproj" tests/T/packages.lock.json fs.gg.z.core FS.GG.Z.Other "[1.0.0, )"
seal "$noproj"
must_fail "FAIL CLOSED: no project files" "no project files" "$noproj"

# The gate must not be talked into inspecting a negative number of things.
must_fail "a negative --min-ranges is refused" "cannot be negative" "$audio" --min-ranges -1

# ---- what the gate considers part of the repo ------------------------------------------------------
# TRACKED paths, at their CURRENT content. Both halves are deliberate.
#
# Tracked, because `git ls-files` is the repo's own answer to "what is in this repo" — an untracked lock
# under someone's scratch directory, or a lock emitted into an ignored build output, is not the repo's
# and must not red its gate.
untracked="$(newrepo untracked)"
cp -r "$audio/." "$untracked/" 2>/dev/null || true
rm -rf "$untracked/.git"; git -C "$untracked" init -q -b main
git -C "$untracked" config user.email fixture@fs.gg; git -C "$untracked" config user.name fixture
seal "$untracked"
lockfile "$untracked" scratch/Stray/packages.lock.json fs.gg.audio.engine FS.GG.Audio.Core "[0.1.0, )"
must_pass "an UNTRACKED lock file is not part of the repo, and is not inspected" "$untracked"

# Current content, because the gate must be usable BEFORE the commit: a developer who regenerates the
# locks runs this and sees green without having to commit first. There is no gap in CI, which checks out
# the commit — so the working tree it reads IS the commit under test.
regenerated="$(newrepo regenerated)"
cp -r "$stale/." "$regenerated/" 2>/dev/null || true
rm -rf "$regenerated/.git"; git -C "$regenerated" init -q -b main
git -C "$regenerated" config user.email fixture@fs.gg; git -C "$regenerated" config user.name fixture
seal "$regenerated"
must_fail "the stale tree reds…" "do not match the version" "$regenerated"
lockfile "$regenerated" tests/Game.Render.Tests/packages.lock.json fs.gg.game.render FS.GG.Game.Core "[0.5.0, )"
must_pass "…and regenerating the lock clears it WITHOUT a commit — the gate is usable pre-commit" "$regenerated"

echo
echo "lock-range-coherence fixture: $pass passed, $failcount failed."
[ "$failcount" -eq 0 ] || exit 1
