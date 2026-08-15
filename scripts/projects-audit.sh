#!/usr/bin/env bash
# Read-only ProjectV2 access audit. GitHub's typed ProjectV2 surface exposes visibility but not
# organization base permission nor the effective user writer set. Those unavailable access facts
# remain reported for human verification and never justify scraping the browser UI.
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

# ProjectV2 cannot expose these access facts. Keep the human observation in a
# deliberately small, reviewable record so the audit can name its age without
# pretending it read the access controls itself.
attestation="docs/coordination/project-access-attestation.md"
attestation_state=missing
attestation_detail="record is missing"
if [ -f "$attestation" ]; then
  marker_count=$(grep -cx '<!-- fsgg:project-access-attestation v1' "$attestation" || true)
  marker=$(sed -n '/^<!-- fsgg:project-access-attestation v1$/,/^-->$/p' "$attestation")
  if [ "$marker_count" -eq 0 ]; then
    attestation_state=unknown
    attestation_detail="record is missing"
  elif [ "$marker_count" -ne 1 ] || [ -z "$marker" ]; then
    attestation_state=unknown
    attestation_detail="record is malformed"
  else
    mapfile -t marker_lines <<<"$marker"
    expected_fields=(project verified expires base-permission writers verifier)
    marker_is_exact=true
    [ "${#marker_lines[@]}" -eq 8 ] || marker_is_exact=false
    [ "${marker_lines[0]:-}" = '<!-- fsgg:project-access-attestation v1' ] || marker_is_exact=false
    [ "${marker_lines[7]:-}" = '-->' ] || marker_is_exact=false
    for index in "${!expected_fields[@]}"; do
      [[ "${marker_lines[$((index + 1))]:-}" =~ ^${expected_fields[$index]}=.+$ ]] || marker_is_exact=false
    done

    if [ "$marker_is_exact" != true ]; then
      attestation_state=unknown
      attestation_detail="record is malformed"
    else
      attested_project=${marker_lines[1]#project=}
      verified=${marker_lines[2]#verified=}
      expires=${marker_lines[3]#expires=}
      base_permission=${marker_lines[4]#base-permission=}
      attested_writers=${marker_lines[5]#writers=}
      verifier=${marker_lines[6]#verifier=}
      today=$(date -u +%F)

      if [ "$attested_project" != "$project" ]; then
      attestation_state=unknown
      attestation_detail="record is incomplete or names another project"
      elif [[ ! "$verified" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]] || [[ ! "$expires" =~ ^[0-9]{4}-[0-9]{2}-[0-9]{2}$ ]]; then
      attestation_state=unknown
      attestation_detail="record has an invalid date"
      elif ! max_expires=$(date -u -d "$verified + 30 days" +%F 2>/dev/null) || ! date -u -d "$expires" +%F >/dev/null 2>&1; then
      attestation_state=unknown
      attestation_detail="record has an invalid calendar date"
      elif [ "$verified" \> "$today" ] || [ "$expires" \> "$max_expires" ]; then
      attestation_state=unknown
      attestation_detail="record exceeds the 30-day attestation interval"
      elif [ "$expires" \< "$today" ]; then
      attestation_state=stale
      attestation_detail="expired $expires (verified $verified by $verifier)"
      elif [ "$base_permission" != Read ] || [ "$attested_writers" != "$writers" ]; then
      attestation_state=unknown
      attestation_detail="does not match required base Read and writers [$writers]"
      else
      attestation_state=current
      attestation_detail="verified $verified by $verifier; expires $expires; base $base_permission; writers [$attested_writers]"
      fi
    fi
  fi
fi

coord_bin=${FSGG_COORD_BIN:-"$(dirname "$0")/fsgg-coord"}
if ! actual=$("$coord_bin" graphql project-visibility "$owner" "$title" | jq -r '.isPublic as $value | if $value == null then "null" else $value end'); then
  echo "noverdict: $project — ProjectV2 visibility could not be read" >&2
  exit 4
fi
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
echo "noverdict: $project — GitHub ProjectV2 does not expose organization base permission or the effective writer set; verify Project → Settings → Manage access: base Read; writers [$writers]; access attestation $attestation_state in $attestation: $attestation_detail." >&2
# The unavailable access facts remain an explicit human check, but they do not
# invalidate the visibility verdict that this audit was able to establish.
exit 0
