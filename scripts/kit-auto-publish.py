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
    "version", "mergedPrReachable", "prArm", "orgFeed", "nugetFeed", "tagExists",
)


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
    if not facts.get("mergedPrReachable"):
        return result("refuse", "merged-pr-provenance-missing", version)
    if facts.get("prArm") != "pass":
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
    args = parser.parse_args()
    try:
        with open(args.facts, encoding="utf-8") as source:
            answer = decide(json.load(source))
    except (OSError, json.JSONDecodeError) as error:
        answer = result("refuse", "facts-unreadable", "")
        answer["diagnostic"] = str(error)
    print(json.dumps(answer, sort_keys=True))


if __name__ == "__main__":
    main()
