#!/usr/bin/env python3
"""Read-only pipeline investment estimates and literal dependency checks."""
import argparse
import json
import math
from pathlib import Path
import re
import sys

NUMBERS = (
    'horizon_runs', 'setup_cost', 'maintenance_cost', 'defect_probability_low',
    'defect_probability_high', 'detection_probability', 'avoidable_runner_minutes',
    'runner_cost_per_minute', 'preflight_runner_minutes', 'false_block_probability', 'triage_cost',
)
PROBABILITIES = {n for n in NUMBERS if 'probability' in n}


def assess(data):
    if not isinstance(data, dict):
        raise ValueError('Estimate must be an object')
    extra = set(data) - set(NUMBERS) - {'assumptions'}
    if extra:
        raise ValueError('Unknown fields: ' + ', '.join(sorted(extra)))
    missing = [n for n in (*NUMBERS, 'assumptions') if data.get(n) is None]
    if missing:
        return {'decision': 'insufficient-data', 'missing': missing, 'basis': 'estimates, not observed savings'}
    if not isinstance(data['assumptions'], str) or not data['assumptions'].strip():
        raise ValueError('Provide the horizon, rates and provenance in assumptions')
    for name in NUMBERS:
        value = data[name]
        if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
            raise ValueError(name + ' must be a finite nonnegative number')
        if name in PROBABILITIES and value > 1:
            raise ValueError(name + ' must be between 0 and 1')
    if data['horizon_runs'] != int(data['horizon_runs']):
        raise ValueError('horizon_runs must be an integer')
    if data['defect_probability_low'] > data['defect_probability_high']:
        raise ValueError('Probability bounds are reversed')
    fixed = data['setup_cost'] + data['maintenance_cost']
    overhead = data['preflight_runner_minutes'] * data['runner_cost_per_minute'] + data['false_block_probability'] * data['triage_cost']
    outcomes = {}
    for label in ('low', 'high'):
        benefit = data['defect_probability_' + label] * data['detection_probability'] * data['avoidable_runner_minutes'] * data['runner_cost_per_minute']
        margin = benefit - overhead
        net = data['horizon_runs'] * margin - fixed
        if not all(math.isfinite(n) for n in (fixed, overhead, benefit, margin, net)):
            raise ValueError('Costs exceed supported numeric range')
        outcomes[label] = {'net_benefit': net, 'net_per_run': margin,
                           'break_even_runs': math.ceil(fixed / margin) if margin > 0 else None}
    decision = ('not-cost-justified' if outcomes['high']['net_benefit'] <= 0 else
                'pilot-candidate' if outcomes['low']['net_benefit'] > 0 else 'uncertain')
    return {'decision': decision, 'basis': 'estimates, not observed savings', 'assumptions': data['assumptions'],
            'outcomes': outcomes, 'authority': 'advisory; never waives required checks'}


def graph(data, requirements):
    if not isinstance(data, dict) or not isinstance(data.get('jobs'), dict) or not data['jobs']:
        raise ValueError('Workflow must contain a nonempty jobs mapping')
    jobs = data['jobs']
    if len(jobs) > 256:
        raise ValueError('More than 256 jobs: select a bounded explicit scope')
    valid_id = re.compile(r'^[A-Za-z_][A-Za-z0-9_-]*$')
    deps = {}
    for name, job in jobs.items():
        if not isinstance(name, str) or not valid_id.fullmatch(name) or not isinstance(job, dict):
            raise ValueError('Expected literal job identifiers and job mappings')
        needs = job.get('needs', [])
        if isinstance(needs, str):
            needs = [needs]
        if not isinstance(needs, list) or any(not isinstance(n, str) or not valid_id.fullmatch(n) for n in needs):
            raise ValueError(f'{name}: needs must be literal job identifiers; expressions are unsupported')
        if len(needs) != len(set(needs)):
            raise ValueError(f'{name}: duplicate dependency')
        deps[name] = set(needs)
    for name, needs in deps.items():
        if needs - jobs.keys():
            raise ValueError(f'{name}: unknown dependencies {sorted(needs - jobs.keys())}')
    visiting, ancestors = set(), {}
    def visit(name):
        if name in visiting:
            raise ValueError(f'Dependency cycle at {name}')
        if name not in ancestors:
            visiting.add(name)
            result = set(deps[name])
            for dependency in deps[name]:
                result.update(visit(dependency))
            visiting.remove(name)
            ancestors[name] = result
        return ancestors[name]
    for name in jobs:
        visit(name)
    for requirement in requirements:
        target, separator, sources = requirement.partition(':')
        needed = sources.split(',')
        if not separator or target not in jobs or any(n not in jobs for n in needed):
            raise ValueError(f'Unknown or malformed requirement {requirement!r}; use target:prerequisite[,prerequisite]')
        missing = set(needed) - ancestors[target]
        if missing:
            return {'decision': 'blocked', 'scope': 'dependency-order-only', 'target': target, 'missing_ancestors': sorted(missing)}
    return {'decision': 'passed', 'scope': 'dependency-order-only', 'jobs': len(jobs), 'requirements': requirements}


def read_yaml(text):
    try:
        import yaml
    except ImportError as error:
        raise ValueError('graph requires PyYAML in the current environment; no dependencies were installed') from error
    class UniqueLoader(yaml.SafeLoader):
        pass
    def mapping(loader, node, deep=False):
        loader.flatten_mapping(node)
        result = {}
        for key_node, value_node in node.value:
            key = loader.construct_object(key_node, deep=deep)
            if key in result:
                raise ValueError(f'Duplicate YAML key: {key}')
            result[key] = loader.construct_object(value_node, deep=deep)
        return result
    UniqueLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, mapping)
    return yaml.load(text, Loader=UniqueLoader)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest='command', required=True)
    sub.add_parser('assess', help='advisory investment estimate').add_argument('path')
    g = sub.add_parser('graph', help='check literal YAML needs ordering only')
    g.add_argument('path')
    g.add_argument('--requires', action='append', required=True, metavar='TARGET:PREREQUISITE,...')
    args = parser.parse_args()
    try:
        raw = Path(args.path).read_bytes()
        if len(raw) > 2 * 1024 * 1024:
            raise ValueError('Input exceeds 2 MiB')
        text = raw.decode('utf-8')
        result = assess(json.loads(text)) if args.command == 'assess' else graph(read_yaml(text), args.requires)
        print(json.dumps(result, indent=2, allow_nan=False))
        return 1 if result['decision'] == 'blocked' else 0
    except Exception as error:
        # Any input/parser/dependency problem is an explicit refusal, never a passing preflight.
        print(json.dumps({'decision': 'error', 'message': str(error)}), file=sys.stderr)
        return 2


if __name__ == '__main__':
    sys.exit(main())
