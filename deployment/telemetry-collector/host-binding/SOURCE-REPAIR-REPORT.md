# OPS-TYPED-01.5 bounded input and profile repair

**Status:** source repair tested on 2026-10-01. Protected recipe adoption,
Sandbox rendering, wrapper adoption, and a native attempt remain pending root
work.

This successor addresses the two blockers in
`/tmp/ops-typed-01-5-host-binding-independent-review-20261001.md`, SHA-256
`a3437c2e183fe721ece2de03168a12883fe33f088ce6d20933e17bc61e9f6be0`.
The reviewed predecessor `4a53d1d1a02455564f6c757b3e073f059f40875f`
and its report remain unchanged.

## Repairs

The verifier reads at most 66 bytes from standard input. The only accepted frame
is exactly 64 lowercase hexadecimal ASCII bytes, optionally followed by one LF.
It does not trim whitespace or allocate in proportion to input. The Python
adapter applies the same 64-byte shape check before encoding or starting the
compiled verifier. Oversized, padded, CRLF, multi-record, and malformed frames
refuse before private-directory creation or authentication access.

Profile validation now mirrors the constants and constraints in the existing
`native-operation-v1.schema.json`: every closed object field set, scalar kind,
operation, supported operation list, provider/model/effort/prompt, native
identity and digests, producer identity and coherent digest, runtime limits and
paths, and network policy value is checked. `configSha256` retains its schema
hex-digest rule. No new ceiling was invented. A malformed native digest with a
fresh matching source pin and clean Git commit refuses.

Git subprocess capture now drains stdout and stderr concurrently into fixed byte
buffers. Overflow kills the owned process tree, a ten-second producer deadline
kills and joins it, invalid UTF-8 refuses, and no captured content enters the
closed error. Controlled exact-bound, stdout-flood, stderr-flood, and sleeping
child tests cover the boundary.

## Verification

- cold locked restore and Release build with .NET SDK 10.0.401;
- 12 F# tests, including exact clean malformed-profile fixtures and bounded
  child-process probes;
- 36 private qualification tests, including real compiled oversized-frame tests
  and seven pre-materialization probes: valid-to-sentinel, old profile,
  profile mutation, source-pin mutation, source revision drift, producer mutation,
  and missing producer;
- exact current profile and four immutable native payload bytes preserved;
- Python compilation and `git diff --check`.

No actual admission, auth, private wrapper, runtime, container, native operation,
API, publication, or remote effect is used or claimed.
