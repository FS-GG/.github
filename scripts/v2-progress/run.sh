#!/usr/bin/env bash
set -euo pipefail

if [[ $# -ne 4 ]]; then
  echo 'usage: scripts/v2-progress/run.sh METADATA.json ROOT_SESSION_ID REPOSITORY REPORT.md' >&2
  exit 2
fi

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
repo_root=$(cd -- "$script_dir/../.." && pwd)
metadata_path=$1
root_session_id=$2
repository_name=$3
report_path=$4
snapshot_path=$(mktemp)
rendered_path=$(mktemp)
render_script_path=$(mktemp)
restore_artifacts_path=$(mktemp -d)
trap 'rm -f -- "$snapshot_path" "$rendered_path" "$render_script_path"; rm -rf -- "$restore_artifacts_path"' EXIT

progress_project="$repo_root/src/FS.GG.V2.Progress/FS.GG.V2.Progress.fsproj"
dotnet restore "$progress_project" --locked-mode --artifacts-path "$restore_artifacts_path" >&2
{
  printf '#load "%s"\n' "$repo_root/src/FS.GG.V2.Progress/ProgressRenderer.fs"
  tail -n +2 "$script_dir/render.fsx"
} > "$render_script_path"
python3 "$script_dir/collect-local.py" --metadata "$metadata_path" --output "$snapshot_path" \
  --root-session "$root_session_id" --repository "$repository_name" >&2
dotnet fsi "$render_script_path" "$snapshot_path" > "$rendered_path"
mv -- "$rendered_path" "$report_path"
echo "rendered $report_path" >&2
