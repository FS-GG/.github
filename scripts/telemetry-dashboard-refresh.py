#!/usr/bin/env python3
"""Choose whether a public Host revision needs a Pages rebuild."""

import argparse
import json
import re


def refresh_decision(current: str | None, requested: str | None, deployed: str | None) -> dict[str, str]:
    for name, value in (("current", current), ("requested", requested), ("deployed", deployed)):
        if value and not re.fullmatch(r"[0-9a-f]{40}", value):
            if name == "deployed":
                if requested:
                    raise ValueError("invalid deployed host revision")
                deployed = None
            else:
                raise ValueError(f"invalid {name} host revision")
    if requested and requested != current:
        return {"build": "false", "reason": "stale-public-revision", "revision": ""}
    if requested and deployed == requested:
        return {"build": "false", "reason": "already-deployed", "revision": ""}
    return {"build": "true", "reason": "revision-needed" if requested else "ordinary-refresh", "revision": current or ""}


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--current")
    parser.add_argument("--requested")
    parser.add_argument("--deployed")
    args = parser.parse_args()
    print(json.dumps(refresh_decision(args.current, args.requested, args.deployed), sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
