#!/usr/bin/env bash
set -euo pipefail

readonly source_sha="264725f374e3f05da46d7c3089462076a1f9bf7a"
readonly version="0.10.1"
readonly project="scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
readonly package_id="FS.GG.NewSddWorkspace"
readonly tag="new-sdd-workspace/v${version}"

fail() {
  printf 'release-preflight: %s\n' "$1" >&2
  return 1
}

check_version() {
  test "$#" -eq 1 || fail "version subject is missing"
  test -n "$1" || fail "version subject is empty"
  test "$1" = "$version" || fail "version '$1' is not '$version'"
}

# Input is one non-empty JSON array of version/tag strings. This common reader
# gives tag and both feed absence assertions the same unreadable/non-vacuity
# behavior and the same known-present control.
check_inventory() {
  test "$#" -eq 3 || fail "inventory requires candidate, control, and file"
  local candidate="$1" control="$2" path="$3"
  test -s "$path" || fail "inventory '$path' is missing or empty"
  jq -e 'type == "array" and length > 0 and all(.[]; type == "string" and length > 0)' "$path" >/dev/null \
    || fail "inventory '$path' is unreadable or vacuous"
  jq -e --arg control "$control" 'index($control) != null' "$path" >/dev/null \
    || fail "inventory '$path' did not observe positive control '$control'"
  jq -e --arg candidate "$candidate" 'index($candidate) == null' "$path" >/dev/null \
    || fail "inventory '$path' already contains candidate '$candidate'"
}

check_source_diff() {
  test "$#" -ge 3 || fail "source-diff requires repository, source, and at least one path"
  local repo="$1" source="$2"
  shift 2
  git -C "$repo" cat-file -e "${source}^{commit}" 2>/dev/null \
    || fail "source '$source' is unreadable"
  test "$(git -C "$repo" merge-base HEAD "$source")" = "$source" \
    || fail "source '$source' is not an ancestor of HEAD"
  git -C "$repo" diff --quiet "$source" -- "$@" \
    || fail "release-owned source differs after '$source'"
}

check_selftest_log() {
  test "$#" -eq 1 || fail "selftest-log requires one file"
  test -s "$1" || fail "selftest log '$1' is missing or empty"
  grep -q '91 assertion(s): 91 passed, 0 failed' "$1" \
    || fail "selftest assertion census is not 91/91"
  grep -q 'mutation controls — 2 passed, 0 failed' "$1" \
    || fail "existing CLI mutation-control census is not 2/2"
}

check_build_log() {
  test "$#" -eq 1 || fail "build-log requires one file"
  test -s "$1" || fail "build log '$1' is missing or empty"
  grep -q '0 Warning(s)' "$1" || fail "build warnings are not zero"
  grep -q '0 Error(s)' "$1" || fail "build errors are not zero"
}

check_nuspec() {
  test "$#" -eq 1 || fail "nuspec requires one file"
  test -s "$1" || fail "nuspec '$1' is missing or empty"
  local actual_id actual_version actual_commit
  actual_id="$(sed -n 's:.*<id>\([^<]*\)</id>.*:\1:p' "$1")"
  actual_version="$(sed -n 's:.*<version>\([^<]*\)</version>.*:\1:p' "$1")"
  actual_commit="$(sed -n 's:.*commit="\([^"]*\)".*:\1:p' "$1")"
  test "$actual_id" = "$package_id" || fail "nuspec package id is not '$package_id'"
  check_version "$actual_version"
  test "$actual_commit" = "$source_sha" || fail "nuspec repository commit is not '$source_sha'"
}

check_tool_list() {
  test "$#" -eq 1 || fail "tool-list requires one file"
  test -s "$1" || fail "tool list '$1' is missing or empty"
  grep -Eq '^fs\.gg\.newsddworkspace[[:space:]]+0\.10\.1[[:space:]]+new-sdd-workspace$' "$1" \
    || fail "clean tool list does not expose FS.GG.NewSddWorkspace 0.10.1"
}

check_tool_help() {
  test "$#" -eq 1 || fail "tool-help requires one file"
  test -s "$1" || fail "tool help '$1' is missing or empty"
  local normalized
  normalized="$(LC_ALL=C sed $'s/\033\\[[0-9;]*m//g' "$1")"
  if LC_ALL=C grep -q $'\033' <<<"$normalized"; then
    fail "tool help contains an unsupported ANSI escape"
  fi
  grep -q '^Usage$' <<<"$normalized" || fail "installed tool help smoke did not reach Usage"
}

check_tool_help_literal() {
  test "$#" -eq 1 || fail "literal tool-help requires one file"
  test -s "$1" || fail "literal tool help '$1' is missing or empty"
  grep -q '^Usage$' "$1" || fail "literal tool help did not contain plain Usage"
}

release_subject_changed() {
  test "$#" -eq 3 || fail "release-subject requires repository, base, and head"
  local repo="$1" base="$2" head="$3"
  git -C "$repo" cat-file -e "${base}^{commit}" 2>/dev/null || fail "release-subject base is unreadable"
  git -C "$repo" cat-file -e "${head}^{commit}" 2>/dev/null || fail "release-subject head is unreadable"
  if git -C "$repo" diff --name-only "$base" "$head" \
    | grep -Eq '^(work|readiness)/2968-new-sdd-workspace-0-10-1-release/'; then
    printf 'true\n'
  else
    printf 'false\n'
  fi
}

case "${1:-}" in
  --check-version) shift; check_version "$@"; exit ;;
  --check-inventory) shift; check_inventory "$@"; exit ;;
  --check-source-diff) shift; check_source_diff "$@"; exit ;;
  --check-selftest-log) shift; check_selftest_log "$@"; exit ;;
  --check-build-log) shift; check_build_log "$@"; exit ;;
  --check-nuspec) shift; check_nuspec "$@"; exit ;;
  --check-tool-list) shift; check_tool_list "$@"; exit ;;
  --check-tool-help) shift; check_tool_help "$@"; exit ;;
  --check-tool-help-literal) shift; check_tool_help_literal "$@"; exit ;;
  --release-subject-changed) shift; release_subject_changed "$@"; exit ;;
  "") ;;
  *) fail "unknown mode '$1'"; exit 1 ;;
esac

artifact_dir="$(mktemp -d /tmp/fsgg-2968-preflight.XXXXXX)"
case "$artifact_dir" in
  /tmp/fsgg-2968-preflight.*) ;;
  *) fail "unsafe temporary directory '$artifact_dir'"; exit 1 ;;
esac
trap 'rm -r -- "$artifact_dir"' EXIT

check_source_diff . "$source_sha" \
  scripts/NewSddWorkspace \
  .github/workflows/release-new-sdd-workspace.yml

evaluated_version="$(dotnet msbuild "$project" -getProperty:Version | tr -d '[:space:]')"
check_version "$evaluated_version"

# Absence checks carry a known-present control through the same reader.
git ls-remote --tags origin 'refs/tags/new-sdd-workspace/v*' \
  | awk -F'refs/tags/' 'NF == 2 && $2 !~ /\^\{\}$/ { print $2 }' \
  | jq -Rsc 'split("\n") | map(select(length > 0))' >"$artifact_dir/tags.json"
check_inventory "$tag" 'new-sdd-workspace/v0.10.0' "$artifact_dir/tags.json"

curl -fsSL 'https://api.nuget.org/v3-flatcontainer/fs.gg.newsddworkspace/index.json' \
  | jq '.versions' >"$artifact_dir/public-versions.json"
check_inventory "$version" '0.10.0' "$artifact_dir/public-versions.json"

gh api --paginate /orgs/FS-GG/packages/nuget/FS.GG.NewSddWorkspace/versions \
  | jq -cs '[.[][] | .name]' >"$artifact_dir/org-versions.json"
check_inventory "$version" '0.10.0' "$artifact_dir/org-versions.json"

bash tests/new-sdd-workspace/run.sh >"$artifact_dir/selftest.log" 2>&1
check_selftest_log "$artifact_dir/selftest.log"

dotnet build "$project" -c Release -p:RestoreLockedMode=true >"$artifact_dir/build.log"
check_build_log "$artifact_dir/build.log"
dotnet pack "$project" -c Release --no-build -p:RestoreLockedMode=true \
  -p:RepositoryCommit="$source_sha" -o "$artifact_dir/packages" >"$artifact_dir/pack.log"

package="$artifact_dir/packages/${package_id}.${version}.nupkg"
test -f "$package" || fail "expected package '$package' was not produced"
unzip -p "$package" "${package_id}.nuspec" >"$artifact_dir/package.nuspec"
check_nuspec "$artifact_dir/package.nuspec"
nuspec_commit="$(sed -n 's:.*commit="\([^"]*\)".*:\1:p' "$artifact_dir/package.nuspec")"

mkdir "$artifact_dir/tool"
printf '%s\n' \
  '<?xml version="1.0" encoding="utf-8"?>' \
  '<configuration>' \
  '  <packageSources>' \
  '    <clear />' \
  "    <add key=\"preflight\" value=\"$artifact_dir/packages\" />" \
  '  </packageSources>' \
  '</configuration>' >"$artifact_dir/NuGet.Config"
dotnet tool install "$package_id" --version "$version" --tool-path "$artifact_dir/tool" \
  --configfile "$artifact_dir/NuGet.Config" >"$artifact_dir/install.log"
dotnet tool list --tool-path "$artifact_dir/tool" >"$artifact_dir/tool-list.txt"
check_tool_list "$artifact_dir/tool-list.txt"
GITHUB_ACTIONS=true env -u NO_COLOR "$artifact_dir/tool/new-sdd-workspace" --help >"$artifact_dir/tool-help.txt"
if check_tool_help_literal "$artifact_dir/tool-help.txt" 2>/dev/null; then
  fail "hosted writer control did not reproduce ANSI-styled Usage"
fi
LC_ALL=C grep -q $'\033\[1mUsage\033\[0m' "$artifact_dir/tool-help.txt" \
  || fail "hosted writer control did not observe Spectre bold Usage"
check_tool_help "$artifact_dir/tool-help.txt"

# Deliberately omit the raw nupkg hash: SDK pack zip timestamps make a second
# equivalent preflight byte-different. The reproducible subject is package
# identity/version/nuspec provenance plus the clean install behavior. The real
# release workflow packs once and pushes that one prepared byte set to both feeds.
jq -n \
  --arg sourceSha "$source_sha" \
  --arg version "$version" \
  --arg tag "$tag" \
  --arg nuspecRepositoryCommit "$nuspec_commit" \
  '{
    schema: "fsgg.new-sdd-workspace.release-preflight/v1",
    sourceSha: $sourceSha,
    version: $version,
    tag: { name: $tag, present: false, positiveControl: "new-sdd-workspace/v0.10.0" },
    feeds: {
      githubPackages: { candidatePresent: false, positiveControl: "0.10.0" },
      nugetOrg: { candidatePresent: false, positiveControl: "0.10.0" }
    },
    sourceDiff: { packageAndReleasePathsChangedAfterSource: false },
    selftest: { passed: 91, failed: 0, mutationControlsPassed: 2, mutationControlsFailed: 0 },
    build: { configuration: "Release", lockedRestore: true, warnings: 0, errors: 0 },
    package: { id: "FS.GG.NewSddWorkspace", version: $version, nuspecRepositoryCommit: $nuspecRepositoryCommit },
    cleanLocalInstall: { packageVersion: $version, command: "new-sdd-workspace", helpSmoke: "pass" },
    hostedWriterControl: { environment: "GITHUB_ACTIONS=true; NO_COLOR absent", ansiBoldUsageObserved: true, literalAssertionRefused: true, normalizedAssertion: "pass" },
    publicationPerformed: false,
    registryReconciliationPerformed: false
  }'
