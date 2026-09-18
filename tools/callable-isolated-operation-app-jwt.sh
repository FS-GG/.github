#!/usr/bin/env bash
set -euo pipefail

[[ $# = 1 && "$1" =~ ^[1-9][0-9]*$ ]] || { echo 'usage: callable-isolated-operation-app-jwt.sh APP_ID' >&2; exit 2; }
umask 077
key="$(mktemp)"
trap 'rm -f "$key"' EXIT
cat > "$key"
now="$(date +%s)"
header='{"alg":"RS256","typ":"JWT"}'
payload="$(printf '{\"iat\":%d,\"exp\":%d,\"iss\":%d}' "$((now - 30))" "$((now + 540))" "$1")"
base64url() { openssl base64 -A | tr '+/' '-_' | tr -d '='; }
unsigned="$(printf %s "$header" | base64url).$(printf %s "$payload" | base64url)"
signature="$(printf %s "$unsigned" | openssl dgst -sha256 -sign "$key" -binary | base64url)"
printf '%s.%s\n' "$unsigned" "$signature"
