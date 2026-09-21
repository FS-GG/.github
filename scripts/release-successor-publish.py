#!/usr/bin/env python3
"""Publish one exact, independently verified candidate through protected effects."""

from __future__ import annotations

import argparse
import json
import os
import pathlib
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
        require(manifest["descriptor"]["version"] == "0.91.4", "publisher version differs")
        admission = SingleOperatorAdmission(api, manifest, publisher_sha, run_id, operator, "refs/heads/main")
        provider = LiveProvider(api, manifest_path, github_token, nuget_key)
        intent = {
            "contentId": manifest["contentId"],
            "sourceSha": candidate_source,
            "version": "0.91.4",
            "candidateArchiveSha256": args.candidate_archive_sha256,
            "operator": operator,
        }
        ledger_api = GitHubAPI(ledger_token)
        journal = ProtectedReleaseJournal(ledger_api)
        try:
            ledger_api.get(f"repos/{JOURNAL_REPOSITORY}/git/ref/{REF.removeprefix('refs/')}")
        except NotFound:
            require(candidate_source == publisher_sha, "fresh publication requires candidate and publisher source to match")
            require(admission.authorize(manifest["contentId"], "journal", "intent", manifest["contentId"]),
                    "release journal initialization admission denied")
            try:
                api.get("repos/FS-GG/.github/git/ref/tags/coherent-set/v0.91.4")
            except NotFound:
                pass
            else:
                raise Refused("successor tag already exists outside the protected journal")
            require(provider._release() is None, "successor release already exists outside the protected journal")
            subprocess.run(
                [sys.executable, str(pathlib.Path(__file__).with_name("check-release-candidate-uniqueness.py")),
                 "--version", "0.91.4", "--predecessor", "0.91.3"],
                check=True, env={**os.environ, "GITHUB_TOKEN": github_token},
            )
            if args.preflight_only:
                print("Release successor preflight passed; no journal, tag, feed or release was changed.")
                return 0
            journal.initialize(intent)
        else:
            journal.read()
            require(not args.preflight_only, "preflight requires an unused release journal and version")
            if candidate_source != publisher_sha:
                comparison = api.get(f"repos/{REPOSITORY}/compare/{candidate_source}...{publisher_sha}")
                require(comparison.get("status") == "ahead", "recovery publisher does not descend from candidate source")
        journal.validate_intent(intent)
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
    except (KeyError, ValueError, OSError, Refused, subprocess.CalledProcessError) as error:
        print(f"release successor refused: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
