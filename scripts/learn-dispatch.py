#!/usr/bin/env python3
"""Prepare LEARN-02 dispatch comment and existing-schema telemetry batch, with offline preparation by default.
Input is a frozen public issue snapshot; outputs belong in private scratch until reviewed.
"""
import argparse
import fcntl
import os
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
 if safety and (chosen['tier']!='best' or chosen['criticTier']!='best' or chosen['effort']!='high' or chosen['reviewDepth']!='source-and-delta'):
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

def batch_for(snapshot):
    digest = hashlib.sha256(canonical(snapshot)).hexdigest()
    identity = 'learn-02-' + digest
    event = dict(kind='learn-task-snapshot', identity=identity, itemId=snapshot['itemId'], revision=1,
                 snapshotId=identity, rubricVersion=RUBRIC, snapshotDigest=digest, capturedAt=snapshot['capturedAt'])
    return dict(schema='fsgg.telemetry.ingest/1', ingestId=identity, sourceIdentity=snapshot['producer'],
                generation=identity, cursor='1', eventCount=1, events=[event])


def comment_for(snapshot):
    digest = hashlib.sha256(canonical(snapshot)).hexdigest()
    return '<!-- fsgg:learn-dispatch/v1 snapshot=' + digest + ' -->\n```json\n' + canonical(snapshot).decode() + '```\n'


def prepare(snapshot, out):
    for key in ('runtime', 'model'):
        if not snapshot.get(key):
            raise ValueError(key + ' is required for an actual dispatch')
    stamp = datetime.fromisoformat(snapshot['capturedAt'].replace('Z', '+00:00'))
    if stamp.utcoffset() is None or stamp.utcoffset().total_seconds() != 0:
        raise ValueError('capturedAt must be UTC')
    snapshot = classify(snapshot)
    out.mkdir(parents=True, exist_ok=True)
    for name, data in [('snapshot.json', canonical(snapshot)), ('observation.json', canonical(batch_for(snapshot))),
                       ('comment.md', comment_for(snapshot).encode())]:
        with (out / name).open('xb') as f:
            f.write(data)
    print(json.dumps(dict(snapshotDigest=batch_for(snapshot)['events'][0]['snapshotDigest'], files=3, effects=0)))


def durable_directory(path):
    if path.is_dir():
        return
    durable_directory(path.parent)
    path.mkdir(mode=0o700)
    parent = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(parent)
    finally:
        os.close(parent)


def durable_json(path, value):
    temporary = path.with_suffix('.pending')
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_TRUNC, 0o600)
    with os.fdopen(descriptor, 'wb') as stream:
        stream.write(canonical(value))
        stream.flush()
        os.fsync(stream.fileno())
    os.replace(temporary, path)
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def read_issue(repo, number):
    return json.loads(subprocess.check_output(['gh', 'issue', 'view', number, '--repo', repo, '--json', 'body'], text=True))


def read_comments(repo, number):
    pages = json.loads(subprocess.check_output(['gh', 'api', '--paginate', '--slurp',
                        'repos/' + repo + '/issues/' + number + '/comments'], text=True))
    if not isinstance(pages, list) or not pages or any(not isinstance(page, list) for page in pages):
        raise ValueError('unreadable comment census')
    comments = [comment for page in pages for comment in page]
    if any(not isinstance(comment, dict) or type(comment.get('id')) is not int or not isinstance(comment.get('body'), str) for comment in comments):
        raise ValueError('unreadable comment census')
    return comments


def read_comment(repo, identity):
    return json.loads(subprocess.check_output(['gh', 'api', 'repos/' + repo + '/issues/comments/' + str(identity)], text=True))


def post_comment(repo, number, request):
    return json.loads(subprocess.check_output(['gh', 'api', '--method', 'POST',
                      'repos/' + repo + '/issues/' + number + '/comments', '--input', str(request)], text=True))


def submit_telemetry(repo, args, packet):
    return subprocess.check_output([args.engine, 'telemetry', 'workspace', 'submit', '--config', args.config,
        '--repository', repo, '--producer', args.producer, '--binding-digest', args.binding_digest,
        '--input', str(packet)], text=True)


def record(out, args):
    # Validate every retained byte before any remote operation, then use only custody copies.
    raw = (out / 'snapshot.json').read_bytes()
    snapshot = json.loads(raw)
    if raw != canonical(snapshot):
        raise ValueError('noncanonical snapshot')
    if args.producer != snapshot['producer']:
        raise ValueError('producer mismatch')
    batch = batch_for(snapshot)
    if (out / 'observation.json').read_bytes() != canonical(batch):
        raise ValueError('snapshot/observation mismatch: exact full batch required')
    expected = comment_for(snapshot)
    if (out / 'comment.md').read_bytes() != expected.encode():
        raise ValueError('comment mismatch')
    digest = batch['events'][0]['snapshotDigest']
    repo, number = snapshot['itemId'].split('#')
    default_root = Path(os.environ.get('XDG_STATE_HOME') or Path.home() / '.local/state') / 'fsgg/learn-dispatch'
    state_root = Path(getattr(args, 'state_root', None) or default_root)
    # All cooperating invocations for this native Delivery share a lock, even with different intent directories.
    custody = state_root / hashlib.sha256(snapshot['itemId'].encode()).hexdigest()
    durable_directory(custody)
    lock = os.open(custody / 'writer.lock', os.O_RDWR | os.O_CREAT, 0o600)
    try:
        try:
            fcntl.flock(lock, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError as error:
            raise ValueError('Delivery recording lock is held') from error
        scope = dict(snapshotDigest=digest, producer=args.producer, config=str(Path(args.config).resolve()),
                     bindingDigest=args.binding_digest, engine=args.engine)
        state_path = custody / (digest + '.json')
        for other in custody.glob('*.json'):
            prior = json.loads(other.read_bytes())
            if other != state_path and prior['phase'] != 'telemetry-submitted':
                raise ValueError('another Delivery intent has unresolved effects; reconcile its custody')
        state = json.loads(state_path.read_bytes()) if state_path.exists() else dict(schema='fsgg.learn-dispatch-custody/1', scope=scope, phase='prepared')
        if state.get('schema') != 'fsgg.learn-dispatch-custody/1' or state.get('phase') not in ('prepared', 'comment-possible', 'comment-observed', 'telemetry-possible', 'telemetry-submitted'):
            raise ValueError('unreadable recording custody; reconciliation required')
        if state['phase'] == 'telemetry-submitted' and not isinstance(state.get('receipt'), str):
            raise ValueError('missing retained receipt; reconciliation required')
        if state['scope'] != scope:
            raise ValueError('recording scope changed; retain original custody')
        if state['phase'] == 'telemetry-possible':
            raise ValueError('telemetry possible-send is unresolved; receiver reconciliation required, no automatic resend')
        # Exact immutable local payload copies avoid trusting mutable caller files at send time.
        packet = custody / (digest + '.observation')
        request = custody / (digest + '.comment')
        for path, payload in [(packet, batch), (request, dict(body=expected))]:
            if path.exists() and path.read_bytes() != canonical(payload):
                raise ValueError('custody payload mismatch')
            if not path.exists():
                durable_json(path, payload)
        current = read_issue(repo, number)
        if hashlib.sha256(current['body'].encode()).hexdigest() != snapshot['inputDigest']:
            raise ValueError('issue body changed; reconcile old effects before preparing a new snapshot')
        matches = [comment for comment in read_comments(repo, number) if comment['body'] == expected]
        if len(matches) > 1:
            raise ValueError('duplicate exact dispatch comments require owner reconciliation')
        if matches:
            comment = matches[0]
        elif state['phase'] != 'prepared':
            raise ValueError('comment possible-send/readback is unresolved; no automatic repost')
        else:
            state['phase'] = 'comment-possible'
            durable_json(state_path, state)  # durable before a subprocess can send anything
            comment = post_comment(repo, number, request)
        if type(comment.get('id')) is not int or comment['id'] <= 0:
            raise ValueError('unreadable native comment identity; retain possible-send custody')
        if state.get('commentId') is not None and state['commentId'] != comment['id']:
            raise ValueError('native comment identity changed; reconciliation required')
        state['commentId'] = comment['id']
        if state['phase'] == 'comment-possible':
            durable_json(state_path, state)
        observed = read_comment(repo, comment['id'])
        if observed['id'] != comment['id'] or observed['body'] != expected:
            raise ValueError('native comment readback mismatch; retain possible-send custody')
        if state['phase'] == 'telemetry-submitted':
            print('retained telemetry submission; native comment readback verified; inspect receipt acceptance before dispatch')
            return
        state.update(phase='comment-observed', commentId=observed['id'], commentUrl=observed.get('html_url'))
        durable_json(state_path, state)
        state['phase'] = 'telemetry-possible'
        durable_json(state_path, state)
        receipt = submit_telemetry(repo, args, packet)
        state.update(phase='telemetry-submitted', receipt=receipt)
        durable_json(state_path, state)
        print('native comment readback verified and telemetry submission retained; inspect receipt acceptance before dispatch')
    finally:
        os.close(lock)

if __name__=='__main__':
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--input',type=Path);parser.add_argument('--output',type=Path,required=True)
 parser.add_argument('--record',action='store_true',help='explicit live comment and telemetry effects; needs separate authority')
 parser.add_argument('--state-root',type=Path,help='one shared private custody root per host; retain across retries')
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
