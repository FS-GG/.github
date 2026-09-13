# SVG-NETWORK-01 — multiplayer authority, reconnect and resync

Status: complete at the source/generated-candidate boundary. Route: routine for source and generated-candidate
work; package publication remains a separately evidenced protected operation owned by Release C.

This is the executable M8 plan for the
[accepted SVG game-engine programme](../2026-09-07-064259-svg-game-engine-template-design-roadmap.md). It
delivers C17 through one server authority, monotonic admission and snapshot contracts, reconnect/resync, and a
real two-browser generated game whose accepted session can be reviewed through the M7 replay contract.

## Stable boundary

- Game owns portable semantic input, snapshot, session, replay and deterministic ordering contracts.
- Net owns transport-neutral connection, admission, backpressure, reconnect and resync coordination. Transport
  adapters carry those decisions and do not define gameplay transitions.
- The generated server remains the only multiplayer gameplay authority. Clients may predict presentation but
  cannot commit state or reinterpret rejected/stale traffic.
- Network records contain admitted semantic intents and accepted snapshots, never device events or hidden
  server facts. Rebinding therefore cannot change historical meaning.
- Templates qualifies the exact Game/Net candidate in two production browsers, including disconnect, missed
  updates, resync, invalid/stale refusal and review of the same accepted replay outcome.

## Milestones

- [x] **SVG-NETWORK-01.1 — Define admission, order and resync contracts — route: routine**

  Add portable Game/Net identities for client/session binding, monotonic semantic input, deterministic ordering,
  accepted snapshot cursors and resync reasons. Refuse wrong sessions, duplicates, stale sequences, invalid
  payloads and snapshots older than the last accepted cursor. Prove equal admitted streams produce equal
  accepted order and canonical replay bytes across .NET and Fable.

- [x] **SVG-NETWORK-01.2 — Implement reconnect and bounded delivery — route: routine**

  Add the transport lifecycle for disconnect detection, reconnect token validation, bounded outbound queues,
  backpressure/coalescing policy and full-snapshot resync. Qualify cancellation/disposal, delayed and reordered
  traffic, queue saturation, reconnect expiry and refusal of a stale client without inventing a second session
  authority in the transport layer.

- [x] **SVG-NETWORK-01.3 — Compose the generated authoritative game — route: routine**

  Replace the template's illustrative room behavior with the exact Game/Net contracts and the real server
  transition function. Keep bootstrap, hub messages and codecs versioned; record accepted semantic inputs and
  snapshots into the replay authority. Preserve single-player and Player/Studio bundle boundaries.

- [x] **SVG-NETWORK-01.4 — Qualify two-client reconnect, resync and review — route: routine**

  Run two independent Chromium, Firefox and WebKit clients against the generated production server. Both must
  change authoritative gameplay, observe identical accepted order, survive one disconnect, reject stale and
  invalid traffic, resync missed state, and review matching replay/export identities without leaking hidden
  facts. Include keyboard and accessible status/focus assertions for reconnect and failure states.

- [x] **SVG-NETWORK-01.5 — Close M8 and select scale — route: routine**

  Reconcile C17, M8 and section 13 network/disclosure evidence against exact source and generated-candidate
  identities. Record package/public availability separately, keep Release C open, and select `SVG-SCALE-01`
  only after all required authority, order, refusal, reconnect, resync, replay and two-client checks pass.

## Completion boundary

This milestone completes C17 and M8 at the source/generated-candidate boundary. It does not claim public
Release C artifacts, integrated C18/C19 scale, complete C20 delivery, Release D, or lifecycle/default
activation. Those remain owned by their later roadmap stages and explicit evidence boundaries.

## Closure evidence

- Game [PR #633](https://github.com/FS-GG/FS.GG.Game/pull/633), merged as
  `f23c7eb47c4183f218560cc2d036795b9d9e96de`, supplies runtime client binding, monotonic semantic input
  admission, deterministic accepted order, canonical accepted-stream bytes and explicit resync decisions.
  [PR #634](https://github.com/FS-GG/FS.GG.Game/pull/634), merged as
  `f024993774cd046edf5f3cb0ba047f2c79a882e2`, completes bounded expiry by retiring a binding and its cursor
  without removing immutable accepted history.
- Net [PR #86](https://github.com/FS-GG/FS.GG.Net/pull/86), merged as
  `49a90425d705cc11ec0a31549fabeb4a36ec0c85`, supplies bounded delivery, snapshot coalescing,
  acknowledgement, reconnect-token/expiry refusal and disposal as a transport-neutral reducer.
- Templates [PR #477](https://github.com/FS-GG/FS.GG.Templates/pull/477), merged as
  `1f213149a2d39bfe5116131f3f6dc62340a164fd`, composes the exact Game and Net
  candidates into the only generated server authority, orders tick-frontier moves by accepted server order,
  and records accepted moves, advances, snapshots and checkpoints through the replay authority.
- Templates [PR #478](https://github.com/FS-GG/FS.GG.Templates/pull/478), merged as
  `1b8d43f81fd84cafa0e8fd5b50ccb3831cec14b1`, drives independent clients in Chromium, Firefox and WebKit. Both clients change the real
  authoritative game; one disconnects across missed state and receives a bounded resync; duplicate, stale and
  invalid traffic is refused; keyboard focus and live status remain usable; and both observe the same
  disclosure-filtered accepted/replay export.

The evidence closes C17 and M8 for exact source and generated-candidate identities. The public package set
remains Release B, so this closure makes no Release-C installation or availability claim. `SVG-SCALE-01` is
selected next.
