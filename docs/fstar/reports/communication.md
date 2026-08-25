# Communication-network specification

## Result

The communication slice verified successfully with F* 2026.08.23. Unlike the
combat slice, its authority is an accepted canonical living-design document,
not a committed runtime implementation. This is useful coverage of the other
kind of source the typed protocol kernel must support: game rules that are
precise enough to prove before implementation exists.

## Source authority

- Repository: `EHotwagner/S.I.R.`
- Commit: `b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3`
- Source: `docs/communications-network.md`, status `accepted`, decision status
  `canonical`, version 1.4
- Relevant rules: weakest-link capacity at lines 108-124; delivery and latency
  at lines 328-383; remote-loop bound at lines 414-428; relay trade-off at
  lines 441-450
- Model: `communication/SIR.CommunicationNetwork.fst`

The F* route is a non-empty tree consisting of one direct link followed by zero
or more relay links. Each link carries a non-negative capacity and an additional
non-negative degradation delay. This directly represents the closed rules while
leaving prototype parameters open.

| Accepted rule | F* definition |
|---|---|
| command delivery costs 20 ticks per traversed leg | `route_delay` |
| degradation adds delay | `link.degradation_delay` |
| path capacity is bounded by the weakest link | `route_capacity` |
| a relay creates another leg | `Relay` and `route_legs` |
| remote direction requires upstream and downstream paths | `round_trip_delay` |
| an older observation cannot overwrite newer knowledge | `merge_observation` |

Observation `arrival_tick` and `value` remain in the executable record, while
merge authority is deliberately decided by `observed_tick`. Arrival order is
therefore representable without letting it corrupt knowledge chronology.

## Proved properties

F* and its bundled Z3 discharged all verification conditions for:

- `relay_never_increases_capacity`: prepending a relay link cannot improve the
  remainder's weakest-link capacity;
- `relay_adds_at_least_one_second`: each relay adds at least 20 ticks;
- `route_delay_has_structural_floor`: every route costs at least 20 ticks times
  its number of command-network legs;
- `remote_round_trip_is_slower_than_local_delivery`: two non-empty command
  routes always exceed the one-tick local delivery minimum;
- `stale_observation_cannot_overwrite`: older evidence leaves knowledge intact;
- `knowledge_tick_never_moves_backwards`: any merge is monotonic in observation
  time;
- `fresh_observation_is_accepted`: equal or newer evidence becomes current.

The structural route-floor proof is recursive; it demonstrates that F* is doing
more here than checking a few concrete examples.

## What this does not prove

The design intentionally leaves device ranges, capacities, congestion curves,
queue bounds, and relay ratios as prototype parameters. The model does not
invent them. It also does not yet formalize shared-net congestion collapse,
ordering within one link, provenance identity, disagreement between independent
observers, bounded store-and-forward queues, or liveness under reconnection.
Those require richer state machines and temporal properties rather than this
small total-function slice.
