module ExtractionSmoke

open SIR_CombatConsequences
open SIR_CommunicationNetwork

let private require condition message =
    if not condition then failwith message

[<EntryPoint>]
let main _ =
    let consequence = resolve 100I 0I 10I 25I
    require (consequence.remaining_health = 75I) "Extracted combat health result diverged."
    require (consequence.wound = FStar_Pervasives_Native.Some Serious) "Extracted wound threshold diverged."
    require (consequence.applied_suppression = 10I) "Extracted suppression commitment diverged."

    let direct = Direct { capacity = 100I; degradation_delay = 0I }
    let relayed = Relay({ capacity = 60I; degradation_delay = 5I }, direct)
    require (route_capacity relayed = 60I) "Extracted weakest-link capacity diverged."
    require (route_delay relayed = 45I) "Extracted relay latency diverged."

    let known = { observed_tick = 20I; arrival_tick = 21I; value = 7I }
    let stale = { observed_tick = 19I; arrival_tick = 30I; value = 99I }
    require (merge_observation known stale = known) "Extracted stale-observation protection diverged."

    printfn "F* F# extraction smoke passed."
    0
