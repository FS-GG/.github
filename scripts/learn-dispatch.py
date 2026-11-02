#!/usr/bin/env python3
"""Prepare LEARN-02 dispatch comment and existing-schema telemetry batch, with offline preparation by default.
Input is a frozen public issue snapshot; outputs belong in private scratch until reviewed.
"""
import argparse
import hashlib
import json
import re
import subprocess
from datetime import datetime
from pathlib import Path

RUBRIC = 'learn-02-rubric-v1'
POLICY = 'learn-02-routing-v1'
# Match highest level first. A zero means no textual signal, never proved absence of risk.
RULES = {
 'D1': [r'cross.repo|several repos|contract boundar', r'public (?:api|contract)|consumer|template|package', r'files|module|script'],
 'D2': [r'\badr\b|research|new design', r'design|algorithm|investigat', r'implement|extend|add|fix'],
 'D3': [r'concurren|crash|atomic|lifecycle|fenc', r'invariant|cancel|async|state machine', r'pipeline|replay|ordering'],
 'D4': [r'credential|delet|publish|upgrade|overwrite|retained.upgrade', r'release|migration|data loss', r'write|replac|mutat'],
 'D5': [r'fable|lockstep|fixed.point|determinism', r'deterministic|numeric|floating.point|replay', r'algorithm|bench|latency'],
 'D6': [r'three backends|all three backends|browser.matrix|svg.*pixi.*babylon', r'browser|pixel|accessibility|render', r'\bui\b|frontend|visual'],
 'D7': [r'atomic|all.or.nothing|public contract|hostile|crash', r'conformance|acceptance|compatib|preserv|regression', r'tests|coverage|criteria'],
 'D8': [r'installed|live|release|publish|adopted|bench', r'integration|end.to.end|browser|conformance', r'test|build|check'],
}

def canonical(x):
 return (json.dumps(x, sort_keys=True, separators=(',', ':'), ensure_ascii=False)+'\n').encode()

def classify(snapshot):
 for key in ('itemId','body','capturedAt','sourceRevision','dispatchBoundary','producer'):
  if not isinstance(snapshot.get(key),str) or not snapshot[key].strip():
   raise ValueError('missing '+key)
 if not re.fullmatch(r'[\w.-]+/[\w.-]+#[1-9][0-9]*',snapshot['itemId']):
  raise ValueError('itemId must be owner/repository#issue')
 unknown=snapshot.get('unknownDimensions',[])
 if not isinstance(unknown,list) or any(x not in RULES for x in unknown):
  raise ValueError('unknownDimensions must name rubric dimensions')
 text=snapshot['body']; scores={}; evidence={}
 for d,patterns in RULES.items():
  matches=[(3-i,re.search(p,text,re.I|re.S)) for i,p in enumerate(patterns)]
  hit=next(((score,m.group()) for score,m in matches if m), (0,None))
  scores[d]=None if d in unknown else hit[0]; evidence[d]=hit[1]
 total=None if None in scores.values() else sum(scores.values())
 safety=scores['D4'] is None or scores['D4']>=2
 if safety or total is None or total>=13 or scores['D3']>=2 or scores['D5']>=2:
  route=dict(tier='best',effort='high',criticTier='best',reviewDepth='source-and-delta')
 elif total>=7:
  route=dict(tier='normal',effort='high',criticTier='best',reviewDepth='source-and-delta')
 else:
  route=dict(tier='normal',effort='medium',criticTier='normal',reviewDepth='single-source-review')
 chosen=snapshot.get('chosenProfile',route)
 if set(chosen)!=set(route) or chosen['tier'] not in ('normal','best') or chosen['criticTier'] not in ('normal','best') or chosen['effort'] not in ('medium','high') or chosen['reviewDepth'] not in ('source-and-delta','single-source-review'):
  raise ValueError('invalid chosenProfile')
 if chosen!=route and not snapshot.get('overrideReason'):
  raise ValueError('overrideReason required')
 if safety and (chosen['tier']!='best' or chosen['criticTier']!='best' or chosen['effort']!='high'):
  raise ValueError('safety floor cannot be lowered')
 propensity=snapshot.get('propensity',1.0)
 if isinstance(propensity,bool) or not isinstance(propensity,(int,float)) or not 0<propensity<=1:
  raise ValueError('propensity must be in (0,1]')
 if propensity!=1: raise ValueError('v1 deterministic policy has propensity 1; exploration is not enabled')
 return dict(schema='fsgg.learn-dispatch/1',itemId=snapshot['itemId'],rubricVersion=RUBRIC,policyVersion=POLICY,
  capturedAt=snapshot['capturedAt'],sourceRevision=snapshot['sourceRevision'],dispatchBoundary=snapshot['dispatchBoundary'],
  producer=snapshot['producer'],inputDigest=hashlib.sha256(text.encode()).hexdigest(),provenance='rule',
  scores=scores,evidence=evidence,hardness=total,safetyFloor=safety,recommendedProfile=route,chosenProfile=chosen,
  overrideReason=snapshot.get('overrideReason'),propensity=propensity,runtime=snapshot.get('runtime'),
  model=snapshot.get('model'),reviewActor='existing owner or authorized reviewer; no automatic critic dispatch')

def prepare(snapshot,out):
 for key in ('runtime','model'):
  if not snapshot.get(key): raise ValueError(key+' is required for an actual dispatch')
 stamp=datetime.fromisoformat(snapshot['capturedAt'].replace('Z','+00:00'))
 if stamp.utcoffset() is None or stamp.utcoffset().total_seconds()!=0: raise ValueError('capturedAt must be UTC')
 record=classify(snapshot); digest=hashlib.sha256(canonical(record)).hexdigest()
 identity='learn-02-'+digest
 event=dict(kind='learn-task-snapshot',identity=identity,itemId=record['itemId'],revision=1,snapshotId=identity,
  rubricVersion=RUBRIC,snapshotDigest=digest,capturedAt=record['capturedAt'])
 batch=dict(schema='fsgg.telemetry.ingest/1',ingestId=identity,sourceIdentity=record['producer'],generation=identity,
  cursor='1',eventCount=1,events=[event])
 out.mkdir(parents=True,exist_ok=True)
 # Exclusive writes retain original intent; an interrupted prepare must be inspected, never overwritten.
 for name,data in [('snapshot.json',canonical(record)),('observation.json',canonical(batch)),
  ('comment.md',('<!-- fsgg:learn-dispatch/v1 snapshot='+digest+' -->\n```json\n'+canonical(record).decode()+'```\n').encode())]:
  with (out/name).open('xb') as f: f.write(data)
 print(json.dumps(dict(snapshotDigest=digest,files=3,effects=0)))

def record(out,args):
 # Consume exact retained intent. A partial failure is retried with these same bytes.
 snapshot=json.loads((out/'snapshot.json').read_text()); digest=hashlib.sha256(canonical(snapshot)).hexdigest()
 batch=json.loads((out/'observation.json').read_text())
 if batch['events'][0]['snapshotDigest']!=digest or batch['events'][0]['itemId']!=snapshot['itemId']:
  raise ValueError('snapshot/observation mismatch')
 expected='<!-- fsgg:learn-dispatch/v1 snapshot='+digest+' -->\n```json\n'+canonical(snapshot).decode()+'```\n'
 if (out/'comment.md').read_text()!=expected: raise ValueError('comment mismatch')
 repo,number=snapshot['itemId'].split('#')
 current=json.loads(subprocess.check_output(['gh','issue','view',number,'--repo',repo,'--json','body'],text=True))
 if hashlib.sha256(current['body'].encode()).hexdigest()!=snapshot['inputDigest']:
  raise ValueError('issue body changed; prepare a new pre-dispatch snapshot')
 comments=json.loads(subprocess.check_output(['gh','api','--paginate','--slurp','repos/'+repo+'/issues/'+number+'/comments'],text=True))
 matches=[c for page in comments for c in page if c['body']==expected]
 if len(matches)>1: raise ValueError('duplicate exact dispatch comments require owner reconciliation')
 if not matches:
  subprocess.run(['gh','issue','comment',number,'--repo',repo,'--body-file',str(out/'comment.md')],check=True)
 receipt=subprocess.check_output([args.engine,'telemetry','workspace','submit','--config',args.config,
  '--repository',repo,'--producer',args.producer,'--binding-digest',args.binding_digest,'--input',str(out/'observation.json')],text=True)
 # Preserve receipts beside private intent. Submission errors retain the comment and intent for retry.
 with (out/'receipt.json').open('a') as f: f.write(receipt+'\n')
 print('recorded comment and telemetry submission; verify receipt acceptance before dispatch')

if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--input',type=Path);parser.add_argument('--output',type=Path,required=True)
 parser.add_argument('--record',action='store_true',help='explicit live comment and telemetry effects; needs separate authority')
 parser.add_argument('--engine',default='fsgg-coord-engine');parser.add_argument('--config');parser.add_argument('--producer');parser.add_argument('--binding-digest')
 args=parser.parse_args()
 try:
  if args.record:
   if not all((args.config,args.producer,args.binding_digest)):raise ValueError('record requires config, producer, binding-digest')
   record(args.output,args)
  else:
   if not args.input:raise ValueError('prepare requires input')
   prepare(json.loads(args.input.read_text()),args.output)
 except (ValueError,OSError,subprocess.CalledProcessError) as error: parser.exit(2,str(error)+'\n')
