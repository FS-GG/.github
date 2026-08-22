#!/usr/bin/env python3
"""Fail-closed non-vacuity check over one original VSTest TRX result."""

from __future__ import annotations

import argparse
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


def fail(message: str) -> int:
    print(f"trx-count: {message}", file=sys.stderr)
    return 1


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("trx")
    parser.add_argument("--minimum", type=int, required=True)
    parser.add_argument("--label", required=True)
    parser.add_argument("--summary-file")
    args = parser.parse_args()

    if args.minimum < 1:
        return fail("minimum must be positive")
    path = Path(args.trx)
    if not path.is_file():
        return fail(f"missing result: {path}")
    try:
        root = ET.parse(path).getroot()
    except (ET.ParseError, OSError) as error:
        return fail(f"unreadable result: {type(error).__name__}")

    counters = [element for element in root.iter() if element.tag.rsplit("}", 1)[-1] == "Counters"]
    if len(counters) != 1:
        return fail(f"expected exactly one Counters element, found {len(counters)}")
    values: dict[str, int] = {}
    for name in ("total", "executed", "passed", "failed", "error"):
        raw = counters[0].get(name)
        try:
            values[name] = int(raw) if raw is not None else -1
        except ValueError:
            return fail(f"Counters.{name} is not an integer")
        if values[name] < 0:
            return fail(f"Counters.{name} is missing or negative")

    if values["failed"] or values["error"]:
        return fail(f"result contains failed={values['failed']} error={values['error']}")
    if values["passed"] > values["executed"] or values["executed"] > values["total"]:
        return fail("Counters relation passed <= executed <= total is false")
    if values["passed"] < args.minimum:
        return fail(f"only {values['passed']} tests passed; minimum is {args.minimum}")

    message = f"{args.label}: {values['passed']} test(s) passed (one measured execution)"
    print(message)
    if args.summary_file:
        with open(args.summary_file, "a", encoding="utf-8") as stream:
            stream.write(message + "\n")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
