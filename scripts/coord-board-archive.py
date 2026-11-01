#!/usr/bin/env python3
"""Done retention for the Coordination board (Project 3): archive the Done cards beyond the newest 200 (#5119).

THE POLICY (user requirement, 2026-10-31, FS-GG/.github#5119)
=============================================================

Coordination Project 3 and its readers are limited to 500 items. When Done holds more than 200 cards, every Done
card older than the newest 200 is ARCHIVED, never deleted. "Older" is the card's issue or pull request close time
(`closedAt`), newest first. Archiving is reversible with `unarchiveProjectV2Item`, and the custody file records
every item id this tool touched.

WHY THIS REPLACES THE PREVIOUS ARCHIVER
=======================================

This path used to hold the Project 1 sweeper from .github#2420 and #3052: 30-day retention and at most 100 visible
rows, scheduled daily with `--execute`. Root disabled that workflow on 2026-10-31 and retired its policy for three
reasons:
- It targeted Project 1, not the Coordination V2 board (Project 3).
- Its policy (30 days, 100 visible, any terminal row) contradicts the user's rule (keep the newest 200 Done).
- It failed every day. Its typed `archive-items` route is refused by the engine's v1 admission ("production v1
  admission is not installed"), and that refusal was the only thing stopping it from archiving 211 rows a day.
The read and write guards below replace it. The scheduled workflow runs a dry run only, until Root accepts the
first real run.

THE GUARDS, ALL FAIL-CLOSED
===========================

1. Done only. A card is a candidate only when its Status is Done, its content is an issue or pull request whose
   state is terminal (CLOSED, or MERGED for a PR), and its `closedAt` parses. Anything else stays visible, with
   its reason reported. A Done card over an open issue is drift that must stay visible. A Done card that a live
   (not Done) card still names as a native `blockedBy` blocker stays visible too; the live cards' blocker lists are
   read completely or the run refuses. This is the "never a live row's blocker" guard the reconciler design keeps.
2. Newest 200 kept. Candidates are ranked by `closedAt` descending, with ties broken by repository, number and
   item id, so the selection never depends on connection order. The first `--keep` stay; only the rest are
   planned. The reconciler (tend) must use exactly this ranking (closedAt, then repository, number and item
   id); a shared test pins the two together when tend lands (#5136).
3. Complete read or nothing. The scan reads `items(query: "status:Done")` in pages of 100, at most 5 pages, the
   500-item board limit. A partial page set, a count mismatch, a GraphQL error or an over-limit board refuses
   without archiving anything.
4. Dry run by default. Without `--execute` nothing is sent. `--execute` also requires the exact project id
   (`--expect-project-id`), a per-run bound (`--max-archive`) and the private enrollment binding
   (`--enrollment-binding`), so every real run records which archived cards were enrolled V2 rows.
5. Preimage, one send, readback, custody. Each card is processed in four steps:
   - Its preimage is re-read by item id: same project, not archived, Status Done, and the same content id, state
     and `closedAt` as planned. Any difference skips the card, which is recorded.
   - An intent record is written and flushed.
   - Exactly one `archiveProjectV2Item` is sent.
   - The card is read back. A failed or unreadable send, or a readback that is not archived, is recorded as
     `uncertain` and stops the run. Nothing is ever resent; the next run's preimage read resolves it.

ENROLLED V2 ROWS
================

Archiving an enrolled row is safe. The coherent 0.114.1 reader reports an archived enrolled card as an explicit
archived membership with a "Board card is archived (Done retention)" discrepancy (#5119), not as a missing row.
The custody record marks every archived card `enrolled: true/false` from the private runtime binding's
`selectedIssues`. Do not archive an enrolled card while a reader older than 0.114.1 is the installed reader.

LOCKING (#5136 design, section 8)
=================================

`--execute` takes the shared Project 3 writer lock around each card's preimage re-read, send and readback:
`$XDG_RUNTIME_DIR/fsgg/board-writer-<project>.lock`, plus the installed engine's legacy locks at /tmp during the
transition, with `flock(LOCK_EX | LOCK_NB)`, exactly as tools/routine-delivery.py and V2WriterLock take them (#5141).
It refuses when XDG_RUNTIME_DIR is unset or relative, and stops (nothing sent for that card) when the lock is busy.
`--execute` is therefore a Root-run, fdev-only writer. The hosted workflow only runs the counts-only dry run, which
takes no lock; the reconciler (tend) writes nothing to archive-eligible cards, so the two cannot overlap.

Environment: GH_TOKEN with organization Projects read, plus write for `--execute`. COORD_ARCHIVE_GH overrides the
`gh` binary (tests).
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import os
import subprocess
import sys
from pathlib import Path

KEEP = 200
PAGE = 100
MAX_PAGES = 5
BOARD_LIMIT = PAGE * MAX_PAGES
TERMINAL = ("CLOSED", "MERGED")
CUSTODY_SCHEMA = "fsgg.board-done-retention-custody/1"

SCAN = """
query($owner:String!,$number:Int!,$cursor:String){
  organization(login:$owner){ projectV2(number:$number){
    id title
    all: items(first:1){ totalCount }
    items(first:100, after:$cursor, query:"status:Done"){
      totalCount
      pageInfo{ hasNextPage endCursor }
      nodes{
        id isArchived
        status: fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } }
        content{
          __typename
          ... on Issue{ id number state closedAt repository{ nameWithOwner } }
          ... on PullRequest{ id number state closedAt repository{ nameWithOwner } }
          ... on DraftIssue{ id }
        }
      }
    }
  }}
}
"""

LIVE = """
query($owner:String!,$number:Int!,$cursor:String){
  organization(login:$owner){ projectV2(number:$number){
    items(first:100, after:$cursor, query:"-status:Done"){
      totalCount
      pageInfo{ hasNextPage endCursor }
      nodes{ id content{ __typename ... on Issue{ blockedBy(first:50){ totalCount nodes{ id } } } } }
    }
  }}
}
"""

ITEM = """
query($id:ID!){
  node(id:$id){ ... on ProjectV2Item{
    id isArchived project{ id }
    status: fieldValueByName(name:"Status"){ ... on ProjectV2ItemFieldSingleSelectValue{ name } }
    content{ __typename
      ... on Issue{ id state closedAt blocking(first:50){ totalCount nodes{ id state } } }
      ... on PullRequest{ id state closedAt } }
  } }
}
"""

ARCHIVE = """
mutation($project:ID!,$item:ID!){
  archiveProjectV2Item(input:{projectId:$project, itemId:$item}){ item{ id isArchived } }
}
"""


class Refused(Exception):
    """A failed or incomplete read: nothing is archived. The message is fixed text, never response data."""


class Uncertain(Exception):
    """A send whose effect is unknown: recorded, never resent, and the run stops."""


def gh(args: list[str]) -> str:
    cmd = [os.environ.get("COORD_ARCHIVE_GH", "gh"), *args]
    try:
        proc = subprocess.run(cmd, capture_output=True, text=True, timeout=120, check=False)
    except (subprocess.TimeoutExpired, OSError) as exc:
        raise Refused(f"gh call failed ({type(exc).__name__})") from None
    if proc.returncode:
        raise Refused(f"gh call failed (exit {proc.returncode})")
    return proc.stdout


def graphql(query: str, variables: dict[str, object]) -> dict:
    args = ["api", "graphql", "-f", f"query={query}"]
    for name, value in variables.items():
        if value is not None:
            args += ["-F" if isinstance(value, int) else "-f", f"{name}={value}"]
    try:
        response = json.loads(gh(args))
    except json.JSONDecodeError:
        raise Refused("GraphQL response is not JSON") from None
    if not isinstance(response, dict) or response.get("errors") or not isinstance(response.get("data"), dict):
        raise Refused("GraphQL response carries errors or no data")
    return response["data"]


def parse_time(value: object) -> dt.datetime | None:
    if not isinstance(value, str) or not value:
        return None
    try:
        parsed = dt.datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
    return parsed if parsed.tzinfo else None


def normalize(node: dict) -> dict:
    content = node.get("content") if isinstance(node.get("content"), dict) else {}
    return {
        "itemId": node.get("id"),
        "archived": node.get("isArchived"),
        "status": (node.get("status") or {}).get("name"),
        "kind": content.get("__typename"),
        "contentId": content.get("id"),
        "repo": (content.get("repository") or {}).get("nameWithOwner"),
        "number": content.get("number"),
        "state": content.get("state"),
        "closedAt": content.get("closedAt"),
    }


def scan(owner: str, number: int) -> tuple[dict, list[dict]]:
    """Every Done card on the board, read completely, or Refused."""
    rows: list[dict] = []
    cursor: str | None = None
    project: dict = {}
    total: int | None = None
    for _ in range(MAX_PAGES):
        data = graphql(SCAN, {"owner": owner, "number": number, "cursor": cursor})
        node = (data.get("organization") or {}).get("projectV2")
        if not isinstance(node, dict) or not isinstance(node.get("items"), dict):
            raise Refused("no organization.projectV2.items (token scope or project number?)")
        board_total = (node.get("all") or {}).get("totalCount")
        if not isinstance(board_total, int):
            raise Refused("the board item count is unreadable")
        if board_total > BOARD_LIMIT:
            raise Refused(f"the board holds {board_total} items, above the {BOARD_LIMIT}-item limit: refusing")
        project = {"id": node.get("id"), "title": node.get("title"), "totalCount": board_total}
        page = node["items"]
        nodes, info, count = page.get("nodes"), page.get("pageInfo") or {}, page.get("totalCount")
        if not isinstance(nodes, list) or not isinstance(count, int) or "hasNextPage" not in info:
            raise Refused("a Done page is missing nodes, totalCount or pageInfo")
        if total is not None and count != total:
            raise Refused("the Done count changed while it was read")
        total = count
        rows.extend(normalize(n) for n in nodes if isinstance(n, dict))
        if not info["hasNextPage"]:
            break
        cursor = info.get("endCursor")
        if not cursor:
            raise Refused("a Done page says hasNextPage without an endCursor")
    else:
        raise Refused(f"Done still has more pages after {MAX_PAGES}: refusing a partial read")
    if total is None or len(rows) != total:
        raise Refused("the Done rows read differ from the Done count")
    if len({r["itemId"] for r in rows}) != len(rows):
        raise Refused("a Done card appeared twice: the board changed while it was read")
    return project, rows


def live_blockers(owner: str, number: int) -> tuple[set[str], int]:
    """Content ids every live (not Done) card names as a native blocker, read completely, or Refused.

    A Done card that a live card is still blocked by stays visible: the guard the reconciler design keeps from the
    previous archiver ("never a live row's blocker"). A truncated blocker list refuses rather than guesses.
    """
    protected: set[str] = set()
    cursor: str | None = None
    total: int | None = None
    seen = 0
    for _ in range(MAX_PAGES):
        data = graphql(LIVE, {"owner": owner, "number": number, "cursor": cursor})
        node = (data.get("organization") or {}).get("projectV2")
        page = node.get("items") if isinstance(node, dict) else None
        if not isinstance(page, dict):
            raise Refused("no live items page (token scope or project number?)")
        nodes, info, count = page.get("nodes"), page.get("pageInfo") or {}, page.get("totalCount")
        if not isinstance(nodes, list) or not isinstance(count, int) or "hasNextPage" not in info:
            raise Refused("a live page is missing nodes, totalCount or pageInfo")
        if total is not None and count != total:
            raise Refused("the live count changed while it was read")
        total = count
        for card in nodes:
            seen += 1
            content = (card or {}).get("content") or {}
            if content.get("__typename") != "Issue":
                continue
            blockers = content.get("blockedBy") or {}
            listed = blockers.get("nodes")
            if not isinstance(listed, list) or blockers.get("totalCount") != len(listed):
                raise Refused("a live card's blocker list is truncated or unreadable")
            protected.update(b.get("id") for b in listed if isinstance(b, dict) and b.get("id"))
        if not info["hasNextPage"]:
            break
        cursor = info.get("endCursor")
        if not cursor:
            raise Refused("a live page says hasNextPage without an endCursor")
    else:
        raise Refused(f"live cards still have more pages after {MAX_PAGES}: refusing a partial read")
    if total is None or seen != total:
        raise Refused("the live cards read differ from the live count")
    return protected, total


def plan(rows: list[dict], keep: int = KEEP, protected: set[str] | None = None) -> tuple[list[dict], list[dict], list[tuple[dict, str]]]:
    """Split Done rows into (archive, kept, not-rankable-with-reason). Pure, so every guard is testable.

    Ranked: Done, issue or PR, terminal state, parseable `closedAt`, not already archived. Newest first by
    `closedAt`, ties broken by (repo, number, itemId) so connection order never decides. The first `keep` stay.
    """
    if keep < 0:
        raise ValueError("keep must not be negative")
    ranked: list[tuple[dt.datetime, str, int, str, dict]] = []
    unranked: list[tuple[dict, str]] = []
    for row in rows:
        closed = parse_time(row.get("closedAt"))
        if not row.get("itemId") or not row.get("contentId"):
            unranked.append((row, "unreadable card or content identity: never archived"))
        elif row.get("archived") is not False:
            unranked.append((row, "already archived or archive state unreadable"))
        elif row.get("status") != "Done":
            unranked.append((row, f"Status is {row.get('status')!r}, not Done"))
        elif row.get("kind") not in ("Issue", "PullRequest"):
            unranked.append((row, f"content is {row.get('kind')!r}, not an issue or pull request"))
        elif row.get("state") not in TERMINAL or (row.get("state") == "MERGED" and row.get("kind") != "PullRequest"):
            unranked.append((row, f"Done over a {row.get('state')!r} item: drift stays visible"))
        elif closed is None:
            unranked.append((row, "closedAt is unreadable: never archived"))
        elif not isinstance(row.get("number"), int) or not row.get("repo"):
            unranked.append((row, "unreadable repository or number: never archived"))
        elif protected and row["contentId"] in protected:
            unranked.append((row, "a live card is still blocked by it: stays visible"))
        else:
            ranked.append((closed, str(row["repo"]), int(row["number"]), str(row["itemId"]), row))
    ranked.sort(key=lambda entry: (-entry[0].timestamp(), entry[1], entry[2], entry[3]))
    kept = [entry[4] for entry in ranked[:keep]]
    archive = [entry[4] for entry in ranked[keep:]]
    return archive, kept, unranked


def enrolled_nodes(path: str | None) -> set[str] | None:
    """The private runtime binding's selected issue node ids, or None when no binding is supplied."""
    if not path:
        return None
    try:
        binding = json.loads(Path(path).read_text(encoding="utf-8"))
        selected = binding["selectedIssues"]
        if not isinstance(selected, dict) or not selected:
            raise ValueError
        return set(selected)
    except (OSError, ValueError, KeyError, TypeError):
        raise Refused("the enrollment binding is unreadable") from None


BOARD_WRITER_LOCK = "board-writer-{project}.lock"
LEGACY_LOCKS = ("fsgg-v2-migration-{project}.lock", "fsgg-v2-observation-{project}.lock")


def lock_paths(project_id: str, directory: str | None = None, legacy_directory: str | None = None) -> list[Path]:
    """The shared Project 3 writer lock paths, as tools/routine-delivery.py and the engine's V2WriterLock take them.

    Production uses $XDG_RUNTIME_DIR/fsgg and refuses when it is unset or relative; during the transition it also
    takes the installed engine's legacy locks at the literal /tmp path. Both directories are injectable for tests,
    so no test opens a production path (#5126).
    """
    production = directory is None
    if production:
        runtime = os.environ.get("XDG_RUNTIME_DIR")
        if not runtime or not os.path.isabs(runtime):
            raise Refused("the board writer lock needs an absolute XDG_RUNTIME_DIR; refusing to write without the shared lock")
        directory = os.path.join(runtime, "fsgg")
    # An injected lock directory also hosts the legacy locks unless they are injected separately: a test never
    # touches /tmp.
    legacy = legacy_directory if legacy_directory is not None else ("/tmp" if production else directory)
    return [Path(directory) / BOARD_WRITER_LOCK.format(project=project_id)] + [Path(legacy) / name.format(project=project_id) for name in LEGACY_LOCKS]


class WriterLock:
    """flock(2) LOCK_EX | LOCK_NB on every path, all or none. Busy is a refusal, never a wait or a queue."""

    def __init__(self, paths: list[Path]):
        self.paths, self.held = paths, []

    def __enter__(self):
        import fcntl
        taken: list[int] = []
        try:
            for path in self.paths:
                path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
                fd = os.open(path, os.O_RDWR | os.O_CREAT | os.O_NOFOLLOW | os.O_CLOEXEC, 0o600)
                taken.append(fd)
                fcntl.flock(fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        except BlockingIOError:
            for fd in taken:
                os.close(fd)
            raise LockBusy("the board writer lock is held by another writer") from None
        except OSError as exc:
            for fd in taken:
                os.close(fd)
            raise Refused(f"the board writer lock is unavailable ({type(exc).__name__})") from None
        self.held = taken
        return self

    def __exit__(self, *_):
        import fcntl
        for fd in reversed(self.held):
            try:
                fcntl.flock(fd, fcntl.LOCK_UN)
            finally:
                os.close(fd)
        self.held = []
        return False


class LockBusy(Exception):
    """Another writer holds the shared board lock: the run stops before this card, with nothing sent for it."""


def reader_admits_archived(version: str | None) -> bool:
    """True when the asserted installed reader is 0.114.1 or later, the first to report archived enrolled rows."""
    try:
        parts = tuple(int(x) for x in str(version).split("."))
    except ValueError:
        return False
    return len(parts) == 3 and parts >= (0, 114, 1)


class Custody:
    """Append-only private custody: one JSON line per step, flushed and fsynced before the next step."""

    def __init__(self, directory: Path):
        directory.mkdir(parents=True, exist_ok=True, mode=0o700)
        self.path = directory / "custody.jsonl"
        fd = os.open(self.path, os.O_WRONLY | os.O_CREAT | os.O_APPEND, 0o600)
        self.handle = os.fdopen(fd, "a", encoding="utf-8")

    def record(self, **fields: object) -> None:
        line = {"schema": CUSTODY_SCHEMA, "at": dt.datetime.now(dt.timezone.utc).isoformat(), **fields}
        self.handle.write(json.dumps(line, sort_keys=True) + "\n")
        self.handle.flush()
        os.fsync(self.handle.fileno())

    def close(self) -> None:
        self.handle.close()


def read_item(item_id: str) -> dict:
    node = graphql(ITEM, {"id": item_id}).get("node")
    if not isinstance(node, dict):
        raise Refused("the card is unreadable by id")
    content = node.get("content") if isinstance(node.get("content"), dict) else {}
    blocking = content.get("blocking")
    return {"itemId": node.get("id"), "archived": node.get("isArchived"), "projectId": (node.get("project") or {}).get("id"),
            "status": (node.get("status") or {}).get("name"), "contentId": content.get("id"),
            "state": content.get("state"), "closedAt": content.get("closedAt"), "kind": content.get("__typename"),
            "blocking": blocking}


def preimage_matches(observed: dict, planned: dict, project_id: str) -> str | None:
    """None when the live card is exactly the planned Done card; otherwise the fixed reason it is not."""
    if observed.get("itemId") != planned["itemId"] or observed.get("projectId") != project_id:
        return "card or project identity differs"
    if observed.get("archived") is not False:
        return "already archived or archive state unreadable"
    if observed.get("status") != "Done":
        return "Status is no longer Done"
    if (observed.get("contentId"), observed.get("state"), observed.get("closedAt")) != (planned["contentId"], planned["state"], planned["closedAt"]):
        return "content identity, state or closedAt changed"
    # Blocker recheck at the preimage: a blocker link added after the run's live read is caught here. An issue that
    # still blocks an OPEN issue stays visible; a truncated or unreadable list is never archived.
    if observed.get("kind") == "Issue":
        blocking = observed.get("blocking")
        listed = blocking.get("nodes") if isinstance(blocking, dict) else None
        if not isinstance(listed, list) or blocking.get("totalCount") != len(listed):
            return "the blocking list is truncated or unreadable"
        if any(not isinstance(b, dict) or b.get("state") != "CLOSED" for b in listed):
            return "it still blocks an open issue"
    return None


def archive_one(row: dict, project_id: str, custody: Custody, enrolled: set[str], locks: list[Path]) -> str:
    """Under the shared writer lock: preimage re-read, one send, readback. Returns 'archived' or 'skipped'; raises
    Uncertain or LockBusy to stop the run."""
    with WriterLock(locks):
        return _archive_locked(row, project_id, custody, enrolled)


def _archive_locked(row: dict, project_id: str, custody: Custody, enrolled: set[str]) -> str:
    ref = {"itemId": row["itemId"], "contentId": row["contentId"], "repo": row["repo"], "number": row["number"],
           "closedAt": row["closedAt"], "enrolled": row["contentId"] in enrolled}
    try:
        reason = preimage_matches(read_item(row["itemId"]), row, project_id)
    except Refused as exc:
        reason = f"preimage unreadable ({exc})"
    if reason:
        custody.record(step="skipped", reason=reason, **ref)
        return "skipped"
    custody.record(step="intent", **ref)
    try:
        payload = graphql(ARCHIVE, {"project": project_id, "item": row["itemId"]})
        sent = ((payload.get("archiveProjectV2Item") or {}).get("item") or {})
    except Refused as exc:
        custody.record(step="uncertain", reason=f"send failed or unreadable ({exc}); not resent", **ref)
        raise Uncertain(row["itemId"]) from None
    try:
        after = read_item(row["itemId"])
    except Refused as exc:
        custody.record(step="uncertain", reason=f"readback unreadable ({exc}); not resent", sentArchived=sent.get("isArchived"), **ref)
        raise Uncertain(row["itemId"]) from None
    if after.get("archived") is not True or after.get("itemId") != row["itemId"]:
        custody.record(step="uncertain", reason="readback is not archived; not resent", sentArchived=sent.get("isArchived"), **ref)
        raise Uncertain(row["itemId"])
    custody.record(step="archived", **ref)
    return "archived"


def ref_of(row: dict) -> str:
    return f"{row.get('repo')}#{row.get('number')}"


def main(argv: list[str] | None = None) -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--owner", default="FS-GG")
    ap.add_argument("--project", type=int, default=3, help="organization project number (default 3, Coordination V2)")
    ap.add_argument("--keep", type=int, default=KEEP, help=f"newest Done cards kept visible (default {KEEP})")
    ap.add_argument("--execute", action="store_true", help="archive; without it this is a dry run")
    ap.add_argument("--expect-project-id", default=None, help="required with --execute: the exact ProjectV2 node id")
    ap.add_argument("--max-archive", type=int, default=0, help="required with --execute: at most this many sends")
    ap.add_argument("--enrollment-binding", default=None, help="private runtime binding; required with --execute")
    ap.add_argument("--custody-dir", default=None, help="private custody directory (default under ~/.local/share/fs-gg-private)")
    ap.add_argument("--reader-version", default=None,
                    help="the installed board-v2 reader version; an enrolled card is archived only when it is >= 0.114.1")
    ap.add_argument("--lock-dir", default=None, help=argparse.SUPPRESS)
    ap.add_argument("--legacy-lock-dir", default=None, help=argparse.SUPPRESS)
    ap.add_argument("--counts-only", action="store_true",
                    help="print counts only, never a repository or number (for public logs; refused with --execute)")
    args = ap.parse_args(argv)
    if args.keep < 0:
        ap.error("--keep must not be negative")
    if args.execute and (not args.expect_project_id or args.max_archive < 1 or not args.enrollment_binding):
        ap.error("--execute requires --expect-project-id, a positive --max-archive and --enrollment-binding")
    if args.execute and args.counts_only:
        ap.error("--counts-only is a public dry-run report and cannot be combined with --execute")

    try:
        enrolled = enrolled_nodes(args.enrollment_binding)
        project, rows = scan(args.owner, args.project)
        protected, live_total = live_blockers(args.owner, args.project)
        if len(rows) + live_total != project.get("totalCount"):
            raise Refused("Done and live counts do not add up to the board total: the board changed while it was read")
    except Refused as exc:
        print(f"coord-board-archive: REFUSED, nothing archived: {exc}", file=sys.stderr)
        return 1
    if args.expect_project_id and project.get("id") != args.expect_project_id:
        print("coord-board-archive: REFUSED, nothing archived: the project id differs from --expect-project-id", file=sys.stderr)
        return 1

    archive, kept, unranked = plan(rows, args.keep, protected)
    print(f"board: {project.get('totalCount')} item(s) (limit {BOARD_LIMIT}); Done: {len(rows)}; "
          f"ranked {len(archive) + len(kept)}, keep newest {len(kept)}, plan to archive {len(archive)}")
    for row, why in unranked:
        print(f"  KEEP {'<card>' if args.counts_only else ref_of(row)}: {why}")
    if enrolled is None:
        print("  enrolled V2 rows: Unknown (no --enrollment-binding supplied)")
    else:
        print(f"  enrolled V2 rows among the planned: {sum(1 for row in archive if row['contentId'] in enrolled)}")
    if not args.counts_only:
        for row in archive:
            print(f"  ARCHIVE {ref_of(row)} closed {row['closedAt']}")
    if not archive:
        print("nothing to archive.")
        return 0
    if not args.execute:
        print("DRY RUN: nothing archived.")
        return 0

    batch = archive[: args.max_archive]
    enrolled_planned = [row for row in batch if row["contentId"] in (enrolled or set())]
    if enrolled_planned and not reader_admits_archived(args.reader_version):
        print(f"coord-board-archive: REFUSED, nothing archived: {len(enrolled_planned)} planned card(s) are enrolled V2 rows; "
              "assert --reader-version 0.114.1 or later, the first reader that reports an archived enrolled row", file=sys.stderr)
        return 1
    try:
        locks = lock_paths(str(project["id"]), args.lock_dir, args.legacy_lock_dir)
    except Refused as exc:
        print(f"coord-board-archive: REFUSED, nothing archived: {exc}", file=sys.stderr)
        return 1
    stamp = dt.datetime.now(dt.timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    directory = Path(args.custody_dir) if args.custody_dir else Path.home() / ".local/share/fs-gg-private/board-done-retention" / stamp
    custody = Custody(directory)
    custody.record(step="run", project=project.get("id"), board=project.get("totalCount"), done=len(rows),
                   keep=args.keep, planned=len(archive), maxArchive=args.max_archive)
    counts = {"archived": 0, "skipped": 0}
    try:
        for row in batch:
            counts[archive_one(row, str(project["id"]), custody, enrolled or set(), locks)] += 1
    except (LockBusy, Refused) as exc:
        custody.record(step="stopped", reason=f"{exc}; nothing sent for the remaining cards", **counts)
        print(f"coord-board-archive: STOPPED, lock refused ({exc}); custody {custody.path}", file=sys.stderr)
        custody.close()
        return 4
    except Uncertain as exc:
        custody.record(step="stopped", reason="uncertain send; no resend", itemId=str(exc), **counts)
        print(f"coord-board-archive: STOPPED on an uncertain send ({exc}); custody {custody.path}", file=sys.stderr)
        custody.close()
        return 3
    custody.record(step="finished", **counts)
    custody.close()
    print(f"archived {counts['archived']}, skipped {counts['skipped']}; custody {custody.path}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
