#!/usr/bin/env python3
r"""Board currency rules: a versioned, deterministic rule set over a Project 3 snapshot (Refs #4730).

WHY THIS EXISTS
===============

The user asked for "the board is current and triaged" to be mandatory and deterministic, ideally part
of the merge process. Design: docs/coordination/2026-10-31-board-currency-gate.md. This module is
phase 1 of that design: the rule evaluator as a PURE function of one snapshot, plus a shadow report.
It writes nothing anywhere. The board-drift observer (scripts/board-drift-observer.py) stays the
current issue-writing detector; these rules are its deterministic successor and run beside it in
shadow mode until Root approves the advisory phase.

SHAPE
=====

  board-currency-fetch.py -> snapshot.json   (the only IO; read-only; fails closed)
  this file `check`       -> findings.json   (PURE: snapshot in, findings out; the clock is the
                                              snapshot's own fetchedAt, so a snapshot always yields the
                                              same findings)
  this file `report`      -> markdown        (shadow report; counts, and public findings only)

RULE SET  fsgg:board-currency-rules/v1
======================================

Every rule reads only native state carried in the snapshot: issue/PR state and state reason, PR body,
native sub-issue and blocked-by links, the Project 3 single-select fields, and Root-login marker
comments. Each finding has a stable ID `<rule>:<subject>:<reason>` with no ages or counts in it.

  R1  A routine PR (body carries `fsgg:routine-development/v1`), open and non-draft or merged inside the
      window, references (Refs or a closing keyword) EXACTLY ONE Project 3 item whose Work level is
      Delivery; that Delivery has a parent that is a Project 3 Outcome; and while the PR is open the
      Delivery is open and not Done.
  R2  Closure and Done agree: a closed item is Done; a Done item is closed; an Outcome closed as
      completed on or after the rule set's effective date carries a Root `fsgg:outcome-accepted/v1`
      marker.
  R3  An open Blocked item names at least one OPEN blocker: a native blocked-by issue, or an issue in
      the newest Root `fsgg:blocked-on/v1` marker. An Outcome may instead be blocked through an open
      child Delivery that is itself Blocked.
  R4  Active Status has live evidence. A Delivery In progress has an open PR (draft allowed) or activity
      inside ACTIVITY_HOURS; In review has an open non-draft PR or activity inside ACTIVITY_HOURS. An
      Outcome In progress has an open child In progress or In review; an Outcome In review has no open
      child outside In review.
  R5  An open Outcome whose sub-issues are all closed: propose closure (Root acceptance) or add the
      missing remaining Deliveries.
  R6  An open Outcome (not R5) with no open child Delivery on Project 3.
  R7  Every item has Status, Work level and Track; a draft card is not a native issue and cannot be
      reasoned about.
  R8  An open Delivery with an open non-draft PR is not Backlog, Ready or Blocked; a closed Delivery has
      no open PR referencing it.
  R9  Hierarchy: an open Delivery has a parent that is a Project 3 Outcome; an Outcome is not a child of
      a Delivery.
  U   The input needed for a verdict could not be read. Unknown is never a pass (.github#266).

An Outcome with a Root `fsgg:outcome-closure-proposed/v1` marker still yields R5/R6 (the board still
claims open work that is not open) with `pending: "closure-proposed"`, so the report separates findings
waiting on a Root decision from findings nobody has looked at.

FAIL CLOSED
===========

`check` exits 2 (NO VERDICT) and writes nothing for a snapshot that is not `complete: true`, whose item
count differs from the project's totalCount, that holds no items, or whose schema it does not know.

PRIVACY
=======

This repository is public; Project 3 holds private-repository cards. Findings.json is private scratch
(it carries raw subjects). `report` publishes per-rule counts for everything, and IDs/titles only for
subjects in repositories whose visibility is an explicit `false`. A private or unknown subject is
counted, never named.

Exit codes: 0 success; 1 an unexpected error; 2 NO VERDICT.
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import re
import sys
from pathlib import Path

RULESET = "fsgg:board-currency-rules/v1"
SNAPSHOT_SCHEMA = "fsgg:board-currency-snapshot/v1"
FINDINGS_SCHEMA = "fsgg:board-currency-findings/v1"
NO_VERDICT = 2

# Rule-set parameters. Changing any of these changes verdicts, so it is a new rule-set version.
PARAMS = {
    "activityHours": 48,
    "mergedWindowHours": 24,
    "effectiveFrom": "2026-10-31T00:00:00Z",
    "rootLogins": ["EHotwagner"],
}

RULES = ("R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "R9", "U")
RULE_TITLES = {
    "R1": "Routine PR references exactly one open Delivery with a parent Outcome",
    "R2": "Closure, Done and Outcome acceptance agree",
    "R3": "Blocked names an open blocker",
    "R4": "Active Status has live evidence",
    "R5": "Outcome with every sub-issue closed",
    "R6": "Open Outcome with no open Delivery",
    "R7": "Status, Work level and Track are set on a native issue",
    "R8": "Delivery Status agrees with its PR state",
    "R9": "Delivery sits under an Outcome",
    "U": "Unknown: input could not be read",
}

ROUTINE_MARKER = "fsgg:routine-development/v1"
ACCEPTED = re.compile(r"<!--\s*fsgg:outcome-accepted/v1\b[^>]*-->")
PROPOSED = re.compile(r"<!--\s*fsgg:outcome-closure-proposed/v1\b[^>]*-->")
BLOCKED_ON = re.compile(r"<!--\s*fsgg:blocked-on/v1\b([^>]*)-->")

STATUSES = ("Backlog", "Ready", "In progress", "In review", "Blocked", "Done")
PRE_ACTION = ("Backlog", "Ready", "Blocked")
ACTIVE = ("In progress", "In review")

_FENCE = re.compile(r"```.*?```|~~~.*?~~~", re.S)
_INLINE = re.compile(r"`[^`\n]*`")
_REF_LIST = r"((?:[\w.-]+/[\w.-]+)?#\d+(?:\s*(?:,|and|&)\s*(?:[\w.-]+/[\w.-]+)?#\d+)*)"
_REFS = re.compile(r"\bRefs\s+" + _REF_LIST, re.I)
_CLOSING = re.compile(r"\b(?:close[sd]?|fix(?:e[sd])?|resolve[sd]?)\s*:?\s+" + _REF_LIST, re.I)
_ONE_REF = re.compile(r"(?:([\w.-]+/[\w.-]+))?#(\d+)")
_FULL_REF = re.compile(r"([\w.-]+/[\w.-]+)#(\d+)")


class NoVerdict(Exception):
    """The input cannot be trusted; the caller exits NO_VERDICT, never 'current'."""


def parse_time(value: object) -> dt.datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=dt.timezone.utc)


def ref_key(repo: object, number: object) -> str:
    return f"{repo}#{number}"


def visible_text(body: object) -> str:
    """Body text with fenced blocks and code spans removed: a quoted example is not a reference."""
    return _INLINE.sub(" ", _FENCE.sub(" ", body if isinstance(body, str) else ""))


def pr_refs(pr_repo: str, body: object) -> set[str]:
    """Every issue a PR body names with `Refs` or a closing keyword, as `owner/repo#N` keys."""
    text = visible_text(body)
    found: set[str] = set()
    for pattern in (_REFS, _CLOSING):
        for line in pattern.finditer(text):
            for ref in _ONE_REF.finditer(line.group(1)):
                found.add(ref_key(ref.group(1) or pr_repo, int(ref.group(2))))
    return found


def is_routine(body: object) -> bool:
    return isinstance(body, str) and ROUTINE_MARKER in body


def root_comments(item: dict, roots: set[str]) -> list[dict] | None:
    """Root-login comments, oldest first; None when the comments were not read."""
    comments = item.get("comments")
    if comments is None:
        return None
    picked = [c for c in comments if str(c.get("author", "")).lower() in roots and parse_time(c.get("createdAt"))]
    return sorted(picked, key=lambda c: (parse_time(c["createdAt"]), str(c.get("id"))))


def has_marker(comments: list[dict], pattern: re.Pattern) -> bool:
    """Markers are HTML comments; a marker quoted in a code span or fence does not count."""
    return any(pattern.search(visible_text(c.get("body"))) for c in comments)


def blocked_on_refs(comments: list[dict]) -> list[str] | None:
    """Refs in the NEWEST blocked-on marker (an empty marker clears); None when there is no marker."""
    newest: list[str] | None = None
    for c in comments:
        for m in BLOCKED_ON.finditer(visible_text(c.get("body"))):
            newest = sorted({ref_key(r.group(1), int(r.group(2))) for r in _FULL_REF.finditer(m.group(1))})
    return newest


def validate_snapshot(snapshot: object) -> dict:
    if not isinstance(snapshot, dict) or snapshot.get("schema") != SNAPSHOT_SCHEMA:
        raise NoVerdict(f"snapshot schema is not {SNAPSHOT_SCHEMA}")
    if snapshot.get("complete") is not True:
        raise NoVerdict("snapshot is not marked complete: a partial read proves nothing")
    items, prs = snapshot.get("items"), snapshot.get("pullRequests")
    if not isinstance(items, list) or not isinstance(prs, list):
        raise NoVerdict("snapshot lacks its items or pullRequests array")
    total = (snapshot.get("project") or {}).get("totalCount")
    if not isinstance(total, int) or total != len(items):
        raise NoVerdict(f"snapshot holds {len(items)} item(s) but the project reports {total}")
    if not items:
        raise NoVerdict("snapshot holds no items: an empty board is a failed read, not a clean one")
    if parse_time(snapshot.get("fetchedAt")) is None:
        raise NoVerdict("snapshot has no fetchedAt")
    return snapshot


def is_public(flag: object) -> bool:
    """Only an explicit `false` visibility is public; unknown counts as private."""
    return flag is False


class _Board:
    """Indexes over one snapshot. Read-only; built once per evaluation."""

    def __init__(self, snapshot: dict) -> None:
        self.items = [i for i in snapshot["items"] if isinstance(i, dict)]
        self.by_ref: dict[str, dict] = {}
        for item in self.items:
            if item.get("repo") and item.get("number") is not None:
                self.by_ref[ref_key(item["repo"], item["number"])] = item
        self.ref_states: dict[str, str] = dict(snapshot.get("refStates") or {})
        self.prs = [p for p in snapshot["pullRequests"] if isinstance(p, dict)]
        self.prs_by_ref: dict[str, list[dict]] = {}
        for pr in self.prs:
            own = ref_key(pr.get("repo"), pr.get("number"))
            for ref in pr_refs(str(pr.get("repo", "")), pr.get("body")):
                if ref != own:
                    self.prs_by_ref.setdefault(ref, []).append(pr)

    def state_of(self, ref: str) -> str | None:
        item = self.by_ref.get(ref)
        if item is not None and not item.get("unreadable"):
            return item.get("state")
        return self.ref_states.get(ref)

    def children(self, item: dict) -> tuple[list[dict], bool]:
        """Native sub-issues as (nodes, complete). A node is {repo, number, state}."""
        sub = item.get("subIssues")
        if not isinstance(sub, dict) or not isinstance(sub.get("nodes"), list):
            return [], False
        nodes = [n for n in sub["nodes"] if isinstance(n, dict)]
        return nodes, sub.get("total") == len(nodes)


def evaluate(snapshot: dict, params: dict | None = None) -> list[dict]:
    """PURE. Findings for a validated snapshot, in a stable order. The clock is snapshot.fetchedAt."""
    validate_snapshot(snapshot)
    p = dict(PARAMS, **(params or {}))
    now = parse_time(snapshot["fetchedAt"])
    activity = dt.timedelta(hours=p["activityHours"])
    merged_window = dt.timedelta(hours=p["mergedWindowHours"])
    effective = parse_time(p["effectiveFrom"])
    roots = {login.lower() for login in p["rootLogins"]}
    board = _Board(snapshot)
    out: list[dict] = []

    def emit(rule: str, subject: str, reason: str, text: str, *, private: object, verdict: str = "fail",
             **more: object) -> None:
        out.append({"id": f"{rule}:{subject}:{reason}", "rule": rule, "verdict": verdict, "subject": subject,
                    "reason": reason, "text": text, "public": is_public(private), **more})

    # ---------- per-PR: R1 ----------
    for pr in sorted(board.prs, key=lambda x: (str(x.get("repo")), x.get("number") or 0)):
        if not is_routine(pr.get("body")):
            continue
        state = pr.get("state")
        if state == "OPEN":
            if pr.get("draft"):
                continue
        elif state == "MERGED":
            merged_at = parse_time(pr.get("mergedAt"))
            if merged_at is None or now - merged_at > merged_window:
                continue
        else:
            continue
        subject = ref_key(pr.get("repo"), pr.get("number"))
        priv = pr.get("repoPrivate")
        refs = pr_refs(str(pr.get("repo", "")), pr.get("body")) - {subject}
        on_board = [board.by_ref[r] for r in sorted(refs) if r in board.by_ref]
        if any(i.get("unreadable") for i in on_board):
            emit("U", subject, "r1-reference-unreadable", "a referenced card could not be read", private=priv,
                 verdict="unknown", pr=True)
            continue
        deliveries = [i for i in on_board if i.get("workLevel") == "Delivery"]
        outcomes = [i for i in on_board if i.get("workLevel") == "Outcome"]
        tense = "open" if state == "OPEN" else "merged"
        if not deliveries:
            why = "references only an Outcome" if outcomes else "references no Project 3 Delivery"
            emit("R1", subject, "no-delivery", f"{tense} routine PR {why}", private=priv, pr=True, prState=state)
            continue
        if len(deliveries) > 1:
            emit("R1", subject, "multiple-deliveries",
                 f"{tense} routine PR references {len(deliveries)} Deliveries, not exactly one",
                 private=priv, pr=True, prState=state)
            continue
        d = deliveries[0]
        if state == "OPEN" and d.get("state") != "OPEN":
            emit("R1", subject, "delivery-closed", "open routine PR references a closed Delivery", private=priv,
                 pr=True, prState=state)
        elif state == "OPEN" and d.get("status") == "Done":
            emit("R1", subject, "delivery-done", "open routine PR references a Delivery whose Status is Done",
                 private=priv, pr=True, prState=state)
        parent = d.get("parent") or {}
        parent_item = board.by_ref.get(ref_key(parent.get("repo"), parent.get("number"))) if parent else None
        if parent_item is None or parent_item.get("workLevel") != "Outcome":
            emit("R1", subject, "no-parent-outcome",
                 f"{tense} routine PR's Delivery has no parent Outcome on Project 3", private=priv, pr=True,
                 prState=state)

    # ---------- per-item: R2..R9, U ----------
    for item in board.items:
        if item.get("repo") and item.get("number") is not None:
            subject = ref_key(item["repo"], item["number"])
        else:
            subject = f"draft:{item.get('itemId') or 'unknown'}"
        priv = item.get("repoPrivate") if item.get("type") != "DraftIssue" else None
        status, level, state = item.get("status"), item.get("workLevel"), item.get("state")
        kw = {"private": priv, "status": status, "level": level, "title": item.get("title")}

        if item.get("unreadable"):
            emit("U", subject, "unreadable", "card content could not be read", verdict="unknown", **kw)
            continue
        # R7 first: without Status or Work level the other rules have nothing to compare.
        if item.get("type") == "DraftIssue":
            emit("R7", subject, "draft", "draft card: convert it to an issue or remove it", **kw)
            continue
        missing = [n for n, v in (("Status", status), ("Work level", level), ("Track", item.get("track"))) if not v]
        if missing:
            emit("R7", subject, "missing-" + "-".join(m.lower().replace(" ", "") for m in missing),
                 "missing " + ", ".join(missing), **kw)
        if item.get("type") != "Issue" or not status or state not in ("OPEN", "CLOSED"):
            continue
        is_open = state == "OPEN"
        comments = root_comments(item, roots)
        linked = board.prs_by_ref.get(subject, [])
        open_prs = [x for x in linked if x.get("state") == "OPEN"]
        ready_prs = [x for x in open_prs if not x.get("draft")]

        # R2
        if not is_open and status != "Done":
            emit("R2", subject, "closed-not-done", f"closed but Status is {status}", **kw)
        if is_open and status == "Done":
            emit("R2", subject, "done-open", "open but Status is Done", **kw)
        closed_at = parse_time(item.get("closedAt"))
        if (level == "Outcome" and not is_open and item.get("stateReason") == "COMPLETED"
                and closed_at is not None and closed_at >= effective):
            if comments is None:
                emit("U", subject, "r2-comments-unread", "Outcome acceptance marker could not be read",
                     verdict="unknown", **kw)
            elif not has_marker(comments, ACCEPTED):
                emit("R2", subject, "outcome-unaccepted",
                     "Outcome closed as completed without a Root fsgg:outcome-accepted/v1 marker", **kw)

        if not is_open:
            # R8b: work that is closed must not still be under an open PR.
            if level == "Delivery" and ready_prs:
                emit("R8", subject, "pr-open-on-closed", "closed Delivery is referenced by an open PR", **kw)
            continue

        children, children_complete = board.children(item)
        open_children = [n for n in children if n.get("state") == "OPEN"]
        open_child_items = [board.by_ref.get(ref_key(n.get("repo"), n.get("number"))) for n in open_children]

        # R3
        if status == "Blocked":
            native = item.get("blockedBy") if isinstance(item.get("blockedBy"), dict) else {}
            native_nodes = [n for n in native.get("nodes") or [] if isinstance(n, dict)]
            native_complete = native.get("total") == len(native_nodes)
            marker = blocked_on_refs(comments) if comments is not None else None
            blockers = {ref_key(n.get("repo"), n.get("number")): n.get("state") for n in native_nodes}
            for ref in marker or []:
                blockers.setdefault(ref, board.state_of(ref))
            via_child = level == "Outcome" and any(
                c is not None and c.get("status") == "Blocked" and c.get("workLevel") == "Delivery"
                for c in open_child_items)
            if any(s == "OPEN" for s in blockers.values()) or via_child:
                pass
            elif not native_complete or comments is None or any(s is None for s in blockers.values()):
                emit("U", subject, "r3-blockers-unread", "blocker state could not be read", verdict="unknown", **kw)
            elif not blockers:
                emit("R3", subject, "no-blocker",
                     "Blocked without a native blocked-by issue or a Root fsgg:blocked-on/v1 marker", **kw)
            else:
                emit("R3", subject, "blockers-closed", "Blocked but every named blocker is closed", **kw)

        # R4
        if status in ACTIVE:
            if level == "Delivery":
                evidence = open_prs if status == "In progress" else ready_prs
                if not evidence:
                    signals = [parse_time(item.get("contentUpdatedAt")), parse_time(item.get("statusUpdatedAt"))]
                    signals += [parse_time(x.get("updatedAt")) for x in linked]
                    signals += [parse_time(c.get("createdAt")) for c in (item.get("comments") or [])]
                    known = [s for s in signals if s is not None]
                    if not known or now - max(known) > activity:
                        emit("R4", subject, "stale-active",
                             f"{status} with no open PR and no activity in {p['activityHours']}h", **kw)
            elif level == "Outcome" and any(c is not None and c.get("workLevel") == "Delivery"
                                            for c in open_child_items):
                # Only when the Outcome has an open Delivery: without one, R5/R6 already name the cause.
                child_status = [c.get("status") if c is not None else None for c in open_child_items]
                if status == "In progress" and not any(s in ACTIVE for s in child_status):
                    emit("R4", subject, "outcome-no-active-child",
                         "Outcome In progress with no open child In progress or In review", **kw)
                if status == "In review" and any(s != "In review" for s in child_status):
                    emit("R4", subject, "outcome-review-open-child",
                         "Outcome In review while an open child is not In review", **kw)

        # R5 / R6
        if level == "Outcome":
            pending = {"pending": "closure-proposed"} if comments and has_marker(comments, PROPOSED) else {}
            if children and children_complete and not open_children:
                emit("R5", subject, "children-complete",
                     "every sub-issue is closed: propose closure for Root acceptance, or add the remaining "
                     "Deliveries", **kw, **pending)
            elif not any(c is not None and c.get("workLevel") == "Delivery" for c in open_child_items):
                if not children_complete:
                    emit("U", subject, "r6-children-unread", "sub-issues could not be read completely",
                         verdict="unknown", **kw)
                else:
                    why = "no sub-issues" if not children else "no open sub-issue is a Project 3 Delivery"
                    emit("R6", subject, "no-open-delivery", f"open Outcome with {why}", **kw, **pending)

        # R8a
        if level == "Delivery" and ready_prs and status in PRE_ACTION:
            emit("R8", subject, "open-pr-pre-action", f"{status} while an open non-draft PR references it", **kw)

        # R9
        parent = item.get("parent")
        parent_item = board.by_ref.get(ref_key(parent.get("repo"), parent.get("number"))) if parent else None
        if level == "Delivery":
            if not parent:
                emit("R9", subject, "orphan-delivery", "Delivery has no parent issue", **kw)
            elif parent_item is None:
                emit("R9", subject, "parent-off-board", "Delivery's parent is not on Project 3", **kw)
            elif parent_item.get("workLevel") != "Outcome":
                emit("R9", subject, "parent-not-outcome", "Delivery's parent is not an Outcome", **kw)
        elif level == "Outcome" and parent_item is not None and parent_item.get("workLevel") == "Delivery":
            emit("R9", subject, "outcome-under-delivery", "Outcome is a sub-issue of a Delivery", **kw)

    order = {r: n for n, r in enumerate(RULES)}
    return sorted(out, key=lambda f: (order[f["rule"]], f["subject"], f["reason"]))


def summarize(findings: list[dict]) -> dict:
    counts = {rule: 0 for rule in RULES}
    for f in findings:
        counts[f["rule"]] += 1
    return counts


def check(snapshot: dict, params: dict | None = None) -> dict:
    findings = evaluate(snapshot, params)
    return {
        "schema": FINDINGS_SCHEMA,
        "ruleset": RULESET,
        "params": dict(PARAMS, **(params or {})),
        "snapshotFetchedAt": snapshot["fetchedAt"],
        "items": len(snapshot["items"]),
        "pullRequests": len(snapshot["pullRequests"]),
        "counts": summarize(findings),
        "findings": findings,
    }


def code(value: object) -> str:
    """A code span: never autolinked, so the report writes no cross-reference anywhere."""
    return "`" + str(value).replace("`", "'").replace("\n", " ") + "`"


def render_report(result: dict, limit: int = 200) -> str:
    """Shadow report markdown. Counts cover everything; only explicitly public subjects are named."""
    findings = result["findings"]
    lines = [
        f"### Board currency (shadow) — {result['ruleset']}",
        "",
        f"Snapshot {result['snapshotFetchedAt']}: {result['items']} cards, {result['pullRequests']} PRs read. "
        "Shadow mode: nothing is written, nothing is enforced.",
        "",
        "| Rule | Findings | Public | Private/unknown | Closure proposed |",
        "|---|---:|---:|---:|---:|",
    ]
    for rule in RULES:
        group = [f for f in findings if f["rule"] == rule]
        public = sum(1 for f in group if f["public"])
        proposed = sum(1 for f in group if f.get("pending"))
        lines.append(f"| {rule} {RULE_TITLES[rule]} | {len(group)} | {public} | {len(group) - public} | {proposed} |")
    lines += ["", f"Total: {len(findings)}.", ""]
    shown = [f for f in findings if f["public"]]
    for f in shown[:limit]:
        title = f" {code(f['title'])}" if f.get("title") else ""
        lines.append(f"- {code(f['id'])}{title}: {f['text']}")
    if len(shown) > limit:
        lines.append(f"- ... {len(shown) - limit} more public finding(s)")
    hidden = len(findings) - len(shown)
    if hidden:
        lines.append(f"- {hidden} finding(s) in private or unknown-visibility repositories are counted, not named.")
    return "\n".join(lines) + "\n"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    c = sub.add_parser("check", help="evaluate a snapshot (pure)")
    c.add_argument("--snapshot", required=True)
    c.add_argument("--out", required=True)
    r = sub.add_parser("report", help="render the shadow report from findings.json")
    r.add_argument("--findings", required=True)
    r.add_argument("--reveal-private", action="store_true", help="local only: name every subject")
    args = ap.parse_args(argv)
    try:
        if args.cmd == "check":
            try:
                snapshot = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
            except (OSError, json.JSONDecodeError):
                raise NoVerdict("snapshot is unreadable or not JSON") from None
            result = check(snapshot)
            Path(args.out).write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
            print(" ".join(f"{k}={v}" for k, v in result["counts"].items()) + f" total={len(result['findings'])}")
            return 0
        result = json.loads(Path(args.findings).read_text(encoding="utf-8"))
        if result.get("schema") != FINDINGS_SCHEMA:
            raise NoVerdict(f"findings schema is not {FINDINGS_SCHEMA}")
        if args.reveal_private:
            result = dict(result, findings=[dict(f, public=True) for f in result["findings"]])
        sys.stdout.write(render_report(result))
        return 0
    except NoVerdict as exc:
        print(f"board-currency: NO VERDICT: {exc}", file=sys.stderr)
        return NO_VERDICT


if __name__ == "__main__":
    sys.exit(main())
