# BAR knowledge backup retention

Owner: the BAR knowledge-store operator owns backup execution and recovery. The public roadmap records
the bounded policy and aggregate evidence; credentials, restricted payloads and host-local paths remain
in their existing custody.

## Outcome

The requested running-host retention boundary is Closed on 2026-10-02. The active BAR knowledge store
now has four distinct verified versions: the current version and three older versions. Initial adoption
pruned 51 redundant or older snapshots and freed 65,733,767,168 bytes. The first cycle completed at
04:51 UTC, and the scheduled cycle completed at `2026-10-02T05:22:38.1344414Z` with:

- retained versions: 4;
- pending entries: 0;
- staged entries: 0;
- alerts: none;
- live allocated bytes: 1,383,022,592; and
- backup allocated bytes: 5,532,090,368.

The keeper runs every 1,800 seconds and was observed as PID `3007815`. It has no host-restart
supervision, so this closure applies to the running host. Recovery after a host restart still requires
an operator to restart the keeper and inspect its health.

## Policy and implementation authority

Frozen source `f3c4b9043c78ce5e96795cc8cdf4f3811b769a6c` owns the F# retention policy. Its manifest
SHA-256 is `ab9464dbb28aedfd1d18725db6f3ed7d9b8b0d4456cccdf86a253374c1253f7b`, and its compiled DLL
SHA-256 is `412f9d03de5b1682374f0f3f5948b38d49268ea53bf6d851af926c6e8003760e`.
The production reducer corresponds to a canonical 31-state replay. Python is a mechanical SQLite and
filesystem bridge; it does not decide retention, scheduling or health policy.

Retention keeps the current snapshot and at most three older distinct logical versions. A backup is
eligible only after both growth thresholds are met: at least 256 MiB and at least 25 percent growth.
The independent database-to-source-payload warning ratio is 1.5. Snapshot identity is content based,
so repeated unchanged cycles do not consume the three-version history.

## Recovery and limits

The reviewed recovery path resumes exact pending cleanup after interruption, including loss of one
database or sidecar leaf or the whole pending bundle. Retained entries continue to require physical,
logical, integrity, foreign-key and full-text-search validation. Deletion is allowlisted and
descriptor-relative; ancestor and link escapes are refused.

This operational result does not accept BAR native gameplay, modify stock Recoil, establish Count1,
or change the six useful-play outcomes, which remain 0/6. It also does not claim machine power-loss
durability or host restart supervision. Those product and operating boundaries remain separate from
the fulfilled request for regular running-host backups with no more than three older versions.
