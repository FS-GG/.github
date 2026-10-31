#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/src/FS.GG.Coord.GitHub" "$work/scripts" "$work/.github/workflows"
for f in projects-audit.sh repos-audit.sh coord-board-archive.py check-roster-closure.py; do : >"$work/scripts/$f"; done
# The named exemption (scripts/board-drift-fetch.py, owner Programme/root, Refs #4730) is read, not skipped.
printf '%s\n' 'info = page["pageInfo"]' >"$work/scripts/board-drift-fetch.py"
printf '%s\n' 'info = page.get("pageInfo")' >"$work/scripts/board-currency-fetch.py"
printf '%s\n' 'data = doc["data"]' >"$work/scripts/board-linkage.py"
: >"$work/.github/workflows/coord-board-archive.yml"
: >"$work/src/FS.GG.Coord.GitHub/GraphQl.fs"
: >"$work/src/FS.GG.Coord.GitHub/GraphQlEnvelope.fs"
: >"$work/src/FS.GG.Coord.GitHub/Budget.fs"
python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null
printf '%s\n' '# retired compatibility frontend' >"$work/scripts/graphql_complete_read.py"
if python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null 2>&1; then
  echo "FAIL: checker accepted the retired compatibility frontend" >&2; exit 1
fi
rm "$work/scripts/graphql_complete_read.py"
printf '%s\n' 'let bypass root = root.TryGetProperty "data"' >"$work/src/FS.GG.Coord.GitHub/Budget.fs"
if python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null 2>&1; then
  echo "FAIL: checker accepted a raw Budget envelope selector" >&2; exit 1
fi
: >"$work/src/FS.GG.Coord.GitHub/Budget.fs"
printf '%s\n' 'gh api graphql -f query=x' >"$work/scripts/projects-audit.sh"
if python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null 2>&1; then
  echo "FAIL: checker accepted a production shell transport bypass" >&2; exit 1
fi
: >"$work/scripts/projects-audit.sh"
printf '%s\n' 'info = page["pageInfo"]' >"$work/scripts/coord-board-archive.py"
if python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null 2>&1; then
  echo "FAIL: checker accepted a second production page parser" >&2; exit 1
fi
: >"$work/scripts/coord-board-archive.py"
# An exempt file with raw GraphQL is NOT a finding, and the exemption is announced.
out=$(python3 "$root/scripts/check-graphql-boundary.py" --root "$work")
case "$out" in *"EXEMPT scripts/board-drift-fetch.py"*"Programme/root"*"#4730"*) ;; *)
  echo "FAIL: exemption was not announced with owner and tracking" >&2; exit 1;; esac
# A stale exemption (file no longer needs it, or is gone) is a finding.
: >"$work/scripts/board-drift-fetch.py"
if python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null 2>&1; then
  echo "FAIL: checker accepted a stale exemption" >&2; exit 1
fi
rm "$work/scripts/board-drift-fetch.py"
if python3 "$root/scripts/check-graphql-boundary.py" --root "$work" >/dev/null 2>&1; then
  echo "FAIL: checker accepted an exemption for a missing file" >&2; exit 1
fi
# An exemption record without reason, owner or tracking is a finding.
python3 - "$root/scripts/check-graphql-boundary.py" <<'PY'
import importlib.util, sys
spec = importlib.util.spec_from_file_location("gb", sys.argv[1]); gb = importlib.util.module_from_spec(spec); spec.loader.exec_module(gb)
assert gb.exemption_problems(gb.EXEMPTIONS) == [], "shipped exemption must be complete"
for missing in ("reason", "owner", "tracking"):
    record = dict(gb.EXEMPTIONS["scripts/board-drift-fetch.py"]); record[missing] = " "
    assert gb.exemption_problems({"x.py": record}), f"accepted an exemption without {missing}"
assert gb.exemption_problems({"x.py": {"reason": "r", "owner": "o", "tracking": "no issue"}}), "accepted untracked exemption"
PY
echo "graphql-boundary inversion fixture: OK"
