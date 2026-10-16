# ADR-0094: Separate V3 and UTEL principals and credential custody

- **Status:** Accepted policy; provisioning, enrollment and activation pending
- **Date:** 2026-10-16
- **Decision owner:** FS-GG Programme under the sponsor's explicit delegation
- **Affects:** V3 original-intent, grant, host and receiver authority; UTEL `.103` descriptor, phase proof and external ACK authority
- **Decision:** [V3 selection](https://github.com/FS-GG/.github/issues/4516#issuecomment-6209884629), [UTEL selection](https://github.com/FS-GG/.github/issues/4517#issuecomment-6209889196), [V3 transport selection](https://github.com/FS-GG/.github/issues/4516#issuecomment-6211348444)

## Context

V3 `/3` compatibility source and UTEL's inert service source do not supply enrolled
production principals. The sponsor authorized Programme to name them and their custody.
This record replaces the open principal-and-custodian selection in the two issue
contracts. It does not claim that a credential, environment, service account or public
pin exists. [ADR-0088](0088-ci-owned-unattended-credential-execution.md) supplies a
prospective remote-CI custody pattern for ordinary v2; this decision selects that
pattern separately for the named V3 and UTEL issuer roles. It does not activate v2,
V3 or UTEL credential use by implication.

## Decision

Every signing purpose has a distinct principal and key. The named `.github` Actions
environments are dedicated to protected-main, GitHub-hosted ephemeral jobs. Programme
is accountable for their credential custody and enrollment authorization. Provisioning
must occur administratively outside the fdev development container, with no SSH relay,
host wallet or per-run human signer.

| Purpose | Selected principal | Selected custody |
|---|---|---|
| V3 original-intent issuance | `fsgg-v3-original-intent-issuer` | `.github` environment `v3-original-intent`; `V3_ORIGINAL_INTENT_SIGNING_KEY`; Programme |
| V3 create/start/retire grant issuance | `fsgg-v3-effect-grant-issuer` | Separate environment `v3-effect-grant`; `V3_EFFECT_GRANT_SIGNING_KEY`; Programme |
| V3 receiver command/ACK signing | `fsgg-v3-receiver-command-ack-signer` | Separate environment `v3-receiver-command-ack`; `V3_RECEIVER_COMMAND_ACK_SIGNING_KEY`; Programme |
| V3 detached host attestation | `fsgg-v3-host-attestor` | Dedicated supervised host service identity and host-only secret store; SystemAdmin/eugen |
| UTEL original descriptor issuance | `fsgg-utel-original-descriptor-issuer` | `.github` environment `utel-original-descriptor`; `UTEL_ORIGINAL_DESCRIPTOR_SIGNING_KEY`; Programme |
| UTEL phase proof | `fsgg-utel-phase-proof-provider` | Dedicated supervised host service identity and proof-only secret store; SystemAdmin/eugen |
| UTEL external ACK signing | `fsgg-utel-ack-authority` | Different supervised host service identity and ACK-only secret store, outside the facts store and original job/runner/VM; SystemAdmin/eugen |

`fsgg-v3-receiver-verifier` and `fsgg-utel-native-verifier` are secret-free receiver
principals. Coordination/fdev1 owns their integration and purpose-specific public-pin
policy; Programme accepts independent enrollment and readback. SystemAdmin's V3 host
attestor cannot issue originals or grants, and the UTEL phase proof key cannot sign
external ACKs. EHotwagner remains custodian of the existing UTEL App credential; it
is not a descriptor, phase-proof or ACK issuer. Existing v1, ordinary-v2, V3 and UTEL
keys cannot be reused across these roles.

An independently selected trust policy pins repository, workflow, protected ref,
qualified source artifact, signer purpose and public fingerprint outside the received
packet. Original intent, grants and receiver commands remain separately scoped and
one-use. CI queue delay cannot renew an original deadline. Missing transport,
fencing, clock mapping, receipt or enrollment evidence refuses the effect.

## V3 transport custody

Programme selects the existing-node shared TLS hostname and terminator custody scope
for V3 source preparation: a separate HTTPS origin on port 8443, a fixed POST exchange
path and a dedicated loopback backend on port 18443. The exact hostname remains
restricted. The existing private service on port 443 must remain unchanged. This
selection does not assert a dedicated TLS key or an installed endpoint.

The shared TLS administrator is a common transport trust boundary. Transport
authentication supplies no original-intent, grant, attestation or ACK authority.
Every purpose-specific signing principal and key above remains distinct, independently
enrolled and verified; sharing transport custody does not permit signing-key reuse.

Provisioning requires separate admission against the joined source contract, including
exact source, version and configuration; finite header, token, body, error, concurrency,
depth, time and copy limits; the OIDC allowlist; and independent pin, rotation and
readback policy. Production-provider and physical-copy maxima remain unproved.
Any Funnel-related certificate and tailnet-policy changes must be explicit in that
admission. This selection admits no Funnel command, listener, certificate or key
operation, enrollment, workflow dispatch or native effect.

## Activation and recovery

Before use, deliver the protected policy and workflow source; create and restrict each
environment and host service identity; provision distinct keys through the selected
custody route; independently acquire and read back fingerprints and receiver pins;
and qualify canonical signing/refusal, authenticated machine transport, original-bound
commit/readback/fencing, cold recovery and unattended lifecycle. The V3 CI route must
also demonstrate bounded queue latency against the original command and lease windows.
The UTEL route retains the original 270/285/300-second cutoffs and 90-day nonrenewing
retention. Rotation or revocation must fence outstanding attempts and preserve unknown
originals; it never reissues `.103`.

## Consequences

The named V3 and UTEL environments were absent from the live `.github` environment
enumeration at selection. Host service identities, credentials, pin enrollment,
qualified production provider and installed source remain unverified. V3 protected
source still has abstract verifier ports and refuses native ACK/handoff. UTEL
AUTH-SVC-03's exact source candidate is separately held for a retained-evidence
substitution defect; this identity decision cannot admit that source.

This record fills the principal and custody-policy selection left open in
[ADR-0093](0093-original-103-authentication-domain-policy.md). ADR-0093's complete
physical-copy accounting, 65,536-byte operative cap, original-effect safeguards and
other domain decisions remain in force. `.103` is attempted-unknown and unreplayed;
`.104` and native/original effects remain held. No key operation, installation,
signing, grant, ACK, handoff or replay is authorized by this record alone.
