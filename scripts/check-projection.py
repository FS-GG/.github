#!/usr/bin/env python3
"""Gate the hand-maintained compatibility.md projection against the registry (.github#128, review H4).

docs/registry/compatibility.md is a hand-maintained projection of registry/dependencies.yml.
Nothing enforced the two stay in sync, so drift recurred silently: a coherence id shipped in
the registry but never got a projection row (the registry changelog records this exact class
for `agent-skill-mirror`, back-filled after the fact), and contract-row version literals fell
behind the registry scalars. This gate makes that drift a red check.

It asserts, at minimum (H4):
  1. Every registry `coherence[].id` has a row in compatibility.md's "Coherence state" table.
  2. Every registry `contracts[].id`'s `version` and `package-version` literal appears in that
     contract's row in the "Versioned contracts" table.

It also cross-checks the coherent ✅/❌ flag per row against the registry `coherent:` boolean,
since a projected-but-contradictory flag is the same self-contradiction class (review H3).

Pure-stdlib (PyYAML only, already a coherence-gate dependency); no network. Exit 0 = coherent.

Usage: scripts/check-projection.py [registry/dependencies.yml] [docs/registry/compatibility.md]
"""
from __future__ import annotations

import re
import sys

import yaml


def _rows_under(md: str, header: str) -> list[str]:
    """Return the data rows (as raw lines) of the first Markdown table after a `## <header>`.

    A table starts at the `| ... |` header line, whose next line is the `|---|` separator;
    data rows follow until the first non-table line.
    """
    lines = md.splitlines()
    # find the section header
    start = next((i for i, ln in enumerate(lines)
                  if ln.strip().lower() == f"## {header}".lower()), None)
    if start is None:
        return []
    # find the table header row (first pipe-row after the section header)
    i = start + 1
    while i < len(lines) and not lines[i].lstrip().startswith("|"):
        i += 1
    if i >= len(lines):
        return []
    # skip header row + separator row, then collect data rows
    i += 2
    rows: list[str] = []
    while i < len(lines) and lines[i].lstrip().startswith("|"):
        rows.append(lines[i])
        i += 1
    return rows


def _first_cell_id(row: str) -> str | None:
    """Extract a leading `` `id` `` from the first cell of a table row, else None."""
    m = re.match(r"\s*\|\s*`([^`]+)`", row)
    return m.group(1) if m else None


def main() -> int:
    reg_path = sys.argv[1] if len(sys.argv) > 1 else "registry/dependencies.yml"
    proj_path = sys.argv[2] if len(sys.argv) > 2 else "docs/registry/compatibility.md"

    doc = yaml.safe_load(open(reg_path, encoding="utf-8"))
    md = open(proj_path, encoding="utf-8").read()

    errors: list[str] = []

    # --- 1 + 3. Coherence ids projected, with matching flag -------------------------------
    coh_rows = {cid: row for row in _rows_under(md, "Coherence state")
                if (cid := _first_cell_id(row))}
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
        want = bool(entry.get("coherent"))
        cell = row.split("|")[2] if row.count("|") >= 2 else row  # the "Coherent?" column
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
    contract_rows = {cid: row for row in _rows_under(md, "Versioned contracts")
                     if (cid := _first_cell_id(row))}
    for c in doc.get("contracts") or []:
        cid = str(c.get("id", "")).strip()
        if not cid:
            continue
        row = contract_rows.get(cid)
        if row is None:
            errors.append(
                f"contract id {cid!r} has no row in the '{proj_path}' Versioned contracts table.")
            continue
        for field in ("version", "package-version"):
            val = c.get(field)
            if val is None:
                continue
            if str(val) not in row:
                errors.append(
                    f"contract id {cid!r}: {field} literal {str(val)!r} does not appear in its "
                    f"projection row (row is stale relative to the registry).")

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
