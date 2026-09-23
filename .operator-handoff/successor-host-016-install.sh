#!/usr/bin/env bash
# Successor-only Host 0.1.3 -> 0.1.6, one authenticated invocation.
# Run only on the intended successor: sudo /usr/bin/bash THIS_FILE
set -euo pipefail

[[ $EUID == 0 ]] || { echo 'refused: root authentication required' >&2; exit 1; }
# The service account cannot traverse Main's private checkout directory.
cd /
service_user=fsgg-telemetry-podman
service_home=/var/lib/fs-gg/telemetry-podman
config=$service_home/.config/fs-gg/telemetry-host-podman/update.json
deployment=$service_home/.config/fs-gg/telemetry-host-podman/deployment.env
operator=$service_home/.local/libexec/fs-gg/telemetry-host-podman/telemetry-host-podman.sh
updater=$service_home/.local/libexec/fs-gg/telemetry-host-podman/telemetry_host_update.py
diagnostic=/home/eugen/.local/share/fs-gg/operator-handoff/successor-item-relation-readback.py
command_id=successor-013-to-016-20260923
service_uid=$(id -u "$service_user")
service_run=(runuser -u "$service_user" -- env HOME="$service_home" XDG_RUNTIME_DIR="/run/user/$service_uid")

[[ -x $operator && -x $updater && -f $config && -f $deployment ]] || {
  echo 'refused: successor updater inputs missing' >&2; exit 1;
}

# Read private state without printing private IDs, paths, credentials, or receipts.
precheck=$(python3 - "$config" "$deployment" <<'PY'
import hashlib, json, pathlib, re, shlex, sys
config_path, deployment_path = map(pathlib.Path, sys.argv[1:])
config = json.loads(config_path.read_text())
if config.get('schema') != 'fsgg.telemetry.host-update-config/1':
    raise SystemExit('refused: update config schema differs')
if config.get('activeDeploymentEnv') != str(deployment_path):
    raise SystemExit('refused: selected deployment differs')
if config.get('retainedReceipt', {}).get('workspaceId') != 'successor-fsharp-dev':
    raise SystemExit('refused: selected workspace differs')
if config.get('serviceUnit') != 'fsgg-telemetry-host-podman.service':
    raise SystemExit('refused: selected service differs')
values = {}
for line in deployment_path.read_text().splitlines():
    parts = shlex.split(line, comments=True, posix=True)
    if parts:
        if len(parts) != 1 or '=' not in parts[0]:
            raise SystemExit('refused: deployment format differs')
        key, value = parts[0].split('=', 1)
        values[key] = value
if values.get('TELEMETRY_WORKSPACE_ID') != 'successor-fsharp-dev':
    raise SystemExit('refused: active workspace differs')
image = values.get('TELEMETRY_IMAGE_ID', '')
if not re.fullmatch(r'sha256:[0-9a-f]{64}', image):
    raise SystemExit('refused: active image identity invalid')
journal_path = pathlib.Path(config['releaseRoot']) / 'update-state' / 'active.json'
journal = json.loads(journal_path.read_text())
if not (journal.get('phase') == 'settled' and journal.get('fromVersion') == '0.1.3'
        and journal.get('toVersion') == '0.1.5'
        and journal.get('outcome', {}).get('status') == 'failed-rolled-back'):
    raise SystemExit('refused: held 0.1.5 journal differs')
manifest_path = pathlib.Path(config['releaseRoot'], '0.1.3', 'manifest.json')
if not manifest_path.is_file():
    raise SystemExit('refused: current release manifest missing')
manifest = json.loads(manifest_path.read_text())
if not (manifest.get('version') == '0.1.3'
        and manifest.get('supportedStoreSchemaMin') == 10
        and manifest.get('supportedStoreSchemaMax') == 10):
    raise SystemExit('refused: current release schema differs')
old_command = journal.get('operation', {}).get('command', {}).get('commandId')
if not old_command or not pathlib.Path(config['releaseRoot'], 'update-state', 'commands', old_command + '.json').is_file():
    raise SystemExit('refused: prior failed command receipt missing')
print(image, hashlib.sha256(config_path.read_bytes()).hexdigest(),
      hashlib.sha256(deployment_path.read_bytes()).hexdigest(),
      hashlib.sha256(journal_path.read_bytes()).hexdigest())
PY
)
read -r expected_image expected_config_sha expected_deployment_sha expected_journal_sha <<< "$precheck"

[[ $("${service_run[@]}" podman image inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' "$expected_image") == 0.1.3 ]] || {
  echo 'refused: active image is not Host 0.1.3' >&2; exit 1;
}
"${service_run[@]}" "$operator" "$deployment" check >/dev/null
[[ $("${service_run[@]}" systemctl --user is-active fsgg-telemetry-host-podman.service) == active ]] || {
  echo 'refused: Host service is not active' >&2; exit 1;
}
[[ $("${service_run[@]}" systemctl --user is-active fsgg-telemetry-host-update.timer) == active ]] || {
  echo 'refused: updater timer is not active' >&2; exit 1;
}
[[ $(curl --silent --show-error --output /dev/null --write-out '%{http_code}' http://127.0.0.1:18444/private/dashboard/) == 200 ]] || {
  echo 'refused: predecessor dashboard unavailable' >&2; exit 1;
}
[[ -f $diagnostic && $(sha256sum "$diagnostic" | cut -d ' ' -f 1) == 8a59df9a40a3002591109ad2a34412799a47c0ad26e50051573fbbbd34f4af4e ]] || {
  echo 'refused: reviewed relation diagnostic missing or changed' >&2; exit 1;
}
updater_state=$("${service_run[@]}" systemctl --user is-active fsgg-telemetry-host-update.service || true)
[[ $updater_state == inactive || $updater_state == failed ]] || {
  echo 'refused: updater service already active' >&2; exit 1;
}

# Fence the timer before the final predecessor/journal comparison.
"${service_run[@]}" systemctl --user disable --now fsgg-telemetry-host-update.timer
for attempt in {1..60}; do
  state=$("${service_run[@]}" systemctl --user is-active fsgg-telemetry-host-update.service || true)
  [[ $state == inactive || $state == failed ]] && break
  [[ $attempt -lt 60 ]] || { echo 'refused: updater service still active; timer remains disabled' >&2; exit 1; }
  sleep 1
done
"${service_run[@]}" "$operator" "$deployment" check >/dev/null
"${service_run[@]}" python3 - "$config" "$deployment" "$expected_image" "$expected_config_sha" "$expected_deployment_sha" "$expected_journal_sha" <<'PY'
import hashlib, json, os, pathlib, shlex, sys
config_path, deployment_path = map(pathlib.Path, sys.argv[1:3])
expected_image, expected_config_sha, expected_deployment_sha, expected_journal_sha = sys.argv[3:]
if hashlib.sha256(config_path.read_bytes()).hexdigest() != expected_config_sha:
    raise SystemExit('refused: complete update config changed after timer fence')
if hashlib.sha256(deployment_path.read_bytes()).hexdigest() != expected_deployment_sha:
    raise SystemExit('refused: complete deployment changed after timer fence')
config = json.loads(config_path.read_text())
if config.get('activeDeploymentEnv') != str(deployment_path) or config.get('retainedReceipt', {}).get('workspaceId') != 'successor-fsharp-dev':
    raise SystemExit('refused: selected successor config changed after timer fence')
values = {}
for line in deployment_path.read_text().splitlines():
    parts = shlex.split(line, comments=True, posix=True)
    if parts:
        key, value = parts[0].split('=', 1)
        values[key] = value
if values.get('TELEMETRY_IMAGE_ID') != expected_image or values.get('TELEMETRY_WORKSPACE_ID') != 'successor-fsharp-dev':
    raise SystemExit('refused: predecessor changed after timer fence')
root = pathlib.Path(config['releaseRoot'])
source = root / 'update-state' / 'active.json'
target = root / 'update-state' / 'prior-0.1.5-failed-rolled-back.json'
raw = source.read_bytes()
if hashlib.sha256(raw).hexdigest() != expected_journal_sha:
    raise SystemExit('refused: held journal changed after timer fence')
# The updater replaces active.json. Preserve exact prior bytes and keep the
# command receipt and cold backup; a repeat must match this snapshot.
if target.exists():
    if target.read_bytes() != raw:
        raise SystemExit('refused: prior failed journal snapshot differs')
else:
    descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'wb') as output:
        output.write(raw)
        output.flush()
        os.fsync(output.fileno())
directory = os.open(target.parent, os.O_RDONLY | os.O_DIRECTORY)
try:
    os.fsync(directory)
finally:
    os.close(directory)
PY

# A distinct command targets only 0.1.6; the failed 0.1.5 command is retained.

"${service_run[@]}" "$updater" --config "$config" --command-id "$command_id" \
  --expected-current-image "$expected_image" --target-qualified-release telemetry-host/v0.1.6 >/dev/null
"${service_run[@]}" "$updater" --config "$config" --status-command "$command_id" | \
  python3 -c 'import json,sys; x=json.load(sys.stdin); assert x["result"]["status"] == "updated" and x["result"]["toVersion"] == "0.1.6", "update did not settle updated to 0.1.6"'
"${service_run[@]}" python3 - "$config" <<'PY'
import json, pathlib, sys
root = pathlib.Path(json.loads(pathlib.Path(sys.argv[1]).read_text())['releaseRoot'])
state = root / 'update-state'
old = json.loads((state / 'prior-0.1.5-failed-rolled-back.json').read_text())
new = json.loads((state / 'active.json').read_text())
manifest = json.loads((root / '0.1.6' / 'manifest.json').read_text())
if not (old.get('toVersion') == '0.1.5' and old.get('outcome', {}).get('status') == 'failed-rolled-back'):
    raise SystemExit('refused: old journal snapshot changed')
if not (new.get('phase') == 'settled' and new.get('toVersion') == '0.1.6'
        and new.get('outcome', {}).get('status') == 'updated'
        and new.get('storeSchemaMin') == 10 and new.get('storeSchemaMax') == 10):
    raise SystemExit('refused: new update journal differs')
if not (manifest.get('supportedStoreSchemaMin') == 10 and manifest.get('supportedStoreSchemaMax') == 10):
    raise SystemExit('refused: new release schema differs')
PY
selected_image=$("${service_run[@]}" python3 - "$deployment" <<'PY'
import pathlib, shlex, sys
values = {}
for line in pathlib.Path(sys.argv[1]).read_text().splitlines():
    parts = shlex.split(line, comments=True, posix=True)
    if parts:
        key, value = parts[0].split('=', 1)
        values[key] = value
image = values.get('TELEMETRY_IMAGE_ID', '')
if not image.startswith('sha256:'):
    raise SystemExit('refused: selected image identity is invalid')
print(image)
PY
)
[[ $("${service_run[@]}" podman image inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' "$selected_image") == 0.1.6 ]] || {
  echo 'refused: selected active deployment is not Host 0.1.6' >&2; exit 1;
}
"${service_run[@]}" "$operator" "$deployment" check >/dev/null
"${service_run[@]}" systemctl --user enable --now fsgg-telemetry-host-update.timer
[[ $("${service_run[@]}" systemctl --user is-active fsgg-telemetry-host-podman.service) == active ]]
[[ $(curl --silent --show-error --output /dev/null --write-out '%{http_code}' http://127.0.0.1:18444/private/dashboard/) == 200 ]]

# This diagnostic emits only ordinals and relation counts; retain its output locally.
if ! python3 "$diagnostic"; then
  echo 'Host update settled at 0.1.6; relation diagnostic failed and needs separate readback' >&2
  exit 1
fi
echo 'Host update settled at 0.1.6; inspect the private graphical dashboard and retained updater result.'
