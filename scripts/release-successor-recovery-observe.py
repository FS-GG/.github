#!/usr/bin/env python3
"""Closed GET-only observation of the original partial coherent publication."""
import datetime
import hashlib
import json
import os
import pathlib
import re
import time
import urllib.error
import urllib.request

REPOSITORY = "FS-GG/.github"
AUTHORITY = "FS-GG/FS.GG.Coordination.Authority"
SOURCE = "79051e56b025dadfc6fcaabd58b79e33f2854928"
TAG = "coherent-set/v0.101.0"
PREDECESSOR = "coherent-set/v0.100.0"
CONTENT = "sha256:e8ed439047f663dfcb34aba1152ffd4c6966a0c3a27be0ebb48eebcf47e26996"
MARKER = "release-successor:" + CONTENT
API = "https://api.github.com/"
HEADERS = ("date", "x-github-request-id", "x-ratelimit-resource", "x-ratelimit-limit",
           "x-ratelimit-used", "x-ratelimit-remaining", "x-ratelimit-reset", "retry-after", "link", "content-length")

class Refused(RuntimeError):
    def __init__(self, code, evidence=None):
        super().__init__(code)
        self.evidence = evidence

class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        raise Refused("redirect-refused")


def guard(env):
    expected = {"GITHUB_REPOSITORY": REPOSITORY, "GITHUB_EVENT_NAME": "workflow_dispatch",
                "GITHUB_REF": "refs/heads/main", "GITHUB_ACTOR": "EHotwagner",
                "GITHUB_RUN_ATTEMPT": "1", "APP_ID": "4882140"}
    if any(env.get(k) != v for k, v in expected.items()):
        raise Refused("native-context-refused")
    if not re.fullmatch(r"[0-9a-f]{40}", env.get("GITHUB_SHA", "")):
        raise Refused("observer-source-refused")
    if not re.fullmatch(r"[1-9][0-9]*", env.get("GITHUB_RUN_ID", "")):
        raise Refused("observer-run-refused")
    installation = env.get("EXPECTED_INSTALLATION_ID", "")
    if not re.fullmatch(r"[1-9][0-9]*", installation) or env.get("ACTUAL_INSTALLATION_ID") != installation:
        raise Refused("installation-identity-refused")
    if not env.get("ACTIONS_TOKEN") or not env.get("LEDGER_TOKEN"):
        raise Refused("credential-unavailable")


def allowed(credential, path):
    if path == "rate_limit":
        return credential in {"actions", "ledger"}
    if credential == "ledger":
        return path == "installation/repositories?per_page=100"
    return credential == "actions" and (
        path == f"repos/{REPOSITORY}/releases/tags/{PREDECESSOR}"
        or re.fullmatch(r"repos/FS-GG/\.github/releases\?per_page=100&page=([1-9]|10)", path)
        or re.fullmatch(r"repos/FS-GG/\.github/releases/[1-9][0-9]*", path))


class Reader:
    def __init__(self, tokens, rawdir, opener=None, clock=time.monotonic):
        self.tokens, self.rawdir, self.clock = tokens, rawdir, clock
        self.deadline = clock() + 180
        self.opener = opener or urllib.request.build_opener(NoRedirect())
        self.calls, self.bytes, self.evidence = 0, 0, []
        rawdir.mkdir(mode=0o700, parents=False, exist_ok=False)

    def get(self, credential, path):
        if not allowed(credential, path):
            raise Refused("GET-allowlist-refused")
        remaining = self.deadline - self.clock()
        if remaining <= 0 or self.calls >= 17:
            raise Refused("request-or-deadline-bound")
        self.calls += 1
        request_deadline = min(self.deadline, self.clock() + min(12, remaining))
        req = urllib.request.Request(API + path, method="GET", headers={
            "Authorization": "Bearer " + self.tokens[credential],
            "Accept": "application/vnd.github+json", "X-GitHub-Api-Version": "2022-11-28"})
        evidence = {"credential": credential, "path": path, "status": None,
                    "headers": {}, "bytes": 0, "complete": False}
        self.evidence.append(evidence)
        chunks, size, primary = [], 0, None
        try:
            try:
                response = self.opener.open(req, timeout=min(12, remaining))
            except urllib.error.HTTPError as error:
                response = error
            with response:
                evidence["status"] = response.code
                evidence["headers"] = {k: response.headers[k] for k in HEADERS if k in response.headers}
                if response.geturl() != API + path:
                    raise Refused("response-origin-refused")
                declared = evidence["headers"].get("content-length")
                if declared is not None and not re.fullmatch(r"[0-9]+", declared):
                    raise Refused("response-content-length-refused")
                expected_length = int(declared) if declared is not None else None
                if expected_length is not None and (expected_length > 2 * 1024 * 1024
                        or expected_length + self.bytes > 24 * 1024 * 1024):
                    raise Refused("response-byte-bound")
                if self.clock() > request_deadline:
                    raise Refused("request-deadline-bound")
                # HTTPError wraps HTTPResponse; both must expose a bounded socket.
                transport = response
                sock = None
                for _ in range(3):
                    sock = getattr(getattr(getattr(transport, "fp", None), "raw", None), "_sock", None)
                    if sock is not None:
                        break
                    transport = getattr(transport, "fp", None)
                if sock is None:
                    raise Refused("transport-timeout-unavailable")
                if expected_length == 0:
                    evidence["complete"] = True
                while not evidence["complete"] and size <= 2 * 1024 * 1024:
                    remaining = request_deadline - self.clock()
                    if remaining <= 0:
                        raise Refused("request-deadline-bound")
                    sock.settimeout(remaining)
                    chunk = response.read1(min(65536, 2 * 1024 * 1024 + 1 - size))
                    if chunk:
                        chunks.append(chunk)
                        size += len(chunk)
                    if self.clock() > request_deadline:
                        raise Refused("request-deadline-bound")
                    if not chunk or size == expected_length:
                        # HTTPResponse closes fp/socket when Content-Length reaches
                        # zero. Do not settimeout on that now-closed socket for EOF.
                        evidence["complete"] = True
                        break
                if size > 2 * 1024 * 1024 or self.bytes + size > 24 * 1024 * 1024:
                    evidence["complete"] = False
                    raise Refused("response-byte-bound")
                declared = evidence["headers"].get("content-length")
                if declared is not None and (not re.fullmatch(r"[0-9]+", declared) or int(declared) != size):
                    evidence["complete"] = False
                    raise Refused("response-content-length-refused")
                if evidence["status"] != 200:
                    raise Refused("HTTP-status-" + str(evidence["status"]))
        except Exception as error:
            primary = error
        raw = b"".join(chunks)
        evidence.update(bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
        retained = raw[:max(0, min(2 * 1024 * 1024, 24 * 1024 * 1024 - self.bytes))]
        self.bytes += len(retained)
        evidence["retainedBytes"] = len(retained)
        # Partial evidence is explicitly labelled; it is never parsed as a response.
        try:
            target = self.rawdir / f"{self.calls:02d}.body"
            with target.open("xb") as output:
                os.chmod(target, 0o600)
                output.write(retained)
        except Exception as error:
            evidence["rawRetentionFailure"] = type(error).__name__
            if primary is None:
                primary = error
        if primary is not None:
            raise primary
        if self.clock() > self.deadline:
            raise Refused("collection-deadline-bound")
        try:
            return json.loads(raw), evidence["headers"]
        except (ValueError, UnicodeError) as error:
            raise Refused("response-JSON-refused") from error


def next_page(headers, page):
    link = headers.get("link", "")
    if not link:
        return False
    relations = {}
    for part in link.split(","):
        match = re.fullmatch(r'\s*<([^>]+)>; rel="(next|prev|first|last)"\s*', part)
        if not match or match[2] in relations:
            raise Refused("pagination-shape-refused")
        destination = re.fullmatch(re.escape(API + f"repos/{REPOSITORY}/releases?per_page=100&page=") + r"([1-9][0-9]*)", match[1])
        if not destination:
            raise Refused("pagination-destination-refused")
        relations[match[2]] = int(destination[1])
    if (relations.get("first", 1) != 1
            or ("prev" in relations and (page == 1 or relations["prev"] != page - 1))
            or ("last" in relations and relations["last"] < page)
            or ("next" in relations and relations["next"] != page + 1)
            or ("next" in relations and "last" in relations and relations["next"] > relations["last"])
            or ("next" not in relations and relations.get("last", page) != page)):
        raise Refused("pagination-completeness-refused")
    return "next" in relations


def draft(reader):
    prior, _ = reader.get("actions", f"repos/{REPOSITORY}/releases/tags/{PREDECESSOR}")
    if not isinstance(prior, dict) or prior.get("tag_name") != PREDECESSOR or type(prior.get("id")) is not int:
        raise Refused("predecessor-visibility-refused")
    targets, seen, prior_seen = [], set(), False
    for page in range(1, 11):
        rows, headers = reader.get("actions", f"repos/{REPOSITORY}/releases?per_page=100&page={page}")
        if not isinstance(rows, list) or not rows or len(rows) > 100:
            raise Refused("release-page-shape-refused")
        for row in rows:
            if not isinstance(row, dict) or type(row.get("id")) is not int or row["id"] <= 0 or row["id"] in seen:
                raise Refused("release-page-identity-refused")
            seen.add(row["id"])
            prior_seen |= row["id"] == prior["id"] and row.get("tag_name") == PREDECESSOR
            if row.get("tag_name") == TAG:
                targets.append(row)
        more = next_page(headers, page)
        if not more:
            break
    else:
        raise Refused("release-page-bound")
    if not prior_seen or len(targets) != 1:
        raise Refused("draft-population-unknown")
    selected = targets[0]
    detail, _ = reader.get("actions", f"repos/{REPOSITORY}/releases/{selected['id']}")
    if not isinstance(detail, dict) or any(detail.get(k) != selected.get(k) for k in
        ("id", "node_id", "tag_name", "draft", "prerelease", "target_commitish", "body", "created_at", "updated_at")):
        raise Refused("draft-detail-identity-refused")
    for key in ("created_at", "updated_at"):
        timestamp = detail.get(key)
        try:
            if not isinstance(timestamp, str):
                raise ValueError()
            datetime.datetime.strptime(timestamp, "%Y-%m-%dT%H:%M:%SZ")
        except ValueError as error:
            raise Refused("draft-timestamp-refused") from error
    body = detail.get("body")
    if (detail.get("target_commitish") != SOURCE or detail.get("draft") is not True
            or detail.get("prerelease") is not False or not isinstance(body, str) or MARKER not in body
            or not isinstance(detail.get("node_id"), str) or not detail["node_id"]):
        raise Refused("draft-original-binding-refused", {
            "id": detail.get("id"), "tag": detail.get("tag_name"),
            "bodyBytes": len(body.encode()) if isinstance(body, str) else None,
            "bodySHA256": hashlib.sha256(body.encode()).hexdigest() if isinstance(body, str) else None,
            "bodyEqualsMarker": body == MARKER, "bodyContainsMarker": isinstance(body, str) and MARKER in body,
            "originalSourceMatches": detail.get("target_commitish") == SOURCE})
    result = {k: detail.get(k) for k in ("id", "node_id", "tag_name", "draft", "prerelease",
                                      "target_commitish", "created_at", "updated_at")}
    result.update(repository=REPOSITORY, populationComplete=True, bodyMarker=MARKER,
                  bodyEqualsMarker=body == MARKER, bodyContainsMarker=True,
                  bodyBytes=len(body.encode()), bodySHA256=hashlib.sha256(body.encode()).hexdigest(),
                  historicalCreateResponseIdentity="unavailable")
    return result


def observe(reader, env):
    result = {"schema": "fsgg.release-recovery-observation/1", "observerSource": env["GITHUB_SHA"],
              "observerRun": env["GITHUB_RUN_ID"], "observerAttempt": 1,
              "originalPublisherRun": 38029395937, "originalPublisherAttempt": 1,
              "candidateRun": 38026714312, "candidateArtifact": 11660242226,
              "originalSource": SOURCE, "archiveSHA256": "d5d7d68ab0f73ce020e36f6ae10321b632eb177162d925cd9faabc071891bd64",
              "contentId": CONTENT, "logicalRelease": "utel-rel-19", "version": "0.101.0",
              "firstCause": None, "outcomes": {}, "readerEffects": "closed GET only",
              "platformEffects": "App mint/revocation and summary artifact transport are separate workflow effects"}
    def independent(name, action):
        try:
            result["outcomes"][name] = {"status": "observed", "value": action()}
        except Exception as error:
            code = str(error) if isinstance(error, Refused) else type(error).__name__
            result["outcomes"][name] = {"status": "unknown", "cause": code}
            if isinstance(error, Refused) and error.evidence is not None:
                result["outcomes"][name]["evidence"] = error.evidence
            if result["firstCause"] is None:
                result["firstCause"] = code
    def get(credential, path):
        value = reader.get(credential, path)[0]
        if path == "rate_limit":
            core = value.get("resources", {}).get("core") if isinstance(value, dict) else None
            if not isinstance(core, dict) or any(type(core.get(k)) is not int for k in ("limit", "remaining", "used", "reset")):
                raise Refused("core-rate-shape-refused")
            return {"core": {k: core[k] for k in ("limit", "remaining", "used", "reset")},
                    "scope": "current fresh credential; not historic original token"}
        return value
    independent("ledgerInitialRate", lambda: get("ledger", "rate_limit"))
    def scope():
        value = get("ledger", "installation/repositories?per_page=100")
        if (not isinstance(value, dict) or value.get("total_count") != 1
                or not isinstance(value.get("repositories"), list) or len(value["repositories"]) != 1
                or not isinstance(value["repositories"][0], dict)
                or value["repositories"][0].get("full_name") != AUTHORITY):
            raise Refused("ledger-repository-scope-refused")
        return {"repository": AUTHORITY, "installationId": env["ACTUAL_INSTALLATION_ID"], "appId": 4882140}
    independent("ledgerScope", scope)
    independent("actionsInitialRate", lambda: get("actions", "rate_limit"))
    if result["outcomes"]["ledgerScope"]["status"] == "observed":
        independent("draft", lambda: draft(reader))
    else:
        result["outcomes"]["draft"] = {"status": "unknown", "cause": "scope-prerequisite-refused"}
    independent("actionsFinalRate", lambda: get("actions", "rate_limit"))
    independent("ledgerFinalRate", lambda: get("ledger", "rate_limit"))
    result.update(requests=reader.calls, retainedResponseBytes=reader.bytes, requestsEvidence=reader.evidence)
    return result


def write_report(path, result):
    try:
        with path.open("x") as output:
            json.dump(result, output, indent=2)
            output.write("\n")
    except Exception as error:
        # Do not replace an already recorded collection failure with reporting failure.
        print(json.dumps({"firstCause": result.get("firstCause"), "reportFailure": type(error).__name__}))
        raise Refused(result.get("firstCause") or "summary-report-unavailable",
                      {"reportFailure": type(error).__name__}) from error


def main():
    guard(os.environ)  # No credential request or directory creation before context refusal.
    root = pathlib.Path(os.environ["OBSERVATION_ROOT"])
    root.mkdir(mode=0o700, exist_ok=False)
    reader = Reader({"actions": os.environ["ACTIONS_TOKEN"], "ledger": os.environ["LEDGER_TOKEN"]}, root / "restricted-raw")
    result = observe(reader, os.environ)
    write_report(root / "summary.json", result)
    print(json.dumps({"requests": result["requests"], "firstCause": result["firstCause"],
                      "draft": result["outcomes"]["draft"]["status"]}))
    return 0 if result["firstCause"] is None else 1

if __name__ == "__main__":
    raise SystemExit(main())
