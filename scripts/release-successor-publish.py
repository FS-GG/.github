#!/usr/bin/env python3
"""Publish one exact, independently verified candidate through protected effects."""

from __future__ import annotations

import argparse
import datetime
import hashlib
import importlib.util
import io
import json
import os
import pathlib
import re
import stat
import subprocess
import sys
import time
import zipfile

from release_successor_admission import SingleOperatorAdmission
from release_successor_execution import Refused, advance, ordered_effects
from release_successor_journal import ProtectedReleaseJournal, REF, REPOSITORY as JOURNAL_REPOSITORY, canonical
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
KIT_REPEAT_PROFILE = "utel-rel-19/original-nuget-kit-repeat/1"
RUNTIME_RECOVERY_PROFILE = "utel-rel-19/original-runtime-intent-recovery/1"
RUNTIME_EFFECT = "qualification-asset:runtime"
RUNTIME_NAME = "standalone-telemetry-runtime-evidence.json"
ORIGINAL_RUNTIME_HEAD = "e25521bff7f24dc0c454327c854f0d912fc6fa2c"
ORIGINAL_RUNTIME_STATE_SHA256 = "8b7b4995a328d9fc300f9db579aeeb7703d1d03c80834f95df55b43ec1d93829"
RUNTIME_REFUSAL_SOURCE = "a988d25c68263ce44a1d0f48c3a1e72f2428fa82"
RUNTIME_REFUSAL_ZIP_SHA256 = "7f50582d26f171d2d446ad5eed10268fe169b07e321881b97f41a59fe9232107"
RUNTIME_REFUSAL_MEMBER_SHA256 = "76dcf6db0ed2f138d22fb19678ec16cdaa09d7a6aabb537c75e8d5c11295dffb"
KIT_EFFECT = "nuget:FS.GG.Kit"
KIT_ENDPOINT = "https://api.nuget.org/v3/index.json"
ORIGINAL_KIT_JOURNAL_HEAD = "30edfbd8aee914492ce359f4dd34454701a41d5a"
ORIGINAL_KIT_STATE_SHA256 = "494c772deaadd71f635a50237fc4dee0077c71c71968ee4c3ff6ef31a25e1dce"
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


class RuntimePublisherAPI:
    """Attribute bounded publisher calls separately from the Authority counter."""
    def __init__(self, api):
        self.api, self.attempted, self.succeeded = api, {}, {}

    def __getattr__(self, name):
        if name not in {"get", "runtime_recovery_json", "download_asset", "download_runtime_refusal_artifact",
                        "download_runtime_recovery_asset", "upload_runtime_recovery_asset"}:
            raise Refused("runtime recovery API method is not selected")
        def call(*args):
            require(sum(self.attempted.values()) < 256, "runtime recovery publisher API bound exhausted")
            self.attempted[name] = self.attempted.get(name, 0) + 1
            result = getattr(self.api, name)(*args)
            self.succeeded[name] = self.succeeded.get(name, 0) + 1
            return result
        return call

    def report(self):
        return {"attempted": self.attempted, "succeeded": self.succeeded, "ceiling": 256,
                "scope": "publisher API application entries; candidate gh ZIP, feed downloads/subprocesses and redirects are separate"}


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


class KitObservationProvider(ReportingProvider):
    """Record ordinary observations; only the explicit repeat seam enables one send."""
    def __init__(self, provider, report, effect):
        super().__init__(provider, report)
        self.effect = effect
        self.observations = []
        self.repeat_enabled = False
        self.repeat_attempted = False

    def observe(self, effect):
        value = self.provider.observe(effect)
        self.observations.append({"effect": effect.identity, "state": value.state,
                                  "observedDigest": value.observed_digest})
        self.report["kitRepeat"]["observations"] = list(self.observations)
        return value

    def dispatch(self, effect):
        require(self.repeat_enabled and not self.repeat_attempted and effect == self.effect
                and effect.identity == KIT_EFFECT, "original Kit repeat send is not admitted")
        self.repeat_attempted = True  # Exceptions still consume this invocation's send.
        return super().dispatch(effect)


class UnchangedKitJournal:
    """Fence ordinary pre-send reads to the exact retained generation14 binding."""
    def __init__(self, journal, observed, physical):
        self.journal, self.observed, self.physical = journal, observed, physical

    def read(self):
        value = self.journal.read()
        require(self.journal._observed == self.observed
                and self.journal._physical_head == self.physical,
                "original Kit journal moved before the repeat boundary")
        return value

    def compare_and_swap(self, *args):
        return self.journal.compare_and_swap(*args)


def validate_original_kit_state(manifest, journal):
    validate_checkpoint_state(manifest, journal)
    effects = ordered_effects(manifest)
    expected = {effect.identity: "verified" for effect in effects[:6]}
    expected[KIT_EFFECT] = "intent"
    observed = journal._observed
    require(observed.head == ORIGINAL_KIT_JOURNAL_HEAD
            and hashlib.sha256(canonical(observed.state)).hexdigest() == ORIGINAL_KIT_STATE_SHA256
            and observed.state["generation"] == 14 and observed.state["effects"] == expected
            and observed.state["contentId"] == manifest["contentId"]
            and effects[6].identity == KIT_EFFECT,
            "original Kit repeat requires the exact retained head and generation14 prefix")
    return effects[6]


def verify_kit_repeat_candidate(manifest, provider, manifest_bytes):
    """Recheck original manifest/archive/payload locally using existing pure semantics."""
    require(provider.manifest_path.read_bytes() == manifest_bytes
            and provider.manifest == manifest and provider.content_id == manifest["contentId"]
            and provider.version == "0.101.0" and provider.source == manifest["descriptor"]["sourceSha"],
            "original Kit repeat manifest or provider binding changed")
    effect = next(effect for effect in ordered_effects(manifest) if effect.identity == KIT_EFFECT)
    package = next(row for row in manifest["descriptor"]["packages"] if row["id"] == "FS.GG.Kit")
    path = provider.root / "FS.GG.Kit.0.101.0.nupkg"
    require(package["artifact"]["path"] == path.name and not path.is_symlink()
            and stat.S_ISREG(path.stat().st_mode) and path.resolve().parent == provider.root.resolve(),
            "original Kit repeat archive is not the owned exact candidate file")
    saga_path = pathlib.Path(__file__).with_name("release-saga.py")
    spec = importlib.util.spec_from_file_location("kit_repeat_payload", saga_path)
    saga = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(saga)
    require(saga.sha256(path) == effect.request_digest
            and saga.payload_id(path) == effect.target_digest
            and saga.nuspec(path)[:2] == ("FS.GG.Kit", "0.101.0"),
            "original Kit repeat archive, normalized payload or package identity differs")
    require(provider._feed_url("nuget", "FS.GG.Kit") ==
            "https://api.nuget.org/v3-flatcontainer/fs.gg.kit/0.101.0/fs.gg.kit.0.101.0.nupkg",
            "original Kit repeat observation endpoint differs")
    return {"effect": KIT_EFFECT, "packageId": "FS.GG.Kit", "version": "0.101.0", "feed": "nuget.org",
            "archiveSha256": effect.request_digest, "payloadSha256": effect.target_digest,
            "argv": ["dotnet", "nuget", "push", str(path), "--source", KIT_ENDPOINT,
                     "--api-key", "<credential omitted>"],
            "localFile": {"device": path.stat().st_dev, "inode": path.stat().st_ino,
                          "size": path.stat().st_size, "mtimeNs": path.stat().st_mtime_ns}}


def write_kit_repeat_attempt(path, record):
    """One exclusive fsynced local invocation record, never a global retry lease."""
    body = (json.dumps(record, sort_keys=True, separators=(",", ":")) + "\n").encode()
    require(len(body) <= 16384, "original Kit repeat attempt record exceeds16KiB")
    with path.open("xb") as stream:
        stream.write(body)
        stream.flush()
        os.fsync(stream.fileno())
    directory = os.open(path.parent, os.O_RDONLY | os.O_DIRECTORY)
    try:
        os.fsync(directory)
    finally:
        os.close(directory)


def run_original_kit_repeat(manifest, journal, admission, provider, ledger_api, report, deadline,
                            verify_candidate, attempt_path, clock=time.monotonic):
    effect = validate_original_kit_state(manifest, journal)
    observed = journal._observed
    physical = journal._physical_head
    report["kitRepeat"] = {"profile": KIT_REPEAT_PROFILE, "effect": KIT_EFFECT,
                           "maxDispatchesThisInvocation": 1, "historicalNonDispatchProved": False,
                           "globalOnceClaim": False, "observations": [], "attemptRecord": None,
                           "branch": "ordinary-observation"}
    guarded = UnchangedKitJournal(journal, observed, physical)
    recorded = KitObservationProvider(provider, report, effect)
    require(clock() < deadline, "original Kit repeat observation deadline expired")
    report["iterationsAttempted"] = 1
    result = advance(manifest, guarded, admission, recorded)
    report["lastAdvanceResult"] = result
    report["lastSuccessfulStage"] = "advance"
    require(clock() < deadline, "original Kit repeat observation deadline expired")
    if result == "verified":
        report["kitRepeat"]["branch"] = "matched-settlement-no-send"
        return "checkpoint"  # Ordinary matched CAS14→15; no second advance/send.
    require(result == "waiting" and recorded.observations[-1] ==
            {"effect": KIT_EFFECT, "state": "absent", "observedDigest": None},
            "original Kit repeat lacks the targeted fresh absent observation")
    require(clock() < deadline, "original Kit repeat observation deadline expired")
    report["stage"] = "repeat-unchanged-journal"
    guarded.read()  # One extra read only, with all ordinary canonical/history checks.
    candidate = verify_candidate()
    require(candidate["effect"] == KIT_EFFECT and candidate["feed"] == "nuget.org"
            and candidate["packageId"] == "FS.GG.Kit" and candidate["version"] == "0.101.0"
            and candidate["archiveSha256"] == effect.request_digest
            and candidate["payloadSha256"] == effect.target_digest
            and candidate["argv"][0:3] == ["dotnet", "nuget", "push"]
            and candidate["argv"][4:] == ["--source", KIT_ENDPOINT, "--api-key", "<credential omitted>"],
            "original Kit repeat candidate or push route differs")
    require(admission.authorize(manifest["contentId"], effect.identity, "dispatch", effect.request_digest),
            "original Kit repeat fresh dispatch admission denied")
    require(sum(ledger_api.attempted.values()) < AUTHORITY_CALL_CEILING,
            "original Kit repeat Authority ceiling exhausted before send")
    require(clock() < deadline, "original Kit repeat observation deadline expired")
    record = {"schema": "fsgg.original-kit-repeat-attempt/1", "profile": KIT_REPEAT_PROFILE,
              "execution": report.get("execution"), "originalPublisher": report.get("originalPublisher"),
              "candidate": report.get("candidate"), "effect": candidate,
              "journal": {"logicalHead": observed.head, "physicalHead": physical, "generation": 14,
                          "canonicalStateSha256": hashlib.sha256(canonical(observed.state)).hexdigest()},
              "absentObservation": recorded.observations[-1], "originalIntentRetained": True,
              "historicalNonDispatchProved": False, "globalOnceClaim": False,
              "permissionScope": "one dispatch in this explicitly selected invocation only",
              "providerOutcome": "not yet delegated; any prior send remains uncertain"}
    report["stage"] = "repeat-presend-record"
    write_kit_repeat_attempt(attempt_path, record)
    report["kitRepeat"]["attemptRecord"] = {"path": str(attempt_path),
                                             "sha256": hashlib.sha256(attempt_path.read_bytes()).hexdigest()}
    require(verify_candidate() == candidate, "original Kit repeat local candidate changed before send")
    require(sum(ledger_api.attempted.values()) < AUTHORITY_CALL_CEILING and clock() < deadline,
            "original Kit repeat budget or deadline exhausted before delegation")
    report["kitRepeat"]["branch"] = "one-explicit-repeat"
    report["stage"] = "repeat-provider-dispatch"
    recorded.repeat_enabled = True
    outcome = recorded.dispatch(effect)
    require(outcome.state == "applied", "original Kit repeat outcome unconfirmed; reconcile without retry")
    report["lastSuccessfulStage"] = "repeat-provider-dispatch"
    return "checkpoint"  # No settlement, final reread, other effect or second send.


def authenticate_runtime_refusal(api):
    """Authenticate the fixed original refusal, never caller-provided JSON."""
    run = api.runtime_recovery_json(f"repos/{REPOSITORY}/actions/runs/38063768172")
    require(run.get("id") == 38063768172 and run.get("workflow_id") == 362181999
            and run.get("run_attempt") == 1 and run.get("head_sha") == RUNTIME_REFUSAL_SOURCE
            and run.get("path") == ".github/workflows/release-successor-publish.yml"
            and run.get("head_branch") == "main" and run.get("event") == "workflow_dispatch"
            and run.get("actor", {}).get("login") == "EHotwagner"
            and run.get("repository", {}).get("id") == 1269292704
            and run.get("repository", {}).get("full_name") == REPOSITORY
            and run.get("status") == "completed" and run.get("conclusion") == "failure",
            "original runtime refusal run identity differs")
    artifact = api.runtime_recovery_json(f"repos/{REPOSITORY}/actions/artifacts/11675095562")
    require(artifact.get("id") == 11675095562 and artifact.get("name") == "release-successor-result"
            and artifact.get("size_in_bytes") == 1413 and artifact.get("expired") is False
            and artifact.get("digest") == "sha256:" + RUNTIME_REFUSAL_ZIP_SHA256
            and artifact.get("workflow_run", {}).get("id") == 38063768172
            and artifact.get("workflow_run", {}).get("head_sha") == RUNTIME_REFUSAL_SOURCE,
            "original runtime refusal artifact identity differs")
    expiry = datetime.datetime.fromisoformat(artifact["expires_at"].replace("Z", "+00:00"))
    require(expiry.tzinfo is not None and expiry > datetime.datetime.now(datetime.timezone.utc),
            "original runtime refusal artifact expiry passed")
    raw = api.download_runtime_refusal_artifact()
    require(len(raw) == 1413 and hashlib.sha256(raw).hexdigest() == RUNTIME_REFUSAL_ZIP_SHA256,
            "original runtime refusal archive differs")
    with zipfile.ZipFile(io.BytesIO(raw)) as archive:
        entries = archive.infolist()
        require(len(entries) == 1 and entries[0].filename == "release-successor-result.json"
                and not entries[0].flag_bits & 1 and not entries[0].is_dir()
                and stat.S_IFMT(entries[0].external_attr >> 16) in (0, stat.S_IFREG)
                and 0 < entries[0].file_size <= 16384, "original runtime refusal member differs")
        body = archive.read(entries[0])
    require(len(body) <= 16384 and hashlib.sha256(body).hexdigest() == RUNTIME_REFUSAL_MEMBER_SHA256,
            "original runtime refusal member bytes differ")
    def unique(items):
        value = {}
        for key, item in items:
            require(key not in value, "original runtime refusal duplicate JSON key")
            value[key] = item
        return value
    value = json.loads(body, object_pairs_hook=unique,
                       parse_constant=lambda _: (_ for _ in ()).throw(Refused("nonfinite runtime refusal JSON")))
    require(value.get("schema") == "fsgg.release-successor-result/1" and value.get("profile") == RECOVERY_PROFILE
            and value.get("execution") == {"operator": "EHotwagner", "runAttempt": "1", "runId": 38063768172,
                                           "sourceSha": RUNTIME_REFUSAL_SOURCE}
            and value.get("candidate") == {**RECOVERY_CANDIDATE, "version": "0.101.0"}
            and value.get("originalPublisher") == {"runId": 38029395937, "runAttempt": 1, "jobId": 114146971214}
            and value.get("disposition") == "failed-or-unknown" and value.get("publicationComplete") is False
            and value.get("stage") == "advance" and value.get("iterationsAttempted") == 3
            and value.get("journalRef") == REF and value.get("journalMutationMayHaveOccurred") is True
            and value.get("lastDispatch") == {"attempted": True, "effect": "qualification-asset:evidence", "state": "applied"}
            and value.get("failure") == {"chain": [{"message": "qualification-asset:runtime: dispatch admission denied",
                                                       "messageTruncated": False, "type": "Refused"}], "chainIncomplete": False}
            and value.get("lastObservedJournal", {}).get("generation") == 26
            and value["lastObservedJournal"].get("logicalHead") == ORIGINAL_RUNTIME_HEAD,
            "original runtime refusal semantic binding differs")
    return {"runId": 38063768172, "runAttempt": 1, "sourceSha": RUNTIME_REFUSAL_SOURCE,
            "artifactId": 11675095562, "archiveSha256": RUNTIME_REFUSAL_ZIP_SHA256,
            "memberSha256": RUNTIME_REFUSAL_MEMBER_SHA256, "thisInvocationRuntimeDispatchEntered": False,
            "historicalNonDispatchProved": False, "globalOnceClaim": False}


def validate_original_runtime_state(manifest, journal):
    validate_checkpoint_state(manifest, journal)
    effects = ordered_effects(manifest)
    expected = {effect.identity: "verified" for effect in effects[:12]}
    expected[RUNTIME_EFFECT] = "intent"
    observed = journal._observed
    require(observed.head == ORIGINAL_RUNTIME_HEAD and observed.state["generation"] == 26
            and hashlib.sha256(canonical(observed.state)).hexdigest() == ORIGINAL_RUNTIME_STATE_SHA256
            and observed.state["effects"] == expected and effects[12].identity == RUNTIME_EFFECT
            and observed.state["contentId"] == manifest["contentId"],
            "runtime recovery requires the exact original generation26 intent")
    return effects[12]


def verify_runtime_candidate(manifest, provider, manifest_bytes, archive):
    require(provider.manifest_path.read_bytes() == manifest_bytes and provider.manifest == manifest
            and provider.content_id == RECOVERY_CANDIDATE["contentId"]
            and provider.source == RECOVERY_CANDIDATE["sourceSha"] and provider.version == "0.101.0"
            and not archive.is_symlink() and stat.S_ISREG(archive.stat().st_mode)
            and hashlib.sha256(archive.read_bytes()).hexdigest() == RECOVERY_CANDIDATE["archiveSha256"],
            "runtime recovery original candidate drift")
    path = provider.root / RUNTIME_NAME
    qualification = manifest["descriptor"]["standaloneTelemetry"]
    require(qualification["qualificationPath"] == RUNTIME_NAME and not path.is_symlink()
            and path.resolve().parent == provider.root.resolve() and stat.S_ISREG(path.stat().st_mode)
            and 0 < path.stat().st_size <= 1048576
            and hashlib.sha256(path.read_bytes()).hexdigest() == qualification["qualificationSha256"],
            "runtime recovery original payload drift")
    info = path.stat()
    return {"effect": RUNTIME_EFFECT, "name": RUNTIME_NAME, "payloadSha256": qualification["qualificationSha256"],
            "localFile": {"device": info.st_dev, "inode": info.st_ino, "size": info.st_size, "mtimeNs": info.st_mtime_ns}}


class RuntimeRecoveryProvider(ReportingProvider):
    def __init__(self, provider, report, effect):
        super().__init__(provider, report)
        self.effect, self.witness = effect, None
        self.send_enabled = self.send_consumed = False

    def observe(self, effect):
        if effect.identity != RUNTIME_EFFECT:
            return self.provider.observe(effect)
        value, self.witness = self.provider.observe_runtime_recovery(effect)
        self.report["runtimeRecovery"]["targetObservation"] = {"state": value.state, "witness": self.witness}
        return value

    def dispatch(self, effect):
        require(self.send_enabled and not self.send_consumed and effect == self.effect,
                "runtime recovery send is not admitted")
        self.send_consumed = True  # Any exception consumes this invocation's permission.
        self.report["lastDispatch"] = {"effect": effect.identity, "attempted": True, "state": "unknown"}
        result = self.provider.dispatch_runtime_recovery(effect, self.witness)
        self.report["lastDispatch"]["state"] = result.state
        return result


def run_original_runtime_recovery(manifest, journal, admission, provider, ledger_api, report, deadline,
                                  proof, verify_candidate, attempt_path, clock=time.monotonic):
    effect = validate_original_runtime_state(manifest, journal)
    observed, physical = journal._observed, journal._physical_head
    require(proof == {"runId": 38063768172, "runAttempt": 1, "sourceSha": RUNTIME_REFUSAL_SOURCE,
                      "artifactId": 11675095562, "archiveSha256": RUNTIME_REFUSAL_ZIP_SHA256,
                      "memberSha256": RUNTIME_REFUSAL_MEMBER_SHA256, "thisInvocationRuntimeDispatchEntered": False,
                      "historicalNonDispatchProved": False, "globalOnceClaim": False},
            "runtime recovery authenticated refusal proof differs")
    report["runtimeRecovery"] = {"profile": RUNTIME_RECOVERY_PROFILE, "originalRefusal": proof,
                                 "maxDispatchesThisInvocation": 1, "globalOnceClaim": False,
                                 "historicalNonDispatchProved": False, "branch": "ordinary-observation"}
    guarded = UnchangedKitJournal(journal, observed, physical)  # Same exact logical/physical read fence.
    recorded = RuntimeRecoveryProvider(provider, report, effect)
    candidate = verify_candidate()  # Required even for the zero-send matched branch.
    require(clock() < deadline, "runtime recovery deadline expired")
    report["iterationsAttempted"] = 1
    result = advance(manifest, guarded, admission, recorded)
    report["lastAdvanceResult"] = result
    report["lastSuccessfulStage"] = "advance"
    require(clock() < deadline, "runtime recovery deadline expired")
    if result == "verified":
        report["runtimeRecovery"]["branch"] = "matched-settlement-no-send"
        return "checkpoint"  # Normal CAS26->27, no other effect.
    require(result == "waiting" and report["runtimeRecovery"]["targetObservation"]["state"] == "absent",
            "runtime recovery lacks a complete fresh absence witness")
    witness = recorded.witness
    guarded.read()  # Full original history, exact selected state and paired physical fence.
    validate_original_runtime_state(manifest, journal)
    require(verify_candidate() == candidate, "runtime recovery payload changed during observation")
    require(recorded.observe(effect).state == "absent" and recorded.witness == witness,
            "runtime recovery release or asset population moved")
    require(clock() < deadline, "runtime recovery deadline expired")
    record = {"schema": "fsgg.original-runtime-recovery-attempt/1", "profile": RUNTIME_RECOVERY_PROFILE,
              "execution": report.get("execution"), "originalPublisher": report.get("originalPublisher"),
              "originalRefusal": proof, "candidate": report.get("candidate"), "effect": candidate,
              "journal": {"logicalHead": observed.head, "physicalHead": physical, "generation": 26,
                          "canonicalStateSha256": ORIGINAL_RUNTIME_STATE_SHA256},
              "absenceWitness": witness, "originalIntentRetained": True, "globalOnceClaim": False,
              "permissionScope": "one fixed runtime upload in this explicitly admitted invocation only",
              "providerOutcome": "not delegated; prior operations require root reconciliation"}
    report["stage"] = "runtime-presend-record"
    write_kit_repeat_attempt(attempt_path, record)  # Existing exclusive fsynced record mechanism.
    report["runtimeRecovery"]["attemptRecord"] = {"path": str(attempt_path),
                                                 "sha256": hashlib.sha256(attempt_path.read_bytes()).hexdigest()}
    require(admission.authorize(manifest["contentId"], effect.identity, "dispatch", effect.request_digest),
            "runtime recovery fresh dispatch admission denied")
    require(authority_main(ledger_api) == physical, "runtime recovery physical head moved before upload")
    require(verify_candidate() == candidate, "runtime recovery payload changed before upload")
    require(sum(ledger_api.attempted.values()) < AUTHORITY_CALL_CEILING and clock() < deadline
            and ledger_api.quota is not None and ledger_api.quota["reset"] > time.time(),
            "runtime recovery quota, counter or deadline expired before upload")
    report["runtimeRecovery"]["branch"] = "one-explicit-runtime-upload"
    report["stage"] = "runtime-provider-dispatch"
    recorded.send_enabled = True
    outcome = recorded.dispatch(effect)
    require(outcome.state == "applied", "runtime recovery upload unconfirmed; reconcile without retry")
    report["lastSuccessfulStage"] = "runtime-provider-dispatch"
    return "checkpoint"  # Still26/intent: no final read, settlement or later effect.


def execute_publisher(args, report):
    runtime_recovery = getattr(args, "recover_original_runtime_intent", False)
    runtime_deadline = time.monotonic() + 45 * 60 if runtime_recovery else None
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
    if args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery:
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
    if runtime_recovery:
        api = RuntimePublisherAPI(api)
        report["publisherCounter"] = api
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
    manifest_bytes = manifest_path.read_bytes()
    manifest = json.loads(manifest_bytes)
    require(manifest["descriptor"]["version"] == "0.101.0", "publisher version differs")
    if args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery:
        require(candidate_source == RECOVERY_CANDIDATE["sourceSha"]
                and manifest["contentId"] == RECOVERY_CANDIDATE["contentId"],
                "fixed recovery original source or content differs")
    report["candidate"].update(sourceSha=candidate_source, contentId=manifest["contentId"], version="0.101.0")
    report["lastSuccessfulStage"] = "candidate-authentication"
    publisher_admission = SingleOperatorAdmission(api, manifest, publisher_sha, run_id, operator, "refs/heads/main")
    provider = LiveProvider(api, manifest_path, github_token, nuget_key)
    runtime_proof = None
    if runtime_recovery:
        report["stage"] = "runtime-original-refusal-authentication"
        runtime_proof = authenticate_runtime_refusal(api)
        report["runtimeOriginalRefusal"] = runtime_proof
        require(time.monotonic() < runtime_deadline, "runtime recovery deadline expired")
    intent = {
        "contentId": manifest["contentId"],
        "sourceSha": candidate_source,
        "version": "0.101.0",
        "candidateArchiveSha256": args.candidate_archive_sha256,
        "operator": operator,
    }
    ledger_api = GitHubAPI(ledger_token)
    if args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery:
        ledger_api = CountedAuthorityAPI(ledger_api)
        report["authorityCounter"] = ledger_api
        report["stage"] = "Authority-quota"
        ledger_api.admit_quota()
        report["lastSuccessfulStage"] = "Authority-quota"
    journal = ProtectedReleaseJournal(ledger_api, ref=REF, main_directory=True)
    if args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery:
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
                               candidate_source, publisher_sha, args.preflight_only, uniqueness,
                               recovery_checkpoint=args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or runtime_recovery):
        print("Release successor preflight passed; no journal, tag, feed or release was changed.")
        return "preflight"
    report["lastSuccessfulStage"] = "journal-preparation"
    deadline = time.monotonic() + 45 * 60
    if runtime_recovery:
        return run_original_runtime_recovery(
            manifest, journal, admission, provider, ledger_api, report, runtime_deadline, runtime_proof,
            lambda: verify_runtime_candidate(manifest, provider, manifest_bytes, archive),
            args.result_path.with_name(args.result_path.stem + "-runtime-recovery-attempt.json"))
    if args.repeat_original_nuget_kit_intent:
        return run_original_kit_repeat(
            manifest, journal, admission, provider, ledger_api, report, deadline,
            lambda: verify_kit_repeat_candidate(manifest, provider, manifest_bytes),
            args.result_path.with_name(args.result_path.stem + "-kit-repeat-attempt.json"))
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
    profile = parser.add_mutually_exclusive_group()
    profile.add_argument("--recovery-checkpoint", action="store_true",
                        help="Recover only the retained journal19 candidate, with at most four advances")
    profile.add_argument("--repeat-original-nuget-kit-intent", action="store_true",
                         help="Explicitly permit one same-original nuget.org Kit send under retained intent uncertainty")
    profile.add_argument("--recover-original-runtime-intent", action="store_true",
                         help="Recover only the authenticated original generation26 runtime intent; one upload or matched settlement")
    mode = parser.add_mutually_exclusive_group(required=True)
    mode.add_argument("--preflight-only", action="store_true")
    mode.add_argument("--publish", action="store_true")
    args = parser.parse_args()
    result_path = args.result_path or args.workdir.with_name(args.workdir.name + "-result.json")
    args.result_path = result_path
    report = {"schema": "fsgg.release-successor-result/1",
              "profile": RUNTIME_RECOVERY_PROFILE if args.recover_original_runtime_intent else KIT_REPEAT_PROFILE if args.repeat_original_nuget_kit_intent else RECOVERY_PROFILE if args.recovery_checkpoint else "ordinary/1",
              "candidate": {"runId": args.candidate_run_id, "artifactId": args.candidate_artifact_id,
                            "archiveSha256": args.candidate_archive_sha256},
              "execution": None, "originalPublisher": {"runId": 38029395937, "runAttempt": 1, "jobId": 114146971214} if args.recovery_checkpoint or args.repeat_original_nuget_kit_intent or args.recover_original_runtime_intent else None,
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
    publisher_counter = report.pop("publisherCounter", None)
    if publisher_counter is not None:
        report["publisherRequests"] = publisher_counter.report()
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
