# ADR-0093 inert encoding construction

This is a proposed source contract and executable bounded model, with no production
adapter. Run `python3 tests/release-successor-authentication/domain-contract-tests.py`.
The existing authentication suite also loads these tests. No cryptographic positive
vector, durable backend, installed trust or native admission follows from a pass.

`domain_contract.py` defines closed fields and canonical compact ASCII JSON. Objects
reject duplicate, extra and missing keys and noncanonical encodings. Integers are
bounded; signatures are canonical unpadded base64url encodings of exactly 64 bytes.
The proposed algorithm is Ed25519 (RFC 8032), with the distinct exact domain prefixes
in `signing_bytes`. No signer/library/key is selected or constructed. Committed
maximum vectors contain placeholder signatures and are explicitly unauthenticated.

The descriptor includes the complete original candidate, all nine original dependency
roles, both explicit compatible dependency rows and the complete original checkpoint.
Those fixed values are extracted from protected publisher source without executing it.
Publisher workflow revision/run provenance remains in the original checkpoint and
legacy binding; the separate `authentication` object binds the executing authentication
workflow revision/run/attempt. Changing a workflow path in the existing binding is not
this extension.

The descriptor signs both the exact canonical legacy binding digest and the digest of
the full ready frame. The ready frame retains that complete binding, including source,
process, original clock and App tuple; no required original is replaced by an absent
hash witness. These relations avoid a descriptor/frame signature cycle. Checkpoints
reference the prior phase ACK and controls reference the prior checkpoint. The current
external ACK authenticates the current checkpoint, frame, descriptor, original ordinal,
phase, challenge, enrolled generation, purpose and original-clock bounds.

The legacy child ACK remains a distinct four-byte BE-u32 framed `b,h,k,n,v` object.
The proposed external ACK is separately framed; its maximum is 510 bytes including
its four-byte length. A trusted adapter would need to authenticate the descriptor and
external receipt against independently enrolled identities, verify pending purpose,
challenge, generation, exact original process/binding/frame/checkpoint and clock, then
join the existing typed readback. `receipt_matches` only models the conditional
predicate; setting its authentication booleans in a test supplies no authenticator.
Existing TakeAck/ConfirmAckSent full-write and deadline requirements remain obligations.
The separate framed diagnostic read response has closed keys `v,k,evidence` and
a 546-byte maximum. It is not the existing 512-byte callback retain response.
Diagnostic read returns exact retained bytes in `DiagnosticEvidence`; it cannot pass
the effect predicate, restamp a receipt, renew state or generate a replacement gate.

`legacy_bounds` gives a structural upper bound for every legal legacy phase. It takes
the longest encoding of every enum and nullable field independently, even when their
combination is semantically impossible. Decimal maxima dominate null for nullable IDs;
null dominates the one-digit token slot and three-digit HTTP status. Fixed-width source
and UUID fields cannot escape or grow. The maximum complete ready frame is 1,291 bytes;
the separately retained StartFrame is 1,273, including both four-byte length headers.
Every other structural phase bound is smaller. This preserves the 2,048-byte ABI.
Structural upper vectors are deliberately distinguished from executable legal ready
bytes. Regenerating vectors is a development action, never acceptance of source drift.

The proposed serialized retained population is **57,282 bytes**, leaving 8,254 of
65,536 bytes. The generated accounting is exhaustive for this proposed slot model:

| Category | Simultaneous copies | Maximum bytes each |
| --- | ---: | ---: |
| Enrollment | 4 | 508 |
| Signed descriptor | 4 | 3,055 |
| Exact phase frames | 16 | 1,291 |
| External ACK | 16 | 510 |
| Baseline and six checkpoints, plus old/temp/candidate extras | 9 | 591 |
| Host core control, old/temp/candidate | 3 | 543 |
| Peer irreversible control, old/temp/candidate | 3 | 377 |
| Three legacy gate ACKs on each side, plus two staging copies | 8 | 199 |
| Append-once loss and expiry, each stable plus candidate | 4 | 427 |
| Independent same-token reserved DELETE/204 observation, stable plus candidate | 2 | 508 |
| Framed diagnostic response with exact embedded receipt | 1 | 546 |
| Original StartFrame | 1 | 1,273 |

Enrollment and descriptor are create-once immutable objects: one host original and
its distinct temp/candidate copies plus one immutable peer copy. Each side may retain
six exact phase and external ACK originals, with at most two staging extras per side.
A single mutation lock per owner fences the candidate population. No seventh logical
ordinal exists. Core uncertainty preserves all original/temp/candidate bytes and
irreversibly refuses further phase/core/gate mutations. Separate loss and expiry slots
remain append-once after uncertainty; an uncertain observation write consumes that slot
and cannot retry. The same-token observation binds descriptor, reservation challenge,
ordinal, method/path/token slot, 204, original-clock bounds and independently enrolled
custodian signature. The hash `e` commits retained uncertainty members for loss/expiry;
it never asserts which ambiguous write committed. First failure and original holds
survive; expiry cannot authorize deletion when age continuity is unknown.

The model explores every write/barrier/readback/sign/reserve/take/send/consume cut,
loss/restart, expiry, duplicate candidate and seventh-ordinal refusal. Its clock is an
abstract conservative upper bound in elapsed original seconds. Authentication of the
mapping from real original ticks, finite uncertainty and restart/rollback behavior is
an unimplemented native obligation. No effect is admitted in settlement (285–300) or
at/after total cutoff. Reads can remain diagnostic after cutoff.

## Exact remaining acceptance blocker

**This is not a complete population-fit proof for the current managed/native path.**
The original pinned `PhaseResultSnapshot` retains distinct frame and canonical payload
arrays; accessors allocate further copies. Its callers, callback tasks, queue, sink,
readback, serialization and restart paths have not been joined to this proposed slot
algorithm. All simultaneous copies surviving callbacks as authority or uncertainty
count, including memory. Seven additional retained copies of the maximum legal ready
frame alone add 9,037 bytes, exceeding this construction's 8,254-byte headroom. The
protocol accessors permit those allocations; this is a possible-copy counterexample
to inferring a bound from the API, not an assertion that a current caller keeps seven.
The actual source-owner copy census/lifetime algorithm is required before interface
freeze. A short-lived working array can be excluded only with a proved bounded-call
lifetime, lack of authority and separate finite memory bound. Filesystem metadata,
allocated blocks, runtime allocations, native enforcement, actual signed positive
vectors, enrolled trust, barrier/readback and original-clock/loss proof remain open.
No uncounted wrapper, durable callback transcript, signing log, journal, receipt or
retained accessor copy may be added under this fit claim. Any such addition requires
recomputing the same population; arbitrary replacement/retry is never admitted.
