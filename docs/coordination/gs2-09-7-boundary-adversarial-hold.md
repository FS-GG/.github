# GS2-09.7 adversarial boundary hold

This packet reviews the source-only draft chain without admitting a protected
sandbox run. It is non-authoritative. Coordination #552 at
`5f5110ccd69006896390a59531f7ab188f845cc5` checks the candidate's mint
proof for consistency with the token and selected sandbox grants. Coordination
#558 at `8b519bf1ba417b36fcf7941763645eee6d67ec08` defines a signed
run-binding verifier whose expected run facts must arrive from a protected
host. Neither candidate source can grant itself authority. The candidate
live script in #552 calls the consistency validator before provider effects;
it does not consume an installed signed envelope. No dispatch or provider
call was made in this review.

The .github drafts are #3711 `0cb96ed438a7d75d0a79585a1ea978df3ff54136`
(host signer), #3712 `9eda9e51d1345931f88413e504f290ff033d05e1`
(release), #3713 `52b15a5f0a565e35de97a8c91656f7b0bbb28031`
(durable claim), #3714 `e59a47e55f4dc3ec83bf4627bc4c1394bc6ff09c`
(outer finalizer), #3715 `715a5c6558ce5a587b4990d6c245c808bd1f4fa6`
(one-subject recovery), and #3716
`e5c30b383d44f5df959f2288a54c8be73f798857` (pending census). These
are stacked draft heads, not protected installed authority.

## Independent negative controls and repairs

Two adversarial controls failed before this draft's source repair. A fake CAS
store returned `committed` without storing a claim; #3713 returned `granted`
and #3712 invoked the candidate once. The claim adapter now requires an
exact token-digest native readback after a positive CAS response. A lost CAS
response remains unknown even when later readback finds the claim, and a
duplicate claim never invokes again. The second control showed #3716 could
return recovery subjects when only resource IDs were pinned while the worker
endpoint, finalizer endpoint, and revoker identity remained unset. The census
now refuses until all those inherited protected pins are configured.

The independent suite also refuses a coherent but stale run ID or candidate
SHA, a stale nonce, an omitted pending token, a foreign protected journal or
vault, and a stale high-water mark. These source controls do not prove the
origin of runtime facts or a real service's durable behavior. They return no
candidate token or provider effect. The #552 mint tests and #558 binding
tests pass against their exact draft heads; those are source tests only.

## Protected authority still required

The host must install and independently verify a protected signer and pinned
public key, protected runner facts (workflow SHA, run ID/attempt, candidate
SHA and nonce), exact App actor and selected sandbox grants, and token custody
before any release. A host-owned atomic CAS store must provide durable claim
readback and prevent candidate writes. A separate encrypted token vault,
native revoker credential, durable pending intent and receipt journal, and
complete append-only mint index must survive failures. A protected scheduler
must prove no pending-token omission across sealed high-water snapshots,
revalidate each subject, and escalate an active token after duplicate or lost
recovery claim; #3715 intentionally does not retry that uncertain effect.
The source pin values remain empty, and no live adapter, scheduler, signer,
or key is installed.

The #3690 workflow merge is an unadmitted source-delivery observation.
GS2-09.7 Q5/Q6 require the protected native receiver readback and an
acceptance receipt. GS2-09.8 acceptance remains dependent on that receipt.
No source test, candidate consistency result, or census verdict here is a
protected merge, receipt, sandbox qualification, or cutover decision.
