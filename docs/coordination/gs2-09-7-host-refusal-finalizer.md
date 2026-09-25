# GS2-09.7 protected refusal-path finalizer contract

This source-only draft models the outer protected finalizer missing from #3712
and #3713. `release_once` can reject an envelope or mint proof before its own
revocation call. The #3713 claim adapter also checks store authority before
native revocation, so a store failure after handoff could inhibit that call.
The wrapper in this draft records a pending token before `release_once` and
keeps a separately pinned native revoker available if the store later fails.
It is not wired into a workflow; direct use of #3712 or #3713 still has those
gaps. All five new protected pins and the inherited signer pin are empty.

## Required protected identities and ordering

The host must mint under a protected run identity and write a host-owned mint
record and encrypted token escrow before any candidate handoff. The record
binds an opaque mint ID, token digest, exact protected run-context digest,
sandbox repository ID, App ID, actor, installation ID, and vault ID. The mint
ID and token escrow must not come from the candidate checkout or proof. A
separate protected review must pin the exact HTTPS ledger origin, resource ID,
endpoint, vault ID, and native revoker identity. The ledger and vault must be
durable and separately credentialed; the candidate token, sandbox repository,
runner workspace, caches, and artifacts must have no read path to token escrow
and no write or delete path to the vault or journal. The vault must return the
exact string token to the protected host before handoff; an equality-like
proxy is insufficient. Adapter descriptors in this draft are assertions, not
evidence of real ACLs, atomicity, or isolation.

Before invoking #3712, the host writes an atomic, durable pending revoke
intent for the mint ID with `revokeRequired: true` and confirms the token is
recoverable from the protected vault.
Only a fresh committed pending write followed by exact independent readback
can proceed to the release call. A duplicate, lost, or falsely committed
append response cannot trigger a new invocation. The outer finalizer attempts native
revocation after a release refusal, unknown invocation, or unknown inner
revocation result. It requires the independent provider observation and a
durable revoked receipt before reporting `revocation: revoked`. A lost native
or receipt response is resolved only through native and ledger readback. If
the ledger becomes unavailable after handoff, the independent revoker still
receives the token; the verdict stays pending because durable intent and
receipt cannot be proved.

Crash recovery must run from protected host storage and retrieve the escrowed
token. It may attempt revocation and append a receipt, but must never retry
candidate invocation. A crash between mint and pending write still triggers a
revoke attempt, while the absent pending record prevents a confirmed result.
If the ledger is unavailable during recovery, the
separate vault and revoker permit an emergency native revoke attempt; the
verdict remains pending. A missing revoker or vault identity before mint must
block the protected route. If either fails after mint, this source cannot
guarantee token containment: the protected owner needs an independent
recovery/escalation path and must keep the case pending.

The `disposition` field is always `pending`, including fake-port cases where
the release and native revocation both report success. It cannot serve as a
GS2-09.7 Q5/Q6 receipt, sandbox acceptance, or cutover authorization. A
one-time host handoff does not make a GitHub App token one-use; a candidate
that receives it can copy it until the provider revokes or expires it.

## Evidence and next protected work

The isolated fake-port controls cover unsigned envelope refusal; unknown
invocation; duplicate, lost, and crash-interrupted pending writes; unknown
native observation; lost revocation and receipt responses; foreign mint,
escrow, store, vault, and revoker identity; and store failure after handoff
and during recovery. They do not call a provider or prove installed host
credentials, service ACLs, native revocation, durable storage, or a scheduled
recovery worker. Before activation, the owner must install and independently
verify those authorities, wire the wrapper as the sole protected release
entrypoint with a finalizer on every minted-token exit, test crash recovery
against the real store and provider, and perform Q5/Q6 native readback under
the protected gate. #3690 remains an unadmitted source-delivery observation;
the GS2-09.7 acceptance receipt remains open, and GS2-09.8 acceptance depends
on it.
