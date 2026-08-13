#!/usr/bin/env python3
"""Fail closed when this repo's routed dispatch surface cannot resolve from this repo (.github#2510).

The board variants bind a host to an EXACT model *and effort* and refuse to dispatch at any other
route. On Claude Code the `Agent` tool carries `model` but has no `effort` parameter, so effort is
settable only through an agent definition's frontmatter — which means the routed variants are
dispatchable only if this repository ships the definitions itself. It did not: `.claude/agents/` did
not exist on `main`, the 2026-08-13 `drive-board-normal` run resolved `fsgg-worker-normal` and
`fsgg-critic-normal` from the operator's user-global `~/.claude/agents/`, and `fsgg-critic-best` —
named in `independent-review.md` — existed nowhere at all.

Three failure directions, all fail-closed:

1. COVERAGE. Every route the board variants define needs BOTH an `fsgg-worker-<route>` and an
   `fsgg-critic-<route>` definition, and every `fsgg-<role>-<route>` type any checked-in skill names
   as a literal string needs a definition too. A skill that names a new type without shipping its
   definition reds here instead of merging.
2. ROUTE FIDELITY. Each definition's frontmatter `model`/`effort` must equal the Claude Code row of
   the routing table its route's skills mandate, and the variants sharing a route suffix must agree
   with each other. A drift between a variant's routing table and its agent definition is exactly the
   silent downgrade the "never let a host default choose the model or effort" rule exists to prevent.
   Repair-phase tables are resolved too: an escalated route naming a model/effort no ordinary route
   defines would leave the repair phase undispatchable.
3. TRIGGER. `.github/workflows/skill-quality.yml` — the workflow that runs this gate — must list
   `.claude/agents/**` in BOTH its `pull_request` and `push` path filters. Without it this gate would
   not run when its own subject changed, which is the fail-open class of epic #266: a definition-only
   edit could downgrade a route past a green board.

There is deliberately NO `.agents/` mirror. `.agent-skill-roots` declares exactly two SKILL roots and
`.agents/` contains only `skills`; agent definitions are a Claude Code surface with no second runtime
root, so no byte-identity obligation exists here and this gate must not invent one.

Usage: agent-definition-coverage.py [--root DIR]
"""

from __future__ import annotations

import argparse
import re
import sys
from pathlib import Path

import yaml

# The board variants that publish a routing table, and the route each one's ORDINARY table defines.
# The route name is the skill-name suffix; two variants sharing a suffix must publish the same row.
VARIANTS = (
    ("drive-board-normal", "normal"),
    ("work-board-normal", "normal"),
    ("drive-board-best", "best"),
    ("work-board-best", "best"),
)

ROLES = ("worker", "critic")

# `| Claude Code | `sonnet` | `high` |` — the runtime row every routing table carries. Codex's row is
# read too, but only Claude Code's binds an agent definition: Codex has no `.claude/agents` surface.
ROUTE_ROW = re.compile(r"^\|\s*Claude Code\s*\|\s*`([^`]+)`\s*\|\s*`([^`]+)`\s*\|\s*$", re.MULTILINE)

# A literal agent-type mention anywhere in a checked-in skill: `fsgg-worker-best`, `fsgg-critic-normal`.
# `<route>`/`<role>` placeholders never match, because `<` is not in the class.
NAMED_TYPE = re.compile(r"fsgg-(worker|critic)-([a-z0-9][a-z0-9-]*)")

WORKFLOW = ".github/workflows/skill-quality.yml"
AGENTS_TRIGGER = ".claude/agents/**"


class Finding(Exception):
    pass


def require(condition: bool, message: str) -> None:
    if not condition:
        raise Finding(message)


def skill_roots(root: Path) -> list[Path]:
    """The skill roots this tree declares, so a type named in EITHER runtime's copy is covered."""
    declaration = root / ".agent-skill-roots"
    require(declaration.is_file(), "no .agent-skill-roots declaration to read skill roots from")
    roots = [
        root / line.strip()
        for line in declaration.read_text(encoding="utf-8").splitlines()
        if line.strip() and not line.lstrip().startswith("#")
    ]
    require(bool(roots), ".agent-skill-roots declares no skill roots")
    return [path for path in roots if path.is_dir()]


def frontmatter(path: Path) -> dict:
    text = path.read_text(encoding="utf-8")
    require(text.startswith("---\n"), f"{path.name}: no leading YAML frontmatter block")
    end = text.find("\n---\n", 3)
    require(end != -1, f"{path.name}: frontmatter block is never closed")
    block = yaml.safe_load(text[4:end])
    require(isinstance(block, dict), f"{path.name}: frontmatter is not a mapping")
    return block


def routing_tables(root: Path) -> dict[str, tuple[str, str]]:
    """route name -> (model, effort), proven consistent across every variant that names it.

    Each variant publishes an ordinary table and a repair-phase table (or reuses its own). Every row
    read here is resolved: a repair-phase row whose (model, effort) matches no ordinary route would
    leave the repair phase pointing at a tier with no definitions behind it.
    """
    routes: dict[str, tuple[str, str]] = {}
    escalated: dict[str, tuple[str, str]] = {}
    for skill, route in VARIANTS:
        path = root / ".claude/skills" / skill / "SKILL.md"
        require(path.is_file(), f"routed board variant {skill} has no SKILL.md")
        rows = ROUTE_ROW.findall(path.read_text(encoding="utf-8"))
        require(bool(rows), f"{skill}/SKILL.md publishes no Claude Code routing row")
        model, effort = rows[0]
        if route in routes:
            require(
                routes[route] == (model, effort),
                f"{skill} routes '{route}' to {model}/{effort}, but another variant routes it to "
                f"{routes[route][0]}/{routes[route][1]}",
            )
        routes[route] = (model, effort)
        # The repair-phase table when the variant publishes a second one; otherwise it reuses its own.
        escalated[skill] = (rows[1][0], rows[1][1]) if len(rows) > 1 else (model, effort)
    for skill, row in escalated.items():
        require(
            row in routes.values(),
            f"{skill}'s repair-phase route {row[0]}/{row[1]} matches no route this repo defines, so "
            "the repair phase has no agent definitions to dispatch",
        )
    return routes


def main(argv: list[str]) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--root", default=str(Path(__file__).resolve().parents[2]))
    args = parser.parse_args(argv)
    root = Path(args.root).resolve()

    routes = routing_tables(root)

    # (1) coverage: every route needs both roles, and every literally-named type needs a definition.
    required = {f"fsgg-{role}-{route}": route for route in routes for role in ROLES}
    named: dict[str, list[str]] = {}
    for skills in skill_roots(root):
        for path in sorted(skills.rglob("*.md")):
            for role, route in NAMED_TYPE.findall(path.read_text(encoding="utf-8")):
                named.setdefault(f"fsgg-{role}-{route}", []).append(
                    str(path.relative_to(root))
                )
    for name, sources in sorted(named.items()):
        require(
            name in required,
            f"skill {sources[0]} names agent type '{name}', which belongs to no route the board "
            f"variants define (known routes: {', '.join(sorted(routes))})",
        )

    agents = root / ".claude/agents"
    require(agents.is_dir(), ".claude/agents/ does not exist, so no routed dispatch can resolve here")
    shipped = {path.stem: path for path in sorted(agents.glob("*.md"))}

    for name, route in sorted(required.items()):
        model, effort = routes[route]
        where = f" (named by {named[name][0]})" if name in named else ""
        require(
            name in shipped,
            f"no .claude/agents/{name}.md, so a '{route}'-routed dispatch cannot resolve{where}",
        )
        block = frontmatter(shipped[name])
        require(block.get("name") == name, f"{name}.md: frontmatter name is {block.get('name')!r}")
        require(
            isinstance(block.get("description"), str) and block["description"].strip(),
            f"{name}.md: frontmatter carries no description for the host to route on",
        )
        # (2) route fidelity.
        require(
            block.get("model") == model,
            f"{name}.md declares model {block.get('model')!r}, but the '{route}' routing table "
            f"mandates '{model}' — a host dispatching it would silently leave the mandated route",
        )
        require(
            block.get("effort") == effort,
            f"{name}.md declares effort {block.get('effort')!r}, but the '{route}' routing table "
            f"mandates '{effort}' — and effort is settable ONLY here on Claude Code",
        )

    for name in sorted(shipped):
        require(
            name in required,
            f".claude/agents/{name}.md defines '{name}', which no route and no checked-in skill "
            "names — an unreachable definition drifts unnoticed",
        )

    # (3) trigger: this gate must run when its own subject changes.
    workflow = root / WORKFLOW
    require(workflow.is_file(), f"{WORKFLOW} is missing, so nothing runs this gate")
    triggers = yaml.safe_load(workflow.read_text(encoding="utf-8"))
    # PyYAML resolves the bare key `on:` to the boolean True (YAML 1.1); accept either spelling.
    on = triggers.get("on", triggers.get(True))
    require(isinstance(on, dict), f"{WORKFLOW} has no parseable trigger block")
    for event in ("pull_request", "push"):
        paths = (on.get(event) or {}).get("paths") or []
        require(
            AGENTS_TRIGGER in paths,
            f"{WORKFLOW} does not list '{AGENTS_TRIGGER}' under {event}.paths, so a definition-only "
            "change would bypass this gate entirely",
        )

    print(
        f"agent-definition-coverage: {len(required)} definitions cover "
        f"{len(routes)} routes ({', '.join(sorted(routes))}) at their mandated model/effort, "
        f"{len(named)} skill-named types resolve, and the gate triggers on its own subject"
    )
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main(sys.argv[1:]))
    except Finding as finding:
        print(f"agent-definition-coverage: {finding}", file=sys.stderr)
        sys.exit(1)
