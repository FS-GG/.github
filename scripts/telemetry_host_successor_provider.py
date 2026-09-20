"""One-package Host release observations and effects from retained candidate bytes."""

from __future__ import annotations

import base64
import hashlib
import json
import pathlib
import subprocess
import sys
import urllib.error
import urllib.request

from fsgg_feed import _StripAuthOnRedirect
from release_successor_execution import Dispatch, Effect, Observation
from release_successor_provider import GitHubAPI, LiveProvider, NotFound, Refused
from telemetry_host_successor_execution import PACKAGE, VERSION, TAG, effects

REPOSITORY = "FS-GG/.github"


class HostProvider(LiveProvider):
    def __init__(self, api: GitHubAPI, manifest_path: pathlib.Path, github_token: str, nuget_key: str):
        self.api = api
        self.manifest_path = manifest_path
        self.root = manifest_path.parent
        self.manifest = json.loads(manifest_path.read_text())
        self.content_id, self.ordered = effects(self.manifest)
        self.version = VERSION
        self.source = self.manifest["sourceSha"]
        self.tag = TAG
        self.marker = f"telemetry-host-successor:{self.content_id}"
        self.github_token = github_token
        self.nuget_key = nuget_key
        self.observed = self.root / "feed-observations"
        self.observed.mkdir(exist_ok=True)
        self.package = self.root / f"{PACKAGE}.{VERSION}.nupkg"
        self.journal = self.root / "publication-journal.json"

    def _download_package(self, feed: str, package: str = PACKAGE) -> pathlib.Path | None:
        if package != PACKAGE or feed not in {"github", "nuget"}:
            raise Refused("unknown Host package or feed")
        ident = f"fs.gg.telemetry.host.{VERSION}.nupkg"
        url = (f"https://nuget.pkg.github.com/FS-GG/download/fs.gg.telemetry.host/{VERSION}/{ident}"
               if feed == "github" else
               f"https://api.nuget.org/v3-flatcontainer/fs.gg.telemetry.host/{VERSION}/{ident}")
        headers = {"User-Agent": "fsgg-host-successor"}
        if feed == "github":
            headers["Authorization"] = "Basic " + base64.b64encode(f"x:{self.github_token}".encode()).decode()
        request = urllib.request.Request(url, headers=headers)
        try:
            with urllib.request.build_opener(_StripAuthOnRedirect).open(request, timeout=90) as response:
                raw = response.read()
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise Refused(f"{feed} Host package observation returned HTTP {error.code}") from error
        target = self.observed / f"{feed}-{ident}"
        target.write_bytes(raw)
        checker = pathlib.Path(__file__).with_name("telemetry-host-release.py")
        subprocess.run([sys.executable, str(checker), "verify", "--manifest", str(self.manifest_path),
                        "--package", str(target), "--feed", feed, "--journal", str(self.journal)],
                       check=True, capture_output=True, text=True)
        return target

    def _remote_journal(self) -> dict | None:
        raw = self._asset("publication-journal.json")
        if raw is None:
            return None
        remote = json.loads(raw)
        if remote.get("schema") != "fsgg.telemetry-host-release-journal/v1" or remote.get("manifestSha256") != self.content_id:
            raise Refused("Host publication journal identity differs")
        rows = remote.get("observations")
        if not isinstance(rows, dict) or set(rows) != {"github", "nuget"}:
            raise Refused("Host publication journal is incomplete")
        for feed in ("github", "nuget"):
            found = self._download_package(feed)
            if found is None:
                raise Refused(f"{feed} Host package disappeared")
            row = rows[feed]
            if (row.get("archiveSha256") != hashlib.sha256(found.read_bytes()).hexdigest()
                    or row.get("payloadSha256") != self.manifest["producerPayloadSha256"]
                    or row.get("producerPayloadEqual") is not True):
                raise Refused(f"{feed} Host journal and public feed differ")
        return remote

    def observe(self, effect: Effect) -> Observation:
        identity = effect.identity
        if identity == "tag":
            try:
                tag = self.api.get(f"repos/{REPOSITORY}/git/ref/tags/{TAG}")
            except NotFound:
                return Observation("absent")
            actual = tag.get("object", {}).get("sha")
            return Observation("matched" if actual == self.source else "mismatched", actual)
        if identity == "draft":
            release = self._release()
            if release is None:
                return Observation("absent")
            valid = release.get("tag_name") == TAG and self.marker in release.get("body", "")
            return Observation("matched" if valid else "mismatched", self.content_id if valid else None)
        if identity in {"github", "nuget"}:
            try:
                found = self._download_package(identity)
            except (Refused, subprocess.CalledProcessError):
                return Observation("mismatched")
            return Observation("matched", effect.target_digest) if found else Observation("absent")
        if identity in {"package-asset", "manifest-asset"}:
            name = self.package.name if identity == "package-asset" else "manifest.json"
            raw = self._asset(name)
            if raw is None:
                return Observation("absent")
            digest = hashlib.sha256(raw).hexdigest()
            return Observation("matched" if digest == effect.target_digest else "mismatched", digest)
        if identity == "publication-journal-asset":
            remote = self._remote_journal()
            return Observation("matched", self.content_id) if remote is not None else Observation("absent")
        if identity == "promote":
            release = self._release()
            if release is None or release.get("draft"):
                return Observation("absent")
            if self._remote_journal() is None:
                return Observation("mismatched")
            return Observation("matched", self.content_id)
        raise Refused(f"unknown Host release effect {identity}")

    def dispatch(self, effect: Effect) -> Dispatch:
        identity = effect.identity
        try:
            if identity == "tag":
                self.api.post(f"repos/{REPOSITORY}/git/refs", {"ref": f"refs/tags/{TAG}", "sha": self.source})
            elif identity == "draft":
                self.api.post(f"repos/{REPOSITORY}/releases",
                              {"tag_name": TAG, "target_commitish": self.source,
                               "name": f"FS.GG.Telemetry.Host {VERSION}", "body": self.marker,
                               "draft": True, "prerelease": False})
            elif identity in {"github", "nuget"}:
                key = self.github_token if identity == "github" else self.nuget_key
                source = ("https://nuget.pkg.github.com/FS-GG/index.json" if identity == "github"
                          else "https://api.nuget.org/v3/index.json")
                if not key:
                    raise Refused(f"{identity} publishing credential missing")
                subprocess.run(["dotnet", "nuget", "push", str(self.package), "--source", source, "--api-key", key],
                               check=True, capture_output=True, text=True)
            elif identity in {"package-asset", "manifest-asset", "publication-journal-asset"}:
                name = (self.package.name if identity == "package-asset" else
                        "manifest.json" if identity == "manifest-asset" else "publication-journal.json")
                if identity == "publication-journal-asset":
                    if not self.journal.exists() or set(json.loads(self.journal.read_text()).get("observations", {})) != {"github", "nuget"}:
                        raise Refused("both feed observations are required before Host journal asset")
                release = self._release()
                if release is None or not release.get("draft"):
                    raise Refused("Host draft release unavailable")
                self.api.upload_asset(release["id"], name, self.root / name)
            elif identity == "promote":
                release = self._release()
                if release is None or not release.get("draft") or self._remote_journal() is None:
                    raise Refused("complete Host draft release unavailable")
                self.api.patch(f"repos/{REPOSITORY}/releases/{release['id']}",
                               {"draft": False, "make_latest": "false"})
            else:
                raise Refused(f"unknown Host release effect {identity}")
        except (Refused, subprocess.CalledProcessError, OSError, urllib.error.URLError, ValueError):
            return Dispatch("unknown")
        return Dispatch("applied")
