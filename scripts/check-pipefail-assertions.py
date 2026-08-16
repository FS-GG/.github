#!/usr/bin/env python3
r"""Assert no shell file consumes the status of a pipeline that ends in an early-exiting reader.

.github#2689, generalising the single-file source guard .github#2668 shipped in
`tests/coord-engine-e2e/run.sh`. Epic #266 (the rule nothing asserts).

THE DEFECT THIS CLOSES. Under `set -o pipefail`, the spelling

    printf '%s' "$out" | grep -q PATTERN

does not answer "does $out contain PATTERN?". `grep -q` exits the instant it matches, by contract.
If the writer still had bytes to deliver at that moment it takes SIGPIPE, the pipeline's status
under `pipefail` becomes 141, and `if` reads 141 as "the pattern was NOT found" -- for output that
plainly contains it. The verdict is a function of how much the writer had left when the reader
stopped, which is a buffer-size and scheduling fact, not a fact about the haystack.

`.github#2668` established that with 30-run repeatability across five haystack sizes; the hole opens
past roughly 16 KB. Its sharpest datum: the identical assertion over the identical 22,956-byte real
engine refusal string returned BOTH 141 and 0 on one host, minutes apart. None of that is re-derived
here, and this gate deliberately does not depend on it being re-derived: the shape is disqualifying
whatever the buffer does on any particular day, because an assertion whose verdict depends on
scheduling is not an assertion.

`head` and `grep -m N` are the same mechanism with no `-q` in sight, and missing them was
`.github#2668`'s own round-1 finding M3 -- both were measured 141 on 30/30 runs at 282,640 B. A
guard blind to them is blind to real offenders, so all three spellings are banned here.

WHY A GUARD AND NOT A SWEEP. `.github#2681` proposed converting every existing site and was closed
under the filing bar: it would have declared most of `tests/` on a board already reporting
`CEILING: 0 worker(s) can start right now`. A guard makes every FUTURE site red on arrival, which is
the difference between enumerating a class and closing it, and it lets the existing sites convert
opportunistically inside whatever touch-set already owns each file, at zero marginal lane cost.

WHAT IT SCANS. Every shell file `git ls-files` knows about, decided by extension and then by SHEBANG
-- never by a `paths:`-style shortcut. That is `scripts/lint-shell.sh`'s discovery discipline and it
is load-bearing for the same #648 reason: four of this repo's shell files are spelled as COMMANDS
with no extension (`scripts/fsgg-coord` is the kit itself), and an extension-only sweep reports green
having never opened them. Files that do not set `pipefail` are out of scope, because without it the
pipeline's status is the LAST stage's and the SIGPIPE of an earlier stage is invisible.

The scan is repo-wide rather than `tests/`-only. The row's title says repo-wide, and the class is
not test-specific: `scripts/lint-shell.sh`'s own `is_shell()` carries a comment about the identical
race in its shebang read.

HEREDOC BODIES ARE NOT TRACKED, and that is a measured decision rather than an omission. Of the 1186
sites this gate found on the tree it shipped against, exactly ONE sits inside a heredoc body
(0.08%). Tracking `<<`/`<<-` with quoted and unquoted delimiters, several on a line, is a meaningful
amount of fragile machinery to buy that, and skipping heredoc bodies would ALSO create a real blind
spot -- a heredoc that writes a shell script which later runs under `pipefail` is a genuine offender
one level down. So heredoc bodies are scanned like any other line, and the pragma below is the
escape hatch for the rare case.

THE PRAGMA. A line may carry

    # discarded-status-ok: <reason>

and a reason is REQUIRED -- a bare `# discarded-status-ok` does not suppress anything. That mirrors
`.github/workflows/shell-lint.yml`'s rule for `# shellcheck disable=`: a suppression without a
stated reason is a suppression nobody can review. Whole-line comments are exempt too, so a file may
quote the banned spelling while explaining it, which `tests/coord-engine-e2e/run.sh`'s header does.

THE BASELINE, AND WHY IT IS EXACT COUNTS. Acceptance criterion 4 of `.github#2689` requires that the
pre-existing sites do NOT red today: a gate that reds 1186 times on arrival is a gate nobody reads,
which is `scripts/check-gate-finding-history.py`'s own subject. The mechanism chosen here, stated
explicitly as that criterion requires, is A PER-FILE COUNT BASELINE THAT MUST MATCH EXACTLY -- not a
`>=` allowance, and not a scoping of the gate to changed lines.

  - a file with MORE hits than its baseline is a finding: a new site arrived.
  - a file with FEWER hits than its baseline is ALSO a finding: the baseline is stale, and the gate
    prints the lower number to write. This is what makes the baseline SHRINK rather than merely
    exist. A repair and its decrement land in one commit, so the baseline is provably current.
  - a file with hits and NO baseline entry is a finding: a new offender in a new file.
  - a baseline entry for a path that is no longer in the corpus is a finding: delete the entry.

Per-file counts, not `path:line`: line numbers move under every unrelated edit, and a baseline that
reds on an unrelated edit is a baseline somebody deletes. The known cost of counting is that a
repair and a fresh offender in the SAME file cancel out; that is the standard ratchet trade and it
is stated here rather than discovered later.

FAILS CLOSED (epic #266). Scanning zero files is a failure to scan, not a clean scan. A corpus that
comes back empty, a corpus in which no file sets `pipefail`, and a baseline that cannot be read are
all exit 3 -- NO VERDICT -- never exit 0. "I could not check" must never share an exit code with "I
checked, and it's fine" (#266), nor with "I checked, and it's broken" (#320).

THIS GATE IS PURE AND OFFLINE. It reads the working tree and nothing else: no API, no network, no
credentials, no shellcheck. There is no condition under which "try again" is the right advice, so
exit 2 is deliberately absent from the vocabulary rather than reserved.

THE OTHER HALF OF THIS CLASS IS NOT HERE, AND THAT IS DELIBERATE. `.github#2689`'s criterion 6 asks
for a second rule: a bare `! cmd` statement under `errexit`, which bash exempts from `errexit` and
which therefore computes the right answer and discards it -- the same property, a different bash
mechanism. That rule is NOT implemented in this file. shellcheck's SC2251 is exactly that condition,
and it is enabled in `scripts/lint-shell.sh` against the already-pinned, already-wired binary. See
that file's `SC2251` block for the measurements that settled it. Writing a second detector for a
condition a pinned linter already decides correctly -- including the `find ... ! -name` operand this
repo carries as a committed counter-example, where a naive `^\s*!` regex is 20% wrong -- would have
been new machinery with strictly worse discrimination.

Usage:
  check-pipefail-assertions.py [--root <dir>] [--baseline <file>]
Exit: 0 = every file matches its baseline exactly; 1 = a new site, a stale baseline entry, or an
unbaselined file; 3 = no verdict, PERMANENT -- empty corpus, no `pipefail` file, or an unreadable
baseline.
"""

from __future__ import annotations

import argparse
import os
import re
import subprocess
import sys

DEFAULT_BASELINE = "tests/pipefail-assertions/baseline.txt"

# Shell, decided exactly as scripts/lint-shell.sh decides it. The interpreter must be a whole PATH
# COMPONENT -- anchored to the `/` or the `env ` in front of it -- or `#!/bin/zsh` and `#!/bin/fish`
# read as shell. That over-match was a live bug in lint-shell.sh's first draft; the alternation
# names its dialects rather than matching anything whose name merely ENDS in `sh`.
SHEBANG_RE = re.compile(r"^#!\s*(?:[^\s]*/)?(?:env\s+)?(?:bash|sh|dash|ksh)(?:\s|$)")

# Does this file turn on `pipefail`? Without it an earlier stage's SIGPIPE never reaches the
# pipeline's status at all, so the shape is not the defect this gate is about.
# Matches `set -o pipefail`, `set -euo pipefail`, `set -eo pipefail`, and `set +o pipefail` is not
# excluded here on purpose: a file that toggles it off and on still has the hazard in the window.
PIPEFAIL_RE = re.compile(r"^\s*set\s+[-+][^#]*\bpipefail\b")

# The banned pipeline shapes, generalised from the regex .github#2668 proved in miniature:
#   `| grep ... -q`   any flag bundle ending in q, so -qE / -qi / -m1 -q are all caught
#   `| grep ... -m N` stops after N matches and exits -- early exit with no `q` in sight
#   `| head`          stops after its count, including `$(... | head)` where `)` follows at once
# `[^|]*` cannot cross a pipe, so `| grep -v foo | wc -l` is correctly NOT a hit: the flag has to
# belong to the grep that ends the pipeline.
OFFENDER_RE = re.compile(
    r"\|\s*(?:grep[^|]*(?:-[A-Za-z]*q|-m\s*[0-9])|head(?:[^\w-]|$))"
)

# A suppression must carry a reason. `# discarded-status-ok:` with nothing after it suppresses
# nothing, so an empty pragma cannot be used to wave a site through.
PRAGMA_RE = re.compile(r"#\s*discarded-status-ok:\s*\S")

REMEDY = """\
Replace the pipeline with a spelling that has no live writer to kill. All three run as printed:

    # fixed string, no regex metacharacters interpreted, no pipeline at all
    case "$haystack" in *"$needle"*) found=1 ;; *) found=0 ;; esac

    # extended regular expression, here-string: bash materialises it before grep reads it
    grep -qE -- "$re" <<<"$haystack"

    # a prefix of a string, without `head`
    prefix="${haystack:0:200}"

If a site genuinely must keep the pipeline, annotate the line with a reason:

    ... | grep -q PATTERN   # discarded-status-ok: <why this one cannot mislead>
"""


class NoVerdict(Exception):
    """The gate could not reach a verdict. Exit 3, never 0."""


def discover(root: str) -> list[str]:
    """Every shell file git knows about, by extension and then by shebang."""
    try:
        out = subprocess.run(
            ["git", "ls-files", "-z"],
            cwd=root,
            capture_output=True,
            text=True,
            check=True,
        )
    except (OSError, subprocess.CalledProcessError) as exc:
        raise NoVerdict(f"could not enumerate the tree with `git ls-files` in {root!r}: {exc}")

    shell: list[str] = []
    for rel in out.stdout.split("\0"):
        if not rel:
            continue
        path = os.path.join(root, rel)
        if not os.path.isfile(path):
            # A deleted-but-staged path, or a symlink to nowhere.
            continue
        if rel.endswith((".sh", ".bash")):
            shell.append(rel)
            continue
        try:
            with open(path, "rb") as fh:
                first = fh.readline().decode("utf-8", "replace")
        except OSError:
            continue
        if SHEBANG_RE.match(first):
            shell.append(rel)
    return sorted(shell)


def scan_file(path: str) -> tuple[bool, list[tuple[int, str]]]:
    """(sets pipefail, [(lineno, text), ...]) for one file."""
    try:
        with open(path, encoding="utf-8", errors="replace") as fh:
            lines = fh.read().splitlines()
    except OSError as exc:
        raise NoVerdict(f"could not read a discovered shell file {path!r}: {exc}")

    pipefail = any(PIPEFAIL_RE.match(ln) for ln in lines)
    if not pipefail:
        return False, []

    hits: list[tuple[int, str]] = []
    for n, ln in enumerate(lines, start=1):
        if ln.lstrip().startswith("#"):
            continue
        if PRAGMA_RE.search(ln):
            continue
        if OFFENDER_RE.search(ln):
            hits.append((n, ln.strip()))
    return True, hits


def read_baseline(path: str) -> dict[str, int]:
    """`<count> <path>` per line; `#` comments and blanks ignored."""
    try:
        with open(path, encoding="utf-8") as fh:
            raw = fh.read()
    except OSError as exc:
        raise NoVerdict(
            f"could not read the baseline {path!r}: {exc}. An unreadable baseline is NO VERDICT: "
            "without it this gate cannot tell a pre-existing site from a new one, and reporting "
            "green would be reporting that it checked."
        )

    baseline: dict[str, int] = {}
    for n, line in enumerate(raw.splitlines(), start=1):
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        parts = stripped.split(None, 1)
        if len(parts) != 2 or not parts[0].isdigit():
            raise NoVerdict(
                f"{path}:{n}: malformed baseline entry {stripped!r}; expected `<count> <path>`"
            )
        count, rel = int(parts[0]), parts[1]
        if rel in baseline:
            raise NoVerdict(f"{path}:{n}: duplicate baseline entry for {rel!r}")
        baseline[rel] = count
    return baseline


def run(root: str, baseline_path: str) -> tuple[int, list[str], list[str]]:
    """-> (exit code, findings, notes)."""
    findings: list[str] = []
    notes: list[str] = []

    shell = discover(root)
    if not shell:
        raise NoVerdict(
            "discovered ZERO shell files. This repo demonstrably contains shell, so this means "
            "DISCOVERY BROKE -- not that the tree is clean."
        )

    observed: dict[str, int] = {}
    pipefail_files = 0
    for rel in shell:
        sets_pipefail, hits = scan_file(os.path.join(root, rel))
        if not sets_pipefail:
            continue
        pipefail_files += 1
        if hits:
            observed[rel] = len(hits)
            notes.extend(f"    {rel}:{n}: {text}" for n, text in hits)

    if pipefail_files == 0:
        raise NoVerdict(
            f"scanned {len(shell)} shell file(s) and NONE of them sets `pipefail`. This repo's "
            "shell demonstrably does, so this means the `pipefail` detection broke -- not that "
            "the tree is clean."
        )

    baseline = read_baseline(os.path.join(root, baseline_path))

    for rel in sorted(set(observed) | set(baseline)):
        got = observed.get(rel, 0)
        want = baseline.get(rel)
        if want is None:
            findings.append(
                f"{rel}: {got} site(s) consuming an early-exiting pipeline's status, and this file "
                f"has no baseline entry. This is a NEW offender.\n"
                f"    Repair it, or -- if it is genuinely pre-existing -- add `{got} {rel}` to "
                f"{baseline_path}."
            )
        elif got > want:
            findings.append(
                f"{rel}: {got} site(s), but the baseline allows {want}. {got - want} NEW site(s) "
                f"arrived.\n    Repair them. Raising the baseline is not the remedy."
            )
        elif got < want:
            line = f"{got} {rel}" if got else "(delete the line)"
            findings.append(
                f"{rel}: {got} site(s), but the baseline still claims {want}. The baseline is "
                f"STALE.\n    Lower it in this same commit: {baseline_path} should read `{line}`."
            )

    return (1 if findings else 0), findings, notes


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(
        description=__doc__.splitlines()[0],
        formatter_class=argparse.RawDescriptionHelpFormatter,
    )
    ap.add_argument("--root", default=".", help="repo root (default: .)")
    ap.add_argument(
        "--baseline",
        default=DEFAULT_BASELINE,
        help=f"per-file count baseline, relative to --root (default: {DEFAULT_BASELINE})",
    )
    ap.add_argument(
        "--show-sites",
        action="store_true",
        help="also list every site found, baselined or not",
    )
    args = ap.parse_args(argv)

    rc, findings, notes = run(args.root, args.baseline)

    if args.show_sites and notes:
        print("sites found (baselined and new alike):")
        print("\n".join(notes))

    if rc == 0:
        print(
            "check-pipefail-assertions: OK -- every file matches its baseline exactly."
        )
        return 0

    for f in findings:
        print(f"::error::check-pipefail-assertions: {f}", file=sys.stderr)
    print("", file=sys.stderr)
    print(REMEDY, file=sys.stderr)
    return 1


def cli(argv: list[str]) -> int:
    try:
        return main(argv)
    except NoVerdict as exc:
        print(f"::error::check-pipefail-assertions: no verdict -- {exc}", file=sys.stderr)
        return 3
    except Exception as exc:  # noqa: BLE001 -- a crash is a no-verdict, never a pass
        print(
            "::error::check-pipefail-assertions: the gate crashed, so it has NO VERDICT. This is "
            f"not a clean tree: {exc!r}",
            file=sys.stderr,
        )
        return 3


if __name__ == "__main__":
    sys.exit(cli(sys.argv[1:]))
