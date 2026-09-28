#!/usr/bin/env python3
"""Read-only receiver probe. Runs only in the pinned, credential-minimal container."""
import datetime as dt
import hashlib
import json
import re
import sys
import urllib.error
import urllib.request

APP_ID = 4166418
INSTALLATION_ID = 143110413
REPO_ID = 1353050537
NODE_ID = 'R_kgDOUKXpqQ'
FULL_NAME = 'FS-GG/FS.GG.GitHub.Substrate.Sandbox'
REF_PREFIX = 'refs/heads/gs2-09-7/'
API = 'https://api.github.com'
VERSION = '2026-03-10'
MAX_BYTES = 1024 * 1024


class ProbeRefusal(ValueError):
    def __init__(self, code, stage, http_status=None):
        super().__init__(code)
        self.code = code
        self.stage = stage
        self.http_status = http_status


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def pairs_unique(pairs):
    result = {}
    for key, value in pairs:
        require(key not in result, 'duplicate JSON key')
        result[key] = value
    return result


def parse(raw):
    require(isinstance(raw, bytes) and 0 < len(raw) <= MAX_BYTES, 'invalid response size')
    return json.loads(raw, object_pairs_hook=pairs_unique)


def digest(raw):
    return hashlib.sha256(raw).hexdigest()


def validate_mint(raw):
    value = parse(raw)
    require(isinstance(value, dict), 'mint response is not an object')
    token = value.get('token')
    require(isinstance(token, str) and len(token) > 20 and token.isascii()
            and not any(c.isspace() for c in token), 'missing narrow bearer')
    require(value.get('repository_selection') == 'selected', 'mint selection drift')
    permissions = value.get('permissions')
    require(permissions in ({'contents': 'read'},
                            {'contents': 'read', 'metadata': 'read'}),
            'mint permissions drift')
    repositories = value.get('repositories')
    require(isinstance(repositories, list) and len(repositories) == 1, 'mint repository count drift')
    check_repo(repositories[0])
    expiry_text = value.get('expires_at')
    require(isinstance(expiry_text, str) and expiry_text.endswith('Z'), 'mint expiry missing')
    expiry = dt.datetime.fromisoformat(expiry_text.replace('Z', '+00:00'))
    require(expiry > dt.datetime.now(dt.timezone.utc) + dt.timedelta(minutes=5), 'mint expires too soon')
    return token, {'mintResponseSha256': digest(raw), 'tokenSha256': digest(token.encode()),
                   'expiresAt': expiry_text}


def check_repo(repo):
    require(isinstance(repo, dict) and repo.get('id') == REPO_ID
            and repo.get('node_id') == NODE_ID and repo.get('full_name') == FULL_NAME,
            'sandbox repository identity drift')


class Api:
    def __init__(self, token):
        self.token = token

    def get(self, path):
        require(path.startswith('/') and '://' not in path, 'invalid API path')
        request = urllib.request.Request(API + path, method='GET', headers={
            'Accept': 'application/vnd.github+json',
            'Authorization': 'Bearer ' + self.token,
            'X-GitHub-Api-Version': VERSION,
        })
        opener = urllib.request.build_opener(NoRedirect())
        try:
            with opener.open(request, timeout=20) as response:
                require(response.status == 200 and response.url == API + path,
                        'GET redirected or returned non-200')
                raw = response.read(MAX_BYTES + 1)
        except urllib.error.HTTPError as error:
            raise ProbeRefusal('github-http-error', 'bearer-read', error.code) from None
        return parse(raw), raw


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def one_pass(api):
    # Installation identity is established by the credential host with its App JWT.
    # This isolated process receives only the minted installation bearer and uses
    # endpoints that GitHub supports for that authentication mode.
    repositories, repositories_raw = api.get('/installation/repositories?per_page=100')
    require(isinstance(repositories, dict) and repositories.get('total_count') == 1
            and isinstance(repositories.get('repositories'), list)
            and len(repositories['repositories']) == 1, 'visible repository count drift')
    check_repo(repositories['repositories'][0])
    repo, repo_raw = api.get('/repos/' + FULL_NAME)
    check_repo(repo)
    refs, refs_raw = api.get('/repos/' + FULL_NAME + '/git/matching-refs/heads/gs2-09-7/')
    require(isinstance(refs, list), 'receiver refs are not an array')
    names = []
    for ref in refs:
        require(isinstance(ref, dict) and isinstance(ref.get('ref'), str)
                and ref['ref'].startswith(REF_PREFIX)
                and isinstance(ref.get('object'), dict)
                and re.fullmatch(r'[0-9a-f]{40}', str(ref['object'].get('sha', ''))),
                'receiver ref namespace drift')
        names.append(ref['ref'])
    require(names == sorted(set(names)), 'receiver refs duplicate or unordered')
    repo_repeat, repo_repeat_raw = api.get('/repos/' + FULL_NAME)
    check_repo(repo_repeat)
    require(repo == repo_repeat, 'repository metadata changed within pass')
    return {'repositoriesSha256': digest(repositories_raw),
            'repositorySha256': digest(repo_raw),
            'repositoryRepeatSha256': digest(repo_repeat_raw),
            'receiverRefsSha256': digest(refs_raw), 'receiverRefCount': len(refs)}


def probe(raw, api_factory=Api):
    token, proof = validate_mint(raw)
    api = api_factory(token)
    first = one_pass(api)
    second = one_pass(api)
    require(first == second, 'receiver observations drifted across passes')
    return {'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
            'status': 'observed-no-authority', 'complete': True,
            'repositoryId': REPO_ID, 'installationId': INSTALLATION_ID,
            'mint': proof, 'passes': [first, second],
            'copyAuthorized': False, 'refMutationAuthorized': False}


def main():
    raw = sys.stdin.buffer.read(MAX_BYTES + 1)
    try:
        result = probe(raw)
    except (ValueError, TypeError, KeyError, json.JSONDecodeError) as error:
        # Refusals expose only a bounded classification. Raw provider bytes, paths,
        # exception text, stderr, and bearer material never cross this boundary.
        if isinstance(error, ProbeRefusal):
            code, stage, http_status = error.code, error.stage, error.http_status
        else:
            code, stage, http_status = 'validation-error', 'observation-validation', None
        print(json.dumps({'schema': 'fsgg.gs2-09-7.receiver-observer-probe/1',
                          'status': 'refused', 'complete': False,
                          'refusal': {'code': code, 'stage': stage,
                                      'httpStatus': http_status},
                          'copyAuthorized': False,
                          'refMutationAuthorized': False}, sort_keys=True))
        return 1
    print(json.dumps(result, sort_keys=True, separators=(',', ':')))
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
