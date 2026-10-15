# Inert slot callback copy and lifetime witness

Run `python3 tests/release-successor-authentication/slot-copy-lifetime-tests.py`.
This source-only control selects the closed `inert-slot-callback-pair/1` manifest
and frozen 13-exchange corpus, then executes the actual controlled entry,
request decoder, callback owner, typed sink and backend synchronously with inert
in-memory streams. It requires no subprocess, network, credential or native job.
The profile selector verifies the runtime source hashes before the journey.

The observer intercepts the sixth backend `readback` stage before the original
stage returns. It records only numeric object identities and lengths, then clears
its stack-frame references. It also observes all six temporary writes and stable
replacements: each stable phase aliases its former temporary without a new copy.
All sixteen listed objects at the terminal cut have distinct live identities.
Aliases through the owner, sink and append call sites share one pending frame.

| Objects at the selected cut | Logical byte lengths |
| --- | ---: |
| Six stable bytearrays, S | 1141 + 972 + 830 + 917 + 831 + 991 = 5682 |
| Old phase-five bytearray, F5 | 831 |
| Candidate and readback bytearrays, 2F6 | 1982 |
| Backend local `actual` bytes, F6 | 991 |
| Owner pending frame bytes, F6 | 991 |
| Decoder bytearray and immutable request, 2R | 590 |
| Entry body and four-byte prefix, R | 291 + 4 = 295 |
| Entry previous validate reply, V | 298 |

Named backend stores account for 8495 logical bytes at this cut. The listed
objects account for `S + F5 + 4F6 + 3R + V = 11660` logical bytes. These are
length sums for one concrete population, with distinct identities checked;
they are not allocator or resident-memory measurements, a complete peak, or a
reservation for all other reachable paths.

All thirteen successful confirmations clear the decoder buffer and reply
reference. Validate retains its pending frame until retain; successful retain
clears pending before response transport. After terminal confirmation, only the
six stable backend stores remain, with the original temporary identities.
Clearing these references establishes the observed ownership transition only;
it does not demonstrate collection, zeroization or physical release.

The terminal readback ambiguity control preserves all six committed stores,
three staging stores, the consumed six attempts, pending uncertainty and the
sink's first readback cause at logical ordinal five. Terminal short-write and
flush failures preserve all six committed stores with logical ordinal six and
cleared pending; neither confirms the terminal response. Reconstructing the
entry against each original backend refuses before any additional backend event,
attempt or store replacement. No failure means NotCommitted or permits replay.

The harness independently owns frozen JSON/hex, concatenated requests, input and
output streams, decoded expected frames and returned synthetic evidence. Those
objects and the instrumentation's scalars are excluded from this source-object
subtotal. Runtime parsed dictionaries/strings, binding/identity data, stack and
object overhead, allocator capacity, stream copies, managed producer/caller/task
copies, uncertainty members outside these cases, filesystem/kernel/peer copies
and concurrent/restart populations are also excluded. The test does not bound
arbitrary caller retention. No complete 65536-byte fit, real durability/barrier,
clock authenticity, custody, native ACK, installed adoption, factory compatibility,
original operation acceptance or release authority follows.
