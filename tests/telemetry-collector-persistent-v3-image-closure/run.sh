#!/usr/bin/env bash
set -euo pipefail
export DOTNET_CLI_DO_NOT_USE_MSBUILD_SERVER=1 MSBUILDDISABLENODEREUSE=1
repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
model="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner.qnt"
model_test="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/PersistentV3Runner_test.qnt"
itf_root="$(mktemp -d)"
custody_source_started=false
custody_cleanup() {
  local custody_exit_code=$?
  if test "$custody_exit_code" -eq 0 || test "$custody_source_started" = false; then
    rm -rf -- "$itf_root"
  else
    printf "FAIL retaining source fixture root after failed/unknown qualification: %s\n" "$itf_root" >&2
  fi
}
trap custody_cleanup EXIT
# Source qualification requires an independently accepted provider/toolchain
# closure. Hash equality below checks that selection; it does not authenticate it.
# The existing root source-operation admission supplies trust and outer resource
# custody. Missing closure inputs refuse before compiler or test invocation.
custody_compiler="${FSGG_LEARN_CUSTODY_CORE_CC:?root-selected C compiler required}"
custody_compiler_sha="${FSGG_LEARN_CUSTODY_CORE_CC_SHA256:?compiler byte pin required}"
custody_toolchain="${FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256_FILE:?accepted complete toolchain closure required}"
custody_toolchain_sha="${FSGG_LEARN_CUSTODY_CORE_TOOLCHAIN_SHA256:?accepted closure manifest pin required}"
[[ "$custody_compiler" = /* && "$custody_toolchain" = /* ]]
[[ "$custody_compiler_sha" =~ ^[a-f0-9]{64}$ && "$custody_toolchain_sha" =~ ^[a-f0-9]{64}$ ]]
printf '%s  %s\n' "$custody_toolchain_sha" "$custody_toolchain" | sha256sum --check --strict
grep -Fx -- "$custody_compiler_sha  $custody_compiler" "$custody_toolchain"
sha256sum --check --strict "$custody_toolchain"
if test "${GITHUB_ACTIONS:-}" = true; then
  test "${RUNNER_ENVIRONMENT:-}" = github-hosted
  test "${RUNNER_OS:-}" = Linux
  test "${RUNNER_ARCH:-}" = X64
  custody_image="${FSGG_LEARN_CUSTODY_CORE_IMAGE_VERSION:?root-selected immutable hosted image required}"
  test "${ImageVersion:-}" = "$custody_image"
  printf 'custody-core hosted-image=%s compiler=%s closure=%s\n' "$custody_image" "$custody_compiler_sha" "$custody_toolchain_sha"
fi
custody_source="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/native/custody_core.c"
custody_tests="$repo_root/tests/telemetry-collector-persistent-v3-image-closure/CustodyCoreTests.c"
# One deadline covers both serial compiles and the ordinary C test binary.
# The enclosing admitted source profile must separately enforce RSS/census and
# complete descendant retirement; timeout alone supplies no custody acceptance.
custody_source_started=true
timeout --kill-after=15s 120s bash -c '
  set -euo pipefail
  unset LD_PRELOAD LD_LIBRARY_PATH GCC_EXEC_PREFIX COMPILER_PATH LIBRARY_PATH CPATH C_INCLUDE_PATH CPLUS_INCLUDE_PATH OBJC_INCLUDE_PATH DEPENDENCIES_OUTPUT SUNPRO_DEPENDENCIES
  export PATH=/usr/bin:/bin LC_ALL=C LANG=C TZ=UTC
  compiler="$1"; source="$2"; controls="$3"; destination="$4"
  headers=(-nostdinc -isystem /usr/lib/gcc/x86_64-linux-gnu/13/include -isystem /usr/include/x86_64-linux-gnu -isystem /usr/include)
  "$compiler" -std=c11 -O2 -Wall -Wextra -Werror -pedantic "${headers[@]}" "$source" "$controls" -o "$destination/custody-core-tests"
  "$compiler" -std=c11 -O2 -Wall -Wextra -Werror -pedantic "${headers[@]}" -fPIC -shared -DCC_TEST_LIBRARY -Wl,-z,relro,-z,now -Wl,--no-undefined "$source" "$controls" -o "$destination/libfsgg-learn-custody-core-tests.so"
  "$destination/custody-core-tests"
  sha256sum -- "$destination/custody-core-tests" "$destination/libfsgg-learn-custody-core-tests.so"
' custody-core-source "$custody_compiler" "$custody_source" "$custody_tests" "$itf_root"
export FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY="$itf_root/libfsgg-learn-custody-core-tests.so"
FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY_SHA256="$(sha256sum -- "$FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY" | cut -d' ' -f1)"
export FSGG_LEARN_CUSTODY_CORE_TEST_LIBRARY_SHA256
quint typecheck "$model"
quint test "$model_test" --backend typescript \
  --match '^(success|lost|mismatch|cancellation|cleanupFailureCase|stale|duplicateStore|duplicateProcess|lateDeadline|lateCancellation|c4Success|c4Stale|c4LateDeadline|c4LateCancellation|c4CleanupFailure|c4SealFailure)$' \
  --max-samples 1 --out-itf "$itf_root/{test}-{seq}.itf.json" | tee "$itf_root/quint-tests.log"
grep -E '^  16 passing \(' "$itf_root/quint-tests.log"
test "$(find "$itf_root" -maxdepth 1 -name '*.itf.json' -type f | wc -l)" -eq 16
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/PersistentV3.ImageClosure.Tests.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
dotnet "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.Tests.dll"
dotnet restore "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/PersistentV3.ImageClosure.Correspondence.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
runner_tool="${PERSISTENT_V3_RUNNER_TOOL:-$(type -P quint)}"
PERSISTENT_V3_RUNNER_ITF_ROOT="$itf_root" \
PERSISTENT_V3_RUNNER_MODEL="$model" \
PERSISTENT_V3_RUNNER_TOOL="$runner_tool" \
PERSISTENT_V3_RUNNER_ADAPTER="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/Runner.fs" \
PERSISTENT_V3_RUNNER_PROFILE="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" \
PERSISTENT_V3_RUNNER_PRODUCTION_DLL="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll" \
dotnet "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/correspondence/bin/Debug/net10.0/PersistentV3.ImageClosure.Correspondence.dll"
dotnet restore "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/PersistentV3.ImageClosure.Mutant.fsproj" --locked-mode --disable-build-servers -m:1 /nodeReuse:false
dotnet build "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/PersistentV3.ImageClosure.Mutant.fsproj" --no-restore --disable-build-servers -m:1 /nodeReuse:false -p:UseSharedCompilation=false
set +e
dotnet "$repo_root/tests/telemetry-collector-persistent-v3-image-closure/mutant/bin/Debug/net10.0/PersistentV3.ImageClosure.Mutant.dll"
mutant_code=$?
set -e
test "$mutant_code" -eq 1
set +e
dotnet "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll" "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" >"$itf_root/production.stdout" 2>"$itf_root/production.stderr"
code=$?
set -e
test "$code" -eq 2
test ! -s "$itf_root/production.stdout"
grep -Fx 'persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required' "$itf_root/production.stderr"
cli="$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/bin/Debug/net10.0/PersistentV3.ImageClosure.dll"
printf '%s' '{"IdentityClass":"synthetic-test","IdentityClass":"synthetic-test"}' >"$itf_root/duplicate.json"
set +e
dotnet "$cli" "$itf_root/duplicate.json" >"$itf_root/duplicate.stdout" 2>"$itf_root/duplicate.stderr"
duplicate_code=$?
set -e
test "$duplicate_code" -eq 3
test ! -s "$itf_root/duplicate.stdout"
grep -Fx 'persistent-v3-image-closure-refused: input-is-not-closed' "$itf_root/duplicate.stderr"
truncate -s 4194305 "$itf_root/oversize.json"
set +e
dotnet "$cli" "$itf_root/oversize.json" >"$itf_root/oversize.stdout" 2>"$itf_root/oversize.stderr"
oversize_code=$?
set -e
test "$oversize_code" -eq 3
test ! -s "$itf_root/oversize.stdout"
grep -Fx 'persistent-v3-image-closure-refused: input-size' "$itf_root/oversize.stderr"

# Explicit command preserves the immutable placeholder before any effect.
set +e
dotnet "$cli" prepare --selection "$repo_root/deployment/telemetry-collector/persistent/v3/image-closure/production-selection.json" --trusted-native-selection "$itf_root/missing-trusted.json" >"$itf_root/explicit.stdout" 2>"$itf_root/explicit.stderr"
explicit_code=$?
set -e
test "$explicit_code" -eq 2
test ! -s "$itf_root/explicit.stdout"
grep -Fx 'persistent-v3-image-closure-unavailable: native-image-closure-inventory-acquisition-required' "$itf_root/explicit.stderr"
set +e
dotnet "$cli" prepare --selection "$itf_root/duplicate.json" --selection "$itf_root/duplicate.json" >"$itf_root/flags.stdout" 2>"$itf_root/flags.stderr"
flags_code=$?
set -e
test "$flags_code" -eq 3
test ! -s "$itf_root/flags.stdout"
grep -Fx 'persistent-v3-image-closure-refused: duplicate-flag-or-relative-path' "$itf_root/flags.stderr"
