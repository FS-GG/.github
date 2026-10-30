#!/usr/bin/env python3
"""Exercise the production F# validator/constructor through its actual executable."""
import base64
import copy
import hashlib
import zlib
import json
import hashlib
import os
import sys
import pathlib
import subprocess
import tempfile
import unittest

ROOT = pathlib.Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'tools/BoardV2Import/BoardV2Import.fsproj'
DLL = pathlib.Path(os.environ.get('BOARD_V2_IMPORT_DLL', ROOT / 'tools/BoardV2Import/bin/Debug/net10.0/BoardV2Import.dll'))
SOURCE_REVISION = os.environ.get('BOARD_V2_IMPORT_SOURCE_REVISION')

# Exact immutable blob b8e99000690ba0136290b40b6a33e3a43cbc7f02; usable in shallow CI checkouts.
HISTORICAL_FOUR_TARGET_SHA256 = '7322cdaf3b5a16449e2e5863efb915249cb5802e4972a8d4b139c8f09d73c84c'
HISTORICAL_FOUR_TARGET_ZLIB_BASE64 = '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'

# Immutable legacy import/v1 constructor seed; independent of the current public enrollment path.
LEGACY_IMPORT_BLOB = '24857656881499d450c25b5c5fe93ffd07156517'
LEGACY_IMPORT_SHA256 = '253cb33a83d85a487f112b65dc51a2388996df86833ebee9c26a2159d83a0bd6'
LEGACY_IMPORT_ZLIB_BASE64 = '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'

class ManifestTests(unittest.TestCase):
    def setUp(self):
        original = zlib.decompress(base64.b64decode(LEGACY_IMPORT_ZLIB_BASE64))
        self.assertEqual(LEGACY_IMPORT_SHA256, hashlib.sha256(original).hexdigest())
        self.manifest = json.loads(original)

    def call(self, manifest, plan=False):
        with tempfile.TemporaryDirectory() as directory:
            path = pathlib.Path(directory) / 'manifest.json'
            path.write_text(json.dumps(manifest))
            return subprocess.run(['dotnet', str(DLL), str(path), *(['--pilot-plan'] if plan else [])], capture_output=True, text=True)

    def test_actual_current_public_path_remains_valid_without_becoming_a_legacy_seed(self):
        path = ROOT / 'docs/coordination/board-v2-import-manifest.json'
        document = json.loads(path.read_text())
        result = subprocess.run(['dotnet',str(DLL),str(path)],capture_output=True,text=True)
        self.assertEqual(0,result.returncode,result.stderr)
        summary = json.loads(result.stdout)
        self.assertFalse(summary['executable'])
        if document['schema'] == 'fsgg.coordination-board-v2-enrollment/v3':
            self.assertEqual({'publicCommitmentValid':True,'authenticated':False,'executable':False},summary)
        else:
            self.assertEqual('fsgg.coordination-board-v2-import/v1',document['schema'])
            self.assertEqual(len(document['items']),summary['candidateCount'])

    def test_public_commitment_validator_is_closed_offline_and_wrapper_reachable(self):
        valid = '{"schema":"fsgg.coordination-board-v2-enrollment/v3","commitmentSha256":"' + 'a'*64 + '"}'
        invalid = [
            valid.replace('/v3', '/v99'),
            '{"schema":"fsgg.coordination-board-v2-enrollment/v99","SYNTHETIC_PRIVATE_SENTINEL":null}',
            valid[:-1] + ',"SYNTHETIC_PRIVATE_SENTINEL":null,"SYNTHETIC_PRIVATE_SENTINEL":null}',
            '{"schema":{"SYNTHETIC_PRIVATE_SENTINEL":null}}',
            '{"schema":["SYNTHETIC_PRIVATE_SENTINEL"]}',
            '{"schema":null,"SYNTHETIC_PRIVATE_SENTINEL":null}',
            '{"SYNTHETIC_PRIVATE_SENTINEL":null}',
            '{"SYNTHETIC_PRIVATE_SENTINEL":',

            '{"schema":"fsgg.coordination-board-v2-enrollment/v3"}',
            valid[:-1] + ',"commitmentSha256":"' + 'a'*64 + '"}',
            valid.replace('a'*64, 'A'*64),
            valid.replace('a'*64, 'a'*63),
            valid.replace('a'*64, 'g'*64),
            valid[:-1] + ',"programmeEnrollment":{"nonceHex":"PRIVATE_SENTINEL"}}',
            valid[:-1] + ',"items":[{"issue":"PRIVATE_SENTINEL"}]}',
            valid + ' '*1024,
            valid + ' '*(256*1024),
        ]
        with tempfile.TemporaryDirectory() as directory:
            root = pathlib.Path(directory)
            # Exercise the unchanged Python wrapper and its exact dotnet-run argument contract,
            # forwarding to the genuinely built constructor instead of rebuilding on each case.
            shim = root / 'dotnet'
            shim.write_text('#!' + sys.executable + '\n'
                'import os,sys\n'
                'expected=["run","--project",' + repr(str(PROJECT)) + ',"--no-launch-profile","--"]\n'
                'assert sys.argv[1:6]==expected,sys.argv\n'
                'os.execv(' + repr(subprocess.check_output(['which','dotnet'],text=True).strip()) + ',["dotnet",' + repr(str(DLL)) + ',*sys.argv[6:]])\n')
            shim.chmod(0o700)
            env = dict(os.environ, PATH=str(root) + os.pathsep + os.environ['PATH'])
            path = root / 'public.json'
            for raw, expected in [(valid,0), *[(row,2) for row in invalid]]:
                path.write_text(raw)
                direct = subprocess.run(['dotnet',str(DLL),str(path)],capture_output=True,text=True)
                wrapped = subprocess.run([sys.executable,str(ROOT/'tools/board-v2-import.py'),str(path)],env=env,capture_output=True,text=True)
                for result in [direct, wrapped]:
                    self.assertEqual(expected,result.returncode,result.stderr)
                    self.assertNotIn('PRIVATE_SENTINEL',result.stdout+result.stderr)
                    if expected == 0:
                        self.assertEqual({'publicCommitmentValid':True,'authenticated':False,'executable':False},json.loads(result.stdout))
            path.write_text(valid)
            plan = subprocess.run(['dotnet',str(DLL),str(path),'--pilot-plan'],capture_output=True,text=True)
            self.assertEqual(2,plan.returncode)
            self.assertEqual('board-v2 public document refused',plan.stderr.strip())

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

class ExistingProductTests(unittest.TestCase):
    """Synthetic files exercise the same compiled qualifier, not native authentication."""
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = pathlib.Path(self.temp.name)
        self.artifact = hashlib.sha256(DLL.read_bytes()).hexdigest()
        self.assertIsNotNone(SOURCE_REVISION, 'qualification must bind compiled SourceRevisionId')

    def retain(self, name, value):
        path = self.directory / name
        data = json.dumps(value, separators=(',', ':')).encode()
        path.write_bytes(data)
        return {'path': str(path), 'sha256': hashlib.sha256(data).hexdigest()}

    def fixture(self, game=False):
        key = 'game' if game else 'rendering'
        repo = 'FS-GG/FS.GG.Game' if game else 'FS-GG/FS.GG.Rendering'
        number, issue_number = (4, 7) if game else (5, 1274)
        node = 'I_fixture_' + key
        repo_node = {'id': 'R_fixture_'+key, 'nameWithOwner': repo}
        principal = {'id': 'USER_fixture', 'login': 'fixture-reader'}
        project = 'PVT_fixture_' + key
        title = repo.split('/')[1] + ' — Product V2 qualification'
        url = f'https://github.com/{repo}/issues/{issue_number}'
        schema = {}
        option_names = {'Status':['Backlog','Ready','In progress','Blocked','Done'],
                        'Track':['Active delivery','Follow-up'],
                        'Observation':['Verified','Stale','Unknown']}
        for name in ['Status','Roadmap','Track','Observation']:
            field = {'__typename':'ProjectV2Field' if name=='Roadmap' else 'ProjectV2SingleSelectField',
                     'dataType':'TEXT' if name=='Roadmap' else 'SINGLE_SELECT',
                     'id':'FIELD_'+key+'_'+name,'name':name}
            if name != 'Roadmap':
                field['options'] = [{'id':key+'_'+name+'_'+str(i),'name':v} for i,v in enumerate(option_names[name])]
            schema[name] = field
        chosen = {'Status':'Backlog' if game else 'Blocked', 'Track':'Follow-up' if game else 'Active delivery', 'Observation':'Unknown', 'Roadmap':url}
        values = {}
        for name, field in schema.items():
            value = {'__typename':'ProjectV2ItemFieldTextValue' if name=='Roadmap' else 'ProjectV2ItemFieldSingleSelectValue', 'field':copy.deepcopy(field), 'id':'VALUE_'+name}
            if name=='Roadmap': value['text']=chosen[name]
            else: value.update(name=chosen[name],optionId=next(o['id'] for o in field['options'] if o['name']==chosen[name]))
            values[name]=value
        member = {'id':'PVTI_fixture_'+key,'isArchived':False,'type':'ISSUE',
                  'content':{'__typename':'Issue','id':node,'number':issue_number,'repository':repo_node}}
        observation = {'target':{'expectedTitle':title,'issueId':node,'issueNumber':issue_number,'number':number,'projectId':project,'repository':repo},
                       'identityAfter':{'closed':False,'id':project,'number':number,'public':False,'title':title,'url':f'https://github.com/orgs/FS-GG/projects/{number}'},
                       'schema':schema,'membership':{'complete':True,'issueIdentityCoverage':True,'items':[member],'opaqueItemIds':[],'pages':1,'selectedAbsence':'not-absent','selectedMemberships':[copy.deepcopy(member)],'totalCount':1},
                       'values':{'allPayloadPreservation':False,'otherValueTypes':[],'selected':values},'findings':[],'notRun':[]}
        metadata = {'additionalFindings':[],'atomicSnapshot':False,'calls':5,'credentialPrincipal':principal,'firstCause':None,'projects':[observation],'scope':'synthetic fixture only'}
        text = 'Synthetic remaining outcome with preserved SDD924 textual blocker' if not game else 'Synthetic game remaining outcome'
        body = text.encode()
        issue = {'id':node,'number':issue_number,'url':url,'state':'OPEN','updatedAt':'2026-10-10T10:00:00Z'}
        raw_issue = dict(issue, __typename='Issue', body=text, repository=repo_node)
        raw_graph = {'data':{'viewer':principal,'organization':{'id':'O_kgDOEYAWYw','login':'FS-GG'},'repository':dict(repo_node,issue=raw_issue)}}
        requests = []
        for step,(index,phase) in enumerate(zip([2,4,6] if game else [1,3,5],['issue-before','blocked-by','issue-after'])):
            raw = self.retain(f'{key}-{phase}.raw.json', [] if phase=='blocked-by' else raw_graph)
            raw['bytes'] = pathlib.Path(raw['path']).stat().st_size
            method = 'GET' if phase=='blocked-by' else 'POST'
            endpoint = f'https://api.github.com/repos/{repo}/issues/{issue_number}/dependencies/blocked_by?per_page=50' if phase=='blocked-by' else 'https://api.github.com/graphql'
            start, end = f'2026-10-10T10:00:{step*2:02d}Z', f'2026-10-10T10:00:{step*2+1:02d}Z'
            status = {'index':index,'targetKey':key,'phase':phase,'method':method,'url':endpoint,'startedAt':start,'finishedAt':end,'status':200,'retainedBytes':raw['bytes'],'sha256':raw['sha256'],'bodyRetained':True,'bodyComplete':True,'truncated':False,'transportFailure':None,'additionalFailures':[],
                      'headers':dict.fromkeys(['link','contentLength','contentType','requestId','rateLimitRemaining'])}
            ref = self.retain(f'{key}-{phase}.status.json',status)
            ref['bytes'] = pathlib.Path(ref['path']).stat().st_size
            requests.append({'index':index,'phase':phase,'method':method,'url':endpoint,'startedAt':start,'finishedAt':end,'status':200,'raw':raw,'metadata':ref})
        currentness = {'schema':'private.board-dot6.source-currentness/1','targetKey':key,'principal':principal,'organization':{'id':'O_kgDOEYAWYw','login':'FS-GG'},'repository':repo_node,'issue':issue,
                       'body':{'sha256':hashlib.sha256(body).hexdigest(),'utf8Bytes':len(body)},
                       'owningPlan':{'kind':'same-native-issue','url':url,'issueId':node,'updatedAt':issue['updatedAt'],'bodySha256':hashlib.sha256(body).hexdigest(),'nonempty':True},
                       'dependencies':{'complete':True,'items':[],'observedAt':requests[1]['finishedAt']},'revisionStable':True,'requests':requests,'atomicSnapshot':False,'mutations':0,'historicalConstructorAuthenticated':False}
        prior = {'schema':'fsgg.coordination-board-v2-import/v1','target':{'ownerKind':'organization','owner':'FS-GG','title':title,'id':project,'number':number,'creationState':'created-and-read-back'},
                 'binding':{'recipeRevision':'2ab0c0ff9f37bdec9a11ba3604ebdc230711934f','artifactSha256':'cb318977a143085680b27ba4c86fffd6fb3f5a91dc6a7e07cf79ef7c67825615'},'items':[]}
        adjudication = {'schema':'private.board-dot6.source-adjudication/1','targetKey':key,'issueId':node,'currentnessSha256':'','authorization':'root-selected','adjudication':'verified-remaining','decision':'follow-up' if game else 'import','roadmap':url,'remainingOutcome':'Synthetic remaining outcome; no native acceptance',
                        'textualBlockerDisposition':{'status':'none-observed' if game else 'preserved','evidenceBodySha256':currentness['body']['sha256'],'statement':text}}
        request = {'schema':'fsgg.board-v2-existing-product-request/1',
                   'selection':{'authorization':'root-selected','organizationId':'O_kgDOEYAWYw','ownerKind':'organization','owner':'FS-GG','repository':repo,'projectId':project,'projectNumber':number,'projectTitle':title,'principal':principal,'selectedIssues':{node:f'{repo}#{issue_number}'}},
                   'qualifier':{'recipeRevision':SOURCE_REVISION,'artifactSha256':self.artifact},'adapter':{'recipeRevision':'c'*40,'artifactSha256':'d'*64},'populationRevision':None}
        return request, metadata, currentness, adjudication, prior

    def call_product(self, fixture, raw_request=None):
        request,metadata,currentness,adjudication,prior=fixture
        request=copy.deepcopy(request);adjudication=copy.deepcopy(adjudication)
        request['metadata']=self.retain('metadata.json',metadata)
        request['currentness']=self.retain('currentness.json',currentness)
        adjudication['currentnessSha256']=request['currentness']['sha256']
        request['adjudication']=self.retain('adjudication.json',adjudication)
        request['priorPopulation']=self.retain('prior.json',prior)
        path=self.directory/'request.json'
        path.write_text(json.dumps(request) if raw_request is None else raw_request(request))
        return subprocess.run(['dotnet',str(DLL),str(path),'--existing-product'],capture_output=True,text=True)

    def refuse(self, fixture, message):
        result=self.call_product(fixture)
        self.assertEqual(2,result.returncode,result.stdout)
        self.assertIn(message,result.stderr)

    def test_two_product_drafts_are_deterministic_and_never_mutate(self):
        for game in [False,True]:
            f=self.fixture(game);r=self.call_product(f)
            self.assertEqual(0,r.returncode,r.stderr)
            self.assertEqual(r.stdout,self.call_product(f).stdout)
            out=json.loads(r.stdout)
            self.assertFalse(out['executable']);self.assertIsNone(out['binding']['populationRevision'])
            self.assertEqual([],out['nativeMutationIntents'])
            for k in ['projectCreationPerformed','membershipMutationPerformed','historicalConstructorAuthenticated']:self.assertFalse(out['provenance'][k])
            self.assertEqual(f[0]['adapter']['artifactSha256'],out['binding']['artifactSha256'])
            self.assertEqual(self.artifact,out['binding']['importArtifactSha256'])
            self.assertEqual(f[4]['binding']['artifactSha256'],'cb318977a143085680b27ba4c86fffd6fb3f5a91dc6a7e07cf79ef7c67825615')
            item=out['population']['items'][0]
            self.assertEqual(('Backlog','Follow-up','Unknown') if game else ('Blocked','Active delivery','Unknown'),tuple(item[k] for k in ['status','track','observation']))
            self.assertIn('prospectiveQualification',out['population']['inventory'])
            self.assertNotIn(str(self.directory),json.dumps(out['population']))
            self.assertNotIn('path',out['population']['inventory']['prospectiveQualification'])
            prior=out['population']['inventory']['prospectiveQualification']['priorImportProvenance']
            for k in ['recipeRevision','artifactSha256']:self.assertEqual(f[4]['binding'][k],prior[k])
            self.assertFalse(prior['historicalConstructorAuthenticated'])

    def test_population_commit_is_separate_explicit_axis(self):
        f=self.fixture();f[0]['populationRevision']='e'*40
        r=self.call_product(f);self.assertEqual(0,r.returncode,r.stderr)
        self.assertEqual('e'*40,json.loads(r.stdout)['binding']['populationRevision'])
        for value in ['fake',SOURCE_REVISION,'c'*40]:
            f=self.fixture();f[0]['populationRevision']=value;self.refuse(f,'population')

    def test_unknown_duplicate_and_digest_modified_inputs_refuse(self):
        f=self.fixture();f[0]['unexpected']=True;self.refuse(f,'unexpected property')
        f=self.fixture();r=self.call_product(f,lambda request:json.dumps(request)[:-1]+',"schema":"duplicate"}')
        self.assertEqual(2,r.returncode);self.assertIn('duplicate property',r.stderr)
        f=self.fixture();r=self.call_product(f,lambda request:json.dumps(dict(request,metadata=dict(request['metadata'],sha256='0'*64))))
        self.assertEqual(2,r.returncode);self.assertIn('evidence digest differs',r.stderr)

    def test_actual_assembly_and_historical_constructor_substitution_refuse(self):
        for key,value in [('artifactSha256','cb318977a143085680b27ba4c86fffd6fb3f5a91dc6a7e07cf79ef7c67825615'),('recipeRevision','2ab0c0ff9f37bdec9a11ba3604ebdc230711934f')]:
            f=self.fixture();f[0]['qualifier'][key]=value;self.refuse(f,'qualifier')
        f=self.fixture();f[0]['adapter']=copy.deepcopy(f[0]['qualifier']);self.refuse(f,'axes')

    def test_scope_and_bound_refusals(self):
        for key,value in [('authorization','unknown'),('ownerKind','user'),('repository','Foreign/Repo'),('projectNumber',1),('projectNumber',3),('projectId','PVT_kwDOEYAWY84Bb08W'),('projectId','PVT_kwDOEYAWY84Bldpa')]:
            f=self.fixture();f[0]['selection'][key]=value;self.refuse(f,'refused' if key.startswith('project') else ('differs' if key in ['authorization','ownerKind'] else 'repository'))
        for count in [0,6]:
            f=self.fixture();f[0]['selection']['selectedIssues']={f'I_{i}':f'FS-GG/FS.GG.Rendering#{i+1}' for i in range(count)};self.refuse(f,'one to five')

    def test_principal_project_and_member_joins_refuse(self):
        f=self.fixture();f[1]['credentialPrincipal']['login']='other';self.refuse(f,'differs')
        f=self.fixture();f[1]['projects'][0]['identityAfter']['id']='PVT_wrong';self.refuse(f,'differs')
        for key,value in [('complete',False),('issueIdentityCoverage',False),('pages',0),('totalCount',2)]:
            f=self.fixture();f[1]['projects'][0]['membership'][key]=value;self.refuse(f,'membership')
        f=self.fixture();f[1]['projects'][0]['membership']['items'][0]['isArchived']=True;self.refuse(f,'archived')
        f=self.fixture();f[1]['projects'][0]['membership']['items'].append(copy.deepcopy(f[1]['projects'][0]['membership']['items'][0]));f[1]['projects'][0]['membership']['totalCount']=2;self.refuse(f,'duplicate membership')

    def test_field_and_option_identity_joins_refuse(self):
        for mutate in [lambda row:row['schema']['Status'].update(id=row['schema']['Roadmap']['id']),lambda row:row['schema']['Status']['options'][0].update(name='Claimed'),lambda row:row['values']['selected']['Status'].update(optionId='wrong'),lambda row:row['values']['selected']['Status']['field'].update(id='wrong')]:
            f=self.fixture();mutate(f[1]['projects'][0]);self.refuse(f,'field' if f[1]['projects'][0]['schema']['Status']['id']==f[1]['projects'][0]['schema']['Roadmap']['id'] else 'differ')

    def test_source_closed_stale_unknown_or_nonempty_dependencies_refuse(self):
        for mutate,message in [(lambda c:c['issue'].update(state='CLOSED'),'state differs'),(lambda c:c.update(revisionStable=False),'source revision'),(lambda c:c['dependencies'].update(complete=False),'empty dependencies'),(lambda c:c['dependencies'].update(items=[{'id':'I_dependency'}]),'empty dependencies'),(lambda c:c['owningPlan'].update(nonempty=False),'owning plan empty'),(lambda c:c['issue'].update(updatedAt='bad'),'invalid timestamp')]:
            f=self.fixture();mutate(f[2]);self.refuse(f,message)

    def test_raw_source_and_status_completeness_are_recomputed(self):
        for mutate,message in [(lambda c:c['body'].update(sha256='0'*64),'bodySha256 differs'),(lambda c:c['requests'][0]['raw'].update(sha256='0'*64),'source raw reference differs'),(lambda c:c['requests'][0].update(status=403),'slot/status differs'),(lambda c:c['requests'][2].update(startedAt='2026-10-10T09:00:00Z'),'request ordering differs')]:
            f=self.fixture();mutate(f[2]);self.refuse(f,message)
        for key,value in [('bodyComplete',False),('truncated',True),('transportFailure','Unknown')]:
            f=self.fixture();ref=f[2]['requests'][1]['metadata'];path=pathlib.Path(ref['path']);status=json.loads(path.read_text());status[key]=value;fresh=self.retain(path.name,status);fresh['bytes']=path.stat().st_size;f[2]['requests'][1]['metadata']=fresh;self.refuse(f,'coverage incomplete')
        f=self.fixture();ref=f[2]['requests'][1]['metadata'];path=pathlib.Path(ref['path']);status=json.loads(path.read_text());status['headers']['link']='next';fresh=self.retain(path.name,status);fresh['bytes']=path.stat().st_size;f[2]['requests'][1]['metadata']=fresh;self.refuse(f,'paginated')

    def test_root_adjudication_and_textual_blocker_never_inferred(self):
        for key,value,message in [('authorization','unknown','authorization differs'),('remainingOutcome','','remainingOutcome is required'),('decision','follow-up','decision differs')]:
            f=self.fixture();f[3][key]=value;self.refuse(f,message)
        f=self.fixture();f[3]['textualBlockerDisposition']['status']='none-observed';self.refuse(f,'differs')
        f=self.fixture();f[3]['textualBlockerDisposition']['evidenceBodySha256']='0'*64;self.refuse(f,'differs')

    def test_input_size_links_and_missing_nested_properties_refuse(self):
        f=self.fixture();r=self.call_product(f,lambda request:' '*1048577)
        self.assertEqual(2,r.returncode);self.assertIn('exceeds 1 MiB',r.stderr)
        f=self.fixture();del f[2]['atomicSnapshot'];self.refuse(f,'missing property')
        f=self.fixture();f[2]['body']['unexpected']=1;self.refuse(f,'unexpected property')
        f=self.fixture();ref=f[2]['requests'][0]['raw'];original=pathlib.Path(ref['path']);link=self.directory/'linked.json';link.symlink_to(original);ref['path']=str(link);self.refuse(f,'linked input path')

    def test_foreign_member_opaque_population_and_missing_selected_coverage_refuse(self):
        f=self.fixture();f[1]['projects'][0]['membership']['items'][0]['content']['repository']['nameWithOwner']='FS-GG/Other';self.refuse(f,'nameWithOwner differs')
        f=self.fixture();f[1]['projects'][0]['membership']['items'][0]['content']['id']='I_wrong';self.refuse(f,'absent or duplicated')
        f=self.fixture();f[1]['projects'][0]['membership']['opaqueItemIds']=['PVTI_opaque'];self.refuse(f,'opaque')
        f=self.fixture();f[1]['projects'][0]['membership']['selectedMemberships']=[];self.refuse(f,'summary differs')
        f=self.fixture();del f[1]['projects'][0]['values']['selected']['Roadmap'];self.refuse(f,'missing property')

    def test_original_raw_revision_and_body_are_not_replaced_by_summary(self):
        for key,value in [('updatedAt','2026-10-10T10:01:00Z'),('body','changed body')]:
            f=self.fixture();row=f[2]['requests'][2];path=pathlib.Path(row['raw']['path']);raw=json.loads(path.read_text());raw['data']['repository']['issue'][key]=value
            fresh=self.retain(path.name,raw);fresh['bytes']=path.stat().st_size;row['raw']=fresh
            path=pathlib.Path(row['metadata']['path']);status=json.loads(path.read_text());status.update(sha256=fresh['sha256'],retainedBytes=fresh['bytes']);ref=self.retain(path.name,status);ref['bytes']=path.stat().st_size;row['metadata']=ref
            self.refuse(f,'updatedAt differs' if key=='updatedAt' else 'raw body/owning-plan identity differs')

    def test_option_permutation_and_utf8_header_byte_boundary(self):
        f=self.fixture();row=f[1]['projects'][0];row['schema']['Status']['options'].reverse();row['values']['selected']['Status']['field']['options'].reverse()
        r=self.call_product(f);self.assertEqual(0,r.returncode,r.stderr)
        for text,expected in [('x'*4096,0),('é'*2049,2)]:
            f=self.fixture();ref=f[2]['requests'][0]['metadata'];path=pathlib.Path(ref['path']);status=json.loads(path.read_text());status['headers']['requestId']=text;fresh=self.retain(path.name,status);fresh['bytes']=path.stat().st_size;f[2]['requests'][0]['metadata']=fresh
            r=self.call_product(f);self.assertEqual(expected,r.returncode,r.stderr)
            if expected:self.assertIn('header exceeds bound',r.stderr)

class ProgrammePreparationTests(unittest.TestCase):
    """Actual offline CLI, synthetic enrollment only; hashlib is an independent wire oracle."""
    DOMAIN = b'fsgg.coordination-board-v2-enrollment/v3\x00'
    SENTINEL = 'SYNTHETIC_PRIVATE_SENTINEL_9f310'

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = pathlib.Path(self.temp.name)
        self.template_path = self.directory / 'template.json'
        self.payload_path = self.directory / 'payload.json'
        self.private_path = self.directory / 'private.json'
        self.public_path = self.directory / 'public.json'
        self.template, self.payload = self.fixture()

    def fixture(self):
        rows = [{'issue': f'FS-GG/.github#{70001+i}', 'nodeId': f'I_synthetic_{i}',
                 'roadmap': f'docs/coordination/{self.SENTINEL}-{i}.md'} for i in range(20)]
        def field(name, options):
            return {'id': 'synthetic-' + name,
                    'options': {value: f'synthetic-{name}-{i}' for i, value in enumerate(options)}}
        template = {
            'bindingVersion': 4, 'schemaVersion': 2,
            'importRecipeRevision': 'a'*40, 'importArtifactSha256': 'b'*64,
            'selectedIssues': {row['nodeId']: row['issue'] for row in rows},
            'recipeRevision': 'c'*40, 'populationRevision': 'e'*40,
            'organizationId': 'O_kgDOEYAWYw', 'artifactSha256': 'd'*64,
            'ownerKind': 'organization', 'owner': 'FS-GG', 'projectNumber': 3,
            'projectTitle': 'Coordination V2', 'projectId': 'PVT_kwDOEYAWY84Bldpa',
            'status': field('status', ['Backlog', 'Ready', 'In progress', 'In review', 'Blocked', 'Done']),
            'roadmapFieldId': 'synthetic-roadmap',
            'track': field('track', ['Active delivery', 'Follow-up']),
            'observation': field('observation', ['Verified', 'Stale', 'Unknown']),
            'repositories': ['FS-GG/.github']}
        payload = {
            'schema': 'fsgg.coordination-board-v2-enrollment-payload/v1',
            'target': {'owner': 'FS-GG', 'organizationId': template['organizationId'],
                       'projectId': template['projectId'], 'projectNumber': 3, 'schemaVersion': 2},
            'binding': {'recipeRevision': template['importRecipeRevision'],
                        'artifactSha256': template['importArtifactSha256'],
                        'repositories': template['repositories']},
            'items': rows}
        return template, payload

    @staticmethod
    def wire(value):
        return json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')

    def call(self, template=None, payload=None, *, raw_template=None, raw_payload=None, arguments=None):
        self.template_path.write_bytes(raw_template if raw_template is not None else self.wire(self.template if template is None else template))
        self.payload_path.write_bytes(raw_payload if raw_payload is not None else self.wire(self.payload if payload is None else payload))
        args = ['prepare-programme', '--binding-template', str(self.template_path), '--payload', str(self.payload_path),
                '--private-output', str(self.private_path), '--public-output', str(self.public_path)]
        return subprocess.run(['dotnet', str(DLL), *(args if arguments is None else arguments)],
                              capture_output=True, text=True, timeout=30)

    def refused(self, result):
        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        self.assertEqual('', result.stdout)
        self.assertEqual('board-v2 programme preparation refused\n', result.stderr)
        self.assertNotIn(self.SENTINEL, result.stdout + result.stderr)

    def assert_pair(self, raw_payload):
        private_bytes, public_bytes = self.private_path.read_bytes(), self.public_path.read_bytes()
        private, public = json.loads(private_bytes), json.loads(public_bytes)
        self.assertEqual(set(self.template) | {'programmeEnrollment'}, set(private))
        self.assertEqual({'nonceHex', 'payloadBase64'}, set(private['programmeEnrollment']))
        for key in self.template:
            self.assertEqual(self.template[key], private[key])
        envelope = private['programmeEnrollment']
        self.assertRegex(envelope['nonceHex'], r'^[0-9a-f]{64}$')
        nonce = bytes.fromhex(envelope['nonceHex'])
        self.assertNotEqual(bytes(32), nonce)
        self.assertEqual(raw_payload, base64.b64decode(envelope['payloadBase64'], validate=True))
        self.assertEqual(base64.b64encode(raw_payload).decode(), envelope['payloadBase64'])
        self.assertEqual({'schema', 'commitmentSha256'}, set(public))
        self.assertEqual('fsgg.coordination-board-v2-enrollment/v3', public['schema'])
        expected = hashlib.sha256(self.DOMAIN + nonce + len(raw_payload).to_bytes(8, 'big') + raw_payload).hexdigest()
        self.assertEqual(expected, public['commitmentSha256'])
        self.assertNotEqual(hashlib.sha256(self.DOMAIN[:-1] + b'\\0' + nonce + len(raw_payload).to_bytes(8, 'big') + raw_payload).hexdigest(), expected)
        for secret in [self.SENTINEL, envelope['nonceHex'], envelope['payloadBase64'], 'I_synthetic', '70001']:
            self.assertNotIn(secret, public_bytes.decode())
        if os.name != 'nt':
            self.assertEqual(0o600, self.private_path.stat().st_mode & 0o777)
            self.assertEqual(0o600, self.public_path.stat().st_mode & 0o777)
        return envelope

    def test_actual_twenty_row_pair_matches_independent_exact_byte_hash(self):
        raw = self.wire(self.payload) + b' \n'
        result = self.call(raw_payload=raw)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('programme enrollment pair prepared; offline only\n', result.stdout)
        self.assertEqual('', result.stderr)
        envelope = self.assert_pair(raw)
        self.assertNotIn(envelope['nonceHex'], result.stdout + result.stderr)
        self.assertNotIn(self.SENTINEL, result.stdout + result.stderr)
        # A separately prepared pair gets a new production RNG nonce; no deterministic/default salt.
        other = self.directory / 'other'
        other.mkdir()
        self.private_path, self.public_path = other/'private.json', other/'public.json'
        result = self.call(raw_payload=raw)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertNotEqual(envelope['nonceHex'], self.assert_pair(raw)['nonceHex'])

    def test_exact_input_byte_bounds_and_one_beyond(self):
        for kind, limit in [('template', 256*1024), ('payload', 128*1024)]:
            with self.subTest(kind=kind):
                original = self.wire(self.template if kind == 'template' else self.payload)
                raw = original + b' '*(limit-len(original))
                result = self.call(**{'raw_'+kind: raw})
                self.assertEqual(0, result.returncode, result.stderr)
                self.assert_pair(raw if kind == 'payload' else self.wire(self.payload))
                self.private_path.unlink(); self.public_path.unlink()
                self.refused(self.call(**{'raw_'+kind: raw+b' '}))
                self.assertFalse(self.private_path.exists()); self.assertFalse(self.public_path.exists())

    def test_empty_invalid_utf8_bom_and_invalid_json_never_write_or_echo(self):
        for kind in ['template', 'payload']:
            for raw in [b'', b'\xff'+self.SENTINEL.encode(), b'\xef\xbb\xbf{}', b'{'+self.SENTINEL.encode()]:
                with self.subTest(kind=kind, raw=raw[:4]):
                    self.refused(self.call(**{'raw_'+kind: raw}))
                    self.assertFalse(self.private_path.exists()); self.assertFalse(self.public_path.exists())

    def test_duplicate_nested_unknown_and_depth_inputs_refuse(self):
        cases = []
        raw = self.wire(self.payload)
        cases.append(raw[:-1] + b',"schema":"duplicate"}')
        cases.append(raw.replace(b'"projectNumber":3', b'"projectNumber":3,"projectNumber":3'))
        cases.append(raw.replace(b'"nodeId":"I_synthetic_0"', b'"nodeId":"I_synthetic_0","nodeId":"I_synthetic_0"'))
        extra = copy.deepcopy(self.payload); extra['items'][0]['unexpected'] = self.SENTINEL
        cases.append(self.wire(extra))
        extra = copy.deepcopy(self.payload); extra['unexpected'] = [[[[[[[[[[[[[[[[[self.SENTINEL]]]]]]]]]]]]]]]]]
        cases.append(self.wire(extra))
        for raw in cases:
            with self.subTest(raw=raw[:20]):
                self.refused(self.call(raw_payload=raw))
                self.assertFalse(self.private_path.exists()); self.assertFalse(self.public_path.exists())
        raw = self.wire(self.template)
        self.refused(self.call(raw_template=raw[:-1] + b',"bindingVersion":4}'))

    def test_template_selection_and_closed_payload_joins_refuse(self):
        for mutate in [lambda p: p['target'].update(projectNumber=1),
                       lambda p: p['binding'].update(recipeRevision='f'*40),
                       lambda p: p['binding'].update(artifactSha256='f'*64),
                       lambda p: p['binding'].update(repositories=['FS-GG/Other']),
                       lambda p: p['items'].pop(),
                       lambda p: p['items'].append(copy.deepcopy(p['items'][0])),
                       lambda p: p['items'][0].update(nodeId='I_foreign'),
                       lambda p: p['items'][0].update(issue='FS-GG/Other#70001'),
                       lambda p: p.update(schema='fsgg.coordination-board-v2-enrollment/v2')]:
            payload = copy.deepcopy(self.payload); mutate(payload)
            self.refused(self.call(payload=payload))
            self.assertFalse(self.private_path.exists()); self.assertFalse(self.public_path.exists())
        for mutate in [lambda t: t.update(bindingVersion=3),
                       lambda t: t.update(programmeEnrollment={'nonceHex':'0'*64, 'payloadBase64':'e30='}),
                       lambda t: t.update(unexpected=self.SENTINEL)]:
            template = copy.deepcopy(self.template); mutate(template)
            self.refused(self.call(template=template))
            self.assertFalse(self.private_path.exists()); self.assertFalse(self.public_path.exists())

    def test_existing_either_output_preserved_and_pair_not_reseeded(self):
        result = self.call()
        self.assertEqual(0, result.returncode, result.stderr)
        originals = (self.private_path.read_bytes(), self.public_path.read_bytes())
        self.refused(self.call())
        self.assertEqual(originals, (self.private_path.read_bytes(), self.public_path.read_bytes()))
        for retained in ['private', 'public']:
            with self.subTest(retained=retained):
                directory = self.directory / retained; directory.mkdir()
                self.private_path, self.public_path = directory/'private.json', directory/'public.json'
                path = self.private_path if retained == 'private' else self.public_path
                path.write_bytes(self.SENTINEL.encode())
                self.refused(self.call())
                self.assertEqual(self.SENTINEL.encode(), path.read_bytes())
                self.assertFalse((self.public_path if retained == 'private' else self.private_path).exists())

    def test_partial_second_output_failure_preserves_private_and_never_retries(self):
        self.public_path = self.directory / self.SENTINEL / 'absent-parent' / 'public.json'
        self.refused(self.call())
        original = self.private_path.read_bytes()
        envelope = json.loads(original)['programmeEnrollment']
        result = self.call()
        self.assertNotIn(envelope['nonceHex'], result.stderr)
        self.refused(result)
        self.assertEqual(original, self.private_path.read_bytes())
        self.assertFalse(self.public_path.exists())

    def test_disjoint_normalized_paths_arguments_and_link_custody_refuse(self):
        for pair in [('private', self.template_path), ('public', self.payload_path), ('public', self.private_path)]:
            original_private, original_public = self.private_path, self.public_path
            if pair[0] == 'private': self.private_path = pair[1]
            else: self.public_path = pair[1]
            self.refused(self.call())
            self.assertEqual(self.wire(self.template), self.template_path.read_bytes())
            self.assertEqual(self.wire(self.payload), self.payload_path.read_bytes())
            self.private_path, self.public_path = original_private, original_public
        self.refused(self.call(arguments=['prepare-programme', '--nonce', self.SENTINEL]))
        self.refused(self.call(arguments=['prepare-programme']))
        if os.name != 'nt':
            real = self.directory/'real.json'; real.write_bytes(self.wire(self.payload))
            self.payload_path.unlink(); self.payload_path.symlink_to(real)
            # call() intentionally writes through this synthetic link; CLI itself must reject it.
            self.refused(self.call())
            self.assertFalse(self.private_path.exists()); self.assertFalse(self.public_path.exists())


class OutcomeDeliveryProgrammePreparationTests(ProgrammePreparationTests):
    """The same actual offline constructor handles the selected new binding, not a second writer."""
    def setUp(self):
        super().setUp()
        self.template['bindingVersion'] = 5
        self.template['schemaVersion'] = 3
        self.template['workLevel'] = {'id': 'synthetic-work-level',
            'options': {'Outcome': 'synthetic-level-outcome', 'Delivery': 'synthetic-level-delivery'}}
        self.payload['target']['schemaVersion'] = 3

    def test_actual_successor_pair_keeps_enrollment_wire_and_exact_field_identities(self):
        raw = self.wire(self.payload) + b' \n'
        result = self.call(raw_payload=raw)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assert_pair(raw)
        private = json.loads(self.private_path.read_bytes())
        self.assertEqual(5, private['bindingVersion'])
        self.assertEqual(3, private['schemaVersion'])
        self.assertEqual(self.template['workLevel'], private['workLevel'])

    def test_wrong_successor_target_schema_refuses_without_output(self):
        self.payload['target']['schemaVersion'] = 2
        self.refused(self.call())
        self.assertFalse(self.private_path.exists())
        self.assertFalse(self.public_path.exists())

    def test_legacy_binding_cannot_acquire_new_field_by_extra_property(self):
        self.template['bindingVersion'] = 4
        self.template['schemaVersion'] = 2
        self.payload['target']['schemaVersion'] = 2
        self.refused(self.call())
        self.assertFalse(self.private_path.exists())
        self.assertFalse(self.public_path.exists())

class SuccessorEnrollmentTests(unittest.TestCase):
    """Actual offline batched-successor subcommands. Synthetic identities only; every private value is a sentinel."""
    DOMAIN = b'fsgg.coordination-board-v2-enrollment/v3\x00'
    SENTINEL = 'SYNTHETIC_PRIVATE_SENTINEL_9f310'
    PLAN_PATH = 'docs/roadmaps/synthetic-plan-' + SENTINEL + '.md'
    PROJECT = 'PVT_kwDOEYAWY84Bldpa'
    MANIFEST = 'docs/coordination/board-v2-import-manifest.json'
    GIT = ['git', '-c', 'user.name=synthetic', '-c', 'user.email=synthetic@example.invalid', '-c', 'commit.gpgsign=false']

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.dir = pathlib.Path(self.temp.name)
        self.fixture = self.make_predecessor('base', 5)

    @staticmethod
    def wire(value):
        return json.dumps(value, ensure_ascii=False, separators=(',', ':')).encode('utf-8')

    def git(self, repository, *args):
        env = dict(os.environ, GIT_CONFIG_GLOBAL=os.devnull, GIT_CONFIG_SYSTEM=os.devnull)
        return subprocess.run([*self.GIT, '-C', str(repository), *args], capture_output=True, text=True, check=True, env=env).stdout.strip()

    def commit_manifest(self, repository, public_bytes):
        target = repository / self.MANIFEST
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_bytes(public_bytes)
        self.git(repository, 'add', '-A')
        self.git(repository, 'commit', '-q', '-m', 'synthetic manifest')
        return self.git(repository, 'rev-parse', 'HEAD')

    def run_cli(self, *args):
        return subprocess.run(['dotnet', str(DLL), *map(str, args)], capture_output=True, text=True, timeout=60)

    def make_predecessor(self, name, count, trailing=b''):
        """A real predecessor pair built by the existing constructor, plus a git repository holding its public blob."""
        base = self.dir / name
        base.mkdir()
        repositories = ['FS-GG/.github', 'FS-GG/FS.GG.Templates']
        rows = [{'issue': f'FS-GG/.github#{70001+i}', 'nodeId': f'I_synthetic_{self.SENTINEL}_{i}',
                 'roadmap': f'https://github.com/FS-GG/.github/blob/main/docs/roadmaps/old-{self.SENTINEL}-{i}.md'} for i in range(count)]
        def field(label, options):
            return {'id': 'synthetic-' + label, 'options': {value: f'synthetic-{label}-{i}' for i, value in enumerate(options)}}
        template = {
            'bindingVersion': 5, 'schemaVersion': 3, 'importRecipeRevision': 'a'*40, 'importArtifactSha256': 'b'*64,
            'selectedIssues': {row['nodeId']: row['issue'] for row in rows},
            'recipeRevision': 'c'*40, 'populationRevision': 'e'*40, 'organizationId': 'O_kgDOEYAWYw', 'artifactSha256': 'd'*64,
            'ownerKind': 'organization', 'owner': 'FS-GG', 'projectNumber': 3, 'projectTitle': 'Coordination V2', 'projectId': self.PROJECT,
            'status': field('status', ['Backlog', 'Ready', 'In progress', 'In review', 'Blocked', 'Done']),
            'roadmapFieldId': 'synthetic-roadmap', 'track': field('track', ['Active delivery', 'Follow-up']),
            'observation': field('observation', ['Verified', 'Stale', 'Unknown']),
            'workLevel': {'id': 'synthetic-work-level', 'options': {'Outcome': 'synthetic-level-outcome', 'Delivery': 'synthetic-level-delivery'}},
            'repositories': repositories}
        payload = {'schema': 'fsgg.coordination-board-v2-enrollment-payload/v1',
                   'target': {'owner': 'FS-GG', 'organizationId': template['organizationId'], 'projectId': template['projectId'], 'projectNumber': 3, 'schemaVersion': 3},
                   'binding': {'recipeRevision': template['importRecipeRevision'], 'artifactSha256': template['importArtifactSha256'], 'repositories': repositories},
                   'items': rows}
        raw = self.wire(payload) + trailing
        (base / 'template.json').write_bytes(self.wire(template))
        (base / 'payload.json').write_bytes(raw)
        done = self.run_cli('prepare-programme', '--binding-template', base/'template.json', '--payload', base/'payload.json',
                            '--private-output', base/'private.json', '--public-output', base/'public.json')
        self.assertEqual(0, done.returncode, done.stderr)
        repository = base / 'repository'
        repository.mkdir()
        self.git(repository, 'init', '-q')
        merge = self.commit_manifest(repository, (base / 'public.json').read_bytes())
        return {'base': base, 'private': base / 'private.json', 'public': base / 'public.json', 'payload': raw, 'rows': rows,
                'template': template, 'repository': repository, 'merge': merge}

    def request(self, fixture=None, **changes):
        fixture = fixture or self.fixture
        value = {'schema': 'fsgg.board-v2-successor-request/1', 'cohort': 'cohort-synthetic',
                 'predecessor': {'privatePath': str(fixture['private']), 'mergeSha': fixture['merge']},
                 'issues': [
                     {'ref': 'FS-GG/.github#80001', 'slice': 'synthetic-outcome', 'plan': {'kind': 'programmeIssue3008'}, 'classification': 'Outcome'},
                     {'ref': 'FS-GG/FS.GG.Templates#80002', 'slice': 'synthetic-delivery',
                      'plan': {'kind': 'protectedDoc', 'repo': 'FS-GG/.github', 'path': self.PLAN_PATH}, 'classification': 'Delivery'}]}
        value.update(changes)
        return value

    def facts(self, **changes):
        def issue(ref, node, repository, **extra):
            row = {'ref': ref, 'nodeId': node, 'kind': 'Issue', 'repository': repository, 'projectMember': True, 'visibility': 'complete', 'humanRoadmap': None}
            row.update(extra)
            return row
        value = {'schema': 'fsgg.board-v2-native-facts/1', 'projectId': self.PROJECT,
                 'issues': [issue('FS-GG/.github#80001', f'I_synthetic_{self.SENTINEL}_new1', 'FS-GG/.github'),
                            issue('FS-GG/FS.GG.Templates#80002', f'I_synthetic_{self.SENTINEL}_new2', 'FS-GG/FS.GG.Templates')],
                 'planBlobs': [{'repository': 'FS-GG/.github', 'path': self.PLAN_PATH, 'onMain': True}]}
        value.update(changes)
        return value

    def plan(self, request=None, facts=None, fixture=None, name='plan', raw_request=None):
        fixture = fixture or self.fixture
        out = self.dir / name
        out.mkdir(exist_ok=True)
        (out / 'request.json').write_bytes(raw_request if raw_request is not None else self.wire(request or self.request(fixture)))
        (out / 'facts.json').write_bytes(self.wire(facts or self.facts()))
        result = self.run_cli('plan-successor', '--request', out/'request.json', '--native-facts', out/'facts.json', '--repository', fixture['repository'],
                              '--template-output', out/'template.json', '--payload-output', out/'payload.json')
        return result, out

    def refused(self, result, code):
        self.assertEqual(2, result.returncode, result.stdout + result.stderr)
        self.assertEqual('', result.stdout)
        self.assertEqual(f'board-v2 successor refused: {code}\n', result.stderr)
        self.assertNotIn(self.SENTINEL, result.stdout + result.stderr)

    def prepare(self, out, fixture=None, request=None, name='pair', facts=None):
        fixture = fixture or self.fixture
        (out / 'request.json').write_bytes(self.wire(request or self.request(fixture)))
        if facts is not None:
            (out / 'facts.json').write_bytes(self.wire(facts))
        return self.run_cli('prepare-successor', '--request', out/'request.json', '--native-facts', out/'facts.json', '--repository', fixture['repository'],
                            '--template', out/'template.json', '--payload', out/'payload.json',
                            '--private-output', out/(name+'-private.json'), '--public-output', out/(name+'-public.json'), '--receipt-output', out/(name+'-receipt.json'))

    def expected_fragment(self, fixture):
        return [(f'FS-GG/.github#80001', f'I_synthetic_{self.SENTINEL}_new1', 'https://github.com/FS-GG/.github/issues/3008'),
                (f'FS-GG/FS.GG.Templates#80002', f'I_synthetic_{self.SENTINEL}_new2', 'https://github.com/FS-GG/.github/blob/main/' + self.PLAN_PATH)]

    def test_actual_plan_prepare_bind_and_carrier_with_independent_oracle(self):
        fixture = self.fixture
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual('successor planned; rows=7 appended=2; offline only\n', result.stdout)
        self.assertEqual('', result.stderr)
        # Independent oracle: the successor payload is the predecessor bytes minus the closing, plus the rows.
        fragment = b','.join(self.wire({'issue': i, 'nodeId': n, 'roadmap': r}) for i, n, r in self.expected_fragment(fixture))
        expected = fixture['payload'][:-2] + b',' + fragment + b']}'
        self.assertEqual(expected, (out / 'payload.json').read_bytes())
        self.assertEqual(fixture['payload'][:-2], (out / 'payload.json').read_bytes()[:len(fixture['payload']) - 2])
        template = json.loads((out / 'template.json').read_bytes())
        self.assertNotIn('programmeEnrollment', template)
        self.assertEqual(fixture['merge'], template['populationRevision'])
        self.assertEqual(7, len(template['selectedIssues']))
        for key in fixture['template']:
            if key not in ('selectedIssues', 'populationRevision'):
                self.assertEqual(fixture['template'][key], template[key])
        if os.name != 'nt':
            self.assertEqual(0o600, (out / 'payload.json').stat().st_mode & 0o777)
            self.assertEqual(0o600, (out / 'template.json').stat().st_mode & 0o777)
        prepared = self.prepare(out)
        self.assertEqual(0, prepared.returncode, prepared.stderr)
        self.assertEqual('successor pair prepared; offline only\n', prepared.stdout)
        self.assertEqual('', prepared.stderr)
        private_bytes, public_bytes = (out / 'pair-private.json').read_bytes(), (out / 'pair-public.json').read_bytes()
        envelope = json.loads(private_bytes)['programmeEnrollment']
        nonce = bytes.fromhex(envelope['nonceHex'])
        self.assertEqual(expected, base64.b64decode(envelope['payloadBase64'], validate=True))
        commitment = hashlib.sha256(self.DOMAIN + nonce + len(expected).to_bytes(8, 'big') + expected).hexdigest()
        self.assertEqual(commitment, json.loads(public_bytes)['commitmentSha256'])
        previous = json.loads(fixture['private'].read_bytes())['programmeEnrollment']['nonceHex']
        self.assertNotEqual(previous, envelope['nonceHex'])
        receipt_bytes = (out / 'pair-receipt.json').read_bytes()
        receipt = json.loads(receipt_bytes)
        self.assertEqual({'schema', 'cohort', 'predecessorMergeSha', 'rows', 'privateSha256', 'publicSha256', 'commitmentSha256', 'constructor',
                          'prefixPreserved', 'headerPreserved', 'recomputedCommitment'}, set(receipt))
        self.assertEqual({'predecessor': 5, 'appended': 2, 'total': 7}, receipt['rows'])
        self.assertEqual(hashlib.sha256(private_bytes).hexdigest(), receipt['privateSha256'])
        self.assertEqual(hashlib.sha256(public_bytes).hexdigest(), receipt['publicSha256'])
        self.assertEqual(commitment, receipt['commitmentSha256'])
        self.assertEqual(fixture['merge'], receipt['predecessorMergeSha'])
        self.assertEqual({'revision', 'artifactSha256'}, set(receipt['constructor']))
        self.assertEqual(hashlib.sha256(DLL.read_bytes()).hexdigest(), receipt['constructor']['artifactSha256'])
        if SOURCE_REVISION:
            self.assertEqual(SOURCE_REVISION, receipt['constructor']['revision'])
        self.assertEqual([True, True, True], [receipt['prefixPreserved'], receipt['headerPreserved'], receipt['recomputedCommitment']])
        if os.name != 'nt':
            for name in ['pair-private.json', 'pair-public.json', 'pair-receipt.json']:
                self.assertEqual(0o600, (out / name).stat().st_mode & 0o777)
        # The fixed carrier is a pure function of the receipt.
        carrier = self.run_cli('render-carrier', '--receipt', out/'pair-receipt.json', '--output', out/'carrier.md')
        self.assertEqual(0, carrier.returncode, carrier.stderr)
        text = (out / 'carrier.md').read_text()
        for needle in [commitment, receipt['publicSha256'], fixture['merge'], 'cohort-synthetic', '5 rows']:
            if needle != '5 rows':
                self.assertIn(needle, text)
        self.assertIn('appends 2 row(s) to the 5 rows', text)
        other = self.run_cli('render-carrier', '--receipt', out/'pair-receipt.json', '--output', out/'carrier2.md')
        self.assertEqual(text, (out / 'carrier2.md').read_text())
        # Bind: the delivered public blob at the merge authenticates the pair; only populationRevision changes.
        successor_repository = self.dir / 'successor-repository'
        successor_repository.mkdir()
        self.git(successor_repository, 'init', '-q')
        merge = self.commit_manifest(successor_repository, public_bytes)
        bound = self.run_cli('bind-runtime', '--private-binding', out/'pair-private.json', '--repository', successor_repository,
                             '--merge-sha', merge, '--runtime-output', out/'runtime.json')
        self.assertEqual(0, bound.returncode, bound.stderr)
        runtime_bytes = (out / 'runtime.json').read_bytes()
        self.assertEqual(len(private_bytes), len(runtime_bytes))
        differing = [i for i in range(len(private_bytes)) if private_bytes[i] != runtime_bytes[i]]
        needle = private_bytes.index(b'"populationRevision":"') + len(b'"populationRevision":"')
        self.assertTrue(differing and all(needle <= i < needle + 40 for i in differing))
        self.assertEqual(merge, json.loads(runtime_bytes)['populationRevision'])
        # The pair does not authenticate at the predecessor's merge, and an existing output is never overwritten.
        self.refused(self.run_cli('bind-runtime', '--private-binding', out/'pair-private.json', '--repository', fixture['repository'],
                                  '--merge-sha', fixture['merge'], '--runtime-output', out/'runtime-wrong.json'), 'PredecessorCommitmentMismatch')
        self.assertFalse((out / 'runtime-wrong.json').exists())
        self.refused(self.run_cli('bind-runtime', '--private-binding', out/'pair-private.json', '--repository', successor_repository,
                                  '--merge-sha', merge, '--runtime-output', out/'runtime.json'), 'OutputExists')
        self.assertEqual(runtime_bytes, (out / 'runtime.json').read_bytes())
        # No private value reaches any output stream, receipt or carrier.
        visible = result.stdout + result.stderr + prepared.stdout + prepared.stderr + carrier.stdout + carrier.stderr + bound.stdout + bound.stderr + receipt_bytes.decode() + text
        for secret in [self.SENTINEL, envelope['nonceHex'], envelope['payloadBase64'], 'I_synthetic', '80001', '70001', self.PLAN_PATH]:
            self.assertNotIn(secret, visible)

    def assert_refusal(self, code, request=None, facts=None, fixture=None, raw_request=None):
        result, out = self.plan(request, facts, fixture, name='refusal-' + code.lower() + str(len(list(self.dir.iterdir()))), raw_request=raw_request)
        self.refused(result, code)
        self.assertFalse((out / 'template.json').exists())
        self.assertFalse((out / 'payload.json').exists())

    def facts_issue(self, index, **changes):
        facts = self.facts()
        facts['issues'][index].update(changes)
        return facts

    def test_one_negative_control_per_refusal_code(self):
        request = self.request()
        # DuplicateIssue
        duplicate = self.request(); duplicate['issues'].append(copy.deepcopy(duplicate['issues'][0]))
        self.assert_refusal('DuplicateIssue', duplicate)
        # AlreadyEnrolled: the same ref and node as a predecessor row
        enrolled = self.request(); enrolled['issues'][0]['ref'] = self.fixture['rows'][0]['issue']
        self.assert_refusal('AlreadyEnrolled', enrolled, self.facts_issue(0, ref=self.fixture['rows'][0]['issue'], nodeId=self.fixture['rows'][0]['nodeId']))
        # IsPullRequest
        self.assert_refusal('IsPullRequest', request, self.facts_issue(0, kind='PullRequest'))
        # NodeIdMismatch: a node id already enrolled for a different ref, a foreign repository fact and a duplicated node
        self.assert_refusal('NodeIdMismatch', request, self.facts_issue(0, nodeId=self.fixture['rows'][1]['nodeId']))
        self.assert_refusal('NodeIdMismatch', request, self.facts_issue(0, repository='FS-GG/FS.GG.Templates'))
        self.assert_refusal('NodeIdMismatch', request, self.facts_issue(1, nodeId=self.facts()['issues'][0]['nodeId']))
        # NotProjectMember
        self.assert_refusal('NotProjectMember', request, self.facts_issue(1, projectMember=False))
        wrong_project = self.facts(); wrong_project['projectId'] = 'PVT_other'
        self.assert_refusal('NotProjectMember', request, wrong_project)
        # RepositoryOutsideBinding
        outside = self.request(); outside['issues'][0]['ref'] = 'FS-GG/Other#80001'
        self.assert_refusal('RepositoryOutsideBinding', outside)
        # PredecessorCommitmentMismatch: the blob at the supplied merge is a different commitment
        other = self.make_predecessor('other', 5)
        mismatch = self.request(); mismatch['predecessor']['mergeSha'] = other['merge']
        result, out = self.plan(mismatch, name='mismatch')
        self.refused(result, 'GitReadFailed')  # that commit is not in this repository: a failed read, not a mismatch
        swapped = self.request(); swapped['predecessor']['privatePath'] = str(other['private'])
        self.assert_refusal('PredecessorCommitmentMismatch', swapped)
        # PrefixNotPreserved: a predecessor payload that cannot be spliced (trailing newline after the closing)
        newline = self.make_predecessor('newline', 5, trailing=b'\n')
        self.assert_refusal('PrefixNotPreserved', self.request(newline), fixture=newline)
        # PlanNotAdmitted: the r1 cohort-89 failure mode, an Outcome issue URL as Roadmap, is not constructible and refuses first
        issue_plan = self.request(); issue_plan['issues'][1]['plan'] = {'kind': 'protectedDoc', 'repo': 'FS-GG/.github', 'path': 'issues/4930'}
        self.assert_refusal('PlanNotAdmitted', issue_plan, self.facts())
        for repo, path in [('FS-GG/.github', 'roadmaps/not-docs.md'), ('FS-GG/.github', 'docs/../escape.md'), ('FS-GG/.github', 'docs//double.md'),
                           ('FS-GG/Other', 'docs/x.md')]:
            with self.subTest(repo=repo, path=path):
                bad = self.request(); bad['issues'][1]['plan'] = {'kind': 'protectedDoc', 'repo': repo, 'path': path}
                self.assert_refusal('PlanNotAdmitted', bad, self.facts())
        # PlanBlobAbsentOnMain: offline plan-existence fact absent or false
        self.assert_refusal('PlanBlobAbsentOnMain', request, self.facts(planBlobs=[]))
        self.assert_refusal('PlanBlobAbsentOnMain', request, self.facts(planBlobs=[{'repository': 'FS-GG/.github', 'path': self.PLAN_PATH, 'onMain': False}]))
        # ConflictingHumanValue (an equal value is accepted below)
        self.assert_refusal('ConflictingHumanValue', request, self.facts_issue(0, humanRoadmap='https://github.com/FS-GG/.github/issues/4930'))
        # PartialVisibility: partial fact and missing fact
        self.assert_refusal('PartialVisibility', request, self.facts_issue(0, visibility='partial'))
        missing = self.facts(); missing['issues'].pop()
        self.assert_refusal('PartialVisibility', request, missing)
        # RowCapExceeded: exactly at the installed ceiling is accepted, one beyond refuses
        full98 = self.make_predecessor('full98', 98)
        accepted, _ = self.plan(self.request(full98), fixture=full98, name='cap-ok')
        self.assertEqual(0, accepted.returncode, accepted.stderr)
        self.assertEqual('successor planned; rows=100 appended=2; offline only\n', accepted.stdout)
        full99 = self.make_predecessor('full99', 99)
        self.assert_refusal('RowCapExceeded', self.request(full99), fixture=full99)
        # MalformedInput: unknown, duplicate and missing members
        raw = self.wire(self.request())
        self.assert_refusal('MalformedInput', raw_request=raw[:-1] + b',"extra":1}')
        self.assert_refusal('MalformedInput', raw_request=raw[:-1] + b',"cohort":"again"}')
        self.assert_refusal('MalformedInput', raw_request=b'{"schema":"fsgg.board-v2-successor-request/1"}')

    def test_equal_human_roadmap_value_is_accepted(self):
        facts = self.facts()
        facts['issues'][0]['humanRoadmap'] = 'https://github.com/FS-GG/.github/issues/3008'
        result, out = self.plan(facts=facts)
        self.assertEqual(0, result.returncode, result.stderr)

    def test_outputs_exist_refuses_and_never_overwrites(self):
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        originals = [(out / n).read_bytes() for n in ['template.json', 'payload.json']]
        again = self.run_cli('plan-successor', '--request', out/'request.json', '--native-facts', out/'facts.json', '--repository', self.fixture['repository'],
                             '--template-output', out/'template.json', '--payload-output', out/'payload.json')
        self.refused(again, 'OutputExists')
        self.assertEqual(originals, [(out / n).read_bytes() for n in ['template.json', 'payload.json']])
        for retained in ['pair-private.json', 'pair-public.json', 'pair-receipt.json']:
            with self.subTest(retained=retained):
                for name in ['pair-private.json', 'pair-public.json', 'pair-receipt.json']:
                    if (out / name).exists(): (out / name).unlink()
                (out / retained).write_bytes(self.SENTINEL.encode())
                self.refused(self.prepare(out), 'OutputExists')
                self.assertEqual(self.SENTINEL.encode(), (out / retained).read_bytes())
                self.assertEqual({retained}, {n for n in ['pair-private.json', 'pair-public.json', 'pair-receipt.json'] if (out / n).exists()})

    PAIR = ['pair-private.json', 'pair-public.json', 'pair-receipt.json']

    def assert_nothing_prepared(self, out):
        self.assertEqual([], [n for n in self.PAIR if (out / n).exists()])

    def test_prefix_and_template_mutations_are_refused_before_any_output_exists(self):
        """O1: the supplied template and payload are untrusted; they must equal the planner's own re-derivation."""
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        original = (out / 'payload.json').read_bytes()
        original_template = (out / 'template.json').read_bytes()
        prefix_end = len(self.fixture['payload']) - 2
        positions = {'first old roadmap': original.index(b'old-') + 1, 'last old roadmap': original.rindex(b'old-', 0, prefix_end) + 1,
                     'last prefix byte before the closing object': prefix_end - 3}
        for label, position in positions.items():
            with self.subTest(mutation=label):
                mutated = bytearray(original)
                self.assertLess(position, prefix_end)
                mutated[position] = ord('Q') if mutated[position] != ord('Q') else ord('R')
                (out / 'payload.json').write_bytes(bytes(mutated))
                self.refused(self.prepare(out), 'PrefixNotPreserved')
                self.assert_nothing_prepared(out)
        # A repository reorder keeps the payload valid: header bytes differ, rows are identical.
        reordered = original.replace(b'"repositories":["FS-GG/.github","FS-GG/FS.GG.Templates"]', b'"repositories":["FS-GG/FS.GG.Templates","FS-GG/.github"]')
        self.assertNotEqual(original, reordered)
        (out / 'payload.json').write_bytes(reordered)
        self.refused(self.prepare(out), 'PrefixNotPreserved')
        self.assert_nothing_prepared(out)
        # A template that drifts any binding field other than the selected issues and population revision.
        (out / 'payload.json').write_bytes(original)
        template = json.loads(original_template)
        template['status']['options']['Ready'] = 'synthetic-drifted-option-id'
        (out / 'template.json').write_bytes(self.wire(template))
        self.refused(self.prepare(out), 'SuccessorNotRederived')
        self.assert_nothing_prepared(out)
        # Causal control: restoring exactly the derived bytes is accepted, so the re-derivation caused every refusal above.
        (out / 'template.json').write_bytes(original_template)
        accepted = self.prepare(out)
        self.assertEqual(0, accepted.returncode, accepted.stderr)

    def test_hand_edited_appended_rows_are_refused_and_the_old_behaviour_would_have_reached_a_commitment(self):
        """O1 causal: each edited payload is accepted by the installed constructor (prepare-programme) and refused by prepare-successor."""
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        payload = (out / 'payload.json').read_bytes()
        template = (out / 'template.json').read_bytes()
        outcome_url = b'"roadmap":"https://github.com/FS-GG/.github/issues/4930"'
        cases = {
            'PlanNotAdmitted': (payload.replace(b'"roadmap":"https://github.com/FS-GG/.github/issues/3008"', outcome_url), template),
            'NodeIdMismatch': (payload.replace(f'I_synthetic_{self.SENTINEL}_new1'.encode(), b'I_synthetic_other'),
                               template.replace(f'I_synthetic_{self.SENTINEL}_new1'.encode(), b'I_synthetic_other')),
            'SuccessorNotRederived': (payload.replace(b'},{"issue":"FS-GG/FS.GG.Templates#80002"', b'}, {"issue":"FS-GG/FS.GG.Templates#80002"'), template),
        }
        for code, (edited_payload, edited_template) in cases.items():
            with self.subTest(code=code):
                self.assertNotEqual(payload, edited_payload)
                (out / 'payload.json').write_bytes(edited_payload)
                (out / 'template.json').write_bytes(edited_template)
                # Positive leg: the unchanged installed constructor turns the very same bytes into a commitment.
                legacy = self.run_cli('prepare-programme', '--binding-template', out/'template.json', '--payload', out/'payload.json',
                                      '--private-output', out/f'legacy-{code}-private.json', '--public-output', out/f'legacy-{code}-public.json')
                self.assertEqual(0, legacy.returncode, legacy.stderr)
                self.assertTrue((out / f'legacy-{code}-public.json').exists())
                # Negative leg: the successor route refuses before creating anything.
                self.refused(self.prepare(out), code)
                self.assert_nothing_prepared(out)

    def test_facts_that_no_longer_support_the_inputs_refuse_prepare(self):
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        self.refused(self.prepare(out, facts=self.facts(planBlobs=[])), 'PlanBlobAbsentOnMain')
        self.refused(self.prepare(out, facts=self.facts_issue(1, projectMember=False)), 'NotProjectMember')
        self.assert_nothing_prepared(out)
        self.assertEqual(0, self.prepare(out, facts=self.facts()).returncode)

    def test_error_codes_are_split_by_cause(self):
        """O2: input-unreadable, custody-unavailable and git-read-failed are distinct from malformed input and mismatch."""
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        run = lambda **c: self.run_cli('plan-successor', '--request', c.get('request', out/'request.json'), '--native-facts', c.get('facts', out/'facts.json'),
                                       '--repository', c.get('repository', self.fixture['repository']),
                                       '--template-output', c.get('template', out/'o-template.json'), '--payload-output', c.get('payload', out/'o-payload.json'))
        # InputUnreadable: absent, oversize and linked input files.
        self.refused(run(facts=out/'missing.json'), 'InputUnreadable')
        (out / 'big.json').write_bytes(b' ' * (1048576 + 1))
        self.refused(run(facts=out/'big.json'), 'InputUnreadable')
        self.refused(self.run_cli('prepare-successor', '--request', out/'request.json', '--native-facts', out/'facts.json', '--repository', self.fixture['repository'],
                                  '--template', out/'template.json', '--payload', out/'missing-payload.json',
                                  '--private-output', out/'a.json', '--public-output', out/'b.json', '--receipt-output', out/'c.json'), 'InputUnreadable')
        # Undecodable content is still MalformedInput, not InputUnreadable.
        (out / 'garbage.json').write_bytes(b'\xff\xfe')
        self.refused(run(facts=out/'garbage.json'), 'MalformedInput')
        # GitReadFailed: a repository that is not a repository, and a merge sha it does not contain.
        not_repo = self.dir / 'not-a-repo'
        not_repo.mkdir()
        self.refused(run(repository=not_repo), 'GitReadFailed')
        self.refused(run(repository=self.dir / 'absent-repo'), 'GitReadFailed')
        # ...whereas a blob that is read and does not authenticate remains a commitment mismatch.
        other = self.make_predecessor('other-split', 5)
        swapped = self.request(); swapped['predecessor']['privatePath'] = str(other['private'])
        self.assert_refusal('PredecessorCommitmentMismatch', swapped)
        # CustodyUnavailable: the output directory cannot take a new file.
        if os.name != 'nt' and os.geteuid() != 0:
            locked = self.dir / 'locked'
            locked.mkdir()
            locked.chmod(0o500)
            try:
                self.refused(run(template=locked/'t.json', payload=locked/'p.json'), 'CustodyUnavailable')
                self.assertEqual([], list(locked.iterdir()))
            finally:
                locked.chmod(0o700)

    def test_a_linked_output_path_is_refused_as_custody_and_creates_nothing(self):
        """The CreateNew race itself is controlled in the F# custody tests, where it can be made deterministic."""
        if os.name == 'nt':
            self.skipTest('symbolic links')
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        race = out / 'race-template.json'
        race.symlink_to(out / 'nowhere-at-all')
        again = self.run_cli('plan-successor', '--request', out/'request.json', '--native-facts', out/'facts.json', '--repository', self.fixture['repository'],
                             '--template-output', race, '--payload-output', out/'race-payload.json')
        self.refused(again, 'CustodyUnavailable')
        self.assertTrue(race.is_symlink())
        self.assertFalse((out / 'nowhere-at-all').exists())
        self.assertFalse((out / 'race-payload.json').exists())

    FAKE_GH = """#!/usr/bin/env python3
import json, os, sys
argv = sys.argv[1:]
with open(os.environ['FAKE_GH_LOG'], 'a') as log:
    log.write(json.dumps(argv) + '\\n')
routes = json.load(open(os.environ['FAKE_GH_ROUTES']))
path = argv[-1]
for prefix, body in routes:
    if path == prefix or path.startswith(prefix):
        if body is None:
            sys.stderr.write('gh: Not Found (HTTP 404)\\n'); sys.exit(1)
        if body == 'FAIL':
            sys.stderr.write('gh: boom (HTTP 500)\\n'); sys.exit(1)
        if body == 'HUGE':
            sys.stdout.write('x' * (33554432 + 4096)); sys.exit(0)
        sys.stdout.write(json.dumps(body) if not isinstance(body, str) else body)
        sys.exit(0)
sys.stderr.write('gh: Not Found (HTTP 404)\\n'); sys.exit(1)
"""

    def item_json(self, node, number, repository, status=True, track=True, level=True, roadmap=None, updated='2026-01-01T00:00:00Z'):
        def single(name, text):
            return {'id': 2, 'name': name, 'data_type': 'single_select', 'value': {'id': 'o', 'name': {'html': text, 'raw': text}}}
        fields = []
        if status: fields.append(single('Status', 'Backlog'))
        if track: fields.append(single('Track', 'Active delivery'))
        if level: fields.append(single('Work level', 'Delivery'))
        if roadmap: fields.append({'id': 1, 'name': 'Roadmap', 'data_type': 'text', 'value': {'html': '<a>x</a>', 'raw': roadmap}})
        return {'node_id': 'PVTI_' + node, 'content_type': 'Issue', 'updated_at': updated,
                'content': {'node_id': node, 'number': number, 'repository_url': 'https://api.github.com/repos/' + repository}, 'fields': fields}

    MAIN_SHA = 'e' * 40

    def fake_gh(self, items=None, overrides=None, name='fake', main=None):
        """A recording stand-in for the GitHub CLI. Returns (environment, argv log path)."""
        base = self.dir / name
        base.mkdir(exist_ok=True)
        script = base / 'gh'
        script.write_text(self.FAKE_GH)
        script.chmod(0o755)
        enrolled = [self.item_json(row['nodeId'], 70001 + i, 'FS-GG/.github') for i, row in enumerate(self.fixture['rows'])]
        seeded = [self.item_json(f'I_synthetic_{self.SENTINEL}_new1', 80001, 'FS-GG/.github', roadmap='https://github.com/FS-GG/.github/issues/3008'),
                  self.item_json(f'I_synthetic_{self.SENTINEL}_new2', 80002, 'FS-GG/FS.GG.Templates')]
        routes = [[p, b] for p, b in (overrides or [])] + [
            ['/orgs/FS-GG/projectsV2/3/fields', [[{'id': 10, 'name': 'Status'}, {'id': 11, 'name': 'Roadmap'}, {'id': 12, 'name': 'Track'}, {'id': 13, 'name': 'Work level'}]]],
            ['/orgs/FS-GG/projectsV2/3/items', [enrolled + seeded + (items or [])]],
            ['/orgs/FS-GG/projectsV2/3', {'number': 3, 'node_id': self.PROJECT}],
            ['/repos/FS-GG/.github/issues/80001', {'node_id': f'I_synthetic_{self.SENTINEL}_new1', 'repository_url': 'https://api.github.com/repos/FS-GG/.github'}],
            ['/repos/FS-GG/FS.GG.Templates/issues/80002', {'node_id': f'I_synthetic_{self.SENTINEL}_new2', 'repository_url': 'https://api.github.com/repos/FS-GG/FS.GG.Templates'}],
            ['/repos/FS-GG/.github/contents/' + self.PLAN_PATH, {'type': 'file'}],
            ['/repos/FS-GG/.github/git/ref/heads/main', {'object': {'sha': main or SOURCE_REVISION or self.MAIN_SHA}}]]
        (base / 'routes.json').write_text(json.dumps(routes))
        log = base / 'argv.log'
        log.write_text('')
        env = dict(os.environ, BOARD_V2_IMPORT_GH=str(script), FAKE_GH_LOG=str(log), FAKE_GH_ROUTES=str(base / 'routes.json'))
        return env, log

    def run_cli_env(self, env, *args):
        return subprocess.run(['dotnet', str(DLL), *map(str, args)], capture_output=True, text=True, timeout=120, env=env)

    def argv_log(self, log):
        return [json.loads(line) for line in log.read_text().splitlines()]

    def test_resolve_issues_emits_the_facts_the_planner_consumes_using_only_get(self):
        env, log = self.fake_gh()
        out = self.dir / 'resolve'
        out.mkdir()
        (out / 'request.json').write_bytes(self.wire(self.request()))
        done = self.run_cli_env(env, 'resolve-issues', '--request', out/'request.json', '--native-facts-output', out/'resolved.json')
        self.assertEqual(0, done.returncode, done.stderr)
        self.assertEqual('native facts resolved; issues=2; read-only\n', done.stdout)
        self.assertEqual('', done.stderr)
        self.assertNotIn(self.SENTINEL, done.stdout + done.stderr)
        if os.name != 'nt':
            self.assertEqual(0o600, (out / 'resolved.json').stat().st_mode & 0o777)
        resolved = json.loads((out / 'resolved.json').read_bytes())
        self.assertEqual('fsgg.board-v2-native-facts/1', resolved['schema'])
        self.assertEqual(self.PROJECT, resolved['projectId'])
        self.assertEqual([f'I_synthetic_{self.SENTINEL}_new1', f'I_synthetic_{self.SENTINEL}_new2'], [r['nodeId'] for r in resolved['issues']])
        self.assertEqual(['https://github.com/FS-GG/.github/issues/3008', None], [r['humanRoadmap'] for r in resolved['issues']])
        self.assertEqual([True, True], [r['projectMember'] for r in resolved['issues']])
        self.assertEqual([{'repository': 'FS-GG/.github', 'path': self.PLAN_PATH, 'onMain': True}], resolved['planBlobs'])
        # Every call is a GET and cannot carry a body, field, method override or the GraphQL endpoint.
        calls = self.argv_log(log)
        self.assertEqual(6, len(calls))
        for argv in calls:
            self.assertEqual(['api', '--method', 'GET'], argv[:3])
            self.assertTrue(argv[-1].startswith('/orgs/') or argv[-1].startswith('/repos/'))
            for forbidden in ['-f', '-F', '--field', '--raw-field', '--input', '-X', 'POST', 'PATCH', 'PUT', 'DELETE', 'graphql']:
                self.assertNotIn(forbidden, argv)
        # The planner accepts the resolved document: resolution is the missing online half of plan-successor.
        planned = self.run_cli('plan-successor', '--request', out/'request.json', '--native-facts', out/'resolved.json', '--repository', self.fixture['repository'],
                               '--template-output', out/'template.json', '--payload-output', out/'payload.json')
        self.assertEqual(0, planned.returncode, planned.stderr)
        self.assertEqual('successor planned; rows=7 appended=2; offline only\n', planned.stdout)

    def test_resolve_issues_failures_are_typed_and_write_nothing(self):
        out = self.dir / 'resolve-fail'
        out.mkdir()
        (out / 'request.json').write_bytes(self.wire(self.request()))
        for label, overrides in {'issue read fails': [['/repos/FS-GG/.github/issues/80001', 'FAIL']],
                                 'project read fails': [['/orgs/FS-GG/projectsV2/3/items', 'FAIL']],
                                 'malformed body': [['/repos/FS-GG/.github/issues/80001', '{"leak":"' + self.SENTINEL + '"']]}.items():
            with self.subTest(label):
                env, log = self.fake_gh(overrides=overrides, name='fail-' + label.replace(' ', '-'))
                done = self.run_cli_env(env, 'resolve-issues', '--request', out/'request.json', '--native-facts-output', out/'never.json')
                self.refused(done, 'NativeReadFailed')
                self.assertFalse((out / 'never.json').exists())
        # An absent issue (a clean 404) is partial visibility, which the planner then refuses with its own code.
        env, _ = self.fake_gh(overrides=[['/repos/FS-GG/.github/issues/80001', None]], name='absent')
        done = self.run_cli_env(env, 'resolve-issues', '--request', out/'request.json', '--native-facts-output', out/'absent.json')
        self.assertEqual(0, done.returncode, done.stderr)
        self.assertEqual('partial', json.loads((out / 'absent.json').read_bytes())['issues'][0]['visibility'])
        planned = self.run_cli('plan-successor', '--request', out/'request.json', '--native-facts', out/'absent.json', '--repository', self.fixture['repository'],
                               '--template-output', out/'t.json', '--payload-output', out/'p.json')
        self.refused(planned, 'PartialVisibility')
        # Outputs are never overwritten.
        self.refused(self.run_cli_env(env, 'resolve-issues', '--request', out/'request.json', '--native-facts-output', out/'absent.json'), 'OutputExists')

    def preflight(self, env, stage='PlanSuccessor', online=True, facts=None, extra=(), fixture=None, request=None, route='ProgrammeEnrollmentSuccessor', main=None, live_main=False):
        fixture = fixture or self.fixture
        out = self.dir / ('preflight-' + str(len(list(self.dir.iterdir()))))
        out.mkdir()
        (out / 'request.json').write_bytes(self.wire(request or self.request(fixture)))
        lock = {'version': 2, 'dependencies': {'net10.0': {'FSharp.Core': {'type': 'Direct', 'resolved': '1.2.3', 'contentHash': 'synthetic-hash=='}}}}
        (out / 'lock.json').write_bytes(self.wire(lock))
        cache = out / 'cache' / 'fsharp.core' / '1.2.3'
        cache.mkdir(parents=True)
        (cache / '.nupkg.metadata').write_bytes(self.wire({'version': 2, 'contentHash': 'synthetic-hash==', 'source': 's'}))
        args = ['preflight', '--route', route, '--stage', stage, '--request', out/'request.json', '--repository', fixture['repository'],
                '--lockfile', out/'lock.json', '--nuget-cache', out/'cache', '--min-headroom-bytes', '0']
        if not live_main:
            args += ['--main-revision', main or SOURCE_REVISION or 'a'*40]
        if online:
            args.append('--online')
        else:
            (out / 'facts.json').write_bytes(self.wire(facts or self.facts()))
            args += ['--native-facts', out/'facts.json']
        result = self.run_cli_env(env, *args, *extra)
        return result, out

    def verdicts(self, result):
        report = json.loads(result.stdout)
        self.assertEqual('fsgg.board-v2-preflight/1', report['schema'])
        return report, {row['check']: row for row in report['findings']}

    def assert_hygiene(self, result, out):
        visible = result.stdout + result.stderr
        for secret in [self.SENTINEL, 'I_synthetic', self.fixture['payload'].decode(), json.loads(self.fixture['private'].read_bytes())['programmeEnrollment']['nonceHex']]:
            self.assertNotIn(secret, visible)

    def test_preflight_exit_codes_distinguish_supported_unsupported_and_unknown(self):
        env, log = self.fake_gh()
        runtime = self.fixture['base'] / 'runtime-binding.json'
        binding = json.loads(self.fixture['private'].read_bytes())
        binding['populationRevision'] = self.fixture['merge']
        runtime.write_bytes(self.wire(binding))
        full = ['--runtime-binding', runtime, '--installed-reader', DLL, '--known-readers', self.write_known_readers()]
        # Everything decidable supports the stage. The host's cgroup may be unreadable: that is Unknown and never exit 0.
        result, out = self.preflight(env, extra=full)
        report, rows = self.verdicts(result)
        self.assert_hygiene(result, out)
        if SOURCE_REVISION:
            for name, row in rows.items():
                if name != 'MemoryHeadroom':
                    self.assertEqual('SupportedForNextStage', row['verdict'], (name, row))
            if rows['MemoryHeadroom']['verdict'] == 'SupportedForNextStage':
                self.assertEqual((0, 'SupportedForNextStage'), (result.returncode, report['overall']))
            else:
                self.assertEqual((11, 'Unknown'), (result.returncode, report['overall']))
                self.assertEqual('CgroupMemory/Unreadable', rows['MemoryHeadroom']['gap'])
        # Unknown: the installed artifact is not named, so its grammar cannot be asserted. Never exit 0.
        unknown, out = self.preflight(env, extra=['--runtime-binding', runtime])
        report, rows = self.verdicts(unknown)
        self.assertEqual('Unknown', report['overall'])
        self.assertEqual(11, unknown.returncode)
        self.assertEqual('InstalledReader/NotSupplied', rows['PlanGrammar']['gap'])
        # Unsupported: an unenrolled item without Status, Track and Work level (Project 3 auto-add). Names it.
        bare = self.item_json('I_auto_added', 4940, 'FS-GG/.github', status=False, track=False, level=False)
        env2, _ = self.fake_gh(items=[bare], name='autoadd')
        unsupported, out = self.preflight(env2, extra=full)
        report, rows = self.verdicts(unsupported)
        self.assertEqual((11, 'Unknown'), (unsupported.returncode, report['overall']))
        self.assertEqual(('Unknown', 'ProjectItems/BareMembershipsObserved:1', ['FS-GG/.github#4940[Status,Track,Work level]']),
                         tuple(rows['ProjectAutoAdd'][k] for k in ['verdict', 'gap', 'subjects']))
        self.assertIn('unproven', rows['ProjectAutoAdd']['note'])
        self.assertNotIn('Unsupported', {row['verdict'] for row in rows.values()})
        self.assert_hygiene(unsupported, out)
        # Causal: the same board with the item seeded (Status, Track and Work level present) is not an offender.
        seeded = self.item_json('I_auto_added', 4940, 'FS-GG/.github')
        env3, _ = self.fake_gh(items=[seeded], name='seeded')
        fine, out = self.preflight(env3, extra=full)
        self.assertEqual('SupportedForNextStage', self.verdicts(fine)[1]['ProjectAutoAdd']['verdict'])

    def write_known_readers(self):
        path = self.dir / 'known-readers.txt'
        path.write_text(hashlib.sha256(DLL.read_bytes()).hexdigest() + '\n')
        return path

    def test_preflight_offline_facts_cannot_assert_main_now_and_unregistered_routes_are_unknown(self):
        env, log = self.fake_gh()
        result, out = self.preflight(env, online=False)
        report, rows = self.verdicts(result)
        self.assertEqual('NativeFacts/NotLive', rows['PlanBlob']['gap'])
        self.assertEqual('ProjectItems/NotSupplied', rows['ProjectAutoAdd']['gap'])
        self.assertNotEqual(0, result.returncode)
        self.assertEqual([], self.argv_log(log))  # offline facts: no native read at all
        for route in ['RoutineDelivery', 'PublicationWriterRegistration']:
            unregistered, _ = self.preflight(env, online=False, route=route)
            self.assertEqual(11, unregistered.returncode)
            self.assertEqual(['RouteRegistry'], [row['check'] for row in json.loads(unregistered.stdout)['findings']])

    def test_preflight_names_each_unsupported_route_check_and_refuses_bad_arguments(self):
        env, _ = self.fake_gh()
        # The cap: 99 predecessor rows leave room for one request issue, not two.
        full99 = self.make_predecessor('preflight99', 99)
        result, _ = self.preflight(env, online=False, fixture=full99)
        report, rows = self.verdicts(result)
        self.assertEqual(('Unsupported', 'RowCapExceeded', 'ProtocolRelease', ['99+2>100']), tuple(rows['RowCap'][k] for k in ['verdict', 'reason', 'supplier', 'subjects']))
        self.assertEqual(10, result.returncode)
        # A constructor that is not the intended protected main is unsupported, supplied by a rebuild.
        wrong, _ = self.preflight(env, online=False, main='0' * 40)
        self.assertEqual(('Unsupported', 'ConstructorDiffersFromMain', 'ConstructorBuild'),
                         tuple(self.verdicts(wrong)[1]['ConstructorIdentity'][k] for k in ['verdict', 'reason', 'supplier']))
        # Usage errors are refusals (exit 2), not verdicts, and print only a code.
        for bad in (['preflight'], ['preflight', '--route', 'Nope', '--stage', 'PlanSuccessor', '--request', 'x', '--repository', 'y', '--online']):
            done = self.run_cli_env(env, *bad)
            self.refused(done, 'MalformedInput')

    # ---- PR 3: the live main head, the stage's own inputs, the routes and the stream cap ----

    def test_online_constructor_identity_reads_the_live_main_head_by_get(self):
        if not SOURCE_REVISION:
            self.skipTest('needs the build revision')
        env, log = self.fake_gh(main=SOURCE_REVISION, name='main-same')
        result, out = self.preflight(env, live_main=True)
        report, rows = self.verdicts(result)
        self.assertEqual('SupportedForNextStage', rows['ConstructorIdentity']['verdict'])
        self.assertIn('live remote head', rows['ConstructorIdentity']['evidence'])
        calls = [argv[-1] for argv in self.argv_log(log)]
        self.assertIn('/repos/FS-GG/.github/git/ref/heads/main', calls)
        for argv in self.argv_log(log):
            self.assertEqual(['api', '--method', 'GET'], argv[:3])
        # Causal: only the live head differs, and the verdict flips to Unsupported.
        other, _ = self.fake_gh(main='0' * 40, name='main-other')
        result, out = self.preflight(other, live_main=True)
        row = self.verdicts(result)[1]['ConstructorIdentity']
        self.assertEqual(('Unsupported', 'ConstructorDiffersFromMain'), (row['verdict'], row['reason']))
        # A failed head read is Unknown, not a verdict.
        broken, _ = self.fake_gh(overrides=[['/repos/FS-GG/.github/git/ref/heads/main', 'FAIL']], name='main-fail')
        result, out = self.preflight(broken, live_main=True)
        self.assertEqual('ProtectedMain/ReadFailed', self.verdicts(result)[1]['ConstructorIdentity']['gap'])

    def test_offline_constructor_identity_keeps_the_flag_and_marks_the_local_ref_as_possibly_stale(self):
        if not SOURCE_REVISION:
            self.skipTest('needs the build revision')
        env, log = self.fake_gh()
        # Flag asserted by the operator: a difference is Unsupported.
        result, out = self.preflight(env, online=False, main='0' * 40)
        row = self.verdicts(result)[1]['ConstructorIdentity']
        self.assertEqual(('Unsupported', 'ConstructorDiffersFromMain'), (row['verdict'], row['reason']))
        # No flag: the checkout's local tracking ref. A fixture repository carries one; a different value is only Unknown.
        repository = self.fixture['repository']
        self.git(repository, 'update-ref', 'refs/remotes/origin/main', 'HEAD')
        result, out = self.preflight(env, online=False, live_main=True)
        row = self.verdicts(result)[1]['ConstructorIdentity']
        self.assertEqual(('Unknown', 'ProtectedMain/NotLive'), (row['verdict'], row['gap']))
        self.assertEqual([], self.argv_log(log))  # offline: no read of the live head

    def test_bind_runtime_observes_the_successor_inputs(self):
        if not SOURCE_REVISION:
            self.skipTest('needs the build revision')
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertEqual(0, self.prepare(out).returncode)
        public_bytes = (out / 'pair-public.json').read_bytes()
        merge = self.commit_manifest(self.fixture['repository'], public_bytes)
        env, log = self.fake_gh()
        extra = ['--successor-private', out/'pair-private.json', '--successor-merge-sha', merge]
        done, _ = self.preflight(env, stage='BindRuntime', extra=extra)
        report, rows = self.verdicts(done)
        self.assertEqual(['ConstructorIdentity', 'SuccessorIntegrity', 'SuccessorExtendsPredecessor', 'PlanGrammar', 'PlanBlob', 'ToolchainLock',
                          'MemoryHeadroom', 'ProjectAutoAdd', 'AdjacencyChurn'], [row['check'] for row in report['findings']])
        self.assertEqual('SupportedForNextStage', rows['SuccessorIntegrity']['verdict'])
        self.assertEqual('SupportedForNextStage', rows['SuccessorExtendsPredecessor']['verdict'])
        self.assertNotIn('PredecessorIntegrity', rows)
        self.assert_hygiene(done, out)
        # Without the successor flags the stage's own inputs are unknown, never silently taken from the predecessor.
        missing, _ = self.preflight(env, stage='BindRuntime')
        rows = self.verdicts(missing)[1]
        self.assertEqual('SuccessorPrivate/NotSupplied', rows['SuccessorIntegrity']['gap'])
        self.assertEqual('SuccessorPrivate/NotSupplied', rows['SuccessorExtendsPredecessor']['gap'])
        # A merge that does not hold the successor's public blob is a failed git read, i.e. Unknown, not a verdict.
        wrong, _ = self.preflight(env, stage='BindRuntime', extra=['--successor-private', out/'pair-private.json', '--successor-merge-sha', '1' * 40])
        self.assertEqual('SuccessorPublic/Unreadable', self.verdicts(wrong)[1]['SuccessorIntegrity']['gap'])
        # The predecessor's own commit is a real merge, but its blob is the predecessor's commitment, not the successor's.
        not_it, _ = self.preflight(env, stage='BindRuntime', extra=['--successor-private', out/'pair-private.json', '--successor-merge-sha', self.fixture['merge']])
        row = self.verdicts(not_it)[1]['SuccessorIntegrity']
        self.assertEqual(('Unsupported', 'SuccessorCommitmentMismatch'), (row['verdict'], row['reason']))

    def routine_repository(self, branch='routine/synthetic'):
        """A real checkout carrying the routine policy, on a routine branch, with one commit."""
        repository = self.dir / ('routine-' + str(len(list(self.dir.iterdir()))))
        repository.mkdir()
        self.git(repository, 'init', '-q', '-b', branch)
        (repository / '.fsgg').mkdir()
        (repository / '.fsgg' / 'routine-development.json').write_bytes((ROOT / '.fsgg' / 'routine-development.json').read_bytes())
        self.git(repository, 'add', '-A')
        self.git(repository, 'commit', '-q', '-m', 'synthetic')
        return repository

    def routine(self, env, stage, repository, *extra, online=True):
        args = ['preflight', '--route', 'RoutineDelivery', '--stage', stage, '--repository', repository, *extra]
        if online:
            args.append('--online')
        return self.run_cli_env(env, *args)

    def pull_routes(self, head, base='b' * 40, body=None, number=7, listing=1, branch='routine/synthetic', main=None):
        pr = lambda n: {'number': n, 'state': 'open', 'title': 'feat: synthetic (Refs #4941)',
                        'body': body if body is not None else f'Refs #4941\n\n<!-- fsgg:routine-development/v1 head={head} operation=source-change -->\n',
                        'base': {'ref': 'main', 'sha': base}, 'head': {'ref': branch, 'sha': head}}
        return [[f'/repos/FS-GG/.github/pulls/{number}', pr(number)],
                ['/repos/FS-GG/.github/pulls?', [[pr(number + i) for i in range(listing)]]],
                ['/repos/FS-GG/.github/git/ref/heads/main', {'object': {'sha': main or base}}]]

    def test_routine_delivery_route_through_the_cli(self):
        repository = self.routine_repository()
        head = self.git(repository, 'rev-parse', 'HEAD')
        # Merge stage over a native pull request: healthy is exit 0 with eight supported checks.
        env, log = self.fake_gh(overrides=self.pull_routes(head), name='routine-ok')
        done = self.routine(env, 'MergePullRequest', repository, '--pr', '7')
        report = json.loads(done.stdout)
        self.assertEqual((0, 'SupportedForNextStage', 8), (done.returncode, report['overall'], len(report['findings'])), done.stdout)
        for argv in self.argv_log(log):
            self.assertEqual(['api', '--method', 'GET'], argv[:3])
        # One change at a time flips it, with the exact verdict.
        stale = self.routine(self.fake_gh(overrides=self.pull_routes(head, body='Refs #4941\n<!-- fsgg:routine-development/v1 head=' + '9' * 40 + ' operation=source-change -->'), name='routine-stale')[0], 'MergePullRequest', repository, '--pr', '7')
        rows = {r['check']: r for r in json.loads(stale.stdout)['findings']}
        self.assertEqual((10, 'MarkerHeadDiffers'), (stale.returncode, rows['MarkerHeadBound']['reason']))
        moved = self.routine(self.fake_gh(overrides=self.pull_routes(head, main='c' * 40), name='routine-moved')[0], 'MergePullRequest', repository, '--pr', '7')
        rows = {r['check']: r for r in json.loads(moved.stdout)['findings']}
        self.assertEqual((11, 'BaseHead/BaseMoved'), (moved.returncode, rows['BaseCurrent']['gap']))
        two = self.routine(self.fake_gh(overrides=self.pull_routes(head, listing=2), name='routine-two')[0], 'MergePullRequest', repository, '--pr', '7')
        rows = {r['check']: r for r in json.loads(two.stdout)['findings']}
        self.assertEqual((10, 'PullRequestNotSingle'), (two.returncode, rows['SinglePullRequest']['reason']))
        closing = self.routine(self.fake_gh(overrides=self.pull_routes(head, body='Closes #4941\n<!-- fsgg:routine-development/v1 head=' + head + ' operation=source-change -->'), name='routine-closing')[0], 'MergePullRequest', repository, '--pr', '7')
        rows = {r['check']: r for r in json.loads(closing.stdout)['findings']}
        self.assertEqual((11, 'PullRequest/ClosingLinkPresent:1', ['Closes #4941']), (closing.returncode, rows['ClosingLinkDeclared']['gap'], rows['ClosingLinkDeclared']['subjects']))
        # Open stage over a candidate that is not yet a pull request: the checkout's branch and head, plus a body file.
        body = self.dir / 'candidate-body.md'
        body.write_text(f'Refs #4941\n\n<!-- fsgg:routine-development/v1 head={head} operation=source-change -->\n')
        empty, _ = self.fake_gh(overrides=[['/repos/FS-GG/.github/pulls?', [[]]], ['/repos/FS-GG/.github/git/ref/heads/main', {'object': {'sha': 'b' * 40}}]], name='routine-open')
        opened = self.routine(empty, 'OpenPullRequest', repository, '--title', 'feat: synthetic (Refs #4941)', '--body-file', body)
        self.assertEqual((0, 7), (opened.returncode, len(json.loads(opened.stdout)['findings'])), opened.stdout)
        # Offline, the open-pull-request count cannot be known: Unknown, never supported.
        offline = self.routine(empty, 'OpenPullRequest', repository, '--title', 'feat: synthetic (Refs #4941)', '--body-file', body, online=False)
        rows = {r['check']: r for r in json.loads(offline.stdout)['findings']}
        self.assertEqual((11, 'OpenPullRequests/NotSupplied'), (offline.returncode, rows['SinglePullRequest']['gap']))
        # A body whose marker names another head is refused before a pull request exists.
        body.write_text('Refs #4941\n\n<!-- fsgg:routine-development/v1 head=' + '7' * 40 + ' operation=source-change -->\n')
        wrong = self.routine(empty, 'OpenPullRequest', repository, '--title', 't', '--body-file', body)
        self.assertEqual(10, wrong.returncode)

    def test_publication_writer_route_through_the_cli(self):
        env, _ = self.fake_gh()
        def run(stage, workflow):
            done = subprocess.run(['dotnet', str(DLL), 'preflight', '--route', 'PublicationWriterRegistration', '--stage', stage, '--repository', str(ROOT), '--workflow', workflow],
                                  capture_output=True, text=True, timeout=180, env=env)
            return done, {r['check']: r for r in json.loads(done.stdout)['findings']}
        registered = '.github/workflows/release-successor-publish.yml'
        done, rows = run('RunWriter', registered)
        self.assertEqual(['RegistryReadable', 'WriterRegistered', 'CoverageAudit'], list(rows))
        # The maintained gate is run for real and is authoritative: clean here, so the registered writer is supported.
        self.assertEqual((0, 'SupportedForNextStage'), (done.returncode, rows['CoverageAudit']['verdict']), done.stdout)
        done, rows = run('RegisterWriter', registered)
        self.assertEqual((10, 'WriterAlreadyRegistered'), (done.returncode, rows['NotYetRegistered']['reason']))
        self.assertEqual('SupportedForNextStage', rows['WorkflowPublishes']['verdict'])
        plain = '.github/workflows/graphql-monopoly.yml'
        done, rows = run('RegisterWriter', plain)
        self.assertEqual((10, 'WorkflowHasNoPublicationEffect'), (done.returncode, rows['WorkflowPublishes']['reason']))
        done, rows = run('RunWriter', plain)
        self.assertEqual((10, 'WriterNotRegistered'), (done.returncode, rows['WriterRegistered']['reason']))
        # A workflow path outside the workflow directory is refused rather than read.
        for bad in ['../../etc/passwd', '.github/workflows/../../x.yml', 'scripts/x.yml']:
            refused = subprocess.run(['dotnet', str(DLL), 'preflight', '--route', 'PublicationWriterRegistration', '--stage', 'RegisterWriter', '--repository', str(ROOT), '--workflow', bad],
                                     capture_output=True, text=True, timeout=60, env=env)
            self.refused(refused, 'MalformedInput')

    def test_a_response_over_the_stream_cap_is_a_failed_read_not_a_buffered_one(self):
        out = self.dir / 'huge'
        out.mkdir()
        (out / 'request.json').write_bytes(self.wire(self.request()))
        env, log = self.fake_gh(overrides=[['/orgs/FS-GG/projectsV2/3/items', 'HUGE']], name='huge')
        done = self.run_cli_env(env, 'resolve-issues', '--request', out/'request.json', '--native-facts-output', out/'never.json')
        self.refused(done, 'NativeReadFailed')
        self.assertFalse((out / 'never.json').exists())

    def test_carrier_refuses_open_or_foreign_receipts_and_never_echoes_them(self):
        result, out = self.plan()
        self.assertEqual(0, self.prepare(out).returncode)
        receipt = json.loads((out / 'pair-receipt.json').read_bytes())
        cases = []
        extra = copy.deepcopy(receipt); extra['note'] = self.SENTINEL; cases.append(extra)
        injected = copy.deepcopy(receipt); injected['cohort'] = 'cohort\n# ' + self.SENTINEL; cases.append(injected)
        short = copy.deepcopy(receipt); short['commitmentSha256'] = self.SENTINEL; cases.append(short)
        counts = copy.deepcopy(receipt); counts['rows']['total'] = 99; cases.append(counts)
        flag = copy.deepcopy(receipt); flag['prefixPreserved'] = self.SENTINEL; cases.append(flag)
        for index, case in enumerate(cases):
            with self.subTest(case=index):
                path = out / f'bad-{index}.json'
                path.write_bytes(self.wire(case))
                self.refused(self.run_cli('render-carrier', '--receipt', path, '--output', out / f'bad-{index}.md'), 'MalformedInput')
                self.assertFalse((out / f'bad-{index}.md').exists())

    def test_argument_and_path_custody_refuse(self):
        result, out = self.plan()
        self.assertEqual(0, result.returncode, result.stderr)
        self.refused(self.run_cli('plan-successor', '--nonce', self.SENTINEL), 'MalformedInput')
        self.refused(self.run_cli('prepare-successor'), 'MalformedInput')
        self.refused(self.run_cli('render-carrier', '--receipt', out/'request.json'), 'MalformedInput')
        # outputs may not alias inputs
        self.refused(self.run_cli('plan-successor', '--request', out/'request.json', '--native-facts', out/'facts.json', '--repository', self.fixture['repository'],
                                  '--template-output', out/'request.json', '--payload-output', out/'new-payload.json'), 'MalformedInput')
        if os.name != 'nt':
            link = out / 'link-facts.json'
            link.symlink_to(out / 'facts.json')
            self.refused(self.run_cli('plan-successor', '--request', out/'request.json', '--native-facts', link, '--repository', self.fixture['repository'],
                                      '--template-output', out/'t2.json', '--payload-output', out/'p2.json'), 'InputUnreadable')
            self.assertFalse((out / 't2.json').exists())


if __name__ == '__main__':
    if '--no-build' in sys.argv:
        sys.argv.remove('--no-build')
    else:
        SOURCE_REVISION = subprocess.run(['git','rev-parse','HEAD'],cwd=ROOT,capture_output=True,text=True,check=True).stdout.strip()
        subprocess.run(['dotnet','build',str(PROJECT),'--nologo','-m:1','-p:UseSharedCompilation=false','-p:SourceRevisionId='+SOURCE_REVISION],check=True)
    unittest.main()
