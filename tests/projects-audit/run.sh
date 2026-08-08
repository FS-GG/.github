#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
work=$(mktemp -d)
trap 'rm -rf "$work"' EXIT
mkdir -p "$work/bin"
cat >"$work/bin/gh" <<'EOF'
#!/usr/bin/env bash
case "${PROJECTS_AUDIT_FIXTURE:-correct}" in
  correct)
    printf '%s\n' '{"data":{"organization":{"projectsV2":{"nodes":[{"id":"PVT_x","title":"Roadmap","public":true}]}}}}'
    ;;
  wrong-visibility)
    printf '%s\n' '{"data":{"organization":{"projectsV2":{"nodes":[{"id":"PVT_x","title":"Roadmap","public":false}]}}}}'
    ;;
  missing)
    printf '%s\n' '{"data":{"organization":{"projectsV2":{"nodes":[]}}}}'
    ;;
  unreadable)
    exit 1
    ;;
  *)
    exit 2
    ;;
esac
EOF
chmod +x "$work/bin/gh"

run_case() {
  local fixture=$1 expected=$2 visibility=$3
  set +e
  PROJECTS_AUDIT_FIXTURE="$fixture" PATH="$work/bin:$PATH" "$root/scripts/projects-audit.sh" --project acme/Roadmap --visibility "$visibility" --trusted-writers platform >"$work/out" 2>&1
  rc=$?
  set -e
  [ "$rc" = "$expected" ] || { cat "$work/out"; exit 1; }
}

run_case correct 0 public
grep -q 'visibility verified: acme/Roadmap is public' "$work/out"
grep -q 'noverdict: acme/Roadmap' "$work/out"
grep -q 'writers \[platform\]' "$work/out"

run_case wrong-visibility 3 public
grep -q 'visibility is false; expected public' "$work/out"

run_case missing 3 public
grep -q 'configured Project is missing' "$work/out"

run_case unreadable 4 public
grep -q 'visibility could not be read' "$work/out"
echo 'projects-audit fixture: OK'
