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
  5. EXECUTION-STATE AGREEMENT — if an amending ADR marks one of its own `**§N — …**` clauses
     `EXECUTED`, then every record it amends, and every index row for that amendment, must say so
     too: carry an `EXECUTED` marker naming the amender, or cite a §N of the amender that has NOT
     executed. Silence is a finding. (.github#1703 — see below.)

WHAT ASSERTION 5 IS FOR, AND WHY IT IS SHAPED AS A REQUIRED MARKER RATHER THAN A FORBIDDEN PHRASE
  ADR-0067 §5 retired `.codex/skills`. `.github#1636` executed it on 2026-07-28 and amended ADR-0065
  in the same change — its header, its §Decision, and index rows 88/114/130. It did NOT touch ADR-0011
  or ADR-0014, the other two records ADR-0067 amends, and it did not touch their index rows. For a day
  the corpus said both things at once: row 130 said EXECUTED 2026-07-28, and row 109 said *"D1 still
  governs today"*. The reader who opened ADR-0011 to ask *"how many roots does a materializer write?"*
  was told **three**, by a record that told them it was in force.

  Assertions 1-4 were all green over that corpus. The amendment link ADR-0011 <-> ADR-0067 is present
  and two-sided; assertion 2 is satisfied by a link that says nothing about whether the amendment has
  HAPPENED. That is the .github#266 shape — a check that passes because it never looked at the thing
  that was wrong — inside the gate written to close it, for the second time (.github#1637 was the
  first).

  The obvious check is the one this gate does NOT implement: forbid *"still governs today"* / *"direction
  only"* beside an EXECUTED note. It was considered and rejected, because it gates the TENSE of a
  hand-written English sentence, and this corpus deliberately QUOTES its own falsified sentences as
  history — ADR-0011's amendment-history line quotes *"its three-root union is still the rule today"*
  verbatim, on purpose, and `.github#1706` was filed to stop workers "correcting" dated historical
  measurements into the present tense. A gate that reds on a record for recording its own history
  teaches workers to stop recording it, and it still misses every author who writes "carried out" where
  the vocabulary expected "EXECUTED".

  So the check is inverted into a POSITIVE one, and every input is DERIVED rather than restated
  (ADR-0058):

    * WHICH CLAUSES EXECUTED is read from the amending record's OWN `**§N — …**` sections — the one
      authoritative home of that fact. Nothing else declares it.
    * WHICH RECORDS ARE AMENDED is the amendment graph assertion 2 already computes.
    * The amended record must then carry the `EXECUTED` token in a note naming the amender. The token
      is the corpus's existing convention (ADR-0065:8, ADR-0067:63, README row 130), not a new field.

  A record legitimately amended only by a clause that has NOT executed says so by citing that clause:
  `§6` is untouched by `§5`'s execution, and a note that cites `§6` is clean. That escape hatch is
  also the fix for the smaller defect underneath this one — more than half of the corpus's amendment
  notes never named WHICH clause of the amender they were about, so a reader could not tell whether an
  execution falsified them. Under assertion 5 they have to.

WHAT IT DOES NOT ASSERT
  Not whether a decision is GOOD, not whether it shipped, not whether the prose is current. A
  record can be coherent and wrong. This gate only proves the corpus does not CONTRADICT ITSELF —
  which is the class of defect no reviewer catches, because catching it means reading 37 files at
  once and diffing them against a table.

  Assertion 5 is not an exception to that. It asserts that an execution recorded in ONE place is
  recorded in the OTHERS; it does not read the repo to find out whether anything actually shipped, and
  a corpus that unanimously and wrongly says EXECUTED is coherent by this gate. Nor does it judge the
  PROSE around the marker: ADR-0067's §9 phase-4 note asserted, an hour after the flip, that
  *"ADR-0065's root set remain[s] amended in direction only"* while ADR-0065's own header said
  EXECUTED. Assertion 5 does not see that sentence, and closing that gap would mean gating tense —
  see above. That example is HISTORY: the sentence was corrected by hand on .github#1709 — with two
  more in the same record that scoped "direction only" over the root set as well as over §6 — and the
  note there records what it used to say. Cited by SECTION rather than by line, because the line had
  already drifted 139 -> 205 while this paragraph went on naming :139 — by then the blank line above
  §7, an unrelated clause.

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

# ---------------------------------------------------------------- assertion 5: execution state
#
# An amending record declares its own clauses as `**§5 — The end state has TWO runtime roots…**`.
# That heading is the section key, and the EXECUTED marker inside the section is the fact.
SECTION_HEAD_RE = re.compile(r"^\*\*§(\d+)\s*[—-]", re.M)

# The corpus's execution token, and it is CASE-SENSITIVE: `**EXECUTED 2026-07-28 ([#1636](…)).**`.
# All three sites that record an execution today write it in caps (ADR-0065:8, ADR-0067:63,
# README row 130) — it is a declaration, in the same way `**Status:** Accepted` is a declaration
# drawn from a fixed vocabulary this gate already requires verbatim.
#
# Case-insensitive was tried first and is WRONG, measured on this corpus: it matched
# `already executed the reclassification` in ADR-0033's Affects line and `the reciprocal amendment
# markers when executed` in ADR-0056:83 — ordinary prose, in records that have executed nothing —
# and reported three findings and three index findings against records that are fine. A marker that
# ordinary English can utter by accident is not a marker.
#
# The trade is named rather than hidden: an author who writes `Executed 2026-07-28` in a note gets
# no credit for it and the gate reports the record as silent. That is a FALSE ALARM, which this
# corpus can afford — the failure mode is a worker being told to capitalise a word. The
# case-insensitive alternative's failure mode was the reverse.
EXECUTED_RE = re.compile(r"\bEXECUTED\b")

# A citation of the AMENDER's clause, inside a note: `§5`, `§6`. `§Retiring a root` and `§Decision`
# are section names, not numbers, and are deliberately not citations for this purpose — a numbered
# clause is what the amender's own headings key on.
SECTION_CITE_RE = re.compile(r"§(\d+)\b")


def notes(text: str):
    """Split a record into NOTES: the unit an amendment marker is written in.

    A note is either one ordinary line (a `- **Amended by:** …` header field, an index row) or one
    maximal run of blockquote lines (`   > **AMENDED 2026-07-28 …**`, which wraps across four or five
    of them). Splitting per-LINE instead would tear a blockquote note in half and lose the `§5` on
    its first line from the `EXECUTED` on its third.

    Yields (line_number, note_text).
    """
    lines = text.splitlines()
    i = 0
    while i < len(lines):
        if re.match(r"^\s*>", lines[i]):
            start = i
            while i < len(lines) and re.match(r"^\s*>", lines[i]):
                i += 1
            yield start + 1, "\n".join(lines[start:i])
        else:
            yield i + 1, lines[i]
            i += 1


def refs(text: str):
    """Every ADR number named anywhere in `text` (not clause-scoped — a note is already the scope)."""
    return {m.group(1) or m.group(2) for m in REF_RE.finditer(text)}


def executed_sections(num: str, text: str) -> set[str]:
    """The clauses of ADR-`num` that ITS OWN body marks EXECUTED.

    Two shapes, and the difference is what anchors the marker to this record rather than another:

      * The record numbers its clauses `**§N — …**` (ADR-0067). The heading IS the anchor, so any
        EXECUTED marker inside a section belongs to that section — even though ADR-0067 §5's note
        also names ADR-0065 (*"ADR-0065 was amended in the same change"*). Text BEFORE the first
        heading is deliberately ignored: that is the header block, and an `EXECUTED` marker there
        is in an `- **Amended by:**` field, which records what SOMEONE ELSE executed. The record
        that executed it will be found as its own amender.
      * The record numbers nothing. Then the only unambiguous self-execution is a note that names
        no OTHER record — because the overwhelmingly common note that says EXECUTED is the one an
        AMENDED record carries about its amender (`- **Amended by:** [ADR-0067] §5 — EXECUTED …`,
        ADR-0065:8). Counting that as ADR-0065 executing would make every amended record an amender
        and cascade a demand for markers nobody owes. Reported as `*`, which no §-citation dodges.
    """
    heads = list(SECTION_HEAD_RE.finditer(text))
    if not heads:
        for _, note in notes(text):
            if EXECUTED_RE.search(note) and not (refs(note) - {num}):
                return {"*"}
        return set()
    done = set()
    for n, head in enumerate(heads):
        end = heads[n + 1].start() if n + 1 < len(heads) else len(text)
        if EXECUTED_RE.search(text[head.end(): end]):
            done.add(head.group(1))
    return done


def acknowledges_execution(text: str, amender: str, done: set[str]) -> bool:
    """Does `text` record the execution state of `amender`'s executed clauses?

    Clean if ANY note naming the amender either carries the EXECUTED token, or cites a numbered
    clause of the amender that is NOT in `done` (an amendment by an unexecuted clause is untouched
    by the execution, and saying WHICH clause is how a record proves that rather than asserting it).
    """
    for _, note in notes(text):
        if amender not in refs(note):
            continue
        if EXECUTED_RE.search(note):
            return True
        cited = {m.group(1) for m in SECTION_CITE_RE.finditer(note)}
        if cited and not (cited & done):
            return True
    return False


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

    # (5) EXECUTION-STATE AGREEMENT
    #
    # The amender's own numbered clauses say WHICH parts have executed; the amendment graph built for
    # assertion 2 says WHO must know. Both are derived — nothing here reads a field a human maintains
    # for this check's benefit (ADR-0058).
    executed = {num: executed_sections(num, text) for num, text in bodies.items()}

    for a, b in sorted(declared):
        for amender, amended in ((a, b), (b, a)):
            done = executed.get(amender)
            if not done or amended not in bodies:
                continue
            if acknowledges_execution(bodies[amended], amender, done):
                continue
            clauses = "the record" if done == {"*"} else "§" + ", §".join(sorted(done))
            findings.append(
                f"docs/adr/{[p for p in files if p.name.startswith(amended)][0].name}:1 — "
                f"EXECUTION STATE UNRECORDED: ADR-{amender} marks {clauses} EXECUTED in its own "
                f"body, and ADR-{amended} — which it amends — records no execution state for it. "
                f"The reader who opens ADR-{amended} is told an amendment is pending that has "
                f"already happened. Either carry the `EXECUTED` marker in the note that names "
                f"ADR-{amender} (ADR-0065:8 is the house form), or, if this record is amended only "
                f"by a clause that has NOT executed, cite that clause: a note naming `§N` for an "
                f"unexecuted N is clean. (.github#1703)"
            )

    # …and the index rows say the same thing, or they are the half of the corpus that lies. Row 130
    # was flipped to EXECUTED by #1636 and row 109 was not, so the table disagreed WITH ITSELF about
    # whether one flip had happened. A supersession row is `| amended | § | amender | what changed |`.
    for n, line in enumerate(index_text.splitlines(), start=1):
        cells = [c.strip() for c in line.strip().strip("|").split("|")] if line.strip().startswith("|") else []
        if len(cells) < 4:
            continue
        subject = refs(cells[0])
        if len(subject) != 1:
            continue
        amended = subject.pop()
        for amender in sorted(refs(cells[2])):
            done = executed.get(amender)
            if not done or amender == amended:
                continue
            if acknowledges_execution(line, amender, done):
                continue
            clauses = "the record" if done == {"*"} else "§" + ", §".join(sorted(done))
            findings.append(
                f"docs/adr/README.md:{n} — EXECUTION STATE UNRECORDED: this row says ADR-{amender} "
                f"amends ADR-{amended}, ADR-{amender} marks {clauses} EXECUTED, and the row does "
                f"not. An index that flips one row of a flip and not the others disagrees with "
                f"itself about whether the flip happened. (.github#1703)"
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
