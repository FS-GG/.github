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
     ruling marker (RULING_MARKER). THIS IS A HEURISTIC, NOT A RULE BY CONSTRUCTION: it reads only the
     first line of a comment for a Root/owner/user signature plus a ruling-type word. It has false
     positives (a progress note that opens "Root/fdev ... decision") and false negatives (a ruling
     phrased without those words, or a ruling made outside a comment on the card). A comment that
     carries ACK_TOKEN is a disposition and is never itself a ruling.
  s  A watched workflow's cron has stopped firing (SCHEDULE_WATCH): the nightly main-health observer has no
     fulfilling run newer than 2 days, or this observer's own PREVIOUS fulfilling run is older than 2 hours.
     A fulfilling run is a `schedule` run or a `workflow_dispatch` run by the hourly fdev dispatcher (Root
     decision on #4990: the dispatcher is the designed fallback for a dropped cron).
     Not a card rule: it reads schedule.json (board-drift-schedule-fetch.py), the mutual half of the
     main-health observer's freshness check. Reported in the same issue, with no ack route (nothing on a
     card to comment on): it clears when the schedule fires again. LIMIT: the self-check is reported by a
     run, so it can only appear when a run happens; a total stop of this workflow is caught by the
     main-health observer's freshness check, which reads this workflow's schedule from outside.
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

Since phase 2 of the board-currency design the issue is "Board findings: Project 3" (#4985, renamed from
"Board drift: Project 3", which the report still recognises and renames). `check --currency` merges the
board-currency findings (scripts/board-currency-rules.py) into the same finding set, through the same
private-placeholder gate, so drift and currency findings share one issue, one digest and one writer.

  no findings, no issue            -> nothing
  findings, no issue               -> create
  findings, same finding set       -> leave the issue alone (no body edit, no comment)
  findings, changed finding set    -> rewrite the body, and comment once with what changed
  findings, issue closed by Root,
    same finding set               -> leave it closed (reopen only when the digest changes)
  findings, issue closed, changed  -> reopen, rewrite, comment
  no findings, issue open          -> comment, rewrite the marker to the empty set, close
  body near 65,536 characters      -> truncated with an "N more" line; the digest still covers all

The finding set is identified by a digest of finding keys carried in a hidden marker in the body, and a
key excludes volatile values (ages), so an unchanged situation never rewrites the issue.

PRIVACY
=======

The report is published in a PUBLIC repository, but Project 3 holds cards and PRs from private
repositories. `repository.isPrivate` is fetched; only an explicit `false` is public. For a private or
unknown-visibility repository, and for a draft card, the issue body, findings.json and the run summary
carry only `private:<12 hex of HMAC-SHA256>`: no title, no PR name, no URL, no repo or number. Unknown
counts as private. The HMAC key is the repository secret BOARD_DRIFT_HMAC_KEY, used only by the read job's
`check` step, so a placeholder cannot be recomputed by someone who guesses a private name. With the key
missing, empty or shorter than 16 bytes `check` exits NO VERDICT (2): it never falls back to an unkeyed
hash, and it needs the key even when nothing is private. A local operator recovers names with the fetch
script and `check --reveal-private` (no key needed, output stays local).

Create the secret once, before enabling the workflow (rotating it changes every placeholder, so the next
run rewrites the issue once):

    openssl rand -hex 32 | gh secret set BOARD_DRIFT_HMAC_KEY --repo FS-GG/.github

Failure messages from the fetch name only a fixed call label and an exit code (graphql, search, comments,
repo-visibility): never an argv, a repository, a number or response text, because the Actions log is public.

DISPOSITIONS
============

Every rule has a recorded-disposition route: a Root-login comment on the card containing
`[board-drift-ack] <rule>:<key>` (the key is printed as `Ack:` on each report line; `<rule>:*` covers any
finding of that rule). An ack only counts if it is NEWER than the evidence it covers: the Status change
for a/b-open-done/c, the later of Status change and close time for b-closed, the last item change for
d and x, and the ruling comment itself for e. An (a) ack names the PR set and stays cleared while the
current PR set is a subset of it. A (c) ack silences the card for one stale window only. Rule (a) is
ranked low (still listed, separately) when every referencing PR title starts with `docs`.

Refs are rendered in code spans on purpose. A linked reference makes GitHub write a cross-reference
into the referenced issue's timeline, which advances that issue's updatedAt; rule (c) reads that
clock, so a linking report would keep a stale In progress card looking alive.

Exit codes: 0 success; 1 an unexpected error; 2 NO VERDICT (the input could not be trusted).
"""
from __future__ import annotations

import argparse
import datetime as dt
import hashlib
import hmac
import json
import os
import re
import subprocess
import sys
from pathlib import Path

SNAPSHOT_SCHEMA = "fsgg:board-drift-snapshot/v1"
SCHEDULE_SCHEMA = "fsgg:board-drift-schedule/v1"
FINDINGS_SCHEMA = "fsgg:board-drift-findings/v1"
REPORT_TITLE = "Board findings: Project 3"
# The issue's title before drift and board-currency findings converged (design §4, Root decision 3:
# reuse #4985). The report still finds an issue with the legacy title and renames it.
LEGACY_TITLES = ("Board drift: Project 3",)
CURRENCY_SCHEMA = "fsgg:board-currency-findings/v1"
CURRENCY_RULES = ("R1", "R2", "R3", "R4", "R5", "R6", "R7", "R8", "R9", "U")
REPORT_LABEL = "board-drift"
REPORT_REPO = "FS-GG/.github"
MARKER_PREFIX = "fsgg:board-drift/v1"

NO_VERDICT = 2

# Rule s: workflow file -> (longest allowed gap since its newest `schedule` run, why).
# Two days for the nightly main-health observer (2x its cadence, and a missed night is not paged at once);
# two hours for this observer itself (2x its hourly cadence). Strictly greater than is a finding.
SCHEDULE_WATCH: dict[str, tuple[dt.timedelta, str]] = {
    "main-health-observer.yml": (dt.timedelta(days=2), "peer"),
    "board-drift-observer.yml": (dt.timedelta(hours=2), "self"),
}

HMAC_KEY_ENV = "BOARD_DRIFT_HMAC_KEY"
MIN_KEY_BYTES = 16

# GitHub rejects an issue body above 65,536 characters; stay well under it and say what was cut.
BODY_LIMIT = 60000
MARKER_KEYS_LIMIT = 12000

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
# A recorded disposition: `[board-drift-ack] <rule>:<key>` in a Root-login comment on the card, newer than
# the evidence it covers (see `acked`). `<key>` is the suffix of the finding key shown on the report line,
# or `*` for any finding of that rule on the card.
ACK_TOKEN = "[board-drift-ack]"
_ACK = re.compile(r"\[board-drift-ack\]\s+([a-ex]):(\S+)")
# Heuristic only: a PR whose title starts with "docs" that says Refs is usually a record, not the work.
_DOCS_TITLE = re.compile(r"\s*docs?\b(?!\w)", re.I)

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


def raw_ref(item: dict) -> str:
    if item.get("repo") and item.get("number") is not None:
        return f"{item['repo']}#{item['number']}"
    return f"draft:{item.get('itemId') or 'unknown'}"


def opaque(raw: str, key: bytes) -> str:
    """A stable placeholder for something that must not be published: no name, no number, no title.
    HMAC-SHA256 under a repository secret, so it cannot be recomputed by someone who guesses a name."""
    return "private:" + hmac.new(key, raw.encode("utf-8"), hashlib.sha256).hexdigest()[:12]


def load_key(environ: dict[str, str]) -> bytes | None:
    """The placeholder key from BOARD_DRIFT_HMAC_KEY, or None when absent or empty."""
    value = environ.get(HMAC_KEY_ENV, "")
    return value.encode("utf-8") if value else None


def is_public(flag: object) -> bool:
    """Only an explicit `false` visibility is public. Missing, null or anything else is unknown, and
    unknown counts as private: this repository is public and the report is published here."""
    return flag is False


def shown_ref(item: dict, reveal: bool, key: bytes | None) -> str:
    raw = raw_ref(item)
    return raw if reveal or is_public(item.get("repoPrivate")) else opaque(raw, key)


def shown_pr(pr: dict, reveal: bool, key: bytes | None) -> str:
    raw = f"{pr.get('repo')}#{pr.get('number')}"
    return raw if reveal or is_public(pr.get("repoPrivate")) else opaque(raw, key)


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


def finding(rule: str, item: dict, extra: str, text: str, *, reveal: bool = False, key: bytes | None = None,
            rank: str = "normal",
            **more: object) -> dict:
    """One finding. Everything published (ref, title, urls, PR names) goes through the visibility gate
    here, so findings.json, the run summary and the issue body cannot disagree about what is private."""
    public = reveal or is_public(item.get("repoPrivate"))
    ref = shown_ref(item, reveal, key)
    record = {"rule": rule, "ref": ref, "key": f"{rule}|{ref}|{extra}", "text": text, "rank": rank,
              "ack": f"{ACK_TOKEN} {rule}:{extra}", **more}
    if public and item.get("title"):
        record["title"] = item["title"]
    if not public:
        record["private"] = True
        for url in ("prUrl", "commentUrl"):
            record.pop(url, None)
    return record


def parse_acks(comments: list[dict], roots: set[str]) -> list[tuple[dt.datetime, str, str]]:
    """Recorded dispositions: `[board-drift-ack] <rule>:<key>` in a Root-login comment. `<key>` is the
    finding's key suffix (shown on the report line), or `*` for any finding of that rule."""
    out = []
    for c in comments:
        created = parse_time(c.get("createdAt"))
        if created is None or str(c.get("author", "")).lower() not in roots:
            continue
        for m in _ACK.finditer(c.get("body") or ""):
            out.append((created, m.group(1), m.group(2).rstrip(".,;)")))
    return out


def acked(acks: list[tuple[dt.datetime, str, str]], rule: str, extra: str, since: dt.datetime | None) -> bool:
    """True when a Root ack of this finding is NEWER than the evidence it covers (`since`). An (a) ack
    covers the PR set it names and stays cleared while the current PR set is a subset of it."""
    for created, ack_rule, ack_key in acks:
        if ack_rule != rule or (since is not None and created <= since):
            continue
        if ack_key == "*" or ack_key == extra:
            return True
        if rule == "a" and set(extra.split("+")) <= set(ack_key.split("+")):
            return True
    return False


def analyze(
    snapshot: dict,
    now: dt.datetime,
    *,
    pr_window_hours: float = 24,
    stale_days: float = 7,
    root_logins: tuple[str, ...] = DEFAULT_ROOT_LOGINS,
    reveal_private: bool = False,
    hmac_key: bytes | None = None,
    schedule: dict | None = None,
) -> list[dict]:
    """PURE. Findings for a validated snapshot at `now`, in a stable order.

    `reveal_private` exists for a local operator who holds the snapshot and wants real names; the
    workflow never passes it, so published output carries placeholders for private/unknown repos."""
    validate_snapshot(snapshot)
    # Fail closed: without a usable key there is no safe placeholder, and falling back to an unkeyed
    # hash would let a reader confirm a guessed private name. Required even when nothing is private.
    if not reveal_private and (hmac_key is None or len(hmac_key) < MIN_KEY_BYTES):
        raise NoVerdict(f"{HMAC_KEY_ENV} is missing or shorter than {MIN_KEY_BYTES} bytes: refusing to publish "
                        "placeholders without a key")
    hk = hmac_key
    roots = {login.lower() for login in root_logins}
    window = dt.timedelta(hours=pr_window_hours)
    stale = dt.timedelta(days=stale_days)
    rv = reveal_private

    prs_by_ref: dict[tuple[str, int], list[dict]] = {}
    for pr in snapshot["pullRequests"]:
        for ref in pr_refs(pr.get("repo", ""), pr.get("body")):
            prs_by_ref.setdefault(ref, []).append(pr)

    out: list[dict] = []
    for item in snapshot["items"]:
        status, state = item.get("status"), item.get("state")
        key = (item.get("repo"), item.get("number"))
        status_at = parse_time(item.get("statusUpdatedAt"))
        acks = parse_acks(item.get("comments", []), roots)

        # (d) Metadata first: an item with no Status cannot be reasoned about by the other rules.
        missing = [
            name
            for name, value in (("Status", status), ("Track", item.get("track")), ("Work level", item.get("workLevel")))
            if not value
        ]
        if missing and not acked(acks, "d", ",".join(missing), parse_time(item.get("itemUpdatedAt"))):
            out.append(finding("d", item, ",".join(missing), "missing " + ", ".join(missing), reveal=rv, key=hk,
                               missing=missing, status=status))
        if item.get("unreadable"):
            if not acked(acks, "x", "unreadable", parse_time(item.get("itemUpdatedAt"))):
                out.append(finding("x", item, "unreadable", "content could not be read (null content)",
                                   reveal=rv, key=hk, status=status))
            continue
        if not status or state is None:
            continue

        closed = state in CLOSED_STATES
        # (b)
        closed_at = parse_time(item.get("closedAt"))
        b_since = max([t for t in (status_at, closed_at) if t is not None], default=None)
        if closed and status != "Done" and not acked(acks, "b", "closed", b_since):
            out.append(finding("b", item, "closed", f"{state.lower()} but Status is {status}", reveal=rv, key=hk,
                               status=status, state=state))
        if not closed and status == "Done" and not acked(acks, "b", "open-done", status_at):
            out.append(finding("b", item, "open-done", "open but Status is Done", reveal=rv, key=hk,
                               status=status, state=state))

        linked = [p for p in prs_by_ref.get(key, []) if (p.get("repo"), p.get("number")) != key]

        # (a)
        if status in PRE_ACTION_STATUSES and not closed:
            fresh = []
            for pr in sorted(linked, key=lambda p: (p.get("repo", ""), p.get("number", 0))):
                changed = parse_time(pr.get("updatedAt"))
                if pr.get("draft") or changed is None or now - changed > window:
                    continue
                fresh.append((pr, shown_pr(pr, rv, hk), changed))
            if fresh:
                extra = "+".join(name for _, name, _ in fresh)
                if not acked(acks, "a", extra, status_at):
                    names = ", ".join(name for _, name, _ in fresh)
                    newest = max(changed for _, _, changed in fresh)
                    # Heuristic: a docs-titled PR that says Refs is usually a record, not the work.
                    docs_only = all(_DOCS_TITLE.match(pr.get("title") or "") for pr, _, _ in fresh)
                    out.append(
                        finding("a", item, extra,
                                f"{status} while open non-draft PR {names} says Refs (newest change {iso(newest)})"
                                + ("; every PR title starts with docs, lower confidence" if docs_only else ""),
                                reveal=rv, key=hk, rank="low" if docs_only else "normal", status=status,
                                prs=[name for _, name, _ in fresh])
                    )

        # (c)
        if status == "In progress" and not closed:
            signals = [status_at, parse_time(item.get("contentUpdatedAt"))]
            signals += [parse_time(p.get("updatedAt")) for p in linked]
            signals += [parse_time(c.get("createdAt")) for c in item.get("comments", [])]
            signals += [parse_time(c.get("committedAt")) for c in item.get("commits", [])]
            known = [s for s in signals if s is not None]
            last = max(known) if known else None
            # An ack silences (c) for one stale window only, so a stalled card resurfaces.
            if (last is None or now - last > stale) and not acked(acks, "c", "idle", now - stale):
                idle = "no activity signal at all" if last is None else f"last activity {iso(last)}"
                out.append(finding("c", item, "idle", f"In progress with {idle} (more than {stale_days:g} days)",
                                   reveal=rv, key=hk, status=status))

        # (e) A first-line ruling/decision-marker heuristic with known precision and recall limits.
        if status != "Done" and not closed and status_at is not None:
            newer = sorted(
                (c for c in item.get("comments", [])
                 if (created := parse_time(c.get("createdAt"))) is not None and created > status_at
                 and str(c.get("author", "")).lower() in roots),
                key=lambda c: (parse_time(c["createdAt"]), str(c.get("id"))),
            )
            pending = [
                comment for comment in newer
                # An ack comment is a disposition, never a ruling, whatever words it contains.
                if ACK_TOKEN not in (comment.get("body") or "")
                and RULING_MARKER.search(comment.get("body") or "")
                and not acked(acks, "e", str(comment.get("id")), parse_time(comment["createdAt"]))
            ]
            if pending:
                # One finding per card, keyed by the newest ruling: a later ruling changes the key.
                last = pending[-1]
                out.append(
                    finding("e", item, str(last.get("id")),
                            f"{len(pending)} Root ruling comment(s) newer than the Status change ({iso(status_at)}); "
                            f"newest {last.get('id')} at {last.get('createdAt')}",
                            reveal=rv, key=hk, status=status, commentUrl=last.get("url"))
                )

    if schedule is not None:
        out += schedule_findings(schedule, now)

    order = {"a": 0, "b": 1, "c": 2, "d": 3, "e": 4, "s": 5, "x": 6}
    return sorted(out, key=lambda f: (order[f["rule"]], f["ref"], f["key"]))


def key_rule(key: str) -> str:
    """The rule of a finding key (`<rule>|<ref>|<extra>`)."""
    return key.split("|", 1)[0]


def validate_schedule(doc: object) -> dict:
    if not isinstance(doc, dict) or doc.get("schema") != SCHEDULE_SCHEMA:
        raise NoVerdict(f"schedule document schema is not {SCHEDULE_SCHEMA}")
    if not isinstance(doc.get("workflows"), list):
        raise NoVerdict("schedule document lacks its workflows array")
    return doc


def schedule_finding(file: str, kind: str, text: str, role: str) -> dict:
    """A rule-s finding. Workflow files are public repository paths: nothing here is private, and there
    is no card to comment on, so there is no ack token. The key excludes ages so it dedupes."""
    ref = f"workflow:{file}"
    return {"rule": "s", "ref": ref, "key": f"s|{ref}|{kind}", "text": text, "rank": "normal", "role": role}


def schedule_findings(doc: object, now: dt.datetime) -> list[dict]:
    """PURE. Rule s: a watched workflow whose newest fulfilling run is too old, never ran, is absent or
    disabled, or could not be read. A fulfilling run is a `schedule` run, or a `workflow_dispatch` run that
    the hourly fdev dispatcher triggered (the designed fallback for a dropped cron; the fetch decides which
    dispatches qualify, by actor and event). A watched workflow missing from the document counts as
    unreadable."""
    validate_schedule(doc)
    entries = {e.get("file"): e for e in doc["workflows"] if isinstance(e, dict)}
    out: list[dict] = []
    for file, (limit, role) in SCHEDULE_WATCH.items():
        who = "this observer's previous fulfilling run" if role == "self" else "the fulfilling run"
        entry = entries.get(file)
        state = (entry or {}).get("state")
        if state == "absent":
            out.append(schedule_finding(file, "absent", f"workflow file not found on the default branch ({who} cannot exist)", role))
            continue
        created = parse_time((entry or {}).get("createdAt"))
        times = [parse_time((entry or {}).get(k)) for k in ("newestScheduleRunAt", "newestDispatchRunAt")]
        given = [(entry or {}).get(k) for k in ("newestScheduleRunAt", "newestDispatchRunAt")]
        if state != "ok" or created is None or any(g and t is None for g, t in zip(given, times)):
            out.append(schedule_finding(file, "unreadable", "schedule history could not be read (no verdict on this schedule)", role))
            continue
        if entry.get("workflowState") != "active":
            out.append(schedule_finding(file, "disabled", f"workflow state is {entry.get('workflowState')!r}: a disabled workflow does not fire", role))
            continue
        newest = max((t for t in times if t is not None), default=None)
        if newest is None:
            if now - created > limit:
                out.append(schedule_finding(file, "never-ran", f"no scheduled or dispatcher run since the workflow was registered "
                                            f"{iso(created)} (more than {format_limit(limit)})", role))
            continue
        if now - newest > limit:
            out.append(schedule_finding(file, "stale", f"newest scheduled or dispatcher run {iso(newest)} is older than "
                                        f"{format_limit(limit)}; {who} is overdue", role))
    return out


def format_limit(limit: dt.timedelta) -> str:
    hours = limit.total_seconds() / 3600
    return f"{hours / 24:g} days" if hours >= 48 else f"{hours:g} hours"


def digest(findings: list[dict]) -> str:
    return hashlib.sha256("\n".join(sorted(f["key"] for f in findings)).encode()).hexdigest()


RULE_TITLES = {
    "a": "a. Pre-action Status (Blocked, Backlog, Ready) with a fresh open PR that says Refs",
    "b": "b. Closed/open state disagrees with Done",
    "c": "c. In progress with no activity",
    "d": "d. Missing Status, Track or Work level",
    "e": "e. Root ruling comment newer than the last Status change (heuristic)",
    "s": "s. A watched workflow's schedule has stopped firing (peer and self check)",
    "x": "x. Content could not be read",
    # Board-currency rule set (scripts/board-currency-rules.py), merged in by `check --currency`.
    "R1": "R1. Routine PR references exactly one open Delivery with a parent Outcome",
    "R2": "R2. Closure, Done and Outcome acceptance agree",
    "R3": "R3. Blocked names an open blocker",
    "R4": "R4. Active Status has live evidence",
    "R5": "R5. Outcome with every sub-issue closed",
    "R6": "R6. Open Outcome with no open Delivery",
    "R7": "R7. Status, Work level and Track are set on a native issue",
    "R8": "R8. Delivery Status agrees with its PR state",
    "R9": "R9. Delivery sits under an Outcome",
    "U": "U. Board-currency input could not be read (Unknown)",
}


def currency_findings(document: object, key: bytes | None, reveal: bool = False) -> list[dict]:
    """PURE. Board-currency findings as report records, through the same visibility gate as drift findings.

    The ruleset document carries raw subjects and an explicit `public` flag. A subject that is not
    explicitly public (private, unknown, or a draft card) is published only as `private:<HMAC>`, with no
    title. Currency findings have no ack route: they clear when the native state they read changes."""
    if (not isinstance(document, dict) or not str(document.get("schema", "")).startswith("fsgg:board-currency-findings/")
            or not isinstance(document.get("findings"), list)):
        raise NoVerdict("currency findings file is not a board-currency findings document")
    if not reveal and (key is None or len(key) < MIN_KEY_BYTES):
        raise NoVerdict(f"{HMAC_KEY_ENV} is missing or shorter than {MIN_KEY_BYTES} bytes: refusing to publish "
                        "placeholders without a key")
    out = []
    for f in document["findings"]:
        rule, subject, reason = f.get("rule"), f.get("subject"), f.get("reason")
        if rule not in CURRENCY_RULES or not isinstance(subject, str) or not isinstance(reason, str):
            raise NoVerdict("currency finding lacks a rule, subject or reason")
        public = reveal or f.get("public") is True
        ref = subject if public else opaque(subject, key)
        record = {"rule": rule, "ref": ref, "key": f"{rule}|{ref}|{reason}", "text": str(f.get("text", "")),
                  "rank": "normal", "ack": "", "ruleset": document.get("ruleset")}
        if f.get("pending"):
            record["text"] += f" ({f['pending']})"
        if public and f.get("title"):
            record["title"] = f["title"]
        if not public:
            record["private"] = True
        out.append(record)
    return sorted(out, key=lambda r: (CURRENCY_RULES.index(r["rule"]), r["ref"], r["key"]))


def code(value: object) -> str:
    """A code span: never autolinked, so the report writes no cross-reference or mention anywhere."""
    return "`" + str(value).replace("`", "'").replace("\n", " ") + "`"


def _marker(findings: list[dict]) -> str:
    keys = sorted(f["key"] for f in findings)
    data: dict = {"digest": digest(findings), "count": len(keys), "keys": keys}
    if len(json.dumps(data)) > MARKER_KEYS_LIMIT:
        data = {"digest": data["digest"], "count": len(keys)}
    # `#` is written as a JSON escape so even the hidden marker carries no issue reference.
    text = json.dumps(data, separators=(",", ":"), sort_keys=True).replace("#", "\\u0023")
    return f"<!-- {MARKER_PREFIX} {text} -->"


def finding_line(f: dict) -> str:
    title = f" {code(f['title'])}" if f.get("title") else ""
    if f.get("ack"):
        ack = f" Ack: {code(f['ack'])}"
    elif f["rule"] == "s":
        ack = " Clears when the schedule fires again."
    else:
        ack = ""
    return f"- {code(f['ref'])}{title}: {code(f['text'])}{ack}"


def render_body(findings: list[dict], snapshot_meta: dict, limit: int = BODY_LIMIT) -> str:
    head = [
        _marker(findings),
        "",
        f"Generated by the hourly board observer from read-only snapshots of Project "
        f"{snapshot_meta.get('number', 3)} taken {snapshot_meta.get('fetchedAt', 'unknown')} "
        f"({snapshot_meta.get('items', '?')} cards): board-drift rules a-e, s and x, and the deterministic "
        f"board-currency rules R1-R9 and U ({snapshot_meta.get('currencyRuleset') or 'not evaluated'}; design "
        "`docs/coordination/2026-10-31-board-currency-gate.md`). It never edits board fields: Root resolves each "
        "finding with a guarded Status write, a native fix or a recorded disposition, and this issue is rewritten "
        "when the set changes.",
        "",
        "This repository is public. Cards in private or unknown-visibility repositories appear only as "
        "`private:<id>` with no title; reveal them locally by running `board-drift-fetch.py` then "
        "`board-drift-observer.py check --reveal-private`.",
        "",
        f"{len(findings)} finding(s).",
    ]
    foot = [
        "",
        "Disposition: write the Status, or comment on the card from the Root login with the finding's "
        "`Ack:` token (`[board-drift-ack] <rule>:<key>`, or `<rule>:*` for any finding of that rule) when no "
        "Status change is owed. An ack must be newer than the evidence it covers. Rule e is a first-line "
        "ruling-marker heuristic with known precision and recall limits. Board-currency findings (R1-R9, U) "
        "have no ack: each clears when the native state it reads is fixed. Rule s (a schedule that stopped "
        "firing) has no ack either: it clears when that schedule fires again.",
        "",
        f"Owner: {code('FS-GG/.github#4730')}. Rule: work-board lifecycle-contract, same-turn Status write.",
    ]
    sections: list[tuple[str, list[dict]]] = []
    for rule in ("a", "b", "c", "d", "e", "s", "x") + CURRENCY_RULES:
        normal = [f for f in findings if f["rule"] == rule and f.get("rank") != "low"]
        low = [f for f in findings if f["rule"] == rule and f.get("rank") == "low"]
        if normal:
            sections.append((RULE_TITLES[rule], normal))
        if low:
            sections.append((RULE_TITLES[rule] + " - lower confidence", low))
    budget = limit - sum(len(x) + 1 for x in head + foot) - 400
    lines: list[str] = []
    shown = 0
    full = False
    for title, group in sections:
        # R1 churns with every merge, so its section leads with a count (Root decision on #5103).
        count = f" ({len(group)})" if title.startswith("R1.") else ""
        header = ["", f"### {title}{count}", ""]
        cost = sum(len(x) + 1 for x in header)
        if full or budget - cost < 0:
            break
        lines += header
        budget -= cost
        for f in group:
            line = finding_line(f)
            if budget - len(line) - 1 < 0:
                full = True
                break
            lines.append(line)
            budget -= len(line) + 1
            shown += 1
    if shown < len(findings):
        lines += ["", f"- ... {len(findings) - shown} more finding(s) not shown to stay under the issue size limit; "
                      "the digest above covers all of them."]
    return "\n".join(head + lines + foot) + "\n"


def render_clear_body(snapshot_meta: dict) -> str:
    return "\n".join([
        _marker([]),
        "",
        f"No board findings at {snapshot_meta.get('fetchedAt', 'unknown')}. The observer reopens this "
        "issue if a finding reappears.",
    ]) + "\n"


def parse_marker(body: str | None) -> dict | None:
    match = re.search(rf"<!--\s*{re.escape(MARKER_PREFIX)}\s+(\{{.*?\}})\s*-->", body or "", re.S)
    if not match:
        return None
    try:
        data = json.loads(match.group(1))
    except json.JSONDecodeError:
        return None
    return data if isinstance(data, dict) and isinstance(data.get("digest"), str) else None


def plan_report(findings: list[dict], existing: dict | None, snapshot_meta: dict) -> dict:
    """PURE. What to do to the one report issue. `existing` is None or {number, state, body}."""
    if not findings:
        if existing and existing.get("state") == "open":
            return {
                "action": "close",
                "body": render_clear_body(snapshot_meta),
                "comment": "No board findings at the latest hourly observation "
                f"({snapshot_meta.get('fetchedAt', 'unknown')}). Closing; the observer reopens this issue if a finding reappears.\n",
            }
        if existing and existing.get("title") in LEGACY_TITLES:
            return {"action": "rename", "reason": "no findings; legacy title"}
        return {"action": "noop", "reason": "no findings and no open report issue"}

    current = digest(findings)
    previous = parse_marker(existing.get("body")) if existing else None
    if existing is not None and previous and previous.get("digest") == current:
        # Unchanged set: leave the issue alone. That includes an issue Root closed - it is reopened
        # only when the finding set changes, never merely because the same findings still stand.
        if existing.get("title") in LEGACY_TITLES:
            return {"action": "rename", "reason": "finding set unchanged; legacy title"}
        return {"action": "noop", "reason": "finding set unchanged"
                + ("" if existing.get("state") == "open" else " (issue closed; not reopened)")}

    body = render_body(findings, snapshot_meta)
    if existing is None:
        return {"action": "create", "body": body}

    current_keys = {f["key"] for f in findings}
    if previous and isinstance(previous.get("keys"), list):
        previous_keys = set(previous["keys"])
        added = sorted(current_keys - previous_keys)
        removed = sorted(previous_keys - current_keys)
        # Root decision (#5103 review): comment only when the drift set changes or a NEW non-R1 currency
        # finding appears. R1 churns with every merge; it is shown as a count plus a list in the body only,
        # and a change that touches nothing else rewrites the body without a comment or a reopen.
        notable = [k for k in added + removed if key_rule(k) not in CURRENCY_RULES]
        notable += [k for k in added if key_rule(k) in CURRENCY_RULES and key_rule(k) != "R1"]
        if not notable:
            return {"action": "update", "body": body, "comment": None}
        listed_added = [k for k in added if key_rule(k) != "R1"]
        listed_removed = [k for k in removed if key_rule(k) != "R1"]
        r1_added = len(added) - len(listed_added)
        r1_removed = len(removed) - len(listed_removed)
        comment = [f"Finding set changed: {len(added)} new, {len(removed)} cleared, {len(current_keys)} open."]
        comment += [f"- new {code(key)}" for key in listed_added[:50]]
        comment += [f"- cleared {code(key)}" for key in listed_removed[:50]]
        if len(listed_added) > 50 or len(listed_removed) > 50:
            comment.append("- (list truncated; the body holds the current set)")
        if r1_added or r1_removed:
            comment.append(f"- R1: {r1_added} new, {r1_removed} cleared (listed in the body only)")
    else:
        comment = [f"Finding set changed: {len(current_keys)} open (previous set not recorded in full)."]
    return {
        "action": "update" if existing.get("state") == "open" else "reopen",
        "body": body,
        "comment": "\n".join(comment)[:BODY_LIMIT] + "\n",
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
        # Fixed text only: this log is public and a response body is not ours to republish.
        raise RuntimeError(f"gh api call failed (exit {proc.returncode})")
    return proc.stdout


def find_report_issue(repo: str) -> dict | None:
    """The single issue with REPORT_LABEL and REPORT_TITLE (or a legacy title). Two open ones are a hard error."""
    out = gh_api([f"repos/{repo}/issues?labels={REPORT_LABEL}&state=all&per_page=100", "--paginate", "--jq", ".[]"])
    matches = []
    for line in out.splitlines():
        if not line.strip():
            continue
        issue = json.loads(line)
        if "pull_request" not in issue and issue.get("title") in (REPORT_TITLE, *LEGACY_TITLES):
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
    if action == "rename":
        gh_api([f"repos/{repo}/issues/{existing['number']}", "-X", "PATCH"], {"title": REPORT_TITLE})
        return f"renamed #{existing['number']}"
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
        gh_api([f"repos/{repo}/issues/{number}", "-X", "PATCH"],
               {"state": "closed", "state_reason": "completed", "body": plan["body"], "title": REPORT_TITLE})
        return f"closed #{number}"
    patch = {"body": plan["body"], "title": REPORT_TITLE}
    if action == "reopen":
        patch["state"] = "open"
    gh_api([f"repos/{repo}/issues/{number}", "-X", "PATCH"], patch)
    if plan.get("comment"):
        gh_api([f"repos/{repo}/issues/{number}/comments"], {"body": plan["comment"]})
    return f"{'reopened' if action == 'reopen' else 'updated'} #{number}" + ("" if plan.get("comment") else " (body only)")


def snapshot_meta(snapshot: dict) -> dict:
    project = snapshot.get("project") or {}
    return {"number": project.get("number", 3), "fetchedAt": snapshot.get("fetchedAt"), "items": len(snapshot.get("items", []))}


def cmd_check(args: argparse.Namespace) -> int:
    try:
        snapshot = json.loads(Path(args.snapshot).read_text(encoding="utf-8"))
        now = parse_time(args.now) if args.now else dt.datetime.now(dt.timezone.utc)
        if now is None:
            raise NoVerdict(f"--now {args.now!r} is not an ISO timestamp")
        # With --schedule, an unusable document is NO VERDICT: the schedule read records its own failures per
        # workflow, so a missing or malformed file is a broken step, never "no drift".
        schedule = validate_schedule(json.loads(Path(args.schedule).read_text(encoding="utf-8"))) if args.schedule else None
        findings = analyze(snapshot, now, pr_window_hours=args.pr_window_hours, stale_days=args.stale_days,
                           root_logins=tuple(args.root_login or DEFAULT_ROOT_LOGINS),
                           reveal_private=args.reveal_private, hmac_key=load_key(dict(os.environ)),
                           schedule=schedule)
        currency_ruleset = None
        if args.currency:
            document = json.loads(Path(args.currency).read_text(encoding="utf-8"))
            findings += currency_findings(document, load_key(dict(os.environ)), reveal=args.reveal_private)
            currency_ruleset = document.get("ruleset")
    except (NoVerdict, OSError, json.JSONDecodeError) as exc:
        print(f"board-drift-observer: NO VERDICT: {exc}", file=sys.stderr)
        return NO_VERDICT
    meta = dict(snapshot_meta(snapshot), currencyRuleset=currency_ruleset)
    result = {"schema": FINDINGS_SCHEMA, "meta": meta, "evaluatedAt": iso(now),
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
    check.add_argument("--schedule", default=None,
                       help="schedule.json from board-drift-schedule-fetch.py; enables rule s")
    check.add_argument("--now", default=None, help="ISO timestamp override, for tests")
    check.add_argument("--pr-window-hours", type=float, default=24)
    check.add_argument("--stale-days", type=float, default=7)
    check.add_argument("--reveal-private", action="store_true",
                       help="LOCAL OPERATOR USE ONLY: print real names for private/unknown-visibility cards. "
                            "The workflow never passes this.")
    check.add_argument("--root-login", action="append", help=f"Root account login (default {DEFAULT_ROOT_LOGINS[0]})")
    check.add_argument("--currency", default=None,
                       help="board-currency findings.json (scripts/board-currency-rules.py check) to merge into the report")
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
