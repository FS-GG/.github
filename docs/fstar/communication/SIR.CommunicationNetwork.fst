module SIR.CommunicationNetwork

(**
  A proof-oriented model of accepted S.I.R. communications rules at commit
  b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3:

  * each command-network leg costs at least 20 ticks;
  * route capacity is bounded by its weakest link;
  * adding a relay cannot improve capacity and adds at least one second;
  * a later-arriving older observation cannot overwrite newer knowledge.

  Open balance parameters such as concrete device ranges and collapse curves
  are intentionally not invented here.
*)

type link = {
  capacity: nat;
  degradation_delay: nat
}

type route =
  | Direct: link -> route
  | Relay: link -> route -> route

let minimum (left:nat) (right:nat) : Tot nat =
  if left <= right then left else right

let rec route_capacity (path:route) : Tot nat =
  match path with
  | Direct connection -> connection.capacity
  | Relay connection remainder ->
      minimum connection.capacity (route_capacity remainder)

let rec route_legs (path:route) : Tot nat =
  match path with
  | Direct _ -> 1
  | Relay _ remainder -> 1 + route_legs remainder

let rec route_delay (path:route) : Tot nat =
  match path with
  | Direct connection -> 20 + connection.degradation_delay
  | Relay connection remainder ->
      20 + connection.degradation_delay + route_delay remainder

let round_trip_delay (upstream:route) (downstream:route) : Tot nat =
  route_delay upstream + route_delay downstream

let relay_never_increases_capacity (connection:link) (remainder:route)
  : Lemma
      (route_capacity (Relay connection remainder) <= route_capacity remainder)
  = ()

let relay_adds_at_least_one_second (connection:link) (remainder:route)
  : Lemma
      (route_delay (Relay connection remainder) >= route_delay remainder + 20)
  = ()

let rec route_delay_has_structural_floor (path:route)
  : Lemma (route_delay path >= 20 * route_legs path)
  =
  match path with
  | Direct _ -> ()
  | Relay _ remainder -> route_delay_has_structural_floor remainder

let remote_round_trip_is_slower_than_local_delivery
  (upstream:route)
  (downstream:route)
  : Lemma (round_trip_delay upstream downstream > 1)
  =
  route_delay_has_structural_floor upstream;
  route_delay_has_structural_floor downstream

type observation = {
  observed_tick: nat;
  arrival_tick: nat;
  value: int
}

let merge_observation
  (known:observation)
  (incoming:observation)
  : Tot observation
  =
  if incoming.observed_tick >= known.observed_tick
  then incoming
  else known

let stale_observation_cannot_overwrite
  (known:observation)
  (incoming:observation)
  : Lemma
      (requires incoming.observed_tick < known.observed_tick)
      (ensures merge_observation known incoming == known)
  = ()

let knowledge_tick_never_moves_backwards
  (known:observation)
  (incoming:observation)
  : Lemma
      ((merge_observation known incoming).observed_tick >= known.observed_tick)
  = ()

let fresh_observation_is_accepted
  (known:observation)
  (incoming:observation)
  : Lemma
      (requires incoming.observed_tick >= known.observed_tick)
      (ensures merge_observation known incoming == incoming)
  = ()
