#!/usr/bin/env python3
"""Prepend one dated entry to registry/CHANGELOG.md's Entries list (.github#2558).

WHY THIS EXISTS AS A SCRIPT RATHER THAN A `sed` IN A WORKFLOW.

kit-auto-publish.yml used to insert its evidence entry with `sed -i "2i\\${entry}"` — a HARDCODED
line 2, while `## Entries` sits at line 20. `check-registry-changelog.py::top_date` refuses any dated
entry above that heading, so the workflow wrote precisely the shape its own gate rejects, and every
evidence PR was born red. `grep -cF "auto-publish evidence" registry/CHANGELOG.md` on `main` returned
**0** across every kit publish this repository had ever made.

The root cause is not the number 2. It is that the WRITER and the GATE were two independent readings
of the same protocol, free to disagree — and they did, silently, for months. So this writer does not
re-implement the protocol: it IMPORTS `check-registry-changelog.py` and calls that gate's own
`entries_start`, `top_date` and `updated` (a) to locate the heading, and (b) to VERIFY its own output
before it returns. A future edit that breaks the agreement can no longer produce a quietly-red PR;
it fails here, in the writer, at the moment of writing.

THE HEADING'S POSITION IS DATA. It is located by matching the gate's own `^## Entries$` regex, never
by an offset. `.github#2558` acceptance criterion 1 forbids a new fixed offset, and the file is under
active change (`.github#2536`, `.github#2552`), so any constant would be stale on arrival.

THE SECOND ARM IS WHY THIS ALSO TOUCHES dependencies.yml. Moving the entry below the heading is NOT
sufficient to make the gate pass. `check-registry-changelog.py::main` compares
`dependencies.yml::updated:` against the TOP changelog date and refuses when they differ:

    ::error::check-registry-changelog: dependencies.yml updated=2026-08-13 != top changelog date=2026-08-20

A repair that fixed only the insertion point would therefore still have produced a red PR on every
publish that did not happen to land on the same UTC day as the last registry flip — the same
never-lands outcome, reached by a different refusal, and one that a fixture asserting only "the entry
is below the heading" would not have caught. CHANGELOG.md's own stated protocol already requires this
pairing: "Every change to `dependencies.yml`, and every FS.GG.Kit republish, prepends one dated entry
at the top of the Entries list below (newest first) and sets the file's `updated:` date to match."
"and every FS.GG.Kit republish" is this caller. So both files move together, or neither does.
"""
from __future__ import annotations

import argparse
import datetime as _dt
import importlib.util
import re
import sys
from pathlib import Path

HERE = Path(__file__).resolve().parent


class Refused(Exception):
    pass


def _load_gate():
    """Import check-registry-changelog.py by path (its filename is not an identifier).

    Importing the gate — rather than restating its regexes — is the whole point of this module: the
    writer and the checker share one definition of where the Entries heading is and what a dated
    entry looks like, so they cannot drift apart again.
    """
    path = HERE / "check-registry-changelog.py"
    spec = importlib.util.spec_from_file_location("check_registry_changelog", path)
    if spec is None or spec.loader is None:
        raise Refused(f"cannot load the registry-changelog gate from {path}")
    mod = importlib.util.module_from_spec(spec)
    try:
        spec.loader.exec_module(mod)
    except OSError as exc:
        raise Refused(f"cannot load the registry-changelog gate from {path}: {exc}") from exc
    return mod


# The gate's own `updated:` shape, decomposed so only the DATE is rewritten and the surrounding
# quoting and trailing comment survive byte-for-byte. Keep this in step with
# check-registry-changelog.py::updated — the post-write verification below calls that function on the
# result, so a divergence here is caught rather than shipped.
UPDATED_RE = re.compile(
    r'^(updated:\s*)(["\'])(\d{4}-\d{2}-\d{2})(["\'])((?:\s+#.*)?\s*)$', re.M
)


def gate_call(gate, name: str, *args):
    """Call a gate function, translating ITS `Refused` into OURS.

    The gate defines its own `Refused` class, which is not this module's — so an uncaught one escapes
    as a traceback instead of the `::error::` line every other checker in scripts/ emits. The exit
    code is 1 either way, but a traceback is not a diagnostic a workflow log reader can act on.
    """
    try:
        return getattr(gate, name)(*args)
    except gate.Refused as exc:
        raise Refused(str(exc)) from exc


def read(path: Path) -> str:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exc:
        raise Refused(f"cannot read {path}: {exc}") from exc


def write(path: Path, text: str) -> None:
    try:
        path.write_text(text, encoding="utf-8")
    except OSError as exc:
        raise Refused(f"cannot write {path}: {exc}") from exc


def insert_entry(text: str, entry: str, gate) -> str:
    """Insert `entry` as the FIRST item of the Entries list. Position is found, never assumed."""
    marker = re.search(r"^## Entries\s*$", text, re.M)
    if not marker:
        # Same refusal the gate raises, at the same wording, because it is the same condition.
        raise Refused("CHANGELOG.md lacks its Entries heading")
    lines = text.splitlines(keepends=True)
    heading_index = text.count("\n", 0, marker.start())
    at = heading_index + 1
    # Preserve the single blank line the file keeps between the heading and its first entry.
    if at < len(lines) and lines[at].strip() == "":
        at += 1
    body = entry.rstrip("\n") + "\n"
    return "".join(lines[:at] + [body, "\n"] + lines[at:])


def set_updated(text: str, date: str) -> str:
    hits = UPDATED_RE.findall(text)
    if len(hits) != 1:
        raise Refused(
            "dependencies.yml must contain exactly one quoted updated: YYYY-MM-DD "
            f"(found {len(hits)})"
        )
    return UPDATED_RE.sub(lambda m: f"{m[1]}{m[2]}{date}{m[4]}{m[5]}", text, count=1)


def main(argv: list[str]) -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--changelog", default="registry/CHANGELOG.md")
    ap.add_argument("--dependencies", default="registry/dependencies.yml")
    ap.add_argument("--date", default=_dt.datetime.now(_dt.timezone.utc).strftime("%Y-%m-%d"))
    ap.add_argument(
        "--marker",
        required=True,
        help="idempotence key; if this substring is already in the changelog, do nothing",
    )
    ap.add_argument("--entry", required=True, help="the entry body, without its leading '- **DATE** '")
    args = ap.parse_args(argv)

    try:
        gate = _load_gate()
        if not re.fullmatch(r"\d{4}-\d{2}-\d{2}", args.date):
            raise Refused(f"--date must be YYYY-MM-DD, got {args.date!r}")
        if not args.marker.strip():
            raise Refused("--marker must not be empty")

        log_path, dep_path = Path(args.changelog), Path(args.dependencies)
        log_text = read(log_path)

        # STRUCTURE IS CHECKED BEFORE IDEMPOTENCE, and the order is deliberate. If the early
        # "already recorded" return came first, a changelog that had LOST its Entries heading would
        # be reported as a satisfied no-op instead of refused — the structural defect masked by an
        # idempotence hit, which is the same class of silent success this script exists to end.
        gate_call(gate, "entries_start", log_text)

        # Idempotence: a re-run for the same version must not stack a second copy. This is the
        # `grep -Fq ... ||` guard the workflow used to carry, moved in here so the writer owns it.
        if args.marker in log_text:
            print(f"registry changelog already records {args.marker!r}; nothing to do")
            return 0

        # Build the entry in the exact shape the gate's `top_date` recognises. Composing it here,
        # rather than accepting a caller-formatted line, is what stops a caller from inventing a
        # bullet the gate cannot see.
        entry = f"- **{args.date}** — {args.entry.strip()}"
        if not re.match(r"^- \*\*(\d{4}-\d{2}-\d{2})\*\*", entry):
            raise Refused("composed entry does not match the gate's dated-entry shape")

        new_log = insert_entry(log_text, entry, gate)
        dep_text = read(dep_path)
        new_dep = set_updated(dep_text, args.date)

        # VERIFY BEFORE WRITING, using the gate's own functions on the prospective content. This is
        # the check whose absence let the old `sed` ship a shape the gate refused.
        try:
            top = gate_call(gate, "top_date", new_log)
            dep_date = gate_call(gate, "updated", new_dep)
        except Refused as exc:
            raise Refused(f"the entry this writer produced would not satisfy the gate: {exc}") from exc
        if top != args.date:
            raise Refused(f"inserted entry is not the top dated entry (top={top}, wrote {args.date})")
        if dep_date != top:
            raise Refused(f"dependencies.yml updated={dep_date} would not match top entry {top}")

        write(log_path, new_log)
        write(dep_path, new_dep)
    except Refused as exc:
        print(f"::error::prepend-registry-changelog-entry: {exc}", file=sys.stderr)
        return 1
    print(f"prepended {args.marker!r} below the Entries heading and set updated: {args.date}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
