#!/usr/bin/env bash
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/../.." && pwd)"; WF="$ROOT/.github/workflows/cross-repo-request-predicate.yml"; W="$(mktemp -d)"; trap 'rm -rf "$W"' EXIT
python3 - "$WF" "$W/oracle.sh" <<'PY'
import sys,yaml
w=yaml.safe_load(open(sys.argv[1]).read()); s=next(x for x in w['jobs']['predicate']['steps'] if x.get('id')=='predicate')
open(sys.argv[2],'w').write(s['run'])
PY
mkdir -p "$W/root/src/FS.GG.Coord.Cli/bin/Release/net10.0"
cat > "$W/root/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine" <<'SH'
#!/usr/bin/env bash
cat "$STUB_JSON"; exit "$STUB_RC"
SH
chmod +x "$W/root/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine"
run() { printf '%s' "$1" > "$W/json"; : > "$W/out"; (cd "$W/root" && BODY=x STUB_JSON="$W/json" STUB_RC="$2" GITHUB_OUTPUT="$W/out" bash "$W/oracle.sh"); }
for verdict in agrees unknown none; do run "{\"verdict\":\"$verdict\"}" 0; test "$(sed -n 's/^verdict=//p' "$W/out")" = "$verdict"; done
run '{"verdict":"contradicts","ownerValue":"line1\\nFSGG_looks_like_delimiter\\n`backtick`"}' 3
test "$(sed -n 's/^verdict=//p' "$W/out")" = contradicts
grep -q '^ownerValue<<FSGG_' "$W/out"
grep -q 'FSGG_looks_like_delimiter' "$W/out"
python3 - "$WF" <<'PY'
import sys,yaml
w=yaml.safe_load(open(sys.argv[1]).read()); s=next(x for x in w['jobs']['predicate']['steps'] if x.get('name','').startswith('Auto-comment'))
assert s['if']=="steps.predicate.outputs.verdict == 'contradicts'"
PY
cp "$WF" "$W/mutant.yml"; sed -i "s/'contradicts'/'agrees'/" "$W/mutant.yml"
if python3 - "$W/mutant.yml" <<'PY'
import sys,yaml
w=yaml.safe_load(open(sys.argv[1]).read()); s=next(x for x in w['jobs']['predicate']['steps'] if x.get('name','').startswith('Auto-comment'))
assert s['if']=="steps.predicate.outputs.verdict == 'contradicts'"
PY
then echo 'negative control survived'; exit 1; fi
echo 'cross-repo-request-predicate wiring fixture — OK'
