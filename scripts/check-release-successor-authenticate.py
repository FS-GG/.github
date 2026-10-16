#!/usr/bin/env python3
"""Validate the closed inactive authentication workflow; never execute a job."""
from __future__ import annotations

import argparse
import pathlib
import sys

import yaml

WORKFLOW = '.github/workflows/release-successor-authenticate.yml'
SOURCE_WORKFLOW = '.github/workflows/release-successor-authenticate-source.yml'
JOB = 'original-103-authentication'
DESCRIPTOR_JOB = 'original-103-descriptor-issuer'
GUARD = "${{ false && github.event_name == 'workflow_dispatch' && github.repository == 'FS-GG/.github' && github.ref == 'refs/heads/main' && github.actor == 'EHotwagner' && github.run_attempt == 1 }}"
CHECKOUT = 'actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1'
REFUSAL = "printf '%s\\n' 'Inactive source preparation; authentication is unavailable.' >&2\nexit 78\n"

DESCRIPTOR_REFUSAL = "printf '%s\\n' 'Inactive descriptor preparation; issuer admission, durable reservation and signer are unavailable.' >&2\nexit 78\n"


class Refused(ValueError):
    pass


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


class ClosedLoader(yaml.SafeLoader):
    """Reject duplicate keys rather than accepting a hidden later permission."""


def mapping(loader, node):
    pairs = loader.construct_pairs(node)
    result = {}
    for key, value in pairs:
        if key in result:
            raise Refused('duplicate workflow key')
        result[key] = value
    return result


ClosedLoader.add_constructor(yaml.resolver.BaseResolver.DEFAULT_MAPPING_TAG, mapping)


def parse(raw: str) -> dict:
    value = yaml.load(raw, Loader=ClosedLoader)
    require(isinstance(value, dict), 'workflow must be a mapping')
    # PyYAML's YAML1.1 reader recognizes the Actions key `on` as boolean true.
    if True in value:
        require('on' not in value, 'ambiguous workflow trigger')
        value['on'] = value.pop(True)
    return value


def validate(doc: dict) -> None:
    require(set(doc) == {'name', 'on', 'permissions', 'concurrency', 'jobs'},
            'extra or missing workflow surface')
    require(doc['name'] == 'release-successor-authenticate', 'workflow identity differs')
    require(doc['on'] == {'workflow_dispatch': None}, 'only manual dispatch may select the inactive job')
    require(doc['permissions'] == {'contents': 'read'}, 'workflow permissions exceed contents read')
    require(doc['concurrency'] == {'group': 'release-successor-authenticate', 'cancel-in-progress': False},
            'concurrency selection differs')
    require(isinstance(doc['jobs'], dict) and set(doc['jobs']) == {JOB, DESCRIPTOR_JOB},
            'job identity or population differs')
    validate_job(doc['jobs'][JOB], 'callable-isolated-operation',
                 'Refuse activation without a separately admitted native producer', REFUSAL)
    validate_job(doc['jobs'][DESCRIPTOR_JOB], 'utel-original-descriptor',
                 'Refuse descriptor issuance without qualified trusted ports', DESCRIPTOR_REFUSAL)


def validate_job(job: dict, environment: str, terminal_name: str, refusal: str) -> None:
    """Each declared route is independently closed and unconditionally inactive."""
    require(isinstance(job, dict) and set(job) ==
            {'if', 'environment', 'permissions', 'runs-on', 'timeout-minutes', 'steps'},
            'extra or missing job surface')
    require(job['if'] == GUARD, 'job must remain unconditionally inactive with complete identity fences')
    require(job['environment'] == environment, 'protected environment differs')
    require(job['permissions'] == {'contents': 'read'}, 'job permissions exceed contents read')
    require(job['runs-on'] == 'ubuntu-24.04' and type(job['timeout-minutes']) is int
            and job['timeout-minutes'] == 5, 'runner or preparation budget differs')
    steps = job['steps']
    require(isinstance(steps, list) and len(steps) == 2, 'no extra credential, native or effect steps')
    checkout, terminal = steps
    require(checkout == {'name': 'Read the selected immutable source', 'uses': CHECKOUT,
                         'with': {'ref': '${{ github.sha }}', 'fetch-depth': 1,
                                  'persist-credentials': False}},
            'checkout must pin the action and exact source without persistent credentials')
    require(terminal == {'name': terminal_name, 'run': refusal}, 'terminal path must only refuse native authentication')


def validate_source_caller(doc: dict) -> None:
    """The ordinary CI caller cannot inherit the protected job's capabilities."""
    paths = [WORKFLOW, SOURCE_WORKFLOW, '.github/actions/setup-policy-python/**',
             'scripts/check-release-successor-authenticate.py',
             'tests/release-successor-authenticate/**']
    require(set(doc) == {'name', 'on', 'permissions', 'jobs'}, 'extra source caller surface')
    require(doc['name'] == 'release-successor-authenticate-source', 'source caller identity differs')
    require(doc['on'] == {'pull_request': {'paths': paths},
                         'push': {'branches': ['main'], 'paths': paths}},
            'source controls must cover every input on PR and protected push only')
    require(doc['permissions'] == {'contents': 'read'}, 'source workflow permissions differ')
    require(isinstance(doc['jobs'], dict) and set(doc['jobs']) == {'inert-source-controls'},
            'source caller job population differs')
    job = doc['jobs']['inert-source-controls']
    require(isinstance(job, dict) and set(job) ==
            {'runs-on', 'timeout-minutes', 'permissions', 'steps'},
            'source caller cannot select a protected environment, credentials or native job')
    require(job['runs-on'] == 'ubuntu-24.04' and type(job['timeout-minutes']) is int
            and job['timeout-minutes'] == 5, 'source caller runner or budget differs')
    require(job['permissions'] == {'contents': 'read'}, 'source job permissions differ')
    require(job['steps'] == [
        {'uses': CHECKOUT, 'with': {'ref': '${{ github.sha }}', 'fetch-depth': 1,
                                  'persist-credentials': False}},
        {'uses': './.github/actions/setup-policy-python'},
        {'name': 'Check inactive policy and rejection controls without native execution',
         'env': {'PYTHONDONTWRITEBYTECODE': '1'},
         'run': 'python3 scripts/check-release-successor-authenticate.py\n'
                'python3 tests/release-successor-authenticate/run.py\n'},
    ], 'source caller must run only maintained Python controls through pinned source')


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('workflow', nargs='?', default=WORKFLOW)
    args = parser.parse_args()
    try:
        validate(parse(pathlib.Path(args.workflow).read_text()))
        validate_source_caller(parse(pathlib.Path(SOURCE_WORKFLOW).read_text()))
    except (Refused, OSError, yaml.YAMLError, TypeError) as error:
        print('Inactive authentication workflow refused: ' + str(error), file=sys.stderr)
        return 1
    print('Inactive authentication workflow: closed identity, read-only permissions, no credential/native route.')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
