# ADR-0052: Onboard FS.GG.Net as a transport component, and model wire contracts in the registry

- **Status:** Proposed
- **Date:** 2026-07-19
- **Affects:** FS-GG/.github (registry, roster, architecture), new repo FS-GG/FS.GG.Net; downstream consumers are app repos (SC2 client, BAR client), not FS-GG components.

## Context

FS-GG has no networking/transport concern anywhere today (confirmed: every "transport/client/server" hit is either the coordination engine's GitHub API plumbing or explicit *contrast* text in ADR-0033/architecture.md). Two concrete first uses drive the need:

1. **A custom StarCraft II client** talking to the SC2 headless server. That protocol (per `Blizzard/s2client-api`) is **raw Protocol Buffers over a WebSocket** — *not* gRPC — strictly request/response (`Request`/`Response`), single message pair at a time.
2. **A custom Beyond All Reason client** talking to a **gRPC** proxy the author owns.

The through-line is **protobuf serialization**; the transports differ (WebSocket vs gRPC/HTTP2). The component must be reusable beyond games and must reach a *stable* surface that rarely changes — which is only achievable if the volatile, externally-owned message schemas stay *out* of the stable core.

Substantial prior art exists as agent skills in `EHotwagner/fsGRPCSkills` (three approaches — FsGrpc, standard Grpc.Tools, protobuf-net code-first — with 83 validated tests and hard-won F# gotchas). Every one of those skills assumes a **gRPC channel frames the bytes**. The SC2 case has no gRPC channel: it needs *protobuf-serialize-to-raw-bytes + WebSocket framing + request/response correlation*, which none of the existing skills cover. That gap is the net-new work.

Relevant existing decisions: ADR-0022 (extract a component), ADR-0023 (onboard a born-elsewhere component — the closest precedent), ADR-0015 (registry-schema is itself a governed contract), ADR-0020 (platform/component/workspace vocabulary), ADR-0007/0012/0013 (publishing, OIDC, reproducible restore).

## Decision

### 1. A new standalone component repo, `FS-GG/FS.GG.Net`

Onboarded per **ADR-0023** (born-elsewhere → transfer → register), **not** extracted per ADR-0022. It is a **bottom-layer sibling** that "reaches up to nothing" — depends on no FS-GG component — so the one-way acyclic rule (architecture.md §"the one rule") holds trivially. It is domain-neutral (no game types), so other applications can consume it. Naming keeps the `FS.GG.*` convention; ".Net" reads unambiguously here because the whole platform is .NET/F#.

### 2. A two-tier seam, and a deliberately *small* stable surface

The honest abstraction is **two tiers**, not "WebSocket vs gRPC as sibling transports":

- **Tier 1 — `ITransport`**: an ordered, message-framed **duplex byte channel**. WebSocket implements it. gRPC does **not** — gRPC owns its own framing/dispatch and sits above this tier.
- **Tier 2 — `IMessageChannel<'Req,'Resp>`**: a typed protobuf **request/response exchange** over a transport, with a pluggable **correlation strategy**. This is the piece with no off-the-shelf .NET equivalent, and the SC2 substrate.

We explicitly **reject** a unifying `IRpcChannel` that hides both WS-protobuf and gRPC behind one method-dispatch interface (an earlier idea). SC2 and BAR are different apps with no shared app code, so the unification earns almost nothing, and forcing gRPC's method model behind a generic channel fights grpc-dotnet. What the two channel kinds *share* is only the **connection lifecycle** (`ConnectionState`) and the **Elmish `Cmd`/`Sub` idiom** — and that is all the core unifies. Keeping the shared surface thin is what lets the version stay stable.

### 3. Package decomposition (coherent set on `$(FsGgNetVersion)`)

| Package | Responsibility | Depends on |
|---|---|---|
| `FS.GG.Net.Core` | pure: `ConnectionState` FSM, `ITransport` seam, `IMessageChannel<'Req,'Resp>` + `CorrelationMode`, `IMessageCodec<'T>` seam, reconnect/backpressure policy types. No sockets, no protobuf. | BCL only |
| `FS.GG.Net.WebSocket` | **client-only** `ITransport` over `System.Net.WebSockets.ClientWebSocket`, with fragment reassembly, pooled receive buffers, and initial connect-retry. **The net-new work; SC2 substrate.** | Core |
| `FS.GG.Net.Protobuf` | `IMessageCodec<'T>` impls: Google.Protobuf (`ToByteArray`/`ParseFrom`) and protobuf-net (with the F# registration gotchas turned into API). | Core |
| `FS.GG.Net.Grpc` | thin lifecycle + Elmish wrapper over grpc-dotnet / protobuf-net.Grpc. Does **not** reimplement gRPC. BAR substrate. | Core |
| `FS.GG.Net.Elmish` | `Cmd` (exchange) + `Sub` (incoming/stream) bridge — one idiom for both channel kinds. Mirrors `FS.GG.Audio.Elmish`. | Core |

Sketched seams (illustrative `.fsi`-level):

```fsharp
// FS.GG.Net.Core
type ConnectionState = Disconnected | Connecting | Connected | Closing | Faulted of exn

/// Tier 1: an ordered duplex channel of COMPLETE application messages.
/// The unit is a whole message, not a wire frame — the WebSocket impl reassembles
/// protocol-level fragments (continuation frames) internally before yielding one.
type ITransport =
    inherit System.IAsyncDisposable
    abstract State   : ConnectionState
    abstract Send    : message: System.ReadOnlyMemory<byte> * ct: System.Threading.CancellationToken -> System.Threading.Tasks.ValueTask
    abstract Receive : System.Collections.Generic.IAsyncEnumerable<System.ReadOnlyMemory<byte>>   // complete inbound messages

/// Optional id-echo the correlator can use to bind a response to its request.
type IdEcho<'Req,'Resp> =
    { Stamp : 'Req -> uint64 -> 'Req     // return the request carrying this id (SC2 Request.id = 97)
      Read  : 'Resp -> uint64 }          // read the echoed id off the response (SC2 Response.id = 97)

/// How a response is matched to its request.
type Correlation<'Req,'Resp> =
    /// One request in flight; response matched by arrival order. When an IdEcho is
    /// supplied the correlator stamps a monotonic id and ASSERTS the response echoes it —
    /// a cheap desync guard. `None` fits raw protobuf-over-WS protocols with no id field.
    | Sequential  of IdEcho<'Req,'Resp> option
    /// Many requests in flight; response matched by echoed id (required).
    | Multiplexed of IdEcho<'Req,'Resp>

/// Serialization seam — one impl per protobuf stack.
type IMessageCodec<'T> =
    abstract Encode : 'T -> System.ReadOnlyMemory<byte>
    abstract Decode : System.ReadOnlyMemory<byte> -> 'T

/// Tier 2: typed protobuf request/response over a transport. The SC2 substrate.
/// SC2 fits `IMessageChannel<Request, Response>` exactly — one oneof-envelope each way.
type IMessageChannel<'Req,'Resp> =
    inherit System.IAsyncDisposable
    abstract State    : ConnectionState
    abstract Exchange : 'Req * System.Threading.CancellationToken -> System.Threading.Tasks.Task<'Resp>
    abstract Incoming : System.Collections.Generic.IAsyncEnumerable<'Resp>   // unsolicited server messages (empty for SC2)
```

**SC2 correlation = `Sequential (Some idEcho)`** — single in-flight (matches the game's lockstep step→observe→act loop) but id-verified, so a lost/misordered response surfaces as an error instead of silently feeding stale observations into the agent. SC2 pushes nothing, so `Incoming` stays empty; any message arriving with no request outstanding is a protocol error worth raising. The SC2 `Response.status`/`error` fields are *payload* the app maps — the core never sees them, preserving the schema-lives-in-the-app boundary.

**v1 transport scope — client-only, no server host.** Both first uses are *clients*: for SC2 the game **is** the WebSocket server; for BAR the proxy is a gRPC server. So v1 ships no WebSocket server. A fake WS echo server for testing `Net.WebSocket` lives in the **test project**, not the shipped surface. An inbound/server WebSocket transport becomes a later additive package (`FS.GG.Net.WebSocket.Server`) when a real use case appears — keeping the stable core from committing to a server API it hasn't validated. `Net.WebSocket` *does* ship **initial connect-retry with backoff** in v1, because SC2 boots its process and only then starts listening on `ws://127.0.0.1:<port>/sc2api`; mid-session reconnect is deferred (a dead SC2 instance isn't reconnectable anyway).

### 4. Codegen policy: reproducibility wins for external schemas

- **SC2 (external, Blizzard-owned `.proto`)** → **Google.Protobuf / Grpc.Tools** codegen, accepting C# interop types at the SC2 boundary — because its `ToByteArray`/`ParseFrom` raw API is exactly what WebSocket framing needs, and it restores reproducibly under FS-GG's locked-restore/ApiCompat publishing model. Generated code is **committed** (per the existing skill guidance), sidestepping any CI-toolchain dependency.
- **BAR / owned schemas** → FsGrpc (idiomatic F# records/DUs) or protobuf-net code-first, at the author's discretion — this is where FsGrpc's idiom pays off and its from-source-plugin toolchain is acceptable.
- FsGrpc is thereby **demoted from the default** it holds in `fsGRPCSkills` to the owned-schema/gRPC case, because its toolchain (`FsGrpc.Tools` not on nuget.org; `protoc-gen-fsgrpc` built from a pinned source commit) is at odds with a *stable, feed-published* component.

### 5. The stability boundary is the design

`FS.GG.Net` knows **no `.proto` and no game types**. All message schemas — SC2's (Blizzard, bumps per patch) and BAR's (owned, evolving) — live in **separate app repos** that depend on `FS.GG.Net`. Blur this line (pull SC2 types into the core "for convenience") and the core re-releases every SC2 patch. Hold it and a stable `$(FsGgNetVersion)` is realistic.

### 6. Registry: model wire contracts as a new provenance dimension

Today `dependencies.yml` models F# `.fsi` **API surfaces** + package versions. Networking adds a **wire-contract** dimension the registry does not yet capture, in **three provenances**:

1. **Vendored external `.proto`** (SC2) — FS-GG does not own it; track the vendored upstream ref/version as a mirror, versioned independently of `FS.GG.Net`'s source version.
2. **Owned `.proto`** (possibly BAR) — FS-GG owns the wire contract; field-number/reserved rules are the compatibility surface.
3. **Code-first protobuf-net surface** (possibly BAR) — *no `.proto` artifact*; the F# `[<ProtoContract>]` types **are** the wire contract.

This is an **additive registry-schema growth** and therefore, per **ADR-0015** (its §3 same-change procedure superseded by **ADR-0037**: no PR spans two repos), bumps `schemaVersion` + `registry-schema.version` and advances the SDD CLI validator pin in two ordered PRs (SDD teaches `Fsgg.Registry` + ships a CLI first; `.github` bumps + pins after). The `FS.GG.Net` source/package rows themselves follow the ordinary `fs-gg-audio` template (owner `net`, `package-version` leads the flip — publish-before-flip, FR-007). Consumer edges (SC2/BAR → net) are app-repo edges, added when a consumer really pins.

### 7. Skills: library eats the type-gotchas, skills carry the operational knowledge

`fsGRPCSkills` is absorbed and re-homed into `FS.GG.Net`, split by what can vs cannot be encoded in a library:

- **Encoded as library API** (consumers can't trip): per-record protobuf-net registration, `array` not `list`, `Dictionary` not `Map`, `option` via `protobuf-net-fsharp`, DU `[<ProtoInclude>]`. `FS.GG.Net.Protobuf` exposes a registration helper and a codec surface that makes the wrong collection types unrepresentable.
- **Carried in skills** (can't be encoded): the buf / `protoc-gen-fsgrpc` install and `buf generate` workflow, "commit generated code", version pins, and the three-approach decision tree.

Ships one umbrella `fs-gg-product-net` skill (mirroring `fs-gg-product-audio`) plus sub-skills re-homed from `fsGRPCSkills` — `fs-gg-net-setup`, `fs-gg-net-proto`, `fs-gg-net-codefirst` — and one **net-new** skill, `fs-gg-net-websocket`, covering the gap: protobuf-over-WebSocket, framing, correlation modes, and the SC2 handshake recipe. The 83 fsGRPCSkills tests migrate as the seed suite for `FS.GG.Net.Protobuf`/`.Grpc`.

## Consequences

- The one-way rule is preserved by construction; `FS.GG.Net` is a fourth bottom layer beside `FS.GG.Game.Core` and the `FS.GG.Audio.*` set.
- The registry-schema bump is a real cross-repo obligation on FS.GG.SDD before `.github` can record wire contracts — sequence it on the Coordination board.
- `docs/architecture.md` must be reconciled (new component + the wire-contract registry dimension) after the registry update.
- App repos (SC2, BAR) carry the volatile schemas and their regeneration; they are not FS-GG components and do not gate the coherent set.
- First stable cut (`0.1.0`) should follow the SC2 vertical slice proving the whole new stack end-to-end against a real headless server — that is the evidence the seams are right before freezing them.

## Alternatives considered

- **Package inside FS.GG.Rendering or FS.GG.Game.** Rejected: same reasoning ADR-0022/0023 used for Audio/Game — a subsystem this size is not a flag, and it would break render-independence and (for Game) domain-neutrality. Networking must be reusable beyond games.
- **Extract from an existing gRPC repo (FSBarV2/HighBarV3) per ADR-0022.** Rejected for now: forces a donor SemVer-major and frozen-profile cost while those apps keep working; greenfield-onboard is cheaper and keeps the apps as *consumers*.
- **A unifying `IRpcChannel` over both WS-protobuf and gRPC.** Rejected: no shared app code to justify it; hiding gRPC's method model behind a generic channel fights grpc-dotnet. Unify only lifecycle + Elmish idiom.
- **FsGrpc as the default codegen (as fsGRPCSkills recommends).** Kept for owned schemas; rejected as default for the *stable published core* because its from-source, not-on-nuget toolchain is at odds with reproducible restore. Google.Protobuf for external schemas.
- **Do both WebSocket and gRPC in the first slice.** Rejected: build the seam + one transport (WebSocket, driven by SC2) first so the seam isn't accidentally shaped by whichever came first.
