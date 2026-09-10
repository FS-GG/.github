---
title: "Telemetry publication handoff"
category: Reference
categoryindex: 4
description: "P1 source contract for separating aggregate production from the GitHub publisher."
---

# Telemetry publication handoff

P1 supplies a candidate source path for moving public telemetry publication to a dedicated OS account. It does not install a service, provision a credential, disable the incumbent publisher, publish a snapshot, or authorize cutover. H3 must provide the concrete Main paths, UIDs, group, immutable candidate, and operational proof before this path can become active.

The producer continues to own the existing label selection and canonical public projection. `handoff-stage` reads the configured private store only through the existing engine-backed `build_host` path, applies the existing closed label projection and `validate_host`, and requires the exact SHA-256 label approval. It never discovers a GitHub credential. The publisher never receives the store config, engine path, labels file, database, or ingestion credential; it accepts only the already validated public `dashboard-host/3` bytes.

## Filesystem ownership and transfer

The operator provisions an outgoing directory owned by the producer UID and a dedicated handoff GID with exact mode `2750`. The publisher account is a member of that group. The producer creates immutable `snapshot-<sha256>.json` files and the coalescing `current.json` pointer with mode `0640`; the directory gives the publisher read and traverse access but no create, rename, or delete access. The publisher must have no access to the producer's store or label paths.

The publisher uses a different directory owned by its UID and primary GID with exact mode `0700`. Its activation, lock, copied candidate, durable intent, and last-success files use mode `0600`. The producer has no access to this directory and therefore cannot activate publication, replace an unresolved intent, or claim success. H3 qualification must exercise these checks with the two selected UIDs and GID; same-UID unit fixtures prove serialization and mode behavior but are not evidence of Main account isolation.

Staging writes and fsyncs a digest-named blob before atomically replacing and fsyncing `current.json`. Rename or directory-fsync failure reports `HANDOFF_DURABILITY_UNKNOWN`; it never claims which pointer survived, and it does not prune either candidate. Each side retains the current blob, any last-success blob, and a bounded recent set of eight blobs. Pruning happens only after the new pointer or durable intent is committed. An already opened file remains readable if a later producer prune unlinks its name. The publisher takes a bounded shared lock on the producer-owned, group-readable `stage.lock` while it reads the pointer and blob; it never chmods or writes that lock. The producer takes the same lock exclusively with a bounded wait. Publisher state has its own bounded lock. None of these locks covers or opens the telemetry store.

## Candidate readiness and cutover fence

`handoff-setup` first produces a candidate-readiness preview while the incumbent may still be running. Preview reads no cutover proof and has no effect. Recording activation additionally requires `--record-activation`, `--authorize-single-publisher-cutover`, and an operator-owned cutover proof. The resulting activation binds:

- the canonical outgoing path and exact producer UID/handoff GID;
- the approved label digest and fixed FS-GG repository, branch, and path;
- the SHA-256 digest of the installed publisher script;
- the `environment-only` publisher credential source; and
- the incumbent account and user-manager identity, inactive and disabled timer, inactive service, the digest of the expected event-receipt path, its final absent state, and either the removed receipt digest or a typed `already-absent` prior state with a null receipt digest.

The cutover proof is a closed, digest-bound `fsgg.telemetry.publisher-cutover-proof/1` receipt supplied by the privileged host integration. Its directory is operator-owned, exact `2750`, and only group-readable by the publisher; `cutover.json` is exact `0640`, and the operator UID must differ from both publisher and producer UIDs. The activation receipt retains the canonical proof directory, operator UID, handoff GID, and proof digest so review can establish its origin. The proof binds the exact candidate and publisher-config digests. The unprivileged publisher does not run `systemctl` against another account, receive sudo, inspect the old private config, or author the proof. SystemAdmin owns authoritative capture from the actual old account manager and config. Caller-supplied state flags are not a supported proof source.

The setup command records no token and performs no publication or systemd mutation. `handoff-publish` rechecks the installed script digest and consumes the bound immutable proof before credential discovery. Changing any bound path, owner, group, label approval, destination, candidate bytes, or receipt requires a new reviewed activation. The replacement has no source-level auto-enable path. The operator must separately verify on Main that the named incumbent is the only prior writer and that exactly one replacement invocation mechanism is enabled.

The publisher credential is inherited only by the publisher process as `GITHUB_TOKEN`. The handoff path requires `environment-only`, so it cannot fall back to another account's `gh auth` session. The credential remains limited to contents write on the fixed destination. Ingestion and aggregate production run without it.

## Restart and unknown-effect behavior

Before a push, the publisher copies the selected blob into its own state, fsyncs it, and atomically records a digest-bound intent. A later producer pointer coalesces only after the publisher has established the prior intent's remote outcome. If remote readback is unavailable, the old intent remains byte-for-byte unchanged and no push occurs.

When the current remote bytes already equal the intended bytes, restart records a reconciled success without another commit. Observation-time-only changes use the existing normalized semantic comparison, retain the original remote observation and revision, and record an unchanged success. A push response loss or optimistic ref conflict triggers immediate current-ref readback and immutable verification. Exact intended bytes at the current ref complete the intent; any unavailable or mismatched result remains pending. Failed immutable bytes, payload revision, or current-ref verification also keeps the intent. The next invocation must reconcile that durable unknown before it can accept a newer producer pointer.

`last-success.json` is written and fsynced before the intent is removed. It records the requested snapshot digest separately from the verified remote snapshot digest, retained remote snapshot filename and revision, commit, disposition, and completion time. This distinction keeps a semantic no-op honest when the newer requested bytes differ only by observation time: the retained last-good bytes and revision are the bytes actually served. The receipt is recovery information, not activation authority. The existing `publish`, `current_publication`, `verify_publication`, `validate_host`, label projection, optimistic non-force ref update, and fixed-destination rules remain the only GitHub writer and public schema implementation.

## Source qualification and pending operation

The focused fixtures cover approval drift, corrupt blobs, atomic pointer failure, exact candidate and cutover binding, semantic no-op, restart with unavailable remote state, ambiguous push recovery, current-ref drift, durable intent retention, and the producer's lack of credential access. They use a fake transport and do not publish or alter systemd. A disposable local proof with numeric producer and publisher UIDs plus a shared handoff GID confirms that the publisher can read and validate the handoff while it cannot write there or read the producer's private store/labels, and the producer cannot read publisher state or credentials.

Operational completion remains pending until H3 supplies and checks the concrete Main account and group configuration, SystemAdmin emits the authoritative operator proof from the actual incumbent account manager and config, the operator records the exact inactive-incumbent and installed-candidate activation, enables one authorized replacement, and verifies an actual publication and remote readback. Source acceptance alone cannot satisfy those boundaries.
