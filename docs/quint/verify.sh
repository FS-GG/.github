#!/usr/bin/env bash
set -euo pipefail

experiment_root="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
quint_version="0.32.0"
quint_archive="quint-${quint_version}.tgz"
quint_sha512="f58a238bb0d4ea6a119b5a1f0da37e7ed77135c1067228866e6ede37832395c09b7efeff63fcdc85a75c5d746631998df684cf5d493d1a686340e8400c9b2249"
quint_url="https://registry.npmjs.org/@informalsystems/quint/-/quint-${quint_version}.tgz"

apalache_version="0.56.1"
apalache_archive="apalache-${apalache_version}.tgz"
apalache_sha256="91125e5a3646b9c9d3a7d921d3323f321fac5071909f72b3960c66ff2f998ee1"
apalache_url="https://github.com/apalache-mc/apalache/releases/download/v${apalache_version}/apalache.tgz"

java_archive="OpenJDK21U-jre_x64_linux_hotspot_21.0.12.1_1.tar.gz"
java_sha256="2413149700df0f7d440500a84a8f764c535f21e5a5e87d38328b64eec2c5b500"
java_url="https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/${java_archive}"

cache_root="${QUINT_SPIKE_CACHE:-${XDG_CACHE_HOME:-/tmp}/fsgg-quint-specification-spike}"
archive_root="$cache_root/archives"
quint_home="$cache_root/quint-home"
java_root="$cache_root/temurin-21.0.12.1+1"
mkdir -p "$archive_root" "$quint_home"

require_command() {
  if ! command -v "$1" >/dev/null 2>&1; then
    echo "Required command is unavailable: $1" >&2
    exit 1
  fi
}

download() {
  local url="$1"
  local destination="$2"
  if [[ ! -f "$destination" ]]; then
    curl --fail --location --silent --show-error "$url" --output "$destination"
  fi
}

require_command curl
require_command jq
require_command npm
require_command sha256sum
require_command sha512sum
require_command tar

quint_path="$archive_root/$quint_archive"
download "$quint_url" "$quint_path"
printf '%s  %s\n' "$quint_sha512" "$quint_path" | sha512sum --check --status || {
  echo "Quint archive digest mismatch: $quint_path" >&2
  exit 1
}

java_path="$archive_root/$java_archive"
download "$java_url" "$java_path"
printf '%s  %s\n' "$java_sha256" "$java_path" | sha256sum --check --status || {
  echo "Java archive digest mismatch: $java_path" >&2
  exit 1
}

if [[ ! -x "$java_root/bin/java" ]]; then
  if [[ -e "$java_root" ]]; then
    echo "Incomplete cached Java runtime: $java_root" >&2
    exit 1
  fi
  java_staging="$(mktemp -d "$cache_root/java.XXXXXX")"
  tar -xzf "$java_path" -C "$java_staging" --strip-components=1
  mv "$java_staging" "$java_root"
fi

apalache_path="$archive_root/$apalache_archive"
download "$apalache_url" "$apalache_path"
printf '%s  %s\n' "$apalache_sha256" "$apalache_path" | sha256sum --check --status || {
  echo "Apalache archive digest mismatch: $apalache_path" >&2
  exit 1
}

apalache_root="$quint_home/apalache-dist-${apalache_version}"
apalache_executable="$apalache_root/apalache/bin/apalache-mc"
if [[ ! -x "$apalache_executable" ]]; then
  if [[ -e "$apalache_root" ]]; then
    echo "Incomplete cached Apalache distribution: $apalache_root" >&2
    exit 1
  fi
  apalache_staging="$(mktemp -d "$cache_root/apalache.XXXXXX")"
  tar -xzf "$apalache_path" -C "$apalache_staging"
  mv "$apalache_staging" "$apalache_root"
fi

work_root="$(mktemp -d /tmp/fsgg-quint-specifications.XXXXXX)"
typed_root="$work_root/typed-ir"
trace_root="$work_root/control-traces"
mkdir -p "$typed_root" "$trace_root"

export PATH="$java_root/bin:$PATH"
export QUINT_HOME="$quint_home"
export npm_config_cache="$cache_root/npm"

quint() {
  npm exec --yes --package="$quint_path" -- quint "$@"
}

quint --version
"$java_root/bin/java" -version

models=(
  "$experiment_root/combat/SIRCombatConsequences.qnt"
  "$experiment_root/communication/SIRCommunicationNetwork.qnt"
)

for model in "${models[@]}"; do
  name="$(basename "$model" .qnt)"
  typed_output="$typed_root/$name.json"
  quint typecheck "$model" --out "$typed_output"
  jq --exit-status '
    .stage == "typechecking" and
    (.errors | length) == 0 and
    ((.modules | length) > 0) and
    ((.types | length) > 0) and
    ((.effects | length) > 0)
  ' "$typed_output" >/dev/null
  quint test "$model" --backend typescript --match Example --verbosity 2
  (
    cd "$work_root"
    quint verify "$model" \
      --backend apalache \
      --apalache-version "$apalache_version" \
      --init init \
      --step step \
      --invariant invariant \
      --max-steps 2 \
      --verbosity 1
  )
done

controls=(
  "combat:$experiment_root/combat/SIRCombatConsequences.qnt"
  "communication:$experiment_root/communication/SIRCommunicationNetwork.qnt"
)

for control in "${controls[@]}"; do
  name="${control%%:*}"
  model="${control#*:}"
  log="$trace_root/$name.log"
  trace="$trace_root/$name.itf.json"
  if (
    cd "$work_root"
    quint verify "$model" \
      --backend apalache \
      --apalache-version "$apalache_version" \
      --init brokenInit \
      --step brokenStep \
      --invariant invariant \
      --max-steps 1 \
      --out-itf "$trace" \
      --verbosity 1
  ) >"$log" 2>&1; then
    echo "Negative control unexpectedly passed: $name" >&2
    exit 1
  fi
  if ! grep -Fq 'found a counterexample' "$log"; then
    echo "Negative control failed without the expected counterexample: $name" >&2
    sed -n '1,160p' "$log" >&2
    exit 1
  fi
  jq --exit-status '(.vars | length) > 0 and (.states | length) > 0' "$trace" >/dev/null
done

echo "Typechecked, tested, and symbolically verified 2 Quint models."
echo "Proved 2 injected controls produce machine-readable counterexamples."
echo "Typed Quint IR evidence: $typed_root"
echo "ITF counterexample evidence: $trace_root"
