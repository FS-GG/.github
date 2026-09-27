# GS2-09.7 signer, runner binding, and token custody hold

This is a source-only qualification packet stacked on #3717. It does not
admit the #3690 workflow merge, install a key, pin a protected service, or
authorize a sandbox run. The source review covers .github drafts #3711–#3717
and Coordination #552 (`5f5110ccd69006896390a59531f7ab188f845cc5`)
and #558 (`8b519bf1ba417b36fcf7941763645eee6d67ec08`).

## Installed sequence that still needs proof

The current #3690 workflow mints a selected sandbox token and passes it to
candidate execute and cleanup steps. It does not call #3711's signer, #3712's
protected release gate, #3714's outer finalizer, or #3715/#3716 recovery and
census. Candidate #552 checks proof/token consistency before provider calls;
it cannot authenticate protected provenance. Candidate #558 can verify a
signature only when its trust anchor and expected run facts come from an
independent protected host. All reviewed signer, store, vault, revoker,
recovery, queue, and journal pins remain empty. The candidate checkout and
shared runner workspace are therefore not an admitted place to hold the
signer, token escrow, or one-use claim state.

The protected installation must establish this order and custody:

1. Admit one exact protected workflow SHA and one exact candidate SHA. Derive
   run ID, attempt and nonce from protected runner facts, then verify the
   protected checkout SHA and registered sandbox target before App secret use.
2. Mint the selected-repository App token under host-only credentials and
   independently read back the actor, repository, Project, effective grants,
   token expiry and token digest. Keep the raw token and signing key out of
   candidate-controlled files, environment, logs and artifacts.
3. Commit the exact mint/binding record and encrypted token escrow in a
   host-owned vault that the candidate can neither read nor write. Prove the
   signer private-key FD's protected origin and ACL; pin its exact public SPKI
   digest in reviewed host and candidate source. Sign the exact proof/token,
   workflow SHA, run ID/attempt, candidate SHA, nonce and target binding.
4. From protected runner facts, verify the signed envelope and exact token
   string; durably commit and read back pending revocation intent and one-use
   claim before one synchronous candidate handoff. Every refusal, exception,
   cancellation and uncertain result needs protected native revocation and
   durable readback. A host handoff limit does not make an App token one-use.
5. Retain a complete append-only mint index, sealed pending census, isolated
   recovery worker and escalation for an active token after an unknown or
   duplicate recovery claim. Perform separate Q5/Q6 native receiver readback
   and issue a protected acceptance receipt only after those gates pass.

## Source control result and limit

Two token-custody controls failed red-before. #3714 accepted a vault descriptor
that made no candidate-read assertion, and a non-string escrow object whose
custom equality returned true let the fake candidate invocation proceed. The
source now requires `candidateCanRead: false` and an exact string token
readback before release. This is a contract assertion, not evidence of real
vault ACLs or key custody. No source test can establish the provenance of an
installed FD, GitHub runner variables, App token, provider response, or
protected journal. A source-only green verdict must not become GS2-09.7 Q5/Q6
acceptance. #3690 remains an unadmitted source-delivery observation, and
GS2-09.8 acceptance still depends on the GS2-09.7 protected receipt.
