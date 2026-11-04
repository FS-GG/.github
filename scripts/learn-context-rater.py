#!/usr/bin/env python3
"""Frozen context broker for LEARN-02. No model/network/native writer implementation.

Prepare bounded model-visible bytes; reject tool requests; quarantine responses. Installed
Codex flags alone do not qualify a tools-empty/model-context boundary, so launch fails closed.
"""
from __future__ import annotations
import argparse
import hashlib
import importlib.util
import json
import math
import os
from pathlib import Path, PurePosixPath
import re
import stat
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
MAX_PAYLOAD = 256 * 1024
MAX_BUNDLE = 64 * 1024 * 1024
MODEL, EFFORT = 'gpt-6.1-sol', 'medium'
HEX = re.compile(r'[0-9a-f]{64}')
MANIFEST_FIELDS = {'schema','caseId','boundaryKind','checkpointKind','checkpointLagSeconds',
    'exactHistoricalHead','taskSha256','taskRedactionVersion','files','contextGaps',
    'identityBlinding','ratingLaunchAllowed','isolation'}
ENTRY_FIELDS = {'path','mode','blob','bytes','sha256','contentKind'}
GAPS = {'unresolved-gitlink','unmaterialized-lfs-object','dependency-environment-not-reconstructed',
    'creator-private-context-unavailable','related-discussion-and-external-research-not-reconstructed',
    'creation-before-first-work-not-established','exact-branch-state-not-established'}

class Refused(ValueError): pass

def require(ok, reason):
    if not ok: raise Refused(reason)

def canonical(value):
    return (json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=True,
                       allow_nan=False) + '\n').encode()

def sha(raw): return hashlib.sha256(raw).hexdigest()

def pairs(entries):
    out = {}
    for key, value in entries:
        require(key not in out, 'duplicate-json-key')
        out[key] = value
    return out

def decode(raw, limit=MAX_PAYLOAD):
    require(len(raw) <= limit, 'document-byte-bound')
    try:
        return json.loads(raw, object_pairs_hook=pairs,
                          parse_constant=lambda _: (_ for _ in ()).throw(Refused('nonfinite-json')))
    except (UnicodeError, json.JSONDecodeError, RecursionError) as error:
        raise Refused('invalid-json') from error

def closed(value, fields):
    require(isinstance(value, dict) and set(value) == set(fields), 'closed-object-fields')

def relpath(value):
    require(isinstance(value, str) and value and '\\' not in value and '\x00' not in value,
            'unsafe-path')
    parts = value.split('/')
    require(not value.startswith('/') and all(p not in ['', '.', '..', '.git'] for p in parts),
            'unsafe-path')
    require(len(value) <= 4096 and str(PurePosixPath(value)) == value, 'unsafe-path')
    return parts

def validate_gap(gap):
    require(isinstance(gap, dict) and set(gap) <= {'kind','path','object'} and gap.get('kind') in GAPS,
            'unsupported-context-gap')
    if 'path' in gap: relpath(gap['path'])
    if 'object' in gap:
        require(isinstance(gap['object'],str) and re.fullmatch(r'[0-9a-f]{40,64}',gap['object']),
                'context-gap-object')

def read_at(root, relative, limit):
    """Open each component without links. Never resolve a controller path or shared hardlink."""
    parts = relpath(relative)
    fd = os.open(root, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW)
    try:
        for component in parts[:-1]:
            child = os.open(component, os.O_RDONLY | os.O_DIRECTORY | os.O_NOFOLLOW, dir_fd=fd)
            os.close(fd); fd = child
        child = os.open(parts[-1], os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK, dir_fd=fd)
        try:
            before = os.fstat(child)
            require(stat.S_ISREG(before.st_mode) and before.st_nlink == 1, 'regular-unlinked-input-required')
            require(before.st_size <= limit, 'file-byte-bound')
            raw = b''
            while len(raw) <= limit:
                block = os.read(child, min(65536, limit + 1 - len(raw)))
                if not block: break
                raw += block
            after = os.fstat(child)
            require((before.st_dev,before.st_ino,before.st_size,before.st_mtime_ns) ==
                    (after.st_dev,after.st_ino,after.st_size,after.st_mtime_ns), 'input-changed-during-read')
            require(len(raw) == before.st_size, 'file-byte-bound-or-partial-read')
            return raw
        finally: os.close(child)
    except OSError as error:
        raise Refused('input-path-or-read-refused') from error
    finally: os.close(fd)

def read_document(path, limit=MAX_PAYLOAD):
    path=Path(path).absolute()
    require(path.parent.resolve() == path.parent, 'canonical-document-parent-required')
    return read_at(path.parent,path.name,limit)

def module(name):
    spec = importlib.util.spec_from_file_location(name.replace('-', '_'), ROOT / 'scripts' / (name+'.py'))
    value = importlib.util.module_from_spec(spec); spec.loader.exec_module(value); return value

def prepare(bundle, manifest_sha, plan, rubric_raw):
    require(isinstance(manifest_sha, str) and HEX.fullmatch(manifest_sha), 'expected-manifest-digest-required')
    root = Path(bundle)
    require(root.is_absolute() and root.resolve() == root and root.is_dir(), 'canonical-bundle-root-required')
    raw = read_at(root, 'manifest.json', 8*1024*1024)
    require(sha(raw) == manifest_sha, 'frozen-manifest-drift')
    manifest = decode(raw, 8*1024*1024); closed(manifest, MANIFEST_FIELDS)
    require(manifest['schema'] == 'fsgg.learn-context-snapshot/1' and
            manifest['boundaryKind'] == 'item-creation' and manifest['checkpointKind'] == 'proven-before-cutoff' and
            manifest['exactHistoricalHead'] is False and manifest['ratingLaunchAllowed'] is False and
            manifest['identityBlinding'] == 'partial-source-identifiability' and
            manifest['taskRedactionVersion'] == 'github-identity-links/1', 'unsupported-snapshot-contract')
    require(re.fullmatch(r'context-(00[1-9]|01[0-6])', manifest['caseId']), 'case-identity')
    require(type(manifest['checkpointLagSeconds']) in [int,float] and math.isfinite(manifest['checkpointLagSeconds']) and manifest['checkpointLagSeconds'] >= 0,
            'checkpoint-lag')
    closed(plan, {'schema','manifestSha256','treatment','files'})
    require(plan['schema'] == 'fsgg.context-file-plan/1' and plan['manifestSha256'] == manifest_sha,
            'file-plan-join')
    selected = plan['files']
    require(isinstance(selected, list) and len(selected) <= 64 and all(isinstance(x,str) for x in selected) and len(set(selected)) == len(selected), 'file-plan-bound')
    require(plan['treatment'] in ['issue-only','frozen-context'] and
            ((plan['treatment'] == 'issue-only' and not selected) or
             (plan['treatment'] == 'frozen-context' and bool(selected))), 'treatment-file-plan')
    for path in selected: relpath(path)
    task = read_at(root, 'task.md', MAX_PAYLOAD)
    require(sha(task) == manifest['taskSha256'], 'frozen-task-drift')
    try: task_text = task.decode('utf-8'); rubric_text = rubric_raw.decode('utf-8')
    except UnicodeError as error: raise Refused('utf8-required') from error
    require(isinstance(manifest['files'], list) and 1 <= len(manifest['files']) <= 20000, 'manifest-file-bound')
    seen = set(); total = 0; chosen = {}
    for entry in manifest['files']:
        closed(entry, ENTRY_FIELDS); path = entry['path']; relpath(path)
        require(path not in seen, 'duplicate-source-path'); seen.add(path)
        require(entry['mode'] in ['100644','100755'] and entry['contentKind'] in ['utf8','binary'] and
                type(entry['bytes']) is int and 0 <= entry['bytes'] <= 4*1024*1024, 'source-entry-contract')
        data = read_at(root, 'source/'+path, 4*1024*1024); total += len(data)
        require(total <= MAX_BUNDLE and len(data) == entry['bytes'] and sha(data) == entry['sha256'] and
                hashlib.sha1(b'blob '+str(len(data)).encode()+b'\0'+data).hexdigest() == entry['blob'], 'frozen-source-drift-or-bound')
        if path in selected:
            require(entry['contentKind'] == 'utf8', 'selected-binary-refused')
            try: text = data.decode('utf-8')
            except UnicodeError as error: raise Refused('selected-source-utf8') from error
            chosen[path] = {'path':path,'sha256':entry['sha256'],'text':text}
    require(set(chosen) == set(selected), 'file-plan-path-not-in-frozen-manifest')
    require(isinstance(manifest['contextGaps'], list) and len(manifest['contextGaps']) <= 20106, 'context-gaps')
    for gap in manifest['contextGaps']: validate_gap(gap)
    require(bool(task_text.strip()), 'empty-task')
    packet = {'schema':'fsgg.semantic-task-packet/1','item_id':manifest['caseId'],
              'task_lines':[{'line':i+1,'text':line} for i,line in enumerate(task_text.splitlines())]}
    payload = {'schema':'fsgg.context-rater-payload/1','manifestSha256':manifest_sha,
        'filePlanSha256':sha(canonical(plan)),'taskPacket':packet,'rubricSha256':sha(rubric_raw),
        'rubric':rubric_text,'treatment':plan['treatment'],'source': [chosen[x] for x in selected],
        'checkpointLagSeconds':manifest['checkpointLagSeconds'],'contextGaps':manifest['contextGaps'],
        'identityBlinding':manifest['identityBlinding'],'requestedProfile':{'model':MODEL,'effort':EFFORT},
        'payloadOnly':True,'toolPolicy':'none','history':None,'evaluationEligible':False}
    require(len(canonical(payload)) <= MAX_PAYLOAD, 'flat-payload-bound-no-truncation')
    return payload

def request(payload):
    """One new input, no conversation ID, no dynamic tool definitions or attached workspace."""
    closed(payload, {'schema','manifestSha256','filePlanSha256','taskPacket','rubricSha256','rubric','treatment',
        'source','checkpointLagSeconds','contextGaps','identityBlinding','requestedProfile','payloadOnly',
        'toolPolicy','history','evaluationEligible'})
    require(payload['schema'] == 'fsgg.context-rater-payload/1' and payload['toolPolicy'] == 'none' and
            payload['history'] is None and payload['payloadOnly'] is True and payload['evaluationEligible'] is False,
            'payload-boundary')
    require(payload['requestedProfile'] == {'model':MODEL,'effort':EFFORT}, 'normal-profile-required')
    require(len(canonical(payload)) <= MAX_PAYLOAD, 'flat-payload-bound')
    for name in ['manifestSha256','filePlanSha256','rubricSha256']:
        require(isinstance(payload[name],str) and HEX.fullmatch(payload[name]), 'payload-digest')
    require(isinstance(payload['rubric'],str) and sha(payload['rubric'].encode()) == payload['rubricSha256'], 'rubric-drift')
    packet = payload['taskPacket']; closed(packet, {'schema','item_id','task_lines'})
    require(packet['schema'] == 'fsgg.semantic-task-packet/1' and re.fullmatch(r'context-(00[1-9]|01[0-6])',packet['item_id']), 'packet-contract')
    require(isinstance(packet['task_lines'],list) and 1 <= len(packet['task_lines']) <= 20000, 'task-line-bound')
    for number,line in enumerate(packet['task_lines'],1):
        closed(line, {'line','text'});require(type(line['line']) is int and line['line'] == number and isinstance(line['text'],str), 'task-line-contract')
    require(isinstance(payload['source'],list) and len(payload['source']) <= 64, 'context-file-bound')
    paths = set()
    for item in payload['source']:
        closed(item, {'path','sha256','text'});relpath(item['path']);require(item['path'] not in paths,'duplicate-source-path');paths.add(item['path'])
        require(isinstance(item['text'],str) and sha(item['text'].encode()) == item['sha256'], 'payload-source-drift')
    require(payload['treatment'] in ['issue-only','frozen-context'] and (bool(paths) == (payload['treatment']=='frozen-context')), 'payload-treatment')
    require(type(payload['checkpointLagSeconds']) in [int,float] and math.isfinite(payload['checkpointLagSeconds']) and payload['checkpointLagSeconds'] >= 0,'payload-lag')
    require(payload['identityBlinding'] == 'partial-source-identifiability' and isinstance(payload['contextGaps'],list),'payload-context-contract')
    require(len(payload['contextGaps']) <= 20106, 'context-gaps')
    for gap in payload['contextGaps']: validate_gap(gap)
    return {'schema':'fsgg.context-rater-request/1','payloadSha256':sha(canonical(payload)),
            'requestedProfile':payload['requestedProfile'],'freshConversation':True,
            'tools':[],'toolChoice':'none','previousConversation':None,'input':payload}

def inspect_provider_request(value, payload):
    """Audit exact outbound JSON, including input-embedded tools; never send it.

    This narrow candidate direct-Responses contract is deliberately not an adapter.
    No injected messages, instructions, opaque context IDs or tool declarations qualify.
    """
    request(payload)
    def walk(node):
        if isinstance(node,dict):
            require(node.get('type') != 'additional_tools', 'provider-input-tools-denied')
            for key,item in node.items():
                require(key != 'tools' or item == [], 'provider-tools-denied')
                walk(item)
        elif isinstance(node,list):
            for item in node: walk(item)
    walk(value)
    closed(value, {'model','reasoning','input','tools','tool_choice','store','text'})
    require(value['model'] == MODEL and value['reasoning'] == {'effort':EFFORT}, 'provider-requested-profile')
    require(value['tools'] == [] and value['tool_choice'] == 'none' and value['store'] is False,
            'provider-tool-history-policy')
    expected=[{'role':'user','content':[{'type':'input_text','text':canonical(payload).decode()}]}]
    require(value['input'] == expected, 'provider-input-must-equal-frozen-payload')
    # Reuse the existing strict response schema, rather than trusting a model's promise of JSON.
    schema=decode((ROOT/'contracts/learn/semantic-hardness-v1.schema.json').read_bytes())
    require(value['text'] == {'format':{'type':'json_schema','name':'semantic_hardness',
                                       'strict':True,'schema':schema}}, 'provider-output-schema')
    return {'schema':'fsgg.context-wire-inspection/1','requestSha256':sha(canonical(value)),
            'payloadSha256':sha(canonical(payload)), 'toolsEmpty':True,'contextExact':True,
            'inferenceExecuted':False,'isolationQualified':False,'evaluationEligible':False}

def broker_frame(frame):
    """Causal protocol boundary. It never dispatches a tool, even a supposedly harmless read."""
    closed(frame, {'type','value'})
    require(frame['type'] == 'final', 'all-tool-and-control-frames-denied')
    require(isinstance(frame['value'], dict), 'final-json-object-required')
    return frame['value']

def quarantine(payload, frame):
    request(payload); rating = broker_frame(frame)
    module('learn-classifier-pilot').validate(rating, payload['taskPacket'])
    return {'schema':'fsgg.context-rater-quarantine/1','payloadSha256':sha(canonical(payload)),
        'requestedProfile':payload['requestedProfile'],'effectiveProfile':{'model':None,'effort':None,'evidence':None},
        'rating':rating,'isolationQualified':False,'evaluationEligible':False,
        'limitation':'Protocol validation only; no qualified tool-free model transport is registered.'}

def probe(executable):
    """Local help only. Do not load credentials, start a session or claim flags prove isolation."""
    path = Path(executable).resolve(); require(path.is_file(), 'runtime-executable-required')
    outputs = []
    for args in [[str(path),'--version'],[str(path),'exec','--help']]:
        result = subprocess.run(args, capture_output=True, timeout=10)
        require(result.returncode == 0 and len(result.stdout)+len(result.stderr) <= 65536, 'runtime-probe-refused')
        outputs.append(result.stdout.decode('utf-8'))
    flags = ['--ignore-user-config','--ignore-rules','--ephemeral','--output-schema','--sandbox']
    return {'schema':'fsgg.context-rater-capability/1','runtimeLauncherSha256':sha(path.read_bytes()),
        'version':outputs[0].strip(),'observedExecFlags':{f:f in outputs[1] for f in flags},
        'effectiveToolSet':None,'automaticContextInputs':None,'isolationQualified':False,'ratingLaunchAllowed':False,
        'gaps':['Runtime help is not a model-request tool inventory or automatic-context read trace.',
                'Read-only filesystem sandbox does not deny reads of future/controller files.',
                'No current canonical transport with enforced empty tools and context-only request has been qualified.']}

def launch(payload, capability):
    request(payload)
    # No user-supplied receipt/boolean can turn this prototype into a qualified model launcher.
    raise Refused('rating-launch-refused: no qualified context-only model transport')

def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__); commands = parser.add_subparsers(dest='command', required=True)
    p = commands.add_parser('prepare'); p.add_argument('--bundle',type=Path,required=True);p.add_argument('--manifest-sha256',required=True);p.add_argument('--file-plan',type=Path,required=True);p.add_argument('--output',type=Path,required=True)
    p = commands.add_parser('probe');p.add_argument('--codex',type=Path,required=True)
    p = commands.add_parser('launch');p.add_argument('--payload',type=Path,required=True);p.add_argument('--payload-sha256',required=True)
    p = commands.add_parser('inspect-wire');p.add_argument('--request',type=Path,required=True);p.add_argument('--payload',type=Path,required=True);p.add_argument('--payload-sha256',required=True)
    p = commands.add_parser('protocol');p.add_argument('--payload',type=Path,required=True);p.add_argument('--payload-sha256',required=True)
    args = parser.parse_args(argv)
    try:
        if args.command == 'prepare':
            value = prepare(args.bundle,args.manifest_sha256,decode(read_document(args.file_plan)),
                            (ROOT/'.agents/skills/assess-task-hardness/SKILL.md').read_bytes())
            snapshot = module('learn-context-snapshot');snapshot.private_parent(args.output);snapshot.write_new(args.output,canonical(value))
            print(json.dumps({'payloadSha256':sha(canonical(value)),'bytes':len(canonical(value)),
                              'ratingLaunchAllowed':False}));return 0
        if args.command == 'probe': print(json.dumps(probe(args.codex)));return 0
        raw = read_document(args.payload);require(sha(raw) == args.payload_sha256, 'sealed-payload-drift');payload = decode(raw);request(payload)
        if args.command == 'launch': launch(payload,None)
        if args.command == 'inspect-wire':
            print(json.dumps(inspect_provider_request(decode(read_document(args.request,2*1024*1024),2*1024*1024),payload)));return 0
        # Protocol qualification only, not an inference process or a claim of OS isolation.
        frame = decode(sys.stdin.buffer.read(MAX_PAYLOAD+1));print(json.dumps(quarantine(payload,frame)));return 0
    except (Refused,OSError,ValueError,TypeError) as error:
        print(json.dumps({'schema':'fsgg.context-rater-refusal/1','reason':str(error),'ratingLaunchAllowed':False}),file=sys.stderr);return 3

if __name__ == '__main__': raise SystemExit(main())
