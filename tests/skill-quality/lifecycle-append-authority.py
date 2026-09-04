#!/usr/bin/env python3
"""Fail closed if the lifecycle-comment writer loses its live claim serialization."""

from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src/FS.GG.Coord.Cli.BoardOps/Handlers.fs"


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


def main() -> None:
    source = SOURCE.read_text(encoding="utf-8")
    validate(source)
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
    print(f"PASS  lifecycle append authority: live claim serialization + {len(witnesses)} mutations")


if __name__ == "__main__":
    main()
