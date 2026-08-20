#!/usr/bin/env python3
"""`Scan.scope` is the engine's ONLY `--repo` filter — assert it (FS-GG/.github#979).

A verb that takes `--repo` and filters the board rows ITSELF is a fail-open, and the engine had FIVE
of them: `Scan.snapshot`, `ready`, `who`, `inbox` and `lint` each wrote the comparison out by hand. A
`--repo` naming no rostered repo resolves to itself (a literal name is a legitimate spelling —
`--repo .github` works that way), matches no row, and reports an EMPTY QUEUE WITH A GREEN EXIT. That
is indistinguishable from a repo which genuinely has no items, which is epic #266's signature exactly:
a check reporting success over a subject it never found.

THE CLASS, NOT THE INSTANCE. This is the same shape #978 fixed one layer down for repo RESOLUTION, and
`Options.fs` states the rule in as many words about its own subject:

    IT LIVES IN THE PARSER BECAUSE THAT IS THE ONLY PLACE EVERY VERB REACHES (#962). It used to live
    in `Client`, applied per-verb at a dispatch site, and being left out of that list is a silent
    fail-open.

#978 moved the resolution MAP to one funnel and left the FILTER scattered across five. The class had
already produced four instances — #381, #446, #962, #979 — and every repair added the missing verb
rather than removing the list. A funnel fixes today's five copies. A funnel plus a gate is what makes
a sixth unwritable, which is the only thing that ends a class (#724: "a fifth copy is not
discouraged; it is unwritable").

Nothing here is hypothetical about the enumeration being hard: #979's own investigation tabled THREE
hand-rolled sites after a careful read of the tree. There were five. `who` and `inbox` were missed by
the very worker who had the file open and was counting them — which is the argument for a gate rather
than for reading more carefully.

WHAT IS A SUBJECT, AND WHAT THE ALLOW-LIST IS (FS-GG/.github#1161, D3 of #1158)

The subject is a CASE-INSENSITIVE STRING COMPARISON OF A ROW'S `.Ref.Repo` — `String.Equals(<x>.Ref
.Repo, <y>, StringComparison…)`, or a bare `=`/`<>` with `.Ref.Repo` on either side. That is the
exact shape all five copies had, and the shape a sixth would have.

AN ALLOW-LIST, NOT A DENY-LIST. EVERY such comparison is a subject; the only ones that pass are `HOME`
(the funnel, the one place allowed to filter) and lines carrying an EXPLICIT exempt marker. The gate
used to guess — a bare `=` whose RHS named `.Repo` was auto-excluded as "ref-to-ref by construction" —
and that heuristic is precisely the fail-open this item closes: a real `--repo` filter spelled
`r.Ref.Repo = opts.Repo` has `.Repo` on its RHS too, so the guess waved the bug through. A gate that
infers "this one is fine" from an argument name is a gate one argument name can fool.

So a REF-TO-REF comparison — `Lanes.fs`'s `a.Ref.Repo = b.Ref.Repo` (which items share a repo?) and
`Client.fs`'s `String.Equals(r.Ref.Repo, ref.Repo, …)` (`widen`'s "other in-flight items in THIS
ref's repo") — is a legitimate question `--repo` has no part in, but it is no longer excused by
guesswork. It opts out EXPLICITLY, on the line before it, exactly like every other exemption:

    // repo-filter-monopoly: exempt — <why this is not a --repo filter>

WHAT IT DOES NOT CLAIM. A verb could still scope by `--repo` in a shape this gate cannot see: mapping
rows to names first and comparing those, or a `List.contains` over a name set. Those are not the bug
that has bitten four times, and a gate that tried to catch every conceivable spelling would be a
regex that guesses. The gate's job is to make the KNOWN shape unwritable and to say plainly which
shape that is. It is not a proof that no filter exists.

EXIT CODES
  0  clean
  1  findings
  3  NO_VERDICT — the walker found no subjects AT ALL. `Scan.scope` demonstrably contains one, so
     silence means the walker broke, not that the tree is clean. An unverifiable subject must not
     report green (epic #266) — a gate that cannot see its own home would go green across the org
     while the fail-open it exists to catch went on happening.
"""

import re
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from lib.gate import report_findings, report_ok, run, subject_census  # noqa: E402

# The engine's F# source. The gate walks `.fs`; `.fsi` files are signatures and carry no filter.
SURFACES = ["src"]

# THE HOME. The one file allowed to compare a row's repo against a `--repo` — `Scan.scope`, the funnel
# every verb now routes through. A path, because the F# module boundary IS the unit of the monopoly.
HOME = "src/FS.GG.Coord.GitHub/Scan.fs"

# WHAT THIS GATE READS, FOR THE WORKFLOW THAT RUNS IT (#996, epic #266). `check-paths-coherence.py`
# reads this BY AST and reds `repo-filter-monopoly.yml` if its `paths:` does not select every entry.
# It is the SURFACES list itself, not a copy: the thing this gate walks is the thing the filter is
# checked against, so widening one widens the other.
PATHS_SUBJECT = SURFACES + [HOME]

# The shape every one of the five copies had. `String.Equals(r.Ref.Repo, name, StringComparison…)`.
EQUALS = re.compile(r"String\.Equals\(\s*[A-Za-z_][\w.]*\.Ref\.Repo\s*,")

# A bare structural comparison on `.Ref.Repo`. NO negative lookahead: a ref-to-ref comparison is a
# subject like any other and opts out with an explicit marker, not by the RHS happening to name
# `.Repo` — that guess is what let `r.Ref.Repo = opts.Repo`, a real `--repo` filter, through (#1161).
BARE = re.compile(r"\.Ref\.Repo\s*(?:=|<>)")

# The funnel first resolves `PathRepo` and therefore no longer compares `.Ref.Repo` directly. This
# is the actual production spelling the monopoly permits, separate from the known hand-rolled shape.
HOME_EQUALS = re.compile(
    r"RepoScope\.Repository\s+(?P<resolved>[A-Za-z_]\w*)\s*->\s*String\.Equals\(\s*(?P=resolved)\s*,\s*name\s*,"
)

EXEMPT = re.compile(r"//\s*repo-filter-monopoly:\s*exempt\b")


def producer_subjects(root: Path) -> tuple[str, ...]:
    """Independently enumerate `String.Equals` calls emitted inside production `Scan.scope`.

    This parser knows only the F# function boundary and the call name. It does not consume EQUALS,
    BARE, HOME_EQUALS, or the walker's output, so a broken semantic matcher cannot zero both sides.
    """

    path = root / HOME
    if not path.is_file():
        return ()
    lines = path.read_text(encoding="utf-8").splitlines()
    in_scope = False
    produced: list[str] = []
    for n, line in enumerate(lines, 1):
        if re.match(r"^    let scope\b", line):
            in_scope = True
            continue
        if in_scope and re.match(r"^    let\s+", line):
            break
        if in_scope and "String.Equals(" in line:
            produced.append(f"{HOME}:{n}")
    return tuple(produced)


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    root = Path(__file__).resolve().parent.parent
    if "--root" in argv:
        root = Path(argv[argv.index("--root") + 1]).resolve()

    findings = []
    subjects: list[str] = []

    for surface in SURFACES:
        base = root / surface
        if not base.is_dir():
            continue
        for path in sorted(base.rglob("*.fs")):
            # Build output is not source. A stale `obj/` copy of a fixed file would red the tree.
            if any(part in ("obj", "bin") for part in path.parts):
                continue

            rel = path.relative_to(root).as_posix()
            lines = path.read_text(encoding="utf-8").splitlines()
            pending_exempt = False

            for n, line in enumerate(lines, 1):
                if EXEMPT.search(line):
                    pending_exempt = True
                    continue

                hit = EQUALS.search(line) or BARE.search(line) or (rel == HOME and HOME_EQUALS.search(line))
                if not hit:
                    # A blank line does not carry an exemption across to the next statement.
                    if line.strip() and not line.strip().startswith("//"):
                        pending_exempt = False
                    continue

                subjects.append(f"{rel}:{n}")

                if rel == HOME:
                    pending_exempt = False
                    continue

                if pending_exempt:
                    pending_exempt = False
                    continue

                findings.append(
                    f"{rel}:{n}: a hand-rolled `--repo` filter. Scope through "
                    f"`Scan.scope opts.Repo` instead — it filters AND reports the name that "
                    f"matched nothing, in one breath (#979).\n    {line.strip()}"
                )

    census = subject_census(
        declared=PATHS_SUBJECT,
        resolved=tuple(subject for subject in PATHS_SUBJECT if (root / subject).exists()),
        examined=subjects,
        producers=producer_subjects(root),
        authority_revision="PATHS_SUBJECT/v2-semantic-census",
    )

    # Completeness is decided before the monopoly predicate: a moved HOME can itself look like a
    # hand-rolled filter, but the stronger fact is that the declared authority did not resolve.
    if not census.complete:
        return report_ok(
            "repo-filter-monopoly",
            f"{len(subjects)} comparison subject(s) examined",
            census,
        )

    if findings:
        return report_findings("repo-filter-monopoly", findings)

    return report_ok(
        "repo-filter-monopoly",
        f"{len(subjects)} comparison subject(s) examined; the filter lives only in {HOME}.",
        census,
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name="repo-filter-monopoly"))
