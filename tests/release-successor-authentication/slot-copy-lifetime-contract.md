# Inert slot callback copy and lifetime witness

Run `python3 tests/release-successor-authentication/slot-copy-lifetime-tests.py`.
This source-only control selects the closed `inert-slot-callback-pair/2` manifest
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
clears pending before response transport. After terminal confirmation, the backend
store map contains only the six stable stores, with the original temporary
identities; parsed state and entry
locals remain reachable independently.
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
subtotal. Stack and
object overhead, dictionary keys, unselected values, allocator capacity, stream
copies, managed producer/caller/task copies, uncertainty members outside these cases, filesystem/kernel/peer copies
and concurrent/restart populations are also excluded. The test does not bound
arbitrary caller retention. No complete 65536-byte fit, real durability/barrier,
clock authenticity, custody, native ACK, installed adoption, factory compatibility,
original operation acceptance or release authority follows.

## Additional selected Python representations

The original sixteen-object cut and its 11,660-byte subtotal stay unchanged.
A separate selected-value-graph observation at the same sixth readback exposes
entry/owner/sink binding dictionaries, canonical owner binding bytes, entry
identity, sink/reservation identity bytes, the parsed retain request, pending and
sink-parsed terminal phases, owner previous phase/facts and sink previous facts.
Pending phase and handle phase alias; the sink reparses a distinct phase. Owner
and sink bindings are distinct dictionaries. Sink identity aliases reservation
identity. Owner previous facts and the sink's deep-copied facts are distinct.
The observer walks only the named roots' values; dictionary keys and integers
are excluded. A dict/list/tuple has quantity type `object-count` and quantity 1;
this is not its element count or a byte size. Strings have `logical-bytes` equal
to UTF-8 encoded length; bytes/bytearrays use their length. Identities are scoped
to one cut, never compared across later cuts after possible object reuse.

The actual entry `main` also runs with inert stdin/stdout and an exclusive-file
output double. Separate cuts observe terminal-confirmed evidence construction
(including the final 991-byte slot conversion still in `execute.raw`), JSON text
before ASCII encoding, encoded bytes after encoding, and the file-write argument
before return. Evidence slots hold hexadecimal strings, each twice the original
frame length, plus digest strings. `records` aliases `evidence.slots`; main's
encoded bytes alias the write argument. The evidence-bound encoding in execute
and the later main encoding are separate invocations: the retained encoding
observations are the final invocation, and no cross-invocation identity claim is
made. JSON text and encoded bytes are separate cuts and cannot be added together.

The instrumentation stores numeric identities, types, quantities and literal
role/ownership labels. Temporary traversal/frame references are dropped on return;
observer wrappers and the file double add harness references, never production
ownership or release evidence. Graph roots shared with the independent caller
are labeled as such. Neither scalars nor the independently retained output stream
are counted as production copies. A malformed one-byte JSON request refuses
before evidence-file creation. Evidence short-write and flush failures return 3
after one file open/write attempt, preserving uncertainty without retry. Existing
readback and terminal-response write/flush controls retain their original checks.

These scoped representations narrow earlier omissions. They do not complete
parser/base64/encoder transient inventories, all binding/start/ACK cuts, arbitrary
caller/stream retention, headers, allocator capacities, physical release or the
whole simultaneous physical-copy bound. Original `.103` remains attempted-unknown;
`.104` remains held. Every physical quantity stays unknown, fit unproved and
admission false.

The explicit `/2` SOURCE successor changes the preparation implementation and selected
binding digest. The same unchanged lifetime observer executes that selection; its stack
frame names, locals, aliases, transfer and failure checks remain required. Equal-length
digest substitution does not by itself prove unchanged graph sizes: the selected journey
and backing-capacity checks must run. Historical `/1` observations and the joint copy
ledger retain their original scope; they are not measurements of the future private
CAP03 composition. No observer hash or historical accounting input changes in this
successor, and no private-provider or physical-fit result follows from these controls.
