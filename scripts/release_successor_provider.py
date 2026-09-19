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
import subprocess
import sys
import urllib.error
import urllib.parse
import urllib.request

from fsgg_feed import _StripAuthOnRedirect
from release_successor_execution import Dispatch, Effect, Observation

REPOSITORY = "FS-GG/.github"
PACKAGES = ("FS.GG.Coord.Cli", "FS.GG.Kit", "FS.GG.Drivers")


class NotFound(RuntimeError):
    pass


class Refused(RuntimeError):
    pass


class GitHubAPI:
    def __init__(self, token: str):
        if not token:
            raise Refused("GitHub token is missing")
        self.token = token

    def _request(self, url: str, method: str = "GET", body: bytes | None = None, content_type: str = "application/json") -> bytes:
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
                return response.read()
        except urllib.error.HTTPError as error:
            if error.code == 404:
                raise NotFound(url) from error
            raise Refused(f"GitHub {method} {url} returned HTTP {error.code}") from error

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

    def download_asset(self, asset_id: int) -> bytes:
        url = f"https://api.github.com/repos/{REPOSITORY}/releases/assets/{asset_id}"
        request = urllib.request.Request(
            url,
            headers={"Authorization": f"Bearer {self.token}", "Accept": "application/octet-stream"},
        )
        try:
            with urllib.request.build_opener(_StripAuthOnRedirect).open(request, timeout=60) as response:
                return response.read()
        except urllib.error.HTTPError as error:
            raise Refused(f"release asset {asset_id} returned HTTP {error.code}") from error


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
            return Dispatch("unknown")
        return Dispatch("applied")
