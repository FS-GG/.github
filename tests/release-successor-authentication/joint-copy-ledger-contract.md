# Joint source copy and lifetime ledger

Run `python3 tests/release-successor-authentication/joint-copy-ledger-tests.py`
or the maintained authentication entry `python3 tests/release-successor-authentication/run.py`.
`python3 tests/release-successor-authentication/joint_copy_ledger.py` emits the
observations, supported alias deductions, separate cut summaries and missing
obligations. These are offline controls with inert streams; no CLR, subprocess,
network, credentials or native effects are launched.

The ledger reconciles evidence for the [owning policy](../../docs/adr/0093-original-103-authentication-domain-policy.md)
and [remaining simultaneous-copy census](../../docs/roadmaps/utel-release-successor.md#original-103-authentication-domain-policy--2026-10-15).
It preserves Atlas `github/original-103-authentication-domain-policy` as the
curated knowledge pointer. No interface freeze or operation admission follows.

Each closed observation records source type, repository, exact revision/path and
SHA-256 (or a public-comment snapshot digest with no claimed Git revision), exact
topology/run/process/attempt scope, selected cut, role, representation, quantity
type, ownership, alias class, identity basis and uncertainty. Parsed observations
and provenance are frozen values. Scope labels name the controlled source witness;
they are not authenticated runtime/process identifiers or reusable object IDs.
The source digest binds attribution, not authenticity or acceptance of arbitrary
input. Local source hashes are rechecked before CLI output; remote and comment
snapshots are pinned evidence inputs and are not fetched during tests.

| Evidence | Selected observation | Limits |
| --- | --- | --- |
| Protected [.github PR4578](https://github.com/FS-GG/.github/pull/4578), `d84e66bc2eafc44b6f53e7ef0b20ed6f1477cc95`, [Python witness](https://github.com/FS-GG/.github/blob/d84e66bc2eafc44b6f53e7ef0b20ed6f1477cc95/tests/release-successor-authentication/slot-copy-lifetime-tests.py) | Six stable bytearrays plus old/candidate/readback, actual/pending, decoder/entry/previous reply: **16 distinct live objects, 11,660 logical bytes** at sixth backend readback. Append raw/owner frame alias pending. | One synchronous source cut. The regression reruns the actual controlled journey and checks every recorded length and all 16 identities. Harness, runtime and other populations remain excluded. |
| Candidate [Coordination PR1001](https://github.com/FS-GG/FS.GG.Coordination/pull/1001), `88b09cb7823935a048cec057ce87b6c1dbaf303c`, [managed witness](https://github.com/FS-GG/FS.GG.Coordination/blob/88b09cb7823935a048cec057ce87b6c1dbaf303c/tests/FS.GG.Coordination.Orchestration.Execution.Tests/ManagedOwnershipCaptureTests.fs) | Completed writes preserve 13 original-request aliases and 13 deliberate endpoint copies after task settlement. Async blocked and synchronous blocked cases each preserve one alias/copy pair and outstanding custody at their respective cuts. | Exact-head requalification is pending at this recorded frontier ([validation38817219968](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38817219968), [bootstrap38817219946](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38817219946)). The capture source SHA-256 remains `cf5a374297598dbbf22cdf83b5a1b8a2255a79d0361e844bd6d96fff70d30d31`. Earlier [run38807111675/a1](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38807111675) success and reported 55/55 TRX belong to superseded head `80dbe782`; they do not qualify the new head. PR remains open. Counts are source witness populations, without a complete byte bound; each cut stays separate. |
| [Public accepted host review, comment6202693564](https://github.com/FS-GG/.github/issues/4517#issuecomment-6202693564) | Four modeled entries at selected successful core readback map to two scoped model byte objects: candidate/stable/readback alias; old is distinct and remains in the active commit frame until return. | This is a pure model with an incompatible topology. Its public comment body is hashed as a mutable-source snapshot, without a Git revision claim. No private source, packet, receipt or numeric subtotal is included. Modeled roles do not establish actual deployed objects. |

Protected [Coordination PR1002](https://github.com/FS-GG/FS.GG.Coordination/pull/1002)
at `165c22cf764e4d9c14895cc920097f9c52828ea3` is a separate accepted
consumer input. Its [closed source pin](https://github.com/FS-GG/FS.GG.Coordination/blob/165c22cf764e4d9c14895cc920097f9c52828ea3/eng/utel-slot-callback-source.json)
selects `.github` producer `b0f528e2cd23d9f314a4af679de6da3f4e07262c`
and the unchanged 16-role bundle. The [managed transport journey](https://github.com/FS-GG/FS.GG.Coordination/blob/165c22cf764e4d9c14895cc920097f9c52828ea3/tests/FS.GG.Coordination.Orchestration.Execution.Tests/FiniteCustodyTransportTests.fs)
requires three child ACKs, six exact retained slot frames/digests, a confirmed
terminal response, owner exit while input remains open, and settled managed tasks.
It records no input EOF/trailing-input check, operational receipt or native identity.
This protected transport consumer does not accept PR1001's candidate capture or add
a sixth copy cut. The five existing cuts and their identity scopes remain unchanged;
no combined physical-copy fit or native acceptance follows.

The Python logical subtotal is `5682 + 831 + 4*991 + 3*295 + 298 = 11660`.
Its 18 role observations include two additional aliases, which contribute no
additional bytes. The source supports distinct Python candidate/readback objects;
the host model supports their aliasing within its own scope. Equal lengths or
contents do not reconcile that difference. Managed populations, Python lengths,
host modeled entries, 57,562 serialized bytes, 23,552 reserved store bytes and the
historical padded-report overflow witness are never added into a joined subtotal.
The unchanged 16-role source manifest retains bundle
`b73ce545011cc1156bbcf49f2bb98f174e33b970d221b32c0bad6e83b21b5862`;
all source-role hashes and frozen vectors remain unchanged.

Aliases require the same topology, run, process, attempt, cut, provenance,
representation, identity basis and explicit scoped object class. Unknown identity
cannot become an alias. A source cut with conflicting provenance or identity basis
refuses; cross-source correspondence remains an obligation. Repeated roles for
one object count once; inconsistent quantities refuse. Every quantity has an
explicit type. Unknown quantities remain `null`; absent populations do not become
zero. Logical byte lengths never establish a physical bound. A supplied, distinct,
compatible declared physical-byte lower bound above 65,536 yields
`supplied-bound-overflow`, conditional on that supplied premise. The parser does
not validate physical measurements; the label establishes no measured overflow.
The synthetic regression does not add an observed population.

For these inputs every physical lower bound remains unknown and **fit is unproved**.
There is no successful-fit result or runtime admission. Missing caller/evidence/
restart/uncertainty populations, metadata, allocator capacity and native enforcement
remain explicit. Removal of references, callback completion, task settlement,
return, timeout, cancellation or restart does not prove collection, zeroization or
physical release. The ledger accepts no retry/reuse/release authorization fields;
original uncertain/consumed attempts remain held by the source contracts. No
barrier/readback/clock authenticity, signer/ACK custody, installed adoption or
original `.103`/`.104` effect is qualified.

Reusable finding capture remains with the programme integrator: update the existing
Atlas record after semantic review and native source delivery; this lane's bounded
five-file touch-set excludes Atlas and roadmap edits.
