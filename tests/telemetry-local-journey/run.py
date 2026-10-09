#!/usr/bin/env python3
"""Fresh source CLI -> synthetic Codex JSONL -> store -> dashboard journey.

Run within the maintained PID namespace validation runner. All counter values are
explicit test events; this test establishes neither native usage nor activation.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tests/telemetry-dashboard'))
from test_dashboard import D, actions_fixture, deliveries_fixture


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--private-root', type=Path, required=True)
    parser.add_argument('--store-root', type=Path, required=True)
    parser.add_argument('--engine', type=Path)
    parser.add_argument('--skip-browser', action='store_true')
    args = parser.parse_args()
    private = args.private_root.resolve()
    private.mkdir(mode=0o700, parents=True, exist_ok=False)
    private.chmod(0o700)
    store = args.store_root.absolute()
    assert not store.exists(), 'fresh store required'
    assert store.is_absolute() and not store.is_relative_to(ROOT), 'explicit private store outside checkout required'
    assembly = ROOT / 'src/FS.GG.Coord.Cli/bin/Debug/net10.0/fsgg-coord-engine.dll'
    engine = args.engine.resolve() if args.engine else private / 'engine'
    if not args.engine:
        engine.write_text('#!/bin/sh\nexec dotnet ' + "'" + str(assembly).replace("'", "'\\''") + "'" + ' "$@"\n')
        engine.chmod(0o700)
    env = dict(os.environ, XDG_CONFIG_HOME=str(private / 'config'))
    # Keep this fixture independent of any selected installed association.
    for key in ['FSGG_TELEMETRY_STORE', 'FSGG_TELEMETRY_CONFIG', 'CODEX_THREAD_ID']:
        env.pop(key, None)

    def run(*argv, code=0):
        result = subprocess.run([str(engine), *argv], env=env, cwd=private,
                                capture_output=True, timeout=30)
        (private / f'command-{run.number}.json').write_text(json.dumps({
            'argv': list(argv), 'exitCode': result.returncode,
            'stdout': result.stdout.decode(), 'stderr': result.stderr.decode()}, indent=2))
        run.number += 1
        assert result.returncode == code, (argv, result.returncode, result.stderr)
        return result
    run.number = 0
    run('telemetry', 'store', 'init', '--store-root', str(store))
    bindir = private / 'bin'; bindir.mkdir(mode=0o700)
    # Reuse the maintained package fixture wire shape and native-result control.
    wire = (b'{"type":"thread.started","thread_id":"synthetic-journey-thread"}\n'
            b'{"type":"turn.completed","turn_id":"synthetic-journey-turn","usage":'
            b'{"input_tokens":12,"cached_input_tokens":4,"output_tokens":5,"reasoning_output_tokens":2}}\n')
    fake = bindir / 'codex'
    fake.write_text('#!/usr/bin/env python3\nimport sys\nsys.stdout.buffer.write(' + repr(wire) + ')\nsys.exit(37)\n')
    fake.chmod(0o700)
    env['PATH'] = str(bindir) + os.pathsep + env['PATH']
    assignment = private / 'assignment.json'
    assignment.write_text(json.dumps({'schema':'fsgg.telemetry.codex-assignment/1',
        'featureId':'SYNTHETIC-LOCAL-JOURNEY', 'itemId':'SYNTHETIC-LOCAL-JOURNEY',
        'attemptId':'synthetic-failure-1', 'parentAttemptId':None,
        'producerStream':'synthetic-local-journey'}))
    assignment.chmod(0o600)
    observed = run('telemetry', 'runtime', 'codex-exec', '--assignment', str(assignment),
                   '--store-root', str(store), '--', '--json', '--ephemeral', 'synthetic test event', code=37)
    assert observed.stdout == wire, 'collector changed native JSONL bytes'
    assert b'reconciliation pending' not in observed.stderr, observed.stderr
    with sqlite3.connect(store.joinpath('telemetry.sqlite3').as_uri() + '?mode=ro', uri=True) as db:
        counters = db.execute('SELECT input_count,cached_input,output_count,reasoning,total FROM runtime_turn_usage').fetchall()
        terminals = db.execute('SELECT outcome,exit_code FROM runtime_terminals').fetchall()
        assert counters == [(12,4,5,2,17)], counters
        assert terminals == [('failed',37)], terminals
        assert db.execute('SELECT count(*) FROM native_item_outcomes').fetchone()[0] == 0
    labels = private / 'labels.json'
    labels.write_text(json.dumps({'schema':D.LABELS_SCHEMA,'items':{},'models':{},'efforts':{},'scopes':{}}))
    labels.chmod(0o600)
    snapshot, envelope = D._read_host_snapshot(str(store), str(engine))
    host = D.build_host(labels_path=labels, resolved_config={'storeRoot':str(store),'engine':str(engine)})
    D.validate_host(host)
    assert host['store']['schemaVersion'] == 14
    assert host['store']['pendingBatches'] == 0
    assert host['usage']['total'] == 17
    assert host['completedItems']['items'] == [], 'process completion invented a completed item'
    assert host['processEfficiency']['source'] == 'canonical-export'
    dashboard = D.compose(actions_fixture(), deliveries_fixture(), host,
                          subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(), None)
    D.dump(dashboard)
    (private / 'host.json').write_bytes(D.dump(host))
    (private / 'dashboard.json').write_bytes(D.dump(dashboard))
    if not args.skip_browser:
        subprocess.run(['node', str(ROOT / 'tests/telemetry-local-journey/browser.js'), str(private)],
                       check=True, timeout=45, env=env, cwd=ROOT)
    result = {'browser':'not-run' if args.skip_browser else 'passed','evidenceKind':'synthetic-test-events','sourceRevision':dashboard['sourceRevision'],
              'enginePath':str(engine),'engineSha256':hashlib.sha256(engine.read_bytes()).hexdigest(),
              'sourceAssemblySha256':hashlib.sha256(assembly.read_bytes()).hexdigest() if not args.engine else None,
              'collectionStoreDashboard':'passed','testCounters':[12,4,5,2,17],
              'nativeExitPreserved':37,'nativeDeliveryOutcomes':0,
              'snapshotRevision':envelope['revision'], 'hostRevision':host['revision'],
              'genuineAgentUsage':'unknown','installedActivation':'not-exercised',
              'historicalOperations':'not-observed-or-replayed'}
    (private / 'result.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
