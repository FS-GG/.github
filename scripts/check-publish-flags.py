#!/usr/bin/env python3
"""Gate `vars.NUGET_ORG_PUBLISH` against the nuget.org policies it silently controls (.github#750, epic #266).

Two of this repo's release workflows publish to public nuget.org via Trusted Publishing, and each gates
its nuget.org steps on `if: … && vars.NUGET_ORG_PUBLISH == 'true'`. The gate is deliberate — without the
OIDC policy in place, `NuGet/login` fail-closes (401) and would break every org-feed release — so the
variable stays a switch. But **nothing asserts the switch is on.** It is `true` today; nothing holds it
there.

`FS.GG.Coord.Cli` is one of the two packages behind it, and it is the coordination engine the whole fleet
restores. Unset `NUGET_ORG_PUBLISH`, or run a release in a context that does not inherit it, and the engine
silently stops reaching nuget.org — `dotnet tool restore` then fails in five receivers, and the failure mode
is [ADR-0039](../docs/adr/0039-nuget-org-is-the-read-path.md)'s own thesis: **not an error, just an empty
version list.** That is the exact shape of epic #266 (a missing subject reports green), arriving in the
mechanism built to end it.

So this asserts: **for every workflow that GATES a nuget.org publish on `NUGET_ORG_PUBLISH`, the variable is
`true`.**

FAILS CLOSED, which is the whole point of #266. Each of these is an ERROR, not a skip:
  * a policy-gated publisher exists and the variable is unset (`''`), `false`, or any non-`true` value —
    the producer will silently stop publishing;
  * a policy-gated publisher exists and the variable was not PASSED to the gate at all (absent from the
    environment) — the gate cannot see what it is meant to check, which is itself a fail-open;
  * the workflows directory does not exist or cannot be read.

It is NOT vacuous. It keys on the exact variable name `NUGET_ORG_PUBLISH` — the thing #750 is about — and a
green prints the named set it checked, so "nothing gated" and "checked, and it's on" never share a silent
exit. A repo that genuinely gates no publisher on the flag has nothing to assert and says so.

SCOPE. This gate runs in `.github` and asserts `.github`'s own variable, protecting its own two packages —
the load-bearing case, since `FS.GG.Coord.Cli` is restored fleet-wide. Per #750's own survey, the only other
repo that gates on this flag is FS.GG.Templates; asserting *its* variable is a cross-repo concern that needs
a credential this repo does not have (#574), and is tracked separately. The five product repos publish
unconditionally (no flag gate) and are safe by construction — a publisher that does NOT gate on the flag is
not this gate's subject.

Usage:  scripts/check-publish-flags.py [.github/workflows]
        NUGET_ORG_PUBLISH=<value>  is read from the environment; the gate's workflow passes it as
        `NUGET_ORG_PUBLISH: ${{ vars.NUGET_ORG_PUBLISH }}`.
"""

import os
import sys
from pathlib import Path

# The gate's own workflow references the variable name (it passes it as env), so it must not count itself as
# a policy-gated publisher. Excluded by filename.
SELF = "publish-flags.yml"

# The variable #750 is about. Detection keys on this exact name because it is the stable thing — an action
# rename cannot hide it, and a workflow that gates on it is, by definition, one that stops publishing when it
# is unset.
FLAG = "NUGET_ORG_PUBLISH"

# A nuget.org publish signal, so a workflow that merely MENTIONS the variable in prose (or a future doc) is
# not mistaken for a publisher. A real gated publisher both names the flag AND publishes to nuget.org.
PUBLISH_SIGNALS = ("api.nuget.org", "NuGet/login", "nuget.org")


def policy_gated_publishers(workflows_dir: Path) -> list[str]:
    """Workflow filenames that gate a nuget.org publish on NUGET_ORG_PUBLISH."""
    found = []
    for path in sorted(workflows_dir.glob("*.yml")):
        if path.name == SELF:
            continue
        text = path.read_text(encoding="utf-8")
        if FLAG in text and any(sig in text for sig in PUBLISH_SIGNALS):
            found.append(path.name)
    return found


def main(argv: list[str]) -> int:
    workflows_dir = Path(argv[0]) if argv else Path(".github/workflows")

    if not workflows_dir.is_dir():
        # A gate that cannot find its subjects must not pass. An absent workflows directory is a
        # misconfiguration, never an all-clear.
        print(
            f"::error::check-publish-flags: workflows directory not found: {workflows_dir}",
            file=sys.stderr,
        )
        return 1

    gated = policy_gated_publishers(workflows_dir)

    if not gated:
        # Legitimately empty: this repo gates no nuget.org publish on the flag. Say so — a green over a
        # named (here, empty) set is not the same as a green that never looked.
        print(
            f"ok: no workflow in {workflows_dir} gates a nuget.org publish on {FLAG} — nothing to assert."
        )
        return 0

    # The variable, as the gate's workflow passed it. `None` means it was never wired into the environment;
    # `''` means the repo variable is unset (the #750 hazard); anything else is its literal value.
    flag = os.environ.get(FLAG)

    named = ", ".join(gated)

    if flag is None:
        print(
            f"::error::check-publish-flags: {len(gated)} workflow(s) gate a nuget.org publish on {FLAG} "
            f"({named}), but {FLAG} was not passed to this gate. Wire `{FLAG}: ${{{{ vars.{FLAG} }}}}` into "
            f"the gate's env, or it cannot see the switch it is meant to check.",
            file=sys.stderr,
        )
        return 1

    if flag != "true":
        shown = repr(flag) if flag else "unset (empty)"
        print(
            f"::error::check-publish-flags: {FLAG} is {shown}, but {len(gated)} workflow(s) gate a nuget.org "
            f"publish on it ({named}). Each will SILENTLY stop publishing to nuget.org — an empty version "
            f"list, not an error (ADR-0039). FS.GG.Coord.Cli is restored fleet-wide; set {FLAG}=true, or "
            f"remove the policy if the publish is truly meant to be off.",
            file=sys.stderr,
        )
        return 1

    print(
        f"ok: {FLAG}=true, and {len(gated)} nuget.org publisher(s) gate on it: {named}."
    )
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
