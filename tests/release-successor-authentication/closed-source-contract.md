# Closed callback source construction /1

`fsgg.utel-closed-source/1` is an opt-in construction identity under
[ADR-0093](../../docs/adr/0093-original-103-authentication-domain-policy.md) and
[ADR-0094](../../docs/adr/0094-separate-v3-and-utel-principals-and-custody.md).
It changes no child, callback /1, domain /3 wire bytes, signature domain or legacy
manifest/vector. Source qualification supplies no production admission or physical bound.

The production ABI is `release_successor_closed_callback.prepare(profile: bytes)`.
The profile is canonical ASCII JSON, without a newline, at most 65,536 bytes,
with duplicate/unknown keys, versions, substitutions and noncanonical encodings refused.
The shipped JSON file's final newline is file formatting, excluded from the profile digest.
The exact digest is fixed in the constructor. The source manifest names the exact ordered
nine-file closure, including all seven imported Python sources, the existing unsigned
maximum vectors and selected profile. A consumer must bind the protected manifest digest
and producer revision independently; self-consistency alone authenticates no installation.
The profile fixes placement, roles, source maxima and the original 90-day retention rule.
Actual original/generation/provider/source/runtime/endpoint/clock/enrollment/physical
accounting/post-expiry custody fields remain null. `prepare` always refuses after
validation, before constructing a provider or performing reservation, process, network,
filesystem writer, credential or signer effects. No caller argument can activate it.

The private source-owned constructor composes the maintained
`InertCallbackRequestDecoder` → `InertCallbackOwner` → `InertPhaseSlotSink` directly.
It owns the input and reply buffers, fixed destination identity, clock and controlled
backend; it never calls the generic fixture's `execute`. Its `run` returns only a fixed
`Status`. There is no raw evidence/frame/buffer/backend/path accessor, writer callback,
CLI or external provider selector on this route. The existing generic APIs remain separate.
The trusted pinned Python module boundary covers ordinary API use; private qualification
seams do not defend against reflection, interpreter modification or arbitrary code injection.

One original record reserves construction before possible retention and never resets after
failure. Thirteen serialized exchanges, one outstanding reply and 40,960 aggregate request
bytes are the source limits. The maintained decoder checks the 4096-byte framed request
limit before body allocation. Replies are at most 512 bytes and must fully write, flush
and confirm before the next request. Terminal confirmation requires exchange 13 and all
six retained ordinals. The source stops without reading a fourteenth request; it claims
neither EOF nor trailing-input verification. Child frame and aggregate maxima remain
2048 and 16,384 bytes. Completed exchanges do not release cumulative physical-copy terms.

Exactly two recipient roles (`programme-acceptance`, `systemadmin-recovery`) each have
three original-lifetime purpose reservations (`initial-status`, `settlement-status`,
`retention-expiry-status`). All six opportunities belong to the same controlled provider
original record; one nonblocking lock serializes delivery across roles. A purpose consumes
its slot as Unknown before extraction. Failed/lost delivery never grants another extraction,
recipient incarnation or retry, including reconstruction using that original record.
Only one owned typed `DiagnosticEvidence` slot receives the exact retained envelope;
the caller receives a fixed completion enum. No general output receives evidence bytes.
Each response is at most 546 bytes; six opportunities imply at most 3276 output bytes,
which is neither a physical subtotal nor proof of fit.

The controlled provider's **unsigned structural maximum vector** is read from the existing
`domain-max-vectors.json`. Its placeholder signatures are explicitly not enrolled evidence.
`observed-synthetic` means only this controlled recipient retained that vector. It is not
an operational ACK/readback. Loss is Unknown and consumes the slot. The source refuses
both expiry-purpose delivery and reads after the controlled original expires, without
extracting expiring bytes: post-expiry custody is null. Consequently this slice exercises
four possible controlled deliveries and two refused expiry opportunities, never six
qualified operational responses. The controlled provider and private cuts are excluded
from production selection. Production needs the exact retained signed domain/3 envelope,
authenticated role/original/profile/generation enrollment and durable reservation/recovery;
none is synthesized by the callback phase sink or supplied by a public caller.

First callback failure remains in the maintained owner; loss and expiry use its existing
independent append-once typed sink slots. A failed observation consumes its original slot.
The cleanup outcome remains Unknown: input/reply buffers remain owned under the
original reservation, with no native termination, durable settlement or physical release
inferred from a returned status. Internal exception text is not exported.
Runtime exception/allocation maxima remain supplier obligations.

`closed-source-tests.py` runs in the existing authentication suite and tests the actual
thirteen-exchange/six-slot path, exact frozen bytes, fragment/oversize/truncation controls,
short/write/flush/confirmation/backend cuts, cumulative construction and recipient attempts,
source substitutions and egress refusal. Private test inspection is excluded from the
production population. The existing controls retain all legacy maxima, vectors and census.
These are source semantics tests; no CLR, process launch, network or native provider runs.

Coordination owns the matching closed parent constructor, endpoint ownership and protected
producer pin; SystemAdmin owns the actual separately supervised key-free facts provider,
durable commit/readback/fencing/recovery, authenticated clocks and enrolled recipient
runtime/storage/transport custody. Descriptor issuer, phase-proof and external ACK private
keys remain in their existing separate custody, outside parent and Python facts owner.
All endpoint/kernel/cache, parser/object/storage, reservation, diagnostic, failed/unknown
incarnation and alternate egress populations need the existing complete eight-domain,
five-metric physical upper-bound proof. Source privacy conventions, call quotas, GC or
settled tasks supply no such bound. No new engine, journal or accounting schema is added.

The production trust registry stays unchanged; cap 65,536, physical upper bound null,
fit unproved, admission false. Original `.103` remains attempted-unknown/unreplayed;
`.104`, enrollment, credentials, deployment and native acceptance remain held.
