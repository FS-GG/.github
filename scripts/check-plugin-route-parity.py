#!/usr/bin/env python3
"""Assert the fsgg-routes plugin's declared routes match the board variants' route tables.

.github#2230 AC3, standing up the plugin decided on .github#2203 (Option 2 — an FS-GG plugin
distributed by marketplace, payload bounded to route agent definitions only).

THE DEFECT THIS CLOSES
  `<runtime, model, effort>` is written down TWICE and nothing compares the copies:

    * `.claude/skills/{drive,work}-board-{best,normal}/SKILL.md` — a markdown table per variant, which
      is what a HOST reads before it spawns anything.
    * `plugins/fsgg-routes/agents/*.md` — YAML frontmatter `model:` / `effort:`, which is what the
      RUNTIME actually applies when the subagent starts.

  That is #485's shape exactly: computed in two places, agrees in one at best. The failure is silent
  and it is the expensive direction — a host that reads `opus`/`high` from the table, dispatches
  `fsgg-worker-best`, and gets whatever the frontmatter happens to say, has downgraded the route
  without anyone being told. `drive-board-best` is explicit that downgrading is never acceptable
  ("do not downgrade, fall back, or continue a partial wave"), so a disagreement here defeats a rule
  the skills state but cannot enforce.

  #2203's AC3 is that the triple be authored ONCE in `src/FS.GG.Coord.Core/Protocol.fs` and projected.
  That projection is its own slice and it is blocked on this one — there was nothing to project into
  until the definitions had a final home. **This checker is the interim, and .github#2230 says so:
  "this AC is met by a checker that compares the two, and that checker is retired by the projection
  slice."** Delete it when `scripts/generate-projections` owns the route tables.

WHAT IT ASSERTS
  1. ORDINARY ROUTE PARITY. Each variant's Claude Code row equals the frontmatter of BOTH definitions
     at that variant's tier — the worker and the critic. The tables say "pass this route explicitly to
     every subagent spawn", one rule covering both roles, so both are checked against it.

  2. REPAIR-PHASE ROUTE PARITY — and this is the assertion that encodes a live finding. #2203 and
     .github#2230 were written when the escalated route was a fifth definition, `fsgg-worker-repair`,
     at `opus`/`high`. That definition does not exist and is SUPERSEDED, not merely unbuilt: the
     `-best` variants state their repair route is identical to their ordinary route ("the escalation is
     the fresh attempt and the higher round ceiling, not a stronger model"), and the `-normal` variants
     name `-best`'s route. So every variant's repair-phase route must equal the `-best` tier's route.
     If someone reintroduces a separate escalated tier, this is where it surfaces.

  3. ONE HOME (.github#2230 item 5 / AC2). A route definition must NOT also exist under
     `.claude/agents/`. Two copies is the defect this repo files most (#485, #865), and a plugin that
     shadows a loose file is worse than either alone because which one wins is invisible.

  4. THE #2203 BINDING CONSTRAINT, enforced rather than commented. The MARKETPLACE source in
     `.claude/settings.json` must be `github`. A `directory` source installs IN PLACE — `claude plugin
     marketplace add <path>` records an `installLocation` equal to the path given — so the marketplace
     IS the checkout, and every worktree resolves back to the one main checkout: a shared mutable
     resource under a tree whose repairs the host serialises (#1549, #1663). The PLUGIN source inside
     the catalog is a DIFFERENT field and a relative `./plugins/fsgg-routes` is correct there, because
     it resolves against the fetched marketplace copy. Asserting both directions is what keeps a later
     reader from "simplifying" one into the other.

     This lives in a gate instead of a comment because `.claude/settings.json` cannot safely carry one:
     `claude --help` states settings files that fail validation are SILENTLY ignored in non-interactive
     mode, that file also carries this repo's push-guard hook, and a `"//"` key is reported as an
     unrecognized field. See `plugins/fsgg-routes/README.md`.

  5. WIRING. `enabledPlugins` names `<plugin.json name>@<marketplace.json name>`, and the relative
     plugin source resolves to a directory that actually holds the plugin manifest.

FAILS CLOSED (epic #266). Every subject is read through `lib.gate.read_text`, so a missing skill, an
unparsable manifest, or a route table with no runtime row is a NO-VERDICT (exit 3), never a green. A
table this cannot find is the case that would silently check nothing, so it is an error rather than a
skip.

The one deliberate exception is an ABSENT agent definition, which is a FINDING (exit 1) rather than a
no-verdict: the gate has reached a verdict there — a route table naming a definition that does not
exist is the whole defect, already diagnosed. The distinction that decides it is whether the gate is
blind or the tree is broken; only the first is a no-verdict.
"""

from __future__ import annotations

import json
import os
import re
import sys
from pathlib import Path

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lib.gate import GateError, base_parser, read_text, report_findings, report_ok, run  # noqa: E402

NAME = "check-plugin-route-parity"

PLUGIN_DIR = "plugins/fsgg-routes"
MARKETPLACE_JSON = ".claude-plugin/marketplace.json"
PLUGIN_JSON = f"{PLUGIN_DIR}/.claude-plugin/plugin.json"
AGENTS_DIR = f"{PLUGIN_DIR}/agents"
LOOSE_AGENTS_DIR = ".claude/agents"
SETTINGS_JSON = ".claude/settings.json"

# The runtime whose route this repo dispatches. The tables also carry a Codex row; this gate judges the
# Claude Code row because that is the runtime the plugin's frontmatter can express (`model:`/`effort:`).
RUNTIME = "Claude Code"

# variant skill -> tier. The tier names the PAIR of definitions that variant dispatches.
VARIANT_TIERS = {
    "drive-board-best": "best",
    "work-board-best": "best",
    "drive-board-normal": "normal",
    "work-board-normal": "normal",
}

# tier -> the two definitions that carry it. Both roles, because the tables' "every subagent spawn"
# is one rule and a workers-only plugin cannot staff a repair phase's fresh critic.
TIER_AGENTS = {
    "best": ("fsgg-worker-best", "fsgg-critic-best"),
    "normal": ("fsgg-worker-normal", "fsgg-critic-normal"),
}

# Every variant's repair phase escalates to this tier. See assertion 2 in the module docstring.
REPAIR_TIER = "best"

REPAIR_HEADING = "**Repair-phase route.**"

# `| Claude Code | `opus` | `high` |` — backticks optional so a table that drops them still compares.
_ROW = re.compile(
    r"^\|\s*" + re.escape(RUNTIME) + r"\s*\|\s*`?([^`|]+?)`?\s*\|\s*`?([^`|]+?)`?\s*\|\s*$",
    re.MULTILINE,
)


def _frontmatter(text: str, what: str) -> dict[str, str]:
    """The YAML frontmatter block as a flat str->str mapping.

    Hand-parsed rather than via PyYAML on purpose: the block is a fixed set of scalar `key: value`
    lines, and this keeps the gate stdlib-only. A file without a leading `---` fence is a no-verdict —
    an agent definition the runtime cannot read is not a definition this gate may pass.
    """
    if not text.startswith("---\n"):
        raise GateError(f"{what}: no YAML frontmatter fence — the runtime cannot read this definition")
    end = text.find("\n---", 3)
    if end == -1:
        raise GateError(f"{what}: frontmatter fence is never closed")
    out: dict[str, str] = {}
    for line in text[4:end].splitlines():
        if not line.strip() or line.lstrip().startswith("#"):
            continue
        key, sep, value = line.partition(":")
        if not sep or key != key.strip() or line.startswith((" ", "\t")):
            # A continuation or nested line: this gate only needs the top-level scalars.
            continue
        out[key.strip()] = value.strip().strip("'\"")
    return out


def _route_row(block: str, what: str) -> tuple[str, str]:
    """The `(model, effort)` the `Claude Code` row of ``block``'s route table declares."""
    found = _ROW.findall(block)
    if not found:
        raise GateError(
            f"{what}: no `| {RUNTIME} | <model> | <effort> |` row found. This gate cannot compare a "
            f"table it cannot locate, and skipping it would check nothing while reporting green."
        )
    if len({(m.strip(), e.strip()) for m, e in found}) > 1:
        raise GateError(f"{what}: {RUNTIME} rows disagree with each other: {found}")
    model, effort = found[0]
    return model.strip(), effort.strip()


def _variant_routes(root: Path, variant: str) -> tuple[tuple[str, str], tuple[str, str]]:
    """``(ordinary, repair)`` routes a variant's SKILL.md declares for :data:`RUNTIME`.

    The document is split at the repair-phase heading. A variant with no separate repair table declares
    its repair route to be identical to its ordinary one — which is exactly what the `-best` variants
    say in prose — so the ordinary route is returned for both halves rather than treated as missing.
    """
    rel = f".claude/skills/{variant}/SKILL.md"
    text = read_text(root / rel, f"{variant} route table")
    head, sep, tail = text.partition(REPAIR_HEADING)
    ordinary = _route_row(head, f"{rel} (ordinary route)")
    if not sep:
        raise GateError(
            f"{rel}: no `{REPAIR_HEADING}` section. Every board variant must state a repair-phase "
            f"route; a variant that does not is the .github#2144 stop this plugin exists to fix."
        )
    repair = _route_row(tail, f"{rel} (repair-phase route)") if _ROW.search(tail) else ordinary
    return ordinary, repair


def _load_json(root: Path, rel: str, what: str) -> dict:
    try:
        value = json.loads(read_text(root / rel, what))
    except json.JSONDecodeError as e:
        raise GateError(f"{rel}: not parsable as JSON — {e}") from e
    if not isinstance(value, dict):
        raise GateError(f"{rel}: root must be a JSON object")
    return value


def check_routes(root: Path, findings: list[str]) -> dict[str, tuple[str, str]]:
    """Assertions 1 and 2. Returns each agent's declared route, for the summary line."""
    declared: dict[str, tuple[str, str]] = {}
    for tier, agents in TIER_AGENTS.items():
        for agent in agents:
            rel = f"{AGENTS_DIR}/{agent}.md"
            # ABSENT is a FINDING, not a no-verdict. The gate can reach a verdict here — a route table
            # naming a definition that does not exist IS the defect, fully diagnosed, and it is exactly
            # the .github#2144 stop this slice fixes. Only a file that exists and cannot be READ or
            # PARSED is a no-verdict, and `read_text`/`_frontmatter` below still handle that.
            if not (root / rel).is_file():
                findings.append(
                    f"MISSING DEFINITION: {rel} does not exist, but a route table dispatches {agent}. "
                    f"A host would stop before dispatching rather than downgrade, which is the "
                    f"operator failure .github#2230 exists to fix."
                )
                continue
            fm = _frontmatter(read_text(root / rel, f"{agent} definition"), rel)
            if fm.get("name") != agent:
                findings.append(
                    f"{rel}: frontmatter `name:` is {fm.get('name')!r} but the file is {agent}.md — the "
                    f"runtime dispatches by the frontmatter name, so these must agree."
                )
            missing = [k for k in ("model", "effort") if not fm.get(k)]
            if missing:
                findings.append(
                    f"{rel}: frontmatter is missing {', '.join(missing)}. Carrying model and effort "
                    f"verbatim is the entire reason #2203 chose a plugin over prose."
                )
                continue
            declared[agent] = (fm["model"], fm["effort"])

    for variant, tier in sorted(VARIANT_TIERS.items()):
        ordinary, repair = _variant_routes(root, variant)

        for agent in TIER_AGENTS[tier]:
            if agent in declared and declared[agent] != ordinary:
                findings.append(
                    f"ORDINARY ROUTE DRIFT: {variant} dispatches {agent} at {ordinary[0]}/{ordinary[1]} "
                    f"per its route table, but {AGENTS_DIR}/{agent}.md declares "
                    f"{declared[agent][0]}/{declared[agent][1]}. The frontmatter is what the runtime "
                    f"applies, so the host would silently get the route it did not ask for."
                )

        for agent in TIER_AGENTS[REPAIR_TIER]:
            if agent in declared and declared[agent] != repair:
                findings.append(
                    f"REPAIR-PHASE ROUTE DRIFT: {variant}'s repair phase dispatches {agent} at "
                    f"{repair[0]}/{repair[1]} per its route table, but {AGENTS_DIR}/{agent}.md declares "
                    f"{declared[agent][0]}/{declared[agent][1]}. Every variant's repair phase escalates "
                    f"to the {REPAIR_TIER!r} tier — `fsgg-worker-repair` is superseded by "
                    f"fsgg-worker-best, not merely unbuilt (see {PLUGIN_DIR}/README.md)."
                )
    return declared


def check_one_home(root: Path, findings: list[str]) -> None:
    """Assertion 3. A route definition must not also be a loose file."""
    loose = root / LOOSE_AGENTS_DIR
    if not loose.is_dir():
        return
    for tier_agents in TIER_AGENTS.values():
        for agent in tier_agents:
            if (loose / f"{agent}.md").exists():
                findings.append(
                    f"TWO HOMES: {LOOSE_AGENTS_DIR}/{agent}.md exists alongside {AGENTS_DIR}/{agent}.md. "
                    f".github#2230 item 5 requires exactly one home — two copies disagree, which is the "
                    f"class this repo files most (#485, #865), and which copy the runtime prefers is "
                    f"not visible in the diff that breaks them apart."
                )
    for stray in sorted(loose.glob("fsgg-*.md")):
        name = stray.stem
        if not any(name in agents for agents in TIER_AGENTS.values()):
            findings.append(
                f"UNROUTED DEFINITION: {LOOSE_AGENTS_DIR}/{stray.name} is a loose FS-GG agent definition "
                f"that no route table names. Either it belongs in {AGENTS_DIR}/ with a route, or it "
                f"should not exist — an escalated tier below the human park was retired deliberately."
            )


def check_sources(root: Path, findings: list[str]) -> None:
    """Assertions 4 and 5 — the #2203 binding constraint, and the wiring that makes it load."""
    marketplace = _load_json(root, MARKETPLACE_JSON, "marketplace catalog")
    plugin = _load_json(root, PLUGIN_JSON, "plugin manifest")
    settings = _load_json(root, SETTINGS_JSON, "project settings")

    market_name = marketplace.get("name")
    plugin_name = plugin.get("name")

    # --- the MARKETPLACE source: must be `github` -------------------------------------------------
    extra = settings.get("extraKnownMarketplaces")
    if not isinstance(extra, dict) or market_name not in extra:
        findings.append(
            f"{SETTINGS_JSON}: no `extraKnownMarketplaces` entry named {market_name!r}. Without it a "
            f"clean checkout cannot resolve the plugin and no route can be requested at all — the "
            f".github#2144 stop this slice exists to fix."
        )
    else:
        source = extra[market_name].get("source", {})
        kind = source.get("source") if isinstance(source, dict) else source
        if kind != "github":
            findings.append(
                f"{SETTINGS_JSON}: the MARKETPLACE source for {market_name!r} is {kind!r}, not 'github'. "
                f"#2203's decision binds this: a `directory` source installs IN PLACE (installLocation "
                f"== the path given), so the marketplace becomes the checkout itself and every git "
                f"worktree resolves back to the one main checkout — a shared mutable resource under a "
                f"tree whose repairs the host serialises (#1549, #1663). This is NOT the same field as "
                f"the plugin source in {MARKETPLACE_JSON}, where a relative path is correct. See "
                f"{PLUGIN_DIR}/README.md before changing it."
            )
        elif source.get("repo") != "FS-GG/.github":
            findings.append(
                f"{SETTINGS_JSON}: marketplace {market_name!r} points at repo {source.get('repo')!r}, "
                f"not 'FS-GG/.github' — the catalog lives in this repository."
            )

    # --- the PLUGIN source: relative, and it must resolve ------------------------------------------
    entries = marketplace.get("plugins")
    if not isinstance(entries, list) or not entries:
        raise GateError(f"{MARKETPLACE_JSON}: `plugins` must be a non-empty array")
    entry = next((p for p in entries if isinstance(p, dict) and p.get("name") == plugin_name), None)
    if entry is None:
        findings.append(
            f"{MARKETPLACE_JSON}: no catalog entry named {plugin_name!r}; the plugin manifest and the "
            f"catalog disagree about what this marketplace ships."
        )
    else:
        src = entry.get("source")
        if not isinstance(src, str) or not src.startswith("./"):
            findings.append(
                f"{MARKETPLACE_JSON}: plugin source for {plugin_name!r} is {src!r}. It must stay a "
                f"relative './...' path: that resolves against the FETCHED marketplace copy, which is "
                f"what keeps it off any developer's checkout. This is the half of #2203's constraint "
                f"that a relative path SATISFIES rather than violates."
            )
        elif not (root / src[2:] / ".claude-plugin" / "plugin.json").is_file():
            findings.append(
                f"{MARKETPLACE_JSON}: plugin source {src!r} does not resolve to a plugin manifest. The "
                f"catalog would install an empty plugin and every dispatch would fail at spawn."
            )

    # --- enabledPlugins wiring ---------------------------------------------------------------------
    expected = f"{plugin_name}@{market_name}"
    enabled = settings.get("enabledPlugins")
    if not isinstance(enabled, dict) or enabled.get(expected) is not True:
        findings.append(
            f"{SETTINGS_JSON}: `enabledPlugins` does not enable {expected!r}. The marketplace being "
            f"known is not enough — an unenabled plugin contributes no agents, so every routed "
            f"dispatch would fail exactly as it did before this slice."
        )


def main(argv: list[str]) -> int:
    ap = base_parser(__doc__.splitlines()[0])
    args = ap.parse_args(argv)
    root = Path(args.root)

    findings: list[str] = []
    declared = check_routes(root, findings)
    check_one_home(root, findings)
    check_sources(root, findings)

    if findings:
        return report_findings(NAME, findings)
    routes = ", ".join(f"{a}={m}/{e}" for a, (m, e) in sorted(declared.items()))
    return report_ok(
        NAME,
        f"{len(VARIANT_TIERS)} board variants agree with {len(declared)} plugin route definitions "
        f"({routes}); one home; marketplace source is github and the plugin source is relative",
    )


if __name__ == "__main__":
    sys.exit(run(main, sys.argv[1:], name=NAME))
