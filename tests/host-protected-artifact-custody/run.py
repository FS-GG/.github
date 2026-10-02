#!/usr/bin/env python3
"""Static fixtures only; never run HOST qualification or .NET."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest
from unittest.mock import patch

ROOT = Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('custody', ROOT / 'scripts/host-protected-artifact-custody.py')
c = importlib.util.module_from_spec(spec)
spec.loader.exec_module(c)

class CustodyTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.root = Path(self.temp.name)
        self.evidence = self.root / 'evidence'
        self.evidence.mkdir()
    def tearDown(self):
        self.temp.cleanup()
    def refusal(self, action, message=None):
        with self.assertRaises(c.Refusal) as error:
            action()
        if message:
            self.assertIn(message, str(error.exception))
    def synthetic_workflow(self):
        text = ''.join('      - name: ' + name + '\n        env:\n' + ''.join('          ' + key + ': ' + value + '\n' for key, value in env.items()) + '        run: |\n          set -euo pipefail\n          echo synthetic\n' for name, _, env in c.GATES)
        gates = [(name, hashlib.sha256(b'set -euo pipefail\necho synthetic\n').hexdigest(), env) for name, _, env in c.GATES]
        return text, gates
    def test_exact_extraction_and_drift(self):
        text, gates = self.synthetic_workflow()
        with patch.object(c, 'WORKFLOW_HASH', hashlib.sha256(text.encode()).hexdigest()), patch.object(c, 'GATES', gates):
            self.assertEqual(len(c.extract(text)), 2)
            self.refusal(lambda: c.extract(text + '\n'), 'workflow hash')
            wrong = text.replace('echo synthetic', 'echo modified', 1)
            with patch.object(c, 'WORKFLOW_HASH', hashlib.sha256(wrong.encode()).hexdigest()):
                self.refusal(lambda: c.extract(wrong), 'script hash')
            wrong = text.replace('${{ runner.temp }}', '${{ secrets.UNKNOWN }}', 1)
            with patch.object(c, 'WORKFLOW_HASH', hashlib.sha256(wrong.encode()).hexdigest()):
                self.refusal(lambda: c.extract(wrong), 'environment mapping')
    def test_missing_duplicate_and_script_expression(self):
        text, gates = self.synthetic_workflow()
        for wrong in (text.split('      - name: ')[0], text + text):
            with patch.object(c, 'WORKFLOW_HASH', hashlib.sha256(wrong.encode()).hexdigest()), patch.object(c, 'GATES', gates):
                self.refusal(lambda: c.extract(wrong), 'missing or duplicate')
        wrong = text.replace('echo synthetic', 'echo ${{ github.sha }}')
        with patch.object(c, 'WORKFLOW_HASH', hashlib.sha256(wrong.encode()).hexdigest()), patch.object(c, 'GATES', gates):
            self.refusal(lambda: c.extract(wrong), 'expression')
    def test_unknown_expression(self):
        self.refusal(lambda: c.resolve_env({'X': '${{ secrets.X }}'}, self.root, self.root, 'github-hosted'), 'unknown')
    def test_ordered_gates_and_first_failure(self):
        gates = [('receiver', 'echo first\n', {}), ('caller', 'echo second\n', {})]
        calls = []
        def good(path, source, env, log):
            calls.append(path.name)
            self.assertEqual(env['GITHUB_WORKSPACE'], str(self.root))
            return 0
        self.assertEqual(len(c.execute_gates(gates, self.root, self.root, self.evidence, 'github-hosted', good)), 2)
        self.assertEqual(calls, ['gate-1.sh', 'gate-2.sh'])
        calls.clear()
        def bad(*args):
            calls.append(args[0].name)
            return 17
        self.refusal(lambda: c.execute_gates(gates, self.root, self.root, self.evidence, 'github-hosted', bad), 'failed (17)')
        self.assertEqual(calls, ['gate-1.sh'])
        self.assertEqual(json.loads((self.evidence / 'gate-status.json').read_text())[0]['exitCode'], 17)
        self.assertFalse((self.root / 'payload.tar').exists())
    def good_counts(self):
        for name, count in [('host-binding', 35), ('host-attempt', 41)]:
            path = self.root / (name + '-results') / (name + '.trx')
            path.parent.mkdir(exist_ok=True)
            path.write_text(f'<TestRun><Counters total="{count}" executed="{count}" passed="{count}" failed="0" notExecuted="0"/></TestRun>')
        for name, count in [('host-binding-python-tests.log', 38), ('host-attempt-transport.log', 17), ('host-attempt-cli.log', 5)]:
            (self.root / name).write_text(f'Ran {count} tests\n\nOK\n')
        (self.evidence / 'gate-2.log').write_text('12 passing\n8 passing\n[ok] No violation found (11101ms at 450 traces/second).\n7 retained scenarios, 91 retained transitions\n16-step concrete acquired trace matched canonical success ITF\nactual cancellation guard mutation diverged\n8 scenarios, 23 transitions\nactual identity guard mutation diverged\n')
    def test_positive_counts_and_zero_missing_skip_refusal(self):
        self.good_counts()
        c.check_counts(self.root, self.evidence)
        log = self.evidence / 'gate-2.log'
        text = log.read_text()
        for bad in ('0 passing', '', '120 passing'):
            log.write_text(text.replace('12 passing', bad))
            self.refusal(lambda: c.check_counts(self.root, self.evidence), 'Quint')
        log.write_text(text)
        path = self.root / 'host-binding-results/host-binding.trx'
        path.write_text(path.read_text().replace('notExecuted="0"', 'notExecuted="1"'))
        self.refusal(lambda: c.check_counts(self.root, self.evidence), 'skips')
    def production(self):
        directory = self.root / 'build'
        directory.mkdir()
        deps = {'runtimeTarget': {'name': 'net10.0'}, 'targets': {'net10.0': {'HostAttempt/1': {'runtime': {'HostAttempt.dll': {}}}, 'FSharp.Core/1': {'runtime': {'lib/netstandard2.0/FSharp.Core.dll': {}}}}}}
        (directory / 'HostAttempt.deps.json').write_text(json.dumps(deps))
        for name in ('HostAttempt', 'HostAttempt.dll', 'HostAttempt.pdb', 'HostAttempt.runtimeconfig.json', 'FSharp.Core.dll'):
            (directory / name).write_text('fixture')
        (directory / 'HostAttempt').chmod(0o755)
        return directory
    def test_complete_closure_absent_pdb_and_test_substitution(self):
        directory = self.production()
        c.closure(directory, 'HostAttempt', self.root / 'copied')
        (directory / 'HostAttempt.pdb').unlink()
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'bad'), 'missing')
        (directory / 'HostAttempt.pdb').write_text('fixture')
        (directory / 'HostAttempt.Tests.dll').write_text('fixture')
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'bad'), 'unallowlisted')
    def resource_production(self):
        directory = self.production()
        packages = self.root / 'packages'
        package = packages / 'fsharp.core/10.1.401'
        assets = package / 'lib/netstandard2.1'
        assets.mkdir(parents=True)
        (assets / 'FSharp.Core.dll').write_bytes((directory / 'FSharp.Core.dll').read_bytes())
        deps_path = directory / 'HostAttempt.deps.json'
        deps = json.loads(deps_path.read_text())
        target = deps['targets'].pop('net10.0')
        deps['runtimeTarget']['name'] = '.NETCoreApp,Version=v10.0'
        deps['targets']['.NETCoreApp,Version=v10.0'] = target
        target.pop('FSharp.Core/1')
        resources = {}
        for locale in ('cs','de','es','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant'):
            name = 'FSharp.Core.resources.dll'
            resources[f'lib/netstandard2.1/{locale}/{name}'] = {'locale': locale}
            (assets / locale).mkdir()
            (directory / locale).mkdir()
            content = ('synthetic satellite ' + locale).encode()
            (assets / locale / name).write_bytes(content)
            (directory / locale / name).write_bytes(content)
        deps['targets']['.NETCoreApp,Version=v10.0']['FSharp.Core/10.1.401'] = {'runtime': {'lib/netstandard2.1/FSharp.Core.dll': {'assemblyVersion': '10.1.0.0', 'fileVersion': '10.104.126.42413'}}, 'resources': resources}
        deps['libraries'] = {'HostAttempt/1': {'type': 'project'}, 'FSharp.Core/10.1.401': {'type': 'package', 'path': 'fsharp.core/10.1.401'}}
        deps_path.write_text(json.dumps(deps))
        return directory, packages
    def test_actual_deps_resource_shape_complete_locales_and_byte_joins(self):
        directory, packages = self.resource_production()
        destination = self.root / 'copied'
        _, joins = c.closure(directory, 'HostAttempt', destination, packages)
        resources = [join for join in joins if join['kind'] == 'resources']
        self.assertEqual(len(resources), 13)
        self.assertEqual({join['locale'] for join in resources}, {'cs','de','es','fr','it','ja','ko','pl','pt-BR','ru','tr','zh-Hans','zh-Hant'})
        for join in resources:
            self.assertEqual((destination / join['outputPath']).read_bytes(), (directory / join['outputPath']).read_bytes())
            self.assertEqual(c.digest(destination / join['outputPath']), join['sha256'])
    def test_resource_missing_extra_directory_and_byte_mismatch(self):
        directory, packages = self.resource_production()
        satellite = directory / 'de/FSharp.Core.resources.dll'
        content = satellite.read_bytes(); satellite.unlink()
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'missing', packages), 'de/FSharp.Core.resources.dll')
        satellite.write_bytes(content)
        extra = directory / 'de/undeclared.resources.dll'; extra.write_bytes(b'fixture')
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'extra', packages), 'undeclared.resources.dll')
        extra.unlink()
        (directory / 'unexpected-empty').mkdir()
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'extra', packages), 'unexpected-empty')
        (directory / 'unexpected-empty').rmdir()
        satellite.write_bytes(b'substituted')
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'different', packages), 'byte mismatch')
        self.assertFalse((self.root / 'different').exists())
    def test_resource_locale_mismatch_mutant_and_path_escape(self):
        directory, packages = self.resource_production()
        deps_path = directory / 'HostAttempt.deps.json'
        original = deps_path.read_text()
        for asset, locale, reason in [('lib/netstandard2.1/de/FSharp.Core.resources.dll','fr','locale mismatch'), ('lib/netstandard2.1/de/HostAttempt.Tests.resources.dll','de','test/mutant'), ('../de/FSharp.Core.resources.dll','de','path escape'), ('lib/netstandard2.1/de/FSharp.Core.resources.dll','../de','locale')]:
            deps = json.loads(original)
            deps['targets']['.NETCoreApp,Version=v10.0']['FSharp.Core/10.1.401']['resources'] = {asset: {'locale': locale}}
            deps_path.write_text(json.dumps(deps))
            self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'bad', packages), reason)
        deps_path.write_text(original)
        target = directory / 'de/FSharp.Core.resources.dll'; target.unlink(); target.symlink_to('/etc/passwd')
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'bad', packages), 'symlink')
    def test_resource_missing_restored_package_bytes_and_escaping_directory(self):
        directory, packages = self.resource_production()
        restored = packages / 'fsharp.core/10.1.401/lib/netstandard2.1/de/FSharp.Core.resources.dll'
        restored.unlink()
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'bad', packages), 'missing')
        (directory / 'de/FSharp.Core.resources.dll').unlink()
        (directory / 'de').rmdir()
        (directory / 'de').symlink_to('/etc', target_is_directory=True)
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'bad', packages), 'escaping production directory')
    def test_resource_hardlink_refused(self):
        directory, packages = self.resource_production()
        path = directory / 'de/FSharp.Core.resources.dll'
        os.link(path, self.root / 'linked-satellite')
        self.refusal(lambda: c.closure(directory, 'HostAttempt', self.root / 'bad', packages), 'hardlink')
    def test_closure_diagnostic_lists_are_bounded(self):
        directory = self.production()
        for index in range(30):
            (directory / f'extra-{index}').write_bytes(b'fixture')
        with self.assertRaises(c.Refusal) as error:
            c.closure(directory, 'HostAttempt', self.root / 'bad')
        detail = json.loads(str(error.exception).split(': ', 1)[1])
        self.assertEqual(detail['extra']['count'], 30)
        self.assertEqual(len(detail['extra']['first']), 20)
    def test_symlink_escape_and_hardlink(self):
        (self.root / 'escape').symlink_to('/etc/passwd')
        self.refusal(lambda: c.copy_file(self.root / 'escape', self.root, self.root / 'copy'), 'symlink')
        self.refusal(lambda: c.inventory(self.root), 'symlink')
        a = self.root / 'a'; a.write_text('fixture')
        os.link(a, self.root / 'b')
        self.refusal(lambda: c.safe_file(a, self.root), 'hardlink')
    def test_loaded_mismatch(self):
        for name in ('host-attempt-tests/bin/HostAttempt/release/HostAttempt.dll', 'host-attempt-tests/bin/HostAttempt.Tests/release/HostAttempt.dll'):
            path = self.root / name; path.parent.mkdir(parents=True); path.write_text(name)
        self.refusal(lambda: c.selected_loaded(self.root), 'selected/loaded')
    def portable_pdb(self, document):
        blobs = bytearray(b'\0')
        def append(value):
            self.assertLess(len(value), 128)
            index = len(blobs)
            self.assertLess(index, 128)
            blobs.extend(bytes([len(value)]) + value)
            return index
        parts = [append(part.encode()) if part else 0 for part in str(document).split('/')]
        name_index = append(bytes([47] + parts))
        checksum_index = append(hashlib.sha256(document.read_bytes()).digest())
        tables = struct.pack('<IBBBBQQIHHHH', 0, 2, 0, 0, 1, 1 << 48, 0, 1, name_index, 1, checksum_index, 0)
        streams = {'#~': tables, '#Blob': bytes(blobs), '#GUID': bytes.fromhex('0fd02988b8111342878b770e8597ac16'), '#Pdb': bytes(32)}
        header = b'BSJB' + struct.pack('<HHII', 1, 1, 0, 4) + b'v\0\0\0' + struct.pack('<HH', 0, len(streams))
        names = {name: (name.encode() + b'\0') for name in streams}
        names = {name: value + bytes((-len(value)) % 4) for name, value in names.items()}
        offset = len(header) + sum(8 + len(value) for value in names.values())
        payload = b''
        for name, data in streams.items():
            header += struct.pack('<II', offset, len(data)) + names[name]
            offset += len(data)
            payload += data
        path = self.root / 'fixture.pdb'; path.write_bytes(header + payload)
        return path
    def test_portable_pdb_positive_git_join_and_checksum_failure(self):
        source = self.root / 'source'; source.mkdir()
        document = source / 'Program.fs'; document.write_text('printfn "fixture"\n')
        path = self.portable_pdb(document)
        with patch.object(c, 'git', return_value='Program.fs'), patch.object(c.subprocess, 'check_output', return_value=document.read_bytes()):
            result = c.pdb_documents(path, source, self.root, self.evidence)
            self.assertEqual(result['documents'][0]['gitPath'], 'Program.fs')
            self.assertEqual(result['sourceLink'], 'absent')
        document.write_text('changed')
        self.refusal(lambda: c.pdb_documents(path, source, self.root, self.evidence), 'checksum mismatch')
    def test_production_pe_pdb_join_and_wrong_identity(self):
        data = bytearray(512)
        data[:2] = b'MZ'; struct.pack_into('<I', data, 60, 64)
        data[64:68] = b'PE\0\0'
        struct.pack_into('<H', data, 70, 1)
        struct.pack_into('<H', data, 84, 224)
        struct.pack_into('<H', data, 88, 0x10b)
        struct.pack_into('<II', data, 88+96+48, 4096, 28)
        struct.pack_into('<IIII', data, 88+224+8, 100, 4096, 100, 400)
        record = b'RSDS' + bytes(range(16)) + struct.pack('<I', 1) + b'fixture.pdb\0'
        struct.pack_into('<I', data, 404, 123)
        struct.pack_into('<I', data, 412, 2)
        struct.pack_into('<I', data, 416, len(record))
        struct.pack_into('<I', data, 424, 440)
        data[440:440+len(record)] = record
        path = self.root / 'fixture.dll'; path.write_bytes(data)
        identity = bytes(range(16)).hex() + struct.pack('<I', 123).hex()
        self.assertEqual(c.assembly_pdb_join(path, identity)['codeViewPdbPath'], 'fixture.pdb')
        self.refusal(lambda: c.assembly_pdb_join(path, '00'*20), 'identity mismatch')
    def test_missing_or_invalid_pdb_provenance(self):
        path = self.root / 'caller.pdb'; path.write_bytes(b'wrong')
        self.refusal(lambda: c.pdb_documents(path, self.root, self.root, self.evidence), 'portable PDB')
    def test_main_unavailable_tools_and_no_hosted_grant(self):
        arguments = ['custody', '--source', str(self.root / 'source'), '--recipe', str(self.root / 'recipe'), '--output', str(self.root / 'out')]
        with patch.object(c.sys, 'argv', arguments), patch.dict(os.environ, {}, clear=True):
            self.refusal(c.main, 'hosted')
        env = {'GITHUB_ACTIONS': 'true', 'CUSTODY_RUNNER_ENVIRONMENT': 'github-hosted', 'GITHUB_REPOSITORY': 'FS-GG/.github', 'GITHUB_EVENT_NAME': 'push', 'GITHUB_REF': 'refs/heads/main', **{key: 'fixture' for key in ['GITHUB_WORKFLOW_SHA','GITHUB_SHA','GITHUB_RUN_ID','GITHUB_RUN_ATTEMPT','GITHUB_WORKFLOW_REF','GITHUB_JOB','RUNNER_TEMP','ImageOS','ImageVersion']}}
        with patch.object(c.sys, 'argv', arguments), patch.dict(os.environ, env, clear=True), patch.object(c.shutil, 'which', return_value=None):
            self.refusal(c.main, 'missing tool')
            self.assertFalse((self.root / 'out').exists())
        for event in ('workflow_dispatch', 'canceled', 'failure'):
            with patch.object(c.sys, 'argv', arguments), patch.dict(os.environ, dict(env, GITHUB_EVENT_NAME=event), clear=True):
                self.refusal(c.main, 'unsupported event')
    def test_workflow_permissions_upload_and_source_pins(self):
        text = (ROOT / '.github/workflows/host-protected-artifact-custody.yml').read_text()
        for fragment in ('contents: read','cancel-in-progress: false','timeout-minutes: 20','persist-credentials: false','retention-days: 14','overwrite: false', c.SOURCE, c.NATIVE, 'if: failure()', 'payload.tar'):
            self.assertIn(fragment, text)
        for fragment in ('packages: write','id-token: write','secrets:', 'workflow_dispatch:', 'always()', '@v7', 'pull_request_target:'):
            self.assertNotIn(fragment, text)
        self.assertIn('      - name: Static custody preflight\n        working-directory: custody-recipe\n        run: python3 tests/host-protected-artifact-custody/run.py\n', text)
        self.assertLess(text.index('Static custody preflight'), text.index('actions/setup-dotnet'))

if __name__ == '__main__':
    unittest.main(verbosity=2)
