#!/usr/bin/env python3
"""Fail closed if the lifecycle-comment writer loses its live claim serialization."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src/FS.GG.Coord.Cli.BoardOps/Handlers.fs"
COMMAND_TESTS = ROOT / "tests/FS.GG.Coord.Cli.Tests/CommentMutationTests.fs"
SUPERVISION_CONTRACTS = {
    ROOT / ".agents/skills/pnext-item/SKILL.md": [
        "The supervising parent owns the post-child boundary",
        "unposted terminal draft marked `pending final usage`",
        "timing condition to `unavailable`",
        "while any completed child lacks this reconciliation",
        "canonical per-user",
        "separately reviewed non-counting proof",
        "human-authorized synthetic checkpoint",
        "functional check is terminal red",
    ],
    ROOT / ".agents/skills/pnext-item/references/lifecycle-ledger.md": [
        "Supervising a completed child",
        "Post-response collection is a parent responsibility",
        "multiple matching sessions are an attribution failure",
        "`unavailable` reason",
        "`$FSGG_USAGE_RECEIPT_STORE`",
        "`irrecoverable-exclude-usage`",
        "Human-authorized synthetic checkpoint",
        "`reconstruct_missing_data:false`",
    ],
    ROOT / ".agents/skills/work-roadmap/SKILL.md": [
        "roadmap driver is the supervising parent",
        "terminal draft with `pending final usage`",
        "roadmap Done all fail closed",
        "post-completion uniqueness lookup",
        "repository-only copies are not retention",
        "excluded, never reconstructed",
        "first-class synthetic checkpoint",
        "immutable human authorization",
    ],
    ROOT / ".agents/skills/work-roadmap/references/lifecycle-log.md": [
        "supervising parent, owns every post-child seal",
        "It does not post its own terminal lifecycle event",
        "the response means pending, never unavailable",
        "telemetry-reconciliation-<phase>",
        "no-overwrite writes",
        "legacy-receipt-proof:sha256",
        "`fsgg.telemetry.synthetic-checkpoint/v1`",
        "new trusted anchor",
    ],
}
PAGINATION_WITNESSES = [
    "lifecycle election sees a same-key competitor beyond one merged page",
    "paginated (comments merged)",
    "server-ordered winner is comment 101",
]


def validate(text: str) -> None:
    start = text.find("let private authorizeLifecycleComment")
    end = text.find("    let commentCmd", start)
    if start < 0 or end < 0:
        raise ValueError("comment handler has no dedicated lifecycle append authorization boundary")
    boundary = text[start:end]
    required = {
        "canonical-item binding": "target.Canonical <> item.Canonical",
        "complete live marker scan": "Reads.requireCompleteMarkerScan item.Short",
        "lease/liveness authorization": "LiveHandlers.authorizedMarker opts.LeaseMinutes",
        "stale-claim PR liveness": "Reads.prAlive ctx.Transport item.Owner item.Repo item.Number",
        "single claim-worker writer": "marker.Worker.Value = worker.Id",
        "unclaimed refusal": "no live claim marker can serialize this lifecycle append",
    }
    for label, witness in required.items():
        if witness not in boundary:
            raise ValueError(f"lifecycle append boundary lost {label}")
    command = text[end:]
    authorization = command.find("authorizeLifecycleComment ctx opts target item w capability.Body")
    mutation = command.find("Writes.createVerifiedComment", authorization)
    if authorization < 0 or mutation < 0 or authorization > mutation:
        raise ValueError("comment create can mutate before lifecycle append authorization")
    election_start = text.find("let private electLifecycleAppend")
    election_end = text.find("    let private authorizeLifecycleComment", election_start)
    if election_start < 0 or election_end < 0:
        raise ValueError("comment handler has no post-create lifecycle append election")
    election = text[election_start:election_end]
    election_required = {
        "complete authoritative reread": "Reads.commentsWithIdentity ctx.Transport",
        "same append-key competition": "candidate = proposed",
        "server-id winner": "let winner = List.min candidates",
        "submitted-id comparison": "winner = receipt.CommentId",
        "explicit rejected loser": "is preserved rejected-fork evidence",
    }
    for label, witness in election_required.items():
        if witness not in election:
            raise ValueError(f"lifecycle append election lost {label}")
    if "Result.bind (electLifecycleAppend ctx item capability.Body)" not in command:
        raise ValueError("verified lifecycle create does not run the authoritative append election")


def validate_command_tests(text: str) -> None:
    for witness in PAGINATION_WITNESSES:
        if witness not in text:
            raise ValueError(f"lifecycle append pagination control lost: {witness}")


def validate_supervision_contracts(contents: dict[Path, str]) -> None:
    for path, witnesses in SUPERVISION_CONTRACTS.items():
        for witness in witnesses:
            if witness not in contents[path]:
                raise ValueError(f"{path.relative_to(ROOT)} lost post-child usage ownership: {witness}")
        mirror = ROOT / ".claude" / path.relative_to(ROOT / ".agents")
        if contents[path] != mirror.read_text(encoding="utf-8"):
            raise ValueError(f"{path.relative_to(ROOT)} and {mirror.relative_to(ROOT)} diverged")


def main() -> None:
    source = SOURCE.read_text(encoding="utf-8")
    validate(source)
    command_tests = COMMAND_TESTS.read_text(encoding="utf-8")
    validate_command_tests(command_tests)
    supervision_contents = {path: path.read_text(encoding="utf-8") for path in SUPERVISION_CONTRACTS}
    validate_supervision_contracts(supervision_contents)
    witnesses = [
        "target.Canonical <> item.Canonical",
        "Reads.requireCompleteMarkerScan item.Short",
        "LiveHandlers.authorizedMarker opts.LeaseMinutes",
        "Reads.prAlive ctx.Transport item.Owner item.Repo item.Number",
        "marker.Worker.Value = worker.Id",
        "no live claim marker can serialize this lifecycle append",
        "authorizeLifecycleComment ctx opts target item w capability.Body",
        "Reads.commentsWithIdentity ctx.Transport",
        "candidate = proposed",
        "let winner = List.min candidates",
        "winner = receipt.CommentId",
        "is preserved rejected-fork evidence",
        "Result.bind (electLifecycleAppend ctx item capability.Body)",
    ]
    for witness in witnesses:
        try:
            validate(source.replace(witness, "removed-boundary-witness", 1))
        except ValueError:
            continue
        raise SystemExit(f"lifecycle append mutation survived: {witness}")
    for witness in PAGINATION_WITNESSES:
        try:
            validate_command_tests(command_tests.replace(witness, "removed-pagination-witness", 1))
        except ValueError:
            continue
        raise SystemExit(f"lifecycle append pagination mutation survived: {witness}")
    for path, contract_witnesses in SUPERVISION_CONTRACTS.items():
        for witness in contract_witnesses:
            mutated = dict(supervision_contents)
            mutated[path] = mutated[path].replace(witness, "removed-supervision-witness", 1)
            try:
                validate_supervision_contracts(mutated)
            except ValueError:
                continue
            raise SystemExit(f"post-child supervision mutation survived: {path.relative_to(ROOT)}: {witness}")
    count = sum(len(value) for value in SUPERVISION_CONTRACTS.values())
    print(f"PASS  lifecycle append authority: live claim serialization + {len(witnesses)} source mutations + executable merged-page control + {count} post-child supervision mutations")


if __name__ == "__main__":
    main()
