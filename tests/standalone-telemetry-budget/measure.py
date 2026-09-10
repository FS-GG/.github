#!/usr/bin/env python3
"""Measure standalone CLI package size, installed closure, and cold tool restore."""
from __future__ import annotations
import argparse, hashlib, json, math, os, pathlib, platform, shutil, statistics, subprocess, tempfile, time, zipfile
from xml.etree import ElementTree

SCHEMA="fsgg.telemetry.standalone-package-budget/1"
MIB=1024*1024
LIMITS={"compressedDeltaBytes":10*MIB,"installedDeltaBytes":30*MIB,"coldRestoreP95Milliseconds":30_000}
FORBIDDEN_PREFIXES=("FS.GG.Telemetry.Host.","Akka.","Akka.FSharp.")

def sha256(path:pathlib.Path)->str:
    h=hashlib.sha256()
    with path.open('rb') as stream:
        for chunk in iter(lambda:stream.read(1024*1024),b''): h.update(chunk)
    return h.hexdigest()

def package_identity(path:pathlib.Path)->tuple[str,str,list[str]]:
    if not path.is_file() or path.stat().st_size<=0: raise ValueError("package is missing or empty")
    try:
        with zipfile.ZipFile(path) as archive:
            names=sorted(item.filename for item in archive.infolist() if not item.is_dir())
            nuspec=[name for name in names if name.endswith('.nuspec') and '/' not in name]
            if len(nuspec)!=1: raise ValueError("package must contain one root nuspec")
            root=ElementTree.fromstring(archive.read(nuspec[0]))
            def one(name:str)->str:
                values=[(node.text or '').strip() for node in root.iter() if node.tag.rsplit('}',1)[-1]==name]
                if len(values)!=1 or not values[0]: raise ValueError(f"package nuspec has invalid {name}")
                return values[0]
            return one('id'),one('version'),names
    except (zipfile.BadZipFile,ElementTree.ParseError,KeyError) as error: raise ValueError("package is not a valid nupkg") from error

def percentile95(values:list[float])->float:
    if not values: raise ValueError("samples are empty")
    return sorted(values)[max(0,math.ceil(.95*len(values))-1)]

def validate_candidate_provenance(actual:str,declared:str,mode:str,source_sha:str|None)->None:
    if declared!=actual: raise ValueError("candidate archive digest does not match the declared provenance")
    if mode=='qualification' and (source_sha is None or len(source_sha)!=40 or any(c not in '0123456789abcdef' for c in source_sha)):
        raise ValueError("qualification requires an exact lowercase source SHA")

def forbidden_names(names:list[str])->list[str]:
    return [name for name in names if pathlib.PurePosixPath(name).name.startswith(FORBIDDEN_PREFIXES)]

def tree_measure(path:pathlib.Path)->tuple[int,int]:
    files=[p for p in path.rglob('*') if p.is_file() and not p.is_symlink()]
    return sum(p.stat().st_size for p in files),len(files)

def safe_remove(path:pathlib.Path,root:pathlib.Path)->None:
    resolved=path.resolve(); owner=root.resolve()
    if resolved==owner or owner not in resolved.parents: raise ValueError("cleanup escaped the generated measurement root")
    shutil.rmtree(resolved)

def config(path:pathlib.Path)->None:
    path.write_text('<?xml version="1.0" encoding="utf-8"?><configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3"/></packageSources></configuration>\n',encoding='utf-8')

def install(package:pathlib.Path,identity:str,version:str,root:pathlib.Path,index:int)->dict:
    sample=root/f"sample-{index:02d}"; tools=sample/'tools'; cache=sample/'packages'; cfg=sample/'NuGet.Config'
    sample.mkdir(mode=0o700); config(cfg)
    env=os.environ.copy(); env['NUGET_PACKAGES']=str(cache); env['DOTNET_CLI_HOME']=str(sample/'dotnet-home'); env['NUGET_HTTP_CACHE_PATH']=str(sample/'http-cache')
    command=['dotnet','tool','install',identity,'--version',version,'--tool-path',str(tools),'--configfile',str(cfg),'--add-source',str(package.parent),'--no-cache']
    started=time.monotonic_ns()
    try:
        result=subprocess.run(command,env=env,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL,timeout=30)
        elapsed=(time.monotonic_ns()-started)/1_000_000
        row={"index":index,"elapsedMilliseconds":round(elapsed,3),"succeeded":result.returncode==0,"exitCode":result.returncode}
    except subprocess.TimeoutExpired:
        elapsed=(time.monotonic_ns()-started)/1_000_000
        row={"index":index,"elapsedMilliseconds":round(elapsed,3),"succeeded":False,"exitCode":None,"failure":"tool-install-timeout"}
    if not row['succeeded']:
        row.setdefault('failure','tool-install-failed')
    else:
        size,count=tree_measure(tools); row.update(installedBytes=size,installedFiles=count)
        forbidden=forbidden_names([str(p.relative_to(tools)) for p in tools.rglob('*') if p.is_file()])
        runtime_metadata='\n'.join(p.read_text(errors='replace') for p in tools.rglob('*.json'))
        if forbidden or 'Microsoft.AspNetCore.App' in runtime_metadata:
            row.update(succeeded=False,failure='forbidden-runtime-closure')
    safe_remove(sample,root)
    return row

def measure(args:argparse.Namespace)->dict:
    candidate=args.candidate.resolve(); baseline=args.baseline.resolve(); provenance=json.loads(args.baseline_evidence.read_text())
    bid,bver,bnames=package_identity(baseline); cid,cver,cnames=package_identity(candidate)
    if (bid,cid)!=("FS.GG.Coord.Cli","FS.GG.Coord.Cli"): raise ValueError("unexpected package identity")
    if provenance.get('package')!=bid or provenance.get('version')!=bver or provenance.get('packageSha256')!=sha256(baseline): raise ValueError("baseline provenance does not bind the input package")
    candidate_sha=sha256(candidate); baseline_sha=sha256(baseline)
    validate_candidate_provenance(candidate_sha,args.candidate_sha256,args.mode,args.candidate_source_sha)
    if candidate_sha==baseline_sha: raise ValueError("candidate and baseline packages are identical")
    forbidden_entries=forbidden_names(cnames)
    if forbidden_entries: raise ValueError("candidate package contains forbidden Host/Akka closure")
    if args.mode=='qualification' and args.samples<20: raise ValueError("qualification requires at least 20 clean samples")
    if args.mode=='smoke' and not 1<=args.samples<=3: raise ValueError("smoke mode permits one to three samples")
    owner=pathlib.Path(tempfile.mkdtemp(prefix='fsgg-telemetry-budget-',dir=args.work_root))
    owner.chmod(0o700)
    try:
        baseline_row=install(baseline,bid,bver,owner,-1); candidate_row=install(candidate,cid,cver,owner,0)
        rows=[install(candidate,cid,cver,owner,index+1) for index in range(args.samples)]
    finally: shutil.rmtree(owner)
    elapsed=[row['elapsedMilliseconds'] for row in rows if row['succeeded']]
    failures=[row for row in [baseline_row,candidate_row,*rows] if not row['succeeded']]
    compressed=candidate.stat().st_size-baseline.stat().st_size
    installed=(candidate_row.get('installedBytes',0)-baseline_row.get('installedBytes',0)) if not failures else None
    p95=round(percentile95(elapsed),3) if len(elapsed)==len(rows) else None
    summary={"compressedDeltaBytes":compressed,"installedDeltaBytes":installed,"coldRestoreMedianMilliseconds":round(statistics.median(elapsed),3) if elapsed else None,"coldRestoreP95Milliseconds":p95}
    checks={key:(summary[key] is not None and summary[key]<=limit) for key,limit in LIMITS.items()}
    return {"schema":SCHEMA,"mode":args.mode,"qualified":args.mode=='qualification' and len(rows)>=20 and not failures and all(checks.values()),"package":{"id":cid,"version":cver,"sha256":candidate_sha,"bytes":candidate.stat().st_size,"sourceSha":args.candidate_source_sha,"label":args.candidate_label},"baseline":{"id":bid,"version":bver,"sha256":baseline_sha,"bytes":baseline.stat().st_size,"provenance":provenance},"closure":{"forbiddenEntries":forbidden_entries,"candidateZipFiles":len(cnames),"baselineZipFiles":len(bnames)},"samples":rows,"referenceInstalls":{"baseline":baseline_row,"candidate":candidate_row},"summary":summary,"limits":LIMITS,"checks":checks,"failures":failures,"environment":{"os":platform.system(),"architecture":platform.machine(),"python":platform.python_version(),"dotnetSdk":subprocess.check_output(['dotnet','--version'],text=True).strip(),"cachePolicy":"fresh generated NUGET_PACKAGES, HTTP cache, CLI home, and tool path per sample; --no-cache","sourcePolicy":"NuGet config contains only nuget.org; exact prepared artifact directory is an explicit additional source"}}

def main()->int:
    p=argparse.ArgumentParser(); p.add_argument('--candidate',type=pathlib.Path,required=True); p.add_argument('--candidate-sha256',required=True); p.add_argument('--candidate-label',required=True); p.add_argument('--candidate-source-sha'); p.add_argument('--baseline',type=pathlib.Path,required=True); p.add_argument('--baseline-evidence',type=pathlib.Path,required=True); p.add_argument('--output',type=pathlib.Path,required=True); p.add_argument('--mode',choices=('smoke','qualification'),default='qualification'); p.add_argument('--samples',type=int,default=20); p.add_argument('--work-root',type=pathlib.Path,default=pathlib.Path(tempfile.gettempdir())); args=p.parse_args()
    try:
        result=measure(args); args.output.parent.mkdir(parents=True,exist_ok=True); args.output.write_text(json.dumps(result,sort_keys=True,separators=(',',':'))+'\n'); print(json.dumps({"qualified":result['qualified'],"summary":result['summary'],"failures":len(result['failures'])},sort_keys=True,separators=(',',':'))); return 0 if not result['failures'] and all(result['checks'].values()) else 1
    except (OSError,ValueError,subprocess.SubprocessError) as error: print(f"standalone-telemetry-budget: {error}",file=os.sys.stderr); return 2
if __name__=='__main__': raise SystemExit(main())
