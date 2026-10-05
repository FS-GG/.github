"""Collector integrity and coverage controls; no native/helper execution."""
import copy
import json
import tempfile
import unittest
from pathlib import Path
from collect import collect, decode, sha


class CollectorTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.selection = {'schema': 'fsgg.programme.context-window-selection/1', 'identity': {'item': 'fixture'},
                          'helper': {'sha256': 'fixture-only'}, 'records': []}
        for n in range(2):
            self.packet(str(n))

    def put(self, name, data):
        raw = data if isinstance(data, bytes) else json.dumps(data).encode()
        (self.root / name).write_bytes(raw)
        return raw

    def packet(self, stem):
        body = b'Whole mandatory instruction.\n'
        path = '/synthetic/instruction'
        ref = {'path': path, 'sha256': sha(body), 'startLine': 0, 'endLine': 0, 'mandatory': True, 'trust': 'instruction', 'reason': 'fixture'}
        req = {'schema': 'fsgg.programme.packet/1', 'objective': 'synthetic', 'stop': 'no effects', 'mandatoryPaths': [path], 'references': [ref]}
        inp = self.put(stem + '.input.json', req)
        packet = b'Objective: synthetic\nStop: no effects\n' + f'\n--- instruction: {path} ({sha(body)}; fixture) ---\n'.encode() + body + b'\n'
        self.put(stem + '.packet.txt', packet)
        out = self.put(stem + '.json', {'schema': 'fsgg.programme.packet-result/1', 'packetPath': str(self.root / (stem + '.packet.txt')), 'sha256': sha(packet), 'bytes': len(packet), 'selected': [path], 'omitted': []})
        measure = self.put(stem + '.measure.json', {'schema': 'fsgg.programme.measurement/1', 'operation': 'packet', 'inputSha256': sha(inp), 'inputBytes': len(inp), 'outputBytes': len(out), 'artifactBytes': len(out), 'elapsedMs': 1, 'nativeTokens': 'unknown', 'result': 'passed', 'input': str(self.root / (stem + '.input.json')), 'artifact': str(self.root / (stem + '.json'))})
        self.selection['records'].append({'name': stem + '.measure.json', 'sha256': sha(measure), 'inputSha256': sha(inp), 'resultSha256': sha(out), 'packetSha256': sha(packet)})

    def test_deterministic_and_unknown_not_zero(self):
        a = collect(self.root, self.selection)
        self.assertEqual(a, collect(self.root, self.selection))
        self.assertEqual(len(a['operations']), 2)
        self.assertIsNone(a['coverage']['observedModelTurns'])
        self.assertEqual(a['coverage']['nativeUsage'], 'unknown')
        self.assertEqual(a['coverage']['compactions'], 'not-observed')
        self.assertEqual(a['packetProjection']['repeatedConstructionBytes'], len(b'Whole mandatory instruction.\n'))

    def test_equal_length_result_drift(self):
        p = self.root / '0.json'
        p.write_bytes(p.read_bytes().replace(b'instruction', b'xxxxxxxxxxx'))
        with self.assertRaisesRegex(ValueError, 'result-drift'):
            collect(self.root, self.selection)

    def test_input_drift(self):
        p = self.root / '0.input.json'
        p.write_bytes(p.read_bytes() + b' ')
        with self.assertRaisesRegex(ValueError, 'input-drift'):
            collect(self.root, self.selection)

    def test_packet_drift(self):
        p = self.root / '0.packet.txt'
        p.write_bytes(p.read_bytes() + b' ')
        with self.assertRaisesRegex(ValueError, 'packet-drift'):
            collect(self.root, self.selection)

    def test_link_refusal(self):
        p = self.root / '0.packet.txt'
        data = p.read_bytes()
        p.unlink()
        self.put('other', data)
        p.symlink_to(self.root / 'other')
        with self.assertRaisesRegex(ValueError, 'linked-input'):
            collect(self.root, self.selection)

    def test_duplicate_record(self):
        s = copy.deepcopy(self.selection)
        s['records'].append(s['records'][0])
        with self.assertRaisesRegex(ValueError, 'record-name'):
            collect(self.root, s)

    def test_outside_root(self):
        s = copy.deepcopy(self.selection)
        s['records'][0]['name'] = '../outside.measure.json'
        with self.assertRaisesRegex(ValueError, 'record-name'):
            collect(self.root, s)

    def test_duplicate_json_key(self):
        with self.assertRaisesRegex(ValueError, 'duplicate-json-key'):
            decode(b'{"schema":1,"schema":2}')

    def test_population_bound(self):
        s = copy.deepcopy(self.selection)
        s['records'] *= 9
        with self.assertRaisesRegex(ValueError, 'window-bound'):
            collect(self.root, s)

    def test_public_fixture_coverage(self):
        fixture = json.loads(Path(__file__).with_name('replay.json').read_text())
        self.assertEqual(len(fixture['operations']), 5)
        self.assertEqual(fixture['totals']['inputBytes'], 39546)
        self.assertEqual(fixture['coverage']['observedModelTurns'], None)
        text = json.dumps(fixture)
        self.assertNotIn('/home/', text)
        self.assertNotIn('sc2_adapter', text)
        self.assertEqual([r['result'] for r in fixture['operations']], ['passed', 'passed', 'passed', 'refused', 'passed'])


if __name__ == '__main__':
    unittest.main()
