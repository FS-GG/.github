#!/usr/bin/env bash
# Read-only ProjectV2 access audit. GitHub's typed ProjectV2 surface exposes visibility but not
# organization base permission nor the effective user writer set. A missing access fact is a
# no-verdict, not a passing audit and never a reason to scrape the browser UI.
set -euo pipefail

usage() {
  echo "usage: projects-audit.sh --project owner/title --visibility public|private --trusted-writers team-or-user,..." >&2
  exit 2
}

project=''
visibility=''
writers=''
while [ "$#" -gt 0 ]; do
  case "$1" in
    --project) project=${2:-}; shift 2 ;;
    --visibility) visibility=${2:-}; shift 2 ;;
    --trusted-writers) writers=${2:-}; shift 2 ;;
    *) usage ;;
  esac
done
[ -n "$project" ] && [ -n "$visibility" ] || usage
case "$visibility" in public|private) ;; *) usage ;; esac
owner=${project%%/*}; title=${project#*/}
[ "$owner" != "$project" ] && [ -n "$title" ] || usage

query='query($owner:String!){organization(login:$owner){projectsV2(first:100){nodes{id title public}}}}'
if ! result=$(gh api graphql -f "query=$query" -F "owner=$owner"); then
  echo "noverdict: $project — ProjectV2 visibility could not be read" >&2
  exit 4
fi

actual=$(jq -r --arg title "$title" '.data.organization.projectsV2.nodes[] | select(.title == $title) | .public' <<<"$result" | head -1)
if [ -z "$actual" ] || [ "$actual" = null ]; then
  echo "finding: $project — configured Project is missing" >&2
  exit 3
fi
want=false; [ "$visibility" = public ] && want=true
if [ "$actual" != "$want" ]; then
  echo "finding: $project — visibility is $actual; expected $visibility" >&2
  exit 3
fi

echo "visibility verified: $project is $visibility"
echo "noverdict: $project — GitHub ProjectV2 does not expose organization base permission or the effective writer set; verify Project → Settings → Manage access: base Read; writers [$writers]." >&2
exit 4
