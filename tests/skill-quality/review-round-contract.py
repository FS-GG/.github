#!/usr/bin/env python3
"""Fail closed when the structured review authority or authored roots drift."""

from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
RUNTIMES = (".agents", ".claude")


def read(runtime: str, relative: str) -> str:
    return (ROOT / runtime / "skills" / relative).read_text(encoding="utf-8")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise AssertionError(message)


def main() -> None:
    relative = "pnext-item/references/independent-review.md"
    texts = [read(runtime, relative) for runtime in RUNTIMES]
    require(texts[0] == texts[1], "independent-review contract differs between authored roots")
    contract = " ".join(texts[0].split())

    for literal in (
        "`fsgg.coord.review-decision/v2`",
        "`initial, confirmation, escalation, repair-phase, acceptance`",
        "ordinary repair ceiling | 3",
        "repair-phase ceiling | 10",
        "scripts/fsgg-coord review record",
        "Revisions start at one and are contiguous",
        "`previousDigest` binds the prior canonical record",
        "prose is never authority",
        "A moved head retires the accepted older generation",
        "malformed, stale, partial-coverage, mixed-head, or byte-drifted evidence",
        "--require fsgg:review-decision/v2",
    ):
        require(literal in contract, f"structured review contract is missing: {literal}")

    # .github#2756. A protocol-created review wait is durable coordination state, and critic
    # replacement is the measured ordinary route rather than an unverifiable despawn exception.
    # Pin each race/lifetime leg independently so deleting one cannot leave a plausible summary green.
    for literal in (
        "WaitReceipt(item, claimGeneration, reviewGeneration, kind, enteredAt, expiresAt, evidenceRef)",
        "A current receipt plus the open item PR preserves the touch-set reservation",
        "never extends or resurrects the worker's mutation lease",
        "revalidates that `claimGeneration` is still current or explicitly reacquires the item",
        "completion racing timeout therefore has one durable outcome",
        "Timeout returns the item to an explicit recoverable review state",
        "Fresh succession is the ordinary repair route, not an exceptional recovery",
        "Five of five measured repair chains on 2026-08-17",
        "It inherits no prior clearance and performs a full independent review of that head",
        "ephemeral runtime liveness and a host's testimony about despawn are not review evidence",
        "Entering a review queue writes the receipt before the actor yields",
        "scripts/fsgg-coord review wait <ref> <event.json> --pr <n> --json",
    ):
        require(literal in contract, f"durable review-wait contract is missing: {literal}")

    # Requirement-boundary mutation witness from the independent critic: weakening the mandatory
    # entry transition to "may write" must red even though the rest of the paragraph remains present.
    require(
        "Entering a review queue may write the receipt" not in contract,
        "durable queue entry was weakened from writes to may write",
    )

    # The semantic assertions are production-code xUnit witnesses, not this source-text scanner. The
    # selected Core suite executes them when ReviewWait or its consumers change; these names keep each
    # required boundary independently visible in evidence and prevent one catch-all test being reused.
    behavioral = (ROOT / "tests/FS.GG.Coord.Core.Tests/ReviewWaitTests.fs").read_text(encoding="utf-8")
    for witness in (
        "entering a queue writes a round-trippable receipt",
        "a current receipt reserves after the active lease duration",
        "a changed claim generation never resurrects mutation authority",
        "bounded timeout returns an explicit recoverable state",
        "completion recorded before expiry wins a later timeout race",
        "receipt is bounded and cannot reserve forever",
    ):
        require(witness in behavioral, f"durable review-wait behavioral witness is missing: {witness}")

    # .github#2551. Gate-inversion evidence proved a gate CAN fail and never that anything RUNS it,
    # and never named the case where a gate passes because it examined nothing. Both requirements are
    # judgement a critic applies, so nothing downstream can enforce them; pinning the clauses here is
    # what stops them being deleted silently, as the section itself was.
    for literal in (
        "Inventory the gates the change adds or modifies, and show each one is REACHED",
        "name the workflow, the job, and the invocation line that actually calls it",
        "A gate no workflow invokes is graded `NOT_MEASURED` at best",
        '"Reached" includes the trigger\'s own `paths:` filter, evaluated against THIS change',
        "Vacuous green: a gate can also pass because it examined nothing",
        "subject is source text carries a **non-vacuity leg**",
        "A source-text gate has this failure mode and a behavioural gate does not",
        "a self-test for a scanner **calls** the scanner rather than grepping for its name",
        "one mutation per touched gate",
        "The fixture must reproduce production",
        "The measurement environment must not supply what production lacks",
        "`JUSTIFIED` fired, `DECORATIVE` could not fire, `NOT_MEASURED` obtained no measurement",
    ):
        require(literal in contract, f"gate-inversion contract is missing: {literal}")

    retired_parts = (
        "fsgg:independent-review" + ":v1",
        "fsgg:independent-review-confirmation" + ":v1",
        "fsgg:independent-review-escalation" + ":v1",
        "fsgg:independent-review-repair-phase" + ":v1",
        "fsgg:review-accepted" + ":v1",
    )
    active_roots = (ROOT / ".agents", ROOT / ".claude")
    for active_root in active_roots:
        for path in active_root.rglob("*.md"):
            body = path.read_text(encoding="utf-8")
            for retired in retired_parts:
                require(retired not in body, f"retired prose authority remains in {path}: {retired}")

    for relative, acceptance_literal in (
        ("pnext-item/SKILL.md", "structured v2 acceptance record"),
        ("drive-board/references/host-loop.md", "fsgg:review-decision/v2"),
        ("work-board/references/host-loop.md", "fsgg:review-decision/v2"),
    ):
        authored = [read(runtime, relative) for runtime in RUNTIMES]
        require(authored[0] == authored[1], f"{relative} differs between authored roots")
        require(acceptance_literal in authored[0], f"{relative} does not consume structured acceptance")

    print("review-round-contract: structured v2 ledger, digest chain, round bounds, and new-only authority hold")


if __name__ == "__main__":
    main()
