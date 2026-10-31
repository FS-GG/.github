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
  B' = B stepped back --lookback first-parent commits (default 100). Everything the base gained since
       B' is "recently landed". The stale tree of the incident was ONE commit older than B.
  R  = the squash result: `git merge-tree --write-tree base head`, i.e. the real three-way merge GitHub
       performs, not the head tree. (For #4977 it is byte-identical to the merged tree 610adc96.)

A base line is REVERTED when: it was added between B' and the base; the PR's merge R no longer has it; and
R neither replaces it with new text nor keeps it somewhere else in the same file.

EDITING vs REVERTING. This is the whole difficulty, and it is defined on the text, not on intent:

  * EDIT   -- a hunk that removes recently landed lines AND adds at least one line whose text the file
             never held in the window (`git rev-list floor..base -- path`, every version, plus the base
             and the floor blob). The PR wrote something new where the landed lines were. Allowed.
             "Text the file held" covers both sides of the landed change and every version between, so a
             stale tree that is only PARTLY old still reads as the older text coming back.
  * REVERT -- a hunk that removes recently landed lines and adds nothing new: a pure deletion, or a
             replacement made only of text the file already held in the window (the older version
             coming back). Counted toward the refusal bound unless declared.
  * MOVE   -- a removed landed line whose text still occurs in the same file in R as often as before
             (cut and paste, reordering). Not counted.
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

    <!-- fsgg:declared-revert/v1 reverts=dd7665f6,#4968 -->

Tokens are commit SHAs (7-40 hex) or `#<PR>` (resolved to the squash commit whose subject ends `(#N)`).
Each reverted line is attributed with `git blame` and is covered only when the commit that introduced
it is named. Naming a commit that does not account for the reverted lines declares nothing. A commit
message marker lands in the squash message, so the declaration is durable in history; the body is read
from the event payload and, deliberately, an edit to it does NOT re-run CI (#4730 CI load): to declare
after the fact, push a commit whose message carries the marker.

FAIL CLOSED (#266). Exit 0 is the only green. 1 = refused. 3 = no verdict: shallow history, an unreadable
object, no common ancestor, a merge conflict, or more commits/files than the caps below. Never green.

REAL EXCLUSIONS ARE EXPLICIT. `registry/landed-revert-allowlist.json` lists path globs with a mandatory
reason, for files a PR legitimately regenerates from scratch. An entry without a reason is an error.
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

DEFAULT_LOOKBACK = 100
MAX_PR_COMMITS = 2000      # commits in base..head; a bigger branch cannot be reasoned about here
MAX_RAW_ENTRIES = 20000    # changed paths per raw diff
MAX_CANDIDATES = 1500      # files changed both by the landed work and by the PR
MAX_BLAME_FILES = 200
REFUSE_LINES = 30          # undeclared dropped landed lines at which the PR is refused; fewer only warn
MAX_HISTORY_VERSIONS = 300
GIT_TIMEOUT = 180
DEFAULT_ALLOWLIST = "registry/landed-revert-allowlist.json"
MARKER_RE = re.compile(r"<!--\s*fsgg:declared-revert/v1\s+reverts=([^\s>]*)\s*-->")
TOKEN_RE = re.compile(r"^(?:#(\d{1,7})|([0-9a-fA-F]{7,40}))$")
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
        if len(entries) > MAX_RAW_ENTRIES:
            raise GateError(f"{a[:8]}..{b[:8]} changes more than {MAX_RAW_ENTRIES} paths; refusing to guess")
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
    if len(rows) > MAX_PR_COMMITS:
        raise GateError(f"{len(rows)} commits in base..head exceeds the cap {MAX_PR_COMMITS}")
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


def lookback_commit(repo: str, point: str, depth: int) -> str:
    rows = text(git(repo, "rev-list", "--first-parent", "-n", str(depth + 1), point)).split()
    if not rows:
        raise GateError(f"cannot walk first-parent history from {point[:8]}")
    return rows[-1]


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
    kind: str                                   # "whole-file" | "binary" | "lines"
    lines: list[int]                            # base line numbers of the reverted landed lines
    weight: int = 0                             # what this finding adds to the refusal count
    commits: dict[str, str] = field(default_factory=dict)       # introducing commit -> subject
    per_line: dict[int, str] = field(default_factory=dict)      # reverted line -> introducing commit
    uncovered_lines: int = 0                    # weight not accounted for by a declared commit
    uncovered: set[str] = field(default_factory=set)            # introducing commits not declared


def landed_lines(hunks: list[Hunk]) -> dict[int, str]:
    out: dict[int, str] = {}
    for h in hunks:
        for offset, content in enumerate(h.plus):
            out[h.new_start + offset] = content
    return out


def file_history(repo: str, floor: str, base: str, path: str, blobs: list[str | None]) -> set[str]:
    """Every substantive line (stripped) the file held in any version committed in floor..base, plus `blobs`."""
    shas = text(git(repo, "rev-list", f"{floor}..{base}", "--", path)).split()[:MAX_HISTORY_VERSIONS]
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
    before = None if landed.status == "A" else landed.old_sha          # blob at B'
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
        if any(substantive(x) and x.strip() not in history() for x in h.plus):
            continue            # EDIT: the PR wrote text this file never held in the window, before or after.
        if not loaded:          # MOVE: only text with a net loss in this file counts as dropped.
            base_count.update(x.strip() for x in blob_text(repo, now))
            result_count.update(x.strip() for x in blob_text(repo, after))
            for key, count in base_count.items():
                if count > result_count[key]:
                    lost[key] = count - result_count[key]
            loaded = True
        for n, content in hit:
            key = content.strip()
            if lost[key] > 0:
                lost[key] -= 1
                reverted.append(n)
    return Finding(path, "lines", sorted(reverted), weight=len(reverted)) if reverted else None


def blob_text(repo: str, sha: str | None) -> list[str]:
    return [] if sha is None else blob_lines(repo, sha)[1]


def load_allowlist(path: str) -> list[tuple[str, str]]:
    if not os.path.exists(path):
        return []
    try:
        with open(path, encoding="utf-8") as handle:
            doc = json.load(handle)
    except (OSError, ValueError) as error:
        raise GateError(f"cannot read the allowlist {path}: {error}") from error
    entries = doc.get("entries") if isinstance(doc, dict) else None
    if not isinstance(entries, list):
        raise GateError(f"{path}: `entries` must be a list")
    out = []
    for entry in entries:
        glob = entry.get("glob") if isinstance(entry, dict) else None
        reason = entry.get("reason") if isinstance(entry, dict) else None
        if not isinstance(glob, str) or not glob or not isinstance(reason, str) or not reason.strip():
            raise GateError(f"{path}: every entry needs a non-empty `glob` and a non-empty `reason`: {entry!r}")
        out.append((glob, reason))
    return out


def attribute(repo: str, base: str, finding: Finding) -> None:
    """Fill in which commits introduced the reverted lines (git blame on the base)."""
    if finding.kind == "binary":
        sha, _, subject = text(git(repo, "log", "-1", "--format=%H %s", base, "--", finding.path)).strip().partition(" ")
        if sha:
            finding.commits[sha] = subject
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


def declared_commits(repo: str, texts: list[str], floor: str, base: str) -> tuple[set[str], list[str]]:
    """Resolve every token of every marker in `texts`. -> (commits, unresolved tokens)."""
    commits: set[str] = set()
    unresolved: list[str] = []
    subjects: list[str] | None = None
    for blob in texts:
        for marker in MARKER_RE.finditer(blob):
            for token in filter(None, marker.group(1).split(",")):
                m = TOKEN_RE.match(token)
                if not m:
                    unresolved.append(token)
                elif m[1]:
                    if subjects is None:
                        subjects = text(git(repo, "log", "--format=%H %s", f"{floor}..{base}")).splitlines()
                    hits = [r.split()[0] for r in subjects if r.endswith(f"(#{m[1]})")]
                    commits.update(hits) if hits else unresolved.append(token)
                else:
                    sha = text(git(repo, "rev-parse", "--verify", "--quiet", f"{m[2]}^{{commit}}", ok=(0, 1))).strip()
                    commits.add(sha) if SHA_RE.fullmatch(sha) else unresolved.append(token)
    return commits, unresolved


@dataclass
class Result:
    base: str
    head: str
    point: str | None
    floor: str | None
    result_tree: str | None
    candidates: int
    findings: list[Finding]
    declared: set[str]
    unresolved: list[str]
    allowlisted: list[str]
    refuse_lines: int = REFUSE_LINES

    @property
    def undeclared(self) -> int:
        return sum(f.uncovered_lines for f in self.findings)

    @property
    def refused(self) -> bool:
        return self.undeclared >= self.refuse_lines


def analyse(repo: str, base_rev: str, head_rev: str, *, lookback: int, bodies: list[str],
            allowlist: list[tuple[str, str]], refuse_lines: int = REFUSE_LINES) -> Result:
    base = commit_of(repo, base_rev, "base")
    head = commit_of(repo, head_rev, "head")
    point = branch_point(repo, base, head)
    if point is None:
        return Result(base, head, None, None, None, 0, [], set(), [], [], refuse_lines)
    floor = lookback_commit(repo, point, lookback)
    tree = squash_result(repo, base, head)
    landed = {e.new_path: e for e in raw_diff(repo, floor, base) if e.status != "D" and e.new_mode != "160000"}
    effect = {e.old_path: e for e in raw_diff(repo, base, tree)
              if e.status != "A" and e.old_mode != "160000" and e.new_mode != "160000"}
    both = sorted(set(landed) & set(effect))
    if len(both) > MAX_CANDIDATES:
        raise GateError(f"{len(both)} files changed by both the landed work and the PR exceeds the cap {MAX_CANDIDATES}")
    findings: list[Finding] = []
    skipped: list[str] = []
    for path in both:
        if any(fnmatch.fnmatchcase(path, glob) for glob, _ in allowlist):
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
    if len(findings) > MAX_BLAME_FILES:
        raise GateError(f"{len(findings)} files reverted exceeds the attribution cap {MAX_BLAME_FILES}")
    messages = text(git(repo, "log", "--format=%B", f"{base}..{head}"))
    declared, unresolved = declared_commits(repo, [messages, *bodies], floor, base)
    for found in findings:
        attribute(repo, base, found)
        if found.kind == "binary":
            found.uncovered = {c for c in found.commits if c not in declared}
            found.uncovered_lines = found.weight if found.uncovered else 0
        else:
            found.uncovered = {c for c in found.per_line.values() if c not in declared}
            found.uncovered_lines = sum(1 for c in found.per_line.values() if c not in declared)
    return Result(base, head, point, floor, tree, len(both), findings, declared, unresolved, skipped, refuse_lines)


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
    if result.unresolved:
        out.append(f"  unresolvable declaration token(s): {', '.join(result.unresolved)}")
    undeclared = sorted({c for f in result.findings for c in f.uncovered})
    if undeclared:
        out.append("")
        out.append("This merge removes content that landed on the base within the lookback window and puts")
        out.append("nothing new in its place. If that is intended, name the commits in a commit message of the")
        out.append("branch (or in the PR body when the PR is opened):")
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
    ap.add_argument("--refuse-lines", type=int, default=REFUSE_LINES,
                    help=f"undeclared dropped landed lines at which the check refuses; fewer only warn (default {REFUSE_LINES})")
    ap.add_argument("--allowlist", default=None, help=f"default: <repo>/{DEFAULT_ALLOWLIST}")
    ap.add_argument("--json", action="store_true", help="print a machine-readable verdict")
    args = ap.parse_args(argv)
    if not 0 <= args.lookback <= 5000 or args.refuse_lines < 1:
        raise GateError("--lookback must be 0..5000 and --refuse-lines at least 1")
    bodies = []
    for path in args.body_file:
        try:
            with open(path, encoding="utf-8", errors="replace") as handle:
                bodies.append(handle.read())
        except OSError as error:
            raise GateError(f"cannot read the PR body {path}: {error}") from error
    allow_path = args.allowlist or os.path.join(args.repo, DEFAULT_ALLOWLIST)
    result = analyse(args.repo, args.base, args.head, lookback=args.lookback, bodies=bodies,
                     allowlist=load_allowlist(allow_path), refuse_lines=args.refuse_lines)
    if args.json:
        print(json.dumps({
            "base": result.base, "head": result.head, "branchPoint": result.point, "floor": result.floor,
            "resultTree": result.result_tree, "candidates": result.candidates, "undeclaredLines": result.undeclared, "refused": result.refused,
            "findings": [{"path": f.path, "kind": f.kind, "lines": f.weight, "undeclared": f.uncovered_lines,
                          "commits": sorted(f.commits)} for f in result.findings],
            "declared": sorted(result.declared), "unresolved": result.unresolved,
            "allowlisted": result.allowlisted,
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
