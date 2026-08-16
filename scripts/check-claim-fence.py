#!/usr/bin/env python3
"""The merge fence, in full: all six checks of the GitHub-native executor fencing design §6.3.

`.github#2719`, **slice 4** of the eight in §11.2 of
`docs/reports/2026-08-04-github-native-executor-fencing-design.md` ("the design doc" below), the
landed design for `.github#1858` — *"two workers ran `.github#1853` to completion concurrently under
ONE claim marker — six repos got PRs from an unlocked executor"*, Critical, open since 2026-07-28.
The harm `#1858` measured was a MERGE: `FS.GG.Net#58` and `FS.GG.Audio#220` were merged by an executor
that never called a coordination verb. This gate is what catches that at the merge boundary.

OBSERVE-ONLY IS A PROPERTY OF THE WORKFLOW, NOT OF THIS SCRIPT, AND THE SPLIT IS DELIBERATE. This file
returns a real verdict on the org's shared exit-code contract (`scripts/lib/gate.py`). Its caller,
`.github/workflows/fsgg-claim-fence.yml`, deliberately declines to enforce it: that job reports
`success` unconditionally and writes the verdict to the job summary, which is design doc §9.1 step 1
("Land the producer in observe-only... Required in ZERO repositories"). ARMING IT IS SLICE 8's JOB
(`.github#2723`, §9.1 steps 2-5), and the two are separated so that a half-built gate can never block
a merge. Nothing in this file, and nothing in that workflow, adds a required status context to any
repository — that is an administrative branch-protection edit outside a merge.

HOW THIS RELATES TO `claim-generation`, WHICH IS ALREADY ARMED. `scripts/check-claim-generation.py`
(`.github#2342`) is a DELIBERATELY NARROWER earlier fence: it implements checks 1, 2 and 3 over the
`v=`/`item=`/`gen=`/`head=` subset of the authorization marker, and its own docstring records why it
stops there — the two remaining checks depend on the merge election, which was out of that item's
`Paths:`. Its residual is stated there in as many words: *"THIS gate is satisfiable by a PR author who
simply copies the CURRENT live generation into the marker by hand"*. This file is the gate that closes
that residual, because checks 4 and 5 ground the marker in a server-assigned comment id the forger does
not get to choose. The two coexist on purpose during the observe-only phase: the armed narrow gate keeps
its current guarantee while this wider one is observed, and reconciling them is arming's business
(slice 8), not this slice's.

============================================================================================
THE TWO MARKERS THIS GATE READS
============================================================================================

**The PR authorization**, in the pull request BODY, bound to its head (design doc §6.3):

    <!-- fsgg:pr-authorization v=1 item=FS-GG/.github#2719 gen=5309319124
         opkey=<64 lowercase hex> grant=<comment id> head=<40-hex sha> -->

This gate requires ALL SIX fields. `claim-generation` requires only four and *"silently accepts (never
rejects on) any additional `key=value` pairs — including a future `opkey=`/`grant=`"*, which is exactly
the forward-compatibility that lets this stricter reader be added without editing that one.

**The merge election**, an append-only comment on the ITEM (design doc §4.2):

    <!-- fsgg:merge-election v=1 opkey=<64 lowercase hex> item=FS-GG/.github#2719
         gen=5309319124 receiver=FS-GG/.github op=merge -->

THE ELECTION'S WIRE SPELLING IS DEFINED HERE, AND THAT IS A GAP THIS SLICE INHERITED RATHER THAN CHOSE.
§4.2 specifies the election's SEMANTICS exhaustively — append-only, never deleted, no lease, keyed on
the opkey, winner is the lowest-id marker bearing that key, recording the item/generation/receiver it
was elected for — but it gives no literal byte spelling, because writing the election is §11.2 slice 3's
job (*"`delivery` posts the merge election, then writes the PR authorization marker naming it"*). Slice 3
landed as `.github#2395` with the AUTHORIZATION half only; no producer of an election marker exists in
this repository today (checked: `grep -rni election src/ --include=*.fs --include=*.fsi` finds the
ordering function `Reads.lowestId` that slice 2 exported for it, `.github#2312`, and no writer). So the
reader necessarily fixes the spelling. The fields above are not invented freely: they are exactly the
four facts §6.3 check 4 requires the marker to record, plus the `v=`/prefix discipline every other
marker in this repository already carries. A finding is packeted for the missing producer; it is not
this slice's to write, and its absence is precisely why observe-only is the correct landing state.

THE PREFIX IS NEW, AND THAT IS SAFE FOR THE REASON §4.3 GIVES FOR `fsgg:op-effect`: *"A new prefix is
safe here precisely because `Reads.winner` never reads it: it decides no lock, so it cannot forge one."*
The CAS's arbiter matches `fsgg:claim` and nothing else (`src/FS.GG.Coord.GitHub/Reads.fs`, `markerRe`),
so an election marker is invisible to it by construction.

============================================================================================
THE SIX CHECKS (design doc §6.3), EACH FAILURE RED
============================================================================================

  1. EXACTLY ONE authorization marker in the PR body. Missing -> RED. Duplicate -> RED. Missing a
     required field, or naming an unsupported `v=` -> RED. Missing is the `#1858` executor's own case:
     it never obtained one (§8.4).

  2. `head=` EQUALS THE PULL REQUEST'S CURRENT HEAD SHA. A force-push after authorization means the
     authorization is for a different artifact — the same rule `delivery` enforces client-side
     (`src/FS.GG.Coord.Cli/Client.fs`: *"the PR is no longer at the inspected head"*).

  3. `gen=` EQUALS THE LIVE WINNING `fsgg:claim` COMMENT ID on `item`, re-read at check time. Stale ->
     RED. Released, so there is no live winner -> RED.

  4. `grant=` IS THE LOWEST-ID MERGE-ELECTION MARKER on `item` bearing this opkey, and that marker's
     recorded item, generation and receiver match the authorization. Not the lowest -> RED. Absent ->
     RED. Recorded fields disagreeing -> RED.

     THIS IS THE ONLY CHECK A FORGER CANNOT SATISFY BY TYPING, and the design doc is emphatic that
     without it the gate is decorative: *"Checks 1, 2, 3 and 5 are then all satisfiable by typing,
     because each compares the marker against facts a forger can read. Only check 4 grounds the marker
     in something the forger cannot choose: a server-assigned comment id whose winner is decided by a
     CAS."* Two contexts sharing one live generation compute a byte-identical opkey and pass 1, 2, 3
     and 5 identically; they elect on the SAME opkey and only one of them is lowest.

     IT IS A HISTORICAL FACT, NOT A LIVE ONE, which is the whole reason a queued CI job can answer it.
     An election marker is never deleted and never expires, so the answer a runner computes minutes
     after the push is the answer that was true at the push. A LEASE would be exactly the wrong
     primitive here: `release` deletes the claim marker first (`src/FS.GG.Coord.GitHub/Writes.fs`,
     *"Once it is gone the lease is dropped"*), so a correctly-behaving worker that released on
     schedule would leave nothing for this job to verify — `#463`'s fail-ALWAYS shape, which §8.2
     commits against.

  5. `opkey` RECOMPUTES from `(item, gen, this repository, merge)`. Mismatch -> RED. See THE OPKEY,
     RE-EXPRESSED below.

  6. ANY READ THAT CANNOT BE COMPLETED -> RED via the no-verdict path (exit 2 or 3, never 0). §8.2
     answers the fail-closed / fail-always tension this creates, and its answer is what makes the
     blast radius bounded: this gate reads only `issues: read` and `contents: read`, both ordinary
     `GITHUB_TOKEN` scopes, so it cannot fail on a credential the ordinary path does not have.

============================================================================================
APPLICABILITY, AND WHY IT IS THE BRANCH RATHER THAN A `paths:` FILTER
============================================================================================

Only a pull request whose branch is `item/<n>-*` — `pnext-item` §2's naming, the same test
`delivery` makes via `branch.StartsWith($"item/%d{target.Number}-")` — claims to deliver a board item.
Any other branch shape (a dependency bump, a docs fix, an admin errand) never claimed to deliver
anything through this protocol, and this gate reports OK vacuously for it.

That is what lets the workflow carry NO `paths:` filter, which §6.2 requires: *"a filtered workflow
produces no check run on a PR that misses the filter, and protection then waits forever"*. Applicability
is decided from the branch shape inside the gate, where a skipped check run cannot happen.

============================================================================================
`merge_group` IS NOT OPTIONAL, AND IT NEEDS ONE RESOLUTION STEP THIS GATE OWNS
============================================================================================

§6.2 requires triggering on `pull_request` AND `merge_group`, because a check run is per-SHA and the
`pull_request` triggers fire on HEAD events only — so nothing re-evaluates the fence when the claim
marker changes, and a claim released after the final push would leave a green in place through merge.
The merge queue is the one native mechanism that re-evaluates required checks at merge time.

THE MERGE-GROUP EVENT CARRIES NO PULL REQUEST, AND THE NAIVE READING REDS EVERY QUEUED PR. Its payload
gives `merge_group.head_ref` — `refs/heads/gh-readonly-queue/<base>/pr-<number>-<sha>` — and a
`merge_group.head_sha` that is the QUEUE's temporary merge commit, NOT the pull request's head. Check 2
compares `head=` against the PULL REQUEST's current head SHA, so comparing it against the merge-group
SHA would make every queued item/<n>-* PR red on a fact about the queue rather than about the
authorization. `--merge-group-ref` therefore resolves the pull request number out of the ref and reads
that pull request's own body, branch and head SHA back from the API, and everything downstream is
identical to the `pull_request` path. One evaluation rule, two entry points.

============================================================================================
THE OPKEY, RE-EXPRESSED — WHAT IS PINNED HERE AND WHAT IS NOT
============================================================================================

Check 5 needs `sha256(item \n gen \n receiver \n op)` in lowercase hex (§3.3), which the engine already
owns in `FS.GG.Coord.Core.Operation` (`compose`, slice 1, `.github#2311`). This gate is Python, has no
engine binding to call into, and `src/FS.GG.Coord.Core/` is not in this item's `Paths:` — the same
position `check-claim-generation.py` documents for `Reads.winner`, and the same disposition: a
DELIBERATELY MINIMAL, DELIBERATELY NAMED re-expression rather than a redesign.

What is re-expressed is the WHOLE admitted domain, not just the hash, because the domain is what makes
the composition injective and a Python reader that admitted more than the F# producer would compute a
key for an input the producer refuses:

  * `Separator = "\n"`, and every component is refused for ANY control character — `separatorFree`,
    `src/FS.GG.Coord.Core/Operation.fs`. `Char.IsControl` is Unicode general category `Cc`, which is
    what `unicodedata.category(c) == "Cc"` tests here.
  * Every component is refused for an UNPAIRED SURROGATE — `wellFormedUtf16`, same file. This is not
    decoration: `Encoding.UTF8.GetBytes` uses the replacement fallback, so every unpaired surrogate
    becomes the same three bytes and two genuinely different pre-images would otherwise compose ONE
    key. Python raises `UnicodeEncodeError` on the same inputs rather than substituting, so the two
    implementations refuse the same domain by two different mechanisms; this file refuses explicitly so
    the REASON is reportable rather than arriving as a crash.
  * `item` is `owner/repo#N`, `receiver` is `owner/repo`, `gen` is a canonical server-assigned id — no
    leading zero, not `0`. The leading-zero clause is `serverAssignedId`'s and is not pedantry: `0012`
    and `12` are the same generation and would compose two different keys.

AGREEMENT WITH THE F# PRODUCER WAS EXECUTED, NOT REASONED — AND IT IS NOT PINNED BY THE FIXTURE.
Measured 2026-08-16 by loading the built `FS.GG.Coord.Core.dll` under `dotnet fsi` and comparing
`Operation.compose` against `compose_opkey` over five vectors: three that compose
(`FS-GG/.github#2719`/`5309319124`, the same item under a different generation, and
`FS-GG/FS.GG.Net#58`/`5177045416` — one of the two pull requests `#1858` measured as merged by the
unlocked executor) and two that must REFUSE (the board's `.github#2719` shorthand, and the
leading-zero generation `0012`). All five agreed, digests byte-identical and both refusals mutual.

That is an out-of-band measurement, and it is deliberately NOT what `tests/claim-fence/run.sh` runs:
that fixture is offline and has no .NET dependency, so it pins this implementation against an
INDEPENDENT SHA-256 (`sha256sum` over a `printf`-composed pre-image) instead. The shell half catches a
drifted pre-image spelling; the `dotnet fsi` half is what certifies the F# side, and it is a
point-in-time measurement that a later change to `Operation.fs` could invalidate without reddening
anything here. Two halves, two kinds of evidence, and the gap between them is stated rather than
papered over — conflating them would let a shell test appear to certify a module it never loads.

============================================================================================
EXIT CODES (the org's shared contract, `scripts/lib/gate.py`)
============================================================================================

  0  OK           — not an item-delivery branch (nothing to fence), or all five substantive checks pass.
  1  FINDING      — one of checks 1-5 failed. The diagnosis names which.
  2  NO VERDICT, RETRYABLE — a read could not be MADE (rate limit, outage, timeout). Check 6.
  3  NO VERDICT, PERMANENT — a permission failure, an unusable input, or a bug in this gate. Check 6.

A failed read is NEVER a pass (`#266`, `#421`). Both no-verdict codes are non-zero; the split exists so
a human reading the job log can tell "try again" from "fix a credential", not so the gate can be green
on either.

Usage:
  check-claim-fence.py --repo <owner/name> --head-ref <branch> --head-sha <40-hex sha>
                       [--body <file>]            # PR body; default: stdin
                       [--lease-minutes <n>]      # default: marker `lease=`, else
                                                  # $FSGG_CLAIM_LEASE_MIN, else 120
                       [--now <iso8601>]          # evaluate as at this instant, not now

  check-claim-fence.py --repo <owner/name> --merge-group-ref <refs/heads/gh-readonly-queue/...>
                       [--lease-minutes <n>] [--now <iso8601>]
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
import re
import subprocess
import sys
import time
import unicodedata
from datetime import datetime, timedelta, timezone

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib.gate import ExitCode, GateError, Unreachable, run  # noqa: E402

# The engine's own default, `src/FS.GG.Coord.Cli/Options.fs` (`DefaultLeaseMinutes = 120`), overridable
# by the SAME environment variable the engine reads, so a repo that has shortened its lease does not
# have to be told twice.
DEFAULT_LEASE_MINUTES = 120
LEASE_ENV = "FSGG_CLAIM_LEASE_MIN"

# Transport knobs, in this gate's own namespace so a fixture can silence retries/delay without touching
# another gate's environment.
GH_TRIES = int(os.environ.get("FSGG_CLAIM_FENCE_TRIES", "3"))
GH_RETRY_DELAY = float(os.environ.get("FSGG_CLAIM_FENCE_RETRY_DELAY", "2"))
GH_TIMEOUT = float(os.environ.get("FSGG_CLAIM_FENCE_TIMEOUT", "30"))

# A pull request's branch, `pnext-item` §2's naming: `item/<n>-<slug>`.
ITEM_BRANCH_RE = re.compile(r"^item/(?P<n>[0-9]+)-")

# The merge queue's temporary branch: `refs/heads/gh-readonly-queue/<base>/pr-<number>-<sha>`. Anchored
# at the END on the `pr-<n>-<sha>` segment, because the base branch name in the middle is arbitrary and
# may itself contain slashes and digits — a non-anchored search would happily read a PR number out of a
# base branch called `release/pr-9-...`.
MERGE_GROUP_REF_RE = re.compile(r"/pr-(?P<n>[0-9]+)-[0-9a-fA-F]{7,40}$")

# THE PR AUTHORIZATION MARKER, in the pull request BODY. Anchored on the OPEN of the HTML comment so a
# body that merely quotes the marker's text in prose without opening a real `<!--` comment cannot match
# — the same discipline `src/FS.GG.Coord.GitHub/Reads.fs`'s `markerRe` applies to `fsgg:claim`, and the
# reason it applies: an un-anchored pattern lets a body that quotes a marker "forge a lock on the item
# it is posted" on. `\s+` after the prefix is load-bearing too — without it,
# `fsgg:pr-authorization-note` and any other `fsgg:pr-authorization<suffix>` would be read as this
# marker, which the authoring-time inversion sweep confirmed by measuring `\s+` -> `\s*` as CAUGHT
# (leg G-9). `DOTALL` because the design doc's own marker spans multiple lines.
AUTH_MARKER_RE = re.compile(r"<!--\s*fsgg:pr-authorization\s+(?P<fields>.*?)-->", re.DOTALL)

# THE MERGE ELECTION MARKER, on the ITEM, one per comment. Anchored at the START of the comment body,
# which is STRICTLY STRONGER than the authorization marker's anchoring and deliberately so: this is the
# marker check 4 grounds everything else in, and a comment that merely QUOTES an election — a review
# note, a design excerpt, this repository's own design doc pasted into a comment — must not be able to
# enter an election it never joined. `fsgg:claim` is anchored exactly this way in `Reads.fs` for the
# same reason. `\s` (not `\s+`) after the prefix mirrors `Reads.fs`'s own `fsgg:claim\s`; the suffix
# hazard is closed either way because `-` is not whitespace.
#
# THE ANCHORING IS ENFORCED TWICE, AND THAT IS MEASURED RATHER THAN ASSERTED. `re.match` anchors at
# position 0 on its own, and the leading `^` anchors at position 0 on its own (no `MULTILINE`), so
# each is individually redundant and jointly they are defence in depth. The authoring-time inversion
# sweep measured exactly that, and it is recorded here because a reader who deletes "the redundant
# `^`" should know what the other half is: dropping `^` ALONE survives the fixture; switching
# `.match` to `.search` ALONE survives it; doing BOTH is CAUGHT (legs G-1 and G-4 for this marker,
# leg G-10 for `fsgg:claim` below). Removing either one silently converts a two-mechanism guarantee
# into a one-mechanism one, which is the state in which the next refactor breaks it for real.
ELECTION_MARKER_RE = re.compile(r"^<!--\s*fsgg:merge-election\s(?P<fields>.*?)-->", re.DOTALL)

# The `fsgg:claim` marker's own grammar, mirrored only as far as this gate needs — see
# `check-claim-generation.py`'s "WHY THIS DUPLICATES A SLICE OF `Reads.winner`" note, which applies
# here unchanged. `worker=` and `lease=` are parsed; `renewed=` is deliberately NOT read, because
# `Writes.claimMarker`'s own comment says it is an OPAQUE renewal token and deriving a clock from it
# would bind this gate to an unspecified internal encoding.
CLAIM_MARKER_RE = re.compile(r"^<!--\s*fsgg:claim\s(?P<fields>.*?)-->", re.DOTALL)

# One `key=value` token inside a marker. Values are non-whitespace by construction — none of these
# markers' fields ever legitimately contains a space — so a value that does simply fails the shape
# check for its field rather than needing a quoting grammar.
FIELD_RE = re.compile(r"(?P<k>[A-Za-z]+)=(?P<v>\S+)")

# ALL SIX fields, unlike `claim-generation`'s four. `opkey` and `grant` are what make checks 4 and 5
# possible at all, so a marker without them is not a marker this gate can evaluate — it is check 1's
# `missing`, not a silently narrower pass.
REQUIRED_AUTH_FIELDS = ("v", "item", "gen", "opkey", "grant", "head")
REQUIRED_ELECTION_FIELDS = ("v", "opkey", "item", "gen", "receiver", "op")
SUPPORTED_VERSION = "1"

# The operation this gate fences, in `Operation.wire`'s spelling (`Merge -> "merge"`).
MERGE_OP = "merge"

GEN_RE = re.compile(r"^[0-9]+$")
HEAD_SHA_RE = re.compile(r"^[0-9a-f]{40}$")
OPKEY_RE = re.compile(r"^[0-9a-f]{64}$")

# There is deliberately NO `ITEM_RE` here, and its absence is a repair rather than an omission. The
# obvious pattern — `^[^\s#]+#[0-9]+$` — ACCEPTS the board's `<repo>#N` shorthand, because that
# shorthand has no whitespace and no second `#`; the recognition corpus in `tests/claim-fence/run.sh`
# caught it doing exactly that on `.github#2719`, which then fell through to the far weaker "does not
# match the branch's item" diagnosis instead of naming `.github#2107`. `item=` is validated by
# `_qualified_item` below — the engine's OWN rule, which requires the `owner/repo` half — so the gate
# and `Operation.compose` cannot disagree about what an item is.

# The engine's own sentinel for "nobody currently holds this item" — `Client.fs`'s
# `ClaimGeneration = ... |> Option.defaultValue "released"`. Reused here so a live generation this gate
# reports never collides with a real, numeric comment id.
RELEASED = "released"


class Missing(Exception):
    """A 404. A real answer — the named subject does not exist — not a failed read."""


class Forbidden(Exception):
    """A 403/401. The token cannot see this; a human must grant a permission, not retry."""


def gh_api(*args: str) -> str:
    """Read the GitHub API via `gh`, distinguishing "not there" (404) from "not allowed" (403) from
    "could not tell" (anything else, retried).

    Mirrors `check-claim-generation.py`'s own helper so a fixture can stub `gh` on PATH the same way.
    The two are independent copies by necessity — Python gates have no shared transport module today —
    but they agree on the one thing that matters: an HTTP status is READ from `gh`'s stderr, never
    guessed at from its prose.
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


# ------------------------------------------------------------------------------------------------
# The operation key — a named re-expression of `FS.GG.Coord.Core.Operation`. See the module docstring's
# "THE OPKEY, RE-EXPRESSED" for what this pins and what it does not.
# ------------------------------------------------------------------------------------------------

def _control_free(value: str) -> bool:
    """`Operation.separatorFree`: no character in Unicode general category `Cc`.

    `Char.IsControl` in .NET is exactly the `Cc` category — C0 (U+0000-U+001F), U+007F, and C1
    (U+0080-U+009F) — so this predicate and the F# one admit the same strings. Refusing ALL control
    characters rather than just `\\n` is that function's own choice, and its reason carries over: it
    leaves the domain's boundary at a rule rather than at "the two characters we happened to think of".
    """
    return not any(unicodedata.category(c) == "Cc" for c in value)


def _well_formed_utf16(value: str) -> bool:
    """`Operation.wellFormedUtf16`: no unpaired surrogate code point.

    Python strings are sequences of code points rather than UTF-16 units, so a "high surrogate followed
    by a low surrogate" pair cannot occur here the way it can in .NET — a well-formed astral character
    is ONE code point in Python and never a surrogate. So the Python-side rule is the simpler one that
    admits exactly the same set: refuse any surrogate code point at all. (In .NET the same input
    arrives as a surrogate PAIR and is admitted by `wellFormedUtf16`; in Python it arrives as
    U+1F600-and-friends, which is not in the surrogate range. The two rules therefore agree on every
    real string, and disagree on nothing that either can represent.)
    """
    return not any(0xD800 <= ord(c) <= 0xDFFF for c in value)


def _server_assigned_id(value: str) -> bool:
    """`Operation.serverAssignedId`: a non-empty run of ASCII digits whose first digit is not zero.

    The leading-zero clause is not pedantry — `0012` and `12` are the same generation and would compose
    two different keys, which is exactly the "one thing keyed two ways" failure an idempotence key
    exists to prevent. It also excludes `0`, which is neither a comment id nor an issue number.
    """
    return len(value) > 0 and value[0] != "0" and all("0" <= c <= "9" for c in value)


def _qualified_repo(value: str) -> bool:
    """`Operation.qualifiedRepo`: `owner/repo` — one `/`, both halves non-empty, no whitespace, no `#`.

    The `#` clause is what stops `owner/repo#12` being accepted as a receiver.
    """
    parts = value.split("/")
    if len(parts) != 2:
        return False
    return all(
        half and not any(c.isspace() or c == "#" for c in half)
        for half in parts
    )


def _qualified_item(value: str) -> bool:
    """`Operation.qualifiedItem`: `owner/repo#N`.

    The board's `<repo>#N` shorthand fails the qualified-repo half, which is the point (`.github#2107`:
    that shorthand is not GitHub grammar, and it is what it costs when it reaches a field GitHub
    parses).
    """
    parts = value.split("#")
    if len(parts) != 2:
        return False
    return _qualified_repo(parts[0]) and _server_assigned_id(parts[1])


def compose_opkey(item: str, generation: str, receiver: str, op: str) -> str:
    """`sha256(item \\n gen \\n receiver \\n op)`, lowercase hex — or a `GateError` naming the component.

    A REFUSAL, never a fallback, exactly as `Operation.compose` is: this core's rule is that a failed or
    malformed read must not be able to masquerade as a legitimate answer (`#266`), so there is no
    "compose it anyway" arm. A refusal here is a NO-VERDICT rather than a finding, because it means this
    gate could not compute the comparison at all — see `classify`'s call site, which reaches it only
    after the shape checks that would have made it a finding.
    """
    for part, value in (("item", item), ("gen", generation), ("receiver", receiver), ("op", op)):
        if not value or not value.strip():
            raise GateError(f"the opkey's `{part}` component is blank")
        if not _control_free(value):
            raise GateError(
                f"the opkey's `{part}` component carries a control character, which is outside the "
                "key's domain — it would let two different pre-images compose one key "
                "(`Operation.separatorFree`)"
            )
        if not _well_formed_utf16(value):
            raise GateError(
                f"the opkey's `{part}` component carries an unpaired surrogate, which UTF-8 encodes to "
                "the replacement character and would let two different components compose one key "
                "(`Operation.wellFormedUtf16`)"
            )
    if not _qualified_item(item):
        raise GateError(f"the opkey's `item` component ({item!r}) is not `owner/repo#N`")
    if not _server_assigned_id(generation):
        raise GateError(
            f"the opkey's `gen` component ({generation!r}) is not a canonical server-assigned comment id"
        )
    if not _qualified_repo(receiver):
        raise GateError(f"the opkey's `receiver` component ({receiver!r}) is not `owner/repo`")
    preimage = "\n".join([item, generation, receiver, op])
    return hashlib.sha256(preimage.encode("utf-8")).hexdigest()


# ------------------------------------------------------------------------------------------------
# Reading the item
# ------------------------------------------------------------------------------------------------

def lease_minutes(cli_value: int | None) -> int:
    """The lease this gate falls back to when a `fsgg:claim` marker does not declare its own.

    `--lease-minutes` beats `$FSGG_CLAIM_LEASE_MIN` beats the engine's own default — the same
    precedence `Options.fs` documents. A malformed env value is a misconfigured shell, not a signal to
    guess a lease: it is a `GateError` (no verdict), not a silent fallback that could paper over a repo
    which deliberately shortened its lease.
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


def marker_lease(fields: dict[str, str], fallback: int, *, cli_override: bool) -> int:
    """The lease THIS marker runs under, in minutes (`.github#2656`'s rule, applied here unchanged).

    An explicit `--lease-minutes` outranks the marker's own `lease=` (an operator deliberately
    tightening the window in CI, which a marker must not be able to opt out of); below that, the
    marker's own declaration outranks this gate's guess. An unparsable or non-positive `lease=` falls
    back rather than refusing the whole verdict: it is one malformed marker among possibly many, and
    refusing over it would let a single junk comment wedge an item's merges.
    """
    if cli_override:
        return fallback
    raw = fields.get("lease")
    if raw is None:
        return fallback
    try:
        n = int(raw)
    except ValueError:
        return fallback
    return n if n > 0 else fallback


def parse_instant(raw: str, what: str) -> datetime:
    """An ISO-8601 instant as an aware UTC `datetime`, or a `GateError` naming `what`."""
    try:
        at = datetime.fromisoformat(raw.replace("Z", "+00:00"))
    except ValueError:
        raise GateError(f"{what} is not an ISO-8601 instant (got {raw!r})") from None
    return at if at.tzinfo is not None else at.replace(tzinfo=timezone.utc)


def expiry_of(at: datetime, minutes: int) -> datetime | None:
    """`at` plus `minutes`, or `None` when no such instant is representable.

    `at + timedelta(minutes=n)` raises `OverflowError` once the lease passes roughly 4.2e9 minutes, and
    a .NET tick count — which is what a claim marker's `renewed=` field holds — is ~6.4e17. Returning
    `None` rather than raising is what lets the caller apply the rule `marker_lease` already states for
    every other malformation: a lease this gate cannot use falls back to one it can. This is
    `.github#2673`'s F1, fixed at its source rather than caught downstream.
    """
    try:
        return at + timedelta(minutes=minutes)
    except (OverflowError, OSError, ValueError):
        return None


class Election:
    """One readable `fsgg:merge-election` marker, reduced to what check 4 needs."""

    __slots__ = ("id", "fields")

    def __init__(self, cid: int, fields: dict[str, str]) -> None:
        self.id = cid
        self.fields = fields


class ItemState:
    """What the markers on an item say, as of one instant.

    Both of this gate's live questions are answered from ONE read of the item's comments, which is
    deliberate on two counts: it halves the REST spend on the budget ADR-0034 §3 names as the one that
    dies, and — more importantly — checks 3 and 4 then see the SAME snapshot. Two reads could observe a
    claim generation from before a re-claim and an election from after it, and report a coherent-looking
    verdict about a world that never existed.

    `claim_generation` is the live winning `fsgg:claim` comment id, or the engine's `"released"`
    sentinel. `elections` is every readable election marker, unfiltered — check 4 does its own keying.
    """

    __slots__ = ("claim_generation", "elections")

    def __init__(self, claim_generation: str, elections: list[Election]) -> None:
        self.claim_generation = claim_generation
        self.elections = elections


def read_item_state(
    repo: str, number: int, fallback_lease: int, *,
    cli_override: bool = False, now: datetime | None = None,
) -> ItemState:
    """Read `repo#number`'s comments once, and project both the live claim generation and the elections.

    THE CLAIM HALF re-expresses `Reads.winner`'s rule far enough to answer "what is the live claim
    generation right now": keep the comments that OPEN with an anchored `fsgg:claim` marker, drop any
    whose `updated_at` age exceeds that marker's own lease, and take the LOWEST surviving comment id. An
    unreadable timestamp is treated as NOT stale — `Reads.isStale`'s own reading, whose comment explains
    why: inventing a lease age out of a missing field is "a confident sentence with nothing behind it",
    and reading it as expiry would reap a live claim on the strength of a field we failed to parse.

    THE ELECTION HALF applies NO lease and NO liveness judgement of any kind, which is §4.2's whole
    point: an election is append-only, never deleted, never renewed and never expired, so "the lowest
    id bearing this opkey" is a historical fact stable forever. A staleness filter here would recreate
    exactly the fail-ALWAYS defect §4.2 was written to remove.

    Raises `Missing` for a 404, `Forbidden` for a 403/401, and lets `Unreachable` propagate — none is
    caught here; the caller decides what each means for ITS verdict.
    """
    raw = gh_api(
        "--paginate", "--jq", ".[] | {id, body, updated_at}",
        f"repos/{repo}/issues/{number}/comments",
    )
    now = now or datetime.now(timezone.utc)
    live: list[tuple[int, int]] = []          # (id, unused) — only the id decides the winner
    elections: list[Election] = []
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
        if not isinstance(body, str) or not isinstance(cid, int):
            # A marker with no readable numeric id cannot be placed in the id order at all. This gate's
            # only questions are "which id wins" and "is this id the lowest", and a marker that can
            # never win an id-ordered race is excluded from the candidate set rather than promoted to
            # it — the same treatment `check-claim-generation.py` gives the case.
            continue

        claim = CLAIM_MARKER_RE.match(body)
        if claim is not None:
            fields = {fm.group("k"): fm.group("v") for fm in FIELD_RE.finditer(claim.group("fields"))}
            lease = marker_lease(fields, fallback_lease, cli_override=cli_override)
            age_seconds = -1
            updated_at = comment.get("updated_at")
            if isinstance(updated_at, str):
                try:
                    at = parse_instant(updated_at, "a comment's updated_at")
                except GateError:
                    at = None
                if at is not None:
                    age_seconds = int((now - at).total_seconds())
                    if expiry_of(at, lease) is None:
                        # A lease from which no expiry instant is representable is not a lease this
                        # gate will use — the same rule `marker_lease` applies to a non-numeric one,
                        # extended to the malformation only this scope can detect.
                        lease = fallback_lease
            # The same boundary `Reads.isStale` draws: strictly GREATER than the lease is lapsed, and a
            # negative (unreadable) age is not lapsed at all.
            if not (age_seconds >= 0 and age_seconds > lease * 60):
                live.append((cid, 0))
            continue

        election = ELECTION_MARKER_RE.match(body)
        if election is not None:
            fields = {fm.group("k"): fm.group("v") for fm in FIELD_RE.finditer(election.group("fields"))}
            elections.append(Election(cid, fields))

    generation = str(min(live)[0]) if live else RELEASED
    return ItemState(generation, elections)


def find_authorizations(body: str) -> list[dict[str, str]]:
    """Every `fsgg:pr-authorization` marker in `body`, each as its raw `{key: value}` fields."""
    out: list[dict[str, str]] = []
    for m in AUTH_MARKER_RE.finditer(body):
        out.append({fm.group("k"): fm.group("v") for fm in FIELD_RE.finditer(m.group("fields"))})
    return out


# ------------------------------------------------------------------------------------------------
# The verdict
# ------------------------------------------------------------------------------------------------

def classify(args: argparse.Namespace, body: str) -> tuple[str, str] | None:
    """The verdict as `(check, message)`, or `None` when all five substantive checks pass.

    `check` names WHICH of §6.3's checks failed — `"check1"` .. `"check5"` — so a reader is told the
    thing that is wrong rather than a colour. Check 6 is not produced here: a read that could not be
    made raises, and `main` maps it to exit 2/3 directly.

    THE ORDER IS THE DESIGN'S OWN, AND IT IS NOT ARBITRARY. Checks 1, 2 and 5 are decidable from the PR
    body alone plus this repository's name; 3 and 4 need the live item. Deciding the free ones first
    means a malformed marker is diagnosed without spending a REST read, and — the part that matters —
    the live read happens exactly once, for both of the checks that need it.
    """
    # ---- Check 1: exactly one well-formed authorization marker -----------------------------------
    matches = find_authorizations(body)
    if len(matches) == 0:
        return "check1", (
            "no `fsgg:pr-authorization` marker in the PR body. This branch claims to deliver "
            f"{args.expected_item} (its name matches `item/<n>-`), but the PR carries no authorization "
            "at all — exactly the `#1858` shape: an executor that never called a coordination verb has "
            "nothing to write into one (design doc §8.4)."
        )
    if len(matches) > 1:
        return "check1", (
            f"{len(matches)} `fsgg:pr-authorization` markers in the PR body — exactly one is required. "
            "An ambiguous PR cannot be resolved to a single authorization, so this is treated the same "
            "as none."
        )
    fields = matches[0]
    absent = [f for f in REQUIRED_AUTH_FIELDS if f not in fields]
    if absent:
        return "check1", (
            "the `fsgg:pr-authorization` marker is missing required field(s): "
            f"{', '.join(absent)}. This gate implements all six checks of design doc §6.3, so it needs "
            "`opkey=` and `grant=` as well as the four `claim-generation` reads — a marker without them "
            "cannot be grounded in an election, and an ungrounded marker is the decorative case §6.3 "
            "names."
        )
    if fields["v"] != SUPPORTED_VERSION:
        return "check1", (
            f"the `fsgg:pr-authorization` marker names `v={fields['v']}`, which this gate does not "
            f"understand (supported: v={SUPPORTED_VERSION})."
        )
    if not _qualified_item(fields["item"]):
        return "check1", (
            f"`item={fields['item']}` is not shaped like `owner/repo#n`, so this authorization cannot "
            "be resolved to any item at all. The board's `<repo>#n` shorthand lands here: it is not "
            "GitHub grammar (`.github#2107`)."
        )
    if fields["item"] != args.expected_item:
        return "check1", (
            f"`item={fields['item']}` does not match {args.expected_item}, the item this PR's own "
            f"branch (`{args.head_ref}`) declares it delivers."
        )
    if not GEN_RE.match(fields["gen"]):
        return "check1", (
            f"`gen={fields['gen']}` is not shaped like a claim-marker comment id (expected decimal "
            "digits only) — this authorization cannot correspond to any real claim generation."
        )
    if not GEN_RE.match(fields["grant"]):
        return "check1", (
            f"`grant={fields['grant']}` is not shaped like an election-marker comment id (expected "
            "decimal digits only)."
        )
    if not OPKEY_RE.match(fields["opkey"]):
        return "check1", (
            f"`opkey={fields['opkey']}` is not a 64-character lowercase hex digest, so it cannot be "
            "the `sha256(item \\n gen \\n receiver \\n op)` design doc §3.3 specifies."
        )

    # ---- Check 2: `head=` equals the pull request's current head SHA -----------------------------
    if fields["head"].lower() != args.head_sha.lower():
        return "check2", (
            f"`head={fields['head']}` does not equal this pull request's current head SHA "
            f"({args.head_sha}). A push after the authorization was written means the authorization is "
            "for a different artifact — the same rule `delivery` already enforces client-side "
            "(`src/FS.GG.Coord.Cli/Client.fs`: a merge is refused because \"the PR is no longer at the "
            "inspected head\")."
            + (
                "\nNOTE, because this evaluation came from a MERGE GROUP: the SHA compared above is the "
                "PULL REQUEST's head, read back from the API, NOT the merge queue's temporary merge "
                "commit. The queue's own SHA is never what an authorization binds to."
                if args.from_merge_group else ""
            )
        )

    # ---- Check 5: the opkey recomputes -----------------------------------------------------------
    # Decided before 3 and 4 because it is free — it needs no network — and because checks 3 and 4 are
    # about facts keyed BY the opkey, so evaluating them against a key that does not recompute would be
    # answering a question about the wrong key.
    expected = compose_opkey(fields["item"], fields["gen"], args.repo, MERGE_OP)
    if fields["opkey"] != expected:
        return "check5", (
            f"`opkey={fields['opkey']}` does not recompute. From this marker's own `item="
            f"{fields['item']}` and `gen={fields['gen']}`, this repository as the receiver "
            f"({args.repo}), and `op={MERGE_OP}`, design doc §3.3's "
            "`sha256(item \\n gen \\n receiver \\n op)` is:\n"
            f"    {expected}\n"
            "The key is a pure function of those four components, so a mismatch means the marker names "
            "a different operation from the one this merge would be."
        )

    # ---- Checks 3 and 4: one live read of the item -----------------------------------------------
    state = read_item_state(
        args.repo, args.item_number, args.lease_minutes,
        cli_override=args.lease_from_cli, now=args.now,
    )

    if state.claim_generation == RELEASED:
        return "check3", (
            f"{args.expected_item} has no LIVE `fsgg:claim` winner — it was released, reaped, or its "
            f"lease lapsed — but this PR's authorization names generation `gen={fields['gen']}`. There "
            "is no live tenancy for that generation to be current under, so the authorization is "
            "ungrounded. Design doc §8.1 routes recovery as re-authoring rather than retry: re-take the "
            "item and make a fresh `delivery` call."
        )
    if state.claim_generation != fields["gen"]:
        return "check3", (
            f"{args.expected_item} is currently held under claim generation {state.claim_generation}, "
            f"but this PR's authorization names generation `gen={fields['gen']}`. The claim has moved "
            "on since this PR was authorized (released and reclaimed, stolen, or reaped). Comment ids "
            "are server-assigned and monotone (design doc §3.1), so the old generation can never become "
            "current again — re-take the item and re-author the authorization."
        )

    # Check 4. The candidate set is every election marker on the item bearing THIS opkey. Keying on the
    # opkey rather than on the item is what makes cross-item interference structurally impossible (§4.2
    # consequence 1): the opkey contains the item AND the generation, so a worker who legitimately
    # elected for a different item, or under a different tenancy of this one, elects nothing here.
    candidates = [e for e in state.elections if e.fields.get("opkey") == fields["opkey"]]
    if not candidates:
        return "check4", (
            f"no `fsgg:merge-election` marker on {args.expected_item} bears "
            f"`opkey={fields['opkey']}`, but this PR's authorization names `grant={fields['grant']}` as "
            "one. This is the check a forger cannot satisfy by typing (design doc §6.3): a forger who "
            "invents a comment id fails this existence read, and a forger who posts a real election "
            "marker to obtain a real id has thereby entered the election and can still only be lowest "
            "once.\nAn election marker must OPEN the comment it is posted in — a comment that merely "
            "quotes one in prose is not one, the same discipline `Reads.fs` applies to `fsgg:claim`."
        )
    winner = min(candidates, key=lambda e: e.id)
    if str(winner.id) != fields["grant"]:
        others = ", ".join(str(e.id) for e in sorted(candidates, key=lambda e: e.id))
        return "check4", (
            f"`grant={fields['grant']}` is NOT the lowest-id merge election for this opkey. "
            f"{len(candidates)} election marker(s) on {args.expected_item} bear "
            f"`opkey={fields['opkey']}` — comment id(s) {others} — and the lowest is {winner.id}.\n"
            "THIS IS WHERE A SECOND EXECUTOR IS REFUSED. Two contexts sharing one live generation "
            "compute a byte-identical opkey and pass checks 1, 2, 3 and 5 identically; they elect on "
            "the SAME opkey, and \"lowest id wins\" is computed identically by every reader (design doc "
            f"§4.2, §8.3). This authorization lost that election. Comment {winner.id} won it, and its "
            "merge is the one this generation admits."
        )

    # The winner is the marker `grant=` names; now its own recorded facts must agree with the
    # authorization. Without this, an election posted for one (item, generation, receiver) could
    # authorize a merge for another that happened to share a key by construction error.
    absent = [f for f in REQUIRED_ELECTION_FIELDS if f not in winner.fields]
    if absent:
        return "check4", (
            f"the winning merge election (comment {winner.id}) is missing required field(s): "
            f"{', '.join(absent)}. Design doc §6.3 check 4 requires the election to RECORD the item, "
            "generation and receiver it was elected for, so that they can be matched against the "
            "authorization; a marker that records none of them grounds nothing."
        )
    if winner.fields["v"] != SUPPORTED_VERSION:
        return "check4", (
            f"the winning merge election (comment {winner.id}) names `v={winner.fields['v']}`, which "
            f"this gate does not understand (supported: v={SUPPORTED_VERSION})."
        )
    for key, expected_value, what in (
        ("item", fields["item"], "the item"),
        ("gen", fields["gen"], "the claim generation"),
        ("receiver", args.repo, "the receiver"),
        ("op", MERGE_OP, "the operation"),
    ):
        if winner.fields[key] != expected_value:
            return "check4", (
                f"the winning merge election (comment {winner.id}) records `{key}="
                f"{winner.fields[key]}`, but {what} this authorization names is `{expected_value}`. "
                "Design doc §6.3 check 4: \"Recorded fields disagreeing with the authorization -> RED\". "
                "An election that was held for a different operation cannot authorize this one, however "
                "its opkey was arrived at."
            )

    return None


def resolve_pull_request(repo: str, ref: str) -> tuple[int, str, str, str]:
    """The pull request a merge-group ref names, as `(number, body, head_ref, head_sha)`.

    See the module docstring's "`merge_group` IS NOT OPTIONAL" for why this exists: the merge-group
    payload carries no pull request, and its `head_sha` is the queue's temporary merge commit rather
    than the pull request's head, which is what check 2 must compare against.
    """
    m = MERGE_GROUP_REF_RE.search(ref)
    if m is None:
        raise GateError(
            f"the merge-group ref {ref!r} does not end in `pr-<number>-<sha>`, so no pull request can "
            "be resolved from it. GitHub's merge queue names its temporary branch "
            "`refs/heads/gh-readonly-queue/<base>/pr-<number>-<sha>`; a ref in any other shape is one "
            "this gate has never seen and must not guess at."
        )
    number = int(m.group("n"))
    raw = gh_api("--jq", "{body, ref: .head.ref, sha: .head.sha}", f"repos/{repo}/pulls/{number}")
    try:
        pr = json.loads(raw.strip() or "{}")
    except json.JSONDecodeError:
        raise GateError(f"could not parse the API's response for {repo}#{number}") from None
    head_ref, head_sha = pr.get("ref"), pr.get("sha")
    if not isinstance(head_ref, str) or not isinstance(head_sha, str):
        raise GateError(
            f"{repo}#{number} did not report a readable head ref and SHA, so this merge-group "
            "evaluation has no artifact to bind an authorization to."
        )
    return number, pr.get("body") or "", head_ref, head_sha


def build_parser() -> argparse.ArgumentParser:
    p = argparse.ArgumentParser(description="The merge fence — design doc §6.3, all six checks.")
    p.add_argument("--repo", required=True, help="owner/name of the repository the merge lands in")
    p.add_argument("--head-ref", help="the pull request's head branch (pull_request events)")
    p.add_argument("--head-sha", help="the pull request's head SHA (pull_request events)")
    p.add_argument("--body", help="file holding the PR body; default stdin (pull_request events)")
    p.add_argument("--merge-group-ref", help="refs/heads/gh-readonly-queue/<base>/pr-<n>-<sha>")
    p.add_argument("--lease-minutes", type=int, default=None)
    p.add_argument("--now", default=None, help="evaluate as at this ISO-8601 instant")
    return p


def main(argv) -> int:
    args = build_parser().parse_args(list(argv))

    if not _qualified_repo(args.repo):
        raise GateError(f"--repo must be `owner/name` (got {args.repo!r})")

    args.lease_from_cli = args.lease_minutes is not None
    args.lease_minutes = lease_minutes(args.lease_minutes)
    args.now = parse_instant(args.now, "--now") if args.now else None
    args.from_merge_group = args.merge_group_ref is not None

    if args.from_merge_group:
        if args.head_ref or args.head_sha or args.body:
            raise GateError(
                "--merge-group-ref resolves the pull request itself, so --head-ref/--head-sha/--body "
                "must not also be given. Supplying both would let the caller's idea of the head "
                "disagree with the API's, and check 2 exists to catch exactly that kind of "
                "disagreement rather than to be handed it."
            )
        try:
            _, body, head_ref, head_sha = resolve_pull_request(args.repo, args.merge_group_ref)
        except Missing:
            raise GateError(
                f"the pull request named by {args.merge_group_ref!r} does not exist in {args.repo}."
            ) from None
        args.head_ref, args.head_sha = head_ref, head_sha
    else:
        if not args.head_ref or not args.head_sha:
            raise GateError("--head-ref and --head-sha are required without --merge-group-ref")
        body = (
            open(args.body, encoding="utf-8").read() if args.body else sys.stdin.read()
        )

    if not HEAD_SHA_RE.match(args.head_sha.lower()):
        raise GateError(f"--head-sha must be a 40-character hex SHA (got {args.head_sha!r})")

    m = ITEM_BRANCH_RE.match(args.head_ref)
    if m is None:
        print(
            f"check-claim-fence: OK — `{args.head_ref}` is not an `item/<n>-*` branch, so this pull "
            "request does not claim to deliver a board item and there is nothing to fence. (Reported "
            "rather than skipped: a required context must report on EVERY pull request, so "
            "applicability is decided here and never by a `paths:` filter — design doc §6.2.)"
        )
        return ExitCode.OK

    args.item_number = int(m.group("n"))
    args.expected_item = f"{args.repo}#{args.item_number}"

    try:
        finding = classify(args, body)
    except Missing:
        print(
            f"check-claim-fence: [check1] {args.expected_item} does not exist, so this PR's "
            "authorization cannot correspond to any live claim generation or election."
        )
        return ExitCode.FINDING

    if finding is None:
        print(
            f"check-claim-fence: OK — all five substantive checks of design doc §6.3 pass for "
            f"{args.expected_item} at head {args.head_sha}."
        )
        print(
            "check-claim-fence: the merge election grounding this authorization is a HISTORICAL fact "
            "(§4.2) — append-only, never deleted, never expired — so checks 4 and 5 give the same "
            "answer on any runner at any time. Check 3 is the live one, and it is the one whose green "
            "is perishable: it asserts that at this instant the item is held under the generation this "
            "PR names."
        )
        return ExitCode.OK

    check, message = finding
    print(f"check-claim-fence: [{check}] {message}")
    return ExitCode.FINDING


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name="check-claim-fence"))
