#!/usr/bin/env python3
"""Gate the hand-maintained compatibility.md projection against the registry (.github#128, review H4).

docs/registry/compatibility.md is a hand-maintained projection of registry/dependencies.yml.
Nothing enforced the two stay in sync, so drift recurred silently: a coherence id shipped in
the registry but never got a projection row (the registry changelog records this exact class
for `agent-skill-mirror`, back-filled after the fact), and contract-row version literals fell
behind the registry scalars. This gate makes that drift a red check.

It asserts, at minimum (H4):
  1. Every registry `coherence[].id` has a row in compatibility.md's "Coherence state" table.
  2. Every registry `contracts[].id`'s `version` and `package-version` literal appears, as a
     WHOLE VERSION TOKEN, in that contract's `Version` CELL in the "Versioned contracts" table.

It also cross-checks the coherent ✅/❌ flag per row against the registry `coherent:` boolean,
since a projected-but-contradictory flag is the same self-contradiction class (review H3).

Assertion 2 is deliberately narrow on BOTH axes (.github#268, epic #266 — fails-open gates):

  * WHOLE TOKEN, not substring. `str(val) in row` finds `0.4.0` inside `0.4.0-preview.1`, so a
    row still describing the prerelease passed a check for the stable version. Observed: while
    reconciling the stable-channel train (#265) `fs-gg-ui-template` moved to `0.4.0` and the row
    still read `0.3.1-preview.1` over a held `0.3.0-preview.1` pin — green, because the row's
    prose happened to contain `0.4.0-preview.1`. A version literal must be bounded by something
    that cannot continue a version: not `[0-9A-Za-z_+-]`, and not a `.` that is itself followed
    by a segment (so `0.4.0.` at the end of a sentence matches, but `0.4.0.1` does not).

  * The `Version` CELL, not the whole row. Rows carry kilobytes of `PRIOR …` release prose that
    legitimately names superseded versions, so "does this string occur anywhere in the row" is
    satisfied by history alone. Only the current-version cell is the projection of the registry
    scalar.

Both narrowings FAIL CLOSED: a table whose `Version`/`Coherent?` column cannot be located, or a
contract whose version cell is empty, is an error — never a silently skipped row.

Pure-stdlib (PyYAML only, already a coherence-gate dependency); no network. Exit 0 = coherent.

Usage: scripts/check-projection.py [registry/dependencies.yml] [docs/registry/compatibility.md]
"""
from __future__ import annotations

import re
import sys

import yaml


def _table(md: str, header: str) -> tuple[list[str], list[str]]:
    """Return `(column_names, data_rows)` of the first Markdown table after a `## <header>`.

    A table starts at the `| ... |` header line, whose next line is the `|---|` separator;
    data rows follow until the first non-table line. Returns `([], [])` if absent.
    """
    lines = md.splitlines()
    # find the section header
    start = next((i for i, ln in enumerate(lines)
                  if ln.strip().lower() == f"## {header}".lower()), None)
    if start is None:
        return [], []
    # find the table header row (first pipe-row after the section header)
    i = start + 1
    while i < len(lines) and not lines[i].lstrip().startswith("|"):
        i += 1
    if i >= len(lines):
        return [], []
    columns = [c.strip() for c in _cells(lines[i])]
    # skip header row + separator row, then collect data rows
    i += 2
    rows: list[str] = []
    while i < len(lines) and lines[i].lstrip().startswith("|"):
        rows.append(lines[i])
        i += 1
    return columns, rows


def _cells(row: str) -> list[str]:
    """Split a Markdown table row into cells, dropping the empty edges around the outer pipes.

    Splits on UNESCAPED pipes only: a cell may legitimately contain `\\|`. Getting this wrong
    shifts every later column by one, which — now that the version assertion is scoped to a
    single column — would be a spurious red rather than a harmless one.
    """
    parts = re.split(r"(?<!\\)\|", row.strip())
    if parts and not parts[0].strip():
        parts = parts[1:]
    if parts and not parts[-1].strip():
        parts = parts[:-1]
    return parts


def _require_col(columns: list[str], name: str, table: str, proj_path: str,
                 errors: list[str]) -> int | None:
    """Index of the required column `name` (case-insensitive), or None + a recorded error.

    The fail-closed half of epic #266: a column the gate cannot find means the assertion that
    reads it CANNOT RUN, which must be an error rather than a row the loop quietly skips. Callers
    must treat None as "stop checking this table", never as "nothing to check".

    `columns` is empty when the table itself is absent; that is already reported per-row as
    "has no row", so it is not double-reported here.
    """
    idx = next((i for i, c in enumerate(columns) if c.lower() == name.lower()), None)
    if columns and idx is None:
        errors.append(
            f"the '{proj_path}' {table} table has no {name!r} column "
            f"(found: {', '.join(columns) or 'none'}) — the assertion that reads it cannot run.")
    return idx


def _cell(row: str, idx: int) -> str:
    """The `idx`-th cell of `row`, or '' if the row is short."""
    cells = _cells(row)
    return cells[idx] if idx < len(cells) else ""


def _first_cell_id(row: str) -> str | None:
    """Extract a leading `` `id` `` from the first cell of a table row, else None."""
    m = re.match(r"\s*\|\s*`([^`]+)`", row)
    return m.group(1) if m else None


# A version literal continues through digits, letters, `-`, `_` and `+` (SemVer prerelease and
# build metadata), and through a `.` THAT IS FOLLOWED BY one of those. A match bounded by any of
# them is a PREFIX of a different version, not an occurrence of this one — which is exactly how
# `0.4.0` was found inside `0.4.0-preview.1`, and how a bare `1` would be found inside `1.2.0`.
#
# The `.` is conditional so that a cell ending a sentence — "… the pin is 0.4.0." — still matches
# 0.4.0. A trailing period is punctuation, not a fourth version segment. Getting this wrong is
# fail-closed (a spurious red on a correct row), but it is still wrong.
_CONTINUES = r"[0-9A-Za-z_+\-]"
_LOOKBEHIND = rf"(?<!{_CONTINUES})(?<![0-9A-Za-z]\.)"
_LOOKAHEAD = rf"(?!{_CONTINUES})(?!\.[0-9A-Za-z])"


def _token_present(value: str, text: str) -> bool:
    """True iff `value` occurs in `text` as a whole version token."""
    pattern = rf"{_LOOKBEHIND}{re.escape(value)}{_LOOKAHEAD}"
    return re.search(pattern, text) is not None


def main() -> int:
    reg_path = sys.argv[1] if len(sys.argv) > 1 else "registry/dependencies.yml"
    proj_path = sys.argv[2] if len(sys.argv) > 2 else "docs/registry/compatibility.md"

    doc = yaml.safe_load(open(reg_path, encoding="utf-8"))
    md = open(proj_path, encoding="utf-8").read()

    errors: list[str] = []

    # --- 1 + 3. Coherence ids projected, with matching flag -------------------------------
    coh_cols, coh_row_lines = _table(md, "Coherence state")
    coh_rows = {cid: row for row in coh_row_lines if (cid := _first_cell_id(row))}
    coh_flag_col = _require_col(coh_cols, "Coherent?", "Coherence state", proj_path, errors)
    for entry in doc.get("coherence") or []:
        cid = str(entry.get("id", "")).strip()
        if not cid:
            continue
        row = coh_rows.get(cid)
        if row is None:
            errors.append(
                f"coherence id {cid!r} has no row in the '{proj_path}' Coherence state table "
                f"(registry declares it; projection is missing it).")
            continue
        # cross-check the ✅ yes / ❌ no flag against registry `coherent:`
        if coh_flag_col is None:
            continue  # already reported above; do not silently pass the row
        want = bool(entry.get("coherent"))
        cell = _cell(row, coh_flag_col)
        has_yes = "✅" in cell or re.search(r"\byes\b", cell, re.I)
        has_no = "❌" in cell or re.search(r"\bno\b", cell, re.I)
        got = True if (has_yes and not has_no) else False if (has_no and not has_yes) else None
        if got is None:
            errors.append(f"coherence id {cid!r}: cannot read a ✅/❌ flag from its projection row.")
        elif got != want:
            errors.append(
                f"coherence id {cid!r}: registry says coherent={want} but projection row shows "
                f"{'✅ yes' if got else '❌ no'}.")

    # --- 2. Contract version literals projected -------------------------------------------
    con_cols, con_row_lines = _table(md, "Versioned contracts")
    contract_rows = {cid: row for row in con_row_lines if (cid := _first_cell_id(row))}
    version_col = _require_col(con_cols, "Version", "Versioned contracts", proj_path, errors)
    for c in doc.get("contracts") or []:
        cid = str(c.get("id", "")).strip()
        if not cid:
            continue
        row = contract_rows.get(cid)
        if row is None:
            errors.append(
                f"contract id {cid!r} has no row in the '{proj_path}' Versioned contracts table.")
            continue
        if version_col is None:
            continue  # already reported above; do not silently pass the row
        cell = _cell(row, version_col)
        if not cell.strip():
            errors.append(f"contract id {cid!r}: its projection row has an empty Version cell.")
            continue
        for field in ("version", "package-version"):
            val = c.get(field)
            if val is None:
                continue
            if not _token_present(str(val), cell):
                errors.append(
                    f"contract id {cid!r}: {field} literal {str(val)!r} does not appear as a whole "
                    f"version token in its projection row's Version cell (cell reads: "
                    f"{cell.strip()[:80]!r}) — the row is stale relative to the registry.")

    if errors:
        for e in errors:
            print(f"::error::projection-drift: {e}", file=sys.stderr)
        print(f"\n{len(errors)} projection drift(s) between {reg_path} and {proj_path}.",
              file=sys.stderr)
        return 1

    ncoh = len(doc.get("coherence") or [])
    ncon = len(doc.get("contracts") or [])
    print(f"ok: {proj_path} projects all {ncoh} coherence ids (flags match) and all {ncon} "
          f"contract version literals from {reg_path}.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
