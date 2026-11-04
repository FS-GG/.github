"""Synthetic protocol/file-boundary controls; no model, network, native or real-case calls."""
import copy
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest import mock
ROOT=Path(__file__).resolve().parents[2]
spec=importlib.util.spec_from_file_location('rater',ROOT/'scripts/learn-context-rater.py');r=importlib.util.module_from_spec(spec);spec.loader.exec_module(r)

def make_bundle(root):
    root.mkdir();(root/'source').mkdir();task=b'Add a small parser test.';data=b'let parse text = text\n'
    (root/'task.md').write_bytes(task);(root/'source/parser.fs').write_bytes(data)
    manifest={'schema':'fsgg.learn-context-snapshot/1','caseId':'context-001','boundaryKind':'item-creation',
      'checkpointKind':'proven-before-cutoff','checkpointLagSeconds':10,'exactHistoricalHead':False,
      'taskSha256':r.sha(task),'taskRedactionVersion':'github-identity-links/1',
      'files':[{'path':'parser.fs','mode':'100644','blob':hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest(),'bytes':len(data),'sha256':r.sha(data),'contentKind':'utf8'}],
      'contextGaps':[{'kind':'creator-private-context-unavailable'}],'identityBlinding':'partial-source-identifiability',
      'ratingLaunchAllowed':False,'isolation':'not enforced by collector'}
    (root/'manifest.json').write_bytes(r.canonical(manifest));seal=r.sha(r.canonical(manifest));plan={'schema':'fsgg.context-file-plan/1','manifestSha256':seal,'treatment':'frozen-context','files':['parser.fs']}
    return manifest,seal,plan

def rating():
    return {'schema':'fsgg.semantic-hardness/1','item_id':'context-001','scope_clarity':'clear',
      'demand':{'category':'minimal','confidence':'low','basis':'Synthetic protocol control only.','evidence':[{'line_start':1,'line_end':1,'quote':'small parser test'}]},
      'work_volume':{'category':'small','basis':'Synthetic control.'},'facets':{x:'low' for x in ['decomposition','coupling','reasoning','novelty','validation']},
      'safety':{'consequence':'low','basis':'Synthetic control.'},'conditions':{'external_wait':'none_declared','missing_context':[]},
      'worked_time':{'reference':'reference-agent-work/1','p10_minutes':None,'p50_minutes':None,'p90_minutes':None,'confidence':'low'}}

class RunnerTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name);self.bundle=self.root/'rater';self.manifest,self.seal,self.plan=make_bundle(self.bundle)
    def payload(self):return r.prepare(self.bundle,self.seal,self.plan,b'Frozen canonical rubric.')
    def test_frozen_payload_and_issue_only_pair(self):
        payload=self.payload();self.assertEqual(payload['source'][0]['text'],'let parse text = text\n');self.assertFalse(payload['evaluationEligible'])
        plan={**self.plan,'treatment':'issue-only','files':[]};issue=r.prepare(self.bundle,self.seal,plan,b'Frozen canonical rubric.')
        self.assertEqual(issue['taskPacket'],payload['taskPacket']);self.assertEqual(issue['rubric'],payload['rubric']);self.assertEqual(issue['source'],[])
    def test_later_controller_files_cannot_change_allowed_payload(self):
        before=r.canonical(self.payload());(self.root/'controller').mkdir();(self.root/'controller/final-diff.json').write_text('Future fix and duration 999')
        (self.bundle/'unlisted-future.txt').write_text('future outcome');self.assertEqual(before,r.canonical(self.payload()))
    def test_wrong_manifest_and_modified_task_refused(self):
        with self.assertRaises(r.Refused):r.prepare(self.bundle,'0'*64,self.plan,b'rubric')
        (self.bundle/'task.md').write_text('later task')
        with self.assertRaisesRegex(r.Refused,'task-drift'):self.payload()
    def test_blob_or_byte_length_drift_refused(self):
        (self.bundle/'source/parser.fs').write_bytes(b'future solution')
        with self.assertRaisesRegex(r.Refused,'source-drift'):self.payload()
    def test_traversal_git_and_absolute_paths_refused(self):
        for path in ['../controller/final-diff.json','/etc/passwd','a/../b','.git/objects/x','a\\b','a//b']:
            with self.subTest(path=path),self.assertRaises(r.Refused):r.relpath(path)
    def test_leaf_symlink_and_hardlink_refused(self):
        f=self.bundle/'source/parser.fs';data=f.read_bytes();f.unlink();outside=self.root/'future';outside.write_bytes(data);f.symlink_to(outside)
        with self.assertRaises(r.Refused):self.payload()
        f.unlink();os.link(outside,f)
        with self.assertRaisesRegex(r.Refused,'unlinked'):self.payload()
    def test_intermediate_symlink_and_bundle_symlink_refused(self):
        folder=self.root/'external';folder.mkdir();(folder/'x').write_text('secret');(self.bundle/'link').symlink_to(folder)
        with self.assertRaises(r.Refused):r.read_at(self.bundle,'link/x',100)
        alias=self.root/'alias';alias.symlink_to(self.bundle)
        with self.assertRaises(r.Refused):r.prepare(alias,self.seal,self.plan,b'rubric')
    def test_file_plan_unknown_binary_duplicate_and_history_refused(self):
        for files in [['unknown.fs'],['parser.fs','parser.fs']]:
            with self.subTest(files=files),self.assertRaises(r.Refused):r.prepare(self.bundle,self.seal,{**self.plan,'files':files},b'rubric')
        payload=self.payload();payload['history']='prior-conversation'
        with self.assertRaises(r.Refused):r.request(payload)
    def test_selected_binary_is_refused_even_with_valid_frozen_hashes(self):
        data=b'\xff\x00';(self.bundle/'source/parser.fs').write_bytes(data)
        entry=self.manifest['files'][0];entry.update(bytes=len(data),sha256=r.sha(data),blob=hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest(),contentKind='binary')
        raw=r.canonical(self.manifest);(self.bundle/'manifest.json').write_bytes(raw);seal=r.sha(raw)
        with self.assertRaisesRegex(r.Refused,'selected-binary'):
            r.prepare(self.bundle,seal,{**self.plan,'manifestSha256':seal},b'rubric')
    def test_gap_payloads_cannot_smuggle_arbitrary_controller_text(self):
        for gap in [{'kind':'creator-private-context-unavailable','path':'../controller'},
                    {'kind':'unresolved-gitlink','object':'future done in 5 minutes'},
                    {'kind':'creator-private-context-unavailable','detail':'future outcome'}]:
            payload=self.payload();payload['contextGaps']=[gap]
            with self.subTest(gap=gap),self.assertRaises(r.Refused):r.request(payload)
    def test_payload_budget_refuses_without_truncation(self):
        with self.assertRaisesRegex(r.Refused,'payload-bound'):r.prepare(self.bundle,self.seal,self.plan,b'x'*r.MAX_PAYLOAD)
    def test_duplicate_and_nonfinite_json_refused(self):
        for data in [b'{"a":1,"a":2}',b'{"a":NaN}',b'{"a":Infinity}']:
            with self.assertRaises(r.Refused):r.decode(data)
    def test_fresh_request_has_no_tools_history_or_workspace(self):
        request=r.request(self.payload());self.assertEqual(request['tools'],[]);self.assertEqual(request['toolChoice'],'none');self.assertTrue(request['freshConversation']);self.assertIsNone(request['previousConversation']);self.assertNotIn('cwd',request)
    def test_actual_wire_embedded_tools_and_hidden_context_refuse(self):
        payload=self.payload()
        schema=r.decode((ROOT/'contracts/learn/semantic-hardness-v1.schema.json').read_bytes())
        wire={'model':r.MODEL,'reasoning':{'effort':r.EFFORT},'tools':[],'tool_choice':'none','store':False,
              'input':[{'role':'user','content':[{'type':'input_text','text':r.canonical(payload).decode()}]}],
              'text':{'format':{'type':'json_schema','name':'semantic_hardness','strict':True,'schema':schema}}}
        self.assertTrue(r.inspect_provider_request(wire,payload)['contextExact'])
        for change in ['embedded-tools','hidden-context','auto','resume','extra-instructions','wrong-schema']:
            value=copy.deepcopy(wire)
            if change=='embedded-tools': value['input'].insert(0,{'type':'additional_tools','role':'developer','tools':[{'type':'namespace','name':'functions','tools':[{'type':'custom','name':'exec'}]}]})
            elif change=='hidden-context':value['input'].insert(0,{'role':'developer','content':'Future workspace context'})
            elif change=='auto':value['tool_choice']='auto'
            elif change=='resume':value['previous_response_id']='prior-response'
            elif change=='extra-instructions':value['instructions']='Implicit instructions'
            else:value['text']['format']['strict']=False
            with self.subTest(change=change),self.assertRaises(r.Refused):r.inspect_provider_request(value,payload)
    def test_every_tool_control_frame_denied_before_dispatch(self):
        with mock.patch.object(subprocess,'run',side_effect=AssertionError('must not execute')):
            for kind in ['tool_call','function_call','exec','read_file','web_search','mcp','resume','shell']:
                with self.subTest(kind=kind),self.assertRaisesRegex(r.Refused,'denied'):r.broker_frame({'type':kind,'value':{'path':'/controller'}})
    def test_self_asserted_effective_model_and_bad_evidence_rejected(self):
        bad=rating();bad['actual_model']='gpt-6.1-sol'
        with self.assertRaises(ValueError):r.quarantine(self.payload(),{'type':'final','value':bad})
        bad=rating();bad['demand']['evidence'][0]['quote']='future closing comment'
        with self.assertRaises(ValueError):r.quarantine(self.payload(),{'type':'final','value':bad})
    def test_valid_synthetic_result_stays_quarantined_actual_unknown(self):
        out=r.quarantine(self.payload(),{'type':'final','value':rating()});self.assertFalse(out['evaluationEligible']);self.assertFalse(out['isolationQualified']);self.assertEqual(out['effectiveProfile'],{'model':None,'effort':None,'evidence':None})
    def test_declared_capability_cannot_self_authorize_launch(self):
        with mock.patch.object(subprocess,'run',side_effect=AssertionError('no model process')):
            with self.assertRaisesRegex(r.Refused,'no qualified'):r.launch(self.payload(),{'isolationQualified':True,'effectiveToolSet':[]})
    def test_payload_nested_mutation_and_request_extra_keys_rejected(self):
        payload=self.payload();payload['source'][0]['text']='changed'
        with self.assertRaises(r.Refused):r.request(payload)
        payload=self.payload();payload['taskPacket']['outcomes']='leaked'
        with self.assertRaises(r.Refused):r.request(payload)
    def fifo_cli_refusal(self, argv):
        # subprocess.run kills and reaps the child if the no-writer FIFO hangs.
        # A blocking-open mutant must fail this control, not hang the suite.
        done=subprocess.run([sys.executable,str(ROOT/'scripts/learn-context-rater.py'),*argv],
                            input=b'{}',capture_output=True,timeout=2)
        self.assertEqual(done.returncode,3,done.stderr)
        self.assertIn(b'regular-unlinked-input-required',done.stderr)
    def test_prepare_no_writer_fifos_refuse_before_read(self):
        for target in ['manifest.json','task.md','source/parser.fs','file-plan.json']:
            with self.subTest(target=target),tempfile.TemporaryDirectory() as scratch:
                root=Path(scratch);bundle=root/'rater';_,seal,plan=make_bundle(bundle)
                file_plan=bundle/'file-plan.json';file_plan.write_bytes(r.canonical(plan))
                leaf=bundle/target;leaf.unlink();os.mkfifo(leaf)
                self.fifo_cli_refusal(['prepare','--bundle',str(bundle),'--manifest-sha256',seal,
                                      '--file-plan',str(file_plan),'--output',str(root/'out.json')])
    def test_protocol_and_launch_no_writer_payload_fifos_refuse(self):
        file=self.root/'payload.fifo';os.mkfifo(file)
        for command in ['protocol','launch']:
            with self.subTest(command=command):
                self.fifo_cli_refusal([command,'--payload',str(file),'--payload-sha256','0'*64])
    def test_inspect_wire_no_writer_payload_and_request_fifos_refuse(self):
        payload=self.root/'payload.json';raw=r.canonical(self.payload());payload.write_bytes(raw)
        request=self.root/'request.json';request.write_text('{}')
        for target in [payload,request]:
            with self.subTest(target=target.name):
                original=target.read_bytes();target.unlink();os.mkfifo(target)
                try:self.fifo_cli_refusal(['inspect-wire','--payload',str(payload),
                                         '--payload-sha256',r.sha(raw),'--request',str(request)])
                finally:target.unlink();target.write_bytes(original)
    def test_two_fresh_protocol_processes_and_cli_launch_refusal(self):
        file=self.root/'payload.json';raw=r.canonical(self.payload());file.write_bytes(raw);seal=r.sha(raw);argv=[sys.executable,str(ROOT/'scripts/learn-context-rater.py'),'protocol','--payload',str(file),'--payload-sha256',seal];pids=[]
        for _ in range(2):
            proc=subprocess.Popen(argv,stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE);pids.append(proc.pid);out,err=proc.communicate(r.canonical({'type':'final','value':rating()}),timeout=10);self.assertEqual(proc.returncode,0,err);self.assertFalse(json.loads(out)['evaluationEligible'])
        self.assertNotEqual(*pids)
        done=subprocess.run([sys.executable,str(ROOT/'scripts/learn-context-rater.py'),'launch','--payload',str(file),'--payload-sha256',seal],capture_output=True,timeout=10);self.assertEqual(done.returncode,3);self.assertIn(b'no qualified',done.stderr)
        file.write_bytes(raw+b' ')
        bad=subprocess.run(argv,input=b'{}',capture_output=True,timeout=10);self.assertEqual(bad.returncode,3);self.assertIn(b'sealed-payload-drift',bad.stderr)

if __name__=='__main__':unittest.main()
