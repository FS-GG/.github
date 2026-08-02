#!/usr/bin/env python3
"""A test harness that drives the coord engine with `--worker` must decide the identity ladder itself.

.github#1817. THREE harnesses learned this one at a time, by hand:

    tests/coord-engine-e2e/writes.sh      13 of 47 assertions failed under an exported session id
    tests/coord-engine-parity/run.sh      58 of 593 (measured twice, at #1740 and #1779)
    tests/coord-engine-parity/shim.sh     1 of 70, folded into one line by its leg 1

and a FOURTH, found while working #1688 (PR #1898): `ApplicationServiceTests.fs`'s `runQueue` fixture
scrubs `FSGG_COORD_CACHE`/`FSGG_KIT_ROOT` for the duration of every leg it drives — it already knows it
is isolating process-global state — but leaves the identity ladder inherited, the same shape one
`let`-binding over (`runIn`) had already fixed. A fifth, `runReconcileWith`, was found discharging this
very item: same shape, same file, still latent when this gate was written.

THE DEFECT, MECHANICALLY. `Identity.resolve` (src/FS.GG.Coord.Cli/Identity.fs) reads `--worker` FIRST,
so a harness naming its worker that way gets the id it asked for — `Worker.Id`. But `Identity.resolve`
ALSO computes `Worker.Derived`, independently, from whatever the process's OWN environment says: first
`$FSGG_WORKER`, then a session id off `CLAUDE_CODE_SESSION_ID` / `OPENCODE_SESSION_ID` /
`FSGG_AGENT_SESSION_ID` (+`FSGG_AGENT_HARNESS`, which only labels the third of those). Four commands —
`claim`, `release`, `heartbeat`, `widen` — compare the two and REFUSE when they disagree (#1646,
`Client.fs`'s twin/impersonation refusals). A harness that names itself with `--worker` and leaves
those variables INHERITED from whatever shell launched it is not testing the engine — it is testing
that shell, and two workers get two verdicts from the same corpus with **neither one wrong**. CI never
sees it, because GitHub Actions exports none of these variables; it is only visible to a worker running
the corpus locally, from an agent shell — precisely the reproduction path the corpus exists to serve.

TWO SHAPES DECIDE IT, AND BOTH ARE CORRECT (AC 2 — a blanket "must unset" rule is WRONG):

  (a) UNSET THE WHOLE LADDER, ONCE, BEFORE ANY IDENTITY-DEPENDENT CALL. What remains is a caller that
      derives NOTHING of its own and names itself purely with `--worker` — the human-operator case the
      flag exists for, and what every one of these fixtures actually is. `writes.sh`, `shim.sh`, and
      `run.sh`'s file-level default all take this shape:

          unset CLAUDE_CODE_SESSION_ID OPENCODE_SESSION_ID FSGG_AGENT_SESSION_ID FSGG_AGENT_HARNESS

  (b) DECIDE IT PER INVOCATION, BY SETTING BOTH HALVES TOGETHER. `run.sh`'s own twin/session legs
      DELIBERATELY export a session id to prove the refusal fires — and pair it with `FSGG_WORKER` on
      the SAME invocation, so `Derived` still equals the id under test rather than an ambient one:

          env -u OPENCODE_SESSION_ID -u FSGG_AGENT_SESSION_ID \\
              CLAUDE_CODE_SESSION_ID="$s" FSGG_WORKER="$w" "$ENGINE" claim …

      A gate that only recognised shape (a) would red this file for doing exactly what it means to do.

THE SAME RULE, FOR AN IN-PROCESS F# FIXTURE (widened onto this item's own population by the #1817
comment thread, since it is the same rule and not a second one — AC 1 as filed named only shell files,
and a fixture that drives `Client.take`/`Client.claim`/... in-process through a parsed `Options.Options`
has no shell invocation for a shell-file gate to find at all). The discoverable signature is a
`(args: string list)`-parameterised binding that sets `FSGG_COORD_CACHE` — i.e. already knows it is
isolating process-global state, exactly as a shell fixture unsetting the ladder knows it — while
mentioning NONE of `Identity.resolve`'s four variables anywhere in its own body. `runIn`, `runJsonArm`
and `withNoIdentity` all scrub the same four-name list this gate looks for; that list is the discovery
signature FOR THE SAME REASON, not a coincidence.

WHAT THIS GATE DOES NOT DO. It does not change how the engine resolves identity — the ladder is
correct; what was missing is a statement that a harness must decide it. It also does not try to prove a
shell harness's unset/twin discipline holds under branches or subshells it cannot statically evaluate;
see `_blanket_unset_line` and `_is_twin_line` below for the exact textual shape recognised, which is
the shape all four fixed instances in this tree actually use.

FAILS CLOSED (#266). `tests/` missing, or containing not one shell or F# file at all, is a broken
discovery, not a clean tree — the population is not required to be NON-EMPTY of `--worker`/
`FSGG_COORD_CACHE` sites (a subject directory that happens to hold none of them is a legitimately clean
one), but the DIRECTORY WALK finding nothing to classify at all means the walk itself broke.

THIS GATE IS PURE AND OFFLINE: it reads the working tree, and only the working tree. No API, no
network, no credentials — so it never returns the retryable no-verdict (2); `--root` pointed at
anything with no `tests/` at all is a PERMANENT no-verdict (3), same as a crash.

Usage:
  check-harness-identity.py [--root <dir>]
Exit: 0 = every discovered `--worker` invocation and every discovered in-process fixture decided the
ladder for itself; 1 = at least one did not; 3 = no verdict, PERMANENT — `tests/` is missing, or the
walk classified not one shell or F# file under it.
"""
from __future__ import annotations

import os
import re
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib.gate import GateError, base_parser, report_findings, report_ok, run  # noqa: E402

NAME = "check-harness-identity"

# ---- the shell population -----------------------------------------------------------------------

# The four `Identity.resolve` session-ladder variables a SHELL fixture must decide before any
# `--worker` invocation. `FSGG_WORKER` is not in this list for shell: the flag itself is the identity
# under test, and every compliant fixture also clears the env form separately (`export FSGG_WORKER=""`)
# — but that line is not what #1646 exposed, so it is not what this rule requires.
SHELL_LADDER_VARS = ("CLAUDE_CODE_SESSION_ID", "OPENCODE_SESSION_ID", "FSGG_AGENT_SESSION_ID", "FSGG_AGENT_HARNESS")

SHELL_SUFFIXES = (".sh", ".bash")
SHEBANG_RE = re.compile(r"^#!\s*(?:[^\s]*/)?(?:env\s+)?(bash|sh|dash|ksh)(\s|$)")

WORKER_FLAG = re.compile(r"(?<![\w-])--worker(?![\w-])")

# `--worker` ALONE is not the subject — this repo's OWN prose says the word constantly (this gate's
# docstring included). What makes a line an INVOCATION rather than a mention is that it also names the
# engine being run: `$ENGINE`/`"$ENGINE"` (every fixture in the table binds this), or a direct call to
# the resolver / apphost. Anchoring on this is also what keeps a comment ("every `--worker vole-418`
# below is measured…") and a fixture that merely PRINTS the string `--worker` into generated content
# for a DIFFERENT gate's test (tests/worker-id-attractor/run.sh's synthetic doc surface) from reading as
# subjects: neither names the engine on the line that mentions the flag.
ENGINE_MARKER = re.compile(r"\$\{?ENGINE\b|\bfsgg-coord(?:-engine)?\b")

# A whole-line shell comment — cheap and correct for the common case, and defense-in-depth alongside
# ENGINE_MARKER: measured, no comment in this tree's real fixtures mentions both tokens on one line, but
# a gate should not depend on that staying true by accident.
COMMENT_LINE = re.compile(r"^\s*#")

# A `CLAUDE_CODE_SESSION_ID=` / `FSGG_WORKER=` ASSIGNMENT — bare, `export`ed, or as an `env` prefix.
# Matches the left-hand side only; what is on the right does not matter to this rule.
ASSIGNS = {v: re.compile(rf"(?<![\w-]){v}=") for v in ("CLAUDE_CODE_SESSION_ID", "FSGG_WORKER")}

UPPER_TOKEN = re.compile(r"[A-Z][A-Z0-9_]*")


def _is_shell(path: Path) -> bool:
    """Extension first, then shebang — `scripts/fsgg-coord`'s reason: a command has no extension."""
    if path.suffix in SHELL_SUFFIXES:
        return True
    try:
        with path.open("r", encoding="utf-8", errors="ignore") as f:
            first = f.readline()
    except OSError:
        return False
    return bool(first.startswith("#!") and SHEBANG_RE.match(first))


def _logical_lines(text: str) -> list[tuple[int, str]]:
    """Join `\\`-continued shell lines into one logical line, keyed by its FIRST physical line number.

    `--worker` and the env prefix that decides it for that call are frequently split across a
    continuation (see `tests/coord-engine-parity/run.sh`'s `tclaim`/`tverb` helpers) — a pure per-line
    scan would see the flag and the twin assignment on two different "lines" and never join them.
    """
    out: list[tuple[int, str]] = []
    buf: list[str] = []
    start = 1
    for lineno, raw in enumerate(text.splitlines(), 1):
        if not buf:
            start = lineno
        stripped = raw.rstrip("\n")
        if stripped.endswith("\\") and not stripped.endswith("\\\\"):
            buf.append(stripped[:-1])
            continue
        buf.append(stripped)
        out.append((start, "\n".join(buf)))
        buf = []
    if buf:
        out.append((start, "\n".join(buf)))
    return out


def _blanket_unset_line(line: str) -> bool:
    """Does this ONE logical line unset (via `unset` or `env -u`) ALL FOUR shell ladder vars at once?"""
    if not (re.match(r"^\s*unset\b", line) or "env" in line):
        return False
    tokens = set(UPPER_TOKEN.findall(line))
    return all(v in tokens for v in SHELL_LADDER_VARS)


def _is_twin_line(line: str) -> bool:
    """Does this line decide the ladder FOR ITSELF, by setting `CLAUDE_CODE_SESSION_ID` and
    `FSGG_WORKER` together (AC 2's twin shape)? Both halves must be an assignment, not a mention."""
    return bool(ASSIGNS["CLAUDE_CODE_SESSION_ID"].search(line) and ASSIGNS["FSGG_WORKER"].search(line))


def check_shell_file(rel: str, text: str) -> list[str]:
    """Findings for one shell harness: every `--worker` invocation not covered by an EARLIER blanket
    unset and not itself a twin line."""
    lines = _logical_lines(text)
    unset_at = next(
        (ln for ln, line in lines if not COMMENT_LINE.match(line) and _blanket_unset_line(line)), None
    )

    findings: list[str] = []
    for lineno, line in lines:
        if COMMENT_LINE.match(line):
            continue
        if not (WORKER_FLAG.search(line) and ENGINE_MARKER.search(line)):
            continue
        protected = (unset_at is not None and unset_at < lineno) or _is_twin_line(line)
        if not protected:
            findings.append(
                f"{rel}:{lineno}: invokes the engine with `--worker` but never decided the identity "
                f"ladder first — no earlier `unset {' '.join(SHELL_LADDER_VARS)}`, and this line does "
                f"not set `CLAUDE_CODE_SESSION_ID`+`FSGG_WORKER` together. Whatever this shell inherited "
                f"for those four variables now silently overrides the worker id, and `claim`/`release`/"
                f"`heartbeat`/`widen` refuse the disagreement (#1646) — differently for every shell that "
                f"launches this fixture. Decide it: unset the ladder once, at the top, or set both "
                f"halves on this line."
            )
    return findings


# ---- the F# in-process population ------------------------------------------------------------------

# `Identity.resolve`/`derivedIdentity`'s four variables, verbatim — the exact list `runIn`,
# `runJsonArm` and `withNoIdentity` all scrub. `FSGG_WORKER` IS in this list for F#, unlike shell:
# clearing it (not merely passing `--worker`) is what stops `derivedIdentity` resolving anything off
# the env var either, and every compliant fixture in this tree clears it this way.
FS_LADDER_VARS = ("FSGG_WORKER", "CLAUDE_CODE_SESSION_ID", "OPENCODE_SESSION_ID", "FSGG_AGENT_SESSION_ID")

LET_RE = re.compile(r"^([ \t]*)let\s+(?:private\s+|rec\s+|inline\s+)*([A-Za-z_][A-Za-z0-9_']*|``[^`]+``)\b")
ARGS_PARAM = re.compile(r"\(\s*args\s*:\s*string\s+list\s*\)")
MAX_SIG_LINES = 12

# The FILE, not the individual binding, is checked for a literal `--worker` argv element before any
# binding in it is considered a subject at all. `Client.landable`/`Client.who` fixtures in this same test
# project ALSO set `FSGG_COORD_CACHE` through an `(args: string list)` binding — `LandableNotOpenTests.fs`'s
# `runLandable`, `WhoLivenessTests.fs`'s `runWho` — and neither verb this project drives through those two
# files ever takes a worker id, so nothing there depends on the ladder at all. Gating on the file, rather
# than tracing which call sites reach which binding, is the coarser of the two rules AC 1 asks for — but it
# is exact for every file in this tree today: a file with no `--worker` literal anywhere passes no argv
# containing one to ANY of its bindings, and a file that does (`ApplicationServiceTests.fs`) uses it across
# the fixtures this rule is written for.
WORKER_LITERAL = re.compile(r'"--worker"')


def _fs_bindings(lines: list[str]) -> list[tuple[str, int, int, str]]:
    """Every `let` binding: (name, header start index, body start index, header text).

    A binding's HEADER is accumulated from its `let` line until the first line whose stripped text
    ends with a bare `=` (F#'s multi-line signature shape — `runReconcileWith`'s three-line parameter
    list is exactly this), capped so a signature this gate cannot find the end of is simply not
    matched rather than accumulating the rest of the file into one "header".
    """
    out: list[tuple[str, int, int, str]] = []
    n = len(lines)
    i = 0
    while i < n:
        m = LET_RE.match(lines[i])
        if not m:
            i += 1
            continue
        name = m.group(2)
        j = i
        found_eq = False
        while j < n and j - i < MAX_SIG_LINES:
            stripped = lines[j].rstrip()
            if stripped.endswith("=") and not stripped.endswith("=="):
                found_eq = True
                j += 1
                break
            j += 1
        if found_eq:
            out.append((name, i, j, "\n".join(lines[i:j])))
        i += 1
    return out


def _body_span(bindings: list[tuple[str, int, int, str]], idx: int, lines: list[str]) -> tuple[int, int]:
    """The body of `bindings[idx]`: from its first body line to the next line dedented to (or past)
    the `let`'s own indentation — the next sibling binding, attribute, or a module-level dedent."""
    name, hstart, bstart, _ = bindings[idx]
    indent = len(lines[hstart]) - len(lines[hstart].lstrip(" \t"))
    end = len(lines)
    for k in range(bstart, len(lines)):
        line = lines[k]
        if not line.strip():
            continue
        this_indent = len(line) - len(line.lstrip(" \t"))
        if this_indent <= indent:
            end = k
            break
    return bstart, end


def check_fs_file(rel: str, text: str) -> list[str]:
    """Findings for one F# test file: every `(args: string list)` binding that isolates
    `FSGG_COORD_CACHE` but decides none of the four identity variables in its own body.

    Skips the whole file when it never passes a literal `"--worker"` argv element — see
    `WORKER_LITERAL`'s comment for why that is the file's population test, not the binding's."""
    if not WORKER_LITERAL.search(text):
        return []
    lines = text.splitlines()
    bindings = _fs_bindings(lines)
    findings: list[str] = []
    for idx, (name, hstart, bstart, header) in enumerate(bindings):
        if not ARGS_PARAM.search(header):
            continue  # not a reusable argv-driven fixture — a one-off `[<Fact>]` body, e.g.
        bstart2, bend = _body_span(bindings, idx, lines)
        body = "\n".join(lines[bstart2:bend])
        if "FSGG_COORD_CACHE" not in body:
            continue  # does not isolate process-global state at all — not this rule's subject
        missing = [v for v in FS_LADDER_VARS if f'"{v}"' not in body]
        if missing:
            findings.append(
                f"{rel}:{hstart + 1}: `{name}` sets FSGG_COORD_CACHE (it isolates process-global "
                f"state) but its body never mentions {', '.join(missing)} — the identity ladder is "
                f"left inherited from whatever launched the test process. Every leg driven through an "
                f"`args: string list` naming `--worker` derives an id from that ambient environment too, "
                f"and a lock verb comparing it against the flag disagrees differently depending on who "
                f"is running the suite (#1646). Scrub the four `Identity.resolve` variables around the "
                f"call, as `runIn`/`runJsonArm`/`withNoIdentity` in this same file already do."
            )
    return findings


# ---- discovery ----------------------------------------------------------------------------------

# Build output and VCS metadata: never a surface anybody edits, so a finding there is one nobody can
# act on, and `bin`/`obj` under a BUILT `tests/FS.GG.Coord.Cli.Tests/` hold thousands of generated
# `.fs`-adjacent artifacts that would otherwise dwarf the real subject.
WALK_EXCLUDE_DIRS = frozenset((".git", "bin", "obj", "node_modules"))


def _walk_files(base: Path) -> list[str]:
    """Every file under `base`, relative to `base`'s PARENT, pruned at `WALK_EXCLUDE_DIRS`.

    A plain filesystem walk — not `git ls-files` — for `check-worker-id-attractor.py`'s reason: this
    gate's own fixture (below) builds and audits synthetic trees that are not git checkouts at all, and
    a gate that only worked inside one would be untestable outside CI. Pruned AT THE DIRECTORY, rather
    than walked and discarded, so a built `tests/FS.GG.Coord.Cli.Tests/obj/` does not cost this gate a
    stat-and-sort over every generated artifact in it.
    """
    if not base.is_dir():
        return []
    root = base.parent
    out: list[Path] = []
    stack = [base]
    while stack:
        for p in sorted(stack.pop().iterdir()):
            if p.is_dir():
                if p.name not in WALK_EXCLUDE_DIRS:
                    stack.append(p)
            else:
                out.append(p)
    return [str(p.relative_to(root)) for p in sorted(out)]


def main(argv: list[str]) -> int:
    ap = base_parser(__doc__.splitlines()[0])
    args = ap.parse_args(argv)
    root = Path(args.root).resolve()

    if not (root / "tests").is_dir():
        raise GateError(f"{root / 'tests'} is not a directory — there is no test-harness population to audit")

    # THIS GATE'S OWN FIXTURE IS EXEMPT, for `check-worker-id-attractor.py`'s SELF_BASENAME reason: it
    # is the one file that must QUOTE both the compliant and the defective shapes verbatim — including
    # `--worker` beside an engine marker with no protecting unset — in order to prove this gate can
    # tell them apart. Those quotations read as violations to the textual scanner below and ARE THE
    # OPPOSITE: they are the fixture stating the rule and the defect it catches, not code driving an
    # engine. `tests/harness-identity/run.sh` is the only file this rule authors, so excluding the
    # directory rather than the one path leaves nothing else quietly exempt.
    self_fixture_dir = "tests/harness-identity/"
    walked = [p for p in _walk_files(root / "tests") if not p.startswith(self_fixture_dir)]
    shell_files = [p for p in walked if _is_shell(root / p)]
    fs_files = [p for p in walked if p.endswith(".fs")]

    if not shell_files and not fs_files:
        raise GateError(
            "found NOT ONE shell or F# file under tests/ — this repo's test harnesses demonstrably "
            "include both, so the walk is broken, not the tree (#266)."
        )

    findings: list[str] = []
    worker_sites = 0
    fs_subjects = 0

    for rel in shell_files:
        try:
            text = (root / rel).read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        worker_sites += sum(
            1
            for lineno, line in _logical_lines(text)
            if not COMMENT_LINE.match(line) and WORKER_FLAG.search(line) and ENGINE_MARKER.search(line)
        )
        findings.extend(check_shell_file(rel, text))

    for rel in fs_files:
        try:
            text = (root / rel).read_text(encoding="utf-8")
        except (OSError, UnicodeDecodeError):
            continue
        if WORKER_LITERAL.search(text):
            bindings = _fs_bindings(text.splitlines())
            fs_subjects += sum(1 for _, _, _, h in bindings if ARGS_PARAM.search(h))
        findings.extend(check_fs_file(rel, text))

    if findings:
        return report_findings(NAME, findings)

    return report_ok(
        NAME,
        f"{len(shell_files)} shell file(s) audited ({worker_sites} `--worker` site(s)), "
        f"{len(fs_files)} F# file(s) audited ({fs_subjects} argv-driven binding(s)) — every "
        f"identity-dependent invocation decided the ladder for itself.",
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
