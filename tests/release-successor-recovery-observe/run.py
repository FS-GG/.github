#!/usr/bin/env python3
import contextlib
import io
import json
import pathlib
import tempfile
import types
import unittest
import urllib.error

SOURCE = pathlib.Path(__file__).resolve().parents[2] / 'scripts/release-successor-recovery-observe.py'
m = types.ModuleType('closed_observer'); m.__file__ = str(SOURCE)
exec(compile(SOURCE.read_bytes(), str(SOURCE), 'exec'), m.__dict__)
ENV = {'GITHUB_REPOSITORY':m.REPOSITORY,'GITHUB_EVENT_NAME':'workflow_dispatch',
       'GITHUB_REF':'refs/heads/main','GITHUB_ACTOR':'EHotwagner','GITHUB_RUN_ATTEMPT':'1',
       'GITHUB_SHA':'a'*40,'GITHUB_RUN_ID':'42','APP_ID':'4882140',
       'EXPECTED_INSTALLATION_ID':'88','ACTUAL_INSTALLATION_ID':'88',
       'ACTIONS_TOKEN':'actions-secret-sentinel','LEDGER_TOKEN':'ledger-secret-sentinel'}
prior = {'id':1,'tag_name':m.PREDECESSOR}
target = {'id':2,'node_id':'R_2','tag_name':m.TAG,'draft':True,'prerelease':False,
          'target_commitish':m.SOURCE,'body':m.MARKER,'created_at':'2026-10-10T06:00:00Z',
          'updated_at':'2026-10-10T06:01:00Z'}
rate = {'resources':{'core':{'limit':5000,'remaining':99,'used':4901,'reset':123}}}
scope = {'total_count':1,'repositories':[{'full_name':m.AUTHORITY}]}
prepath = f'repos/{m.REPOSITORY}/releases/tags/{m.PREDECESSOR}'
listpath = f'repos/{m.REPOSITORY}/releases?per_page=100&page=1'

class Response:
    def __init__(self,url,value,status=200,headers=None):
        self.url,self.code,self.headers=url,status,headers or {}
        self.timeouts=[]
        self.fp=types.SimpleNamespace(raw=types.SimpleNamespace(_sock=types.SimpleNamespace(settimeout=self.timeouts.append)))
        self.raw = io.BytesIO(value if isinstance(value,bytes) else json.dumps(value).encode())
    def geturl(self): return self.url
    def read1(self,n): return self.raw.read(n)
    def __enter__(self): return self
    def __exit__(self,*args): pass

class FakeHTTP:
    def __init__(self):
        self.calls=[];self.values={prepath:prior,listpath:[prior,target],
                                  f'repos/{m.REPOSITORY}/releases/2':target,
                                  'installation/repositories?per_page=100':scope,'rate_limit':rate}
        self.headers={};self.status={};self.error={};self.url={}
    def open(self,req,timeout):
        path=req.full_url.removeprefix(m.API)
        assert req.method=='GET' and 0 < timeout <=12
        credential=req.get_header('Authorization')
        assert credential in ('Bearer '+ENV['ACTIONS_TOKEN'],'Bearer '+ENV['LEDGER_TOKEN'])
        if path.startswith('repos/'):assert credential=='Bearer '+ENV['ACTIONS_TOKEN']
        if path.startswith('installation/'):assert credential=='Bearer '+ENV['LEDGER_TOKEN']
        self.calls.append((path,credential))
        if path in self.error:raise self.error[path]
        return Response(self.url.get(path,req.full_url),self.values[path],self.status.get(path,200),self.headers.get(path))

class Cases(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.addCleanup(self.temp.cleanup)
        self.sequence=0;self.fake=FakeHTTP();self.reader=m.Reader({'actions':ENV['ACTIONS_TOKEN'],'ledger':ENV['LEDGER_TOKEN']},pathlib.Path(self.temp.name)/'raw',self.fake)
    def fresh_reader(self):
        self.sequence+=1
        self.reader=m.Reader({'actions':ENV['ACTIONS_TOKEN'],'ledger':ENV['LEDGER_TOKEN']},pathlib.Path(self.temp.name)/f'raw-{self.sequence}',self.fake)
    def test_context_guard_before_work(self):
        m.guard(ENV)
        for key,bad in (('GITHUB_REPOSITORY','elsewhere/x'),('GITHUB_ACTOR','other'),('GITHUB_RUN_ATTEMPT','2'),('GITHUB_REF','refs/heads/topic'),('APP_ID','5064713'),('ACTUAL_INSTALLATION_ID','99')):
            with self.subTest(key=key),self.assertRaises(m.Refused):m.guard({**ENV,key:bad})
        self.assertEqual(self.fake.calls,[])
    def test_positive_distinct_credentials_and_scoped_summary(self):
        result=m.observe(self.reader,ENV)
        self.assertIsNone(result['firstCause']);self.assertEqual(result['requests'],8)
        self.assertEqual(result['outcomes']['draft']['value']['id'],2)
        self.assertTrue(result['outcomes']['draft']['value']['bodyEqualsMarker'])
        self.assertNotIn('secret-sentinel',json.dumps(result))
        self.assertTrue(all(p.stat().st_mode & 0o777 == 0o600 for p in self.reader.rawdir.iterdir()))
    def test_large_complete_page_without_unrelated_body_artifact(self):
        self.fake.values[listpath]=[prior,target]+[{'id':i,'body':'private-unrelated-'+'x'*14000} for i in range(3,100)]
        result=m.observe(self.reader,ENV)
        self.assertIsNone(result['firstCause']);self.assertGreater(self.reader.bytes,1024*1024)
        self.assertNotIn('private-unrelated',json.dumps(result))
    def test_complete_multiple_pages(self):
        second=f'repos/{m.REPOSITORY}/releases?per_page=100&page=2'
        self.fake.values[listpath]=[prior];self.fake.values[second]=[target]
        self.fake.headers[listpath]={'link':f'<{m.API+second}>; rel="next"'}
        result=m.observe(self.reader,ENV);self.assertIsNone(result['firstCause']);self.assertEqual(result['requests'],9)
    def test_empty_missing_predecessor_duplicate_and_mismatched_binding(self):
        for rows in ([],[target],[prior,target,target],[prior,{**target,'body':'private-unexpected-body'}],
                     [prior,{**target,'target_commitish':'b'*40}]):
            self.fake.values[listpath]=rows
            self.fake.values[f'repos/{m.REPOSITORY}/releases/2']=rows[-1] if rows else target
            self.fresh_reader()
            result=m.observe(self.reader,ENV)
            self.assertEqual(result['outcomes']['draft']['status'],'unknown')
            self.assertEqual(result['outcomes']['actionsFinalRate']['status'],'observed')
            self.assertNotIn('private-unexpected-body',json.dumps(result))
            self.reader.calls=0
    def test_mismatched_native_detail_id(self):
        self.fake.values[f'repos/{m.REPOSITORY}/releases/2']={**target,'id':3}
        self.assertEqual(m.observe(self.reader,ENV)['outcomes']['draft']['cause'],'draft-detail-identity-refused')
    def test_contains_marker_does_not_claim_exact_body(self):
        altered={**target,'body':'private-prefix '+m.MARKER}
        self.fake.values[listpath]=[prior,altered];self.fake.values[f'repos/{m.REPOSITORY}/releases/2']=altered
        result=m.observe(self.reader,ENV)
        self.assertFalse(result['outcomes']['draft']['value']['bodyEqualsMarker'])
        self.assertNotIn('private-prefix',json.dumps(result))
    def test_403_is_retained_and_other_credential_still_observed(self):
        self.fake.status[prepath]=403;self.fake.values[prepath]={'message':'API rate limit exceeded'}
        self.fake.headers[prepath]={'x-ratelimit-resource':'core','x-ratelimit-remaining':'0','x-ratelimit-reset':'123'}
        result=m.observe(self.reader,ENV)
        self.assertEqual(result['firstCause'],'HTTP-status-403')
        self.assertEqual(result['outcomes']['ledgerFinalRate']['status'],'observed')
        self.assertEqual(result['outcomes']['actionsFinalRate']['status'],'observed')
        evidence=next(x for x in result['requestsEvidence'] if x['status']==403)
        self.assertEqual(evidence['headers']['x-ratelimit-remaining'],'0')
    def test_transport_error_masks_credentials_and_preserves_first_cause(self):
        self.fake.error[prepath]=RuntimeError('signedURL='+ENV['ACTIONS_TOKEN'])
        self.fake.error['rate_limit']=RuntimeError(ENV['LEDGER_TOKEN'])
        result=m.observe(self.reader,ENV)
        self.assertEqual(result['firstCause'],'RuntimeError');self.assertNotIn('secret-sentinel',json.dumps(result))
    def test_allowlist_and_redirect_refusal(self):
        for credential,path in [('ledger',prepath),('actions','installation/repositories?per_page=100'),('actions','https://evil.invalid'),('actions',f'repos/{m.REPOSITORY}/releases?per_page=100&page=11')]:
            with self.assertRaises(m.Refused):self.reader.get(credential,path)
        self.assertEqual(self.fake.calls,[])
        with self.assertRaises(m.Refused):m.NoRedirect().redirect_request(None,None,302,'',{},'https://evil.invalid')
        self.fake.url[prepath]='https://evil.invalid'
        with self.assertRaisesRegex(m.Refused,'response-origin'):self.reader.get('actions',prepath)
    def test_pagination_refusals_and_ten_page_cap(self):
        for link in ('bad', '<https://evil.invalid>; rel="next"'):
            self.fake.headers[listpath]={'link':link}
            self.fresh_reader()
            self.assertEqual(m.observe(self.reader,ENV)['outcomes']['draft']['status'],'unknown')
        for page in range(1,11):
            path=f'repos/{m.REPOSITORY}/releases?per_page=100&page={page}'
            self.fake.values[path]=[{'id':page+100}]
            self.fake.headers[path]={'link':f'<{m.API}repos/{m.REPOSITORY}/releases?per_page=100&page={page+1}>; rel="next"'}
        self.fresh_reader()
        result=m.observe(self.reader,ENV);self.assertEqual(result['outcomes']['draft']['cause'],'release-page-bound');self.assertLessEqual(result['requests'],17)
    def test_response_request_total_byte_and_deadline_caps(self):
        self.fake.values[prepath]=b'x'*(2*1024*1024+1)
        with self.assertRaisesRegex(m.Refused,'response-byte'):self.reader.get('actions',prepath)
        self.assertFalse(self.reader.evidence[-1]['complete'])
        self.reader.calls=17
        with self.assertRaises(m.Refused):self.reader.get('ledger','rate_limit')
        self.reader.calls=0;self.reader.bytes=24*1024*1024
        with self.assertRaisesRegex(m.Refused,'response-byte'):self.reader.get('ledger','rate_limit')
        self.reader.deadline=0
        with self.assertRaises(m.Refused):self.reader.get('ledger','rate_limit')
    def test_empty_followup_page_is_unknown(self):
        second=f'repos/{m.REPOSITORY}/releases?per_page=100&page=2'
        self.fake.headers[listpath]={'link':f'<{m.API+second}>; rel="next"'}
        self.fake.values[second]=[]
        result=m.observe(self.reader,ENV)
        self.assertEqual(result['outcomes']['draft']['status'],'unknown')
        self.assertEqual(result['outcomes']['actionsFinalRate']['status'],'observed')
    def test_stream_deadline_stops_without_retry(self):
        now=[0];self.reader.clock=lambda:now[0];self.reader.deadline=180
        original=self.fake.open
        def slow(req,timeout):
            response=original(req,timeout)
            read=response.read1
            def delayed(n):
                chunk=read(n);now[0]+=13;return chunk
            response.read1=delayed;return response
        self.fake.open=slow
        with self.assertRaisesRegex(m.Refused,'request-deadline-bound'):self.reader.get('ledger','rate_limit')
        self.assertEqual(len(self.fake.calls),1)
    def test_fixture_has_actual_selector_supported_invocation(self):
        workflow=SOURCE.parents[1]/'.github/workflows/release-successor-recovery-observe.yml'
        self.assertIn('run: python3 tests/release-successor-recovery-observe/run.py',workflow.read_text())
    def test_unvisited_last_page_and_offhost_prev_are_unknown(self):
        for link in (f'<{m.API}repos/{m.REPOSITORY}/releases?per_page=100&page=2>; rel="last"',
                     '<https://evil.invalid>; rel="prev"'):
            self.fresh_reader();self.fake.headers[listpath]={'link':link}
            self.assertEqual(m.observe(self.reader,ENV)['outcomes']['draft']['status'],'unknown')
    def test_late_EOF_and_partial_disconnect_retain_native_evidence(self):
        for mode in ('late-eof','disconnect'):
            self.fresh_reader();now=[0];self.reader.clock=lambda:now[0];self.reader.deadline=180
            original=FakeHTTP.open.__get__(self.fake)
            def interrupted(req,timeout):
                response=original(req,timeout);read=response.read1;calls=[0]
                def part(n):
                    calls[0]+=1
                    if calls[0]>1:
                        if mode=='disconnect':raise OSError('private-secret-sentinel')
                        now[0]=13;return b''
                    return read(5)
                response.read1=part;return response
            self.fake.open=interrupted
            with self.assertRaises((m.Refused,OSError)):self.reader.get('ledger','rate_limit')
            evidence=self.reader.evidence[-1]
            self.assertFalse(evidence['complete']);self.assertEqual(evidence['status'],200)
            self.assertEqual(evidence['bytes'],5);self.assertEqual(evidence['retainedBytes'],5)
            self.assertEqual(len(self.fake.calls)>0,True)
    def test_failed_scope_prevents_draft_but_retains_rates(self):
        self.fake.values['installation/repositories?per_page=100']={'total_count':2,'repositories':[]}
        result=m.observe(self.reader,ENV)
        self.assertEqual(result['outcomes']['draft']['cause'],'scope-prerequisite-refused')
        self.assertFalse(any(p.startswith('repos/') for p,_ in self.fake.calls))
    def test_report_failure_retains_collection_primary(self):
        report=pathlib.Path(self.temp.name)/'report';report.write_text('existing')
        output=io.StringIO()
        with contextlib.redirect_stdout(output),self.assertRaises(m.Refused):m.write_report(report,{'firstCause':'HTTP-status-403'})
        self.assertIn('HTTP-status-403',output.getvalue());self.assertEqual(report.read_text(),'existing')

if __name__=='__main__':unittest.main()
