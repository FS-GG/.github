#!/usr/bin/env python3
"""Verify retained timestamp metadata and join transcript envelopes; no active-time inference."""
import argparse
import datetime
import hashlib
import json
import os
from pathlib import Path

def utc(value):
    if value is None: return None
    parsed = datetime.datetime.fromisoformat(value.replace('Z', '+00:00'))
    if parsed.tzinfo is None: raise ValueError('timestamp has no timezone')
    return parsed.astimezone(datetime.timezone.utc)

def normalize(raw_path, manifest_path, query_path, transcript_path):
    manifest = json.loads(Path(manifest_path).read_text())
    if manifest['exitCode'] != 0: raise ValueError('query failed')
    for key, path in [('outputSha256', raw_path), ('querySha256', query_path)]:
        if hashlib.sha256(Path(path).read_bytes()).hexdigest() != manifest[key]: raise ValueError('evidence hash mismatch')
    rows = json.loads(Path(raw_path).read_text())
    local = json.loads(Path(transcript_path).read_text())
    files = {r['invocation']:r for r in local['files']}
    if len(files) != local['fileCount']: raise ValueError('duplicate transcript identity')
    output, seen = [], set()
    for row in rows:
        invocation = row['invocationId']
        if invocation in seen: raise ValueError('duplicate invocation timestamp row')
        seen.add(invocation)
        times = json.loads(row.pop('occurredAtJson'))
        if len(times) != row['responseCount'] or sum(x is not None for x in times) != row['timestampedResponseCount']:
            raise ValueError('timestamp counts disagree')
        known = sorted(utc(x) for x in times if x is not None)
        if (known[0] if known else None) != utc(row['firstObservedResponseAt']) or (known[-1] if known else None) != utc(row['lastObservedResponseAt']):
            raise ValueError('timestamp bounds disagree')
        f = files.get(invocation)
        output.append(dict(row, occurredAt=times, observedModels=json.loads(row.pop('observedModelsJson')),
                           observedEfforts=json.loads(row.pop('observedEffortsJson')),
                           role=row['producerStream'].rsplit(':',1)[-1],
                           observedResponseEnvelopeSeconds=(known[-1]-known[0]).total_seconds() if known else None,
                           transcriptEnvelope={'firstRecordTimestamp':f['firstRecordTimestamp'],'lastRecordTimestamp':f['lastRecordTimestamp'],
                                               'bytes':f['bytes'],'sha256':f['sha256'],'parseGaps':f['counts'].get('parseGaps',0)} if f else None,
                           activeExecutionSeconds=None, terminalCoverage='unknown', populationCoverage='partial'))
    return {'schema':'fsgg.learn-response-timestamps/1','observedAt':manifest['observedAt'],
            'sourceQuerySha256':manifest['querySha256'],'sourceOutputSha256':manifest['outputSha256'],
            'rows':output,'invocations':len(output),'responses':sum(r['responseCount'] for r in output),
            'timestampedResponses':sum(r['timestampedResponseCount'] for r in output),
            'transcriptMatches':sum(r['transcriptEnvelope'] is not None for r in output),
            'interpretation':'completion timestamp arrays and observed envelopes only; no verified execution duration or accepted-completion boundary'}

if __name__ == '__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    for name in ['input','manifest','query','transcripts','output']: parser.add_argument('--'+name,required=True)
    args=parser.parse_args(); result=normalize(args.input,args.manifest,args.query,args.transcripts)
    os.umask(0o077)
    if Path(args.output).exists(): raise ValueError('output exists; preserve original evidence')
    Path(args.output).write_text(json.dumps(result,separators=(',',':'))+'\n'); Path(args.output).chmod(0o600)
    print(json.dumps({k:v for k,v in result.items() if k!='rows'}))
