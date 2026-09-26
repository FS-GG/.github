# GS2-09.7 admission fence at token handoff

Status: source-only, disabled, stacked on draft #3727. #3690 remains an
unadmitted source-delivery observation. No protected launcher, token or
sandbox effect is installed by this contract.

A red-before control revoked the admission decision immediately after the
one-use claim. The old launcher still exposed the fake token and reported
`candidate-complete`. The release port now requires
`invoke_candidate_if_admitted_once(decision_id, binding_id, token_sha256,
token, binding)`. A definite `refused` result means admission was revoked
before token exposure: release records zero invocations, still attempts
native token revocation, and never retries the consumed claim. An exception
or `unknown` result means exposure cannot be excluded; it counts as an
attempt and must not be retried. `complete` still requires subsequent native
token revocation before any complete release disposition.

The protected launcher must authenticate the exact decision, claim, token
digest, binding and current admission state, then hold the same revocation
interlock through the one allowed candidate launch. A revocation committed
before that launch fence must yield `refused` without passing the token to
candidate code. The installed owner must define and prove the linearization
point and behavior for a revocation concurrent with or after launch, including
crash and unknown-result recovery. A source Protocol and fake port cannot
prove token custody, process launch atomicity, revocation ordering or native
provider state. A GitHub App token itself is not single-use.

All live signer, admission, claim, vault, revoker and recovery pins remain
empty. Q5/Q6 native receiver readback and a protected receipt remain open.
This draft authorizes no token release, sandbox dispatch, provider effect,
Authority write, protected merge, receipt or cutover.
