"""Live GitHub, NuGet and release-asset observations for UTEL-REL-01.

The provider never treats a successful push response as settled. Callers must
run release_successor_execution.advance again for independent readback.
"""

from __future__ import annotations

import base64
import datetime as dt
import hashlib
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
        if self._identity["expiresAtUnix"] <= time.time():
            raise Refused("original tag capability expired")
        return json.loads(json.dumps(self._identity))

    def dispatch(self, effect, deadline, clock=time.monotonic):
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
            with urllib.request.build_opener(NoRedirect).open(request, timeout=min(60, deadline - clock())) as response:
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


class GitHubAPI:
    def __init__(self, token: str):
        if not token:
            raise Refused("GitHub token is missing")
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
            with urllib.request.urlopen(request, timeout=60) as response:
                self.last_request_diagnostic = best_effort_request_diagnostic(url, method, getattr(response, "status", None),
                                                                  getattr(response, "headers", None))
                return response.read()
        except urllib.error.HTTPError as error:
            row = best_effort_request_diagnostic(url, method, error.code, error.headers, error)
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

    def get(self, path: str) -> dict:
        return json.loads(self._request(f"https://api.github.com/{path}"))

    def post(self, path: str, body: dict) -> dict:
        return json.loads(self._request(f"https://api.github.com/{path}", "POST", json.dumps(body).encode()))

    def patch(self, path: str, body: dict) -> dict:
        return json.loads(self._request(f"https://api.github.com/{path}", "PATCH", json.dumps(body).encode()))

    def upload_asset(self, release_id: int, name: str, path: pathlib.Path) -> dict:
        url = (
            f"https://uploads.github.com/repos/{REPOSITORY}/releases/{release_id}/assets?"
            + urllib.parse.urlencode({"name": name})
        )
        return json.loads(self._request(url, "POST", path.read_bytes(), "application/octet-stream"))

    def download_original_tag_result(self):
        return self._runtime_read(
            "https://api.github.com/repos/FS-GG/.github/actions/artifacts/11902876694/zip", 65536)

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
    def __init__(self, api: GitHubAPI, manifest_path: pathlib.Path, github_token: str, nuget_key: str):
        self.api = api
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

    def _release(self) -> dict | None:
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

    def dispatch(self, effect: Effect) -> Dispatch:
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
                release = self._release()
                if release is None or not release.get("draft"):
                    raise Refused("draft release is unavailable")
                self.api.upload_asset(release["id"], name, self.root / name)
            elif identity == "channel-asset":
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
                release = self._release()
                if release is None or not release.get("draft"):
                    raise Refused("draft release is unavailable")
                self.api.upload_asset(release["id"], output.name, output)
            elif identity == "manifest-asset":
                output = self._prepare_manifest_asset()
                release = self._release()
                if release is None or not release.get("draft"):
                    raise Refused("draft release is unavailable")
                self.api.upload_asset(release["id"], "release-manifest.json", output)
            elif identity == "promote":
                release = self._release()
                if release is None or not release.get("draft") or self._final_manifest() is None:
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
