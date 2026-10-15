# Actual publisher sink copy witness

The current publisher sink permits a caller whose serialized overlap exceeds
ADR-0093's 65,536-byte cap, although each report satisfies its 16,384-byte limit.
This is a constructed allowed-caller counterexample to inferring fixed-slot fit
from that limit. It is not an observed deployed allocation or a complete
managed/native population census.

The [test](sink-copy-contract-tests.py) invokes the actual
[`persist_authentication_snapshot`](../../scripts/release-successor-publish.py)
with the existing inert callback binding and legal ready/issuance frames, re-encoded
through the current provider. A report containing the existing result schema and
14,984 ASCII padding bytes is accepted by the sink. The filesystem double copies
writes into independent `bytearray` file stores and returns independent read bytes.
It intercepts the actual `os.replace` call after the temporary file's flush/fsync,
while the old and temporary file stores still exist. The live sink's local byte
objects are inspected at that cut; retaining references adds no counted byte copy.
The test checks five distinct identities and equality of the corresponding file
and memory content.

| Distinct backing store at the pre-replace cut | Bytes |
| --- | ---: |
| Existing ready report file | 16,384 |
| Temporary issuance report file | 16,215 |
| Sink `previous_bytes` holding the old report | 16,384 |
| Sink serialized new `body` | 16,215 |
| Incoming framed issuance bytes | 972 |
| **Lower-bound total** | **66,170** |

The ready frame is 1,141 bytes and the issuance frame is 972 bytes, including
framing. Both reports fit independently. Their simultaneous lower bound exceeds
the policy cap by 634 bytes before counting parsed reports, bindings, other frames,
ACKs, readback copies, runtime objects, metadata or allocated filesystem blocks.
The double establishes the source-permitted copy topology and exact serialized
lengths; it does not measure production filesystem blocks or runtime heap usage.
A working-copy exclusion would still require the bounded lifetime, lack of authority
and separate finite memory proof required by the [accepted policy](../../docs/adr/0093-original-103-authentication-domain-policy.md).
The old/temp/prior/new representations cannot simply be assigned the proposed
fixed slots without an actual mapping and simultaneous-population proof.

Controls retain the existing per-report overflow refusal and wrong-order refusal.
An injected replace failure preserves distinct old/temp files and the prior report.
An injected readback mismatch returns no receipt and leaves the caller's ready
projection unchanged even though the stored candidate is issuance; another request
with that stale projection refuses before writing. These are uncertainty controls,
not recovery or retry authority. The tests neither authenticate a durable ACK nor
establish filesystem barriers, original-clock continuity or an actual failure's
commit outcome.

The established authentication suite loads these five tests through
`domain-contract-tests.py`. The accepted 57,562-byte proposed serialized model,
its maximum vectors and the immutable callback pairing profile remain separate.
Interface freeze and native admission remain held. Original `.103` remains
attempted-unknown and unreplayed; `.104` remains held. This witness admits no
credential, service, release or replay effect.
