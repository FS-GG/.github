#!/usr/bin/env bash
set -euo pipefail

readonly gate="${1:-work/2968-new-sdd-workspace-0-10-1-release/verify-release-preparation.sh}"
test -x "$gate"

fixture_dir="$(mktemp -d /tmp/fsgg-2968-preflight-fixture.XXXXXX)"
case "$fixture_dir" in
  /tmp/fsgg-2968-preflight-fixture.*) ;;
  *) printf 'unsafe temporary directory: %s\n' "$fixture_dir" >&2; exit 1 ;;
esac
trap 'rm -r -- "$fixture_dir"' EXIT

passed=0
failed=0
cases=()

record() {
  local label="$1" result="$2"
  cases+=("$label|$result")
  if test "$result" = pass; then passed=$((passed + 1)); else failed=$((failed + 1)); fi
}

expect_pass() {
  local label="$1"
  shift
  if "$@" >/dev/null 2>&1; then record "$label" pass; else record "$label" fail; fi
}

expect_red() {
  local label="$1"
  shift
  if "$@" >/dev/null 2>&1; then record "$label" fail; else record "$label" pass; fi
}

printf '["0.10.0"]\n' >"$fixture_dir/inventory-good.json"
printf '["0.10.0","0.10.1"]\n' >"$fixture_dir/inventory-candidate.json"
printf '[]\n' >"$fixture_dir/inventory-empty.json"
printf '{not-json\n' >"$fixture_dir/inventory-unreadable.json"
expect_pass inventory-control "$gate" --check-inventory 0.10.1 0.10.0 "$fixture_dir/inventory-good.json"
expect_red inventory-candidate-mutation "$gate" --check-inventory 0.10.1 0.10.0 "$fixture_dir/inventory-candidate.json"
expect_red inventory-empty-non-vacuity "$gate" --check-inventory 0.10.1 0.10.0 "$fixture_dir/inventory-empty.json"
expect_red inventory-unreadable "$gate" --check-inventory 0.10.1 0.10.0 "$fixture_dir/inventory-unreadable.json"
expect_red inventory-missing "$gate" --check-inventory 0.10.1 0.10.0 "$fixture_dir/missing.json"

expect_pass version-control "$gate" --check-version 0.10.1
expect_red version-mutation "$gate" --check-version 0.10.2
expect_red version-empty "$gate" --check-version ''

mkdir "$fixture_dir/repo"
git -C "$fixture_dir/repo" init -q
git -C "$fixture_dir/repo" config user.email preflight@example.invalid
git -C "$fixture_dir/repo" config user.name preflight
printf 'frozen\n' >"$fixture_dir/repo/owned.txt"
git -C "$fixture_dir/repo" add owned.txt
git -C "$fixture_dir/repo" commit -qm frozen
fixture_source="$(git -C "$fixture_dir/repo" rev-parse HEAD)"
expect_pass source-diff-control "$gate" --check-source-diff "$fixture_dir/repo" "$fixture_source" owned.txt
printf 'mutated\n' >"$fixture_dir/repo/owned.txt"
expect_red source-diff-mutation "$gate" --check-source-diff "$fixture_dir/repo" "$fixture_source" owned.txt
expect_red source-diff-unreadable "$gate" --check-source-diff "$fixture_dir/repo" deadbeef owned.txt
expect_red source-diff-empty-paths "$gate" --check-source-diff "$fixture_dir/repo" "$fixture_source"

mkdir -p "$fixture_dir/repo/unrelated"
printf 'unrelated\n' >"$fixture_dir/repo/unrelated/file.txt"
git -C "$fixture_dir/repo" add unrelated/file.txt
git -C "$fixture_dir/repo" commit -qm unrelated
unrelated_head="$(git -C "$fixture_dir/repo" rev-parse HEAD)"
expect_pass release-subject-unrelated bash -c 'test "$("$1" --release-subject-changed "$2" "$3" "$4")" = false' _ "$gate" "$fixture_dir/repo" "$fixture_source" "$unrelated_head"
mkdir -p "$fixture_dir/repo/work/2968-new-sdd-workspace-0-10-1-release"
printf 'subject\n' >"$fixture_dir/repo/work/2968-new-sdd-workspace-0-10-1-release/spec.md"
git -C "$fixture_dir/repo" add work
git -C "$fixture_dir/repo" commit -qm subject
subject_head="$(git -C "$fixture_dir/repo" rev-parse HEAD)"
expect_pass release-subject-control bash -c 'test "$("$1" --release-subject-changed "$2" "$3" "$4")" = true' _ "$gate" "$fixture_dir/repo" "$unrelated_head" "$subject_head"
expect_red release-subject-unreadable "$gate" --release-subject-changed "$fixture_dir/repo" deadbeef "$subject_head"

printf '%s\n' \
  'new-sdd-workspace parse fixture — 91 assertion(s): 91 passed, 0 failed' \
  'new-sdd-workspace mutation controls — 2 passed, 0 failed' >"$fixture_dir/selftest-good.log"
sed 's/91 passed/90 passed/' "$fixture_dir/selftest-good.log" >"$fixture_dir/selftest-mutated.log"
: >"$fixture_dir/empty.log"
expect_pass selftest-control "$gate" --check-selftest-log "$fixture_dir/selftest-good.log"
expect_red selftest-count-mutation "$gate" --check-selftest-log "$fixture_dir/selftest-mutated.log"
expect_red selftest-empty "$gate" --check-selftest-log "$fixture_dir/empty.log"
expect_red selftest-missing "$gate" --check-selftest-log "$fixture_dir/missing.log"

printf 'Build succeeded.\n    0 Warning(s)\n    0 Error(s)\n' >"$fixture_dir/build-good.log"
sed 's/0 Warning(s)/1 Warning(s)/' "$fixture_dir/build-good.log" >"$fixture_dir/build-mutated.log"
expect_pass build-control "$gate" --check-build-log "$fixture_dir/build-good.log"
expect_red build-warning-mutation "$gate" --check-build-log "$fixture_dir/build-mutated.log"
expect_red build-empty "$gate" --check-build-log "$fixture_dir/empty.log"

printf '%s\n' \
  '<package><metadata>' \
  '<id>FS.GG.NewSddWorkspace</id><version>0.10.1</version>' \
  '<repository type="git" url="https://github.com/FS-GG/.github" commit="264725f374e3f05da46d7c3089462076a1f9bf7a" />' \
  '</metadata></package>' >"$fixture_dir/package-good.nuspec"
sed 's/<version>0.10.1/<version>0.10.2/' "$fixture_dir/package-good.nuspec" >"$fixture_dir/package-version-mutated.nuspec"
sed 's/264725f374e3f05da46d7c3089462076a1f9bf7a/0000000000000000000000000000000000000000/' "$fixture_dir/package-good.nuspec" >"$fixture_dir/package-commit-mutated.nuspec"
expect_pass nuspec-control "$gate" --check-nuspec "$fixture_dir/package-good.nuspec"
expect_red nuspec-version-mutation "$gate" --check-nuspec "$fixture_dir/package-version-mutated.nuspec"
expect_red nuspec-commit-mutation "$gate" --check-nuspec "$fixture_dir/package-commit-mutated.nuspec"
expect_red nuspec-empty "$gate" --check-nuspec "$fixture_dir/empty.log"

printf 'fs.gg.newsddworkspace   0.10.1   new-sdd-workspace\n' >"$fixture_dir/tool-good.txt"
sed 's/0.10.1/0.10.2/' "$fixture_dir/tool-good.txt" >"$fixture_dir/tool-mutated.txt"
expect_pass tool-list-control "$gate" --check-tool-list "$fixture_dir/tool-good.txt"
expect_red tool-list-mutation "$gate" --check-tool-list "$fixture_dir/tool-mutated.txt"
expect_red tool-list-empty "$gate" --check-tool-list "$fixture_dir/empty.log"

printf 'new-sdd-workspace\nUsage\n' >"$fixture_dir/tool-help-good.txt"
sed 's/Usage/Instructions/' "$fixture_dir/tool-help-good.txt" >"$fixture_dir/tool-help-mutated.txt"
printf 'new-sdd-workspace\n\033[1mUsage\033[0m\n' >"$fixture_dir/tool-help-ansi.txt"
printf 'new-sdd-workspace\n\033]0;title\aUsage\n' >"$fixture_dir/tool-help-unsupported.txt"
expect_pass tool-help-control "$gate" --check-tool-help "$fixture_dir/tool-help-good.txt"
expect_red tool-help-mutation "$gate" --check-tool-help "$fixture_dir/tool-help-mutated.txt"
expect_red tool-help-empty "$gate" --check-tool-help "$fixture_dir/empty.log"
expect_pass tool-help-ansi-normalized "$gate" --check-tool-help "$fixture_dir/tool-help-ansi.txt"
expect_red tool-help-ansi-literal-refusal "$gate" --check-tool-help-literal "$fixture_dir/tool-help-ansi.txt"
expect_red tool-help-unsupported-escape "$gate" --check-tool-help "$fixture_dir/tool-help-unsupported.txt"

printf '<?xml version="1.0" encoding="UTF-8"?>\n'
printf '<testsuite name="new-sdd-workspace-release-preflight" tests="%d" failures="%d">\n' "$((passed + failed))" "$failed"
for entry in "${cases[@]}"; do
  label="${entry%%|*}"
  result="${entry##*|}"
  printf '  <testcase name="%s">' "$label"
  if test "$result" = fail; then printf '<failure message="control did not produce expected verdict" />'; fi
  printf '</testcase>\n'
done
printf '</testsuite>\n'

test "$failed" -eq 0
