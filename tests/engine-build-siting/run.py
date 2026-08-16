#!/usr/bin/env python3
"""No harness a worker runs may build the coordination engine INTO the worker's checkout (.github#2653).

WHY THIS GATE EXISTS AT ALL, AND WHY IT IS NOT A COMMENT IN THE FILE IT GUARDS.

`scripts/fsgg-coord`'s tier 2a prefers a source build under the CALLER's own toplevel, deliberately:
a worker who builds in their own worktree gets THEIR build, because preempting it would hand them the
shared engine and silently discard the edits they are testing (`scripts/fsgg-coord:209-213`). The
unstated premise is that a worktree build EXPRESSES INTENT to use it as the engine. That premise is
false for a build a gate harness made transitively. `scripts/check-skill-quality` used to build the
engine into whatever checkout invoked it, so a worker whose item touched no engine source was REQUIRED
to create the artifact and was then poisoned by it: a feature branch is behind `main` under the
engine's source trees by construction the moment any engine commit lands after it was cut, and
`stale_guard` fail-closes EVERY board write from that worktree. Five occurrences across three agents
in one board-driver run on 2026-08-15, and more after the row was filed.

`.github#570` is why the knowledge cannot live in a comment: a rule enforced by whoever happens to
remember it decays, and this defect's whole signature is recurrence — it is self-repairing once known,
which is exactly why it kept coming back. The next harness that needs an engine will be written by
somebody who has never read `scripts/build-gate-engine`. This gate is what tells them.

WHAT IT REFUSES TO BE: A CHECK THAT MATCHES NOTHING.

A gate asserting an ABSENCE is satisfied by a matcher that has quietly stopped matching, and this repo
has shipped exactly that (`.github#2312`'s ordering gate). Three separate mechanisms stop it here, and
none of them is optional:

  1. A DECLARED SITE MANIFEST, compared as a SET EQUALITY rather than a subset. Every engine-building
     invocation this repo contains is named below with its disposition. An undeclared site is a
     finding; a declared site that has VANISHED is also a finding, because that is what a matcher
     silently breaking looks like from the outside. A subset check would pass over an empty scan.
  2. A SELF-TEST CORPUS, run on every invocation, of N spellings that MUST be detected and M
     lookalikes that MUST NOT. It is the direct answer to `.github#2312`'s repair: an absence is only
     as good as the evidence that the thing asserting it can still see.
  3. A POPULATION FLOOR. Discovering zero candidate files is exit 3 — no verdict — never green.

THE POPULATION IS `scripts/` AND `tests/`, AND THE BOUNDARY IS DELIBERATE.

`.github/workflows/**` is excluded. A workflow runs in a disposable CI checkout that resolves no board
write and is thrown away at the end of the job, so an in-tree engine build there cannot shadow anything
and `coord-engine.yml` builds one on purpose. The subject here is a harness a WORKER executes in a
checkout that holds a claim. Stating the boundary rather than implying it is the point: a future reader
who wants workflows covered should widen this deliberately, not discover the hole.

Exit: 0 = every engine build is sited out of tree or accountably declared; 1 = a finding; 3 = NO
VERDICT (the scan examined nothing, or the matcher failed its own corpus) — which is never green.
"""

from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
POPULATION = ("scripts", "tests")

# The engine's own projects — the only thing `scripts/fsgg-coord`'s tier 2a can ever resolve.
ENGINE_PROJECTS = (
    "src/FS.GG.Coord.Cli",
    "src/FS.GG.Coord.Core",
    "src/FS.GG.Coord.GitHub",
)

# CANDIDACY IS DEFAULT-DENY, AND THAT WAS FORCED BY THIS GATE'S FIRST RUN AGAINST THE REAL TREE.
#
# The obvious rule — "a `dotnet build` is in scope when it names an engine project" — is EVADABLE, and
# not hypothetically: `scripts/build-gate-engine`, the fix this row is built on, spells its own
# invocation `dotnet build "$PROJ" …` with `PROJ` assigned six lines earlier. The literal-matching
# draft reported that file as having no engine build at all, and would have reported the same of any
# future harness that hoisted its project path into a variable. An absence gate defeated by naming a
# variable is not a gate.
#
# So EVERY `dotnet build` is a candidate unless it literally names a project that is known not to be
# the engine. The exclusions are enumerated here, by name, and a `dotnet build` of anything this list
# does not recognise must be declared in DECLARED_SITES with a reason — the fail-closed direction, and
# the same shape `scripts/test` uses for `DELIBERATELY_UNWIRED`.
NON_ENGINE_PROJECT_MARKERS = (
    "scripts/NewSddWorkspace",           # the new-sdd-workspace CLI
    ".config/kit/",                      # a receiver's kit materialize target
    "FS.GG.Kit.receiver.proj",
    "FS.GG.Audio",
)

# Any of these on the invocation means the output was deliberately sited somewhere the caller named.
# `--artifacts-path` is the one `scripts/build-gate-engine` uses because it moves `obj/` as well as
# `bin/`; the rest are accepted because they equally stop the artifact landing where tier 2a probes.
REDIRECT_FLAGS = (
    "--artifacts-path",
    "--output",
    "-o ",
    "-p:ArtifactsPath",
    "-p:OutputPath",
    "-p:BaseOutputPath",
    "-p:BaseIntermediateOutputPath",
)

# EVERY ENGINE-BUILDING SITE IN THIS REPO, WITH AN ACCOUNTABLE DISPOSITION. `None` means "redirected —
# the invocation must carry a redirect flag, and this gate checks that it does". A string is an
# exemption and IS ITS REASON: it is printed in the failure output, so a reader meeting this gate sees
# why each exemption was granted rather than an opaque allowlist.
DECLARED_SITES: dict[str, str | None] = {
    # The one sanctioned way a gate harness gets an engine. Redirect enforced, not assumed.
    "scripts/build-gate-engine": None,
    # Builds `$MUT/src/FS.GG.Coord.Cli` — a mutant COPY the fixture makes under `mktemp -d`, outside
    # the caller's checkout entirely. The artifact it produces is inside the copy, so there is nothing
    # in the caller's tree for tier 2a to prefer.
    "tests/review-critic-succession-wire/run.sh": (
        "builds a mutant copy under mktemp -d, never the caller's checkout"
    ),
    # The mutation sweep's rebuild, and here the in-tree build is the SUBJECT rather than a side
    # effect: `scripts/lib/mutation.py` edits engine source in place and the parity corpus drives the
    # COMPILED binary, so without the rebuild every leg would grade the pristine engine and report
    # ESCAPED. It also cannot cause this row's defect: while it runs, the checkout has uncommitted work
    # under the engine's source trees by construction, which is precisely the kit-author case
    # `authored_engine_source` classifies as DELIBERATE and tier 2a is right to prefer.
    "tests/coord-engine-mutation/leg.sh": (
        "the in-tree build is the mutation subject; the tree is dirty under the engine's source "
        "trees by construction while it runs, which is tier 2a's protected case"
    ),
    # Builds `$PROJ`, assigned `scripts/NewSddWorkspace/NewSddWorkspace.fsproj` ten lines earlier. It is
    # here rather than excluded by NON_ENGINE_PROJECT_MARKERS precisely because the project is named
    # through a variable, so this gate cannot READ that it is not the engine — and default-deny means
    # "cannot read" is answered by a human writing this sentence, not by the gate guessing. Its output
    # lands under `scripts/NewSddWorkspace/bin/Release`, a path `scripts/fsgg-coord` never probes.
    "tests/new-sdd-workspace/run.sh": (
        "builds scripts/NewSddWorkspace, not the engine; its output lands where tier 2a never probes"
    ),
}

# ---------------------------------------------------------------------------------------------------
# THE MATCHER. A regex over raw text cannot tell an INVOCATION from a message that quotes one, and this
# repo is full of the latter: `scripts/fsgg-coord:390` prints the remedy inside `die "…"`,
# `scripts/fsgg-coord-guards.sh` prints it inside MULTI-LINE double-quoted refusal text,
# `tests/coord-engine-parity/shim.sh` asserts on it inside `grep -q "…"`, and `tests/coord-guards`
# rewrites it inside `sed '…'`. Every one of those is prose. So quoting state is tracked across the
# whole file, character by character, rather than per line — a per-line quote count reads the middle of
# a multi-line refusal string as bare code, which is a FALSE POSITIVE on the very file that documents
# this defect.
# ---------------------------------------------------------------------------------------------------

# WHY THIS TOKENISES INSTEAD OF LOOKING BEHIND (.github#2653, review round 0, finding 1).
#
# The first two drafts of this matcher asked "what character precedes `dotnet build`?" and compared it
# against a set of punctuation. That set was widened once during authoring and was STILL wrong, and the
# critic proved it by building a new harness under `tests/` that genuinely builds the engine in tree and
# varying only the SPELLING. Five ordinary forms walked straight past a green gate:
#
#     env DOTNET_NOLOGO=1 dotnet build …          # prefix command
#     DOTNET_NOLOGO=1 dotnet build …              # assignment prefix
#     if [ -n "$X" ]; then dotnet build …; fi     # keyword position
#     for c in Release; do dotnet build …; done   # keyword position
#     out=`dotnet build …`                        # backtick substitution
#
# A lookbehind pins SPELLINGS; what this gate means to pin is a RULE — *"`dotnet build` is the command
# word of a simple command"*. Those are not the same statement, and the gap between them is where three
# separate escapes on this board have lived (`.github#2312`'s ordering gate was evaded twice the same
# way). Widening the punctuation set a third time would buy the next escape, not close this one, so the
# question is answered by walking the shell's own grammar for command position instead: separators reset
# it, reserved words and prefix commands and assignments are TRANSPARENT to it, and the first word that
# is none of those is the command word. That is a rule with a fixed statement rather than a list that
# grows by one every time somebody is clever.
#
# It is deliberately a SMALL tokeniser, not a shell. It does not expand anything, and everywhere it is
# unsure it errs toward CANDIDACY — an over-detected build must merely be declared, while an
# under-detected one is this whole file failing silently.

# Separators after which a new simple command begins. `)` `}` and a backtick are included because what
# follows them starts a command too, and being generous here can only add candidates.
_RESETS_COMMAND_POSITION = set(";|&\n(){}`")

# Reserved words are transparent: they occupy a command position without consuming it.
_RESERVED = {
    "if", "then", "elif", "else", "fi", "while", "until", "do", "done",
    "for", "select", "case", "esac", "in", "function", "!", "[[", "]]", "{", "}",
}

# Prefix commands are transparent for the same reason — each RUNS the command that follows it, so the
# real command word is further right. `time` is both a reserved word and one of these.
_PREFIX_COMMANDS = {
    "env", "command", "builtin", "exec", "nohup", "sudo", "doas", "nice", "ionice",
    "stdbuf", "timeout", "xargs", "time", "setsid", "unbuffer",
}

_ASSIGNMENT = re.compile(r"[A-Za-z_][A-Za-z0-9_]*=")

_TERMINATORS = ";|&<>\n)`"

CODE, STRING, COMMENT = "c", "s", "#"


def classify(text: str) -> list[str]:
    """Per-character CODE / STRING / COMMENT, tracked across the WHOLE file rather than per line.

    Per-line quote counting is the obvious implementation and it is wrong here: the middle line of a
    multi-line double-quoted refusal in `scripts/fsgg-coord-guards.sh` carries no quote at all, so a
    per-line reader calls it bare code and reports the file that DOCUMENTS this defect as committing it.
    """
    kinds = [CODE] * len(text)
    i, n = 0, len(text)
    quote: str | None = None
    while i < n:
        c = text[i]
        if quote is None:
            if c == "\\" and i + 1 < n:
                i += 2
                continue
            if c in "\"'":
                kinds[i] = STRING
                quote = c
                i += 1
                continue
            if c == "#" and (i == 0 or text[i - 1] in " \t\n"):
                j = text.find("\n", i)
                j = n if j < 0 else j
                for k in range(i, j):
                    kinds[k] = COMMENT
                i = j
                continue
            i += 1
            continue
        # Inside a quote the OTHER quote character is literal. A backslash escapes only inside double
        # quotes; inside single quotes a shell passes it straight through, and reading it as an escape
        # would run the closing quote past the end of the string.
        if c == "\\" and quote == '"' and i + 1 < n:
            kinds[i] = kinds[i + 1] = STRING
            i += 2
            continue
        kinds[i] = STRING
        if c == quote:
            quote = None
        i += 1
    return kinds


def _end_of_word(text: str, kinds: list[str], i: int) -> int:
    """The index just past the word starting at `i`, with quoted spans swallowed whole."""
    n = len(text)
    while i < n:
        c = text[i]
        if kinds[i] != CODE:          # inside quotes: part of this word, whatever it contains
            i += 1
            continue
        if c in " \t\n" or c in _RESETS_COMMAND_POSITION or c in "<>":
            break
        if c == "\\" and i + 1 < n:
            i += 2
            continue
        i += 1
    return i


def invocations(text: str) -> list[str]:
    """Every `dotnet build …` that is really invoked, as its FULL command text — quotes included.

    The command text must be read off the original text, not off the code spans: every real invocation
    in this repo names its project inside quotes (`"$ROOT/src/FS.GG.Coord.Cli/…"`), so a reader that
    kept only the unquoted fragments would drop the one argument that says WHICH project is being built
    and would classify every engine build as "not an engine build". Three corpus entries pin that.
    """
    kinds = classify(text)
    n = len(text)
    found: list[str] = []

    i = 0
    at_command_position = True
    while i < n:
        c = text[i]

        # Anything the classifier called COMMENT is skipped whole; a comment cannot start a command.
        if kinds[i] == COMMENT:
            i += 1
            continue

        # Blanks, and a backslash-newline line continuation, carry command position across unchanged.
        if c in " \t":
            i += 1
            continue
        if c == "\\" and i + 1 < n and text[i + 1] == "\n":
            i += 2
            continue

        if kinds[i] == CODE:
            # A redirection and its target are consumed together, so the FILENAME is never mistaken for
            # a command word and the `&` inside `2>&1` never reads as a separator.
            if c in "<>":
                while i < n and kinds[i] == CODE and text[i] in "<>&-":
                    i += 1
                while i < n and text[i] in " \t":
                    i += 1
                i = _end_of_word(text, kinds, i)
                continue
            if c in _RESETS_COMMAND_POSITION:
                i += 1
                at_command_position = True
                continue

        # A WORD: a maximal run up to the next blank or CODE operator. Quoted spans are part of it, so
        # `"$ROOT/src/FS.GG.Coord.Cli/x.fsproj"` is one word rather than three fragments.
        start = i
        i = _end_of_word(text, kinds, i)
        word = text[start:i]
        if not word:
            i += 1
            continue

        if not at_command_position:
            continue

        # Transparent prefixes hold the command position open. A digit-only or flag-shaped word does
        # too, so `timeout 500 dotnet build …` and `xargs -r dotnet build …` reach their real command.
        if (
            _ASSIGNMENT.match(word)
            or word in _RESERVED
            or word in _PREFIX_COMMANDS
            or word.isdigit()
            or word.startswith("-")
        ):
            continue

        # This word is the command word of a simple command. Everything after it is arguments.
        at_command_position = False
        if word != "dotnet" and not word.endswith("/dotnet"):
            continue
        j = i
        while j < n and text[j] in " \t":
            j += 1
        verb_end = _end_of_word(text, kinds, j)
        if text[j:verb_end] != "build":
            continue

        # The command text runs from the command word to the first unquoted terminator. Backslash-newline
        # is a line CONTINUATION, so `dotnet build "$P" \` + `--artifacts-path "$A"` is one invocation and
        # must be read as one, or every multi-line build reads as un-redirected.
        e = start
        while e < n:
            ch = text[e]
            if ch == "\\" and e + 1 < n:
                e += 2
                continue
            if kinds[e] == CODE and ch in _TERMINATORS and e > start:
                break
            e += 1
        found.append(text[start:e])
    return found


def names_engine_project(cmd: str) -> bool:
    return any(p in cmd for p in ENGINE_PROJECTS)


def is_candidate(cmd: str) -> bool:
    """In scope unless it literally names a project known not to be the engine."""
    if names_engine_project(cmd):
        return True
    return not any(m in cmd for m in NON_ENGINE_PROJECT_MARKERS)


def is_redirected(cmd: str) -> bool:
    return any(f in cmd for f in REDIRECT_FLAGS)


# ---------------------------------------------------------------------------------------------------
# THE SELF-TEST CORPUS — mechanism 2. Spellings that MUST be seen and lookalikes that MUST NOT, every
# one drawn from a real line in this repo, from a shape that broke a draft of this matcher, or from the
# critic's harness-insertion table in .github#2653's round-0 review. The counts are NOT restated in
# prose here — an earlier draft said "nine and nine" over ten and ten within one commit of being
# written. `main()` prints the live lengths instead, so the statement cannot drift from the corpus.
# ---------------------------------------------------------------------------------------------------

MUST_MATCH: list[tuple[str, str]] = [
    ("bare, at line start", 'dotnet build src/FS.GG.Coord.Cli -c Release\n'),
    ("indented, quoted project", '  dotnet build "$ROOT/src/FS.GG.Coord.Cli/FS.GG.Coord.Cli.fsproj" -c Release --nologo\n'),
    ("guarded by `if !`", 'if ! dotnet build "$MUT/src/FS.GG.Coord.Cli" -c Release; then\n'),
    ("guarded by `elif !`", 'elif ! dotnet build "$M/src/FS.GG.Coord.Cli" -c Release >"$LOG" 2>&1; then\n'),
    ("after `&&`", 'true && dotnet build src/FS.GG.Coord.Core -c Release\n'),
    ("inside a command substitution", 'out="x"\nrc=$(dotnet build src/FS.GG.Coord.GitHub -c Release)\n'),
    ("after a `;`", 'cd "$d"; dotnet build src/FS.GG.Coord.Cli -c Release\n'),
    ("continued across a backslash-newline", 'dotnet build src/FS.GG.Coord.Cli \\\n  -c Release --nologo\n'),
    ("code following a closed quoted string on the same line", 'echo "harmless"; dotnet build src/FS.GG.Coord.Cli -c Release\n'),
    # THE EVASION THAT DEFEATED THE FIRST DRAFT, pinned so it cannot come back: the project is named
    # through a variable, so nothing in the command text says "engine" — and default-deny must make it
    # a candidate anyway. Without this leg the whole gate is one `PROJ=` assignment away from blind.
    ("project named through a variable", 'PROJ="$ROOT/src/FS.GG.Coord.Cli/x.fsproj"\ndotnet build "$PROJ" -c Release\n'),
    # THE FIVE SPELLINGS THAT WALKED PAST THE SHIPPED GATE (.github#2653 review round 0, finding 1).
    # Each is transcribed from the critic's harness-insertion table, where each one built the engine into
    # the checkout for real and the gate still returned 0. A lookbehind over punctuation could not see
    # any of them; the command-position tokeniser sees all five because each is a command WORD.
    ("prefix command `env`", 'env DOTNET_NOLOGO=1 dotnet build "$ROOT/src/FS.GG.Coord.Cli/x.fsproj" -c Release\n'),
    ("assignment prefix", 'DOTNET_NOLOGO=1 dotnet build "$ROOT/src/FS.GG.Coord.Cli/x.fsproj" -c Release\n'),
    ("keyword position `then`, one-liner", 'if [ -n "$X" ]; then dotnet build "$ROOT/src/FS.GG.Coord.Cli/x.fsproj" -c Release; fi\n'),
    ("keyword position `do`, one-liner", 'for c in Release; do dotnet build "$ROOT/src/FS.GG.Coord.Cli" -c "$c"; done\n'),
    ("backtick command substitution", 'out=`dotnet build "$ROOT/src/FS.GG.Coord.Cli/x.fsproj" -c Release`\n'),
    # The neighbouring positions of the same three classes, so the repair is the RULE and not five
    # patches. Each of these was independently confirmed MISSED by the shipped matcher.
    ("keyword position `else`", 'if $x; then true; else dotnet build src/FS.GG.Coord.Cli -c Release; fi\n'),
    ("keyword position `elif`", 'if $x; then true; elif dotnet build src/FS.GG.Coord.Cli -c Release; then true; fi\n'),
    ("keyword position `do` after `while`", 'while $x; do dotnet build src/FS.GG.Coord.Core -c Release; done\n'),
    ("prefix command `time`", 'time dotnet build src/FS.GG.Coord.Cli -c Release\n'),
    ("prefix command `command`", 'command dotnet build src/FS.GG.Coord.GitHub -c Release\n'),
    ("prefix command `exec`", 'exec dotnet build src/FS.GG.Coord.Cli -c Release\n'),
    ("prefix command with its own numeric argument", 'timeout 600 dotnet build src/FS.GG.Coord.Cli -c Release\n'),
    ("`if` with no `!`", 'if dotnet build src/FS.GG.Coord.Cli -c Release; then true; fi\n'),
    ("`then` on its own line", 'if $x\nthen\n  dotnet build src/FS.GG.Coord.Cli -c Release\nfi\n'),
    ("redirection before the next command", 'echo x >"$LOG"; dotnet build src/FS.GG.Coord.Cli -c Release\n'),
]

MUST_NOT_MATCH: list[tuple[str, str]] = [
    ("inside a double-quoted message", 'die "build it from source (dotnet build src/FS.GG.Coord.Cli -c Release)"\n'),
    ("inside a MULTI-LINE double-quoted refusal — the shape that broke a draft of this matcher",
     'detail="$detail\n\n            Rebuild it:\n              dotnet build $top/src/FS.GG.Coord.Cli -c Release"\n'),
    ("inside a single-quoted sed program", "  | sed 's|^dotnet build .*|true \\&\\&|')\"\n"),
    ("inside a grep pattern", 'printf %s "$out" | grep -q "dotnet build $CL/src/FS.GG.Coord.Cli -c Release"\n'),
    ("in a whole-line comment", '# dotnet build src/FS.GG.Coord.Cli -c Release\n'),
    ("in a trailing comment", 'true   # dotnet build src/FS.GG.Coord.Cli -c Release\n'),
    ("inside a python f-string", 'msg = f"dotnet build src/FS.GG.Coord.Cli -c Release (looked at {engine})"\n'),
    ("inside escaped quotes within a double-quoted string",
     'text="run:\\n  dotnet build \\"$eng/src/FS.GG.Coord.Cli\\" -c Release"\n'),
    ("as a substring of a longer word", 'nodotnet buildsrc/FS.GG.Coord.Cli\n'),
    # NOT AN INVOCATION AND ALSO NOT A CANDIDATE — a YAML `run:` step, which is neither at a shell
    # command position nor inside this gate's population. It is here so that widening the population to
    # workflows later is a DELIBERATE act with a corpus leg to flip, not an accident.
    ("a workflow `run:` step", 'jobs:\n  b:\n    steps:\n      - run: dotnet build src/FS.GG.Coord.Cli -c Release\n'),
]

# Candidacy is a separate question from invocation, so it gets separate legs: default-deny means a
# `dotnet build` whose target this gate cannot READ must still be in scope, while one that literally
# names a known non-engine project must not be.
MUST_BE_CANDIDATE = (
    'dotnet build "$PROJ" -c Release',
    'dotnet build src/FS.GG.Coord.Core -c Release',
    'dotnet build "$MUT/src/FS.GG.Coord.Cli" -c Release',
)
MUST_NOT_BE_CANDIDATE = (
    'dotnet build scripts/NewSddWorkspace/NewSddWorkspace.fsproj -c Release',
    'dotnet build .config/kit/FS.GG.Kit.receiver.proj -t:FsggKitMaterialize',
    'dotnet build FS.GG.Audio.slnx -c Debug --no-restore',
)


def selftest() -> list[str]:
    problems: list[str] = []
    for label, text in MUST_MATCH:
        cmds = [c for c in invocations(text) if is_candidate(c)]
        if not cmds:
            problems.append(f"corpus: MUST match but did not — {label}\n    {text.strip()!r}")
    for label, text in MUST_NOT_MATCH:
        cmds = [c for c in invocations(text) if is_candidate(c)]
        if cmds:
            problems.append(f"corpus: MUST NOT match but did — {label}\n    {text.strip()!r}\n    saw: {cmds!r}")
    for cmd in MUST_BE_CANDIDATE:
        if not is_candidate(cmd):
            problems.append(f"corpus: MUST be a candidate but was not — {cmd!r}")
    for cmd in MUST_NOT_BE_CANDIDATE:
        if is_candidate(cmd):
            problems.append(f"corpus: MUST NOT be a candidate but was — {cmd!r}")
    # The redirect classifier is part of the matcher and gets the same treatment.
    if is_redirected('dotnet build src/FS.GG.Coord.Cli -c Release'):
        problems.append("corpus: a bare in-tree build was classified as redirected")
    if not is_redirected('dotnet build src/FS.GG.Coord.Cli -c Release --artifacts-path "$ART"'):
        problems.append("corpus: an --artifacts-path build was not classified as redirected")
    if not is_redirected('dotnet build src/FS.GG.Coord.Cli \\\n  -c Release --artifacts-path "$ART"'):
        problems.append("corpus: a continued --artifacts-path build was not classified as redirected")
    return problems


def main() -> int:
    corpus = selftest()
    if corpus:
        print("::error::engine-build-siting: NO VERDICT — the matcher failed its own corpus, so an", file=sys.stderr)
        print("  empty finding list would mean nothing. Repair the matcher before trusting a green.", file=sys.stderr)
        for p in corpus:
            print(f"  - {p}", file=sys.stderr)
        return 3

    # THE POPULATION IS WHAT GIT TRACKS, NOT WHAT THE FILESYSTEM HOLDS (.github#2653, round-0 follow-up).
    #
    # An `rglob` walk reads gitignored build byproducts too. Measured: three `__pycache__/*.pyc` files
    # left by earlier suite runs made a working tree report 438 where a clean CI checkout reports 435 —
    # which is precisely how this gate's own file count came to be quoted two different ways in one
    # review. A count that depends on what the developer happened to run last is a bad number, but the
    # real hazard is the other direction: a gitignored scratch file containing a `dotnet build` would
    # red this gate LOCALLY and pass in CI, and a gate that fails only on one machine teaches people to
    # stop believing it. Git's index is the population that actually ships and is identical in both
    # places. The tradeoff is stated rather than hidden: a brand-new harness that has not been `git
    # add`ed is not scanned locally — and it cannot reach a PR unstaged, so CI still judges it.
    #
    # FAILS CLOSED. If `git ls-files` cannot answer, this exits 3 rather than falling back to a walk;
    # "I could not determine the population" must not be spelled the same way as "the population is
    # clean", which is this file's whole thesis one level up.
    try:
        listed = subprocess.run(
            ["git", "-C", str(REPO), "ls-files", "-z", "--", *POPULATION],
            capture_output=True, check=True,
        ).stdout.decode("utf-8", "surrogateescape")
    except (OSError, subprocess.CalledProcessError) as exc:
        print(f"::error::engine-build-siting: NO VERDICT — could not enumerate tracked files: {exc}", file=sys.stderr)
        return 3
    files = [REPO / rel for rel in listed.split("\0") if rel]
    files = [p for p in files if p.is_file() and not p.is_symlink()]
    if len(files) < 50:
        print(
            f"::error::engine-build-siting: NO VERDICT — discovered only {len(files)} candidate files "
            f"tracked under {POPULATION}; a scan that examines nothing must not report green (#266)",
            file=sys.stderr,
        )
        return 3

    discovered: dict[str, list[str]] = {}
    for path in sorted(files):
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue  # a binary or unreadable fixture cannot carry a shell invocation
        cmds = [c for c in invocations(text) if is_candidate(c)]
        if cmds:
            discovered[path.relative_to(REPO).as_posix()] = cmds

    findings: list[str] = []

    undeclared = sorted(set(discovered) - set(DECLARED_SITES))
    for rel in undeclared:
        findings.append(
            f"{rel}: builds the coordination engine but is not declared in this gate's site manifest.\n"
            f"    A harness that builds the engine into the caller's checkout is .github#2653: tier 2a\n"
            f"    prefers that artifact over the shared checkout's engine, and stale_guard then refuses\n"
            f"    EVERY board write from that worktree — for a build the worker never asked for.\n"
            f"    Build through `scripts/build-gate-engine`, which sites the output out of tree, or add\n"
            f"    an entry to DECLARED_SITES stating why this one cannot cause that.\n"
            f"    invocation(s): {[c.strip() for c in discovered[rel]]}"
        )

    vanished = sorted(set(DECLARED_SITES) - set(discovered))
    for rel in vanished:
        findings.append(
            f"{rel}: declared in this gate's site manifest, but no engine build was found there.\n"
            f"    Either the site moved — in which case update the manifest — or this gate's matcher\n"
            f"    has stopped seeing it, which is exactly how an absence gate goes quietly green."
        )

    for rel in sorted(set(discovered) & set(DECLARED_SITES)):
        reason = DECLARED_SITES[rel]
        if reason is not None:
            continue
        unredirected = [c for c in discovered[rel] if not is_redirected(c)]
        if unredirected:
            findings.append(
                f"{rel}: declared as building OUT OF TREE, but an invocation carries no redirect flag\n"
                f"    (one of {list(REDIRECT_FLAGS)}), so its output lands under\n"
                f"    src/FS.GG.Coord.*/bin — exactly where scripts/fsgg-coord's tier 2a probes.\n"
                f"    invocation(s): {[c.strip() for c in unredirected]}"
            )

    if findings:
        print("::error::engine-build-siting: a harness builds the coordination engine into the caller's checkout", file=sys.stderr)
        for f in findings:
            print(f"  - {f}", file=sys.stderr)
        return 1

    exempt = sum(1 for v in DECLARED_SITES.values() if v is not None)
    print(
        f"engine-build-siting: OK — {len(files)} files scanned under {'/'.join(POPULATION)}; "
        f"{len(discovered)} engine-building site(s), {len(DECLARED_SITES) - exempt} sited out of tree "
        f"and {exempt} accountably exempt; matcher corpus "
        f"{len(MUST_MATCH)} must-match / {len(MUST_NOT_MATCH)} must-not-match all held."
    )
    for rel, reason in sorted(DECLARED_SITES.items()):
        print(f"  {rel}: {'out of tree (redirect verified)' if reason is None else reason}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
