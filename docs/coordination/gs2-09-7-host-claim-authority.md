# GS2-09.7 protected host claim authority contract

This draft specifies the missing durable authority behind `claim_once` and
`revoke` in the source-only host release contract (#3712). No store, revoker,
credential, signer, workflow step, or sandbox run is installed by this draft.
The three protected store pins in the new module are empty, so the source
refuses every production claim and revocation.

## Host storage and release prerequisites

The protected host must pin an exact HTTPS store origin, resource ID, and
endpoint in reviewed source. The endpoint must identify a durable, atomic
create-if-absent record keyed by the SHA-256 digest of the exact signed binding
payload. The record binds the token SHA-256 digest and survives host process
restart and run retry. A duplicate key, including one left by a crash between
claim commit and token handoff, must refuse another handoff. A CAS timeout or
lost response is `unknown` even when later readback finds the record: it never
authorizes token release. Native readback exists for recovery and audit, not to
turn an uncertain CAS result into a new grant.

The store and its credential must be owned by the protected host in a separate
service or namespace. The candidate token and candidate-writable checkout,
artifacts, caches, runner workspace, and sandbox repository must have no path
to create, change, or delete records. A filesystem path inside the runner or
repository is not a durable claim authority. Before installation, the host
owner must independently verify the service identity, ACLs, atomicity,
durability, exact endpoint, and native readback. Adapter `describe()` metadata
and these fake-port tests cannot prove those properties. In particular, a
candidate-controlled adapter could lie about `candidateCanWrite`.

After a claim, the protected host may pass the exact token to one synchronous
candidate invocation. The protected runner must retain custody of the token
and prohibit retries after an unknown invocation result. The handoff is
single-use as a host action; a GitHub App installation token itself remains
usable until it expires or is actually revoked. The candidate can copy a token
it receives. This draft does not establish token confinement or acceptance of
the sandbox route.

Before and after the invocation, the host needs an independently credentialed
native revocation adapter. It must durably append a revoke intent, attempt
native revocation, observe the token's revoked state from the provider, and
append a durable revoked receipt. A lost intent, unknown native observation, or
lost receipt keeps the result pending. A lost revoke response may resolve only
through independent native observation. The protected workflow also needs a
finalizer that attempts revocation after envelope or proof refusal, where the
release function has not yet called its port. No such finalizer is installed.

## Evidence and limits

The new tests use fake store and revoker ports. They cover absent pins,
candidate-writable and non-durable descriptors, foreign endpoint and
credentials, exact endpoint drift, duplicate claims across host instances,
lost CAS response after commit, crash before handoff, unknown native revoke,
and missing durable receipt. They compose the new port with #3712's signed
binding verifier and prove that uncertain or duplicate claims cause zero
candidate invocations in that model. No live storage ACL, signer, GitHub App
token custody, provider revocation, or receiver readback is demonstrated.

This source draft depends on #3711's protected signer/pin and #3712's release
contract. The #3690 workflow merge is an unadmitted source-delivery
observation. GS2-09.7 Q5/Q6 and the protected acceptance receipt remain open;
GS2-09.8 acceptance remains dependent on that receipt. Activation needs a
separate protected review of installed host storage and revocation authority,
release binding, credential custody, and native readback. No V1 Authority
journal is implicitly admitted for this V2 purpose.
