#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
cat >"$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
printf '%s\n' '{"data":{"organization":{"projectsV2":{"nodes":[{"id":"PVT_x","title":"Roadmap","public":true}]}}}}'
EOF
chmod +x "$work/bin/gh"
set +e
PATH="$work/bin:$PATH" "$root/scripts/projects-audit.sh" --project acme/Roadmap --visibility public --trusted-writers platform >"$work/out" 2>&1
rc=$?
set -e
[ "$rc" = 4 ] || { cat "$work/out"; exit 1; }
grep -q 'visibility verified: acme/Roadmap is public' "$work/out"
grep -q 'noverdict: acme/Roadmap' "$work/out"
echo 'projects-audit fixture: OK'
