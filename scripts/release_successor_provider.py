"""Live GitHub, NuGet and release-asset observations for UTEL-REL-01.

The provider never treats a successful push response as settled. Callers must
run release_successor_execution.advance again for independent readback.
"""

from __future__ import annotations

import base64
from contextlib import contextmanager
import datetime as dt
import hashlib
import http.client
import json
import pathlib
import re
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request

from fsgg_feed import _StripAuthOnRedirect
from release_successor_execution import Dispatch, Effect, Observation

REPOSITORY = "FS-GG/.github"
PACKAGES = ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers")
ORIGINAL_TAG_REF = "refs/tags/coherent-set/v0.103.0"
ORIGINAL_TAG_SOURCE = "4d136d5450a7032d0b87c4b8a37f69f020144792"
ORIGINAL_TAG_CONTENT = "sha256:0615d0d776f06b824b98193233c749f0ae6983e811d900cc5a166ede5e7e5007"
_ORIGINAL_TAG_TRANSPORT_SEAL = object()


class NotFound(RuntimeError):
    pass


class Refused(RuntimeError):
    pass


def request_diagnostic(url, method, status=None, headers=None, error=None):
    """Retain bounded public request metadata, never credentials or free-form bodies."""
    row = {"method": method if isinstance(method, str) and method in {"GET", "POST", "PATCH"} else "unknown",
           "status": status if type(status) is int and 100 <= status <= 599 else None}
    try:
        parsed = urllib.parse.urlparse(url)
    except Exception:
        parsed = urllib.parse.urlparse("")
    if parsed.scheme == "https" and parsed.netloc in {"api.github.com", "uploads.github.com"}:
        path = parsed.path
        if len(path) <= 512 and re.fullmatch(r"/[A-Za-z0-9._/%-]+", path):
            row["path"] = path  # Queries, signed redirects and authentication are excluded.
    def header(name):
        try:
            value = headers.get(name) if headers is not None else None
            return value if isinstance(value, str) else None
        except Exception:
            return None
    request_id = header("X-GitHub-Request-Id")
    if isinstance(request_id, str) and re.fullmatch(r"[A-Za-z0-9:-]{1,128}", request_id):
        row["requestId"] = request_id
    for key, header_name in (("remaining", "X-RateLimit-Remaining"), ("reset", "X-RateLimit-Reset"),
                        ("retryAfter", "Retry-After")):
        value = header(header_name)
        if isinstance(value, str) and re.fullmatch(r"[0-9]{1,20}", value):
            row[key] = value
    resource = header("X-RateLimit-Resource")
    if resource in {"core", "search", "graphql", "integration_manifest", "code_search"}:
        row["resource"] = resource
    accepted = header("X-Accepted-GitHub-Permissions")
    permissions = {"actions", "contents", "workflows", "packages", "metadata", "pull_requests",
                   "administration", "deployments", "checks", "issues"}
    if isinstance(accepted, str) and len(accepted) <= 512:
        pieces = re.split(r"[,;]\s*", accepted)
        if pieces and len(pieces) <= 16 and all(
                re.fullmatch(r"[a-z_]+=(?:read|write)", piece) and piece.split("=", 1)[0] in permissions
                for piece in pieces):
            row["acceptedPermissions"] = accepted  # Endpoint requirement, not the caller's effective grant.
    if error is not None:
        row["responseClass"] = "unclassified"
        try:
            raw = error.read(2049)
            row["bodyTruncated"] = len(raw) > 2048
            body = json.loads(raw[:2048])
            message = body.get("message") if isinstance(body, dict) else None
            known = {"Resource not accessible by integration": "integration-permission-denied",
                     "Resource not accessible by personal access token": "token-permission-denied",
                     "Reference already exists": "reference-already-exists",
                     "Validation Failed": "validation-failed", "Not Found": "not-found"}
            if isinstance(message, str):
                row["responseClass"] = known.get(message, "unclassified")
                if (message.startswith("refusing to allow a GitHub App to create or update workflow ")
                        and ("without workflows permission" in message or "without `workflows` permission" in message)):
                    row["responseClass"] = "workflow-permission-denied"
        except Exception:
            row["bodyUnavailable"] = True  # Reporting failure must not replace the original HTTP error.
    return row


def sanitized_dispatch_diagnostic(value):
    """Close the report projection over safe fields, even for a malformed diagnostic."""
    if not isinstance(value, dict):
        return None
    row = {}
    if value.get("diagnosticUnavailable") is True:
        row["diagnosticUnavailable"] = True
    category = value.get("category")
    if isinstance(category, str) and category in {"provider-refused", "process-failure", "transport-failure",
                    "os-failure", "protocol-failure", "uncaught-provider-failure"}:
        row["category"] = category
    code = value.get("returnCode")
    if type(code) is int and -(2 ** 31) <= code < 2 ** 31:
        row["returnCode"] = code
    request = value.get("request")
    if isinstance(request, dict):
        projected = request_diagnostic("https://api.github.com" + request.get("path", "")
                                       if isinstance(request.get("path"), str) else "",
                                       request.get("method", "unknown"), request.get("status"), {
            "X-GitHub-Request-Id": request.get("requestId"),
            "X-RateLimit-Remaining": request.get("remaining"), "X-RateLimit-Reset": request.get("reset"),
            "Retry-After": request.get("retryAfter"), "X-RateLimit-Resource": request.get("resource"),
            "X-Accepted-GitHub-Permissions": request.get("acceptedPermissions")})
        classification = request.get("responseClass")
        if isinstance(classification, str) and classification in {"unclassified", "integration-permission-denied", "token-permission-denied",
                               "reference-already-exists", "validation-failed", "not-found",
                               "workflow-permission-denied"}:
            projected["responseClass"] = classification
        for key in ("bodyTruncated", "bodyUnavailable", "diagnosticUnavailable"):
            if type(request.get(key)) is bool:
                projected[key] = request[key]
        row["request"] = projected
    return row or None


def best_effort_request_diagnostic(*args, **kwargs):
    try:
        return request_diagnostic(*args, **kwargs)
    except BaseException:
        return {"diagnosticUnavailable": True}


class VerifiedOriginalTagTransport:
    """One exact create-only send from an approved, authenticated credential factory.

    The production factory is deliberately unavailable until its actual credential
    issuance/repository/permission/expiry route is separately approved and verified.
    The private seal is an in-process construction fence, not provider authorization.
    """
    def __init__(self, seal, token, identity):
        if seal is not _ORIGINAL_TAG_TRANSPORT_SEAL:
            raise Refused("original tag transport was not produced by the approved factory")
        if (not isinstance(token, str) or not token or not isinstance(identity, dict)
                or set(identity) != {"appId", "installationId", "repositoryId", "permissions", "expiresAtUnix"}
                or any(type(identity[key]) is not int or identity[key] <= 0 for key in ("appId", "installationId"))
                or identity["repositoryId"] != 1269292704
                or identity["permissions"] != {"contents": "write", "workflows": "write"}
                or type(identity["expiresAtUnix"]) is not int or identity["expiresAtUnix"] <= time.time()):
            raise Refused("original tag authenticated capability binding differs")
        self._token, self._identity = token, json.loads(json.dumps(identity))
        self._consumed = False
        self.last_dispatch_diagnostic = None

    def identity(self):
        if self._token is None:
            raise Refused('original tag capability was released')
        if self._identity["expiresAtUnix"] <= time.time():
            raise Refused("original tag capability expired")
        return json.loads(json.dumps(self._identity))

    def dispatch(self, effect, deadline, clock=time.monotonic):
        from release_successor_journal import ReleaseSuspension, SUSPENDED_REF
        reader = getattr(self, "suspension", None)
        if not isinstance(reader, ReleaseSuspension):
            raise Refused("original tag transport requires the maintained suspension reader")
        reader.authorize(SUSPENDED_REF, "0.103.0")
        if (self._consumed or effect != Effect("tag", ORIGINAL_TAG_SOURCE, ORIGINAL_TAG_CONTENT)
                or clock() >= deadline):
            raise Refused("original tag transport dispatch differs, expired, or already consumed")
        self.identity()
        self._consumed = True  # Exceptions and ambiguous replies consume the sole invocation slot.
        url = f"https://api.github.com/repos/{REPOSITORY}/git/refs"
        request = urllib.request.Request(url, method="POST", data=json.dumps({
            "ref": ORIGINAL_TAG_REF, "sha": ORIGINAL_TAG_SOURCE}).encode(), headers={
            "Authorization": "Bearer " + self._token, "Accept": "application/vnd.github+json",
            "Content-Type": "application/json", "User-Agent": "fsgg-release-successor",
            "X-GitHub-Api-Version": "2022-11-28"})
        class NoRedirect(urllib.request.HTTPRedirectHandler):
            def redirect_request(self, *args, **kwargs):
                raise Refused("original tag create transport refuses redirects")
        self.last_dispatch_diagnostic = best_effort_request_diagnostic(url, "POST")
        try:
            with (getattr(self, '_opener', None) or urllib.request.build_opener(NoRedirect)).open(
                    request, timeout=min(60, deadline - clock())) as response:
                self.last_dispatch_diagnostic = best_effort_request_diagnostic(
                    url, "POST", getattr(response, "status", None), getattr(response, "headers", None))
                raw = response.read(16385)
                if len(raw) > 16384:
                    raise Refused("original tag create response exceeds bound")
                result = json.loads(raw)
                valid = (response.status == 201 and isinstance(result, dict) and result.get("ref") == ORIGINAL_TAG_REF
                         and result.get("object") == {"type": "commit", "sha": ORIGINAL_TAG_SOURCE,
                             "url": f"https://api.github.com/repos/{REPOSITORY}/git/commits/{ORIGINAL_TAG_SOURCE}"})
                return Dispatch("applied" if valid else "unknown")
        except urllib.error.HTTPError as error:
            self.last_dispatch_diagnostic = best_effort_request_diagnostic(url, "POST", error.code, error.headers, error)
        except (Refused, OSError, urllib.error.URLError, ValueError):
            pass
        return Dispatch("unknown")  # 201 still needs independent observation; no settlement happens here.


def approved_original_tag_transport():
    """No credential/environment fallback: native capability remains an explicit gap."""
    raise Refused("approved original tag credential factory unavailable; no tag send")


class AuthenticationOnlyOriginalTagTransport(VerifiedOriginalTagTransport):
    """A real factory construction that cannot issue a publication request."""
    def dispatch(self, *args, **kwargs):
        raise Refused("authentication qualification forbids every tag dispatch")


# Canonical UTEL v2 payload contract. Portable framing/custody belongs to Coordination.
AUTH_PROTOCOL = 'fsgg.execution.phase-result/1'
AUTH_PROFILE = 'utel-rel-21/original-tag-authentication-only/2'
AUTH_PHASES = ('ready', 'issuance-possible', 'token-owned', 'revocation-possible', 'revoked', 'terminal')
AUTH_PUBLICATION = {name: 0 for name in ('tag', 'journal', 'feed', 'release', 'asset', 'channel')}
_AUTH_STATUSES = {'unknown', 'verified', 'failed'}
_AUTH_PERMISSION_VALUES = {'absent', 'read', 'write', 'unknown'}


def _auth_require(condition):
    if not condition:
        raise Refused('authentication protocol assertion differs')


def _auth_keys(value, keys):
    _auth_require(type(value) is dict and set(value) == set(keys.split()))


def _auth_uint(value, maximum=(1 << 64) - 1, nullable=False, positive=False):
    _auth_require((nullable and value is None) or
                  (type(value) is int and (1 if positive else 0) <= value <= maximum))


def _auth_bool(value, nullable=False):
    _auth_require((nullable and value is None) or type(value) is bool)


def _auth_canonical(value):
    # ASCII-only strings and canonical compact encoding are part of the wire contract.
    def closed(item):
        if type(item) is dict:
            for key, child in item.items():
                _auth_require(type(key) is str)
                key.encode('ascii'); closed(child)
        elif type(item) is str:
            item.encode('ascii')
        elif type(item) is int:
            _auth_uint(item)
        else:
            _auth_require(item is None or type(item) is bool)
    closed(value)
    raw = json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode('ascii')
    return raw


def validate_auth_binding(binding):
    _auth_keys(binding, 'mode profile operation originalGeneration invocation process source workflow clock app')
    _auth_require(binding['mode'] in {'inert', 'native'} and binding['profile'] == AUTH_PROFILE
                  and binding['operation'] == 'utel-rel-21' and type(binding['originalGeneration']) is int
                  and binding['originalGeneration'] == 2)
    uuid = r'[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}'
    _auth_require(type(binding['invocation']) is str and re.fullmatch(uuid, binding['invocation']) is not None)
    process = binding['process']; _auth_keys(process, 'pid birth')
    _auth_uint(process['pid'], positive=True)
    _auth_require(type(process['birth']) is str and re.fullmatch(r'[0-9a-f]{1,64}', process['birth']) is not None)
    source = binding['source']; _auth_keys(source, 'childRevision childClosure runnerRevision runnerClosure contract')
    for name, value in source.items():
        _auth_require(type(value) is str and re.fullmatch('[0-9a-f]{' + ('40' if name.endswith('Revision') else '64') + '}', value) is not None)
    workflow = binding['workflow']; _auth_keys(workflow, 'repositoryId path revision run attempt')
    _auth_require(type(workflow['repositoryId']) is int and workflow['repositoryId'] == 1269292704
                  and workflow['path'] == '.github/workflows/release-successor-publish.yml'
                  and type(workflow['revision']) is str and re.fullmatch(r'[0-9a-f]{40}', workflow['revision']) is not None)
    _auth_uint(workflow['run']); _auth_uint(workflow['attempt'], (1 << 32) - 1)
    _auth_require((workflow['run'] == workflow['attempt'] == 0) if binding['mode'] == 'inert'
                  else (workflow['run'] > 0 and workflow['attempt'] > 0))
    clock = binding['clock']; _auth_keys(clock, 'domain frequency start normal work total')
    _auth_require(type(clock['domain']) is str and re.fullmatch(uuid, clock['domain']) is not None)
    for name in ('frequency', 'start', 'normal', 'work', 'total'):
        value = clock[name]
        _auth_require(type(value) is str and re.fullmatch(r'0|[1-9][0-9]{0,18}', value) is not None
                      and int(value) <= (1 << 63) - 1)
    _auth_require(int(clock['frequency']) > 0 and int(clock['start']) <= int(clock['normal'])
                  <= int(clock['work']) <= int(clock['total']))
    _auth_keys(binding['app'], 'id installation')
    for value in binding['app'].values(): _auth_uint(value, positive=True)
    return hashlib.sha256(_auth_canonical(binding)).hexdigest()


def empty_auth_facts():
    return {'app': {'status': 'unknown', 'id': None},
            'installation': {'status': 'unknown', 'id': None, 'appId': None, 'active': None,
                'selection': None, 'repositoryAssociation': 'unknown', 'requiredGrants': 'unknown'},
            'issuance': {'state': 'not-started', 'tokenSlot': None},
            'permissions': {'status': 'unknown', 'contents': 'unknown', 'workflows': 'unknown',
                            'metadata': 'unknown', 'extra': None},
            'expiry': {'status': 'unknown', 'unix': None, 'observedUnix': None},
            'census': {'status': 'unknown', 'total': None, 'repositoryId': None, 'nameMatches': None},
            'revocation': {'state': 'not-started', 'tokenSlot': None, 'status': None},
            'counters': {'getsStarted': 0, 'issuanceReservations': 0, 'revocationReservations': 0},
            'firstCause': None}


def validate_auth_facts(facts, binding, previous=None):
    shape = empty_auth_facts(); _auth_keys(facts, ' '.join(shape))
    for name in shape:
        if name != 'firstCause': _auth_keys(facts[name], ' '.join(shape[name]))
    for name in ('app', 'installation', 'permissions', 'expiry', 'census'):
        _auth_require(facts[name]['status'] in _AUTH_STATUSES)
    app, installation = facts['app'], facts['installation']
    _auth_uint(app['id'], nullable=True); _auth_uint(installation['id'], nullable=True)
    _auth_uint(installation['appId'], nullable=True); _auth_bool(installation['active'], nullable=True)
    _auth_require(installation['selection'] in {'all', 'selected', None}
                  and installation['repositoryAssociation'] in _AUTH_STATUSES
                  and installation['requiredGrants'] in _AUTH_STATUSES)
    if app['status'] == 'verified': _auth_require(app['id'] == binding['app']['id'])
    if installation['status'] == 'verified':
        _auth_require(installation['id'] == binding['app']['installation']
                      and installation['appId'] == binding['app']['id'] and installation['active'] is True
                      and installation['selection'] in {'all', 'selected'}
                      and installation['repositoryAssociation'] == installation['requiredGrants'] == 'verified')
    issuance = facts['issuance']; revocation = facts['revocation']
    _auth_require(issuance['state'] in {'not-started', 'possible', 'response-owned', 'unknown'}
                  and (issuance['tokenSlot'] is None or type(issuance['tokenSlot']) is int and issuance['tokenSlot'] == 1)
                  and (issuance['tokenSlot'] == 1) == (issuance['state'] == 'response-owned'))
    permissions = facts['permissions']
    for name in ('contents', 'workflows', 'metadata'): _auth_require(permissions[name] in _AUTH_PERMISSION_VALUES)
    _auth_bool(permissions['extra'], nullable=True)
    if permissions['status'] == 'verified':
        _auth_require(permissions['contents'] == permissions['workflows'] == 'write'
                      and permissions['metadata'] in {'absent', 'read'} and permissions['extra'] is False)
    expiry = facts['expiry']
    for name in ('unix', 'observedUnix'): _auth_uint(expiry[name], (1 << 63) - 1, nullable=True)
    if expiry['status'] == 'verified':
        _auth_require(type(expiry['unix']) is int and type(expiry['observedUnix']) is int
                      and expiry['observedUnix'] + 15 < expiry['unix'] <= expiry['observedUnix'] + 3600)
    census = facts['census']
    _auth_uint(census['total'], nullable=True); _auth_uint(census['repositoryId'], nullable=True)
    _auth_bool(census['nameMatches'], nullable=True)
    if census['status'] == 'verified':
        _auth_require(census['total'] == 1 and census['repositoryId'] == 1269292704 and census['nameMatches'] is True)
    _auth_require(revocation['state'] in {'not-started', 'possible', 'verified', 'unknown'}
                  and (revocation['tokenSlot'] is None or type(revocation['tokenSlot']) is int and revocation['tokenSlot'] == 1)
                  and (revocation['status'] is None or type(revocation['status']) is int and revocation['status'] == 204))
    if revocation['state'] != 'not-started':
        _auth_require(revocation['tokenSlot'] == 1 and issuance['state'] == 'response-owned')
    _auth_require((revocation['status'] == 204) == (revocation['state'] == 'verified'))
    counters = facts['counters']
    for name, maximum in [('getsStarted', 8), ('issuanceReservations', 1), ('revocationReservations', 1)]:
        _auth_uint(counters[name], maximum)
    _auth_require(counters['issuanceReservations'] == (0 if issuance['state'] == 'not-started' else 1)
                  and counters['revocationReservations'] == (0 if revocation['state'] == 'not-started' else 1))
    _auth_require((app['status'] != 'verified' or counters['getsStarted'] >= 1)
                  and (installation['status'] != 'verified' or counters['getsStarted'] >= 3)
                  and (census['status'] != 'verified' or counters['getsStarted'] >= 4))
    cause = facts['firstCause']
    if cause is not None:
        _auth_keys(cause, 'stage code')
        _auth_require(cause['stage'] in set(AUTH_PHASES) | {'metadata', 'issuance', 'permissions', 'expiry', 'census', 'cleanup', 'protocol'}
                      and cause['code'] in {'identity', 'grant', 'scope', 'expiry', 'transport', 'response', 'deadline',
                                           'cancelled', 'credential-input', 'protocol', 'reporting', 'child-loss'})
    if previous is not None:
        for name in ('app', 'installation', 'permissions', 'expiry', 'census'):
            if previous[name]['status'] != 'unknown': _auth_require(facts[name] == previous[name])
            for key, old in previous[name].items():
                if key != 'status' and old is not None and old != 'unknown':
                    _auth_require(facts[name][key] == old)
        for name in counters: _auth_require(counters[name] >= previous['counters'][name])
        if previous['firstCause'] is not None: _auth_require(cause == previous['firstCause'])
        for name in ('issuance', 'revocation'):
            before, after = previous[name]['state'], facts[name]['state']
            allowed = ({'not-started': {'not-started', 'possible'}, 'possible': {'possible', 'response-owned', 'unknown'},
                        'response-owned': {'response-owned'}, 'unknown': {'unknown'}} if name == 'issuance' else
                       {'not-started': {'not-started', 'possible'}, 'possible': {'possible', 'verified', 'unknown'},
                        'verified': {'verified'}, 'unknown': {'unknown'}})
            _auth_require(after in allowed[before])
    return facts


def validate_auth_payload(kind, payload, binding, previous=None):
    _auth_require(kind in AUTH_PHASES)
    if kind == 'ready':
        _auth_keys(payload, 'binding'); _auth_require(payload['binding'] == binding)
        validate_auth_binding(payload['binding']); return
    keys = 'facts request' if kind in {'issuance-possible', 'revocation-possible'} else (
        'facts publication publicationComplete qualified credentialReferencesDropped' if kind == 'terminal' else 'facts')
    _auth_keys(payload, keys)
    facts = validate_auth_facts(payload['facts'], binding, previous)
    if kind == 'issuance-possible':
        _auth_require(facts['app']['status'] == facts['installation']['status'] == 'verified'
                      and facts['issuance'] == {'state': 'possible', 'tokenSlot': None}
                      and facts['revocation']['state'] == 'not-started'
                      and all(facts[name]['status'] == 'unknown' for name in ('permissions', 'expiry', 'census'))
                      and payload['request'] == {'method': 'POST',
                          'path': f"/app/installations/{binding['app']['installation']}/access_tokens",
                          'repositoryId': 1269292704, 'contents': 'write', 'workflows': 'write'})
    if kind == 'token-owned':
        _auth_require(facts['issuance'] == {'state': 'response-owned', 'tokenSlot': 1}
                      and facts['revocation']['state'] == 'not-started'
                      and all(facts[name]['status'] == 'unknown' for name in ('permissions', 'expiry', 'census')))
    if kind == 'revocation-possible':
        _auth_keys(payload['request'], 'method path tokenSlot')
        _auth_uint(payload['request']['tokenSlot'], 1, positive=True)
        _auth_require(facts['revocation'] == {'state': 'possible', 'tokenSlot': 1, 'status': None}
                      and payload['request'] == {'method': 'DELETE', 'path': '/installation/token', 'tokenSlot': 1})
    if kind == 'revoked': _auth_require(facts['revocation'] == {'state': 'verified', 'tokenSlot': 1, 'status': 204})
    if kind == 'terminal':
        _auth_require(facts['issuance']['state'] != 'possible' and facts['revocation']['state'] != 'possible')
        _auth_keys(payload['publication'], 'tag journal feed release asset channel')
        for value in payload['publication'].values(): _auth_uint(value, 0)
        _auth_bool(payload['publicationComplete']); _auth_require(payload['publicationComplete'] is False)
        _auth_bool(payload['qualified']); _auth_bool(payload['credentialReferencesDropped'])
        success = (all(facts[name]['status'] == 'verified' for name in ('app', 'installation', 'permissions', 'expiry', 'census'))
                   and facts['issuance']['state'] == 'response-owned' and facts['revocation']['state'] == 'verified'
                   and facts['firstCause'] is None and payload['credentialReferencesDropped'])
        _auth_require(not payload['qualified'] or success)


def encode_auth_frame(binding, ordinal, kind, payload):
    digest = validate_auth_binding(binding)
    _auth_uint(ordinal, 6, positive=True)
    validate_auth_payload(kind, payload, binding)
    raw = _auth_canonical({'v': AUTH_PROTOCOL, 'b': digest, 'n': ordinal, 'k': kind, 'p': payload})
    _auth_require(len(raw) + 4 <= 2048)
    return len(raw).to_bytes(4, 'big') + raw


def decode_auth_frame(raw, maximum=2048):
    _auth_require(type(raw) is bytes and 4 <= len(raw) <= maximum
                  and int.from_bytes(raw[:4], 'big') == len(raw) - 4)
    def unique(pairs):
        row = {}
        for key, value in pairs:
            _auth_require(key not in row); row[key] = value
        return row
    try:
        row = json.loads(raw[4:].decode('ascii'), object_pairs_hook=unique)
        _auth_require(_auth_canonical(row) == raw[4:])
        return row
    except Exception:
        raise Refused('authentication protocol encoding differs') from None


class AuthenticationPhaseAdapter:
    """Inert child-side domain adapter; no FD construction or native custody claim.

    exchange receives immutable exact bytes. A gate ACK is valid only after the
    maintained result sink durably retains and independently reads back this frame.
    That producer-owned contract is qualified separately; Python cannot mint it.
    """
    def __init__(self, binding, exchange, ticks):
        _auth_require(binding['mode'] == 'inert')  # Native profile is deliberately unavailable.
        self.binding = json.loads(_auth_canonical(binding))
        self.digest = validate_auth_binding(self.binding)
        self.exchange, self.ticks = exchange, ticks
        self.normal, self.work = (int(binding['clock'][name]) for name in ('normal', 'work'))
        self.frequency = int(binding['clock']['frequency'])
        self.ordinal, self.previous, self.last_kind = 0, None, None
        self.last_frame = None
        self.charged_bytes = 0
        self.failed = False

    def check(self, cleanup=False):
        value = self.ticks()
        _auth_uint(value, (1 << 63) - 1)
        _auth_require(value < (self.work if cleanup else self.normal))
        return value

    def emit(self, kind, payload):
        _auth_require(not self.failed)
        allowed = {None: {'ready'}, 'ready': {'issuance-possible', 'terminal'},
                   'issuance-possible': {'token-owned', 'terminal'}, 'token-owned': {'revocation-possible'},
                   'revocation-possible': {'revoked', 'terminal'}, 'revoked': {'terminal'}, 'terminal': set()}
        _auth_require(kind in allowed[self.last_kind])
        validate_auth_payload(kind, payload, self.binding, self.previous)
        self.check(cleanup=kind in {'revocation-possible', 'revoked', 'terminal'})
        raw = encode_auth_frame(self.binding, self.ordinal + 1, kind, payload)
        # The generic codec owns complete bidirectional accounting including rejected
        # and partial bytes. This local check additionally reserves terminal capacity.
        reserve = 2048 + (512 if kind not in {'terminal', 'revoked'} else 0)
        _auth_require(self.charged_bytes + len(raw) + (0 if kind == 'terminal' else reserve) <= 16384)
        self.charged_bytes += len(raw)
        # Any uncertain exchange is terminal for this one-shot channel. Never
        # overwrite a possibly retained frame or retry an ACK after readback loss.
        self.failed = True
        ack = self.exchange(raw)
        if kind in {'ready', 'issuance-possible', 'revocation-possible'}:
            _auth_require(type(ack) is bytes)
            self.charged_bytes += len(ack)
            _auth_require(self.charged_bytes <= 16384)
            actual = decode_auth_frame(ack, maximum=512)
            _auth_keys(actual, 'v k b n h')
            _auth_uint(actual['n'], 6, positive=True)
            _auth_require(actual == {'v': AUTH_PROTOCOL, 'k': 'ack', 'b': self.digest,
                                    'n': self.ordinal + 1, 'h': hashlib.sha256(raw).hexdigest()})
            self.check(cleanup=kind == 'revocation-possible')
        else:
            _auth_require(ack is None)
        self.ordinal += 1; self.last_kind = kind
        self.last_frame = raw
        self.previous = json.loads(_auth_canonical(payload.get('facts'))) if 'facts' in payload else None
        self.failed = False
        return raw


class OriginalTagAuthenticationSession:
    """One bounded, same-process App issuance and owned-token cleanup.

    This is source preparation, not a selected credential owner or an enabled
    production route. The caller supplies an owner-issued App JWT, never claimed
    effective identity. Identity comes from authenticated API responses. No
    credential is read from the environment, persisted or included in diagnostics.
    The opener seam permits inert tests of the actual Request/read control flow.
    """
    REPOSITORY_ID = 1269292704
    PERMISSIONS = {"contents": "write", "workflows": "write"}

    def __init__(self, app_jwt, app_id, installation_id, *, phase_adapter, opener=None,
                 wall_clock=time.time):
        if (not isinstance(app_jwt, str) or not app_jwt or "\n" in app_jwt or "\r" in app_jwt
                or type(app_id) is not int or app_id <= 0
                or type(installation_id) is not int or installation_id <= 0):
            raise Refused("authentication qualification configuration differs")
        self._jwt, self._token = app_jwt, None
        self.app_id, self.installation_id = app_id, installation_id
        _auth_require(isinstance(phase_adapter, AuthenticationPhaseAdapter)
                      and phase_adapter.binding["app"] == {"id": app_id, "installation": installation_id}
                      and opener is not None)
        self.adapter, self.wall_clock = phase_adapter, wall_clock
        self.facts = empty_auth_facts()
        self._used = False
        self._gets = self._issues = self._revokes = 0
        # Only an explicitly supplied inert transport is admitted in this source
        # window. Native descriptor/opener construction remains unavailable.
        self._opener = opener

    def _request(self, method, path, credential, body=None, cleanup=False):
        if type(credential) is not str or not credential or '\r' in credential or '\n' in credential:
            raise Refused('authentication credential shape differs')
        installation = f"/app/installations/{self.installation_id}"
        allowed_gets = {"/app", installation, f"/repos/{REPOSITORY}/installation",
                        "/installation/repositories?per_page=100&page=1"}
        allowed = ((method == "GET" and path in allowed_gets and self._gets < 8)
                   or (method == "POST" and path == installation + "/access_tokens" and self._issues == 0
                       and body == {"repository_ids": [self.REPOSITORY_ID], "permissions": self.PERMISSIONS})
                   or (method == "DELETE" and path == "/installation/token" and self._revokes == 0
                       and credential is self._token and self._token is not None))
        if not allowed:
            raise Refused("authentication qualification request or deadline bound")
        self.adapter.check(cleanup=cleanup)
        if method == "GET":
            self._gets += 1
            self.facts["counters"]["getsStarted"] = self._gets
        if method == "POST": self._issues += 1
        if method == "DELETE": self._revokes += 1
        url = "https://api.github.com" + path
        request = urllib.request.Request(url, method=method,
            data=json.dumps(body).encode() if body is not None else None,
            headers={"Authorization": "Bearer " + credential, "Accept": "application/vnd.github+json",
                     "Content-Type": "application/json", "User-Agent": "fsgg-release-successor-auth-only",
                     "X-GitHub-Api-Version": "2022-11-28"})
        try:
            cutoff = self.adapter.work if cleanup else self.adapter.normal
            remaining = (cutoff - self.adapter.check(cleanup=cleanup)) / self.adapter.frequency
            with self._opener.open(request, timeout=min(15, remaining)) as response:
                if (response.status != {"GET": 200, "POST": 201, "DELETE": 204}[method]
                        or response.geturl() != url or response.headers.get("Link") is not None):
                    raise Refused("authentication qualification response identity differs")
                # Cooperative checks do not bound urllib header/chunk parsing.
                # Only independently qualified producer custody can close R1.
                sock = getattr(getattr(getattr(response, "fp", None), "raw", None), "_sock", None)
                if sock is None:
                    raise Refused("authentication qualification response timeout unavailable")
                raw = bytearray()
                while True:
                    left = (cutoff - self.adapter.check(cleanup=cleanup)) / self.adapter.frequency
                    sock.settimeout(min(15, left))
                    part = response.read1(min(65537 - len(raw), 8192))
                    raw.extend(part)
                    self.adapter.check(cleanup=cleanup)
                    if len(raw) > 65536:
                        raise Refused("authentication qualification response bound")
                    if not part:
                        break
                if method == "DELETE":
                    if raw: raise Refused("authentication cleanup response differs")
                    return None
                return json.loads(raw)
        except Exception:
            # Provider bodies, exception messages, JWTs and token bytes are private.
            raise Refused("authentication qualification request failed") from None

    @classmethod
    def _permissions(cls, value):
        if not isinstance(value, dict):
            raise Refused("authentication qualification permissions missing")
        normalized = dict(value)
        if normalized.get("metadata") == "read": normalized.pop("metadata")
        if normalized != cls.PERMISSIONS:
            raise Refused("authentication qualification effective permissions differ")
        return normalized

    def _cause(self, stage, code):
        if self.facts['firstCause'] is None:
            self.facts['firstCause'] = {'stage': stage, 'code': code}

    def _emit(self, kind, request=None, qualified=False):
        if kind == 'ready':
            return self.adapter.emit(kind, request)
        payload = {'facts': json.loads(_auth_canonical(self.facts))}
        if request is not None: payload['request'] = request
        if kind == 'terminal':
            payload.update({'publication': dict(AUTH_PUBLICATION), 'publicationComplete': False,
                            'qualified': qualified, 'credentialReferencesDropped': self._token is None and self._jwt is None})
        return self.adapter.emit(kind, payload)

    def _acquire(self, transport_class):
        """Shared authenticated Request/read proof; caller owns cleanup on EVERY exit."""
        self._stage = 'metadata'
        self._emit('ready', {'binding': self.adapter.binding})
        app = self._request('GET', '/app', self._jwt)
        value = app.get('id') if type(app) is dict else None
        self.facts['app'] = {'status': 'verified' if type(value) is int and value == self.app_id else 'failed',
                             'id': value if type(value) is int and 0 < value < (1 << 64) else None}
        if self.facts['app']['status'] != 'verified':
            self._cause('metadata', 'identity'); raise Refused('App identity differs')
        installation = self._request('GET', f'/app/installations/{self.installation_id}', self._jwt)
        row = self.facts['installation']
        if type(installation) is dict:
            for key, source in [('id', 'id'), ('appId', 'app_id')]:
                value = installation.get(source)
                row[key] = value if type(value) is int and 0 < value < (1 << 64) else None
            row['active'] = installation['suspended_at'] is None if 'suspended_at' in installation else None
            selection = installation.get('repository_selection')
            row['selection'] = selection if type(selection) is str and selection in {'all', 'selected'} else None
        identity_ok = (row['id'] == self.installation_id and row['appId'] == self.app_id
                       and row['active'] is True and row['selection'] is not None
                       and type(installation.get('account')) is dict and installation['account'].get('login') == 'FS-GG') if type(installation) is dict else False
        grants = installation.get('permissions') if type(installation) is dict else None
        row['requiredGrants'] = 'verified' if type(grants) is dict and all(grants.get(k) == v for k, v in self.PERMISSIONS.items()) else 'failed'
        selected = self._request('GET', f'/repos/{REPOSITORY}/installation', self._jwt)
        row['repositoryAssociation'] = 'verified' if (type(selected) is dict
            and type(selected.get('id')) is int and selected['id'] == self.installation_id
            and type(selected.get('app_id')) is int and selected['app_id'] == self.app_id
            and selected.get('suspended_at', True) is None) else 'failed'
        row['status'] = 'verified' if identity_ok and row['requiredGrants'] == row['repositoryAssociation'] == 'verified' else 'failed'
        if row['status'] != 'verified':
            self._cause('metadata', 'grant' if row['requiredGrants'] == 'failed' else 'identity')
            raise Refused('installation metadata differs')
        self._stage = 'issuance'
        self.facts['issuance'] = {'state': 'possible', 'tokenSlot': None}
        self.facts['counters']['issuanceReservations'] = 1
        self._emit('issuance-possible', {'method': 'POST', 'path': f'/app/installations/{self.installation_id}/access_tokens',
            'repositoryId': self.REPOSITORY_ID, 'contents': 'write', 'workflows': 'write'})
        issued = self._request('POST', f'/app/installations/{self.installation_id}/access_tokens', self._jwt,
            {'repository_ids': [self.REPOSITORY_ID], 'permissions': dict(self.PERMISSIONS)})
        if type(issued) is dict and type(issued.get('token')) is str and issued['token']:
            self._token = issued.pop('token')
        if self._token is None: raise Refused('issuance response unavailable')
        self.facts['issuance'] = {'state': 'response-owned', 'tokenSlot': 1}
        self._emit('token-owned')
        self._stage = 'permissions'
        values = issued.get('permissions')
        permission = self.facts['permissions']
        if type(values) is dict:
            for name in ('contents', 'workflows', 'metadata'):
                value = values.get(name, 'absent')
                permission[name] = value if type(value) is str and value in {'absent', 'read', 'write'} else 'unknown'
            permission['extra'] = bool(set(values) - {'contents', 'workflows', 'metadata'})
        try:
            permissions = self._permissions(values)
            permission['status'] = 'verified'
        except Exception:
            permission['status'] = 'failed'; self._cause(self._stage, 'scope'); raise
        self._stage = 'expiry'
        expiry = issued.get('expires_at'); observed = int(self.wall_clock())
        self.facts['expiry']['observedUnix'] = observed if 0 <= observed < (1 << 63) else None
        try:
            if type(expiry) is not str or not re.fullmatch(r'\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}Z', expiry):
                raise Refused('expiry shape differs')
            expires = int(dt.datetime.strptime(expiry, '%Y-%m-%dT%H:%M:%SZ').replace(tzinfo=dt.timezone.utc).timestamp())
            self.facts['expiry']['unix'] = expires if 0 <= expires < (1 << 63) else None
            _auth_require(observed + 15 < expires <= observed + 3600)
            self.facts['expiry']['status'] = 'verified'
        except Exception:
            self.facts['expiry']['status'] = 'failed'; self._cause(self._stage, 'expiry'); raise
        self._stage = 'census'
        census = self._request('GET', '/installation/repositories?per_page=100&page=1', self._token)
        row = self.facts['census']; repositories = census.get('repositories') if type(census) is dict else None
        total = census.get('total_count') if type(census) is dict else None
        row['total'] = total if type(total) is int and 0 <= total < (1 << 64) else None
        if type(repositories) is list and len(repositories) == 1 and type(repositories[0]) is dict:
            value = repositories[0].get('id')
            row['repositoryId'] = value if type(value) is int and 0 < value < (1 << 64) else None
            row['nameMatches'] = repositories[0].get('full_name') == REPOSITORY
        row['status'] = 'verified' if row['total'] == 1 and row['repositoryId'] == self.REPOSITORY_ID and row['nameMatches'] is True else 'failed'
        if row['status'] != 'verified': self._cause(self._stage, 'scope'); raise Refused('census differs')
        identity = {'appId': self.app_id, 'installationId': self.installation_id,
            'repositoryId': self.REPOSITORY_ID, 'permissions': permissions, 'expiresAtUnix': expires}
        transport = transport_class(_ORIGINAL_TAG_TRANSPORT_SEAL, self._token, identity)
        return transport

    def _cleanup(self, transport):
        if self._token is not None:
            self.facts['revocation'] = {'state': 'possible', 'tokenSlot': 1, 'status': None}
            self.facts['counters']['revocationReservations'] = 1
            try:
                self._emit('revocation-possible', {'method': 'DELETE', 'path': '/installation/token', 'tokenSlot': 1})
                self._request('DELETE', '/installation/token', self._token, cleanup=True)
                self.facts['revocation'] = {'state': 'verified', 'tokenSlot': 1, 'status': 204}
                self._emit('revoked')
            except Exception:
                if self.facts['revocation']['state'] != 'verified': self.facts['revocation']['state'] = 'unknown'
                self._cause('cleanup', 'protocol' if self._revokes == 0 else 'transport')
            self._token = None
        self._jwt = None
        if transport is not None: transport._token = None

    def qualify(self, report):
        if self._used:
            raise Refused('authentication qualification session already consumed')
        self._used = True
        report.update({'profile': AUTH_PROFILE, 'qualified': False, 'publicationComplete': False,
                       'publicationEffects': dict(AUTH_PUBLICATION), 'cleanup': 'not-issued', 'failure': None})
        self._stage, transport = 'metadata', None
        try:
            transport = self._acquire(AuthenticationOnlyOriginalTagTransport)
            report['identity'] = transport.identity()
        except Exception:
            self._cause(self._stage, 'protocol' if self.adapter.failed else 'transport')
            if self.facts['issuance']['state'] == 'possible': self.facts['issuance']['state'] = 'unknown'
            report['failure'] = 'authentication-qualification-failed'
        finally:
            self._cleanup(transport)
        report['qualified'] = transport is not None and self.facts['firstCause'] is None and self.facts['revocation']['state'] == 'verified'
        report['requests'] = {'metadataGets': self._gets, 'issuancePosts': self._issues, 'revocationDeletes': self._revokes}
        report['cleanup'] = ('revoked' if self.facts['revocation']['state'] == 'verified' else
                             'revocation-unknown' if self.facts['revocation']['state'] != 'not-started' else
                             'issuance-unknown' if self.facts['issuance']['state'] == 'unknown' else 'not-issued')
        if self.facts['firstCause'] is not None:
            report['failure'] = 'authentication-qualification-failed'
        try:
            self._emit('terminal', qualified=report['qualified'])
        except Exception:
            self._cause('protocol', 'reporting'); report['qualified'] = False
        # authenticationSession belongs to the retained sink; never replace its
        # exact pre-ACK/terminal projection with a final-only summary.
        return report['qualified']  # No credential-bearing object crosses the boundary.


ORIGINAL_TAG_FACTORY_PROFILE = 'utel-rel-21/original-tag-factory-inert/1'
ORIGINAL_TAG_FACTORY_CANDIDATE = {
    'runId': 38290966724, 'attempt': 1, 'artifactId': 11766710761, 'archiveBytes': 32744064,
    'archiveSha256': 'd8b22728b9e36932bf8d31e1ab486b312f2d1aedddf96660c872fc69f97f25ba',
    'contentId': ORIGINAL_TAG_CONTENT, 'sourceSha': ORIGINAL_TAG_SOURCE, 'version': '0.103.0',
    'predecessor': '0.102.0',
    'predecessorContentId': 'sha256:fc16321e011ea616648ccc1dafda00bfc06fa34210fd49ddb008bb56c9ceecf5',
    'manifestSha256': '0b9236c6fd6d3893361d86f67e2d639c02d4e72ce3044b9b6150eeae2259d18f'}
ORIGINAL_TAG_FACTORY_CHECKPOINT = {
    'generation': 2, 'logicalHead': '80bf2bace917316c79032542660a5c00519a828c',
    'stateSHA256': '67018515b0e6b49f96986d7844729461099e950058d20d0a9fa0b8ba3f3864d4'}


class _InertOriginalTagProof(OriginalTagAuthenticationSession):
    """Reuse real issuance proof, without asserting authentication-only wire outcomes.

    These snapshots are inert application reports, NOT durable phase ACKs or
    native credential custody. A reporting failure before an effect refuses it;
    already-owned cleanup remains an obligation even when reporting fails.
    """
    def _emit(self, kind, request=None, qualified=False):
        self.lease_report['facts'] = json.loads(_auth_canonical(self.facts))
        self.lease_report['lastPhase'] = kind
        try:
            self.retain(json.loads(_auth_canonical(self.lease_report)))
        except BaseException:
            self.lease_report['reporting'] = 'failed'
            self._cause('reporting', 'retention')
            if kind not in {'revocation-possible', 'revoked', 'terminal'}:
                raise Refused('inert factory retention failed') from None


class InertOriginalTagCredentialFactory:
    """Selected original proof and lexical lease, with NO native constructor.

    Only injected inert JWT/opener/clock/retention/admission ports are accepted.
    The approved production factory above still refuses before any acquisition.
    This does not sign a JWT, read an environment/key, qualify hard custody or
    supply authority for an original unknown operation or another candidate.
    """
    def __init__(self, binding, candidate, checkpoint, *, phase_adapter, jwt_supplier,
                 opener, admit, retain, report, wall_clock=time.time):
        self.binding = json.loads(_auth_canonical(binding))
        self.candidate = json.loads(_auth_canonical(candidate))
        self.checkpoint = json.loads(_auth_canonical(checkpoint))
        self.adapter, self.jwt_supplier, self.opener = phase_adapter, jwt_supplier, opener
        self.admit, self.retain, self.report, self.wall_clock = admit, retain, report, wall_clock
        self.used = False
        self._validate()

    def _validate(self):
        validate_auth_binding(self.binding)
        if (self.binding['mode'] != 'inert'
                or self.binding['app'] != {'id': 4995487, 'installation': 162873149}
                or _auth_canonical(self.candidate) != _auth_canonical(ORIGINAL_TAG_FACTORY_CANDIDATE)
                or _auth_canonical(self.checkpoint) != _auth_canonical(ORIGINAL_TAG_FACTORY_CHECKPOINT)
                or type(self.adapter) is not AuthenticationPhaseAdapter
                or self.adapter.binding != self.binding
                or not all(callable(x) for x in (self.jwt_supplier, self.admit, self.retain))
                or self.opener is None or type(self.report) is not dict):
            raise Refused('inert original tag factory selection differs')
        self.adapter.check()

    @contextmanager
    def __call__(self):
        if self.used:
            raise Refused('inert original tag factory already consumed')
        self.used = True  # A refused or ambiguous acquisition is never retried.
        self._validate()
        if self.admit() is not True:
            raise Refused('inert original tag pre-credential admission denied')
        self.report.update({'profile': ORIGINAL_TAG_FACTORY_PROFILE, 'mode': 'inert',
            'qualified': False, 'cleanup': 'not-issued', 'reporting': 'passed',
            'firstCause': None, 'historicalNonDispatchProved': False})
        session, transport = None, None
        try:
            # Supplier is called only after complete fixed tuple/mode/admission checks.
            session = _InertOriginalTagProof(self.jwt_supplier(), 4995487, 162873149,
                phase_adapter=self.adapter, opener=self.opener, wall_clock=self.wall_clock)
            session.lease_report, session.retain = self.report, self.retain
            transport = session._acquire(VerifiedOriginalTagTransport)
            transport._opener = self.opener
            remaining = (self.adapter.work - self.adapter.check()) / self.adapter.frequency
            if transport.identity()['expiresAtUnix'] <= self.wall_clock() + remaining + 15:
                session._cause('expiry', 'reserve')
                raise Refused('inert original tag cleanup lifetime unavailable')
            self._validate()
            if self.admit() is not True:
                raise Refused('inert original tag post-proof admission denied')
            self.report['identity'] = transport.identity()
            session._stage = 'consumer'
            yield transport  # Caller context owns every later refusal/send/report exit.
        except BaseException:
            if session is not None:
                session._cause(getattr(session, '_stage', 'acquisition'), 'failed')
                if session.facts['issuance']['state'] == 'possible':
                    session.facts['issuance']['state'] = 'unknown'
            else:
                self.report['firstCause'] = {'stage': 'acquisition', 'code': 'failed'}
            raise
        finally:
            if session is not None:
                try:
                    session._cleanup(transport)
                except BaseException:
                    session._cause('cleanup', 'transport')
                    if session._token is not None:
                        session.facts['revocation'] = {'state': 'unknown', 'tokenSlot': 1, 'status': None}
                finally:
                    session._token = session._jwt = None
                    if transport is not None: transport._token = None
                self.report['firstCause'] = session.facts['firstCause']
                revocation = session.facts['revocation']['state']
                self.report['cleanup'] = ('revoked' if revocation == 'verified' else
                    'revocation-unknown' if revocation != 'not-started' else
                    'issuance-unknown' if session.facts['issuance']['state'] == 'unknown' else 'not-issued')
                self.report['qualified'] = (transport is not None and self.report['firstCause'] is None
                    and revocation == 'verified' and self.report['reporting'] == 'passed')
                session._emit('terminal')
                self.report['firstCause'] = session.facts['firstCause']
                if self.report['firstCause'] is not None:
                    self.report['qualified'] = False
                # Cleanup/reporting failure cannot turn a successful body into success.
                if self.report['firstCause'] is not None and sys.exc_info()[0] is None:
                    raise Refused('inert original tag cleanup or reporting unqualified')


# Only newly selected installer metadata instances opt into these acquisition
# bounds. These are raw HTTP response bytes/application opens, not heap or wire
# memory accounting; historical publication/recovery transports retain defaults.
INSTALL_METADATA_RESPONSE_LIMIT = 2 * 1024 * 1024
INSTALL_METADATA_REQUEST_CEILING = 256


class MetadataRequestBudget:
    """One install process shares this finite counter across reader and writer."""
    def __init__(self):
        self.attempted = {"GET": 0, "POST": 0, "PATCH": 0}
        self.stream_close_failures = 0

    def reserve(self, method):
        if method not in self.attempted or sum(self.attempted.values()) >= INSTALL_METADATA_REQUEST_CEILING:
            raise Refused("install metadata request ceiling exhausted; preserve original operation")
        self.attempted[method] += 1

    def report(self):
        return {"scope": "this process metadata opens; redirects/retries forbidden",
                "ceiling": INSTALL_METADATA_REQUEST_CEILING, "attempted": dict(self.attempted),
                "responseByteLimit": INSTALL_METADATA_RESPONSE_LIMIT, "errorByteLimit": 2049,
                "streamCloseFailures": self.stream_close_failures}


def _close_metadata_stream(response, budget, *, preserving_failure):
    try:
        response.close()
    except Exception:
        budget.stream_close_failures += 1
        if not preserving_failure:
            raise Refused("install metadata stream cleanup failed; response or effect unconfirmed") from None


class _DenyMetadataRedirect(urllib.request.HTTPRedirectHandler):
    def __init__(self, budget):
        self.budget = budget

    def redirect_request(self, req, fp, code, msg, headers, target):
        if fp is not None:
            # Refuse before urllib's ordinary unbounded redirect body drain.
            _close_metadata_stream(fp, self.budget, preserving_failure=True)
        raise Refused("install metadata redirect refused; original request not retried")


def _bounded_metadata_response(response):
    limit = INSTALL_METADATA_RESPONSE_LIMIT
    length = response.headers.get("Content-Length")
    if length is not None and (not length.isascii() or not length.isdecimal() or int(length) > limit):
        raise Refused("install metadata content length exceeds acquisition bound")
    expected = int(length) if length is not None else None
    raw = bytearray()
    while len(raw) <= limit:
        requested = min(65536, limit + 1 - len(raw))
        chunk = response.read(requested)
        if not isinstance(chunk, bytes) or len(chunk) > requested:
            raise Refused("install metadata stream response is malformed")
        raw.extend(chunk)
        if len(raw) > limit:
            raise Refused("install metadata response exceeds acquisition bound")
        if not chunk:
            break
    if expected is not None and len(raw) != expected:
        raise Refused("install metadata response is partial or has conflicting length")
    return bytes(raw)


class GitHubAPI:
    def __init__(self, token: str, *, metadata_budget=None):
        if not token:
            raise Refused("GitHub token is missing")
        if metadata_budget is not None and not isinstance(metadata_budget, MetadataRequestBudget):
            raise Refused("install metadata counter binding differs")
        self.metadata_budget = metadata_budget
        self.token = token
        self.last_request_diagnostic = None

    def _request(self, url: str, method: str = "GET", body: bytes | None = None, content_type: str = "application/json") -> bytes:
        self.last_request_diagnostic = best_effort_request_diagnostic(url, method)
        request = urllib.request.Request(
            url,
            data=body,
            method=method,
            headers={
                "Authorization": f"Bearer {self.token}",
                "Accept": "application/vnd.github+json",
                "Content-Type": content_type,
                "User-Agent": "fsgg-release-successor",
                "X-GitHub-Api-Version": "2022-11-28",
            },
        )
        try:
            if self.metadata_budget is not None:
                parsed = urllib.parse.urlsplit(url)
                if parsed.scheme != "https" or parsed.netloc != "api.github.com" or parsed.fragment:
                    raise Refused("install metadata requires the fixed GitHub API origin")
                self.metadata_budget.reserve(method)  # Before the sole possible open.
                opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), _DenyMetadataRedirect(self.metadata_budget))
                response = opener.open(request, timeout=60)
                try:
                    self.last_request_diagnostic = best_effort_request_diagnostic(url, method, getattr(response, "status", None),
                                                                      getattr(response, "headers", None))
                    return _bounded_metadata_response(response)
                finally:
                    _close_metadata_stream(response, self.metadata_budget, preserving_failure=sys.exc_info()[0] is not None)
            with urllib.request.urlopen(request, timeout=60) as response:
                self.last_request_diagnostic = best_effort_request_diagnostic(url, method, getattr(response, "status", None),
                                                                  getattr(response, "headers", None))
                return response.read()
        except urllib.error.HTTPError as error:
            try:
                row = best_effort_request_diagnostic(url, method, error.code, error.headers, error)
            finally:
                if self.metadata_budget is not None:
                    # Original HTTP status/cause survives a separate close failure.
                    _close_metadata_stream(error, self.metadata_budget, preserving_failure=True)
            self.last_request_diagnostic = row
            if error.code == 404:
                failure = NotFound(url)
                failure.diagnostic = row
                raise failure from error
            limits = ", ".join(f"{name}={row[key]}" for name, key in
                               (("remaining", "remaining"), ("reset", "reset"),
                                ("resource", "resource"), ("retry-after", "retryAfter")) if key in row)
            detail = f" ({limits})" if limits else ""
            failure = Refused(f"GitHub {method} {url} returned HTTP {error.code}{detail}")
            failure.diagnostic = row
            raise failure from error
        except (http.client.HTTPException, OSError):
            if self.metadata_budget is None:
                raise
            raise Refused("install metadata acquisition failed; response or effect unconfirmed") from None

    def get(self, path: str) -> dict:
        return json.loads(self._request(f"https://api.github.com/{path}"))

    def post(self, path: str, body: dict) -> dict:
        return json.loads(self._request(f"https://api.github.com/{path}", "POST", json.dumps(body).encode()))

    def patch(self, path: str, body: dict) -> dict:
        return json.loads(self._request(f"https://api.github.com/{path}", "PATCH", json.dumps(body).encode()))

    def upload_asset(self, release_id: int, name: str, path: pathlib.Path, expected_sha256=None) -> dict:
        url = (
            f"https://uploads.github.com/repos/{REPOSITORY}/releases/{release_id}/assets?"
            + urllib.parse.urlencode({"name": name})
        )
        if expected_sha256 is None:
            raw = path.read_bytes()
        else:
            if (type(expected_sha256) is not str or re.fullmatch(r"[0-9a-f]{64}", expected_sha256) is None
                    or path.is_symlink() or not path.is_file()):
                raise Refused("continuation upload bytes differ from fsynced presend receipt")
            with path.open("rb") as stream:
                raw = stream.read(1048577)
            if not raw or len(raw) > 1048576 or hashlib.sha256(raw).hexdigest() != expected_sha256:
                raise Refused("continuation upload bytes differ from fsynced presend receipt")
        # The same bytes checked here become the HTTP body; no second path read.
        return json.loads(self._request(url, "POST", raw, "application/octet-stream"))

    def download_continuation_candidate(self):
        return self._runtime_read("https://api.github.com/repos/FS-GG/.github/actions/artifacts/12115444808/zip", 33326837, None)

    def download_continuation_result(self):
        return self._runtime_read("https://api.github.com/repos/FS-GG/.github/actions/artifacts/12116094456/zip", 1525, None)

    def download_original_tag_result(self):
        # Actions archive redirects use the existing artifact-reader header set,
        # rather than the release-asset octet-stream media type.
        return self._runtime_read(
            "https://api.github.com/repos/FS-GG/.github/actions/artifacts/11902876694/zip", 65536, None)

    def download_asset(self, asset_id: int) -> bytes:
        url = f"https://api.github.com/repos/{REPOSITORY}/releases/assets/{asset_id}"
        self.last_request_diagnostic = best_effort_request_diagnostic(url, "GET")
        request = urllib.request.Request(
            url,
            headers={"Authorization": f"Bearer {self.token}", "Accept": "application/octet-stream"},
        )
        try:
            with urllib.request.build_opener(_StripAuthOnRedirect).open(request, timeout=60) as response:
                self.last_request_diagnostic = best_effort_request_diagnostic(url, "GET", getattr(response, "status", None),
                                                                  getattr(response, "headers", None))
                return response.read()
        except urllib.error.HTTPError as error:
            row = best_effort_request_diagnostic(url, "GET", error.code, error.headers, error)
            self.last_request_diagnostic = row
            failure = Refused(f"release asset {asset_id} returned HTTP {error.code}")
            failure.diagnostic = row
            raise failure from error

    def _runtime_read(self, url: str, cap: int, accept: str | None = "application/octet-stream", redirects: bool = True) -> bytes:
        """Bound the two read-only downloads selected by runtime recovery."""
        deadline = time.monotonic() + 12
        class Redirect(_StripAuthOnRedirect):
            count = 0
            def redirect_request(self, req, fp, code, msg, headers, target):
                self.count += 1
                if not redirects or self.count > 3 or urllib.parse.urlparse(target).scheme != "https":
                    raise Refused("runtime recovery download redirect bound")
                left = deadline - time.monotonic()
                if left <= 0:
                    raise Refused("runtime recovery download deadline")
                req.timeout = left
                redirected = super().redirect_request(req, fp, code, msg, headers, target)
                redirected.timeout = left
                return redirected
        headers = {"Authorization": f"Bearer {self.token}"}
        if accept is not None:
            headers["Accept"] = accept
        request = urllib.request.Request(url, headers=headers)
        opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), Redirect())
        with opener.open(request, timeout=12) as response:
            if response.status != 200:
                raise Refused("runtime recovery download status differs")
            length = response.headers.get("Content-Length")
            if length is not None and (not length.isascii() or not length.isdecimal() or int(length) > cap):
                raise Refused("runtime recovery download content length bound")
            expected = int(length) if length is not None else None
            transport, sock = response, None
            for _ in range(3):
                sock = getattr(getattr(getattr(transport, "fp", None), "raw", None), "_sock", None)
                if sock is not None:
                    break
                transport = getattr(transport, "fp", None)
            if sock is None:
                raise Refused("runtime recovery transport timeout unavailable")
            parts, size = [], 0
            while size <= cap:
                left = deadline - time.monotonic()
                if left <= 0:
                    raise Refused("runtime recovery download deadline")
                sock.settimeout(left)
                chunk = response.read1(min(65536, cap + 1 - size))
                parts.append(chunk); size += len(chunk)
                if size > cap or time.monotonic() >= deadline:
                    raise Refused("runtime recovery download byte/deadline bound")
                if not chunk or size == expected:
                    break
            if expected is not None and size != expected:
                raise Refused("runtime recovery download content length differs")
            return b"".join(parts)

    def runtime_recovery_json(self, path: str):
        prefix = f"repos/{REPOSITORY}/"
        allowed = path in {prefix + "actions/runs/38063768172", prefix + "actions/artifacts/11675095562"}
        allowed = allowed or re.fullmatch(re.escape(prefix) + r"releases\?per_page=25&page=(?:[1-9]|[1-3][0-9]|40)", path) is not None
        allowed = allowed or re.fullmatch(re.escape(prefix) + r"releases/408709919/assets\?per_page=100&page=[1-4]", path) is not None
        if not allowed:
            raise Refused("runtime recovery JSON endpoint is not selected")
        def unique(items):
            value = {}
            for key, item in items:
                if key in value:
                    raise Refused("runtime recovery duplicate JSON key")
                value[key] = item
            return value
        return json.loads(self._runtime_read("https://api.github.com/" + path, 1048576,
                                           "application/vnd.github+json", False), object_pairs_hook=unique,
                          parse_constant=lambda _: (_ for _ in ()).throw(Refused("nonfinite runtime recovery JSON")))

    def download_runtime_refusal_artifact(self) -> bytes:
        # Actions archive retrieval uses the bounded artifact reader's header set.
        return self._runtime_read(f"https://api.github.com/repos/{REPOSITORY}/actions/artifacts/11675095562/zip", 1413, None)

    def download_runtime_recovery_asset(self, asset_id: int, size: int) -> bytes:
        if type(asset_id) is not int or asset_id <= 0 or type(size) is not int or not 0 < size <= 1048576:
            raise Refused("runtime recovery asset identity/size differs")
        body = self._runtime_read(f"https://api.github.com/repos/{REPOSITORY}/releases/assets/{asset_id}", size)
        if len(body) != size:
            raise Refused("runtime recovery asset size differs")
        return body

    def upload_runtime_recovery_asset(self, release_id: int, raw: bytes) -> dict:
        # One fixed name, exact snapshot bytes, no overwrite/delete or release lookup.
        url = (f"https://uploads.github.com/repos/{REPOSITORY}/releases/{release_id}/assets?"
               + urllib.parse.urlencode({"name": "standalone-telemetry-runtime-evidence.json"}))
        return json.loads(self._request(url, "POST", raw, "application/octet-stream"))


class LiveProvider:
    def __init__(self, api: GitHubAPI, manifest_path: pathlib.Path, github_token: str, nuget_key: str, *, suspension=None, logical_ref=None):
        self.api = api
        self.suspension = suspension
        self.logical_ref = logical_ref
        self.manifest_path = manifest_path
        self.root = manifest_path.parent
        self.manifest = json.loads(manifest_path.read_text())
        self.version = self.manifest["descriptor"]["version"]
        self.source = self.manifest["descriptor"]["sourceSha"]
        self.content_id = self.manifest["contentId"]
        self.tag = f"coherent-set/v{self.version}"
        self.marker = f"release-successor:{self.content_id}"
        self.github_token = github_token
        self.nuget_key = nuget_key
        self.observed = self.root / "feed-observations"
        self.observed.mkdir(exist_ok=True)
        self._runtime_witness = None
        self._runtime_send_consumed = False
        self.last_dispatch_diagnostic = None

    def _authorize_release(self, effect=None, *, ordinary=False):
        if self.version in {"0.103.0", "0.104.0"}:
            from release_successor_journal import ReleaseSuspension, SUSPENDED_REF, ALLOWED_SUCCESSOR
            expected = SUSPENDED_REF if self.version == "0.103.0" else ALLOWED_SUCCESSOR["logicalRef"]
            if not isinstance(self.suspension, ReleaseSuspension) or self.logical_ref != expected:
                raise Refused("direct sensitive provider requires the maintained suspension reader")
            if ordinary and self.version == "0.104.0":
                # One fresh sandwich joins immutable suspension and claim-marker
                # predicates at this sole send boundary. No authorization escapes.
                physical = self.suspension.main()
                _, digest = self.suspension._read_at(physical)
                raw = self.suspension.file(physical, "state/releases/utel-rel-22/release-state.json")
                state = json.loads(raw) if raw is not None else {}
                expected = getattr(self, "_ordinary_expected_journal", None)
                pointer = self.suspension.file(physical, "state/releases/utel-rel-22/release-head.txt")
                if (not isinstance(expected, tuple) or len(expected) != 2
                        or raw != expected[1] or pointer != (expected[0] + "\n").encode()
                        or state.get("contentId") != self.content_id or state.get("sourceSha") != self.source
                        or state.get("version") != self.version or effect is None
                        or state.get("effects", {}).get(effect.identity) != "intent"):
                    raise Refused("ordinary provider no longer binds the exact committed intent")
                if self.suspension.main() != physical:
                    raise Refused("Authority main moved during ordinary provider fence")
                self.suspension.digest = digest
                if "continuation" in state:
                    raise Refused("claimed .104 journal cannot enter ordinary provider")
                return
            self.suspension.authorize(self.logical_ref, self.version)
            if self.version == "0.104.0":
                from release_successor_journal import ContinuationPermit, _claimed_state
                physical = self.suspension.main()
                raw = self.suspension.file(physical, "state/releases/utel-rel-22/release-state.json")
                if self.suspension.main() != physical:
                    raise Refused("Authority main moved during provider continuation fence")
                state = json.loads(raw) if raw is not None else {}
                permit = getattr(self, "_continuation_permit", None)
                if "continuation" in state:
                    if (not _claimed_state(state) or not isinstance(permit, ContinuationPermit) or effect is None
                            or permit.active_effect != effect.identity
                            or state["effects"].get(effect.identity) != "intent"
                            or state["contentId"] != self.content_id or state["sourceSha"] != self.source
                            or state["version"] != self.version
                            or state["continuation"] != permit.record):
                        raise Refused("claimed .104 journal requires its same-invocation consumed send permit")
                elif permit is not None:
                    raise Refused("owned continuation claim disappeared before provider send")

    def _release(self) -> dict | None:
        if getattr(self, "_ordinary_mode", False):
            witness = self._ordinary_release_witness()
            return witness["release"] if witness is not None else None
        try:
            return self.api.get(f"repos/{REPOSITORY}/releases/tags/{self.tag}")
        except NotFound:
            # GitHub's by-tag endpoint hides draft releases even from a token
            # that can list them. A draft's immutable tag/body binding is
            # therefore recovered through the authenticated releases list.
            for page in range(1, 11):
                releases = self.api.get(f"repos/{REPOSITORY}/releases?per_page=100&page={page}")
                matches = [item for item in releases if item.get("tag_name") == self.tag]
                if len(matches) > 1:
                    raise Refused("duplicate releases share the successor tag")
                if matches:
                    return matches[0]
                if len(releases) < 100:
                    return None
            raise Refused("release list exceeds bounded draft lookup")

    def observe_runtime_recovery(self, effect: Effect) -> tuple[Observation, dict]:
        """Complete bounded original draft/asset census, only for the fixed profile."""
        if (effect.identity != "qualification-asset:runtime" or effect.request_digest != self.content_id
                or effect.target_digest != self.content_id):
            raise Refused("runtime recovery effect differs")
        releases = []
        for page in range(1, 41):
            rows = self.api.runtime_recovery_json(f"repos/{REPOSITORY}/releases?per_page=25&page={page}")
            if not isinstance(rows, list) or len(rows) > 25 or any(
                not isinstance(row, dict) or type(row.get("id")) is not int or row["id"] <= 0
                or not isinstance(row.get("tag_name"), str) for row in rows
            ):
                raise Refused("runtime recovery release census malformed")
            releases.extend(rows)
            if len(rows) < 25:
                break
        else:
            raise Refused("runtime recovery release census incomplete")
        if len({row["id"] for row in releases}) != len(releases):
            raise Refused("runtime recovery release census duplicate ID")
        matches = [row for row in releases if row["tag_name"] == self.tag]
        if len(matches) != 1:
            raise Refused("runtime recovery original release is not unique")
        release = matches[0]
        if (release["id"] != 408709919 or release.get("draft") is not True
                or release.get("prerelease") is not False or release.get("target_commitish") != self.source
                or release.get("body") != self.marker):
            raise Refused("runtime recovery original draft binding differs")
        assets = []
        for asset_page in range(1, 5):
            rows = self.api.runtime_recovery_json(f"repos/{REPOSITORY}/releases/{release['id']}/assets?per_page=100&page={asset_page}")
            if not isinstance(rows, list) or len(rows) > 100 or any(
                not isinstance(row, dict) or type(row.get("id")) is not int or row["id"] <= 0
                or not isinstance(row.get("name"), str) or not row["name"]
                or type(row.get("size")) is not int or row["size"] < 0 for row in rows
            ):
                raise Refused("runtime recovery asset census malformed")
            assets.extend(rows)
            if len(rows) < 100:
                break
        else:
            raise Refused("runtime recovery asset census incomplete")
        if len({row["id"] for row in assets}) != len(assets) or len({row["name"] for row in assets}) != len(assets):
            raise Refused("runtime recovery asset census duplicate")
        required = [*(f"{package}.{self.version}.nupkg" for package in PACKAGES), "standalone-telemetry-evidence.json"]
        for prior_name in required:
            prior = [row for row in assets if row["name"] == prior_name]
            payload = (self.root / prior_name).read_bytes()
            if (len(prior) != 1 or prior[0].get("state") != "uploaded" or prior[0]["size"] != len(payload)
                    or prior[0].get("digest") != "sha256:" + hashlib.sha256(payload).hexdigest()):
                raise Refused("runtime recovery verified release asset metadata moved")
        if any(row["name"] in {"stable-channel.json", "release-manifest.json"} for row in assets):
            raise Refused("runtime recovery has a later release asset outside the original intent")
        name = "standalone-telemetry-runtime-evidence.json"
        target = [row for row in assets if row["name"] == name]
        summaries = sorted((row["id"], row["name"], row["size"], row.get("digest")) for row in assets)
        witness = {"releaseId": release["id"], "tag": self.tag, "sourceSha": self.source,
                   "contentId": self.content_id, "name": name, "releasePages": page, "assetPages": asset_page,
                   "releasePopulationSha256": hashlib.sha256(json.dumps(sorted((r["id"], r["tag_name"]) for r in releases)).encode()).hexdigest(),
                   "assetPopulationSha256": hashlib.sha256(json.dumps(summaries).encode()).hexdigest(),
                   "releaseCount": len(releases), "assetCount": len(assets), "complete": True}
        if not target:
            self._runtime_witness = witness
            return Observation("absent"), witness
        asset = target[0]
        expected = (self.root / name).read_bytes()
        digest = hashlib.sha256(expected).hexdigest()
        if digest != self.manifest["descriptor"]["standaloneTelemetry"]["qualificationSha256"]:
            raise Refused("runtime recovery matched local payload differs from original manifest")
        if asset.get("state") != "uploaded" or asset["size"] != len(expected) or asset.get("digest") != "sha256:" + digest:
            raise Refused("runtime recovery target asset metadata differs")
        actual = self.api.download_runtime_recovery_asset(asset["id"], asset["size"])
        if actual != expected:
            raise Refused("runtime recovery target asset bytes differ")
        witness.update(assetId=asset["id"], payloadSha256=digest)
        self._runtime_witness = witness
        return Observation("matched", self.content_id), witness

    def dispatch_runtime_recovery(self, effect: Effect, witness: dict) -> Dispatch:
        self._authorize_release(effect)
        if (self._runtime_send_consumed or witness is not self._runtime_witness
                or effect.identity != "qualification-asset:runtime" or effect.request_digest != self.content_id
                or effect.target_digest != self.content_id
                or witness.get("releaseId") != 408709919 or witness.get("name") != "standalone-telemetry-runtime-evidence.json"
                or witness.get("contentId") != self.content_id or witness.get("sourceSha") != self.source
                or witness.get("complete") is not True or "assetId" in witness):
            raise Refused("runtime recovery upload witness differs")
        self._runtime_send_consumed = True
        raw = (self.root / witness["name"]).read_bytes()
        digest = self.manifest["descriptor"]["standaloneTelemetry"]["qualificationSha256"]
        if hashlib.sha256(raw).hexdigest() != digest:
            raise Refused("runtime recovery upload payload drift")
        try:
            result = self.api.upload_runtime_recovery_asset(witness["releaseId"], raw)
            if (not isinstance(result, dict) or type(result.get("id")) is not int or result["id"] <= 0 or result.get("name") != witness["name"]
                    or result.get("size") != len(raw) or result.get("state") != "uploaded"
                    or result.get("digest") != "sha256:" + digest):
                return Dispatch("unknown")
        except (Refused, OSError, urllib.error.URLError, ValueError, KeyError, TypeError):
            return Dispatch("unknown")
        return Dispatch("applied")

    def _asset(self, name: str) -> bytes | None:
        if getattr(self, "_ordinary_mode", False):
            witness = self._ordinary_release_witness()
            if witness is None: return None
            matches = [row for row in witness["assets"] if row["name"] == name]
            return self.api.download_asset(matches[0]["id"]) if matches else None
        release = self._release()
        if release is None:
            return None
        assets = self.api.get(f"repos/{REPOSITORY}/releases/{release['id']}/assets?per_page=100")
        matches = [row for row in assets if row.get("name") == name]
        if len(matches) > 1:
            raise Refused(f"duplicate release asset: {name}")
        return self.api.download_asset(matches[0]["id"]) if matches else None

    def _feed_url(self, feed: str, package: str) -> str:
        ident = f"{package.lower()}.{self.version.lower()}.nupkg"
        if feed == "github":
            return f"https://nuget.pkg.github.com/FS-GG/download/{package.lower()}/{self.version.lower()}/{ident}"
        return f"https://api.nuget.org/v3-flatcontainer/{package.lower()}/{self.version.lower()}/{ident}"

    def _download_package(self, feed: str, package: str) -> pathlib.Path | None:
        url = self._feed_url(feed, package)
        headers = {"User-Agent": "fsgg-release-successor"}
        if feed == "github":
            headers["Authorization"] = "Basic " + base64.b64encode(f"x:{self.github_token}".encode()).decode()
        request = urllib.request.Request(url, headers=headers)
        try:
            with urllib.request.build_opener(_StripAuthOnRedirect).open(request, timeout=90) as response:
                raw = response.read()
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise Refused(f"{feed} package observation returned HTTP {error.code}") from error
        target = self.observed / f"{feed}-{package}.{self.version}.nupkg"
        target.write_bytes(raw)
        checker = pathlib.Path(__file__).with_name("release-saga.py")
        subprocess.run(
            [sys.executable, str(checker), "verify-external", "--manifest", str(self.manifest_path),
             "--package", package, "--artifact", str(target)],
            check=True, capture_output=True, text=True,
        )
        return target

    def _channel(self) -> dict | None:
        raw = self._asset("stable-channel.json")
        if raw is None:
            return None
        receipt = json.loads(raw)
        if (
            receipt.get("contentId") != self.content_id
            or receipt.get("version") != self.version
            or receipt.get("sourceSha") != self.source
            or not isinstance(receipt.get("promotedAt"), str)
        ):
            raise Refused("stable-channel asset does not bind the candidate")
        return receipt

    def _final_manifest(self) -> dict | None:
        raw = self._asset("release-manifest.json")
        if raw is None:
            return None
        value = json.loads(raw)
        receipt = self._channel()
        if (
            value.get("contentId") != self.content_id
            or value.get("descriptor") != self.manifest["descriptor"]
            or receipt is None
            or value.get("state", {}).get("channelPromotion", {}).get("receipt") != receipt
            or value.get("state", {}).get("channelPromotion", {}).get("state") != "promoted"
            or any(value.get("state", {}).get("feeds", {}).get(feed, {}).get("state") != "verified" for feed in ("github", "nuget"))
        ):
            raise Refused("release-manifest asset is not a complete promoted candidate")
        packages = {row["id"]: row for row in self.manifest["descriptor"]["packages"]}
        for feed in ("github", "nuget"):
            rows = value["state"]["feeds"][feed].get("packages", {})
            for package in PACKAGES:
                row = rows.get(package, {})
                found = self._download_package(feed, package)
                if (
                    row.get("state") != "verified"
                    or row.get("externalPayloadSha256") != packages[package]["artifact"]["payloadSha256"]
                    or found is None
                    or row.get("externalSha256") != hashlib.sha256(found.read_bytes()).hexdigest()
                ):
                    raise Refused(f"{feed}/{package} release asset and public readback differ")
        return value

    def observe(self, effect: Effect) -> Observation:
        identity = effect.identity
        if identity == "tag":
            try:
                tag = self.api.get(f"repos/{REPOSITORY}/git/ref/tags/{self.tag}")
            except NotFound:
                return Observation("absent")
            if getattr(self, "_ordinary_mode", False) and (
                    not isinstance(tag, dict) or tag.get("ref") != f"refs/tags/{self.tag}"
                    or tag.get("object", {}).get("type") != "commit"):
                raise Refused("ordinary semantic tag binding is malformed")
            actual = tag.get("object", {}).get("sha")
            return Observation("matched" if actual == self.source else "mismatched", actual)
        if identity == "draft":
            release = self._release()
            if release is None:
                return Observation("absent")
            valid = (
                release.get("tag_name") == self.tag
                and self.marker in release.get("body", "")
            )
            return Observation("matched" if valid else "mismatched", self.content_id if valid else None)
        if identity.startswith(("github:", "nuget:")):
            feed, package = identity.split(":", 1)
            try:
                found = self._download_package(feed, package)
            except (Refused, subprocess.CalledProcessError):
                return Observation("mismatched")
            return Observation("matched", effect.target_digest) if found else Observation("absent")
        if identity.startswith("archive-asset:"):
            package = identity.split(":", 1)[1]
            if package not in PACKAGES:
                raise Refused("unknown archive asset package")
            raw = self._asset(f"{package}.{self.version}.nupkg")
            if raw is None:
                return Observation("absent")
            digest = hashlib.sha256(raw).hexdigest()
            return Observation("matched" if digest == effect.target_digest else "mismatched", digest)
        if identity.startswith("qualification-asset:"):
            name = {
                "qualification-asset:evidence": "standalone-telemetry-evidence.json",
                "qualification-asset:runtime": "standalone-telemetry-runtime-evidence.json",
            }.get(identity)
            if name is None:
                raise Refused("unknown qualification asset")
            raw = self._asset(name)
            if raw is None:
                return Observation("absent")
            candidate = (self.root / name).read_bytes()
            return Observation("matched" if raw == candidate else "mismatched", self.content_id if raw == candidate else None)
        if identity == "channel-asset":
            return Observation("matched", self.content_id) if self._channel() else Observation("absent")
        if identity == "manifest-asset":
            return Observation("matched", self.content_id) if self._final_manifest() else Observation("absent")
        if identity == "promote":
            release = self._release()
            if release is None:
                return Observation("absent")
            if release.get("draft"):
                return Observation("absent")
            return Observation("matched", self.content_id) if self._final_manifest() else Observation("mismatched")
        raise Refused(f"unknown release effect {identity}")

    def _prepare_manifest_asset(self) -> pathlib.Path:
        if self._channel() is None:
            raise Refused("channel receipt is not externally readable")
        checker = pathlib.Path(__file__).with_name("release-saga.py")
        for feed in ("github", "nuget"):
            observations = []
            for package in PACKAGES:
                found = self._download_package(feed, package)
                if found is None:
                    raise Refused(f"{feed} package is not externally readable")
                observations.extend(("--observed", f"{package}={found}"))
            subprocess.run(
                [sys.executable, str(checker), "record-observed", "--manifest", str(self.manifest_path),
                 "--feed", feed, *observations, "--detail", "successor protected-journal readback"],
                check=True,
            )
        value = json.loads(self.manifest_path.read_text())
        receipt = self._channel()
        assert receipt is not None
        value["state"]["channelPromotion"] = {"state": "promoted", "promotedAt": receipt["promotedAt"], "receipt": receipt}
        value["state"]["phase"] = "promoted"
        value["state"]["updatedAt"] = receipt["promotedAt"]
        output = self.root / "promoted-release-manifest.json"
        output.write_text(json.dumps(value, sort_keys=True, separators=(",", ":")) + "\n")
        return output

    def _prepare_channel_asset(self):
        previous = json.loads((self.root / "previous-stable-channel.json").read_text())
        if (
            previous.get("version") != self.manifest["descriptor"].get("previousStableVersion")
            or previous.get("contentId") != self.manifest["descriptor"].get("previousStableContentId")
        ):
            raise Refused("stable predecessor changed")
        receipt = {
            "contentId": self.content_id, "version": self.version, "sourceSha": self.source,
            "promotedAt": dt.datetime.now(dt.timezone.utc).isoformat().replace("+00:00", "Z"),
        }
        output = self.root / "stable-channel.json"
        output.write_text(json.dumps(receipt, sort_keys=True, separators=(",", ":")) + "\n")
        return output

    def prepare_continuation_asset(self, effect):
        if effect.identity not in {"channel-asset", "manifest-asset"}:
            raise Refused("continuation preparation has a different effect")
        prepared = getattr(self, "_continuation_assets", None)
        if prepared is None:
            prepared = self._continuation_assets = {}
        if effect.identity in prepared:
            raise Refused("continuation payload was already prepared; no timestamp regeneration")
        path = self._prepare_channel_asset() if effect.identity == "channel-asset" else self._prepare_manifest_asset()
        raw = path.read_bytes()
        if not raw or len(raw) > 1048576 or path.is_symlink():
            raise Refused("continuation payload is not bounded regular source output")
        prepared[effect.identity] = (path, raw)
        return {"name": "stable-channel.json" if effect.identity == "channel-asset" else "release-manifest.json",
                "sha256": hashlib.sha256(raw).hexdigest(), "bytes": len(raw)}

    def _continuation_asset_path(self, effect):
        prepared = getattr(self, "_continuation_assets", {}).get(effect.identity)
        if prepared is None:
            raise Refused("continuation requires prepared exact source-owned payload")
        path, raw = prepared
        if path.is_symlink() or not path.is_file() or path.read_bytes() != raw:
            raise Refused("continuation prepared payload changed")
        return path

    def _ordinary_release_witness(self):
        """One selected release and complete bounded asset population, or absence."""
        try:
            release = self.api.get(f"repos/{REPOSITORY}/releases/tags/{self.tag}")
        except NotFound:
            matches = []; ids = set()
            for page in range(1, 11):
                rows = self.api.get(f"repos/{REPOSITORY}/releases?per_page=100&page={page}")
                if not isinstance(rows, list) or len(rows) > 100:
                    raise Refused("ordinary draft lookup population is malformed")
                for row in rows:
                    if (not isinstance(row, dict) or type(row.get("id")) is not int or row["id"] <= 0
                            or not isinstance(row.get("tag_name"), str) or row["id"] in ids):
                        raise Refused("ordinary draft lookup identity is malformed or duplicate")
                    ids.add(row["id"])
                    if row["tag_name"] == self.tag: matches.append(row)
                if len(rows) < 100: break
            else:
                raise Refused("ordinary draft lookup exceeds complete population bound")
            if len(matches) > 1: raise Refused("ordinary successor release is ambiguous")
            if not matches: return None
            release = matches[0]
        if (not isinstance(release, dict) or type(release.get("id")) is not int or release["id"] <= 0
                or release.get("tag_name") != self.tag or release.get("target_commitish") != self.source
                or release.get("body") != self.marker or type(release.get("draft")) is not bool
                or release.get("prerelease") is not False):
            raise Refused("ordinary release witness does not bind exact candidate")
        rows = self.api.get(f"repos/{REPOSITORY}/releases/{release['id']}/assets?per_page=100")
        allowed = [*(f"{package}.{self.version}.nupkg" for package in PACKAGES),
                   "standalone-telemetry-evidence.json", "standalone-telemetry-runtime-evidence.json",
                   "stable-channel.json", "release-manifest.json"]
        if (not isinstance(rows, list) or len(rows) > 7
                or any(not isinstance(row, dict) or type(row.get("id")) is not int or row["id"] <= 0
                       or row.get("name") not in allowed or row.get("state") != "uploaded"
                       or type(row.get("size")) is not int or row["size"] < 0 for row in rows)
                or len({row["id"] for row in rows}) != len(rows)
                or len({row["name"] for row in rows}) != len(rows)):
            raise Refused("ordinary complete asset population is malformed or duplicate")
        return {"release": release, "assets": rows, "names": {row["name"] for row in rows}, "allowed": allowed}

    def ordinary_visibility(self, effect):
        """Read-only hint for this pending effect; never payload or effect authority."""
        identity = effect.identity
        if identity == "tag":
            try: value = self.api.get(f"repos/{REPOSITORY}/git/ref/tags/{self.tag}")
            except NotFound: return "absent"
            if (not isinstance(value, dict) or value.get("ref") != f"refs/tags/{self.tag}"
                    or value.get("object", {}).get("type") != "commit"
                    or value["object"].get("sha") != self.source):
                raise Refused("ordinary tag witness does not bind candidate")
            return "potential"
        if identity.startswith(("github:", "nuget:")):
            observation = self.observe(effect)  # Conservative authenticated package validation.
            if observation.state == "absent": return "absent"
            if observation.state == "matched" and observation.observed_digest == effect.target_digest: return "potential"
            raise Refused("ordinary pending package witness is unknown or mismatched")
        witness = self._ordinary_release_witness()
        if witness is None:
            if identity == "draft": return "absent"
            raise Refused("ordinary pending effect lost its verified release")
        names = witness["names"]; allowed = witness["allowed"]
        if identity == "draft":
            if names or witness["release"]["draft"] is not True:
                raise Refused("ordinary pending draft has unexpected assets or promotion")
            return "potential"
        if identity == "promote":
            if names != set(allowed): raise Refused("ordinary promotion population is incomplete")
            return "absent" if witness["release"]["draft"] else "potential"
        if identity.startswith("archive-asset:"): name = f"{identity.split(':', 1)[1]}.{self.version}.nupkg"
        else:
            name = {"qualification-asset:evidence": allowed[3], "qualification-asset:runtime": allowed[4],
                    "channel-asset": allowed[5], "manifest-asset": allowed[6]}.get(identity)
        if name not in allowed: raise Refused("ordinary visibility effect is unknown")
        index = allowed.index(name)
        if (not set(allowed[:index]) <= names or not names <= set(allowed[:index + 1])
                or witness["release"]["draft"] is not True):
            raise Refused("ordinary pending asset population is incomplete or out of order")
        return "potential" if name in names else "absent"

    def prepare_ordinary_dispatch(self, effect):
        """Complete slow payload checks before the execution layer's fresh admission."""
        if hasattr(self, "_ordinary_prepared"):
            raise Refused("ordinary prepared send was not consumed")
        identity = effect.identity
        release = None; path = None; digest = None
        if identity in {"channel-asset", "manifest-asset"}:
            prepared = getattr(self, "_ordinary_asset_bytes", {})
            if identity in prepared: raise Refused("ordinary payload cannot be regenerated after one send")
            path = self._prepare_channel_asset() if identity == "channel-asset" else self._prepare_manifest_asset()
            raw = path.read_bytes()
            if not raw or len(raw) > 1048576 or path.is_symlink(): raise Refused("ordinary generated payload exceeds custody bound")
            digest = hashlib.sha256(raw).hexdigest()
            self._ordinary_asset_bytes = {**prepared, identity: (path, raw)}
        if identity not in {"tag", "draft"} and not identity.startswith(("github:", "nuget:")):
            witness = self._ordinary_release_witness()
            if witness is None or witness["release"]["draft"] is not True:
                raise Refused("ordinary send has no exact draft release")
            release = witness["release"]
            if identity == "promote" and self._final_manifest() is None:
                raise Refused("ordinary promotion requires complete semantic manifest")
        self._ordinary_prepared = (identity, release, path, digest)

    def continuation_visibility(self, effect):
        """Absence only from one validated witness, never matched payload proof."""
        witness = self.continuation_release_witness()
        if effect.identity == "promote":
            return "absent" if witness["draft"] is True else "potential"
        name = {"channel-asset": "stable-channel.json", "manifest-asset": "release-manifest.json"}.get(effect.identity)
        if name is None:
            raise Refused("continuation visibility is outside remaining actions")
        return "potential" if name in witness["names"] else "absent"

    def continuation_release_witness(self):
        release = self._release()
        if (not isinstance(release, dict) or release.get("id") != 416097241 or release.get("tag_name") != self.tag
                or release.get("target_commitish") != self.source or release.get("body") != self.marker
                or type(release.get("draft")) is not bool or release.get("prerelease") is not False):
            raise Refused("original .104 release identity differs")
        rows = self.api.get(f"repos/{REPOSITORY}/releases/416097241/assets?per_page=100")
        allowed = {*(f"{package}.{self.version}.nupkg" for package in PACKAGES),
                   "standalone-telemetry-evidence.json", "standalone-telemetry-runtime-evidence.json",
                   "stable-channel.json", "release-manifest.json"}
        if (not isinstance(rows, list) or not 5 <= len(rows) <= 7
                or any(not isinstance(row, dict) or type(row.get("id")) is not int or row["id"] <= 0
                       or row.get("name") not in allowed or row.get("state") != "uploaded"
                       or type(row.get("size")) is not int or row["size"] < 0 for row in rows)
                or len({row["id"] for row in rows}) != len(rows) or len({row["name"] for row in rows}) != len(rows)
                or not allowed - {"stable-channel.json", "release-manifest.json"} <= {row["name"] for row in rows}):
            raise Refused("original .104 complete asset population differs")
        return {"releaseId": release["id"], "draft": release["draft"],
                "assetsSHA256": hashlib.sha256(json.dumps(rows, sort_keys=True, separators=(",", ":")).encode()).hexdigest(),
                "names": sorted(row["name"] for row in rows)}

    def dispatch(self, effect: Effect) -> Dispatch:
        ordinary = self.__dict__.pop("_ordinary_prepared", None)
        if ordinary is not None and ordinary[0] != effect.identity:
            raise Refused("ordinary prepared effect differs")
        self._authorize_release(effect, ordinary=ordinary is not None)
        if ordinary is not None:
            check = getattr(self, "_ordinary_deadline_check", None)
            if check is not None: check()
        identity = effect.identity
        self.last_dispatch_diagnostic = None
        if isinstance(self.api, GitHubAPI):
            self.api.last_request_diagnostic = None  # Do not join an earlier observation to this send.
        try:
            if identity == "tag":
                self.api.post(f"repos/{REPOSITORY}/git/refs", {"ref": f"refs/tags/{self.tag}", "sha": self.source})
            elif identity == "draft":
                self.api.post(
                    f"repos/{REPOSITORY}/releases",
                    {"tag_name": self.tag, "target_commitish": self.source, "name": f"FS.GG coherent set {self.version}",
                     "body": self.marker, "draft": True, "prerelease": False},
                )
            elif identity.startswith(("github:", "nuget:")):
                feed, package = identity.split(":", 1)
                key = self.github_token if feed == "github" else self.nuget_key
                source = "https://nuget.pkg.github.com/FS-GG/index.json" if feed == "github" else "https://api.nuget.org/v3/index.json"
                if not key:
                    raise Refused(f"{feed} publishing credential is missing")
                subprocess.run(
                    ["dotnet", "nuget", "push", str(self.root / f"{package}.{self.version}.nupkg"),
                     "--source", source, "--api-key", key],
                    check=True, capture_output=True, text=True,
                )
            elif identity.startswith(("archive-asset:", "qualification-asset:")):
                if identity.startswith("archive-asset:"):
                    package = identity.split(":", 1)[1]
                    if package not in PACKAGES:
                        raise Refused("unknown archive asset package")
                    name = f"{package}.{self.version}.nupkg"
                else:
                    name = {
                        "qualification-asset:evidence": "standalone-telemetry-evidence.json",
                        "qualification-asset:runtime": "standalone-telemetry-runtime-evidence.json",
                    }.get(identity)
                    if name is None:
                        raise Refused("unknown qualification asset")
                release = ordinary[1] if ordinary is not None else self._release()
                if release is None or not release.get("draft"):
                    raise Refused("draft release is unavailable")
                self.api.upload_asset(release["id"], name, self.root / name)
            elif identity == "channel-asset":
                output = ordinary[2] if ordinary is not None else self._continuation_asset_path(effect) if getattr(self, "_continuation_permit", None) is not None else self._prepare_channel_asset()
                release = ordinary[1] if ordinary is not None else self._release()
                if release is None or not release.get("draft"):
                    raise Refused("draft release is unavailable")
                if ordinary is not None:
                    self.api.upload_asset(release["id"], output.name, output, ordinary[3])
                elif getattr(self, "_continuation_permit", None) is not None:
                    digest = hashlib.sha256(self._continuation_assets[effect.identity][1]).hexdigest()
                    self.api.upload_asset(release["id"], output.name, output, digest)
                else:
                    self.api.upload_asset(release["id"], output.name, output)
            elif identity == "manifest-asset":
                output = ordinary[2] if ordinary is not None else self._continuation_asset_path(effect) if getattr(self, "_continuation_permit", None) is not None else self._prepare_manifest_asset()
                release = ordinary[1] if ordinary is not None else self._release()
                if release is None or not release.get("draft"):
                    raise Refused("draft release is unavailable")
                if ordinary is not None:
                    self.api.upload_asset(release["id"], "release-manifest.json", output, ordinary[3])
                elif getattr(self, "_continuation_permit", None) is not None:
                    digest = hashlib.sha256(self._continuation_assets[effect.identity][1]).hexdigest()
                    self.api.upload_asset(release["id"], "release-manifest.json", output, digest)
                else:
                    self.api.upload_asset(release["id"], "release-manifest.json", output)
            elif identity == "promote":
                release = ordinary[1] if ordinary is not None else self._release()
                if release is None or not release.get("draft") or (ordinary is None and self._final_manifest() is None):
                    raise Refused("complete draft release is unavailable")
                self.api.patch(f"repos/{REPOSITORY}/releases/{release['id']}", {"draft": False, "make_latest": "true"})
            else:
                raise Refused(f"unknown release effect {identity}")
        except (Refused, subprocess.CalledProcessError, OSError, urllib.error.URLError, ValueError) as error:
            # A transport failure can be after the provider applied the write.
            category = ("provider-refused" if isinstance(error, Refused) else
                        "process-failure" if isinstance(error, subprocess.CalledProcessError) else
                        "transport-failure" if isinstance(error, urllib.error.URLError) else
                        "os-failure" if isinstance(error, OSError) else "protocol-failure")
            try:
                row = {"category": category}
                diagnostic = getattr(error, "diagnostic", None)
                if diagnostic is None and isinstance(error, (urllib.error.URLError, ValueError)):
                    diagnostic = getattr(self.api, "last_request_diagnostic", None)
                if diagnostic is not None:
                    row["request"] = diagnostic
                if isinstance(error, subprocess.CalledProcessError):
                    row["returnCode"] = error.returncode  # Never retain command/API key or raw subprocess output.
                self.last_dispatch_diagnostic = sanitized_dispatch_diagnostic(row)
            except BaseException:
                self.last_dispatch_diagnostic = {"category": category, "diagnosticUnavailable": True}
            return Dispatch("unknown")
        return Dispatch("applied")
