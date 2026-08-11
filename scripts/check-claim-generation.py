#!/usr/bin/env python3
"""Fence a pull request's merge on the LIVE GitHub claim generation of the item it delivers.

`.github#2342`, slice 1 of `.github#1858`'s replacement-plan step 2 ("fence merges"), scoped by the
design gate `.github#2210` — `docs/reports/2026-08-04-github-native-executor-fencing-design.md`
("the design doc" below). `#1858` records that on 2026-07-28 two executors carried one item to
completion concurrently while the board showed exactly one claim marker, and that the second executor
— which never called a single coordination verb — **merged pull requests in six repositories**. This
gate closes the boundary the design doc identifies as the one every guard *inside* `fsgg-coord` cannot
reach: an executor that never ran a lock verb has nothing a client-side guard can intercept, so the
fence must live where the *effect* lands (the merge), not where the tool is invoked (design doc §1.1).

THIS SLICE'S SCOPE, DELIBERATELY NARROWER THAN THE FULL DESIGN. The design's full merge gate (§6.3)
has six checks, two of which (`grant=` names the lowest-id merge-ELECTION marker; the opkey recomputes)
depend on machinery — `OpKey`, the merge election, `delivery` writing the PR authorization marker —
that live in `src/FS.GG.Coord.*` and are explicitly OUT OF this item's `Paths:`. Those two checks are
what grounds the marker in something a forger cannot type (design doc §6.3: "Only check 4 grounds the
marker in something the forger cannot choose"). Without them, THIS gate is satisfiable by a PR author
who simply copies the CURRENT live generation into the marker by hand — it deduplicates and fences the
`#1853` incident's actual shape (an executor holding NO claim at all, so it can name no generation and
is refused at check 1 regardless), but it does not yet defend against a forger who *can* read the live
generation. That is `.github#1858`'s own AC1 caveat repeated here, not a new one: `#2342`'s own scope
says "Root cause is deliberately NOT asserted" and "[t]his slice consumes that identity at the merge
boundary rather than defining the whole scheme." A later slice landing the election and re-wiring this
gate's check 4 (see `#1858`'s replacement plan) closes that residual; it is out of scope here.

THE MARKER THIS GATE READS. The design doc (§6.3) specifies a `fsgg:pr-authorization` marker in the PR
BODY, bound to its head, carrying `item=`, `gen=`, `opkey=`, `grant=`, `head=`. Nothing in `src/`
writes it yet (that is design slice 3, also out of `Paths:`), so this gate DEFINES the subset of that
marker it can validate today and is written to remain forward-compatible with the rest: it requires
`v=1 item=<owner>/<repo>#<n> gen=<comment id> head=<40-hex sha>`, and silently accepts (never rejects
on) any *additional* `key=value` pairs — including a future `opkey=`/`grant=` — so slice 3 landing does
not have to avoid emitting them and this gate does not have to change to tolerate them:

    <!-- fsgg:pr-authorization v=1 item=FS-GG/.github#2342 gen=5250268950 head=<40-hex sha> -->

APPLICABILITY: ONLY a pull request whose branch is `item/<n>-*` — `pnext-item` §2's own naming
convention, the SAME test `Delivery.fs`'s `ItemBranchCanonical` already makes
(`src/FS.GG.Coord.Cli/Client.fs`, the `branch.StartsWith($"item/%d{target.Number}-")` line in the
`delivery` command) — is "a pull request claiming to deliver a board item" (the scope line `#2342`
itself opens with). A PR on any other branch shape (a dependency bump, a docs fix, an admin errand)
never claimed to deliver anything through this protocol, and this gate has nothing to say about it: it
reports OK, vacuously, rather than reading every unrelated PR in the repository as a violation. This is
what lets the gate be WIRED with no `paths:` filter (a requirement for producibility — see
`check-required-contexts.py`'s own docstring on `#1508`) without turning every housekeeping PR red.

FOUR DIAGNOSES, EACH A DIFFERENT OBSERVABLE FACT (`#2342` AC2 — "missing" / "stale" / "mismatched" /
"unreadable" must be distinguishable, not decorative labels on one code path):

  MISSING     — the PR body carries no `fsgg:pr-authorization` marker at all, more than one (an
                ambiguous PR cannot be resolved to a single authorization and is treated the same as
                none), or one missing a required field / naming an unsupported `v=`. This is exactly
                the `#1853` incident's shape: an executor that never called a coordination verb has
                nothing to write into a marker (design doc §8.4).

  STALE       — the item IS currently live-claimed by someone (the engine's own sentinel for "nobody
                holds it" is the literal string `"released"`, `src/FS.GG.Coord.Cli/Client.fs:1723`;
                this gate reuses that reading), under a comment id that DIFFERS from the PR's declared
                `gen=`. Comment ids are server-assigned and monotone (design doc §3.1: a release, a
                steal, or a `reap` only ever mints a STRICTLY HIGHER one), so this is unambiguously "the
                claim moved on since this PR was authorized" — the ordinary, expected shape of `#1853`'s
                root scenario (ordinary, because *some* legitimate tenancy IS live; it is simply not the
                one this PR was authorized under).

  MISMATCHED  — the marker's claim does not correspond to any coherent, CURRENT tenancy at all, which
                is a different defect from "behind": either (a) `item=` names a different item/repo than
                the one this PR's own `item/<n>-` branch declares, (b) `head=` no longer equals this
                PR's actual current head SHA (a force-push invalidated the authorization — the same rule
                `delivery --apply` already enforces client-side, `src/FS.GG.Coord.Cli/Client.fs:1096`),
                (c) `gen=` is not even shaped like a marker id, or (d) the live claim generation IS the
                `"released"` sentinel — the item is not held by ANYONE right now, so there is no live
                tenancy for `gen=` to be stale RELATIVE TO; the assertion is simply ungrounded.

  UNREADABLE  — the live claim state could not be established: a rate limit or outage (retryable, exit
                2) or a permission/parse failure (permanent, exit 3). Per `#2342` AC3, a failed read is
                NEVER a pass — both exit codes are non-zero, and branch protection sees "not success"
                either way; the split exists so a human reading the job log can tell "try again" from
                "fix a credential", not so the gate can be green on either.

WHAT THIS GATE DOES AND DOES NOT ENFORCE TODAY. `#2342` AC6 requires the check to exist and be
correctly wired where this repo's other required contexts run — NOT to be armed. Nothing in this
change adds `fsgg:pr-authorization` to `FS-GG/.github`'s required status checks: that is an
administrative branch-protection edit outside a merge (AC6's own text), and arming BEFORE `delivery`
ever writes the marker would wedge every in-flight `item/<n>-*` PR (design doc §9.1's migration
sequence, step 4/5, is explicit that slice 3 must land, and a full lease window must pass, before
arming). Until an admin runs (after this lands, and after slice 3 lands):

    gh api -X PUT repos/FS-GG/.github/branches/main/protection/required_status_checks/contexts \\
      -f "contexts[]=contract-coherence / coherence" -f "contexts[]=projection" \\
      -f "contexts[]=roster-closure" -f "contexts[]=drift" -f "contexts[]=reconcile" \\
      -f "contexts[]=claim-generation"

...a red verdict from this job does not block a merge — it is observed, not enforced. That is a
DELIBERATE, NAMED gap (AC6), not a silent one.

CROSS-REPO ITEMS ARE READABLE-OR-UNREADABLE, NOT ASSUMED. `.github/workflows/coherence.yml` runs only
on `.github`'s own pull requests, so `--repo` is always `FS-GG/.github` here and its items live in the
same repository the default `GITHUB_TOKEN` can already read comments on. The algorithm below does not
hard-code that: it reads whatever `item=` names, and a token that cannot see a genuinely cross-repo
item simply produces UNREADABLE, which is the correct fail-closed answer for a read that failed for
any reason (`#266`).

WHY THIS DUPLICATES A SLICE OF `Reads.winner` RATHER THAN CALLING IT. `Reads.winner` — "the CAS's
winner, in one place" (`src/FS.GG.Coord.GitHub/Reads.fs`) — is exactly the computation this gate needs
for "the live claim generation": the lowest-id `fsgg:claim` marker whose lease has not lapsed. This
gate is Python, has no engine binding to call into, and `#2342`'s `Paths:` does not include
`src/FS.GG.Coord.GitHub/Reads.fs` (that file is design slice 2's, not this one's) — so there is no
route to call the real function without widening scope. What follows is a DELIBERATELY MINIMAL,
DELIBERATELY NAMED re-expression of `winner`'s rule (lowest live id wins; staleness is `updated_at`
age vs. a lease in minutes; an unreadable age is treated as NOT stale, `Reads.fs`'s own
`isStale`/`AgeSeconds` comment: "inventing... out of a missing field is a confident sentence with
nothing behind it") — not a redesign of it. A later slice that exports a callable ordering function
(design doc §4.2, §11.2 row 2) and rewires this gate to it is the correct fix for the duplication; it
is out of `Paths:` here, and is recorded as a known, accepted, MINIMAL-SURFACE-AREA gap rather than a
silent one — the same disposition `#485`'s own instances are given elsewhere in this repository.

EXIT CODES (the org's shared contract, `scripts/lib/gate.py`):
  0  OK           — not an item-delivery branch (nothing to fence), or the authorization is current.
  1  FINDING      — missing, stale, or mismatched authorization. See the four diagnoses above.
  2  NO VERDICT, RETRYABLE — the live claim state could not be READ (rate limit, outage, timeout).
  3  NO VERDICT, PERMANENT — a permission failure, an unusable input, or a bug in this gate.

Usage:
  check-claim-generation.py --repo <owner/name> --head-ref <branch> --head-sha <40-hex sha>
                             [--body <file>]                 # PR body; default: stdin
                             [--lease-minutes <n>]            # default: $FSGG_CLAIM_LEASE_MIN or 120
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from datetime import datetime, timezone

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib.gate import ExitCode, GateError, Unreachable, run  # noqa: E402

# The engine's own default, `src/FS.GG.Coord.Cli/Options.fs:241` (`DefaultLeaseMinutes = 120`),
# overridable by the SAME environment variable the engine reads (`FSGG_CLAIM_LEASE_MIN`,
# `Options.fs:243`), so a repo that has shortened its lease does not have to be told twice.
DEFAULT_LEASE_MINUTES = 120
LEASE_ENV = "FSGG_CLAIM_LEASE_MIN"

# Transport knobs, in this gate's own namespace so a fixture can silence retries/delay without
# touching another gate's environment (`check-required-contexts.py` uses `FSGG_CONTEXT_*` for the
# same purpose, over a different subject).
GH_TRIES = int(os.environ.get("FSGG_CLAIM_GEN_TRIES", "3"))
GH_RETRY_DELAY = float(os.environ.get("FSGG_CLAIM_GEN_RETRY_DELAY", "2"))
GH_TIMEOUT = float(os.environ.get("FSGG_CLAIM_GEN_TIMEOUT", "30"))

# A pull request's branch, `pnext-item` §2's naming: `item/<n>-<slug>`. The SAME shape
# `src/FS.GG.Coord.Cli/Client.fs`'s `delivery` command tests via `branch.StartsWith`.
ITEM_BRANCH_RE = re.compile(r"^item/(?P<n>[0-9]+)-")

# The PR authorization marker this gate reads. Anchored on the OPEN of the HTML comment so a body
# that merely quotes the marker's text in prose without opening a real `<!--` comment cannot match —
# the same discipline `src/FS.GG.Coord.GitHub/Reads.fs`'s `markerRe` applies to `fsgg:claim`. `DOTALL`
# because the design doc's own marker spans multiple lines.
AUTH_MARKER_RE = re.compile(r"<!--\s*fsgg:pr-authorization\s+(?P<fields>.*?)-->", re.DOTALL)

# One `key=value` token inside a marker. Values are non-whitespace by construction (a GitHub owner/
# repo#n, a decimal id, a 40-hex sha) — none of this marker's fields ever legitimately contains a
# space, so a value that does simply fails the shape check for its field below rather than needing a
# quoting grammar.
FIELD_RE = re.compile(r"(?P<k>[A-Za-z]+)=(?P<v>\S+)")

REQUIRED_FIELDS = ("v", "item", "gen", "head")
SUPPORTED_VERSION = "1"

GEN_RE = re.compile(r"^[0-9]+$")
HEAD_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
ITEM_RE = re.compile(r"^(?P<repo>[^\s#]+)#(?P<n>[0-9]+)$")

# The engine's own sentinel for "nobody currently holds this item" —
# `src/FS.GG.Coord.Cli/Client.fs:1723`: `ClaimGeneration = ... |> Option.defaultValue "released"`.
# Reused here so a live generation this gate reports never collides with a real, numeric comment id.
RELEASED = "released"

# The `fsgg:claim` marker's own grammar, mirrored ONLY as far as this gate needs — see the docstring's
# "WHY THIS DUPLICATES A SLICE OF `Reads.winner`" note. Anchored at the start of the comment body, and
# `worker=` need not be parsed here: this gate only needs the WINNING marker's id, never who holds it.
CLAIM_MARKER_RE = re.compile(r"^<!--\s*fsgg:claim\s")


class Missing(Exception):
    """A 404. A real answer — the named issue does not exist — not a failed read."""


class Forbidden(Exception):
    """A 403/401. The token cannot see this; a human must grant a permission, not retry."""


def gh_api(*args: str) -> str:
    """Read the GitHub API via `gh`, distinguishing "not there" (404) from "not allowed" (403) from
    "could not tell" (anything else, retried). Mirrors `check-required-contexts.py`'s own `gh_api` so
    a fixture can stub `gh` on PATH the same way; the two are independent copies by necessity (Python
    gates have no shared transport module today) but agree on the one thing that matters — an HTTP
    status is read from `gh`'s stderr, never guessed at from its prose.
    """
    delay = GH_RETRY_DELAY
    for attempt in range(1, GH_TRIES + 1):
        try:
            proc = subprocess.run(
                ["gh", "api", *args],
                capture_output=True, text=True, check=False, timeout=GH_TIMEOUT,
            )
        except subprocess.TimeoutExpired:
            if attempt >= GH_TRIES:
                raise Unreachable(f"gh api {' '.join(args)} timed out after {GH_TIMEOUT}s") from None
            time.sleep(delay)
            delay *= 2
            continue
        if proc.returncode == 0:
            return proc.stdout
        err = " ".join(proc.stderr.split())
        if re.search(r"\(HTTP 404\)", err):
            raise Missing(err)
        if re.search(r"\(HTTP 40[13]\)", err):
            raise Forbidden(err)
        if attempt >= GH_TRIES:
            raise Unreachable(err or f"gh exited {proc.returncode}")
        time.sleep(delay)
        delay *= 2
    raise Unreachable("unreachable")  # pragma: no cover


def lease_minutes(cli_value: int | None) -> int:
    """`--lease-minutes` beats `$FSGG_CLAIM_LEASE_MIN` beats the engine's own default — the SAME
    precedence `Options.fs` documents for `--lease`/the env var/`DefaultLeaseMinutes`. A malformed env
    value is a misconfigured shell, not a signal to guess a lease: it is a `GateError` (no verdict, not
    a silent fallback to 120 that could paper over a repo that deliberately shortened its lease).
    """
    if cli_value is not None:
        return cli_value
    raw = os.environ.get(LEASE_ENV)
    if raw is None or raw.strip() == "":
        return DEFAULT_LEASE_MINUTES
    try:
        n = int(raw.strip())
    except ValueError:
        raise GateError(f"{LEASE_ENV} needs a number of minutes (got {raw!r})") from None
    if n <= 0:
        raise GateError(f"{LEASE_ENV} must be a positive number of minutes (got {n})")
    return n


def find_authorizations(body: str) -> list[dict[str, str]]:
    """Every `fsgg:pr-authorization` marker in `body`, each as its raw `{key: value}` fields.

    Deliberately permissive about UNKNOWN fields (a future `opkey=`/`grant=`) and deliberately silent
    about fields it does not understand — this gate validates only `v`/`item`/`gen`/`head`; anything
    else passes through unread, so a later slice adding fields to the SAME marker does not have to
    change this gate first (see the docstring's "THE MARKER THIS GATE READS").
    """
    out: list[dict[str, str]] = []
    for m in AUTH_MARKER_RE.finditer(body):
        fields: dict[str, str] = {}
        for fm in FIELD_RE.finditer(m.group("fields")):
            fields[fm.group("k")] = fm.group("v")
        out.append(fields)
    return out


def live_claim_generation(repo: str, number: int, minutes: int, *, now: datetime | None = None) -> str:
    """The current CAS winner's comment id on `repo#number`, or the `RELEASED` sentinel.

    Re-expresses `Reads.winner`'s rule (`src/FS.GG.Coord.GitHub/Reads.fs`) far enough to answer "what
    is the live claim generation right now": read every issue comment, keep the ones that open with an
    (anchored) `fsgg:claim` marker, drop any whose lease has lapsed (`updated_at` age in minutes >
    `minutes`; an unreadable timestamp is treated as NOT stale — the same fail-closed reading
    `Reads.isStale`'s own comment gives), and return the LOWEST surviving comment id. No live marker
    at all is the engine's own `"released"` reading, reused verbatim (`RELEASED` above).

    Raises `Missing` for a 404 (the named issue does not exist), `Forbidden` for a 403/401, and lets
    `Unreachable` propagate from `gh_api` on a retried-out transient failure — none of the three is
    caught here; the caller decides what each means for ITS verdict (an absent item is "mismatched"
    here, not "unreadable" — see the module docstring).
    """
    owner_repo = repo
    raw = gh_api(
        "--paginate", "--jq", ".[] | {id, body, updated_at}",
        f"repos/{owner_repo}/issues/{number}/comments",
    )
    now = now or datetime.now(timezone.utc)
    live_ids: list[int] = []
    for line in raw.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            comment = json.loads(line)
        except json.JSONDecodeError:
            continue
        body = comment.get("body")
        cid = comment.get("id")
        if not isinstance(body, str) or not CLAIM_MARKER_RE.match(body):
            continue
        if not isinstance(cid, int):
            # A claim marker with no readable numeric id cannot be placed in the CAS's order and
            # cannot be safely dropped either (`Reads.fs`'s own `Unclassifiable` case) — but this
            # gate's ONLY question is "what is the winning id", and a marker that can never win an
            # id-ordered race is simply excluded from the candidate set, never promoted to it.
            continue
        age_seconds = -1
        updated_at = comment.get("updated_at")
        if isinstance(updated_at, str):
            try:
                at = datetime.fromisoformat(updated_at.replace("Z", "+00:00"))
                age_seconds = int((now - at).total_seconds())
            except ValueError:
                age_seconds = -1
        is_stale = age_seconds >= 0 and age_seconds > minutes * 60
        if not is_stale:
            live_ids.append(cid)
    if not live_ids:
        return RELEASED
    return str(min(live_ids))


def classify(
    args: argparse.Namespace, body: str
) -> tuple[str, str] | None:
    """The verdict, as `(kind, message)` for a finding, or `None` for a pass.

    `kind` is one of `"missing"`, `"stale"`, `"mismatched"` — see the module docstring's "FOUR
    DIAGNOSES" section. `"unreadable"` is not produced here: a read failure raises before this
    function is reached, and `main` maps it to exit 2/3 directly.
    """
    matches = find_authorizations(body)
    if len(matches) == 0:
        return "missing", (
            "no `fsgg:pr-authorization` marker in the PR body. This branch claims to deliver "
            f"{args.expected_item} (its name matches `item/<n>-`), but the PR carries no authorization "
            "naming the claim generation it was written under — exactly the `#1853` shape: an executor "
            "that never held a claim has nothing to write into one."
        )
    if len(matches) > 1:
        return "missing", (
            f"{len(matches)} `fsgg:pr-authorization` markers in the PR body — exactly one is required. "
            "An ambiguous PR cannot be resolved to a single authorization, so this is treated the same "
            "as none."
        )
    fields = matches[0]
    missing_fields = [f for f in REQUIRED_FIELDS if f not in fields]
    if missing_fields:
        return "missing", (
            "the `fsgg:pr-authorization` marker is missing required field(s): "
            f"{', '.join(missing_fields)}."
        )
    if fields["v"] != SUPPORTED_VERSION:
        return "missing", (
            f"the `fsgg:pr-authorization` marker names `v={fields['v']}`, which this gate does not "
            f"understand (supported: v={SUPPORTED_VERSION})."
        )
    if not GEN_RE.match(fields["gen"]):
        return "mismatched", (
            f"`gen={fields['gen']}` is not shaped like a claim-marker comment id (expected decimal "
            "digits only) — this authorization cannot correspond to any real claim generation."
        )
    if fields["head"].lower() != args.head_sha.lower():
        return "mismatched", (
            f"`head={fields['head']}` does not equal this PR's current head SHA "
            f"({args.head_sha}). A push after the authorization was written means the authorization is "
            "for a different artifact — the same rule `delivery --apply` already enforces client-side "
            "(`src/FS.GG.Coord.Cli/Client.fs`: a merge is refused because \"the PR is no longer at the "
            "inspected head\")."
        )
    if not ITEM_RE.match(fields["item"]):
        return "mismatched", (
            f"`item={fields['item']}` is not shaped like `owner/repo#n` — this authorization cannot "
            "be resolved to any item at all."
        )
    if fields["item"] != args.expected_item:
        return "mismatched", (
            f"`item={fields['item']}` does not match {args.expected_item}, the item this PR's own "
            f"branch (`{args.head_ref}`) declares it delivers."
        )

    live = live_claim_generation(args.repo, args.item_number, args.lease_minutes)
    if live == RELEASED:
        return "mismatched", (
            f"{args.expected_item} is not currently held by anyone (no live `fsgg:claim` marker), but "
            f"this PR's authorization names generation `gen={fields['gen']}`. There is no live tenancy "
            "for that generation to correspond to — the claim was released, and nothing has reclaimed it."
        )
    if live != fields["gen"]:
        return "stale", (
            f"{args.expected_item} is currently held under claim generation {live}, but this PR's "
            f"authorization names generation `gen={fields['gen']}`. The claim has moved on since this "
            "PR was authorized (released and reclaimed, stolen, or reaped) — re-take the item and "
            "re-author the authorization; comment ids are monotone, so the old generation can never "
            "become current again."
        )
    return None


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--repo", required=True, help="owner/name of this repository, e.g. FS-GG/.github")
    ap.add_argument("--head-ref", required=True, help="the pull request's head branch name")
    ap.add_argument("--head-sha", required=True, help="the pull request's current head commit SHA")
    ap.add_argument("--body", default=None, help="file holding the PR body (default: read stdin)")
    ap.add_argument(
        "--lease-minutes", type=int, default=None,
        help=f"override the claim lease (default: ${LEASE_ENV} or {DEFAULT_LEASE_MINUTES})",
    )
    args = ap.parse_args(argv)

    if not HEAD_SHA_RE.match(args.head_sha.lower()):
        raise GateError(f"--head-sha {args.head_sha!r} is not a 40-hex commit SHA")

    m = ITEM_BRANCH_RE.match(args.head_ref)
    if not m:
        print(
            f"check-claim-generation: OK — {args.head_ref!r} is not an item-delivery branch (no "
            "`item/<n>-` prefix); this PR never claimed to deliver a board item, so there is nothing "
            "to fence."
        )
        return ExitCode.OK

    args.item_number = int(m.group("n"))
    args.expected_item = f"{args.repo}#{args.item_number}"
    args.lease_minutes = lease_minutes(args.lease_minutes)

    if args.body:
        try:
            with open(args.body, encoding="utf-8") as fh:
                body = fh.read()
        except OSError as e:
            raise GateError(f"cannot read PR body at {args.body}: {e}") from e
    else:
        if sys.stdin.isatty():
            raise GateError("no PR body supplied on stdin and no --body given")
        body = sys.stdin.read()

    try:
        finding = classify(args, body)
    except Missing:
        # The item this PR names does not exist at all. A comprehensible, definitive answer — not a
        # failed read — so it is folded into "mismatched": the authorization cannot correspond to any
        # tenancy of an item that is not there.
        message = (
            f"{args.expected_item} does not exist, so this PR's authorization cannot correspond to "
            "any live claim generation."
        )
        print(f"::error::check-claim-generation [mismatched]: {message}", file=sys.stderr)
        print(f"check-claim-generation: FINDING [mismatched] — {message}", file=sys.stderr)
        return ExitCode.FINDING
    except Forbidden as e:
        raise GateError(
            f"cannot read {args.expected_item}'s comments: {e}. Reading issue comments needs "
            "`issues: read`; this job's GITHUB_TOKEN does not have it (or the item lives outside "
            "this repository and the token cannot see it)."
        ) from e

    if finding is None:
        print(
            f"check-claim-generation: OK — {args.expected_item} authorization is current "
            f"(gen={find_authorizations(body)[0]['gen']}, head={args.head_sha})."
        )
        return ExitCode.OK

    kind, message = finding
    print(f"::error::check-claim-generation [{kind}]: {message}", file=sys.stderr)
    print(f"check-claim-generation: FINDING [{kind}] — {message}", file=sys.stderr)
    return ExitCode.FINDING


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name="check-claim-generation"))
