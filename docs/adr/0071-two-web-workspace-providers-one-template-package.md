# ADR-0071: Two web workspace providers, one template package

- **Status:** Accepted — §5's Fable.Remoting clause and §6's Fable.Remoting pin
  entry are **superseded by [ADR-0073](0073-plain-http-with-explicit-dtos-replaces-fable-remoting.md)**;
  §242's rejection of "Use only Fable.Remoting" and every other decision in
  this record remain in force.
- **Date:** 2026-08-01
- **Amended by:** [ADR-0072](0072-console-and-fable-bindings-are-separate-workspace-providers.md)
  §1 and §6 widen the unpublished package and wizard selector to include the
  independent `console` and `fable-bindings` providers. All web/game-specific
  decisions in this record remain in force.
- **Amended by:** [ADR-0073](0073-plain-http-with-explicit-dtos-replaces-fable-remoting.md)
  (2026-08-02, [.github#2082](https://github.com/FS-GG/.github/issues/2082)) —
  §5's Fable.Remoting bullet and §6's Fable.Remoting pin entry are superseded:
  the upstream `Fable.Remoting.MsgPack` defect that blocks `fable-game`'s Fable
  compiler pin (`FS.GG.Templates#370`) is unfixed and unacknowledged, so plain
  HTTP endpoints with explicit versioned DTOs now own typed request/response
  instead. SignalR's ownership of connection-oriented traffic, the narrow
  `@microsoft/signalr` binding, the rejection of `Fable.SignalR`, §242's
  rejection of "Use only Fable.Remoting", and every other clause of §5/§6
  stand unchanged — see ADR-0073 §1 for the precise cut.
- **Affects:** FS.GG.Templates (producer), FS.GG.SDD (provider contract and
  lifecycle), FS.GG.Game (shared lockstep contract and game skills),
  EHotwagner/S.I.R. (first consumer), FS-GG/.github (`new-sdd-workspace`,
  registry, and Coordination board)

## Context

FS-GG needs two related but different browser workspace shapes:

- a neutral F# ASP.NET Core server with a plain TypeScript/Vite website; and
- a game-specialized F# ASP.NET Core server with a Fable/Elmish browser
  application, shared qualified F# logic, typed request/response calls, and a
  real-time session channel.

The existing wizard has one implicit application shape. It fetches the
`rendering` provider and treats `--profile` as that template's shape selector.
There is no published FS-GG web template package, `web` provider, or
`fable-game` provider. Overloading `--profile` with a second meaning would make
the selected producer implicit and would couple unrelated parameter
vocabularies.

The general web architecture report selected plain TypeScript as the neutral
browser baseline. It also named meaningful shared application logic as a
bounded reason to reconsider Fable. That reason now exists for game clients,
but it does not invalidate TypeScript as the independent, low-prescription web
baseline or as the browser Scene-conformance implementation.

Game already publishes the
`fs-gg-game-core-fable-lockstep-v1` compatibility profile under
[ADR-0069](0069-fable-lockstep-is-a-profiled-game-core-package-contract.md).
The new game workspace must consume that producer-owned promise rather than
create another shared-code or determinism contract. `FS.GG.Net` v1, meanwhile,
is a protobuf WebSocket/gRPC component; a product's SignalR and
Fable.Remoting adapters are not evidence that its contract should be widened.

[.github#2067](https://github.com/FS-GG/.github/issues/2067) owns the programme
and its dependency graph. [.github#2068](https://github.com/FS-GG/.github/issues/2068)
records the human direction this ADR encodes.

The first forcing consumer is the sibling
[`EHotwagner/S.I.R.`](https://github.com/EHotwagner/S.I.R.) repository. S.I.R. is
not the template definition: it is the first real application whose source and
acceptance scenarios will be incorporated into an `fs-gg-fable-game` workspace
to prove that the generic scaffold supports more than its toy starter.

## Decision

### 1. Identities and ownership

FS.GG.Templates owns one packable dotnet-template package named
`FS.GG.Web.Template`. It exposes two independent template identities:

| Provider | Template identity | Generated browser lane |
|---|---|---|
| `web` | `fs-gg-web` | plain TypeScript and Vite |
| `fable-game` | `fs-gg-fable-game` | Fable and Elmish |

They share package, build-orchestration, and composition infrastructure, not an
application abstraction. A generated workspace selects exactly one identity.
`web` remains free of Fable, Game, Rendering, CanvasKit, SignalR, and
Fable.Remoting dependencies. `fable-game` is a bounded specialization, not a
new default and not an extension layered onto generated `web` source.

FS.GG.Templates owns both provider descriptors and generic web/Fable workspace
skills. FS.GG.Game owns the skill that teaches use of its Fable lockstep
profile and publishes it through the independently versioned
`FS.GG.Game.Skills` package. Under ADR-0063, the scaffold materializer obtains
a skill from the declared owner package/source; neither Templates nor the
generated workspace copies Game's skill body.

### 2. Wizard and provider semantics

`new-sdd-workspace` gains `--template <rendering|web|fable-game>`. `--template`
selects the provider and therefore the package/template identity.
`--profile` remains an optional parameter interpreted only by the selected
provider; it never selects a provider indirectly.

Omitting `--template` preserves the existing `rendering` path and current
`--profile` behaviour for at least the first stable release carrying the new
switch. Interactive use asks for a template before asking only the parameters
that template supports. Supplying a profile that the selected provider does
not declare is a validation error, not a fallback to rendering.

The wizard consumes provider identities and pins from the registry/provider
descriptor; it does not restate package versions. Help text may announce a
future default change, but changing or removing the omitted-template
compatibility path requires a later decision and a stable-release deprecation
window.

### 3. Generated workspace contracts

Both templates generate one repository, one root SDD lifecycle, and one root
build/test entry point spanning server and browser evidence. Both include an
F# ASP.NET Core server, production static-asset hosting, a development proxy,
locked npm dependencies, server and browser tests, and clean-machine
restore/build/test evidence.

`fs-gg-web` starts with plain TypeScript/Vite and a minimal DOM application. It
does not select React, Vue, Svelte, or another framework on the platform's
behalf. Products may make that downstream choice.

`fs-gg-fable-game` starts with an Elmish application core and the smallest
browser view binding that the executable toolchain spike qualifies. The spike
may choose a direct DOM/Elmish binding or a maintained Fable UI binding; that
choice is a template implementation parameter and does not rename the provider
or template. The generated example must demonstrate browser startup, server
connection, reconnect/resync, one typed request/response operation, and one
real-time state/input loop.

S.I.R. is the first reference integration. Its application source is migrated
into, or the scaffold is applied in place to form, the generated workspace
shape. The checked-in result must be self-contained: it consumes published
FS-GG packages and pinned npm artifacts and has no runtime/build dependency on
a sibling checkout. S.I.R.-specific domain rules remain in S.I.R.; only a
second qualified consumer can justify promoting a generic abstraction back
into an FS-GG component or the template.

### 4. Shared F# boundary

“Shared” means source compiled for both .NET and Fable only where its APIs and
semantics are qualified on both runtimes. Shared application contracts and pure
domain transitions may live in a shared F# project/source set. ASP.NET Core,
filesystem, clock, thread, reflection-heavy, and server-authoritative
infrastructure stays server-only; DOM and JavaScript interop stays client-only.

Authoritative Game logic may use only surfaces graded for the published
`fs-gg-game-core-fable-lockstep-v1` profile. Cross-runtime exactness is derived
from Game's canonical-byte fixtures and profile identity, never from successful
Fable compilation or from the template's own example tests. A product may use
other portable F# code with an explicitly weaker semantic contract, but may
not describe it as lockstep-exact.

### 5. Communication boundaries

*(Amended by [ADR-0073](0073-plain-http-with-explicit-dtos-replaces-fable-remoting.md),
2026-08-02: the Fable.Remoting bullet below is **superseded** — plain HTTP
endpoints with explicit versioned DTOs now own typed request/response,
because the upstream `Fable.Remoting.MsgPack` compile defect blocking
`fable-game`'s pinned Fable compiler is unfixed. The SignalR bullet, and
everything below this list, are unchanged.)*

The game template uses two deliberately separate adapters:

- ~~**Fable.Remoting** owns typed HTTP request/response operations such as
  initial data, commands that return one result, and ordinary application
  queries.~~ Superseded by [ADR-0073](0073-plain-http-with-explicit-dtos-replaces-fable-remoting.md):
  **plain ASP.NET Core HTTP endpoints with explicit, versioned request/response
  DTOs** (hand-written codecs on both sides, no RPC-proxy generation) now own
  this role.
- **ASP.NET Core SignalR** owns connection/session-oriented traffic such as
  input, snapshots, presence, acknowledgement, reconnect, and resync.

Shared DTOs may cross both adapters, but neither library becomes the domain
model or persistence boundary. The SignalR browser client is a small,
template-owned and executable-tested Fable binding over the official
`@microsoft/signalr` npm client. The template does not adopt the currently
stale `Fable.SignalR` NuGet package. Protocol messages carry explicit version
or compatibility information where persisted sessions/replays could outlive a
deployment.

No v1 contract is added to FS.GG.Net. Promotion is a later cross-repository
decision only after a second consumer demonstrates a stable, domain-neutral
surface.

### 6. Toolchain, release, and activation

*(Amended by [ADR-0073](0073-plain-http-with-explicit-dtos-replaces-fable-remoting.md),
2026-08-02: the coherent set below no longer pins Fable.Remoting — see ADR-0073
§1. The rest of this section is unchanged.)*

The qualification spike records and tests a coherent set: .NET SDK, Fable,
FSharp.Core, Node, npm package manager/lockfile format, Elmish/view binding,
~~Fable.Remoting,~~ and `@microsoft/signalr`. Generated workspaces pin the selected
set through the normal lock/config files and expose one documented upgrade
path; unconstrained “latest” dependencies are not template output.

Rollout is publish-before-flip:

1. SDD proves the mixed F#/TypeScript lifecycle and evidence contract.
2. Templates and Game qualify the browser toolchain and the published Game
   lockstep artifact from clean consumers.
3. Templates implements and proves both template identities and its generic
   owner-sourced skills; Game implements and proves its lockstep skill.
4. Game versions and publishes the exact `FS.GG.Game.Skills` package to both
   feeds, then an independent consumer restores and materializes that version
   from every required public read path.
5. SDD pins that exact Game Skills version and proves the production scaffold
   materializer emits the lockstep skill bytes, digest, owner/source, package
   version, and provenance under locked restore from the public read path.
6. Templates publishes the exact `FS.GG.Web.Template` package to both feeds;
   an independent consumer installs and instantiates it from the public read
   path.
7. S.I.R. incorporates the public scaffold/packages and passes its real
   browser/gameplay acceptance slice without sibling-checkout dependencies.
8. `.github` activates provider/pin registry entries and releases the wizard
   support.

Both producer handoffs record the exact package version, tag, commit, and
artifact hashes. Required-feed artifacts are byte-identical except for a
documented feed signature, and restore/materialization evidence comes from the
public read paths rather than either producer checkout.

The package and providers are not live merely because this ADR names them.
Registry activation records a proven published artifact; it does not predict
one.

## Consequences

- A normal website pays no Fable or game-toolchain cost, while a game can share
  qualified F# application logic without pretending all .NET code is
  isomorphic.
- One package reduces publication and coherence machinery, while separate
  identities keep generated dependency graphs and compatibility promises
  honest.
- The wizard obtains a durable top-level selector without breaking existing
  omitted-`--template` invocations.
- Two communication mechanisms remain visible in generated code and tests;
  contributors must choose request/response or session traffic intentionally.
- The Fable-game template inherits Game's exactness limits and release cadence.
  It cannot locally widen the lockstep surface.
- S.I.R. supplies the first non-toy integration evidence without becoming a
  hidden framework source or causing its application-specific rules to leak
  into every generated game.
- The coherent release has two independently versioned producer artifacts.
  Changing a Game-owned skill requires a Game Skills release even when the web
  template package itself is unchanged.
- Publishing the Game package is necessary but insufficient: the new skill is
  scaffold-reachable only after SDD adopts the exact version and proves its
  production materializer path.
- Framework/view-library selection remains replaceable behind the stable
  `fable-game` identity, but changing generated source materially still needs
  normal template SemVer, upgrade notes, and drift evidence.
- Coordination sequencing remains on the board rather than in this record.

## Alternatives considered

- **One `web` template with a `language=fable|typescript` parameter.** Rejected
  because it hides materially different dependency, transport, shared-code,
  and skill contracts behind one identity.
- **Two separately published template packages.** Rejected initially because
  Templates owns both and their composition/release machinery is shared. Split
  packages remain possible if independent release cadence becomes measured
  value rather than predicted flexibility.
- **Make Fable the general web default.** Rejected because ordinary websites
  need no shared F# execution and should retain direct access to the broad
  TypeScript ecosystem.
- **Use only SignalR.** Rejected because routine typed request/response APIs
  would be encoded as session messages and lose the simpler HTTP/RPC lifecycle.
- **Use only Fable.Remoting.** Rejected because reconnect, presence, streaming
  state, backpressure, and resync are connection concerns.
- **Adopt `Fable.SignalR`.** Rejected for v1 because its maintenance evidence is
  stale relative to the official JavaScript client. A narrow binding keeps the
  supported runtime dependency explicit and testable.
- **Promote the adapters into FS.GG.Net now.** Rejected because one generated
  product shape has not demonstrated a reusable, domain-neutral contract.
- **Copy or recreate Game.Core logic in the template.** Rejected by ADR-0069:
  only the producer-owned package/profile and fixtures can carry the lockstep
  claim.
