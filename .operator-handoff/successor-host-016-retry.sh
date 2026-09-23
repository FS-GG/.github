#!/usr/bin/env bash
# One bounded same-schema retry after the settled 0.1.6 rollback.
set -euo pipefail
[[ $EUID == 0 ]] || { echo 'refused: root authentication required' >&2; exit 1; }
cd /
home=/var/lib/fs-gg/telemetry-podman
config=$home/.config/fs-gg/telemetry-host-podman/update.json
deployment=$home/.config/fs-gg/telemetry-host-podman/deployment.env
operator=$home/.local/libexec/fs-gg/telemetry-host-podman/telemetry-host-podman.sh
updater=$home/.local/libexec/fs-gg/telemetry-host-podman/telemetry_host_update.py
diagnostic=/home/eugen/.local/share/fs-gg/operator-handoff/successor-item-relation-readback.py
old_id=successor-013-to-016-20260923
new_id=successor-013-to-016-retry-20260923-1
uid=$(id -u fsgg-telemetry-podman)
as_service=(runuser -u fsgg-telemetry-podman -- env HOME="$home" XDG_RUNTIME_DIR="/run/user/$uid")

[[ $(sha256sum "$updater" | cut -d ' ' -f 1) == 7b174428c47dfdad66b7c1ba35776058accead23501c7cbb954dfd381a8ba470 ]] || {
  echo 'refused: installed retry updater differs from qualified source' >&2; exit 1;
}
[[ $(sha256sum "$diagnostic" | cut -d ' ' -f 1) == 8a59df9a40a3002591109ad2a34412799a47c0ad26e50051573fbbbd34f4af4e ]] || {
  echo 'refused: reviewed relation diagnostic differs' >&2; exit 1;
}

precheck=$(python3 - "$config" "$deployment" "$old_id" <<'PY'
import hashlib, json, pathlib, re, shlex, sys
config_path, deployment_path = map(pathlib.Path, sys.argv[1:3])
old_id = sys.argv[3]
config = json.loads(config_path.read_text())
if not (config.get('schema') == 'fsgg.telemetry.host-update-config/1'
        and config.get('activeDeploymentEnv') == str(deployment_path)
        and config.get('serviceUnit') == 'fsgg-telemetry-host-podman.service'
        and config.get('retainedReceipt', {}).get('workspaceId') == 'successor-fsharp-dev'):
    raise SystemExit('refused: selected successor config differs')
values = {}
for line in deployment_path.read_text().splitlines():
    parts = shlex.split(line, comments=True, posix=True)
    if parts:
        key, value = parts[0].split('=', 1)
        values[key] = value
image = values.get('TELEMETRY_IMAGE_ID', '')
if not re.fullmatch(r'sha256:[0-9a-f]{64}', image) or values.get('TELEMETRY_WORKSPACE_ID') != 'successor-fsharp-dev':
    raise SystemExit('refused: active predecessor differs')
root = pathlib.Path(config['releaseRoot'])
state = root / 'update-state'
journal_path = state / 'active.json'
journal = json.loads(journal_path.read_text())
old_command = journal.get('operation', {}).get('command', {})
if not (journal.get('phase') == 'settled' and journal.get('fromVersion') == '0.1.3'
        and journal.get('toVersion') == '0.1.6'
        and journal.get('outcome', {}).get('status') == 'failed-rolled-back'
        and old_command.get('commandId') == old_id):
    raise SystemExit('refused: settled 0.1.6 failure differs')
if pathlib.Path(journal['previousDeployment']).read_bytes() != deployment_path.read_bytes():
    raise SystemExit('refused: prior predecessor differs from active deployment')
if not (pathlib.Path(journal['candidateDeployment']).is_file()
        and (state / 'commands' / (old_id + '.json')).is_file()
        and (state / 'prior-0.1.5-failed-rolled-back.json').is_file()):
    raise SystemExit('refused: retained candidate or older failure evidence missing')
print(image, hashlib.sha256(config_path.read_bytes()).hexdigest(),
      hashlib.sha256(deployment_path.read_bytes()).hexdigest(),
      hashlib.sha256(journal_path.read_bytes()).hexdigest())
PY
)
read -r expected_image config_sha deployment_sha journal_sha <<< "$precheck"
[[ $("${as_service[@]}" podman image inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' "$expected_image") == 0.1.3 ]]
"${as_service[@]}" "$operator" "$deployment" check >/dev/null
[[ $("${as_service[@]}" systemctl --user is-active fsgg-telemetry-host-podman.service) == active ]]
[[ $("${as_service[@]}" systemctl --user is-active fsgg-telemetry-host-update.timer || true) == inactive ]]
state=$("${as_service[@]}" systemctl --user is-active fsgg-telemetry-host-update.service || true)
[[ $state == inactive || $state == failed ]]

# Preserve the first failed 0.1.6 journal before the retry replaces active.json.
"${as_service[@]}" python3 - "$config" "$deployment" "$config_sha" "$deployment_sha" "$journal_sha" <<'PY'
import hashlib, json, os, pathlib, sys
config_path, deployment_path = map(pathlib.Path, sys.argv[1:3])
config_sha, deployment_sha, journal_sha = sys.argv[3:]
if hashlib.sha256(config_path.read_bytes()).hexdigest() != config_sha or hashlib.sha256(deployment_path.read_bytes()).hexdigest() != deployment_sha:
    raise SystemExit('refused: successor selection changed before retry')
root = pathlib.Path(json.loads(config_path.read_text())['releaseRoot'])
state = root / 'update-state'
raw = (state / 'active.json').read_bytes()
if hashlib.sha256(raw).hexdigest() != journal_sha:
    raise SystemExit('refused: settled failure journal changed before retry')
target = state / 'prior-0.1.6-failed-rolled-back.json'
if target.exists():
    if target.read_bytes() != raw:
        raise SystemExit('refused: prior 0.1.6 failure snapshot differs')
else:
    descriptor = os.open(target, os.O_WRONLY | os.O_CREAT | os.O_EXCL | os.O_NOFOLLOW, 0o600)
    with os.fdopen(descriptor, 'wb') as output:
        output.write(raw)
        output.flush()
        os.fsync(output.fileno())
directory = os.open(state, os.O_RDONLY | os.O_DIRECTORY)
try:
    os.fsync(directory)
finally:
    os.close(directory)
PY

"${as_service[@]}" "$updater" --config "$config" --command-id "$new_id" \
  --expected-current-image "$expected_image" --target-qualified-release telemetry-host/v0.1.6 \
  --retry-failed-command "$old_id" >/dev/null
"${as_service[@]}" "$updater" --config "$config" --status-command "$new_id" | \
  python3 -c 'import json,sys; x=json.load(sys.stdin); assert x["result"]["status"] == "updated" and x["result"]["toVersion"] == "0.1.6"'
"${as_service[@]}" python3 - "$config" <<'PY'
import json, pathlib, sys
root = pathlib.Path(json.loads(pathlib.Path(sys.argv[1]).read_text())['releaseRoot'])
state = root / 'update-state'
old = json.loads((state / 'prior-0.1.6-failed-rolled-back.json').read_text())
new = json.loads((state / 'active.json').read_text())
if not (old.get('outcome', {}).get('status') == 'failed-rolled-back'
        and new.get('phase') == 'settled' and new.get('toVersion') == '0.1.6'
        and new.get('outcome', {}).get('status') == 'updated'
        and new.get('storeSchemaMin') == 10 and new.get('storeSchemaMax') == 10):
    raise SystemExit('refused: retry result or retained failure differs')
PY
selected_image=$("${as_service[@]}" python3 - "$deployment" <<'PY'
import pathlib, shlex, sys
values = {}
for line in pathlib.Path(sys.argv[1]).read_text().splitlines():
    parts = shlex.split(line, comments=True, posix=True)
    if parts:
        key, value = parts[0].split('=', 1)
        values[key] = value
print(values['TELEMETRY_IMAGE_ID'])
PY
)
[[ $("${as_service[@]}" podman image inspect --format '{{index .Config.Labels "org.opencontainers.image.version"}}' "$selected_image") == 0.1.6 ]]
"${as_service[@]}" "$operator" "$deployment" check >/dev/null
"${as_service[@]}" systemctl --user enable --now fsgg-telemetry-host-update.timer
[[ $("${as_service[@]}" systemctl --user is-active fsgg-telemetry-host-podman.service) == active ]]
[[ $(curl --silent --show-error --output /dev/null --write-out '%{http_code}' --max-time 10 http://127.0.0.1:18444/private/dashboard/) == 200 ]]
python3 "$diagnostic"
echo 'Host 0.1.6 retry settled and dashboard responded; inspect graphical item steps.'
