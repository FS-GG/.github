#!/usr/bin/env python3
"""Is the obligation arm's set of reachable post-merge worlds COMPLETE? (.github#2571 round-1 repair)

The arm calls an act MANUAL when the mapped `decide()` declines to cut in every world the feed can
still reach. That is only sound if the worlds it enumerates are all the ones that matter — and the
first draft of `.github#2571` enumerated exactly one, the frontier measured now, while CLAIMING the
verdict held on "any post-merge state of the world". It did not: the frontier advances, `decide()`'s
rail is `candidate.patch == frontier.patch + 1`, and a forward move flips `candidate-not-next-patch`
into `tag`. That claim was prose no leg could falsify, which is why it survived authoring and review.

So the property is GRADED here instead of asserted there. For every (candidate, observed frontier) pair
in a bounded grid this computes, by BRUTE FORCE, whether ANY reachable world performs the act — sweeping
every frontier at or above the observed one, and also `tagExists` and both feed-presence states, which
the shipped builder pins rather than varies — and compares that to what the shipped
`merge_performs_act` answers. Any disagreement is printed with its pair and exits non-zero.

The reference is deliberately WIDER than the builder: it varies facts the builder fixes, so a pinned
fact that turns out not to be uniquely permissive shows up here too, not only a mis-enumerated frontier.

Usage:  completion-sweep.py <gate.py> <kit-auto-publish.py>
        (the gate path is a parameter so the fixture can point this at a MUTANT and prove the sweep
        reds when the completion is un-pinned again — the inversion evidence for this very leg)
"""
import importlib.util
import itertools
import sys

# The grid. Three minor lines so a candidate can sit on, above and below the observed frontier's line,
# and five patches so "next patch", "several patches ahead" and "behind" are all represented. The
# defect this grades needed a candidate two or more patches above the observed frontier, which a
# one-patch grid could not express.
MINORS = (57, 58, 59)
PATCHES = (0, 1, 2, 3, 4)


def load(path, name):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    # Registered before execution: `dataclasses` resolves annotations through `sys.modules`, and the
    # gate carries dataclasses.
    sys.modules[name] = module
    spec.loader.exec_module(module)
    return module


def main(argv):
    if len(argv) != 2:
        sys.exit("usage: completion-sweep.py <gate.py> <kit-auto-publish.py>")
    gate = load(argv[0], "sweep_gate")
    decider = load(argv[1], "sweep_decider")

    automation = next(a for a in gate.MERGE_AUTOMATION if a.decision)
    performs = gate._DECISION_PERFORMS
    grid = [f"0.{minor}.{patch}" for minor, patch in itertools.product(MINORS, PATCHES)]
    order = decider.patch_tuple

    def reference(candidate, observed):
        """Brute force: does ANY reachable world cut this candidate?"""
        for frontier in grid:
            if order(frontier) < order(observed):
                continue  # the feed moves forward; it cannot return to an earlier frontier.
            for tag_exists, feeds in itertools.product((False, True), ("absent", "present")):
                facts = gate._kit_auto_publish_facts(candidate, frontier)
                facts["tagExists"] = tag_exists
                facts["orgFeed"] = facts["nugetFeed"] = feeds
                if decider.decide(facts)["action"] in performs:
                    return True
        return False

    disagreements = []
    for candidate, observed in itertools.product(grid, grid):
        shipped, _, _ = gate.merge_performs_act(
            automation, candidate=candidate, frontier=observed
        )
        truth = reference(candidate, observed)
        if shipped != truth:
            disagreements.append((candidate, observed, shipped, truth))

    pairs = len(grid) ** 2
    if disagreements:
        print(
            f"UNSOUND: the arm's enumerated worlds disagree with brute force on "
            f"{len(disagreements)} of {pairs} (candidate, observed) pairs.",
            file=sys.stderr,
        )
        for candidate, observed, shipped, truth in disagreements[:5]:
            verb = "MISSES a reachable cut" if truth else "invents a cut"
            print(
                f"  candidate {candidate} against observed frontier {observed}: arm says "
                f"performed={shipped}, brute force says {truth} — the arm {verb}.",
                file=sys.stderr,
            )
        return 1
    print(f"ok: the arm agrees with brute force on all {pairs} (candidate, observed) pairs.")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
