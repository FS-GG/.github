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
  dir-drop                one mutation PER PROJECT DIRECTORY UNDER `src/` that holds a subject,
                          enumerated from the filesystem rather than named here, each narrowing
                          `discover()` to exclude that project. This is M5 itself, generalised: it
                          extends to a fourth project on the day one is added, with no edit here.
  early-ok                `main` returns `EX_OK` before doing anything. The unconditional pass.

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
import os
import queue
import re
import shutil
import subprocess
import sys
import tempfile

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.abspath(os.path.join(HERE, "..", ".."))
GATE_REL = os.path.join("scripts", "check-signature-doc-siting.py")
FIXTURE_REL = os.path.join("tests", "signature-doc-siting", "run.sh")
ALLOWED = os.path.join(HERE, "mutants-allowed.txt")

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
    """`value` with its alphabetic runs replaced by same-length `X`s, or None if nothing changed."""
    mutated = ALPHA_RUN.sub(lambda m: "X" * len(m.group(0)), value)
    if mutated != value:
        return mutated
    # A literal with no run of two letters -- `"i"`, `"#"`, `"/"`. If it has any alphanumeric at all
    # it is still a behavioural constant worth mutating; `p + "i"` is the whole sibling `.fsi` test.
    if value and any(c.isalnum() for c in value):
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


def enumerate_mutants(gate_source: str, projects: list[str]) -> tuple[list[Mutant], list[str]]:
    """(mutants, skipped) enumerated from the gate's AST and from the discovered project list."""
    tree = ast.parse(gate_source)
    docs = docstring_spans(tree)
    mutants: list[Mutant] = []
    skipped: list[str] = []
    seen: set[str] = set()

    def add(operator: str, node: ast.AST, new: str, note: str = "", tag: str = "") -> None:
        where = f"L{node.lineno}"
        # `tag` keeps several DISTINCT mutations at one source position from colliding on one id and
        # silently deduping down to the first — `report-swap` generates one per candidate name at the
        # same site, and without this only one of them would ever run.
        mid = f"{operator}@{node.lineno}:{node.col_offset}" + (f"={tag}" if tag else "")
        if mid in seen:
            return
        seen.add(mid)
        mutated = splice(
            gate_source, node.lineno, node.col_offset, node.end_lineno, node.end_col_offset, new
        )
        if mutated == gate_source:
            skipped.append(f"{mid}: replacement is a no-op")
            return
        try:
            compile(mutated, "<mutant>", "exec")
        except SyntaxError as exc:
            skipped.append(f"{mid}: mutant does not compile ({exc.msg})")
            return
        mutants.append(Mutant(mid, operator, where + (f" {note}" if note else ""), mutated))

    for node in ast.walk(tree):
        # CONDITIONS.
        if isinstance(node, (ast.If, ast.While)):
            add("cond-false", node.test, "False")
            add("cond-true", node.test, "True")
        # COMPARISONS.
        if isinstance(node, ast.Compare) and len(node.ops) == 1:
            flip = CMP_FLIP.get(type(node.ops[0]))
            if flip is not None:
                left = _span(gate_source, node.left)
                right = _span(gate_source, node.comparators[0])
                add("cmp-flip", node, f"{left} {flip} {right}")
        # CONSTANTS: numbers, then non-docstring strings.
        if isinstance(node, ast.Constant):
            if (node.lineno, node.col_offset) in docs:
                continue
            if isinstance(node.value, bool):
                add("bool-flip", node, "False" if node.value else "True")
            elif isinstance(node.value, int):
                add("num", node, str(node.value + 1))
            elif isinstance(node.value, str):
                new_value = sentinel(node.value)
                if new_value is None:
                    continue
                raw = _span(gate_source, node)
                if raw[:1] in ("'", '"') or raw[:2].lower() in ("r'", 'r"', "f'", 'f"', "b'", 'b"'):
                    add("str", node, repr(new_value))
                elif "{" not in raw and "}" not in raw and "\\" not in raw:
                    # A literal segment inside an f-string: its span is the raw text, unquoted.
                    add("str", node, new_value)
                else:
                    skipped.append(f"str@{node.lineno}:{node.col_offset}: unrewritable literal span")
        # CONNECTIVES. `cond-false`/`cond-true` replace a whole test and `cmp-flip` rewrites one
        # comparison, so neither can reach the `and`/`or` BETWEEN two comparisons -- and a guard
        # written `A and B` whose fixture only ever exercises inputs where A and B agree is a guard
        # with an unasserted half. `//`-vs-`////` is exactly that shape (`i + 3 < n and line[i + 3]`).
        if isinstance(node, ast.BoolOp) and len(node.values) == 2:
            parts = [_span(gate_source, value) for value in node.values]
            flip = "or" if isinstance(node.op, ast.And) else "and"
            add("boolop-flip", node, f"{parts[0]} {flip} {parts[1]}")
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
            skipped.append(f"dir-drop/{project}: discover() anchor not found")
            break
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
    if skipped:
        print(f"sites SKIPPED (reported, never silent): {len(skipped)}")
        for entry in skipped:
            print(f"  {entry}")

    if args.list:
        for mutant in mutants:
            print(f"  {mutant.mid}  {mutant.where}")
        return 0

    # A SWEEP THAT ENUMERATED NOTHING WOULD REPORT `0 mutants, 0 killed, 0 survived` AND EXIT 0.
    # That is this row's own subject turned on its own instrument -- "0 survivors" and "0 mutants"
    # are the same bytes -- and it is reachable for real: an `ast` change, a parse that silently
    # yields an empty tree, or a refactor that renames the anchors below would each produce it. So
    # the floor is checked, and it is checked against a SECOND, textual reading of the same file
    # rather than against a number typed here.
    if not projects:
        print("REFUSING: no project under src/ holds a subject, so dir-drop asserts nothing.")
        return 3

    textual_conditions = sum(
        1
        for raw in gate_source.split("\n")
        if re.match(r"\s*(if|elif|while)\s", raw) and not raw.lstrip().startswith("#")
    )
    required = {
        "cond-false": textual_conditions,
        "cond-true": textual_conditions,
        "num": 1,
        "str": 1,
        "len-pin": 1,
        "cmp-flip": 1,
        "dir-drop": len(projects),
    }
    shortfall = [
        f"{operator}: enumerated {by_operator.get(operator, 0)}, expected at least {floor}"
        for operator, floor in sorted(required.items())
        if by_operator.get(operator, 0) < floor
    ]
    if shortfall:
        print("REFUSING: the enumeration is smaller than a second reading of the same gate says it")
        print("should be, so a green sweep below would mean 'nothing was measured', not 'nothing")
        print("survived'. The AST walk and the textual count disagree:")
        for entry in shortfall:
            print(f"  {entry}")
        return 3

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
    print()
    print(
        f"signature-doc-siting mutation sweep: {len(mutants)} mutants, {killed} killed, "
        f"{len(survivors)} survived ({len(survivors) - len(unjustified)} justified), "
        f"{len(unjustified)} UNJUSTIFIED"
    )
    stale = sorted(set(allowed) - {m.mid for m in survivors})
    if stale:
        print(f"stale entries in {os.path.relpath(ALLOWED, REPO)} -- these mutants no longer survive, delete them:")
        for mid in stale:
            print(f"  {mid}")
    if unjustified:
        print()
        print("Each UNJUSTIFIED mutant is a dimension of the gate the fixture does not assert.")
        print("Add a fixture leg that reds on it, or justify it in mutants-allowed.txt.")
        return 1
    if stale:
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
