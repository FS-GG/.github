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
