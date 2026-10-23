#!/usr/bin/env python3
"""Observe one already-published Host archive; never select or replay a release."""
from __future__ import annotations

import argparse
import base64
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import re
import stat
import subprocess
import sys
import time
import urllib.error
import urllib.parse
import urllib.request
import zipfile

from fsgg_feed import _StripAuthOnRedirect

ROOT = Path(__file__).resolve().parents[1]
SOURCE = "74cbd6f0b1b598d48556eb854c4ba6a3c3abda54"
PACKAGE = "FS.GG.Telemetry.Host.0.8.0.nupkg"
TAG = "telemetry-host/v0.8.0"
CONTENT = "sha256:0dab8fe535fbe5b2b95f099fd6005ec92a3f749fdc9cea98c1cfb1946ffee41e"
PAYLOAD = "sha256:f036808546d75e62e9fdf5fc8de518a0e4de055efa3ef6e742db8dd1ee1802ee"
MANIFEST_SHA = "c4af467ae6bca804c53debf7ba0e3e0b86db9d117ab4d69c52c5999e6b523054"
JOURNAL_SHA = "90b2684db25e78abe51ea2fda77699b0a3bae5644d0cd0e85b7a7d8947d61408"
RELEASE_ID = 419394647
API = "https://api.github.com/repos/FS-GG/.github"
TAG_URL = API + "/git/ref/tags/" + TAG
RELEASE_URL = API + "/releases/tags/" + TAG
ORG_URL = "https://nuget.pkg.github.com/FS-GG/download/fs.gg.telemetry.host/0.8.0/fs.gg.telemetry.host.0.8.0.nupkg"
ASSETS = {"manifest.json": (662076940, 768, MANIFEST_SHA),
          "publication-journal.json": (662078749, 831, JOURNAL_SHA)}
PINS = {"telemetry-host-release.py": "b87002d3192c2cf70d9974a8d7a6b2c3cdca248378c171f04b81519d14c40f7e",
        "release-saga.py": "24bdfa2a4c1491ecc1ddf24f797a072372fdce14881d413d9301b739dc77501c",
        "fsgg_feed.py": "f7650429432417af72168c288cdee347b338328848870cb4f2f18d6b62d0733f"}
CHOICES = ("coherent-0112-absence", "host-080-org-artifact")
FILES = {PACKAGE, "manifest.json", "publication-journal.json", "local-feed-observation.json",
         "native-before.json", "native-after.json", "observation.json"}


class Refused(RuntimeError):
    """Only closed diagnostics, never transport URLs/credentials, reach the log."""


def require(condition, reason):
    if not condition:
        raise Refused(reason)


def sha(raw):
    return hashlib.sha256(raw).hexdigest()


def strict(raw):
    def pairs(items):
        result = {}
        for key, value in items:
            require(key not in result, "duplicate-json-key")
            result[key] = value
        return result

    def nonfinite(_):
        raise Refused("nonfinite-json")
    return json.loads(raw, object_pairs_hook=pairs, parse_constant=nonfinite)


def select(value):
    require(value in CHOICES, "observation-not-selected")
    return value


def context(env, head):
    require(select(env.get("OBSERVATION")) == CHOICES[1], "host-observation-not-selected")
    expected = {"GITHUB_REPOSITORY": "FS-GG/.github", "GITHUB_EVENT_NAME": "workflow_dispatch",
                "GITHUB_REF": "refs/heads/main", "GITHUB_RUN_ATTEMPT": "1",
                "GITHUB_ACTOR": "EHotwagner", "GITHUB_TRIGGERING_ACTOR": "EHotwagner"}
    require(all(env.get(k) == v for k, v in expected.items()), "original-host-read-context")
    source = env.get("GITHUB_SHA", "")
    require(re.fullmatch(r"[0-9a-f]{40}", source) and source == head, "workflow-checkout-source")
    run = env.get("GITHUB_RUN_ID", "")
    require(re.fullmatch(r"[1-9][0-9]*", run), "original-run-identity")
    return {"workflowSourceSha": source, "publishedSourceSha": SOURCE, "runId": int(run),
            "attempt": 1, "actor": "EHotwagner", "observation": CHOICES[1]}


def verifier(root=ROOT):
    for name, digest in PINS.items():
        require(sha((root / "scripts" / name).read_bytes()) == digest, "immutable-verifier-source")
    path = root / "scripts/telemetry-host-release.py"
    spec = importlib.util.spec_from_file_location("published_host_verifier", path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def redirects(deadline, allowed):
    # Same timeout/strip-auth contract as GitHubAPI._runtime_read; initial headers
    # here distinguish the selected org Basic auth from GitHub API Bearer auth.
    class Redirect(_StripAuthOnRedirect):
        count = 0

        def redirect_request(self, req, fp, code, msg, headers, target):
            self.count += 1
            parsed = urllib.parse.urlparse(target)
            require(allowed and self.count <= 3 and parsed.scheme == "https" and
                    parsed.hostname and not parsed.username and not parsed.password,
                    "redirect-refused")
            left = deadline - time.monotonic()
            require(left > 0, "read-deadline")
            req.timeout = left
            redirected = super().redirect_request(req, fp, code, msg, headers, target)
            redirected.timeout = left
            return redirected
    return Redirect()


class Reader:
    def __init__(self, token, deadline):
        require(bool(token), "existing-package-read-token-missing")
        self.token, self.deadline = token, deadline
        self.records, self.binary_seen = [], set()
        self.metadata_bytes = 0

    def get(self, url, cap, *, binary=False):
        selected = {TAG_URL, RELEASE_URL, ORG_URL, *(API + "/releases/assets/" + str(v[0]) for v in ASSETS.values())}
        require(url in selected and len(self.records) < 20, "selected-read-bound")
        require(cap <= (8 * 1024**2 if binary else 2 * 1024**2), "read-cap")
        if binary:
            require(url not in self.binary_seen, "binary-read-already-attempted")
            self.binary_seen.add(url)
        deadline = min(self.deadline, time.monotonic() + 12)
        require(time.monotonic() < deadline, "read-deadline")
        authorization = ("Basic " + base64.b64encode(("x:" + self.token).encode()).decode()
                         if url == ORG_URL else "Bearer " + self.token)
        headers = {"Authorization": authorization,
                   "Accept": "application/octet-stream" if binary else "application/vnd.github+json"}
        row = {"method": "GET", "url": url, "cap": cap, "state": "attempted"}
        self.records.append(row)
        try:
            opener = urllib.request.build_opener(urllib.request.ProxyHandler({}), redirects(deadline, binary))
            request = urllib.request.Request(url, headers=headers, method="GET")
            with opener.open(request, timeout=deadline - time.monotonic()) as response:
                require(response.status == 200, "read-status")
                length = response.headers.get("Content-Length")
                require(length is None or (length.isascii() and length.isdecimal() and int(length) <= cap), "read-content-length")
                expected = int(length) if length is not None else None
                transport, sock = response, None
                for _ in range(3):
                    sock = getattr(getattr(getattr(transport, "fp", None), "raw", None), "_sock", None)
                    if sock is not None:
                        break
                    transport = getattr(transport, "fp", None)
                require(sock is not None, "read-timeout-unavailable")
                parts, size = [], 0
                while size <= cap:
                    left = deadline - time.monotonic()
                    require(left > 0, "read-deadline")
                    sock.settimeout(left)
                    chunk = response.read1(min(65536, cap + 1 - size))
                    parts.append(chunk)
                    size += len(chunk)
                    require(size <= cap and time.monotonic() < deadline, "read-byte-deadline-bound")
                    if not chunk or size == expected:
                        break
                require(expected is None or size == expected, "read-content-length-mismatch")
                raw = b"".join(parts)
            if not binary:
                self.metadata_bytes += len(raw)
                require(self.metadata_bytes <= 1024**2, "metadata-aggregate-cap")
            row.update(state="read", bytes=len(raw), sha256=sha(raw))
            return raw
        except urllib.error.HTTPError as error:
            row.update(state="refused", status=error.code)
            error.close()
            raise Refused("HTTP-" + str(error.code)) from None
        except Exception:
            row["state"] = "refused"
            raise


def zipguard(path):
    with zipfile.ZipFile(path) as archive:
        rows = archive.infolist()
        require(0 < len(rows) <= 128 and sum(x.file_size for x in rows) <= 32 * 1024**2, "package-expanded-bound")
        names = set()
        for entry in rows:
            require(entry.filename not in names and entry.file_size <= 8 * 1024**2 and not entry.flag_bits & 1, "package-entry-bound")
            names.add(entry.filename)
            require(not entry.filename.startswith("/") and ".." not in Path(entry.filename).parts and
                    "\\" not in entry.filename and not stat.S_ISLNK(entry.external_attr >> 16), "package-entry-path")


def native(reader):
    tag = strict(reader.get(TAG_URL, 2 * 1024**2))
    release = strict(reader.get(RELEASE_URL, 2 * 1024**2))
    require(tag.get("ref") == "refs/tags/" + TAG and tag.get("object", {}).get("type") == "commit" and
            tag["object"]["sha"] == SOURCE, "published-tag-source")
    require(release.get("id") == RELEASE_ID and release.get("tag_name") == TAG and
            release.get("target_commitish") == SOURCE and release.get("draft") is False and
            release.get("prerelease") is False and release.get("immutable") is True and
            release.get("body") == "telemetry-host-successor:" + CONTENT, "immutable-published-release")
    assets = release.get("assets")
    require(isinstance(assets, list) and len(assets) == 3, "closed-release-assets")
    for name, (ident, size, digest) in ASSETS.items():
        matches = [x for x in assets if x.get("name") == name]
        require(len(matches) == 1 and matches[0].get("id") == ident and matches[0].get("size") == size and
                matches[0].get("digest") == "sha256:" + digest and matches[0].get("state") == "uploaded", "published-asset-identity")
    return {"tag": tag, "release": release}


def stable_native(value):
    # Download counts are observational counters; all identity/content/timestamps stay bound.
    value = strict(json.dumps(value).encode())
    for asset in value["release"]["assets"]:
        if "download_count" in asset:
            require(type(asset["download_count"]) is int and asset["download_count"] >= 0, "asset-download-counter")
            asset["download_count"] = "observational-counter"
    return value


def observe(reader, out, identity, host, deadline):
    require(not out.exists(), "fresh-output-required")
    out.mkdir(parents=True, mode=0o700)

    def save(name, raw):
        require(time.monotonic() < deadline, "observation-deadline")
        require(sum(x.stat().st_size for x in out.iterdir()) + len(raw) <= 16 * 1024**2, "observation-output-cap")
        with (out / name).open("xb") as stream:
            stream.write(raw)
        return out / name

    before = native(reader)
    save("native-before.json", json.dumps(before, sort_keys=True).encode())
    for name, (ident, size, digest) in ASSETS.items():
        raw = reader.get(API + "/releases/assets/" + str(ident), size, binary=True)
        require(len(raw) == size and sha(raw) == digest, "published-asset-bytes")
        save(name, raw)
    manifest = host.load_manifest(out / "manifest.json")
    require(manifest["sourceSha"] == SOURCE and manifest["producerPayloadSha256"] == PAYLOAD and
            manifest["version"] == "0.8.0", "published-manifest")
    journal = strict((out / "publication-journal.json").read_bytes())
    require(journal.get("schema") == "fsgg.telemetry-host-release-journal/v1" and
            journal.get("manifestSha256") == CONTENT and set(journal.get("observations", {})) == {"github", "nuget"}, "published-journal")
    raw = reader.get(ORG_URL, 8 * 1024**2, binary=True)
    package = save(PACKAGE, raw)
    zipguard(package)
    observation = host.verify_artifact(out / "manifest.json", package)
    row = journal["observations"]["github"]
    require(observation["producerPayloadEqual"] is True and observation["payloadSha256"] == PAYLOAD and
            observation["archiveSha256"] == row.get("archiveSha256") and row.get("payloadSha256") == PAYLOAD and
            row.get("producerPayloadEqual") is True, "org-feed-journal-payload-mismatch")
    host.journal_record(out / "local-feed-observation.json", manifest, "github", observation)
    after = native(reader)
    require(stable_native(before) == stable_native(after), "immutable-release-drift")
    save("native-after.json", json.dumps(after, sort_keys=True).encode())
    result = {"schema": "fsgg.telemetry-host-org-observation/v1", **identity,
              "state": "org-feed-bytes-verified", "publicationAccepted": False,
              "manifestSha256": MANIFEST_SHA, "publicJournalSha256": JOURNAL_SHA,
              "releaseId": RELEASE_ID, "package": observation, "packageBytes": len(raw),
              "sourcePins": PINS, "reads": reader.records}
    save("observation.json", json.dumps(result, sort_keys=True).encode())
    require({x.name for x in out.iterdir()} == FILES and
            sum(x.stat().st_size for x in out.iterdir()) <= 16 * 1024**2, "exact-evidence-files")
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--check-selection", action="store_true")
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    start = time.monotonic()
    try:
        choice = select(os.environ.get("OBSERVATION", ""))
        if args.check_selection:
            print(choice)
            return 0
        require(args.output is not None, "output-required")
        head = subprocess.run(["git", "rev-parse", "HEAD"], cwd=ROOT, check=True,
                              capture_output=True, text=True, timeout=5).stdout.strip()
        identity = context(os.environ, head)
        host = verifier()
        reader = Reader(os.environ.get("GITHUB_TOKEN", ""), start + 180)
        result = observe(reader, args.output, identity, host, start + 180)
        print(json.dumps({"state": result["state"], "runId": identity["runId"],
                          "archiveSha256": result["package"]["archiveSha256"], "publicationAccepted": False}))
        return 0
    except Exception as error:
        reason = str(error) if isinstance(error, Refused) else type(error).__name__
        print("Host org observation refused: " + reason, file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
