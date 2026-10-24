# Trusted Home export and dedicated qualification

Status: accepted architecture; implementation and runtime acceptance remain separate.

The user accepted three execution changes on 24 October 2026. The durable ruling is
[Coordination Delivery 4820](https://github.com/FS-GG/.github/issues/4820#issuecomment-6380220325).
This decision applies to the trusted Home route. It preserves the complete acceptance
of Delivery 4820 and Outcome 4650, including original outcomes and first causes,
durability, recovery, actual resource retirement, publication and adoption.

## Accepted changes

A verified durable export may survive termination without preserving the original
source filesystem's device, inode or owner identity. Commit must bind the original
operation, selected content and exact retained copy-observation bytes. Recovery
verifies the committed export; a pathname or a reserialized receipt is insufficient.

Home is the trusted local execution owner for this route. A separate privileged
reader, UID 30034, signing key or production reader attestation is not a prerequisite
for it. This does not claim protection against a compromised Home owner or remove
other profiles' requirements.

Native qualification uses a dedicated, preconfigured Linux runner. Home supplies
the machine/provider proposal and its capacity/access evidence. The coordinator
owns the protected workflow and shared source integration. Provider selection,
source delivery, actual provisioning, qualification and disposal remain distinct.

## Journal and recovery boundary

Retain the original context, idempotency key, journal and compare-and-swap revision.
An occupied legacy entry does not authorize overwriting, rekeying or replay. Quiesce
writers while their output remains available, then copy and commit the exact cold
observation bytes before starting removal.

Read is observational and never grants cleanup. The first successful BeginCleanup
reply supplies a consumed permission; a later cold Read can confirm the recorded
intent but cannot recreate that permission. Missing replies, expired windows and
uncertain effects retain the original hold.

Cleanup after expiry or reboot needs an explicitly admitted finite cleanup-only
continuation bound to the same original and committed export, with current resource
observations. It must not renew useful work or repeat copying. Repository/manifest
image digests and image configuration IDs are different identities; any adapter must
join them through actual image metadata rather than compare their digest tails.

## Delivered runner source and remaining acceptance

[Coordination PR 1063](https://github.com/FS-GG/FS.GG.Coordination/pull/1063)
delivered the reviewed runner source at
`a6196dc36bda5813e8a237f29196605a2d1724a3`. Its reference candidate is four CPUs,
16 GiB guest memory and 256 GiB backing storage. The actual 14 GiB available-memory
and 80 GiB free-space gates remain independent of nominal allocation. The fixed
runner group/label, original job budgets, rootless/no-new-privileges requirements
and source-controlled refusal of unselected image/provider inputs remain in force.

This merge creates no machine or runner registration. Actual supported capacity and
write-domain enforcement, authenticated complete input closure, guest and SDK
compatibility, original-owned lifecycle implementation, and independent retirement
observations remain required. A version label, source test, registration heartbeat
or current free-memory reading cannot establish those effects. One VM does not
establish the separate two-independent-machine acceptance.

Original failures and unavailable historical observations remain retained at their
actual scopes. Consult the live owning issues and
[worker channels](worker-channels.md) for current owners, pending qualification and
mailbox requests; this decision is not a second operational ledger.
