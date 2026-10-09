#!/usr/bin/env python3
"""Fresh source CLI -> synthetic Codex JSONL -> store -> dashboard journey.

Run within the maintained PID namespace validation runner. All counter values are
explicit test events; this test establishes neither genuine usage nor global activation.
"""
import argparse
from contextlib import contextmanager
import hashlib
import json
import os
from pathlib import Path
import sqlite3
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tests/telemetry-dashboard'))
from test_dashboard import D, actions_fixture, deliveries_fixture


ASSOCIATION_KEYS = {'FSGG_TELEMETRY_STORE', 'FSGG_TELEMETRY_CONFIG',
                    'FSGG_TELEMETRY_REPOSITORY', 'FSGG_TELEMETRY_BINDING_DIGEST',
                    'FSGG_TELEMETRY_CODEX_INVOCATION', 'GITHUB_REPOSITORY', 'CODEX_THREAD_ID'}


def private_environment(config_home):
    env = dict(os.environ, XDG_CONFIG_HOME=str(config_home))
    for key in ASSOCIATION_KEYS | {key for key in env if key.startswith('FSGG_TELEMETRY_CREDENTIAL_')}:
        env.pop(key, None)
    return env


@contextmanager
def process_environment(env):
    """Scope inherited helper children to the same environment as direct calls."""
    original = dict(os.environ)
    try:
        os.environ.clear()
        os.environ.update(env)
        yield
    finally:
        os.environ.clear()
        os.environ.update(original)


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--private-root', type=Path, required=True)
    parser.add_argument('--store-root', type=Path, required=True)
    parser.add_argument('--engine-path', dest='engine', type=Path)
    parser.add_argument('--skip-browser', action='store_true')
    parser.add_argument('--activate-workspace', action='store_true',
                        help='qualify an explicit fresh private association, never global selection')
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
    # Keep this fixture independent of any selected installed association.
    env = private_environment(private / 'config')

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
    config = private / 'workspace.json'
    repository = 'SYNTHETIC/activation'
    nonce = uuid.uuid4().hex
    workspace = 'synthetic-activation-' + nonce
    selected = ['--store-root', str(store)]
    if args.activate_workspace:
        before = sorted(path.name for path in private.iterdir())
        unconfigured = json.loads(run('telemetry', 'workspace', 'status', '--config', str(config),
                                     '--repository', repository).stdout)
        assert unconfigured['status'] == 'unconfigured'
        assert before == sorted(path.name for path in private.iterdir() if not path.name.startswith('command-'))
        run('telemetry', 'workspace', 'activate-local', '--config', str(config),
            '--workspace', workspace, '--producer', 'synthetic-producer-' + nonce,
            '--stream', 'runtime', '--repository', repository, '--store-root', str(store))
        assert config.stat().st_mode & 0o777 == 0o600
        selected = ['--config', str(config), '--repository', repository]
        binding = json.loads(run('telemetry', 'workspace', 'binding', *selected).stdout)
        assert binding['repository'] == repository
        assert binding['producerId'] == 'synthetic-producer-' + nonce
        assert binding['configPath'] == str(config)
        assert binding['destination'] == 'local' and binding['privateStateRoot'] == str(store)
        (private / 'binding.json').write_text(json.dumps(binding, indent=2)+'\n')
        dashboard_status = json.loads(run('telemetry', 'dashboard', 'status', *selected).stdout)
        assert dashboard_status['status'] == 'ready', dashboard_status
    else:
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
                   *selected, '--', '--json', '--ephemeral', 'synthetic test event', code=37)
    assert observed.stdout == wire, 'collector changed native JSONL bytes'
    assert b'reconciliation pending' not in observed.stderr, observed.stderr
    if args.activate_workspace:
        run('telemetry', 'workspace', 'drain', *selected)
        status = json.loads(run('telemetry', 'workspace', 'status', *selected).stdout)
        assert status['pending'] == 0, status
        assert status['workspaceId'] == workspace and status['producerId'] == binding['producerId']
        assert status['streamId'] == 'runtime' and status['destination'] == 'local'
    with sqlite3.connect(store.joinpath('telemetry.sqlite3').as_uri() + '?mode=ro', uri=True) as db:
        counters = db.execute('SELECT input_count,cached_input,output_count,reasoning,total FROM runtime_turn_usage').fetchall()
        terminals = db.execute('SELECT outcome,exit_code FROM runtime_terminals').fetchall()
        assert counters == [(12,4,5,2,17)], counters
        assert terminals == [('failed',37)], terminals
        assert db.execute('SELECT count(*) FROM native_item_outcomes').fetchone()[0] == 0
        if args.activate_workspace:
            receipts = db.execute('SELECT state,count(*) FROM transport_receipts GROUP BY state').fetchall()
            assert receipts and all(state == 'applied' for state, _ in receipts), receipts
            (private / 'applied-receipts.json').write_text(json.dumps(receipts)+'\n')
    labels = private / 'labels.json'
    labels.write_text(json.dumps({'schema':D.LABELS_SCHEMA,'items':{},'models':{},'efforts':{},'scopes':{}}))
    labels.chmod(0o600)
    # The helper owns its subprocess calls and inherits os.environ. Exercise
    # that real boundary with a checked launcher, rather than mocking its IO.
    checked_engine = private / 'checked-engine'
    environment_evidence = private / 'helper-environment'
    environment_evidence.mkdir(mode=0o700)
    checked_engine.write_text('#!' + sys.executable + '\n' +
        'import json, os, pathlib, sys, uuid\n' +
        'assert os.environ.get("XDG_CONFIG_HOME") == ' + repr(env['XDG_CONFIG_HOME']) + '\n' +
        'for key in os.environ:\n' +
        '    assert key not in ' + repr(sorted(ASSOCIATION_KEYS)) + ' and not key.startswith("FSGG_TELEMETRY_CREDENTIAL_"), "inherited association visible"\n' +
        'path = pathlib.Path(' + repr(str(environment_evidence)) + ') / (uuid.uuid4().hex + ".json")\n' +
        'path.write_text(json.dumps({"command":sys.argv[1:3], "privateXdg":True, "associationAbsent":True}))\n' +
        'os.execv(' + repr(str(engine)) + ', [' + repr(str(engine)) + '] + sys.argv[1:])\n')
    checked_engine.chmod(0o700)
    with process_environment(env):
        snapshot, envelope = D._read_host_snapshot(str(store), str(checked_engine))
        host = D.build_host(labels_path=labels, resolved_config={'storeRoot':str(store),'engine':str(checked_engine)})
    helper_checks = [json.loads(path.read_text()) for path in environment_evidence.glob('*.json')]
    assert [row['command'] for row in helper_checks].count(['telemetry','item-detail']) == 2
    assert [row['command'] for row in helper_checks].count(['telemetry','efficiency-export']) == 1
    assert all(row['privateXdg'] and row['associationAbsent'] for row in helper_checks)
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
    if args.activate_workspace and not args.skip_browser:
        stdout_path = private / 'packaged-dashboard-url'
        stderr_path = private / 'packaged-dashboard-stderr'
        with stdout_path.open('wb') as out, stderr_path.open('wb') as err:
            server = subprocess.Popen([str(engine), 'telemetry', 'dashboard', 'serve', *selected, '--no-open'],
                                      env=env, cwd=private, stdout=out, stderr=err)
            try:
                deadline = time.monotonic()+10
                url = ''
                while time.monotonic() < deadline and server.poll() is None:
                    url = stdout_path.read_text().strip()
                    if url: break
                    time.sleep(0.05)
                assert url.startswith('http://127.0.0.1:') and '/bootstrap/' in url, stderr_path.read_text()
                subprocess.run(['node', str(ROOT / 'tests/FS.GG.Telemetry.LocalDashboard.Tests/browser-journey.mjs'),
                                url, workspace, 'SYNTHETIC-LOCAL-JOURNEY'], env=env, cwd=ROOT,
                               check=True, timeout=45, stdout=(private / 'packaged-browser.stdout').open('wb'),
                               stderr=(private / 'packaged-browser.stderr').open('wb'))
            finally:
                if server.poll() is None: server.terminate()
                assert server.wait(timeout=10) == 0, stderr_path.read_text()
            assert url not in stderr_path.read_text(), 'bootstrap capability leaked to stderr'
    if not args.skip_browser:
        subprocess.run(['node', str(ROOT / 'tests/telemetry-local-journey/browser.js'), str(private)],
                       check=True, timeout=45, env=env, cwd=ROOT)
    result = {'helperSubprocessEnvironment':'private-verified','browser':'not-run' if args.skip_browser else 'passed','evidenceKind':'synthetic-test-events','sourceRevision':dashboard['sourceRevision'],
              'enginePath':str(engine),'engineSha256':hashlib.sha256(engine.read_bytes()).hexdigest(),
              'sourceAssemblySha256':hashlib.sha256(assembly.read_bytes()).hexdigest() if not args.engine else None,
              'collectionStoreDashboard':'passed','testCounters':[12,4,5,2,17],
              'nativeExitPreserved':37,'nativeDeliveryOutcomes':0,
              'snapshotRevision':envelope['revision'], 'hostRevision':host['revision'],
              'genuineAgentUsage':'unknown','installedActivation':'private-association-qualified' if args.activate_workspace else 'not-exercised',
              'globalActivation':'not-exercised','packagedDashboard':'passed' if args.activate_workspace and not args.skip_browser else 'not-run',
              'historicalOperations':'not-observed-or-replayed'}
    (private / 'result.json').write_text(json.dumps(result,indent=2)+'\n')
    print(json.dumps(result))


if __name__ == '__main__':
    main()
