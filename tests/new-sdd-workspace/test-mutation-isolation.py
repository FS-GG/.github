#!/usr/bin/env python3
"""Exercise the real mutation shell with inert acceptance stubs, never a compiler.

This proves checkout/environment/restoration routing. Genuine compiled controls
remain required in the maintained fixture once the promoted producer is available.
"""
import json
import os
import shlex
import sys
from pathlib import Path
import subprocess
import tempfile
import unittest

HERE = Path(__file__).resolve().parent
SUBJECT = '''Product = product
        Template = "rendering"
        Lifecycle = "sdd"
sprintf "lifecycle=%s" lifecycle
sourceLine = Some "source: FS.GG.Workspace.Template::0.15.0"
'''
RUNNER = r'''#!/usr/bin/env bash
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
python3 - "$HERE/../.." <<'PY_INERT_ACCEPTANCE'
import json, os, pathlib, sys
root = pathlib.Path(sys.argv[1]).resolve()
mode = os.environ.get('FSGG_CREATOR_DEPENDENCY_MODE', 'source-only')
if 'FSGG_CREATOR_SOURCE_ARTIFACTS' in os.environ:
    raise SystemExit('inert control: outer positive artifacts leaked')
if os.environ.get('FSGG_MUTATION_CHILD') != '1':
    raise SystemExit('inert control: missing child guard')
if mode == 'published-dependency':
    if any((root / 'scripts/NewSddWorkspace' / name).exists() for name in ('bin', 'obj')):
        raise SystemExit('inert control: stale Creator output refused before mutation')
    for name in ('bin', 'obj'):
        (root / 'scripts/NewSddWorkspace' / name).mkdir()
record = {'creator': str(root), 'mode': mode,
          'producer': os.environ.get('FSGG_CREATOR_PRODUCER_SOURCE'),
          'dependencies': os.environ.get('FSGG_CREATOR_DEPENDENCIES'),
          'caller': os.environ.get('FSGG_CREATOR_CALLER_ROOT')}
with open(os.environ['FSGG_TEST_MUTANT_RECORD'], 'a') as output:
    output.write(json.dumps(record) + '\n')
body = (root / 'scripts/NewSddWorkspace/Program.fs').read_text()
if 'Lifecycle = "typed-sdd"' in body:
    print('the wizard must preserve Standard SDD as the P4 default')
elif 'sprintf "lifecycle=%s" "sdd"' in body:
    print('FAIL  explicit Typed SDD is forwarded unchanged')
elif 'sourceLine = Some "source: FS.GG.Workspace.Template::0.14.0"' in body:
    print('the D.5 fable-game descriptor must select Typed SDD on omission')
else:
    raise SystemExit(0)
raise SystemExit(1)
PY_INERT_ACCEPTANCE
'''


class MutationIsolation(unittest.TestCase):
    def fixture(self, mode, *, break_checkout_route=False):
        temporary = tempfile.TemporaryDirectory()
        self.addCleanup(temporary.cleanup)
        base = Path(temporary.name)
        creator = base / 'positive-creator'
        scripts = creator / 'tests/new-sdd-workspace'
        scripts.mkdir(parents=True)
        source = creator / 'scripts/NewSddWorkspace/Program.fs'
        source.parent.mkdir(parents=True)
        source.write_text(SUBJECT)
        shell = (HERE / 'mutation-controls.sh').read_text()
        if break_checkout_route:
            before = 'runner="$mutant/tests/new-sdd-workspace/run.sh"'
            self.assertEqual(shell.count(before), 1)
            shell = shell.replace(before, 'runner="$HERE/run.sh"')
        (scripts / 'mutation-controls.sh').write_text(shell)
        (scripts / 'run.sh').write_text(RUNNER)
        (creator / '.gitignore').write_text('scripts/NewSddWorkspace/bin/\nscripts/NewSddWorkspace/obj/\n')
        for command in (['git', 'init', '-q'], ['git', 'add', '.'],
                        ['git', '-c', 'user.name=inert-fixture', '-c', 'user.email=inert@example.invalid',
                         'commit', '-qm', 'synthetic mutation routing subject']):
            subprocess.run(command, cwd=creator, check=True, capture_output=True, timeout=10)
        # Model the already compiled positive Creator. These outputs must survive.
        for name in ('bin', 'obj'):
            directory = source.parent / name
            directory.mkdir()
            (directory / 'positive-marker').write_bytes(b'inert positive output')
        env = dict(os.environ, FSGG_CREATOR_DEPENDENCY_MODE=mode,
                   FSGG_CREATOR_SOURCE_ARTIFACTS=str(base / 'retained-positive-artifacts'),
                   FSGG_TEST_MUTANT_RECORD=str(base / 'mutants.jsonl'), TMPDIR=str(base))
        for name, key in (('producer', 'FSGG_CREATOR_PRODUCER_SOURCE'),
                          ('dependencies', 'FSGG_CREATOR_DEPENDENCIES'), ('caller', 'FSGG_CREATOR_CALLER_ROOT')):
            path = base / name
            path.mkdir()
            (path / 'preserved-marker').write_bytes(b'inert published environment')
            env[key] = str(path)
        before = source.stat().st_mtime_ns
        result = subprocess.run(['bash', str(scripts / 'mutation-controls.sh')], cwd=creator,
                                env=env, text=True, capture_output=True, timeout=30)
        records = base / 'mutants.jsonl'
        rows = [json.loads(line) for line in records.read_text().splitlines()] if records.exists() else []
        self.assertEqual(source.read_text(), SUBJECT)
        if mode == 'published-dependency':
            self.assertEqual(source.stat().st_mtime_ns, before, 'published controls touched the positive source')
        for name in ('bin', 'obj'):
            self.assertEqual((source.parent / name / 'positive-marker').read_bytes(), b'inert positive output')
        for name in ('producer', 'dependencies', 'caller'):
            self.assertEqual((base / name / 'preserved-marker').read_bytes(), b'inert published environment')
        return creator, env, result, rows

    def test_published_mutants_use_three_fresh_creator_checkouts(self):
        creator, env, result, rows = self.fixture('published-dependency')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertIn('mutation controls — 3 passed, 0 failed', result.stdout)
        self.assertEqual(len(rows), 3)
        self.assertEqual(len({row['creator'] for row in rows}), 3)
        for row in rows:
            self.assertNotEqual(row['creator'], str(creator))
            self.assertFalse(Path(row['creator']).exists(), 'mutant checkout was not cleaned')
            self.assertEqual(row['mode'], 'published-dependency')
            for name, key in (('producer', 'FSGG_CREATOR_PRODUCER_SOURCE'),
                              ('dependencies', 'FSGG_CREATOR_DEPENDENCIES'), ('caller', 'FSGG_CREATOR_CALLER_ROOT')):
                self.assertEqual(row[name], env[key])

    def test_source_mode_retains_its_original_restoration_route(self):
        creator, _, result, rows = self.fixture('source-only')
        self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
        self.assertEqual(len(rows), 3)
        self.assertTrue(all(row['creator'] == str(creator) and row['mode'] == 'source-only' for row in rows))

    def test_reusing_positive_creator_cannot_count_as_mutation_detection(self):
        _, _, result, rows = self.fixture('published-dependency', break_checkout_route=True)
        self.assertNotEqual(result.returncode, 0)
        self.assertIn('suite failed, but not on the named subject', result.stdout)
        self.assertIn('stale Creator output refused before mutation', result.stdout)
        self.assertEqual(rows, [])


class FixtureCleanup(unittest.TestCase):
    def run_cleanup(self, *, changed=None, original_exit=0, fail_remove=False):
        with tempfile.TemporaryDirectory() as temporary:
            base = Path(temporary)
            output = base / 'state'
            output.mkdir()
            creator = base / 'creator'
            layout = creator / 'scripts/creator-current-coord-dependencies.py'
            layout.parent.mkdir(parents=True)
            layout.write_text('raise SystemExit(0)  # Explicit inert layout double; no pin/build/package.\n')
            roots = {}
            for name in ('producer', 'active'):
                root = base / name
                project = root / 'src/FS.GG.Coord.Cli'
                project.mkdir(parents=True)
                (project / 'fixture.fsproj').write_text('<Project />')
                (project / 'bin').mkdir()
                (project / 'bin/marker').write_bytes(b'before')
                subprocess.run([sys.executable, '-B', str(HERE / 'build-artifact-state.py'),
                                '--root', str(root), '--output', str(output / (name + '-before.json'))],
                               check=True, capture_output=True, timeout=10)
                roots[name] = root
            if changed:
                (roots[changed] / 'src/FS.GG.Coord.Cli/bin/marker').write_bytes(b'change')
            source = (HERE / 'run.sh').read_text()
            self.assertEqual(source.count('cleanup() {'), 1)
            definition = 'cleanup() {' + source.split('cleanup() {', 1)[1].split('\n}\ntrap cleanup EXIT', 1)[0] + '\n}'
            variables = {'HERE': str(HERE), 'REPO_ROOT': str(creator), 'BUILD_ROOT': str(output),
                         'BUILD_ROOT_OWNED': '0', 'PUBLISHED_CENSUS_READY': '1', 'HTTP_PID': '',
                         'WORK': str(base / 'work') if fail_remove else '',
                         'FSGG_CREATOR_PRODUCER_SOURCE': str(roots['producer']),
                         'FSGG_CREATOR_CALLER_ROOT': str(roots['active']),
                         'FSGG_CREATOR_DEPENDENCIES': str(base / 'synthetic-dependencies')}
            script = 'set -euo pipefail\n' + '\n'.join(key + '=' + shlex.quote(value) for key, value in variables.items())
            if fail_remove:
                script += '\nrm() { return 1; }\n'
            script += '\n' + definition + '\ntrap cleanup EXIT\nexit ' + str(original_exit) + '\n'
            result = subprocess.run(['bash', '-c', script], capture_output=True, text=True, timeout=10)
            # Both final reads happen even when an earlier failure is preserved.
            self.assertTrue((output / 'producer-fixture-final.json').is_file())
            self.assertTrue((output / 'active-fixture-final.json').is_file())
            return result

    def test_final_census_covers_late_mutation_and_preserves_first_exit(self):
        self.assertEqual(self.run_cleanup().returncode, 0)
        for changed in ('producer', 'active'):
            for original_exit in (0, 17):
                with self.subTest(changed=changed, original_exit=original_exit):
                    result = self.run_cleanup(changed=changed, original_exit=original_exit)
                    self.assertEqual(result.returncode, original_exit or 1)
                    self.assertIn('published Creator fixture changed producer/caller state', result.stderr)

    def test_cleanup_failure_refuses_success_without_replacing_first_failure(self):
        for original_exit in (0, 17):
            with self.subTest(original_exit=original_exit):
                result = self.run_cleanup(original_exit=original_exit, fail_remove=True)
                self.assertEqual(result.returncode, original_exit or 1)
                self.assertIn('Creator fixture work cleanup failed', result.stderr)


if __name__ == '__main__':
    unittest.main()
