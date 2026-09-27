# GS2-09.7 launch outcome and finalizer boundary

Status: source-only fake-port characterization stacked on draft #3728. #3690
remains an unadmitted source observation. All live protected pins and ports
remain unconfigured.

The red-before cancellation test proved that `release_once` consumed the
one-use decision claim and entered candidate invocation, but skipped its
native token revocation attempt when the launch adapter raised
`KeyboardInterrupt`. Release now attempts `port.revoke(token)` in a `finally`
block around claim and candidate invocation. Cancellation still propagates
to the outer refusal-path finalizer. A normal launch exception or an unknown
result counts as possible token exposure, leaves the release pending, and
cannot cause a second invocation against the consumed decision claim.

Fake-port controls also revoke the admission decision while one launch is
in progress. Once exposure is possible, the result remains pending, token
revocation is attempted, and a rerun refuses the duplicate claim. When the
native revocation observation is unknown, the durable refusal-path finalizer
leaves its receipt pending for protected recovery. These controls specify
the failure behavior; they do not establish atomicity of the installed
admission/launch interlock or prove native token revocation.

The protected owner must install a host-only launcher with a durable
decision-ID claim and an authenticated, same-authority admission/revocation
interlock through the single candidate launch. It must define the launch
linearization point and prove the outcome of a concurrent revoke, unknown
adapter result, process cancellation or crash, including token custody and
durable recovery scheduling. A protected finalizer must read back native
revocation and commit an exact receipt before closing pending state. Q5/Q6
native receiver readback and an accepted protected receipt remain open.

This draft authorizes no signer/key installation, token release, sandbox
dispatch, provider effect, Authority write, protected merge, receipt or
cutover.
