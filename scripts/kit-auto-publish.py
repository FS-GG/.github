#!/usr/bin/env python3
"""Fail-closed decision engine for the FS.GG.Kit auto-publish coordinator.

This program deliberately has no tag, feed, GitHub, or PR write capability.  The
workflow supplies observed facts and performs a write only when this program
returns the single ``tag`` action.  Keeping the irreversible edge outside the
state machine makes dry-run/mutation tests meaningful and prevents a partial
publish from turning into a blind retry.
"""
import argparse
import json
import re
import sys


PATCH = re.compile(r"^0\.(\d+)\.(\d+)$")
REQUIRED_FACTS = (
    "version", "provenance", "orgFeed", "nugetFeed", "orgLatest", "nugetLatest", "tagExists",
)


def patch_tuple(value):
    match = PATCH.fullmatch(value or "")
    return (int(match.group(1)), int(match.group(2))) if match else None


def escalation(previous, action, reason, version, run):
    """Typed, deterministic sticky-marker state; malformed history never resets."""
    if previous is None:
        streak = 1
    elif (not isinstance(previous, dict) or set(previous) != {"valid", "streak", "action", "reason", "version", "lastRun"}
          or previous.get("valid") is not True or not isinstance(previous.get("streak"), int) or previous["streak"] < 1
          or not all(isinstance(previous.get(key), str) and previous[key] for key in ("action", "reason", "version", "lastRun"))):
        return {"valid": False, "reason": "sticky-marker-malformed"}
    elif previous["lastRun"] == run:
        if previous["action"] != action or previous["reason"] != reason or previous["version"] != version:
            return {"valid": False, "reason": "sticky-marker-run-conflict"}
        streak = previous["streak"]
    else:
        streak = previous["streak"] + 1
    return {"valid": True, "streak": streak, "action": action, "reason": reason, "version": version, "lastRun": run}


def decide(facts):
    # A missing key is not the same as a negative observation.  Keeping that
    # distinction typed here prevents a workflow adapter outage from becoming
    # an accidental permission to tag.
    missing = [key for key in REQUIRED_FACTS if key not in facts]
    if missing:
        return result("refuse", "observation-incomplete", facts.get("version", ""))
    version = facts.get("version", "")
    match = PATCH.fullmatch(version)
    if not match:
        return result("refuse", "version-not-stable-0x-patch", version)
    provenance = facts.get("provenance")
    if not isinstance(provenance, dict) or not provenance.get("mergedReachable") or not provenance.get("introducedVersion") or provenance.get("introducedVersion") != version:
        return result("refuse", "merged-pr-provenance-missing", version)
    if provenance.get("prArm") != "pass":
        return result("refuse", "pr-arm-not-passed", version)
    org = facts.get("orgFeed")
    nuget = facts.get("nugetFeed")
    if org not in ("absent", "present") or nuget not in ("absent", "present"):
        return result("refuse", "feed-observation-unknown", version)
    if org != nuget:
        return result("stickyEscalate", "partial-publish-stop-no-retry", version)
    if org == "present":
        if not isinstance(facts.get("releaseRun"), dict) or not facts["releaseRun"].get("id") or not facts["releaseRun"].get("url") or not facts["releaseRun"].get("nuspecCommit"):
            return result("stickyEscalate", "published-without-observed-release-run", version)
        if facts["releaseRun"]["nuspecCommit"] != facts.get("sourceSha"):
            return result("stickyEscalate", "published-nuspec-commit-disagrees", version)
        return result("openEvidencePr", "both-feeds-published", version)
    org_latest, nuget_latest = patch_tuple(facts.get("orgLatest")), patch_tuple(facts.get("nugetLatest"))
    candidate = patch_tuple(version)
    if not org_latest or not nuget_latest:
        return result("refuse", "feed-frontier-unknown", version)
    if org_latest != nuget_latest:
        return result("stickyEscalate", "feed-frontier-disagrees", version)
    if candidate <= org_latest:
        return result("refuse", "candidate-not-strictly-newer-than-frontier", version)
    if candidate[0] != org_latest[0] or candidate[1] != org_latest[1] + 1:
        return result("refuse", "candidate-not-next-patch", version)
    if facts.get("tagExists"):
        return result("stickyEscalate", "tag-exists-without-both-feed-publication", version)
    return result("tag", "eligible-authored-unpublished-patch", version)


def result(action, reason, version):
    return {"schemaVersion": 1, "action": action, "reason": reason, "version": version,
            "terminal": action in ("refuse", "stickyEscalate")}


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--facts", required=True, help="JSON fixture/observed fact file")
    parser.add_argument("--json", action="store_true")
    parser.add_argument("--previous-escalation")
    parser.add_argument("--run", default="")
    args = parser.parse_args()
    try:
        with open(args.facts, encoding="utf-8") as source:
            answer = decide(json.load(source))
        if answer["action"] in ("refuse", "stickyEscalate"):
            previous = None
            if args.previous_escalation:
                try:
                    with open(args.previous_escalation, encoding="utf-8") as state:
                        previous = json.load(state)
                except (OSError, json.JSONDecodeError):
                    answer["escalation"] = {"valid": False, "reason": "sticky-marker-malformed"}
                    print(json.dumps(answer, sort_keys=True))
                    return
            answer["escalation"] = escalation(previous, answer["action"], answer["reason"], answer["version"], args.run)
    except (OSError, json.JSONDecodeError) as error:
        answer = result("refuse", "facts-unreadable", "")
        answer["diagnostic"] = str(error)
    print(json.dumps(answer, sort_keys=True))


if __name__ == "__main__":
    main()
