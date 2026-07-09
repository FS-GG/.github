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

This script asserts the roster is closed, from two directions:

  A. REGISTRY closure (offline, no token).  Every repo in `dependencies.yml`'s `repos:` block has a
     row in `repos.yml`. Audio sat in one and not the other for weeks; this is the cheap, strictly-
     implied invariant that would have caught it.

  B. ORG closure (one REST call).  Every repo that actually exists in the GitHub org has a row in
     `repos.yml`, or an explicit row in its `outside-fabric:` opt-out list. This compares against
     REALITY rather than against a second record of it.

Both fail CLOSED, which is the whole point of the epic this belongs to:

  * an unreachable / errored / empty org listing is an ERROR, never a skip;
  * a rostered repo MISSING from the org listing means the listing cannot be trusted (a token that
    cannot see the org would otherwise report a vacuously-closed world) — also an ERROR;
  * an `outside-fabric:` entry that does not exist in the org is a STALE exemption — an error, so
    the opt-out list cannot quietly accumulate permission to ignore repos.

Nothing here auto-exempts archived or forked repos: "archived" would be a one-click hole in the
gate. Exemption is always an explicit, reviewed row.

Pure-stdlib + PyYAML (already a coherence-gate dependency). Exit 0 = the world is closed.

Usage:
  scripts/check-roster-closure.py [--roster registry/repos.yml] [--deps registry/dependencies.yml]
                                  [--org FS-GG] [--org-repos-json FILE] [--skip-org]
"""
from __future__ import annotations

import argparse
import json
import os
import sys
import urllib.error
import urllib.request

import yaml

API = "https://api.github.com"


def _full(owner: str, name: str) -> str:
    """Canonicalize a repo reference to `owner/name`.

    `dependencies.yml` spells the shared repo two ways — key `github`, name `FS-GG/.github` — while
    `repos.yml` gives id `.github`, full `FS-GG/.github`. Comparing on ids would report a phantom
    drift, so both sides are normalized to the full name, which is the thing GitHub agrees on.
    """
    name = str(name).strip()
    return name if "/" in name else f"{owner}/{name}"


def _fetch_org_repos(org: str) -> list[str]:
    """Every repo in the org, following pagination. Raises on any non-200 or malformed page.

    Anonymous requests see public repos; a token (GITHUB_TOKEN) is used when present. Either way a
    private repo the caller cannot see is invisible — which is exactly why the caller cross-checks
    that every rostered repo came back (see `check_org_closure`). Silence is not evidence.
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
    `check_org_closure` a partial world — precisely the fails-open shape this file exists to prevent.
    """
    for part in link_header.split(","):
        seg = [s.strip() for s in part.split(";")]
        if any(s.replace(" ", "") in ('rel="next"', "rel=next") for s in seg[1:]):
            return seg[0].lstrip("<").rstrip(">")
    return ""


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


def check_org_closure(roster: dict, org: str, live: list[str]) -> list[str]:
    """(B) Every repo that exists in the org is rostered or explicitly exempt. Fails closed."""
    errors: list[str] = []
    rostered = {str(r.get("full", "")).strip() for r in (roster.get("repos") or [])}
    exempt = {str(e.get("full", "")).strip() for e in (roster.get("outside-fabric") or [])}

    if not live:
        return [f"the GitHub API returned ZERO repos for org {org!r}. An empty listing cannot "
                f"distinguish 'the org is empty' from 'this token sees nothing'. Failing closed."]

    live_set = set(live)

    # The listing's own trustworthiness, checked before it is used as evidence of absence. Every
    # rostered repo is one we KNOW exists; if the API did not return it, the caller's visibility is
    # too narrow for "not in the listing" to mean "does not exist", and the closure conclusion below
    # would be vacuous. (A rostered repo that was genuinely deleted or renamed also lands here, and
    # should: that is a roster the org no longer backs.)
    for full in sorted(rostered):
        if full not in live_set:
            errors.append(
                f"{full} is rostered in registry/repos.yml but did NOT come back from "
                f"GET /orgs/{org}/repos. Either it was deleted/renamed, or this token cannot see "
                f"the whole org — in which case the closure check below would be vacuous. "
                f"Refusing to report green on an unreachable subject.")
    if errors:
        return errors

    for full in sorted(live_set - rostered - exempt):
        errors.append(
            f"{full} exists in the GitHub org but is in NEITHER registry/repos.yml NOR its "
            f"`outside-fabric:` opt-out list. This is the FS.GG.Audio shape: the repo is invisible "
            f"to every org fabric and no other gate can report it. Roster it, or add an "
            f"`outside-fabric:` row saying why it is deliberately outside.")

    # A stale exemption is a standing licence to ignore a repo that no longer exists; over time the
    # opt-out list stops describing the org and starts hiding it.
    for full in sorted(exempt - live_set):
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
    ap.add_argument("--skip-org", action="store_true",
                    help="Run only the offline registry-closure check (A). Loud, and never the CI "
                         "default: it leaves the org-closure question unanswered.")
    args = ap.parse_args(argv)

    roster = yaml.safe_load(open(args.roster, encoding="utf-8"))
    deps = yaml.safe_load(open(args.deps, encoding="utf-8"))

    errors = check_registry_closure(roster, deps, args.org.split("/")[0])

    if args.skip_org:
        print(f"WARNING: org closure NOT checked (--skip-org). Only registry closure was verified; "
              f"a repo present in org {args.org!r} but absent from the roster would go unreported.",
              file=sys.stderr)
    else:
        try:
            if args.org_repos_json:
                raw = json.load(open(args.org_repos_json, encoding="utf-8"))
                live = [r if isinstance(r, str) else str(r["full_name"]) for r in raw]
            else:
                live = _fetch_org_repos(args.org)
        except (urllib.error.URLError, urllib.error.HTTPError, OSError, ValueError, KeyError) as exc:
            # The subject is unreachable. "Could not check" and "checked, and it's fine" must not
            # share an exit code — that is the defect class this gate exists to end.
            print(f"::error::roster-closure: could not read the repo list for org {args.org!r}: "
                  f"{exc}. Failing closed.", file=sys.stderr)
            return 1
        errors += check_org_closure(roster, args.org, live)

    if errors:
        for e in errors:
            print(f"::error::roster-closure: {e}", file=sys.stderr)
        print(f"\n{len(errors)} roster-closure violation(s).", file=sys.stderr)
        return 1

    nrost = len(roster.get("repos") or [])
    nexempt = len(roster.get("outside-fabric") or [])
    ndeps = len(deps.get("repos") or {})
    scope = "registry closure only" if args.skip_org else f"org {args.org} is closed"
    print(f"ok: {scope} — {nrost} rostered repo(s), {nexempt} explicit exemption(s), "
          f"all {ndeps} dependencies.yml participant(s) rostered.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
