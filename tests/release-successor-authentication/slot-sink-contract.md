# Inert typed phase slot sink

This source-only model connects the actual `InertCallbackOwner` bind/validate/retain
route to an injected `InertPhaseSlotSink` callable. The current publisher sink and
callback-pair profile, vectors and source pins are unchanged. The existing publisher
overlap control still constructs 66,170 retained bytes; this model does not repair or
replace that production sink.

`prepare(expected_binding, original_sink_identity, closed_report_envelope,
original_window, reserved_population, slot_backend)` independently pins the complete
inert binding. Identity has exactly `path`, `binding` (digest), `invocation`, and
positive `storeGeneration`. The closed report is exactly
`{"schema":"fsgg.release-successor-result/1","sink":"inert-phase-slots/1"}`.
Padding, extra fields, a native binding, changed clock window or changed reservation
refuse before backend reservation. The original window equals the binding clock;
the actual owner checks that clock around callbacks. This model neither authenticates
time nor supplies a fresh execution window.

The prepared callable accepts `(path, report, binding, exact_frame)`. It rechecks
identity, the closed report and complete exact frame; validates payloads using the
actual provider validator; and permits at most six ordered phase slots. The graph
matches the existing publisher: start → ready; ready → issuance-possible or terminal;
issuance-possible → token-owned or terminal; token-owned → revocation-possible;
revocation-possible → revoked or terminal; revoked → terminal. Repeated,
foreign or out-of-order phase calls refuse before backend mutation. The report never
enters a backend operation and is never serialized or mutated. The callback receipt
contains only `binding`, `ordinal`, and `frameDigest` as required by the current owner.

The controlled backend offers `reserve`, `append_phase`,
`observe_loss_or_expiry`, and diagnostic-only `inspect_original`. Internal immutable
receipts bind original identity, generation, slot, ordinal, exact digest, completed
write, barriers and fresh readback. A backend or receipt exception after entry freezes
the core as **Unknown** and consumes the original attempt. It never means
NotCommitted. A new sink cannot reserve the same backend again. Cold reconstruction,
cross-process exclusion, real barriers, authenticated receipts and native restart
reconciliation remain unqualified.

There is one serial attempt per slot. Nonblocking exclusion refuses overlapping
entries. Each core transaction models independent old, expected candidate, temporary
and fresh readback bytes. Completed temporary storage becomes the stable phase store
without another modeled copy. Failure cuts preserve whatever stores existed when
the failure occurred; core writes then stop. Independent loss and expiry observations
have their own candidate/file/readback stores and consume their attempt before any
possible write. Ambiguous observations cannot be retried. Observations retain the
first sink failure and an optional closed `lateCause`; later loss/expiry never rewrites
`firstCause`, including when it was absent. Both observations report Unknown and
cannot authorize an ACK, retry, gate or effect. Phase payload facts remain verbatim
inside the retained exact frame, including any provider `facts.firstCause`; the
observation's `firstCause` is specifically the first sink transaction failure.
Candidate/write/file-barrier/replace/directory-barrier cuts retain `transport`;
fresh-readback failure or mismatched readback/receipt retains `readback`. An unexpected
backend exception conservatively retains `transport` and Unknown. The exported report
dict is only a convenience value; a fresh literal predicate enforces the closed
envelope even if a caller mutates that dict.

| Explicit modeled byte roles | Copies | Per-role capacity | Total |
| --- | ---: | ---: | ---: |
| Stable phase stores | 6 | 2,048 | 12,288 |
| Core old/candidate/temporary/readback | 4 | 2,048 | 8,192 |
| Loss/expiry candidate/file/readback | 6 | 512 | 3,072 |
| Total | 16 | | 23,552 |

Every modeled store holds a distinct `bytearray`; high-water accounting sums store
lengths, and admission reserves capacities before mutation. This accounting describes
only named modeled byte stores. It excludes Python objects, decoder/serialization
temporaries, retained caller frames, callback copies, managed lifetimes, allocator/GC
behavior, filesystem blocks/metadata and a native backend. It must not be added to or
substituted into the domain's 57,562-byte population as a claim of joined fit. The
65,536-byte common-population policy and domain maxima remain unchanged.

Run `python3 tests/release-successor-authentication/slot-sink-tests.py`, or the
established `python3 tests/release-successor-authentication/run.py` entry, which loads
these controls and the original sink-overflow controls. Positive evidence is an inert
six-frame actual-owner journey. Negative evidence includes pre-mutation refusals,
every modeled core write/barrier/readback cut, malformed receipt fields, independent
append-once observations and same-backend restart refusal.

No CLR, process, socket, signer, host, credential, native backend, publication or
replay operation is selected. Native campaign and attempt IDs are **null/unknown**;
the local controls do not fabricate them. Native selections and admission/effect
flags remain false. Original `.103` remains attempted-unknown and unreplayed;
`.104` remains held. Installed/native acceptance and interface freeze remain held.

Atlas capture gap: this bounded four-file delivery cannot add an archive record.
Programme owns capture of the reusable finding that an injected closed-report sink
can exercise the real callback route while its explicit byte-store reservation still
does not establish complete population fit. The owning source is this contract and
its adjacent implementation/tests; no private record or receipt is published here.
