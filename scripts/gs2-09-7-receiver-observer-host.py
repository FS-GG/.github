#!/usr/bin/env python3
"""Protected host for the narrow, read-only receiver observer probe."""
import datetime as dt
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request

APP_ID = 4166418
INSTALLATION_ID = 143110413
REPO_ID = 1353050537
REPO_NODE_ID = 'R_kgDOUKXpqQ'
FULL_NAME = 'FS-GG/FS.GG.GitHub.Substrate.Sandbox'
APP_SLUG = 'fs-gg-cross-repo-dispatch'
IMAGE = 'python:3.14.6-slim@sha256:b921fe7e7522f828d45197a47656ec465a9b15689b27fa8e1fba2864fca5b967'
SCHEMA = 'fsgg.gs2-09-7.receiver-observer-host/1'
SCRIPT = Path(__file__).with_name('gs2-09-7-receiver-observer.py')
MAX_BYTES = 1024 * 1024

spec = importlib.util.spec_from_file_location('protected_mint', Path(__file__).with_name('gs2-09-7-mint-sandbox-token.py'))
mint = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mint)


def require(ok, reason):
    if not ok:
        raise ValueError(reason)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, fp, code, msg, headers, newurl):
        return None


def unique_pairs(pairs):
    value = {}
    for key, item in pairs:
        require(key not in value, 'duplicate provider JSON key')
        value[key] = item
    return value


def request_json(method, path, bearer, body=None):
    require(method in ('GET', 'POST') and path.startswith('/') and '://' not in path,
            'invalid App request')
    encoded = None if body is None else json.dumps(body, separators=(',', ':')).encode()
    request = urllib.request.Request('https://api.github.com' + path, data=encoded,
        method=method, headers={
            'Accept': 'application/vnd.github+json',
            'Authorization': 'Bearer ' + bearer,
            'X-GitHub-Api-Version': '2026-03-10',
            **({'Content-Type': 'application/json'} if encoded is not None else {}),
        })
    try:
        with urllib.request.build_opener(NoRedirect()).open(request, timeout=20) as response:
            require(response.status == (201 if method == 'POST' else 200)
                    and response.url == 'https://api.github.com' + path,
                    'App response redirected or returned unexpected status')
            raw = response.read(MAX_BYTES + 1)
    except urllib.error.HTTPError as error:
        raise ValueError('App API returned HTTP ' + str(error.code)) from None
    require(0 < len(raw) <= MAX_BYTES, 'App response size invalid')
    value = json.loads(raw, object_pairs_hook=unique_pairs)
    require(isinstance(value, dict), 'App response is not an object')
    return value, raw


def revoke_token(token):
    require(isinstance(token, str) and len(token) > 20, 'revoke bearer missing')
    request = urllib.request.Request('https://api.github.com/installation/token',
        method='DELETE', headers={
            'Accept': 'application/vnd.github+json',
            'Authorization': 'Bearer ' + token,
            'X-GitHub-Api-Version': '2026-03-10',
        })
    try:
        with urllib.request.build_opener(NoRedirect()).open(request, timeout=20) as response:
            require(response.status == 204 and
                    response.url == 'https://api.github.com/installation/token',
                    'token revocation was not confirmed')
    except urllib.error.HTTPError as error:
        raise ValueError('token revocation returned HTTP ' + str(error.code)) from None


def identity(app, installation):
    require(app.get('id') == APP_ID and app.get('slug') == APP_SLUG,
            'protected App identity drift')
    require(installation.get('id') == INSTALLATION_ID
            and installation.get('app_id') == APP_ID
            and installation.get('app_slug') == APP_SLUG
            and installation.get('account', {}).get('login') == 'FS-GG'
            and installation.get('suspended_at') is None,
            'protected installation identity drift')
    app_grants = app.get('permissions')
    install_grants = installation.get('permissions')
    require(isinstance(app_grants, dict) and isinstance(install_grants, dict)
            and app_grants.get('contents') in ('read', 'write')
            and install_grants.get('contents') in ('read', 'write')
            and app_grants.get('metadata') == 'read'
            and install_grants.get('metadata') == 'read',
            'read-only grant unavailable')


def validate_mint(response):
    token = response.get('token')
    require(isinstance(token, str) and len(token) > 20 and token.isascii()
            and not any(c.isspace() for c in token), 'mint response omitted bearer')
    require(response.get('repository_selection') == 'selected', 'mint selection drift')
    require(response.get('permissions') == {'contents': 'read', 'metadata': 'read'},
            'minted grant is not exact read-only scope')
    repos = response.get('repositories')
    require(isinstance(repos, list) and len(repos) == 1
            and repos[0].get('id') == REPO_ID
            and repos[0].get('node_id') == REPO_NODE_ID
            and repos[0].get('full_name') == FULL_NAME,
            'minted repository is not exact sandbox singleton')
    expires_at = response.get('expires_at')
    require(isinstance(expires_at, str) and expires_at.endswith('Z'), 'mint expiry missing')
    expiry = dt.datetime.fromisoformat(expires_at.replace('Z', '+00:00'))
    require(expiry > dt.datetime.now(dt.timezone.utc) + dt.timedelta(minutes=5),
            'mint expiry too close')
    return token


def container_argv(script):
    require(script.is_file() and not script.is_symlink(), 'observer source missing')
    return ['docker', 'run', '--rm', '--interactive', '--platform', 'linux/amd64',
            '--read-only', '--cap-drop=ALL', '--security-opt', 'no-new-privileges',
            '--pids-limit=64', '--memory=256m', '--cpus=1',
            '--network=bridge', '--tmpfs', '/tmp:rw,noexec,nosuid,size=16m',
            '--mount', 'type=bind,source=' + str(script.resolve()) + ',target=/observer.py,readonly',
            '--user', '65534:65534', IMAGE, 'python', '-I', '/observer.py']


def run_container(raw, script=SCRIPT):
    # The raw mint response crosses only stdin. No App key, JWT, Actions bearer,
    # host credential directory, Docker socket, host PID, or candidate checkout is mounted.
    with tempfile.TemporaryDirectory(prefix='receiver-docker-config-') as config_dir:
        result = subprocess.run(container_argv(script), input=raw, capture_output=True,
                                timeout=180, check=False,
                                env={'PATH': '/usr/local/bin:/usr/bin:/bin',
                                     'HOME': config_dir, 'DOCKER_CONFIG': config_dir})
    require(result.returncode == 0, 'isolated observer refused or failed')
    require(0 < len(result.stdout) <= 1024 * 1024, 'observer output size invalid')
    report = json.loads(result.stdout)
    require(isinstance(report, dict) and report.get('schema') ==
            'fsgg.gs2-09-7.receiver-observer-probe/1'
            and report.get('status') == 'observed-no-authority'
            and report.get('complete') is True
            and report.get('copyAuthorized') is False
            and report.get('refMutationAuthorized') is False,
            'observer report is not a complete read-only observation')
    require(set(report) == {'schema', 'status', 'complete', 'repositoryId',
                           'installationId', 'mint', 'passes', 'copyAuthorized',
                           'refMutationAuthorized'} and
            report['repositoryId'] == REPO_ID and
            report['installationId'] == INSTALLATION_ID and
            isinstance(report['passes'], list) and len(report['passes']) == 2 and
            report['passes'][0] == report['passes'][1] and
            isinstance(report['mint'], dict) and
            set(report['mint']) == {'mintResponseSha256', 'tokenSha256', 'expiresAt'},
            'observer report contains unexpected data or drift')
    require(response_token_absent(result.stdout, raw), 'observer output contains bearer')
    return report


def response_token_absent(stdout, mint_raw):
    value = json.loads(mint_raw)
    token = value.get('token')
    return isinstance(token, str) and token.encode() not in stdout


def write_private(path, raw):
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, 'wb') as stream:
        stream.write(raw)


def write_report(path, report):
    raw = (json.dumps(report, sort_keys=True, separators=(',', ':')) + '\n').encode()
    write_private(path, raw)


def run(root, app_id, private_key):
    require(app_id == APP_ID and 'PRIVATE KEY' in private_key, 'App credential missing')
    require(os.environ.get('GITHUB_REPOSITORY') == 'FS-GG/.github'
            and os.environ.get('GITHUB_REF') == 'refs/heads/main'
            and os.environ.get('GITHUB_SHA') == os.environ.get('FSGG_PROTECTED_SHA'),
            'protected main source binding missing')
    run_id = os.environ.get('GITHUB_RUN_ID', '')
    run_attempt = os.environ.get('GITHUB_RUN_ATTEMPT', '')
    require(run_id.isdecimal() and int(run_id) > 0 and
            run_attempt.isdecimal() and int(run_attempt) > 0,
            'protected run identity missing')
    require(SCRIPT.is_file() and SCRIPT.resolve().parent == Path(__file__).resolve().parent,
            'protected observer script absent')
    root.mkdir(mode=0o700, parents=True, exist_ok=False)
    jwt = mint.app_jwt(APP_ID, private_key)
    app, app_raw = request_json('GET', '/app', jwt)
    installation, installation_raw = request_json(
        'GET', '/app/installations/' + str(INSTALLATION_ID), jwt)
    identity(app, installation)
    response, mint_raw = request_json('POST',
        '/app/installations/' + str(INSTALLATION_ID) + '/access_tokens', jwt,
        {'repository_ids': [REPO_ID], 'permissions': {'contents': 'read'}})
    token = response.get('token')
    if isinstance(token, str):
        print('::add-mask::' + token, flush=True)
    # Preserve cleanup custody even if the narrower grant validator refuses.
    if isinstance(token, str) and token:
        write_private(root / 'token.private', token.encode())
    report = {'schema': SCHEMA, 'status': 'revocation-pending', 'complete': False,
              'runId': int(run_id), 'runAttempt': int(run_attempt),
              'protectedSha': os.environ['GITHUB_SHA'],
              'hostSourceSha256': sha(Path(__file__).read_bytes()),
              'observerSourceSha256': sha(SCRIPT.read_bytes()),
              'containerImage': IMAGE,
              'appId': APP_ID, 'installationId': INSTALLATION_ID, 'repositoryId': REPO_ID,
              'appResponseSha256': sha(app_raw), 'installationResponseSha256': sha(installation_raw),
              'mintResponseSha256': sha(mint_raw),
              'tokenSha256': sha(token.encode()) if isinstance(token, str) else None,
              'observer': None, 'revoked': False, 'authority': False}
    try:
        validate_mint(response)
        observer = run_container(mint_raw)
        require(observer['mint']['mintResponseSha256'] == sha(mint_raw)
                and observer['mint']['tokenSha256'] == report['tokenSha256'],
                'observer mint binding drift')
        report['observer'] = observer
    finally:
        # A failed probe still revokes. A failed revocation keeps a 0600 retry token
        # and a sanitized, nonauthorizing pending report on the host only.
        write_report(root / 'revocation-pending.json', report)
        try:
            revoke_token(token)
        except Exception:
            raise ValueError('token revocation was not confirmed; private retry custody retained') from None
        (root / 'token.private').unlink(missing_ok=True)
        report['status'] = 'observed-no-authority' if report['observer'] else 'refused'
        report['complete'] = bool(report['observer'])
        report['revoked'] = True
        write_report(root / 'report.json', report)
        (root / 'revocation-pending.json').unlink()
    return report


def cleanup(root):
    token_file = root / 'token.private'
    if token_file.is_file() and not token_file.is_symlink():
        revoke_token(token_file.read_text())
        token_file.unlink()
        pending = root / 'revocation-pending.json'
        if pending.is_file():
            data = json.loads(pending.read_bytes())
            data['revoked'] = True
            data['status'] = 'refused'
            write_report(root / 'cleanup.json', data)
            pending.unlink()


def main():
    root = Path(os.environ['FSGG_OBSERVER_PRIVATE_DIR'])
    if len(sys.argv) == 2 and sys.argv[1] == 'cleanup':
        cleanup(root)
        return
    if len(sys.argv) != 2 or sys.argv[1] != 'probe':
        raise SystemExit('usage: receiver-observer-host.py probe|cleanup')
    report = run(root, int(os.environ['FSGG_DISPATCH_APP_ID']),
                 os.environ['FSGG_DISPATCH_APP_PRIVATE_KEY'])
    require(report['complete'] is True, 'observer did not complete')


if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        # Credential-bearing provider errors are intentionally reduced to a fixed log line.
        print('receiver observer host refused: ' + type(error).__name__, file=sys.stderr)
        raise SystemExit(1)
