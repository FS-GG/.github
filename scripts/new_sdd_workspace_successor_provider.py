"""One-package Wizard release observations and effects from retained candidate bytes."""

from __future__ import annotations

import base64
import hashlib
import json
import os
import pathlib
import re
import subprocess
import sys
import tempfile
import urllib.error
import urllib.request

from fsgg_feed import _StripAuthOnRedirect
from release_successor_execution import Dispatch, Effect, Observation, Refused as ExecutionRefused
from release_successor_provider import GitHubAPI, LiveProvider, NotFound, Refused
from new_sdd_workspace_successor_execution import PACKAGE, VERSION, TAG, effects

REPOSITORY = "FS-GG/.github"


# Only fixed diagnostic vocabulary leaves this process. Raw output, exception
# messages, argv, credentials, URLs (including queries) and private paths do not.
DIAGNOSTIC_LIMIT = 64
DIAGNOSTIC_SCAN_LIMIT = 8192
KNOWN_CODES = frozenset({"NU1100", "NU1101", "NU1102", "NU1301", "NU1900", "NU3000", "NU3018", "NU3028", "NU3037", "NETSDK1045", "NETSDK1147"})
KNOWN_SIGNALS = ("signature", "certificate", "unauthorized", "forbidden", "framework", "hostfxr", "hostpolicy", "timeout", "restore", "authentication")


def output_signals(value, secrets=()):
    text = value.decode("utf-8", errors="replace") if isinstance(value, bytes) else (value or "")
    prefix = text[:DIAGNOSTIC_SCAN_LIMIT]
    for secret in secrets:
        if secret:
            prefix = prefix.replace(secret, "")
    words = set(re.findall(r"[A-Za-z]+[0-9]*", prefix))
    return {"text": "withheld", "characters": len(text), "scannedCharacters": len(prefix),
            "codes": sorted(words & KNOWN_CODES),
            "signals": [word for word in KNOWN_SIGNALS if word in {w.lower() for w in words}]}


def exception_kind(error):
    for kind in (subprocess.TimeoutExpired, subprocess.CalledProcessError, Refused, ExecutionRefused, urllib.error.URLError, OSError, ValueError, KeyError):
        if isinstance(error, kind):
            return kind.__name__
    return "unclassified"


def exit_code(value):
    return value if isinstance(value, int) and -(2 ** 31) <= value < 2 ** 31 else None


def failure_fields(error):
    cause = getattr(error, "__cause__", None)
    http = error if isinstance(error, urllib.error.HTTPError) else cause if isinstance(cause, urllib.error.HTTPError) else None
    code = http.code if http is not None else None
    return {"exceptionKind": exception_kind(error) if error is not None else None,
            "causeKind": exception_kind(cause) if cause is not None else None,
            "httpStatus": code if isinstance(code, int) and 100 <= code <= 599 else None,
            "actualExitCode": exit_code(error.returncode) if isinstance(error, subprocess.CalledProcessError) else None}


def publisher_error(error):
    known = {"Wizard publication exceeded bounded reconciliation steps", "Wizard publication observation deadline expired"}
    detail = str(error) if isinstance(error, (Refused, ExecutionRefused)) and str(error) in known else "free text withheld"
    return {"detail": detail, **failure_fields(error)}


class WizardProvider(LiveProvider):
    def __init__(self, api: GitHubAPI, manifest_path: pathlib.Path, github_token: str, nuget_key: str):
        self.api = api
        self.manifest_path = manifest_path
        self.root = manifest_path.parent
        self.manifest = json.loads(manifest_path.read_text())
        self.content_id, self.ordered = effects(self.manifest)
        self.version = VERSION
        self.source = self.manifest["sourceSha"]
        self.tag = TAG
        self.marker = f"new-sdd-workspace-successor:{self.content_id}"
        self.github_token = github_token
        self.nuget_key = nuget_key
        self.observed = self.root / "feed-observations"
        self.observed.mkdir(exist_ok=True)
        self.package = self.root / f"{PACKAGE}.{VERSION}.nupkg"
        self.journal = self.root / "publication-journal.json"
        self._diagnostic_count = 0

    def _diagnostic(self, effect, stage, *, error=None, result=None, reason=None):
        if self._diagnostic_count >= DIAGNOSTIC_LIMIT:
            return
        self._diagnostic_count += 1
        # Effect/stage/reason are internal literals, not remote error strings.
        allowed_effects = {"public-install", "tag", "draft", "github", "nuget", "package-asset", "manifest-asset", "publication-journal-asset", "promote"}
        allowed_stages = {"install", "apphost", "help", "dispatch", "promote-release", "promote-journal", "promote-install", "promote-patch"}
        allowed_reasons = {None, "nonzero-exit", "missing-apphost", "help-name-absent", "passed", "release-absent", "draft-observed"}
        row = {"schema": "fsgg.wizard-release-diagnostic/1", "effect": effect if effect in allowed_effects else "withheld",
               "stage": stage if stage in allowed_stages else "withheld", "reason": reason if reason in allowed_reasons else "withheld",
               **failure_fields(error)}
        if result is not None:
            row["actualExitCode"] = exit_code(getattr(result, "returncode", None))
        if result is not None or isinstance(error, (subprocess.TimeoutExpired, subprocess.CalledProcessError)):
            secrets = (self.github_token, self.nuget_key, *[v for k, v in os.environ.items() if any(word in k.upper() for word in ("TOKEN", "KEY", "SECRET", "PASSWORD", "CREDENTIAL"))])
            row["stdout"] = output_signals(getattr(result, "stdout", None) if result is not None else error.output, secrets)
            row["stderr"] = output_signals(getattr(result, "stderr", None) if result is not None else error.stderr, secrets)
        print("Wizard release diagnostic: " + json.dumps(row, sort_keys=True), flush=True)

    def _public_install(self) -> bool:
        """Read the independent nuget.org consumer route from an empty tool path."""
        with tempfile.TemporaryDirectory(prefix="wizard-public-install-") as temporary:
            root = pathlib.Path(temporary)
            config = root / "NuGet.Config"
            config.write_text("""<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear/><add key="nuget.org" value="https://api.nuget.org/v3/index.json"/></packageSources></configuration>
""")
            env = {**os.environ, "NUGET_PACKAGES": str(root / "packages"),
                   "NUGET_HTTP_CACHE_PATH": str(root / "http-cache"),
                   "DOTNET_CLI_HOME": str(root / "dotnet-home")}
            command = ["dotnet", "tool", "install", PACKAGE, "--version", VERSION,
                       "--tool-path", str(root / "tool"), "--configfile", str(config)]
            try:
                installed = subprocess.run(command, capture_output=True, text=True, timeout=180, env=env)
            except (subprocess.TimeoutExpired, OSError) as error:
                self._diagnostic("public-install", "install", error=error)
                raise
            self._diagnostic("public-install", "install", result=installed, reason="passed" if installed.returncode == 0 else "nonzero-exit")
            if installed.returncode != 0:
                return False
            tool = root / "tool" / "new-sdd-workspace"
            if not tool.is_file():
                self._diagnostic("public-install", "apphost", reason="missing-apphost")
                return False
            try:
                help_result = subprocess.run([str(tool), "--help"], capture_output=True, text=True, timeout=60, env=env)
            except (subprocess.TimeoutExpired, OSError) as error:
                self._diagnostic("public-install", "help", error=error)
                raise
            matched = "new-sdd-workspace" in (help_result.stdout + help_result.stderr)
            self._diagnostic("public-install", "help", result=help_result,
                             reason="nonzero-exit" if help_result.returncode != 0 else "passed" if matched else "help-name-absent")
            return help_result.returncode == 0 and matched

    def _download_package(self, feed: str, package: str = PACKAGE) -> pathlib.Path | None:
        if package != PACKAGE or feed not in {"github", "nuget"}:
            raise Refused("unknown Wizard package or feed")
        ident = f"fs.gg.newsddworkspace.{VERSION}.nupkg"
        url = (f"https://nuget.pkg.github.com/FS-GG/download/fs.gg.newsddworkspace/{VERSION}/{ident}"
               if feed == "github" else
               f"https://api.nuget.org/v3-flatcontainer/fs.gg.newsddworkspace/{VERSION}/{ident}")
        headers = {"User-Agent": "fsgg-wizard-successor"}
        if feed == "github":
            headers["Authorization"] = "Basic " + base64.b64encode(f"x:{self.github_token}".encode()).decode()
        request = urllib.request.Request(url, headers=headers)
        try:
            with urllib.request.build_opener(_StripAuthOnRedirect).open(request, timeout=90) as response:
                raw = response.read()
        except urllib.error.HTTPError as error:
            if error.code == 404:
                return None
            raise Refused(f"{feed} Wizard package observation returned HTTP {error.code}") from error
        target = self.observed / f"{feed}-{ident}"
        target.write_bytes(raw)
        checker = pathlib.Path(__file__).with_name("new-sdd-workspace-release.py")
        subprocess.run([sys.executable, str(checker), "verify", "--manifest", str(self.manifest_path),
                        "--package", str(target), "--feed", feed, "--journal", str(self.journal)],
                       check=True, capture_output=True, text=True)
        return target

    def _remote_journal(self) -> dict | None:
        raw = self._asset("publication-journal.json")
        if raw is None:
            return None
        remote = json.loads(raw)
        if remote.get("schema") != "fsgg.new-sdd-workspace-release-journal/v1" or remote.get("manifestSha256") != self.content_id:
            raise Refused("Wizard publication journal identity differs")
        rows = remote.get("observations")
        if not isinstance(rows, dict) or set(rows) != {"github", "nuget"}:
            raise Refused("Wizard publication journal is incomplete")
        for feed in ("github", "nuget"):
            found = self._download_package(feed)
            if found is None:
                raise Refused(f"{feed} Wizard package disappeared")
            row = rows[feed]
            if (row.get("archiveSha256") != hashlib.sha256(found.read_bytes()).hexdigest()
                    or row.get("payloadSha256") != self.manifest["producerPayloadSha256"]
                    or row.get("producerPayloadEqual") is not True):
                raise Refused(f"{feed} Wizard journal and public feed differ")
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
                self._diagnostic("promote", "promote-release", reason="release-absent" if release is None else "draft-observed")
                return Observation("absent")
            if self._remote_journal() is None or not self._public_install():
                return Observation("mismatched")
            return Observation("matched", self.content_id)
        raise Refused(f"unknown Wizard release effect {identity}")

    def dispatch(self, effect: Effect) -> Dispatch:
        identity = effect.identity
        stage = "dispatch"
        try:
            if identity == "tag":
                self.api.post(f"repos/{REPOSITORY}/git/refs", {"ref": f"refs/tags/{TAG}", "sha": self.source})
            elif identity == "draft":
                self.api.post(f"repos/{REPOSITORY}/releases",
                              {"tag_name": TAG, "target_commitish": self.source,
                               "name": f"FS.GG.NewSddWorkspace {VERSION}", "body": self.marker,
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
                        raise Refused("both feed observations are required before Wizard journal asset")
                release = self._release()
                if release is None or not release.get("draft"):
                    raise Refused("Wizard draft release unavailable")
                self.api.upload_asset(release["id"], name, self.root / name)
            elif identity == "promote":
                stage = "promote-release"
                release = self._release()
                if release is None or not release.get("draft"):
                    raise Refused("complete Wizard draft release unavailable")
                stage = "promote-journal"
                if self._remote_journal() is None:
                    raise Refused("complete Wizard draft release unavailable")
                stage = "promote-install"
                if not self._public_install():
                    raise Refused("complete Wizard draft release unavailable")
                stage = "promote-patch"
                self.api.patch(f"repos/{REPOSITORY}/releases/{release['id']}",
                               {"draft": False, "make_latest": "false"})
            else:
                raise Refused(f"unknown Wizard release effect {identity}")
        except (Refused, subprocess.CalledProcessError, subprocess.TimeoutExpired, OSError, urllib.error.URLError, ValueError) as error:
            self._diagnostic(identity, stage, error=error)
            return Dispatch("unknown")
        return Dispatch("applied")
