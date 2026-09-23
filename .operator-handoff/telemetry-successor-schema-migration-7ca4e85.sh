sudo /usr/bin/bash <<'ROOT'
set -euo pipefail

staged=/home/eugen/.local/share/fs-gg/telemetry-host-manager-staging/0.1.3
source_root=/home/eugen/.local/share/fs-gg/systemadmin-host-source/dbcc9356df5c459cf561537e066f5aa8f67746eb
source_commit=dbcc9356df5c459cf561537e066f5aa8f67746eb
manager_root=/opt/fs-gg/telemetry-host-manager
manager_version=0.1.3
manager="$manager_root/$manager_version/TelemetryHostManager"
service_home=/var/lib/fs-gg/telemetry-podman
deployment="$service_home/.config/fs-gg/telemetry-host-podman/deployment.env"
update_config="$service_home/.config/fs-gg/telemetry-host-podman/update.json"
operator="$service_home/.local/libexec/fs-gg/telemetry-host-podman/telemetry-host-podman.sh"
updater="$service_home/.local/libexec/fs-gg/telemetry-host-podman/telemetry_host_update.py"
command_id=successor-schema-9-to-10-20260922

sha256sum --check <<'HASHES'
25edcf87fab343f4e2feb665e0db6d0d4fc3646a29f46972374708c408f4a1a8  /home/eugen/.local/share/fs-gg/telemetry-host-manager-staging/0.1.3/TelemetryHostManager
e0911de26d17fba42ee5f4988a58c703697f9bc7e59c5bfcfca43575be474311  /home/eugen/.local/share/fs-gg/telemetry-host-manager-staging/0.1.3/TelemetryHostManager.deps.json
3c818c3d16bdde0421d3fa35ea08ea40f8dd3a2b0814e395ca248d5726198c6e  /home/eugen/.local/share/fs-gg/telemetry-host-manager-staging/0.1.3/TelemetryHostManager.dll
9950d4583cfc9a106857c0d5b8a75b53e8774852b57b80bc3823a74d44a4d87f  /home/eugen/.local/share/fs-gg/telemetry-host-manager-staging/0.1.3/TelemetryHostManager.runtimeconfig.json
517c1301e8cb5086fbe63a3c8879049933eb998b1569f754b1e653314ad51232  /home/eugen/.local/share/fs-gg/telemetry-host-manager-staging/0.1.3/FSharp.Core.dll
HASHES

"$staged/TelemetryHostManager" install-manager --root "$manager_root" --version "$manager_version"
"$manager" update-host-files --systemadmin-root "$source_root" --commit "$source_commit"

service_uid="$(id -u fsgg-telemetry-podman)"
service_run=(runuser -u fsgg-telemetry-podman -- env
  HOME="$service_home" XDG_RUNTIME_DIR="/run/user/$service_uid")
"${service_run[@]}" systemctl --user disable --now fsgg-telemetry-host-update.timer
for attempt in {1..60}; do
  state="$("${service_run[@]}" systemctl --user is-active fsgg-telemetry-host-update.service || true)"
  [[ "$state" == inactive || "$state" == failed ]] && break
  [[ "$attempt" -lt 60 ]] || { echo 'updater service did not become inactive' >&2; exit 1; }
  sleep 1
done

identity="$("${service_run[@]}" "$operator" "$deployment" identity)"
expected_image="$(/usr/bin/python3 -c 'import json,sys; print(json.loads(sys.argv[1])["imageId"])' "$identity")"
"${service_run[@]}" "$manager" migrate-host \
  --updater "$updater" \
  --config "$update_config" \
  --command-id "$command_id" \
  --expected-current-image "$expected_image" \
  --target-qualified-release telemetry-host/v0.1.5
"${service_run[@]}" "$updater" --config "$update_config" --status-command "$command_id"
"${service_run[@]}" systemctl --user enable --now fsgg-telemetry-host-update.timer
ROOT
