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
depend on machinery — `OpKey` and the merge election itself — that live in `src/FS.GG.Coord.*` and are
explicitly OUT OF this item's `Paths:`. (`delivery` writing the plain `v=`/`item=`/`gen=`/`head=` marker
landed separately, `.github#2395`; the election and its `opkey=`/`grant=` fields have not.) Those two
checks are what grounds the marker in something a forger cannot type (design doc §6.3: "Only check 4
grounds the marker in something the forger cannot choose"). Without them, THIS gate is satisfiable by a
PR author who simply copies the CURRENT live generation into the marker by hand — it deduplicates and
fences the `#1853` incident's actual shape (an executor holding NO claim at all, so it can name no
generation and is refused at check 1 regardless), but it does not yet defend against a forger who *can*
read the live generation. That is `.github#1858`'s own AC1 caveat repeated here, not a new one:
`#2342`'s own scope says "Root cause is deliberately NOT asserted" and "[t]his slice consumes that
identity at the merge boundary rather than defining the whole scheme." A later slice landing the
election and re-wiring this gate's check 4 (see `#1858`'s replacement plan) closes that residual; it is
out of scope here.

THE MARKER THIS GATE READS. The design doc (§6.3) specifies a `fsgg:pr-authorization` marker in the PR
BODY, bound to its head, carrying `item=`, `gen=`, `opkey=`, `grant=`, `head=`. `.github#2395` (design
slice 3) made `delivery --apply --pr N` write and rebind this marker's `v=`/`item=`/`gen=`/`head=`
fields automatically — `Client.ensureAuthorization`/`Client.rebindAuthorization`,
`src/FS.GG.Coord.Cli/Client.fs` — but deliberately not the merge-election `opkey=`/`grant=`, which still
depend on machinery out of that item's `Paths:` (see "THIS SLICE'S SCOPE" above). So this gate DEFINES
the subset of that marker it can validate today and is written to remain forward-compatible with the
rest: it requires `v=1 item=<owner>/<repo>#<n> gen=<comment id> head=<40-hex sha>`, and silently accepts
(never rejects on) any *additional* `key=value` pairs — including a future `opkey=`/`grant=` — so a
later slice landing them does not have to avoid emitting them and this gate does not have to change to
tolerate them:

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

FIVE DIAGNOSES, EACH A DIFFERENT OBSERVABLE FACT (`#2342` AC2 — "missing" / "stale" / "mismatched" /
"unreadable" must be distinguishable, not decorative labels on one code path; `.github#2656` split
"expired" out of "mismatched" for the same reason, see EXPIRY IS NOT EVICTION below):

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

  EXPIRED     — `fsgg:claim` marker(s) ARE on the item and still name a worker, but every one of them
                has outlived its OWN declared `lease=` (`.github#2656`). Nobody stole, reaped, or
                released anything; the holder simply stopped renewing. See EXPIRY IS NOT EVICTION.

  MISMATCHED  — the marker's claim does not correspond to any coherent, CURRENT tenancy at all, which
                is a different defect from "behind": either (a) `item=` names a different item/repo than
                the one this PR's own `item/<n>-` branch declares, (b) `head=` no longer equals this
                PR's actual current head SHA (a force-push invalidated the authorization — the same rule
                `delivery --apply` already enforces client-side, `src/FS.GG.Coord.Cli/Client.fs:1096`),
                (c) `gen=` is not even shaped like a marker id, or (d) the item carries NO `fsgg:claim`
                marker at all — it was released or reaped away, so there is no tenancy for `gen=` to be
                stale RELATIVE TO; the assertion is simply ungrounded.

  UNREADABLE  — the live claim state could not be established: a rate limit or outage (retryable, exit
                2) or a permission/parse failure (permanent, exit 3). Per `#2342` AC3, a failed read is
                NEVER a pass — both exit codes are non-zero, and branch protection sees "not success"
                either way; the split exists so a human reading the job log can tell "try again" from
                "fix a credential", not so the gate can be green on either.

EXPIRY IS NOT EVICTION, AND THE OLD TEXT SAID IT WAS (`.github#2656` AC3). Both cases used to collapse
into the `"released"` sentinel and print *"is not currently held by anyone (no live `fsgg:claim`
marker) … the claim was released, and nothing has reclaimed it"* — which reads as a LOST RACE. Worker
`osprey-174c` diagnosed a real occurrence correctly only because the marker was still present and named
them. They are different facts with different repairs: an EXPIRED lease is repaired by `heartbeat` (or a
re-`take` if the holder is gone) and its marker is still there to read; a RELEASED item has no marker and
must be re-claimed from scratch. This gate now reports them separately and names the holder, the lease it
declared, and the instant it lapsed.

THE LEASE IS THE MARKER'S OWN, NOT THIS GATE'S (`.github#2656` AC2). Each `fsgg:claim` marker carries
`lease=<minutes>` (`src/FS.GG.Coord.GitHub/Writes.fs`, `claimMarker`), so staleness is measured per
marker against the lease THAT marker declares. `--lease-minutes` still wins when given (an operator
deliberately tightening the window in CI), then the marker's own `lease=`, then `$FSGG_CLAIM_LEASE_MIN`,
then the engine's default. This is a DELIBERATE, NAMED divergence from `Reads.winner`, which applies one
`leaseMinutes` from its own options to every marker: there, the option and the marker were written by the
same process in the same breath and cannot disagree; here the gate is a separate process reading someone
else's marker, and preferring a number this gate guessed over one the marker states is how a worker who
took an item with a non-default `--lease` gets its claim declared dead by CI.

`renewed=` IS NOT A CLOCK, AND THIS GATE DOES NOT READ IT AS ONE. It looks like a .NET tick count, but
`Writes.claimMarker`'s own comment says it is an OPAQUE renewal token whose only job is to make every
PATCH a real server-side update ("readers deliberately do not need to interpret it"). Lease age is
measured from the COMMENT's `updated_at`, which that token exists to keep moving. Deriving a lease start
from `renewed=` would bind this gate to an unspecified internal encoding.

THE VERDICT HAS A DEADLINE, AND IT IS PUBLISHED (`.github#2656` AC1). A green here asserts a PERISHABLE
fact — "at instant T the item was held under generation G". The instant that fact can first stop being
true is exactly the WINNING marker's own expiry: comment ids are monotone, so a later claim can only ever
mint a HIGHER id and can never displace the winner; only the winner's lease lapsing can change the answer.
So the verdict's deadline is derived, not guessed, and every OK prints it — both as prose and as a
machine-readable `verdict-expires-at=<iso8601>` line.

NOTHING IN GITHUB RE-EVALUATES A CHECK ON THAT DEADLINE, WHICH IS THE DEFECT `.github#2656` NAMES.
`coherence.yml` runs this job on `opened`/`synchronize`/`reopened`/`edited`. A lease expiring is none of
those — it is the passage of time — so a green computed before the lapse stays green, and branch
protection (which is the ONLY fence on a merge made from the GitHub UI or a bare `gh pr merge`, since
`delivery --apply` re-reads the winning claim itself) would admit the merge on a premise that has since
lapsed. `--sweep` is the re-evaluation that bounds it: `coherence.yml`'s `claim-generation-sweep` job runs
it on a schedule, re-computes this verdict for every open `item/<n>-*` PR from that PR's OWN lease, and
PATCHes a fresh `claim-generation` check run when — and only when — the answer has changed. See that job's
comment for the two invariants that bound its blast radius (it never ORIGINATES a verdict, and a failed
read leaves the standing one alone).

WHAT THIS GATE ENFORCES TODAY: IT IS ARMED, AND ITS RED BLOCKS THE MERGE. `claim-generation` is one of
`FS-GG/.github`'s required status checks on `main`, read live on 2026-08-13:

    $ gh api repos/FS-GG/.github/branches/main/protection \\
        --jq '.required_status_checks.contexts'
    ["contract-coherence / coherence","projection","roster-closure","drift","reconcile",
     "claim-generation"]

HOW IT GOT THERE, AS HISTORY (`#2342` AC6, arming by `.github#1858`). This gate shipped OBSERVE-ONLY:
AC6 required the check to exist and be correctly wired where this repo's other required contexts run —
NOT to be armed. No change to this file adds a context to `FS-GG/.github`'s required status checks:
that is an administrative branch-protection edit outside a merge (AC6's own text), and arming BEFORE
`delivery` ever wrote the marker would have wedged every in-flight `item/<n>-*` PR (design doc §9.1's
migration sequence, step 4/5, is explicit that slice 3 must land, and a full lease window must pass,
before arming). Slice 3 landed — `delivery` writes the marker, `.github#2395` — the window passed, and
an admin ran:

    gh api -X PUT repos/FS-GG/.github/branches/main/protection/required_status_checks/contexts \\
      -f "contexts[]=contract-coherence / coherence" -f "contexts[]=projection" \\
      -f "contexts[]=roster-closure" -f "contexts[]=drift" -f "contexts[]=reconcile" \\
      -f "contexts[]=claim-generation"

...so a red verdict from this job now BLOCKS a merge: enforced, not merely observed. The DELIBERATE,
NAMED gap AC6 recorded is closed, and this paragraph is the record of that, not a standing deferral.

THAT PROMISE WAS TRUE FOR BRANCH PROTECTION AND FALSE FOR `landable` (`.github#2373`), BEFORE THE
ARMING ABOVE. Branch protection did not then gate on this job's own conclusion, as designed — but
`scripts/fsgg-coord landable`, the merge gate `pnext-item` names as authoritative for every fleet
worker, scored EVERY live check-run unconditionally, `claim-generation` included, so a FINDING here
(the ordinary, expected shape for any PR opened before its worker knew to write the marker) turned
`landable` red anyway. Reproduced live across three PRs in one wave: the PR without a hand-added
`fsgg:pr-authorization` marker read `landable` RED; an otherwise-identical PR with one read GREEN —
the manual workaround this gate's own design never asked workers to perform, and which (Actions
replays the frozen `pull_request` event payload; a body edit alone never re-triggers this job) was
often unreachable for a PR already open when `claim-generation` first landed. `.github#2373` fixed
`Landable.fs` to hold the SAME "observed, not enforced" promise this gate's docstring made at the time,
by excluding `claim-generation`'s FINDING conclusion from `landable`'s rollup by name — and this
paragraph used to instruct that BOTH that name and Landable's carve-out be removed in the same change
that armed the job. NEITHER IS OUTSTANDING, AND NO NAME REMAINS TO REMOVE: `.github#2517` deleted the
`advisoryCheckNames` literal and made the advisory set the DERIVED COMPLEMENT of the base branch's own
required contexts (`Landable.advisoryFrom`), so a check `main` requires is scored `Blocking` — this one
included, given the arming above — with no source edit and no paired removal to remember. See
`Landable.fsi`'s `scoreRequired` doc for the caller-side detail.

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
  1  FINDING      — missing, stale, expired, or mismatched authorization. See the five diagnoses above.
  2  NO VERDICT, RETRYABLE — the live claim state could not be READ (rate limit, outage, timeout).
  3  NO VERDICT, PERMANENT — a permission failure, an unusable input, or a bug in this gate.

`--sweep` reports on the SWEEP, not on any one PR: 0 once it has re-evaluated what it could (including
when it deliberately changed nothing), 2/3 only when the sweep itself could not run at all. It is not a
required context and must never red the default branch for a per-PR problem — see `sweep_all`.

Usage:
  check-claim-generation.py --repo <owner/name> --head-ref <branch> --head-sha <40-hex sha>
                             [--body <file>]                 # PR body; default: stdin
                             [--lease-minutes <n>]            # default: marker `lease=`, else
                                                              # $FSGG_CLAIM_LEASE_MIN, else 120
                             [--now <iso8601>]                # evaluate as at this instant, not now

  check-claim-generation.py --sweep --repo <owner/name>       # re-evaluate every open item/<n>-* PR
                             [--lease-minutes <n>] [--now <iso8601>] [--dry-run]
"""
from __future__ import annotations

import argparse
import json
import os
import re
import subprocess
import sys
import time
from datetime import datetime, timedelta, timezone

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
# "WHY THIS DUPLICATES A SLICE OF `Reads.winner`" note. Anchored at the start of the comment body.
# `worker=` and `lease=` ARE parsed now (`.github#2656`): the first so an expired lease can name its
# holder instead of reading as a lost race, the second so staleness is measured against the lease the
# marker itself declares. `renewed=` is deliberately NOT read — see the docstring's "`renewed=` IS NOT
# A CLOCK".
CLAIM_MARKER_RE = re.compile(r"^<!--\s*fsgg:claim\s(?P<fields>.*?)-->", re.DOTALL)

# The name of the check run this gate's verdict is published under — both by `coherence.yml`'s
# `claim-generation` job (implicitly, as the job id) and by `--sweep` (explicitly, as the check-run name
# it PATCHes). It is also the REQUIRED status context on `main`, which is why the two must agree: a
# sweep that posted under any other name would leave the stale green in place and fence nothing.
CHECK_RUN_NAME = "claim-generation"


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


def gh_api_post(path: str, payload: dict) -> str:
    """POST `payload` as JSON to `path`, distinguishing the same three failures `gh_api` does.

    Separate from `gh_api` because the body goes in on stdin (`--input -`), which the read helper's
    retry loop has no place for: a POST is not idempotent in general, so this one does NOT retry. The
    only POST this gate makes — creating a `claim-generation` check run — IS idempotent in effect (the
    latest check run for a context wins), but a retry that half-succeeded would still double-post, and
    `--sweep`'s own next pass is a better retry than a tighter one here.
    """
    proc = subprocess.run(
        ["gh", "api", "-X", "POST", path, "--input", "-"],
        input=json.dumps(payload), capture_output=True, text=True, check=False, timeout=GH_TIMEOUT,
    )
    if proc.returncode == 0:
        return proc.stdout
    err = " ".join(proc.stderr.split())
    if re.search(r"\(HTTP 404\)", err):
        raise Missing(err)
    if re.search(r"\(HTTP 40[13]\)", err):
        raise Forbidden(err)
    raise Unreachable(err or f"gh exited {proc.returncode}")


def lease_minutes(cli_value: int | None) -> int:
    """The lease this gate falls back to when a `fsgg:claim` marker does not declare its own.

    `--lease-minutes` beats `$FSGG_CLAIM_LEASE_MIN` beats the engine's own default — the SAME
    precedence `Options.fs` documents for `--lease`/the env var/`DefaultLeaseMinutes`. A malformed env
    value is a misconfigured shell, not a signal to guess a lease: it is a `GateError` (no verdict, not
    a silent fallback to 120 that could paper over a repo that deliberately shortened its lease).

    NOTE the ORDER relative to the marker (`.github#2656` AC2): an EXPLICIT `--lease-minutes` outranks
    the marker's own `lease=`, because that flag is an operator deliberately tightening the window in
    CI and a marker must not be able to opt out of it. Everything below it — the env var and the
    built-in default — is only this gate's guess, and a marker that states its lease outranks a guess.
    `marker_lease` applies exactly that ordering; this function computes the fallback half of it.
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


def marker_lease(fields: dict[str, str], fallback: int, *, cli_override: bool) -> tuple[int, str]:
    """The lease THIS marker runs under, in minutes, WITH where that number came from.

    `cli_override` is True when the caller passed `--lease-minutes` explicitly; then `fallback` IS that
    operator instruction and wins outright. Otherwise the marker's own `lease=` wins when it is a
    positive integer, and `fallback` (env or default) applies only when the marker does not say.

    The provenance is returned, not inferred by the caller, because the diagnosis PRINTS it: telling a
    worker their marker "declares" a 30-minute lease when the marker says 120 and CI passed
    `--lease-minutes 30` is a confidently wrong sentence, and this gate exists to stop a reader
    misdiagnosing exactly this kind of thing (`.github#2656` AC3).

    An unparsable or non-positive `lease=` is NOT a no-verdict: it is one malformed marker among
    possibly many, and refusing the whole verdict over it would let a single junk comment on an item
    wedge that item's merges. It falls back, and says so.
    """
    if cli_override:
        return fallback, "set by --lease-minutes"
    raw = fields.get("lease")
    if raw is None:
        return fallback, "this gate's fallback — the marker declares no `lease=`"
    try:
        n = int(raw)
    except ValueError:
        return fallback, f"this gate's fallback — the marker's `lease={raw}` is not a number"
    if n <= 0:
        return fallback, f"this gate's fallback — the marker's `lease={raw}` is not positive"
    return n, "declared by that marker"


def parse_instant(raw: str, what: str) -> datetime:
    """An ISO-8601 instant as an aware UTC `datetime`, or a `GateError` naming `what`."""
    try:
        at = datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except ValueError:
        raise GateError(f"{what} is not an ISO-8601 instant (got {raw!r})") from None
    return at if at.tzinfo is not None else at.replace(tzinfo=timezone.utc)


class Claimant:
    """One readable `fsgg:claim` marker, reduced to what a verdict needs."""

    __slots__ = ("id", "worker", "lease", "lease_source", "expires_at", "age_seconds")

    def __init__(self, cid: int, worker: str | None, lease: int, lease_source: str,
                 expires_at: datetime | None, age_seconds: int) -> None:
        self.id = cid
        self.worker = worker
        self.lease = lease
        self.lease_source = lease_source
        self.expires_at = expires_at
        self.age_seconds = age_seconds


class ClaimState:
    """What the `fsgg:claim` markers on an item say, as of one instant.

    `kind` is one of:

      "held"      — at least one marker's lease is still live. `winner` is the lowest-id live marker
                    (`Reads.winner`'s rule), and `winner.expires_at` is the instant this answer can
                    FIRST change: ids are monotone, so no later claim can displace a live winner, and
                    only the winner's own lapse can move the generation.
      "expired"   — markers exist and every one has outlived its own declared lease. `lapsed` is the
                    one that lapsed most recently, i.e. the last worker to hold the item.
      "released"  — the item carries no readable `fsgg:claim` marker at all.

    The three are kept apart because they have three different repairs, and collapsing "expired" into
    "released" is precisely the misdiagnosis `.github#2656` AC3 names.
    """

    __slots__ = ("kind", "winner", "lapsed")

    def __init__(self, kind: str, winner: Claimant | None = None, lapsed: Claimant | None = None) -> None:
        self.kind = kind
        self.winner = winner
        self.lapsed = lapsed

    @property
    def generation(self) -> str:
        """The live generation as `classify` compares it, or the engine's `"released"` sentinel."""
        return str(self.winner.id) if self.winner is not None else RELEASED


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


def read_claim_state(
    repo: str, number: int, fallback_lease: int, *,
    cli_override: bool = False, now: datetime | None = None,
) -> ClaimState:
    """The `fsgg:claim` state of `repo#number` as of `now` — held, expired, or released.

    Re-expresses `Reads.winner`'s rule (`src/FS.GG.Coord.GitHub/Reads.fs`) far enough to answer "what
    is the live claim generation right now": read every issue comment, keep the ones that open with an
    (anchored) `fsgg:claim` marker, drop any whose lease has lapsed (`updated_at` age in minutes >
    that marker's OWN `lease=`; an unreadable timestamp is treated as NOT stale — the same fail-closed
    reading `Reads.isStale`'s own comment gives), and take the LOWEST surviving comment id.

    WHAT `.github#2656` CHANGED HERE, AND WHY IT IS NOT A SECOND ANSWER TO AN ANSWERED QUESTION: this
    used to return a bare string and collapse "every lease lapsed" into the same `"released"` value as
    "there was never a marker". Both are still exactly as NON-authorizing as before — the caller reds on
    either — but they are now distinguishable, which is the whole of AC3, and the surviving `expires_at`
    is what lets a verdict publish its own deadline (AC1).

    Raises `Missing` for a 404 (the named issue does not exist), `Forbidden` for a 403/401, and lets
    `Unreachable` propagate from `gh_api` on a retried-out transient failure — none of the three is
    caught here; the caller decides what each means for ITS verdict (an absent item is "mismatched"
    here, not "unreadable" — see the module docstring).
    """
    raw = gh_api(
        "--paginate", "--jq", ".[] | {id, body, updated_at}",
        f"repos/{repo}/issues/{number}/comments",
    )
    now = now or datetime.now(timezone.utc)
    live: list[Claimant] = []
    lapsed: list[Claimant] = []
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
        if not isinstance(body, str):
            continue
        m = CLAIM_MARKER_RE.match(body)
        if m is None:
            continue
        if not isinstance(cid, int):
            # A claim marker with no readable numeric id cannot be placed in the CAS's order and
            # cannot be safely dropped either (`Reads.fs`'s own `Unclassifiable` case) — but this
            # gate's ONLY question is "what is the winning id", and a marker that can never win an
            # id-ordered race is simply excluded from the candidate set, never promoted to it. It is
            # excluded from the LAPSED set too, for the same reason: it cannot be reported as "the
            # worker who let this lapse" when it cannot be ordered against the ones that can.
            continue
        fields: dict[str, str] = {
            fm.group("k"): fm.group("v") for fm in FIELD_RE.finditer(m.group("fields"))
        }
        lease, lease_source = marker_lease(fields, fallback_lease, cli_override=cli_override)
        worker = fields.get("worker")
        age_seconds = -1
        expires_at: datetime | None = None
        updated_at = comment.get("updated_at")
        if isinstance(updated_at, str):
            try:
                at = parse_instant(updated_at, "a comment's updated_at")
            except GateError:
                at = None
            if at is not None:
                age_seconds = int((now - at).total_seconds())
                expires_at = at + timedelta(minutes=lease)
        claimant = Claimant(cid, worker, lease, lease_source, expires_at, age_seconds)
        # The SAME boundary `Reads.isStale` draws: strictly GREATER than the lease is lapsed, and a
        # negative (unreadable) age is not lapsed at all.
        if age_seconds >= 0 and age_seconds > lease * 60:
            lapsed.append(claimant)
        else:
            live.append(claimant)
    if live:
        return ClaimState("held", winner=min(live, key=lambda c: c.id))
    if lapsed:
        # The one that lapsed MOST RECENTLY is the last worker to have held the item, and so the one a
        # reader needs named. `age_seconds` is the ordering because it is the field every lapsed marker
        # is guaranteed to have (a lapsed marker by definition had a readable timestamp).
        return ClaimState("expired", lapsed=min(lapsed, key=lambda c: c.age_seconds))
    return ClaimState("released")


def classify(
    args: argparse.Namespace, body: str
) -> tuple[tuple[str, str] | None, ClaimState | None]:
    """The verdict as `((kind, message) or None, claim state or None)`.

    `kind` is one of `"missing"`, `"stale"`, `"expired"`, `"mismatched"` — see the module docstring's
    "FIVE DIAGNOSES" section. `"unreadable"` is not produced here: a read failure raises before this
    function is reached, and `main` maps it to exit 2/3 directly.

    The `ClaimState` rides along so the caller can publish the verdict's own deadline on a pass
    (`.github#2656` AC1) without making the live read twice. It is `None` for every finding decided
    from the PR body alone, because those are reached before the live read is made at all.
    """
    matches = find_authorizations(body)
    if len(matches) == 0:
        return ("missing", (
            "no `fsgg:pr-authorization` marker in the PR body. This branch claims to deliver "
            f"{args.expected_item} (its name matches `item/<n>-`), but the PR carries no authorization "
            "naming the claim generation it was written under — exactly the `#1853` shape: an executor "
            "that never held a claim has nothing to write into one."
        )), None
    if len(matches) > 1:
        return ("missing", (
            f"{len(matches)} `fsgg:pr-authorization` markers in the PR body — exactly one is required. "
            "An ambiguous PR cannot be resolved to a single authorization, so this is treated the same "
            "as none."
        )), None
    fields = matches[0]
    missing_fields = [f for f in REQUIRED_FIELDS if f not in fields]
    if missing_fields:
        return ("missing", (
            "the `fsgg:pr-authorization` marker is missing required field(s): "
            f"{', '.join(missing_fields)}."
        )), None
    if fields["v"] != SUPPORTED_VERSION:
        return ("missing", (
            f"the `fsgg:pr-authorization` marker names `v={fields['v']}`, which this gate does not "
            f"understand (supported: v={SUPPORTED_VERSION})."
        )), None
    if not GEN_RE.match(fields["gen"]):
        return ("mismatched", (
            f"`gen={fields['gen']}` is not shaped like a claim-marker comment id (expected decimal "
            "digits only) — this authorization cannot correspond to any real claim generation."
        )), None
    if fields["head"].lower() != args.head_sha.lower():
        return ("mismatched", (
            f"`head={fields['head']}` does not equal this PR's current head SHA "
            f"({args.head_sha}). A push after the authorization was written means the authorization is "
            "for a different artifact — the same rule `delivery --apply` already enforces client-side "
            "(`src/FS.GG.Coord.Cli/Client.fs`: a merge is refused because \"the PR is no longer at the "
            "inspected head\")."
        )), None
    if not ITEM_RE.match(fields["item"]):
        return ("mismatched", (
            f"`item={fields['item']}` is not shaped like `owner/repo#n` — this authorization cannot "
            "be resolved to any item at all."
        )), None
    if fields["item"] != args.expected_item:
        return ("mismatched", (
            f"`item={fields['item']}` does not match {args.expected_item}, the item this PR's own "
            f"branch (`{args.head_ref}`) declares it delivers."
        )), None

    state = read_claim_state(
        args.repo, args.item_number, args.lease_minutes,
        cli_override=args.lease_from_cli, now=args.now,
    )
    if state.kind == "released":
        return ("mismatched", (
            f"{args.expected_item} carries no `fsgg:claim` marker at all, but this PR's authorization "
            f"names generation `gen={fields['gen']}`. There is no tenancy for that generation to "
            "correspond to — the claim was RELEASED or reaped away, and nothing has reclaimed it. This "
            "is EVICTION, not expiry: an expired lease leaves its marker in place and is reported as "
            "`[expired]`, naming the worker who held it."
        )), state
    if state.kind == "expired":
        c = state.lapsed
        assert c is not None  # `expired` is constructed only with a lapsed claimant
        who = f"`{c.worker}`" if c.worker else "an unnamed worker"
        when = c.expires_at.strftime("%Y-%m-%dT%H:%M:%SZ") if c.expires_at else "an unreadable instant"
        over = max(0, c.age_seconds - c.lease * 60) // 60
        return ("expired", (
            f"{args.expected_item}'s claim LEASE HAS LAPSED. Its `fsgg:claim` marker (comment "
            f"{c.id}) is still there and still names {who}, but its {c.lease}-minute lease "
            f"({c.lease_source}) ran out at {when} — {over} minute(s) ago — and nothing has renewed it. This PR's "
            f"authorization names generation `gen={fields['gen']}`, which is no longer live.\n"
            "READ THIS AS EXPIRY, NOT AS A LOST RACE: nobody stole, reaped, or released the item, so "
            "`heartbeat` renews the SAME generation and this verdict goes green again on the next "
            "evaluation. Only if the holder is genuinely gone does the item need re-`take`ing — which "
            "mints a HIGHER generation and requires a fresh `delivery` call to re-author this PR."
        )), state
    live = state.generation
    if live != fields["gen"]:
        return ("stale", (
            f"{args.expected_item} is currently held under claim generation {live}, but this PR's "
            f"authorization names generation `gen={fields['gen']}`. The claim has moved on since this "
            "PR was authorized (released and reclaimed, stolen, or reaped) — re-take the item and "
            "re-author the authorization; comment ids are monotone, so the old generation can never "
            "become current again."
        )), state
    return None, state


def evaluate(args: argparse.Namespace, body: str) -> tuple[str | None, str, ClaimState | None]:
    """`classify`, plus the one fold `main` and `--sweep` must not spell differently.

    A 404 on the item is a comprehensible, definitive answer — not a failed read — so it becomes
    `mismatched` here rather than a no-verdict. `Forbidden` and `Unreachable` deliberately propagate:
    they are "could not look", and #266 forbids either caller from turning that into a verdict.
    """
    try:
        finding, state = classify(args, body)
    except Missing:
        return "mismatched", (
            f"{args.expected_item} does not exist, so this PR's authorization cannot correspond to "
            "any live claim generation."
        ), None
    if finding is None:
        return None, "", state
    return finding[0], finding[1], state


def deadline_lines(args: argparse.Namespace, state: ClaimState | None) -> list[str]:
    """The verdict's own expiry, as prose plus one machine-readable line (`.github#2656` AC1).

    A green here is a PERISHABLE assertion, and this is the instant it stops holding: the WINNING
    claim marker's own lease deadline. Derived from the marker, never guessed alongside it (AC2).
    An unreadable `updated_at` yields no deadline — `Reads.isStale`'s reading treats such a marker as
    live, and inventing an expiry for it would be exactly the confident sentence with nothing behind it
    that comment warns against.
    """
    if state is None or state.winner is None or state.winner.expires_at is None:
        return [
            "check-claim-generation: verdict-expires-at=unknown — the winning claim marker has no "
            "readable `updated_at`, so this verdict has no computable deadline."
        ]
    c = state.winner
    now = args.now or datetime.now(timezone.utc)
    left = int((c.expires_at - now).total_seconds()) // 60
    who = f"`{c.worker}`" if c.worker else "an unnamed worker"
    stamp = c.expires_at.strftime("%Y-%m-%dT%H:%M:%SZ")
    return [
        f"check-claim-generation: verdict-expires-at={stamp}",
        (
            f"check-claim-generation: this verdict is BOUNDED — it stops being true at {stamp} "
            f"(in ~{left}m), when {who}'s {c.lease}-minute claim lease ({c.lease_source}) lapses "
            "unless it is renewed. "
            "No GitHub event fires on that instant, so this check run does not re-evaluate itself "
            "(.github#2656); `coherence.yml`'s `claim-generation-sweep` job is what revokes this green "
            "if the lease lapses before the merge."
        ),
    ]


def item_args(args: argparse.Namespace, head_ref: str, head_sha: str) -> argparse.Namespace | None:
    """A per-PR argument namespace for `evaluate`, or `None` when the branch fences nothing.

    ONE construction shared by `main` and `--sweep`, so the sweep cannot decide applicability, the item
    number, or the lease by a different rule than the required check does — two rules for "does this PR
    deliver an item" is how a sweep would revoke a verdict the job would never have issued.
    """
    m = ITEM_BRANCH_RE.match(head_ref)
    if not m:
        return None
    per = argparse.Namespace(
        repo=args.repo,
        head_ref=head_ref,
        head_sha=head_sha,
        item_number=int(m.group("n")),
        lease_minutes=args.lease_minutes,
        lease_from_cli=args.lease_from_cli,
        now=args.now,
    )
    per.expected_item = f"{per.repo}#{per.item_number}"
    return per


def open_item_prs(repo: str) -> list[dict]:
    """Every OPEN pull request in `repo`, as `{number, body, ref, sha}`.

    Unfiltered by branch shape on purpose: `item_args` owns applicability (see its note), so this read
    stays a plain listing and the two callers cannot disagree about what an item branch is.
    """
    raw = gh_api(
        "--paginate", "--jq",
        ".[] | {number, body, ref: .head.ref, sha: .head.sha}",
        f"repos/{repo}/pulls?state=open&per_page=100",
    )
    out: list[dict] = []
    for line in raw.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            out.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    return out


def standing_conclusion(repo: str, sha: str) -> str | None:
    """The conclusion of the most recent `claim-generation` check run on `sha`, or `None` if there is none.

    `None` is load-bearing, not a shrug: the sweep NEVER originates a verdict (see `sweep_all`), so a
    head with no standing check run is left entirely alone.
    """
    raw = gh_api(
        "--jq", ".check_runs[] | {id, conclusion, started_at}",
        f"repos/{repo}/commits/{sha}/check-runs?check_name={CHECK_RUN_NAME}&per_page=100",
    )
    runs: list[dict] = []
    for line in raw.splitlines():
        line = line.strip()
        if not line:
            continue
        try:
            runs.append(json.loads(line))
        except json.JSONDecodeError:
            continue
    if not runs:
        return None
    latest = max(runs, key=lambda r: (str(r.get("started_at") or ""), int(r.get("id") or 0)))
    got = latest.get("conclusion")
    return got if isinstance(got, str) else None


def sweep_all(args: argparse.Namespace) -> int:
    """Re-evaluate `claim-generation` for every open `item/<n>-*` PR, and publish the ones that changed.

    THIS IS THE RE-EVALUATION `.github#2656` AC1 ASKS FOR. A required check is computed on an event —
    `opened`/`synchronize`/`reopened`/`edited` — and a claim lease lapsing is none of those, so a green
    verdict outlives the claim it fences on with nothing to notice. This function is what notices: it
    recomputes each PR's verdict from THAT PR's own claim lease and posts a fresh `claim-generation`
    check run when the answer has changed.

    TWO INVARIANTS BOUND ITS BLAST RADIUS, and both are refusals rather than best-effort intentions:

      IT NEVER ORIGINATES A VERDICT. If a head has no standing `claim-generation` check run, the sweep
      does nothing for it. Everything it can ever do is CHANGE an answer `coherence.yml`'s own job
      already gave, using the same script, the same applicability rule, and the same lease — so a PR the
      required job has not judged cannot first be judged by a cron.

      A FAILED READ LEAVES THE STANDING VERDICT ALONE. A rate limit, an outage, or a permission problem
      on one PR is logged and skipped, never converted into a conclusion. That is #266 in the only
      direction available here: this sweep can only ever STRENGTHEN the fence, so declining to act
      leaves exactly today's behaviour, whereas posting a guess would be a verdict nobody computed.

    The sweep's own exit code reports on the SWEEP, not on any PR: per-PR trouble is a warning line and
    exit 0, because this job is not a required context and must not red the default branch for it. Only
    a failure to enumerate the PRs at all — which means the sweep did not run — propagates as 2 or 3.
    """
    prs = open_item_prs(args.repo)
    looked = changed = skipped = 0
    for pr in prs:
        ref, sha, number = pr.get("ref"), pr.get("sha"), pr.get("number")
        if not isinstance(ref, str) or not isinstance(sha, str) or not HEAD_SHA_RE.match(sha.lower()):
            continue
        per = item_args(args, ref, sha)
        if per is None:
            continue
        looked += 1
        body = pr.get("body") or ""
        try:
            kind, message, _state = evaluate(per, body)
        except (Forbidden, Unreachable, GateError) as e:
            skipped += 1
            print(
                f"::warning::check-claim-generation sweep: PR #{number} ({ref}) could not be "
                f"re-evaluated ({e}). The standing verdict is left exactly as it was — a failed read is "
                "never a verdict (#266)."
            )
            continue
        conclusion = "success" if kind is None else "failure"
        try:
            standing = standing_conclusion(args.repo, sha)
        except (Missing, Forbidden, Unreachable, GateError) as e:
            skipped += 1
            print(
                f"::warning::check-claim-generation sweep: PR #{number} ({ref}) — could not read the "
                f"standing `{CHECK_RUN_NAME}` check run ({e}); leaving it alone."
            )
            continue
        if standing is None:
            print(
                f"check-claim-generation sweep: PR #{number} ({ref}) has no standing "
                f"`{CHECK_RUN_NAME}` check run — the sweep re-evaluates verdicts, it never originates "
                "one. Leaving it alone."
            )
            continue
        if standing == conclusion:
            print(f"check-claim-generation sweep: PR #{number} ({ref}) is unchanged ({conclusion}).")
            continue
        summary = message or (
            f"{per.expected_item}'s authorization is current and its claim lease is still live."
        )
        print(
            f"check-claim-generation sweep: PR #{number} ({ref}) {standing} -> {conclusion} "
            f"[{kind or 'ok'}] at head {sha}."
        )
        if args.dry_run:
            changed += 1
            continue
        payload = {
            "name": CHECK_RUN_NAME,
            "head_sha": sha,
            "status": "completed",
            "conclusion": conclusion,
            "output": {
                "title": (
                    f"claim-generation re-evaluated: {kind}" if kind
                    else "claim-generation re-evaluated: authorization is current"
                ),
                "summary": (
                    f"{summary}\n\nRe-evaluated by `check-claim-generation.py --sweep` "
                    f"(.github#2656) because a claim lease expiring fires no GitHub event, so the "
                    f"standing `{standing}` verdict could not have noticed."
                ),
            },
        }
        try:
            gh_api_post(f"repos/{args.repo}/check-runs", payload)
        except (Missing, Forbidden, Unreachable, GateError) as e:
            skipped += 1
            print(
                f"::warning::check-claim-generation sweep: PR #{number} ({ref}) — could not publish "
                f"the re-evaluated verdict ({e}). The standing one still stands."
            )
            continue
        changed += 1
    print(
        f"check-claim-generation sweep: {looked} item PR(s) re-evaluated, {changed} verdict(s) "
        f"changed, {skipped} left alone."
    )
    return ExitCode.OK


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--repo", required=True, help="owner/name of this repository, e.g. FS-GG/.github")
    ap.add_argument("--head-ref", default=None, help="the pull request's head branch name")
    ap.add_argument("--head-sha", default=None, help="the pull request's current head commit SHA")
    ap.add_argument("--body", default=None, help="file holding the PR body (default: read stdin)")
    ap.add_argument(
        "--lease-minutes", type=int, default=None,
        help=(
            "override every claim marker's own `lease=` (default: the marker's, else "
            f"${LEASE_ENV}, else {DEFAULT_LEASE_MINUTES})"
        ),
    )
    ap.add_argument(
        "--now", default=None,
        help=(
            "evaluate as at this ISO-8601 instant instead of the wall clock. The lease-expiry "
            "regression legs need a deterministic clock: the defect is that ONE unchanged PR reads "
            "green at T and must read red at T+lease, with no push and no body edit in between."
        ),
    )
    ap.add_argument(
        "--sweep", action="store_true",
        help="re-evaluate every open item/<n>-* PR in --repo and publish the verdicts that changed",
    )
    ap.add_argument(
        "--dry-run", action="store_true",
        help="--sweep only: report what would change without publishing any check run",
    )
    args = ap.parse_args(argv)

    args.lease_from_cli = args.lease_minutes is not None
    args.lease_minutes = lease_minutes(args.lease_minutes)
    args.now = parse_instant(args.now, "--now") if args.now else None

    if args.sweep:
        if args.head_ref or args.head_sha or args.body:
            raise GateError("--sweep reads every open PR itself; it takes no --head-ref/--head-sha/--body")
        return sweep_all(args)

    if not args.head_ref or not args.head_sha:
        raise GateError("--head-ref and --head-sha are required unless --sweep is given")
    if not HEAD_SHA_RE.match(args.head_sha.lower()):
        raise GateError(f"--head-sha {args.head_sha!r} is not a 40-hex commit SHA")

    per = item_args(args, args.head_ref, args.head_sha)
    if per is None:
        print(
            f"check-claim-generation: OK — {args.head_ref!r} is not an item-delivery branch (no "
            "`item/<n>-` prefix); this PR never claimed to deliver a board item, so there is nothing "
            "to fence."
        )
        return ExitCode.OK

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
        kind, message, state = evaluate(per, body)
    except Forbidden as e:
        raise GateError(
            f"cannot read {per.expected_item}'s comments: {e}. Reading issue comments needs "
            "`issues: read`; this job's GITHUB_TOKEN does not have it (or the item lives outside "
            "this repository and the token cannot see it)."
        ) from e

    if kind is None:
        print(
            f"check-claim-generation: OK — {per.expected_item} authorization is current "
            f"(gen={find_authorizations(body)[0]['gen']}, head={per.head_sha})."
        )
        for line in deadline_lines(per, state):
            print(line)
        return ExitCode.OK

    print(f"::error::check-claim-generation [{kind}]: {message}", file=sys.stderr)
    print(f"check-claim-generation: FINDING [{kind}] — {message}", file=sys.stderr)
    return ExitCode.FINDING


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name="check-claim-generation"))
