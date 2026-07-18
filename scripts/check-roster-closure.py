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

Neither reports a vacuous green — but "could not look" and "looked, and the world is open" are
DIFFERENT answers and must not share an exit code (#1154). A human who sees a red treats it as a
roster to fix; on a transient outage or a too-narrow token that is the wrong action, so the two are
split three ways:

  * exit 0 — the world is closed: the roster covers every repo the listing (proven complete) holds.
  * exit 1 — a VIOLATION the roster must fix: a `dependencies.yml` participant with no roster row;
    a repo live in the org and rostered nowhere (the FS.GG.Audio shape); a stale or contradictory
    `outside-fabric:` exemption.
  * exit 3 — NO VERDICT, the gate could not look: an unreachable/errored/empty org listing; a
    rostered repo missing from the listing; or — the #1154 gap — a token that cannot prove it sees
    the WHOLE org. Org closure needs the listing to be at least as large as the org's own repo
    total (`public_repos + total_private_repos`); a run-scoped token cannot read the private count,
    so it can prove the PUBLIC world closed but not the private one, and says so rather than
    guessing 0. A definite violation outranks a no-verdict when both are present.

Nothing here auto-exempts archived or forked repos: "archived" would be a one-click hole in the
gate. Exemption is always an explicit, reviewed row.

Pure-stdlib + PyYAML (already a coherence-gate dependency).

Usage:
  scripts/check-roster-closure.py [--roster registry/repos.yml] [--deps registry/dependencies.yml]
                                  [--org FS-GG] [--org-repos-json FILE] [--org-meta-json FILE]
                                  [--skip-org]
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

    for full in sorted(rostered):
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
    ap.add_argument("--org-meta-json", default=None,
                    help="Read GET /orgs/{org} from a JSON file (an object with `public_repos` and, "
                         "when the token can see it, `total_private_repos`) instead of the API. The "
                         "count-leg of org-visibility reads this. For fixtures.")
    ap.add_argument("--skip-org", action="store_true",
                    help="Run only the offline registry-closure check (A). Loud, and never the CI "
                         "default: it leaves the org-closure question unanswered.")
    args = ap.parse_args(argv)

    roster = yaml.safe_load(open(args.roster, encoding="utf-8"))
    deps = yaml.safe_load(open(args.deps, encoding="utf-8"))

    # Two buckets, deliberately not one: a VIOLATION (exit 1) is a roster to fix, a NO-VERDICT
    # (exit 3) is a look that could not be made. Registry closure (A) is offline and definite, so
    # it only ever contributes violations.
    findings = check_registry_closure(roster, deps, args.org.split("/")[0])
    noverdicts: list[str] = []

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
        print(f"\n{len(noverdicts)} roster-closure no-verdict condition(s) — the gate could not "
              f"establish that it sees the whole org, so it neither passes nor fails closure "
              f"(#1154).", file=sys.stderr)
        return 3

    nrost = len(roster.get("repos") or [])
    nexempt = len(roster.get("outside-fabric") or [])
    ndeps = len(deps.get("repos") or {})
    scope = "registry closure only" if args.skip_org else f"org {args.org} is closed"
    print(f"ok: {scope} — {nrost} rostered repo(s), {nexempt} explicit exemption(s), "
          f"all {ndeps} dependencies.yml participant(s) rostered.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
