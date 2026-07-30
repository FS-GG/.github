#!/usr/bin/env python3
"""Assert a workflow's `paths:` filters AGREE with each other, and COVER what they build.

.github#880 and #930, epic #266 (coherence gates that fail open). Found by worker `finch-2e3f` while
working #860 (the suite selector), which derives its answer from these very filters.

THREE RULES, AND THEY ARE ONE GATE ON PURPOSE
  (a) AGREEMENT — for every workflow declaring BOTH `pull_request.paths` and `push.paths`, the two
      lists are identical as sets.
  (b) COVERAGE / PROJECTS — a workflow whose filter names a .NET project's own directory must also
      select every project in that project's `ProjectReference` closure.
  (c) COVERAGE / GATE SCRIPTS — a workflow whose filter names a gate script must select everything
      that script declares it READS, in its `PATHS_SUBJECT`.

  #880 shipped (a) and deliberately left the rest. #930 added (b), #996 added (c). They land as one
  gate because of how (b) and (c) are missed: a worker fixing (a) reads the filter, sees two matching
  copies, and moves on — which is exactly what happened three hours after #880 closed (#930). Set
  equality is satisfied when both copies are wrong THE SAME WAY, and the gate stays green.

  (b) and (c) are the same rule over two graphs. In both, the workflow's OWN `paths:` list says what
  the workflow is about — that list is YAML, and structural — and something else supplies the
  closure: the project files for (b), the script's AST for (c). Neither ever reads `run:` text.

Nearly every workflow here duplicates its `paths:` list verbatim between the two triggers. That
duplication is invisible: nothing reads both copies, so when one is edited and the other is not, the
result is a gate that still passes its own tests and simply stops running on `main`. Green, and
wrong — the #266 signature, arrived at by a typo rather than a bug.

Two had already drifted when this gate was written:

  - `adr-coherence.yml`      — the push copy omitted `tests/adr-coherence/**` and its own workflow
                               file, so editing that fixture ON MAIN did not re-run the gate the
                               fixture belongs to.
  - `skill-registry-coherence.yml` — the push copy omitted `tests/skill-registry/**`.

WHY A GATE, AND NOT A FIFTH HAND-REPAIR
  This is the fifth instance of one class:
    #332 — repos-audit-selftest.yml did not trigger on repos-audit.yml.
    #334 — the kit digest gate's filter covered 1 of 3 kit sources. Its fix sketch asked, in as many
           words, for an assertion that each source is matched "on BOTH pull_request and push".
    #508 — sync-build-config-selftest.yml's filter omitted `dist/dotnet/**`, its own input. It
           closes by wondering aloud whether the other selftests have the same mismatch.
    these two.
  Nobody was careless. There is no gate on the gates' own triggers, so the class regenerates faster
  than it is repaired and each repair is one workflow wide. pnext-item §4: if a fix keeps
  regenerating the same finding, the finding is not the bug — the thing that regenerates it is.

WHAT THIS GATE DELIBERATELY DOES NOT DO
  This paragraph used to read "Do not grow it", and #930 is the decision to grow it by exactly one
  rule. The old text was right about the general case and wrong about the whole of it:

  > The harder half — DOES A FILTER COVER THE WORKFLOW'S OWN SUBJECT? — is #334/#508's shape and is
  > NOT mechanically decidable: it needs a human to say what a given gate's inputs are.

  "What is this workflow's subject?" is still not mechanically decidable IN GENERAL, and this gate
  still does not ask it. But it is not undecidable UNIFORMLY, and #930 named the seam: a .NET
  suite's subject includes its `ProjectReference` closure, and that closure is a FACT IN THE PROJECT
  FILES. No judgement in it, nothing for a human to say. So (b) below asks the one question it can
  answer from the graph, and asks nothing else.

  A GATE SCRIPT'S SURFACE WAS THE OTHER HALF, AND #996 DECIDED IT — see RULE (c) below. #930 left it
  because it needs a CONVENTION rather than a derivation, and a convention is a decision: keying off
  one script's private constant name would have been a hardcode wearing a rule's clothes. The
  convention is `PATHS_SUBJECT`, and what makes it trustworthy is that it is COMPOSED from the
  constants the script already walks with, not retyped beside them.

  STILL OUT OF SCOPE, for a reason rather than an omission:

    - A `*-selftest`'S FIXTURE AND TOOL (#332, #334, #508). Now expressible — a selftest that
      declares `PATHS_SUBJECT` gets (c) for free — but none of the three is MIGRATED, so the class
      is covered by a convention nobody has applied to it yet rather than by a rule. Migrating them
      is ordinary work, not a decision.

  WHAT IS PERMANENTLY OUT OF SCOPE, and this one is a finding rather than a gap: DERIVING THE
  SUBJECT BY READING `run:` TEXT. It looks like the obvious general answer and it is unsound. A
  scan of every `run:` block for repo paths, measured against this repo, returns THREE hits and all
  three are false: `scripts/fsgg-coord` named inside a YAML or shell COMMENT in recipe-landable.yml,
  recipe-pagination.yml and worker-id-attractor.yml. That is #683 exactly — a parser cannot tell a
  MENTION from a USE — and it is the same trap the escape hatch below took two goes to escape. A
  gate that cries wolf on a comment teaches workers to scroll past it (#698). So (b) reads the
  PROJECT GRAPH and the workflow's own `paths:` list, both structural, and never free text.

SHAPES THIS GATE REFUSES RATHER THAN SKIPS
  Set equality is only a sound reading of `paths:` while the lists are plain allow-lists. Two shapes
  would break that, and both are exit 3 (no verdict) rather than a silent skip — a skip is how a
  coherence gate fails open, which is the whole of #266:

    - `paths-ignore:` INVERTS selection. A `pull_request.paths` and a `push.paths-ignore` are
      genuinely different filters, but they are not two lists this gate can compare. scripts/test
      refuses it for the same reason.
    - a NEGATED (`!`) pattern makes ORDER significant — GitHub lets a later pattern override an
      earlier one — so `[a, !b]` and `[!b, a]` are equal as sets and different as filters. Set
      equality would call them coherent, which is exactly the confident-wrong-answer this gate is
      supposed to end.

  Neither shape is live in this repo today. They are refused anyway, because the gate must not
  quietly start being unsound the day one appears.

RULE (b): WHAT COUNTS AS DECLARING A PROJECT, AND WHY IT IS NARROW
  The obligation attaches to a project only when a pattern's LITERAL PREFIX is that project's own
  directory — `src/FS.GG.Coord.Cli/**` declares it; `src/**` and `tests/**` do not, and neither does
  `src/FS.GG.Coord.Cli/Options.fs`.

  That narrowness is the whole soundness of the rule, and each exclusion is a false positive it was
  measured to produce without it:

    - A CATCH-ALL is not a declaration. `test-selector-selftest.yml` filters on `tests/**`, which
      incidentally selects three .Tests projects it never builds — it runs a shell script that greps.
      Requiring their closure would drag `src/**` onto a workflow whose subject it is not, and the
      first thing a wrongly-red gate teaches is that this gate is noise (#698).
    - NAMING ONE FILE is not declaring the project. `recipe-landable.yml` watches
      `src/FS.GG.Coord.Cli/Options.fs` because it GREPS that file. Watching a file you read is not
      building the project it lives in.
    - A pattern that already covers the whole tree needs no obligation: `src/**` selects every
      project's closure by construction, so there is nothing that could be missing.

  Read positively: naming a project's directory is a workflow saying "this project's SOURCE is my
  input". Then a change to what that project is BUILT FROM is a change to its input, and the graph
  says exactly what that is.

RULE (c): A GATE SCRIPT DECLARES WHAT IT READS, AND ITS WORKFLOW MUST SELECT IT (#996)
  A gate script MAY declare a module-level `PATHS_SUBJECT`. If it does, any workflow whose `paths:`
  NAMES that script (an exact literal path, per (b)'s narrowness) must select every entry.

      PATHS_SUBJECT = DOC_SURFACE + CODE_SURFACE + (ROOTS_DECL,) + FALLBACK_ROOTS

  WHY A DECLARATION AND NOT A DERIVATION. A script's subject is not written in a schema anywhere —
  unlike a `ProjectReference`, there is nothing to read. The only other place it exists is the
  `run:` line that invokes it, and reading `run:` text cannot tell a mention from a use (#683).
  So it has to be declared, and declaring is a convention: #996 is the decision to have one.

  WHY IT IS COMPOSED, NEVER RETYPED — this is the whole of why the declaration can be believed. A
  retyped list is a SECOND copy of the surface, free to drift from the constants beside it the day
  one changes, which is #865: a hand-maintained source moves the drift upstream and makes it
  authoritative. Built from the constants the script WALKS with, widening a surface widens the
  trigger and nobody has to remember. That is the same property that makes (b) derivable — not
  "somebody wrote it down accurately", but "the thing that does the work is the thing being read".
  It is why `declared_subject()` folds names and `+` instead of taking `ast.literal_eval`'s easy
  road, which would have forced the retyped copy and quietly reintroduced the disease.

  WHAT IT IS NOT. It is opt-in: a script declaring nothing is out of scope and silent, so the rule
  cannot blanket-red a workflow nobody has migrated (#698). It is never IMPORTED or EXECUTED — this
  gate runs on PRs that are editing these very scripts, and a gate that runs its subject is not a
  gate. And an expression it cannot fold statically is a REFUSAL, never a guess: a subject read
  wrong is worse than a subject not read (#266).

THE ESCAPE HATCH
  A gate with no way to say "yes, on purpose" becomes a straitjacket that gets disabled. Two hatches,
  one per rule, because they license different things and a marker must not silently do more than it
  says. Both go anywhere in the workflow file, as a standalone YAML comment:

      # paths-coherence: allow-divergence — <reason>            (rule a)
      # paths-coherence: allow-uncovered <path> — <reason>       (rule b)

  The reason is REQUIRED and must be non-empty: the point is not to permit divergence but to make it
  a decision somebody made and signed, rather than a typo nobody saw. A bare marker with no reason
  is itself a finding.

  `allow-uncovered` NAMES THE PATH it excuses, and that is not decoration (#496). A blanket "this
  workflow is exempt from (b)" would render identically to a workflow nobody had thought about — the
  absence must be deliberate AND readable, or it is `Paths:` with no declaration all over again. So
  the hatch excuses `src/FS.GG.Coord.GitHub` and leaves every other uncovered dependency a finding,
  including one added later.

EXIT CODES — THE CONTRACT
  0  every workflow declaring both filters declares them identically, and every filter that names a
     project covers that project's `ProjectReference` closure.
  1  FINDING: a workflow's two copies have drifted (a), or a filter omits a project it builds (b).
  3  NO VERDICT (permanent): a workflow would not parse, a shape cannot be compared, a project file
     would not parse, or NOTHING was audited. Examining nothing is a failure to audit, not a clean
     audit (#266).

  There is deliberately no exit 2 ("no verdict, retryable"): this gate is pure and offline. It reads
  files, makes no network call, and has no condition a re-run could resolve.

usage:
  check-paths-coherence.py [--root <dir>]
"""

from __future__ import annotations

import argparse
import ast
import glob
import os
import re
import sys
import traceback

import yaml

OK, FINDING, NO_VERDICT_PERMANENT = 0, 1, 3

# The escape hatch, and it must match a USE of the marker rather than a MENTION of it.
#
# This took two goes, and the second one is the point. .github#683's lesson is that a parser cannot
# tell a mention from a use unless you MAKE it — and "make it" here means asking the YAML parser,
# not writing a cleverer regex.
#
#   draft 1  matched the text anywhere in the file. The first thing it licensed was
#            paths-coherence.yml itself, whose FINDING step prints the marker to document it: the
#            gate read its own documentation as a signed divergence.
#   draft 2  anchored to `^[ \t]*#`, which does exclude an `echo '# paths-coherence: …'` line — and
#            still licensed real drift from a SHELL comment inside a `run: |` block, because a shell
#            comment is `^[ \t]*#` too. Same fail-open, one layer down.
#
# A regex cannot see the difference: `# x` is a YAML comment at one indent and opaque block-scalar
# TEXT at another. So the line filter below asks PyYAML which lines are inside a block scalar, and
# the marker is only honoured on a line that is not. The anchor stays as the second half of the
# test — together they mean "a standalone YAML comment", which is what the hatch was always
# documented to be.
#
# `[ \t]*`, never `\s*`: `\s` matches a newline, so `\s*` would let the `#` sit on one line and the
# marker on the next, defeating the anchor it is standing next to.
#
# The separator before the reason is OPTIONAL and the reason is not: `— why`, `: why`, and a bare
# ` why` all sign the marker, while none of them is required for the marker to be RECOGNISED. That
# asymmetry is deliberate. If the separator were mandatory, a marker written with a reason but no
# dash would not match at all — so instead of "you forgot to sign this", the author would get an
# unrelated drift finding about their paths, which is a worse answer to a smaller mistake.
ALLOW_MARKER = re.compile(
    r"^[ \t]*#[ \t]*paths-coherence:[ \t]*allow-divergence[ \t]*[—:-]?[ \t]*(?P<reason>.*)$",
    re.MULTILINE,
)

# Rule (b)'s hatch. It NAMES the path it excuses — see THE ESCAPE HATCH. Same standalone-YAML-comment
# discipline as ALLOW_MARKER, and for the same reason: a `#` inside a `run: |` block is shell text.
ALLOW_UNCOVERED = re.compile(
    r"^[ \t]*#[ \t]*paths-coherence:[ \t]*allow-uncovered[ \t]+(?P<path>[^\s—:]+)"
    r"[ \t]*[—:-]?[ \t]*(?P<reason>.*)$",
    re.MULTILINE,
)

# Returned by allow_divergence() when a marker is present but none of them is signed.
UNSIGNED = ""

# The project files whose reference graph rule (b) reads. MSBuild's own languages; there is no
# judgement in the list, only which extensions carry a `ProjectReference`.
PROJECT_GLOBS = ("*.fsproj", "*.csproj", "*.vbproj")


class GateError(Exception):
    """A condition under which the gate must fail rather than skip. Maps to exit 3."""


def load_yaml(text: str, what: str) -> dict:
    try:
        doc = yaml.safe_load(text)
    except yaml.YAMLError as e:
        raise GateError(f"{what}: not parsable as YAML — {e}") from e
    if not isinstance(doc, dict):
        raise GateError(f"{what}: not a YAML mapping")
    return doc


def triggers(doc: dict, what: str) -> dict:
    """The `on:` block, with all three legal spellings normalised.

    PyYAML resolves the bare key `on` to the boolean True (YAML 1.1), so a plain doc["on"] misses it
    entirely. Same trap, same handling, as scripts/check-workflow-timeouts.py and
    scripts/check-workflow-permissions.py.

    `on: pull_request` and `on: [push, pull_request]` are as legal as the mapping form. Reading only
    the mapping form does not refuse them — it silently decides the workflow triggers on nothing,
    which here would mean skipping it. scripts/test made exactly that mistake (#879); do not repeat
    it. Anything that is none of the three spellings is refused, not guessed.
    """
    for key in ("on", True):
        if key in doc:
            got = doc[key]
            if isinstance(got, dict):
                return got
            if isinstance(got, list):
                return {str(k): None for k in got}
            if isinstance(got, str):
                return {got: None}
            raise GateError(
                f"{what}: `on:` is {type(got).__name__}, not a string, list, or mapping — this gate "
                f"cannot tell what triggers the workflow, and guessing would silently skip it (#266)."
            )
    return {}


def declared(on: dict, trigger: str) -> tuple[object, bool]:
    """`(<trigger>.paths as declared or None, whether it declares paths-ignore)`.

    `pull_request:` with a NULL value means EVERY PR, not "no PR trigger" (`coherence.yml` is in
    that state). Either way it declares no `paths:`, so it is not half of a pair — a workflow with no
    `paths:` on either trigger is not drift and must not be flagged.

    THIS READS AND DOES NOT JUDGE, and that is the entire fix for a real fail-closed bug.

    The first draft raised on `paths-ignore:` the moment it saw one, before it had established that
    the workflow was even in scope. So a workflow declaring ONLY `paths-ignore:` — no `paths:` at
    all, not half of a pair, plainly outside the rule — refused. And because a refusal aborts the
    run, ONE such file took the ENTIRE audit to exit 3 and every real drift in the repo went
    unreported. `paths-ignore:` is an ordinary Actions feature; the first person to add one would
    have blocked CI with a diagnostic naming three causes, none of them theirs.

    A gate may only refuse what it was actually asked to judge. Reading is not judging, so the read
    happens here and every refusal happens in main(), after scope is established.
    """
    t = on.get(trigger)
    if not isinstance(t, dict):
        return None, False
    return (t.get("paths") if "paths" in t else None), ("paths-ignore" in t)


def validated(raw: object, trigger: str, what: str) -> list[str]:
    """`<trigger>.paths` as a list of patterns this gate can soundly compare.

    Only ever called on a workflow that IS a pair. A one-sided workflow's patterns are never
    compared, so refusing them would be a false alarm about a file outside the rule.
    """
    if not isinstance(raw, list) or not raw:
        raise GateError(
            f"{what}: `{trigger}.paths:` is present but is not a non-empty list ({raw!r})."
        )

    pats = [str(p) for p in raw]
    for p in pats:
        if p.startswith("!"):
            raise GateError(
                f"{what}: `{trigger}.paths:` carries the negated pattern {p!r}. Negation makes ORDER "
                f"significant — a later pattern overrides an earlier one — so two lists can be equal "
                f"as SETS and different as FILTERS. This gate's equality test would call that "
                f"coherent, which is the confident-wrong-answer it exists to prevent (#266)."
            )
    return pats


def block_scalar_lines(text: str) -> set[int]:
    """The 0-based lines covered by a block scalar (`|` / `>`) value.

    A `#` inside a `run: |` block is shell TEXT, not a YAML comment, and nothing about the character
    says which. This is the only reliable way to tell: ask the parser where the opaque regions are.
    Without it the hatch reads a shell comment — or a heredoc line — as a signed divergence and
    licenses real drift (exit 0 on a broken workflow), which is the fail-open this gate exists to end.
    """
    try:
        node = yaml.compose(text)
    except yaml.YAMLError:
        # load_yaml() has already refused this file with a no-verdict; nothing to exclude.
        return set()

    covered: set[int] = set()

    def walk(n: object) -> None:
        if isinstance(n, yaml.ScalarNode):
            if n.style in ("|", ">"):
                covered.update(range(n.start_mark.line, n.end_mark.line + 1))
        elif isinstance(n, yaml.SequenceNode):
            for child in n.value:
                walk(child)
        elif isinstance(n, yaml.MappingNode):
            for key, value in n.value:
                walk(key)
                walk(value)

    walk(node)
    return covered


def allow_divergence(text: str, what: str) -> str | None:
    """The signed reason this workflow may diverge.

    None  — no marker at all.
    ""    — a marker is present but NONE of them is signed (UNSIGNED). The caller makes that a
            FINDING: the hatch exists to turn a typo nobody saw into a decision somebody made, and
            an unsigned marker does neither.
    str   — the reason.

    Every marker is considered, not just the first: a header comment may legitimately DOCUMENT the
    bare form above the file's real, signed marker, and `search()` would have read that first line
    and called the file unsigned — a confidently wrong verdict against a file that did exactly what
    the gate asked.
    """
    opaque = block_scalar_lines(text)
    markers = [
        m for m in ALLOW_MARKER.finditer(text)
        if text.count("\n", 0, m.start()) not in opaque
    ]
    if not markers:
        return None
    for m in markers:
        reason = (m.group("reason") or "").strip()
        if reason:
            return reason
    return UNSIGNED


def glob_to_regex(pattern: str) -> re.Pattern[str]:
    """A GitHub `paths:` pattern as a regex over repo-relative paths.

    Actions' globbing, not fnmatch's: `**` crosses `/`, `*` and `?` do not. fnmatch would translate
    `*` to `.*` and quietly decide `src/*` matches `src/a/b.fs` — a filter that selects more than it
    does, which for rule (b) means silently reporting a dependency as covered when a push to it would
    not trigger the workflow. Wrong in the fail-OPEN direction, so it is spelled out here.
    """
    i, out = 0, ["^"]
    while i < len(pattern):
        if pattern.startswith("**/", i):
            # `a/**/b` must also match `a/b` — the doubled star may stand for no directory at all.
            out.append("(?:.*/)?")
            i += 3
        elif pattern.startswith("**", i):
            out.append(".*")
            i += 2
        elif pattern[i] == "*":
            out.append("[^/]*")
            i += 1
        elif pattern[i] == "?":
            out.append("[^/]")
            i += 1
        else:
            out.append(re.escape(pattern[i]))
            i += 1
    out.append("$")
    return re.compile("".join(out))


def selects(path: str, patterns: list[str]) -> bool:
    """Would a push touching `path` trigger a workflow filtered on `patterns`?"""
    return any(glob_to_regex(p).match(path) for p in patterns)


def literal_prefix(pattern: str) -> str:
    """The pattern up to its first wildcard — the part that names a place rather than a shape.

    `src/FS.GG.Coord.Cli/**` -> `src/FS.GG.Coord.Cli`.  `src/**` -> `src`.
    """
    cut = min((i for i in (pattern.find("*"), pattern.find("?")) if i != -1), default=len(pattern))
    return pattern[:cut].rstrip("/")


def project_graph(root: str) -> dict[str, list[str]]:
    """Every MSBuild project in the tree, and the projects it references. Repo-relative paths.

    Structural: a `ProjectReference`'s `Include=` is a path, in a schema, in a file — there is no
    prose to misread here, which is precisely why rule (b) is derivable and reading `run:` is not.
    """
    graph: dict[str, list[str]] = {}
    for pattern in PROJECT_GLOBS:
        for path in glob.glob(os.path.join(root, "**", pattern), recursive=True):
            rel = os.path.relpath(path, root).replace(os.sep, "/")
            try:
                with open(path, encoding="utf-8") as fh:
                    text = fh.read()
            except OSError as e:
                raise GateError(f"{rel}: unreadable — {e}") from e
            refs = []
            for m in re.finditer(r'ProjectReference\s+[^>]*Include\s*=\s*"([^"]+)"', text):
                # MSBuild writes Windows separators; they are legal on every platform.
                inc = m.group(1).replace("\\", "/")
                target = os.path.normpath(os.path.join(os.path.dirname(path), inc))
                refs.append(os.path.relpath(target, root).replace(os.sep, "/"))
            graph[rel] = refs
    return graph


def closure(graph: dict[str, list[str]], project: str) -> set[str]:
    """Every project `project` transitively references. Never includes `project` itself.

    Cycle-safe: MSBuild forbids reference cycles, but a gate that hangs on a malformed tree is a gate
    that stops the repo — and `seen` costs nothing.
    """
    found: set[str] = set()
    stack = list(graph.get(project, []))
    while stack:
        p = stack.pop()
        if p in found:
            continue
        found.add(p)
        stack.extend(graph.get(p, []))
    return found


def declared_subject(path: str, where: str) -> tuple[str, ...] | None:
    """`PATHS_SUBJECT` from a gate script, read BY AST. None if it declares none.

    NEVER IMPORTS AND NEVER EXECUTES, and that is not squeamishness — this gate runs on every PR,
    against scripts the PR is editing. Importing them would run whatever the PR wrote, at gate time,
    and a gate that executes its subject is not a gate. `ast.parse` reads the text; nothing in the
    file gets a chance to act.

    IT RESOLVES NAMES AND `+`, WHICH `ast.literal_eval` ALONE WILL NOT, and that is the whole design.
    The declaration is worth trusting only because it is COMPOSED from the constants the script walks
    with — `DOC_SURFACE + CODE_SURFACE + (ROOTS_DECL,) + FALLBACK_ROOTS` — so widening a surface
    widens the trigger and nobody has to remember. A literal-only reader would force a retyped copy,
    free to drift from the constants beside it, which is #865: the cure reintroducing the disease.
    So this resolves module-level names to their literals and folds `+` over sequences. Nothing else:
    no calls, no attributes, no comprehensions. An expression it cannot fold is a REFUSAL, never a
    guess — a subject read wrong is worse than one not read (#266).
    """
    try:
        with open(path, encoding="utf-8") as fh:
            tree = ast.parse(fh.read(), filename=path)
    except (OSError, SyntaxError) as e:
        raise GateError(f"{where}: names {path!r}, which will not parse as Python — {e}") from e

    def binds(node: ast.AST) -> bool:
        return isinstance(node, ast.Assign) and any(
            isinstance(t, ast.Name) and t.id == "PATHS_SUBJECT" for t in node.targets
        )

    # EVERY binding of the name, ANYWHERE in the file — `ast.walk`, not `tree.body`. Both of the
    # shapes this refuses were measured against the first draft, which scanned only module level and
    # took the first hit:
    #
    #   PATHS_SUBJECT = A ; PATHS_SUBJECT = B   -> the gate read A. Python uses B. The gate would
    #                                              have checked a surface the script does not walk,
    #                                              confidently, which is the one thing this reader
    #                                              may not do.
    #   if …: PATHS_SUBJECT = A else: … = B     -> invisible to a body-only scan, so it read as
    #                                              "declares nothing" and rule (c) SILENTLY stopped
    #                                              applying — a skip, and a skip is how a coherence
    #                                              gate fails open (#266).
    #
    # One binding, at module level, or no verdict. There is no third answer worth having.
    decls = [n for n in ast.walk(tree) if binds(n)]
    if not decls:
        return None
    top = [n for n in tree.body if binds(n)]
    if len(decls) != 1 or len(top) != 1:
        raise GateError(
            f"{path}: PATHS_SUBJECT is bound {len(decls)} time(s), {len(top)} of them at module "
            f"level. This gate reads the declaration statically, so it must be assigned exactly "
            f"once, at module level — anything else and the value it reads is not the value the "
            f"script uses (#266)."
        )
    target: ast.expr = top[0].value

    # Module-level literal bindings ABOVE the declaration, in source order: a name is only usable
    # once bound, so a PATHS_SUBJECT referring to a constant defined below it is unresolvable and
    # refused rather than silently read as empty.
    env: dict[str, object] = {}
    for node in tree.body:
        if node is top[0]:
            break
        if not isinstance(node, ast.Assign) or len(node.targets) != 1:
            continue
        name = node.targets[0]
        if not isinstance(name, ast.Name):
            continue
        try:
            # Broad on purpose: literal_eval raises ValueError for a non-literal, and TypeError or
            # SyntaxError for shapes it cannot even attempt. None of them is interesting — this loop
            # is asking "is this a literal?", and every no is the same no. Letting one escape would
            # surface as `the gate crashed` against a script whose only sin is an ordinary
            # `X = re.compile(…)`.
            env[name.id] = ast.literal_eval(node.value)
        except (ValueError, TypeError, SyntaxError):
            continue

    def fold(node: ast.expr) -> object:
        if isinstance(node, ast.Name):
            if node.id not in env:
                raise GateError(
                    f"{path}: PATHS_SUBJECT refers to {node.id!r}, which is not a module-level "
                    f"literal defined above it. This gate reads the declaration statically and will "
                    f"not guess at one it cannot fold (#266)."
                )
            return env[node.id]
        if isinstance(node, ast.BinOp) and isinstance(node.op, ast.Add):
            left, right = fold(node.left), fold(node.right)
            if not isinstance(left, (list, tuple)) or not isinstance(right, (list, tuple)):
                raise GateError(f"{path}: PATHS_SUBJECT adds something that is not a sequence.")
            return tuple(left) + tuple(right)
        # A sequence DISPLAY whose elements may themselves be names — `(ROOTS_DECL,)`, the idiom for
        # lifting one constant into the sum. literal_eval refuses the whole node the moment one
        # element is a Name, so the elements are folded one at a time rather than the tuple as a
        # unit; without this the composed form is unwritable and the only shape left is a retyped
        # copy, which is the drift this whole convention exists to avoid.
        if isinstance(node, (ast.Tuple, ast.List)):
            return tuple(fold(e) for e in node.elts)
        try:
            return ast.literal_eval(node)
        except ValueError as e:
            raise GateError(
                f"{path}: PATHS_SUBJECT is not a literal, a module-level constant, or a `+` of "
                f"those — {e}. Keep it foldable: this gate must read it without running the file."
            ) from e

    value = fold(target)
    if not isinstance(value, (list, tuple)) or not value:
        raise GateError(f"{path}: PATHS_SUBJECT is not a non-empty sequence ({value!r}).")
    for entry in value:
        if not isinstance(entry, str) or not entry:
            raise GateError(f"{path}: PATHS_SUBJECT contains {entry!r}, which is not a path.")
    return tuple(str(v) for v in value)


def named_scripts(patterns: list[str], root: str) -> list[str]:
    """The gate scripts this filter names EXACTLY — the linkage, and it is structural.

    "Which script does this workflow run?" is a question about `run:` text, and reading `run:` text
    cannot tell a mention from a use (#683 — measured: three false hits in this repo, every one a
    `scripts/fsgg-coord` inside a comment). So it is not asked. The workflow's OWN `paths:` list is
    YAML, and a filter naming `scripts/check-X.py` is the workflow saying that script is its input.
    That is the same move rule (b) makes with the project graph: the filter's list IS the
    declaration, and the AST supplies the closure.

    EXACT literal paths only, for #930's reason: `scripts/**` is a catch-all, not a workflow naming
    a script, and reading it as one would impose every script's subject on any workflow watching the
    directory.
    """
    out = []
    for p in patterns:
        # The wildcard test is REDUNDANT with the isfile() below — no file is named `*.py`, so a
        # glob fails that check anyway, and a mutation removing this line changes no behaviour. It
        # stays because the two say different things: this states the RULE (only an exact name is a
        # declaration), isfile() asks a question about the disk. Collapsing them would leave the
        # rule resting on a filesystem accident.
        if "*" in p or "?" in p:
            continue
        path = os.path.join(root, p)
        if not os.path.isfile(path):
            continue

        # `.py` is an explicit Python declaration. The repo also has CLI-shaped Python gates with
        # no extension; for those, the executable declaration is the shebang, just as
        # `scripts/lint-shell.sh` classifies extensionless shell. Requiring BOTH no extension and a
        # Python shebang keeps a shebang-less text file that happens to parse as Python out of the
        # linkage — ast.parse() says only that bytes are syntactically valid, not that the file is a
        # gate script (#1639).
        if p.endswith(".py"):
            out.append(p)
            continue
        if os.path.splitext(os.path.basename(p))[1]:
            continue
        with open(path, "rb") as f:
            first = f.readline(256).decode("ascii", errors="ignore")
        if re.match(r"^#!\s*(?:\S*/)?(?:env\s+)?python(?:[0-9.]+)?(?:\s|$)", first):
            out.append(p)
    return sorted(set(out))


def uncovered(patterns: list[str], graph: dict[str, list[str]]) -> list[tuple[str, str]]:
    """`(subject, dependency)` for each project this filter NAMES whose closure it does not select.

    "Names" is deliberately narrow — a pattern's literal prefix must BE the project's directory. See
    RULE (b) in the module docstring for the three false positives that narrowness is measured to
    prevent; the short of it is that a catch-all (`tests/**`) and a single file
    (`src/.../Options.fs`) are not a workflow declaring a build subject, and treating them as one
    reds workflows whose subject it is not.
    """
    out: list[tuple[str, str]] = []
    for project in subjects(patterns, graph):
        for dep in sorted(closure(graph, project)):
            if not selects(dep, patterns):
                out.append((project, dep))
    return out


def subjects(patterns: list[str], graph: dict[str, list[str]]) -> list[str]:
    """The projects this filter DECLARES as its build subject — see uncovered()."""
    prefixes = {literal_prefix(p) for p in patterns}
    return [p for p in sorted(graph) if os.path.dirname(p) in prefixes]


def allow_uncovered(text: str) -> dict[str, str]:
    """`{path: reason}` for each signed `allow-uncovered` marker. An unsigned one maps to UNSIGNED.

    Same block-scalar exclusion as allow_divergence(): a marker inside a `run: |` is shell text, and
    honouring it would license a real omission from a line that is not a YAML comment at all.
    """
    opaque = block_scalar_lines(text)
    out: dict[str, str] = {}
    for m in ALLOW_UNCOVERED.finditer(text):
        if text.count("\n", 0, m.start()) in opaque:
            continue
        out[m.group("path").strip().rstrip("/")] = (m.group("reason") or "").strip()
    return out


def workflow_files(root: str) -> list[str]:
    d = os.path.join(root, ".github", "workflows")
    if not os.path.isdir(d):
        raise GateError(f"{d} is not a directory — there are no workflows to audit")
    files = sorted(f for ext in ("yml", "yaml") for f in glob.glob(os.path.join(d, f"*.{ext}")))
    if not files:
        raise GateError(f"{d} contains no workflow files — there is nothing to audit")
    return files


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="the working tree to audit (default: .)")
    args = ap.parse_args(argv)

    findings: list[str] = []
    pairs_seen = 0
    allowed = 0

    # A SET OF (workflow, project), not a running count. Both triggers declare the same list on a
    # paired workflow, so incrementing per trigger reported 26 subjects where the repo has 13 — a
    # confidently wrong number, in the one line a human reads to decide whether the gate looked at
    # anything. The fixture asserts on this number too, so an inflated one is a check agreeing with
    # its own arithmetic rather than with the repo.
    subjects_seen: set[tuple[str, str]] = set()
    scripts_seen: set[tuple[str, str]] = set()

    graph = project_graph(args.root)

    for path in workflow_files(args.root):
        where = os.path.relpath(path, args.root)
        with open(path, encoding="utf-8") as fh:
            text = fh.read()
        doc = load_yaml(text, where)
        on = triggers(doc, where)

        pr_raw, pr_ignores = declared(on, "pull_request")
        push_raw, push_ignores = declared(on, "push")

        # RULE (b), AND IT RUNS BEFORE THE PAIRING RULE RETURNS.
        #
        # Coverage is a property of ONE filter, so it is not the pairing rule's business and must not
        # inherit its scope. A one-sided workflow (`build-config-propagate.yml` is push-only) declares
        # a real filter that can omit a real project, and (a) skips it by design. Checking each
        # declared list on its own terms means neither rule can hide a workflow from the other.
        # ONE FINDING PER MISSING DEPENDENCY, not one per (trigger x subject) that reaches it. The
        # first draft emitted four for coord-engine.yml — the same absent directory, reached from two
        # subjects on two triggers — and one omission stated four times reads as four problems. A
        # gate whose output has to be skimmed is one that gets skimmed (#698); the repair is the same
        # `src/FS.GG.Coord.GitHub/**` either way, so it is one finding that names its several causes.
        # The value carries the RELATION as well as the causes: a project is BUILT FROM its
        # references, a script READS its surface, and telling a reader their workflow "builds"
        # `.agent-skill-roots` is the kind of confidently wrong sentence that gets a gate distrusted.
        excused = allow_uncovered(text)
        missing: dict[str, tuple[set[str], set[str], set[str]]] = {}

        def note(entry: str, cause: str, trigger: str, verb: str) -> None:
            subs, trigs, verbs = missing.setdefault(entry, (set(), set(), set()))
            subs.add(cause)
            trigs.add(trigger)
            verbs.add(verb)

        for trigger, raw in (("pull_request", pr_raw), ("push", push_raw)):
            if raw is None or not isinstance(raw, list) or not raw:
                continue
            pats = [str(p) for p in raw]
            subjects_seen.update((where, sub) for sub in subjects(pats, graph))
            for project, dep in uncovered(pats, graph):
                note(os.path.dirname(dep), os.path.dirname(project), trigger, "builds")

        # RULE (c): a filter naming a gate SCRIPT must select what that script READS.
        #
        # (b)'s shape with a different graph: the workflow's own `paths:` list names the script, and
        # the script's AST supplies the closure. Folded into the same `missing` map so one absent
        # directory is one finding however many rules reach it — a reader does not care which rule
        # noticed, only what to add.
        for trigger, raw in (("pull_request", pr_raw), ("push", push_raw)):
            if raw is None or not isinstance(raw, list) or not raw:
                continue
            pats = [str(p) for p in raw]
            for script in named_scripts(pats, args.root):
                subject = declared_subject(os.path.join(args.root, script), where)
                if subject is None:
                    continue
                scripts_seen.add((where, script))
                for entry in subject:
                    # PATHS_SUBJECT has THREE filesystem dispositions, not two. An existing file is
                    # selected by its exact name; an existing directory is selected by a change
                    # under it; and a path that MAY NOT EXIST YET is still a file-shaped subject.
                    # The last case matters for config discovery: `renovate.json5` does not exist
                    # today, but its appearance changes check-pin-coherence's verdict and must run
                    # that gate. Treating every non-file as a directory probes `entry/x`, which
                    # makes a correct exact filter look uncovered (#1873).
                    path = os.path.join(args.root, entry)
                    probe = f"{entry}/x" if os.path.isdir(path) else entry
                    if not selects(probe, pats):
                        note(entry, script, trigger, "reads")

        for entry, (subs, trigs, verbs) in sorted(missing.items()):
            reason = excused.get(entry)
            if reason == UNSIGNED:
                findings.append(
                    f"{where}: carries `# paths-coherence: allow-uncovered {entry}` with NO "
                    f"reason. An unsigned hatch makes the omission neither a decision nor a typo "
                    f"— write the reason after the marker."
                )
                continue
            if reason:
                allowed += 1
                continue
            named = ", ".join(repr(s) for s in sorted(subs))
            plural = len(subs) > 1
            scope = "both filters" if len(trigs) > 1 else f"the `{next(iter(trigs))}` filter"
            # `builds` and `reads` can both reach one absent path; say so rather than picking.
            verb = " and ".join(sorted(verbs))
            # A FILE (including one that may appear later) IS NOT A DIRECTORY. `.agent-skill-roots`
            # is a file, and telling somebody to add `.agent-skill-roots/**` is advice that matches
            # nothing — the gate would keep reding after they did exactly as told. Only a directory
            # earns the recursive remedy (#1873).
            fix = entry + "/**" if os.path.isdir(os.path.join(args.root, entry)) else entry
            findings.append(
                f"{where}: {scope} name{'' if len(trigs) > 1 else 's'} {named}, which "
                f"{verb if plural else verb.rstrip('s') + 's'} {entry!r} — and nothing in the filter "
                f"selects {entry!r}. A change there changes what "
                f"{'they' if plural else 'it'} {verb}, and this workflow would not run: green by "
                f"absence (#266). Add a pattern covering {fix!r}."
            )

        # An allow-list facing an ignore-list: two filters that both narrow the trigger, in opposite
        # directions, with no way to say whether they agree. Silently skipping it is how a coherence
        # gate fails open (#266), so it is refused.
        #
        # Note what guards this: `pr_raw is not None`. The refusal can only fire on a workflow that
        # HAS an allow-list — i.e. one this gate was actually asked to judge. See declared() for the
        # bug that shape exists to prevent.
        if (pr_raw is not None and push_ignores) or (push_raw is not None and pr_ignores):
            raise GateError(
                f"{where}: one trigger declares `paths:` and the other declares `paths-ignore:`. "
                f"An ignore-list INVERTS selection, so this gate cannot say whether the two agree — "
                f"and a silent skip is how a coherence gate fails open (#266). Refusing to guess."
            )

        # One-sided is a deliberate shape (`build-config-propagate.yml` is push-only,
        # `reusable-job-id-coherence.yml` is PR-only) and is not this gate's business.
        if pr_raw is None or push_raw is None:
            continue

        pr = validated(pr_raw, "pull_request", where)
        push = validated(push_raw, "push", where)

        pairs_seen += 1
        reason = allow_divergence(text, where)

        # A marker with no reason is a FINDING, not a no-verdict. The gate checked and found
        # something definite; exit 3 would tell the developer "I could not check" and hand them
        # paths-coherence.yml's no-verdict summary, which names three causes and none of them is
        # theirs. That is #266's conflation running backwards — a verdict dressed as a failure to
        # reach one.
        if reason == UNSIGNED:
            findings.append(
                f"{where}: carries `# paths-coherence: allow-divergence` with NO reason. The hatch "
                f"exists to make divergence a decision somebody made rather than a typo nobody saw "
                f"— an unsigned marker does neither. Write the reason after the marker."
            )
            continue

        if set(pr) == set(push):
            # A hatch on a workflow that does not diverge is stale licence — it will silently permit
            # a REAL drift the day one arrives, which is the hatch quietly becoming the hole.
            if reason:
                findings.append(
                    f"{where}: carries `# paths-coherence: allow-divergence` ({reason}) but its two "
                    f"`paths:` lists are IDENTICAL. The marker licenses a divergence that does not "
                    f"exist, so it would silently permit a real one later. Remove it."
                )
            else:
                print(f"  ok   {where:<44} {len(pr)} pattern(s), both triggers agree")
            continue

        if reason:
            allowed += 1
            print(f"  allow {where:<43} diverges on purpose: {reason}")
            continue

        only_pr = sorted(set(pr) - set(push))
        only_push = sorted(set(push) - set(pr))
        detail = []
        if only_pr:
            detail.append(f"the `push` copy omits {', '.join(repr(p) for p in only_pr)}")
        if only_push:
            detail.append(f"the `pull_request` copy omits {', '.join(repr(p) for p in only_push)}")
        findings.append(f"{where}: " + "; and ".join(detail) + ".")

    # #266, AND THIS IS THE LINE THAT MATTERS. A reader that breaks — a new `on:` spelling, a YAML
    # quirk, a bad glob — would find zero pairs and report a clean audit over a repo full of them.
    # Zero pairs is therefore a NO VERDICT, not an OK. If this repo ever legitimately drops to zero,
    # that is a deliberate change and this gate should be deleted in the same commit, not left
    # printing green about nothing.
    if pairs_seen == 0:
        raise GateError(
            "audited every workflow and found NO workflow declaring both `pull_request.paths` and "
            "`push.paths`. This repo HAS them, so the trigger reader is broken — examining nothing "
            "is a failure to audit, not a clean audit (#266)."
        )

    # #266 FOR RULE (b) IS ASSERTED BY THE FIXTURE, NOT HERE, AND THAT IS DELIBERATE.
    #
    # The obvious guard — "this tree has projects but no workflow names one, so the reader is broken"
    # — was written, and it is UNSOUND. A repo with projects whose workflows all filter on catch-alls
    # declares no subject and is perfectly healthy; the guard red-lit three of this fixture's own
    # legs, each a legitimate shape (a `tests/**` catch-all, a single named file, a whole-tree
    # `src/**`). It was encoding a fact about THIS repo — "our workflows name projects" — into a rule
    # that runs against any tree, which is how a gate starts refusing correct work.
    #
    # The exposure it was aiming at is real: if the prefix reader broke, (b) would examine nothing and
    # report the same green it reports when everything is covered. That belongs where the repo-specific
    # knowledge is — `tests/paths-coherence/run.sh` asserts the shipped tree declares subjects and
    # finds the real instances, so a reader that goes blind fails there, loudly, against real files.
    # Same reasoning as #724: put the assertion somewhere that executes, not somewhere that reads well.
    #
    # What the gate owes instead is HONESTY about what it looked at, which the summary below prints
    # as a count rather than implying with a bare "ok".

    if findings:
        for f in findings:
            print(f"::error::check-paths-coherence: {f}", file=sys.stderr)
        print(
            f"\n{len(findings)} finding(s), across {pairs_seen} workflow(s) declaring both filters, "
            f"{len(subjects_seen)} declared project subject(s), and {len(scripts_seen)} declared "
            f"gate script surface(s).\n"
            "\n(a) A workflow duplicates its `paths:` list between `pull_request` and `push`. When "
            "the copies drift,\n    the gate still passes its own tests and simply STOPS RUNNING on "
            "`main` — green, and wrong (#880).\n"
            "\n      fix:  make the two lists identical.\n"
            "            Diverging on purpose? Say so, and sign it:\n"
            "              # paths-coherence: allow-divergence — <why>\n"
            "\n(b) A filter names a project's directory but omits something that project is BUILT "
            "FROM. Both\n    copies can agree perfectly and both be wrong the same way — which is "
            "how this class survived\n    (a) landing (#930).\n"
            "\n      fix:  add a pattern covering the referenced project.\n"
            "            Genuinely not an input? Say so, name it, and sign it:\n"
            "              # paths-coherence: allow-uncovered <path> — <why>",
            file=sys.stderr,
        )
        return FINDING

    print(
        f"ok: {pairs_seen} workflow(s) declaring both filters agree; {len(subjects_seen)} declared "
        f"project subject(s) cover their `ProjectReference` closure; {len(scripts_seen)} declared "
        f"gate script surface(s) covered"
        + (f"; {allowed} exception(s) signed" if allowed else "")
        + "."
    )
    return OK


def cli(argv: list[str]) -> int:
    """Guarantee the exit code is a VERDICT, never an accident.

    Python exits 1 on any uncaught exception — and 1 is this gate's "a workflow has drifted". So a
    crash anywhere in here would be dressed up by paths-coherence.yml as a specific, confident,
    WRONG finding about somebody's workflow. That is the conflation the exit-code contract exists to
    prevent (#266, #320): "I could not check" must never share a code with "I checked, and it's
    broken".
    """
    try:
        return main(argv)
    except GateError as e:
        print(f"::error::check-paths-coherence: no verdict — {e}", file=sys.stderr)
        return NO_VERDICT_PERMANENT
    except Exception:  # noqa: BLE001 — deliberately broad; see the docstring
        traceback.print_exc()
        print(
            "::error::check-paths-coherence: the gate crashed, so it has NO VERDICT. This is not a "
            "finding about any workflow's filters — it is a bug in the gate. See the traceback.",
            file=sys.stderr,
        )
        return NO_VERDICT_PERMANENT


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
