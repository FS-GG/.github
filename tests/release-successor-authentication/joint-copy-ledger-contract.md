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
| Protected [Coordination PR1001](https://github.com/FS-GG/FS.GG.Coordination/pull/1001), `a235822e181dfb8e1aa42affaa1e7f734f2847d0`, [managed witness](https://github.com/FS-GG/FS.GG.Coordination/blob/a235822e181dfb8e1aa42affaa1e7f734f2847d0/tests/FS.GG.Coordination.Orchestration.Execution.Tests/ManagedOwnershipCaptureTests.fs) | Completed writes preserve 13 original-request aliases and 13 deliberate endpoint copies after task settlement. Async blocked and synchronous blocked cases each preserve one alias/copy pair and outstanding custody at their respective cuts. | Merged at 2026-10-15 23:01:11 UTC. Reviewed head `88b09cb7823935a048cec057ce87b6c1dbaf303c` passed [validation38817219968](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38817219968) and [bootstrap38817219946/a1](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38817219946). Independently retrieved `compiler-and-tests` artifact12012516290 includes `finite-custody-inert.trx`: 55 executed/passed, zero failed or not-executed, including all three capture cases; SHA-256 `0d6ca056bb5e9551b42ff04c0c13f02f3f6a09c2e2d0af8fbff04ef2d90117b3`. Protected capture source/project Git blobs match reviewed head (`31abccfe77297bdd37be0017e6a0e2bd67118ad6` / `a0fd046117b64665b87ed99479b98f6c0472ff4e`); capture source SHA-256 remains `cf5a374297598dbbf22cdf83b5a1b8a2255a79d0361e844bd6d96fff70d30d31`. Earlier [run38807111675/a1](https://github.com/FS-GG/FS.GG.Coordination/actions/runs/38807111675) and reported 55/55 belong to superseded head `80dbe782` and remain historical. Counts are source witness populations without a complete byte bound; each cut stays separate. |
| [Public accepted host review, comment6202693564](https://github.com/FS-GG/.github/issues/4517#issuecomment-6202693564) | Four modeled entries at selected successful core readback map to two scoped model byte objects: candidate/stable/readback alias; old is distinct and remains in the active commit frame until return. | This is a pure model with an incompatible topology. Its public comment body is hashed as a mutable-source snapshot, without a Git revision claim. No private source, packet, receipt or numeric subtotal is included. Modeled roles do not establish actual deployed objects. |

Protected [Coordination PR1002](https://github.com/FS-GG/FS.GG.Coordination/pull/1002)
at `165c22cf764e4d9c14895cc920097f9c52828ea3` is a separate accepted
consumer input. Its [closed source pin](https://github.com/FS-GG/FS.GG.Coordination/blob/165c22cf764e4d9c14895cc920097f9c52828ea3/eng/utel-slot-callback-source.json)
selects `.github` producer `b0f528e2cd23d9f314a4af679de6da3f4e07262c`
and the unchanged 16-role bundle. The [managed transport journey](https://github.com/FS-GG/FS.GG.Coordination/blob/165c22cf764e4d9c14895cc920097f9c52828ea3/tests/FS.GG.Coordination.Orchestration.Execution.Tests/FiniteCustodyTransportTests.fs)
requires three child ACKs, six exact retained slot frames/digests, a confirmed
terminal response, owner exit while input remains open, and settled managed tasks.
It records no input EOF/trailing-input check, operational receipt or native identity.
The protected transport consumer and separately protected PR1001 capture establish
their respective source controls;
neither adds a transport copy cut. The five previously accepted cuts and their
identity scopes remain unchanged;
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

The Python extension adds five separate selected cuts without changing the original
16-object subtotal: selected binding/identity/parsed/previous value roots at sixth
readback, terminal-confirmed evidence construction, JSON text before ASCII encoding,
ASCII bytes after encoding, and evidence-file write before return. The ledger records
root representations and the evidence slot hex/digest values; the observer also
walks their selected value graphs to establish alias distinctions. Containers have
`object-count: 1`; strings use UTF-8 logical length. No dictionary, list or tuple
count is converted to bytes. Evidence construction still retains the last raw slot
conversion. JSON text and ASCII bytes are distinct cuts, as are construction and
write. The retained encoder observations belong to the final main encoding; the
earlier bound-check encoding is not added. Main encoded bytes and the write argument
are the same object within the write cut. Instrumentation and file-double references
are harness ownership and are excluded. Malformed JSON refuses before evidence
creation; short evidence writes and evidence flush failures preserve uncertainty
after one attempt, without retry. Existing backend readback and terminal response
write/flush controls remain. The regression reruns actual main and checks selected
roles, typed quantities, ownership labels and every pair of alias classes within
each snapshot; it never compares object IDs across cuts. Current witness source
provenance supersedes the old witness pin in the local ledger while the protected
PR4578 evidence above remains historical. The unchanged sixteen-role runtime bundle
and original `.103` attempted-unknown / `.104` hold remain intact.

The protected [Coordination PR1005](https://github.com/FS-GG/FS.GG.Coordination/pull/1005)
source at `2af47b82f95dea136e9d9f4c2c8edd0be7f90987` adds a separate
managed-parent group at ordinal-six Python readback, before release. Its
[field census](https://github.com/FS-GG/FS.GG.Coordination/blob/2af47b82f95dea136e9d9f4c2c8edd0be7f90987/tests/FS.GG.Coordination.Orchestration.Execution.Tests/TerminalCutCorrelationTests.fs)
has SHA-256 `531d296e2a670f2c1a3c2729ae092a5482ccec6cc85efca9beda9ea847a80669`.
Atlas `coordination/managed-held-cut-backing-roots`, protected by
[PR1006](https://github.com/FS-GG/FS.GG.Coordination/pull/1006) at
`eb007772a44b7d1edb3dab3bdd11ba2157047d0d`, indexes the same source bytes.
The original ten groups, observations and aliases remain intact. This additional
source-controlled group has explicit topology/run/process/attempt/cut labels;
it does not authenticate a native process or join a different witness execution.

| Selected managed backing | Distinct arrays | Backing capacity bytes |
| --- | ---: | ---: |
| Six snapshot frames | 6 | 5,682 |
| Result and callback parser inputs | 2 | 2,048 + 512 |
| Result and callback binding copies | 2 | 994 + 994 |
| Start frame | 1 | 1,123 |
| Selected backing subtotal | 11 | 11,353 |

`backing-capacity-bytes` records byte-array lengths, independently of logical
lengths, object counts and physical-byte lower bounds. Only observed identities
may carry capacity. Summaries deduplicate scoped backing classes and expose
`known_object_backing_capacity_bytes`; absent capacity remains `null`. Capacity
never determines fit, including a supplied capacity above the policy cap.

Six payload-backing roles alias the six snapshot arrays. These roles name the
underlying byte arrays, not the `ReadOnlyMemory` views. The 4,835 payload slice
bytes are excluded from the capacity subtotal and are not another allocation.
Twenty-six distinct original-request/endpoint-copy array identity witnesses stay
disjoint from the eleven selected backings. Their object counts claim no byte
inventory. The thirteenth original request alone records its verified 295 logical
bytes; the discovered current-request role aliases that same object and contributes
no additional bytes. The bounded field walk discovers twelve arrays: the eleven
selected backings plus that existing request. The full ledger group names 37
objects because it also retains the other endpoint identity witnesses; it does
not claim all 37 were discovered by the field walk. No endpoint byte total is
inferred or combined with the backing subtotal.

The offline regressions bind these rows to the pinned source assertions, including
all eleven capacities, six payload backing aliases, the current-request alias,
endpoint disjointness and exact provenance/scope/cut. Wrong capacity, false alias,
changed provenance, process or cut, and payload-as-allocation mutations fail those
regressions. The generic reconciler checks consistency; it does not authenticate
arbitrary remote source claims. Cross-cut aliases refuse and separate cuts have
no combined capacity or byte total.

Two later source observations add separate groups using the existing ledger grammar:

| Source observation | Selected capacities | Identity boundary |
| --- | --- | --- |
| Protected [Coordination PR1007](https://github.com/FS-GG/FS.GG.Coordination/pull/1007), `1cc2af9a5e4ba65cd2db39afbc86cfdb7ca8029e`, [ACK/read census](https://github.com/FS-GG/FS.GG.Coordination/blob/1cc2af9a5e4ba65cd2db39afbc86cfdb7ca8029e/tests/FS.GG.Coordination.Orchestration.Execution.Tests/TerminalCutCorrelationTests.fs) | One 199-byte ACK array and one 2,048-byte child-read array: **2,247 backing-capacity bytes**. | Earlier phase-four ACK write is pending, before return. Read call five waits for ordinal four; confirmed ordinal remains two and phase six stays queued. |
| Protected [Coordination PR1009](https://github.com/FS-GG/FS.GG.Coordination/pull/1009), `6970fd73a6fecafde61d403713c16fb5b0f7aa63`, [callback-reader census](https://github.com/FS-GG/FS.GG.Coordination/blob/6970fd73a6fecafde61d403713c16fb5b0f7aa63/tests/FS.GG.Coordination.Orchestration.Execution.Tests/TerminalCutCorrelationTests.fs) | One callback-reader array: **one backing-capacity byte**. | Python ordinal-six readback remains held while the actual callback stdout read is pending, after the selected backing census. |

Atlas `coordination/managed-held-ack-child-read-roots`, protected by
[PR1008](https://github.com/FS-GG/FS.GG.Coordination/pull/1008) at
`b81a598512a273da4e888c8fbf032e160396c906`, cites the earlier source.
Its test SHA-256 is
`4c52908092508e241878113aa5338783a3ea756d35f87612ad8993910ed4afaa`;
the protected PR1008 test and README bytes are identical to PR1007. The callback
source SHA-256 is
`8e34fd991e9e0eb858fb59d225ad1b6d39eb6972b8f0b781a05458714e1e985c`,
equal to reviewed PR1009 head `ec3aaeded1266b3fcf56c2a14e34e35377a37af6`.

The earlier cut aliases each supplied memory backing to its exact production
state bytes/buffer. Both actual pending stream tasks are incomplete, and named
state/channel/awaiter joins bind the observations. The two arrays are mutually
distinct and distinct from the result parser input and eighteen endpoint arrays
(nine original requests and nine copies). Those eighteen endpoint identities have
object-count quantities, giving twenty classified distinct arrays with the two
capacity arrays. The parser role has `object-count: 1` and **`alias_class: null`**:
source does not establish whether it aliases an endpoint array. No twenty-first
distinct array, parser capacity or parser-endpoint alias is inferred. Its separation
from the two selected buffers remains evidence in the cited source contract.

The terminal callback cut aliases the actual supplied read-memory backing to the
production callback-state buffer. The observer forwards identical memory and token
once to actual Python stdout. Memory length one and offset zero are separate facts,
not added byte quantities. The observer work and underlying stream tasks are distinct
and incomplete; named continuations join the production bridge/awaiter and the
wrapper's underlying task/awaiter. The retained reader is incomplete. The ledger
asserts no additional equality between that retained reader task and a continuation
task. Generated state names, bounded ancestor lookup and changed/unavailable field
structure belong to the producer's fail-closed witness, not a consumer scanning
fallback. Clearing the pending observation after settlement establishes no physical
release.

That one-byte backing is distinct from eleven selected backings and twenty-six
endpoint arrays. These thirty-seven identities use object-count quantities in the
new callback group: thirty-eight distinct arrays are witnessed, with only one byte
of capacity recorded there. The older 11,353-byte group retains its original
PR1005 provenance and is not repinned, merged or added into this later observation.
The two new groups have separate full topology/run/process/attempt/cut and source
provenance. Their labels identify controlled source witnesses, not authenticated
execution IDs. No alias, lifetime, combined peak or physical-fit conclusion crosses
these observations.

The extension preserves the previous 143 observations, thirteen aliases and eight
missing obligations as equal prefixes, then adds the two groups and scoped remaining
obligations. Earlier source-specific gaps remain historical evidence; later scoped
observations do not erase them. Regressions bind the exact capacities, identities,
partial parser identity, ownership, source pins and cuts. Wrong capacities, invented
parser distinctness, missing endpoints, changed provenance/process/cut, task bytes
and cross-cut aliases or merged provenance are rejected by the source regression
or reconciler. The maintained consumer controls use pinned source evidence and inert
Python paths; they do not execute the producer's CLR/Python subprocess journey.

For these inputs every physical lower bound remains unknown and **fit is unproved**.
There is no successful-fit result or runtime admission. Missing caller/evidence/
restart/uncertainty populations, metadata, allocator capacity and native enforcement
remain explicit. Removal of references, callback completion, task settlement,
return, timeout, cancellation or restart does not prove collection, zeroization or
physical release. The ledger accepts no retry/reuse/release authorization fields;
original uncertain/consumed attempts remain held by the source contracts. No
barrier/readback/clock authenticity, signer/ACK custody, installed adoption or
original `.103`/`.104` effect is qualified.

Reusable consumer findings belong in the owning `.github` Atlas archive after
accepted source delivery, through a separately scoped capture. The three-file pending-reader
extension does not edit Atlas or the roadmap.

## Selected Python bytearray backing capacity

Protected [PR4588](https://github.com/FS-GG/.github/pull/4588), commit
`a6d63d2657ed15c66ca46b07a66ebd25f5789799`, supplies a separate ten-row group
from the [capacity companion](https://github.com/FS-GG/.github/blob/a6d63d2657ed15c66ca46b07a66ebd25f5789799/tests/release-successor-authentication/slot-backing-capacity-tests.py)
(SHA256 `a831e911089a322ccdc5bb1add8b8da5ec3219c2adfb5bb09b5554f89b9221b6`).
It reuses the real synchronous Python callback journey at
`sixth-readback-selected-value-graphs`, joining nine backend bytearrays and the
decoder buffer to their original identities, contents and logical lengths.

| Selected role | Observed backing capacity bytes |
| --- | ---: |
| phase-1 through phase-6 | 1142, 973, 831, 918, 832, 992 |
| old, candidate, readback | 832, 992, 992 |
| decoder_buffer | 318 |

The ten mutually distinct built-in bytearrays contribute **8822 backing-capacity
bytes**, read directly through `bytearray.__alloc__`. Their **8790 logical bytes**
are a separate source-comparison assertion, excluded from the capacity group's
logical subtotal. The nine backend buffers account for 8504 capacity bytes versus
8495 logical bytes; the decoder accounts for 318 versus 295. No temporary row is
added: each stable phase keeps its former temporary identity without a second
simultaneous allocation. Stable backend, staging backend and decoder ownership
remain separate role labels.

These vectors were observed on local CPython 3.14.8 and hosted CPython 3.12.3
([run 38848183268](https://github.com/FS-GG/.github/actions/runs/38848183268)).
They are immutable source-witness inputs, not universal capacities inferred from
length. The consumer reruns the protected pure journey, checks the producer hash,
records actual implementation/version/build, and compares every role's capacity,
identity, type and logical length with the live scalar census. A changed runtime
vector fails visibly for review; it does not regenerate these inputs. Unsupported
runtime/capability semantics fail through the producer's unchanged controls.
Capacity includes CPython buffer overallocation and its trailing byte, excluding
object headers, allocator rounding, resident memory and unselected populations.

The scope is topology `python-terminal-backing-capacity-controlled`, run
`pr4588-capacity-source-witness`, process `python-in-memory-owner`, attempt
`original-controlled-attempt`. These are controlled source-witness labels, not
authenticated native execution identities. Ten alias classes correspond exactly
to ten observed identities within this one synchronous run. No alias reaches the
older Python logical group or any managed/Python cut. Matching role names, stage
labels or runtime vectors establish no cross-run simultaneity. The unchanged
parser rejects two quantity types for one same-object alias class; no duplicate
identity, repinning or grammar exception is used to record both quantities.

The extension preserves the protected predecessor's 205 observations, sixteen
aliases and ten missing obligations as exact prefixes. Canonical prefix digests
and deliberate drift controls protect all three arrays. Ten appended observations
produce fourteen separate groups and 215 observations; aliases remain sixteen.
One scoped missing obligation is appended. The new group has a known capacity
subtotal, null logical subtotal and null physical lower bound. There is no combined
capacity or byte total. Even synthetic capacity above 65536 leaves fit unproved
and admission false; a possible future cap change requires complete accounting
and is not justified by this selected group.

Adversarial controls reject missing/extra roles, wrong capacity, logical length
used as capacity, false aliases among equal-length candidate/readback/phase-six
objects, wrong type/ownership, source hash/revision drift, changed scope/cut,
unsupported identity basis, old-run aliases and group collapse. Original reader
and terminal groups keep their earlier pins and quantities. No CLR/native
producer run, complete population, physical release, native barrier/clock/custody,
installation, replay or original-effect acceptance follows. Original `.103` stays
attempted-unknown/unreplayed and `.104` held. Atlas capture belongs to the existing
owning follow-up after source acceptance, without competing catalog edits.


## Finite upper-bound preparation

`upper_bound_profile` is an optional, separate `fsgg.copy-upper-bound/1` input.
The existing observations, aliases, missing obligations and fourteen group results
remain unchanged. Omitting this field preserves the complete historical result
shape. Including it adds only `upper_bound`; it never changes historical `fit`,
selects a cap or grants admission. The shipped pending profile has no selected
runtime, source proof, closed inventory or component bound. Its result is
`unresolved` with `physical_upper_bound_bytes: null`.

A complete profile must name its version, exact topology and runtime implementation,
version, build, architecture, allocator and storage geometry. Its inventory explicitly
covers authority, managed, Python, backend, peer, transport/kernel/cache,
diagnostic/export and observation populations. Each domain needs a nonempty closed
role inventory; a null inventory remains unknown. A component assigns each role
exactly once to a disjoint physical allocation class, with a positive maximum
multiplicity and five separate upper-bound metrics: capacity, object header,
allocator rounding, storage metadata and transport/cache bytes. An applicable term
may be zero only with the reviewed contract's supporting argument. Omitted metrics
refuse; null metrics or multiplicity leave accounting unresolved. Logical byte sizes,
observed capacities, object counts and physical lower bounds cannot substitute for
any upper-bound metric.

Every component binds its exact profile/runtime/topology, immutable Git source
citation and cumulative lifetime through the operation and uncertainty. Reservations
must be owned and supported by an explicit proof. Unknown or expired ownership and
missing reservation proof remain unresolved. The schema admits no GC, task completion,
timeout, cancellation or restart release discount. Multiplicity must conservatively
cover cumulative allocations and all uncertain replacements. Transient exclusions
require a separate finite budget, bounded non-authority lifetime and reviewed proof
under the completeness obligations; they cannot be created by an extra JSON field.

Completeness obligations cover the closed call graph, every representation,
headers/rounding/storage, uncertainty/replacement, concurrency/restart, diagnostic
recipient population, transient exclusions and runtime/source layout. Missing or
unknown obligations, source citations or proof leave accounting unresolved. A
component may map several roles to one allocation only as a reviewed shared
allocation. Equal bytes, names or sizes never establish that identity. Unknown
alias relationships must use separate bounded allocation classes conservatively.

Source authenticity is a separate protected-code boundary:
`REVIEWED_UPPER_BOUND_CONTRACTS` in the evaluator is initially empty. A future
protected source review must pin the exact profile identity and canonical SHA256
of the **whole** reviewed profile there, including its source citations, inventory,
component values, alias mapping, runtime, lifetimes and completeness proofs. Input
JSON cannot add a registry entry. A syntactically valid Git citation, caller-provided
hash, `proved` status or claimed review Boolean supplies no trust. A changed term,
source or omitted population invalidates the pin. Adding a production pin requires
review of authentic immutable source and the complete accounting argument, rather
than simply copying the digest of a producer's claim. This source window adds no
production pin and accepts no production finite bound.

Only complete, trusted profiles produce `finite-complete` and a physical upper bound
`sum(maximum_multiplicity * sum(five metrics))`. Arithmetic is checked against the
signed 64-bit accounting range for every term and sum; overflow refuses. Partial
sums are never exposed as a bound. The maxima need not occur at one observed cut:
this is conservative complete-profile accounting, independent of the historical
observation groups. It is neither measured occupancy nor an authenticated native
runtime assertion. The result always has `admission: false` and has no cap or fit
field. Selection and protection of a justified finite cap, actual runtime/profile
qualification, enrollment/custody and effect admission remain their owning gates.

The synthetic tests inject a test-only reviewed-contract registry to exercise full
arithmetic and shared allocations. This fixture is explicitly synthetic and does
not authenticate any producer. Adversarial controls cover missing domain/role/
overhead, unknown copies, expired ownership, lower-bound/capacity substitutions,
false aliases, incompatible runtime/topology, arithmetic overflow and self-asserted
review/source claims. Canonical digests preserve all 215 historical observations,
sixteen aliases and eleven missing obligations. Original `.103` remains
attempted-unknown/unreplayed and `.104` held; no retention or cutoff is renewed.
