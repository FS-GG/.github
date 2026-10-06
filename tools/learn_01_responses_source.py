#!/usr/bin/env python3
"""Independent offline replay of a Host-owned Responses capture.

This module authenticates neither provider access nor queue history. The installed
Host owns the profile/receipt, grant, claim, original deadline and process custody.
No network, credential reading, process launch or telemetry writes occur here.
"""
import argparse
import base64
import datetime as dt
import hashlib
import json
import os
import pathlib
import re
import stat

VARIANT = 'openai-responses/1'
SHARED = {'model', 'instructions', 'input', 'text', 'reasoning', 'tools', 'tool_choice', 'parallel_tool_calls', 'truncation'}
GENERATION = {'max_output_tokens', 'store', 'stream', 'background'}
CAPTURE_FIELDS = {'schema', 'sourceVariant', 'operationId', 'invocationId', 'originalItemId', 'observedAt', 'dispatchRef', 'claimRef', 'countRequestBase64', 'generationRequestBase64', 'countResponseBase64', 'generationResponseBase64', 'countStatus', 'generationStatus', 'countBodyComplete', 'generationBodyComplete', 'stage', 'elapsedMilliseconds', 'wholeMilliseconds', 'networkMilliseconds', 'failures', 'cleanupFailures'}
SNAPSHOT_FIELDS = {'schema', 'sourceVariant', 'operationId', 'invocationId', 'originalItemId', 'dispatchRef', 'claimRef', 'installedProfileSha256', 'instructionsSha256', 'responseSchemaSha256', 'responseSchemaName', 'responseSchemaBase64'}
# These are completion diagnostics, never transport/custody admission. Their
# underlying response and nullable quantities are independently replayed below.
COMPLETION_FAILURES = {
    'provider-error-present', 'provider-incomplete-details-present',
    'provider-not-completed', 'provider-refusal', 'usage-unavailable',
    'usage-not-complete', 'input_tokens-unavailable', 'output_tokens-unavailable',
    'total_tokens-unavailable', 'count-input-not-corresponding',
    'inclusive-output-not-admitted', 'inclusive-total-mismatch',
    'cached-input-exceeds-input', 'cache-write-exceeds-input',
    'reasoning-exceeds-inclusive-output', 'assessment-text-unavailable',
    'error-invalid', 'incomplete_details-invalid', 'output-unavailable',
    'output-item-invalid', 'output-message-incomplete', 'output-message-role-invalid',
    'output-content-invalid', 'output-text-unavailable', 'refusal-text-unavailable',
    'unexpected-output-content', 'output-content-unavailable', 'unexpected-output-item',
}
STAGES = {'not-sent', 'count-sent', 'count-accepted', 'generation-sent', 'response-captured'}

class Refusal(ValueError):
    pass

def require(ok, code):
    if not ok:
        raise Refusal(code)

def sha(raw):
    return hashlib.sha256(raw).hexdigest()

def integer(value, minimum=0, maximum=2**63-1):
    return type(value) is int and minimum <= value <= maximum

def hex64(value):
    return isinstance(value, str) and re.fullmatch('[0-9a-f]{64}', value) is not None and value != '0'*64

def text(value, maximum=262144):
    return isinstance(value, str) and bool(value.strip()) and '\x00' not in value and len(value.encode('utf-8')) <= maximum

def exact(value, fields, code):
    require(type(value) is dict and set(value) == fields, code)
    return value

def parse(raw, maximum, code):
    require(type(raw) is bytes and 0 < len(raw) <= maximum, code)
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, code)
            result[key] = value
        return result
    try:
        result = json.loads(raw.decode('utf-8'), object_pairs_hook=pairs, parse_constant=lambda _: (_ for _ in ()).throw(Refusal(code)))
        # Reject unpaired UTF16 surrogates and excessively nested/provider scalar payloads.
        json.dumps(result, ensure_ascii=False).encode('utf-8')
        require(type(result) is dict, code)
        return result
    except (ValueError, UnicodeError, RecursionError):
        raise Refusal(code) from None

def reference(value, kind):
    exact(value, {'id', 'kind', 'revision', 'contentDigest'}, 'malformed-reference')
    require(text(value['id'], 512) and value['kind'] == kind and integer(value['revision']), 'malformed-reference')
    digest = value['contentDigest']
    require(isinstance(digest, str) and digest.startswith('sha256:') and hex64(digest[7:]), 'malformed-reference')

def claim_reference(value):
    exact(value, {'requestId','claimId','revision','contentDigest','owner','generation'}, 'malformed-reference')
    require(text(value['requestId'],512) and text(value['claimId'],512) and integer(value['revision']) and integer(value['generation'],1), 'malformed-reference')
    owner = exact(value['owner'], {'producer','stream'}, 'malformed-reference')
    require(text(owner['producer'],128) and text(owner['stream'],128), 'malformed-reference')
    digest = value['contentDigest']
    require(isinstance(digest,str) and digest.startswith('sha256:') and hex64(digest[7:]), 'malformed-reference')

def raw_body(capture, name, maximum):
    value = capture[name]
    require(isinstance(value, str) and len(value) <= 4*((maximum+2)//3), 'malformed-body')
    try:
        raw = base64.b64decode(value, validate=True)
    except ValueError:
        raise Refusal('malformed-body') from None
    require(len(raw) <= maximum and base64.b64encode(raw).decode('ascii') == value, 'malformed-body')
    return raw

def same(left, right):
    if type(left) is not type(right):
        return False
    if type(left) is dict:
        return set(left) == set(right) and all(same(left[k],right[k]) for k in left)
    if type(left) is list:
        return len(left) == len(right) and all(same(a,b) for a,b in zip(left,right))
    return left == right

def request_policy(count, generation, snapshot):
    exact(count, SHARED, 'request-policy-mismatch')
    exact(generation, SHARED | GENERATION, 'request-policy-mismatch')
    require(all(same(count[k], generation[k]) for k in SHARED), 'request-policy-mismatch')
    require(count['model'] == 'gpt-6.1-sol' and count['reasoning'] == {'effort': 'medium'}, 'request-policy-mismatch')
    require(count['tools'] == [] and count['tool_choice'] == 'none' and count['parallel_tool_calls'] is False and count['truncation'] == 'disabled', 'request-policy-mismatch')
    require(text(count['instructions']) and sha(count['instructions'].encode('utf-8')) == snapshot['instructionsSha256'], 'request-policy-mismatch')
    inputs = count['input']
    require(type(inputs) is list and len(inputs) == 1, 'request-policy-mismatch')
    exact(inputs[0], {'role', 'content'}, 'request-policy-mismatch')
    require(inputs[0]['role'] == 'user' and text(inputs[0]['content']), 'request-policy-mismatch')
    exact(count['text'], {'format'}, 'request-policy-mismatch')
    fmt = exact(count['text']['format'], {'type', 'name', 'strict', 'schema'}, 'request-policy-mismatch')
    require(fmt['type'] == 'json_schema' and fmt['strict'] is True and isinstance(fmt['name'], str) and re.fullmatch('[A-Za-z0-9_-]{1,64}', fmt['name']) and type(fmt['schema']) is dict, 'request-policy-mismatch')
    schema_raw = raw_body(snapshot, 'responseSchemaBase64', 262144)
    require(sha(schema_raw) == snapshot['responseSchemaSha256'] and fmt['name'] == snapshot['responseSchemaName'] and same(parse(schema_raw,262144,'request-policy-mismatch'),fmt['schema']), 'request-policy-mismatch')
    require(integer(generation['max_output_tokens'], 1500, 1500) and all(generation[k] is False for k in ('store', 'stream', 'background')), 'request-policy-mismatch')

def response_observation(response):
    status = response.get('status')
    known = {'completed', 'incomplete', 'failed', 'cancelled', 'in_progress', 'queued'}
    response_id = response.get('id')
    require(text(response_id,256) and not any(ch.isspace() or ord(ch)<32 or 127<=ord(ch)<=159 for ch in response_id) and response.get('object') == 'response' and response.get('model') == 'gpt-6.1-sol', 'response-not-complete')
    require(isinstance(status,str) and status in known, 'response-not-complete')
    if response.get('created_at') is not None:
        require(integer(response['created_at'], 0, 253402300799), 'response-not-complete')
    usage = response.get('usage')
    usage_state = 'unknown'
    usage_valid = usage is None or type(usage) is dict
    usage_ok = False
    observed_input = None
    if type(usage) is dict:
        counters = [usage.get(k) for k in ('input_tokens', 'output_tokens', 'total_tokens')]
        usage_state = 'partial'
        usage_valid = all(n is None or integer(n) for n in counters)
        i, o, total = counters
        observed_input = i
        # Actual measured quantities are not rewritten or dropped for a policy
        # breach. Structural containment still applies to every known operand.
        lower_input = i if integer(i) else 0
        lower_output = o if integer(o) else 0
        for field, keys, cap in [('input_tokens_details', ('cached_tokens', 'cache_write_tokens'), i), ('output_tokens_details', ('reasoning_tokens',), o)]:
            details = usage.get(field)
            if details is not None:
                usage_valid = usage_valid and type(details) is dict
                if type(details) is dict:
                    for key in keys:
                        n = details.get(key)
                        if n is not None:
                            usage_valid = usage_valid and integer(n) and (cap is None or (integer(cap) and n <= cap))
                            if integer(n):
                                if field == 'input_tokens_details':
                                    lower_input = max(lower_input,n)
                                else:
                                    lower_output = max(lower_output,n)
        usage_valid = usage_valid and lower_input+lower_output <= 2**63-1
        if integer(total):
            usage_valid = usage_valid and lower_input+lower_output <= total
        if all(integer(n) for n in counters):
            usage_valid = usage_valid and total == i+o
        usage_ok = usage_valid and all(integer(n) for n in counters) and 1 <= i <= 8000 and 1 <= o <= 1500
        if usage_valid and all(integer(n) for n in counters):
            usage_state = 'complete'
    content_ok = False
    outputs = response.get('output')
    if type(outputs) is list:
        content_ok = True
        texts = []
        for output in outputs:
            if type(output) is not dict:
                content_ok = False
                continue
            if output.get('type') == 'reasoning':
                continue
            if output.get('type') != 'message' or output.get('role') != 'assistant' or output.get('status') != 'completed' or type(output.get('content')) is not list:
                content_ok = False
                continue
            for part in output['content']:
                if type(part) is not dict or part.get('type') != 'output_text' or not text(part.get('text')):
                    content_ok = False
                else:
                    texts.append(part['text'])
        content_ok = content_ok and bool(texts)
    terminal_ok = status == 'completed' and response.get('error') is None and response.get('incomplete_details') is None and response.get('tools', []) == [] and content_ok
    return response_id, status, usage_state, usage_ok, terminal_ok, usage_valid, observed_input

def verify(capture_raw, snapshot_raw):
    result = dict(schema='fsgg.telemetry.responses-verification/1', sourceVariant=VARIANT, captureSha256=sha(capture_raw), snapshotSha256=sha(snapshot_raw), profileSha256=None, requestSha256=None, countRequestSha256=None, countResponseSha256=None, responseSha256=None, responseId=None, status='unknown', usageState='unknown', observationVerified=False, accepted=False, errors=[])
    def issue(code):
        if code not in result['errors']:
            result['errors'].append(code)
    try:
        c = exact(parse(capture_raw, 1100000, 'malformed-capture'), CAPTURE_FIELDS, 'malformed-capture')
        s = exact(parse(snapshot_raw, 400000, 'malformed-snapshot'), SNAPSHOT_FIELDS, 'malformed-snapshot')
        require(c['schema'] == 'fsgg.telemetry.responses-owned-capture/1' and s['schema'] == 'fsgg.telemetry.responses-verification-snapshot/1' and c['sourceVariant'] == s['sourceVariant'] == VARIANT, 'binding-mismatch')
        require(isinstance(c['operationId'], str) and re.fullmatch('[0-9a-f]{32}', c['operationId']) and text(c['invocationId'], 512) and text(c['originalItemId'], 512), 'malformed-capture')
        require(all(hex64(s[k]) for k in ('installedProfileSha256','instructionsSha256','responseSchemaSha256')) and text(s['responseSchemaName'],64), 'malformed-snapshot')
        result['profileSha256'] = s['installedProfileSha256']
        reference(c['dispatchRef'], 'expected-dispatch'); claim_reference(c['claimRef'])
        reference(s['dispatchRef'], 'expected-dispatch'); claim_reference(s['claimRef'])
        require(all(same(c[k],s[k]) for k in ('operationId', 'invocationId', 'originalItemId', 'dispatchRef', 'claimRef')), 'binding-mismatch')
        require(isinstance(c['observedAt'], str) and c['observedAt'].endswith(('Z', '+00:00')), 'malformed-capture')
        try:
            timestamp = dt.datetime.fromisoformat(c['observedAt'].replace('Z', '+00:00'))
            require(timestamp.utcoffset() == dt.timedelta(0), 'malformed-capture')
        except ValueError:
            raise Refusal('malformed-capture') from None
        require(integer(c['elapsedMilliseconds']) and integer(c['wholeMilliseconds'],60000,60000) and integer(c['networkMilliseconds'],55000,55000) and isinstance(c['stage'],str) and c['stage'] in STAGES, 'malformed-capture')
        for field in ('countStatus', 'generationStatus'):
            require(c[field] is None or integer(c[field],100,599), 'malformed-capture')
        for field in ('countBodyComplete', 'generationBodyComplete'):
            require(type(c[field]) is bool, 'malformed-capture')
        for field in ('failures', 'cleanupFailures'):
            require(type(c[field]) is list and len(c[field]) <= 32 and all(isinstance(x,str) and re.fullmatch('[a-z][a-z0-9_-]{0,95}',x) for x in c[field]), 'malformed-capture')
        bodies = {key:raw_body(c,key+'Base64',bound) for key,bound in [('countRequest',262144),('generationRequest',262144),('countResponse',4096),('generationResponse',262144)]}
        result.update(requestSha256=sha(bodies['generationRequest']), countRequestSha256=sha(bodies['countRequest']), countResponseSha256=sha(bodies['countResponse']), responseSha256=sha(bodies['generationResponse']))
    except Refusal as e:
        issue(str(e));return result
    # Collect independently replayable request/count/response failures in this bounded input.
    request_ok = count_ok = response_ok = False
    try:
        request_policy(parse(bodies['countRequest'],262144,'request-policy-mismatch'),parse(bodies['generationRequest'],262144,'request-policy-mismatch'),s)
        request_ok = True
    except Refusal as e:
        issue(str(e))
    amount = None
    try:
        count = exact(parse(bodies['countResponse'],4096,'count-not-admitted'),{'object','input_tokens'},'count-not-admitted')
        require(count['object']=='response.input_tokens' and integer(count['input_tokens'],1,8000),'count-not-admitted');amount=count['input_tokens'];count_ok=True
    except Refusal as e:
        issue(str(e))
    try:
        response = parse(bodies['generationResponse'],262144,'response-not-complete')
        rid,status,usage_state,usage_ok,terminal_ok,usage_valid,observed_input = response_observation(response)
        result.update(responseId=rid,status=status,usageState=usage_state)
        response_ok = usage_valid and response.get('tools', []) == []
        if not terminal_ok:issue('response-not-complete')
        if observed_input is not None and observed_input != amount:issue('count-input-not-corresponding')
        if type(response.get('usage')) is dict and any(integer(response['usage'].get(k)) and response['usage'][k] > bound for k,bound in [('input_tokens',8000),('output_tokens',1500),('total_tokens',9500)]):issue('usage-policy-exceeded')
        if not usage_ok or observed_input != amount:issue('usage-not-complete')
    except Refusal as e:
        issue(str(e))
    if c['elapsedMilliseconds'] >= 60000:issue('deadline-exhausted')
    if c['stage'] != 'response-captured' or c['countStatus'] != 200 or c['generationStatus'] != 200 or not c['countBodyComplete'] or not c['generationBodyComplete'] or c['failures'] or c['cleanupFailures']:issue('operation-failed')
    semantic_failures = all(code in COMPLETION_FAILURES or (c['generationStatus'] != 200 and code == 'responses-http-status-'+str(c['generationStatus'])) for code in c['failures'])
    result['observationVerified'] = bool(request_ok and count_ok and response_ok and c['elapsedMilliseconds'] < 60000 and c['stage'] == 'response-captured' and c['countStatus'] == 200 and c['generationStatus'] is not None and c['countBodyComplete'] and c['generationBodyComplete'] and not c['cleanupFailures'] and semantic_failures)
    result['accepted'] = result['observationVerified'] and not result['errors']
    return result

def read_owned(path, maximum):
    path = pathlib.Path(path)
    require(path.is_absolute() and str(path) == os.path.abspath(path), 'unsafe-input')
    for parent in path.parents:
        require(not parent.is_symlink(), 'unsafe-input')
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    try:
        before = os.fstat(fd)
        require(stat.S_ISREG(before.st_mode) and before.st_uid == os.getuid() and stat.S_IMODE(before.st_mode) == 0o600 and before.st_nlink == 1 and 0 < before.st_size <= maximum, 'unsafe-input')
        raw = os.read(fd, maximum+1)
        after = os.fstat(fd)
        require(len(raw) == before.st_size and (before.st_dev,before.st_ino,before.st_size,before.st_mtime_ns,before.st_ctime_ns)==(after.st_dev,after.st_ino,after.st_size,after.st_mtime_ns,after.st_ctime_ns), 'unsafe-input')
        named = path.stat(follow_symlinks=False)
        require((after.st_dev,after.st_ino)==(named.st_dev,named.st_ino), 'unsafe-input')
        return raw
    finally:
        os.close(fd)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    commands = parser.add_subparsers(dest='command', required=True)
    cmd = commands.add_parser('verify-responses')
    cmd.add_argument('--capture',required=True);cmd.add_argument('--telemetry-snapshot',required=True)
    args = parser.parse_args()
    try:
        result = verify(read_owned(args.capture,1100000),read_owned(args.telemetry_snapshot,400000))
    except (OSError, Refusal):
        parser.exit(2,'Responses verification input refused\n')
    print(json.dumps(result,separators=(',',':'),ensure_ascii=True))
    # Exit success means independent observation replay, not completed assessment.
    return 0 if result['observationVerified'] else 1

if __name__ == '__main__':
    raise SystemExit(main())
