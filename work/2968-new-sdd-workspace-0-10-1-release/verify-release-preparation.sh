#!/usr/bin/env bash
set -euo pipefail

readonly source_sha="264725f374e3f05da46d7c3089462076a1f9bf7a"
readonly version="0.10.1"
readonly project="scripts/NewSddWorkspace/NewSddWorkspace.fsproj"
readonly package_id="FS.GG.NewSddWorkspace"
readonly tag="new-sdd-workspace/v${version}"

artifact_dir="$(mktemp -d /tmp/fsgg-2968-preflight.XXXXXX)"
case "$artifact_dir" in
  /tmp/fsgg-2968-preflight.*) ;;
  *) printf 'unsafe temporary directory: %s\n' "$artifact_dir" >&2; exit 1 ;;
esac
trap 'rm -r -- "$artifact_dir"' EXIT

test "$(git merge-base HEAD "$source_sha")" = "$source_sha"
git diff --quiet "$source_sha" -- \
  scripts/NewSddWorkspace \
  .github/workflows/new-sdd-workspace-selftest.yml \
  .github/workflows/release-new-sdd-workspace.yml

evaluated_version="$(dotnet msbuild "$project" -getProperty:Version | tr -d '[:space:]')"
test "$evaluated_version" = "$version"

# Absence checks carry a known-present control through the same reader.
remote_tags="$(git ls-remote --tags origin 'refs/tags/new-sdd-workspace/v*')"
grep -q 'refs/tags/new-sdd-workspace/v0.10.0$' <<<"$remote_tags"
if grep -q "refs/tags/${tag}$" <<<"$remote_tags"; then
  printf '%s already exists\n' "$tag" >&2
  exit 1
fi

public_versions="$(curl -fsSL 'https://api.nuget.org/v3-flatcontainer/fs.gg.newsddworkspace/index.json')"
jq -e '.versions | index("0.10.0") != null' <<<"$public_versions" >/dev/null
jq -e '.versions | index("0.10.1") == null' <<<"$public_versions" >/dev/null

org_versions="$(gh api --paginate /orgs/FS-GG/packages/nuget/FS.GG.NewSddWorkspace/versions | jq -cs '[.[][] | .name]')"
jq -e 'index("0.10.0") != null' <<<"$org_versions" >/dev/null
jq -e 'index("0.10.1") == null' <<<"$org_versions" >/dev/null

bash tests/new-sdd-workspace/run.sh >"$artifact_dir/selftest.log" 2>&1
grep -q '91 assertion(s): 91 passed, 0 failed' "$artifact_dir/selftest.log"
grep -q 'mutation controls — 2 passed, 0 failed' "$artifact_dir/selftest.log"

dotnet build "$project" -c Release -p:RestoreLockedMode=true >"$artifact_dir/build.log"
grep -q '0 Warning(s)' "$artifact_dir/build.log"
grep -q '0 Error(s)' "$artifact_dir/build.log"
dotnet pack "$project" -c Release --no-build -p:RestoreLockedMode=true \
  -p:RepositoryCommit="$source_sha" -o "$artifact_dir/packages" >"$artifact_dir/pack.log"

package="$artifact_dir/packages/${package_id}.${version}.nupkg"
test -f "$package"
package_sha="$(sha256sum "$package" | cut -d' ' -f1)"
unzip -p "$package" "${package_id}.nuspec" >"$artifact_dir/package.nuspec"
nuspec_version="$(sed -n 's:.*<version>\([^<]*\)</version>.*:\1:p' "$artifact_dir/package.nuspec")"
nuspec_commit="$(sed -n 's:.*commit="\([^"]*\)".*:\1:p' "$artifact_dir/package.nuspec")"
test "$nuspec_version" = "$version"
test "$nuspec_commit" = "$source_sha"

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
grep -Eq '^fs\.gg\.newsddworkspace[[:space:]]+0\.10\.1[[:space:]]+new-sdd-workspace$' "$artifact_dir/tool-list.txt"
"$artifact_dir/tool/new-sdd-workspace" --help >"$artifact_dir/tool-help.txt"
grep -q '^Usage$' "$artifact_dir/tool-help.txt"

jq -n \
  --arg sourceSha "$source_sha" \
  --arg version "$version" \
  --arg tag "$tag" \
  --arg packageSha256 "$package_sha" \
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
    package: { id: "FS.GG.NewSddWorkspace", sha256: $packageSha256, nuspecRepositoryCommit: $nuspecRepositoryCommit },
    cleanLocalInstall: { packageVersion: $version, command: "new-sdd-workspace", helpSmoke: "pass" },
    publicationPerformed: false,
    registryReconciliationPerformed: false
  }'
