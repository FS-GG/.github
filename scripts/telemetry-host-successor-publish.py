#!/usr/bin/env python3
"""Publish the exact Host 0.1.4 candidate through fresh admitted effects."""

from __future__ import annotations

import argparse
import json
import os
import pathlib
import subprocess
import sys
import time

from release_successor_execution import Refused, advance_effects
from release_successor_journal import ProtectedReleaseJournal, REPOSITORY as JOURNAL_REPOSITORY
from release_successor_provider import GitHubAPI, NotFound
from telemetry_host_successor_admission import HostAdmission
from telemetry_host_successor_execution import effects
from telemetry_host_successor_provider import HostProvider

REPOSITORY = "FS-GG/.github"
REF = "refs/heads/fsgg/v2/journal/release/utel-host-rel-03"


def require(ok: bool, detail: str) -> None:
    if not ok:
        raise Refused(detail)


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
        require(len(args.candidate_archive_sha256) == 64, "candidate archive digest malformed")
        require(not args.workdir.exists(), "candidate workdir already exists")
        args.workdir.mkdir(parents=True)

        github_token = os.environ["GH_TOKEN"]
        ledger_token = os.environ["ORDINARY_LEDGER_TOKEN"]
        nuget_key = os.environ["NUGET_API_KEY"]
        api = GitHubAPI(github_token)
        artifact = api.get(f"repos/{REPOSITORY}/actions/artifacts/{args.candidate_artifact_id}")
        run = api.get(f"repos/{REPOSITORY}/actions/runs/{args.candidate_run_id}")
        require(run.get("id") == args.candidate_run_id, "candidate run API identity differs")
        require(artifact.get("id") == args.candidate_artifact_id, "candidate artifact API identity differs")
        candidate_source = run.get("head_sha")
        require(isinstance(candidate_source, str) and len(candidate_source) == 40, "candidate source malformed")
        require(artifact.get("digest") == "sha256:" + args.candidate_archive_sha256, "artifact digest differs")
        artifact_json = args.workdir / "artifact.json"
        run_json = args.workdir / "run.json"
        archive = args.workdir / "candidate.zip"
        artifact_json.write_text(json.dumps(artifact))
        run_json.write_text(json.dumps(run))
        with archive.open("xb") as stream:
            subprocess.run(["gh", "api", f"repos/{REPOSITORY}/actions/artifacts/{args.candidate_artifact_id}/zip"],
                           stdout=stream, check=True)
        candidate = args.workdir / "candidate"
        verifier = pathlib.Path(__file__).with_name("telemetry-host-successor-artifact.py")
        subprocess.run([sys.executable, str(verifier), "--artifact-json", str(artifact_json),
                        "--run-json", str(run_json), "--archive", str(archive),
                        "--source-sha", candidate_source, "--output", str(candidate)], check=True)
        manifest_path = candidate / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        content_id, ordered = effects(manifest)
        admission = HostAdmission(api, manifest, publisher_sha, run_id, operator, "refs/heads/main")
        provider = HostProvider(api, manifest_path, github_token, nuget_key)
        intent = {"contentId": content_id, "sourceSha": candidate_source, "version": "0.1.4",
                  "candidateArchiveSha256": args.candidate_archive_sha256, "operator": operator}
        ledger_api = GitHubAPI(ledger_token)
        journal = ProtectedReleaseJournal(ledger_api, REF)
        try:
            ledger_api.get(f"repos/{JOURNAL_REPOSITORY}/git/ref/{REF.removeprefix('refs/')}")
        except NotFound:
            require(candidate_source == publisher_sha, "fresh Host publication requires exact current main candidate")
            require(admission.authorize(content_id, "journal", "intent", content_id), "Host journal admission denied")
            for effect in ordered:
                observation = provider.observe(effect)
                require(observation.state == "absent", f"{effect.identity}: preexisting or unreadable effect")
            if args.preflight_only:
                print("Host successor preflight passed; no journal, tag, feed or release changed.")
                return 0
            journal.initialize(intent)
        else:
            journal.read()
            require(not args.preflight_only, "preflight requires unused Host journal and version")
            if candidate_source != publisher_sha:
                comparison = api.get(f"repos/{REPOSITORY}/compare/{candidate_source}...{publisher_sha}")
                require(comparison.get("status") == "ahead", "recovery publisher does not descend from candidate")
        journal.validate_intent(intent)
        deadline = time.monotonic() + 45 * 60
        for _ in range(len(ordered) * 2 + 120):
            result = advance_effects(content_id, ordered, journal, admission, provider)
            print(f"Host successor effect state: {result}", flush=True)
            if result == "complete":
                return 0
            if time.monotonic() >= deadline:
                raise Refused("Host publication observation deadline expired")
            time.sleep(5)
        raise Refused("Host publication exceeded bounded reconciliation steps")
    except (KeyError, ValueError, OSError, Refused, subprocess.CalledProcessError) as error:
        print(f"Host successor refused: {error}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
