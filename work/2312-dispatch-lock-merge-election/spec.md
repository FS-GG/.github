# Per-Receiver Dispatch Lock And Lease-Free Merge Election Specification

- **Work id:** 2312-dispatch-lock-merge-election
- **Stage:** specify
- **Status:** specified
- **Item:** [.github#2312](https://github.com/FS-GG/.github/issues/2312) — slice 2 of [.github#1858](https://github.com/FS-GG/.github/issues/1858)
- **Governing design:** `docs/reports/2026-08-04-github-native-executor-fencing-design.md` §3, §4.1, §4.2, §6.3, §11.2, §11.3, §12.5

## User Value

On 2026-07-28 two executors ran one item to completion concurrently under **one** claim marker, and six
repositories received pull requests from an unlocked executor. The lock did not malfunction: both executors
were the same worker, in the same session, on the same claim, and every GitHub-visible fact about them was
identical.

The value this slice delivers is the substrate that makes those two executors **distinguishable to a fence
without comparing their identities at all** — a per-receiver lock whose holder is decided by a
server-assigned comment id, and one ordering rule that every reader in the engine computes identically.

It delivers no user-visible verb. Six further slices are written against what it fixes, so its value is
measured by whether those slices can be authored against a stable identity and a single ordering rule.

## Scope

- **S-001** A `opLockRef` table in `Options.fs`/`Options.fsi`, beside `choreLockRef`, covering **all eight**
  roster repositories, `FS.GG.Net` included.
- **S-002** Acquire and release for that lock, composed from `Writes.claimScoped` with the lock ref and stub
  callbacks, in `Client.fs`.
- **S-003** One new exported lease-free ordering function in `Reads.fs`/`Reads.fsi`.
- **S-004** Conversion of the four existing hand-rolled copies of "lowest id wins" onto that function.
- **S-005** The ADR that records §3's identity model and §4.1's third application of the item CAS.
- **S-006** The eight `[op-lock]` issues themselves, created closed, unlocked and off-board.

## Non-Goals

- **N-001** **Writing** the merge-election marker. That is slice 3 (`delivery` posts the election, then the
  PR authorization marker). This slice supplies the ordering rule the election reads through; it does not
  post one.
- **N-002** The merge-gate workflow and its six checks. Slice 4.
- **N-003** The broker workflow and receiver-side validation. Slices 5 and 6.
- **N-004** Any change to the CAS write path. §4.1 forbids a new function, prefix, field or parameter, and
  this slice's own acceptance requires demonstrating it added none.
- **N-005** Authenticating *which* executor is the rightful claim holder. That is `#1938`'s unroutable
  harness boundary; this slice deduplicates under a generation and claims nothing more.
- **N-006** A new CLI verb. Nothing in the item asks for one, and adding one would put an unfenced surface
  in front of operators before the fence's own callers exist.

## User Stories

- **US-001** As a broker about to dispatch against a receiver, I can ask whether anyone else is dispatching
  against that receiver right now, and get an answer that two contexts of one session cannot both receive.
- **US-002** As a CI job evaluating a merge minutes after a push, I can compute the same winner the pusher
  computed, from a fact that cannot change between being written and being read.
- **US-003** As an engineer reading `who`, `reap` or `adopt`, I find one ordering rule with one
  implementation, so that changing it changes every consumer together.
- **US-004** As the operator onboarding a ninth repository, I am told mechanically that it has no operation
  lock, rather than discovering it after an unfenced dispatch reaches it.

## Acceptance Scenarios

- **AC-001** `opLockRef` resolves a ref for **every** `FS-GG`-owned row of `registry/repos.yml`, and the
  proof is derived from that roster rather than from a list restated in a test.
- **AC-002** The completeness proof is non-vacuous: if the roster parse stops matching, the suite reds on
  the parse rather than passing over an empty list.
- **AC-003** Acquire and release call `Writes.claimScoped` with the lock ref, and `Writes.fs`'s write path
  is unchanged — demonstrated by diff and by the CAS's own tests passing unmodified.
- **AC-004** Exactly one exported lease-free ordering function exists in `Reads`, and no file in
  `src/FS.GG.Coord.Cli` re-implements "lowest id wins".
- **AC-005** Breaking the exported rule changes consumers' behaviour, demonstrated by execution and not by
  inspection.
- **AC-006** The four pre-existing copies are converted; the "provably one implementation" claim is made
  only if all four are, and is otherwise scoped explicitly with survivors named.
- **AC-007** A receiver with no lock ref is REFUSED, and the refusal costs no network request.
- **AC-008** The eight lock issues are closed, not locked, carry no labels, and are on no board — verified
  by reading GitHub, not by intending it.
- **AC-009** Acquiring the lock makes zero GraphQL calls, which is the mechanical form of "the lock issue is
  off-board, so `#516`'s one-item-per-worker check cannot see it".
- **AC-010** The ADR amending ADR-0027 and extending ADR-0041 lands with this slice and passes
  `adr-coherence`, including both ends of both amendment links.

## Functional Requirements

- **FR-001** `Options.opLockRef: Ref list -> string -> string -> Ref option`, owner-gated to `FS-GG` for the
  embedded table, consulting an injected roster first, canonicalising its input and its output.
- **FR-002** The embedded table carries eight rows, one per roster repository.
- **FR-003** `None` is the fail-closed answer and callers must refuse on it.
- **FR-004** `Reads.lowestId: Marker list -> Marker option`, applying no lease filter and sorting its input.
- **FR-005** `Reads.winner` is `lowestId` composed with the staleness filter; `Reads.reserver` falls back to
  `lowestId`; `who`, `reap` and `adopt` call it.
- **FR-006** `Client.OpLock.acquire` returns a typed refusal that distinguishes "no lock for this receiver"
  from "somebody else holds it", because those need opposite responses.
- **FR-007** `Client.OpLock.release` takes the capability, so a marker nobody holds cannot be dropped by
  naming it.

## Ambiguities

- **AM-001** Whether the separable clause of §4.2 (converting all four copies) is adopted. **Resolved** in
  clarifications DEC-001: adopted, because the design records the disposition as *"absorbed into slice 2"*
  and the election would otherwise have made a fifth copy.
- **AM-002** Whether `claim` or `claimScoped` is the right callee. **Resolved** in DEC-002: `claimScoped`,
  because the item's acceptance criterion 2 names it explicitly and the two stubs are visible at the call
  site, which is what §4.1 calls the configuration.
- **AM-003** Whether creating the eight lock issues is in scope given the operator's standing
  sole-filing-authority directive. **Resolved** in DEC-004.

## Public Or Tool-Facing Impact

`Reads.fsi` and `Options.fsi` gain one exported value each. Both `FS.GG.Coord.GitHub` and
`FS.GG.Coord.Cli` are `IsPackable=false` and ship only inside `FS.GG.Coord.Cli`'s pack, so these are
internal library signatures rather than a published API surface. No CLI verb, flag, or wire schema changes,
so `renderCommandContract`'s output is byte-identical.

## Lifecycle Notes

Blocked by slice 1 ([.github#2311](https://github.com/FS-GG/.github/issues/2311)), which landed as
`a8f343ad` and supplies `Operation.OpKey` and the closed operation vocabulary this slice's election is keyed
on. Slices 3–8 are written against what this fixes.
