#!/usr/bin/env python3
"""Measure an installed local dashboard on an assessor-qualified store."""
from __future__ import annotations
import argparse,hashlib,http.cookiejar,importlib.util,json,math,os,pathlib,secrets,selectors,signal,statistics,subprocess,tempfile,time,urllib.request,zipfile

SCHEMA="fsgg.telemetry.standalone-qualification/2"
LIMITS={"startupP95Milliseconds":2000,"idleRssPeakBytes":100*1024*1024,"coldCliSubmissionP95Milliseconds":1000,"warmInProcessAdmissionP95Milliseconds":100}

def digest(path:pathlib.Path)->str:
 h=hashlib.sha256()
 with path.open('rb') as stream:
  for block in iter(lambda:stream.read(1024*1024),b''): h.update(block)
 return h.hexdigest()

def p95(values:list[float])->float:
 if not values: raise ValueError('samples are empty')
 return sorted(values)[math.ceil(.95*len(values))-1]

def distribution(values:list[float])->dict:
 if not values: raise ValueError('samples are empty')
 return {"medianMilliseconds":round(statistics.median(values),3),"p95Milliseconds":round(p95(values),3),"maximumMilliseconds":round(max(values),3)}

def verify_phase_diagnostics(data:dict,assembly:pathlib.Path,package:pathlib.Path|None=None,required:bool=False)->None:
 diagnostics=data.get('diagnostics')
 if diagnostics is None:
  if required: raise ValueError('Store phase diagnostics are missing')
  return # Historical evidence remains readable; it does not acquire diagnostics retroactively.
 if not isinstance(diagnostics,dict) or diagnostics.get('schema')!='fsgg.telemetry.store-phase-diagnostics/1': raise ValueError('Store phase diagnostics schema differs')
 sampling=[diagnostics.get(key) for key in ['warmupPairs','firstSample','sampleCount']]
 if any(type(value) is not int for value in sampling) or sampling!=[5,0,100]: raise ValueError('Store phase diagnostics sampling differs')
 frequency=diagnostics.get('timestampFrequency')
 if type(frequency) is not int or frequency<=0: raise ValueError('Store phase diagnostics clock differs')
 rows=diagnostics.get('admissions')
 if not isinstance(rows,list) or len(rows)!=100: raise ValueError('Store phase diagnostics count differs')
 allowed={'after-inbox-directory-sync','before-file-sync','after-file-sync','after-rename','after-directory-sync','index-committed'}
 required_phases=['before-file-sync','after-file-sync','after-rename','after-directory-sync','index-committed']
 previous_end=-1
 for index,row in enumerate(rows):
  if not isinstance(row,dict) or type(row.get('index')) is not int or row['index']!=index: raise ValueError('Store phase diagnostics sample order differs')
  start,end=row.get('startTimestamp'),row.get('endTimestamp')
  if type(start) is not int or type(end) is not int or start<0 or start<previous_end or end<start: raise ValueError('Store phase diagnostics timestamps differ')
  previous_end=end
  milliseconds=row.get('elapsedMilliseconds'); cpu=row.get('processCpuMilliseconds')
  if any(isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value) or value<0 for value in [milliseconds,cpu]): raise ValueError('Store phase diagnostics timings are invalid')
  if milliseconds!=data['samples']['warmInProcessAdmissionMilliseconds'][index] or abs(milliseconds-round((end-start)*1000/frequency,3))>0.001: raise ValueError('Store phase diagnostics wall time differs')
  gc=row.get('gcCollections')
  if not isinstance(gc,list) or len(gc)!=3 or any(type(count) is not int or count<0 for count in gc): raise ValueError('Store phase diagnostics GC counts differ')
  if row.get('phasesTruncated') is not False: raise ValueError('Store phase diagnostics are truncated')
  phases=row.get('phases')
  if not isinstance(phases,list) or len(phases)>256: raise ValueError('Store phase diagnostics phases are invalid')
  names=[]; previous=start
  for phase in phases:
   if not isinstance(phase,dict) or phase.get('name') not in allowed or type(phase.get('timestamp')) is not int or not previous<=phase['timestamp']<=end: raise ValueError('Store phase diagnostics phase order differs')
   previous=phase['timestamp']; names.append(phase['name'])
  if names not in [required_phases,['after-inbox-directory-sync']+required_phases]: raise ValueError('Store phase diagnostics durability boundaries differ')
 context=diagnostics.get('context')
 if not isinstance(context,dict): raise ValueError('Store phase diagnostics context is missing')
 context_names={'runtimeVersion','runtimeAssemblySha256','fsiHostVersion','fsiHostSha256','framework','processArchitecture','osArchitecture','processorCount','cpuModel','cpuQuota','operatingSystem','isServerGC','gcLatencyMode','observedEnvironment','storeAssemblySha256','coreAssemblySha256','nativeSqliteSha256'}
 if set(context)!=context_names: raise ValueError('Store phase diagnostics context allowlist differs')
 for name in ['runtimeVersion','fsiHostVersion','framework','processArchitecture','osArchitecture','cpuModel','cpuQuota','operatingSystem','gcLatencyMode']:
  value=context[name]
  if value is not None and (not isinstance(value,str) or not value or len(value)>512): raise ValueError('Store phase diagnostics runtime context differs')
 if type(context['processorCount']) is not int or context['processorCount']<=0 or type(context['isServerGC']) is not bool: raise ValueError('Store phase diagnostics runtime context differs')
 for name in ['runtimeAssemblySha256','fsiHostSha256']:
  value=context[name]
  if value is not None and (not isinstance(value,str) or len(value)!=64 or any(char not in '0123456789abcdef' for char in value)): raise ValueError('Store phase diagnostics runtime identity differs')
 settings=context.get('observedEnvironment')
 setting_names={prefix+name for prefix in ['DOTNET_','COMPlus_'] for name in ['TieredCompilation','TieredPGO','ReadyToRun','gcServer','gcConcurrent']}
 if not isinstance(settings,dict) or set(settings)!=setting_names or any(value not in [None,'0','1'] for value in settings.values()): raise ValueError('Store phase diagnostics configuration differs')
 identities={'storeAssemblySha256':assembly,'coreAssemblySha256':assembly.with_name('FS.GG.Coord.Core.dll')}
 for key,binary in identities.items():
  value=context.get(key)
  if value is not None and (not binary.is_file() or value!=digest(binary)): raise ValueError('Store phase diagnostics assembly differs')
 if context.get('storeAssemblySha256')!=data['storeAssemblySha256']: raise ValueError('Store phase diagnostics Store binding differs')
 native=context.get('nativeSqliteSha256')
 if native is not None and (not isinstance(native,str) or len(native)!=64 or any(char not in '0123456789abcdef' for char in native)): raise ValueError('Store phase diagnostics native identity differs')
 if package is not None:
  try:
   with zipfile.ZipFile(package) as archive:
    for key,name in [('storeAssemblySha256','FS.GG.Telemetry.Store.dll'),('coreAssemblySha256','FS.GG.Coord.Core.dll'),('nativeSqliteSha256','libe_sqlite3.so')]:
     value=context.get(key)
     if value is None: continue # Missing observations stay unknown.
     entries=[entry for entry in archive.namelist() if pathlib.PurePosixPath(entry).name==name]
     if not entries or value not in [hashlib.sha256(archive.read(entry)).hexdigest() for entry in entries]: raise ValueError('Store phase diagnostics package payload differs')
  except zipfile.BadZipFile as error: raise ValueError('Store phase diagnostics package archive is invalid') from error

def verify_store_probe(path:pathlib.Path,assembly:pathlib.Path,package:pathlib.Path|None=None,require_diagnostics:bool=False)->dict:
 if path.stat().st_size>1024*1024: raise ValueError('packaged Store performance evidence is oversized')
 data=json.loads(path.read_text())
 if data.get('schema')!='fsgg.telemetry.packaged-store-performance/1': raise ValueError('packaged Store performance evidence schema differs')
 if data.get('storeAssemblySha256')!=digest(assembly): raise ValueError('packaged Store performance evidence assembly differs')
 if data.get('assessment')!='approved-local-durable': raise ValueError('packaged Store performance evidence is not assessor-qualified')
 samples=data.get('samples',{})
 if not isinstance(samples,dict): raise ValueError('packaged Store performance samples are invalid')
 for name in ['warmInProcessAdmissionMilliseconds','applicationDrainMilliseconds','accumulatingPendingAdmissionMilliseconds']:
  values=samples.get(name)
  if not isinstance(values,list) or len(values)!=100: raise ValueError('packaged Store performance sample count differs')
  if any(isinstance(value,bool) or not isinstance(value,(int,float)) or not math.isfinite(value) or value<0 for value in values): raise ValueError('packaged Store performance samples are invalid')
 limits=data.get('limits'); summary=data.get('summary')
 if not isinstance(limits,dict) or limits.get('warmInProcessAdmissionP95Milliseconds')!=LIMITS['warmInProcessAdmissionP95Milliseconds']: raise ValueError('packaged Store performance limit differs')
 if not isinstance(summary,dict) or not isinstance(summary.get('warmInProcessAdmission'),dict): raise ValueError('packaged Store performance summary is invalid')
 measured=round(p95(samples['warmInProcessAdmissionMilliseconds']),3)
 reported=summary['warmInProcessAdmission'].get('p95Milliseconds')
 if reported!=measured or data.get('qualified') != (measured<=LIMITS['warmInProcessAdmissionP95Milliseconds']): raise ValueError('packaged Store performance verdict contradicts samples')
 verify_phase_diagnostics(data,assembly,package,require_diagnostics)
 return data

def verify_manifest(path:pathlib.Path|None,package:pathlib.Path,source_sha:str)->dict:
 if path is None:
  return {"contentId":None,"sourceSha":source_sha,"packageSha256":digest(package),"version":None,"sourceBinding":"prepared-for-release-manifest"}
 data=json.loads(path.read_text()); descriptor=data.get('descriptor')
 canonical=json.dumps(descriptor,sort_keys=True,separators=(',',':')).encode()
 if data.get('schema')!='fsgg.release-saga/1' or data.get('contentId')!='sha256:'+hashlib.sha256(canonical).hexdigest(): raise ValueError('release manifest binding is invalid')
 if descriptor.get('sourceSha')!=source_sha: raise ValueError('release manifest source differs')
 rows=[row for row in descriptor.get('packages',[]) if row.get('id')=='FS.GG.Coord.Cli']
 if len(rows)!=1: raise ValueError('release manifest package differs')
 artifact=rows[0].get('artifact',{})
 external_sha=digest(package); relationship='exact-archive'
 if artifact.get('sha256')!=external_sha:
  tool=pathlib.Path(__file__).resolve().parents[2]/'scripts'/'release-saga.py'
  spec=importlib.util.spec_from_file_location('fsgg_release_saga',tool); release=importlib.util.module_from_spec(spec); spec.loader.exec_module(release)
  if release.payload_id(package)!=artifact.get('payloadSha256'): raise ValueError('release manifest package payload differs')
  relationship='repository-signed-normalized-payload'
 return {"contentId":data['contentId'],"sourceSha":source_sha,"packageSha256":external_sha,"preparedArchiveSha256":artifact.get('sha256'),"payloadSha256":artifact.get('payloadSha256'),"relationship":relationship,"version":rows[0]['version'],"sourceBinding":"verified-release-manifest"}

def verify_public_readback(path:pathlib.Path,binding:dict)->dict:
 data=json.loads(path.read_text())
 expected={
  'acquisition':'nuget.org-public-readback',
  'packageId':'FS.GG.Coord.Cli',
  'version':binding.get('version'),
  'sourceSha':binding.get('sourceSha'),
  'preparedArchiveSha256':binding.get('preparedArchiveSha256'),
  'externalArchiveSha256':binding.get('packageSha256'),
  'payloadSha256':binding.get('payloadSha256'),
 }
 if any(data.get(key)!=value for key,value in expected.items()): raise ValueError('public readback evidence differs')
 return data

def batch(index:int,run_id:str="qualification")->bytes:
 filler='x'*(61*1024)
 event={"kind":"item","identity":f"{run_id}-{index:03d}-"+filler,"itemId":"standalone-qualification","revision":index}
 result=json.dumps({"schema":"fsgg.telemetry."+"ingest/1","ingestId":f"{run_id}-{index:03d}","sourceIdentity":"standalone-qualification","generation":run_id,"cursor":str(index),"eventCount":1,"events":[event]},sort_keys=True,separators=(',',':')).encode()
 if len(result)<60*1024: raise ValueError('generated batch is not representative of 64 KiB admission')
 return result

def start(engine:pathlib.Path,config:pathlib.Path,repository:str,timeout:float=5.0):
 started=time.monotonic_ns()
 process=subprocess.Popen([str(engine),'telemetry','dashboard','serve','--config',str(config),'--repository',repository,'--no-open'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True,start_new_session=True)
 try:
  selector=selectors.DefaultSelector(); selector.register(process.stdout,selectors.EVENT_READ)
  events=selector.select(timeout)
  if not events: raise ValueError('dashboard ready URL timed out')
  url=process.stdout.readline().strip()
  if not url.startswith('http://127.0.0.1:') or '/bootstrap/' not in url: raise ValueError('dashboard emitted an invalid bootstrap URL')
  jar=http.cookiejar.CookieJar(); opener=urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
  with opener.open(url,timeout=2) as response:
   if response.status!=200: raise ValueError('bootstrap did not reach the dashboard')
  origin=url.split('/bootstrap/',1)[0]
  request=urllib.request.Request(origin+'/api/session',data=b'',method='POST',headers={'Origin':origin})
  with opener.open(request,timeout=2) as response:
   session=json.load(response)
  if session.get('schema')!='fsgg.telemetry.browser-session/1': raise ValueError('dashboard session response is invalid')
  workspaces=session.get('workspaces')
  if not isinstance(workspaces,list) or len(workspaces)!=1: raise ValueError('dashboard session workspace scope is invalid')
  workspace=workspaces[0]
  body=json.dumps({'workspaceId':workspace},separators=(',',':')).encode()
  snapshot_request=urllib.request.Request(origin+'/api/snapshot',data=body,method='POST',headers={'Origin':origin,'Content-Type':'application/json'})
  with opener.open(snapshot_request,timeout=2) as response:
   snapshot=json.load(response)
  if snapshot.get('schema')!='fsgg.telemetry.private-dashboard/1' or snapshot.get('workspaceId')!=workspace: raise ValueError('dashboard readiness snapshot is invalid')
  elapsed=(time.monotonic_ns()-started)/1_000_000
  return process,elapsed,origin
 except BaseException:
  if process.poll() is None:
   os.killpg(process.pid,signal.SIGKILL)
   process.wait()
  raise

def stop(process:subprocess.Popen)->None:
 os.killpg(process.pid,signal.SIGTERM)
 try: process.wait(timeout=5)
 except subprocess.TimeoutExpired: process.kill(); raise ValueError('dashboard did not stop within five seconds')
 if process.returncode!=0: raise ValueError(f'dashboard exited {process.returncode}')

def rss_bytes(pid:int)->int:
 for line in pathlib.Path(f'/proc/{pid}/status').read_text().splitlines():
  if line.startswith('VmRSS:'): return int(line.split()[1])*1024
 raise ValueError('Linux RSS is unavailable')

def run(args)->dict:
 binding=verify_manifest(args.manifest,args.package,args.source_sha)
 store_probe=verify_store_probe(args.store_probe_evidence,args.store_assembly,args.package,require_diagnostics=True)
 if args.public_release:
  verify_public_readback(args.public_readback_evidence,binding)
 status=subprocess.run([str(args.cli_path),'telemetry','dashboard','status','--config',str(args.config),'--repository',args.repository],capture_output=True,text=True,timeout=10)
 if status.returncode!=0 or json.loads(status.stdout).get('status')!='ready': raise ValueError('dashboard/store is not assessor-qualified and ready')
 starts=[]
 for _ in range(args.starts):
  process,elapsed,_=start(args.cli_path,args.config,args.repository); starts.append(round(elapsed,3)); stop(process)
 process,_,_=start(args.cli_path,args.config,args.repository)
 try:
  time.sleep(args.idle_seconds)
  rss=[]
  for _ in range(args.rss_samples): rss.append(rss_bytes(process.pid)); time.sleep(.2)
 finally: stop(process)
 submissions=[]; drains=[]; sizes=[]; run_id='q-'+secrets.token_hex(8)
 with tempfile.TemporaryDirectory(prefix='fsgg-telemetry-submit-') as root:
  def submit(index:int,series:str,timed:bool):
   payload=batch(index,series); path=pathlib.Path(root)/f'{series}-{index:03d}.json'; path.write_bytes(payload)
   begun=time.monotonic_ns()
   result=subprocess.run([str(args.cli_path),'telemetry','workspace','submit','--config',str(args.config),'--repository',args.repository,'--input',str(path)],stdout=subprocess.DEVNULL,stderr=subprocess.PIPE,timeout=5)
   elapsed=(time.monotonic_ns()-begun)/1_000_000
   if result.returncode!=0: raise ValueError(f'submission {series}-{index} failed')
   drain_begun=time.monotonic_ns()
   drained=subprocess.run([str(args.cli_path),'telemetry','workspace','drain','--config',str(args.config),'--repository',args.repository],stdout=subprocess.DEVNULL,stderr=subprocess.PIPE,timeout=5)
   drain_elapsed=(time.monotonic_ns()-drain_begun)/1_000_000
   if drained.returncode!=0: raise ValueError(f'drain after submission {series}-{index} failed')
   if timed: submissions.append(round(elapsed,3)); drains.append(round(drain_elapsed,3)); sizes.append(len(payload))
  for index in range(args.warmup_submissions): submit(index,run_id+'-warmup',False)
  for index in range(args.submissions):
   submit(index,run_id,True)
 startup_summary=distribution(starts); submission_summary=distribution(submissions); drain_summary=distribution(drains); rss_peak=max(rss)
 checks={"startupP95Milliseconds":startup_summary['p95Milliseconds']<=LIMITS['startupP95Milliseconds'],"idleRssPeakBytes":rss_peak<=LIMITS['idleRssPeakBytes'],"coldCliSubmissionP95Milliseconds":submission_summary['p95Milliseconds']<=LIMITS['coldCliSubmissionP95Milliseconds'],"warmInProcessAdmissionP95Milliseconds":store_probe.get('qualified') is True}
 return {"schema":SCHEMA,"qualified":all(checks.values()),"binding":binding,"filesystem":json.loads(args.filesystem_evidence.read_text()),"packagedStore":store_probe,"samples":{"startupMilliseconds":starts,"idleRssBytes":rss,"coldCliSubmissionMilliseconds":submissions,"applicationDrainMilliseconds":drains,"submissionBytes":sizes},"summary":{"startup":startup_summary,"idleRssPeakBytes":rss_peak,"coldCliSubmission":submission_summary,"applicationDrain":drain_summary},"limits":LIMITS,"checks":checks,"claims":{"processCrashAndFilesystemApi":True,"physicalPowerLoss":False,"mainInstalled":False,"publicRelease":args.public_release}}

def main()->int:
 p=argparse.ArgumentParser(); p.add_argument('--cli-path',type=pathlib.Path,required=True); p.add_argument('--config',type=pathlib.Path,required=True); p.add_argument('--repository',required=True); p.add_argument('--package',type=pathlib.Path,required=True); p.add_argument('--manifest',type=pathlib.Path); p.add_argument('--source-sha',required=True); p.add_argument('--filesystem-evidence',type=pathlib.Path,required=True); p.add_argument('--store-probe-evidence',type=pathlib.Path,required=True); p.add_argument('--store-assembly',type=pathlib.Path,required=True); p.add_argument('--public-readback-evidence',type=pathlib.Path); p.add_argument('--output',type=pathlib.Path,required=True); p.add_argument('--starts',type=int,default=20); p.add_argument('--submissions',type=int,default=100); p.add_argument('--warmup-submissions',type=int,default=5); p.add_argument('--rss-samples',type=int,default=10); p.add_argument('--idle-seconds',type=float,default=2); p.add_argument('--public-release',action='store_true'); args=p.parse_args()
 try:
  if len(args.source_sha)!=40 or any(c not in '0123456789abcdef' for c in args.source_sha): raise ValueError('exact lowercase source SHA is required')
  if args.public_release and (args.manifest is None or args.public_readback_evidence is None): raise ValueError('public release qualification requires verified manifest and public-readback evidence')
  if args.starts<20 or args.submissions<100 or args.warmup_submissions<5 or args.rss_samples<5: raise ValueError('qualification sample floors are not met')
  result=run(args); args.output.write_text(json.dumps(result,sort_keys=True,separators=(',',':'))+'\n'); print(json.dumps({'qualified':result['qualified'],'summary':result['summary']},sort_keys=True,separators=(',',':'))); return 0 if result['qualified'] else 1
 except (OSError,ValueError,subprocess.SubprocessError,json.JSONDecodeError) as error: print(f'standalone-telemetry-dashboard: {error}',file=os.sys.stderr); return 2
if __name__=='__main__': raise SystemExit(main())
