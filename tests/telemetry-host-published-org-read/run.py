#!/usr/bin/env python3
"""Offline causal controls: real verifier, fake transport, no live package or runtime."""
import copy
import importlib.util
import io
import json
from pathlib import Path
import socket
import sys
import tempfile
import time
from types import SimpleNamespace
import unittest
from unittest.mock import patch
import urllib.error
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "scripts"))
spec = importlib.util.spec_from_file_location("host_org_read", ROOT / "scripts/telemetry-host-published-org-read.py")
read = importlib.util.module_from_spec(spec)
spec.loader.exec_module(read)


def workflow_contract(text):
    required = ["default: coherent-0111-absence", "- coherent-0111-absence", "- host-080-org-artifact",
                "contents: read\n  packages: read", "cancel-in-progress: false", "timeout-minutes: 5",
                "persist-credentials: false", "python3 scripts/telemetry-host-published-org-read.py --check-selection",
                "python3 tests/release-successor-candidate/run.py", "python3 tests/telemetry-host-published-org-read/run.py",
                "if: inputs.observation == 'coherent-0111-absence' || inputs.observation == ''",
                "if: inputs.observation == 'host-080-org-artifact'",
                "if: inputs.observation == 'host-080-org-artifact' && steps.host-org.outcome == 'success'",
                "name: telemetry-host080-org-read-${{ github.sha }}-${{ github.run_id }}",
                "path: ${{ runner.temp }}/host080-org-read/", "if-no-files-found: error", "retention-days: 7"]
    for literal in required:
        if literal not in text:
            raise ValueError("workflow obligation: " + literal)
    if text.count("permissions:") != 1 or text.count("actions/upload-artifact@") != 1:
        raise ValueError("closed permissions/retention")
    for forbidden in ["contents: write", "packages: write", "id-token:", "secrets.", "environment:",
                      "continue-on-error", "always()", "release-successor-publish.py", "dotnet", "gh release", "git push"]:
        if forbidden in text:
            raise ValueError("workflow effect or bypass")
    default, host = text.split("      - name: Observe the published Host 0.8 org archive once", 1)
    if "upload-artifact" in default or "--version 0.111.0 --predecessor 0.110.0" not in default:
        raise ValueError("default coherent branch changed")
    if text.index("--check-selection") > text.index("python3 tests/"):
        raise ValueError("selection ordering")
    if text.index("tests/telemetry-host-published-org-read/run.py") > text.index("        id: host-org"):
        raise ValueError("offline controls must precede actual read")
    if host.index("--output") > host.index("actions/upload-artifact@"):
        raise ValueError("retention ordering")


class FakeResponse:
    def __init__(self, raw, *, length=True, status=200, hook=None):
        self.data = io.BytesIO(raw)
        self.headers = {"Content-Length": str(len(raw))} if length is True else ({} if length is None else {"Content-Length": str(length)})
        self.status, self.hook = status, hook
        self.socket = SimpleNamespace(settimeout=lambda _: None)
        self.fp = SimpleNamespace(raw=SimpleNamespace(_sock=self.socket))
        self.closed = False

    def __enter__(self):return self
    def __exit__(self, *args):self.closed = True
    def read1(self, count):
        if self.hook:self.hook()
        return self.data.read(count)


class TransportTests(unittest.TestCase):
    def setUp(self):
        self.no_network = patch.object(socket, "socket", side_effect=AssertionError("live network forbidden"))
        self.no_network.start(); self.addCleanup(self.no_network.stop)

    def response(self, response):
        opener = SimpleNamespace(open=lambda *args, **kwargs: response)
        return patch.object(read.urllib.request, "build_opener", return_value=opener)

    def test_exact_origin_headers_and_one_binary_attempt(self):
        calls = []
        def open_request(req, **kwargs):
            calls.append(req); return FakeResponse(b"abcd")
        with patch.object(read.urllib.request, "build_opener", return_value=SimpleNamespace(open=open_request)):
            r = read.Reader("fixture-token", time.monotonic()+100)
            self.assertEqual(r.get(read.ORG_URL, 4, binary=True), b"abcd")
            with self.assertRaisesRegex(read.Refused, "already-attempted"):r.get(read.ORG_URL, 4, binary=True)
            r.get(read.TAG_URL, 4)
        self.assertTrue(calls[0].get_header("Authorization").startswith("Basic "))
        self.assertEqual(calls[1].get_header("Authorization"), "Bearer fixture-token")
        self.assertNotIn("fixture-token", json.dumps(r.records))

    def test_http403_closes_original_and_never_retries(self):
        stream = io.BytesIO(b"denied")
        error = urllib.error.HTTPError(read.ORG_URL, 403, "private-detail", {}, stream)
        with patch.object(read.urllib.request, "build_opener", return_value=SimpleNamespace(open=lambda *a, **k: (_ for _ in ()).throw(error))):
            r = read.Reader("fixture-token", time.monotonic()+100)
            with self.assertRaisesRegex(read.Refused, "^HTTP-403$"):r.get(read.ORG_URL, 8, binary=True)
            with self.assertRaisesRegex(read.Refused, "already-attempted"):r.get(read.ORG_URL, 8, binary=True)
        self.assertTrue(stream.closed); self.assertEqual(len(r.records), 1)

    def test_size_status_length_socket_and_deadline_refusals(self):
        for response, cap in [(FakeResponse(b"12345", length=None),4), (FakeResponse(b"123", length=5),8),
                              (FakeResponse(b"123", length="-1"),8), (FakeResponse(b"123", status=204),8)]:
            with self.subTest(response=response), self.response(response), self.assertRaises(read.Refused):
                read.Reader("fixture",time.monotonic()+100).get(read.ORG_URL,cap,binary=True)
            self.assertTrue(response.closed)
        clock=[0.0];response=FakeResponse(b"123",hook=lambda:clock.__setitem__(0,13.0))
        with patch.object(read.time,"monotonic",side_effect=lambda:clock[0]),self.response(response),self.assertRaises(read.Refused):
            read.Reader("fixture",100).get(read.ORG_URL,8,binary=True)

    def test_closed_routes_missing_token_and_metadata_aggregate(self):
        with self.assertRaises(read.Refused):read.Reader("",time.monotonic()+100)
        r=read.Reader("fixture",time.monotonic()+100)
        with self.assertRaises(read.Refused):r.get("https://example.com/",8)
        with self.response(FakeResponse(b"x"*(1024**2+1))),self.assertRaisesRegex(read.Refused,"metadata-aggregate"):
            r.get(read.TAG_URL,2*1024**2)

    def test_redirects_strip_auth_and_remain_bounded(self):
        handler=read.redirects(time.monotonic()+100,True)
        req=urllib.request.Request(read.ORG_URL,headers={"Authorization":"Basic secret"})
        req.add_unredirected_header("Authorization","Basic secret")
        for _ in range(3):
            redirected=handler.redirect_request(req,None,302,"",{},"https://objects.example/package")
            self.assertFalse(any(k.lower()=="authorization" for k in [*redirected.headers,*redirected.unredirected_hdrs]))
        with self.assertRaises(read.Refused):handler.redirect_request(req,None,302,"",{},"https://objects.example/package")
        for url in ["http://objects.example/package","https://user:secret@objects.example/package"]:
            with self.assertRaises(read.Refused):read.redirects(time.monotonic()+100,True).redirect_request(req,None,302,"",{},url)
        with self.assertRaises(read.Refused):read.redirects(time.monotonic()+100,False).redirect_request(req,None,302,"",{},"https://objects.example/package")


class ObservationTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory();self.addCleanup(self.tmp.cleanup);self.root=Path(self.tmp.name)
        self.host=read.verifier();package=self.root/'fixture.nupkg'
        with zipfile.ZipFile(package,'w') as z:
            z.writestr('FS.GG.Telemetry.Host.nuspec','<package><metadata><id>FS.GG.Telemetry.Host</id><version>0.8.0</version></metadata></package>')
            z.writestr('tools/net10.0/linux-x64/FS.GG.Telemetry.Host.dll',b'fixture')
        (self.root/'lock.json').write_text('{}');(self.root/'assets').mkdir();(self.root/'assets/app.js').write_text('fixture')
        manifest=self.host.build_manifest(SimpleNamespace(package=package,lock=self.root/'lock.json',assets=self.root/'assets',version='0.8.0',tag=read.TAG,source_sha=read.SOURCE))
        manifest_raw=self.host.canonical(manifest)+b'\n';(self.root/'manifest.json').write_bytes(manifest_raw)
        observation=self.host.verify_artifact(self.root/'manifest.json',package)
        journal={'schema':'fsgg.telemetry-host-release-journal/v1','manifestSha256':'sha256:'+read.sha(self.host.canonical(manifest)),
                 'observations':{'github':observation,'nuget':observation}}
        journal_raw=json.dumps(journal).encode();assets={name:(read.ASSETS[name][0],len(raw),read.sha(raw)) for name,raw in [('manifest.json',manifest_raw),('publication-journal.json',journal_raw)]}
        patches=patch.multiple(read,MANIFEST_SHA=read.sha(manifest_raw),JOURNAL_SHA=read.sha(journal_raw),CONTENT=journal['manifestSha256'],PAYLOAD=manifest['producerPayloadSha256'],ASSETS=assets)
        patches.start();self.addCleanup(patches.stop)
        self.binary={read.API+'/releases/assets/'+str(assets['manifest.json'][0]):manifest_raw,
                     read.API+'/releases/assets/'+str(assets['publication-journal.json'][0]):journal_raw,read.ORG_URL:package.read_bytes()}
        self.release={'id':read.RELEASE_ID,'tag_name':read.TAG,'target_commitish':read.SOURCE,'draft':False,'prerelease':False,'immutable':True,'body':'telemetry-host-successor:'+read.CONTENT,'updated_at':'fixed',
                      'assets':[{'name':name,'id':ident,'size':size,'digest':'sha256:'+digest,'state':'uploaded','download_count':0} for name,(ident,size,digest) in assets.items()]+[{'name':read.PACKAGE,'id':3,'download_count':0}]}
        self.tag={'ref':'refs/tags/'+read.TAG,'object':{'type':'commit','sha':read.SOURCE}}
        self.calls=[];self.final_mutation=None;self.package_error=None
        test=self
        class FakeReader:
            records=[]
            def get(self,url,cap,**kwargs):
                test.calls.append(url)
                if url==read.TAG_URL:return json.dumps(test.tag).encode()
                if url==read.RELEASE_URL:
                    value=copy.deepcopy(test.release)
                    if test.calls.count(url)>1 and test.final_mutation:test.final_mutation(value)
                    return json.dumps(value).encode()
                if url==read.ORG_URL and test.package_error:raise test.package_error
                raw=test.binary[url];assert len(raw)<=cap;return raw
        self.reader=FakeReader();self.identity={'workflowSourceSha':'a'*40,'publishedSourceSha':read.SOURCE,'runId':123,'attempt':1,'actor':'EHotwagner','observation':read.CHOICES[1]}
        self.out=self.root/'out'
        no_network=patch.object(socket,'socket',side_effect=AssertionError('live network forbidden'));no_network.start();self.addCleanup(no_network.stop)

    def observe(self):return read.observe(self.reader,self.out,self.identity,self.host,time.monotonic()+100)

    def test_real_verifier_yields_exact_raw_evidence_for_already_occupied_version(self):
        result=self.observe();self.assertEqual(result['state'],'org-feed-bytes-verified');self.assertFalse(result['publicationAccepted'])
        self.assertEqual({x.name for x in self.out.iterdir()},read.FILES);self.assertEqual(self.calls.count(read.ORG_URL),1)
        self.assertEqual((self.out/read.PACKAGE).read_bytes(),self.binary[read.ORG_URL]);self.assertTrue(result['package']['producerPayloadEqual'])

    def test_org403_cannot_produce_success_evidence(self):
        self.package_error=read.Refused('HTTP-403')
        with self.assertRaisesRegex(read.Refused,'HTTP-403'):self.observe()
        self.assertEqual(self.calls.count(read.ORG_URL),1);self.assertFalse((self.out/'observation.json').exists())

    def test_wrong_source_or_assets_prevent_org_get(self):
        for mutation in [lambda:self.tag['object'].update(sha='b'*40),lambda:self.release.update(id=1),lambda:self.release['assets'][0].update(digest='sha256:wrong')]:
            saved=(copy.deepcopy(self.tag),copy.deepcopy(self.release));mutation()
            with self.assertRaises(read.Refused):self.observe()
            self.assertNotIn(read.ORG_URL,self.calls);self.tag,self.release=saved
            self.out.rmdir()

    def test_tampered_manifest_and_package_refused(self):
        key=next(k for k in self.binary if k.endswith(str(read.ASSETS['manifest.json'][0])))
        original=self.binary[key];self.binary[key]=original.replace(b'net10.0',b'net11.0')
        with self.assertRaisesRegex(read.Refused,'asset-bytes'):self.observe()
        self.assertNotIn(read.ORG_URL,self.calls)
        self.out=self.root/'out2';self.binary[key]=original;self.binary[read.ORG_URL]+=b'archive-change'
        with self.assertRaisesRegex(read.Refused,'journal-payload'):self.observe()
        self.assertFalse((self.out/'observation.json').exists())

    def test_observational_counter_only_may_change(self):
        self.final_mutation=lambda r:r['assets'][0].update(download_count=1)
        self.observe()
        self.out=self.root/'out2';self.calls=[];self.final_mutation=lambda r:r.update(updated_at='changed')
        with self.assertRaisesRegex(read.Refused,'release-drift'):self.observe()

    def test_existing_output_and_zip_paths_refused(self):
        self.out.mkdir()
        with self.assertRaisesRegex(read.Refused,'fresh-output'):self.observe()
        bad=self.root/'bad.zip'
        with zipfile.ZipFile(bad,'w') as z:z.writestr('../escape',b'x')
        with self.assertRaises(read.Refused):read.zipguard(bad)


class ContextAndWorkflowTests(unittest.TestCase):
    def test_context_separates_current_workflow_from_published_source(self):
        env={'OBSERVATION':read.CHOICES[1],'GITHUB_REPOSITORY':'FS-GG/.github','GITHUB_EVENT_NAME':'workflow_dispatch',
             'GITHUB_REF':'refs/heads/main','GITHUB_RUN_ATTEMPT':'1','GITHUB_ACTOR':'EHotwagner','GITHUB_TRIGGERING_ACTOR':'EHotwagner','GITHUB_SHA':'a'*40,'GITHUB_RUN_ID':'123'}
        self.assertEqual(read.context(env,'a'*40)['publishedSourceSha'],read.SOURCE)
        for key in env:
            with self.subTest(key=key),self.assertRaises(read.Refused):read.context({**env,key:'wrong'},'a'*40)
        for value in ['',None,'other']:
            with self.assertRaises(read.Refused):read.select(value)

    def test_strict_metadata_and_source_pin(self):
        for raw in [b'{"a":1,"a":2}',b'{"a":NaN}']:
            with self.assertRaises(read.Refused):read.strict(raw)
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);(root/'scripts').mkdir()
            for name in read.PINS:(root/'scripts'/name).write_bytes((ROOT/'scripts'/name).read_bytes())
            (root/'scripts/release-saga.py').write_text('changed')
            with self.assertRaisesRegex(read.Refused,'verifier-source'):read.verifier(root)

    def test_actual_workflow_and_causal_mutations(self):
        text=(ROOT/'.github/workflows/release-successor-feed-readiness.yml').read_text();workflow_contract(text)
        replacements=[('packages: read','packages: write'),('default: coherent-0111-absence','default: host-080-org-artifact'),
                      ('--check-selection','--unchecked'),(" && steps.host-org.outcome == 'success'",''),
                      ('if-no-files-found: error','if-no-files-found: warn'),('tests/telemetry-host-published-org-read/run.py','other.py'),
                      ("if: inputs.observation == 'host-080-org-artifact'\n",'if: always()\n')]
        for old,new in replacements:
            with self.subTest(old=old),self.assertRaises(ValueError):workflow_contract(text.replace(old,new))


if __name__=='__main__':unittest.main(verbosity=2)
