#!/usr/bin/env python3
r"""Mutation sweep over `scripts/check-signature-doc-siting.py`, DERIVED FROM ITS AST.

.github#2730, epic #266 (the rule nothing asserts).

WHY THIS FILE EXISTS, AND IT IS NOT "MORE COVERAGE". `.github#2730`'s ordinary review chain
exhausted at round 3 with the SAME defect regenerating one layer down five times: a declared
NO-VERDICT arm no leg asserted; a leg named "(nested) block comments" that could not discriminate
nesting; two of four unlexable arms unasserted; two finding branches separated only by a substring
both emit; and the fixture's real-tree legs anchoring only two of the three projects under `src/`.
Each was invisible to the previous round's sweep, and the reason is structural rather than careless:

    EVERY SWEEP WAS DRAWN FROM ITS AUTHOR'S OWN MODEL OF WHAT THE GATE DOES.

A mutation an author does not think of is a mutation the author does not run, and the fixture's
silence about it is then indistinguishable from coverage. Adding a sixth hand-picked mutation is a
sixth draw from that same well. So this sweep is not hand-picked: the mutation set is ENUMERATED
FROM THE GATE'S OWN ABSTRACT SYNTAX TREE and from the filesystem the gate discovers over, so it
covers sites the author of the fixture never considered, including sites added later.

WHAT IT ASSERTS. For every mutation it generates, the fixture `run.sh` must FAIL. A mutation the
fixture survives is a dimension of the gate's behaviour that nothing asserts -- which
`independent-review.md` calls material by definition, and which this row has now paid for five
times. Survivors are therefore listed with a written justification in `mutants-allowed.txt`, a file
that only ever shrinks, exactly like `baseline.txt` beside it. An unjustified survivor is a failure.

THE OPERATORS, and each is mechanical rather than chosen:

  cond-false / cond-true  every `if`/`while` test in the module, forced to a constant. This is the
                          CONDITIONS dimension: it reaches all six `EX_NO_VERDICT` returns, all four
                          finding branches, every lexer state arm and every baseline-parse arm,
                          because it reads them off the tree rather than off a list someone wrote.
  cmp-flip                every comparison operator, inverted (`==`<->`!=`, `<`<->`>=`, ...). A
                          boundary asserted only at one side survives this and nothing else.
  num                     every numeric literal, incremented. This is the EXIT-CODE dimension at its
                          root -- `EX_OK`, `EX_FINDING`, `EX_NO_VERDICT` are numeric literals -- and
                          also the lexer's stride constants and the finding renderer's cap.
  str                     every string literal that is not a docstring, with its alphabetic runs
                          replaced by same-length `X`s. This is the MESSAGES dimension, and it is
                          strictly stronger than the pairwise message swap that instance 4 was found
                          by: if a leg asserts any word of a message, sentinel-replacing that
                          message reds; and if a leg asserts only a prefix the CALLER emits -- the
                          exact shape of instance 4 -- the arm's own mutation SURVIVES and is
                          reported. It is also the discovery dimension at code level, because
                          `"src"`, `"obj"`, `"bin"`, `".fs"` and `"i"` are string literals.
  len-pin                 every `len(...)`, pinned to the literal 999. M5's second half: a leg that
                          asserts a printed count with `[0-9]+` cannot tell a real count from a
                          fabricated one.
  bool-flip               every `True`/`False` literal, inverted. Nine today, and not one of them is
                          decorative: each is a flag some caller of the gate's own functions reads.
  boolop-flip             every two-operand `and`/`or`, swapped. `cond-false`/`cond-true` replace a
                          whole test and `cmp-flip` rewrites one comparison, so neither reaches the
                          connective BETWEEN two comparisons; see its generation site below for the
                          `//`-vs-`////` guard that is exactly that shape.
  report-swap             within one `print(...)`, every reported name substituted for every other
                          name in the SAME call -- the largest operator here (119 today) and the one
                          with the measured provenance: two quantities printed side by side are
                          usually EQUAL in the green case, so a leg that only ever sees a green tree
                          cannot tell which of the two it is reading. See its generation site for the
                          bound and the measurement that chose it.
  dir-drop                one mutation PER PROJECT DIRECTORY UNDER `src/` that holds a subject,
                          enumerated from the filesystem rather than named here, each narrowing
                          `discover()` to exclude that project. This is M5 itself, generalised: it
                          extends to a fourth project on the day one is added, with no edit here.
  early-ok                `main` returns `EX_OK` before doing anything. The unconditional pass.

TEN OPERATORS, AND THE LIST ABOVE IS NOT THE AUTHORITY -- `OPERATORS` below is, and the enumeration,
the second reading and the accounting are all checked against it. An earlier version of this header
named seven of the ten, omitting `bool-flip`, `boolop-flip` and `report-swap`, which between them
generate 131 of 385 mutants; the same three were the ones with no floor entry at all. A prose list
and a predicate that drift apart is how a guard becomes decorative, so neither is now allowed to be
the only place an operator is named.

CONTROLS, BECAUSE AN INSTRUMENT'S SILENCE PROVES NOTHING (#266, and seven instrument faults measured
in this row's own session -- a `dotnet test` that exited 0 while running nothing, a gate graded
against an empty string, and a critic's "always-green" control that was a semantic no-op and duly
reported the unmutated pass count). Three run before any mutant is scored, and all three must land
on their expected side or this program refuses to report at all:

  * the UNMUTATED fixture must PASS -- otherwise every "KILLED" below is killed by the harness;
  * `early-ok` must be KILLED -- otherwise "KILLED" is unreachable and the sweep proves nothing;
  * a semantically INERT edit (a `pass` statement appended to a function body) must SURVIVE --
    otherwise "SURVIVED" is unreachable, every mutant reports killed for a harness reason, and a
    sweep of all-green is the exact false comfort this file exists to refuse.

Each mutant is additionally verified to have CHANGED THE FILE BYTES and to still COMPILE before it
is run, so "SURVIVED" can never mean "was never applied" and never means "did not parse".

AND THE ENUMERATION IS ITSELF ACCOUNTED FOR, SITE BY SITE, because the controls above prove the
sweep can SPEAK and prove nothing about whether it SAW. `token_reading` reads the same gate a second
time through `tokenize` -- the CPython lexer, which shares no code with the `ast` walk above -- and
counts the candidate sites of every one of the ten operators. `main` then asserts an EQUALITY rather
than a floor:

    enumerated(op) + skipped(op) == second reading(op),   for every operator, both directions

Every site the AST walk declines is therefore REPORTED (there is no `continue` that drops one in
silence), and every reported skip must additionally carry a written justification in
`mutants-allowed.txt`, exactly as a survivor must -- because a site the sweep does not measure and a
mutant nothing kills are the same fact about coverage. A silent loss breaks the equality; a loud but
unaccounted loss is an unjustified skip and fails the run. See the block above the check in `main`
for the measurement that made this necessary and for what each direction of the inequality means.

Pure stdlib. No network, no build, no `dotnet`. Each mutant runs against its own private copy of
`scripts/`, `src/` and `tests/signature-doc-siting/`, so the sweep never writes into the repository
it is invoked from.

    python3 tests/signature-doc-siting/mutants.py            # the whole sweep
    python3 tests/signature-doc-siting/mutants.py --jobs 1   # serial, for a clean log
    python3 tests/signature-doc-siting/mutants.py --list     # enumerate without running
"""

from __future__ import annotations

import argparse
import ast
import concurrent.futures
import io
import os
import queue
import re
import shutil
import subprocess
import sys
import tempfile
import token as token_module
import tokenize

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
GATE_REL = os.path.join("scripts", "check-signature-doc-siting.py")
FIXTURE_REL = os.path.join("tests", "signature-doc-siting", "run.sh")
ALLOWED = os.path.join(HERE, "mutants-allowed.txt")

# THE CLOSED SET OF OPERATORS, named once. `enumerate_mutants` may emit only these, `token_reading`
# must count sites for exactly these, and `main` refuses if either disagrees with this tuple -- so an
# operator added to one and not the other is a red check rather than an operator with no floor, which
# is the shape of the defect this file's own guard shipped with.
OPERATORS = (
    "bool-flip",
    "boolop-flip",
    "cmp-flip",
    "cond-false",
    "cond-true",
    "dir-drop",
    "len-pin",
    "num",
    "report-swap",
    "str",
)

# An alphabetic run of two or more characters, not immediately preceded by a backslash -- so `\n`,
# `\t` and `A` inside a literal keep their escape meaning while `src`, `obj` and `fs` do not.
ALPHA_RUN = re.compile(r"(?<!\\)[A-Za-z]{2,}")


class Mutant:
    def __init__(self, mid: str, operator: str, where: str, source: str) -> None:
        self.mid = mid
        self.operator = operator
        self.where = where
        self.source = source


def splice(text: str, lineno: int, col: int, end_lineno: int, end_col: int, new: str) -> str:
    """Replace the 1-based (lineno, col)..(end_lineno, end_col) span of `text` with `new`.

    Offsets are UTF-8 byte columns, which is what `ast` reports; the gate is ASCII today but
    encoding to bytes and back costs nothing and removes the assumption.
    """
    lines = text.split("\n")
    head = lines[lineno - 1].encode("utf-8")[:col].decode("utf-8")
    tail = lines[end_lineno - 1].encode("utf-8")[end_col:].decode("utf-8")
    return "\n".join(lines[: lineno - 1] + [head + new + tail] + lines[end_lineno:])


def _span(text: str, node: ast.AST) -> str:
    """The exact source text of `node`, read back out of `text` by its reported byte offsets."""
    lines = text.split("\n")
    if node.lineno == node.end_lineno:
        return lines[node.lineno - 1].encode("utf-8")[node.col_offset : node.end_col_offset].decode("utf-8")
    out = [lines[node.lineno - 1].encode("utf-8")[node.col_offset :].decode("utf-8")]
    out.extend(lines[node.lineno : node.end_lineno - 1])
    out.append(lines[node.end_lineno - 1].encode("utf-8")[: node.end_col_offset].decode("utf-8"))
    return "\n".join(out)


def sentinel(value: str) -> str | None:
    """`value` with its alphabetic runs replaced by same-length `X`s, or None for the empty literal.

    THE `any(c.isalnum())` GUARD THIS USED TO CARRY DROPPED 47 SITES, and they were the gate's entire
    lexical vocabulary: every slash run it tests for, the block-comment opener and closer, all three
    string openers (ordinary, verbatim and triple-quoted), the bare quote and the bare apostrophe,
    the doubled quote, the backslash -- plus the separators and indents of the gate's own messages.
    A gate whose subject is a comment MARKER cannot have its markers be the one class of constant
    its sweep does not mutate. Measured when they were first generated: 47 mutants, 41 killed, 6
    survived, every load-bearing delimiter among the killed -- the three-slash doc marker (47/39),
    the two-slash ordinary comment (82/4), the disqualifying fourth slash (84/2), the block-comment
    opener (79/7) and its closer (80/6), the string opener (79/7) and the character-literal quote
    (81/5). Named by construct rather than by line, because a line number for a site in ANOTHER file
    rots silently and this file's whole subject is a claim that rotted. The six survivors were the
    gate's own message separators and indents, and `run.sh` now pins all six. So the guard is gone
    and only the empty literal is left, which has no same-length sentinel to replace it with.
    """
    mutated = ALPHA_RUN.sub(lambda m: "X" * len(m.group(0)), value)
    if mutated != value:
        return mutated
    # A literal with no run of two letters -- `"i"`, `"#"`, `"///"`, `'"'`. Every one of them is a
    # behavioural constant: `p + "i"` is the whole sibling `.fsi` test, and `"///"` is the gate's
    # subject. `X * len(value)` keeps the length, so a raw f-string span stays the width it was.
    if value:
        return "X" * len(value)
    return None


CMP_FLIP = {
    ast.Eq: "!=",
    ast.NotEq: "==",
    ast.Lt: ">=",
    ast.LtE: ">",
    ast.Gt: "<=",
    ast.GtE: "<",
    ast.In: "not in",
    ast.NotIn: "in",
    ast.Is: "is not",
    ast.IsNot: "is",
}


def docstring_spans(tree: ast.AST) -> set[tuple[int, int]]:
    out: set[tuple[int, int]] = set()
    for node in ast.walk(tree):
        if isinstance(node, (ast.Module, ast.FunctionDef, ast.AsyncFunctionDef, ast.ClassDef)):
            body = getattr(node, "body", None)
            if (
                body
                and isinstance(body[0], ast.Expr)
                and isinstance(body[0].value, ast.Constant)
                and isinstance(body[0].value.value, str)
            ):
                first = body[0].value
                out.add((first.lineno, first.col_offset))
    return out


class Skip:
    """One candidate site the enumeration declined, named so the accounting can subtract it.

    EVERY declined site becomes one of these. There is no `continue` left in `enumerate_mutants`
    that drops a site without recording it: the repair-phase critic measured that `sentinel()`
    returning None was dropped by a bare `continue` that never reached this list, so the program
    printed `sites SKIPPED (reported, never silent): 1` when the truth was 48.
    """

    def __init__(self, operator: str, site: str, reason: str) -> None:
        self.operator = operator
        self.site = site
        self.reason = reason

    def __str__(self) -> str:
        return f"{self.site}: {self.reason}"


def enumerate_mutants(gate_source: str, projects: list[str]) -> tuple[list[Mutant], list[Skip]]:
    """(mutants, skipped) enumerated from the gate's AST and from the discovered project list."""
    tree = ast.parse(gate_source)
    docs = docstring_spans(tree)
    mutants: list[Mutant] = []
    skipped: list[Skip] = []
    seen: set[str] = set()

    def add(operator: str, node: ast.AST, new: str, note: str = "", tag: str = "") -> None:
        where = f"L{node.lineno}"
        # `tag` keeps several DISTINCT mutations at one source position from colliding on one id and
        # silently deduping down to the first — `report-swap` generates one per candidate name at the
        # same site, and without this only one of them would ever run.
        mid = f"{operator}@{node.lineno}:{node.col_offset}" + (f"={tag}" if tag else "")
        if mid in seen:
            # A COLLIDING ID IS A DROPPED SITE, and it used to return here in silence. No collision
            # occurs at today's head; it is reachable through a `print(...)` nested inside another,
            # whose names `ast.walk` reaches from both calls.
            skipped.append(Skip(operator, mid, "duplicate mutant id, already enumerated"))
            return
        seen.add(mid)
        mutated = splice(
            gate_source, node.lineno, node.col_offset, node.end_lineno, node.end_col_offset, new
        )
        if mutated == gate_source:
            skipped.append(Skip(operator, mid, "replacement is a no-op"))
            return
        try:
            compile(mutated, "<mutant>", "exec")
        except SyntaxError as exc:
            skipped.append(Skip(operator, mid, f"mutant does not compile ({exc.msg})"))
            return
        mutants.append(Mutant(mid, operator, where + (f" {note}" if note else ""), mutated))

    for node in ast.walk(tree):
        # CONDITIONS.
        if isinstance(node, (ast.If, ast.While)):
            add("cond-false", node.test, "False")
            add("cond-true", node.test, "True")
        # COMPARISONS. The second reading counts comparison OPERATORS, so a chain contributes one
        # per operator there and must contribute one skip per operator here, or the two readings
        # would disagree for a reason that is about neither of them.
        if isinstance(node, ast.Compare):
            site = f"cmp-flip@{node.lineno}:{node.col_offset}"
            if len(node.ops) != 1:
                for _ in node.ops:
                    skipped.append(
                        Skip(
                            "cmp-flip",
                            site,
                            f"chained comparison ({len(node.ops)} operators); this operator rewrites "
                            f"a single comparison only",
                        )
                    )
            else:
                flip = CMP_FLIP.get(type(node.ops[0]))
                if flip is None:
                    skipped.append(
                        Skip("cmp-flip", site, f"{type(node.ops[0]).__name__} has no flip in CMP_FLIP")
                    )
                else:
                    left = _span(gate_source, node.left)
                    right = _span(gate_source, node.comparators[0])
                    add("cmp-flip", node, f"{left} {flip} {right}")
        # CONSTANTS: numbers, then non-docstring strings.
        if isinstance(node, ast.Constant):
            if (node.lineno, node.col_offset) in docs:
                continue
            site = f"str@{node.lineno}:{node.col_offset}"
            if isinstance(node.value, bool):
                add("bool-flip", node, "False" if node.value else "True")
            elif isinstance(node.value, int):
                add("num", node, str(node.value + 1))
            elif isinstance(node.value, str):
                new_value = sentinel(node.value)
                raw = _span(gate_source, node)
                if new_value is None:
                    skipped.append(Skip("str", site, "empty literal has no same-length sentinel"))
                elif raw[:1] in ("'", '"') or raw[:2].lower() in ("r'", 'r"', "f'", 'f"', "b'", 'b"'):
                    add("str", node, repr(new_value))
                elif "{" not in raw and "}" not in raw and "\\" not in raw:
                    # A literal segment inside an f-string: its span is the raw text, unquoted.
                    #
                    # A SPAN MAY CROSS AN IMPLICIT CONCATENATION, because `ast` merges the segments
                    # either side of one into a single `Constant` whose span then covers the closing
                    # and opening quotes between them. Most such splices are still well formed -- the
                    # two physical lines collapse into one valid f-string -- and five of this gate's
                    # literals are mutated exactly that way. Where the result is not well formed,
                    # `compile()` in `add` catches it and records the skip; guarding here on a quote
                    # in the raw span would be tighter than the truth and cost those five mutants.
                    add("str", node, new_value)
                else:
                    skipped.append(
                        Skip("str", site, "unrewritable raw span (brace or backslash in an f-string segment)")
                    )
        # CONNECTIVES. `cond-false`/`cond-true` replace a whole test and `cmp-flip` rewrites one
        # comparison, so neither can reach the `and`/`or` BETWEEN two comparisons -- and a guard
        # written `A and B` whose fixture only ever exercises inputs where A and B agree is a guard
        # with an unasserted half. `//`-vs-`////` is exactly that shape (`i + 3 < n and line[i + 3]`).
        if isinstance(node, ast.BoolOp):
            if len(node.values) == 2:
                parts = [_span(gate_source, value) for value in node.values]
                flip = "or" if isinstance(node.op, ast.And) else "and"
                add("boolop-flip", node, f"{parts[0]} {flip} {parts[1]}")
            else:
                # As with a chained comparison: the second reading counts `and`/`or` TOKENS, and a
                # k-operand connective carries k-1 of them.
                for _ in range(len(node.values) - 1):
                    skipped.append(
                        Skip(
                            "boolop-flip",
                            f"boolop-flip@{node.lineno}:{node.col_offset}",
                            f"{len(node.values)}-operand connective; this operator rewrites a "
                            f"two-operand one only",
                        )
                    )
        # COUNT PINNING.
        if isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "len":
            add("len-pin", node, "999")

    # REPORTED QUANTITIES, CONFUSED WITH EACH OTHER. Within one `print(...)`, every name whose value
    # reaches the reader is swapped for every other name in the SAME call. This is a deliberately
    # tight bound on a large space, and the bound is where the risk actually concentrates: two
    # quantities printed side by side are usually EQUAL in the green case -- `observed` and
    # `baseline` are equal exactly when this gate passes -- so a leg that only ever sees a green tree
    # cannot tell which of the two it is reading, and neither can a leg over a regex.
    #
    # Measured, and this operator exists because of it: substituting `observed` for `baseline` in
    # `total_hits` left the fixture at 82 passed, 0 failed at the head that added the rest of this
    # sweep. Unbounded same-scope substitution would enumerate 3,907 mutants over this gate (877 if
    # restricted to reported values), which is not a required check anyone would keep; this bound
    # gives a few dozen and covers the class that survived.
    for node in ast.walk(tree):
        if not (isinstance(node, ast.Call) and isinstance(node.func, ast.Name) and node.func.id == "print"):
            continue
        names = [
            child
            for child in ast.walk(node)
            if isinstance(child, ast.Name) and isinstance(child.ctx, ast.Load) and child.id != "print"
        ]
        pool = sorted({child.id for child in names})
        for child in names:
            for replacement in pool:
                if replacement != child.id:
                    add("report-swap", child, replacement, f"{child.id} -> {replacement}", replacement)

    # DISCOVERY, per project, enumerated from the filesystem rather than named here.
    anchor = "    subjects = [p for p in sources if os.path.exists(p + \"i\")]"
    for project in projects:
        if anchor not in gate_source:
            # One skip PER PROJECT, not one and a `break`: the second reading counts projects on the
            # filesystem, so a lost anchor has to account for every one of them or the equality would
            # read as a silent loss of two and a reported loss of one.
            skipped.append(Skip("dir-drop", f"dir-drop@{project}", "discover() anchor not found"))
            continue
        injected = (
            f'    sources = [p for p in sources if "/{project}/" not in p.replace(os.sep, "/")]\n'
            + anchor
        )
        mutants.append(
            Mutant(
                f"dir-drop@{project}",
                "dir-drop",
                f"discover() excludes src/{project}",
                gate_source.replace(anchor, injected, 1),
            )
        )

    return mutants, skipped


# ---- THE SECOND READING ---------------------------------------------------------------------------
#
# Everything above reads the gate through `ast`. Everything below reads the SAME BYTES through
# `tokenize`, which shares no code with the parser's tree walk: it is the CPython lexer, so a change
# to `ast`, a walk that silently yields nothing, or a refactor that renames an anchor cannot move
# both readings the same way. That is the whole point -- a floor derived from the reading it is
# supposed to check is not a check.
#
# WHY A LEXER AND NOT A REGEX. A regex over lines cannot tell a `==` in code from a `==` inside this
# gate's own 150-line docstring, and this gate is mostly prose: its docstrings hold `'` , `///`,
# `while`, `and` and `!=` in quantity. `tokenize` classifies each of those as STRING and the guard
# never sees them. The `if|elif|while` line count this file used to carry for `cond-*` had exactly
# that exposure and was the only floor here that was ever real.

_STMT_BOUNDARY = (token_module.NEWLINE, token_module.INDENT, token_module.DEDENT)
_CMP_OP_TOKENS = frozenset({"==", "!=", "<", "<=", ">", ">="})
_OPENERS = frozenset("([{")
_CLOSERS = frozenset(")]}")
# Names that are keywords, so never an `ast.Name` a `report-swap` could substitute.
_KEYWORDS = frozenset(
    """and as assert async await break class continue def del elif else except finally for from
    global if import in is lambda nonlocal not or pass raise return try while with yield
    None True False""".split()
)
_FSTRING_MIDDLE = getattr(token_module, "FSTRING_MIDDLE", None)
_FSTRING_START = getattr(token_module, "FSTRING_START", None)
_FSTRING_END = getattr(token_module, "FSTRING_END", None)


def _significant_tokens(gate_source: str) -> list[tokenize.TokenInfo]:
    """The gate's token stream, less the tokens that carry no syntax (comments and blank lines)."""
    return [
        tok
        for tok in tokenize.generate_tokens(io.StringIO(gate_source).readline)
        if tok.type not in (token_module.COMMENT, token_module.NL, token_module.ENCODING)
    ]


def _is_integer_literal(text: str) -> bool:
    """A NUMBER token that `ast` renders as `Constant(int)` -- not a float, not complex."""
    lowered = text.replace("_", "").lower()
    if lowered.endswith("j"):
        return False
    if lowered.startswith(("0x", "0o", "0b")):
        return True
    return "." not in lowered and "e" not in lowered


def _string_groups(toks: list[tokenize.TokenInfo]) -> list[tuple[int, int]]:
    """Half-open index ranges of maximal adjacent string literals -- one per implicit concatenation.

    `"a" "b"` is TWO tokens and ONE `ast.Constant`, so a reading that counted tokens would over-count
    the very operator it is checking. An f-string's interior travels inside its group.
    """
    groups: list[tuple[int, int]] = []
    index = 0
    while index < len(toks):
        if toks[index].type != token_module.STRING and toks[index].type != _FSTRING_START:
            index += 1
            continue
        start = index
        depth = 0
        while index < len(toks):
            kind = toks[index].type
            if kind == _FSTRING_START:
                depth += 1
            elif kind == _FSTRING_END:
                depth -= 1
            elif depth == 0 and kind != token_module.STRING:
                break
            index += 1
        groups.append((start, index))
    return groups


def _literal_segment_runs(toks: list[tokenize.TokenInfo], start: int, end: int) -> int:
    """`ast.Constant` string nodes inside one group: maximal runs of adjacent literal segments.

    `f"a{x}b"` is two constants because the replacement field separates them; `f"a{x}b" f"c"` is
    three tokens' worth of literal but only THREE constants, because `b` and `c` are adjacent across
    the concatenation and `ast` merges them. Runs reproduce that exactly.
    """
    runs = 0
    previous_was_segment = False
    for index in range(start, end):
        kind = toks[index].type
        if kind in (_FSTRING_START, _FSTRING_END):
            continue  # a quote boundary does not separate two literal segments
        is_segment = kind == token_module.STRING or kind == _FSTRING_MIDDLE
        if is_segment and not previous_was_segment:
            runs += 1
        previous_was_segment = is_segment
    return runs


def _is_docstring_group(toks: list[tokenize.TokenInfo], start: int, end: int) -> bool:
    """Is this group an expression statement consisting of nothing but the literal?

    That is `docstring_spans`' subject read textually. It also catches a bare string statement that
    is not a docstring, which `ast` would mutate -- an over-count in the direction that LOWERS this
    reading, and the gate holds none.
    """
    before = toks[start - 1] if start else None
    after = toks[end] if end < len(toks) else None
    at_statement_start = before is None or before.type in _STMT_BOUNDARY
    is_whole_statement = after is not None and after.type == token_module.NEWLINE
    return at_statement_start and is_whole_statement


def token_reading(gate_source: str, projects: list[str]) -> dict[str, int]:
    """Candidate sites per operator, counted from the token stream and the filesystem.

    Raises `SystemExit` on an interpreter whose `tokenize` cannot see inside an f-string, because
    such a reading would under-count `report-swap`, `len-pin` and `str` without saying so -- a
    silently loose floor, which is the defect being repaired.
    """
    if _FSTRING_MIDDLE is None:
        raise SystemExit(
            "harness: this interpreter's `tokenize` does not emit FSTRING_* tokens (Python < 3.12), "
            "so the second reading cannot see the names and literals inside an f-string and would "
            "under-count them in silence. Run this sweep on Python 3.12 or newer."
        )

    toks = _significant_tokens(gate_source)
    counts = {operator: 0 for operator in OPERATORS}

    # DISCOVERY: the filesystem is the second reading, exactly as it is the first one.
    counts["dir-drop"] = len(projects)

    depth = 0
    pending_for_at: set[int] = set()
    for index, tok in enumerate(toks):
        previous = toks[index - 1] if index else None
        following = toks[index + 1] if index + 1 < len(toks) else None

        if tok.type == token_module.OP:
            if tok.string in _OPENERS:
                depth += 1
            elif tok.string in _CLOSERS:
                pending_for_at.discard(depth)
                depth -= 1
            elif tok.string in _CMP_OP_TOKENS:
                counts["cmp-flip"] += 1
            continue

        if tok.type == token_module.NUMBER:
            if _is_integer_literal(tok.string):
                counts["num"] += 1
            continue

        if tok.type != token_module.NAME:
            continue

        if tok.string in ("True", "False"):
            counts["bool-flip"] += 1
        elif tok.string in ("and", "or"):
            counts["boolop-flip"] += 1
        elif tok.string == "for":
            pending_for_at.add(depth)
        elif tok.string == "in":
            # `for x in xs` is not a comparison. Its `in` is the only one that is not, and it is
            # told apart by the `for` that opened the clause at this bracket depth.
            if depth in pending_for_at:
                pending_for_at.discard(depth)
            else:
                counts["cmp-flip"] += 1
        elif tok.string == "is":
            counts["cmp-flip"] += 1  # `is not` is ONE operator; its `not` is counted nowhere
        elif tok.string == "len":
            is_call = following is not None and following.type == token_module.OP and following.string == "("
            is_attribute = previous is not None and previous.type == token_module.OP and previous.string == "."
            if is_call and not is_attribute:
                counts["len-pin"] += 1
        elif tok.string in ("if", "elif", "while") and depth == 0:
            # A statement, not a ternary and not a comprehension guard: those sit at depth > 0 or
            # follow an expression rather than a statement boundary, and neither is an `ast.If`.
            at_statement_start = previous is None or previous.type in _STMT_BOUNDARY or (
                previous.type == token_module.OP and previous.string in (":", ";")
            )
            if at_statement_start:
                counts["cond-false"] += 1
                counts["cond-true"] += 1

    for start, end in _string_groups(toks):
        if not _is_docstring_group(toks, start, end):
            counts["str"] += _literal_segment_runs(toks, start, end)

    counts["report-swap"] = _report_swap_sites(toks)
    return counts


def _report_swap_sites(toks: list[tokenize.TokenInfo]) -> int:
    """Substitutions `report-swap` must generate: for each `print(...)`, occurrences x (distinct - 1).

    The names counted are the ones `ast` reports as `Name` in Load context: an attribute's leading
    name (`os` in `os.path.exists`) counts, the attribute itself does not; a keyword argument's name
    is not a `Name`; a comprehension target is Store context, not Load.
    """
    total = 0
    for index, tok in enumerate(toks):
        if not (tok.type == token_module.NAME and tok.string == "print"):
            continue
        opener = toks[index + 1] if index + 1 < len(toks) else None
        if opener is None or opener.type != token_module.OP or opener.string != "(":
            continue
        depth = 0
        names: list[str] = []
        in_for_target = False
        cursor = index + 1
        while cursor < len(toks):
            inner = toks[cursor]
            if inner.type == token_module.OP and inner.string in _OPENERS:
                depth += 1
            elif inner.type == token_module.OP and inner.string in _CLOSERS:
                depth -= 1
                if depth == 0:
                    break
            elif inner.type == token_module.NAME:
                if inner.string == "for":
                    in_for_target = True
                elif inner.string == "in":
                    in_for_target = False
                elif inner.string not in _KEYWORDS and inner.string != "print" and not in_for_target:
                    previous = toks[cursor - 1]
                    following = toks[cursor + 1] if cursor + 1 < len(toks) else None
                    is_attribute = previous.type == token_module.OP and previous.string == "."
                    is_keyword_argument = (
                        following is not None
                        and following.type == token_module.OP
                        and following.string == "="
                    )
                    if not is_attribute and not is_keyword_argument:
                        names.append(inner.string)
            cursor += 1
        distinct = len(set(names))
        if distinct > 1:
            total += len(names) * (distinct - 1)
    return total


def control_early_ok(gate_source: str) -> str:
    """`main` returns EX_OK before doing anything -- the unconditional pass. Must be KILLED."""
    tree = ast.parse(gate_source)
    for node in ast.walk(tree):
        if isinstance(node, ast.FunctionDef) and node.name == "main":
            first = node.body[1] if len(node.body) > 1 else node.body[0]
            indent = " " * first.col_offset
            lines = gate_source.split("\n")
            lines.insert(first.lineno - 1, f"{indent}return EX_OK")
            return "\n".join(lines)
    raise SystemExit("control: main() not found in the gate")


def control_inert(gate_source: str) -> str:
    """A `pass` statement appended to `discover`'s body: syntactically real, semantically nothing.

    Must SURVIVE. If it does not, every KILLED verdict in this sweep is a harness artefact rather
    than a fixture assertion, which is the failure mode that produced this row's round-3 control.
    """
    tree = ast.parse(gate_source)
    for node in ast.walk(tree):
        if isinstance(node, ast.FunctionDef) and node.name == "discover":
            last = node.body[-1]
            indent = " " * last.col_offset
            lines = gate_source.split("\n")
            lines.insert(last.end_lineno, f"{indent}pass")
            return "\n".join(lines)
    raise SystemExit("control: discover() not found in the gate")


def prepare_copy(dest: str) -> None:
    """A private repo copy holding exactly what the fixture reads: scripts/, src/, tests/<this>."""
    os.makedirs(os.path.join(dest, "scripts"), exist_ok=True)
    os.makedirs(os.path.join(dest, "tests"), exist_ok=True)
    shutil.copytree(
        os.path.join(REPO, "src"),
        os.path.join(dest, "src"),
        ignore=shutil.ignore_patterns("obj", "bin"),
        dirs_exist_ok=True,
    )
    shutil.copytree(
        HERE, os.path.join(dest, "tests", "signature-doc-siting"), dirs_exist_ok=True
    )
    shutil.copy2(os.path.join(REPO, GATE_REL), os.path.join(dest, GATE_REL))


def run_fixture(root: str, gate_source: str) -> tuple[int, str]:
    gate_path = os.path.join(root, GATE_REL)
    with open(gate_path, "w", encoding="utf-8") as handle:
        handle.write(gate_source)
    with open(gate_path, encoding="utf-8") as handle:
        if handle.read() != gate_source:
            raise SystemExit(f"harness: {gate_path} does not hold the source it was given")
    proc = subprocess.run(
        ["bash", os.path.join(root, FIXTURE_REL)],
        capture_output=True,
        text=True,
        env={**os.environ, "PYTHONDONTWRITEBYTECODE": "1"},
    )
    # AND STILL HOLDS IT AFTERWARDS. Checking only before the run cannot see a copy that a second
    # thread rewrote WHILE the fixture was reading it -- which is precisely the race that made this
    # sweep report `cond-false@403` killed when nothing killed it. Root ownership is now exclusive,
    # so this must never fire; it is here because "must never fire" is exactly the claim that earned
    # this row its repair phase, and an unchecked one is worth nothing.
    with open(gate_path, encoding="utf-8") as handle:
        if handle.read() != gate_source:
            raise SystemExit(
                f"harness: {gate_path} changed underneath a running fixture -- two mutants shared "
                f"one copy, so every verdict in this sweep is unsound. Refusing to report."
            )
    return proc.returncode, proc.stdout + proc.stderr


def tally(output: str) -> str:
    match = re.search(r"fixture: (\d+) passed, (\d+) failed", output)
    return f"{match.group(1)}/{match.group(2)}" if match else "no tally"


def read_allowed() -> dict[str, str]:
    out: dict[str, str] = {}
    if not os.path.exists(ALLOWED):
        return out
    with open(ALLOWED, encoding="utf-8") as handle:
        for line in handle:
            stripped = line.strip()
            if not stripped or stripped.startswith("#"):
                continue
            parts = stripped.split(None, 1)
            if len(parts) == 2:
                out[parts[0]] = parts[1]
    return out


def discovered_projects() -> list[str]:
    """Every immediate subdirectory of src/ holding at least one .fs with a sibling .fsi."""
    src = os.path.join(REPO, "src")
    found: list[str] = []
    for entry in sorted(os.listdir(src)):
        base = os.path.join(src, entry)
        if not os.path.isdir(base):
            continue
        for dirpath, dirnames, filenames in os.walk(base):
            dirnames[:] = [d for d in dirnames if d not in ("obj", "bin")]
            if any(
                name.endswith(".fs") and os.path.exists(os.path.join(dirpath, name + "i"))
                for name in filenames
            ):
                found.append(entry)
                break
    return found


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--jobs", type=int, default=min(16, os.cpu_count() or 1))
    parser.add_argument("--list", action="store_true", help="enumerate mutants and stop")
    args = parser.parse_args(argv)

    with open(os.path.join(REPO, GATE_REL), encoding="utf-8") as handle:
        gate_source = handle.read()

    projects = discovered_projects()
    mutants, skipped = enumerate_mutants(gate_source, projects)

    print(f"gate: {GATE_REL}")
    print(f"projects with subjects under src/: {', '.join(projects) or '(none)'}")
    print(f"mutants enumerated: {len(mutants)}")
    by_operator: dict[str, int] = {}
    for mutant in mutants:
        by_operator[mutant.operator] = by_operator.get(mutant.operator, 0) + 1
    for operator in sorted(by_operator):
        print(f"  {operator}: {by_operator[operator]}")
    skipped_by_operator: dict[str, int] = {}
    for skip in skipped:
        skipped_by_operator[skip.operator] = skipped_by_operator.get(skip.operator, 0) + 1
    print(f"sites SKIPPED (reported, never silent): {len(skipped)}")
    for skip in skipped:
        print(f"  {skip}")

    if args.list:
        for mutant in mutants:
            print(f"  {mutant.mid}  {mutant.where}")
        return 0

    # A SWEEP THAT ENUMERATED NOTHING WOULD REPORT `0 mutants, 0 killed, 0 survived` AND EXIT 0.
    # That is this row's own subject turned on its own instrument -- "0 survivors" and "0 mutants"
    # are the same bytes -- and it is reachable for real: an `ast` change, a parse that silently
    # yields an empty tree, or a refactor that renames the anchors would each produce it. So the
    # enumeration is checked against a SECOND reading of the same file, through a different reader,
    # rather than against a number typed here.
    #
    # THAT SENTENCE WAS TRUE OF THREE OPERATORS AND FALSE OF SEVEN, and the repair phase's critic
    # measured what the difference bought. `report-swap` (119 mutants), `bool-flip` (9) and
    # `boolop-flip` (3) had no entry at all; `num`, `str`, `len-pin` and `cmp-flip` had the literal
    # `1`. Executed through this `main()` with all three controls landing: suppressing the first
    # three printed `254 mutants, 254 killed, 0 survived, 0 UNJUSTIFIED` at exit 0 with no
    # `REFUSING:` line, and additionally capping the other four at one each printed `103 mutants,
    # 103 killed` -- 282 of 385, 73% of the sweep, able to vanish with this required check green and
    # this guard silent. A floor of `1` for an operator that generates 59 is a number typed here
    # wearing a predicate's clothes.
    #
    # SO THE RELATION IS AN EQUALITY, PER OPERATOR, IN BOTH DIRECTIONS:
    #
    #     enumerated(op) + skipped(op) == token_reading(op)
    #
    #   * enumerated + skipped BELOW the reading is a site that vanished without being reported.
    #     That is the vacuity this whole file exists to refuse, and it is the direction the old
    #     floor was aimed at.
    #   * ABOVE it is the second reading under-counting -- which does not fabricate coverage, but
    #     does make the guard loose in exactly the way that let 73% through, so it refuses too. The
    #     remedy is to teach `token_reading` the construct, or to record the skip that explains it.
    #
    # Both remedies are cheap and both are visible; a guard that quietly tolerates disagreement
    # between its two readings has stopped being a second reading at all.
    if not projects:
        print("REFUSING: no project under src/ holds a subject, so dir-drop asserts nothing.")
        return 3

    reading = token_reading(gate_source, projects)

    # AND NEITHER SIDE MAY GROW AN OPERATOR THE OTHER HAS NEVER HEARD OF. This is the guard that
    # keeps the finding above from regenerating: the three operators with no floor were added to the
    # AST walk and nobody added them here, and nothing said so.
    unknown_enumerated = sorted(set(by_operator) - set(OPERATORS))
    unknown_skipped = sorted(set(skipped_by_operator) - set(OPERATORS))
    unread = sorted(set(OPERATORS) - set(reading))
    if unknown_enumerated or unknown_skipped or unread:
        print("REFUSING: the operator registry, the enumeration and the second reading disagree.")
        for operator in unknown_enumerated:
            print(f"  {operator}: enumerated, but not in OPERATORS -- it would have no second reading")
        for operator in unknown_skipped:
            print(f"  {operator}: skipped, but not in OPERATORS -- its losses would be unaccounted")
        for operator in unread:
            print(f"  {operator}: in OPERATORS, but `token_reading` counts no sites for it")
        return 3

    disagreements = []
    for operator in OPERATORS:
        enumerated = by_operator.get(operator, 0)
        dropped = skipped_by_operator.get(operator, 0)
        expected = reading[operator]
        if enumerated + dropped != expected:
            direction = "fewer" if enumerated + dropped < expected else "more"
            disagreements.append(
                f"{operator}: enumerated {enumerated} + skipped {dropped} = {enumerated + dropped}, "
                f"but a second reading of the same gate finds {expected} candidate site(s) "
                f"-- {direction} than the tree walk accounted for"
            )
    if disagreements:
        print("REFUSING: the AST enumeration and the token-stream reading of the same gate disagree,")
        print("so a green sweep below would mean 'nothing was measured' rather than 'nothing")
        print("survived'. Per operator:")
        for entry in disagreements:
            print(f"  {entry}")
        return 3

    print()
    print("enumeration accounted for, per operator (enumerated + skipped == second reading):")
    for operator in OPERATORS:
        enumerated = by_operator.get(operator, 0)
        dropped = skipped_by_operator.get(operator, 0)
        print(f"  {operator:<14} {enumerated:>4} + {dropped:>2} == {reading[operator]:>4}")

    jobs = max(1, args.jobs)
    tmp = tempfile.mkdtemp(prefix="signature-doc-siting-mutants.")
    try:
        roots = []
        for index in range(jobs):
            root = os.path.join(tmp, f"repo{index}")
            prepare_copy(root)
            roots.append(root)

        # ---- CONTROLS. All three must land, or nothing below is evidence. --------------------
        print()
        print("controls:")
        rc, out = run_fixture(roots[0], gate_source)
        print(f"  unmutated fixture         rc={rc} ({tally(out)}) -- want PASS")
        if rc != 0:
            print("REFUSING: the unmutated fixture does not pass, so every kill below is a harness artefact.")
            print(out[-4000:])
            return 3

        rc, out = run_fixture(roots[0], control_early_ok(gate_source))
        print(f"  control early-ok          rc={rc} ({tally(out)}) -- want KILLED")
        if rc == 0:
            print("REFUSING: an unconditional `return EX_OK` survives, so KILLED is unreachable.")
            return 3

        inert = control_inert(gate_source)
        rc, out = run_fixture(roots[0], inert)
        print(f"  control inert (`pass`)    rc={rc} ({tally(out)}) -- want SURVIVED")
        if rc != 0:
            print("REFUSING: a semantically inert edit is reported killed, so SURVIVED is unreachable.")
            print(out[-4000:])
            return 3

        # ---- THE SWEEP ------------------------------------------------------------------------
        print()
        results: dict[str, tuple[bool, str]] = {}

        # EACH RUNNING MUTANT OWNS ITS COPY EXCLUSIVELY, CHECKED OUT OF A QUEUE.
        #
        # This was `roots[index % jobs]`, and that is a RACE that silently fabricates verdicts. With
        # `map`, tasks finish out of order, so two in-flight indices can differ by a multiple of
        # `jobs` and address the SAME copy -- each overwriting the other's gate between the write and
        # the fixture run. The verdict then belongs to whichever source won, in either direction.
        #
        # It was not theoretical and it was not caught locally: at `--jobs 16` the sweep reported
        # `cond-false@403` KILLED, and CI at `--jobs 4` reported it SURVIVED. The truth is SURVIVED,
        # reproduced by hand single-threaded three times over. A harness that reports a kill for a
        # mutant nothing kills is the exact failure this whole row exists to refuse, one level up:
        # the sweep's silence about a dimension has to mean the dimension is asserted.
        available: queue.Queue[str] = queue.Queue()
        for root in roots:
            available.put(root)

        def work(mutant: Mutant) -> tuple[str, bool, str]:
            root = available.get()
            try:
                rc, out = run_fixture(root, mutant.source)
            finally:
                available.put(root)
            return mutant.mid, rc != 0, tally(out)

        with concurrent.futures.ThreadPoolExecutor(max_workers=jobs) as pool:
            for mid, killed, counts in pool.map(work, mutants):
                results[mid] = (killed, counts)

        # Restore every copy's gate, so a later reader of the temp dir is not misled.
        for root in roots:
            with open(os.path.join(root, GATE_REL), "w", encoding="utf-8") as handle:
                handle.write(gate_source)
    finally:
        shutil.rmtree(tmp, ignore_errors=True)

    allowed = read_allowed()
    survivors = [m for m in mutants if not results[m.mid][0]]
    killed = len(mutants) - len(survivors)

    for mutant in mutants:
        was_killed, counts = results[mutant.mid]
        if not was_killed:
            note = allowed.get(mutant.mid)
            mark = "SURVIVED (allowed)" if note else "SURVIVED"
            print(f"  {mark:<18} {mutant.mid:<28} {mutant.where}")
            if note:
                print(f"                     -> {note}")
        else:
            print(f"  killed   ({counts:>7}) {mutant.mid:<28} {mutant.where}")

    unjustified = [m for m in survivors if m.mid not in allowed]

    # A SKIPPED SITE IS A SURVIVOR'S TWIN AND IS HELD TO THE SAME BAR. The equality above proves
    # every declined site was REPORTED; it cannot prove any of them was harmless, and a broken
    # `splice` that made every mutation fail to compile would satisfy it perfectly while enumerating
    # nothing. So each skip carries a `skip:<site>` line in `mutants-allowed.txt` with its written
    # justification, in the same shrink-only file, for the same reason: a site the sweep does not
    # measure and a mutant nothing kills are one fact about coverage wearing two names.
    unjustified_skips = [s for s in skipped if f"skip:{s.site}" not in allowed]
    justified_skips = [s for s in skipped if f"skip:{s.site}" in allowed]

    print()
    print(
        f"signature-doc-siting mutation sweep: {len(mutants)} mutants, {killed} killed, "
        f"{len(survivors)} survived ({len(survivors) - len(unjustified)} justified), "
        f"{len(unjustified)} UNJUSTIFIED; {len(skipped)} site(s) skipped "
        f"({len(justified_skips)} justified), {len(unjustified_skips)} UNJUSTIFIED"
    )
    live = {m.mid for m in survivors} | {f"skip:{s.site}" for s in skipped}
    stale = sorted(set(allowed) - live)
    if stale:
        print(f"stale entries in {os.path.relpath(ALLOWED, REPO)} -- these mutants no longer survive")
        print("and these sites are no longer skipped, so delete them:")
        for mid in stale:
            print(f"  {mid}")
    if unjustified_skips:
        print()
        print("Each UNJUSTIFIED skip is a candidate site this sweep declined to measure.")
        print("Make it mutatable, or justify it in mutants-allowed.txt as `skip:<site> <why>`:")
        for skip in unjustified_skips:
            print(f"  skip:{skip.site}  ({skip.reason})")
    if unjustified:
        print()
        print("Each UNJUSTIFIED mutant is a dimension of the gate the fixture does not assert.")
        print("Add a fixture leg that reds on it, or justify it in mutants-allowed.txt.")
    if unjustified or unjustified_skips or stale:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
