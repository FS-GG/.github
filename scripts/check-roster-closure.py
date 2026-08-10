#!/usr/bin/env python3
"""Gate the CLOSED-WORLD assumption behind registry/repos.yml (.github#269, epic #266 instance (c)).

Every org-level fabric — labels, the coordination kit, propagate, repos-audit — iterates the repos
listed in `registry/repos.yml`. That roster is a closed-world assumption, and nothing asserted the
world was actually closed. `coordination-sync --check --repo <full>` exits 0 for a repo that does
not `receives: coordination-kit`, which is right for a repo deliberately outside the fabric and
catastrophic for one accidentally outside it: the two are indistinguishable to every gate we have.

FS.GG.Audio proved it. The org's seventh repo was transferred in, registered in
`registry/dependencies.yml` as a contract owner, and cut 0.1.0-preview.1 — while never appearing in
`repos.yml`. No fabric iterated it; no gate could say so. `repos-audit.yml` was no help either: it
audits repos IN the roster, so a repo absent from the roster is absent from the audit.

This script asserts the roster is closed, from three directions:

  A. REGISTRY closure (offline, no token).  Every repo in `dependencies.yml`'s `repos:` block has a
     row in `repos.yml`. Audio sat in one and not the other for weeks; this is the cheap, strictly-
     implied invariant that would have caught it.

  B. ORG closure (one REST call).  Every repo that actually exists in the GitHub org has a row in
     `repos.yml`, or an explicit row in its `outside-fabric:` opt-out list. This compares against
     REALITY rather than against a second record of it.

     ITS SUBJECT IS THE ORG'S OWN REPOSITORIES, and since `.github#2245` the roster can name a repo
     the org does NOT own (`.github#2206` rosters `EHotwagner/S.I.R.`). `GET /orgs/{org}/repos`
     cannot return such a repo, so an owner-blind comparison would report it as an unreachable
     subject on every run, forever.

     AND THE COST OF THAT IS FAIL-OPEN, NOT A RED CHECK — which is the opposite of what it looks
     like. `main()` treats ANY no-verdict from the visibility legs as a reason to skip
     `org_closure_findings` entirely (the `if nv: … else:` below), and `coherence.yml:96-100` maps
     exit 3 to `::warning::` + `exit 0`. So the permanent no-verdict would be a permanently GREEN
     required check with the whole of direction B silently switched off: the FS.GG.Audio shape — a
     repo live in the org and rostered nowhere — would stop being reported and nothing would say so.
     Measured, not reasoned: owner-blind, with the S.I.R. row and a listing containing an unrostered
     `FS-GG/FS.GG.Audio`, the script exits 3 and that finding does not appear
     (`tests/roster-closure/run.sh`, "an unrostered ORG repo is still a finding alongside a
     user-owned row", which reds under exactly that mutation).

     Direction B therefore compares only the org-owned rows, and every row it does not grade is
     PRINTED with the reason — never dropped, and never counted as verified.

  C. BOARD closure (one `gh api graphql` call).  Every repository naming a non-`Done` row on the
     FS-GG Coordination Projects v2 board has a row in `repos.yml`, or an explicit `outside-fabric:`
     entry (`.github#2206`). This is the direction that actually found the defect this script was
     extended for: two user-owned repos (`EHotwagner/rogue3`, `EHotwagner/S.I.R.`) held live board
     rows while absent from `repos.yml` and from `outside-fabric:`, and neither direction A nor B
     could see it — A is offline and knows nothing about the board, and B enumerates the GitHub
     **organization**, which by construction can never return a repository a **user** owns.

     ITS SUBJECT IS THE BOARD'S OWN ITEM SET, READ DIRECTLY. Each board item already carries its own
     `owner`/`repo` (the issue's or PR's repository), so direction C needs no org-membership proof at
     all — unlike direction B, it applies no `_org_owned` filter, and a user-owned board row is graded
     exactly like an org-owned one. This is deliberate: direction B's blind spot is owner enumeration,
     and the fix is a direction that never enumerates an owner in the first place.

     "Every repository" here means every repository with at least one **schedulable** row — status not
     `Done` (case-insensitive; a missing/unset status counts as non-Done; #2206's maintainer decision).
     A row whose status is `Done` costs nothing to leave unrostered: its closed history is a non-event,
     and a *future* schedulable row on that repository is what re-triggers the check. This is why
     `EHotwagner/rogue3` needs no roster action from this script — its only board row is `Done` — while
     `EHotwagner/S.I.R.` (rostered `role: non-participant` per `.github#2245`) is graded closed.

     The disposition vocabulary is NOT new. A schedulable board row is closed by either an existing
     `repos.yml` row (any `role:`, including `non-participant` + its mandatory `reason:`) or an
     existing `outside-fabric:` entry (its own mandatory `reason:`, `repos.sh validate`-enforced). Both
     are already falsifiable — a row with no `reason:` does not validate — so no third opt-out list is
     introduced here; the falsifiability #2206 asked for was already shipped by `.github#2245`.

Neither reports a vacuous green — but "could not look" and "looked, and the world is open" are
DIFFERENT answers and must not share an exit code (#1154). A human who sees a red treats it as a
roster to fix; on a transient outage or a too-narrow token that is the wrong action, so the two are
split three ways:

  * exit 0 — the world is closed: the roster covers every repo the listing (proven complete) holds,
    including every schedulable board row.
  * exit 1 — a VIOLATION the roster must fix: a `dependencies.yml` participant with no roster row;
    a repo live in the org and rostered nowhere (the FS.GG.Audio shape); a stale or contradictory
    `outside-fabric:` exemption; a schedulable board row naming a repo with no roster row and no
    `outside-fabric:` entry (the `.github#2206` shape).
  * exit 3 — NO VERDICT, the gate could not look: an unreachable/errored/empty org listing; a
    rostered repo missing from the listing; an unreachable/errored/empty/malformed board read; or —
    the #1154 gap — a token that cannot prove it sees the WHOLE org. Org closure needs the listing to
    be at least as large as the org's own repo total (`public_repos + total_private_repos`); a
    run-scoped token cannot read the private count, so it can prove the PUBLIC world closed but not
    the private one, and says so rather than guessing 0. A definite violation outranks a no-verdict
    when both are present.

Nothing here auto-exempts archived or forked repos: "archived" would be a one-click hole in the
gate. Exemption is always an explicit, reviewed row.

Pure-stdlib + PyYAML (already a coherence-gate dependency) for directions A and B. Direction C shells
out to the `gh` CLI's `api graphql` (already a repo-wide dependency — `scripts/project-field-options`
uses the identical subprocess pattern) rather than adding an HTTP/GraphQL library; there is no
REST enumeration of Projects v2 item content, so unlike A/B this direction cannot be `urllib`-only.

Usage:
  scripts/check-roster-closure.py [--roster registry/repos.yml] [--deps registry/dependencies.yml]
                                  [--org FS-GG] [--org-repos-json FILE] [--org-meta-json FILE]
                                  [--skip-org]
                                  [--board-owner FS-GG] [--board-title Coordination]
                                  [--board-json FILE] [--skip-board]
"""
from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
import urllib.error
import urllib.request

import yaml

API = "https://api.github.com"

DEFAULT_BOARD_OWNER = "FS-GG"
DEFAULT_BOARD_TITLE = "Coordination"

# `content` covers both possible board-item shapes: an Issue (the normal row) and a PullRequest (a
# board can carry one directly). A DraftIssue — the third possible `content` — has no `repository`
# and is intentionally NOT queried here: it names no repository, so it is not this direction's
# subject and is skipped rather than treated as an error.
_BOARD_ITEMS_QUERY = r"""
query RosterClosureBoardItems($owner:String!,$number:Int!,$after:String){
  organization(login:$owner){projectV2(number:$number){
    id number title
    items(first:100,after:$after){
      totalCount
      pageInfo{hasNextPage endCursor}
      nodes{
        id
        status: fieldValueByName(name:"Status"){
          ... on ProjectV2ItemFieldSingleSelectValue{name}
        }
        content{
          __typename
          ... on Issue{number repository{owner{login} name}}
          ... on PullRequest{number repository{owner{login} name}}
        }
      }
    }
  }}
}
"""

_BOARD_PROJECT_LOOKUP_QUERY = r"""
query RosterClosureBoardProject($owner:String!){
  organization(login:$owner){projectsV2(first:50){nodes{number title}}}
}
"""


def _full(owner: str, name: str) -> str:
    """Canonicalize a repo reference to `owner/name`.

    `dependencies.yml` spells the shared repo two ways — key `github`, name `FS-GG/.github` — while
    `repos.yml` gives id `.github`, full `FS-GG/.github`. Comparing on ids would report a phantom
    drift, so both sides are normalized to the full name, which is the thing GitHub agrees on.
    """
    name = str(name).strip()
    return name if "/" in name else f"{owner}/{name}"


def _owner(full: str) -> str:
    """The owner login of an `owner/name` reference, or `""` when it has none.

    The roster used to be `FS-GG`-only by construction — `repos.sh validate` refused any other
    `full:` — so every reader could treat "rostered" and "owned by this org" as the same set.
    `.github#2245` ended that: `registry/repos.yml` now expresses a rostered repository the
    organization does NOT own, because `.github#2206`'s maintainer decision rosters
    `EHotwagner/S.I.R.` A reader that keeps the old assumption does not merely miss it — direction B
    below asks GitHub for the org's repositories, which by construction can never return a repo owned
    by somebody else, so an owner-blind comparison turns every such row into a permanent no-verdict,
    and a permanent no-verdict here silently disables the whole of direction B (see the module
    docstring: the caller skips the findings pass, and CI maps exit 3 to a warning). Fail-open.
    """
    return str(full).split("/", 1)[0] if "/" in str(full) else ""


def _org_owned(fulls: set[str], org: str) -> set[str]:
    """The subset of `fulls` that org `org` actually owns; the only subset direction B may grade.

    Case-insensitive, because GitHub resolves owner logins case-insensitively and the roster is
    hand-written — the same rule `repos-audit` applies to `owner/name` when it lowercases its
    roster boundary (#1556). Treating `fs-gg/X` as foreign would resurrect the no-verdict this
    function exists to prevent.
    """
    want = org.lower()
    return {f for f in fulls if _owner(f).lower() == want}


def _fetch_org_repos(org: str) -> list[str]:
    """Every repo in the org, following pagination. Raises on any non-200 or malformed page.

    Anonymous requests see public repos; a token (GITHUB_TOKEN) is used when present. Either way a
    private repo the caller cannot see is invisible — which is exactly why the caller cross-checks
    that every rostered repo came back (see `org_visibility_noverdicts`). Silence is not evidence.
    """
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
    url = f"{API}/orgs/{org}/repos?per_page=100&type=all"
    out: list[str] = []
    while url:
        req = urllib.request.Request(url, headers={
            "Accept": "application/vnd.github+json",
            "X-GitHub-Api-Version": "2022-11-28",
            "User-Agent": "fsgg-check-roster-closure",
            **({"Authorization": f"Bearer {token}"} if token else {}),
        })
        with urllib.request.urlopen(req, timeout=30) as resp:
            page = json.loads(resp.read().decode("utf-8"))
            if not isinstance(page, list):
                raise ValueError(f"expected a JSON array from {url}, got {type(page).__name__}")
            out.extend(str(r["full_name"]) for r in page)
            url = _next_link(resp.headers.get("Link", ""))
    return out


def _next_link(link_header: str) -> str:
    """The `rel="next"` URL from a GitHub Link header, or "" when the page is the last one.

    Every parameter segment is scanned, not just the first: `<url>; type="x"; rel="next"` is legal
    per RFC 8288. A parser that silently stopped paginating would truncate the org listing and hand
    the closure check a partial world — precisely the fails-open shape this file exists to prevent.
    """
    for part in link_header.split(","):
        seg = [s.strip() for s in part.split(";")]
        if any(s.replace(" ", "") in ('rel="next"', "rel=next") for s in seg[1:]):
            return seg[0].lstrip("<").rstrip(">")
    return ""


def _fetch_org_meta(org: str) -> tuple[int, int | None]:
    """`(public_repos, total_private_repos)` from GET /orgs/{org}; the private count may be None.

    `public_repos` is on the org's public profile, so every caller — anonymous included — gets it.
    `total_private_repos` is returned ONLY to a token with organization visibility (an org
    owner/member, or an app with org-administration read); a run-scoped `GITHUB_TOKEN` sees it
    OMITTED, which is exactly why its absence is a no-verdict and not a zero (see
    `org_visibility_noverdicts`). Raises on any non-200 or malformed body, like `_fetch_org_repos`.
    """
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN") or ""
    req = urllib.request.Request(f"{API}/orgs/{org}", headers={
        "Accept": "application/vnd.github+json",
        "X-GitHub-Api-Version": "2022-11-28",
        "User-Agent": "fsgg-check-roster-closure",
        **({"Authorization": f"Bearer {token}"} if token else {}),
    })
    with urllib.request.urlopen(req, timeout=30) as resp:
        obj = json.loads(resp.read().decode("utf-8"))
    if not isinstance(obj, dict):
        raise ValueError(f"expected a JSON object from GET /orgs/{org}, got {type(obj).__name__}")
    public = int(obj["public_repos"])  # KeyError -> caught by the caller as an unreadable subject
    private = obj.get("total_private_repos")
    return public, (int(private) if private is not None else None)


def _gh_graphql(query: str, variables: dict) -> dict:
    """Run one GraphQL query through the `gh` CLI and return its `data`. Raises on any failure.

    Mirrors `scripts/project-field-options`'s `graphql()` helper exactly (same subprocess shape,
    same error taxonomy) rather than importing it: that script is a standalone tool with no public
    module surface, and duplicating four lines here is cheaper than inventing one. `gh` is already a
    load-bearing dependency of this repo's Projects v2 tooling.
    """
    payload = json.dumps({"query": query, "variables": variables}, sort_keys=True)
    try:
        proc = subprocess.run(
            ["gh", "api", "graphql", "--input", "-"],
            input=payload.encode("utf-8"),
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            timeout=60,
            check=False,
        )
    except (OSError, subprocess.SubprocessError) as exc:
        raise RuntimeError(f"could not run `gh api graphql`: {exc}") from exc
    if proc.returncode:
        raise RuntimeError(f"`gh api graphql` failed ({proc.returncode}): {proc.stderr.decode(errors='replace').strip()}")
    try:
        reply = json.loads(proc.stdout)
    except json.JSONDecodeError as exc:
        raise RuntimeError(f"`gh api graphql` returned invalid JSON: {exc}") from exc
    if not isinstance(reply, dict):
        raise RuntimeError(f"`gh api graphql` returned a {type(reply).__name__}, expected an object")
    if reply.get("errors"):
        raise RuntimeError("GraphQL refused the query: " + json.dumps(reply["errors"], sort_keys=True))
    if "data" not in reply or not isinstance(reply["data"], dict):
        raise RuntimeError("GraphQL reply carried neither `data` nor `errors`")
    return reply["data"]


def _resolve_board_project_number(owner: str, title: str) -> int:
    """The Projects v2 `number` for `owner`'s project named `title`. Raises if 0 or >1 match.

    Read by TITLE, not hardcoded, so a project renumbering does not silently point this gate at the
    wrong board — the same title-verification discipline `project-field-options.project_field` uses.
    """
    data = _gh_graphql(_BOARD_PROJECT_LOOKUP_QUERY, {"owner": owner})
    org = data.get("organization") or {}
    nodes = ((org.get("projectsV2") or {}).get("nodes")) or []
    matches = [n for n in nodes if isinstance(n, dict) and n.get("title") == title]
    if len(matches) != 1:
        raise RuntimeError(
            f"expected exactly one Projects v2 board named {title!r} under {owner!r}, found "
            f"{len(matches)} (of {len(nodes)} project(s) readable at all)")
    return int(matches[0]["number"])


def _fetch_board_items(owner: str, title: str) -> list[dict]:
    """Every item on `owner`'s `title` Projects v2 board, normalized to `{owner, repo, number,
    status}` per row. Raises on any transport/shape failure or a page-count disagreement.

    Only `Issue`/`PullRequest` content carries a `repository` — the direction's whole subject is
    "which repository does this row name", so a `DraftIssue` (no repository) is silently skipped
    rather than treated as unreadable; it names no repository to close over.
    """
    number = _resolve_board_project_number(owner, title)
    out: list[dict] = []
    after: str | None = None
    total: int | None = None
    while True:
        data = _gh_graphql(_BOARD_ITEMS_QUERY, {"owner": owner, "number": number, "after": after})
        project = (data.get("organization") or {}).get("projectV2")
        if not project:
            raise RuntimeError(f"board {owner}/{number} was not readable while paging items")
        page = project.get("items") or {}
        page_total = int(page.get("totalCount", -1))
        if total is None:
            total = page_total
        elif total != page_total:
            raise RuntimeError(
                f"board item count changed while paging ({total} -> {page_total}); the board "
                f"mutated mid-read, so this pass cannot be trusted as a complete snapshot")
        for raw in page.get("nodes") or []:
            content = (raw or {}).get("content") or {}
            repo = content.get("repository")
            if not repo:
                # LEGITIMATE skip only for content this direction's subject genuinely excludes: a
                # DraftIssue (no repository field exists at all) or a node GraphQL returned nothing
                # for at all (no `content`, no `__typename`) — both name no repository to close over.
                # An Issue/PullRequest with a MISSING `repository` is different: GitHub's schema makes
                # that field non-nullable for those two types, so seeing it absent means this specific
                # node came back only PARTIALLY resolved (a per-item GraphQL error, not a whole-request
                # failure `_gh_graphql`'s error/transport checks already catch). Treating that the same
                # as a draft would silently drop a row that might be exactly the `.github#2206`
                # violation this direction exists to catch — so it is a no-verdict instead, the same
                # "could not read" answer a whole-response failure already gets.
                if content.get("__typename") in ("Issue", "PullRequest"):
                    raise RuntimeError(
                        f"board item #{raw.get('id') if isinstance(raw, dict) else '?'} is an "
                        f"{content.get('__typename')} with no `repository` in the reply — a partially "
                        f"resolved node, not a legitimate no-repository row. Cannot trust this page as "
                        f"complete.")
                continue
            status = ((raw.get("status") or {}).get("name")) or ""
            out.append({
                "owner": str(((repo.get("owner") or {}).get("login")) or ""),
                "repo": str(repo.get("name") or ""),
                "number": content.get("number"),
                "status": str(status),
            })
        info = page.get("pageInfo") or {}
        if not info.get("hasNextPage"):
            break
        after = info.get("endCursor")
        if not after:
            raise RuntimeError("board reported hasNextPage=true but no endCursor to page with")
    return out


def _validate_board_items(raw: object) -> list[dict]:
    """Coerce `raw` into a validated `list[{owner, repo, number, status}]`, or raise `ValueError`.

    A row this reader cannot parse might be exactly the `.github#2206` violation direction C exists
    to catch — silently skipping it (or crashing past it) would let a malformed board masquerade as
    a closed one. So EVERY shape problem — a non-list top level, a non-object element, a missing or
    non-string `owner`/`repo`, a non-string `status` — is a `ValueError` the caller turns into the
    same distinguishable no-verdict AC-4 requires for "could not read the board", never a silent skip
    and never an uncaught exception. `_fetch_board_items`'s own output is re-validated here too (cheap,
    and it means a future defect in that function fails closed here rather than reaching
    `board_closure_findings` unchecked).
    """
    if not isinstance(raw, list):
        raise ValueError(
            f"expected a JSON array (or an object with an `items` array), got {type(raw).__name__}")
    out: list[dict] = []
    for index, entry in enumerate(raw):
        if not isinstance(entry, dict):
            raise ValueError(
                f"item[{index}] is a {type(entry).__name__}, expected an object with owner/repo/status")
        owner = entry.get("owner")
        repo = entry.get("repo")
        if not isinstance(owner, str) or not owner.strip():
            raise ValueError(f"item[{index}] has a missing/non-string/blank `owner`")
        if not isinstance(repo, str) or not repo.strip():
            raise ValueError(f"item[{index}] has a missing/non-string/blank `repo`")
        status = entry.get("status", "")
        if status is not None and not isinstance(status, str):
            raise ValueError(
                f"item[{index}].status is a {type(status).__name__}, expected a string or null")
        out.append({"owner": owner, "repo": repo, "number": entry.get("number"), "status": status or ""})
    return out


def board_closure_findings(roster: dict, items: list[dict]) -> list[str]:
    """(C) Every SCHEDULABLE board row's repository is rostered or explicitly exempted.

    "Schedulable" is status != Done (case-insensitive; unset counts as non-Done) — the maintainer's
    own predicate on `.github#2206`: a `Done` row's history is a non-event and costs nothing to leave
    unrostered, while a future schedulable row on that repository re-triggers this finding. This is
    what lets `EHotwagner/rogue3` (its one board row is Done) need no roster action at all, while a
    same-shaped repo with a live row still fails loudly.

    Deliberately NO `_org_owned` filter, unlike `org_closure_findings`: a board item already names its
    own owner, so grading it needs no proof of GitHub org membership — this is the direction
    `.github#2206` was filed to add precisely because direction B's org enumeration cannot see a
    user-owned repository at all.
    """
    errors: list[str] = []
    rostered = {str(r.get("full", "")).strip() for r in (roster.get("repos") or [])}
    exempt = {str(e.get("full", "")).strip() for e in (roster.get("outside-fabric") or [])}

    examples: dict[str, list[str]] = {}
    for item in items:
        status = item.get("status") or ""
        if status.strip().lower() == "done":
            continue
        owner = item.get("owner") or ""
        repo = item.get("repo") or ""
        if not owner or not repo:
            continue
        full = f"{owner}/{repo}"
        if full in rostered or full in exempt:
            continue
        num = item.get("number")
        tag = f"{full}#{num}" if num is not None else full
        examples.setdefault(full, []).append(tag)

    for full in sorted(examples):
        shown = ", ".join(examples[full][:3])
        more = len(examples[full]) - 3
        suffix = f" (+{more} more)" if more > 0 else ""
        errors.append(
            f"{full} holds a schedulable (non-Done) Coordination board row ({shown}{suffix}) but is "
            f"in NEITHER registry/repos.yml NOR its `outside-fabric:` opt-out list. This is the "
            f"roster-closure-2206 shape: a repository can sit on the board — including a user-owned "
            f"one, invisible to org enumeration — while every org fabric and every other closure "
            f"direction stays blind to it. Roster it (a `role: non-participant` row with a `reason:` "
            f"is valid for a repo that takes no fabric), or add an `outside-fabric:` row saying why "
            f"it is deliberately outside.")
    return errors


def check_registry_closure(roster: dict, deps: dict, owner: str) -> list[str]:
    """(A) Every `dependencies.yml` repo is rostered. Offline; the invariant Audio violated."""
    errors: list[str] = []
    rostered = {str(r.get("full", "")).strip() for r in (roster.get("repos") or [])}

    dep_repos = deps.get("repos") or {}
    if not dep_repos:
        # An empty `repos:` block would make this check vacuously pass — the fails-open shape.
        return ["registry/dependencies.yml has no `repos:` block, so registry closure cannot be "
                "checked. Refusing to report green on an absent subject."]
    if not isinstance(dep_repos, dict):
        return [f"registry/dependencies.yml `repos:` is a {type(dep_repos).__name__}, expected a "
                f"mapping of key -> {{name, role}}. Refusing to report green on a subject this "
                f"gate cannot read."]

    for key, entry in dep_repos.items():
        name = (entry or {}).get("name") if isinstance(entry, dict) else None
        if not name:
            errors.append(f"dependencies.yml repo {key!r} has no `name`, so it cannot be matched "
                          f"against the roster.")
            continue
        full = _full(owner, name)
        if full not in rostered:
            errors.append(
                f"{full} is a contract participant in registry/dependencies.yml (repos.{key}) but "
                f"has NO row in registry/repos.yml. Every org fabric iterates the roster, so this "
                f"repo receives no labels, no coordination kit, and no audit — silently. Add a row.")
    return errors


def org_visibility_noverdicts(rostered: set[str], org: str, live_set: set[str],
                              public_repos: int, total_private: int | None) -> list[str]:
    """(B, part 1) Reasons the org listing CANNOT be trusted as a complete enumeration of the org.

    Closure is a claim about the WHOLE org, so it may only be asserted from a listing proven to hold
    every repo that exists. Neither leg failing is a finding — it is a "could not look", which the
    epic this belongs to insists must not share an exit code with "looked, and it is open" (#1154).
    A non-empty return is a NO-VERDICT (exit 3), never a violation.

    Two independent proofs, because either alone fails open:

      * ROSTER leg — every repo we KNOW exists came back. Cheap, needs no org metadata. Catches a
        token too narrow to see a *rostered* repo.
      * COUNT leg — the listing is at least as large as the org's OWN repo total. This is the leg
        the roster leg cannot supply: an unrostered PRIVATE repo is invisible to the roster (it is
        not in it) AND to a run-scoped token (it cannot read it), so the roster leg passes while the
        world is open. Only the org's own count reveals the gap. When the token cannot even read
        that count, closure over private repos is unprovable — which is the whole of #1154.
    """
    nv: list[str] = []

    # ONLY THE REPOS THIS ORG OWNS. `GET /orgs/{org}/repos` enumerates the org's own repositories and
    # nothing else, so a rostered repo owned by a USER is absent from a complete listing exactly as
    # loudly as it would be from a truncated one — and this leg cannot tell those apart.
    #
    # GRADING IT HERE WOULD FAIL OPEN, WHICH IS NOT WHAT A PERMANENT NO-VERDICT SOUNDS LIKE. It would
    # return non-empty on every run, the caller's `if nv: … else:` would then never reach
    # `org_closure_findings`, and `coherence.yml:96-100` turns exit 3 into `::warning::` + `exit 0` —
    # so `main` would carry a GREEN required check over a direction that had stopped looking. The
    # repo-live-in-the-org-and-rostered-nowhere finding (the FS.GG.Audio shape, the one thing no
    # other gate can report) would simply stop appearing. Its rows are named out loud by the caller
    # instead of silently dropped, and they are never counted as verified by this direction.
    for full in sorted(_org_owned(rostered, org)):
        if full not in live_set:
            nv.append(
                f"{full} is rostered in registry/repos.yml but did NOT come back from "
                f"GET /orgs/{org}/repos. Either it was deleted/renamed, or this token cannot see "
                f"the whole org — so 'not in the listing' cannot mean 'does not exist', and the "
                f"closure check would be vacuous. No verdict on an unreachable subject.")
    if nv:
        return nv

    if total_private is None:
        return [f"GET /orgs/{org} did not report `total_private_repos`: the run's token cannot read "
                f"the org's private-repo count, so an unrostered PRIVATE repo cannot be ruled out. "
                f"The listing proves the PUBLIC world is closed, not the whole org. No verdict — "
                f"give the org-closure step a token with organization read, or accept no-verdict."]

    expected = public_repos + total_private
    if len(live_set) < expected:
        return [f"the org owns {expected} repo(s) (public {public_repos} + private {total_private}) "
                f"but this token enumerated only {len(live_set)} from GET /orgs/{org}/repos, so it "
                f"cannot see the whole org and closure would be vacuous. No verdict."]

    return nv


def org_closure_findings(roster: dict, org: str, live_set: set[str]) -> list[str]:
    """(B, part 2) Violations read off a listing ALREADY proven complete by the visibility legs.

    Only reached once `org_visibility_noverdicts` is empty, so here "not in the listing" means
    "does not exist" — a definite finding (exit 1), not an ambiguous one.
    """
    errors: list[str] = []
    rostered = {str(r.get("full", "")).strip() for r in (roster.get("repos") or [])}
    exempt = {str(e.get("full", "")).strip() for e in (roster.get("outside-fabric") or [])}

    for full in sorted(live_set - rostered - exempt):
        errors.append(
            f"{full} exists in the GitHub org but is in NEITHER registry/repos.yml NOR its "
            f"`outside-fabric:` opt-out list. This is the FS.GG.Audio shape: the repo is invisible "
            f"to every org fabric and no other gate can report it. Roster it, or add an "
            f"`outside-fabric:` row saying why it is deliberately outside.")

    # A stale exemption is a standing licence to ignore a repo that no longer exists; over time the
    # opt-out list stops describing the org and starts hiding it.
    #
    # Restricted to the org's OWN repositories for the reason `org_visibility_noverdicts` gives: the
    # listing cannot contain a user-owned repo, so "not in the listing" would report every
    # non-org-owned exemption as stale and demand its removal — turning the escape hatch
    # `.github#2245` reopened straight back into a violation. The staleness of an exemption naming a
    # repository this org does not own is a claim this listing cannot settle, and the caller says so.
    for full in sorted(_org_owned(exempt, org) - live_set):
        errors.append(
            f"{full} is listed under `outside-fabric:` in registry/repos.yml but does not exist in "
            f"org {org!r}. Remove the stale exemption.")

    for full in sorted(exempt & rostered):
        errors.append(
            f"{full} is BOTH rostered and listed under `outside-fabric:`. The roster would iterate "
            f"it while the opt-out claims it is outside the fabric; resolve the contradiction.")

    return errors


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--roster", default="registry/repos.yml")
    ap.add_argument("--deps", default="registry/dependencies.yml")
    ap.add_argument("--org", default="FS-GG")
    ap.add_argument("--org-repos-json", default=None,
                    help="Read the org listing from a JSON file (array of full names, or of GitHub "
                         "repo objects) instead of calling the API. For fixtures.")
    ap.add_argument("--org-meta-json", default=None,
                    help="Read GET /orgs/{org} from a JSON file (an object with `public_repos` and, "
                         "when the token can see it, `total_private_repos`) instead of the API. The "
                         "count-leg of org-visibility reads this. For fixtures.")
    ap.add_argument("--skip-org", action="store_true",
                    help="Run only the offline registry-closure check (A). Loud, and never the CI "
                         "default: it leaves the org-closure question unanswered.")
    ap.add_argument("--board-owner", default=DEFAULT_BOARD_OWNER,
                    help="The login that owns the Coordination Projects v2 board (default: "
                         f"{DEFAULT_BOARD_OWNER!r}).")
    ap.add_argument("--board-title", default=DEFAULT_BOARD_TITLE,
                    help="The Projects v2 board's title, used to resolve its number (default: "
                         f"{DEFAULT_BOARD_TITLE!r}).")
    ap.add_argument("--board-json", default=None,
                    help="Read board items from a JSON file instead of `gh api graphql` — either a "
                         "bare array of {owner, repo, number, status} objects, or an object with an "
                         "`items` key holding that array. For fixtures.")
    ap.add_argument("--skip-board", action="store_true",
                    help="Run only registry/org closure (A/B). Loud, and never the CI default: it "
                         "leaves the board-closure question unanswered — the direction `.github#2206` "
                         "was filed to add.")
    args = ap.parse_args(argv)

    roster = yaml.safe_load(open(args.roster, encoding="utf-8"))
    deps = yaml.safe_load(open(args.deps, encoding="utf-8"))

    # Two buckets, deliberately not one: a VIOLATION (exit 1) is a roster to fix, a NO-VERDICT
    # (exit 3) is a look that could not be made. Registry closure (A) is offline and definite, so
    # it only ever contributes violations.
    findings = check_registry_closure(roster, deps, args.org.split("/")[0])
    noverdicts: list[str] = []

    # NAMED, NEVER SILENTLY SKIPPED (.github#2245). Direction B's subject is `GET /orgs/{org}/repos`,
    # so a roster row naming a repository the org does not own is outside what this gate can settle.
    # Dropping it quietly would be the #266 shape — a row excused by an unstated rule — so every such
    # row is printed with the reason it is not graded here, on stdout, whatever this run exits with.
    # This is a REPORT, not a verdict: it neither passes nor fails those rows, and it is deliberately
    # not routed through `findings`/`noverdicts`, which are answers about closure.
    _rostered_all = {str(r.get("full", "")).strip() for r in (roster.get("repos") or [])}
    _exempt_all = {str(e.get("full", "")).strip() for e in (roster.get("outside-fabric") or [])}
    _outside_org = sorted((_rostered_all | _exempt_all) - _org_owned(_rostered_all | _exempt_all, args.org) - {""})
    if _outside_org:
        print(f"roster-closure: {len(_outside_org)} roster row(s) name a repository org {args.org!r} "
              f"does not own — {', '.join(_outside_org)}. GET /orgs/{args.org}/repos never returns "
              f"them, so org closure (direction B) does not grade them and does not count them as "
              f"verified; their existence and their fabric participation are asserted elsewhere "
              f"(repos-audit reads them off the roster with the run's own credentials). Registry "
              f"closure (direction A) is owner-blind and unchanged: a dependencies.yml participant "
              f"still needs a roster row whoever owns it.")

    if args.skip_org:
        print(f"WARNING: org closure NOT checked (--skip-org). Only registry closure was verified; "
              f"a repo present in org {args.org!r} but absent from the roster would go unreported.",
              file=sys.stderr)
    else:
        live: list[str] | None = None
        try:
            if args.org_repos_json:
                raw = json.load(open(args.org_repos_json, encoding="utf-8"))
                live = [r if isinstance(r, str) else str(r["full_name"]) for r in raw]
            else:
                live = _fetch_org_repos(args.org)
        except (urllib.error.URLError, urllib.error.HTTPError, OSError, ValueError, KeyError) as exc:
            noverdicts.append(f"could not read the repo list for org {args.org!r}: {exc}. "
                              f"'Could not look' is not 'looked, and the world is closed'.")

        if live is not None and not live:
            noverdicts.append(f"GET /orgs/{args.org}/repos returned ZERO repos. An empty listing "
                              f"cannot distinguish 'the org is empty' from 'this token sees "
                              f"nothing'.")
        elif live is not None:
            live_set = set(live)
            rostered = {str(r.get("full", "")).strip() for r in (roster.get("repos") or [])}
            try:
                if args.org_meta_json:
                    meta = json.load(open(args.org_meta_json, encoding="utf-8"))
                    if not isinstance(meta, dict):
                        raise ValueError(f"expected a JSON object, got {type(meta).__name__}")
                    tp = meta.get("total_private_repos")
                    public_repos = int(meta["public_repos"])
                    total_private = int(tp) if tp is not None else None
                else:
                    public_repos, total_private = _fetch_org_meta(args.org)
            except (urllib.error.URLError, urllib.error.HTTPError, OSError, ValueError, KeyError) as exc:
                noverdicts.append(f"could not read org metadata (GET /orgs/{args.org}) needed to "
                                  f"prove the listing is complete: {exc}.")
            else:
                nv = org_visibility_noverdicts(rostered, args.org, live_set,
                                               public_repos, total_private)
                if nv:
                    noverdicts += nv
                else:
                    findings += org_closure_findings(roster, args.org, live_set)

    # Direction C (BOARD closure, `.github#2206`). Independent of the org-closure block above: it
    # reads its own subject (the board's item set) and shares only the `findings`/`noverdicts`
    # buckets and this script's one exit-code contract. A board read failure never borrows direction
    # B's org-unreachable wording, and vice versa — each no-verdict names its own subject so a reader
    # never has to guess which direction went dark.
    nboard = 0
    if args.skip_board:
        print(f"WARNING: board closure NOT checked (--skip-board). Only registry/org closure was "
              f"verified; a repo holding a schedulable Coordination board row but absent from the "
              f"roster would go unreported.", file=sys.stderr)
    else:
        # EVERY step that can fail on a malformed/unreadable board — the JSON parse, the top-level
        # shape check, the per-item shape check, AND the closure computation itself — lives inside
        # this one try/except. `board_closure_findings` used to sit OUTSIDE it: a `--board-json` list
        # whose elements were not objects (`[1,2,3]`, a list of strings, a list of nulls) reached
        # `item.get("status")` and raised an uncaught `AttributeError`, propagating a Python traceback
        # instead of AC-4's mandated exit-3 no-verdict — the exact "vacuous failure" shape #266 exists
        # to prevent, arriving through the criterion this item is judged on (found in independent
        # review, `.github#2206`). `_validate_board_items` now rejects every such shape BEFORE
        # `board_closure_findings` ever sees it, and the call itself stays inside the guard as a
        # second line of defense against a residual defect in either function.
        board_items: list[dict] | None = None
        try:
            if args.board_json:
                raw = json.load(open(args.board_json, encoding="utf-8"))
                candidate = raw.get("items") if isinstance(raw, dict) else raw
                board_items = _validate_board_items(candidate)
            else:
                board_items = _validate_board_items(_fetch_board_items(args.board_owner, args.board_title))
            if board_items:
                # Computed here too, inside the guard: a defect in board_closure_findings itself must
                # no-verdict, not crash, on validated-but-still-unanticipated data.
                board_findings = board_closure_findings(roster, board_items)
            else:
                board_findings = []
        except (RuntimeError, OSError, ValueError, KeyError, TypeError, AttributeError) as exc:
            noverdicts.append(f"could not read the Coordination board ({args.board_owner}/"
                              f"{args.board_title!r}): {exc}. 'Could not look' is not 'looked, and "
                              f"the board is closed'.")
            board_items = None

        if board_items is not None and not board_items:
            noverdicts.append(f"the Coordination board ({args.board_owner}/{args.board_title!r}) "
                              f"reported ZERO items. An empty read cannot distinguish 'the board is "
                              f"empty' from 'this read saw nothing'.")
        elif board_items is not None:
            nboard = len(board_items)
            findings += board_findings

    if findings:
        for e in findings:
            print(f"::error::roster-closure: {e}", file=sys.stderr)
        print(f"\n{len(findings)} roster-closure violation(s).", file=sys.stderr)
        if noverdicts:
            print(f"(also {len(noverdicts)} no-verdict condition(s); the violation(s) above are "
                  f"definite and outrank them.)", file=sys.stderr)
        return 1

    if noverdicts:
        for n in noverdicts:
            print(f"roster-closure: no verdict: {n}", file=sys.stderr)
        print(f"\n{len(noverdicts)} roster-closure no-verdict condition(s) — at least one direction "
              f"could not establish that it sees its whole subject (the org, or the board), so the "
              f"gate neither passes nor fails closure for that direction (#1154). See each line above "
              f"for which subject and why.", file=sys.stderr)
        return 3

    nrost = len(roster.get("repos") or [])
    nexempt = len(roster.get("outside-fabric") or [])
    ndeps = len(deps.get("repos") or {})
    scopes = []
    scopes.append("registry" if args.skip_org else f"registry + org {args.org}")
    if not args.skip_board:
        scopes.append(f"{nboard} board item(s)")
    scope = " and ".join(scopes)
    print(f"ok: {scope} closed — {nrost} rostered repo(s), {nexempt} explicit exemption(s), "
          f"all {ndeps} dependencies.yml participant(s) rostered.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
