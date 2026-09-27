# GS2-09.7 admission revocation and claim ordering hold

Status: source-only, disabled, stacked on draft #3725. #3690 remains an
unadmitted source-delivery observation. No protected authority, App token,
sandbox effect or Q5/Q6 receipt is installed by this contract.

A red-before composition control revoked the admission decision after the
release readback and immediately before the claim. The plain claim CAS still
returned `committed` and the fake candidate was invoked. Revocation between
signing and release already refused at release readback; that check cannot
close the later race.

The claim port now requires `cas_claim_if_admitted(decision_id, binding_id,
token_sha256)`. The protected store descriptor must identify the exact
admission resource and assert one atomic decision-state check with the
single-key claim CAS. A positive CAS response permits handoff only after
native readback of the exact decision, binding, token digest, admission
resource and `admitted` state at the claim commit point. `not-admitted`,
duplicate, unknown, exception and lost CAS response never permit handoff.
The fake race and lost-response controls pass under this source contract.

The protected release owner must select and verify a real transaction domain
shared by admission revocation and claim. The owner must define the
linearization rule: a revocation committed before claim prevents handoff;
a claim committed first records that ordering durably. The installed adapter
must authenticate the same protected admission resource, prove atomicity and
native readback under crash, retry and lost-result conditions, and keep the
candidate and workflow from writing either state. A descriptor assertion and
fake store cannot prove any of those properties. The host still needs a
separate policy for a revocation committed after claim but before candidate
invocation, plus native token revocation and finalizer readback. The later
source-only launcher fence is recorded in
`gs2-09-7-handoff-revocation-fence.md`; its installed interlock remains
unqualified.

All live pins remain empty. No token release, sandbox dispatch, provider
effect, Authority write, protected merge, receipt or cutover is authorized.
