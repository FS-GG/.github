#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; WORK="$(mktemp -d)"; trap 'kill "$PID" 2>/dev/null || true; rm -rf "$WORK"' EXIT
PORT=18761
python3 "$ROOT/tests/gate-finding-history/fetch-transcript-server.py" "$ROOT/tests/gate-finding-history/fetch-transcript.json" "$PORT" >"$WORK/queries" 2>&1 & PID=$!
sleep 0.2
GITHUB_API_URL="http://127.0.0.1:$PORT" GITHUB_TOKEN=fixture python3 "$ROOT/scripts/check-gate-finding-history.py" --fetch --repo FS-GG/fixture --out "$WORK/corpus.json"
jq -e '.repos[0].workflows[0] | .totalRuns == 12 and .findingRuns == 24' "$WORK/corpus.json" >/dev/null
grep -Fq 'status=failure' "$WORK/queries"; grep -Fq 'status=timed_out' "$WORK/queries"
echo 'ok - recorded transcript drives fetch URL filters and total_count'
kill "$PID"; wait "$PID" 2>/dev/null || true
GFH_TRUNCATED=1 python3 "$ROOT/tests/gate-finding-history/fetch-transcript-server.py" "$ROOT/tests/gate-finding-history/fetch-transcript.json" "$PORT" >"$WORK/truncated" 2>&1 & PID=$!
sleep 0.2
GITHUB_API_URL="http://127.0.0.1:$PORT" GITHUB_TOKEN=fixture python3 "$ROOT/scripts/check-gate-finding-history.py" --fetch --repo FS-GG/fixture --out "$WORK/truncated.json"
jq -e '.repos[0].unread | contains("truncated")' "$WORK/truncated.json" >/dev/null
echo 'ok - transcript proves workflow-list truncation becomes unread'
kill "$PID"; wait "$PID" 2>/dev/null || true
GFH_ZERO_RUNS=1 python3 "$ROOT/tests/gate-finding-history/fetch-transcript-server.py" "$ROOT/tests/gate-finding-history/fetch-transcript.json" "$PORT" >"$WORK/triggers" 2>&1 & PID=$!
sleep 0.2
GITHUB_API_URL="http://127.0.0.1:$PORT" GITHUB_TOKEN=fixture python3 "$ROOT/scripts/check-gate-finding-history.py" --fetch --repo FS-GG/fixture --out "$WORK/triggers.json"
jq -e '.repos[0].workflows[0].triggers == ["workflow_call"]' "$WORK/triggers.json" >/dev/null
echo 'ok - transcript proves zero-run acquisition reads workflow triggers'
