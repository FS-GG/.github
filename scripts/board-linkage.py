#!/usr/bin/env python3
"""board-linkage: rule R1 for one pull request, as an advisory PR check (Refs #5094).

Design: docs/coordination/2026-10-31-board-currency-gate.md §2(a) and "Evaluation order". The check
reads one PR and the Project 3 cards its body references, and evaluates R1 with the same
`linkage_of()` / `override_of()` the observer uses (scripts/board-currency-rules.py), so the check, the
merge gate and the observer cannot disagree about a PR.

  not a routine PR (no fsgg:routine-development/v1)  -> skip   (exit 0, the check passes)
  valid override                                     -> overridden (exit 0, notice) - Project 3 not read
  linked                                             -> linked (exit 0)
  R1 failure                                         -> refused (advisory: exit 0 + warning; required: exit 1)
  read failure / incomplete read                     -> unknown (advisory: exit 0 + warning; required: exit 1)

Phase 2 runs `--mode advisory` only: the check can never turn red on a verdict. Required mode is a
later, separate decision (design §2 "Which control binds": a Root settings step adding the
`board-linkage` context with app_id 15368 to main's protection, with readback).

READ-ONLY. Two bounded GraphQL reads: the PR, then every referenced issue (with its Project 3 item and
native parent) in one aliased query, then the parents not already read in one more. This repository is
public: output names the PR, the verdict and the reason, never a referenced issue (it may be private)
and never body text. Failures print a fixed label only.

Environment: GH_TOKEN with organization Projects read, issues read and pull requests read.
`BOARD_LINKAGE_GH` overrides the `gh` binary (tests). Exit codes: 0 pass; 1 required-mode refusal;
2 usage or setup error.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import subprocess
import sys
from pathlib import Path

PROJECT_OWNER = "FS-GG"
PROJECT_NUMBER = 3
MAX_REFS = 50

PR_QUERY = """
query($owner:String!,$name:String!,$number:Int!){
  repository(owner:$owner,name:$name){ pullRequest(number:$number){
    number state isDraft body lastEditedAt author{login} editor{login}
  } }
}
"""

_REPO = re.compile(r"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")


class ReadError(Exception):
    """A failed or incomplete read; the message is fixed text only."""


def rules_module():
    path = Path(__file__).with_name("board-currency-rules.py")
    spec = importlib.util.spec_from_file_location("board_currency_rules", path)
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module


def graphql(args: list[str]) -> dict:
    cmd = [os.environ.get("BOARD_LINKAGE_GH", "gh"), "api", "graphql", *args]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=60, check=False)
    except (subprocess.TimeoutExpired, OSError):
        raise ReadError("graphql call did not complete") from None
    if proc.returncode:
        raise ReadError("graphql call failed")
    try:
        doc = json.loads(proc.stdout)
    except json.JSONDecodeError:
        raise ReadError("graphql response is not JSON") from None
    if not isinstance(doc, dict) or doc.get("errors") or not isinstance(doc.get("data"), dict):
        raise ReadError("graphql response carries errors or no data")
    return doc["data"]


def read_pr(repo: str, number: int) -> dict:
    owner, name = repo.split("/", 1)
    node = ((graphql(["-F", f"owner={owner}", "-F", f"name={name}", "-F", f"number={number}",
                      "-f", f"query={PR_QUERY}"]).get("repository") or {}).get("pullRequest"))
    if not isinstance(node, dict) or not isinstance(node.get("body"), str):
        raise ReadError("pull request could not be read")
    return {"repo": repo, "number": number, "state": node.get("state"), "draft": node.get("isDraft"),
            "body": node["body"], "author": (node.get("author") or {}).get("login"),
            "edited": node.get("lastEditedAt") is not None, "editor": (node.get("editor") or {}).get("login")}


def _issue_fragment(alias: str, ref: str) -> str:
    repo, number = ref.split("#", 1)
    owner, name = repo.split("/", 1)
    return (f'{alias}: repository(owner:{json.dumps(owner)},name:{json.dumps(name)}){{ issueOrPullRequest(number:{int(number)}){{'
            ' __typename ... on Issue{ state parent{ number repository{ nameWithOwner } }'
            ' projectItems(first:20){ totalCount nodes{ project{ number owner{ ... on Organization{ login } } }'
            ' status: fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } }'
            ' workLevel: fieldValueByName(name:"Work level"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } } } } } } }')


def read_cards(refs: list[str]) -> dict[str, dict | None]:
    """Project 3 card per ref: {workLevel, status, state, parent} or None when the issue is not on Project 3.
    A missing issue, a non-issue, a truncated projectItems list or any failed read raises ReadError."""
    if not refs:
        return {}
    if len(refs) > MAX_REFS:
        raise ReadError("too many references to read")
    data = graphql(["-f", "query={" + " ".join(_issue_fragment(f"r{n}", r) for n, r in enumerate(refs)) + "}"])
    cards: dict[str, dict | None] = {}
    for n, ref in enumerate(refs):
        node = ((data.get(f"r{n}") or {}).get("issueOrPullRequest"))
        if not isinstance(node, dict):
            raise ReadError("a referenced issue could not be read")
        if node.get("__typename") != "Issue":
            cards[ref] = None  # a PR or discussion is never a Project 3 Delivery
            continue
        items = node.get("projectItems") or {}
        nodes = items.get("nodes")
        if not isinstance(nodes, list) or items.get("totalCount") != len(nodes):
            raise ReadError("a referenced issue's project items are incomplete")
        mine = [i for i in nodes if isinstance(i, dict) and (i.get("project") or {}).get("number") == PROJECT_NUMBER
                and ((i.get("project") or {}).get("owner") or {}).get("login") == PROJECT_OWNER]
        if not mine:
            cards[ref] = None
            continue
        parent = node.get("parent")
        cards[ref] = {
            "workLevel": (mine[0].get("workLevel") or {}).get("name"),
            "status": (mine[0].get("status") or {}).get("name"),
            "state": node.get("state"),
            "parent": ({"repo": (parent.get("repository") or {}).get("nameWithOwner"), "number": parent.get("number")}
                       if isinstance(parent, dict) else None),
        }
    return cards


def evaluate(pr: dict, rules) -> tuple[str, str | None]:
    """(verdict, reason). Verdict: skip | overridden | linked | refused | unknown."""
    if not rules.is_routine(pr.get("body")):
        return "skip", None
    roots = {login.lower() for login in rules.PARAMS["rootLogins"]}
    if rules.override_of(pr, roots) is not None:
        return "overridden", None
    own = rules.ref_key(pr["repo"], pr["number"])
    refs = sorted(rules.pr_refs(pr["repo"], pr["body"]) - {own})
    try:
        cards = read_cards(refs)
        parents = set()
        for card in cards.values():
            if card and card.get("workLevel") == "Delivery" and card.get("parent"):
                parents.add(rules.ref_key(card["parent"]["repo"], card["parent"]["number"]))
        cards.update(read_cards(sorted(parents - set(cards))))
    except ReadError:
        return "unknown", "board-read"
    failures, _ = rules.linkage_of(pr, rules.RefIndex(cards).get)
    unknown = [reason for rule, reason in failures if rule == "U"]
    if unknown:
        return "unknown", unknown[0]
    if failures:
        return "refused", ",".join(reason for _, reason in failures)
    return "linked", None


def precheck(event_path: str, output: str | None) -> int:
    """Decide `skip` from the event payload alone, before any token exists: a non-routine PR must pass even
    when the App token cannot be minted. Writes `routine=true|false` to GITHUB_OUTPUT. An unreadable
    payload is treated as routine, so the full check (and its Unknown) decides rather than a silent skip."""
    rules = rules_module()
    try:
        event = json.loads(Path(event_path).read_text(encoding="utf-8"))
        body = ((event.get("pull_request") or {}).get("body"))
        routine = rules.is_routine(body) if isinstance(event.get("pull_request"), dict) else True
    except (OSError, json.JSONDecodeError, AttributeError):
        routine = True
    print(f"board-linkage: {'routine PR, evaluating' if routine else 'not a routine PR: skip (pass)'}")
    if output:
        with open(output, "a", encoding="utf-8") as handle:
            handle.write(f"routine={'true' if routine else 'false'}\n")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--precheck-event", help="decide skip from this event payload only; no network, no token")
    ap.add_argument("--repo")
    ap.add_argument("--pr", type=int)
    ap.add_argument("--mode", choices=("advisory", "required"), default="advisory")
    args = ap.parse_args(argv)
    if args.precheck_event:
        return precheck(args.precheck_event, os.environ.get("GITHUB_OUTPUT"))
    if not args.repo or args.pr is None:
        print("board-linkage: --repo and --pr are required", file=sys.stderr)
        return 2
    if not _REPO.fullmatch(args.repo) or args.pr <= 0:
        print("board-linkage: invalid --repo or --pr", file=sys.stderr)
        return 2
    rules = rules_module()
    try:
        pr = read_pr(args.repo, args.pr)
        verdict, reason = evaluate(pr, rules)
    except ReadError:
        verdict, reason = "unknown", "pr-read"
    line = f"board-linkage ({rules.RULESET}, {args.mode}): PR {args.pr} {verdict}" + (f" ({reason})" if reason else "")
    if verdict in ("refused", "unknown"):
        level = "error" if args.mode == "required" else "warning"
        print(f"::{level} title=board-linkage::{line}. Reference exactly one Project 3 Delivery that has a parent "
              "Outcome (Refs OWNER/REPO#N), or record an override (design §1).")
    else:
        print(line)
    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(f"### board-linkage\n\n{line}\n")
    return 1 if args.mode == "required" and verdict in ("refused", "unknown") else 0


if __name__ == "__main__":
    sys.exit(main())
