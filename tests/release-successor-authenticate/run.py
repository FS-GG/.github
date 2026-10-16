#!/usr/bin/env python3
"""Inert policy mutants for the inactive workflow; no workflow/provider execution."""
from __future__ import annotations

import copy
import hashlib
import json
import importlib.util
import pathlib
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
spec = importlib.util.spec_from_file_location('inactive_auth_policy', ROOT / 'scripts/check-release-successor-authenticate.py')
policy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(policy)


class InactiveAuthenticationControls(unittest.TestCase):
    JOB = policy.JOB
    def setUp(self):
        self.raw = (ROOT / policy.WORKFLOW).read_text()
        self.doc = policy.parse(self.raw)

    def reject(self, mutate):
        doc = copy.deepcopy(self.doc)
        mutate(doc)
        with self.assertRaises(policy.Refused):
            policy.validate(doc)

    def test_actual_workflow_is_closed_and_inactive(self):
        policy.validate(self.doc)

    def test_inactivity_and_each_identity_fence_cannot_be_removed_or_changed(self):
        for fragment in ["false && ", "github.event_name == 'workflow_dispatch' && ",
                         "github.repository == 'FS-GG/.github' && ",
                         "github.ref == 'refs/heads/main' && ",
                         "github.actor == 'EHotwagner' && ", " && github.run_attempt == 1"]:
            with self.subTest(fragment=fragment):
                self.reject(lambda d: d['jobs'][self.JOB].update({'if': policy.GUARD.replace(fragment, '')}))

    def test_other_workflow_job_environment_or_trigger_refuses(self):
        for mutate in [lambda d: d.update(name='release-successor-publish'),
                       lambda d: d.update(on={'push': None}),
                       lambda d: d['jobs'].update({'another': d['jobs'].pop(self.JOB)}),
                       lambda d: d['jobs'][self.JOB].update(environment='release-successor')]:
            with self.subTest(mutate=mutate): self.reject(mutate)

    def test_permission_escalation_and_inheritance_refuse(self):
        for level in ['workflow', 'job']:
            for permissions in [{}, 'write-all', {'contents': 'write'},
                                {'contents': 'read', 'id-token': 'write'},
                                {'contents': 'read', 'packages': 'read'},
                                {'contents': 'read', 'actions': 'write'}]:
                with self.subTest(level=level, permissions=permissions):
                    def mutate(d):
                        subject = d if level == 'workflow' else d['jobs'][self.JOB]
                        subject['permissions'] = permissions
                    self.reject(mutate)

    def test_mutable_action_source_and_persisted_token_refuse(self):
        for field, value in [('uses', 'actions/checkout@v7'), ('ref', 'main'),
                             ('persist-credentials', True), ('token', '${{ github.token }}')]:
            with self.subTest(field=field):
                def mutate(d):
                    step = d['jobs'][self.JOB]['steps'][0]
                    if field == 'uses': step[field] = value
                    else: step['with'][field] = value
                self.reject(mutate)

    def test_credential_ingress_at_any_level_refuses(self):
        for level in ['workflow', 'job', 'step']:
            for variable in ['GH_TOKEN', 'GITHUB_TOKEN', 'APP_KEY', 'NUGET_API_KEY']:
                with self.subTest(level=level, variable=variable):
                    def mutate(d):
                        subject = d if level == 'workflow' else d['jobs'][self.JOB]
                        if level == 'step': subject = subject['steps'][1]
                        subject['env'] = {variable: '${{ secrets.UNAVAILABLE }}'}
                    self.reject(mutate)

    def test_native_or_effect_steps_and_terminal_success_refuse(self):
        for command in ['dotnet fsi native.fsx', 'gh api /app', 'gh workflow run effect.yml',
                        'git push origin main', 'dotnet nuget push candidate.nupkg',
                        'curl https://api.github.com/app', 'exit 0']:
            with self.subTest(command=command):
                self.reject(lambda d: d['jobs'][self.JOB]['steps'][1].update(run=command))
                self.reject(lambda d: d['jobs'][self.JOB]['steps'].append({'run': command}))

    def test_duplicate_keys_do_not_hide_later_permissions(self):
        with self.assertRaises(policy.Refused):
            policy.parse(self.raw.replace('  contents: read', '  contents: read\n  contents: write', 1))

    def test_unsupported_runner_budget_or_shared_environment_capability_refuses(self):
        for field, value in [('runs-on', 'self-hosted'), ('timeout-minutes', 60),
                             ('secrets', 'inherit'), ('container', 'image'),
                             ('services', {'provider': {'image': 'image'}})]:
            with self.subTest(field=field):
                self.reject(lambda d: d['jobs'][self.JOB].update({field: value}))


class InactiveDescriptorIssuerControls(InactiveAuthenticationControls):
    JOB = policy.DESCRIPTOR_JOB

    def test_selected_principal_custody_cannot_use_existing_authentication_environment(self):
        self.reject(lambda d: d['jobs'][self.JOB].update(environment='callable-isolated-operation'))

    def test_descriptor_job_cannot_be_omitted_or_replaced_by_original_job(self):
        self.reject(lambda d: d['jobs'].pop(self.JOB))
        self.reject(lambda d: d['jobs'].update({self.JOB: copy.deepcopy(d['jobs'][policy.JOB])}))

    def test_original_authentication_job_matches_protected_baseline(self):
        # Protected a9b13c15: adding the descriptor route cannot rewrite the old job.
        original = json.dumps(self.doc['jobs'][policy.JOB], sort_keys=True, separators=(',', ':')).encode()
        self.assertEqual(hashlib.sha256(original).hexdigest(), '322152dbbaa93f70b8cd3e0aaf4cf041c28cefaad856dc9adc583a9f2ab6a320')


class ReadOnlySourceCallerControls(unittest.TestCase):
    def setUp(self):
        self.doc = policy.parse((ROOT / policy.SOURCE_WORKFLOW).read_text())

    def test_actual_source_caller_runs_policy_and_mutants(self):
        policy.validate_source_caller(self.doc)

    def reject(self, mutate):
        doc = copy.deepcopy(self.doc)
        mutate(doc)
        with self.assertRaises(policy.Refused): policy.validate_source_caller(doc)

    def test_every_input_remains_in_both_trigger_sets(self):
        for event in ['pull_request', 'push']:
            for index in range(5):
                with self.subTest(event=event, index=index):
                    self.reject(lambda d: d['on'][event]['paths'].pop(index))
        self.reject(lambda d: d['on'].update(workflow_dispatch=None))
        self.reject(lambda d: d['on']['push'].update(branches=['other']))

    def test_source_caller_cannot_gain_protected_or_write_capabilities(self):
        for field, value in [('environment', 'callable-isolated-operation'),
                             ('env', {'GH_TOKEN': '${{ github.token }}'}),
                             ('permissions', {'contents': 'write'}),
                             ('secrets', 'inherit'), ('uses', 'owner/repo/workflow@main')]:
            with self.subTest(field=field):
                self.reject(lambda d: d['jobs']['inert-source-controls'].update({field: value}))
        self.reject(lambda d: d.update(permissions={'id-token': 'write'}))

    def test_source_caller_cannot_skip_controls_or_add_effects(self):
        for command in ['true', 'dotnet fsi native.fsx', 'gh workflow run effect.yml']:
            with self.subTest(command=command):
                self.reject(lambda d: d['jobs']['inert-source-controls']['steps'][2].update(run=command))
        self.reject(lambda d: d['jobs']['inert-source-controls']['steps'].append({'run': 'true'}))
        self.reject(lambda d: d['jobs']['inert-source-controls']['steps'][0].update(uses='actions/checkout@v7'))


if __name__ == '__main__':
    unittest.main(verbosity=2)
