#!/usr/bin/env python3
"""Publish the exact Wizard 0.16.0 candidate through fresh admitted effects."""

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

from release_successor_execution import Refused, advance_effects
from release_successor_journal import ProtectedReleaseJournal
from release_successor_provider import GitHubAPI
from new_sdd_workspace_successor_admission import WizardAdmission
from new_sdd_workspace_successor_execution import effects
from new_sdd_workspace_successor_provider import WizardProvider, publisher_error

REPOSITORY = "FS-GG/.github"
REF = "refs/heads/fsgg/v2/journal/release/board-v2-product-creator-016"


def require(ok: bool, detail: str) -> None:
    if not ok:
        raise Refused(detail)


def publication_primitives():
    """Import inert shared predicates from this exact publisher checkout."""
    path = pathlib.Path(__file__).with_name("release-successor-publish.py")
    spec = importlib.util.spec_from_file_location("creator_publication_primitives", path)
    require(spec is not None and spec.loader is not None, "publication primitives unavailable")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def publication_report(candidate_run, artifact, archive, mode):
    return {"schema": "fsgg.creator016-publication-summary/1", "mode": mode,
            "candidateRunId": candidate_run, "candidateArtifactId": artifact,
            "candidateArchiveSha256": archive, "candidateSourceSha": None,
            "publisherSourceSha": None, "publisherRunId": None,
            "logicalJournalRef": REF, "physicalJournalRef": "refs/heads/main",
            "physicalJournalDirectory": "state/releases/board-v2-product-creator-016",
            "classifiedPhysicalHead": None, "fresh": None, "startingJournal": None,
            "lastObservedJournal": None, "journalMutationMayHaveOccurred": False,
            "lastDispatch": None, "iterationsAttempted": 0, "complete": False,
            "preflightPassed": False, "stage": "startup", "firstCause": None, "reportingFailure": None,
            "authority": None}


def safe_cause(error):
    # Do not project free text, arguments, request bodies or chained messages.
    kinds = {"Refused", "KeyError", "ValueError", "TypeError", "OSError", "RuntimeError",
             "CalledProcessError", "TimeoutExpired", "HTTPError", "URLError", "NotFound"}
    kind = type(error).__name__
    codes = {
        "fixed recovery Authority API ceiling exhausted; reconcile original operation": "authority-call-bound",
        "fixed recovery requires at least 4500 observed Authority core requests": "authority-quota-floor",
        "Authority core quota fields are malformed": "authority-quota-malformed",
        "Authority core quota resources are malformed": "authority-quota-malformed",
        "Authority core quota is unavailable": "authority-quota-unavailable",
        "Wizard publication observation deadline expired": "observation-deadline",
        "Wizard publication exceeded bounded reconciliation steps": "iteration-bound",
        "fresh Wizard publication requires exact current main candidate": "fresh-source-mismatch",
        "Wizard journal admission denied": "genesis-admission-denied",
    }
    result = {"kind": kind if kind in kinds else "Other",
            "exitCode": error.returncode if isinstance(error, subprocess.CalledProcessError)
            and type(error.returncode) is int else None}
    message = str(error)
    if len(message) <= 256 and message in codes:
        result["code"] = codes[message]
    return result


def safe_summary(report, counted):
    """Project only fixed fields and validated identifiers into the retained file."""
    fields = publication_report(None, None, None, "preflight")
    require(set(report) == set(fields), "Creator summary fields differ")
    value = dict(report)
    require(value["schema"] == fields["schema"] and value["mode"] in {"preflight", "publish"},
            "Creator summary schema or mode differs")
    for key in ("candidateSourceSha", "publisherSourceSha", "classifiedPhysicalHead"):
        raw = value[key]
        value[key] = raw if isinstance(raw, str) and re.fullmatch(r"[0-9a-f]{40}", raw) else None
    raw = value["candidateArchiveSha256"]
    value["candidateArchiveSha256"] = raw if isinstance(raw, str) and re.fullmatch(r"[0-9a-f]{64}", raw) else None
    for key in ("candidateRunId", "candidateArtifactId", "publisherRunId"):
        raw = value[key]
        value[key] = raw if type(raw) is int and raw > 0 else None
    for key in ("startingJournal", "lastObservedJournal"):
        row = value[key]
        if row is not None:
            known = {"tag", "draft", "github", "nuget", "package-asset", "manifest-asset",
                     "publication-journal-asset", "promote"}
            value[key] = {"generation": row["generation"] if type(row.get("generation")) is int else None,
                          **{name: row.get(name) if isinstance(row.get(name), str)
                             and re.fullmatch(r"[0-9a-f]{40}", row[name]) else None
                             for name in ("logicalHead", "physicalHead")},
                          "effects": {effect: state for effect, state in row.get("effects", {}).items()
                                      if effect in known and state in {"intent", "verified"}}}
    row = value["lastDispatch"]
    if row is not None:
        known = {"tag", "draft", "github", "nuget", "package-asset", "manifest-asset",
                 "publication-journal-asset", "promote"}
        value["lastDispatch"] = {"effect": row.get("effect") if row.get("effect") in known else None,
                                 "state": row.get("state") if row.get("state") in {"applied", "unknown", "refused"} else "unknown",
                                 "attempted": row.get("attempted") is True}
    if counted is not None:
        value["authority"] = {"ceiling": 4200, "quotaFloor": 4500,
                              "attemptedByMethod": dict(counted.attempted),
                              "succeededByMethod": dict(counted.succeeded),
                              "blockedBeforeSend": counted.blocked is not None,
                              "quota": counted.quota,
                              "scope": "application API entries; no reservation or wire-request claim"}
    return value


def retain_summary(path, report, counted):
    raw = (json.dumps(safe_summary(report, counted), sort_keys=True, separators=(",", ":")) + "\n").encode()
    require(len(raw) <= 64 * 1024, "Creator summary exceeds fixed bound")
    # Exclusive creation refuses stale evidence; failed retention never means success.
    fd = os.open(path, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(fd, "wb") as stream:
        stream.write(raw)


def prepare_publication(primitives, journal, counted, admission, provider, api, intent,
                        ordered, publisher_sha, preflight_only, report):
    """Classify the complete physical destination before fresh or recovery work."""
    fresh, physical = primitives.classify_journal_destination(counted, REF)
    report.update(fresh=fresh, classifiedPhysicalHead=physical)
    if fresh:
        require(intent["sourceSha"] == publisher_sha,
                "fresh Wizard publication requires exact current main candidate")
        for effect in ordered:
            require(provider.observe(effect).state == "absent",
                    "Wizard fresh publication has a preexisting or unreadable effect")
        require(primitives.authority_main(counted) == physical, "Authority main moved before Wizard preparation")
        require(admission.authorize(intent["contentId"], "journal", "intent", intent["contentId"]),
                "Wizard journal admission denied")
        if preflight_only:
            report["preflightPassed"] = True
            return True
        report["journalMutationMayHaveOccurred"] = True
        try:
            journal.initialize(intent)
        finally:
            journal.capture()
    else:
        require(primitives.authority_main(counted) == physical, "Authority main moved before Wizard recovery")
        journal.read()
        require(primitives.authority_main(counted) == physical, "Authority main moved during Wizard recovery")
        journal.validate_intent(intent)
        require(not preflight_only, "preflight requires unused Wizard journal and version")
        if intent["sourceSha"] != publisher_sha:
            comparison = api.get(f"repos/{REPOSITORY}/compare/{intent['sourceSha']}...{publisher_sha}")
            require(comparison.get("status") == "ahead", "recovery publisher does not descend from candidate")
    journal.validate_intent(intent)
    return False


def reconcile_publication(content_id, ordered, journal, admission, provider, report,
                          *, clock=time.monotonic, sleep=time.sleep):
    deadline = clock() + 45 * 60
    for index in range(len(ordered) * 2 + 120):
        require(clock() < deadline, "Wizard publication observation deadline expired")
        report["iterationsAttempted"] = index + 1
        result = advance_effects(content_id, ordered, journal, admission, provider)
        if result == "complete":
            report["complete"] = True
            return
        sleep(5)
    raise Refused("Wizard publication exceeded bounded reconciliation steps")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-run-id", required=True, type=int)
    parser.add_argument("--candidate-artifact-id", required=True, type=int)
    parser.add_argument("--candidate-archive-sha256", required=True)
    parser.add_argument("--workdir", required=True, type=pathlib.Path)
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preflight-only", action="store_true")
    mode.add_argument("--publish", action="store_true")
    mode.add_argument("--promotion-recovery", choices=("diagnostic", "complete"))
    parser.add_argument("--recovery-binding")
    args = parser.parse_args()
    if args.promotion_recovery:
        # Retained 0.13 route keeps its original error projection and entrypoint.
        try:
            from new_sdd_workspace_promote_recovery import entry, CANDIDATE_RUN, ARTIFACT, ARCHIVE
            require((args.candidate_run_id, args.candidate_artifact_id, args.candidate_archive_sha256) ==
                    (CANDIDATE_RUN, ARTIFACT, ARCHIVE), "recovery original candidate differs")
            require(args.recovery_binding is not None, "explicit recovery binding absent")
            return entry(args.promotion_recovery, args.recovery_binding, args.workdir, pathlib.Path(__file__).resolve().parents[1])
        except (KeyError, ValueError, OSError, Refused, subprocess.CalledProcessError) as error:
            print("Wizard successor refused: " + json.dumps(publisher_error(error), sort_keys=True), file=sys.stderr)
            return 1
    report = publication_report(args.candidate_run_id, args.candidate_artifact_id,
                                args.candidate_archive_sha256,
                                "preflight" if args.preflight_only else "publish")
    counted = None
    summary = args.workdir.parent / "creator016-publication-summary.json"
    outcome = 1
    try:
        require(args.recovery_binding is None, "recovery binding outside selected mode")
        publisher_sha = os.environ["GITHUB_SHA"]
        report["publisherSourceSha"] = publisher_sha
        operator = os.environ["GITHUB_ACTOR"]
        run_id = int(os.environ["GITHUB_RUN_ID"])
        report["publisherRunId"] = run_id
        require(os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch", "publisher event differs")
        require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY, "publisher repository differs")
        require(os.environ.get("GITHUB_REF") == "refs/heads/main", "publisher branch differs")
        require(os.environ.get("GITHUB_RUN_ATTEMPT") == "1", "publisher rerun is not admitted")
        require(operator == "EHotwagner", "publisher operator differs")
        require(re.fullmatch(r"[0-9a-f]{64}", args.candidate_archive_sha256) is not None, "candidate archive digest malformed")
        require(not summary.exists(), "Creator summary already exists")
        require(not args.workdir.exists(), "candidate workdir already exists")
        args.workdir.mkdir(parents=True)

        github_token = os.environ["GH_TOKEN"]
        ledger_token = os.environ["ORDINARY_LEDGER_TOKEN"]
        nuget_key = os.environ["NUGET_API_KEY"]
        report["stage"] = "candidate"
        api = GitHubAPI(github_token)
        artifact = api.get(f"repos/{REPOSITORY}/actions/artifacts/{args.candidate_artifact_id}")
        run = api.get(f"repos/{REPOSITORY}/actions/runs/{args.candidate_run_id}")
        require(run.get("id") == args.candidate_run_id, "candidate run API identity differs")
        require(artifact.get("id") == args.candidate_artifact_id, "candidate artifact API identity differs")
        candidate_source = run.get("head_sha")
        require(isinstance(candidate_source, str) and len(candidate_source) == 40, "candidate source malformed")
        report["candidateSourceSha"] = candidate_source
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
        verifier = pathlib.Path(__file__).with_name("new-sdd-workspace-successor-artifact.py")
        subprocess.run([sys.executable, str(verifier), "--artifact-json", str(artifact_json),
                        "--run-json", str(run_json), "--archive", str(archive),
                        "--source-sha", candidate_source, "--output", str(candidate)], check=True)
        manifest_path = candidate / "manifest.json"
        manifest = json.loads(manifest_path.read_text())
        content_id, ordered = effects(manifest)
        primitives = publication_primitives()
        report["stage"] = "quota"
        counted = primitives.CountedAuthorityAPI(GitHubAPI(ledger_token))
        counted.admit_quota()
        admission = primitives.ProtectedPublisherAdmission(
            WizardAdmission(api, manifest, publisher_sha, run_id, operator, "refs/heads/main"), counted, REF)
        provider = primitives.ReportingProvider(WizardProvider(api, manifest_path, github_token, nuget_key), report)
        intent = {"contentId": content_id, "sourceSha": candidate_source, "version": "0.16.0",
                  "candidateArchiveSha256": args.candidate_archive_sha256, "operator": operator}
        journal = primitives.ReportingJournal(ProtectedReleaseJournal(counted, REF, main_directory=True), report)
        report["stage"] = "preparation"
        preflight = prepare_publication(primitives, journal, counted, admission, provider, api, intent,
                                       ordered, publisher_sha, args.preflight_only, report)
        if not preflight:
            report["stage"] = "reconciliation"
            reconcile_publication(content_id, ordered, journal, admission, provider, report)
        report["stage"] = "terminal"
        outcome = 0
    except Exception as error:
        report["firstCause"] = safe_cause(error)
        print("Wizard successor refused: " + json.dumps(report["firstCause"], sort_keys=True), file=sys.stderr)
    try:
        retain_summary(summary, report, counted)
    except Exception as error:
        report["reportingFailure"] = safe_cause(error)
        print("Wizard successor summary retention failed: " + json.dumps(
            {"firstCause": report["firstCause"], "reportingFailure": report["reportingFailure"]},
            sort_keys=True), file=sys.stderr)
        outcome = 1
    return outcome


if __name__ == "__main__":
    raise SystemExit(main())
