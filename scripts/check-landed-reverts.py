#!/usr/bin/env python3
r"""Refuse a PR whose merge would silently revert content that recently landed on its base (.github#4730).

THE INCIDENT. #4977's squash merge (99c83d57) deleted about 2,530 lines that #4968 (dd7665f6) had
delivered one commit earlier: the routine and publication preflight routes, V2RoutesTests, the
differential tests, the BoardV2Import changes. Main stayed green because the tests were deleted with the
code. Nothing looked for it: review read the branch's own commits, and CI judged the tree it was given.

READ THIS BEFORE "FIXING" THE DEFINITION. The first account of the incident was "the branch was based
before #4968 and its merge kept the older side". The objects say otherwise. #4977's first commit
(3c6498f1) has dd7665f6 -- #4968's own squash -- as its PARENT, so #4968's lines are in the merge base,
in the branch point and in the base. The commit simply carries a tree in which those 13 files are the
OLDER version (consistent with a stale working tree committed on top of a newer parent; the mechanism is
inferred from the objects, not observed). Measured: `git diff dd7665f6 3c6498f1` is +755/-2597 for a 26-file "census" change. So a
check of "lines on the base that did not exist at merge-base(base, head)" finds NOTHING here: the
deleted lines existed at the merge base. The check therefore measures recency, not the merge base:

  B  = the branch point: the earliest commit the PR's own commits were built on
       (the oldest of the parents of `base..head` that are not themselves in `base..head`).
  B' = the floor of the lookback window: B stepped back --lookback first-parent commits (default 100) or
       --lookback-days days of commit date (default 7), whichever reaches further. Everything the base
       gained since B' is "recently landed". The stale tree of the incident was ONE commit older than B.
  R  = the squash result: `git merge-tree --write-tree base head`, i.e. the real three-way merge GitHub
       performs, not the head tree. (For #4977 it is byte-identical to the merged tree 610adc96.)

A base line is REVERTED when: it was added between B' and the base; the PR's merge R no longer has it; and
R neither replaces it with new text nor keeps it somewhere else in the same file.

EDITING vs REVERTING. This is the whole difficulty, and it is defined on the text, not on intent:

  * EDIT   -- credit is PER LINE, never per hunk. Each line the hunk adds whose text the file never held in
             the window (`git rev-list floor..base -- path`, every version, plus the base and floor
             blobs) offsets one dropped landed line. The PR wrote something new where the lines were.
  * REVERT -- every dropped landed line left over: a pure deletion, a restored older version, a
             replacement made of text the file already held. One new line beside a stale restore, or a
             `# removed` comment after a deletion, hides one line, not the hunk. (An all-or-nothing hunk
             rule hid 100 lines behind 1; the price of per-line credit is that consolidating 50 landed
             lines into 20 new ones counts 30. Measured in the PR that introduced it.)
  * MOVE   -- a removed landed line whose text still occurs in the same file in R as often as before
             (cut and paste, reordering). Not counted.
  * RESURRECTION -- a file the window deleted or renamed away that R has again counts its restored lines
             (restored minus new) as dropped; it is attributed to the commit that removed it.
  * A file added after B' and absent from R, or whose blob in R equals its blob at B', is a whole-file
             REVERT. A binary file is judged by blob identity only (restored or deleted -> REVERT;
             otherwise EDIT).

Blank and punctuation-only lines (no letter or digit) never count either way.

TWO TIERS, MEASURED. Replaying the last 281 squash-merged PRs on this repo (every one of which the
computed squash result reproduced byte-for-byte), 16 dropped between 1 and 11 recently landed lines
without replacing them (the three inspected were deliberate: a trimmed paragraph, a retired path filter),
and exactly one,
#4977, dropped more, 2,294. So the check REFUSES (exit 1) at --refuse-lines undeclared dropped lines
(default 30), and below that exits 0 with a `::warning` that names the files: loud, not blocking. A
binary file restored or deleted counts as the whole bound. Lower the bound only with a replay behind it.

DECLARING A REVERT. A deliberate revert is allowed, but the PR must say what it reverts, by commit, in
the PR body or in any commit message of the branch:

    <!-- fsgg:declared-revert/v1 reverts=dd7665f6,#4968,#4976:tests/** -->

Tokens are commit SHAs (7-40 hex) or `#<PR>` (resolved to the squash commit whose subject ends `(#N)`),
optionally followed by `:<path-glob>` to limit the token to the files it is meant for. Each reverted line
is attributed with `git blame` and is covered only when the commit that introduced it is named, within
that token's scope; the output says how many lines each token covered, and a token that covered nothing. Naming a commit that does not account for the reverted lines declares nothing. A commit
message marker lands in the squash message, so the declaration is durable in history; the body is read
from the event payload and, deliberately, an edit to it does NOT re-run CI (#4730 CI load): to declare
after the fact, push a commit whose message carries the marker.

FAIL CLOSED (#266). Exit 0 is the only green. 1 = refused. 3 = no verdict: shallow history, an unreadable
object, no common ancestor, a merge conflict, or any cap in CAPS reached (never a silent truncation). A
maintainer can raise a cap with --max-<name>, up to ten times its default: docs/coordination/landed-revert-guard.md.

EXCLUSIONS ARE EXPLICIT AND COME FROM THE BASE. `registry/landed-revert-allowlist.json` lists path-segment
globs (`*` never crosses `/`) with a mandatory reason, for files a PR legitimately regenerates from scratch.
It is read from the base commit, never the PR head, so a PR cannot exempt itself; the workflow also runs
this script and scripts/lib/gate.py from the base. A glob made only of wildcards is an error.
"""

from __future__ import annotations

import argparse
import fnmatch
import json
import os
import re
import subprocess
import sys
from collections import Counter
from dataclasses import dataclass, field
from typing import Callable

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

from lib.gate import ExitCode, GateError, run  # noqa: E402

NAME = "check-landed-reverts"

DEFAULT_LOOKBACK = 100     # first-parent commits before the branch point still counted as landed ...
DEFAULT_LOOKBACK_DAYS = 7  # ... or this many days before the branch point's commit date, whichever reaches further
REFUSE_LINES = 30          # undeclared dropped landed lines at which the PR is refused; fewer only warn
# Every cap that can stop the check is a NO VERDICT (exit 3), never a silent truncation. A maintainer can
# raise one with --max-<name> (at most 10x the default) after review: docs/coordination/landed-revert-guard.md.
CAPS = {
    "pr-commits": 2000,        # commits in base..head
    "paths": 20000,            # changed paths per raw diff
    "candidates": 1500,        # files changed by both the landed work and the PR
    "blame-files": 200,        # files with dropped lines that need git blame
    "history-versions": 300,   # versions of one file in the window
    "window-commits": 3000,    # first-parent commits in the lookback window
}
CAP_CEILING = 10
GIT_TIMEOUT = 180
DEFAULT_ALLOWLIST = "registry/landed-revert-allowlist.json"
MARKER_RE = re.compile(r"<!--\s*fsgg:declared-revert/v1\s+reverts=([^\s>]*)\s*-->")
TOKEN_RE = re.compile(r"^(?:#(\d{1,7})|([0-9a-fA-F]{7,40}))(?::(\S+))?$")
HUNK_RE = re.compile(r"^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@")
SHA_RE = re.compile(r"^[0-9a-f]{40}$")


# ---------------------------------------------------------------------------------------------------
# git plumbing. Every failure to READ is a GateError: "I could not look" must never be a green.
# ---------------------------------------------------------------------------------------------------

def git(repo: str, *args: str, ok: tuple[int, ...] = (0,), input: bytes | None = None) -> subprocess.CompletedProcess:
    env = dict(os.environ, LC_ALL="C", GIT_TERMINAL_PROMPT="0", GIT_OPTIONAL_LOCKS="0")
    try:
        done = subprocess.run(
            ["git", "-C", repo, "-c", "core.quotePath=false", "-c", "diff.renames=false", *args],
            capture_output=True, timeout=GIT_TIMEOUT, env=env, check=False, input=input)
    except (OSError, subprocess.TimeoutExpired) as error:
        raise GateError(f"git {args[0]} could not run: {error}") from error
    if done.returncode not in ok:
        detail = done.stderr.decode("utf-8", "replace").strip().splitlines()[:3]
        raise GateError(f"git {' '.join(args[:3])} failed (rc={done.returncode}): {' | '.join(detail)}")
    return done


def text(done: subprocess.CompletedProcess) -> str:
    return done.stdout.decode("utf-8", "replace")


def commit_of(repo: str, rev: str, what: str) -> str:
    out = text(git(repo, "rev-parse", "--verify", "--quiet", f"{rev}^{{commit}}", ok=(0, 1))).strip()
    if not SHA_RE.fullmatch(out):
        raise GateError(f"{what} {rev!r} is not a commit this clone can read (shallow or unfetched?)")
    return out


def cap_hit(name: str, seen: int) -> GateError:
    return GateError(
        f"{seen} exceeds the {name} cap {CAPS[name]}. This is NO VERDICT, not a pass: a maintainer reviews the PR and may "
        f"re-run the check locally with --max-{name} (at most {CAP_CEILING}x the default); see "
        f"docs/coordination/landed-revert-guard.md#override")


def glob_match(pattern: str, path: str) -> bool:
    """Path-segment glob: `*` and `?` never cross `/`; a whole `**` segment matches any number of segments."""
    pat, seg = pattern.split("/"), path.split("/")

    def walk(i: int, j: int) -> bool:
        if i == len(pat):
            return j == len(seg)
        if pat[i] == "**":
            return any(walk(i + 1, k) for k in range(j, len(seg) + 1))
        return j < len(seg) and fnmatch.fnmatchcase(seg[j], pat[i]) and walk(i + 1, j + 1)

    return walk(0, 0)


@dataclass
class Entry:
    status: str
    old_path: str
    new_path: str
    old_mode: str
    new_mode: str
    old_sha: str
    new_sha: str


def raw_diff(repo: str, a: str, b: str) -> list[Entry]:
    out = git(repo, "diff-tree", "-r", "-z", "--raw", "--no-abbrev", "-M", "-l2000", a, b).stdout
    parts = out.decode("utf-8", "replace").split("\0")
    entries: list[Entry] = []
    i = 0
    while i < len(parts):
        head = parts[i]
        if not head.startswith(":"):
            i += 1
            continue
        fields = head[1:].split(" ")
        if len(fields) != 5:
            raise GateError(f"unparsable raw diff header {head!r}")
        status = fields[4]
        if status[0] in "RC":
            old_path, new_path = parts[i + 1], parts[i + 2]
            i += 3
        else:
            old_path = new_path = parts[i + 1]
            i += 2
        entries.append(Entry(status[0], old_path, new_path, fields[0], fields[1], fields[2], fields[3]))
        if len(entries) > CAPS["paths"]:
            raise cap_hit("paths", len(entries))
    return entries


@dataclass
class Hunk:
    old_start: int
    old_count: int
    new_start: int
    new_count: int
    minus: list[str] = field(default_factory=list)
    plus: list[str] = field(default_factory=list)


def blob_lines(repo: str, sha: str) -> tuple[bool, list[str]]:
    """-> (binary, lines) of one blob."""
    data = git(repo, "cat-file", "blob", sha).stdout
    if b"\0" in data[:8000]:
        return True, []
    lines = data.decode("utf-8", "replace").split("\n")
    if lines and lines[-1] == "":
        lines.pop()
    return False, lines


def blob_hunks(repo: str, a: str | None, b: str | None) -> tuple[bool, list[Hunk]]:
    """Hunks (zero context) between two blobs; a missing blob (None) is an empty file. -> (binary, hunks)."""
    if a is None or b is None:
        present = b if a is None else a
        if present is None:
            return False, []
        binary, lines = blob_lines(repo, present)
        if binary:
            return True, []
        if not lines:
            return False, []
        if a is None:
            return False, [Hunk(0, 0, 1, len(lines), [], lines)]
        return False, [Hunk(1, len(lines), 0, 0, lines, [])]
    out = text(git(repo, "diff", "--no-color", "--no-ext-diff", "--no-textconv", "-U0", "--diff-algorithm=histogram",
                   a, b, ok=(0, 1)))
    hunks: list[Hunk] = []
    current: Hunk | None = None
    for line in out.split("\n"):
        if line.startswith("Binary files ") or line.startswith("GIT binary patch"):
            return True, []
        m = HUNK_RE.match(line)
        if m:
            current = Hunk(int(m[1]), int(m[2]) if m[2] is not None else 1,
                           int(m[3]), int(m[4]) if m[4] is not None else 1)
            hunks.append(current)
        elif current is not None:
            if line.startswith("-"):
                current.minus.append(line[1:])
            elif line.startswith("+"):
                current.plus.append(line[1:])
    return False, hunks


def substantive(line: str) -> bool:
    return any(ch.isalnum() for ch in line)


# ---------------------------------------------------------------------------------------------------
# the subject: branch point, lookback, squash result
# ---------------------------------------------------------------------------------------------------

def branch_point(repo: str, base: str, head: str) -> str | None:
    """The commit the PR's own commits were built on, or None when head is already contained in base."""
    if text(git(repo, "rev-parse", "--is-shallow-repository")).strip() != "false":
        raise GateError("the clone is shallow; the branch point and landed history cannot be established")
    rows = text(git(repo, "rev-list", "--parents", f"{base}..{head}")).splitlines()
    if len(rows) > CAPS["pr-commits"]:
        raise cap_hit("pr-commits", len(rows))
    if not rows:
        return None
    own = {row.split()[0] for row in rows}
    boundary = sorted({p for row in rows for p in row.split()[1:] if p not in own})
    if not boundary:
        raise GateError("the PR history has no ancestor on the base (unrelated histories)")
    if len(boundary) == 1:
        return boundary[0]
    done = git(repo, "merge-base", "--octopus", *boundary, ok=(0, 1))
    found = text(done).split()
    if not found:
        raise GateError("the PR's parents share no common ancestor")
    return found[0]


def window_floor(repo: str, point: str, commits: int, days: int) -> str:
    """The commit before the lookback window: `commits` first-parent steps back from the branch point, or the
    last first-parent commit older than `days` before the branch point's commit date, whichever reaches further."""
    by_count = text(git(repo, "rev-list", "--first-parent", "-n", str(commits + 1), point)).split()
    if not by_count:
        raise GateError(f"cannot walk first-parent history from {point[:8]}")
    floor = by_count[-1]
    stamp = int(text(git(repo, "log", "-1", "--format=%ct", point)).strip())
    recent = text(git(repo, "rev-list", "--first-parent", f"--after={stamp - days * 86400}", "-n",
                      str(CAPS["window-commits"] + 1), point)).split()
    if len(recent) > CAPS["window-commits"]:
        raise cap_hit("window-commits", len(recent))
    if recent:
        before = text(git(repo, "rev-list", "--first-parent", "-n", "1", f"{recent[-1]}^", ok=(0, 128))).strip()
        # Both candidates are on the same first-parent chain: keep the older one.
        before = before if SHA_RE.fullmatch(before) else recent[-1]      # the window reaches the root commit
        if git(repo, "merge-base", "--is-ancestor", before, floor, ok=(0, 1)).returncode == 0:
            floor = before
    return floor


def squash_result(repo: str, base: str, head: str) -> str:
    done = git(repo, "merge-tree", "--write-tree", "--no-messages", base, head, ok=(0, 1))
    if done.returncode == 1:
        raise GateError("head does not merge cleanly into base (conflicts); update the branch first")
    tree = text(done).split("\n", 1)[0].strip()
    if not SHA_RE.fullmatch(tree):
        raise GateError(f"git merge-tree returned no tree: {tree!r}")
    return tree


# ---------------------------------------------------------------------------------------------------
# the analysis
# ---------------------------------------------------------------------------------------------------

@dataclass
class Finding:
    path: str
    kind: str                                   # "whole-file" | "binary" | "lines" | "resurrected"
    lines: list[int]                            # base line numbers of the reverted landed lines
    weight: int = 0                             # what this finding adds to the refusal count
    commits: dict[str, str] = field(default_factory=dict)       # introducing commit -> subject
    per_line: dict[int, str] = field(default_factory=dict)      # reverted line -> introducing commit
    uncovered_lines: int = 0                    # weight not accounted for by a declared commit
    uncovered: set[str] = field(default_factory=set)            # introducing commits not declared
    covered_by: dict[str, int] = field(default_factory=dict)    # declaration token -> lines it covered here


def landed_lines(hunks: list[Hunk]) -> dict[int, str]:
    out: dict[int, str] = {}
    for h in hunks:
        for offset, content in enumerate(h.plus):
            out[h.new_start + offset] = content
    return out


def file_history(repo: str, floor: str, base: str, path: str, blobs: list[str | None]) -> set[str]:
    """Every substantive line (stripped) the file held in any version committed in floor..base, plus `blobs`."""
    shas = text(git(repo, "rev-list", f"{floor}..{base}", "--", path)).split()
    if len(shas) > CAPS["history-versions"]:
        raise cap_hit("history-versions", len(shas))
    specs = [f"{c}:{path}" for c in shas] + [b for b in blobs if b]
    seen: set[str] = set()
    if not specs:
        return seen
    data = git(repo, "cat-file", "--batch", input="".join(f"{x}\n" for x in specs).encode()).stdout
    pos = 0
    while pos < len(data):
        end = data.index(b"\n", pos)
        header = data[pos:end].decode("utf-8", "replace").split(" ")
        pos = end + 1
        if len(header) != 3 or header[1] != "blob":
            continue                                   # missing at that commit (deleted or renamed)
        size = int(header[2])
        body = data[pos:pos + size]
        pos += size + 1
        if b"\0" in body[:8000]:
            continue
        seen.update(line.strip() for line in body.decode("utf-8", "replace").split("\n") if substantive(line))
    return seen


def analyse_file(repo: str, landed: Entry, effect: Entry, history: Callable[[], set[str]]) -> Finding | None:
    path = effect.old_path
    before = None if landed.status == "A" else landed.old_sha          # blob at the floor
    now = effect.old_sha                                               # blob on the base
    after = None if effect.status == "D" else effect.new_sha           # blob in R
    binary_landed, landed_hunks = blob_hunks(repo, before, now)
    added = {n: c for n, c in landed_lines(landed_hunks).items() if substantive(c)}
    if before == after and before != now:
        # R is exactly what the file was before the landed work: wholly reverted (or never existing).
        if binary_landed:
            return Finding(path, "binary", [], weight=REFUSE_LINES)
        return Finding(path, "whole-file", sorted(added), weight=len(added)) if added else None
    binary_effect, effect_hunks = blob_hunks(repo, now, after)
    if binary_landed or binary_effect:
        # Binary: only identity is decidable. Deleting a binary the landed work changed is a revert.
        return Finding(path, "binary", [], weight=REFUSE_LINES) if after is None and before is not None else None
    if not added:
        return None
    base_count: Counter = Counter()
    result_count: Counter = Counter()
    lost: Counter = Counter()
    loaded = False
    reverted: list[int] = []
    for h in effect_hunks:
        if h.old_count == 0:
            continue
        hit = [(h.old_start + i, c) for i, c in enumerate(h.minus) if (h.old_start + i) in added]
        if not hit:
            continue
        if not loaded:          # MOVE: only text with a net loss in this file counts as dropped.
            base_count.update(x.strip() for x in blob_text(repo, now))
            result_count.update(x.strip() for x in blob_text(repo, after))
            for key, count in base_count.items():
                if count > result_count[key]:
                    lost[key] = count - result_count[key]
            loaded = True
        dropped = []
        for n, content in hit:
            key = content.strip()
            if lost[key] > 0:
                lost[key] -= 1
                dropped.append(n)
        # EDIT credit is per line, never per hunk: only a plus line whose text the file never held in the window
        # (before or after the landed work) offsets one dropped line. One new line beside a stale restore, or a
        # `# removed` comment after a deletion, therefore hides one line, not the whole hunk.
        fresh = sum(1 for x in h.plus if substantive(x) and x.strip() not in history()) if any(
            substantive(x) for x in h.plus) else 0
        reverted.extend(dropped[min(fresh, len(dropped)):])
    return Finding(path, "lines", sorted(reverted), weight=len(reverted)) if reverted else None


def resurrected(repo: str, gone: Entry, back: Entry) -> Finding | None:
    """A file the window deleted (or renamed away) that the PR's result has again."""
    same = gone.old_sha == back.new_sha
    old_binary, old_lines = blob_lines(repo, gone.old_sha)
    new_binary, new_lines = blob_lines(repo, back.new_sha)
    if old_binary or new_binary:
        return Finding(back.new_path, "resurrected", [], weight=REFUSE_LINES) if same else None
    old = Counter(x.strip() for x in old_lines if substantive(x))
    new = Counter(x.strip() for x in new_lines if substantive(x))
    held = sum(min(count, old[key]) for key, count in new.items())
    weight = max(0, held - (sum(new.values()) - held))
    return Finding(back.new_path, "resurrected", [], weight=weight) if weight else None


def blob_text(repo: str, sha: str | None) -> list[str]:
    return [] if sha is None else blob_lines(repo, sha)[1]


def parse_allowlist(raw: str, source: str) -> list[tuple[str, str]]:
    try:
        doc = json.loads(raw)
    except ValueError as error:
        raise GateError(f"cannot parse the allowlist {source}: {error}") from error
    entries = doc.get("entries") if isinstance(doc, dict) else None
    if not isinstance(entries, list):
        raise GateError(f"{source}: `entries` must be a list")
    out = []
    for entry in entries:
        glob = entry.get("glob") if isinstance(entry, dict) else None
        reason = entry.get("reason") if isinstance(entry, dict) else None
        if not isinstance(glob, str) or not glob or not isinstance(reason, str) or not reason.strip():
            raise GateError(f"{source}: every entry needs a non-empty `glob` and a non-empty `reason`: {entry!r}")
        if all(set(part) <= {"*", "?"} for part in glob.split("/")):
            raise GateError(f"{source}: glob {glob!r} names no file, only wildcards; that is an override, not an allowlist")
        out.append((glob, reason))
    return out


def load_allowlist(repo: str, base: str, override: str | None) -> list[tuple[str, str]]:
    """The allowlist as it is on the BASE. A PR cannot exempt itself by editing the file in its own head."""
    if override is not None:
        try:
            with open(override, encoding="utf-8") as handle:
                return parse_allowlist(handle.read(), override)
        except OSError as error:
            raise GateError(f"cannot read the allowlist {override}: {error}") from error
    if not text(git(repo, "ls-tree", "--name-only", base, "--", DEFAULT_ALLOWLIST)).strip():
        return []
    return parse_allowlist(text(git(repo, "show", f"{base}:{DEFAULT_ALLOWLIST}")), f"{base[:8]}:{DEFAULT_ALLOWLIST}")


def attribute(repo: str, base: str, floor: str, finding: Finding) -> None:
    """Fill in which commits introduced the reverted lines (git blame on the base)."""
    if finding.kind in ("binary", "resurrected"):
        # The last commit in the window that touched the path: the one that changed or removed it.
        sha = text(git(repo, "rev-list", "-n", "1", f"{floor}..{base}", "--", finding.path)).strip()
        if not SHA_RE.fullmatch(sha):
            raise GateError(f"no commit in the window touches {finding.path}")
        finding.commits[sha] = text(git(repo, "log", "-1", "--format=%s", sha)).strip()
        return
    ranges: list[list[int]] = []
    for n in finding.lines:
        if ranges and n == ranges[-1][1] + 1:
            ranges[-1][1] = n
        else:
            ranges.append([n, n])
    args = ["blame", "--porcelain"]
    for first, last in ranges:
        args += ["-L", f"{first},{last}"]
    for row in text(git(repo, *args, base, "--", finding.path)).split("\n"):
        m = re.match(r"^([0-9a-f]{40}) \d+ (\d+)", row)
        if m:
            finding.per_line[int(m[2])] = m[1]
    if set(finding.per_line) != set(finding.lines):
        raise GateError(f"git blame did not attribute every reverted line of {finding.path}")
    for sha in set(finding.per_line.values()):
        finding.commits[sha] = text(git(repo, "log", "-1", "--format=%s", sha)).strip()


@dataclass
class Declaration:
    token: str                      # as written, e.g. `#4968` or `dd7665f6:src/**`
    commits: set[str]
    scope: str | None               # path glob the declaration is limited to, or None for every path
    covered: int = 0                # dropped lines this declaration accounted for


def declarations(repo: str, texts: list[str], floor: str, base: str) -> tuple[list[Declaration], list[str]]:
    """Resolve every token of every marker in `texts`. -> (declarations, unresolved tokens)."""
    found: list[Declaration] = []
    unresolved: list[str] = []
    subjects: list[str] | None = None
    for blob in texts:
        for marker in MARKER_RE.finditer(blob):
            for token in filter(None, marker.group(1).split(",")):
                m = TOKEN_RE.match(token)
                if not m:
                    unresolved.append(token)
                    continue
                commits: set[str] = set()
                if m[1]:
                    if subjects is None:
                        subjects = text(git(repo, "log", "--format=%H %s", f"{floor}..{base}")).splitlines()
                    commits.update(r.split()[0] for r in subjects if r.endswith(f"(#{m[1]})"))
                else:
                    sha = text(git(repo, "rev-parse", "--verify", "--quiet", f"{m[2]}^{{commit}}", ok=(0, 1))).strip()
                    if SHA_RE.fullmatch(sha):
                        commits.add(sha)
                if commits:
                    found.append(Declaration(token, commits, m[3]))
                else:
                    unresolved.append(token)
    return found, unresolved


def covering(decls: list[Declaration], commit: str, path: str) -> Declaration | None:
    for d in decls:
        if commit in d.commits and (d.scope is None or glob_match(d.scope, path)):
            return d
    return None


@dataclass
class Result:
    base: str
    head: str
    point: str | None
    floor: str | None
    result_tree: str | None
    candidates: int
    findings: list[Finding]
    declared: list[Declaration]
    unresolved: list[str]
    allowlisted: list[str]
    refuse_lines: int = REFUSE_LINES

    @property
    def undeclared(self) -> int:
        return sum(f.uncovered_lines for f in self.findings)

    @property
    def refused(self) -> bool:
        return self.undeclared >= self.refuse_lines


def analyse(repo: str, base_rev: str, head_rev: str, *, lookback: int, lookback_days: int, bodies: list[str],
            allowlist: list[tuple[str, str]], refuse_lines: int = REFUSE_LINES) -> Result:
    base = commit_of(repo, base_rev, "base")
    head = commit_of(repo, head_rev, "head")
    point = branch_point(repo, base, head)
    if point is None:
        return Result(base, head, None, None, None, 0, [], [], [], [], refuse_lines)
    floor = window_floor(repo, point, lookback, lookback_days)
    tree = squash_result(repo, base, head)
    window = raw_diff(repo, floor, base)
    landed = {e.new_path: e for e in window if e.status != "D" and e.new_mode != "160000"}
    gone = {e.old_path: e for e in window if e.status in "DR" and e.old_mode != "160000"}
    result = raw_diff(repo, base, tree)
    effect = {e.old_path: e for e in result if e.status != "A" and e.old_mode != "160000" and e.new_mode != "160000"}
    back = [e for e in result if e.status == "A" and e.new_path in gone and e.new_mode != "160000"]
    both = sorted(set(landed) & set(effect))
    if len(both) + len(back) > CAPS["candidates"]:
        raise cap_hit("candidates", len(both) + len(back))
    findings: list[Finding] = []
    skipped: list[str] = []
    for path in both:
        if any(glob_match(glob, path) for glob, _ in allowlist):
            skipped.append(path)
            continue
        memo: list[set[str]] = []

        def history(path: str = path, memo: list[set[str]] = memo) -> set[str]:
            if not memo:
                old = landed[path].old_sha if landed[path].status != "A" else None
                memo.append(file_history(repo, floor, base, path, [old, effect[path].old_sha]))
            return memo[0]

        found = analyse_file(repo, landed[path], effect[path], history)
        if found:
            findings.append(found)
    for entry in back:
        if any(glob_match(glob, entry.new_path) for glob, _ in allowlist):
            skipped.append(entry.new_path)
            continue
        found = resurrected(repo, gone[entry.new_path], entry)
        if found:
            findings.append(found)
    if len(findings) > CAPS["blame-files"]:
        raise cap_hit("blame-files", len(findings))
    messages = text(git(repo, "log", "--format=%B", f"{base}..{head}"))
    declared, unresolved = declarations(repo, [messages, *bodies], floor, base)
    for found in findings:
        attribute(repo, base, floor, found)
        if found.kind in ("binary", "resurrected"):
            commit = next(iter(found.commits))
            cover = covering(declared, commit, found.path)
            if cover:
                cover.covered += found.weight
                found.covered_by[cover.token] = found.weight
            else:
                found.uncovered.add(commit)
                found.uncovered_lines = found.weight
        else:
            for n, commit in found.per_line.items():
                cover = covering(declared, commit, found.path)
                if cover:
                    cover.covered += 1
                    found.covered_by[cover.token] = found.covered_by.get(cover.token, 0) + 1
                else:
                    found.uncovered.add(commit)
                    found.uncovered_lines += 1
    return Result(base, head, point, floor, tree, len(both) + len(back), findings, declared, unresolved, skipped, refuse_lines)


def render(result: Result) -> str:
    out = [f"{NAME}: base {result.base[:8]} head {result.head[:8]} branch-point {(result.point or '-')[:8]} "
           f"lookback-floor {(result.floor or '-')[:8]} candidates {result.candidates} "
           f"files-with-dropped-landed-lines {len(result.findings)} undeclared-lines {result.undeclared} "
           f"refuse-at {result.refuse_lines}"]
    for found in result.findings:
        state = "undeclared" if found.uncovered_lines else "declared"
        out.append(f"  {state} {found.kind} {found.path}: {found.weight} landed line(s) dropped")
        for sha, subject in sorted(found.commits.items()):
            out.append(f"      introduced by {sha[:8]} {subject}")
    for d in result.declared:
        scope = f" within {d.scope}" if d.scope else ""
        note = "" if d.covered else " -- covers nothing in this merge (wrong commit, wrong scope, or not needed)"
        out.append(f"  declaration {d.token}{scope}: covers {d.covered} dropped line(s){note}")
    if result.unresolved:
        out.append(f"  unresolvable declaration token(s): {', '.join(result.unresolved)}")
    undeclared = sorted({c for f in result.findings for c in f.uncovered})
    if undeclared:
        out.append("")
        out.append("This merge removes content that landed on the base within the lookback window and puts")
        out.append("nothing new in its place. If that is intended, name the commits in a commit message of the")
        out.append("branch (or in the PR body when the PR is opened); append `:<path-glob>` to limit a name to")
        out.append("the files you mean to revert:")
        out.append(f"    <!-- fsgg:declared-revert/v1 reverts={','.join(n[:12] for n in undeclared)} -->")
        out.append("If it is not intended, the branch carries an older copy of these files: rebuild it on the")
        out.append("current base instead of resolving the conflict toward the branch's side.")
    return "\n".join(out)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description="Refuse a PR whose merge silently reverts recently landed work.")
    ap.add_argument("--repo", default=".", help="git checkout holding the full history (default: .)")
    ap.add_argument("--base", required=True, help="the live base tip (sha or ref)")
    ap.add_argument("--head", required=True, help="the PR head (sha or ref)")
    ap.add_argument("--body-file", action="append", default=[], help="PR body to read declarations from")
    ap.add_argument("--lookback", type=int, default=DEFAULT_LOOKBACK,
                    help=f"first-parent commits before the branch point still counted as landed (default {DEFAULT_LOOKBACK})")
    ap.add_argument("--lookback-days", type=int, default=DEFAULT_LOOKBACK_DAYS,
                    help=f"also count anything landed within this many days before the branch point (default {DEFAULT_LOOKBACK_DAYS}); "
                         f"the window is whichever of the two reaches further")
    ap.add_argument("--refuse-lines", type=int, default=REFUSE_LINES,
                    help=f"undeclared dropped landed lines at which the check refuses; fewer only warn (default {REFUSE_LINES})")
    ap.add_argument("--allowlist", default=None,
                    help=f"allowlist file; default: {DEFAULT_ALLOWLIST} as it is on the BASE, never the PR head")
    for name, default in CAPS.items():
        ap.add_argument(f"--max-{name}", type=int, default=default,
                        help=f"cap on {name} (default {default}, at most {default * CAP_CEILING}); a hit is NO VERDICT")
    ap.add_argument("--json", action="store_true", help="print a machine-readable verdict")
    args = ap.parse_args(argv)
    if not 0 <= args.lookback <= 5000 or not 0 <= args.lookback_days <= 365 or args.refuse_lines < 1:
        raise GateError("--lookback must be 0..5000, --lookback-days 0..365 and --refuse-lines at least 1")
    for name, default in CAPS.items():
        value = getattr(args, f"max_{name.replace('-', '_')}")
        if not 1 <= value <= default * CAP_CEILING:
            raise GateError(f"--max-{name} must be between 1 and {default * CAP_CEILING}")
        CAPS[name] = value
    bodies = []
    for path in args.body_file:
        try:
            with open(path, encoding="utf-8", errors="replace") as handle:
                bodies.append(handle.read())
        except OSError as error:
            raise GateError(f"cannot read the PR body {path}: {error}") from error
    base = commit_of(args.repo, args.base, "base")
    result = analyse(args.repo, base, args.head, lookback=args.lookback, lookback_days=args.lookback_days,
                     bodies=bodies, allowlist=load_allowlist(args.repo, base, args.allowlist),
                     refuse_lines=args.refuse_lines)
    if args.json:
        print(json.dumps({
            "base": result.base, "head": result.head, "branchPoint": result.point, "floor": result.floor,
            "resultTree": result.result_tree, "candidates": result.candidates, "undeclaredLines": result.undeclared,
            "refused": result.refused,
            "findings": [{"path": f.path, "kind": f.kind, "lines": f.weight, "undeclared": f.uncovered_lines,
                          "commits": sorted(f.commits), "coveredBy": f.covered_by} for f in result.findings],
            "declarations": [{"token": d.token, "scope": d.scope, "covered": d.covered} for d in result.declared],
            "unresolved": result.unresolved, "allowlisted": result.allowlisted,
        }, indent=2))
        print(render(result), file=sys.stderr)
    else:
        print(render(result))
    if result.undeclared and not result.refused:
        print(f"::warning title={NAME}: {result.undeclared} landed line(s) dropped::below the refusal bound "
              f"of {result.refuse_lines}; check the listed files are meant to lose those lines.", file=sys.stderr)
    if result.refused:
        print(f"::error title={NAME}: this merge reverts landed work::{result.undeclared} line(s) that landed on the base "
              f"are dropped without being replaced or declared. See the job log for the files and the declaration form.",
              file=sys.stderr)
    return int(ExitCode.FINDING if result.refused else ExitCode.OK)


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
