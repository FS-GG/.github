# Communication-network specification

## Result

The communication slice typechecked, its two named examples passed, and
Apalache 0.56.1 found no violation through two symbolic transitions across the
finite link, route, and observation corpus. An empty-route initializer produced
a one-state ITF counterexample, proving that the model rejects absence of the
required delivery leg.

Unlike the combat slice, this model's authority is an accepted canonical living
design rather than committed runtime behavior. It therefore tests whether Quint
is readable and executable before an implementation exists.

## Source authority

- Repository: `EHotwagner/S.I.R.`
- Commit: `b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3`
- Source: `docs/communications-network.md`, status `accepted`, decision status
  `canonical`, version 1.4
- Relevant rules: weakest-link capacity at lines 108-124; delivery and latency
  at lines 328-383; remote-loop bound at lines 414-428; relay trade-off at
  lines 441-450
- Model: `communication/SIRCommunicationNetwork.qnt`

| Accepted rule | Quint definition |
|---|---|
| command delivery costs 20 ticks per traversed leg | `routeDelay` |
| degradation adds delay | `Link.degradationDelay` |
| route capacity is its weakest link | `routeCapacity` |
| prepending a relay creates another leg | `addRelay` and `routeLegs` |
| a remote loop has upstream and downstream paths | `roundTripDelay` |
| older observations cannot overwrite newer knowledge | `mergeObservation` |

Arrival time and value remain in the observation record, while merge authority
is decided by observation time. Arrival order is therefore representable
without letting it rewrite knowledge chronology.

## Checked properties

The combined invariant covers:

- both admitted routes are non-empty;
- prepending a relay cannot improve weakest-link capacity;
- a relay adds at least 20 ticks;
- route delay is at least 20 ticks per leg;
- two non-empty routes exceed one-tick local delivery;
- stale evidence cannot overwrite current knowledge;
- knowledge time never moves backwards; and
- equal or newer evidence becomes current.

## Bounded representation finding

F* represents a route as a recursive non-empty tree and proves the structural
delay floor for arbitrary depth. Quint deliberately has neither recursive
functions nor recursive types. This experiment therefore represents a route as
a non-empty `List[Link]`, implements folds for capacity and delay, and admits a
finite corpus of one-, two-, and three-leg routes for symbolic checking.

The list representation is clearer than the proof-oriented tree for this
domain, but the proof strength is lower: the checked result applies to the
admitted finite route corpus. A production Quint authoring decision must either
accept explicit operational bounds, generate larger domains from FS-GG model
metadata, or retain a theorem-prover capability for genuinely unbounded
structural claims.

## Negative control

`brokenInit` supplies an empty route. The invariant requires both directions to
contain at least one delivery leg. Verification must exit non-zero, report a
counterexample, and emit a non-empty ITF trace.

## What this does not prove

The design leaves concrete device ranges, congestion curves, queue bounds, and
relay ratios open. The model does not invent them. It also does not cover
shared-net congestion, per-link ordering, provenance identity, disagreement
between observers, store-and-forward queues, or liveness under reconnection.
