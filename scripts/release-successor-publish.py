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
    selected = logical_ref.removeprefix(prefix)
    for depth, segment in enumerate(("state", "releases", selected)):
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
            require(depth == 2, "Authority required parent directory is missing")
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
                            candidate_source, publisher_sha, preflight_only, uniqueness, *, recovery_checkpoint=False):
    """Qualify the physical route before genesis or original-intent recovery."""
    fresh, physical = classify_journal_destination(ledger_api, journal.ref)
    if fresh:
        require(not recovery_checkpoint, "recovery checkpoint requires the existing original journal")
        require(candidate_source == publisher_sha, "fresh publication requires candidate and publisher source to match")
        try:
            api.get("repos/FS-GG/.github/git/ref/tags/coherent-set/v0.101.0")
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


# This profile recovers only the retained journal19 candidate. These are identity
# bindings, not credentials or an authority receipt for a new invocation.
RECOVERY_PROFILE = "utel-rel-19/four-advance/1"
AUTHORITY_CALL_CEILING = 4200
AUTHORITY_QUOTA_FLOOR = 4500
RECOVERY_ADVANCES = 4
RECOVERY_CANDIDATE = {
    "runId": 38026714312,
    "artifactId": 11660242226,
    "archiveSha256": "d5d7d68ab0f73ce020e36f6ae10321b632eb177162d925cd9faabc071891bd64",
    "sourceSha": "79051e56b025dadfc6fcaabd58b79e33f2854928",
    "contentId": "sha256:e8ed439047f663dfcb34aba1152ffd4c6966a0c3a27be0ebb48eebcf47e26996",
}


def error_chain(error):
    """Bound reporting while retaining the primary and explicitly omitted causes."""
    rows, seen = [], set()
    while error is not None and id(error) not in seen and len(rows) < 8:
        seen.add(id(error))
        message = str(error)
        rows.append({"type": type(error).__name__, "message": message[:512],
                     "messageTruncated": len(message) > 512})
        error = error.__cause__ if error.__cause__ is not None else error.__context__
    return {"chain": rows, "chainIncomplete": error is not None}


class CountedAuthorityAPI:
    """One counter for application calls through the original installation token.

    This does not count transport redirects, token provisioning, or provider calls.
    Its fixed ceiling cannot be increased by a CLI input or by token replacement.
    """
    def __init__(self, api):
        self.api = api
        self.attempted = {method: 0 for method in ("get", "post", "patch")}
        self.succeeded = {method: 0 for method in self.attempted}
        self.last_success = None
        self.last_failure = None
        self.blocked = None
        self.quota = None

    def _call(self, method, path, *args):
        if sum(self.attempted.values()) >= AUTHORITY_CALL_CEILING:
            self.blocked = {"method": method, "path": path[:256]}
            error = Refused("fixed recovery Authority API ceiling exhausted; reconcile original operation")
            self.last_failure = {"method": method, "path": path[:256], "error": error_chain(error)}
            raise error
        self.attempted[method] += 1
        try:
            result = getattr(self.api, method)(path, *args)
        except Exception as error:
            self.last_failure = {"method": method, "path": path[:256], "error": error_chain(error)}
            raise
        self.succeeded[method] += 1
        self.last_success = {"method": method, "path": path[:256]}
        return result

    def get(self, path):
        return self._call("get", path)

    def post(self, path, body):
        return self._call("post", path, body)

    def patch(self, path, body):
        return self._call("patch", path, body)

    def admit_quota(self, now=None):
        value = self.get("rate_limit")  # Counted before journal construction.
        require(isinstance(value, dict) and isinstance(value.get("resources"), dict),
                "Authority quota resources are malformed")
        core = value["resources"].get("core")
        require(isinstance(core, dict), "Authority core quota is unavailable")
        require(all(type(core.get(key)) is int for key in ("limit", "remaining", "reset", "used")),
                "Authority core quota fields are malformed")
        limit, remaining, reset, used = (core[key] for key in ("limit", "remaining", "reset", "used"))
        require(0 < limit <= 1000000000 and 0 <= remaining <= limit and 0 <= used <= limit
                and used + remaining == limit and 0 < reset < 2**63,
                "Authority core quota values are inconsistent")
        observed = time.time() if now is None else now
        require(reset > observed, "Authority core quota reset is not in the future")
        self.quota = {"resource": "core", "limit": limit, "remaining": remaining,
                      "reset": reset, "used": used, "observedUnix": observed}
        require(remaining >= AUTHORITY_QUOTA_FLOOR,
                "fixed recovery requires at least 4500 observed Authority core requests")
        return self.quota

    def report(self):
        return {"ceiling": AUTHORITY_CALL_CEILING, "attemptedByMethod": dict(self.attempted),
                "succeededByMethod": dict(self.succeeded), "lastSuccessfulEntry": self.last_success,
                "lastFailedEntry": self.last_failure, "blockedBeforeSend": self.blocked,
                "quota": self.quota, "scope": "application API entries; no reservation or wire-request claim"}


class ReportingJournal:
    """Observe existing successful reads without extra reads or altered CAS policy."""
    def __init__(self, journal, report):
        self.journal = journal
        self.report = report

    def __getattr__(self, name):
        return getattr(self.journal, name)

    def capture(self):
        observed = self.journal._observed
        if observed is not None:
            state = observed.state
            value = {"generation": state["generation"], "logicalHead": observed.head,
                     "physicalHead": self.journal._physical_head,
                     "effects": dict(state["effects"])}
            if self.report["startingJournal"] is None:
                self.report["startingJournal"] = value
            self.report["lastObservedJournal"] = value

    def read(self):
        state = self.journal.read()
        self.capture()
        return state

    def compare_and_swap(self, expected, effect, state):
        self.report["journalMutationMayHaveOccurred"] = True
        try:
            result = self.journal.compare_and_swap(expected, effect, state)
            return result
        finally:
            # A failed PATCH/readback never becomes a committed-CAS assertion.
            self.capture()


class ReportingProvider:
    """Retain dispatch uncertainty without retrying or changing provider behavior."""
    def __init__(self, provider, report):
        self.provider, self.report = provider, report

    def __getattr__(self, name):
        return getattr(self.provider, name)

    def observe(self, effect):
        return self.provider.observe(effect)

    def dispatch(self, effect):
        row = {"effect": effect.identity, "state": "unknown", "attempted": True}
        self.report["lastDispatch"] = row
        result = self.provider.dispatch(effect)
        row["state"] = result.state
        return result


def validate_checkpoint_state(manifest, journal):
    effects = ordered_effects(manifest)
    require(journal.ref == "refs/heads/fsgg/v2/journal/release/utel-rel-19"
            and journal.main_directory and len(effects) == 16,
            "fixed recovery journal or effect set differs")
    observed = journal._observed
    require(observed is not None, "fixed recovery has no successfully observed journal")
    current = observed.state
    require(type(current["generation"]) is int and 14 <= current["generation"] <= 33,
            "fixed recovery requires starting generation 14 through 33")
    known = {effect.identity for effect in effects}
    require(not set(current["effects"]) - known
            and all(value in {"intent", "verified"} for value in current["effects"].values()),
            "fixed recovery has an unknown effect or state")
    opened = False
    verified = intents = 0
    for effect in effects:
        state = current["effects"].get(effect.identity, "pending")
        require(not (state == "verified" and opened), "fixed recovery verified prefix is incoherent")
        if state == "verified":
            verified += 1
        else:
            require(not (opened and state != "pending"), "fixed recovery has multiple in-flight effects")
            opened = True
            intents += state == "intent"
    require(current["generation"] == 1 + 2 * verified + intents,
            "fixed recovery generation disagrees with the retained effect prefix")


def run_recovery_checkpoint(manifest, journal, admission, provider, report, deadline,
                            clock=time.monotonic, sleep=time.sleep):
    validate_checkpoint_state(manifest, journal)
    for index in range(RECOVERY_ADVANCES):
        require(clock() < deadline, "publication observation deadline expired; reconcile before another run")
        report["iterationsAttempted"] = index + 1
        report["stage"] = "advance"
        result = advance(manifest, journal, admission, provider)
        report["lastAdvanceResult"] = result
        report["lastSuccessfulStage"] = "advance"
        require(result in {"verified", "waiting", "complete"}, "unexpected recovery advance result")
        if result == "complete":
            return "complete"
        require(clock() < deadline, "publication observation deadline expired; reconcile before another run")
        if index + 1 < RECOVERY_ADVANCES:
            sleep(5)
    return "checkpoint"


def write_result(path, report):
    body = (json.dumps(report, sort_keys=True, separators=(",", ":")) + "\n").encode()
    require(len(body) <= 16384, "publisher result exceeds its 16KiB bound")
    with path.open("xb") as stream:
        stream.write(body)


def execute_publisher(args, report):
    report["stage"] = "publisher-context"
    publisher_sha = os.environ["GITHUB_SHA"]
    operator = os.environ["GITHUB_ACTOR"]
    run_id = int(os.environ["GITHUB_RUN_ID"])
    require(os.environ.get("GITHUB_EVENT_NAME") == "workflow_dispatch", "publisher event differs")
    require(os.environ.get("GITHUB_REPOSITORY") == REPOSITORY, "publisher repository differs")
    require(os.environ.get("GITHUB_REF") == "refs/heads/main", "publisher branch differs")
    require(os.environ.get("GITHUB_RUN_ATTEMPT") == "1", "publisher rerun is not admitted")
    require(operator == "EHotwagner", "publisher operator differs")
    require(len(args.candidate_archive_sha256) == 64, "candidate archive digest is malformed")
    if args.recovery_checkpoint:
        require(args.publish and not args.preflight_only, "recovery checkpoint requires publish, never preflight")
        require((args.candidate_run_id, args.candidate_artifact_id, args.candidate_archive_sha256) ==
                (RECOVERY_CANDIDATE["runId"], RECOVERY_CANDIDATE["artifactId"], RECOVERY_CANDIDATE["archiveSha256"]),
                "fixed recovery original candidate tuple differs")
    report["execution"] = {"sourceSha": publisher_sha, "runId": run_id,
                           "runAttempt": os.environ["GITHUB_RUN_ATTEMPT"], "operator": operator}
    report["lastSuccessfulStage"] = "publisher-context"
    require(not args.workdir.exists(), "candidate workdir already exists")
    args.workdir.mkdir(parents=True)

    github_token = os.environ["GH_TOKEN"]
    ledger_token = os.environ["ORDINARY_LEDGER_TOKEN"]
    nuget_key = os.environ["NUGET_API_KEY"]
    api = GitHubAPI(github_token)
    report["stage"] = "candidate-authentication"
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
    require(manifest["descriptor"]["version"] == "0.101.0", "publisher version differs")
    if args.recovery_checkpoint:
        require(candidate_source == RECOVERY_CANDIDATE["sourceSha"]
                and manifest["contentId"] == RECOVERY_CANDIDATE["contentId"],
                "fixed recovery original source or content differs")
    report["candidate"].update(sourceSha=candidate_source, contentId=manifest["contentId"], version="0.101.0")
    report["lastSuccessfulStage"] = "candidate-authentication"
    publisher_admission = SingleOperatorAdmission(api, manifest, publisher_sha, run_id, operator, "refs/heads/main")
    provider = LiveProvider(api, manifest_path, github_token, nuget_key)
    intent = {
        "contentId": manifest["contentId"],
        "sourceSha": candidate_source,
        "version": "0.101.0",
        "candidateArchiveSha256": args.candidate_archive_sha256,
        "operator": operator,
    }
    ledger_api = GitHubAPI(ledger_token)
    if args.recovery_checkpoint:
        ledger_api = CountedAuthorityAPI(ledger_api)
        report["authorityCounter"] = ledger_api
        report["stage"] = "Authority-quota"
        ledger_api.admit_quota()
        report["lastSuccessfulStage"] = "Authority-quota"
    journal = ProtectedReleaseJournal(ledger_api, ref=REF, main_directory=True)
    if args.recovery_checkpoint:
        journal = ReportingJournal(journal, report)
        provider = ReportingProvider(provider, report)
    admission = ProtectedPublisherAdmission(publisher_admission, ledger_api, REF)
    def uniqueness():
        subprocess.run(
            [sys.executable, str(pathlib.Path(__file__).with_name("check-release-candidate-uniqueness.py")),
             "--version", "0.101.0", "--predecessor", "0.100.0"],
            check=True, env={**os.environ, "GITHUB_TOKEN": github_token},
        )
    report["stage"] = "journal-preparation"
    if prepare_release_journal(journal, ledger_api, admission, provider, api, manifest, intent,
                               candidate_source, publisher_sha, args.preflight_only, uniqueness, recovery_checkpoint=args.recovery_checkpoint):
        print("Release successor preflight passed; no journal, tag, feed or release was changed.")
        return "preflight"
    report["lastSuccessfulStage"] = "journal-preparation"
    deadline = time.monotonic() + 45 * 60
    if args.recovery_checkpoint:
        return run_recovery_checkpoint(manifest, journal, admission, provider, report, deadline)
    max_steps = len(ordered_effects(manifest)) * 2 + 120
    for _ in range(max_steps):
        result = advance(manifest, journal, admission, provider)
        print(f"successor effect state: {result}", flush=True)
        if result == "complete":
            return "complete"
        if time.monotonic() >= deadline:
            raise Refused("publication observation deadline expired; reconcile before another run")
        time.sleep(5)
    raise Refused("publication exceeded bounded reconciliation steps")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--candidate-run-id", required=True, type=int)
    parser.add_argument("--candidate-artifact-id", required=True, type=int)
    parser.add_argument("--candidate-archive-sha256", required=True)
    parser.add_argument("--workdir", required=True, type=pathlib.Path)
    parser.add_argument("--result-path", type=pathlib.Path)
    parser.add_argument("--recovery-checkpoint", action="store_true",
                        help="Recover only the retained journal19 candidate, with at most four advances")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preflight-only", action="store_true")
    mode.add_argument("--publish", action="store_true")
    args = parser.parse_args()
    result_path = args.result_path or args.workdir.with_name(args.workdir.name + "-result.json")
    report = {"schema": "fsgg.release-successor-result/1",
              "profile": RECOVERY_PROFILE if args.recovery_checkpoint else "ordinary/1",
              "candidate": {"runId": args.candidate_run_id, "artifactId": args.candidate_artifact_id,
                            "archiveSha256": args.candidate_archive_sha256},
              "execution": None, "originalPublisher": {"runId": 38029395937, "runAttempt": 1, "jobId": 114146971214} if args.recovery_checkpoint else None,
              "journalRef": REF, "startingJournal": None, "lastObservedJournal": None,
              "iterationsAttempted": 0, "lastAdvanceResult": None, "lastDispatch": None,
              "journalMutationMayHaveOccurred": False, "stage": "preparation",
              "lastSuccessfulStage": None, "disposition": "failed-or-unknown",
              "publicationComplete": False, "failure": None}
    exit_code = 1
    try:
        require(not result_path.exists() and not result_path.is_symlink(), "publisher result path already exists")
        disposition = execute_publisher(args, report)
        report["disposition"] = disposition
        report["publicationComplete"] = disposition == "complete"
        exit_code = 0
    except BaseException as error:
        report["failure"] = error_chain(error)
        print(f"release successor refused: {error}", file=sys.stderr)
    counter = report.pop("authorityCounter", None)
    report["authorityRequests"] = counter.report() if counter is not None else None
    try:
        write_result(result_path, report)
    except Exception as reporting_error:
        # Reporting cannot replace the primary cause or normalize a failed effect.
        print(f"release successor result reporting failed: {reporting_error}", file=sys.stderr)
        return 1
    output = os.environ.get("GITHUB_OUTPUT")
    if output:
        try:
            with open(output, "a") as stream:
                stream.write(f"disposition={report['disposition']}\n")
                stream.write(f"publication_complete={str(report['publicationComplete']).lower()}\n")
        except OSError as reporting_error:
            print(f"release successor output reporting failed: {reporting_error}", file=sys.stderr)
            return 1
    print(f"Release successor result: {report['disposition']}; publicationComplete={report['publicationComplete']}")
    return exit_code


if __name__ == "__main__":
    raise SystemExit(main())
