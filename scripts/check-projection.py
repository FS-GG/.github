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
     WHOLE VERSION TOKEN, in that contract's row of the GENERATED "Contract version literals"
     table — `version` in the `version` column, `package-version` in the `package-version` column.

It also cross-checks the coherent ✅/❌ flag per row against the registry `coherent:` boolean,
since a projected-but-contradictory flag is the same self-contradiction class (review H3).

WHY A GENERATED REGION, NOT THE PROSE CELL (#1081 option 2, DECIDED 2026-07-17 — ADR-0044 / #527).
Assertion 2 used to read the literal from the hand-authored `Version` CELL of the "Versioned
contracts" table. That cell also carries the JUDGEMENT about what a release means, which #748
forbids `feed-autofix` to write — so a required gate demanding the literal there and a bot
forbidden to author there were jointly unsatisfiable, and the bot's flips were structurally red.
The literal is now the machine-owned half of the page: `scripts/generate-projections` emits it into
the `## Contract version literals` region from `registry/dependencies.yml`, so a registry flip
REGENERATES that region green rather than demanding a prose edit. This gate reads the literal from
that region; the prose `Version` cell stays human-owned and is no longer a version-literal subject.

Assertion 2 is deliberately narrow on BOTH axes (.github#268, epic #266 — fails-open gates):

  * WHOLE TOKEN, not substring. `str(val) in row` finds `0.4.0` inside `0.4.0-preview.1`, so a
    cell still describing the prerelease passed a check for the stable version. Observed: while
    reconciling the stable-channel train (#265) `fs-gg-ui-template` moved to `0.4.0` and the row
    still read `0.3.1-preview.1` over a held `0.3.0-preview.1` pin — green, because the row's
    prose happened to contain `0.4.0-preview.1`. A version literal must be bounded by something
    that cannot continue a version: not `[0-9A-Za-z_+-]`, and not a `.` that is itself followed
    by a segment (so `0.4.0.` at the end of a sentence matches, but `0.4.0.1` does not).

  * The literal's own COLUMN, not the whole row. Even the generated region names an owner and both
    scalars per row, so `package-version 0.4.0` must not satisfy a check for `version`'s literal by
    landing in the wrong column. Each field is read from its own cell.

Both narrowings FAIL CLOSED: a table whose `version`/`package-version`/`Coherent?` column cannot be
located, or a contract with no row in the generated region, is an error — never a silently skipped row.

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
    # #1081 option 2 (DECIDED 2026-07-17, ADR-0044 / #527): the version literal is now the MACHINE-OWNED
    # half of this page, split out of the hand-authored `Version` cell into the GENERATED `## Contract
    # version literals` region (emitted by scripts/generate-projections, held byte-for-byte by the
    # `projections` gate). This required gate reads the literal from THERE, not from the prose cell — so
    # a registry flip REGENERATES the region green rather than demanding a prose edit `feed-autofix` is
    # forbidden to make (#748). The prose `## Versioned contracts` `Version` cell stays human-owned and
    # is no longer a version-literal gate subject; only the `## Coherence state` flag (check 1+3) and this
    # generated region are gated against the registry.
    #
    # The literal now lives in its own `version` / `package-version` COLUMNS (one fact per cell) rather
    # than fused into one prose cell, so each is read from its own column. Same two narrowings as before,
    # both fail-closed: whole-token (not substring), and a MISSING column/row is an error, never a skip.
    cv_cols, cv_row_lines = _table(md, "Contract version literals")
    cv_rows = {cid: row for row in cv_row_lines if (cid := _first_cell_id(row))}
    ver_col = _require_col(cv_cols, "version", "Contract version literals", proj_path, errors)
    pkg_col = _require_col(cv_cols, "package-version", "Contract version literals", proj_path, errors)
    for c in doc.get("contracts") or []:
        cid = str(c.get("id", "")).strip()
        if not cid:
            continue
        row = cv_rows.get(cid)
        if row is None:
            errors.append(
                f"contract id {cid!r} has no row in the '{proj_path}' Contract version literals table "
                f"(the generated region is stale — run scripts/generate-projections).")
            continue
        # `version` is always present; `package-version` only when the contract ships a package (its
        # generated cell then reads `—`, which no version literal will match — and `val is None` skips it).
        for field, col in (("version", ver_col), ("package-version", pkg_col)):
            val = c.get(field)
            if val is None:
                continue
            if col is None:
                continue  # column absent — already reported by _require_col; do not silently pass
            cell = _cell(row, col)
            if not _token_present(str(val), cell):
                errors.append(
                    f"contract id {cid!r}: {field} literal {str(val)!r} does not appear as a whole "
                    f"version token in its {field!r} cell of the Contract version literals table "
                    f"(cell reads: {cell.strip()[:80]!r}) — the generated region is stale relative to "
                    f"the registry; run scripts/generate-projections.")

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
