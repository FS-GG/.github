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
  (cd "$work" && PROJECTS_AUDIT_FIXTURE="$fixture" PATH="$work/bin:$PATH" "$root/scripts/projects-audit.sh" --project acme/Roadmap --visibility "$visibility" --trusted-writers platform >"$work/out" 2>&1)
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

sed -i '/verifier=operator/a unrecognised=field' "$work/docs/coordination/project-access-attestation.md"
(cd "$work" && PROJECTS_AUDIT_FIXTURE=correct PATH="$work/bin:$PATH" "$root/scripts/projects-audit.sh" --project acme/Roadmap --visibility public --trusted-writers platform >"$work/out" 2>&1)
grep -q 'access attestation unknown' "$work/out"
grep -q 'record is malformed' "$work/out"
if grep -q 'access attestation current' "$work/out"; then
  cat "$work/out"
  exit 1
fi

sed -i '/unrecognised=field/d; s/writers=platform/writers=other/' "$work/docs/coordination/project-access-attestation.md"
(cd "$work" && PROJECTS_AUDIT_FIXTURE=correct PATH="$work/bin:$PATH" "$root/scripts/projects-audit.sh" --project acme/Roadmap --visibility public --trusted-writers platform >"$work/out" 2>&1)
grep -q 'access attestation unknown' "$work/out"
grep -q 'does not match required base Read and writers \[platform\]' "$work/out"

sed -i 's/writers=other/writers=platform/' "$work/docs/coordination/project-access-attestation.md"
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

# The live workflow's credential wiring is part of the repair: the script fixture stubs `gh`, so it
# cannot prove a runner will actually authenticate to the private organization ProjectV2.
python3 - "$root/.github/workflows/projects-audit.yml" <<'PY'
import sys, yaml
doc = yaml.safe_load(open(sys.argv[1], encoding="utf-8"))
steps = doc["jobs"]["audit"]["steps"]
mint = next((s for s in steps if s.get("id") == "app-token"), None)
assert mint is not None, "projects-audit workflow does not mint the org App token"
assert mint.get("uses") == "actions/create-github-app-token@v3"
inputs = mint.get("with") or {}
assert inputs.get("owner") == "FS-GG"
assert inputs.get("permission-organization-projects") == "read"
audit = next(s for s in steps if str(s.get("run", "")).startswith("scripts/projects-audit.sh "))
assert (audit.get("env") or {}).get("GH_TOKEN") == "${{ steps.app-token.outputs.token }}"
print("projects-audit workflow credential wiring: OK")
PY
