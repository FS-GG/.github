#!/usr/bin/env python3
"""Archive terminal rows off the Coordination board, so the scan stops paying for the dead.

WHY THIS EXISTS
===============

On 2026-08-12 the board held **2,206 rows, of which 2,172 were `Done` and closed — 98.8%**. Every
truth read (`reconcile`, `lint`, `ready`, `who`, `batch`, `driver --events`, and `check-board`'s whole
pass) scans the entire `items` connection to find ~30 live rows. That is 23 pages to answer a question
about 26 of them, and it grows forever.

This is not a performance nicety. It is the measured cause of a recurring class of incident, and the
repo has repaired the SYMPTOM four times without touching it:

  * `.github#2313` — "a reconciling scan pays **1658 sequential REST body reads**" (that number IS the
    dead population), exhausting REST entirely: `0/5000 remaining`.
  * `.github#2314` — "Fix reconcile scan amplification".
  * `.github#2352` — reconcile "fires a full board scan on every comment, exhausts the fleet's REST
    budget, and blocks merges".
  * `.github#2356` — still open: the pre-flight reserve "is too small for **this board's actual scan
    cost**".

Each made the scan cheaper. None made the board smaller, so the cause survived every repair and the
budget kept dying. The one-off sweep on 2026-08-12 took the board 2,206 -> 36 and a full scan 23 pages
-> 1; this script is what stops it regrowing.

REVERSIBLE, AND MEASURED RATHER THAN ASSUMED
============================================

Archiving REMOVES a row from `items(first:)` — measured directly before relying on it: `totalCount`
2206 -> 2205 with one row archived, and back to 2206 once unarchived. So this is fully reversible,
`unarchiveProjectV2Item` puts a row back, and `--manifest` records every id it touched for exactly
that purpose.

Nothing of record is lost. The board is a PROJECTION; the issue is truth (`/check-board`'s own first
line), and `Class` authority is the item's own `Class:` body line, never the column. An archived row's
board fields are derived data, recomputable, and the row itself is one mutation from returning.

THE GUARDS, ALL FAIL-CLOSED
===========================

  1. **Terminal on BOTH axes.** Board `Status` must be `Done` AND the issue state must be `CLOSED`.
     Either alone is insufficient — a `Done` column over an open issue is exactly the drift `lint`
     reports, and archiving it would hide the drift instead of surfacing it.
  2. **Aged past the retention window.** Closed at least `--retention-days` ago (default 30), so a
     just-finished item stays visible to reporting, to a re-open, and to a driver still holding it in
     a cursor.
  3. **Never a live row's blocker.** A row named in any non-`Done` row's `Blocked by` is protected. An
     off-board blocker ref renders `BlockerUnknown` (`Protocol.fs:406`), which would silently turn
     "closed, so this is clear" into "cannot tell" for a live item.
  4. **Unreadable is never archived.** A row whose status, state, number or repo could not be read is
     SKIPPED. "I could not look" is not "I looked and it is terminal" (`.github#266`).

WHY NOT GITHUB'S BUILT-IN AUTO-ARCHIVE WORKFLOW
===============================================

Projects v2 ships one, but its configuration is UI-only and not manageable through the API, so it
cannot be reviewed, tested, guarded, or explained in the repository that depends on it. Guards 3 and 4
in particular have no expression in it at all. A rule this load-bearing belongs in the repo.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import json
import re
import subprocess
import sys

PROJECT_ENV = "FSGG_COORD_PROJECT_ID"
BATCH = 50

# THE FOUR CANONICAL FORMS, mirroring `Blockers.canonToken` (src/FS.GG.Coord.Core/Blockers.fs:126-149)
# rather than inventing a second grammar beside it. Round-1 review finding on PR #2421: the first
# version understood two of the four, and — worse — resolved a bare `#n` against ONE GLOBAL default
# repo. The engine resolves it against the REFERRING ROW'S OWN repo, and this board is genuinely
# multi-repo, so on `FS-GG/FS.GG.SDD#9 blocked by #8` the global default resolved to
# `FS-GG/.github#8` and left the real `FS-GG/FS.GG.SDD#8` unprotected and archivable.
_URL = re.compile(r"https?://github\.com/([\w.-]+)/([\w.-]+)/issues/(\d+)")
_FULL = re.compile(r"(?<![\w./-])([\w.-]+)/([\w.-]+)#(\d+)")
_REPO_N = re.compile(r"(?<![\w./-])([\w.-]+)#(\d+)")
_BARE = re.compile(r"(?<![\w./-])#(\d+)")


def protected_refs(rows: list[dict]) -> tuple[set[tuple[str, int]], set[int]]:
    """Every ref a LIVE row declares as a blocker, in every form the engine accepts.

    Returns `(qualified, unresolvable_numbers)`. The second set is the FAIL-CLOSED half: when a live
    row declares a bare `#n` but its own repo could not be read, the ref cannot be resolved to one
    repository — so the number is protected in EVERY repository rather than guessed into one. Guessing
    is exactly the defect this replaces.

    Computed from non-`Done` rows only: a blocker edge on an already-finished row cannot strand
    anything, and treating it as protective would pin the whole historical graph on-board forever.
    """
    qualified: set[tuple[str, int]] = set()
    unresolvable: set[int] = set()

    for row in rows:
        if row.get("status") == "Done":
            continue
        text = row.get("blockedBy") or ""
        if not text:
            continue

        # The referring row's OWN owner/repo is the default, exactly as `canonToken` uses the blocked
        # item's. `None` when unreadable, which routes to `unresolvable` below rather than to a guess.
        own = row.get("repo")
        own_owner = own.split("/")[0] if own and "/" in own else None

        consumed: list[tuple[int, int]] = []

        def take(match: re.Match) -> bool:
            """Longer forms win: `owner/repo#n` must not be re-read as `repo#n` or `#n`."""
            for start, end in consumed:
                if match.start() >= start and match.end() <= end:
                    return False
            consumed.append((match.start(), match.end()))
            return True

        for m in _URL.finditer(text):
            if take(m):
                qualified.add((f"{m.group(1)}/{m.group(2)}", int(m.group(3))))
        for m in _FULL.finditer(text):
            if take(m):
                qualified.add((f"{m.group(1)}/{m.group(2)}", int(m.group(3))))
        for m in _REPO_N.finditer(text):
            if take(m):
                # `repo#n` — the OWNER defaults to the referring row's owner.
                if own_owner:
                    qualified.add((f"{own_owner}/{m.group(1)}", int(m.group(2))))
                else:
                    unresolvable.add(int(m.group(2)))
        for m in _BARE.finditer(text):
            if take(m):
                # `#n` — adopts the referring row's OWN owner/repo.
                if own:
                    qualified.add((own, int(m.group(1))))
                else:
                    unresolvable.add(int(m.group(1)))

    return qualified, unresolvable


def plan(rows: list[dict], now: _dt.datetime, retention_days: int, default_repo: str | None = None
         ) -> tuple[list[dict], list[tuple[dict, str]]]:
    """Split the board into (archive, skipped-with-reason). PURE — no IO, so the guards are testable.

    The reason string is part of the contract: a row that is not archived must say why, or a silent
    skip becomes indistinguishable from a silent archive.
    """
    protected, unresolvable = protected_refs(rows)
    cutoff = now - _dt.timedelta(days=retention_days)

    archive: list[dict] = []
    skipped: list[tuple[dict, str]] = []

    for row in rows:
        status, state = row.get("status"), row.get("state")
        number, repo = row.get("number"), row.get("repo")

        if number is None or repo is None or status is None or state is None:
            skipped.append((row, "unreadable row — never archived (#266)"))
            continue
        if status != "Done":
            skipped.append((row, f"status is {status}, not Done"))
            continue
        if state != "CLOSED":
            skipped.append((row, f"Done but the issue state is {state}"))
            continue

        closed_at = row.get("closedAt")
        if not closed_at:
            skipped.append((row, "closed-at is unreadable — never archived (#266)"))
            continue
        try:
            closed = _dt.datetime.fromisoformat(closed_at.replace("Z", "+00:00"))
        except ValueError:
            skipped.append((row, f"closed-at {closed_at!r} did not parse — never archived"))
            continue
        if closed > cutoff:
            skipped.append((row, f"closed {closed:%Y-%m-%d}, inside the {retention_days}-day window"))
            continue

        if (repo, number) in protected:
            skipped.append((row, "named by a live row's `Blocked by`"))
            continue
        if number in unresolvable:
            # A live row named `#n` but its own repo was unreadable, so the ref cannot be pinned to one
            # repository. Protect the number everywhere rather than guess a repo — guessing is the
            # round-1 defect, and over-protecting only leaves a row on the board one more day.
            skipped.append((row, "a live row names `#%d` but its own repo is unreadable — protected "
                                 "in every repo rather than guessed" % number))
            continue

        archive.append(row)

    return archive, skipped


# --------------------------------------------------------------------------------------------------
# IO below this line.
# --------------------------------------------------------------------------------------------------

SCAN = """
query($project: ID!, $cursor: String) {
  node(id: $project) { ... on ProjectV2 {
    items(first: 100, after: $cursor) {
      pageInfo { hasNextPage endCursor }
      nodes {
        id
        status: fieldValueByName(name: "Status") { ... on ProjectV2ItemFieldSingleSelectValue { name } }
        blockedBy: fieldValueByName(name: "Blocked by") { ... on ProjectV2ItemFieldTextValue { text } }
        content {
          __typename
          ... on Issue { number state closedAt repository { nameWithOwner } }
          ... on PullRequest { number state closedAt repository { nameWithOwner } }
        }
      }
    }
  } }
  rateLimit { cost remaining }
}"""


def _gql(query: str, **variables) -> dict:
    cmd = ["gh", "api", "graphql", "-f", f"query={query}"]
    for key, value in variables.items():
        cmd += ["-F", f"{key}={value}"]
    done = subprocess.run(cmd, capture_output=True, text=True)
    if done.returncode != 0:
        raise RuntimeError(f"GraphQL call failed: {done.stderr.strip()[:500]}")
    return json.loads(done.stdout)


def scan(project: str) -> tuple[list[dict], int, int]:
    rows, cursor, pages, spent = [], None, 0, 0
    while True:
        data = _gql(SCAN, project=project, **({"cursor": cursor} if cursor else {}))
        conn = data["data"]["node"]["items"]
        spent += data["data"]["rateLimit"]["cost"]
        pages += 1
        for node in conn["nodes"]:
            content = node.get("content") or {}
            rows.append({
                "itemId": node["id"],
                "status": (node.get("status") or {}).get("name"),
                "blockedBy": (node.get("blockedBy") or {}).get("text"),
                "number": content.get("number"),
                "state": content.get("state"),
                "closedAt": content.get("closedAt"),
                "repo": (content.get("repository") or {}).get("nameWithOwner"),
            })
        if not conn["pageInfo"]["hasNextPage"]:
            break
        cursor = conn["pageInfo"]["endCursor"]
    return rows, pages, spent


def archive_batch(project: str, chunk: list[dict]) -> None:
    """One aliased document per chunk.

    A ProjectV2 mutation returns ~1 node, so GitHub bills it the ONE-POINT FLOOR whatever else is in
    the document — which makes cost track REQUEST COUNT, not row count. Aliased, the 2026-08-12 sweep
    archived 2,170 rows in 44 requests instead of 2,170. This is `.github#448`'s lever, applied here.
    """
    decls = ", ".join(["$p: ID!"] + [f"$i{j}: ID!" for j in range(len(chunk))])
    body = "\n".join(
        f"  a{j}: archiveProjectV2Item(input: {{projectId: $p, itemId: $i{j}}}) {{ item {{ id }} }}"
        for j in range(len(chunk)))
    variables = {"p": project}
    for j, row in enumerate(chunk):
        variables[f"i{j}"] = row["itemId"]
    _gql(f"mutation({decls}) {{\n{body}\n}}", **variables)


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--project", default=None, help=f"ProjectV2 node id (or ${PROJECT_ENV})")
    ap.add_argument("--retention-days", type=int, default=30)
    ap.add_argument("--default-repo", default="FS-GG/.github",
                    help="repo a bare `#123` blocker ref resolves against")
    ap.add_argument("--execute", action="store_true", help="archive; without it this is a dry run")
    ap.add_argument("--manifest", default=None, help="append every archived id here, for reversal")
    ap.add_argument("--min-graphql", type=int, default=500,
                    help="defer entirely below this many GraphQL points")
    args = ap.parse_args()

    import os
    project = args.project or os.environ.get(PROJECT_ENV)
    if not project:
        print(f"coord-board-archive: no project id (--project or ${PROJECT_ENV})", file=sys.stderr)
        return 2

    # Pre-flight. A sweep that dies half way is not harmful — archiving is idempotent and the manifest
    # records what landed — but starting one on a budget that cannot finish it spends points to
    # accomplish nothing, and this job is never the urgent work on the board.
    try:
        remaining = int(_gql("query { rateLimit { remaining cost } }")["data"]["rateLimit"]["remaining"])
    except (RuntimeError, KeyError, ValueError) as exc:
        print(f"coord-board-archive: could not read the GraphQL meter ({exc}); deferring rather than "
              f"scanning blind", file=sys.stderr)
        return 0
    if remaining < args.min_graphql:
        print(f"coord-board-archive: deferring — {remaining} GraphQL points remain, below the "
              f"{args.min_graphql} reserve. The board is not harmed by waiting.")
        return 0

    rows, pages, spent = scan(project)
    now = _dt.datetime.now(_dt.timezone.utc)
    archive, skipped = plan(rows, now, args.retention_days, args.default_repo)

    print(f"scanned {len(rows)} row(s) over {pages} page(s) for {spent} GraphQL point(s)")
    print(f"plan: archive {len(archive)}, keep {len(rows) - len(archive)} "
          f"(retention {args.retention_days}d)")

    # Every non-routine skip is named. A silent skip and a silent archive must not look alike.
    for row, why in skipped:
        if not why.startswith("status is"):
            print(f"  KEEP {row.get('repo')}#{row.get('number')} — {why}")

    if not archive:
        print("nothing to archive.")
        return 0

    if not args.execute:
        print(f"\nDRY RUN — nothing archived. Pages would go {pages} -> "
              f"{max(1, -(-(len(rows) - len(archive)) // 100))}.")
        return 0

    # The manifest is written AFTER each batch lands, never before the loop. Round-1 review finding on
    # PR #2421: writing it up-front recorded rows as archived that a mid-sweep failure then never
    # touched, so the file claimed more than had happened. Board recovery was always fine — archiving is
    # idempotent — but a REVERSAL manifest that overstates is worse than none, because it is consulted
    # precisely when something has already gone wrong.
    archived, requests = 0, 0
    manifest_fh = open(args.manifest, "a", encoding="utf-8") if args.manifest else None
    try:
        for i in range(0, len(archive), BATCH):
            chunk = archive[i:i + BATCH]
            try:
                archive_batch(project, chunk)
            except RuntimeError as exc:
                print(f"coord-board-archive: batch at {i} failed: {exc}", file=sys.stderr)
                print(f"coord-board-archive: stopped after {archived}; archiving is idempotent, so the "
                      f"next run resumes, and the manifest names exactly what landed.", file=sys.stderr)
                return 1

            archived += len(chunk)
            requests += 1
            if manifest_fh:
                stamp = _dt.datetime.now(_dt.timezone.utc).isoformat()
                for row in chunk:
                    manifest_fh.write(json.dumps({**row, "archivedAt": stamp}) + "\n")
                manifest_fh.flush()
    finally:
        if manifest_fh:
            manifest_fh.close()

    if args.manifest:
        print(f"manifest: {args.manifest} ({archived} id(s), reversible with unarchiveProjectV2Item)")
    print(f"archived {archived} row(s) in {requests} aliased request(s) "
          f"(vs {archived} unaliased — .github#448's lever)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
