#!/bin/sh
set -eu

test "$(id -u)" = 32768
test -d /native-source
test ! -e /collector-config
test ! -e /collector-store
test ! -e /collector-evidence
test ! -e /run/podman/podman.sock

umask 077
nonce="$(cat /proc/sys/kernel/random/uuid)"
temporary="/native-source/.controlled-source-${nonce}"
cat >"$temporary" <<EOF
{"schema":"fsgg.telemetry.controlled-native-source/1","producer":"disposable-development-container","nonce":"${nonce}","claim":"topology-only"}
EOF
chmod 0600 "$temporary"
mv "$temporary" /native-source/source.json
printf '%s\n' "$nonce"
