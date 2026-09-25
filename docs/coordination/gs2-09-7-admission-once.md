# GS2-09.7 one-use admission decision claim

Status: source-only, disabled, stacked on draft #3724. No protected store,
release port, App credential, or sandbox route is installed. #3690 remains
unadmitted; Q5/Q6 native readback and receipt remain open.

The prior release claim was keyed by the signed binding digest, which includes
the token digest. A red-before control minted two distinct fake token
bindings under one admitted decision; both reached the fake candidate. A
second red-before control showed that an arbitrary alternate decision ID for
the same admitted subject was accepted.

The admission decision ID is now a domain-separated SHA-256 digest of the
stable protected subject: authority resource, workflow repository/path/commit,
candidate commit, run ID/attempt/nonce, sandbox repository and Project IDs,
signer SPKI digest, and release policy digest. Token mint and decision
validity times are excluded, so the same admitted subject retains one key.
The native decision readback must carry that exact digest. Release uses the
same readback to submit `claim_once(decision_id, binding_id, token_sha256)`.
The protected CAS is keyed by decision ID and its native readback must match
all three identities before a candidate handoff. A duplicate or lost CAS
response never grants another handoff. Revocation intent and receipt remain
bound to the exact token binding.

The protected owner must verify that the installed store enforces a single
durable CAS namespace for the derived decision ID, including across runner
crashes, token remints and hosts for one run attempt. A new attempt needs a
separate protected admission decision. Admission revocation and claim
must have a reviewed ordering or atomic rule so a decision cannot be revoked
between release readback and claim while still granting a token. Source fake
ports cannot prove store durability, ACL, native readback, or that ordering.
The later source-only conditional CAS contract for this ordering is recorded
in `gs2-09-7-admission-claim-ordering.md`; installed atomicity remains open.
One protected host handoff does not make a GitHub App token single-use; native
token revocation and Q5/Q6 readback remain separate gates.

All live pins remain empty. This draft authorizes no token release, sandbox
dispatch, provider effect, Authority write, protected merge, receipt or
cutover.
