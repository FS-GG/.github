#!/usr/bin/env python3
"""Publish one exact, independently verified candidate through protected effects."""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import pathlib
import re
import subprocess
import sys
import time

from release_successor_admission import SingleOperatorAdmission
from release_successor_execution import Refused, advance, ordered_effects
from release_successor_journal import ProtectedReleaseJournal, REF, REPOSITORY as JOURNAL_REPOSITORY
from release_successor_provider import GitHubAPI, LiveProvider, NotFound

REPOSITORY = "FS-GG/.github"


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise Refused(reason)


def authority_protection():
    """Load only the existing read-only predicate and reviewed rule binding."""
    path = pathlib.Path(__file__).with_name("authority-state-import.py")
    spec = importlib.util.spec_from_file_location("successor_authority_protection", path)
    require(spec is not None and spec.loader is not None, "Authority protection source unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    binding = module.ADMITTED_IMPORT
    require(isinstance(binding, dict), "reviewed Authority protection binding unavailable")
    return module.validate_native_protection, {
        "rulesets": binding["rulesets"], "protectionAcceptedAt": binding["protectionAcceptedAt"],
    }


class ProtectedPublisherAdmission:
    """Compose exact publisher admission with fresh main/retirement protections."""

    def __init__(self, publisher, ledger_api, logical_ref):
        self.publisher = publisher
        self.ledger_api = ledger_api
        self.logical_ref = logical_ref
        self.protection, self.binding = authority_protection()

    def authorize(self, content_id, effect, action, request_digest):
        try:
            if not self.publisher.authorize(content_id, effect, action, request_digest):
                return False
            self.protection(self.ledger_api, self.binding, [self.logical_ref])
            return True
        except (KeyError, TypeError, ValueError, OSError, RuntimeError):
            return False


def authority_main(api):
    value = api.get(f"repos/{JOURNAL_REPOSITORY}/git/ref/heads/main")
    require(isinstance(value, dict) and isinstance(value.get("object"), dict),
            "Authority main response is malformed")
    obj = value.get("object", {})
    require(obj.get("type") == "commit" and isinstance(obj.get("sha"), str)
            and re.fullmatch(r"[0-9a-f]{40}", obj["sha"]), "Authority main has no exact commit head")
    return obj["sha"]


def classify_journal_destination(api, logical_ref):
    """Return (fresh, physical head); nested read failures never mean absence."""
    prefix = "refs/heads/fsgg/v2/journal/release/"
    require(isinstance(logical_ref, str) and logical_ref.startswith(prefix)
            and re.fullmatch(r"[A-Za-z0-9][A-Za-z0-9._-]*", logical_ref.removeprefix(prefix)),
            "selected release journal identity differs")
    repository = api.get(f"repos/{JOURNAL_REPOSITORY}")
    require(isinstance(repository, dict) and repository.get("id") == 1351660651
            and repository.get("full_name") == JOURNAL_REPOSITORY,
            "Authority repository identity differs")
    physical = authority_main(api)
    commit = api.get(f"repos/{JOURNAL_REPOSITORY}/git/commits/{physical}")
    require(isinstance(commit, dict) and commit.get("sha") == physical
            and isinstance(commit.get("tree"), dict) and isinstance(commit.get("parents"), list)
            and len(commit["parents"]) <= 2
            and all(isinstance(parent, dict) and isinstance(parent.get("sha"), str)
                    and re.fullmatch(r"[0-9a-f]{40}", parent["sha"]) for parent in commit["parents"]),
            "Authority main commit identity differs")
    tree = commit.get("tree", {}).get("sha")
    present = True
    for segment in ("state", "releases", logical_ref.removeprefix(prefix)):
        require(isinstance(tree, str) and re.fullmatch(r"[0-9a-f]{40}", tree),
                "Authority tree identity is malformed")
        value = api.get(f"repos/{JOURNAL_REPOSITORY}/git/trees/{tree}")
        require(isinstance(value, dict), "Authority destination tree response is malformed")
        entries = value.get("tree")
        require(value.get("sha") == tree and value.get("truncated") is False
                and isinstance(entries, list) and len(entries) <= 1000,
                "Authority destination tree is incomplete")
        require(all(isinstance(entry, dict) and isinstance(entry.get("path"), str)
                    and entry["path"] and entry["path"] not in {".", ".."} and "/" not in entry["path"]
                    and entry.get("type") in {"tree", "blob", "commit"}
                    and entry.get("mode") in {"040000", "100644", "100755", "120000", "160000"}
                    and isinstance(entry.get("sha"), str)
                    and re.fullmatch(r"[0-9a-f]{40}", entry["sha"]) for entry in entries)
                and len({entry["path"] for entry in entries}) == len(entries),
                "Authority destination entries are malformed or ambiguous")
        matches = [entry for entry in entries if entry["path"] == segment]
        if not matches:
            present = False
            break
        require(matches[0].get("type") == "tree" and matches[0].get("mode") == "040000",
                "Authority journal destination is not a directory")
        tree = matches[0]["sha"]
    try:
        api.get(f"repos/{JOURNAL_REPOSITORY}/git/ref/{logical_ref.removeprefix('refs/')}")
    except NotFound:
        pass
    else:
        raise Refused("selected legacy release journal still exists; separate disposition required")
    require(authority_main(api) == physical, "Authority main moved during destination classification")
    return not present, physical


def prepare_release_journal(journal, ledger_api, admission, provider, api, manifest, intent,
                            candidate_source, publisher_sha, preflight_only, uniqueness):
    """Qualify the physical route before genesis or original-intent recovery."""
    fresh, physical = classify_journal_destination(ledger_api, journal.ref)
    if fresh:
        require(candidate_source == publisher_sha, "fresh publication requires candidate and publisher source to match")
        try:
            api.get("repos/FS-GG/.github/git/ref/tags/coherent-set/v0.100.0")
        except NotFound:
            pass
        else:
            raise Refused("successor tag already exists outside the protected journal")
        require(provider._release() is None, "successor release already exists outside the protected journal")
        uniqueness()
        require(authority_main(ledger_api) == physical, "Authority main moved before journal preparation")
        require(admission.authorize(manifest["contentId"], "journal", "intent", manifest["contentId"]),
                "release journal initialization admission denied")
        if preflight_only:
            return True
        journal.initialize(intent)
    else:
        require(authority_main(ledger_api) == physical, "Authority main moved before journal recovery")
        journal.read()
        require(authority_main(ledger_api) == physical, "Authority main moved during journal recovery")
        require(not preflight_only, "preflight requires an unused release journal and version")
        if candidate_source != publisher_sha:
            comparison = api.get(f"repos/{REPOSITORY}/compare/{candidate_source}...{publisher_sha}")
            require(comparison.get("status") == "ahead", "recovery publisher does not descend from candidate source")
    journal.validate_intent(intent)
    return False


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-run-id", required=True, type=int)
    parser.add_argument("--candidate-artifact-id", required=True, type=int)
    parser.add_argument("--candidate-archive-sha256", required=True)
    parser.add_argument("--workdir", required=True, type=pathlib.Path)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preflight-only", action="store_true")
    mode.add_argument("--publish", action="store_true")
    args = parser.parse_args()
    try:
        publisher_sha = os.environ["GITHUB_SHA"]
        operator = os.environ["GITHUB_ACTOR"]
        run_id = int(os.environ["GITHUB_RUN_ID"])
        require(os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch", "publisher event differs")
        require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY, "publisher repository differs")
        require(os.environ.get("GITHUB_REF") == "refs/heads/main", "publisher branch differs")
        require(os.environ.get("GITHUB_RUN_ATTEMPT") == "1", "publisher rerun is not admitted")
        require(operator == "EHotwagner", "publisher operator differs")
        require(len(args.candidate_archive_sha256) == 64, "candidate archive digest is malformed")
        require(not args.workdir.exists(), "candidate workdir already exists")
        args.workdir.mkdir(parents=True)

        github_token = os.environ["GH_TOKEN"]
        ledger_token = os.environ["ORDINARY_LEDGER_TOKEN"]
        nuget_key = os.environ["NUGET_API_KEY"]
        api = GitHubAPI(github_token)
        artifact = api.get(f"repos/{REPOSITORY}/actions/artifacts/{args.candidate_artifact_id}")
        run = api.get(f"repos/{REPOSITORY}/actions/runs/{args.candidate_run_id}")
        candidate_source = run.get("head_sha")
        require(isinstance(candidate_source, str) and len(candidate_source) == 40, "candidate source is malformed")
        require(artifact.get("digest") == "sha256:" + args.candidate_archive_sha256, "candidate artifact digest differs")
        artifact_json = args.workdir / "artifact.json"
        run_json = args.workdir / "run.json"
        archive = args.workdir / "candidate.zip"
        artifact_json.write_text(json.dumps(artifact))
        run_json.write_text(json.dumps(run))
        with archive.open("xb") as stream:
            subprocess.run(
                ["gh", "api", f"repos/{REPOSITORY}/actions/artifacts/{args.candidate_artifact_id}/zip"],
                stdout=stream, check=True,
            )
        candidate = args.workdir / "candidate"
        verifier = pathlib.Path(__file__).with_name("release-successor-artifact.py")
        subprocess.run(
            [sys.executable, str(verifier), "--artifact-json", str(artifact_json), "--run-json", str(run_json),
             "--archive", str(archive), "--source-sha", candidate_source, "--output", str(candidate)],
            check=True,
        )
        manifest_path = candidate / "release-manifest.json"
        manifest = json.loads(manifest_path.read_text())
        require(manifest["descriptor"]["version"] == "0.100.0", "publisher version differs")
        publisher_admission = SingleOperatorAdmission(api, manifest, publisher_sha, run_id, operator, "refs/heads/main")
        provider = LiveProvider(api, manifest_path, github_token, nuget_key)
        intent = {
            "contentId": manifest["contentId"],
            "sourceSha": candidate_source,
            "version": "0.100.0",
            "candidateArchiveSha256": args.candidate_archive_sha256,
            "operator": operator,
        }
        ledger_api = GitHubAPI(ledger_token)
        journal = ProtectedReleaseJournal(ledger_api, ref=REF, main_directory=True)
        admission = ProtectedPublisherAdmission(publisher_admission, ledger_api, REF)
        def uniqueness():
            subprocess.run(
                [sys.executable, str(pathlib.Path(__file__).with_name("check-release-candidate-uniqueness.py")),
                 "--version", "0.100.0", "--predecessor", "0.99.0"],
                check=True, env={**os.environ, "GITHUB_TOKEN": github_token},
            )
        if prepare_release_journal(journal, ledger_api, admission, provider, api, manifest, intent,
                                   candidate_source, publisher_sha, args.preflight_only, uniqueness):
            print("Release successor preflight passed; no journal, tag, feed or release was changed.")
            return 0
        deadline = time.monotonic() + 45 * 60
        max_steps = len(ordered_effects(manifest)) * 2 + 120
        for _ in range(max_steps):
            result = advance(manifest, journal, admission, provider)
            print(f"successor effect state: {result}", flush=True)
            if result == "complete":
                return 0
            if time.monotonic() >= deadline:
                raise Refused("publication observation deadline expired; reconcile before another run")
            time.sleep(5)
        raise Refused("publication exceeded bounded reconciliation steps")
    except (KeyError, TypeError, ValueError, OSError, RuntimeError, subprocess.CalledProcessError) as error:
        print(f"release successor refused: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
