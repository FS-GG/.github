#!/usr/bin/env python3
r"""Assert the ADR corpus agrees with itself — the index with the records, and each amendment with both of its ends.

.github#266 (gates that fail open), ADR-0034 §4 (a rule stated in six places drifts in five of them).

WHY THIS GATE EXISTS, WHEN NOTHING ELSE IN THE ORG IS UNGATED
  This org gates everything: contract-coherence, pin-coherence, skill-registry-coherence,
  timeout-coherence, reusable-job-id-coherence, coordination-coherence. Every registry
  (repos.yml, skills.yml, dependencies.yml) carries a schemaVersion, a validator and a
  generated projection.

  The ADR corpus carried none of it — 37 hand-typed records and a hand-typed index, with no
  check comparing them. It drifted exactly the way an ungated registry always drifts:

    * FIVE records said `Accepted` in the file and `Proposed` in the index. Commit c08ebce
      ("advance five shipped ADRs to Accepted") rewrote the five bodies and never touched the
      table. Its own message boasted that it "clears the 0028(Accepted)-builds-on-0022(Proposed)
      inversion" — and in the index, the inversion was still there. Those five included the
      records for the FS.GG.Game extraction and the FS.GG.Audio onboarding: two of the seven
      components, filed to every reader as unratified proposals.

    * SEVEN of sixteen amendment links were ONE-SIDED. The amended record did not know it had
      been amended, so the correction lived only in the index row. A reader who opened ADR-0015
      directly — the natural thing to do — read a §3 procedure that ADR-0037 had proved CANNOT
      EXIST, with nothing on the page to say so. `registry/dependencies.yml` even carried a
      comment asserting the fix had been made. It had not.

    * Because corrections landed in the TABLE instead of the RECORDS, the Title column became
      an abstract layer. ADR-0001's row is 123 characters; ADR-0038's had grown to 2,385. The
      index was becoming a second copy of the corpus — which is ADR-0034's "projection family"
      defect, in the corpus that named it.

  ADR-0034 line 32 lists the six places a rule gets restated, and the list BEGINS with "the ADR".
  Its §4 then made the docs and skills generated projections to retire that drift class by
  construction — and left the ADR layer, the first item on its own list, hand-written. This gate
  is that decision, applied to the layer it forgot.

WHAT IT ASSERTS
  1. STATUS AGREEMENT — every record's own `**Status:**` matches its row in the index. Neither
     end is privileged: they must simply agree. (The five-row rot was the index being stale; the
     0018 rot was the record being stale. A gate that trusted either one would have missed one.)
  2. BIDIRECTIONAL AMENDMENT — if ANY record declares that A amends/supersedes B, then A's file
     must mention B *and* B's file must mention A. A one-sided link is the corpus's most common
     defect and the most damaging, because it misleads precisely the reader who did the right
     thing and opened the record instead of the summary. A field naming N records declares N
     links: the scan runs to the end of the CLAUSE, not to the first `.` (.github#1637).
  3. NO ORPHANS, NO GHOSTS — every file has an index row, every row has a file (or is an explicit
     `~~NNNN~~` tombstone: withdrawn numbers are retired, not reused).
  4. SHAPE — every record carries Status, Date, Affects, Context, Decision, Consequences.

WHAT IT DOES NOT ASSERT
  Not whether a decision is GOOD, not whether it shipped, not whether the prose is current. A
  record can be coherent and wrong. This gate only proves the corpus does not CONTRADICT ITSELF —
  which is the class of defect no reviewer catches, because catching it means reading 37 files at
  once and diffing them against a table.

EXIT CODES (the contract; nothing greps this script's prose)
  0  coherent
  1  finding — a real incoherence, named, with the file and line
  3  NO VERDICT (permanent) — the corpus could not be read. An empty ADR set counts: this repo has
     had ADRs since 2026-06-27, so "I found none" means the path or the parse is broken, not that
     the corpus is clean. Examining nothing is a failure to audit, not a clean audit (#266).
"""

import argparse
import re
import sys
from pathlib import Path

DEFAULT_ADR_DIR = Path(__file__).resolve().parent.parent / "docs" / "adr"

# A record's own status line:  - **Status:** Accepted — §1 superseded by …
STATUS_RE = re.compile(r"^-\s+\*\*Status:\*\*\s*(.+)$", re.M)
DATE_RE = re.compile(r"^-\s+\*\*Date:\*\*", re.M)
AFFECTS_RE = re.compile(r"^-\s+\*\*Affects:\*\*", re.M)

# An index row:  | [0039](0039-….md) | title | Status |
#           or:  | ~~0010~~ | *declined …* | **Withdrawn** |
ROW_RE = re.compile(r"^\|\s*(?:\[(\d{4})\]\([^)]+\)|~~(\d{4})~~)\s*\|(.*)\|([^|]*)\|\s*$", re.M)

# A declared amendment, from either end. Header fields (`- **Amends:** [ADR-0022] §Decision 1`)
# and status lines (`superseded by [ADR-0032]`, `amended by [ADR-0036]`) both count: the whole
# point is that the two ends must agree, so we accept the claim from whichever end made it.
#
# A DECLARATION IS A KEYWORD PLUS EVERY TARGET IN ITS CLAUSE — NOT JUST THE FIRST.
#
# This used to be one regex, `<keyword>[^.\n]*?(?:ADR-)?(\d{4})`: a single capture group, whose
# span stopped at the first `.`. Every ADR cross-reference in this corpus is a markdown link
# whose target ends in `.md`, so in a field naming three records the FIRST link's `.md` closed
# the scan and the other two were never declared at all (.github#1637, measured on ADR-0067:
# a three-target `**Amends:**` field yielded `['0011']`). That is a silent FAIL-OPEN of assertion
# 2 — the corpus's most damaging defect class going unreported over a region the gate never
# examined, which is #266's shape inside the gate written to close it.
#
# So the scan is split in three, and each part earns its own boundary.
#
# (a) THE KEYWORD that opens a declaration. Unchanged vocabulary; it no longer swallows a number.
DECLARES_RE = re.compile(
    r"\*\*(?:Amends|Supersedes|Amended by|Superseded by|Extends|Extended by):\*\*|"
    r"(?:amends|supersedes|superseded by|amended by|extends|extended by)",
    re.I,
)

# (b) WHERE THE CLAUSE ENDS. A sentence-ending `.` or a `;` closes it; the end of the line always
#     does. `,` / `and` / `&` do NOT — that is precisely how a multi-target field separates its
#     targets. "Sentence-ending" means a `.` or `;` followed by whitespace or end-of-line, with
#     markdown/quote closers allowed in between (`only;**`, `nothing.**`). A `.` inside `.md)`,
#     `.NET`, `§4.4` or `FS.GG.SDD` is followed by a letter or digit and is NOT a boundary — which
#     is the whole bug. The boundary matters: ADR-0065's header reads
#     `**Amends:** [ADR-0014](…) Decision 5; interacts with [ADR-0019](…) and [ADR-0062](…)`, and
#     *interacts with* is not an amendment. Widening that trades a fail-open for a fail-loud on a
#     clean corpus, so the `;` stops it.
CLAUSE_END_RE = re.compile(r"[.;](?=[*_`\"')\]]*(?:\s|$))")

# (c) WHAT COUNTS AS A TARGET inside the clause. `ADR-0022`, a bare `0018` (ADR-0026's title says
#     "extends 0018"), a `[0019]` link label, or a `(0019-….md)` link target. Explicitly NOT: an
#     issue number (`#1636`), a URL path segment (`/issues/1636`), a section reference (`§0001`),
#     a date (`2026-07-28` — the `-0` after it), or four digits inside a longer word such as a
#     digest. Numbers that survive this are still filtered against the corpus by the caller, but
#     a scan widened from one target to all of them must not start reading dates as records.
REF_RE = re.compile(r"\((\d{4})-[^)\s]*\.md\)|(?<![\w#/§-])(?:ADR-)?(\d{4})(?![\w-])")


def declared_targets(text: str):
    """Yield every ADR number declared as an amendment target anywhere in `text`.

    A field naming N records yields N numbers, whatever separates them.
    """
    for kw in DECLARES_RE.finditer(text):
        eol = text.find("\n", kw.end())
        clause = text[kw.end(): eol if eol != -1 else len(text)]
        stop = CLAUSE_END_RE.search(clause)
        if stop:
            clause = clause[: stop.start()]
        for ref in REF_RE.finditer(clause):
            yield ref.group(1) or ref.group(2)

# The four statuses the corpus uses. Anything else is a typo, and a typo'd status is a status
# nobody can gate on.
KNOWN = {"accepted", "proposed", "withdrawn", "superseded"}


def normalize_status(raw: str) -> str:
    """First meaningful word, stripped of markdown. `**Superseded in part by [ADR-0032]**` -> `superseded`."""
    s = re.sub(r"[*~`\[\]]", "", raw).strip().lower()
    m = re.match(r"([a-z]+)", s)
    return m.group(1) if m else ""


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    ap.add_argument(
        "--dir",
        type=Path,
        default=DEFAULT_ADR_DIR,
        help="ADR directory to check (default: docs/adr/). The fixture points this at a "
             "synthetic corpus — a gate that cannot be shown to say NO is not a gate.",
    )
    args = ap.parse_args()

    adr_dir: Path = args.dir
    index = adr_dir / "README.md"
    findings: list[str] = []

    if not adr_dir.is_dir() or not index.is_file():
        print(f"NO VERDICT: {adr_dir} or its README.md is not readable", file=sys.stderr)
        return 3

    files = sorted(p for p in adr_dir.glob("[0-9][0-9][0-9][0-9]-*.md"))
    if not files:
        print("NO VERDICT: found no ADR files. This repo has had ADRs since 2026-06-27 — "
              "an empty corpus means the parse or the path is broken, not that it is clean.",
              file=sys.stderr)
        return 3

    # THE INDEX TABLE IS THE REGION BEFORE THE FIRST `## ` SECTION, and nothing after it.
    #
    # README.md carries a second table — the Supersession map — whose rows also begin `| [0002](…)`.
    # Parsed naively, its last cell ("Constitution ownership — the open P0 gate is closed") reads as
    # a STATUS, and the gate reports fourteen findings against a corpus that is fine. It did exactly
    # that the first time it met the section. The status table is the one BEFORE the first heading;
    # everything after a `## ` is commentary, and commentary is not the register.
    index_text = index.read_text(encoding="utf-8")
    index_table = re.split(r"^## ", index_text, maxsplit=1, flags=re.M)[0]

    rows = {}
    for m in ROW_RE.finditer(index_table):
        num = m.group(1) or m.group(2)
        rows[num] = {
            "status": normalize_status(m.group(4)),
            "tombstone": m.group(2) is not None,
            "line": index_table[: m.start()].count("\n") + 1,
        }
    if not rows:
        print("NO VERDICT: parsed zero rows out of the index table. The table's shape changed and "
              "this gate can no longer read it — which must fail LOUD, not read as 'all agree'.",
              file=sys.stderr)
        return 3

    bodies = {}
    statuses = {}
    for f in files:
        num = f.name[:4]
        text = f.read_text(encoding="utf-8")
        bodies[num] = text

        # (4) SHAPE
        sm = STATUS_RE.search(text)
        if not sm:
            findings.append(f"{f.name}:1 — no `- **Status:**` line. Every record must declare one.")
            continue
        statuses[num] = normalize_status(sm.group(1))
        if statuses[num] not in KNOWN:
            findings.append(
                f"{f.name}:{text[:sm.start()].count(chr(10)) + 1} — status "
                f"`{statuses[num]}` is not one of {sorted(KNOWN)}."
            )
        for label, rx in (("Date", DATE_RE), ("Affects", AFFECTS_RE)):
            if not rx.search(text):
                findings.append(f"{f.name}:1 — missing `- **{label}:**` header field.")
        for section in ("## Context", "## Decision", "## Consequences"):
            if section not in text:
                findings.append(f"{f.name}:1 — missing `{section}` section.")

        # (3) NO ORPHANS
        if num not in rows:
            findings.append(
                f"{f.name}:1 — the record exists but has NO ROW in docs/adr/README.md. "
                f"A record nobody can navigate to is a record nobody reads."
            )

    # (3) NO GHOSTS
    for num, row in rows.items():
        if num not in bodies and not row["tombstone"]:
            findings.append(
                f"docs/adr/README.md:{row['line']} — row [{num}] points at a file that does not "
                f"exist. If the record was withdrawn, keep it as a `~~{num}~~` tombstone: "
                f"withdrawn numbers are retired, not reused."
            )

    # (1) STATUS AGREEMENT
    for num, file_status in sorted(statuses.items()):
        row = rows.get(num)
        if not row or row["tombstone"]:
            continue
        if row["status"] != file_status:
            findings.append(
                f"docs/adr/README.md:{row['line']} — ADR-{num} status DISAGREES: the record says "
                f"`{file_status}`, the index says `{row['status']}`. Neither end is authoritative; "
                f"they must agree. (This is the c08ebce defect: five records were advanced to "
                f"Accepted in the files and the table was never touched.)"
            )

    # (2) BIDIRECTIONAL AMENDMENT
    declared: set[tuple[str, str]] = set()
    for num, text in bodies.items():
        for other in declared_targets(text):
            if other != num and (other in bodies or other in rows):
                declared.add(tuple(sorted((num, other))))

    for a, b in sorted(declared):
        a_knows_b = a in bodies and re.search(rf"\b{b}\b", bodies[a])
        b_knows_a = b in bodies and re.search(rf"\b{a}\b", bodies[b])
        if a_knows_b and not b_knows_a and b in bodies:
            findings.append(
                f"docs/adr/{Path([p for p in files if p.name.startswith(b)][0]).name}:1 — "
                f"ONE-SIDED LINK: ADR-{a} declares an amendment/supersession relation with "
                f"ADR-{b}, and ADR-{b} never mentions ADR-{a}. The reader who opens ADR-{b} "
                f"directly — the one doing the right thing — is the one who gets misled. Add the "
                f"marker to ADR-{b} (ADR-0021's header banner is the house form)."
            )
        elif b_knows_a and not a_knows_b and a in bodies:
            findings.append(
                f"docs/adr/{Path([p for p in files if p.name.startswith(a)][0]).name}:1 — "
                f"ONE-SIDED LINK: ADR-{b} declares an amendment/supersession relation with "
                f"ADR-{a}, and ADR-{a} never mentions ADR-{b}. Add the marker to ADR-{a}."
            )

    if findings:
        print(f"adr-coherence: {len(findings)} finding(s)\n", file=sys.stderr)
        for f in findings:
            print(f"  {f}\n", file=sys.stderr)
        return 1

    print(f"adr-coherence: OK — {len(bodies)} records, {len(rows)} index rows, "
          f"{len(declared)} amendment link(s), all bidirectional.")
    return 0


if __name__ == "__main__":
    sys.exit(main())
