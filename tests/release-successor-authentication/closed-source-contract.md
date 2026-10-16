# Closed callback source construction /1

`fsgg.utel-closed-source/1` is an opt-in construction identity under
[ADR-0093](../../docs/adr/0093-original-103-authentication-domain-policy.md) and
[ADR-0094](../../docs/adr/0094-separate-v3-and-utel-principals-and-custody.md).
It changes no child, callback /1, domain /3 wire bytes, signature domain or legacy
callback-pair manifest/vector. The separately selected slot source closure advances
to `inert-slot-callback-pair/2`. Source qualification supplies no production admission or physical bound.

The production ABI is `release_successor_closed_callback.prepare(profile: bytes)`.
The profile is canonical ASCII JSON, without a newline, at most 65,536 bytes,
with duplicate/unknown keys, versions, substitutions and noncanonical encodings refused.
The shipped JSON file's final newline is file formatting, excluded from the profile digest.
The exact digest is fixed in the constructor. The source manifest names the exact ordered
nine-file closure, including all seven imported Python sources, the existing unsigned
maximum vectors and selected profile. A consumer must bind the protected manifest digest
and producer revision independently; self-consistency alone authenticates no installation.
The profile fixes placement, roles, source maxima and retention expiry at the original
total cutoff plus 90 days, without read-based renewal. This is distinct from the
300-second operational cutoff.
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

Preparation is factored into internal `_validate_preparation(...)` and
`InertPhaseSlotSink._initialize_owned(snapshot, owned_backend)` helpers. Validation owns
a copied binding and canonical no-newline identity (`path`, `binding`, `invocation`,
`storeGeneration`) and performs no backend operation. Initialization calls the existing
`reserve(identity, ROLES, MAXIMUM_BYTES)` port and initializes the maintained sink.
The public legacy `prepare` still accepts only the exact `ControlledSlotBackend` type;
subclasses and duck-typed adapters refuse before reservation. The sink's `__call__`,
`observe`, phase ordering, first-failure and exact typed `Receipt` checks remain the one
algorithm. Its frame-local values and raw-byte aliases remain available to the unchanged
copy-lifetime observer.

`_Core._initialize_owned(token, owned_original, binding, requests, owned_sink, ...)`
factors shared owned buffer, clock, owner and decoder construction from the controlled
original/sink creation. It receives an already owned exact maintained sink. The controlled
constructor consumes its original before preparation/reservation. A future fixed private
composer must consume its own original and check its exact adapter, pinned public/private
sources, profile/store/original identities and fixture qualification before using these
internal helpers. They provide no registry, plugin lookup, caller callback, validator or
public activation option. Internal naming and the token are source conventions, not a
security mechanism. The same `run` and `diagnostic` algorithms own the callback journey
and fixed-status egress; no second callback engine or generic fixture wrapper is added.

This public SOURCE preparation does **not** implement or exercise CAP03. A separately
reviewed private adapter must preserve the distinct public canonical identity and CAP03
original/descriptor identity, own reservation and proof objects, validate exact frame,
checkpoint, store and source joins, and explicitly translate only checked provider proofs
to existing source receipts. It must commit the sink's exact observation envelope and
check the provider's observation ordinal zero separately from the public phase ordinal.
Actual authenticated original/checkpoint/clock mapping stays null. Public CI imports no
private code and needs no private checkout, endpoint or credential. Private qualification
and subsequent Coordination pin adoption remain dependent work; source receipts grant
no operational ACK, durability qualification or installed admission.

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
production population. The existing controls retain all legacy maxima and census. The separate slot `/2`
vectors use the new bundle binding; their bytes intentionally differ from `/1`. An
explicit historical `/1` binding control compares the maintained codec output against
protected historical request/reply/frame digests; it does not admit `/1` selection here.
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
