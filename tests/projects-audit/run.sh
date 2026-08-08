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
grep -q 'access attestation missing' "$work/out"

run_case wrong-visibility 3 public
grep -q 'visibility is false; expected public' "$work/out"

run_case missing 3 public
grep -q 'configured Project is missing' "$work/out"

run_case unreadable 4 public
grep -q 'visibility could not be read' "$work/out"

mkdir -p "$work/docs/coordination"
today=$(date -u +%F)
expires=$(date -u -d "$today + 30 days" +%F)
cat >"$work/docs/coordination/project-access-attestation.md" <<'EOF'
<!-- fsgg:project-access-attestation v1
project=acme/Roadmap
verified=__TODAY__
expires=__EXPIRES__
base-permission=Read
writers=platform
verifier=operator
-->
EOF
sed -i "s/__TODAY__/$today/; s/__EXPIRES__/$expires/" "$work/docs/coordination/project-access-attestation.md"
(cd "$work" && PROJECTS_AUDIT_FIXTURE=correct PATH="$work/bin:$PATH" "$root/scripts/projects-audit.sh" --project acme/Roadmap --visibility public --trusted-writers platform >"$work/out" 2>&1)
grep -q 'access attestation current' "$work/out"
grep -q "verified $today by operator; expires $expires" "$work/out"

sed -i 's/expires=.*/expires=2000-01-01/' "$work/docs/coordination/project-access-attestation.md"
(cd "$work" && PROJECTS_AUDIT_FIXTURE=correct PATH="$work/bin:$PATH" "$root/scripts/projects-audit.sh" --project acme/Roadmap --visibility public --trusted-writers platform >"$work/out" 2>&1)
grep -q 'access attestation stale' "$work/out"

# The current/stale distinction is not decorative: making an expired record
# look current must turn this focused assertion red.
if grep -q 'access attestation current' "$work/out"; then
  cat "$work/out"
  exit 1
fi
echo 'projects-audit fixture: OK'
