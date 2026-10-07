"""Canonical provider schema typing; no provider calls or API acceptance claim."""
import copy
import json
from pathlib import Path
import unittest

from jsonschema import Draft202012Validator, FormatChecker

ROOT = Path(__file__).resolve().parents[2]
CONTRACTS = ROOT / 'contracts/process-efficiency'
SCHEMA = json.loads((CONTRACTS / 'assessment-v1.schema.json').read_text())
INPUT_SCHEMA = json.loads((CONTRACTS / 'assessment-input-v1.schema.json').read_text())
EMBEDDED = copy.deepcopy(INPUT_SCHEMA['properties']['assessment'])
EMBEDDED['$defs'] = {'sourceRef': INPUT_SCHEMA['$defs']['sourceRef']}


def nodes(schema):
    yield schema
    for key in ('properties', '$defs'):
        for child in schema.get(key, {}).values():
            yield from nodes(child)
    if 'items' in schema:
        yield from nodes(schema['items'])


def without_redundant_string_types(schema):
    result = copy.deepcopy(schema)
    for node in nodes(result):
        values = node.get('enum', [node['const']] if 'const' in node else [])
        if values and all(isinstance(value, str) for value in values):
            if node.get('type') == 'string':
                del node['type']
    return result


class AssessmentSchemaTests(unittest.TestCase):
    def test_provider_nodes_are_typed_and_closed(self):
        Draft202012Validator.check_schema(SCHEMA)
        for node in nodes(SCHEMA):
            self.assertTrue('type' in node or '$ref' in node)
            if node.get('type') == 'object':
                self.assertEqual(set(node['required']), set(node['properties']))
                self.assertIs(node['additionalProperties'], False)

    def test_every_existing_receiver_constraint_is_preserved(self):
        standalone = without_redundant_string_types(SCHEMA)
        embedded = without_redundant_string_types(EMBEDDED)
        for key in ('$schema', '$id', '$comment'):
            standalone.pop(key, None)
        self.assertEqual(standalone, embedded)

    def test_full_validator_matches_receiver_for_samples_and_mutations(self):
        before = Draft202012Validator(EMBEDDED, format_checker=FormatChecker())
        after = Draft202012Validator(SCHEMA, format_checker=FormatChecker())
        fixtures = json.loads((ROOT / 'tests/process-efficiency/metric-fixtures-v1.json').read_text())
        for sample in fixtures['assessmentSamples']:
            self.assertTrue(before.is_valid(sample))
            self.assertTrue(after.is_valid(sample))
            mutations = []
            for key, value in [('schema', None), ('revision', 0), ('evidenceDigest', 'not-a-digest'),
                               ('outcomeSynopsis', ''), ('extra', True)]:
                mutated = copy.deepcopy(sample)
                mutated[key] = value
                mutations.append(mutated)
            missing = copy.deepcopy(sample)
            del missing['publication']
            mutations.append(missing)
            wrong_enum = copy.deepcopy(sample)
            wrong_enum['coverage']['population'] = 'invented'
            mutations.append(wrong_enum)
            wrong_date = copy.deepcopy(sample)
            wrong_date['provenance']['startedAt'] = 'not-a-date'
            mutations.append(wrong_date)
            long_array = copy.deepcopy(sample)
            long_array['wentWell'] = ['Retained source evidence.'] * 9
            mutations.append(long_array)
            for mutation in mutations:
                self.assertFalse(before.is_valid(mutation))
                self.assertEqual(before.is_valid(mutation), after.is_valid(mutation))


if __name__ == '__main__':
    unittest.main()
