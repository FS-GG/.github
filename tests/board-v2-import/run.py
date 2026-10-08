#!/usr/bin/env python3
"""Exercise the production F# validator/constructor through its actual executable."""
import base64
import copy
import hashlib
import zlib
import json
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'tools/BoardV2Import/BoardV2Import.fsproj'
DLL = ROOT / 'tools/BoardV2Import/bin/Debug/net10.0/BoardV2Import.dll'

# Exact immutable blob b8e99000690ba0136290b40b6a33e3a43cbc7f02; usable in shallow CI checkouts.
HISTORICAL_FOUR_TARGET_SHA256 = '7322cdaf3b5a16449e2e5863efb915249cb5802e4972a8d4b139c8f09d73c84c'
HISTORICAL_FOUR_TARGET_ZLIB_BASE64 = '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'

class ManifestTests(unittest.TestCase):
    def setUp(self):
        self.manifest = json.loads((ROOT / 'docs/coordination/board-v2-import-manifest.json').read_text())

    def call(self, manifest, plan=False):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / 'manifest.json'
            path.write_text(json.dumps(manifest))
            return subprocess.run(['dotnet', str(DLL), str(path), *(['--pilot-plan'] if plan else [])], capture_output=True, text=True)

    def approved(self, count=3):
        m = copy.deepcopy(self.manifest)
        m['items'] = m['items'][2:2+count]
        for item in m['items']:
            item.update(decision='import', pilot=True, adjudication='verified-remaining', remainingOutcome='fixture remaining outcome', roadmap='docs/fixture.md', acceptanceEvidence='fixture exact owning-plan revision', track='Active delivery', dependencies=[])
        m['target'].update(creationState='created-and-read-back', id='PVT_fixture_v2', number=2)
        m['binding'].update(visibility='complete', authorization='root-selected', recipeRevision='a'*40, artifactSha256='b'*64, repositories=['FS-GG/.github'])
        for index, field in enumerate(m['fields']):
            field['id'] = 'FIELD_'+str(index)
            if field['kind'] == 'single-select': field['optionIds'] = {name:'option_'+str(index)+'_'+str(n) for n,name in enumerate(field['options'])}
        return m

    def pending(self):
        """Retain the historical unbound inventory independently of the live target."""
        m = copy.deepcopy(self.manifest)
        m['items'] = m['items'][:8] + [m['items'][-1]]
        m['items'][-1].update(decision='adjudicate', pilot=False, adjudication='unknown',
                              remainingOutcome=None, acceptanceEvidence='unknown')
        m['target'].update(creationState='pending-authorized-operation', id=None, number=None)
        m['binding'].update(visibility='incomplete', authorization='unknown',
                            recipeRevision=None, artifactSha256=None, repositories=[])
        for field in m['fields']:
            field['id'] = None
            field.pop('optionIds', None)
        return m

    def refused(self, m, message, plan=False):
        result = self.call(m, plan)
        self.assertEqual(2, result.returncode, result.stdout)
        self.assertIn(message, result.stderr)

    def test_current_inventory_never_becomes_executable(self):
        result = self.call(self.manifest)
        self.assertEqual(0, result.returncode, result.stderr)
        summary = json.loads(result.stdout)
        self.assertEqual((12,3,3,6,True,False), tuple(summary[k] for k in
                         ['candidateCount','approvedImportCount','pilotCount','unresolvedCount',
                          'planConstructible','executable']))
        self.assertEqual('created-and-read-back', summary['targetCreationState'])
        result = self.call(self.manifest, True)
        self.assertEqual(0, result.returncode, result.stderr)
        plan = json.loads(result.stdout)
        selected = [item for item in self.manifest['items'] if item['pilot']]
        self.assertEqual(['FS-GG/FS.GG.SDD#928', 'FS-GG/FS.GG.Templates#441',
                          'FS-GG/.github#3010'], [item['issue'] for item in selected])
        self.assertEqual(selected, plan['items'])
        history = next(item for item in self.manifest['items'] if item['issue'] == 'FS-GG/.github#3009')
        self.assertEqual('I_kwDOS6feoM8AAAABOUU1ew', history['nodeId'])
        self.assertEqual(('closed', 'omit-delivered', False, None, 'unknown', 'Ready'),
                         tuple(history[k] for k in ['observedState', 'decision', 'pilot', 'remainingOutcome', 'adjudication', 'status']))
        self.assertEqual([], selected[-1]['dependencies'])
        historical = self.manifest['inventory']['pilotOperation']
        self.assertEqual(3, historical['selectedCount'])
        self.assertEqual(['FS-GG/FS.GG.SDD#928', 'FS-GG/FS.GG.Templates#441',
                          'FS-GG/.github#3010'], [item['issue'] for item in historical['memberships']])
        self.assertFalse(self.manifest['inventory']['unexpectedMembership']['pilot'])
        self.assertFalse(self.manifest['inventory']['unexpectedMembership']['planningFieldsSeeded'])
        self.assertEqual('source-prepared-awaiting-root-qualification',
                         self.manifest['inventory']['successorPreparation']['state'])
        self.assertEqual(self.manifest['target'], plan['target'])
        self.assertEqual(self.manifest['fields'], plan['fields'])
        self.assertEqual(self.manifest['binding'], plan['binding'])

    def test_historical_four_target_fixture_keeps_its_original_bytes(self):
        # Immutable historical population; never relabel it as current qualification.
        historical_bytes = zlib.decompress(base64.b64decode(HISTORICAL_FOUR_TARGET_ZLIB_BASE64))
        self.assertEqual(HISTORICAL_FOUR_TARGET_SHA256, hashlib.sha256(historical_bytes).hexdigest())
        historical = json.loads(historical_bytes)
        self.assertEqual(self.manifest['inventory'], historical['inventory'])
        self.assertEqual(4, sum(item['pilot'] for item in historical['items']))
        plan = self.call(historical, True)
        self.assertEqual(0, plan.returncode, plan.stderr)
        self.assertEqual(4, len(json.loads(plan.stdout)['items']))
        historical3009 = next(item for item in historical['items'] if item['issue'] == 'FS-GG/.github#3009')
        self.assertEqual(('open', 'import', True, '2026-08-26T20:18:16Z'),
                         tuple(historical3009[k] for k in ['observedState', 'decision', 'pilot', 'observedUpdatedAt']))

    def test_closed_history_cannot_become_an_add_or_seed(self):
        result = self.call(self.manifest, True)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotIn('FS-GG/.github#3009', [item['issue'] for item in json.loads(result.stdout)['items']])
        m = copy.deepcopy(self.manifest)
        row = next(item for item in m['items'] if item['issue'] == 'FS-GG/.github#3009')
        row.update(decision='import', pilot=True, adjudication='verified-remaining', remainingOutcome='invented')
        self.refused(m, 'remaining open')

    def test_fixed_plan_preserves_identities_and_separates_effects(self):
        m=self.approved(); result=self.call(m,True)
        self.assertEqual(0,result.returncode,result.stderr)
        plan=json.loads(result.stdout)
        self.assertEqual(m['items'],plan['items'])
        self.assertEqual(5,plan['limits']['maxItems'])
        self.assertIn('seed-once-preserve-conflicting-owner-edits',plan['effects'])
        self.assertNotIn('create-project',plan['effects'])
        self.assertEqual(result.stdout,self.call(m,True).stdout)

    def test_three_to_five_bound(self):
        self.refused(self.approved(2),'three to five',True)
        m=self.approved()
        for n in range(3):
            item=copy.deepcopy(m['items'][0]);item.update(issue=f'FS-GG/.github#{90000+n}',nodeId=f'I_fixture_{n}');m['items'].append(item)
        self.refused(m,'maximum five',True)

    def test_no_arbitrary_effect_recipe_or_unobserved_dependency(self):
        m=self.approved();m['command']='echo arbitrary';self.refused(m,'unexpected property')
        m=self.approved();m['items'][0]['dependencies']=[{'issue':'FS-GG/.github#1','observedState':'unknown','evidence':'unknown'}];self.refused(m,'native dependency snapshot',True)
        m=self.approved();m['items'][0]['acceptanceEvidence']='unknown';self.refused(m,'actual acceptance evidence')

    def test_duplicate_issue_and_node_refuse(self):
        for key in ['issue','nodeId']:
            m=self.approved();m['items'][1][key]=m['items'][0][key]
            self.refused(m,'duplicate issue')

    def test_pull_request_identity_refuses(self):
        m=self.approved();m['items'][0]['nodeId']='PR_fixture';self.refused(m,'issue node')

    def test_pending_identity_refuses(self):
        pending = self.pending()
        result = self.call(pending)
        self.assertEqual(0, result.returncode, result.stderr)
        summary = json.loads(result.stdout)
        self.assertEqual((9,0,0,7,False,False), tuple(summary[k] for k in
                         ['candidateCount','approvedImportCount','pilotCount','unresolvedCount',
                          'planConstructible','executable']))
        self.assertEqual('pending-authorized-operation', summary['targetCreationState'])
        self.refused(pending, 'three to five', True)
        for key, value in [('id', 'PVT_invented'), ('number', 2)]:
            with self.subTest(key=key):
                m = copy.deepcopy(pending)
                m['target'][key] = value
                self.refused(m, 'must not invent')
        m = copy.deepcopy(pending)
        m['fields'][0]['id'] = 'FIELD_invented'
        self.refused(m, 'pending Status must not invent identity')

    def test_legacy_target_refuses(self):
        m=self.approved();m['target']['number']=1;self.refused(m,'Project 1')
        m=self.approved();m['target']['id']=m['source']['id'];self.refused(m,'target id')

    def test_incomplete_or_denied_visibility_refuses(self):
        for visibility in ['incomplete','unknown']:
            m=self.approved();m['binding']['visibility']=visibility;self.refused(m,'visibility differs',True)
        m=self.approved();m['binding']['authorization']='unknown';self.refused(m,'authorization differs',True)

    def test_untrusted_recipe_refuses(self):
        for key in ['recipeRevision','artifactSha256']:
            m=self.approved();m['binding'][key]='arbitrary';self.refused(m,'recipe/artifact',True)

    def test_foreign_repository_refuses(self):
        m=self.approved();m['items'][0]['issue']='FS-GG/Foreign#42';self.refused(m,'foreign repository',True)

    def test_all_field_kinds_options_owners_refuse_drift(self):
        for index in range(4):
            for key,value in [('kind','drift'),('owner','drift'),('options',['drift'])]:
                m=self.approved();m['fields'][index][key]=value;self.refused(m,'differ')
        m=self.approved();m['fields'][0]['refreshMayWrite']=True;self.refused(m,'ownership differs')

    def test_missing_or_reused_field_option_id_refuses(self):
        m=self.approved();m['fields'][1]['id']=m['fields'][0]['id'];self.refused(m,'duplicate field')
        m=self.approved();m['fields'][0]['optionIds']['Ready']='';self.refused(m,'Ready is required',True)
        m=self.approved();m['fields'][0]['optionIds']['Ready']=m['fields'][0]['optionIds']['Backlog'];self.refused(m,'duplicate option',True)

    def test_unadjudicated_closed_or_missing_acceptance_refuses(self):
        for key,value,message in [('adjudication','unknown','adjudication'),('observedState','closed','remaining open'),('acceptanceEvidence','','acceptanceEvidence')]:
            m=self.approved();m['items'][0][key]=value;self.refused(m,message)

    def test_invalid_seed_and_duplicate_dependency_refuse(self):
        m=self.approved();m['items'][0]['status']='Claimed';self.refused(m,'Status seed')
        m=self.approved();m['items'][0]['dependencies']=[{'issue':'FS-GG/.github#1','observedState':'unknown','evidence':'unknown'}]*2;self.refused(m,'duplicate dependency')

if __name__ == '__main__':
    subprocess.run(['dotnet','build',str(PROJECT),'--nologo','-m:1','-p:UseSharedCompilation=false'],check=True)
    unittest.main()
