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

# --- the kit-pin freshness sweep's world (#1540) -------------------------------------------------
#
# A LOCAL NuGet flat-container, served over file://, standing in for api.nuget.org. This is NOT a
# mock of the sweep's feed reader: `fsgg_feed.nuget_org_versions` builds `<base>/<id>/index.json`,
# fetches it with urllib and parses it, and every one of those steps runs here exactly as it does in
# CI. Only the base URL differs — so an error in the URL shape, the JSON shape, the prerelease
# filter or the version ORDER is caught by this fixture rather than in production.
#
# The version list is deliberately NOT sorted: nuget.org returns creation order, and `newest()`
# exists because of that. A fixture that pre-sorted its feed would let a broken comparand pass.
KITFEED="$WORK/feed"; mkdir -p "$KITFEED/fs.gg.kit"
cat > "$KITFEED/fs.gg.kit/index.json" <<'JSON'
{"versions": ["0.1.0", "0.2.0", "0.10.0-preview.1", "0.8.0", "0.6.0", "0.7.0"]}
JSON
export FSGG_NUGET_ORG_BASE="file://$KITFEED"
# 0.8.0 is the newest STABLE above — 0.10.0-preview.1 sorts higher numerically and must be excluded
# as a prerelease, which is the second thing this feed shape pins.
KIT_PUBLISHED="0.8.0"

# The pin every repo is served unless a leg says otherwise, so the legs that predate this sweep and
# have nothing to do with pins stay green. See the `pinlocal` arm of the gh stub.
mkpin() { cat > "$2" <<XML
<Project>
  <ItemGroup>
    <PackageVersion Include="FS.GG.Kit" Version="$1" />
    <PackageVersion Include="FSharp.Core" Version="9.0.100" />
  </ItemGroup>
</Project>
XML
}
mkpin "$KIT_PUBLISHED" "$FIX/_default.pin"

# --- the roster's git trees (#1556) --------------------------------------------------------------
#
# What `GET /repos/{o}/{r}/git/trees/HEAD?recursive=1` returns, so rule (4) — *does this pattern
# select any tracked path?* — can be asked about a repository whose checkout the audit does not hold.
#
# The shape is the API's, not a convenience: `tree` entries alongside `blob` entries, because the
# real endpoint emits both and the audit must drop the directories. A fixture that listed only blobs
# would be green whether or not the audit filtered them — and if it did not, a cone-mode FILE name
# would be answered by the `tree` entry for the directory of that name and the finding would vanish,
# which is the one thing rule (4) is carrying alone in cone mode.
#
# `src/FS.GG.Contracts/` and `scripts/` are real directories here; `scripts/check-foo.py` and
# `src/FS.GG.Contracts/Contracts.fs` are files. That is what lets one roster express both sides of
# the pair: a cone-mode fetch of the directory is correct, and of the file is the defect.
mktree() { cat > "$2" <<JSON
{"sha": "0000000000000000000000000000000000000000", "truncated": $1, "tree": [
  {"path": "scripts", "type": "tree", "mode": "040000"},
  {"path": "scripts/check-foo.py", "type": "blob", "mode": "100755"},
  {"path": "src", "type": "tree", "mode": "040000"},
  {"path": "src/FS.GG.Contracts", "type": "tree", "mode": "040000"},
  {"path": "src/FS.GG.Contracts/Contracts.fs", "type": "blob", "mode": "100644"}
]}
JSON
}
mktree false "$FIX/_default.tree"

# pin <repo> <version> [local|root]  — this repo pins the kit in a CPM props file, at <version>.
pin() { local slug="${1//\//__}" file="Directory.Packages.${3:-local}.props"
        [ "${3:-local}" = root ] && file="Directory.Packages.props"
        mkdir -p "$FIX/$slug"; mkpin "$2" "$FIX/$slug/$file"; }
# pin_inline <repo> <version> — the FS.GG.Templates shape: the version rides the PackageReference in
# the receiver project and there is NO CPM props file at all. `.receiver.proj.pinned` tells the stub
# to 404 the props reads, which is what that repo really does.
pin_inline() { local slug="${1//\//__}"; mkdir -p "$FIX/$slug"; : > "$FIX/$slug/receiver.proj.pinned"
               cat > "$FIX/$slug/receiver.proj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="FS.GG.Kit" Version="$2" />
  </ItemGroup>
</Project>
XML
}
# nopin <repo> — the receiver pins the kit NOWHERE this sweep knows to look.
nopin() { local slug="${1//\//__}"; mkdir -p "$FIX/$slug"
          rm -f "$FIX/$slug/Directory.Packages.local.props" "$FIX/$slug/Directory.Packages.props"
          : > "$FIX/$slug.nopin"; }
# unpin <repo> — back to the default (current) pin.
unpin() { local slug="${1//\//__}"
          rm -f "$FIX/$slug.nopin" "$FIX/$slug/receiver.proj.pinned" \
                "$FIX/$slug/Directory.Packages.local.props" "$FIX/$slug/Directory.Packages.props"; }

pass=0; failcount=0
ok()  { echo "PASS  $1"; pass=$((pass+1)); }
bad() { echo "FAIL  $1"; [ -n "${2:-}" ] && printf '%s\n' "$2" | sed 's/^/    | /'; failcount=$((failcount+1)); }

# A roster with two coordination-kit receivers; the fixture toggles which are wired via $FIX.
#
# The audit reads its mandate from `capabilities:` (#503), so a fixture roster has to declare one —
# and declaring a capability it roster no receivers for is now a hard failure, which is the whole
# point. The legs under "every capability is audited on its own" build their own rosters to exercise
# exactly that; this base one stays minimal and coherent: one capability, two receivers.
#
# `labels` MUST be declared too, and that is not bookkeeping — it is #628. Every roster here rosters
# `receives: [labels, …]`, and until #628 there was no `capabilities:` row for it anywhere, in the
# fixture OR in the real registry. So the fixture modelled, faithfully and unknowingly, the exact
# defect: a capability that is legal to receive and impossible to detect, swept in neither direction.
# It is now a hard failure, so every roster in this file has to say how `labels` is verified — and the
# honest answer is that it ISN'T, at the receiver, because the authority PUSHES it (apply-labels.sh
# reads the roster and creates the labels via the API). `push: true` is how a roster says that out
# loud, and `repos.sh validate` refuses it without a reason.
LABELS_CAP='  - { id: labels, push: true, reason: authority-pushed by apply-labels.sh; nothing is wired at the receiver }'

mkreg() { cat > "$1" <<YAML
schemaVersion: 5
updated: 2026-07-13
authority: FS-GG/.github
repos:
  - { id: .github,   full: FS-GG/.github,         role: authority, receives: [labels] }
  - { id: sdd,       full: FS-GG/FS.GG.SDD,       role: framework, receives: [labels, coordination-kit], kit-delivery: package }
  - { id: rendering, full: FS-GG/FS.GG.Rendering, role: framework, receives: [labels, coordination-kit], kit-delivery: package }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml }
$LABELS_CAP
YAML
}
# `kit-delivery: package` is on both rows because that is what the real roster says of all seven
# receivers, and because the kit-pin freshness sweep (#1540) grades PACKAGE receivers only — absent
# means byte-copy, which has no pin to grade. A fixture roster that omitted it would silently
# exclude both receivers from that sweep and every kit-pin leg would pass over an empty set.
REG="$WORK/repos.yml"; mkreg "$REG"

# gh stub: `list` (dir) prints filenames from $FIX/<slug>.list; workflow raw reads print
# $FIX/<slug>/<file>; receiver-project reads print $FIX/<slug>/receiver.proj.
# slug = repo full with '/' -> '__'.
#
# The stub FAILS like gh does, because the bug under test (#320) lives entirely in how the audit reads
# a failure. A stub that always exits 0 cannot even express "unreachable", so the old one silently
# modelled a missing workflows dir as an empty-but-successful listing — the exact conflation the audit
# was making. Now: no list file => `Not Found (HTTP 404)` on stderr, exit 1, like the real API.
#   $FIX/<slug>.fail       an HTTP status every call for that repo fails with
#   $FIX/<slug>.failtimes  countdown, so a transient class recovers on a later attempt (retry test)
#   $FIX/<slug>.failfile   only *file* reads fail; the directory still lists
#   $FIX/<slug>.failreceiver only the package receiver-project read fails
#   $FIX/<slug>.gone       the repo itself 404s — private, renamed, or deleted
#   $FIX/<slug>.failtree   only the git TREE read fails (#1556); everything else still answers
#   $FIX/<slug>.tree       the git tree JSON this repo serves (default: $FIX/_default.tree)
#
# EVERY CALL IS LOGGED when $GH_CALL_LOG names a file. #1556 criterion 5 is a claim about API TRAFFIC
# — "no extra call when no step fetches a non-authority repo" — and a claim about traffic cannot be
# checked by reading the audit's output. It is checked by counting what the stub was asked for.
cat > "$STUB/gh" <<'STUB'
#!/usr/bin/env bash
set -uo pipefail
# args: api [-H ...] <path> [--jq ...]
path=""; n=$#; args=("$@")
for ((i=1;i<n;i++)); do case "${args[i]}" in repos/*) path="${args[i]}";; esac; done
# Four request kinds: the repo probe, the workflows dir, one workflow file, and the package receiver
# project used by the materializer detector.
case "$path" in
  */contents/.github/workflows)   kind=list; repo="${path#repos/}"; repo="${repo%%/contents/*}" ;;
  */contents/.github/workflows/*) kind=file; repo="${path#repos/}"; repo="${repo%%/contents/*}"
                                  file="${path##*/contents/.github/workflows/}" ;;
  */contents/.config/kit/FS.GG.Kit.receiver.proj)
                                  kind=receiver; repo="${path#repos/}"; repo="${repo%%/contents/*}" ;;
  # The kit-pin freshness sweep's other two candidate pin shapes (#1540). Served like any other
  # content read, so a repo that does not pin in a given shape 404s exactly as the real API does —
  # which is the ANSWER "this repo does not pin here", not a failure.
  */contents/Directory.Packages.local.props)
                                  kind=pinlocal; repo="${path#repos/}"; repo="${repo%%/contents/*}" ;;
  */contents/Directory.Packages.props)
                                  kind=pinroot; repo="${path#repos/}"; repo="${repo%%/contents/*}" ;;
  # The git TREE endpoint (#1556) — how rule (4) reaches a repository whose checkout the audit does
  # not hold. The real path carries a query string (`?recursive=1`), which is part of the request the
  # audit makes and is therefore matched here rather than stripped: a call that forgot `recursive=1`
  # would fetch only the root directory, and every nested pattern would read as selecting nothing.
  */git/trees/*recursive=1)       kind=tree; repo="${path#repos/}"; repo="${repo%%/git/trees/*}" ;;
  *)                              kind=repo; repo="${path#repos/}" ;;
esac
slug="${repo//\//__}"
[ -n "${GH_CALL_LOG:-}" ] && printf '%s\t%s\n' "$kind" "$repo" >> "$GH_CALL_LOG"

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
{ [ "$kind" = file ] || [ "$kind" = receiver ]; } \
  && [ -f "$FIX/$slug.failfile" ] && apifail 403
[ "$kind" = receiver ] && [ -f "$FIX/$slug.failreceiver" ] && apifail 403
# Only the pin reads fail: lets a fixture prove an unreadable pin file is an UNDETERMINED run and
# never a fabricated "this receiver is current".
{ [ "$kind" = pinlocal ] || [ "$kind" = pinroot ] || [ "$kind" = receiver ]; } \
  && [ -f "$FIX/$slug.failpin" ] && apifail 403
# Only the PROPS reads fail; the receiver project still reads. That is the PARTIAL read — the repo
# looks answerable on the evidence we got, and is not.
{ [ "$kind" = pinlocal ] || [ "$kind" = pinroot ]; } \
  && [ -f "$FIX/$slug.failpinprops" ] && apifail 403
# Only the TREE read fails: the repo still lists and its workflows still read, so the audit gets all
# the way to grading a real cross-repo checkout and THEN loses the index it needs (#1556 criterion 2).
[ "$kind" = tree ] && [ -f "$FIX/$slug.failtree" ] && apifail 403

case "$kind" in
  repo) [ -f "$FIX/$slug.gone" ] && notfound   # invisible to this token: the API says 404, not 403
        echo "$repo" ;;                        # stands in for `--jq '.full_name'`
  list) [ -f "$FIX/$slug.list" ] || notfound   # no workflows dir at all — the real API 404s here
        cat "$FIX/$slug.list" ;;
  file) [ -f "$FIX/$slug/$file" ] || notfound
        cat "$FIX/$slug/$file" ;;
  receiver) [ -f "$FIX/$slug/receiver.proj" ] || notfound
            cat "$FIX/$slug/receiver.proj" ;;
  # A repo with no explicit pin fixture is served the DEFAULT current pin, so the ~60 legs that
  # predate the kit-pin sweep and care nothing about pins stay green without each having to declare
  # one. `<slug>.nopin` suppresses the fallback — that is how a leg models a receiver that pins
  # nowhere, which must be a REFUSAL and not a pass.
  pinlocal) if [ -f "$FIX/$slug/Directory.Packages.local.props" ]; then
              cat "$FIX/$slug/Directory.Packages.local.props"
            elif [ -f "$FIX/$slug.nopin" ] || [ -f "$FIX/$slug/receiver.proj.pinned" ]; then notfound
            else cat "$FIX/_default.pin"; fi ;;
  pinroot)  [ -f "$FIX/$slug/Directory.Packages.props" ] || notfound
            cat "$FIX/$slug/Directory.Packages.props" ;;
  # A repo with no explicit tree fixture serves the DEFAULT one, for the same reason the default pin
  # exists: the ~130 legs that predate #1556 must not each have to declare a tree. `<slug>.gonetree`
  # suppresses it — that is how a leg models a repository whose tree the API says is not there.
  tree)     [ -f "$FIX/$slug.gonetree" ] && notfound
            if [ -f "$FIX/$slug.tree" ]; then cat "$FIX/$slug.tree"; else cat "$FIX/_default.tree"; fi ;;
esac
STUB
chmod +x "$STUB/gh"

# Helpers to shape a repo's workflows in the stub. Each clears any injected failure first, so a
# fixture step never inherits the previous step's outage.
# It clears the PIN shaping too (#1540), for the same reason: a leg that pinned a repo stale must not
# leave it stale for the next leg, which would turn one deliberate finding into a run-wide exit 1.
# So `pin`/`pin_inline`/`nopin` are called AFTER the wire helper, never before.
# `receiver.proj` is cleared here too, and that matters beyond tidiness: it is BOTH the materializer
# detector's evidence and (in the Templates shape) a pin file, so a leg that left one behind would
# contribute a second, contradictory version literal to the next leg and turn a deliberate finding
# into a refusal. Every writer of it — wire_materializer, pin_inline — runs after this.
# The TREE shaping is cleared here too (#1556), for exactly the reason the pin shaping is: a leg that
# made a repository's git tree unreadable must not leave it unreadable for the next leg, which would
# turn one deliberate no-verdict into a run-wide exit 2 and mask whatever that leg was really about.
clearfail(){ local slug="${1//\//__}"; rm -f "$FIX/$slug.fail" "$FIX/$slug.failtimes" "$FIX/$slug.failfile" "$FIX/$slug.failreceiver" "$FIX/$slug.failpin" "$FIX/$slug.failpinprops" "$FIX/$slug.gone" "$FIX/$slug.nopin" "$FIX/$slug.failtree" "$FIX/$slug.gonetree" "$FIX/$slug.tree" "$FIX/$slug/receiver.proj" "$FIX/$slug/receiver.proj.pinned" "$FIX/$slug/Directory.Packages.local.props" "$FIX/$slug/Directory.Packages.props"; }
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
# wire_script <repo> <script-ref> [--no-provenance] — the repo INLINES a job that runs one of the
# authority's scripts, which is how a `script:` capability is really wired (#628): there is no reusable
# workflow to `uses:`.
#
# It emits the AUTHORITY CHECKOUT too, because that is what the real receivers write and what the
# detector reads. A `run:` of a script names only a PATH, and a path cannot say where the file came
# from — so the `repository: FS-GG/.github` line is the provenance, and without it a repo that VENDORED
# its own copy of the script (a fork — precisely NOT participation) would certify as wired.
#
# The ref is passed verbatim so a leg can pin the PATH PREFIX, which is what differs between real
# receivers — SDD/Rendering/Game run it from `.github/`, Governance from `_org-build/` — and is why the
# detector keys on the basename and not the prefix.
#
# --no-provenance omits the checkout: the fork case.
wire_script() { clearfail "$1"; local slug="${1//\//__}" ref="$2" prov=1
                [ "${3:-}" = "--no-provenance" ] && prov=0
                mkdir -p "$FIX/$slug"; printf '%s\n' "gate.yml" > "$FIX/$slug.list"
                { printf 'jobs:\n  drift:\n    steps:\n'
                  printf '      - uses: actions/checkout@v7\n'
                  [ "$prov" -eq 1 ] && printf '      - uses: actions/checkout@v7\n        with:\n          repository: FS-GG/.github\n          path: _org-build\n'
                  printf '      - run: %s --check "$GITHUB_WORKSPACE"\n' "$ref"; } > "$FIX/$slug/gate.yml"; }

# wire_both <repo> <wf> <script-ref> — a receiver that wires a WORKFLOW capability and a SCRIPT
# capability at once. This is the real state of every build-config receiver (SDD wires
# coordination-kit + lockfile-sync by `uses:` AND build-config by an inlined script job), and no leg
# covered it: a regression that made the two detector kinds mutually exclusive in repo_calls — an
# early `return` after the `uses:` grep, say — would pass the whole fixture and break only on the org.
wire_both() { clearfail "$1"; local slug="${1//\//__}"
              mkdir -p "$FIX/$slug"; printf '%s\n%s\n' "coord.yml" "gate.yml" > "$FIX/$slug.list"
              printf 'jobs:\n  j1:\n    uses: FS-GG/.github/.github/workflows/%s@main\n' "$2" > "$FIX/$slug/coord.yml"
              printf 'jobs:\n  drift:\n    steps:\n      - uses: actions/checkout@v7\n        with:\n          repository: FS-GG/.github\n          path: _org-build\n      - run: %s --check\n' "$3" > "$FIX/$slug/gate.yml"; }

# wire_materializer <repo> [opt-in-mode] [enforcement-mode]
#   opt-in-mode: true (default), missing, no-package, commented
#   enforcement-mode: true (default), missing, commented, split, swallowed, no-fail
# The real contract is compound: package provenance + explicit property in the receiver project, and
# an executable workflow block that reruns FsggKitMaterialize then diffs both managed props.
wire_materializer() {
  clearfail "$1"
  local slug="${1//\//__}" opt="${2:-true}" enforce="${3:-true}"
  mkdir -p "$FIX/$slug"
  printf '%s\n' "gate.yml" > "$FIX/$slug.list"
  case "$enforce" in
    true)
      printf 'jobs:\n  build-config-drift:\n    steps:\n      - run: |\n          dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize -v minimal\n          if ! git diff --quiet -- Directory.Build.props Directory.Packages.props; then\n            exit 1\n          fi\n' > "$FIX/$slug/gate.yml" ;;
    missing)
      printf 'jobs:\n  build:\n    steps:\n      - run: dotnet test\n' > "$FIX/$slug/gate.yml" ;;
    commented)
      printf 'jobs:\n  build:\n    steps:\n      - run: |\n          # dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize\n          # if ! git diff --quiet -- Directory.Build.props Directory.Packages.props; then\n          echo no-materialize\n' > "$FIX/$slug/gate.yml" ;;
    split)
      printf 'jobs:\n  build-config-drift:\n    steps:\n      - run: |\n          dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize\n      - run: |\n          if ! git diff --quiet -- Directory.Build.props Directory.Packages.props; then\n            exit 1\n          fi\n' > "$FIX/$slug/gate.yml" ;;
    swallowed)
      printf 'jobs:\n  build-config-drift:\n    steps:\n      - run: |\n          dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize\n          git diff --quiet -- Directory.Build.props Directory.Packages.props || true\n' > "$FIX/$slug/gate.yml" ;;
    no-fail)
      printf 'jobs:\n  build-config-drift:\n    steps:\n      - run: |\n          dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize\n          if ! git diff --quiet -- Directory.Build.props Directory.Packages.props; then\n            echo drift-observed-but-not-failed\n          fi\n' > "$FIX/$slug/gate.yml" ;;
  esac
  case "$opt" in
    true)
      printf '<Project Sdk="Microsoft.NET.Sdk">\n  <PropertyGroup>\n    <FsggKitMaterializeBuildConfig>true</FsggKitMaterializeBuildConfig>\n  </PropertyGroup>\n  <ItemGroup>\n    <PackageReference Include="FS.GG.Kit" />\n  </ItemGroup>\n</Project>\n' > "$FIX/$slug/receiver.proj" ;;
    missing)
      printf '<Project Sdk="Microsoft.NET.Sdk"><ItemGroup><PackageReference Include="FS.GG.Kit" /></ItemGroup></Project>\n' > "$FIX/$slug/receiver.proj" ;;
    no-package)
      printf '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><FsggKitMaterializeBuildConfig>true</FsggKitMaterializeBuildConfig></PropertyGroup></Project>\n' > "$FIX/$slug/receiver.proj" ;;
    commented)
      printf '<Project Sdk="Microsoft.NET.Sdk">\n<!--\n  <PropertyGroup><FsggKitMaterializeBuildConfig>true</FsggKitMaterializeBuildConfig></PropertyGroup>\n  <ItemGroup><PackageReference Include="FS.GG.Kit" /></ItemGroup>\n-->\n</Project>\n' > "$FIX/$slug/receiver.proj" ;;
  esac
}
wire_materializer_and_workflow() {
  wire_materializer "$1" "${3:-true}" "${4:-true}"
  local slug="${1//\//__}"
  printf '%s\n%s\n' "gate.yml" "coord.yml" > "$FIX/$slug.list"
  printf 'jobs:\n  coordination:\n    uses: FS-GG/.github/.github/workflows/%s@main\n' "$2" > "$FIX/$slug/coord.yml"
}

# wire_caller <repo> [mode] — the `caller: skill-union` detector (#1504).
#
# The receiver contract is compound and BOTH halves must live in ONE workflow file: a call to the
# authority's skill-union-assert.yml aimed at the receiver's OWN repository root over all three
# ADR-0011 roots, and a `pull_request` trigger that fires when any of those roots changes.
#
# Every mode below is a real way to look wired and not be, and the ones that matter most are the two a
# bare `uses:` detector cannot tell from the genuine article: `product` (the call audits a GENERATED
# composition product — what FS.GG.Templates legitimately does) and `narrow-roots` (a smaller audit than
# the capability claims). Those two are the reason this capability is not `workflow: skill-union-assert.yml`.
#
# EVERY MODE HERE IS A YAML DIALECT AS WELL AS A WIRING SHAPE, and that is the point. The first version
# of this detector was two line-oriented awk scanners, and every mode in this fixture was block-style,
# `uses:`-before-`with:`, whole-line-comments, list-items-deeper-than-their-key. All eighteen legs passed
# while FIVE legal YAML shapes walked straight through — `with: {product-path: …}`, `with:` before
# `uses:`, a trailing inline comment carrying the `uses:`, `on: {pull_request: {paths: […]}}`, and a
# `paths:` sequence at its key's own indentation. One fixture dialect is how a detector passes its own
# suite and fails on the org, so the modes below deliberately spread across dialects: flow mappings,
# inline sequences, reversed key order, aliases, negated globs, CRLF.
#
# modes: true (default) · default-inputs · unfiltered · product · narrow-roots · no-agents-trigger
#        ignore-root · push-only · commented · local · split
#        flow-with-product · flow-with-narrow-roots · with-before-uses · inline-comment-uses
#        flow-on-narrow · flow-pr-narrow · paths-at-key-indent · inline-paths
#        broad-paths · alias-paths · negated-root · ignore-nonroot · archive-lookalike
#        pr-target · step-level-uses · two-calls · crlf · expression-product · unparseable
wire_caller() {
  clearfail "$1"
  local slug="${1//\//__}" mode="${2:-true}"
  mkdir -p "$FIX/$slug"
  printf '%s\n' "skill-union.yml" > "$FIX/$slug.list"

  # The two-root pull_request filter every honest mode shares (ADR-0067 §5).
  local trigger_all='on:
  pull_request:
    paths:
      - ".claude/skills/**"
      - ".agents/skills/**"
      - ".github/workflows/skill-union.yml"
'
  local call_root='jobs:
  skill-union:
    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main
    with:
      product-path: "."
'
  case "$mode" in
    true)
      printf '%s%s' "$trigger_all" "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    # No `with:` at all. `product-path` defaults to "." and `roots` to ADR-0011's three, so the DEFAULTS
    # are the contract — a detector that demanded the inputs be written out would report the most
    # correct possible caller as a gap.
    default-inputs)
      printf '%sjobs:\n  skill-union:\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    # Unfiltered is WIDER than covered: it runs on every PR, roots included.
    unfiltered)
      printf 'on:\n  pull_request:\n%s' "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    # THE FAIL-OPEN THIS DETECTOR EXISTS FOR: a real `uses:` of the real workflow, aimed at a generated
    # product. It proves nothing about the receiver's committed roots.
    product)
      printf '%sjobs:\n  skill-union:\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n    with:\n      product-path: "artifacts/generated-product"\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    narrow-roots)
      printf '%sjobs:\n  skill-union:\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n    with:\n      product-path: "."\n      roots: ".claude/skills"\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    # The call is right and the gate is armed on one root of two — so a partitioned .agents/ can land
    # without ever re-running the workflow that would have caught it (#332/#334's shape).
    no-agents-trigger)
      printf 'on:\n  pull_request:\n    paths:\n      - ".claude/skills/**"\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    ignore-root)
      printf 'on:\n  pull_request:\n    paths-ignore:\n      - ".agents/skills/**"\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    # A push-only workflow reports nothing on a pull request, so it can never be the required receiver
    # check this capability claims.
    push-only)
      printf 'on:\n  push:\n    branches: [main]\n    paths:\n      - ".claude/skills/**"\n      - ".agents/skills/**"\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    commented)
      printf '%sjobs:\n  skill-union:\n    # uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n    runs-on: ubuntu-latest\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    # A repo running its OWN copy is not participating in the authority's fabric — the rule the `wf:`
    # grep states, and the reason the authority is not a phantom adopter of what it hosts.
    local)
      printf '%sjobs:\n  skill-union:\n    uses: ./.github/workflows/skill-union-assert.yml\n    with:\n      product-path: "."\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    # The call in one file, the root triggers in ANOTHER. A trigger cannot arm a workflow it is not in:
    # the roots change, the other workflow runs, and the one that audits them does not.
    split)
      printf '%s\n%s\n' "skill-union.yml" "roots-watch.yml" > "$FIX/$slug.list"
      printf 'on:\n  pull_request:\n    paths:\n      - "src/**"\n%s' "$call_root" > "$FIX/$slug/skill-union.yml"
      printf '%sjobs:\n  watch:\n    runs-on: ubuntu-latest\n' "$trigger_all" > "$FIX/$slug/roots-watch.yml" ;;

    # --- FLOW MAPPINGS. A flow `with:` puts the key mid-line, so an anchored `/^[ ]*product-path:/`
    #     never matched and the input read as ABSENT — i.e. as the default "." — certifying a
    #     generated-product call as the committed-root gate. The fail-open this capability exists for.
    flow-with-product)
      printf '%sjobs:\n  skill-union:\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n    with: {product-path: "artifacts/generated-product"}\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    flow-with-narrow-roots)
      printf '%sjobs:\n  skill-union:\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n    with: { product-path: ".", roots: ".claude/skills" }\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    # YAML mappings are UNORDERED and Actions accepts either order. A scanner that collected inputs only
    # AFTER the `uses:` line saw none of them.
    with-before-uses)
      printf '%sjobs:\n  skill-union:\n    with:\n      product-path: "artifacts/generated-product"\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    # PROSE IS NOT WIRING, and an INLINE comment is prose too. A repo that DELETED its caller and left a
    # note behind must read as a gap — the whole-line `commented` mode above did not cover this.
    inline-comment-uses)
      printf '%sjobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - run: "true"   # removed: uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;

    # --- FLOW `on:`. Both spellings were read as "inline form, therefore unfiltered, therefore armed",
    #     so a filter that excludes every skill root passed.
    flow-on-narrow)
      printf 'on: {pull_request: {paths: ["src/**"]}}\n%s' "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    flow-pr-narrow)
      printf 'on:\n  pull_request: {paths: ["src/**"]}\n%s' "$call_root" > "$FIX/$slug/skill-union.yml" ;;

    # --- SEQUENCE SHAPES. A sequence at its key's own indentation is ordinary YAML; requiring entries
    #     strictly deeper reported a correctly-armed gate as a gap and told the operator to add the
    #     filter that was already there.
    paths-at-key-indent)
      printf 'on:\n  pull_request:\n    paths:\n    - ".claude/skills/**"\n    - ".agents/skills/**"\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    inline-paths)
      printf 'on:\n  pull_request:\n    paths: [".claude/skills/**", ".agents/skills/**"]\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    alias-paths)
      printf 'x-roots: &roots\n  - ".claude/skills/**"\n  - ".agents/skills/**"\non:\n  pull_request:\n    paths: *roots\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;

    # --- GLOB SEMANTICS. Coverage is glob MATCHING, not a prefix test: a broader filter genuinely fires
    #     on a root change and must pass, a lookalike directory must not, and `!` SUBTRACTS.
    broad-paths)
      printf 'on:\n  pull_request:\n    paths: [".claude/**", ".agents/**"]\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    negated-root)
      printf 'on:\n  pull_request:\n    paths: ["**", "!.agents/skills/**"]\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    ignore-nonroot)
      printf 'on:\n  pull_request:\n    paths-ignore: ["docs/**"]\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    archive-lookalike)
      printf 'on:\n  pull_request:\n    paths: [".claude/skills-archive/**", ".agents/skills-archive/**"]\n%s' \
        "$call_root" > "$FIX/$slug/skill-union.yml" ;;

    # `pull_request_target` checks out the BASE ref, so the assertion would audit the tree the change is
    # NOT in — a gate that reports on the PR and proves nothing about it.
    pr-target)
      printf 'on: [pull_request_target]\n%s' "$call_root" > "$FIX/$slug/skill-union.yml" ;;
    # A reusable workflow is called by a JOB's `uses:`. Actions rejects a step-level `uses:` of one; a
    # text grep counted it.
    step-level-uses)
      printf '%sjobs:\n  build:\n    runs-on: ubuntu-latest\n    steps:\n      - uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    # The realistic FS.GG.Templates end state: a generated-product call AND an own-root call in one file.
    # The own-root one is what the capability is about, and one job must not mask the other either way.
    two-calls)
      printf '%sjobs:\n  product:\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n    with:\n      product-path: "artifacts/generated-product"\n  own-roots:\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n    with:\n      product-path: "."\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    crlf)
      printf '%s%s' "$trigger_all" "$call_root" | sed 's/$/\r/' > "$FIX/$slug/skill-union.yml" ;;
    # An expression is not a value this detector can resolve, so it must NOT satisfy the call: unknown
    # fails CLOSED (#266). Pinned so the choice is deliberate rather than incidental.
    expression-product)
      printf '%sjobs:\n  skill-union:\n    uses: FS-GG/.github/.github/workflows/skill-union-assert.yml@main\n    with:\n      product-path: "${{ inputs.where }}"\n' \
        "$trigger_all" > "$FIX/$slug/skill-union.yml" ;;
    # A workflow GitHub itself cannot parse cannot be the live gate, so it contributes no caller tokens —
    # and it must not take the whole repo's verdict down with it either.
    unparseable)
      printf '%s\n%s\n' "skill-union.yml" "broken.yml" > "$FIX/$slug.list"
      printf '%s%s' "$trigger_all" "$call_root" > "$FIX/$slug/skill-union.yml"
      printf 'on: [pull_request\njobs: {{{\n' > "$FIX/$slug/broken.yml" ;;
  esac
}
unwired(){ clearfail "$1"; local slug="${1//\//__}"; mkdir -p "$FIX/$slug"; printf '%s\n' "ci.yml" > "$FIX/$slug.list";
           printf 'jobs:\n  build:\n    runs-on: ubuntu-latest\n' > "$FIX/$slug/ci.yml"; }
noflows(){ clearfail "$1"; local slug="${1//\//__}"; rm -f "$FIX/$slug.list"; rm -rf "${FIX:?}/$slug"; }  # "${FIX:?}": an empty FIX would make this `rm -rf /$slug` (SC2115, #648)
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
# shellcheck disable=SC2120  # every caller today is a bare `run 2>&1`, so shellcheck is right that no
# argument is ever passed. `"$@"` STAYS: it is the forwarding a wrapper is expected to do, and dropping
# it is the trap — a later `run --apply` would then have its flag SILENTLY swallowed rather than
# forwarded to the audit, which is a fixture that lies about what it ran. #648
run() { PATH="$STUB:$PATH" REPOS_AUDIT_TRIES="${TRIES:-1}" REPOS_AUDIT_RETRY_DELAY=0 \
          bash "$AUDIT" --registry "$REG" --repos-sh "$REPOS_SH" "$@"; }

# run, with every gh call the stub served recorded to <logfile> (#1556 criterion 5, which is a claim
# about API TRAFFIC and cannot be checked from the audit's output).
#
# A FUNCTION OF ITS OWN, rather than `GH_CALL_LOG=… run`. Two reasons, both of which would have made
# the legs lie. A variable assignment prefixing a FUNCTION persists in the calling shell in bash, so
# the log would leak into every later leg and the counts would be of some other run; and it is not
# exported to the function's children, so the stub — two processes down — would never see it. Setting
# it on the `bash` invocation itself is what actually reaches the stub. The log is TRUNCATED here, so
# a leg counts its own calls and not the ones before it.
run_logged() {
  local log="$1" rc=0; shift; : > "$log"
  GH_CALL_LOG="$log" PATH="$STUB:$PATH" REPOS_AUDIT_TRIES="${TRIES:-1}" REPOS_AUDIT_RETRY_DELAY=0 \
    bash "$AUDIT" --registry "$REG" --repos-sh "$REPOS_SH" "$@" || rc=$?
  return "$rc"
}

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
#      `labels` is `push:`, so it contributes no receiver-capability pairs either — which is the point:
#      a roster whose every capability is unsweepable, whether by `receivers: none` or by `push:`, is a
#      gate reporting on participation without looking at a single repo.
ALLNONE="$WORK/allnone.yml"; cat > "$ALLNONE" <<YAML
schemaVersion: 5
updated: 2026-07-13
authority: FS-GG/.github
repos:
  - { id: .github, full: FS-GG/.github,   role: authority, receives: [labels] }
  - { id: sdd,     full: FS-GG/FS.GG.SDD, role: framework, receives: [labels] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml, receivers: none, reason: nobody receives it in this fixture }
$LABELS_CAP
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
    && ! printf '%s' "$out" | grep -q 'FS.GG.Rendering receives .* but nothing in its workflows references' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "unreachable receiver -> exit 2 'could not determine', not a fabricated gap" \
  || bad "unreachable receiver must not be reported as unwired" "rc=$rc: $out"

# (5) the over-correction guard: a 404 IS an answer. A repo with no workflows dir is a real gap
#     (exit 1), not an outage — otherwise every genuine gap would hide behind "could not determine".
wire FS-GG/FS.GG.SDD; noflows FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'FS.GG.Rendering receives .* but nothing in its workflows references' \
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
    && printf '%s' "$out" | grep -q 'FS.GG.SDD receives .* but nothing in its workflows references'; } \
  && ok "undetermined outranks a gap -> exit 2, but the gap is still reported" \
  || bad "undetermined must outrank a gap" "rc=$rc: $out"

# (9) a repo the token cannot see 404s exactly like an empty one. Believing that 404 is the whole bug
#     again, one status code across: a private/renamed/deleted receiver must be undetermined, never a
#     wiring gap. Only the repo probe can tell the two apart.
wire FS-GG/FS.GG.SDD; invisible FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'FS.GG.Rendering is not readable' \
    && ! printf '%s' "$out" | grep -q 'FS.GG.Rendering receives .* but nothing in its workflows references'; } \
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
# Every roster it builds rosters `labels`, so every roster it builds must declare how `labels` is
# detected — see LABELS_CAP. Appended here rather than at each call site so a new leg cannot forget it
# and get an exit-3 closure failure it did not mean to test.
mkreg2() { # $1 = file, $2 = sdd receives, $3 = rendering receives, $4… = capability rows
  local f="$1" sdd="$2" rend="$3"; shift 3
  { printf 'schemaVersion: 5\nupdated: 2026-07-13\nauthority: FS-GG/.github\nrepos:\n'
    printf '  - { id: .github,   full: FS-GG/.github,         role: authority, receives: [labels] }\n'
    printf '  - { id: sdd,       full: FS-GG/FS.GG.SDD,       role: framework, receives: [%s] }\n' "$sdd"
    printf '  - { id: rendering, full: FS-GG/FS.GG.Rendering, role: framework, receives: [%s] }\n' "$rend"
    printf 'capabilities:\n'
    printf '  %s\n' "$@"
    printf '%s\n' "$LABELS_CAP"; } > "$f"
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
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'FS.GG.Rendering references' \
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
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS.GG.Rendering references .*lockfile-sync\.yml" \
    && printf '%s' "$out" | grep -q "does not declare 'receives: lockfile-sync'" \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "an adopted-but-unrostered capability -> exit 1, naming the repo and the capability" \
  || bad "an unrostered adopter must not be invisible (#503)" "rc=$rc: $out"

# (19b) ...and the sweep must not have a quoting-dependent blind spot. YAML lets a receiver write
#       `uses: "FS-GG/.github/…"`, and Actions honours it. The unquoted-only matcher missed it — which
#       fails in opposite directions: a DECLARED receiver that quotes is a false gap (loud and wrong),
#       an UNDECLARED one sails past the drift check (silent — the very adopter this sweep is for).
qwire() { clearfail "$1"; local slug="${1//\//__}"; shift; local q="$1"; shift; local i=0 wf
          mkdir -p "$FIX/$slug"; printf '%s\n' "coord.yml" > "$FIX/$slug.list"
          { printf 'jobs:\n'
            for wf in "$@"; do i=$((i+1))
              printf '  j%s:\n    uses: %sFS-GG/.github/.github/workflows/%s@main%s\n' "$i" "$q" "$wf" "$q"
            done; } > "$FIX/$slug/coord.yml"; }
for q in '"' "'"; do
  # declared + quoted -> wired, NOT a fabricated gap.
  qwire FS-GG/FS.GG.SDD "$q" coordination-coherence.yml lockfile-sync.yml
  # undeclared + quoted -> must still be caught as an unrostered adopter.
  qwire FS-GG/FS.GG.Rendering "$q" coordination-coherence.yml lockfile-sync.yml
  out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$DRIFTREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "does not declare 'receives: lockfile-sync'" \
      && ! printf '%s' "$out" | grep -q 'FS.GG.SDD receives .* but nothing in its workflows references'; } \
    && ok "a quoted ($q) uses: is still matched — no false gap, and the drift check still sees it" \
    || bad "the uses: matcher has a quoting blind spot" "quote=$q rc=$rc: $out"
done

# (19c) an unreadable repo is charged to every capability it was rostered for, so the per-capability
#       line still adds up. It used to report "2 rostered receiver(s): 1 wired, 0 gap(s)" and simply
#       lose the second — a complete-looking accounting of a run that did not complete.
wire FS-GG/FS.GG.SDD; unreachable FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] \
    && printf '%s' "$out" | grep -q 'coordination-kit .* 2 rostered receiver(s): 1 wired, 0 gap(s), 1 undetermined'; } \
  && ok "an unreadable repo is charged to its capabilities — the per-capability tally adds up" \
  || bad "the per-capability line loses an unreadable receiver" "rc=$rc: $out"

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
    # ...and the same argument for the rule the audit IMPORTS (#1529). Sharing the sparse-checkout
    # closure rule with check-sparse-checkout-closure.py is what keeps the two readers from drifting,
    # but it also means an edit THERE changes what the roster sweep asserts about ten repositories.
    # If that file is outside this filter, the legs below can be invalidated by a commit that never
    # re-runs them — reach bought at the price of an unarmed gate, which is #332/#334's shape.
    if "scripts/check-sparse-checkout-closure.py" not in st[True][trigger]["paths"]:
        bad.append(f"repos-audit-selftest {trigger} paths: does not cover scripts/check-sparse-checkout-closure.py, whose rule repos-audit.sh imports — an edit to it would not re-run the legs that pin its verdicts")

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
  # `env`, not a bare assignment prefix: SBOX is not exported, so the prefix is doing real work — but
  # written as a prefix, `GITHUB_OUTPUT="$SBOX/gh_out"` reads as if it might see the SBOX assigned
  # beside it (it does not — it expands the PARENT's, which happens to be the same value, so the code
  # was correct by coincidence rather than by construction). `env` makes the expansions unambiguously
  # the parent's, which is what was meant. Behaviour is identical. SC2097/SC2098, #648.
  ( cd "$SBOX" && env SBOX="$SBOX" GITHUB_OUTPUT="$SBOX/gh_out" REPOS_AUDIT_RETRY_AFTER_S=0 \
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

# ---------------------------------------------------------------------------------------------------
# (17) THE #628 REGRESSION: a SCRIPT-delivered capability is audited, in both directions.
#
# `build-config` is not wired by `uses:` — receivers INLINE a job that checks .github out and runs
# `sync-build-config.sh`. The `uses:` detector is structurally blind to that, so the capability simply
# had no `capabilities:` row, and was therefore swept in NEITHER direction: four repos enforced it (in
# SDD's case as a REQUIRED status check) while `receives:` said zero, and this audit reported green
# over all of them for months. #626 then read those empty rows as "propagates to nobody", shipped on
# it, and four repos went red within twenty minutes.
SCRIPTCAP="- { id: build-config, script: sync-build-config.sh, reason: script-delivered; receivers inline a job }"

# (17a) declared + really wired -> ok. The two receivers reference the script through DIFFERENT path
#       prefixes, which is the real state of the org and the reason the detector matches the basename:
#       anchoring on either prefix would report the other as a false gap.
SCRIPTREG="$WORK/script.yml"
mkreg2 "$SCRIPTREG" "labels, build-config" "labels, build-config" "$SCRIPTCAP"
wire_script FS-GG/FS.GG.SDD       ".github/scripts/sync-build-config.sh"
wire_script FS-GG/FS.GG.Rendering "_org-build/scripts/sync-build-config.sh"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$SCRIPTREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)'; } \
  && ok "script capability: both receivers wired -> ok, whatever path prefix they run it from" \
  || bad "script detector must match on the basename" "rc=$rc: $out"

# (17b) declared + NOT wired -> a GAP. A receiver that quietly drops the drift job is the thing this
#       detector exists to catch, and before #628 nothing could see it.
wire_script FS-GG/FS.GG.SDD ".github/scripts/sync-build-config.sh"; unwired FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$SCRIPTREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'build-config'" \
    && printf '%s' "$out" | grep -q '1 wired, 1 gap(s)'; } \
  && ok "script capability: a declared receiver that does not run the script -> a gap (exit 1)" \
  || bad "an unwired script receiver must be a gap" "rc=$rc: $out"

# (17c) wired + NOT declared -> DRIFT. THIS IS #628 ITSELF: the repo really enforces build-config and
#       the roster does not say so. It is the direction that would have stopped #626 being written.
DRIFTSCRIPT="$WORK/driftscript.yml"
mkreg2 "$DRIFTSCRIPT" "labels, build-config" "labels" "$SCRIPTCAP"
wire_script FS-GG/FS.GG.SDD       ".github/scripts/sync-build-config.sh"
wire_script FS-GG/FS.GG.Rendering ".github/scripts/sync-build-config.sh"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$DRIFTSCRIPT" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering references .* does not declare 'receives: build-config'" \
    && printf '%s' "$out" | grep -q '1 unrostered adopter'; } \
  && ok "script capability: an unrostered repo that really runs it -> drift (exit 1) — #628 itself" \
  || bad "an unrostered script adopter must be reported" "rc=$rc: $out"

# (17d) a receiver's OWN fork of the script is NOT the authority's. The detector compares the whole
#       basename, so `my-sync-build-config.sh` must not satisfy `sync-build-config.sh` — otherwise a
#       repo that forked the script (i.e. deliberately stopped participating) would audit as wired,
#       which is a fail-open in the detector guarding against fail-open.
mkreg2 "$SCRIPTREG" "labels, build-config" "labels, build-config" "$SCRIPTCAP"
wire_script FS-GG/FS.GG.SDD       ".github/scripts/sync-build-config.sh"
wire_script FS-GG/FS.GG.Rendering "scripts/my-sync-build-config.sh"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$SCRIPTREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'build-config'"; } \
  && ok "script capability: a receiver's own fork of the script does not count as wiring it" \
  || bad "a forked script must not satisfy the authority's detector" "rc=$rc: $out"

# (18) THE CLOSURE, and the half that makes this a fix rather than a relocation: a capability a repo
#      RECEIVES but which has no `capabilities:` row at all is UNAUDITABLE — not findable as unwired,
#      not findable as an unrostered adopter — while remaining a legal `receives:` word. That silence
#      is exactly what #626 read as a licence. It must be a permanent no-verdict, not a green.
NODETECT="$WORK/nodetect.yml"
mkreg2 "$NODETECT" "labels, coordination-kit, build-config" "labels, coordination-kit" \
  "- { id: coordination-kit, workflow: coordination-coherence.yml }"
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$NODETECT" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q "receive 'build-config'" \
    && printf '%s' "$out" | grep -q "no 'capabilities:' row" \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "a RECEIVED capability with no detector row -> exit 3, named; never a vacuous green (#628)" \
  || bad "a received-but-undetectable capability must fail closed" "rc=$rc: $out"

# (19) a PUSH capability is not swept — there IS no receiver-side artifact — and every repo rosters
#      `labels`, so a sweep would report all of them as gaps. It must be reported, and excluded from
#      the pair count: the pairs line is a count of what this audit actually LOOKED at, and folding in
#      something it did not examine would be claiming an examination that never happened.
mkreg "$REG"; wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$REG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'labels — 3 rostered receiver(s), PUSHED' \
    && printf '%s' "$out" | grep -q '2 receiver-capability pair(s)' \
    && ! printf '%s' "$out" | grep -q "receives 'labels' but nothing"; } \
  && ok "a push capability is reported, not swept, and not counted as pairs it never examined" \
  || bad "a push capability must not be swept at the receiver" "rc=$rc: $out"

# (20) THE AUTHORITY IS NOT A PHANTOM ADOPTER OF ITS OWN SCRIPT. `.github` owns sync-build-config.sh
#      and naturally names it in its own workflows. The `uses:` detector dodges this for free (the
#      authority calls its own workflows by a LOCAL `uses: ./…`, which is deliberately unmatched); a
#      script reference carries no such tell, so the rule has to be stated. Without it the audit
#      reports the authority as an adopted-but-unrostered receiver of every script it hosts — which is
#      exactly the phantom-adopter failure repo_calls already refuses by name. Observed on the real
#      org on the first run of this detector.
mkreg2 "$SCRIPTREG" "labels, build-config" "labels, build-config" "$SCRIPTCAP"
wire_script FS-GG/FS.GG.SDD       ".github/scripts/sync-build-config.sh"
wire_script FS-GG/FS.GG.Rendering ".github/scripts/sync-build-config.sh"
wire_script FS-GG/.github         "scripts/sync-build-config.sh"     # the authority, using its OWN file
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$SCRIPTREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '0 unrostered adopter' \
    && ! printf '%s' "$out" | grep -q "FS-GG/.github references"; } \
  && ok "the authority running its OWN script is not adoption — no phantom unrostered adopter" \
  || bad "the authority must not be a phantom adopter of a script it hosts" "rc=$rc: $out"

# (21) PROVENANCE. A `run:` of a script names only a PATH, and a path cannot say where the file came
#      from. So a receiver that VENDORED its own copy of `sync-build-config.sh` — committed it, never
#      checks .github out, runs its own — must NOT audit as wired: that is a FORK, which is precisely
#      not participation, and precisely what the receivers' own gate ("sync-not-fork drift check")
#      exists to prevent. The `uses:` detector cannot be fooled this way because it NAMES the
#      authority; the script detector has to read the receiver's `repository: FS-GG/.github` checkout
#      to get the same guarantee. Without this the audit certifies the repo that has silently stopped
#      tracking the org config.
mkreg2 "$SCRIPTREG" "labels, build-config" "labels, build-config" "$SCRIPTCAP"
wire_script FS-GG/FS.GG.SDD       ".github/scripts/sync-build-config.sh"
wire_script FS-GG/FS.GG.Rendering "scripts/sync-build-config.sh" --no-provenance   # vendored fork
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$SCRIPTREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'build-config'" \
    && printf '%s' "$out" | grep -q '1 wired, 1 gap(s)'; } \
  && ok "script capability: a VENDORED fork (no authority checkout) is not wiring — provenance is required" \
  || bad "a vendored script must not satisfy the detector" "rc=$rc: $out"

# (22) PROSE IS NOT WIRING. A receiver that DELETED its drift job and left `# we used to run
#      sync-build-config.sh here` behind must read as a GAP, not as wired — otherwise the one thing
#      this detector exists to find reports green. The codebase already refuses this class for
#      `workflow_call:`: a check whose subject is "does this really run?" must not be satisfiable by
#      prose about running.
clearfail FS-GG/FS.GG.Rendering
mkdir -p "$FIX/FS-GG__FS.GG.Rendering"; printf '%s\n' "gate.yml" > "$FIX/FS-GG__FS.GG.Rendering.list"
printf 'jobs:\n  build:\n    steps:\n      - uses: actions/checkout@v7\n        with:\n          repository: FS-GG/.github\n          path: _org-build\n      # we used to run sync-build-config.sh here, but it was removed\n      - run: echo hi\n' \
  > "$FIX/FS-GG__FS.GG.Rendering/gate.yml"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$SCRIPTREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'build-config'" \
    && printf '%s' "$out" | grep -q '1 wired, 1 gap(s)'; } \
  && ok "script capability: a COMMENT naming the script is not wiring — prose cannot satisfy the gate" \
  || bad "a commented-out script reference must not satisfy the detector" "rc=$rc: $out"

# (23) ONE PASS, BOTH KINDS. Every real build-config receiver wires a workflow capability AND a script
#      capability at once. Nothing covered that, so a regression making the two detector kinds mutually
#      exclusive in repo_calls would have passed this whole fixture and broken only on the live org.
BOTHREG="$WORK/both.yml"
mkreg2 "$BOTHREG" "labels, coordination-kit, build-config" "labels, coordination-kit, build-config" \
  "- { id: coordination-kit, workflow: coordination-coherence.yml }" "$SCRIPTCAP"
wire_both FS-GG/FS.GG.SDD       coordination-coherence.yml ".github/scripts/sync-build-config.sh"
wire_both FS-GG/FS.GG.Rendering coordination-coherence.yml "_org-build/scripts/sync-build-config.sh"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$BOTHREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '4 receiver-capability pair(s) — 4 wired'; } \
  && ok "one repo wiring BOTH a workflow and a script capability -> both detected in a single pass" \
  || bad "the two detector kinds must not be mutually exclusive" "rc=$rc: $out"

# (24) A BIG workflow file must detect exactly like a small one.
#
#      `printf '%s' "$body" | grep -qE …` is silently, NON-DETERMINISTICALLY WRONG under `pipefail`:
#      `grep -q` exits on its first match, and if the writer is still blocked on a full 64KiB pipe
#      buffer it takes SIGPIPE and dies 141 — which pipefail then reports as the PIPELINE's status, so
#      the test reads FALSE although grep matched. Measured on FS.GG.Game's real gate.yml (19.5KiB):
#      the pipeline form returned 141 on SEVEN of ten runs, and the audit called a correctly-wired repo
#      a GAP, confidently, with `0 undetermined`, on about a third of runs.
#
#      Every fixture workflow above is a few hundred bytes — far under the pipe buffer — so the race
#      never fires and the whole suite passed green over it. This leg makes the file big enough that
#      the old form fails RELIABLY (padding AFTER the match, so grep exits with the writer still
#      going), which is what turns a heisenbug into a regression test.
BIGREG="$WORK/big.yml"
mkreg2 "$BIGREG" "labels, build-config" "labels, build-config" "$SCRIPTCAP"
bigwire() { clearfail "$1"; local slug="${1//\//__}"; mkdir -p "$FIX/$slug"
            printf '%s\n' "gate.yml" > "$FIX/$slug.list"
            { printf 'jobs:\n  drift:\n    steps:\n'
              printf '      - uses: actions/checkout@v7\n        with:\n          repository: FS-GG/.github\n          path: _org-build\n'
              printf '      - run: _org-build/scripts/sync-build-config.sh --check\n'
              # >64KiB of trailing steps, so grep -q matches early and the writer is still going.
              for i in $(seq 1200); do
                printf '      - name: padding step %s to outrun the pipe buffer\n        run: echo %s\n' "$i" "$i"
              done; } > "$FIX/$slug/gate.yml"; }
bigwire FS-GG/FS.GG.SDD; bigwire FS-GG/FS.GG.Rendering
big_ok=1
for _ in 1 2 3 4 5; do
  out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$BIGREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)'; } || { big_ok=0; break; }
done
[ "$big_ok" -eq 1 ] \
  && ok "a >64KiB workflow detects identically, on 5 consecutive runs (no pipefail/SIGPIPE race)" \
  || bad "a large workflow must not flip the verdict" "rc=$rc: $(printf '%s' "$out" | tail -4)"

# ---------------------------------------------------------------------------------------------------
# (25) THE #1395 REGRESSION: build-config moved from the authority script to FS.GG.Kit.
#
# The package-era receiver contract has two independently necessary halves:
#   1. receiver.proj references FS.GG.Kit AND explicitly enables FsggKitMaterializeBuildConfig;
#   2. executable CI reruns FsggKitMaterialize AND diffs both committed managed props.
# Either half alone can pass without protecting the files, so the detector is intentionally compound.
MATCAP="- { id: build-config, materializer: build-config, reason: package materializer plus CI drift enforcement }"
MATREG="$WORK/materializer.yml"
mkreg2 "$MATREG" "labels, build-config" "labels, build-config" "$MATCAP"

wire_materializer FS-GG/FS.GG.SDD
wire_materializer FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)'; } \
  && ok "materializer: package provenance + explicit opt-in + CI materialize/diff -> wired" \
  || bad "the current package build-config contract must audit green" "rc=$rc: $out"

# The manifest read is part of the subject. A 403 cannot be rendered as "missing opt-in" (a definite
# gap) or as "not adopted" (a reverse-direction clean); it is the retryable no-verdict.
wire_materializer FS-GG/FS.GG.SDD
wire_materializer FS-GG/FS.GG.Rendering
: > "$FIX/FS-GG__FS.GG.Rendering.failreceiver"
out="$(PATH="$STUB:$PATH" REPOS_AUDIT_TRIES=1 bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'reading .config/kit/FS.GG.Kit.receiver.proj failed' \
    && printf '%s' "$out" | grep -q '1 undetermined'; } \
  && ok "materializer: unreadable receiver project -> retryable no-verdict, never a fabricated gap" \
  || bad "an unreadable package opt-in is not an answer" "rc=$rc: $out"

wire_materializer FS-GG/FS.GG.SDD
wire_materializer FS-GG/FS.GG.Rendering missing true
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'build-config'" \
    && printf '%s' "$out" | grep -q 'FS.GG.Kit package provenance plus explicit'; } \
  && ok "materializer: declared receiver missing explicit opt-in -> gap" \
  || bad "missing materializer opt-in must not pass" "rc=$rc: $out"

wire_materializer FS-GG/FS.GG.Rendering no-package true
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'FS.GG.Kit package provenance plus explicit'; } \
  && ok "materializer: true property without FS.GG.Kit package provenance -> gap" \
  || bad "a bare property must not impersonate package adoption" "rc=$rc: $out"

wire_materializer FS-GG/FS.GG.Rendering true missing
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'CI FsggKitMaterialize + managed-props diff enforcement' \
    && ! printf '%s' "$out" | grep -q 'missing: FS.GG.Kit package provenance'; } \
  && ok "materializer: declared receiver missing CI enforcement -> gap, exact half named" \
  || bad "missing CI enforcement must not pass or blame the present opt-in" "rc=$rc: $out"

# Workflow-wide co-occurrence is not an execution relationship. Separate run blocks (and therefore
# potentially separate jobs/clean checkouts) cannot prove the diff examines what materialization wrote.
wire_materializer FS-GG/FS.GG.Rendering true split
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'CI FsggKitMaterialize + managed-props diff enforcement'; } \
  && ok "materializer: split run blocks cannot assemble a false enforcement contract" \
  || bad "materialize and diff in different run blocks must not pass" "rc=$rc: $out"

wire_materializer FS-GG/FS.GG.Rendering true swallowed
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'CI FsggKitMaterialize + managed-props diff enforcement'; } \
  && ok "materializer: a swallowed diff is observation, not enforcement" \
  || bad "git diff followed by || true must not pass" "rc=$rc: $out"

wire_materializer FS-GG/FS.GG.Rendering true no-fail
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'CI FsggKitMaterialize + managed-props diff enforcement'; } \
  && ok "materializer: a diff guard without a non-zero exit does not enforce drift" \
  || bad "a non-failing diff guard must not pass" "rc=$rc: $out"

DRIFTMAT="$WORK/driftmaterializer.yml"
mkreg2 "$DRIFTMAT" "labels, build-config" "labels" "$MATCAP"
wire_materializer FS-GG/FS.GG.SDD
wire_materializer FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$DRIFTMAT" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering adopts .* does not declare 'receives: build-config'" \
    && printf '%s' "$out" | grep -q '1 unrostered adopter'; } \
  && ok "materializer: fully wired but unrostered adopter -> drift" \
  || bad "reverse-direction materializer adoption must remain visible" "rc=$rc: $out"

# Incomplete unrostered adoption is drift too: either real half is an attempted capability adoption,
# and leaving it unrostered would make the eventual second half invisible to the fabric.
wire_materializer FS-GG/FS.GG.Rendering true missing
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$DRIFTMAT" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q '1 unrostered adopter'; } \
  && ok "materializer: unrostered opt-in without enforcement is still drift" \
  || bad "partial unrostered adoption must fail loud" "rc=$rc: $out"

wire_materializer FS-GG/FS.GG.SDD
wire_materializer FS-GG/FS.GG.Rendering commented commented
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MATREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'build-config'" \
    && printf '%s' "$out" | grep -q 'package provenance plus explicit' \
    && printf '%s' "$out" | grep -q 'CI FsggKitMaterialize'; } \
  && ok "materializer: XML/YAML comments are prose, not opt-in or enforcement" \
  || bad "non-code mentions must not satisfy either materializer half" "rc=$rc: $out"

# Multiple detector rows prove the materializer id is stored per capability, not read from the final
# parser-loop local. Put build-config FIRST and a workflow detector LAST; Rendering has the package
# half but not CI, so the exact missing half must still be diagnosed.
MULTIMAT="$WORK/multimaterializer.yml"
mkreg2 "$MULTIMAT" "labels, build-config, coordination-kit" "labels, build-config, coordination-kit" \
  "$MATCAP" "- { id: coordination-kit, workflow: coordination-coherence.yml }"
wire_materializer_and_workflow FS-GG/FS.GG.SDD coordination-coherence.yml
wire_materializer_and_workflow FS-GG/FS.GG.Rendering coordination-coherence.yml true missing
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MULTIMAT" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'CI FsggKitMaterialize + managed-props diff enforcement' \
    && ! printf '%s' "$out" | grep -q 'missing: FS.GG.Kit package provenance'; } \
  && ok "materializer: detector id is capability-local when a later detector row is different" \
  || bad "materializer state leaked from the capability parse loop" "rc=$rc: $out"

# ---------------------------------------------------------------------------------------------------
# (26) THE FULL SKILL UNION: a compound CALLER detector (#1504).
#
# `coordination-coherence` proves the KIT-OWNED SUBSET of ADR-0065's three-root invariant — the four
# skills registry/repos.yml's `kit:` block names. It was green on Governance, Rendering and SDD while
# 11, 46 and 28 co-tenant skills were absent from a root, because those trees really do hold the four
# kit skills coherently. `skill-union` is the capability that proves the WHOLE union, and its detector
# is compound for a reason no other capability has: `skill-union-assert.yml` is SUBJECT-PARAMETERISED.
# A bare `uses:` of it says nothing about WHAT was audited, so a `workflow:` row would certify the
# full-union capability off FS.GG.Templates' legitimate generated-product call — a green nobody earned
# (#628), one layer in, where the detector's subject is not the workflow but what it was pointed at.
CALLERCAP="- { id: skill-union, caller: skill-union, reason: full three-root union over the repo's own committed roots }"
CALLERREG="$WORK/caller.yml"
mkreg2 "$CALLERREG" "labels, skill-union" "labels, skill-union" "$CALLERCAP"

wire_caller FS-GG/FS.GG.SDD
wire_caller FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)'; } \
  && ok "caller: own-root call + three-root pull_request trigger -> wired" \
  || bad "the canonical skill-union receiver caller must audit green" "rc=$rc: $out"

# The DEFAULTS are the contract. `product-path` defaults to "." and `roots` to ADR-0011's three, so a
# caller with no `with:` block at all is the most correct caller there is — a detector that demanded the
# inputs be spelled out would report it as a gap and teach receivers to write redundant YAML.
wire_caller FS-GG/FS.GG.Rendering default-inputs
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)'; } \
  && ok "caller: absent product-path/roots take the workflow defaults -> wired" \
  || bad "default inputs must satisfy the call half" "rc=$rc: $out"

wire_caller FS-GG/FS.GG.Rendering unfiltered
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)'; } \
  && ok "caller: an UNFILTERED pull_request trigger is wider than covered -> wired" \
  || bad "an unfiltered trigger must not read as a coverage gap" "rc=$rc: $out"

# THE ONE THIS DETECTOR EXISTS FOR. A real `uses:` of the real workflow, on a real three-root trigger —
# aimed at a generated product. Under a `workflow:` detector this is indistinguishable from the genuine
# article, and the capability would report green over ungated committed roots.
wire_caller FS-GG/FS.GG.Rendering product
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'skill-union'" \
    && printf '%s' "$out" | grep -q 'GENERATED product'; } \
  && ok "caller: a call aimed at a GENERATED product is not a gate on the committed roots -> gap" \
  || bad "a generated-product call must not satisfy the full-union capability" "rc=$rc: $out"

wire_caller FS-GG/FS.GG.Rendering narrow-roots
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'skill-union'"; } \
  && ok "caller: a narrowed roots: is a smaller audit than the capability claims -> gap" \
  || bad "a one-root call must not certify the two-root union" "rc=$rc: $out"

# The call is right and the gate is armed on one root of two: a partitioned `.agents/` can land
# without ever re-running the workflow that would have caught it. The diagnostic must name the TRIGGER,
# not report "nothing calls it" — the remedy is a different edit.
wire_caller FS-GG/FS.GG.Rendering no-agents-trigger
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'does not RUN when a committed skill root changes' \
    && ! printf '%s' "$out" | grep -q 'nothing in its workflows calls'; } \
  && ok "caller: a trigger covering 1 of 2 roots -> gap, and the diagnostic names the TRIGGER half" \
  || bad "a partial root trigger must fail and blame the right half" "rc=$rc: $out"

wire_caller FS-GG/FS.GG.Rendering ignore-root
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'does not RUN when a committed skill root changes'; } \
  && ok "caller: a paths-ignore: that excludes a root disarms the gate -> gap" \
  || bad "an excluded root must not pass" "rc=$rc: $out"

wire_caller FS-GG/FS.GG.Rendering push-only
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'does not RUN when a committed skill root changes'; } \
  && ok "caller: a push-only workflow reports nothing on a PR, so it cannot be the required check -> gap" \
  || bad "a push-only caller must not satisfy a receiver gate" "rc=$rc: $out"

# PROSE IS NOT WIRING — the rule the script detector states, applied to a `uses:`. A receiver that
# DELETED its caller and left the line commented must read as a gap, or the one thing this capability
# exists to find reports green.
wire_caller FS-GG/FS.GG.Rendering commented
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'nothing in its workflows calls'; } \
  && ok "caller: a commented-out uses: is prose about calling, not a call -> gap" \
  || bad "a commented caller must not satisfy the call half" "rc=$rc: $out"

wire_caller FS-GG/FS.GG.Rendering local
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'nothing in its workflows calls'; } \
  && ok "caller: a LOCAL uses: ./ is running your own gate, not joining the authority's fabric -> gap" \
  || bad "a local uses: must not count as participation" "rc=$rc: $out"

# BOTH HALVES IN ONE FILE. A trigger cannot arm a workflow it is not in: the roots change, the watcher
# runs, and the workflow that AUDITS them does not. Workflow-wide co-occurrence is the same non-relation
# the materializer detector refuses across run blocks.
wire_caller FS-GG/FS.GG.Rendering split
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'does not RUN when a committed skill root changes'; } \
  && ok "caller: a root trigger in a DIFFERENT workflow file cannot arm the caller -> gap" \
  || bad "split call/trigger must not assemble a false gate" "rc=$rc: $out"

# THE AUTHORITY IS NOT A RECEIVER of its own assertion: it asserts its own roots with
# skill-roots-selfcheck.yml. Even handed a caller-shaped workflow it must not surface as an adopter,
# for the reason the script detector states outright — running your own gate is not participating in
# your own fabric.
wire_caller FS-GG/FS.GG.SDD
wire_caller FS-GG/FS.GG.Rendering
wire_caller FS-GG/.github
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
# Match the authority as a SUBJECT of a verdict, not merely as a string: the capability's own
# description names `FS-GG/.github/.github/workflows/skill-union-assert.yml`, so a bare grep for the
# repo would match every ok/gap line about somebody else and could never fail.
{ [ "$rc" -eq 0 ] && ! printf '%s' "$out" | grep -qE 'FS-GG/\.github (wires|adopts|references|receives)'; } \
  && ok "caller: the authority is never a phantom adopter of its own assertion" \
  || bad "the authority must not be swept for the caller detector" "rc=$rc: $out"
noflows FS-GG/.github

# The reverse direction (#503). A repo that really wires the full-union gate and never rosters it is
# invisible to every fabric that iterates the roster — including this audit's forward check, which
# starts from the declaration that is missing.
DRIFTCALLER="$WORK/driftcaller.yml"
mkreg2 "$DRIFTCALLER" "labels, skill-union" "labels" "$CALLERCAP"
wire_caller FS-GG/FS.GG.SDD
wire_caller FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$DRIFTCALLER" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering adopts .* does not declare 'receives: skill-union'" \
    && printf '%s' "$out" | grep -q '1 unrostered adopter'; } \
  && ok "caller: fully wired but unrostered adopter -> drift" \
  || bad "reverse-direction caller adoption must remain visible" "rc=$rc: $out"

# An unrostered repo that has the CALL but not the trigger is drift too: it is an attempted adoption,
# and leaving it unrostered makes the eventual second half invisible to the fabric.
wire_caller FS-GG/FS.GG.Rendering no-agents-trigger
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$DRIFTCALLER" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q '1 unrostered adopter'; } \
  && ok "caller: an unrostered call without its trigger is still drift" \
  || bad "partial unrostered caller adoption must fail loud" "rc=$rc: $out"

# A `product`-only unrostered caller is NOT drift: auditing a generated product is a different, legitimate
# thing (FS.GG.Templates#49), and reporting it as an unrostered adopter of the committed-root capability
# would make the drift leg cry wolf on correct behaviour — the one thing that teaches an operator to skip it.
wire_caller FS-GG/FS.GG.Rendering product
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$DRIFTCALLER" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '0 unrostered adopter'; } \
  && ok "caller: a generated-product call is not an unrostered adopter of the committed-root capability" \
  || bad "the drift leg must not fire on a legitimate generated-product audit" "rc=$rc: $out"

# `receivers: none` stays falsifiable for this kind too: a real adopter contradicts the recorded claim.
# `coordination-kit` rides along ONLY so the roster is not wholly unsweepable: with `labels` pushed and
# `skill-union` recorded as receiverless, the aggregate backstop (leg 2a) would die at exit 3 before the
# drift scan ever ran, and this leg would pass for the wrong reason.
NONECALLER="$WORK/nonecaller.yml"
mkreg2 "$NONECALLER" "labels, coordination-kit" "labels, coordination-kit" \
  "- { id: skill-union, caller: skill-union, receivers: none, reason: nobody has wired the receiver caller yet }" \
  "- { id: coordination-kit, workflow: coordination-coherence.yml }"
wire_caller FS-GG/FS.GG.SDD
printf '%s\n%s\n' "skill-union.yml" "coord.yml" > "$FIX/FS-GG__FS.GG.SDD.list"
printf 'jobs:\n  j1:\n    uses: FS-GG/.github/.github/workflows/coordination-coherence.yml@main\n' > "$FIX/FS-GG__FS.GG.SDD/coord.yml"
wire FS-GG/FS.GG.Rendering
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$NONECALLER" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "records capability 'skill-union' as having NO receivers"; } \
  && ok "caller: 'receivers: none' + a real adopter -> the recorded claim is falsified, not trusted" \
  || bad "a false 'receivers: none' must go red for the caller kind too" "rc=$rc: $out"

# Detector state must be capability-LOCAL, the same regression the materializer legs pin: put the caller
# row FIRST and a workflow row LAST, and the caller's missing half must still be diagnosed exactly.
MULTICALLER="$WORK/multicaller.yml"
mkreg2 "$MULTICALLER" "labels, skill-union, coordination-kit" "labels, skill-union, coordination-kit" \
  "$CALLERCAP" "- { id: coordination-kit, workflow: coordination-coherence.yml }"
wire_caller FS-GG/FS.GG.SDD
printf '%s\n%s\n' "skill-union.yml" "coord.yml" > "$FIX/FS-GG__FS.GG.SDD.list"
printf 'jobs:\n  j1:\n    uses: FS-GG/.github/.github/workflows/coordination-coherence.yml@main\n' > "$FIX/FS-GG__FS.GG.SDD/coord.yml"
wire_caller FS-GG/FS.GG.Rendering product
printf '%s\n%s\n' "skill-union.yml" "coord.yml" > "$FIX/FS-GG__FS.GG.Rendering.list"
printf 'jobs:\n  j1:\n    uses: FS-GG/.github/.github/workflows/coordination-coherence.yml@main\n' > "$FIX/FS-GG__FS.GG.Rendering/coord.yml"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$MULTICALLER" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'GENERATED product' \
    && printf '%s' "$out" | grep -q "ok: FS-GG/FS.GG.Rendering wires .*coordination-coherence.yml"; } \
  && ok "caller: the detector id is capability-local alongside a workflow row" \
  || bad "caller state leaked from the capability parse loop" "rc=$rc: $out"

# ---------------------------------------------------------------------------------------------------
# (26b) THE YAML DIALECTS. Every leg below is legal YAML that GitHub Actions accepts, and every one of
#       the first five walked straight through the line-oriented scanner this detector replaced — while
#       all eighteen legs above passed. That is why the detector now PARSES: `paths:` and `with:` are
#       structure, and the question "what was the assertion pointed at?" is a question about structure.
wire_caller FS-GG/FS.GG.SDD                       # the honest caller, unchanged, in every leg below

# A flow `with:` hides the key mid-line. This is FS.GG.Templates' legitimate generated-product call being
# certified as the committed-root gate — the fail-open the whole `caller:` kind exists to close.
for mode in flow-with-product with-before-uses flow-with-narrow-roots expression-product; do
  wire_caller FS-GG/FS.GG.Rendering "$mode"
  out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'skill-union'"; } \
    && ok "caller/yaml: '$mode' does not satisfy the call half" \
    || bad "caller/yaml: '$mode' must not certify the committed-root gate" "rc=$rc: $out"
done

# PROSE IS NOT WIRING, and an inline comment is prose. A repo that DELETED its caller and left the line
# in a trailing comment must read as a gap — the whole-line `commented` leg above never covered this.
for mode in inline-comment-uses step-level-uses local commented; do
  wire_caller FS-GG/FS.GG.Rendering "$mode"
  out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'nothing in its workflows calls'; } \
    && ok "caller/yaml: '$mode' is not a job-level call of the authority's workflow" \
    || bad "caller/yaml: '$mode' must not satisfy the call half" "rc=$rc: $out"
done

# Flow-style triggers, and a `!`-negated or lookalike filter: each is a gate that does not run when a
# root changes, and each read as armed under the scanner.
for mode in flow-on-narrow flow-pr-narrow negated-root archive-lookalike pr-target; do
  wire_caller FS-GG/FS.GG.Rendering "$mode"
  out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q "FS-GG/FS.GG.Rendering receives 'skill-union'"; } \
    && ok "caller/yaml: '$mode' is not an armed gate over the declared roots" \
    || bad "caller/yaml: '$mode' must not satisfy the trigger half" "rc=$rc: $out"
done

# ...and the mirror: every one of these IS armed, and reporting it as a gap would tell an operator to add
# a filter that is already there — the #320 lesson (a red whose remedy is already satisfied teaches that
# the gate is broken). `broad-paths` and `ignore-nonroot` are WIDER than covered, which passes.
for mode in paths-at-key-indent inline-paths alias-paths broad-paths ignore-nonroot two-calls crlf; do
  wire_caller FS-GG/FS.GG.Rendering "$mode"
  out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)'; } \
    && ok "caller/yaml: '$mode' is a correctly wired gate and audits green" \
    || bad "caller/yaml: '$mode' must not be reported as a gap" "rc=$rc: $out"
done

# A workflow GitHub cannot parse cannot be the live gate — but it must not take the repo's verdict with
# it either. The sibling workflow in the same repo IS the gate, and the verdict is about that.
wire_caller FS-GG/FS.GG.Rendering unparseable
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$CALLERREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)' \
    && printf '%s' "$out" | grep -q '0 undetermined'; } \
  && ok "caller/yaml: an unparseable sibling workflow yields no caller token and no no-verdict" \
  || bad "an unparseable workflow must not decide the repo's verdict" "rc=$rc: $out"

# An unsupported caller id is a PERMANENT no-verdict (exit 3), not a gap: it is a deterministic read of
# the roster, so a re-run reproduces it and only a commit fixes it. `repos.sh validate` refuses it too —
# this script must not INFER that validate ran.
BADCALLER="$WORK/badcaller.yml"
mkreg2 "$BADCALLER" "labels, skill-union" "labels, skill-union" \
  "- { id: skill-union, caller: bogus-detector, reason: not a supported caller id }"
out="$(PATH="$STUB:$PATH" bash "$AUDIT" --registry "$BADCALLER" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'unsupported caller detector'; } \
  && ok "caller: an unsupported caller id -> exit 3 (permanent), never a fabricated gap" \
  || bad "an unknown caller detector must be a permanent no-verdict" "rc=$rc: $out"

# --- THE SPARSE-CHECKOUT CLOSURE SWEEP, ACROSS THE ROSTER (#1529, closing #1522's reach) ---------
#
# #1522 shipped a gate that reds on a cross-repo `sparse-checkout` which ENUMERATES files instead of
# taking an anchored directory — for ONE repository, whichever `--root` names. A sibling that
# hand-rolls the same checkout against FS-GG/.github is a live instance of the class the gate was
# built to end, and this repo's CI stays green over it. #1529 gave the rule reach; these legs are
# what make that claim checkable.
#
# EVERY LEG FAILS ON ITS SUBJECT. A fixture whose synthetic roster contains only well-formed
# checkouts proves nothing at all — it is green before the feature exists and green after it is
# deleted — which is the criterion #1529 wrote down and the reason each mode below is asserted RED
# with its own message, not merely "rc != 0".
#
# wire_sparse <repo> <mode> — the repo wires coordination-kit in `coord.yml` (so the participation
# audit stays CLEAN and any exit 1 is attributable to the sweep alone) and hand-rolls a cross-repo
# checkout in `fetch.yml`.
#
# The default is CONE MODE, because that is `actions/checkout`'s default and therefore what a sibling
# would really write. Under cone mode a file name is simply a directory that turns out to be empty,
# so no rule that reads the pattern STRING can see it — only rule (4), by asking a git index. The
# audit holds exactly one index, the authority's, which is why the `cone-file` leg below is the
# motivating case and not a curiosity: it is the shape a sibling reaches for, against the one
# repository this sweep can still resolve.
wire_sparse() {
  clearfail "$1"; local slug="${1//\//__}" mode="$2"
  mkdir -p "$FIX/$slug"; printf '%s\n%s\n' "coord.yml" "fetch.yml" > "$FIX/$slug.list"
  printf 'jobs:\n  j1:\n    uses: FS-GG/.github/.github/workflows/coordination-coherence.yml@main\n' \
    > "$FIX/$slug/coord.yml"
  local head='jobs:
  fetch:
    steps:
      - uses: actions/checkout@v7
      - uses: actions/checkout@v7
        with:
          repository: FS-GG/.github
'
  case "$mode" in
    # --- NON-CONE. The patterns are gitignore expressions, so rules (1)-(3) all apply.
    clean)       printf '%s          sparse-checkout-cone-mode: false\n          sparse-checkout: |\n            /scripts/\n' "$head" ;;
    enumerated)  printf '%s          sparse-checkout-cone-mode: false\n          sparse-checkout: |\n            /scripts/check-foo.py\n' "$head" ;;
    unanchored)  printf '%s          sparse-checkout-cone-mode: false\n          sparse-checkout: |\n            scripts/\n' "$head" ;;
    globbed)     printf '%s          sparse-checkout-cone-mode: false\n          sparse-checkout: |\n            /scripts/check-*.py\n' "$head" ;;
    # A FOLDED block scalar joins its lines with a space, so the runner receives ONE pattern
    # containing whitespace and git matches nothing. Written as the folded scalar itself, not as a
    # pre-joined string, so the leg fails if the audit ever stops mirroring how the action splits.
    folded)      printf '%s          sparse-checkout-cone-mode: false\n          sparse-checkout: >\n            /scripts/\n            /docs/\n' "$head" ;;

    # --- CONE MODE (the action's default; no flag at all). Rules (1)-(2) do not apply — git reads
    #     these as rooted directory prefixes, not gitignore patterns — so ONLY rule (4) is left.
    cone-file)   printf '%s          sparse-checkout: |\n            scripts/check-foo.py\n' "$head" ;;
    cone-dir)    printf '%s          sparse-checkout: |\n            scripts\n' "$head" ;;
    # --- CONE MODE ACROSS TWO SIBLINGS, NEITHER OF WHICH IS THE AUTHORITY (#1556). This is the pair
    #     #1529 left ungraded and #1556 closes: the audit holds neither tree, so rule (4) can only
    #     run by FETCHING the fetched repository's index from the API. Both sides are expressed,
    #     because a fixture that only had the clean one would be green before the feature existed.
    cone-foreign)      printf 'jobs:\n  fetch:\n    steps:\n      - uses: actions/checkout@v7\n        with:\n          repository: FS-GG/FS.GG.SDD\n          sparse-checkout: |\n            src/FS.GG.Contracts\n' ;;
    cone-foreign-file) printf 'jobs:\n  fetch:\n    steps:\n      - uses: actions/checkout@v7\n        with:\n          repository: FS-GG/FS.GG.SDD\n          sparse-checkout: |\n            src/FS.GG.Contracts/Contracts.fs\n' ;;
    # A sibling that spells the repository in a DIFFERENT CASE fetches the same repository GitHub
    # does, so it must be graded the same way. If the roster match were case-sensitive this would
    # silently fall off the roster and lose rule (4) over a capitalisation.
    cone-foreign-case) printf 'jobs:\n  fetch:\n    steps:\n      - uses: actions/checkout@v7\n        with:\n          repository: fs-gg/fs.gg.sdd\n          sparse-checkout: |\n            src/FS.GG.Contracts/Contracts.fs\n' ;;
    # OFF THE ROSTER. The roster is the boundary of what this audit may claim to know, so this is
    # UNGRADED and says so — not a finding, and not a no-verdict either: nothing FAILED.
    cone-offroster)    printf 'jobs:\n  fetch:\n    steps:\n      - uses: actions/checkout@v7\n        with:\n          repository: FS-GG/Not.On.The.Roster\n          sparse-checkout: |\n            src/FS.GG.Contracts/Contracts.fs\n' ;;
    # An EXPRESSION. The runner resolves it from values this audit cannot see, so there is no
    # repository to ask for a tree — a permanent boundary, never a read to retry.
    cone-expression)   printf 'jobs:\n  fetch:\n    steps:\n      - uses: actions/checkout@v7\n        with:\n          repository: ${{ inputs.upstream }}\n          sparse-checkout: |\n            src/FS.GG.Contracts/Contracts.fs\n' ;;

    # --- SHAPES THE RULE REFUSES rather than grades. A skip is how a coherence gate fails open.
    negated)     printf '%s          sparse-checkout-cone-mode: false\n          sparse-checkout: |\n            /scripts/\n            !/scripts/lib/\n' "$head" ;;
    blank)       printf '%s          sparse-checkout: ""\n' "$head" ;;

    # A checkout with no `sparse-checkout:` at all is a FULL CLONE. It under-fetches nothing, so it
    # is not a subject — and redding it would be the sweep arguing for its own subject's survival.
    fullclone)   printf '%s' "$head" ;;
    *) echo "wire_sparse: unknown mode '$mode'" >&2; return 1 ;;
  esac > "$FIX/$slug/fetch.yml"
}

# THE RULE IS SHARED, NOT RETYPED (#1529 criterion 2) — and this is how that is proven rather than
# asserted in a comment. The diagnostic wording below is UNIQUE to
# scripts/check-sparse-checkout-closure.py: if repos-audit.sh ever grows its own copy of the rule,
# either this leg fails (the phrase appears in both files) or the wording drifts apart and the legs
# that grep for it fail. A second copy of a rule is the defect #1522 exists to end, one level up.
RULE_PHRASE='ENUMERATES A FILE'
{ grep -qF "$RULE_PHRASE" "$HERE/../../scripts/check-sparse-checkout-closure.py" \
    && ! grep -qF "$RULE_PHRASE" "$AUDIT"; } \
  && ok "sparse: the rule's wording lives ONLY in check-sparse-checkout-closure.py — the audit imports it" \
  || bad "the sparse rule must not be retyped into repos-audit.sh" \
         "'$RULE_PHRASE' must appear in the rule file and NOT in $AUDIT"

# The clean baseline. It must be green AND must say what it examined: a sweep that reports the same
# "ok" whether it graded ten repos or zero is indistinguishable from a collapsed roster (criterion 5).
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering clean
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'sparse-checkout closure (#1529)' \
    && printf '%s' "$out" | grep -q 'across 3 of 3 rostered repo(s) (0 NOT audited' \
    && printf '%s' "$out" | grep -q '0 finding(s), 0 refusal(s)'; } \
  && ok "sparse: an anchored literal directory passes, and the sweep reports how many repos it read" \
  || bad "a clean cross-repo sparse-checkout must pass and be legible" "rc=$rc: $out"

# The three findings #1529 names, each asserted on its own message. `rc -eq 1` alone would be
# satisfied by an unrelated wiring gap, so every leg pins the sentence AND the repo/workflow it names.
#
#   enumerated  the defect itself: a hand-maintained copy of the fetched script's dependency list,
#               in a file that cannot execute the thing it lists (#1510 killed every receiver at load)
#   unanchored  gitignore semantics: a bare `scripts/` matches a directory of that name AT ANY DEPTH
#   globbed     a MATCH EXPRESSION whose result nobody can read off the workflow file
#   folded      a `>` scalar joins its lines, so the runner gets one pattern that matches nothing
for spec in \
  "enumerated:ENUMERATES A FILE" \
  "unanchored:is NOT ANCHORED" \
  "globbed:glob metacharacter" \
  "folded:contains whitespace"
do
  mode="${spec%%:*}"; phrase="${spec#*:}"
  wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering "$mode"
  out="$(run 2>&1)" && rc=0 || rc=$?
  { [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -qF "$phrase" \
      && printf '%s' "$out" | grep -q 'FS.GG.Rendering/.github/workflows/fetch.yml' \
      && printf '%s' "$out" | grep -q '0 gap(s)'; } \
    && ok "sparse: '$mode' in a SIBLING repo is a finding, named at its own workflow" \
    || bad "sparse '$mode' must red on the sibling, not on a wiring gap" "rc=$rc: $out"
done

# THE MOTIVATING CASE. Cone mode is the action's default, so the obvious hand-rolled fetch of one
# script names it with no flags at all — and nothing about the pattern STRING says it is a file.
# Rule (4) is the only thing that can see it, and it can only run against a tree this audit holds.
# The authority's is that tree, which is exactly the repository a sibling would be fetching.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-file
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'selects no tracked path' \
    && printf '%s' "$out" | grep -q 'FS.GG.Rendering/.github/workflows/fetch.yml'; } \
  && ok "sparse: a CONE-mode file name against the authority reds — rule (4) reaches across repos" \
  || bad "the cone-mode enumeration a sibling would really write must red" "rc=$rc: $out"

# ...and its mirror, so the leg above is not passing because everything reds. A real directory in
# cone mode is the correct spelling and must be graded, not merely tolerated.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-dir
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '0 finding(s)' \
    && printf '%s' "$out" | grep -q '0 step(s) UNGRADED'; } \
  && ok "sparse: a cone-mode DIRECTORY against the authority is graded and clean" \
  || bad "a correct cone-mode directory must not be reported as a finding" "rc=$rc: $out"

# --- RULE (4) FOR A REPOSITORY THIS AUDIT DOES NOT HOLD (#1556) ----------------------------------
#
# #1529's residue, and the DEFAULT shape of the defect: a sibling cone-fetching from another sibling.
# The local gate cannot see it (wrong repo) and, until now, neither could the sweep (no index). Rule
# (4) is the only rule that can, because in cone mode nothing about the pattern STRING distinguishes
# a file from a directory — so the audit fetches the fetched repository's tree from the API.
#
# CRITERION 1. A cone-mode cross-repo `sparse-checkout` naming a FILE in a rostered repo other than
# the authority is a FINDING, not an UNGRADED note. This is the leg the whole item exists for, and it
# is asserted on the rule's own sentence — which lives in check-sparse-checkout-closure.py, so a
# repos-audit.sh that grew a private copy of rule (4) would break the sharing leg above instead.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-foreign-file
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'selects no tracked path' \
    && printf '%s' "$out" | grep -q 'FS.GG.Rendering/.github/workflows/fetch.yml' \
    && printf '%s' "$out" | grep -q '0 step(s) UNGRADED' \
    && printf '%s' "$out" | grep -q 'ran for 1 of 1 graded cross-repo step(s)' \
    && printf '%s' "$out" | grep -q '0 gap(s)'; } \
  && ok "sparse: a cone-mode FILE fetched from one sibling by another is a FINDING (#1556)" \
  || bad "the cross-repo cone-mode enumeration must red, not go ungraded" "rc=$rc: $out"

# ...and its mirror, so the leg above is not passing because everything reds. The same fetch of the
# containing DIRECTORY is the correct spelling and must be GRADED — the number that says rule (4)
# actually ran is pinned, because "0 finding(s)" alone is also what an ungraded step produces.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-foreign
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '0 finding(s)' \
    && printf '%s' "$out" | grep -q '0 step(s) UNGRADED' \
    && printf '%s' "$out" | grep -q 'ran for 1 of 1 graded cross-repo step(s)' \
    && ! printf '%s' "$out" | grep -q 'UNGRADED FS-GG'; } \
  && ok "sparse: the same cross-repo fetch of a real DIRECTORY is graded and clean (#1556)" \
  || bad "a correct cross-repo cone-mode directory must be graded, not merely tolerated" "rc=$rc: $out"

# GitHub resolves `owner/name` case-insensitively, so a sibling that lower-cases the repository
# fetches the very same tree. Losing rule (4) over a capitalisation would be a silent fail-open.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-foreign-case
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'selects no tracked path' \
    && printf '%s' "$out" | grep -q 'ran for 1 of 1 graded cross-repo step(s)'; } \
  && ok "sparse: a differently-CASED repository is still on the roster and still graded (#1556)" \
  || bad "the roster match must be case-insensitive, as GitHub's own resolution is" "rc=$rc: $out"

# CRITERION 2. A tree that cannot be READ leaves those steps UNGRADED with the API's reason named,
# and makes the RUN a no-verdict (exit 2) rather than a green. "I could not look" is not "I looked
# and it is fine" (#266) — and note the repo still LISTS and its workflows still read, so the audit
# reaches the grading and only then loses the index: the partial-read shape, not an outage.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-foreign-file
: > "$FIX/FS-GG__FS.GG.SDD.failtree"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q '1 step(s) UNGRADED' \
    && printf '%s' "$out" | grep -q 'HTTP 403' \
    && printf '%s' "$out" | grep -q 'ran for 0 of 1 graded cross-repo step(s)' \
    && printf '%s' "$out" | grep -q 'could not read the git tree behind 1 cross-repo' \
    && printf '%s' "$out" | grep -q '0 finding(s)'; } \
  && ok "sparse: an unreadable git tree is UNGRADED and a NO-VERDICT (exit 2), never a green (#1556)" \
  || bad "an unreadable tree must not round to a clean cross-repo checkout" "rc=$rc: $out"
rm -f "$FIX/FS-GG__FS.GG.SDD.failtree"

# ...and the same, from the other direction: a tree the API says is not there at all. A 404 is
# grouped with the unreachable deliberately — there is no such thing as a repository with no index we
# may then reason about, so "the tree is not there" is only ever "we do not have the tree".
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-foreign-file
: > "$FIX/FS-GG__FS.GG.SDD.gonetree"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q '1 step(s) UNGRADED' \
    && printf '%s' "$out" | grep -q 'could not read the git tree behind 1 cross-repo'; } \
  && ok "sparse: a 404 on the git tree is a no-verdict too, not an answer about the tree (#1556)" \
  || bad "a missing tree must fail closed like an unreachable one" "rc=$rc: $out"
rm -f "$FIX/FS-GG__FS.GG.SDD.gonetree"

# THE TRUNCATED TREE — the failure that arrives at HTTP 200 and looks like success. The endpoint
# returns a PARTIAL array for a large repository and sets `truncated`. Believing it would make
# directories that DO exist read as selecting nothing, manufacturing findings against innocent
# receivers; so it is refused, and refused in the no-verdict direction rather than the finding one.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-foreign
mktree true "$FIX/FS-GG__FS.GG.SDD.tree"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'TRUNCATED' \
    && printf '%s' "$out" | grep -q '1 step(s) UNGRADED' \
    && printf '%s' "$out" | grep -q '0 finding(s)'; } \
  && ok "sparse: a TRUNCATED tree is a no-verdict, never a fabricated 'selects nothing' (#1556)" \
  || bad "a partial tree must not be graded against" "rc=$rc: $out"
rm -f "$FIX/FS-GG__FS.GG.SDD.tree"

# THE ROSTER IS THE BOUNDARY. A fetch of a repository that is not rostered stays UNGRADED and says
# so — and it is NOT a no-verdict: nothing failed, the audit was simply never given that subject.
# That distinction is the leg: exit 0 with an UNGRADED line, not exit 2.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-offroster
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '1 step(s) UNGRADED' \
    && printf '%s' "$out" | grep -q 'not on this audit' \
    && printf '%s' "$out" | grep -q 'ran for 0 of 1 graded cross-repo step(s)' \
    && printf '%s' "$out" | grep -q '0 finding(s)' \
    && ! printf '%s' "$out" | grep -q 'could not read the git tree'; } \
  && ok "sparse: an OFF-ROSTER fetch is UNGRADED and named — a boundary, not a failure (#1556)" \
  || bad "an unrostered repository must be ungraded without making the run a no-verdict" "rc=$rc: $out"

# ...and it must not have cost an API call either. The roster is checked BEFORE the fetcher, so a
# workflow that reaches past the org does not spend the audit's rate budget proving it.
out="$(run_logged "$WORK/calls.offroster" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && ! grep -q '^tree' "$WORK/calls.offroster"; } \
  && ok "sparse: an off-roster fetch costs NO tree call — the roster is checked first (#1556)" \
  || bad "an unrostered repository must not be fetched" "rc=$rc: $(cat "$WORK/calls.offroster" 2>&1)"

# AN EXPRESSION IS A PERMANENT BOUNDARY, NOT A READ TO RETRY. `repository: ${{ inputs.upstream }}` is
# resolved by the runner from values this file cannot see. Ungraded, exit 0, and no fetch attempted —
# inventing a repository name out of an expression is how rule (4) would get run against the wrong
# tree entirely, which is worse than not running it.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-expression
out="$(run_logged "$WORK/calls.expr" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '1 step(s) UNGRADED' \
    && printf '%s' "$out" | grep -q 'not a literal `owner/name`' \
    && printf '%s' "$out" | grep -q '0 finding(s)' \
    && ! grep -q '^tree' "$WORK/calls.expr"; } \
  && ok "sparse: an EXPRESSION repository is ungraded, costs no call, and is not a no-verdict (#1556)" \
  || bad "a non-literal repository must be a boundary, not a fetch" "rc=$rc: $out"

# CRITERION 5. NO EXTRA API CALL WHEN NO STEP FETCHES A NON-AUTHORITY REPO. A weekly audit must not
# gain ten round-trips for a hole that is usually empty, and the only honest way to check a claim
# about TRAFFIC is to count what the stub was asked for. `cone-file` fetches the AUTHORITY, whose
# tree this audit already holds — so it is graded (rule (4) ran, 1 of 1) and yet zero trees are
# fetched. Both halves matter: a lazy fetcher that never fires is also "zero calls".
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-file
out="$(run_logged "$WORK/calls.authority" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'ran for 1 of 1 graded cross-repo step(s)' \
    && ! grep -q '^tree' "$WORK/calls.authority"; } \
  && ok "sparse: a fetch of the AUTHORITY still costs no tree call — the fetch is lazy (#1556)" \
  || bad "the tree fetcher must not fire for a repository the audit already holds" \
         "rc=$rc: $(cat "$WORK/calls.authority" 2>&1)"

# ...and the same for a roster with no cross-repo checkout at all, which is the ordinary state of the
# org and the run that must stay cheapest.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(run_logged "$WORK/calls.none" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && ! grep -q '^tree' "$WORK/calls.none"; } \
  && ok "sparse: a roster with no cross-repo checkout fetches no tree at all (#1556 criterion 5)" \
  || bad "an empty subject set must cost nothing" "rc=$rc: $(cat "$WORK/calls.none" 2>&1)"

# ONE TREE PER REPOSITORY, NOT ONE PER WORKFLOW. The cache is what makes the reach affordable: three
# workflows in one repo all fetching the same sibling must produce exactly ONE tree call, or a roster
# of ten repos with a handful of cross-repo steps each quietly becomes dozens of round-trips.
#
# AND IT IS THE LEG THAT CAUGHT THE WORST BUG IN #1556, which is why it asserts the WORKFLOW COUNT
# and not just the call count. bash locals are dynamically scoped, and `sparse_grade`'s loop over the
# trees it wants sits inside `repo_calls`'s loop over a repository's workflows — so a bare
# `while read -r repo` assigned straight through to the repository being walked, and `read` leaves it
# EMPTY at EOF. Every workflow after the first cross-repo fetch was then requested from the empty
# repository, 404ed, and silently dropped: the sweep read two of four workflows and still reported
# success. Nothing about the call count would have shown it. `3 of 3 graded cross-repo step(s)` does,
# because steps two and three live in the workflows that went missing.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering cone-foreign-file
cp "$FIX/FS-GG__FS.GG.Rendering/fetch.yml" "$FIX/FS-GG__FS.GG.Rendering/fetch2.yml"
cp "$FIX/FS-GG__FS.GG.Rendering/fetch.yml" "$FIX/FS-GG__FS.GG.Rendering/fetch3.yml"
printf '%s\n%s\n%s\n%s\n' coord.yml fetch.yml fetch2.yml fetch3.yml > "$FIX/FS-GG__FS.GG.Rendering.list"
out="$(run_logged "$WORK/calls.cache" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && [ "$(grep -c '^tree' "$WORK/calls.cache")" -eq 1 ] \
    && printf '%s' "$out" | grep -q 'ran for 3 of 3 graded cross-repo step(s)'; } \
  && ok "sparse: the tree is fetched ONCE per repository, not once per workflow (#1556)" \
  || bad "the tree cache must survive across workflows" \
         "rc=$rc calls=$(grep -c '^tree' "$WORK/calls.cache" 2>/dev/null): $out"
rm -f "$FIX/FS-GG__FS.GG.Rendering/fetch2.yml" "$FIX/FS-GG__FS.GG.Rendering/fetch3.yml"

# A REFUSED SHAPE is a PERMANENT no-verdict (exit 3), not a finding (1) and not a rate limit (2).
# Negation makes ORDER significant, so "every pattern is a directory" stops being a sound reading of
# what gets fetched; a `sparse-checkout:` key that supplies no pattern is indistinguishable to the
# runner from three other spellings whose behaviours differ enormously. Both are refused by the
# shared rule, and the sweep must not launder either into a verdict it did not reach.
#
# THE STREAMS ARE READ SEPARATELY, AND THAT SEPARATION IS THE ASSERTION (#1599). The refusal SENTENCE
# belongs to the shared rule, so grepping for it proves only that some copy of it was printed; what
# proves the sweep CAUGHT the refusal rather than DIED on it is WHERE it comes out — the audit's own
# `::error:: … REFUSED a shape it cannot grade` annotation, on STDOUT. This leg merged the streams
# until #1599, and the `blank` case passed on the phrase as it appeared inside a Python TRACEBACK: the
# borrower caught `GateError` while the hoisted parse raised `lib.sparse.SparseRefusal`, the verdict
# program died mid-loop, and the leg stayed green for the entire life of the defect. That is the #266
# vacuous failure, in the fixture asserting a #266 rule — so three things are pinned, not one.
#
#   1. the phrase is ON the refusal annotation, not merely somewhere in the output
#   2. `sparse_grade`'s generic laundering line — the one a dead subprocess produces — is ABSENT
#   3. stderr carries no traceback at all
#
# Measured against the unfixed script, EACH of the three reds on its own — the annotation carries
# "could not be evaluated against this workflow" instead of the phrase, the laundering line is
# present, and the traceback is on stderr. They are kept as three rather than collapsed to one
# because they fail for three different reasons and a future partial regression need only trip one:
# (1) is about the operator getting the RULE's sentence, (2) about `sparse_grade` not laundering a
# death into a verdict, (3) about the program not dying in the first place.
for spec in "negated:negated sparse pattern" "blank:supplies no pattern"; do
  mode="${spec%%:*}"; phrase="${spec#*:}"
  wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering "$mode"
  out="$(run 2>"$WORK/refusal.err")" && rc=0 || rc=$?
  err="$(cat "$WORK/refusal.err")"
  { [ "$rc" -eq 3 ] \
      && printf '%s' "$out" | grep -F 'REFUSED a shape it cannot grade' | grep -qF "$phrase" \
      && ! printf '%s' "$out" | grep -qF 'could not be evaluated against this workflow' \
      && ! printf '%s' "$err" | grep -qF 'Traceback (most recent call last)'; } \
    && ok "sparse: '$mode' is a PERMANENT no-verdict (exit 3) whose REASON reaches the operator as a refusal record, never as a traceback" \
    || bad "a refused sparse shape must be exit 3 and carry the shared rule's own sentence on its refusal annotation" \
           "rc=$rc: out=$out ||| err=$err"
done

# AND THE REFUSED WORKFLOW WAS STILL EXAMINED (#1599 criterion 2). A refusal is a step the sweep
# LOOKED AT and could not read, which is not the same fact as a roster with no cross-repo checkout in
# it — #266 exists to keep those two apart. When the verdict program died, the `counts` record never
# printed, `cross` was lost, and the sweep reported "found NO cross-repo `actions/checkout` at all"
# over a roster that demonstrably had one. The exit code was right and the LEDGER was false, which is
# the harder half to notice; this pins the sentence that gave it away.
#
# ASSERTED POSITIVELY, not just as the absence of the "NO cross-repo" sentence. `sp_cross` is summed
# across the WHOLE roster, so a bare negative would go vacuous the day any other rostered repo in
# this fixture's state happens to carry a cross-repo checkout — the leg would still pass, while
# asserting nothing about the refused step. Naming the counts the else-branch prints keeps it a
# statement about THIS step: one cross-repo checkout seen, none of them graded, one refusal.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering blank
out="$(run 2>/dev/null)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && ! printf '%s' "$out" | grep -qF 'NO cross-repo' \
    && printf '%s' "$out" | grep -qF '0 of 1 cross-repo checkout(s) fully graded' \
    && printf '%s' "$out" | grep -qF '1 refusal(s)'; } \
  && ok "sparse: a REFUSED step is one the sweep examined — counted as a cross-repo checkout, graded 0, never 'nothing found'" \
  || bad "a refused step must still be counted as a cross-repo checkout the sweep examined" "rc=$rc: $out"

# A cross-repo checkout with no `sparse-checkout:` is a full clone. It under-fetches nothing, so it
# is the class permanently foreclosed — the best end state there is — and redding it would be the
# sweep arguing for its own subject's survival.
wire FS-GG/FS.GG.SDD; wire_sparse FS-GG/FS.GG.Rendering fullclone
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '1 full clone(s) not graded' \
    && printf '%s' "$out" | grep -q '0 finding(s)'; } \
  && ok "sparse: a full cross-repo clone is counted, not graded, and never a finding" \
  || bad "a full clone must not be reported as under-fetching" "rc=$rc: $out"

# A REPO THAT COULD NOT BE READ IS A NO VERDICT, NEVER A GREEN (#266/#320, criterion 3). The audit
# already exits 2 for it; what this pins is that the sweep's own REPORT does not quietly count the
# unread repo as audited — "3 of 3, clean" over a run that read two is the fail-open in miniature.
wire FS-GG/FS.GG.SDD; unreachable FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'across 2 of 3 rostered repo(s) (1 NOT audited'; } \
  && ok "sparse: an unreadable repo is excluded from the swept count, not counted as clean" \
  || bad "the sweep must distinguish 'audited, clean' from 'could not audit'" "rc=$rc: $out"

# AND THE SWEEP NEVER CLAIMS A GREEN IT DID NOT EARN. When the roster contains no cross-repo
# `actions/checkout` at all, the sweep examined nothing — and a report that reads the same as a real
# audit is the #266 shape this whole item is about. It must say so in its own words. (It is not an
# ERROR: every rostered repo genuinely reaching the authority through reusable workflows is a
# legitimate, and desirable, state of the org.)
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q 'found NO cross-repo' \
    && printf '%s' "$out" | grep -q 'that is not a clean bill'; } \
  && ok "sparse: a roster with no cross-repo checkout reports that it asserted NOTHING" \
  || bad "an empty subject set must not render as a clean sweep" "rc=$rc: $out"

# AND THE BORROWED RULE FAILS CLOSED WHEN IT IS NOT THERE. Importing across a file boundary makes
# `sparse_steps`/`patterns_of`/`cone_mode_of`/`grade_pattern`/`origin_repository`/`tracked_paths`/
# `GateError`/`SparseRefusal` an interface, and #1530 HAS SINCE hoisted the parse out of that file —
# its criterion 2 was that neither caller keeps a private copy. So the interesting question was never
# whether sharing works today; it is what happens on the day the far side moves.
#
# It moved, and #1599 is what that cost: `SparseRefusal` was not on this list, the borrower kept
# catching `GateError`, and the drift was invisible to the name check because no symbol DISAPPEARED —
# only the exception's identity changed. The list cannot catch that shape on its own, but a
# no-verdict type that is not even NAMED is one nothing downstream is obliged to keep working, so it
# is named now and asserted below.
#
# The answer must be a loud no-verdict, never a quiet green over ten repositories — a gate that
# reports clean because its rule went missing is #266 in its purest form. Both legs run a COPY of the
# shipped script from a sandbox `scripts/`, because the rule's location is derived from the script's
# own (there is no flag to point it elsewhere, deliberately: a flag would be a second place to say
# where the rule lives).
SPARSEBOX="$WORK/sparsebox"; mkdir -p "$SPARSEBOX/scripts/lib"
cp "$AUDIT" "$SPARSEBOX/scripts/repos-audit.sh"
cp "$HERE/../../scripts/lib/args.sh" "$SPARSEBOX/scripts/lib/args.sh"
# The kit-pin sweep borrows fsgg_feed.py by the same mechanism, and asserts it at the same place. The
# sandbox needs it so THESE legs still fail on the symbol they are about; its own absence gets its
# own leg below.
cp "$HERE/../../scripts/fsgg_feed.py" "$SPARSEBOX/scripts/fsgg_feed.py"
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering

# (a) the rule file is GONE. Caught up front, beside the `gh` and `python3` checks, because a missing
#     dependency is a permanent no-verdict about the audit and not a fact about anybody's tree.
out="$(PATH="$STUB:$PATH" bash "$SPARSEBOX/scripts/repos-audit.sh" \
        --registry "$REG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'sparse-checkout closure rule is not at' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "sparse: a MISSING rule file is a permanent no-verdict, not a clean org" \
  || bad "the borrowed rule's absence must fail closed" "rc=$rc: $out"

# (b) the rule file is THERE but no longer exposes what the audit borrows — #1530's hoist, modelled.
#     The diagnostic must NAME the missing symbols and the file, so whoever lands that refactor gets a
#     sentence rather than an AttributeError from inside a loop.
printf '"""A rule module that no longer exposes the borrowed names (models #1530s hoist)."""\n' \
  > "$SPARSEBOX/scripts/check-sparse-checkout-closure.py"
out="$(PATH="$STUB:$PATH" bash "$SPARSEBOX/scripts/repos-audit.sh" \
        --registry "$REG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
#     `SparseRefusal` is named explicitly alongside `grade_pattern` (#1599 criterion 3): the
#     no-verdict type the borrower must catch is part of the borrowed interface, and a leg that only
#     greps for a GRADING symbol would go on passing if it silently dropped off the list again.
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'no longer exposes' \
    && printf '%s' "$out" | grep -q 'grade_pattern' \
    && printf '%s' "$out" | grep -q 'SparseRefusal' \
    && printf '%s' "$out" | grep -q 'REFUSED a shape it cannot grade' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "sparse: a rule that lost the borrowed symbols reds and names them — including the no-verdict type" \
  || bad "a moved rule must fail closed and say which symbols went" "rc=$rc: $out"

# --- THE KIT-PIN FRESHNESS SWEEP (#1540, #1560 criterion 4) --------------------------------------
#
# THE ONE THING THESE LEGS EXIST TO PROVE: the sweep REDS ON A GENUINELY STALE RECEIVER. A fixture
# that only walks the happy path proves nothing about a freshness gate — the whole failure mode
# #1540 is about is a check that reads green over something it cannot see, and a green-only fixture
# is that same defect one level up. So every leg below that asserts "current -> green" has a sibling
# that asserts "behind -> exit 1, naming the repo, the pin and the published version".
#
# The comparand comes from the LOCAL flat-container at $FSGG_NUGET_ORG_BASE (see the top of this
# file). That is not a stub of the reader: fsgg_feed's real URL construction, real urllib fetch, real
# JSON parse, real prerelease filter and real version ordering all run.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering

# (1) Both receivers pin the published version -> green, and the report NAMES the comparand.
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q "newest stable FS.GG.Kit published on nuget.org is $KIT_PUBLISHED" \
    && printf '%s' "$out" | grep -q '2 current'; } \
  && ok "kit-pin: every receiver on the published kit -> green, and the comparand is named" \
  || bad "a current roster must pass and say what it compared against" "rc=$rc: $out"

# (2) THE LOAD-BEARING LEG. One receiver is two minors behind — the real FS.GG.Templates/SDD state on
#     2026-07-27 — and NOTHING about its wiring changed. It must red HERE, without that repo pushing.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
pin FS-GG/FS.GG.Rendering 0.6.0
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'kit-pin freshness — FS-GG/FS.GG.Rendering pins FS.GG.Kit 0.6.0' \
    && printf '%s' "$out" | grep -q "but $KIT_PUBLISHED is published" \
    && printf '%s' "$out" | grep -q '1 current, 1 stale' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired, and every'; } \
  && ok "kit-pin: a STALE receiver reds here, naming its pin and the published version" \
  || bad "a receiver behind the published kit must be a finding" "rc=$rc: $out"

# (3) The stale receiver is FULLY WIRED and its workflows are untouched. This pins the thing that
#     makes the sweep worth having: wiring and freshness are different questions, and a repo can
#     answer the first perfectly while failing the second. Were the sweep folded into the wiring
#     detectors, this leg would report a gap and send someone to fix a workflow that is correct.
{ printf '%s' "$out" | grep -q 'ok: FS-GG/FS.GG.Rendering wires FS-GG/.github/.github/workflows/coordination-coherence.yml' \
    && printf '%s' "$out" | grep -q '2 wired, 0 gap(s)'; } \
  && ok "kit-pin: a stale receiver is still reported WIRED — freshness is not a wiring gap" \
  || bad "staleness must not be reported as a wiring gap" "$out"

# (4) The FS.GG.Templates shape: the pin rides an inline Version= on the PackageReference in
#     .config/kit/FS.GG.Kit.receiver.proj and there is NO Directory.Packages*.props at all. The
#     briefing for #1540 asserted this repo had "no pin found" because it looked only at the two
#     props files; a sweep that inherits that assumption reports the org's stalest receiver as
#     unpinned. Stale in THAT shape must red exactly as stale in a CPM shape does.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
pin_inline FS-GG/FS.GG.Rendering 0.6.0
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'FS-GG/FS.GG.Rendering pins FS.GG.Kit 0.6.0' \
    && printf '%s' "$out" | grep -q '.config/kit/FS.GG.Kit.receiver.proj -> 0.6.0'; } \
  && ok "kit-pin: an INLINE receiver-project pin is read, and reds when stale (the Templates shape)" \
  || bad "the inline no-CPM pin shape must be read like any other" "rc=$rc: $out"

# (5) …and the same shape, current, is green. Otherwise leg (4) would also pass on a sweep that
#     simply cannot read that file and calls every such repo stale.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
pin_inline FS-GG/FS.GG.Rendering "$KIT_PUBLISHED"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '2 current'; } \
  && ok "kit-pin: an INLINE receiver-project pin at the published version is green" \
  || bad "the inline shape must be able to pass, or leg (4) proves nothing" "rc=$rc: $out"

# (6) The hand-authored-CPM shape (net/audio): the pin is in the ROOT Directory.Packages.props.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
nopin FS-GG/FS.GG.Rendering; pin FS-GG/FS.GG.Rendering 0.7.0 root
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'Directory.Packages.props -> 0.7.0'; } \
  && ok "kit-pin: a root Directory.Packages.props pin is read, and reds when stale" \
  || bad "the hand-authored CPM shape must be read too" "rc=$rc: $out"

# (7) PRERELEASE ORDERING. The feed carries 0.10.0-preview.1, which sorts ABOVE 0.8.0 numerically. A
#     receiver on the newest STABLE must be green — a sweep that took the max of all versions would
#     demand a prerelease nobody should pin and red the entire org forever.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q "is $KIT_PUBLISHED;" \
    && ! printf '%s' "$out" | grep -q '0.10.0-preview'; } \
  && ok "kit-pin: the comparand is the newest STABLE, not the highest prerelease" \
  || bad "a prerelease must never become the version receivers are held to" "rc=$rc: $out"

# (8) A pin AHEAD of the feed does not resolve for anybody, so it is a finding and not a pass. The
#     naive comparison — `pin != published` treated as stale — would say the right thing for the
#     wrong reason; this asserts the diagnostic actually distinguishes the two directions.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
pin FS-GG/FS.GG.Rendering 0.9.0
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'AHEAD of the newest published' \
    && printf '%s' "$out" | grep -q 'does not resolve'; } \
  && ok "kit-pin: a pin ahead of the feed is a finding, and says so in its own words" \
  || bad "an unresolvable pin is not a current pin" "rc=$rc: $out"

# (9) A receiver that pins the kit NOWHERE this sweep can see is a REFUSAL (exit 3), never a pass.
#     This is the #266 leg: "I could not find this repo's pin" must not render as "this repo is
#     current". It is exit 3 and not 1 because nothing was asserted — the remedy is to teach the
#     sweep the shape or move the pin, not to bump a version.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
nopin FS-GG/FS.GG.Rendering
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'REFUSED a repo it cannot grade' \
    && printf '%s' "$out" | grep -q 'no FS.GG.Kit version literal' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired, and every'; } \
  && ok "kit-pin: a receiver pinning nowhere is REFUSED, never reported current" \
  || bad "an unlocatable pin must not read as a current one" "rc=$rc: $out"

# (10) Two pin files that DISAGREE. The effective version is then a restore-order accident, so there
#      is no single fact to grade and the sweep refuses rather than picking a winner silently.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
pin FS-GG/FS.GG.Rendering 0.6.0; pin FS-GG/FS.GG.Rendering "$KIT_PUBLISHED" root
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'pinned to more than one version'; } \
  && ok "kit-pin: contradictory pins are REFUSED, not silently resolved" \
  || bad "two disagreeing pins have no single answer" "rc=$rc: $out"

# (11) An UNREADABLE pin file is the retryable no-verdict, exactly as an unreadable workflow is. The
#      repo exists, we failed to read it, and a later run may well succeed — so it must not be
#      reported as either current or stale.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
: > "$FIX/FS-GG__FS.GG.Rendering.failpin"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'kit-pin freshness — FS-GG/FS.GG.Rendering' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired, and every'; } \
  && ok "kit-pin: an unreadable pin file is a RETRYABLE no-verdict, never a fabricated verdict" \
  || bad "an unread pin is not an answer" "rc=$rc: $out"
rm -f "$FIX/FS-GG__FS.GG.Rendering.failpin"

# (11b) A PARTIAL read must not be graded at all. The receiver project reads fine and carries a
#       CURRENT inline pin; the props read then fails. On the evidence held the repo looks current —
#       and the file we could not read may carry a different version, which is a refusal, not a
#       pass. So the repo must be reported ONLY as undetermined, and must NOT also appear as `ok`.
#       This is the leg for the bug that shipped in the first draft of this sweep: rows were appended
#       to the manifest as they were fetched, so a repo could be counted `ok` on half a read.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
pin_inline FS-GG/FS.GG.Rendering "$KIT_PUBLISHED"
: > "$FIX/FS-GG__FS.GG.Rendering.failpinprops"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] \
    && ! printf '%s' "$out" | grep -q 'ok: FS-GG/FS.GG.Rendering pins FS.GG.Kit'; } \
  && ok "kit-pin: a PARTIALLY read receiver is never graded — no 'ok' beside its own undetermined" \
  || bad "half a read must not produce a verdict" "rc=$rc: $out"
rm -f "$FIX/FS-GG__FS.GG.Rendering.failpinprops"
unpin FS-GG/FS.GG.Rendering

# (12) AN UNREADABLE FEED IS NOT A CLEAN SWEEP. Without a comparand every receiver would otherwise be
#      graded against nothing and pass — the exact shape of the bug this whole sweep is about, in the
#      sweep itself. Point the reader at a flat-container that is not there.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
out="$(FSGG_NUGET_ORG_BASE="file://$WORK/no-such-feed" run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'could not resolve the published FS.GG.Kit version' \
    && printf '%s' "$out" | grep -q 'NOTHING was asserted' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired, and every'; } \
  && ok "kit-pin: an unreadable FEED is a no-verdict — nobody is graded against nothing" \
  || bad "no comparand must never mean everybody passes" "rc=$rc: $out"

# (14) THE SWEEP'S SUBJECT MUST EXIST. `repos.sh list --receives <cap>` does not validate the id: an
#      unknown capability selects nothing and exits 0. So a roster that renamed or retired
#      `coordination-kit` would leave this sweep covering NOBODY — and the run would still end on
#      "every coordination-kit receiver pins the published FS.GG.Kit", exit 0, over receivers five
#      releases stale. This is the #503 shape one sweep over, and it is why the literal is asserted
#      against the roster's own capability list.
KITREG="$WORK/kitcap.yml"
cat > "$KITREG" <<YAML
schemaVersion: 5
updated: 2026-07-13
authority: FS-GG/.github
repos:
  - { id: .github,   full: FS-GG/.github,         role: authority, receives: [labels] }
  - { id: sdd,       full: FS-GG/FS.GG.SDD,       role: framework, receives: [labels, coord-kit] }
  - { id: rendering, full: FS-GG/FS.GG.Rendering, role: framework, receives: [labels, coord-kit] }
capabilities:
  - { id: coord-kit, workflow: coordination-coherence.yml }
$LABELS_CAP
YAML
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
pin FS-GG/FS.GG.SDD 0.1.0; pin FS-GG/FS.GG.Rendering 0.1.0
#      The run may legitimately still be exit 0 — the WIRING half really did pass — but the sentence
#      it ends on must not claim kit freshness it never looked at. That claim is what the earlier
#      draft printed over two receivers pinning 0.1.0, five stable releases behind.
out="$(PATH="$STUB:$PATH" REPOS_AUDIT_TRIES=1 REPOS_AUDIT_RETRY_DELAY=0 \
        bash "$AUDIT" --registry "$KITREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ printf '%s' "$out" | grep -q "declares no 'coordination-kit' capability" \
    && printf '%s' "$out" | grep -q 'NO kit pin was graded, so nothing is claimed' \
    && ! printf '%s' "$out" | grep -q 'pin the published FS.GG.Kit'; } \
  && ok "kit-pin: a sweep with no subject says so and claims NOTHING — never a green over nobody" \
  || bad "a sweep whose subject does not exist must not report everyone current" "rc=$rc: $out"
unpin FS-GG/FS.GG.SDD; unpin FS-GG/FS.GG.Rendering

# (15) A byte-copy receiver has no pin to grade and must not be REFUSED for it. `kit-delivery`
#      absent MEANS byte-copy per the roster, so grading every coordination-kit receiver would red
#      the daily audit forever on a correct roster the moment such a receiver is rostered.
BCREG="$WORK/bytecopy.yml"
cat > "$BCREG" <<YAML
schemaVersion: 5
updated: 2026-07-13
authority: FS-GG/.github
repos:
  - { id: .github,   full: FS-GG/.github,         role: authority, receives: [labels] }
  - { id: sdd,       full: FS-GG/FS.GG.SDD,       role: framework, receives: [labels, coordination-kit], kit-delivery: package }
  - { id: rendering, full: FS-GG/FS.GG.Rendering, role: framework, receives: [labels, coordination-kit] }
capabilities:
  - { id: coordination-kit, workflow: coordination-coherence.yml }
$LABELS_CAP
YAML
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
nopin FS-GG/FS.GG.Rendering        # a byte-copy receiver genuinely has no PackageReference
out="$(PATH="$STUB:$PATH" REPOS_AUDIT_TRIES=1 REPOS_AUDIT_RETRY_DELAY=0 \
        bash "$AUDIT" --registry "$BCREG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 0 ] && printf '%s' "$out" | grep -q '1 byte-copy, not graded' \
    && ! printf '%s' "$out" | grep -q 'REFUSED a repo it cannot grade'; } \
  && ok "kit-pin: a byte-copy receiver is EXCLUDED, not refused, and the exclusion is counted" \
  || bad "byte-copy delivery has no pin and must not red the audit" "rc=$rc: $out"
unpin FS-GG/FS.GG.Rendering

# (16) VersionOverride BEATS the central PackageVersion under CPM. A sweep reading only `Version`
#      grades the version that is NOT restored — a green over a stale receiver, which is this
#      sweep's own failure mode one attribute over.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
mkdir -p "$FIX/FS-GG__FS.GG.Rendering"
mkpin "$KIT_PUBLISHED" "$FIX/FS-GG__FS.GG.Rendering/Directory.Packages.local.props"
cat > "$FIX/FS-GG__FS.GG.Rendering/receiver.proj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <PackageReference Include="FS.GG.Kit" VersionOverride="0.6.0" />
  </ItemGroup>
</Project>
XML
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'pins FS.GG.Kit 0.6.0' \
    && printf '%s' "$out" | grep -q 'VersionOverride'; } \
  && ok "kit-pin: a VersionOverride WINS over the central pin, and reds when it is the stale one" \
  || bad "the version that is actually restored is the one that must be graded" "rc=$rc: $out"

# (17) An MSBuild property version is refused, and the message says the pin may be fine rather than
#      calling it malformed — the sweep cannot resolve it, which is a different claim.
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
mkdir -p "$FIX/FS-GG__FS.GG.Rendering"
cat > "$FIX/FS-GG__FS.GG.Rendering/Directory.Packages.local.props" <<'XML'
<Project>
  <ItemGroup>
    <PackageVersion Include="FS.GG.Kit" Version="$(FsggKitVersion)" />
  </ItemGroup>
</Project>
XML
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'not a single literal NuGet version' \
    && printf '%s' "$out" | grep -q 'may well be fine'; } \
  && ok "kit-pin: an MSBuild-property version is refused, and named as unreadable not malformed" \
  || bad "an unresolvable property is not a graded pin" "rc=$rc: $out"

# (18) The MSBuild 2003 namespace, `Update=` instead of `Include=`, and a lowercase package id are
#      all live spellings. Each would silently make a real pin invisible — which reads as "unpinned".
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
mkdir -p "$FIX/FS-GG__FS.GG.Rendering"
cat > "$FIX/FS-GG__FS.GG.Rendering/Directory.Packages.local.props" <<'XML'
<Project xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <ItemGroup>
    <PackageVersion Update="fs.gg.kit" Version="0.6.0" />
  </ItemGroup>
</Project>
XML
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 1 ] && printf '%s' "$out" | grep -q 'pins FS.GG.Kit 0.6.0'; } \
  && ok "kit-pin: the 2003 namespace, Update= and a lowercase id are all read as a real pin" \
  || bad "a real pin in a legal spelling must not read as unpinned" "rc=$rc: $out"

# (19) An unreadable pin file must not be blamed on WIRING. The per-capability lines say 0
#      undetermined; a red that named wiring would send an operator to the wrong file (#327/#335).
wire FS-GG/FS.GG.SDD; wire FS-GG/FS.GG.Rendering
: > "$FIX/FS-GG__FS.GG.Rendering.failpin"
out="$(run 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 2 ] && printf '%s' "$out" | grep -q 'could not determine the FS.GG.Kit pin freshness' \
    && ! printf '%s' "$out" | grep -q 'could not determine wiring'; } \
  && ok "kit-pin: an unread pin reds as a PIN no-verdict, never as a wiring one" \
  || bad "a red must name its own subject" "rc=$rc: $out"
rm -f "$FIX/FS-GG__FS.GG.Rendering.failpin"
unpin FS-GG/FS.GG.Rendering

# (13) The borrowed feed reader fails closed when it is gone, like the sparse rule does.
rm -f "$SPARSEBOX/scripts/fsgg_feed.py"
cp "$HERE/../../scripts/check-sparse-checkout-closure.py" "$SPARSEBOX/scripts/check-sparse-checkout-closure.py"
out="$(PATH="$STUB:$PATH" bash "$SPARSEBOX/scripts/repos-audit.sh" \
        --registry "$REG" --repos-sh "$REPOS_SH" 2>&1)" && rc=0 || rc=$?
{ [ "$rc" -eq 3 ] && printf '%s' "$out" | grep -q 'NuGet feed reader is not at' \
    && ! printf '%s' "$out" | grep -q 'every declared receiver is wired'; } \
  && ok "kit-pin: a MISSING feed reader is a permanent no-verdict, not a clean org" \
  || bad "the borrowed feed reader's absence must fail closed" "rc=$rc: $out"

unpin FS-GG/FS.GG.Rendering

echo "repos-audit fixture — $((pass + failcount)) assertion(s): $pass passed, $failcount failed"
[ "$failcount" -eq 0 ] || { echo "::error::repos-audit fixture FAILED"; exit 1; }
echo "repos-audit fixture — OK"
