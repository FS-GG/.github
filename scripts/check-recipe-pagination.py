#!/usr/bin/env python3
"""Assert every `gh api` LIST read in a recipe carries `--paginate`.

.github#547, epic #266 (coherence gates that fail open). Found while working .github#506.

GitHub's REST API pages at 30 by default. A `gh api` read of a collection without `--paginate`
therefore returns the FIRST THIRTY items and exits 0 — no warning, no truncation marker, nothing in
the output that distinguishes "these are all of them" from "these are the first 30 of 51". The
caller gets a confident, wrong answer.

That is the #266 signature exactly: a surface that runs, reports success, and does nothing — except
here it is worse than doing nothing, because a truncated list is INDISTINGUISHABLE FROM A COMPLETE
ONE and is acted on as if it were complete.

WHY A GATE, AND NOT JUST A FIX
  The two live instances were both in agent-facing recipes, which is the worst place for them:

  1. `pnext-item` §4 / `cross-repo-coordination` — the look-before-you-file dedupe step, which the
     skill itself calls "the highest-signal place to look, and the one people skip":

         gh api repos/FS-GG/<repo>/issues/<parent>/sub_issues --jq '.[] | "#\\(.number) …"'

     It hid 21 of epic #266's 51 children. This step exists BECAUSE eager filing by N parallel
     workers deterministically produces duplicates (#459/#460, the same finding filed eleven minutes
     apart, which #464 fixed the recipe for) — and its command silently truncates on exactly the
     parents where it is needed most, since a big, active epic is precisely where several workers are
     splitting one parent at once. Worse than a duplicate: a false negative on LINKAGE. `done --flip`
     rolls an epic up over its native sub-issue graph and nothing else (#322), so a worker reading a
     truncated graph can conclude an epic's children are all Done when 21 of them are merely
     off-page.

  2. `pnext-item` §5 — the REST merge gate:

         gh api repos/FS-GG/<repo>/commits/$SHA/check-runs --jq '"pending=\\(…) failed=\\(…)"'

     This repo has ~30 workflows. Past page 1, that aggregate reports `pending=0 failed=0` while the
     checks that would have stopped the merge sit on page 2 — a merge gate that greenlights a red PR.

  A recipe is copied, not imported. Fixing the four files fixes today's copies and nothing else; the
  next hand-written `gh api` line reintroduces it. So the rule gets a gate.

WHAT IT ASSERTS
  In every recipe file under the agent-skill roots, for every `gh api` command inside a shell code
  fence: if the command READS A LIST, it carries `--paginate`.

  "Reads a list" = the request is a GET (no -X/--method naming a write verb) AND either
    (a) its jq — whether `--jq`/`-q` on the `gh api` itself, or a `jq` it pipes into — ITERATES an
        array of the response (`.[]`, `.check_runs[]`, …); or
    (b) its endpoint's final path segment is a known REST COLLECTION (`sub_issues`, `check-runs`,
        `comments`, …).

  (a) is the primary signal and is nearly false-positive-free: a command that iterates the response
  as an array is, definitionally, consuming a list. (b) is the backstop for a read with no jq at all
  (`gh api …/sub_issues` piped to a human eyeball), which (a) cannot see. A single-resource read
  (`…/issues/449`, `…/pulls/<n>`) matches neither: its last segment is not a collection, and its jq
  reaches for scalars (`.head.sha`, `.state`) rather than iterating.

EVERY ONE OF THESE IS AN ERROR, NOT A SKIP
  - A GET list read with no `--paginate`                    (it silently truncates at 30)
  - A recipe file that cannot be read, or a `gh api` command that will not tokenize
  - Auditing ZERO `gh api` commands. Examining nothing is a failure to audit, not a clean audit
    (#266) — if the fence extractor breaks, this gate must go RED, not green.

WHAT IT DELIBERATELY DOES NOT DO
  It does not check writes (POST/PUT/PATCH/DELETE): `--paginate` is meaningless on them.
  It does not check `scripts/fsgg-coord`, which pages correctly at every call site and is not a
  recipe — it is the sanctioned reader the skills tell you to prefer BECAUSE it pages for you.

EXIT CODES  (the contract; the workflow greps nothing)
  0  every list read paginates
  1  FINDING — a list read that will truncate
  3  NO VERDICT (permanent) — a file would not read/tokenize, or nothing was audited

  There is no exit 2 ("no verdict, retryable"): this gate is pure and offline. It reads files, makes
  no network call, and has no condition that a re-run could resolve.
"""

from __future__ import annotations

import argparse
import re
import shlex
import sys
from pathlib import Path

# Default recipe surface: every markdown file under an agent-skill root. These are the files whose
# commands get COPIED by an agent, which is what makes an unpaginated read here different in kind
# from one in a script.
DEFAULT_ROOTS = (".claude/skills", ".agents/skills")

FENCE_LANGS = {"sh", "bash", "shell", "console"}

WRITE_VERBS = {"POST", "PUT", "PATCH", "DELETE"}

# An array iteration applied to the response: `.[]` or `.check_runs[]`.
ITERATES_ARRAY = re.compile(r"\.\[\]|\.[A-Za-z_][A-Za-z0-9_]*\[\]")

# Final path segments that are REST COLLECTIONS. Only consulted for a GET whose jq does not already
# give it away, so a miss here is caught by ITERATES_ARRAY in every realistic recipe.
COLLECTIONS = {
    "artifacts", "assignees", "branches", "check-runs", "check-suites", "collaborators",
    "comments", "commits", "deployments", "events", "files", "issues", "jobs", "labels",
    "milestones", "notifications", "projects", "pulls", "releases", "repos", "reviews",
    "runs", "secrets", "statuses", "sub_issues", "tags", "teams", "variables", "workflows",
}


class Unparseable(Exception):
    """A recipe command that will not tokenize. Never a skip — see the exit-code contract."""


def logical_commands(block: str) -> list[str]:
    """Join a code fence's physical lines into logical shell commands.

    Two things continue a command across a newline, and BOTH occur in these recipes:
      - a trailing backslash;
      - an unclosed quote — a jq script is routinely written across several lines inside one pair
        of single quotes, with no backslash anywhere. A line-at-a-time reader sees `| jq -r '[...]`
        and the aggregate on the next line as two commands, and would miss the `--paginate` sitting
        on the first of them.
    """
    out: list[str] = []
    buf: list[str] = []
    quote: str | None = None

    for line in block.splitlines():
        buf.append(line)
        esc = False
        for ch in line:
            if esc:
                esc = False
                continue
            if ch == "\\" and quote != "'":
                # A backslash escapes inside double quotes and when unquoted; inside SINGLE quotes
                # it is a literal, which is why jq scripts can carry `\(...)` unmolested.
                esc = True
                continue
            if quote:
                if ch == quote:
                    quote = None
            elif ch in "'\"":
                quote = ch

        if quote is not None:
            continue  # unclosed quote: the command continues on the next line
        if line.rstrip().endswith("\\"):
            continue  # explicit continuation

        cmd = "\n".join(buf)
        buf = []
        if cmd.strip():
            out.append(cmd)

    if buf:  # unterminated quote at end of fence
        out.append("\n".join(buf))
    return out


def shell_fences(text: str) -> list[tuple[int, str]]:
    """Every ```sh-ish fenced block, as (1-based line of the fence's first content line, body).

    Only fenced shell blocks. Prose that merely NAMES `gh api` — and these skills discuss it a great
    deal — is not a command anybody copies, and flagging it would train workers to ignore this gate.
    """
    fences: list[tuple[int, str]] = []
    lines = text.splitlines()
    i = 0
    while i < len(lines):
        m = re.match(r"^\s*```([A-Za-z0-9_+-]*)\s*$", lines[i])
        if not m:
            i += 1
            continue
        lang = m.group(1).lower()
        start = i + 1
        j = start
        while j < len(lines) and not re.match(r"^\s*```\s*$", lines[j]):
            j += 1
        if lang in FENCE_LANGS:
            fences.append((start + 1, "\n".join(lines[start:j])))
        i = j + 1
    return fences


def endpoint_of(tokens: list[str]) -> str | None:
    """The first bare (non-flag) token after `api` — the endpoint."""
    try:
        k = tokens.index("api")
    except ValueError:
        return None
    skip_next = False
    for tok in tokens[k + 1:]:
        if skip_next:
            skip_next = False
            continue
        if tok.startswith("-"):
            # Flags that take a value; `--paginate`/`--slurp` do not.
            if tok in {"-X", "--method", "-q", "--jq", "-f", "--raw-field", "-F", "--field",
                       "-H", "--header", "--input", "--cache", "--hostname", "-t", "--template",
                       "-p", "--preview"}:
                skip_next = True
            continue
        return tok
    return None


def is_write(tokens: list[str]) -> bool:
    for i, tok in enumerate(tokens):
        if tok in {"-X", "--method"} and i + 1 < len(tokens):
            if tokens[i + 1].upper() in WRITE_VERBS:
                return True
        if tok.startswith("--method="):
            if tok.split("=", 1)[1].upper() in WRITE_VERBS:
                return True
    return False


def reads_a_list(cmd: str, tokens: list[str]) -> str | None:
    """Why this command is a list read, or None if it is not one."""
    # (a) the jq iterates the response as an array. Checked over the whole logical command, so a
    #     `gh api … | jq '.[]…'` pipeline is caught as readily as a `--jq` on the call itself.
    if ITERATES_ARRAY.search(cmd):
        return "its jq iterates the response as an array"

    # (b) no jq to go on — fall back to the endpoint's shape.
    ep = endpoint_of(tokens)
    if ep:
        path = ep.strip("\"'").split("?", 1)[0].rstrip("/")
        last = path.rsplit("/", 1)[-1] if "/" in path else path
        if last in COLLECTIONS:
            return f"its endpoint ends in the collection `{last}`"
    return None


def audit_file(path: Path, rel: str) -> tuple[list[str], int]:
    """(findings, number of `gh api` commands audited)."""
    findings: list[str] = []
    audited = 0
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as e:
        raise Unparseable(f"{rel}: cannot read: {e}") from e

    for fence_line, body in shell_fences(text):
        offset = 0
        for cmd in logical_commands(body):
            here = fence_line + offset
            offset += cmd.count("\n") + 1
            if not re.search(r"\bgh\s+api\b", cmd):
                continue
            try:
                tokens = shlex.split(cmd, comments=True)
            except ValueError as e:
                raise Unparseable(f"{rel}:{here}: will not tokenize: {e}") from e

            audited += 1
            if is_write(tokens):
                continue
            if "--paginate" in tokens:
                continue
            why = reads_a_list(cmd, tokens)
            if why:
                first = cmd.strip().splitlines()[0].strip()
                findings.append(
                    f"{rel}:{here}: LIST read without `--paginate` — {why}, "
                    f"so it silently truncates at 30.\n      {first}"
                )
    return findings, audited


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument("--root", default=".", help="repo root (default: .)")
    ap.add_argument(
        "--recipes", action="append", default=None,
        help=f"recipe dir to audit, repeatable (default: {' '.join(DEFAULT_ROOTS)})",
    )
    args = ap.parse_args()

    root = Path(args.root).resolve()
    recipe_dirs = args.recipes if args.recipes else list(DEFAULT_ROOTS)

    files: list[tuple[Path, str]] = []
    for d in recipe_dirs:
        base = root / d
        if not base.is_dir():
            print(f"check-recipe-pagination: no verdict: recipe dir '{d}' does not exist under "
                  f"{root}", file=sys.stderr)
            return 3
        for p in sorted(base.rglob("*.md")):
            files.append((p, str(p.relative_to(root))))

    if not files:
        print("check-recipe-pagination: no verdict: found NO recipe files to audit. Examining "
              "nothing is a failure to audit, not a clean audit (#266).", file=sys.stderr)
        return 3

    findings: list[str] = []
    audited = 0
    try:
        for path, rel in files:
            f, n = audit_file(path, rel)
            findings.extend(f)
            audited += n
    except Unparseable as e:
        print(f"check-recipe-pagination: no verdict: {e}", file=sys.stderr)
        return 3

    if audited == 0:
        print(f"check-recipe-pagination: no verdict: audited {len(files)} recipe file(s) and found "
              "NO `gh api` commands in any shell fence. The recipes DO carry them, so the fence "
              "extractor is broken — examining nothing is a failure to audit, not a clean audit "
              "(#266).", file=sys.stderr)
        return 3

    if findings:
        print(f"check-recipe-pagination: {len(findings)} list read(s) will truncate at 30:\n",
              file=sys.stderr)
        for f in findings:
            print(f"  - {f}", file=sys.stderr)
        print(
            "\nA `gh api` read of a collection without `--paginate` returns the first 30 items and "
            "exits 0.\nThe output does not look truncated — it looks like an answer (#547).\n"
            "\n  fix:  add `--paginate`.\n"
            "        Aggregating across pages? `--slurp` cannot be combined with `--jq`, so pipe it:\n"
            "          gh api <endpoint> --paginate --slurp | jq -r '[.[].check_runs[]] | ...'\n"
            "        Reading issues? Prefer `scripts/fsgg-coord issues`, which pages for you.",
            file=sys.stderr,
        )
        return 1

    print(f"check-recipe-pagination: OK — {audited} `gh api` command(s) in {len(files)} recipe "
          f"file(s); every list read paginates.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
