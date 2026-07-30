#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; WORK="$(mktemp -d)"; trap 'kill "$PID" 2>/dev/null || true; rm -rf "$WORK"' EXIT
PORT=18761
python3 "$ROOT/tests/gate-finding-history/fetch-transcript-server.py" "$ROOT/tests/gate-finding-history/fetch-transcript.json" "$PORT" >"$WORK/queries" 2>&1 & PID=$!
sleep 0.2
GITHUB_API_URL="http://127.0.0.1:$PORT" GITHUB_TOKEN=fixture python3 "$ROOT/scripts/check-gate-finding-history.py" --fetch --repo FS-GG/fixture --out "$WORK/corpus.json"
jq -e '.repos[0].workflows[0] |
  .totalRuns == 12 and .evaluatedRuns == 12 and .redRunCount == 3 and
  ([.redRuns[].evidence] | sort) == ["ambiguous", "fallover", "finding"]' "$WORK/corpus.json" >/dev/null
grep -Fq 'status=failure' "$WORK/queries"; grep -Fq 'status=timed_out' "$WORK/queries"; grep -Fq 'status=success' "$WORK/queries"
echo 'ok - recorded transcript drives red-run evidence through jobs and annotations'
kill "$PID"; wait "$PID" 2>/dev/null || true
GFH_EXPIRED=1 python3 "$ROOT/tests/gate-finding-history/fetch-transcript-server.py" "$ROOT/tests/gate-finding-history/fetch-transcript.json" "$PORT" >"$WORK/expired-queries" 2>&1 & PID=$!
sleep 0.2
GITHUB_API_URL="http://127.0.0.1:$PORT" GITHUB_TOKEN=fixture python3 "$ROOT/scripts/check-gate-finding-history.py" --fetch --repo FS-GG/fixture --out "$WORK/expired.json"
jq -e '.repos[0].workflows[0].redRuns | map(.evidence) | contains(["expired"])' "$WORK/expired.json" >/dev/null
echo 'ok - a retained run whose annotations expired is preserved as expired evidence'
kill "$PID"; wait "$PID" 2>/dev/null || true
GFH_UNREAD_ANNOTATIONS=1 python3 "$ROOT/tests/gate-finding-history/fetch-transcript-server.py" "$ROOT/tests/gate-finding-history/fetch-transcript.json" "$PORT" >"$WORK/unread-evidence-queries" 2>&1 & PID=$!
sleep 0.2
GITHUB_API_URL="http://127.0.0.1:$PORT" GITHUB_TOKEN=fixture python3 "$ROOT/scripts/check-gate-finding-history.py" --fetch --repo FS-GG/fixture --out "$WORK/unread-evidence.json"
jq -e '.repos[0].workflows[0].redRuns | map(.evidence) | contains(["unread"])' "$WORK/unread-evidence.json" >/dev/null
echo 'ok - an unread annotation response is preserved per-run and never rounded'
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
kill "$PID"; wait "$PID" 2>/dev/null || true
GFH_RATE_LIMIT=1 python3 "$ROOT/tests/gate-finding-history/fetch-transcript-server.py" "$ROOT/tests/gate-finding-history/fetch-transcript.json" "$PORT" >"$WORK/rate-limit" 2>&1 & PID=$!
sleep 0.2
GITHUB_API_URL="http://127.0.0.1:$PORT" GITHUB_TOKEN=fixture python3 "$ROOT/scripts/check-gate-finding-history.py" --fetch --repo FS-GG/fixture --out "$WORK/rate-limit.json"
jq -e '.repos[0].unread | contains("after 5 attempts with backoff")' "$WORK/rate-limit.json" >/dev/null
echo 'ok - exhausted rate-limit retries become unread, never a verdict'
