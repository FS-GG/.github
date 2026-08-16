#!/usr/bin/env python3
"""Reject broken repository-local citations in live documentation.

The gate answers two narrow, independent questions about live tracked Markdown prose. ADRs and
dated reports are historical records and are excluded from both.

**File existence** (``.github#2587``, ADR-0074): does a path-shaped ``path:line`` citation name a
file tracked by this repository? It does not check line ranges, parse free-form counts, or infer
that a bare basename belongs here. Cross-repository citations must be a URL or the explicit
``OWNER/REPOSITORY@REVISION:path:line`` form; bare basenames and source roots this repository does
not own (for example ``src/Canvas/...``) are outside the local-path grammar.

**Section existence** (``.github#2660``): does a Markdown link fragment name a heading that is
actually present in its target? The file-existence predicate could not see this. ``b84423e7``
deleted fifteen headings from ``independent-review.md``; the file stayed tracked, so every citation
into it still passed while four links pointed at a section that no longer existed.

The section grammar is **deliberately bounded to the Markdown inline link**::

    ](#fragment)              same document
    ](relative/target.md#fragment)   another tracked Markdown file in this repository

and to nothing else. A prose reference — "the numbered steps of X", "see the repair-phase section" —
is out of scope by construction. That bound is the point rather than a limitation to be lifted
later: an open-ended natural-language claim checker was explicitly not what ``.github#2660`` asked
for, and it would manufacture the false positives ADR-0074 exists to avoid. Destinations that carry
a scheme, or that resolve outside this repository, are foreign and ignored; a fragment link whose
destination is a Markdown file this repository does not track is a finding, exactly as an untracked
``path:line`` target is.

Anchors are derived the way GitHub derives them: from ATX headings outside fenced code blocks,
lowercased with non-word characters dropped and spaces hyphenated, with ``-1``/``-2`` suffixes for
repeated headings, plus any explicit ``<a name=...>`` or ``<a id=...>``.

Two limits are stated rather than papered over. A section nothing cites is still deletable
silently — the restoration in ``.github#2660`` earns its protection by being cited. And a section
that is *hollowed out* while keeping its heading still resolves; this gate answers presence, not
content.

Exit 0 = checked a non-empty corpus of BOTH kinds and every citation resolves.
Exit 1 = at least one citation names an untracked path or an absent heading.
Exit 3 = permanent no-verdict: a subject or either corpus is empty, or git inventory is unavailable.
"""
from __future__ import annotations

import argparse
import re
import subprocess
import sys
import unicodedata
from pathlib import Path
from urllib.parse import unquote

OK, FINDING, NO_VERDICT = 0, 1, 3
EXEMPT_PREFIXES = ("docs/adr/", "docs/reports/")
QUALIFIED = re.compile(
    r"(?<![\w.-])[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+@[^\s:`]+:"
    r"(?:\.?[A-Za-z0-9_.-]+/)+[A-Za-z0-9_.-]+:\d+(?:-\d+)?"
)
CITATION = re.compile(
    r"(?<![\w/.-])(?P<path>\.?[A-Za-z0-9_.-]+(?:/[A-Za-z0-9_.-]+)+)"
    r":(?P<line>\d+)(?:-(?P<end>\d+))?"
)
FRAGMENT = re.compile(r"\]\((?P<dest>[^)\s#]*)#(?P<fragment>[^)\s]+)\)")
ATX = re.compile(r"^(?P<hashes>#{1,6})\s+(?P<text>.*?)\s*#*\s*$")
EXPLICIT_ANCHOR = re.compile(r"<a\s+[^>]*(?:name|id)=\"(?P<anchor>[^\"]+)\"")


def slugify(heading: str) -> str:
    """GitHub's heading anchor rule, applied to the heading's rendered text."""
    text = re.sub(r"`([^`]*)`", r"\1", heading)
    text = re.sub(r"\[([^\]]*)\]\([^)]*\)", r"\1", text)
    text = re.sub(r"[*_]", "", text)
    text = unicodedata.normalize("NFKD", text).lower()
    return re.sub(r"[^\w\- ]", "", text).strip().replace(" ", "-")


def anchors_of(root: Path, relative: str) -> set[str]:
    """Every fragment GitHub would resolve inside one tracked Markdown file."""
    counts: dict[str, int] = {}
    fenced = False
    for line in (root / relative).read_text(encoding="utf-8").splitlines():
        if line.lstrip().startswith("```"):
            fenced = not fenced
            continue
        if fenced:
            continue
        heading = ATX.match(line)
        if heading:
            slug = slugify(heading.group("text"))
            counts[slug] = counts.get(slug, 0) + 1
        for explicit in EXPLICIT_ANCHOR.finditer(line):
            counts.setdefault(explicit.group("anchor"), 1)
    resolved: set[str] = set()
    for slug, seen in counts.items():
        resolved.add(slug)
        resolved.update(f"{slug}-{index}" for index in range(1, seen))
    return resolved


def tracked_files(root: Path) -> set[str]:
    completed = subprocess.run(
        ["git", "-C", str(root), "ls-files", "-z"], check=False,
        stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    )
    if completed.returncode != 0:
        raise RuntimeError(completed.stderr.decode(errors="replace").strip())
    return {item.decode() for item in completed.stdout.split(b"\0") if item}


def normalize(raw: str) -> str:
    return raw[2:] if raw.startswith("./") else raw


def local_prefixes(tracked: set[str]) -> tuple[str, ...]:
    """Derive owned roots from git, while keeping foreign ``src/X`` references out of scope."""
    top_levels = {path.split("/", 1)[0] + "/" for path in tracked if "/" in path}
    top_levels.discard("src/")
    source_namespaces = {
        "/".join(path.split("/", 2)[:2]) + "/"
        for path in tracked
        if path.startswith("src/") and path.count("/") >= 2
    }
    return tuple(sorted(top_levels | source_namespaces))


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--root", default=".")
    args = parser.parse_args(argv)
    root = Path(args.root).resolve()
    try:
        tracked = tracked_files(root)
    except (OSError, RuntimeError) as exc:
        print(f"::error::prose-citations: cannot enumerate tracked files: {exc}", file=sys.stderr)
        return NO_VERDICT

    subjects = sorted(path for path in tracked if path.endswith(".md")
                      and not path.startswith(EXEMPT_PREFIXES))
    if not subjects:
        print("::error::prose-citations: no live tracked Markdown subjects", file=sys.stderr)
        return NO_VERDICT

    owned = local_prefixes(tracked)
    examined = 0
    sections = 0
    findings: list[str] = []
    anchor_cache: dict[str, set[str]] = {}
    for relative in subjects:
        try:
            lines = (root / relative).read_text(encoding="utf-8").splitlines()
        except OSError as exc:
            print(f"::error::prose-citations: cannot read {relative}: {exc}", file=sys.stderr)
            return NO_VERDICT
        for number, original in enumerate(lines, 1):
            line = QUALIFIED.sub("", original)
            for match in CITATION.finditer(line):
                target = normalize(match.group("path"))
                if not target.startswith(owned):
                    continue
                examined += 1
                if target not in tracked:
                    findings.append(
                        f"{relative}:{number}: repository-local citation "
                        f"{target}:{match.group('line')} does not name a tracked file"
                    )
            for match in FRAGMENT.finditer(original):
                destination = match.group("dest")
                if "://" in destination or destination.startswith("mailto:"):
                    continue
                if destination:
                    if not destination.endswith(".md"):
                        continue
                    try:
                        resolved = ((root / relative).parent / unquote(destination)).resolve()
                        target = resolved.relative_to(root).as_posix()
                    except (ValueError, OSError):
                        continue
                    if target not in tracked:
                        findings.append(
                            f"{relative}:{number}: section citation {destination}"
                            f"#{match.group('fragment')} does not name a tracked file"
                        )
                        continue
                else:
                    target = relative
                sections += 1
                if target not in anchor_cache:
                    try:
                        anchor_cache[target] = anchors_of(root, target)
                    except OSError as exc:
                        print(f"::error::prose-citations: cannot read {target}: {exc}",
                              file=sys.stderr)
                        return NO_VERDICT
                fragment = unquote(match.group("fragment"))
                if fragment not in anchor_cache[target]:
                    findings.append(
                        f"{relative}:{number}: section citation {destination}#{fragment} "
                        f"names no heading in {target}"
                    )

    if examined == 0:
        print("::error::prose-citations: found zero repository-local path:line citations; "
              "the extractor or subject selection has no measurable corpus", file=sys.stderr)
        return NO_VERDICT
    if sections == 0:
        print("::error::prose-citations: found zero Markdown section citations; the fragment "
              "extractor or subject selection has no measurable corpus", file=sys.stderr)
        return NO_VERDICT
    if findings:
        for finding in findings:
            print(f"::error::prose-citations: {finding}", file=sys.stderr)
        return FINDING
    print(f"prose-citations: ok ({len(subjects)} live documents, {examined} local citations, "
          f"{sections} section citations)")
    return OK


if __name__ == "__main__":
    raise SystemExit(main())
