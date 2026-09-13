# SVG-NETWORK-01 — multiplayer authority, reconnect and resync

Status: selected after replay closure. Route: routine for source and generated-candidate work; any package
publication remains a separately evidenced protected operation.

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

- [ ] **SVG-NETWORK-01.1 — Define admission, order and resync contracts — route: routine**

  Add portable Game/Net identities for client/session binding, monotonic semantic input, deterministic ordering,
  accepted snapshot cursors and resync reasons. Refuse wrong sessions, duplicates, stale sequences, invalid
  payloads and snapshots older than the last accepted cursor. Prove equal admitted streams produce equal
  accepted order and canonical replay bytes across .NET and Fable.

- [ ] **SVG-NETWORK-01.2 — Implement reconnect and bounded delivery — route: routine**

  Add the transport lifecycle for disconnect detection, reconnect token validation, bounded outbound queues,
  backpressure/coalescing policy and full-snapshot resync. Qualify cancellation/disposal, delayed and reordered
  traffic, queue saturation, reconnect expiry and refusal of a stale client without inventing a second session
  authority in the transport layer.

- [ ] **SVG-NETWORK-01.3 — Compose the generated authoritative game — route: routine**

  Replace the template's illustrative room behavior with the exact Game/Net contracts and the real server
  transition function. Keep bootstrap, hub messages and codecs versioned; record accepted semantic inputs and
  snapshots into the replay authority. Preserve single-player and Player/Studio bundle boundaries.

- [ ] **SVG-NETWORK-01.4 — Qualify two-client reconnect, resync and review — route: routine**

  Run two independent Chromium, Firefox and WebKit clients against the generated production server. Both must
  change authoritative gameplay, observe identical accepted order, survive one disconnect, reject stale and
  invalid traffic, resync missed state, and review matching replay/export identities without leaking hidden
  facts. Include keyboard and accessible status/focus assertions for reconnect and failure states.

- [ ] **SVG-NETWORK-01.5 — Close M8 and select scale — route: routine**

  Reconcile C17, M8 and section 13 network/disclosure evidence against exact source and generated-candidate
  identities. Record package/public availability separately, keep Release C open, and select `SVG-SCALE-01`
  only after all required authority, order, refusal, reconnect, resync, replay and two-client checks pass.

## Completion boundary

This milestone completes C17 and M8 at the source/generated-candidate boundary. It does not claim public
Release C artifacts, integrated C18/C19 scale, complete C20 delivery, Release D, or lifecycle/default
activation. Those remain owned by their later roadmap stages and explicit evidence boundaries.
