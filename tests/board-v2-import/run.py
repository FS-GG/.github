#!/usr/bin/env python3
"""Exercise the production F# validator/constructor through its actual executable."""
import copy
import json
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'tools/BoardV2Import/BoardV2Import.fsproj'
DLL = ROOT / 'tools/BoardV2Import/bin/Debug/net10.0/BoardV2Import.dll'

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

    def refused(self, m, message, plan=False):
        result = self.call(m, plan)
        self.assertEqual(2, result.returncode, result.stdout)
        self.assertIn(message, result.stderr)

    def test_current_inventory_never_becomes_executable(self):
        result = self.call(self.manifest)
        self.assertEqual(0, result.returncode, result.stderr)
        summary = json.loads(result.stdout)
        self.assertEqual((9,0,0,7,False), tuple(summary[k] for k in ['candidateCount','approvedImportCount','pilotCount','unresolvedCount','executable']))
        self.refused(self.manifest, 'three to five', True)

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
        m=copy.deepcopy(self.manifest);m['target']['id']='PVT_invented';self.refused(m,'must not invent')

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
