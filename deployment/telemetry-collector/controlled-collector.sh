#!/bin/sh
set -eu

test "$(id -u)" = 32768
test -f /collector-config/controlled-boundary.json
test -f /collector-config/credential-boundary.sentinel
test -d /collector-store
test -d /collector-config/native-evidence
test -f /collector-config/native-codex-home/source.json
test -f /collector-config/native-executable/controlled-reader
test ! -e /run/podman/podman.sock

source_mode="$(stat -c '%a' /collector-config/native-codex-home/source.json)"
test "$source_mode" = 600
grep -Eq '^\{"schema":"fsgg\.telemetry\.controlled-native-source/1","producer":"disposable-development-container","nonce":"[0-9a-f-]{36}","claim":"topology-only"\}$' /collector-config/native-codex-home/source.json
source_sha="$(sha256sum /collector-config/native-codex-home/source.json | cut -d ' ' -f 1)"
executable_sha="$(sha256sum /collector-config/native-executable/controlled-reader | cut -d ' ' -f 1)"
receipt=/collector-config/native-evidence/controlled-custody.json
temporary=/collector-config/native-evidence/.controlled-custody.json.tmp
umask 077
cat >"$temporary" <<EOF
{"schema":"fsgg.telemetry.collector-controlled-custody/1","verdict":"controlled-topology-only","sourceSha256":"${source_sha}","controlledExecutableSha256":"${executable_sha}","sourceReadOnly":true,"collectorConfigMounted":true,"collectorStoreMounted":true,"collectorEvidenceMounted":true,"protectedCustodyLayout":true,"nativeAccessQualified":false,"modelSupportObserved":false,"captureApplied":false}
EOF
chmod 0600 "$temporary"
if test -f "$receipt"; then
  cmp -s "$temporary" "$receipt"
  rm "$temporary"
else
  mv "$temporary" "$receipt"
fi
printf '%s\n' "$source_sha"
