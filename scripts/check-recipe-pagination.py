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

# The recipe surface is every markdown file under an agent-skill root — those are the files whose
# commands get COPIED by an agent, which is what makes an unpaginated read here different in kind
# from one in a script.
#
# The ROOTS ARE READ FROM `.agent-skill-roots`, not hardcoded (#517, as skill-union-assert.sh does).
# Keeping a second copy of that list here would mean a root added to the declaration is silently NOT
# audited — a new root's recipes could truncate and this gate would stay green. That is the #266
# fail-open this gate exists to close, and there is no excuse for reintroducing it in the gate.
ROOTS_DECL = ".agent-skill-roots"
FALLBACK_ROOTS = (".claude/skills", ".agents/skills")

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


def scan_line(line: str, quote: str | None) -> tuple[str | None, str]:
    """Scan one line's shell quoting. Returns (quote state after it, the line minus any comment).

    COMMENTS ARE NOT CODE, and this is the whole reason this helper exists (#673). A `#` comment in
    a fence is prose, written in the house voice, and the house voice is full of contractions:

        for _ in $(seq 30); do          # ~10 min ceiling — poll, don't spin forever

    A quote scanner that does not know `don't` is a comment sees that apostrophe OPEN a single quote
    that nothing ever closes, and every following line is swallowed into one command. Both outcomes
    are bad, and the fail-open one is the one nobody would have noticed:

      - FAIL-OPEN. The glued blob still tokenizes (shlex strips the comment), and it carries the
        `--paginate` from the FIRST command — so a later unpaginated list read inside the glue is
        never audited on its own, and the gate reports `OK`. That is the #266 signature exactly,
        inside the gate written to close #266.
      - FAIL-CLOSED, at the wrong line. If the bogus quote is later closed by the OPENING quote of a
        genuine multi-line jq, the splitter cuts the command in half mid-string, `shlex` raises `No
        closing quotation`, and the gate blames the line the mis-split STARTED on — an unrelated
        command, often screenfuls away from the apostrophe that caused it.

    The rule is `shlex`'s own, deliberately: an unquoted, unescaped `#` starts a comment that runs to
    end of line — even mid-word (`shlex.split('a#b', comments=True) == ['a']`). This function's only
    job is to hand `shlex` correctly delimited commands, so where the two could disagree about what a
    comment is, the tokenizer's answer is the one that matters.
    """
    esc = False
    for idx, ch in enumerate(line):
        if esc:
            esc = False
            continue
        if ch == "\\" and quote != "'":
            # A backslash escapes inside double quotes and when unquoted; inside SINGLE quotes it is
            # a literal, which is why jq scripts can carry `\(...)` unmolested.
            esc = True
            continue
        if quote:
            if ch == quote:
                quote = None
        elif ch == "#":
            return quote, line[:idx]  # a comment: the rest of the line is prose, not code
        elif ch in "'\"":
            quote = ch
    return quote, line


def logical_commands(block: str) -> list[tuple[int, str]]:
    """Split a code fence into logical shell commands, as (0-based line offset within block, cmd).

    The OFFSET IS CARRIED, not recomputed by the caller from the emitted commands. A caller that
    advances a counter by each returned command's line count silently loses the lines this function
    drops (blank ones), and every finding after the first blank line in a fence is then reported at
    the wrong line. A lint that points at the wrong line is one workers stop believing.

    Two things continue a command across a newline, and BOTH occur in these recipes:
      - a trailing backslash;
      - an unclosed quote — a jq script is routinely written across several lines inside one pair
        of single quotes, with no backslash anywhere. A line-at-a-time reader sees `| jq -r '[...]`
        and the aggregate on the next line as two commands, and would miss the `--paginate` sitting
        on the first of them.

    Neither is continued by anything a COMMENT contains: an apostrophe in one is not a quote, and a
    trailing backslash in one is not a continuation. See `scan_line`.
    """
    out: list[tuple[int, str]] = []
    buf: list[str] = []
    start = 0
    quote: str | None = None

    for i, line in enumerate(block.splitlines()):
        if not buf:
            start = i
        buf.append(line)
        quote, code = scan_line(line, quote)

        if quote is not None:
            continue  # unclosed quote: the command continues on the next line
        if code.rstrip().endswith("\\"):
            continue  # explicit continuation — in the CODE, not in a trailing comment

        cmd = "\n".join(buf)
        buf = []
        if cmd.strip():
            out.append((start, cmd))

    if buf:  # unterminated quote at end of fence
        out.append((start, "\n".join(buf)))
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


def code_of(cmd: str) -> str:
    """The command with its shell comments removed, quote state threaded across its lines.

    Tokens are the wrong instrument for "is this a `gh api` call?": `SHA=$(gh api …)` tokenizes as
    `['SHA=$(gh', 'api', …]`, so no `gh`/`api` token pair exists, and the command-substitution form —
    which the recipes use to read a head SHA — would go unaudited. The RAW text is wrong too: it
    still carries the comments. Strip the comments and keep the text.
    """
    quote: str | None = None
    out: list[str] = []
    for line in cmd.splitlines():
        quote, code = scan_line(line, quote)
        out.append(code)
    return "\n".join(out)


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


# Passing any of these makes `gh api` send a BODY — and gh's documented default method is "GET
# normally, and POST if any parameters were added". So a field/input flag with no explicit -X is a
# WRITE, even though nothing on the line says POST.
BODY_FLAGS = {"-f", "--raw-field", "-F", "--field", "--input"}


def is_write(tokens: list[str]) -> bool:
    """True if this `gh api` call is a write.

    An explicit -X/--method wins. Otherwise gh infers the method from the presence of a body:
    `gh api .../sub_issues -F sub_issue_id=$cid` — which is exactly how `fsgg-coord child` creates a
    sub-issue edge, and how intra-repo-parallel-work documents it — is a POST with no `-X` anywhere.
    Reading it as a GET would make the collection backstop fire on a write, and `--paginate` is
    meaningless on a write. A gate that cries wolf on correct code is one workers scroll past.
    """
    explicit: str | None = None
    for i, tok in enumerate(tokens):
        if tok in {"-X", "--method"} and i + 1 < len(tokens):
            explicit = tokens[i + 1].upper()
        elif tok.startswith("--method="):
            explicit = tok.split("=", 1)[1].upper()

    if explicit is not None:
        return explicit in WRITE_VERBS

    return any(tok in BODY_FLAGS or tok.split("=", 1)[0] in BODY_FLAGS
               for tok in tokens if tok.startswith("-"))


def reads_a_list(tokens: list[str]) -> str | None:
    """Why this command is a list read, or None if it is not one.

    Everything here reads the TOKENS, never the raw command text: shlex has already stripped shell
    comments, and these recipes comment their commands heavily. Matching the array-iteration pattern
    against raw text would let a comment (`# unlike --jq '.[]'`) turn a correct single-resource read
    into a finding.
    """
    # (a) the jq iterates the response as an array. Checked across the whole tokenized pipeline, so
    #     `gh api … | jq '.[]…'` is caught as readily as a `--jq` on the call itself.
    if ITERATES_ARRAY.search(" ".join(tokens)):
        return "its jq iterates the response as an array"

    # (b) no jq to go on — fall back to the endpoint's shape.
    ep = endpoint_of(tokens)
    if ep:
        path = ep.strip("\"'").split("?", 1)[0].rstrip("/")
        last = path.rsplit("/", 1)[-1] if "/" in path else path
        if last in COLLECTIONS:
            return f"its endpoint ends in the collection `{last}`"
    return None


def slurp_with_jq(tokens: list[str]) -> bool:
    """`gh api --slurp` cannot be combined with `--jq`/`--template`; gh refuses it outright.

    This is not a general lint — it is the specific way the REMEDY this gate prescribes goes wrong.
    Aggregating across pages needs `--slurp`, and the obvious next keystroke is to keep the `--jq`
    that was already there, producing a command that carries `--paginate`, passes a naive pagination
    check, and then dies on execution. A gate that certifies a command which cannot run is worse than
    no gate: it moves the failure from review to the worker's terminal.
    """
    if "--slurp" not in tokens:
        return False
    return any(t in {"--jq", "-q", "--template", "-t"} or t.startswith(("--jq=", "--template="))
               for t in tokens)


def audit_file(path: Path, rel: str) -> tuple[list[str], int]:
    """(findings, number of `gh api` commands audited)."""
    findings: list[str] = []
    audited = 0
    try:
        text = path.read_text(encoding="utf-8")
    except OSError as e:
        raise Unparseable(f"{rel}: cannot read: {e}") from e

    for fence_line, body in shell_fences(text):
        for offset, cmd in logical_commands(body):
            # The offset comes FROM the splitter, which knows about the lines it dropped. Do not
            # recompute it here from the emitted commands: blank lines are not emitted, and the
            # count would drift by one per blank line in the fence.
            here = fence_line + offset
            # Match against the CODE, not the raw text. A comment that merely MENTIONS `gh api` is
            # not a command, and these recipes are full of such comments — they spend paragraphs
            # telling workers to prefer REST ("`gh api repos/…` will still open your PR"). Counting
            # one as a command inflates `audited`, and `audited` is the ONLY thing standing between a
            # broken fence extractor and a green gate (#266): a single such comment was enough to
            # report `OK — 1 gh api command(s)` over a recipe that contained none.
            if not re.search(r"\bgh\s+api\b", code_of(cmd)):
                continue
            try:
                tokens = shlex.split(cmd, comments=True)
            except ValueError as e:
                raise Unparseable(f"{rel}:{here}: will not tokenize: {e}") from e

            audited += 1
            first = cmd.strip().splitlines()[0].strip()

            if slurp_with_jq(tokens):
                findings.append(
                    f"{rel}:{here}: `--slurp` cannot be combined with `--jq`/`--template` — gh "
                    f"refuses this command outright, so the recipe cannot run.\n      {first}"
                )
                continue

            if is_write(tokens):
                continue
            if "--paginate" in tokens:
                continue
            why = reads_a_list(tokens)
            if why:
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
        help=f"recipe dir to audit, repeatable (default: the roots in {ROOTS_DECL})",
    )
    args = ap.parse_args()

    root = Path(args.root).resolve()

    if args.recipes:
        recipe_dirs = args.recipes
    else:
        # Read the roots the tree DECLARES, so a root added there is audited without touching this
        # file. A private copy of the list would go stale silently, and a root nobody audits is a
        # root whose recipes can truncate while this gate reports green (#266, #517).
        decl = root / ROOTS_DECL
        if decl.is_file():
            recipe_dirs = [
                ln.strip() for ln in decl.read_text(encoding="utf-8").splitlines()
                if ln.strip() and not ln.lstrip().startswith("#")
            ]
            if not recipe_dirs:
                print(f"check-recipe-pagination: no verdict: {ROOTS_DECL} declares no roots.",
                      file=sys.stderr)
                return 3
        else:
            recipe_dirs = list(FALLBACK_ROOTS)

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
