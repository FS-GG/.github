# Candidate-engine self-host bootstrap

Use this route only when the shared coordination engine refuses because it cannot understand a new schema
case or because the authoritative decision boundary moved. Business-rule disagreement is not a bootstrap
reason and cannot be represented by the receipt type.

The direct implementation and enforcement live in `.github`. Framework and product workspaces are affected
when they invoke an explicitly selected coordination engine: reads remain available, but an authored or
opaque candidate cannot perform a write without this authority.

## Receipt sequence

1. Run the shared engine and preserve its exact refusal.
2. Build the candidate and produce the immutable snapshot plus its proposed decision and action keys.
3. Prepare a proposal JSON containing `baseSha`, `candidateHeadSha`, `sharedRefusal`, one allowed `reason`,
   the five evidence references, `candidateDecisionKey`, `candidateActionKey`, and accountable
   `hostAcceptance` (`actor` and `acceptedAt`).
4. Have a stable engine measure and bind the candidate and snapshot:

   ```text
   fsgg-coord-engine self-host mint proposal.json candidate-engine snapshot.json receipt.txt
   fsgg-coord-engine self-host verify receipt.txt candidate-engine /candidate/checkout
   fsgg-coord-engine self-host record FS-GG/.github#1234 receipt.txt
   ```

5. Only after verification, expose the candidate to the shim for the one authorized write session:

   ```text
   export FSGG_COORD_ENGINE_BIN=/candidate/checkout/src/FS.GG.Coord.Cli/bin/Release/net10.0/fsgg-coord-engine
   export FSGG_COORD_STABLE_ENGINE_BIN=/shared/stable/fsgg-coord-engine
   export FSGG_SELF_HOST_RECEIPT=/absolute/path/receipt.txt
   scripts/fsgg-coord heartbeat FS-GG/.github#1234 --worker worker-1
   ```

The shim asks the distinct stable engine to verify the receipt immediately before forwarding a write. It
checks the receipt digest, candidate SHA-256, reported version, exact candidate `HEAD`, and merge-base.
Missing, malformed, stale, contradictory, anonymous, or self-verified authority is a refusal with no remote
mutation. Read-only candidate inspection does not require a receipt.

## Post-merge replay

After merge, rebuild the shared engine, run it against the preserved snapshot, and pass its resulting keys
through the replay verifier:

```text
fsgg-coord-engine self-host replay receipt.txt snapshot.json <shared-decision-key> <shared-action-key>
fsgg-coord-engine self-host replay-record FS-GG/.github#1234 receipt.txt snapshot.json <shared-decision-key> <shared-action-key>
```

The first command is a read-only preview. The second appends the digest-bound replay receipt to the same
accountable item. Completion remains blocked after a bootstrap receipt until exactly one matching replay
receipt is durable. A different snapshot digest, decision key, action key, malformed receipt, orphan
replay, or duplicate receipt is refused. Retain the snapshot and provenance material with the two durable
receipts as the delivery evidence set.
