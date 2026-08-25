// A bounded executable model of accepted S.I.R. communication-network rules
// at commit b24c1bfbaa2b0904468c9490e4704bf1bd0ed6e3.
module SIRCommunicationNetwork {
  type Link = { capacity: int, degradationDelay: int }
  type Route = List[Link]
  type Observation = { observedTick: int, arrivalTick: int, value: int }

  pure def link(capacity: int, degradationDelay: int): Link =
    { capacity: capacity, degradationDelay: degradationDelay }

  pure def minimum(left: int, right: int): int =
    if (left <= right) left else right

  // Routes are non-empty in every admitted scenario. The 101 seed is above the
  // bounded capacity domain and therefore disappears after the first link.
  pure def routeCapacity(path: Route): int =
    path.foldl(101, (weakest, connection) => minimum(weakest, connection.capacity))

  pure def routeLegs(path: Route): int = path.length()

  pure def routeDelay(path: Route): int =
    path.foldl(0, (delay, connection) =>
      delay + 20 + connection.degradationDelay)

  pure def roundTripDelay(upstream: Route, downstream: Route): int =
    routeDelay(upstream) + routeDelay(downstream)

  pure def addRelay(connection: Link, remainder: Route): Route =
    [connection].concat(remainder)

  pure def mergeObservation(known: Observation, incoming: Observation): Observation =
    if (incoming.observedTick >= known.observedTick) incoming else known

  pure val LINKS: Set[Link] = Set(
    link(0, 0),
    link(1, 4),
    link(40, 0),
    link(60, 10),
    link(100, 50),
  )

  // This finite corpus covers one, two, and three legs and deliberately varies
  // the position of both the weakest link and degradation delay.
  pure val ROUTES: Set[Route] = Set(
    [link(1, 0)],
    [link(100, 50)],
    [link(60, 10), link(40, 0)],
    [link(100, 0), link(1, 4), link(60, 10)],
  )

  pure val OBSERVATIONS: Set[Observation] = Set(
    { observedTick: 0, arrivalTick: 0, value: 0 },
    { observedTick: 1, arrivalTick: 20, value: 10 },
    { observedTick: 5, arrivalTick: 6, value: 20 },
    { observedTick: 5, arrivalTick: 100, value: 30 },
    { observedTick: 9, arrivalTick: 10, value: 40 },
  )

  var relay: Link
  var route: Route
  var downstream: Route
  var known: Observation
  var incoming: Observation

  action chooseScenario = {
    nondet nextRelay = LINKS.oneOf()
    nondet nextRoute = ROUTES.oneOf()
    nondet nextDownstream = ROUTES.oneOf()
    nondet nextKnown = OBSERVATIONS.oneOf()
    nondet nextIncoming = OBSERVATIONS.oneOf()
    all {
      relay' = nextRelay,
      route' = nextRoute,
      downstream' = nextDownstream,
      known' = nextKnown,
      incoming' = nextIncoming,
    }
  }

  action init = chooseScenario
  action step = chooseScenario

  val routesAreNonEmpty = all { route.length() > 0, downstream.length() > 0 }

  val relayNeverIncreasesCapacity =
    routeCapacity(addRelay(relay, route)) <= routeCapacity(route)

  val relayAddsAtLeastOneSecond =
    routeDelay(addRelay(relay, route)) >= routeDelay(route) + 20

  val routeDelayHasStructuralFloor =
    routeDelay(route) >= 20 * routeLegs(route)

  val remoteRoundTripIsSlowerThanLocalDelivery =
    roundTripDelay(route, downstream) > 1

  val staleObservationCannotOverwrite =
    incoming.observedTick >= known.observedTick or
      mergeObservation(known, incoming) == known

  val knowledgeTickNeverMovesBackwards =
    mergeObservation(known, incoming).observedTick >= known.observedTick

  val freshObservationIsAccepted =
    incoming.observedTick < known.observedTick or
      mergeObservation(known, incoming) == incoming

  val invariant = all {
    routesAreNonEmpty,
    relayNeverIncreasesCapacity,
    relayAddsAtLeastOneSecond,
    routeDelayHasStructuralFloor,
    remoteRoundTripIsSlowerThanLocalDelivery,
    staleObservationCannotOverwrite,
    knowledgeTickNeverMovesBackwards,
    freshObservationIsAccepted,
  }

  run relayExample = assert(all {
    routeCapacity(addRelay(link(50, 3), [link(80, 0)])) == 50,
    routeDelay(addRelay(link(50, 3), [link(80, 0)])) == 43,
  })

  run staleArrivalExample = {
    val newer = { observedTick: 9, arrivalTick: 10, value: 40 }
    val lateButOlder = { observedTick: 5, arrivalTick: 100, value: 30 }
    assert(mergeObservation(newer, lateButOlder) == newer)
  }

  // The empty route is outside the admitted model and violates the structural
  // requirement that every route contain at least one delivery leg.
  action brokenInit = all {
    relay' = link(40, 0),
    route' = [],
    downstream' = [link(40, 0)],
    known' = { observedTick: 1, arrivalTick: 1, value: 1 },
    incoming' = { observedTick: 1, arrivalTick: 1, value: 1 },
  }

  action brokenStep = brokenInit
}
