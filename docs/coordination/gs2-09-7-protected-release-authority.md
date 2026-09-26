# GS2-09.7 protected release authority source contract

Status: source-only and disabled, stacked on draft #3720. No protected
authority, workflow integration, signer, vault, App credential or sandbox
dispatch is installed. #3690 remains an unadmitted source-delivery
observation; no Q5/Q6 receipt follows from this draft.

The previous workflow SHA placeholder could be assigned from the same runner
context. Two independent controls failed red-before: with that self-asserted
value, the host signed an envelope and the release path invoked the fake
candidate. The source now requires a separate protected admission port at
both points. Its identity, endpoint and release policy digest pins are empty.
Absent authority refuses before signer use or token handoff.

The native current readback must be one sealed, admitted decision from an
immutable durable resource that candidate code and the workflow cannot write.
Its exact fields bind the reviewed workflow repository/path/commit,
candidate commit, run ID, run attempt, derived nonce, sandbox repository and
Project IDs, signer SPKI digest, and release policy digest. Wrong, stale,
revoked, partial, unknown-result or mismatched records
must refuse. This source checks a strict record and descriptor returned by
the port; its fake port tests cannot prove a real service's isolation or
durability. The finalizer still owns native token revocation after a minted
token encounters admission refusal.

## Protected owner decision before release

The protected release owner must select and review an external durable
authority, its exact origin/resource/endpoint and immutable decision model,
the release policy revision digest, native readback identity and ACLs. The
owner must establish the workflow commit and candidate commit from separately
admitted facts, then bind the actual run ID/attempt and nonce without using a
candidate or workflow supplied value as the decision source. The host adapter
must authenticate that authority outside the candidate checkout and provide
the readback port to both signer and verifier. It must prove denied candidate
read/write and workflow write paths, signer FD custody, vault isolation,
one-use claim, and native revocation. Until that decision and adapter are
reviewed and installed, all pins stay empty and the source refuses.

Separate GS2-09.7 Q5/Q6 native receiver readback and protected receipt are
still required before GS2-09.8 acceptance. This contract does not authorize
provider effects, protected merge, receipt, or cutover.

The later source-only admission freshness finding and remaining one-use
identity decision are recorded in `gs2-09-7-admission-freshness.md`.
