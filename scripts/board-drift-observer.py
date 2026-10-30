#!/usr/bin/env python3
r"""Board drift observer: compare Project 3 Status with observable signals, report one issue (Refs #4730).

WHY THIS EXISTS
===============

On 2026-10-30 three cards (#4817, Coordination#1026, SDD#928) stayed `Blocked` for hours after
rulings and dispatches had made them actionable. Status changes depend on Root remembering the guarded
write after a ruling, dispatch or merge, and nothing detected the drift. The governing rule now says
the write happens in the same turn (lifecycle-contract.md); this observer is the deterministic net
under that rule. It is a detector only: it NEVER writes a board field. Root stays the single guarded
writer and resolves each finding with a Status write or a recorded disposition.

SHAPE
=====

  board-drift-fetch.py  -> snapshot.json   (the only IO that reads Project 3; fails closed)
  this file `check`     -> findings.json   (PURE: snapshot + clock in, findings out)
  this file `report`    -> the single report issue (create / update / comment / close / no-op)

The analysis and the report planner are pure functions of their inputs, so every rule has a fixture
(`tests/board-drift-observer/`). The only network calls in this file are the REST calls of `report`.

THE RULES (a finding carries the letter)
========================================

  a  A card in Blocked, Backlog or Ready that has an OPEN, NON-DRAFT PR whose body says
     `Refs <owner/repo#N>` (or `Refs #N`, same repo as the PR) and that PR changed in the last 24h.
  b  A closed issue (or closed/merged PR) that is not Done, or an open issue that is Done.
  c  In progress with no linked PR, comment or issue activity for more than 7 days (configurable).
  d  A card with no Status, or missing Track or Work level.
  e  A comment on the item, newer than the Status's last change, by a Root login and carrying a Root
     ruling marker (RULING_MARKER). A later comment by Root containing ACK_TOKEN is a recorded
     disposition and clears it.
  x  The card's content could not be read (null content). Not a rule of the spec: "I could not look" is
     never "no drift" (.github#266), and a silent skip would hide exactly the card that needs a look.

FAIL CLOSED
===========

`check` refuses (exit 2, no findings file) a snapshot that is not `complete: true`, whose item count
differs from the project's `totalCount`, or whose schema it does not know. `report` is only ever handed
a findings file `check` produced. The workflow runs the steps in sequence with no `always()`, so a fetch
error, a partial page or a REST failure leaves the job red and the report issue untouched: it can never
say "no drift" about a board it did not read.

REPORT ISSUE (one issue, fixed title and label)
===============================================

  no findings, no issue            -> nothing
  findings, no issue               -> create
  findings, same finding set       -> leave the issue alone (no body edit, no comment)
  findings, changed finding set    -> rewrite the body, and comment once with what changed
  findings, issue closed           -> reopen, rewrite, comment
  no findings, issue open          -> comment and close

The finding set is identified by a digest of finding keys carried in a hidden marker in the body, and a
key excludes volatile values (ages), so an unchanged situation never rewrites the issue.

Refs are rendered in code spans on purpose. A linked reference makes GitHub write a cross-reference
into the referenced issue's timeline, which advances that issue's updatedAt; rule (c) reads that
clock, so a linking report would keep a stale In progress card looking alive.

Exit codes: 0 success; 1 an unexpected error; 2 NO VERDICT (the input could not be trusted).
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import json
import os
import re
import subprocess
import sys
from pathlib import Path

SNAPSHOT_SCHEMA = "fsgg:board-drift-snapshot/v1"
FINDINGS_SCHEMA = "fsgg:board-drift-findings/v1"
REPORT_TITLE = "Board drift: Project 3"
REPORT_LABEL = "board-drift"
REPORT_REPO = "FS-GG/.github"
MARKER_PREFIX = "fsgg:board-drift/v1"

NO_VERDICT = 2

PRE_ACTION_STATUSES = ("Blocked", "Backlog", "Ready")
CLOSED_STATES = ("CLOSED", "MERGED")

# Today's Root comments on #4730, #4817 and #4935 open with a signature ("Root/fdev", "Root/fdev
# ruling", "Root/fdev decision", "User ruling (durable record)") and say what kind of comment they are
# in the first line. The marker is therefore: a Root/owner/user signature followed, within the same
# opening span, by one of the words that mark an actionability change. Progress notes
# ("Root/fdev 02:42 UTC: delivered ...") carry none of those words and do not match.
RULING_MARKER = re.compile(
    r"""(?isx)
    \A\s*[*_>#\s]*                     # leading emphasis / quote / heading marks
    (?:
        (?:root(?:/fdev)?|owner|user|programme)\b[^\n]{0,120}?
        \b(?:ruling|decision|answer|direction|directive|unblocks?|admission|reconciliation|disposition)\b
      |
        [^\n]{0,60}\(durable[ ]record\)
    )
    """
)
# A Root comment that records "no Status change is owed for the ruling above".
ACK_TOKEN = "[board-drift-ack]"

# Only the author login of a Root comment is trusted for rule (e). Every agent in the fleet
# authenticates as this one account, so the login proves "Root's account", not "Root's hands".
DEFAULT_ROOT_LOGINS = ("EHotwagner",)

_FENCE = re.compile(r"```.*?```|~~~.*?~~~", re.S)
_INLINE = re.compile(r"`[^`\n]*`")
_REFS_LINE = re.compile(
    r"\bRefs\s+((?:[\w.-]+/[\w.-]+)?#\d+(?:\s*(?:,|and|&)\s*(?:[\w.-]+/[\w.-]+)?#\d+)*)", re.I
)
_ONE_REF = re.compile(r"(?:([\w.-]+/[\w.-]+))?#(\d+)")


class NoVerdict(Exception):
    """The input cannot be trusted; the caller exits NO_VERDICT, never 'no drift'."""


def parse_time(value: str | None) -> dt.datetime | None:
    if not value:
        return None
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.tzinfo else parsed.replace(tzinfo=dt.timezone.utc)


def iso(value: dt.datetime) -> str:
    return value.astimezone(dt.timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")


def pr_refs(pr_repo: str, body: str | None) -> set[tuple[str, int]]:
    """Every (repo, number) a PR body names with `Refs`; code spans and fences are not references."""
    text = _INLINE.sub(" ", _FENCE.sub(" ", body or ""))
    found: set[tuple[str, int]] = set()
    for line in _REFS_LINE.finditer(text):
        for ref in _ONE_REF.finditer(line.group(1)):
            found.add((ref.group(1) or pr_repo, int(ref.group(2))))
    return found


def ref_text(item: dict) -> str:
    if item.get("repo") and item.get("number") is not None:
        return f"{item['repo']}#{item['number']}"
    return f"draft:{item.get('itemId') or 'unknown'}"


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


def finding(rule: str, item: dict, extra: str, text: str, **more: object) -> dict:
    ref = ref_text(item)
    return {"rule": rule, "ref": ref, "key": f"{rule}|{ref}|{extra}", "text": text, **more}


def analyze(
    snapshot: dict,
    now: dt.datetime,
    *,
    pr_window_hours: float = 24,
    stale_days: float = 7,
    root_logins: tuple[str, ...] = DEFAULT_ROOT_LOGINS,
) -> list[dict]:
    """PURE. Findings for a validated snapshot at `now`, in a stable order."""
    validate_snapshot(snapshot)
    roots = {login.lower() for login in root_logins}
    window = dt.timedelta(hours=pr_window_hours)
    stale = dt.timedelta(days=stale_days)

    prs_by_ref: dict[tuple[str, int], list[dict]] = {}
    for pr in snapshot["pullRequests"]:
        for ref in pr_refs(pr.get("repo", ""), pr.get("body")):
            prs_by_ref.setdefault(ref, []).append(pr)

    out: list[dict] = []
    for item in snapshot["items"]:
        status, state = item.get("status"), item.get("state")
        key = (item.get("repo"), item.get("number"))

        # (d) Metadata first: an item with no Status cannot be reasoned about by the other rules.
        missing = [
            name
            for name, value in (("Status", status), ("Track", item.get("track")), ("Work level", item.get("workLevel")))
            if not value
        ]
        if missing:
            out.append(
                finding("d", item, ",".join(missing), "missing " + ", ".join(missing), missing=missing,
                        status=status, title=item.get("title"))
            )
        if item.get("unreadable"):
            out.append(finding("x", item, "unreadable", "content could not be read (null content)",
                               status=status, title=item.get("title")))
            continue
        if not status or state is None:
            continue

        closed = state in CLOSED_STATES
        # (b)
        if closed and status != "Done":
            out.append(finding("b", item, f"closed-not-done|{status}", f"{state.lower()} but Status is {status}",
                               status=status, state=state, title=item.get("title")))
        if not closed and status == "Done":
            out.append(finding("b", item, "open-done", "open but Status is Done",
                               status=status, state=state, title=item.get("title")))

        linked = [p for p in prs_by_ref.get(key, []) if (p.get("repo"), p.get("number")) != key]

        # (a)
        if status in PRE_ACTION_STATUSES and not closed:
            fresh = []
            for pr in sorted(linked, key=lambda p: (p.get("repo", ""), p.get("number", 0))):
                changed = parse_time(pr.get("updatedAt"))
                if pr.get("draft") or changed is None or now - changed > window:
                    continue
                fresh.append((f"{pr['repo']}#{pr['number']}", changed, pr.get("url")))
            if fresh:
                names = ", ".join(name for name, _, _ in fresh)
                newest = max(changed for _, changed, _ in fresh)
                out.append(
                    finding("a", item, "+".join(name for name, _, _ in fresh),
                            f"{status} while open non-draft PR {names} says Refs (newest change {iso(newest)})",
                            status=status, prs=[name for name, _, _ in fresh], title=item.get("title"))
                )

        # (c)
        if status == "In progress" and not closed:
            signals = [parse_time(item.get("statusUpdatedAt")), parse_time(item.get("contentUpdatedAt"))]
            signals += [parse_time(p.get("updatedAt")) for p in linked]
            signals += [parse_time(c.get("createdAt")) for c in item.get("comments", [])]
            signals += [parse_time(c.get("committedAt")) for c in item.get("commits", [])]
            known = [s for s in signals if s is not None]
            last = max(known) if known else None
            if last is None or now - last > stale:
                idle = "no activity signal at all" if last is None else f"last activity {iso(last)}"
                out.append(finding("c", item, "idle", f"In progress with {idle} (more than {stale_days:g} days)",
                                   status=status, title=item.get("title")))

        # (e)
        if status != "Done" and not closed:
            since = parse_time(item.get("statusUpdatedAt"))
            if since is not None:
                newer = sorted(
                    (c for c in item.get("comments", [])
                     if (created := parse_time(c.get("createdAt"))) is not None and created > since
                     and str(c.get("author", "")).lower() in roots),
                    key=lambda c: (parse_time(c["createdAt"]), str(c.get("id"))),
                )
                pending = [
                    comment for index, comment in enumerate(newer)
                    if RULING_MARKER.search(comment.get("body") or "")
                    and not any(ACK_TOKEN in (later.get("body") or "") for later in newer[index + 1:])
                ]
                if pending:
                    # One finding per card, keyed by the newest ruling: a later ruling changes the key.
                    last = pending[-1]
                    out.append(
                        finding("e", item, str(last.get("id")),
                                f"{len(pending)} Root ruling comment(s) newer than the Status change ({iso(since)}); "
                                f"newest {last.get('id')} at {last.get('createdAt')}",
                                status=status, commentUrl=last.get("url"), title=item.get("title"))
                    )

    order = {"a": 0, "b": 1, "c": 2, "d": 3, "e": 4, "x": 5}
    return sorted(out, key=lambda f: (order[f["rule"]], f["ref"], f["key"]))


def digest(findings: list[dict]) -> str:
    return hashlib.sha256("\n".join(sorted(f["key"] for f in findings)).encode()).hexdigest()


RULE_TITLES = {
    "a": "a. Pre-action Status (Blocked, Backlog, Ready) with a fresh open PR that says Refs",
    "b": "b. Closed/open state disagrees with Done",
    "c": "c. In progress with no activity",
    "d": "d. Missing Status, Track or Work level",
    "e": "e. Root ruling comment newer than the last Status change",
    "x": "x. Content could not be read",
}


def code(value: object) -> str:
    """A code span: never autolinked, so the report writes no cross-reference or mention anywhere."""
    return "`" + str(value).replace("`", "'").replace("\n", " ") + "`"


def render_body(findings: list[dict], snapshot_meta: dict) -> str:
    keys = sorted(f["key"] for f in findings)
    # `#` is written as a JSON escape so even the hidden marker carries no issue reference.
    marker = json.dumps({"digest": digest(findings), "keys": keys}, separators=(",", ":"), sort_keys=True)
    marker = marker.replace("#", "\\u0023")
    lines = [
        f"<!-- {MARKER_PREFIX} {marker} -->",
        "",
        f"Generated by the scheduled board-drift observer from a read-only snapshot of Project "
        f"{snapshot_meta.get('number', 3)} taken {snapshot_meta.get('fetchedAt', 'unknown')} "
        f"({snapshot_meta.get('items', '?')} cards). It never edits board fields: Root resolves each finding "
        "with a guarded Status write or a recorded disposition, and this issue is rewritten when the set changes.",
        "",
        f"{len(findings)} finding(s).",
    ]
    for rule in ("a", "b", "c", "d", "e", "x"):
        group = [f for f in findings if f["rule"] == rule]
        if not group:
            continue
        lines += ["", f"### {RULE_TITLES[rule]}", ""]
        for f in group:
            title = f" {code(f['title'])}" if f.get("title") else ""
            lines.append(f"- {code(f['ref'])}{title}: {code(f['text'])}")
    lines += [
        "",
        "Disposition: write the Status, or comment on the card with `[board-drift-ack]` and the reason when no "
        "Status change is owed (clears rule e only).",
        "",
        f"Owner: {code('FS-GG/.github#4730')}. Rule: work-board lifecycle-contract, same-turn Status write.",
    ]
    return "\n".join(lines) + "\n"


def parse_marker(body: str | None) -> dict | None:
    match = re.search(rf"<!--\s*{re.escape(MARKER_PREFIX)}\s+(\{{.*?\}})\s*-->", body or "", re.S)
    if not match:
        return None
    try:
        data = json.loads(match.group(1))
    except json.JSONDecodeError:
        return None
    return data if isinstance(data, dict) and isinstance(data.get("keys"), list) else None


def plan_report(findings: list[dict], existing: dict | None, snapshot_meta: dict) -> dict:
    """PURE. What to do to the one report issue. `existing` is None or {number, state, body}."""
    if not findings:
        if existing and existing.get("state") == "open":
            return {
                "action": "close",
                "comment": "No board drift findings at the latest hourly observation "
                f"({snapshot_meta.get('fetchedAt', 'unknown')}). Closing; the observer reopens this issue if drift reappears.\n",
            }
        return {"action": "noop", "reason": "no findings and no open report issue"}

    body = render_body(findings, snapshot_meta)
    if existing is None:
        return {"action": "create", "body": body}

    previous = parse_marker(existing.get("body"))
    previous_keys = set(previous["keys"]) if previous else set()
    current_keys = {f["key"] for f in findings}
    if existing.get("state") == "open" and previous and previous.get("digest") == digest(findings):
        return {"action": "noop", "reason": "finding set unchanged"}

    added = sorted(current_keys - previous_keys)
    removed = sorted(previous_keys - current_keys)
    comment = [f"Finding set changed: {len(added)} new, {len(removed)} cleared, {len(current_keys)} open."]
    comment += [f"- new {code(key)}" for key in added[:50]]
    comment += [f"- cleared {code(key)}" for key in removed[:50]]
    if len(added) > 50 or len(removed) > 50:
        comment.append("- (list truncated; the body holds the full set)")
    return {
        "action": "update" if existing.get("state") == "open" else "reopen",
        "body": body,
        "comment": "\n".join(comment) + "\n",
    }


# --------------------------------------------------------------------------------------------------
# IO below this line: REST calls of `report` only.
# --------------------------------------------------------------------------------------------------

def gh_api(args: list[str], payload: dict | None = None, *, allow_status: tuple[str, ...] = ()) -> str:
    gh = os.environ.get("BOARD_DRIFT_GH", "gh")
    cmd = [gh, "api", *args]
    if payload is not None:
        cmd += ["--input", "-"]
    proc = subprocess.run(cmd, input=json.dumps(payload) if payload is not None else None,
                          capture_output=True, text=True, timeout=120, check=False)
    if proc.returncode:
        text = (proc.stdout + proc.stderr).strip()
        if any(s in text for s in allow_status):
            return ""
        raise RuntimeError(f"gh api {' '.join(args)} failed ({proc.returncode}): {text[:300]}")
    return proc.stdout


def find_report_issue(repo: str) -> dict | None:
    """The single issue with REPORT_TITLE and REPORT_LABEL. Two matching issues are a hard error."""
    out = gh_api([f"repos/{repo}/issues?labels={REPORT_LABEL}&state=all&per_page=100", "--paginate", "--jq", ".[]"])
    matches = []
    for line in out.splitlines():
        if not line.strip():
            continue
        issue = json.loads(line)
        if "pull_request" not in issue and issue.get("title") == REPORT_TITLE:
            matches.append(issue)
    open_ones = [i for i in matches if i.get("state") == "open"]
    if len(open_ones) > 1:
        raise RuntimeError(f"{len(open_ones)} open issues titled {REPORT_TITLE!r}: refusing to guess which to update")
    if open_ones:
        return open_ones[0]
    return max(matches, key=lambda i: i["number"]) if matches else None


def apply_plan(repo: str, plan: dict, existing: dict | None) -> str:
    action = plan["action"]
    if action == "noop":
        return f"noop: {plan['reason']}"
    if action in ("create", "reopen", "update"):
        # Ensure the label before it is needed; 422 means it exists.
        gh_api([f"repos/{repo}/labels", "-X", "POST", "-f", f"name={REPORT_LABEL}", "-f", "color=d93f0b",
                "-f", "description=Scheduled Project 3 board drift report (read-only observer)"],
               allow_status=("already_exists", "422"))
    if action == "create":
        created = json.loads(gh_api([f"repos/{repo}/issues"], {"title": REPORT_TITLE, "body": plan["body"], "labels": [REPORT_LABEL]}))
        return f"created #{created['number']}"
    number = existing["number"]
    if action == "close":
        gh_api([f"repos/{repo}/issues/{number}/comments"], {"body": plan["comment"]})
        gh_api([f"repos/{repo}/issues/{number}", "-X", "PATCH"], {"state": "closed", "state_reason": "completed"})
        return f"closed #{number}"
    patch = {"body": plan["body"]}
    if action == "reopen":
        patch["state"] = "open"
    gh_api([f"repos/{repo}/issues/{number}", "-X", "PATCH"], patch)
    gh_api([f"repos/{repo}/issues/{number}/comments"], {"body": plan["comment"]})
    return f"{'reopened' if action == 'reopen' else 'updated'} #{number}"


def snapshot_meta(snapshot: dict) -> dict:
    project = snapshot.get("project") or {}
    return {"number": project.get("number", 3), "fetchedAt": snapshot.get("fetchedAt"), "items": len(snapshot.get("items", []))}


def cmd_check(args: argparse.Namespace) -> int:
    try:
        snapshot = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
        now = parse_time(args.now) if args.now else dt.datetime.now(dt.timezone.utc)
        if now is None:
            raise NoVerdict(f"--now {args.now!r} is not an ISO timestamp")
        findings = analyze(snapshot, now, pr_window_hours=args.pr_window_hours, stale_days=args.stale_days,
                           root_logins=tuple(args.root_login or DEFAULT_ROOT_LOGINS))
    except (NoVerdict, OSError, json.JSONDecodeError) as exc:
        print(f"board-drift-observer: NO VERDICT: {exc}", file=sys.stderr)
        return NO_VERDICT
    result = {"schema": FINDINGS_SCHEMA, "meta": snapshot_meta(snapshot), "evaluatedAt": iso(now),
              "digest": digest(findings), "findings": findings}
    Path(args.out).write_text(json.dumps(result, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    print(f"board-drift-observer: {len(findings)} finding(s)")
    for f in findings:
        print(f"  {f['rule']}  {f['ref']}  {f['text']}")
    return 0


def cmd_report(args: argparse.Namespace) -> int:
    try:
        data = json.loads(Path(args.findings).read_text(encoding="utf-8"))
        if data.get("schema") != FINDINGS_SCHEMA or not isinstance(data.get("findings"), list):
            raise NoVerdict("findings file is not a board-drift findings document")
    except (NoVerdict, OSError, json.JSONDecodeError) as exc:
        print(f"board-drift-observer: NO VERDICT: {exc}", file=sys.stderr)
        return NO_VERDICT
    existing = find_report_issue(args.repo)
    plan = plan_report(data["findings"], existing, data["meta"])
    print(f"board-drift-observer: plan={plan['action']}")
    if args.dry_run:
        print(json.dumps(plan, indent=2))
        return 0
    print(f"board-drift-observer: {apply_plan(args.repo, plan, existing)}")
    return 0


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="command", required=True)

    check = sub.add_parser("check", help="analyze a snapshot and write findings.json")
    check.add_argument("--snapshot", required=True)
    check.add_argument("--out", required=True)
    check.add_argument("--now", default=None, help="ISO timestamp override, for tests")
    check.add_argument("--pr-window-hours", type=float, default=24)
    check.add_argument("--stale-days", type=float, default=7)
    check.add_argument("--root-login", action="append", help=f"Root account login (default {DEFAULT_ROOT_LOGINS[0]})")
    check.set_defaults(func=cmd_check)

    report = sub.add_parser("report", help="open, update or close the single report issue")
    report.add_argument("--findings", required=True)
    report.add_argument("--repo", default=REPORT_REPO)
    report.add_argument("--dry-run", action="store_true")
    report.set_defaults(func=cmd_report)

    args = ap.parse_args(argv)
    try:
        return args.func(args)
    except RuntimeError as exc:
        print(f"board-drift-observer: ERROR: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
