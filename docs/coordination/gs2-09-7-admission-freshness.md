# GS2-09.7 protected admission freshness hold

Status: source-only, disabled, stacked on draft #3722. #3690 remains an
unadmitted source-delivery observation. No protected authority or token
handoff is installed.

The native admission record previously had no issue or expiry time. Two
independent negative controls failed red-before: the host signed a new token
for the same run one day after the decision, and the release verifier handed
an already signed token to the fake candidate thirty minutes after the
decision. The source now requires canonical UTC `issuedAt` and `expiresAt`,
with `issuedAt <= now < expiresAt` and a maximum ten-minute decision lifetime.
It checks the window at both signer and release readback. Missing, malformed,
future, expired and overlong windows refuse before the corresponding action.

The time fields are source-contract assertions over a fake protected port.
The protected owner must supply an authenticated clock, durable authority
readback and a policy for clock skew and renewal. A renewed decision must get
its own reviewed identity; source tests do not prove that an installed
authority is immutable or that a GitHub runner cannot supply forged facts.

One-use identity still needs a separate protected decision: the existing
`claim_once` key is the signed binding digest, which includes the minted
token digest. A second token for the same admission decision could have a
different binding digest. The protected owner must decide whether one
admission decision authorizes one mint and enforce that rule durably using
the authority decision ID and exact token binding before any live handoff.
The current source-only claim is not evidence of that rule. Endpoint and
policy digest pins remain empty, and their real provenance is unqualified.

GS2-09.7 Q5/Q6 native receiver readback and protected receipt remain open.
This change does not authorize sandbox dispatch, provider effect, token
release, Authority write, protected merge, receipt, or cutover.
