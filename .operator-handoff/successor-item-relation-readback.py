#!/usr/bin/env python3
"""Read the successor's engine-owned snapshot and print redacted relation counts."""
import base64
import gzip
import hashlib
import json
import os
from pathlib import Path
import pwd
import re
import subprocess

DEPLOYMENT = Path('/var/lib/fs-gg/telemetry-podman/.config/fs-gg/telemetry-host-podman/deployment.env')
ENGINE = '/opt/fs-gg/coord-cli/0.91.4/fsgg-coord-engine'
WORKSPACE = 'successor-fsharp-dev'
SERVICE = 'fsgg-telemetry-podman'
RELATIONS = ('admissions', 'starts', 'terminals', 'times', 'usage', 'runtimeGaps',
             'activities', 'activityUsageAttributions', 'ciRuns', 'ciJobs', 'ciSteps',
             'ciCoverage', 'ciPopulationCoverage', 'populations', 'outcomes')

if os.geteuid() != 0:
    raise SystemExit('run through one local root authentication')
lines = DEPLOYMENT.read_text().splitlines()
state_values = [line.split('=', 1)[1] for line in lines if line.startswith('TELEMETRY_STATE_ROOT=')]
if len(state_values) != 1 or not state_values[0].startswith('/var/lib/fs-gg/') or not re.fullmatch(r'/[A-Za-z0-9_./-]+', state_values[0]):
    raise SystemExit('deployment state root is absent or invalid')
store = Path(state_values[0]) / 'workspaces' / WORKSPACE
if not store.is_dir() or store.is_symlink():
    raise SystemExit('selected workspace store is absent or not a directory')
account = pwd.getpwnam(SERVICE)
def drop_privileges():
    os.initgroups(SERVICE, account.pw_gid)
    os.setgid(account.pw_gid)
    os.setuid(account.pw_uid)
result = subprocess.run(
    [ENGINE, 'telemetry', 'item-detail', '--format-version', '2', '--all', '--store-root', str(store)],
    env={'HOME': account.pw_dir, 'PATH': '/usr/bin'}, preexec_fn=drop_privileges,
    capture_output=True, timeout=90, check=False)
if result.returncode:
    print(json.dumps({'schema':'fsgg.telemetry.successor-item-relation-readback/1','query':'refused','exitCode':result.returncode}))
    raise SystemExit(1)
envelope = json.loads(result.stdout)
if envelope.get('schema') != 'fsgg.telemetry.item-detail/2':
    raise SystemExit('engine item-detail schema differs')
canonical = gzip.decompress(base64.b64decode(envelope['canonicalSnapshotGzip'], validate=True))
if hashlib.sha256(canonical).hexdigest() != envelope['revision']:
    raise SystemExit('engine snapshot revision differs')
snapshot = json.loads(canonical)
if snapshot.get('selection',{}).get('complete') is not True or snapshot.get('selection',{}).get('mode') != 'all':
    raise SystemExit('engine snapshot is incomplete')
items = snapshot['items']
if len(items) > 200 or any(not isinstance(snapshot.get(name),list) for name in RELATIONS):
    raise SystemExit('engine snapshot shape differs')
rows = []
for index, item in enumerate(items):
    counts = {name:sum(row.get('item_id') == item for row in snapshot[name]) for name in RELATIONS}
    scopes = sorted({row.get('accounting_scope') for row in snapshot['usage'] if row.get('item_id') == item and row.get('accounting_scope') is not None})
    rows.append({'index':index + 1, 'itemHash':hashlib.sha256(item.encode()).hexdigest()[:12],
                 'counts':counts, 'usageScopeCount':len(scopes)})
print(json.dumps({'schema':'fsgg.telemetry.successor-item-relation-readback/1',
                  'revision':envelope['revision'], 'schemaVersion':snapshot['store']['schemaVersion'],
                  'itemCount':len(items), 'items':rows}, sort_keys=True, separators=(',',':')))
