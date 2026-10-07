#!/usr/bin/env python3
"""Bounded serial collection for this repository's work-programme suites only."""
import hashlib
import json
import os
from pathlib import Path
import selectors
import shutil
import signal
import subprocess
import sys
import tempfile
import time

# One original deadline, including cleanup/reporting; hosted timeout remains 180s.
SECONDS = 170
CLEANUP_SECONDS = 5
OUTPUT_BYTES = 1024 * 1024


def suites(root):
    shared = ['.agents/skills/work-programme/scripts/programme.fsx']
    return [
        dict(id=name, command=['dotnet', 'fsi', '--exec', 'tests/work-programme/' + name + '.fsx'],
             inputs=shared + ['tests/work-programme/' + name + '.fsx'] + extra,
             prerequisites=['dotnet'], dependsOn=[], independent=True)
        for name, extra in [('acceptance', []), ('context-delta', [
            'tests/work-programme/context-delta-input.json', 'tests/work-programme/context-delta-expected.json',
            'tests/work-programme/context-owner-restart-fixtures/interrupted-dispatch.delta-input.json',
            'tests/work-programme/context-owner-restart-fixtures/missed-owner-revision.delta-input.json',
            'tests/work-programme/context-owner-restart-fixtures/absent-owner.delta-input.json',
            'tests/work-programme/context-owner-restart-fixtures/full-reservation.delta-input.json',
            'tests/work-programme/context-owner-restart-fixtures/one-integrator-same-owner-repair.delta-input.json',
            'tests/work-programme/context-owner-restart-fixtures/superseded-owner-return.delta-input.json']),
            ('context-evidence', [])]
    ] + [dict(id='context-baseline', command=[sys.executable, 'tests/work-programme/context-baseline/test_collect.py'],
              inputs=['tests/work-programme/context-baseline/' + f for f in ['test_collect.py', 'collect.py', 'replay.json']],
              prerequisites=['python'], dependsOn=[], independent=True),
         dict(id='context-controls', command=[sys.executable, 'tests/work-programme/test_context.py'],
              inputs=['tests/work-programme/test_context.py', '.agents/skills/work-programme/scripts/context.py'],
              prerequisites=['python'], dependsOn=[], independent=True),
         dict(id='runner-controls', command=[sys.executable, 'tests/work-programme/test_run.py'],
              inputs=['tests/work-programme/test_run.py', 'tests/work-programme/run.py', 'tests/work-programme/run.sh'],
              prerequisites=['python'], dependsOn=[], independent=True)]


def identity(root, paths):
    pins = {}
    for p in paths:
        try:
            path = root / p
            if path.is_file() and not path.is_symlink():
                pins[p] = hashlib.sha256(path.read_bytes()).hexdigest()
        except OSError:
            pass  # Unreadable is missing evidence, never an unchanged identity.
    return pins


def group_alive(group):
    """Linux hosted/local entry: observe live members while retaining its leader."""
    for entry in Path('/proc').iterdir():
        if not entry.name.isdigit():
            continue
        try:
            stat = (entry / 'stat').read_text()
        except FileNotFoundError:
            continue  # This member exited during the census.
        fields = stat[stat.rfind(')') + 2:].split()
        if int(fields[2]) == group and fields[0] not in ('Z', 'X'):
            return True
    return False


def exited_unreaped(child):
    # Unlike poll()/wait(), WNOWAIT retains the original PID/group generation.
    return os.waitid(os.P_PID, child.pid, os.WEXITED | os.WNOHANG | os.WNOWAIT) is not None


def settle(child, deadline):
    """Signal only with an unreaped, waitable direct leader; reap last."""
    if child.returncode is not None:
        return 'leader-already-reaped'
    try:
        exited_unreaped(child)  # ChildProcessError means custody is no longer held.
    except ChildProcessError:
        return 'leader-not-waitable'
    try:
        try:
            os.killpg(child.pid, signal.SIGKILL)
        except ProcessLookupError:
            pass
        while group_alive(child.pid):
            if time.monotonic() >= deadline:
                return 'unobserved-process-group'
            time.sleep(min(0.01, max(0, deadline - time.monotonic())))
        child.wait(timeout=max(0.001, deadline - time.monotonic()))
        # No numeric group signal/query is permitted after this reap.
        return 'passed'
    except (OSError, ValueError, subprocess.TimeoutExpired):
        return 'unobserved-process-group'


def run_child(command, root, scratch, deadline, remaining):
    child = None
    output = bytearray()
    reason = None
    cleanup = 'passed'
    code = None
    try:
        child = subprocess.Popen(command, cwd=root, env={**os.environ, 'TMPDIR': str(scratch)},
                                 stdout=subprocess.PIPE, stderr=subprocess.STDOUT, start_new_session=True)
        with selectors.DefaultSelector() as selector:
            selector.register(child.stdout, selectors.EVENT_READ)
            while selector.get_map():
                left = deadline - CLEANUP_SECONDS - time.monotonic()
                if left <= 0:
                    reason = 'deadline'; break
                events = selector.select(min(left, 0.05))
                for key, _ in events:
                    chunk = os.read(key.fd, min(65536, max(1, remaining - len(output) + 1)))
                    if not chunk:
                        selector.unregister(key.fileobj)
                    elif len(output) + len(chunk) > remaining:
                        output.extend(chunk[:max(0, remaining - len(output))])
                        reason = 'output-bound'; break
                    else:
                        output.extend(chunk)
                if reason:
                    break
        if not reason:
            try:
                while not exited_unreaped(child):
                    if time.monotonic() >= deadline - CLEANUP_SECONDS:
                        reason = 'deadline'; break
                    time.sleep(0.01)
            except subprocess.TimeoutExpired:
                reason = 'deadline'
    except OSError as error:
        reason = 'launch-unknown: ' + str(error)
    finally:
        if child is not None:
            cleanup = settle(child, deadline)
            code = child.returncode
            child.stdout.close()
    return dict(exit=code, reason=reason, cleanup=cleanup,
                output=output.decode('utf-8', errors='replace'), outputBytes=len(output))


def collect(root, selected, seconds=SECONDS, output_limit=OUTPUT_BYTES, reporter=print):
    started = time.monotonic()
    deadline = started + seconds
    report = dict(schema='fsgg.work-programme.test-result/1', candidate={}, suites=[],
                  firstCause=None, cleanup=[], reporting='pending', qualification='incomplete',
                  outputBytes=0, omittedCoverage=[], elapsedSeconds=0)
    tools = {'dotnet': shutil.which('dotnet') is not None, 'python': Path(sys.executable).is_file()}
    paths = sorted({p for suite in selected for p in suite['inputs']})
    pins = identity(root, paths)
    report['candidate'] = pins
    invalid = set()
    hard_stop = None

    def cause(value):
        if report['firstCause'] is None:
            report['firstCause'] = value

    for suite in selected:
        row = dict(id=suite['id'], stage='suite', required=True,
                   dependencies=suite.get('dependsOn'), inputs=suite['inputs'],
                   prerequisites=suite.get('prerequisites'), status='unknown', exit=None,
                   reason=None, cleanup='not-started', evidence=None)
        report['suites'].append(row)
        current = identity(root, paths)
        invalid.update(p for p in pins if current.get(p) != pins[p])
        prereqs = suite.get('prerequisites')
        dependencies = suite.get('dependsOn')
        prior = {r['id']: r for r in report['suites'][:-1]}
        if hard_stop:
            row.update(status='not-run-bound' if hard_stop in ('deadline', 'output-bound') else 'blocked', reason=hard_stop)
        elif time.monotonic() >= deadline - CLEANUP_SECONDS or report['outputBytes'] >= output_limit:
            hard_stop = 'deadline' if time.monotonic() >= deadline - CLEANUP_SECONDS else 'output-bound'
            row.update(status='not-run-bound', reason=hard_stop); cause(hard_stop)
        elif suite.get('independent') is not True or prereqs is None or dependencies is None or any(p not in tools for p in prereqs) or any(p not in prior or prior[p]['status'] == 'unknown' for p in dependencies):
            row.update(status='unknown', reason='unknown-dependency'); cause(suite['id'] + ': unknown-dependency')
        elif any(prior[p]['status'] != 'passed' for p in dependencies):
            row.update(status='blocked', reason='failed-prerequisite')
            cause(suite['id'] + ': failed-prerequisite')
        elif any(not tools[p] for p in prereqs):
            row.update(status='blocked', reason='setup-tool-unavailable'); cause(suite['id'] + ': setup-tool-unavailable')
        elif any(p not in pins or p in invalid for p in suite['inputs']):
            row.update(status='blocked', reason='shared-input-missing-or-changed'); cause(suite['id'] + ': shared-input-missing-or-changed')
        else:
            try:
                scratch = Path(tempfile.mkdtemp(prefix='work-programme-'))
            except OSError as error:
                row.update(status='unknown', reason='scratch-setup-failed: ' + str(error))
                hard_stop = 'scratch-setup-failed'; cause(suite['id'] + ': scratch-setup-failed')
                continue
            try:
                result = run_child(suite['command'], root, scratch, deadline, output_limit - report['outputBytes'])
                row.update(exit=result['exit'], reason=result['reason'], cleanup=result['cleanup'],
                           evidence=result['output'])
                report['outputBytes'] += result['outputBytes']
                row['status'] = ('not-run-bound' if result['reason'] in ('deadline', 'output-bound') else
                                 'unknown' if result['reason'] else 'passed' if result['exit'] == 0 else 'failed')
                if row['status'] != 'passed':
                    cause(suite['id'] + ': ' + (result['reason'] or 'exit ' + str(result['exit'])))
                if result['exit'] is not None and result['exit'] < 0 and not result['reason']:
                    row.update(status='unknown', reason='child-signal')
                    hard_stop = 'child-signal'
                if result['reason']:
                    hard_stop = result['reason']
                if result['cleanup'] != 'passed':
                    hard_stop = 'cleanup-unobserved'; cause(suite['id'] + ': cleanup-unobserved')
            finally:
                try:
                    shutil.rmtree(scratch)
                except OSError as error:
                    row['cleanup'] = 'scratch-cleanup-failed: ' + str(error)
                    hard_stop = 'cleanup-unobserved'; cause(suite['id'] + ': cleanup-unobserved')
                report['cleanup'].append(dict(id=suite['id'], result=row['cleanup']))
            current = identity(root, paths)
            invalid.update(p for p in pins if current.get(p) != pins[p])
            if any(p in invalid for p in suite['inputs']):
                row.update(status='unknown', reason='shared-input-changed')
                cause(suite['id'] + ': shared-input-changed')
    report['omittedCoverage'] = [r['id'] for r in report['suites'] if r['status'] in ('blocked', 'unknown', 'not-run-bound')]
    report['elapsedSeconds'] = round(time.monotonic() - started, 3)
    passed = all(r['status'] == 'passed' and r['cleanup'] == 'passed' for r in report['suites'])
    report['qualification'] = 'passed' if passed else 'failed-or-incomplete'
    report['reporting'] = 'passed'
    try:
        reporter(json.dumps(report, sort_keys=True))
    except (OSError, ValueError) as error:
        report['reporting'] = 'failed: ' + str(error)
        report['qualification'] = 'failed-or-incomplete'
        cause('reporting-failed')
        return report, 1
    return report, 0 if passed else 1


if __name__ == '__main__':
    root = Path(__file__).resolve().parents[2]
    _, code = collect(root, suites(root))
    sys.exit(code)
