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
trap 'rm -f -- "$snapshot_path" "$rendered_path"' EXIT

dotnet restore "$repo_root/src/FS.GG.V2.Progress/FS.GG.V2.Progress.fsproj" --locked-mode >&2
dotnet build "$repo_root/src/FS.GG.V2.Progress/FS.GG.V2.Progress.fsproj" --configuration Release --no-restore >&2
python3 "$script_dir/collect-local.py" --metadata "$metadata_path" --output "$snapshot_path" \
  --root-session "$root_session_id" --repository "$repository_name" >&2
dotnet fsi "$script_dir/render.fsx" "$snapshot_path" > "$rendered_path"
mv -- "$rendered_path" "$report_path"
echo "rendered $report_path" >&2
