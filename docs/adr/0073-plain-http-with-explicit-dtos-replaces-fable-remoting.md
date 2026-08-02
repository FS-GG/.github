# ADR-0073: Plain HTTP with explicit versioned DTOs replaces Fable.Remoting for `fable-game`'s typed request/response

- **Status:** Accepted
- **Date:** 2026-08-02
- **Amends:** [ADR-0071](0071-two-web-workspace-providers-one-template-package.md)
  §5 (Communication boundaries) and §6 (Toolchain, release, and activation). §5's
  Fable.Remoting clause and §6's Fable.Remoting pin entry are **superseded**; every
  other clause of ADR-0071 — including §242's rejection of "Use only
  Fable.Remoting", which this record does not revisit — **remains in force**.
- **Affects:** FS.GG.Templates (producer of `fs-gg-fable-game`), FS-GG/.github
  (registry, wizard, Coordination board), `EHotwagner/S.I.R.` (first forcing
  consumer)

## Context

[.github#2082](https://github.com/FS-GG/.github/issues/2082) is the operator
decision this record executes: **amend ADR-0071 to drop the Fable.Remoting
contract for `fable-game`**, decided 2026-08-01 in favour of the option fully
inside FS-GG's control, over waiting on an unacknowledged upstream report or
shipping a downgraded compiler pin. See the decision comment thread on
[FS.GG.Templates#348](https://github.com/FS-GG/FS.GG.Templates/issues/348) for
who decided, when, and why the third option was chosen over the other two.

The evidence is measured, not re-derived here. `FS.GG.Templates#370` and
[`FS.GG.Templates/docs/reports/2026-08-01-fable-full-stack-toolchain-compatibility-spike.md`](https://github.com/FS-GG/FS.GG.Templates/blob/main/docs/reports/2026-08-01-fable-full-stack-toolchain-compatibility-spike.md)
established, by bisection with negative controls, that `Fable.Remoting.MsgPack`'s
`module Fable` branch of `Write.fs` calls private, non-`inline` helper functions
from `inline` functions — a pattern Fable's compiler did not reject before
5.5.0's `fix(all): Error on inline function referencing private value`
([fable-compiler/Fable#4701](https://github.com/fable-compiler/Fable/pull/4701),
closing [fable-compiler/Fable#3866](https://github.com/fable-compiler/Fable/issues/3866))
and correctly rejects from 5.5.0 onward. The defect is present, unchanged, in
every published `Fable.Remoting.MsgPack` version 1.0.0–2.0.0 and current
`master`; `tests/fable-toolchain-spike/run.sh` (in FS.GG.Templates)
reproduces it fail-closed on every run. The only upstream report,
[Zaid-Ajaj/Fable.Remoting#396](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/396),
has been open and unanswered since 2026-07-06, filed by an unaffiliated reporter,
not the maintainer.

ADR-0071 §5 made Fable.Remoting one of two deliberately separate adapters:

> - **Fable.Remoting** owns typed HTTP request/response operations such as
>   initial data, commands that return one result, and ordinary application
>   queries.
> - **ASP.NET Core SignalR** owns connection/session-oriented traffic such as
>   input, snapshots, presence, acknowledgement, reconnect, and resync.

§242 rejected "Use only Fable.Remoting" *"because reconnect, presence,
streaming state, backpressure, and resync are connection concerns"* — an
argument about what Fable.Remoting **cannot** do, which has nothing to do with
whether Fable.Remoting is what should do the request/response half. That
reasoning is untouched: it is the reason SignalR keeps owning connection-
oriented traffic below, unchanged by this record. What has changed is that the
request/response half's chosen mechanism no longer compiles under the platform's
pinned Fable compiler, on a defect this org does not own and cannot fix.

## Decision

### 1. What is superseded

ADR-0071 §5's first bullet — *"**Fable.Remoting** owns typed HTTP
request/response operations such as initial data, commands that return one
result, and ordinary application queries"* — is superseded. ADR-0071 §6's pin
set — *".NET SDK, Fable, FSharp.Core, Node, npm package manager/lockfile
format, Elmish/view binding, **Fable.Remoting**, and `@microsoft/signalr`"* —
is superseded to the extent it names Fable.Remoting: a generated `fs-gg-fable-game`
workspace no longer pins `Fable.Remoting.Client`, `Fable.Remoting.Server`, any
`Fable.Remoting.*` ASP.NET Core adapter (Giraffe/Saturn/plain), or their
transitive `Fable.Remoting.MsgPack` dependency.

Nothing else in §5 or §6 is superseded. SignalR's ownership of connection/
session-oriented traffic, the narrow hand-written binding over the official
`@microsoft/signalr` npm client, the rejection of the stale `Fable.SignalR`
NuGet package, the "no v1 contract added to FS.GG.Net" clause, and the rest of
§6's coherent-set qualification discipline all stand unchanged.

### 2. The replacement: plain HTTP endpoints with explicit versioned DTOs

`fs-gg-fable-game` now uses two deliberately separate adapters, not one:

- **Plain ASP.NET Core HTTP endpoints** (ordinary minimal-API or framework
  handlers on the same server the template already generates — no new server
  process, no new port) own typed request/response operations: bootstrap,
  metadata, commands that return one result, and ordinary application queries.
  Each operation is an explicit, versioned request/response DTO pair, encoded
  and decoded by named codec functions on **both** the .NET and Fable sides
  (for example, matched `Thoth.Json.Net`/`Thoth.Json` encoders and decoders, or
  an equivalent explicit-codec library qualified by the toolchain spike) —
  never reflection- or attribute-driven automatic serialization, and never a
  generated RPC proxy. The client calls these endpoints with the browser
  `fetch` API (or a thin Fable binding over it); there is no client-side proxy
  generation step to depend on.
- **ASP.NET Core SignalR** continues to own connection/session-oriented
  traffic exactly as ADR-0071 §5 already decided: input, snapshots, presence,
  acknowledgement, reconnect, and resync, through the same narrow, template-
  owned, executable-tested binding over the official `@microsoft/signalr`
  npm client.

This keeps ADR-0071's own two-adapter shape — one mechanism for finite typed
request/response, a separate connection-oriented mechanism for everything that
needs a live channel — and changes only *which* mechanism carries the first
half. §242's reasoning is what makes that split the right one to keep: nothing
about the compile failure bears on whether request/response and connection-
oriented traffic should be one mechanism or two, so this amendment does not
reopen that question.

### 3. The wire-contract discipline survives unchanged

Dropping Fable.Remoting must not become dropping the typed boundary it used to
enforce by generating one. It does not:

- Every request/response operation has an explicit DTO type on both sides —
  primitive fields, arrays/lists, and a named version field where a persisted
  session or replay could outlive a deployment, per ADR-0071 §5's existing
  requirement and the spike report's boundary decision (`docs/reports/2026-08-01-fable-full-stack-toolchain-compatibility-spike.md`
  §"Boundary and view-layer decision").
- Domain discriminated unions are **mapped** to DTOs at the HTTP boundary by
  explicit code, never assumed serializer-compatible and never sent as-is.
  Fable.Remoting's automatic MsgPack encoding is not replaced by a different
  automatic encoding; it is replaced by codec functions a contributor writes
  and a test exercises, which is a **stronger** version of the discipline
  ADR-0071 already required, not a weaker one.
- Every DTO is tested by encoding from .NET and decoding in the browser (and
  the reverse), including a case that is expected to be **rejected** — the
  spike report's "explicit rejected arbitrary-DU case" — so that "serializer-
  compatible" is demonstrated per DTO, not assumed for the type system as a
  whole.
- Neither adapter becomes the domain model or the persistence boundary. That
  clause of ADR-0071 §5 is unchanged.

### 4. What this does not touch

This is settled independently of Fable.Remoting's upstream status. If
[Zaid-Ajaj/Fable.Remoting#396](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/396)
is fixed upstream tomorrow, that does **not** by itself reopen this decision:
plain HTTP with explicit, hand-written codecs is adopted here on its own
merits — no RPC-proxy-generation dependency, no reflection-driven wire format,
one fewer third-party package whose Fable-target correctness this org cannot
verify or fix — not merely as a workaround for the compile failure. Revisiting
it would need a fresh forcing case (for example, measured contributor cost of
hand-written endpoints and codecs at a scale where that cost dominates), argued
in a new ADR the normal way; an upstream fix alone is not that case.

### 5. Downstream consequences

- **[FS.GG.Templates#348](https://github.com/FS-GG/FS.GG.Templates/issues/348)**
  ("Implement the server-authoritative Fable game web template") is re-scoped
  by this record, exactly as its own `Blocked by` note already anticipates: its
  transport acceptance criteria ("one Fable.Remoting call... exercised") must
  be rewritten against §2 above (one plain-HTTP typed request/response call,
  one SignalR real-time flow) before that item is authored. This record does
  not implement #348; it unblocks it.
- **The `fable-game` provider contract** (ADR-0071 §5's decision text, and
  wherever the registry/wizard eventually describes the provider) no longer
  names Fable.Remoting as a dependency, and any pin list that currently
  includes it — including the "not yet registry-active" description in
  `docs/architecture.md` — must be corrected to match §1 above. This record
  makes that correction in `docs/architecture.md` in the same change (see
  below); the registry itself carries no live `fable-game` entry yet
  (ADR-0071 §6's rollout has not reached activation), so there is no registry
  row to edit today.
- **`tests/fable-toolchain-spike/run.sh` and its spike report** live in
  FS.GG.Templates (`Paths: tests/fable-toolchain-spike/ docs/reports/`), not
  in this record's own `Paths: docs/adr/ docs/architecture.md`, so this ADR
  does not edit them directly. It does obligate FS.GG.Templates to update them
  to reflect that the Fable.Remoting route is no longer the template's
  contract — **without** deleting the assertion that records the upstream
  defect. The defect is real, independently reproduced, and worth keeping as
  a pinned regression fixture even though `fs-gg-fable-game` no longer takes
  a hard dependency on the library it lives in: a guard that goes green by
  deleting the evidence would silently relicense a library nobody re-qualified.
  That update is tracked as a FS.GG.Templates-owned follow-up, not executed
  here.

## Consequences

- `fs-gg-fable-game` no longer depends, directly or transitively, on
  `Fable.Remoting.Client`, `Fable.Remoting.Server`, any `Fable.Remoting.*`
  ASP.NET Core adapter, or `Fable.Remoting.MsgPack` — the upstream defect
  stops being a template-blocking dependency rather than being fixed.
- Contributors write and test one codec function pair per DTO instead of
  getting proxy generation for free; that is more boilerplate per operation,
  traded for removing an unmaintained-relative-to-official-client-adjacent
  dependency class and a whole compiler-incompatibility risk that a future
  Fable release could reintroduce even after a hypothetical upstream fix.
- The two-adapter shape ADR-0071 §5 designed — one mechanism for request/
  response, a separate one for connection-oriented traffic — is preserved.
  §242's reasoning for keeping them separate is reaffirmed by this record
  rather than revisited.
- FS.GG.Templates#348 gains a clear, unblocked transport contract to author
  against; it was not implementable against a contract that does not compile.
- FS.GG.Templates owes a follow-up to `tests/fable-toolchain-spike/run.sh` and
  its report that records the new contract without erasing the upstream
  defect evidence already gathered. This record names that obligation; it
  does not close it.

## Alternatives considered

- **Wait for upstream to fix `Fable.Remoting.MsgPack`.** Rejected: the only
  report, [Zaid-Ajaj/Fable.Remoting#396](https://github.com/Zaid-Ajaj/Fable.Remoting/issues/396),
  has been open and unanswered since 2026-07-06 with zero comments, filed by
  an unaffiliated reporter rather than the maintainer. This would mean
  freezing `#348 → #347 → #349 → .github#2070 → S.I.R.#138` on an
  unacknowledged third-party report, not a fix in progress.
- **Adopt the Fable 5.4.0 compiler downgrade as an interim template pin.**
  Rejected: `FS.GG.Templates#370` substantially validated this route (a real
  Fable.Remoting round trip, DU encode/decode, SignalR push, forced reconnect,
  cancel-safety) but it deliberately disables a real Fable compiler safety net
  (`fable-compiler/Fable#4701`, which exists to catch a real class of bug) as
  a workaround for someone else's unfixed defect, and the validation was not
  complete — no production publish, no bundle size, no dev watch, no
  Playwright two-client scenario, no rejected-malformed-DU case. Shipping a
  template pinned to a downgraded compiler for this reason was rejected by the
  operator on 2026-08-01.
- **Carry request/response over the existing SignalR channel**, dropping the
  request/response mechanism distinction entirely. Rejected: this is ADR-0071
  §"Alternatives considered"'s **"Use only SignalR"** alternative, already
  rejected there *"because routine typed request/response APIs would be
  encoded as session messages and lose the simpler HTTP/RPC lifecycle"* — a
  reason the Fable.Remoting compile failure does not touch. Bootstrap,
  metadata, and save operations would become messages tied to a live
  connection's lifetime, losing independent per-call timeout/retry, ordinary
  HTTP caching semantics, and the ability to test a request/response operation
  without standing up a socket. Nothing about this amendment's forcing case —
  a third-party library defect in the request/response leg — is a reason to
  revisit that rejection.
- **Fork or vendor a patched `Fable.Remoting.MsgPack`.** Rejected: the defect
  is a correctness bug the Fable compiler now (correctly) rejects, present
  across the library's entire published history and current `master`, with no
  maintainer engagement on the one upstream report. Taking on long-term
  maintenance of a forked third-party serializer, to preserve automatic
  encoding this record replaces with codecs FS-GG already required to write
  explicitly at the wire boundary, is a disproportionate ongoing cost for a
  library whose only remaining job — automatic MsgPack encoding — this record
  no longer needs.
- **Select a different Fable.Remoting client/server version pair.** Rejected
  on the evidence already gathered: `FS.GG.Templates#370`'s bisection shows
  the defect is unchanged across every published `Fable.Remoting.MsgPack`
  version, 1.0.0 through the current 2.0.0, and current `master` — there is no
  version to select that avoids it.
